using System.Globalization;
using Xunit.Abstractions;
using static Einzel.Library.Tests.PaulTrapEdgeEnsemble;
using static Einzel.Library.Tests.PaulTrapHoles;

namespace Einzel.Library.Tests;

/// <summary>
/// The ejection edge of the holed and stretched quadrupole trap, measured on an ensemble: does
/// the stretch bring the holed trap's edge back toward the ideal one, and by how much.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PaulTrapEndcapHoleDynamics"/> bisects one ion's fate and finds, with holes, a
/// strip: the bracket moves with the integrator's tolerance and with a micrometre of launch, by
/// about as much as the two holed geometries differ. That could not say whether the stretch
/// moves the edge. This class asks the question of <see cref="PaulTrapEdgeEnsemble"/> instead -
/// the loss fraction of a seeded cloud against effective q, the 50 percent crossing, and a
/// bootstrap interval on it - where one ion's coin is one vote in <see cref="Ions"/>.
/// </para>
/// <para>
/// <b>What is held fixed.</b> The comparison is in effective q: each geometry's amplitude is
/// scaled by its own effective radius, taken from the curvature of its own solved field, so the
/// linear Mathieu boundary is at the same q for all of them and the stretch's change of scale
/// (r0_eff 4.019 to 4.252 mm, 11 percent in volts) is taken out. What is left is what the
/// higher multipoles do to an ion on its way to an endcap. The cloud is the same in millimetres
/// in every geometry, ion for ion; the hold is the same number of RF cycles; the loss criterion
/// is the vertex plane of that geometry's own endcaps.
/// </para>
/// <para>
/// The numbers asserted here are from 64 ions on a 0.001 grid of q; the 200-ion tables, the
/// 2000-cycle hold, the 40-cell mesh, the other cloud sizes and the stretch series are recorded
/// in <c>docs/device-templates.md</c> and are not run here.
/// </para>
/// </remarks>
public sealed class PaulTrapEndcapHoleEdge(ITestOutputHelper output)
{
    private const int Ions = 64;

    private const double Cycles = 800.0;

    // The 200-ion edges, q_eff at 50 percent loss (docs/device-templates.md).
    private const double IdealEdge = 0.9058;
    private const double OneHoleEdge = 0.9102;
    private const double StretchedEdge = 0.9146;
    private const double HoledEdge = 0.9176;

    private const double Margin = 0.0012;

    // How far the stretch brings the two-holed edge back: 0.0030 measured, 0.0027 to 0.0037
    // over every control on 200 ions; the window adds the 64-ion sampling error, about 0.0005.
    private const double StepLow = 0.0020;
    private const double StepHigh = 0.0045;

    private static readonly Dictionary<string, Solved> Traps = new(StringComparer.Ordinal);

    private static readonly Dictionary<(string, int, double), Lazy<bool[][]>> Tables = [];

    /// <summary>
    /// The q grid each geometry is scanned on: 0.001 steps, from below where the first ion of
    /// the 200-ion study is lost to above where the last is.
    /// </summary>
    private static readonly Dictionary<string, double[]> Grids = new(StringComparer.Ordinal)
    {
        ["ideal"] = Grid(0.899, 0.912, 14),
        ["exit hole only"] = Grid(0.902, 0.915, 14),
        ["holes + stretch"] = Grid(0.908, 0.921, 14),
        ["both holes"] = Grid(0.910, 0.925, 16),
    };

    private static Solved Trap(string name)
    {
        lock (Traps)
        {
            if (!Traps.TryGetValue(name, out var trap))
            {
                trap = new Solved(Geometry(name));
                Traps[name] = trap;
            }

            return trap;
        }
    }

    /// <summary>
    /// One geometry's loss table for one cloud and tolerance, flown once for the whole class:
    /// the shipped-tolerance tables are read by more than one test.
    /// </summary>
    private static bool[][] Lost(string name, int seed, double relativeTolerance)
    {
        Lazy<bool[][]>? table;

        lock (Tables)
        {
            if (!Tables.TryGetValue((name, seed, relativeTolerance), out table))
            {
                table = new Lazy<bool[][]>(
                    () => Losses(Trap(name), Cloud(Ions, seed), Grids[name], Cycles, relativeTolerance));
                Tables[(name, seed, relativeTolerance)] = table;
            }
        }

        return table.Value;
    }

