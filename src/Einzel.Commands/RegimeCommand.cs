using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>The governing dimensionless numbers at one point on a path.</summary>
/// <param name="TimeUs">When, in microseconds from launch.</param>
/// <param name="PositionMm">Where, in millimetres.</param>
/// <param name="SpeedMs">How fast the ion is going there, in metres per second.</param>
/// <param name="PressureMbar">Local gas pressure, in millibar.</param>
/// <param name="MeanFreePathMm">Local mean free path, in millimetres.</param>
/// <param name="Knudsen">Mean free path over the tightest constriction.</param>
/// <param name="CollisionsPerRfCycle">
/// Expected collisions per drive cycle, or absent where the field is not driven.
/// </param>
/// <param name="ReducedFieldTd">
/// The local field over the local density, in townsend. What decides whether a low-field
/// mobility applies at all.
/// </param>
/// <param name="Violations">
/// The codes of every REG-2 warning this point earns, in descending severity.
/// </param>
public sealed record RegimeSample(
    double TimeUs,
    IReadOnlyList<double> PositionMm,
    double SpeedMs,
    double PressureMbar,
    double MeanFreePathMm,
    double Knudsen,
    double? CollisionsPerRfCycle,
    double ReducedFieldTd,
    IReadOnlyList<string> Violations);

/// <summary>A stretch of path over which one thing is wrong.</summary>
/// <param name="Code">Which REG-2 warning.</param>
/// <param name="Message">What it says, from the first sample that earned it.</param>
/// <param name="Severity">How bad, as GRD-3 grades it.</param>
/// <param name="FromUs">When it starts, in microseconds.</param>
/// <param name="ToUs">When it stops.</param>
/// <param name="FromMm">Where it starts along the path, in millimetres from launch.</param>
/// <param name="ToMm">Where it stops.</param>
/// <param name="Samples">How many sampled points are inside it.</param>
/// <remarks>
/// <b>This is what the inspector is for.</b> A run's warnings say the description failed
/// somewhere; an excursion says between where and where, which is a thing to change. The
/// distinction matters most exactly where the gas is a field: a funnel whose entrance is
/// at 10 mbar and whose exit is at 0.1 mbar is in two different regimes, and a single
/// verdict for the run describes neither.
/// </remarks>
public sealed record RegimeExcursion(
    string Code,
    string Message,
    string Severity,
    double FromUs,
    double ToUs,
    double FromMm,
    double ToMm,
    int Samples);

/// <summary>The dimensionless numbers along a path, and where they go wrong.</summary>
/// <param name="ModelPath">The model, as an absolute path.</param>
/// <param name="Mode">The transport mode the model selected.</param>
/// <param name="ApertureMm">The constriction the Knudsen number is taken against.</param>
/// <param name="Samples">The path, in flight order.</param>
/// <param name="Excursions">Where the selected description does not hold.</param>
/// <param name="Warnings">What applies to the run as a whole (GRD-2).</param>
public sealed record RegimeProfile(
    string ModelPath,
    string Mode,
    double ApertureMm,
    IReadOnlyList<RegimeSample> Samples,
    IReadOnlyList<RegimeExcursion> Excursions,
    IReadOnlyList<ValidityWarning> Warnings);

