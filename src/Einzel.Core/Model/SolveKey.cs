namespace Einzel.Core.Model;

/// <summary>What one solved field element was solved from, as content hashes.</summary>
/// <remarks>
/// <para>
/// <b>The model hash says which text was run; this says which physics was solved.</b> A
/// comment, a renamed electrode or a parameter written another way that evaluates to the
/// same number changes the first and not the second, and a field cache keyed on the text
/// would miss every time somebody tidied a model. Keyed on this instead, a solve is reused
/// exactly when the solver would be handed the same problem.
/// </para>
/// <para>
/// Two levels, because the solves of one element share a mesh and differ only in what each
/// electrode holds. <see cref="GeometryHash"/> covers the mesh and the conductors; each of
/// <see cref="ChannelHashes"/> adds one channel's pattern to it. A static element has one
/// channel whose pattern is its declared potentials, since a static solve is made with them.
/// </para>
/// <para>
/// Neither includes the solver-behaviour version, which a run manifest records beside these
/// and a cache must combine with them (FLD-3): the hashes say what was asked, the version
/// says how it was answered. Here rather than in the field layer because the manifest that
/// records it sits in an assembly that cannot see the field layer.
/// </para>
/// </remarks>
public sealed record SolveKey
{
    /// <summary>Which field element of the model this is, counted from zero.</summary>
    public required int Field { get; init; }

    /// <summary>How many dimensions the element is solved in.</summary>
    public required int Dimensions { get; init; }

    /// <summary>
    /// The mesh and the conductors: everything the solves of this element share, as
    /// <c>sha256:</c> and 64 hex characters.
    /// </summary>
    public required string GeometryHash { get; init; }

    /// <summary>One hash per basis channel, in the order the decomposition found them.</summary>
    public required IReadOnlyList<string> ChannelHashes { get; init; }
}
