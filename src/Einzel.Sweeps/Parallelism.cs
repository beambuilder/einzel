namespace Einzel.Sweeps;

/// <summary>How many evaluations a study driver runs at once.</summary>
/// <remarks>
/// <para>
/// One rule, read by the drivers that run evaluations in parallel and by the cost estimate that
/// bills their memory. Every evaluation in flight compiles its own model and solves its own
/// field, so the number in flight multiplies a study's peak memory - and the estimate restated
/// this rule rather than asking for it, which is how an estimate and the thing it estimates part
/// company: the same argument that has <c>einzel estimate</c> and <c>einzel run</c> call one
/// function for a diffusive step.
/// </para>
/// <para>
/// <see cref="Optimiser"/> and <see cref="BoundarySearch"/> evaluate one point at a time and do
/// not use it.
/// </para>
/// </remarks>
public static class Parallelism
{
    /// <summary>The degree of parallelism a driver runs at.</summary>
    /// <param name="maxParallelism">What the study declares, or null for one per processor.</param>
    /// <returns>At least one.</returns>
    public static int Degree(int? maxParallelism) =>
        Math.Max(1, maxParallelism ?? Environment.ProcessorCount);

    /// <summary>How many of a phase's evaluations are in flight at once.</summary>
    /// <param name="evaluations">How many evaluations the phase runs in parallel.</param>
    /// <param name="maxParallelism">What the study declares, or null for one per processor.</param>
    /// <returns>
    /// The degree of parallelism, capped by the work: a five-point scan on a sixteen-core machine
    /// has five in flight. At least one.
    /// </returns>
    public static int InFlight(int evaluations, int? maxParallelism) =>
        Math.Min(Degree(maxParallelism), Math.Max(1, evaluations));
}
