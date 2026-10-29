using Einzel.Core.Units;

namespace Einzel.Transport.Integration;

/// <summary>
/// The outcome of one trajectory integration at one tolerance.
/// </summary>
/// <remarks>
/// <para>
/// This is an intermediate, not a reported result, and the distinction matters
/// for GRD-1. A single integration has no honest uncertainty to quote: the
/// controller's per-step error estimate bounds local truncation, not the
/// accumulated flight-time error, and claiming otherwise would be exactly the
/// bare number the spec forbids. The reported figure of merit comes from
/// <see cref="FlightTimeStudy"/>, which integrates at several tolerances and
/// derives an interval and an observed convergence order from the sequence.
/// </para>
/// <para>
/// Nothing above Einzel.Transport should consume <see cref="FlightTimeSeconds"/>
/// directly. Analysis, commands, and every reporting surface take a
/// <see cref="Core.Results.Measured"/>.
/// </para>
/// </remarks>
public sealed record TrajectoryResult
{
    /// <summary>Position and velocity where the integration stopped.</summary>
    public required PhaseState FinalState { get; init; }

    /// <summary>Elapsed flight time, in seconds, accumulated with Neumaier compensation.</summary>
    public required double FlightTimeSeconds { get; init; }

    /// <summary>
    /// The correction the compensated sum recovered, in seconds. Its magnitude is
    /// what naive accumulation would have discarded, so it is a direct readout of
    /// whether the compensation is doing anything on this problem.
    /// </summary>
    public required double TimeCompensation { get; init; }

    /// <summary>Why the integration stopped.</summary>
    public required TrajectoryOutcome Outcome { get; init; }

    /// <summary>
    /// The electrode the ion struck, when it struck one.
    /// </summary>
    /// <remarks>
    /// Null for every other outcome. Named rather than indexed because the name is
    /// what the model author wrote and what a loss itemisation reports - "a fifth
    /// of the beam is on the front plate" is actionable in a way that "transmission
    /// is 80 percent" is not, which is the whole of ACC-5.
    /// </remarks>
    public string? StruckSurface { get; init; }

    /// <summary>Accepted steps.</summary>
    /// <remarks>
    /// Under <see cref="IntegrationSettings.LandOnEdges"/> an edge or switch closer than
    /// the minimum step is taken in place, by setting the time to it and re-evaluating the
    /// derivative. That is not a step: it is counted here no more than in
    /// <see cref="RejectedSteps"/>, and shows only as one field evaluation.
    /// </remarks>
    public required int AcceptedSteps { get; init; }

    /// <summary>Rejected steps.</summary>
    /// <remarks>An in-place landing (see <see cref="AcceptedSteps"/>) is not counted here.</remarks>
    public required int RejectedSteps { get; init; }

    /// <summary>Field evaluations, the cost measure that matters once fields are solved and interpolated.</summary>
    public required long FieldEvaluations { get; init; }

    /// <summary>Distance advanced analytically through field-free regions, in metres.</summary>
    public required double AnalyticDriftDistance { get; init; }

    /// <summary>
    /// The largest relative departure of total energy from its initial value over
    /// the flight.
    /// </summary>
    /// <remarks>
    /// ACC-4 budgets this at 1 ppm in a static field and calls it a "cheap
    /// conserved-quantity diagnostic". It is cheap because the potential is
    /// already being evaluated, and diagnostic because in an electrostatic field
    /// energy drift is pure numerical error with no physical component to argue
    /// about.
    /// </remarks>
    public required double MaximumRelativeEnergyDrift { get; init; }

    /// <summary>The flight time as a quantity.</summary>
    /// <returns>The flight time.</returns>
    public Quantity FlightTime() => Quantity.Si(FlightTimeSeconds, Dimension.TimeDimension);
}
