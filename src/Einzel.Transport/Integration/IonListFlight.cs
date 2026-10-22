using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Transport.Collisions;

namespace Einzel.Transport.Integration;

/// <summary>What one row of an ion list did in flight.</summary>
public sealed record IonFlight
{
    /// <summary>The row as it was flown.</summary>
    public required IonRow Row { get; init; }

    /// <summary>
    /// The flight: the one integration, or the finest rung of the ladder. Null when the
    /// integration threw, and <see cref="Failure"/> says why.
    /// </summary>
    /// <remarks>Its times run from the row's birth, not from the model's t = 0.</remarks>
    public TrajectoryResult? Result { get; init; }

    /// <summary>The ladder's half-width on the flight time in seconds; null without the ladder.</summary>
    public double? FlightTimeHalfWidthSeconds { get; init; }

    /// <summary>Collisions the reported flight made; zero in vacuum.</summary>
    public int Collisions { get; init; }

    /// <summary>The ladder's convergence warnings and the collision sampler's own.</summary>
    public IReadOnlyList<ValidityWarning> Warnings { get; init; } = [];

    /// <summary>What the integration threw, or null when it finished.</summary>
    public Exception? Failure { get; init; }
}

/// <summary>Flies the rows of an ion list, one independent trajectory each.</summary>
/// <remarks>
/// <para>
/// <b>Written by index, so the answer does not depend on how many fly at once.</b> Each row
/// is a pure function of its row, its index and inputs nothing writes to: the field, the
/// settings, the stop function and the gas. Its collision stream is seeded with the gas seed
/// plus the row's index, the rule a cloud has always followed, so raising the row count
/// leaves the rows already there unchanged and a row computes the same flight on whichever
/// thread it lands.
/// </para>
/// <para>
/// <b>A birth time is a time-shifted field</b> (<see cref="TimeShiftedField"/>, SIMION's
/// <c>tob</c>): the row sees every drive and switch as they stand at its birth plus its own
/// flight time. A row born at 0 flies the field itself, unwrapped, so it is the flight a
/// single-ion run makes to the bit. A static field has no clock to shift and is flown as it
/// is.
/// </para>
/// <para>
/// <b>One integration at the model's tolerance by default</b>, which is the ladder's first
/// rung bit for bit. The ladder runs <see cref="FlightTimeStudy"/> per row and reports its
/// finest rung with the residual, three flights for the price of one interval.
/// </para>
/// </remarks>
public static class IonListFlight
{
    /// <summary>Flies every row and returns one flight per row, in row order.</summary>
    /// <param name="rows">The rows, in launch order. A row's index seeds its collisions.</param>
    /// <param name="field">The field as the model declares it, at the model's t = 0.</param>
    /// <param name="settings">The model's integration settings.</param>
    /// <param name="detector">The stopping surface.</param>
    /// <param name="gas">The background gas, or <see cref="BackgroundGas.Vacuum"/>.</param>
    /// <param name="gasSeed">The declared collision seed; row k draws from <c>gasSeed + k</c>.</param>
    /// <param name="maxParallelism">How many rows may fly at once. One flies them in order on this thread.</param>
    /// <param name="ladder">Whether to run the three-rung tolerance ladder per row.</param>
    /// <returns>The flights, index for index with <paramref name="rows"/>.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxParallelism"/> is below one.</exception>
    public static IReadOnlyList<IonFlight> Fly(
        IReadOnlyList<IonRow> rows,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction detector,
        BackgroundGas gas,
        int gasSeed,
        int maxParallelism,
        bool ladder = false)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(gas);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelism, 1);

        var flights = new IonFlight[rows.Count];

        // One at a time stays on the caller's thread, in order: a cloud flown inside a
        // study's own Parallel.For must not start a second level of workers.
        if (maxParallelism == 1)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                flights[index] = FlyOne(rows[index], index, field, settings, detector, gas, gasSeed, ladder);
            }

            return flights;
        }

        Parallel.For(
            0,
            rows.Count,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallelism },
            index => flights[index] = FlyOne(rows[index], index, field, settings, detector, gas, gasSeed, ladder));

        return flights;
    }

    private static IonFlight FlyOne(
        IonRow row,
        int index,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction detector,
        BackgroundGas gas,
        int gasSeed,
        bool ladder)
    {
        var flown = row.BirthTimeSeconds > 0.0 && field is ITimeVaryingField driven
            ? new TimeShiftedField(driven, row.BirthTimeSeconds)
            : field;

        var seed = unchecked(gasSeed + index);

        CollisionSampler? Sampler() => gas.IsPresent
            ? new CollisionSampler(gas, row.Species.MassSi, row.Species.ChargeSi, seed)
            : null;

        try
        {
            if (!ladder)
            {
                var sampler = Sampler();
                var result = TrajectoryIntegrator.Integrate(
                    row.Launch, row.Species, flown, settings, detector, collisions: sampler);

                return new IonFlight
                {
                    Row = row,
                    Result = result,
                    Collisions = sampler?.Collisions ?? 0,
                    Warnings = sampler is null
                        ? []
                        : FlightTimeStudy.CollisionWarnings(
                            sampler.BoundExceeded, sampler.SampledOutsideDensity, sampler.SampledOutsideFlow),
                };
            }

            // Each rung gets a fresh stream from the same seed, as a single-ion run's do;
            // the last one made belongs to the finest rung.
            CollisionSampler? finest = null;
            var study = FlightTimeStudy.Run(
                row.Launch, row.Species, flown, settings, detector,
                collisions: gas.IsPresent ? () => finest = Sampler()! : null);

            return new IonFlight
            {
                Row = row,
                Result = study.Runs[^1],
                FlightTimeHalfWidthSeconds = FlightTimeStudy.ConvergenceResidual(study.Runs).ResidualSi,
                Collisions = finest?.Collisions ?? 0,
                Warnings = study.FlightTime.Warnings,
            };
        }
        catch (Exception failure) when (failure is not OutOfMemoryException)
        {
            // One row's failure is that row's result; the caller decides whether it ends the list.
            return new IonFlight { Row = row, Failure = failure };
        }
    }
}
