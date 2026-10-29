using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>What a preview showed.</summary>
public sealed record PreviewOutcome
{
    /// <summary>The model file, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Flight time, as the GRD-1 envelope, tainted as preview tier.</summary>
    public required MeasuredJson FlightTime { get; init; }

    /// <summary>Why the integration stopped.</summary>
    public required string Outcome { get; init; }

    /// <summary>Whether the engine computed what it was asked to.</summary>
    /// <remarks>
    /// The same distinction <c>RunOutcome.Completed</c> carries, and for the same reason:
    /// an ion striking an electrode or being held to the end of its flight time is a
    /// result, while an integrator that gave up is not. Preview had the narrower of the two
    /// lists - success meant <c>StopConditionMet</c> alone - so previewing a trap reported
    /// a failure.
    /// </remarks>
    public required bool Completed { get; init; }

    /// <summary>The integrator tolerance actually used.</summary>
    public required double RelativeTolerance { get; init; }

    /// <summary>The tolerance the model asked for.</summary>
    public required double RequestedTolerance { get; init; }

    /// <summary>Accepted integrator steps.</summary>
    public required int AcceptedSteps { get; init; }

    /// <summary>Wall-clock milliseconds.</summary>
    public required double ElapsedMs { get; init; }
}

/// <summary>
/// A fast, deliberately inexact look at a model.
/// </summary>
/// <remarks>
/// <para>
/// GRD-5: a preview-tier result keeps working and carries a non-suppressible mark.
/// The point of the tier is the loop it enables - an agent or a person adjusting a
/// geometry wants to know within a second whether the ion still arrives, and does
/// not want six figures while doing it.
/// </para>
/// <para>
/// Two things make that safe rather than merely fast. The taint is attached to the
/// number itself, so it travels wherever the number goes and cannot be dropped by
/// a caller who did not think to look; and a preview writes <em>nothing</em>. A
/// tainted result sitting in results/ would be picked up by <c>verify</c> and
/// reported as current, which is exactly the sort of quietly-wrong artifact the
/// manifest discipline exists to prevent.
/// </para>
/// <para>
/// The single number that makes it a preview is the integrator tolerance, floored
/// well above the model's own. It is not a different physics or a different
/// solver - it is the same one told to stop trying so hard, which is why the
/// answer it gives is usually close and never quotable.
/// </para>
/// </remarks>
public static class PreviewCommand
{
    /// <summary>
    /// The tolerance a preview runs at.
    /// </summary>
    /// <remarks>
    /// Loose enough to be several times faster than a full run and tight enough
    /// that the answer is still in the right place - close enough to see that a
    /// change helped, nowhere near ACC-1's one part per million.
    /// </remarks>
    public const double PreviewTolerance = 1e-6;

    /// <summary>Runs a preview.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    public static PreviewOutcome Execute(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            ModelJson.Parse(File.ReadAllText(absolute)),
            null,
            Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        var (field, fieldWarnings) = FieldAssembly.BuildReported(model);
        var species = IonSpecies.FromModel(model);
        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        // Never tighter than the model asked for: a model that wanted something
        // looser than the preview floor should get what it wanted, and a preview
        // that quietly ran more accurately than the real thing would be a strange
        // kind of lie.
        var tolerance = Math.Max(model.RelativeTolerance, PreviewTolerance);

        var result = TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = tolerance,
                LandOnEdges = model.LandOnEdges,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            detector);

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var seconds = Quantity.Si(result.FlightTimeSeconds, Quantity.From(1.0, "s").Dimension);

        // No convergence study, so there is no honest interval to quote - and
        // rather than invent one, the envelope says the width is unknown by
        // carrying the taint that explains why.
        var flightTime = new Measured(
            seconds,
            UncertaintyInterval.Symmetric(seconds, Quantity.Si(0.0, seconds.Dimension), 1.0),
            new Evidence.Convergence("integrator tolerance", double.NaN, 5.0, double.NaN),
            [
                new ValidityWarning(
                    "result.preview-tier",
                    $"preview tier: integrated at a relative tolerance of {tolerance:G3} against the model's "
                    + $"{model.RelativeTolerance:G3}, with no convergence study behind it. This number is for "
                    + "looking at, not for quoting; run 'einzel run' for a result with an interval",
                    WarningSeverity.Provenance),
                .. fieldWarnings,
            ]);

        return new PreviewOutcome
        {
            ModelPath = absolute,
            FlightTime = MeasuredJson.From(flightTime, "us"),
            Outcome = result.Outcome.ToString(),
            Completed = result.Outcome.Completed(),
            RelativeTolerance = tolerance,
            RequestedTolerance = model.RelativeTolerance,
            AcceptedSteps = result.AcceptedSteps,
            ElapsedMs = elapsed,
        };
    }
}
