using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Render;

/// <summary>
/// A plane solve's conductor as the solve holds it: the electrode, ended at every edge of
/// the domain that is not a mirror.
/// </summary>
/// <remarks>
/// <para>
/// <b>An outline traced over the domain could not see a face lying on the domain's edge.</b>
/// A conductor is drawn as the zero level set of its signed distance, and marching squares
/// counts a sample equal to the level as inside. A face flush with the edge has its samples
/// on the last row, at zero or inside, and nothing is sampled past the edge, so there is no
/// crossing to find and the outline stays open there. Every tube of the shipped einzel lens
/// reaches the domain's outer radius and both of its ends, and was drawn as its bore alone
/// with an annulus at each gap: a 7 mm wall read as a thin-walled tube with flanges. The
/// vector section filled the same open outline, and a filled open path is closed by a chord,
/// so it drew wedges.
/// </para>
/// <para>
/// <b>Ended against the domain rather than sampled past it.</b> The solve holds nothing beyond
/// a grounded or profiled edge, so the conductor it holds is the electrode clipped to the
/// domain: the larger of the electrode's signed distance and the distance outside the domain,
/// which is an intersection and has the edge as a face. A conductor carried past the edge -
/// which is what the grounded-face warning advises - is drawn ending where the solve ends it,
/// rather than at a declared extent nothing computed.
/// </para>
/// <para>
/// <b>Inset a millionth of a sample step</b>, so the face sits strictly inside the last row of
/// samples and the row on the edge counts as outside. Clipping exactly at the edge would leave
/// that row at zero, which still counts as inside, and find nothing.
/// </para>
/// <para>
/// <b>Not at a mirror.</b> A conductor continues across a mirror by symmetry, so a face there
/// is a face the instrument does not have; whether an edge is one is asked of
/// <see cref="GeometryBuilder.IsMirror"/>, the function the mask itself is built from.
/// </para>
/// <para>
/// <b>Every conductor that does not reach a clipped edge is traced exactly as before</b>, to
/// the bit: near its surface the electrode is nearer than the edge, so the larger of the two
/// distances is the electrode's own.
/// </para>
/// </remarks>
public static class HeldConductor
{
    /// <summary>
    /// How far inside a clipped domain edge a face lying on it is drawn, as a fraction of the
    /// sample step.
    /// </summary>
    public const double InsetFraction = 1e-6;

    /// <summary>The signed distance of an electrode as its solve holds it.</summary>
    /// <param name="solve">The solve the electrode belongs to.</param>
    /// <param name="electrode">The electrode.</param>
    /// <param name="inset">How far inside a clipped edge its face is put, in meters.</param>
    /// <returns>Negative inside, in meters, in the solve's own (x, y).</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static Func<double, double, double> Distance(
        CompiledSolvedField solve, CompiledElectrode electrode, double inset)
    {
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(electrode);

        var left = !GeometryBuilder.IsMirror(solve, GridEdge.Left);
        var right = !GeometryBuilder.IsMirror(solve, GridEdge.Right);
        var bottom = !GeometryBuilder.IsMirror(solve, GridEdge.Bottom);
        var top = !GeometryBuilder.IsMirror(solve, GridEdge.Top);

        var (minX, maxX, minY, maxY) = (solve.MinX, solve.MaxX, solve.MinY, solve.MaxY);

        return (x, y) =>
        {
            // How far outside the domain, over the edges that end a conductor. Negative
            // inside it, and minus infinity where every edge is a mirror, which leaves the
            // electrode's own distance untouched.
            var outside = double.NegativeInfinity;

            if (left)
            {
                outside = Math.Max(outside, minX - x);
            }

            if (right)
            {
                outside = Math.Max(outside, x - maxX);
            }

            if (bottom)
            {
                outside = Math.Max(outside, minY - y);
            }

            if (top)
            {
                outside = Math.Max(outside, y - maxY);
            }

            return Math.Max(electrode.SignedDistance(x, y), outside + inset);
        };
    }

    /// <summary>
    /// Samples an electrode for tracing, as its solve holds it - or as declared, when
    /// nothing of it is inside.
    /// </summary>
    /// <param name="solve">The solve the electrode belongs to.</param>
    /// <param name="electrode">The electrode.</param>
    /// <param name="step">The sample spacing, in meters; the inset is a fraction of it.</param>
    /// <param name="sample">Samples a signed distance over whatever grid the caller traces.</param>
    /// <returns>
    /// The samples to trace, and the distance they came from - which is the one to orient
    /// the traced surface by, because only it points outward through a face on the edge.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <b>Clipping closes an interior, so a conductor with no sample strictly inside it is
    /// traced as declared.</b> That is a sheet - the planar mirror pair's end cap is a
    /// rectangle of no thickness lying in the grounded edge - or something wholly outside the
    /// domain against it. Inset from the edge, such a conductor holds nothing and would vanish;
    /// traced as declared it is drawn exactly as it always was.
    /// </remarks>
    public static (double[,] Values, Func<double, double, double> Distance) Sample(
        CompiledSolvedField solve,
        CompiledElectrode electrode,
        double step,
        Func<Func<double, double, double>, double[,]> sample)
    {
        ArgumentNullException.ThrowIfNull(solve);
        ArgumentNullException.ThrowIfNull(electrode);
        ArgumentNullException.ThrowIfNull(sample);

        var held = Distance(solve, electrode, InsetFraction * step);
        var values = sample(held);

        foreach (var value in values)
        {
            if (value < 0.0)
            {
                return (values, held);
            }
        }

        Func<double, double, double> declared = electrode.SignedDistance;

        return (sample(declared), declared);
    }
}