/// <summary>
/// REG-2's numbers along a selected path, with the violations located (§16).
/// </summary>
/// <remarks>
/// <para>
/// <b>What a run already reports, and what this adds.</b> Every run computes the governing
/// dimensionless numbers and reports them whether or not anything crosses a threshold -
/// but at the <em>worst</em> point anywhere in the gas, which is the right answer for a
/// warning and the wrong one for a person deciding what to change. §16 asks for the
/// numbers along a path so that "outside validity" becomes "outside validity between 12
/// and 31 millimetres".
/// </para>
/// <para>
/// <b>The thresholds are not restated here.</b> Each sample is handed to the same
/// <c>RegimeDiagnostics.ForTrajectoryMode</c> a run uses, so a boundary that moves moves once. A
/// second copy of "above 1e-2 mbar trajectory integration is the wrong description" would
/// be a second thing to keep in step with spec figure 4.
/// </para>
/// </remarks>
public static class RegimeCommand
{
    /// <summary>Walks a model's path and reports the regime at each step.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="samples">How many points along the path.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Fewer than two samples.</exception>
    /// <exception cref="EinzelException">The model does not validate.</exception>
    public static RegimeProfile Execute(string modelPath, int samples = 128)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(samples, 2);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(absolute)), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var warnings = new List<ValidityWarning>();

        var (field, built) = FieldAssembly.BuildReported(model);

        warnings.AddRange(built);

        // Resolved, not built fresh: an imported velocity or pressure field needs the
        // model's own directory, and a gas rebuilt without it reports a jet as standing
        // still - the silent drop the resolution exists to prevent.
        var gas = Io.GasFlowImport.Resolve(model.Gas, model.SourceDirectory ?? ".");

        if (!gas.IsPresent)
        {
            // Every number here is a statement about a gas. In vacuum they are all
            // infinite or zero, which is true and says nothing - so the profile is empty
            // and the reason is on the record rather than left to be inferred from a
            // table of infinities.
            warnings.Add(new ValidityWarning(
                "regime.no-gas",
                "this model declares no gas, so there are no regime numbers to report: a "
                + "mean free path is unbounded and a Knudsen number infinite everywhere. "
                + "Trajectory integration is unconditionally the right description in "
                + "vacuum, which is what REG-2 exists to check and what makes the check "
                + "vacuous here",
                WarningSeverity.Provenance));

            return new RegimeProfile(absolute, model.TransportMode, 0.0, [], [], warnings);
        }

        var mode = TransportModes.All.FirstOrDefault(
            m => string.Equals(m.Name, model.TransportMode, StringComparison.Ordinal));

        if (!(mode?.ProducesTrajectories ?? true))
        {
            // RND-8, asked of the mode rather than of the pressure. A density has no path,
            // so flying an ion to report the regime along would be reporting it along a
            // trajectory this model says does not exist - and the numbers would look
            // exactly as authoritative as real ones.
            warnings.Add(new ValidityWarning(
                "regime.no-trajectory",
                $"the '{model.TransportMode}' transport mode computes a density rather than "
                + "trajectories, so there is no path to report the regime along. The "
                + "numbers still vary in space; what is missing is the route through them",
                WarningSeverity.Provenance));

            return new RegimeProfile(absolute, model.TransportMode, 0.0, [], [], warnings);
        }

        var path = Flown(model, field, gas, samples);

        if (path.Count == 0)
        {
            warnings.Add(new ValidityWarning(
                "regime.no-path",
                "no trajectory was produced, so there is no path to report the regime "
                + "along. A diffusive model computes a density and has no path by "
                + "construction (RND-8)",
                WarningSeverity.Provenance));

            return new RegimeProfile(absolute, model.TransportMode, 0.0, [], [], warnings);
        }

        var species = IonSpecies.FromModel(model);
        var aperture = Aperture(model);
        var drive = field is ITimeVaryingField driven && driven.ShortestPeriodSeconds > 0.0
            ? 1.0 / driven.ShortestPeriodSeconds
            : 0.0;

        var flight = path[^1].TimeSeconds - path[0].TimeSeconds;

        var reported = new List<RegimeSample>(path.Count);
        var travelled = 0.0;
        var distances = new List<double>(path.Count);

        for (var i = 0; i < path.Count; i++)
        {
            var sample = path[i];

            if (i > 0)
            {
                travelled += (sample.Position - path[i - 1].Position).Length;
            }

            distances.Add(travelled);

            var speed = sample.Velocity.Length;
            var here = sample.Position;

            var numbers = RegimeDiagnostics.MeasureAt(
                gas, species, speed, flight, aperture, drive, in here);

            reported.Add(new RegimeSample(
                TimeUs: sample.TimeSeconds * 1e6,
                PositionMm: [here.X * 1e3, here.Y * 1e3, here.Z * 1e3],
                SpeedMs: speed,
                PressureMbar: numbers.PressureMbar,
                MeanFreePathMm: numbers.MeanFreePathM * 1e3,
                Knudsen: numbers.Knudsen,
                CollisionsPerRfCycle: double.IsFinite(numbers.CollisionsPerRfCycle)
                    ? numbers.CollisionsPerRfCycle
                    : null,
                ReducedFieldTd: RegimeDiagnostics.ReducedFieldTd(
                    gas, Magnitude(field, in here, sample.TimeSeconds), in here),
                Violations: [.. RegimeDiagnostics.ForTrajectoryMode(gas, numbers).Select(w => w.Code)]));
        }

        return new RegimeProfile(
            absolute,
            model.TransportMode,
            aperture * 1e3,
            reported,
            Excursions(gas, species, path, distances, reported, flight, aperture, drive),
            warnings);
    }

    /// <summary>The stretches over which one warning holds, from the per-sample codes.</summary>
    /// <remarks>
    /// Contiguous runs rather than a count, because a warning earned at two separate
    /// places in an instrument is two problems and reporting "17 samples" would merge
    /// them. A gap of one sample ends a run - the sampling is fine enough that a genuine
    /// excursion spans several, and merging across a gap would report a stretch as bad
    /// where it is not.
    /// </remarks>
    private static IReadOnlyList<RegimeExcursion> Excursions(
        BackgroundGas gas,
        IonSpecies species,
        IReadOnlyList<TrajectorySample> path,
        List<double> distances,
        List<RegimeSample> reported,
        double flight,
        double aperture,
        double drive)
    {
        var excursions = new List<RegimeExcursion>();

        foreach (var code in reported.SelectMany(s => s.Violations).Distinct(StringComparer.Ordinal))
        {
            var start = -1;

            for (var i = 0; i <= reported.Count; i++)
            {
                var inside = i < reported.Count
                    && reported[i].Violations.Contains(code, StringComparer.Ordinal);

                if (inside && start < 0)
                {
                    start = i;
                }
                else if (!inside && start >= 0)
                {
                    var first = path[start].Position;
                    var speed = path[start].Velocity.Length;

                    var said = RegimeDiagnostics
                        .ForTrajectoryMode(
                            gas,
                            RegimeDiagnostics.MeasureAt(
                                gas, species, speed, flight, aperture, drive, in first))
                        .First(w => w.Code == code);

                    excursions.Add(new RegimeExcursion(
                        code,
                        said.Message,
                        said.Severity.ToString(),
                        reported[start].TimeUs,
                        reported[i - 1].TimeUs,
                        distances[start] * 1e3,
                        distances[i - 1] * 1e3,
                        i - start));

                    start = -1;
                }
            }
        }

        return [.. excursions.OrderBy(e => e.FromUs).ThenBy(e => e.Code, StringComparer.Ordinal)];
    }

    /// <summary>The field magnitude at a point, at an instant.</summary>
    /// <remarks>
    /// Asked at the ion's own instant rather than at zero. A driven field answers the
    /// time-free interface at t = 0 without failing, which is how a section, a solve
    /// report, a summed field and a diffusive run have each ended up describing an
    /// arbitrary moment of an RF cycle - and a reduced field read at the top of the cycle
    /// would overstate E/N by the crest factor everywhere.
    /// </remarks>
    private static double Magnitude(IElectrostaticField field, in Vec3 point, double atSeconds) =>
        field is ITimeVaryingField driven
            ? driven.ElectricFieldAt(in point, atSeconds).Length
            : field.ElectricFieldAt(in point).Length;

    /// <summary>The tightest constriction the ion passes through, in metres.</summary>
    /// <remarks>
    /// The Knudsen number is meaningless without a length, and the honest choice is the
    /// tightest constriction rather than the size of the whole instrument - the same
    /// choice a run makes, so the two agree.
    /// </remarks>
    private static double Aperture(CompiledModel model) => RunCommand.SmallestAperture(model);

    /// <summary>Flies the nominal ion through its own gas and samples the path.</summary>
    /// <remarks>
    /// <para>
    /// <b>Through the gas, not through a vacuum.</b> The first version of this attached no
    /// collision sampler, so a model declaring a pressure was flown as though it declared
    /// none - and then the gas numbers were reported along that vacuum path. Where the gas
    /// changes the route, which is the only place this view is worth opening, the route
    /// would have been wrong. The same silent substitution a run's own comment warns
    /// against, and the second time a declared gas has taken no part in something.
    /// </para>
    /// <para>
    /// A sampler per integration, because it carries the random state: sharing one between
    /// the scout and the resample would make the second flight continue the first one's
    /// draw sequence rather than repeat it.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<TrajectorySample> Flown(
        CompiledModel model, IElectrostaticField field, BackgroundGas gas, int samples)
    {
        var species = IonSpecies.FromModel(model);

        CollisionSampler? Collisions() => gas.IsPresent
            ? new CollisionSampler(gas, species.MassSi, species.ChargeSi, model.Gas.Seed)
            : null;

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;
        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            LandOnEdges = model.LandOnEdges,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        // Scouted at the model's own cadence to learn how long the flight is, then flown
        // again at a cadence chosen from it. The same pattern the section renderer and the
        // viewport use, and for the same reason: the model's cadence is chosen for VTU.
        var scout = new TrajectoryRecorder(model.SampleIntervalSi);

        TrajectoryIntegrator.Integrate(
            launch, species, field, settings, detector, scout, Collisions());

        if (scout.Samples.Count < 2)
        {
            return [];
        }

        var flight = scout.Samples[^1].TimeSeconds - scout.Samples[0].TimeSeconds;

        if (flight <= 0.0)
        {
            return scout.Samples;
        }

        var recorder = new TrajectoryRecorder(flight / samples, capacity: 4 * samples);

        TrajectoryIntegrator.Integrate(
            launch, species, field, settings, detector, recorder, Collisions());

        return recorder.Samples;
    }
}