    private Edge Measure(string name, int seed = 1, double relativeTolerance = 1e-9)
    {
        var q = Grids[name];
        var edge = EdgeOf(q, Lost(name, seed, relativeTolerance));

        // The grid must hold the whole curve, or the 10 and 90 percent crossings are NaN and
        // the 50 percent one is read off a curve cut short.
        Assert.False(double.IsNaN(edge.Q10) || double.IsNaN(edge.Q90), $"{name}: the q grid does not span the loss curve");

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name,-16} seed {seed} tol {relativeTolerance:E0}  q50 {edge.Q50:F4} [{edge.Low:F4}, {edge.High:F4}]  "
            + $"10-90% {edge.Q10:F4}-{edge.Q90:F4} (width {edge.Width:F4})  {edge.Q50 / Trap(name).QPerVolt:F2} V"));

        return edge;
    }

    /// <summary>
    /// The four geometries' ensemble edges, in order, and the answer to the question: the
    /// stretch brings the two-holed trap's edge back toward the ideal one by about 0.003 of
    /// q_eff, a quarter of what the holes moved it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each window is the 200-ion value in <c>docs/device-templates.md</c> with 0.0012 of q
    /// either side, chosen before the 64-ion edges were measured: more than the half-width of
    /// the widest 64-ion interval (0.0008), twice the largest move any control made in a holed
    /// edge (0.0006, the hold), and less than half the 0.003 between the two holed
    /// geometries, so a window cannot pass an edge that has become its neighbour's.
    /// </para>
    /// <para>
    /// The order is asserted as separated intervals, and the step between the two holed
    /// geometries as a window: it was 0.0027 to 0.0037 on 200 ions under every control tried
    /// (tolerance, cloud, hold, mesh), and 0.0030 with a standard deviation of 0.0002 across
    /// eight independent clouds.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheStretchBringsTheHoledEdgeAQuarterOfTheWayBack()
    {
        var ideal = Measure("ideal");
        var oneHole = Measure("exit hole only");
        var stretched = Measure("holes + stretch");
        var holed = Measure("both holes");

        Assert.InRange(ideal.Q50, IdealEdge - Margin, IdealEdge + Margin);
        Assert.InRange(oneHole.Q50, OneHoleEdge - Margin, OneHoleEdge + Margin);
        Assert.InRange(stretched.Q50, StretchedEdge - Margin, StretchedEdge + Margin);
        Assert.InRange(holed.Q50, HoledEdge - Margin, HoledEdge + Margin);

        // Ordered, and resolved: each geometry's whole interval lies above the one before.
        Assert.True(ideal.High < oneHole.Low, "the ideal trap and one hole are not separated");
        Assert.True(oneHole.High < stretched.Low, "one hole and holes plus stretch are not separated");
        Assert.True(stretched.High < holed.Low, "holes plus stretch and both holes are not separated");

        var back = holed.Q50 - stretched.Q50;
        var moved = holed.Q50 - ideal.Q50;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"holes move the edge out {moved:F4}; the stretch brings it back {back:F4}, {back / moved:P0} of that"));

        Assert.InRange(back, StepLow, StepHigh);
        Assert.InRange(back / moved, 0.15, 0.40);
    }

    /// <summary>
    /// The ensemble edge does not move with the integrator's tolerance, where one ion's
    /// bisected edge moved by a part in a thousand; and the ideal trap loses the same ions.
    /// </summary>
    /// <remarks>
    /// The single-ion control that found the strip is
    /// <see cref="PaulTrapEndcapHoleDynamics.TheHoledEdgeMovesWithTheIntegratorToleranceAndTheIdealOneDoesNot"/>,
    /// which also checks that the tolerance reaches the integrator at all. Here: in the ideal
    /// trap every ion at every amplitude has the same fate at 1e-8 as at 1e-9; with holes,
    /// individual ions near the edge change sides - counted and printed - and the 50 percent
    /// crossing stays inside the interval the 1e-9 cloud states for itself.
    /// </remarks>
    [Fact]
    public void TheEnsembleEdgeHoldsUnderTheIntegratorTolerance()
    {
        foreach (var name in new[] { "ideal", "holes + stretch", "both holes" })
        {
            var q = Grids[name];
            var loose = Lost(name, 1, 1e-8);
            var shipped = Lost(name, 1, 1e-9);

            var changed = Enumerable.Range(0, q.Length)
                .Sum(p => Enumerable.Range(0, Ions).Count(k => loose[p][k] != shipped[p][k]));

            var at8 = EdgeOf(q, loose);
            var at9 = EdgeOf(q, shipped);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name,-16} {changed} of {q.Length * Ions} flights change sides between 1e-8 and 1e-9; "
                + $"q50 {at8.Q50:F5} against {at9.Q50:F5} [{at9.Low:F5}, {at9.High:F5}]"));

            if (name == "ideal")
            {
                Assert.Equal(0, changed);
            }
            else
            {
                Assert.InRange(at8.Q50, at9.Low, at9.High);
            }
        }
    }

    /// <summary>
    /// Another draw of the cloud gives the same edges inside the same windows, and the same
    /// order and step between the two holed geometries.
    /// </summary>
    [Fact]
    public void AnotherCloudGivesTheSameEdgesAndTheSameOrder()
    {
        var stretched = Measure("holes + stretch", seed: 2);
        var holed = Measure("both holes", seed: 2);

        Assert.InRange(stretched.Q50, StretchedEdge - Margin, StretchedEdge + Margin);
        Assert.InRange(holed.Q50, HoledEdge - Margin, HoledEdge + Margin);
        Assert.True(stretched.High < holed.Low, "holes plus stretch and both holes are not separated on the second cloud");
        Assert.InRange(holed.Q50 - stretched.Q50, StepLow, StepHigh);
    }
}
