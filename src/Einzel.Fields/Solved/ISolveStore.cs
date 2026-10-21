using System.Diagnostics.CodeAnalysis;

namespace Einzel.Fields.Solved;

/// <summary>
/// Somewhere a solved basis channel can be kept and found again: "load, or solve and store".
/// </summary>
/// <remarks>
/// <para>
/// <b>Per channel, keyed on <see cref="SolveEntry.ChannelHash"/>.</b> A solve of one channel is
/// a function of what that hash covers and of the solver's own behaviour, which the store
/// versions; so a geometry of three channels whose one stage pattern changed reuses the other
/// two. The builders ask before they build a mask, and hand back what they solved when they
/// had to.
/// </para>
/// <para>
/// <b>Passed, never ambient.</b> Every builder takes the store as an argument that defaults to
/// none, so a call site shows whether a cache is in play and every existing caller solves as
/// it did.
/// </para>
/// <para>
/// <b>What is not kept.</b> The shared-face warnings depend on stage states the key does not
/// cover, so a hit recomputes them from the geometry; the report's scalars are kept, so an
/// unconverged field warns on every reuse exactly as it did when it was solved.
/// </para>
/// </remarks>
public interface ISolveStore
{
    /// <summary>Finds a stored solve of a channel.</summary>
    /// <param name="entry">The channel, and what its solve must match.</param>
    /// <param name="potential">
    /// The potential in <c>Grid.Index</c> order, <see cref="SolveEntry.NodeCount"/> values, when found.
    /// </param>
    /// <param name="record">How that solve went, when found.</param>
    /// <returns>
    /// True only for a solve of exactly this entry; false for a miss, and for anything stored
    /// that fails a check, which the caller then solves and saves over.
    /// </returns>
    bool TryLoad(
        SolveEntry entry,
        [NotNullWhen(true)] out double[]? potential,
        [NotNullWhen(true)] out SolveRecord? record);

    /// <summary>Keeps a channel just solved.</summary>
    /// <param name="entry">The channel.</param>
    /// <param name="potential">The potential in <c>Grid.Index</c> order.</param>
    /// <param name="record">How the solve went.</param>
    /// <remarks>
    /// Must not throw for want of disk: a store that cannot write is a slower run, never a
    /// failed one.
    /// </remarks>
    void Save(SolveEntry entry, ReadOnlySpan<double> potential, SolveRecord record);
}

/// <summary>One basis channel as a store keys and checks it.</summary>
/// <remarks>
/// Everything besides the hashes is derived from them and stored beside the potential so a
/// load can be checked against the grid it is about to be read onto: equal hashes with
/// a different grid would be a defect in the key, and a check costs nothing next to a solve.
/// </remarks>
public sealed record SolveEntry
{
    /// <summary>The channel hash (<see cref="SolveHash.Channel"/>): the key.</summary>
    public required string ChannelHash { get; init; }

    /// <summary>The element's geometry hash (<see cref="SolveHash.Geometry(Core.Model.CompiledSolvedField)"/>).</summary>
    public required string GeometryHash { get; init; }

    /// <summary>Two for a plane, three for a volume.</summary>
    public required int Dimensions { get; init; }

    /// <summary>Nodes along each axis, x first.</summary>
    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>The first node's coordinate along each axis, in metres.</summary>
    public required IReadOnlyList<double> Origin { get; init; }

    /// <summary>The node spacing along each axis, in metres.</summary>
    public required IReadOnlyList<double> Spacing { get; init; }

    /// <summary>
    /// The condition each edge of the potential carries: left, right, bottom, top in a plane;
    /// x, y, z lower and upper in a volume.
    /// </summary>
    public required IReadOnlyList<EdgeCondition> Edges { get; init; }

    /// <summary>What each declared electrode held, in declaration order.</summary>
    public required IReadOnlyList<double> Holding { get; init; }

    /// <summary>How many values the potential has.</summary>
    public long NodeCount => Nodes.Aggregate(1L, (product, count) => product * count);

    internal static SolveEntry Of(
        string geometryHash, Grid2D grid, IReadOnlyList<EdgeCondition> edges, IReadOnlyList<double> holding) => new()
    {
        ChannelHash = SolveHash.Channel(geometryHash, holding),
        GeometryHash = geometryHash,
        Dimensions = 2,
        Nodes = [grid.CountX, grid.CountY],
        Origin = [grid.OriginX, grid.OriginY],
        Spacing = [grid.SpacingX, grid.SpacingY],
        Edges = edges,
        Holding = holding,
    };

    internal static SolveEntry Of(
        string geometryHash, Grid3D grid, IReadOnlyList<EdgeCondition> faces, IReadOnlyList<double> holding) => new()
    {
        ChannelHash = SolveHash.Channel(geometryHash, holding),
        GeometryHash = geometryHash,
        Dimensions = 3,
        Nodes = [grid.CountX, grid.CountY, grid.CountZ],
        Origin = [grid.OriginX, grid.OriginY, grid.OriginZ],
        Spacing = [grid.SpacingX, grid.SpacingY, grid.SpacingZ],
        Edges = faces,
        Holding = holding,
    };
}

/// <summary>What a store keeps about a solve besides its potential.</summary>
/// <param name="Report">
/// How the solve went. Its <see cref="SolveReport.Warnings"/> are not kept: they depend on
/// more than the key, and a hit recomputes them.
/// </param>
/// <param name="FixedNodes">How many finest-level nodes the channel's mask held fixed.</param>
/// <param name="CutLinks">How many stencil arms a conductor surface cut.</param>
/// <param name="SolveSeconds">Wall time of the mask and the solve, for a reader deciding what a hit saved.</param>
public sealed record SolveRecord(SolveReport Report, int FixedNodes, int CutLinks, double SolveSeconds);
