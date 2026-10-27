namespace Einzel.Project;

/// <summary>
/// What a study manifest records of the field basis its fields were built under, when the study
/// asked for one other than the default patterns.
/// </summary>
/// <remarks>
/// The plan is part of what determines the result (PRJ-3): under it a field is a sum of
/// channels, equal to the direct field to the solver's convergence and not to the bit, so a
/// reader comparing two results needs to know which one each was built from.
/// </remarks>
public sealed record FieldBasisRecord
{
    /// <summary>The basis the study asked for: <c>varyingElectrodes</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>Each solved element the plan split, in element order; empty when no element qualified.</summary>
    public required IReadOnlyList<FieldBasisElement> Elements { get; init; }

    /// <summary>
    /// The solved elements the plan could not split, each with the reason (<c>field 0: has
    /// stages</c>, <c>field 0: geometry differs between sampled points</c>); they keep the default
    /// decomposition. Empty when there were none.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>
    /// The points that did not fit the plan and were built with the default decomposition, each
    /// named with the reason, in point order. Empty when every point fitted.
    /// </summary>
    public IReadOnlyList<string> Fallbacks { get; init; } = [];
}

/// <summary>One solved element under a field basis.</summary>
public sealed record FieldBasisElement
{
    /// <summary>Which field element, counted from zero.</summary>
    public required int Field { get; init; }

    /// <summary>The groups of electrodes the study moves, each a DC channel of its own, by name.</summary>
    public required IReadOnlyList<IReadOnlyList<string>> Groups { get; init; }

    /// <summary>
    /// The element's channels under the plan: every channel the study's sampled points are built
    /// from, each once, in the order the samples first reach them from the nominal model on - the
    /// fixed channel, a unit channel per group, the drive's. Worked out from the plan, not from
    /// whichever evaluation ran first, so a group the nominal holds at 0 V is listed too.
    /// </summary>
    public required IReadOnlyList<string> ChannelHashes { get; init; }
}
