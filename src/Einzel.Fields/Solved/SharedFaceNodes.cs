using System.Globalization;
using Einzel.Core.Results;

namespace Einzel.Fields.Solved;

/// <summary>
/// Where a mesh samples a face that two conductors share while holding different
/// excitations - at a node, or where a stencil arm first meets metal - found before a
/// solve. A grounded face of the domain counts as a conductor at zero volts.
/// </summary>
/// <param name="Nodes">Distinct nodes lying on such a face, over every such pair.</param>
/// <param name="Arms">
/// Distinct stencil arms, from a free node, whose first conductor surface lies on such a
/// face.
/// </param>
/// <param name="First">One of the two conductors at the example.</param>
/// <param name="Second">
/// The other: a conductor's name, or - when <see cref="ExampleIsBoundary"/> - which grounded
/// face of the domain, as <c>upper x face</c> or <c>right edge</c>.
/// </param>
/// <param name="X">The example's x, in meters: the node, or the point an arm meets the face.</param>
/// <param name="Y">The example's y, in meters.</param>
/// <param name="Z">The example's z, in meters, or null for a cross-section.</param>
/// <param name="ExampleIsArm">Whether the example is an arm rather than a node.</param>
/// <remarks>
/// <para>
/// <b>A coin toss, not a discretization error.</b> Two conductors at different potentials
/// may share a face - that is how every segmented chain in the library is written, and the
/// overlap checks allow it on purpose. A mesh samples the geometry in exactly two places:
/// whether each node is inside metal, and where each stencil arm from a free node first
/// meets metal. When either lands exactly on the shared face, which conductor it is
/// credited to is decided by the last bit of the face's arithmetic against the mesh's. A
/// node goes to one, the other, or neither - left free between two arms of vanishing length,
/// taking a mixture of both potentials. An arm is cut against whichever conductor's entry
/// rounded nearer, and carries that one's potential. Nothing about the geometry decides it.
/// </para>
/// <para>
/// <b>The arms are not a refinement of the idea; they are where the case that found it
/// lives.</b> <c>astral-3d</c>'s foil stripes are 0.715 mm thick on a 2.9 to 4 mm mesh, so
/// no node is inside any of them, and a check of nodes alone reports the template clean
/// with its mesh shift removed. The rounding acted on the arms in the plane of each shared
/// face, which enter both slices at the same point.
/// </para>
/// <para>
/// <b>No refinement ladder can see it.</b> Interval counts are powers of two over a fixed
/// domain, so a face on node k at one rung is on node 2k at the next: every rung sits on the
/// same faces and tosses the same coin, and the ladder reports a converged number. It was
/// found on <c>astral-3d</c> by electrostatic similarity instead - scaling the instrument by
/// 0.2, which should change nothing, moved the flight time 0.27 percent, because 0.2 is not
/// representable and so moves every coordinate by a rounding and nothing else.
/// </para>
/// <para>
/// <b>The grounded boundary is a third conductor, and gets the same test.</b> A node on a
/// Dirichlet face of the domain is pinned to zero unless an electrode has already claimed it,
/// and an electrode claims it by containing it - so a node on such a face that lies on an
/// electrode's surface holds that electrode's potential or zero according to the rounding.
/// Plates are meant to reach the edge and hold it, so this is common. Measured on a 100 V plate
/// flush with a grounded edge: a trillionth of a cell either way flips the edge node between
/// 0 and 100 V, leaves every free node's solution unchanged (no free node reaches a flipped one
/// through an uncut arm), and moves the interpolated potential half a cell off the contact by
/// 21.9 V, because the interpolant reads the node. A conductor lying <em>outside</em> the domain
/// against the face is in the solve through those nodes alone, so there the coin decides whether
/// it is in the solve at all. Only nodes are counted: a face node is fixed either way and the
/// boundary is never a cut target, so no arm can sample it. A Neumann face is a mirror and
/// does not take part: a node there flips between fixed at the conductor's potential and free
/// beside it, which is an ordinary cut cell rather than a choice between two values.
/// </para>
/// <para>
/// Qualified rather than a violation: the field is a solution of <em>a</em> geometry within
/// a cell of the declared one at a handful of places, and how much that matters depends on
/// where they are. <c>docs/numerics.md</c> records what it was worth on the one shipped
/// geometry that had it.
/// </para>
/// </remarks>
public sealed record SharedFaceNodes(
    int Nodes, int Arms, string First, string Second, double X, double Y, double? Z, bool ExampleIsArm)
{
    /// <summary>The warning code this finding is reported under.</summary>
    public const string Code = "mesh.node-on-shared-face";

    /// <summary>
    /// How near a surface a node must be to count as on it, as a fraction of the finest
    /// mesh spacing; and how near two conductors' entries along an arm must be to count as
    /// one point, as a fraction of that arm.
    /// </summary>
    /// <remarks>
    /// The overlap checks' tangency tolerance, for the same reason. A face written as an
    /// expression and a node written as origin plus index times spacing agree to a few ulps
    /// of the coordinate - about 1e-16 of it - when they coincide, so an exact test would find
    /// nothing. And the coin toss is confined to that rounding: a node a millionth of a cell
    /// from the face is inside one conductor by every arithmetic, and is an ordinary cut cell.
    /// This sits seven orders above the rounding and far below any offset anybody places a
    /// mesh at on purpose.
    /// </remarks>
    public const double ToleranceFraction = 1e-9;

    /// <summary>
    /// Distinct nodes on a grounded face of the domain lying on the surface of a conductor that
    /// holds something other than zero volts in some state - counted apart from
    /// <see cref="Nodes"/>, which lie between two conductors.
    /// </summary>
    public int BoundaryNodes { get; init; }

    /// <summary>
    /// Whether the example is such a node, in which case <see cref="Second"/> names the face.
    /// </summary>
    public bool ExampleIsBoundary { get; init; }

    /// <summary>Every place counted, nodes, arms and boundary nodes together.</summary>
    public int Count => Nodes + Arms + BoundaryNodes;

    /// <summary>The finding as a warning, to travel with every result computed through it.</summary>
    /// <returns>A qualified warning naming the counts, one example, and the fix.</returns>
    public ValidityWarning ToWarning()
    {
        var invariant = CultureInfo.InvariantCulture;

        var at = Z is { } z
            ? string.Create(invariant, $"({X * 1e3:G9}, {Y * 1e3:G9}, {z * 1e3:G9}) mm")
            : string.Create(invariant, $"({X * 1e3:G9}, {Y * 1e3:G9}) mm");

        var sentences = new List<string>(4);

        if (Nodes + Arms > 0)
        {
            var nodes = Nodes == 1 ? "1 mesh node lies on" : string.Create(invariant, $"{Nodes} mesh nodes lie on");
            var arms = Arms == 1
                ? "1 stencil arm first meets metal on"
                : string.Create(invariant, $"{Arms} stencil arms first meet metal on");

            var what = (Nodes, Arms) switch
            {
                ( > 0, > 0) => $"{nodes}, and {arms},",
                ( > 0, _) => nodes,
                _ => arms,
            };

            var example = ExampleIsBoundary
                ? string.Empty
                : ExampleIsArm
                    ? $" - for example an arm meeting it at {at}, on the face between '{First}' and '{Second}'"
                    : $" - for example the node at {at}, on the face between '{First}' and '{Second}'";

            sentences.Add($"{what} a face shared by two conductors that hold different excitations{example}.");
        }

        if (BoundaryNodes > 0)
        {
            var boundary = BoundaryNodes == 1
                ? "1 mesh node on a grounded face of the solve domain lies"
                : string.Create(invariant, $"{BoundaryNodes} mesh nodes on a grounded face of the solve domain lie");

            var example = ExampleIsBoundary
                ? $" - for example the node at {at}, where '{First}' meets the grounded {Second}"
                : string.Empty;

            sentences.Add(
                $"{boundary} on the surface of a conductor that holds something other than zero volts"
                + $"{example}. The grounded boundary is a third conductor, at zero volts.");
        }

        const string Between =
            "which conductor such a node belongs to (or whether it is left free between two arms of "
            + "vanishing length, taking a mixture of both potentials), and which conductor such an arm "
            + "is cut against";

        var decided = (Nodes + Arms > 0, BoundaryNodes > 0) switch
        {
            (true, true) => $"{Between}, and whether a node on a grounded face holds the conductor's potential or zero, is",
            (true, false) => $"{Between}, is",
            _ => "whether such a node holds the conductor's potential or zero is",
        };

        sentences.Add(
            char.ToUpperInvariant(decided[0]) + decided[1..]
            + " decided by the last bit of the face's arithmetic: a coin toss rather than "
            + "discretization error, and one no refinement ladder can see, because a power-of-two mesh "
            + "puts the same face on a node at every rung.");

        var fixes = new List<string>(2);

        if (Nodes + Arms > 0)
        {
            fixes.Add("Move the solve domain by a fraction of a cell - a tenth is plenty - or leave a gap between the conductors");
        }

        if (BoundaryNodes > 0)
        {
            // A cross-section has a spelling for a conductor that is the edge itself; a volume
            // does not, and there the domain is what moves.
            var itself = Z is null
                ? "a conductor meant to be the edge itself, lying in it or outside against it, is an edge profile"
                : "for a conductor lying in the face or outside against it, move the domain face a fraction of a cell past it";

            fixes.Add(
                (fixes.Count > 0 ? "At the grounded face, carry the conductor past it" : "Carry the conductor past the grounded face")
                + ", so every node on the face is inside the conductor by any "
                + "arithmetic, or stop it a fraction of a cell short, and keep any face that crosses the "
                + $"boundary off a line of nodes; {itself}");
        }

        return new ValidityWarning(
            Code,
            string.Join(" ", sentences) + " " + string.Join(". ", fixes),
            WarningSeverity.Qualified);
    }

    /// <summary>The finding as a list of warnings: one, or none when there was nothing.</summary>
    /// <param name="found">The finding, or null.</param>
    /// <returns>A list to put on a <see cref="SolveReport"/>.</returns>
    internal static IReadOnlyList<ValidityWarning> Warnings(SharedFaceNodes? found) =>
        found is null ? [] : [found.ToWarning()];

    /// <summary>
    /// The inclusive range of node indices along one axis that can lie within a closed
    /// interval, widened by a node at each end so rounding cannot shave one off.
    /// </summary>
    /// <returns>The range, clamped to the grid.</returns>
    internal static (int Lo, int Hi) Range(
        double origin, double spacing, int count, double lo, double hi)
    {
        // Clamped as doubles before converting, so a bound far off the grid cannot
        // overflow an int on its way to being clamped.
        var first = Math.Clamp(Math.Floor((lo - origin) / spacing), 0.0, count - 1.0);
        var last = Math.Clamp(Math.Ceiling((hi - origin) / spacing), 0.0, count - 1.0);

        return ((int)first, (int)last);
    }

    /// <summary>
    /// Where along one arm a conductor is first met, if that is strictly inside the arm.
    /// </summary>
    /// <param name="entry">The conductor's entry fraction, or null when the arm misses it.</param>
    /// <param name="at">The entry, when it is inside.</param>
    /// <returns>Whether it is.</returns>
    /// <remarks>
    /// <para>
    /// Strictly inside, as the cut links take it. An entry at zero puts the node itself on
    /// the surface, and an entry at one puts the next node there - the cut links record
    /// neither, and either node is counted as a node when it is on both surfaces, so counting
    /// the arm too would count one place twice.
    /// </para>
    /// <para>
    /// The caller asks whether the point met is on the <em>other</em> conductor's surface,
    /// not whether the arm enters both at the same fraction. The second is the obvious test
    /// and it misses the case it exists for: nudge the face one ulp and the arm, running in
    /// its plane, no longer enters one of the two at all - it grazes it - while the cut has
    /// flipped to the other conductor. A distance to a surface is continuous in the
    /// geometry, and an entry fraction is not.
    /// </para>
    /// </remarks>
    internal static bool Interior(double? entry, out double at)
    {
        at = entry ?? 0.0;
        return entry is { } e && e > 0.0 && e < 1.0 - ToleranceFraction;
    }
}
