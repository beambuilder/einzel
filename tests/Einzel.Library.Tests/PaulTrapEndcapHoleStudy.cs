using System.Globalization;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Xunit.Abstractions;
using static Einzel.Library.Tests.PaulTrapHoles;

namespace Einzel.Library.Tests;

/// <summary>
/// Holes in the endcaps and a stretched endcap spacing: what a real quadrupole ion trap
/// has that the ideal hyperboloids do not, and what each does to the field.
/// </summary>
/// <remarks>
/// <para>
/// A real trap admits ions through a hole in its entrance endcap and ejects them through
/// a hole in its exit endcap to a detector behind it, and commercial instruments move the
/// endcaps outward - the stretched trap - to offset what the holes and the truncation do.
/// All are declared parameters of the <c>paul-trap</c> template, defaulting to zero, and at
/// zero the template is the ideal hyperboloid trap, which the first test checks against the
/// hyperboloids evaluated here and against the geometry the template declared before it had
/// holes, rebuilt from that template's own expressions.
/// </para>
/// <para>
/// The multipoles are read from the on-axis potential. In the axisymmetric expansion
/// <c>phi = sum A_n rho^n P_n(cos theta)</c>, every Legendre polynomial is one on the axis,
/// so <c>phi(z, 0) = sum A_n z^n</c> and a polynomial fitted to the axial potential gives
/// each <c>A_n</c> directly. They are quoted as <c>A_n r0^(n-2) / A_2</c>, the
/// dimensionless weight relative to the quadrupole that the trap literature uses.
/// </para>
/// </remarks>
public sealed class PaulTrapEndcapHoleStudy(ITestOutputHelper output)
{
    /// <summary>
    /// The solve geometry <c>paul-trap</c> declared before it had holes, a stretch or stops:
    /// the shipped document with every expression that work touched put back to what it was.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Each pair below is the shipped expression and the one the template carried before
    /// (commit 19486e2, <c>git show 19486e2:src/Einzel.Library/Templates/paul-trap.json</c>),
    /// copied by hand. Every shipped expression must be found the stated number of times, so a
    /// later edit to the template that this list does not know about fails here rather than
    /// leaving a comparison of the template with itself; the six parameters the work added are
    /// then removed, so an old expression that still named one would not compile; and the two
    /// stops are dropped.
    /// </para>
    /// <para>
    /// What this is not: the old file. The expressions are a transcription, checked against the
    /// old file once when written (the shipped template at its defaults solves to the old
    /// file's field, bit for bit) and not on every run, because the old file is not in the tree and a
    /// frozen copy of it would fail on any later legitimate change to the template for an
    /// unrelated reason. Nothing outside the solve - descriptions, the source, the detector -
    /// is compared.
    /// </para>
    /// </remarks>
    private static ModelDocument BeforeHoles()
    {
        const string Run = "(endcapOuterRadius * k / 24)";
        const string Face =
            "(endcapHalfSpacing * sqrt(1 + ((endcapOuterRadius * k / 24) / inscribedRadius) * ((endcapOuterRadius * k / 24) / inscribedRadius)))";

        static string NowRun(string bore) =>
            $"({bore} + (endcapOuterRadius - {bore}) * k / 24)";

        static string NowFace(string bore) =>
            $"(endcapHalfSpacing * sqrt(1 + (({bore} + (endcapOuterRadius - {bore}) * k / 24) / inscribedRadius) * (({bore} + (endcapOuterRadius - {bore}) * k / 24) / inscribedRadius)) + endcapStretch)";

        // (shipped, before, how many times the shipped one occurs). In order: an entrance
        // expression is replaced before the exit one it contains.
        (string Now, string Before, int Count)[] pairs =
        [
            ("\"-" + NowFace("endcapEntranceBoreRadius") + "\"", "\"-" + Face + "\"", 1),
            ("\"" + NowFace("endcapExitBoreRadius") + "\"", "\"" + Face + "\"", 1),
            ("\"" + NowRun("endcapEntranceBoreRadius") + "\"", "\"" + Run + "\"", 1),
            ("\"" + NowRun("endcapExitBoreRadius") + "\"", "\"" + Run + "\"", 1),
            ("\"-(endcapHalfSpacing + endcapThickness + endcapStretch)\"", "\"-(endcapHalfSpacing + endcapThickness)\"", 2),
            ("\"(endcapHalfSpacing + endcapThickness + endcapStretch)\"", "\"endcapHalfSpacing + endcapThickness\"", 2),
            ("\"expression\": \"endcapEntranceBoreRadius\"", "\"expression\": \"0\"", 1),
            ("\"expression\": \"endcapExitBoreRadius\"", "\"expression\": \"0\"", 1),
            ("\"endcapHalfSpacing + endcapThickness + inscribedRadius + endcapStretch\"", "\"endcapHalfSpacing + endcapThickness + inscribedRadius\"", 1),
            ("(endcapOuterRadius / inscribedRadius)) + endcapStretch\"", "(endcapOuterRadius / inscribedRadius))\"", 1),
        ];

        var text = DeviceTemplates.Read("paul-trap");

        foreach (var (now, before, count) in pairs)
        {
            var found = text.Split(now).Length - 1;

            Assert.True(found == count, $"expected {count} of {now} in the template, found {found}");

            text = text.Replace(now, before, StringComparison.Ordinal);
        }

        var document = Io.ModelJson.Parse(text);

        var parameters = new Dictionary<string, ParameterDocument>(
            document.Parameters!, StringComparer.Ordinal);

        foreach (var added in new[]
        {
            "endcapEntranceHoleFraction", "endcapExitHoleFraction", "endcapStretchFraction",
            "endcapEntranceBoreRadius", "endcapExitBoreRadius", "endcapStretch",
        })
        {
            Assert.True(parameters.Remove(added), $"the template no longer declares {added}");
        }

        var field = document.Fields![0];
        var solve = field.Solve!;

        return document with
        {
            Parameters = parameters,
            Fields =
            [
                field with
                {
                    Solve = solve with
                    {
                        Electrodes = solve.Electrodes!
                            .Where(e => e.Name is not ("entranceStop" or "outerStop")).ToList(),
                    },
                },
            ],
        };
    }

    /// <summary>
    /// With no holes and no stretch the endcaps and ring are the ideal hyperboloids, and the
    /// solved field is the field of the geometry the template declared before it had either.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The outlines are compared with the hyperboloids evaluated here, independently of the
    /// template's expressions: the ring <c>r = r0 sqrt(1 + (z/z0)^2)</c> and each endcap
    /// <c>z = z0 sqrt(1 + (r/r0)^2)</c>, sampled at the template's 25 points, to a part in
    /// 1e14 of r0.
    /// </para>
    /// <para>
    /// The field is compared with <see cref="BeforeHoles"/>: the same number of fixed nodes
    /// and cut links, the same mesh warnings, and every node's potential equal bit for bit.
    /// So the hole and stretch expressions at zero evaluate to what the old ones did -
    /// <c>H + (R - H) k / 24</c> is <c>0 + (R k) / 24</c>, and adding and subtracting zero are
    /// exact - and the two stops, which are always present, fix no node and cut no link:
    /// <c>entranceStop</c>'s face is the domain's own grounded entrance edge and
    /// <c>outerStop</c> lies a cell outside the mesh. What that comparison is and is not is in
    /// <see cref="BeforeHoles"/>'s remarks.
    /// </para>
    /// </remarks>
    [Fact]
    public void ZeroHolesAndZeroStretchAreTheIdealHyperboloids()
    {
        var model = Compile(Trap());
        var solve = model.Fields[0].Solve!;

        var r0 = model.Parameters["inscribedRadius"].SiValue;
        var z0 = r0 / Math.Sqrt(2.0);
        var gap = model.Parameters["electrodeGap"].SiValue;
        var endcapRadius = r0 - gap;
        var ringHalf = z0 - gap;
        var tolerance = 1e-14 * r0;

        Assert.Equal(
            ["ring", "endcapEntrance", "endcapExit", "entranceStop", "outerStop"],
            solve.Electrodes.Select(e => e.Name).ToArray());

        var ring = solve.Electrodes[0].Vertices;
        var entrance = solve.Electrodes[1].Vertices;
        var exit = solve.Electrodes[2].Vertices;

        for (var k = 0; k <= 24; k++)
        {
            var z = -ringHalf + (2.0 * ringHalf * k / 24.0);
            Assert.Equal(z, ring[k].X, tolerance);
            Assert.Equal(r0 * Math.Sqrt(1.0 + ((z / z0) * (z / z0))), ring[k].Y, tolerance);

            var r = endcapRadius * k / 24.0;
            var face = z0 * Math.Sqrt(1.0 + ((r / r0) * (r / r0)));
            Assert.Equal(-face, entrance[k].X, tolerance);
            Assert.Equal(r, entrance[k].Y, tolerance);
            Assert.Equal(face, exit[k].X, tolerance);
            Assert.Equal(r, exit[k].Y, tolerance);
        }

        // The closed endcap's bore edge is on the axis.
        Assert.Equal(0.0, entrance[^1].Y);
        Assert.Equal(0.0, exit[^1].Y);

        // The stops: one whose face is the domain's entrance edge, one wholly outside the mesh.
        var half = model.Parameters["domainHalfLength"].SiValue;
        var outer = model.Parameters["ringOuterRadius"].SiValue;

        Assert.Equal(-half, solve.Electrodes[3].MaxX);
        Assert.True(solve.Electrodes[4].MinY > outer, "the outer stop should lie outside the mesh");

        // The field, against the geometry before the holes.
        var before = Compile(BeforeHoles());

        Assert.Equal(3, before.Fields[0].Solve!.Electrodes.Count);

        var a = GeometryBuilder.SolveChannels(before.Fields[0].Solve!);
        var b = GeometryBuilder.SolveChannels(solve);

        Assert.Equal(a.Count, b.Count);

        for (var c = 0; c < a.Count; c++)
        {
            Assert.Equal(a[c].FixedNodes, b[c].FixedNodes);
            Assert.Equal(a[c].CutLinks, b[c].CutLinks);
            Assert.Equal(
                a[c].Report.Warnings.Select(w => w.Code + ": " + w.Message),
                b[c].Report.Warnings.Select(w => w.Code + ": " + w.Message));

            var pa = a[c].Potential.Values;
            var pb = b[c].Potential.Values;
            Assert.Equal(pa.Length, pb.Length);

            var differing = 0;

            for (var k = 0; k < pa.Length; k++)
            {
                if (BitConverter.DoubleToInt64Bits(pa[k]) != BitConverter.DoubleToInt64Bits(pb[k]))
                {
                    differing++;
                }
            }

            output.WriteLine($"channel {c}: {pa.Length} nodes, {differing} differ");
            Assert.Equal(0, differing);
        }

        // The control, so the comparison above is known to see a change: the smallest hole a
        // fraction can say is a different mask.
        var holed = GeometryBuilder.SolveChannels(
            Compile(Trap(("endcapExitHoleFraction", 0.025))).Fields[0].Solve!);

        Assert.NotEqual(a[0].FixedNodes, holed[0].FixedNodes);
    }

    /// <summary>
    /// A hole is bounded by the endcap it is cut in: a fraction past 0.9 is refused at the
    /// parameter, so an endcap whose outline runs backwards cannot be declared.
    /// </summary>
    [Fact]
    public void AHoleCannotReachTheEdgeOfItsEndcap()
    {
        foreach (var name in new[] { "endcapEntranceHoleFraction", "endcapExitHoleFraction" })
        {
            Compile(Trap((name, 0.9)));

            var refused = ModelValidator.Validate(Trap((name, 1.0)));

            Assert.Null(refused.Model);
            Assert.Contains(refused.Errors, e => e.Path.Contains(name, StringComparison.Ordinal));
        }

        // The geometry does not depend on the mesh: the bore is the declared hole at every
        // cell size, to the bit.
        double[] meshes = [4.0, 20.0, 40.0];
        var bores = meshes
            .Select(cells => Compile(Trap(("endcapExitHoleFraction", HoleFraction), ("cellsPerRadius", cells))))
            .Select(m => (Bore: m.Parameters["endcapExitBoreRadius"].SiValue, Edge: m.Parameters["endcapOuterRadius"].SiValue))
            .ToArray();

        Assert.All(bores, b => Assert.Equal(HoleFraction * b.Edge, b.Bore));
        Assert.Single(bores.Select(b => b.Bore).Distinct());
    }

    /// <summary>
    /// The hole the measurements use has its bore face clear of the mesh's node rows at both
    /// meshes; a hole whose face lies on a row sits on a step in the field, and nothing warns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The bore is exactly what the document declares, so the geometry does not depend on the
    /// mesh. The radial spacing is <c>ringOuterRadius</c> over a power of two, 0.125 mm as
    /// shipped and 0.0625 mm at 40 cells per r0, and the 0.595 mm hole the measurements use
    /// lies 0.24 and 0.48 of a spacing off the nearest row - read here from the solved mesh.
    /// There the field is continuous in the hole: a change of a part in a million moves the
    /// axial potential by the slope times the change.
    /// </para>
    /// <para>
    /// <b>A hole whose face is on a row is the open single-conductor coincidence</b>
    /// (<c>docs/numerics.md</c>, SPEC.md "What to do next" item 11), and the second half
    /// measures it rather than claiming it away: as a declared hole crosses 0.5 mm, exactly
    /// four spacings, the axial potential at 0.8 z0 steps by 0.215 V of 400 V on the ring, for
    /// a change of a nanometre - what 12 micrometres of bore is worth just above the row. On
    /// the row it holds the value from below. No warning is raised: <c>mesh.node-on-shared-face</c>
    /// is about two conductors, or one and the grounded boundary. Asserted so that the day the
    /// engine closes that gap this fails and the template's advice is revisited.
    /// </para>
    /// </remarks>
    [Fact]
    public void ABoreFaceOnANodeRowIsAStepNothingWarnsOf()
    {
        foreach (var cells in new[] { 20.0, 40.0 })
        {
            var model = Compile(Trap(("cellsPerRadius", cells), ("endcapExitHoleFraction", HoleFraction)));
            var grid = GeometryBuilder.SolveChannels(model.Fields[0].Solve!)[0].Potential.Grid;
            var rows = (model.Parameters["endcapExitBoreRadius"].SiValue - grid.OriginY) / grid.SpacingY;
            var off = Math.Abs(rows - Math.Round(rows));

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{cells,3:F0} cells/r0: radial spacing {grid.SpacingY * 1e3:F5} mm; the {HoleFraction} hole's face is at row {rows:F3}, {off:F3} of a spacing off"));

            Assert.True(off >= 0.2, $"the measured hole's face is {off:F3} of a spacing off a row");
        }

        (double Potential, string Warnings) Probe(double fraction)
        {
            var model = Compile(Trap(("endcapExitHoleFraction", fraction)));
            var (built, warnings) = FieldAssembly.BuildReported(model);
            var z0 = model.Parameters["endcapHalfSpacing"].SiValue;

            return (
                ((ITimeVaryingField)built).PotentialAt(new Vec3(0.8 * z0, 0.0, 0.0), 0.0),
                string.Join(" | ", warnings.Select(w => w.Code)));
        }

        var endcapRadius = Compile(Trap()).Parameters["endcapOuterRadius"].SiValue;
        var solid = Probe(0.0);

        double Step(double fraction, string label)
        {
            var below = Probe(fraction * (1.0 - 1e-6));
            var above = Probe(fraction * (1.0 + 1e-6));
            var slope = Math.Abs(Probe(fraction * (1.0 + 1e-3)).Potential - above.Potential) / (1e-3 * fraction);
            var expected = slope * 2e-6 * fraction;
            var step = Math.Abs(above.Potential - below.Potential);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{label}: axial potential at 0.8 z0 {below.Potential:R} / {above.Potential:R} V across 2e-6 of the hole: "
                + $"moved {step:E3} V, the slope accounts for {expected:E3} V; warnings [{above.Warnings}]"));

            // No warning the solid trap does not carry, either side.
            Assert.Equal(solid.Warnings, below.Warnings);
            Assert.Equal(solid.Warnings, above.Warnings);

            return step / expected;
        }

        // The measured hole: continuous. Measured 1.0000 of what the slope accounts for.
        Assert.InRange(Step(HoleFraction, "0.595 mm hole, off the rows"), 0.5, 2.0);

        // A hole of exactly four spacings: a step five orders past the slope (measured 0.215 V
        // against 1.8e-5 V).
        Assert.True(
            Step(0.5e-3 / endcapRadius, "0.5 mm hole, on a row") > 1000.0,
            "a bore face crossing a node row no longer steps the field: revisit the template's advice");
    }

    /// <summary>The on-axis multipole weights, from a polynomial fit to the axial potential.</summary>
    /// <returns><c>A_n r0^(n-2) / A_2</c> for n = 0..8, and the fit's worst residual over A_2 r0^2.</returns>
    private static (double[] Weights, double Residual) AxialMultipoles(CompiledModel model, ITimeVaryingField field)
    {
        const int Degree = 8;
        const int Samples = 321;

        var r0 = model.Parameters["inscribedRadius"].SiValue;
        var half = 0.4 * r0;

        var gram = new double[Degree + 1, Degree + 1];
        var rhs = new double[Degree + 1];
        var us = new double[Samples];
        var phis = new double[Samples];

        for (var s = 0; s < Samples; s++)
        {
            var u = -1.0 + (2.0 * s / (Samples - 1));
            var phi = field.PotentialAt(new Vec3(u * half, 0.0, 0.0), 0.0);

            us[s] = u;
            phis[s] = phi;

            for (var i = 0; i <= Degree; i++)
            {
                rhs[i] += Math.Pow(u, i) * phi;

                for (var j = 0; j <= Degree; j++)
                {
                    gram[i, j] += Math.Pow(u, i + j);
                }
            }
        }

        var c = Solve(gram, rhs);

        var residual = 0.0;

        for (var s = 0; s < Samples; s++)
        {
            var fit = 0.0;

            for (var i = 0; i <= Degree; i++)
            {
                fit += c[i] * Math.Pow(us[s], i);
            }

            residual = Math.Max(residual, Math.Abs(fit - phis[s]));
        }

        // Back from the scaled variable u = z / half to z.
        var a = new double[Degree + 1];

        for (var i = 0; i <= Degree; i++)
        {
            a[i] = c[i] / Math.Pow(half, i);
        }

        var weights = new double[Degree + 1];

        for (var i = 0; i <= Degree; i++)
        {
            weights[i] = a[i] * Math.Pow(r0, i - 2) / a[2];
        }

        return (weights, residual / Math.Abs(a[2] * r0 * r0));
    }

    private static double[] Solve(double[,] matrix, double[] rhs)
    {
        var n = rhs.Length;
        var m = (double[,])matrix.Clone();
        var b = (double[])rhs.Clone();

        for (var col = 0; col < n; col++)
        {
            var pivot = col;

            for (var row = col + 1; row < n; row++)
            {
                if (Math.Abs(m[row, col]) > Math.Abs(m[pivot, col]))
                {
                    pivot = row;
                }
            }

            for (var k = 0; k < n; k++)
            {
                (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
            }

            (b[col], b[pivot]) = (b[pivot], b[col]);

            for (var row = col + 1; row < n; row++)
            {
                var f = m[row, col] / m[col, col];

                for (var k = col; k < n; k++)
                {
                    m[row, k] -= f * m[col, k];
                }

                b[row] -= f * b[col];
            }
        }

        var x = new double[n];

        for (var row = n - 1; row >= 0; row--)
        {
            var sum = b[row];

            for (var k = row + 1; k < n; k++)
            {
                sum -= m[row, k] * x[k];
            }

            x[row] = sum / m[row, row];
        }

        return x;
    }

    private sealed record Multipoles(
        string Name, double Cells, double[] Weights, double Residual,
        double Ratio, double EffectiveMm, string SharedFaceWarnings);

    private static Multipoles Measure(string name, double cells, (string, double)[] overrides)
    {
        var model = Compile(Trap(overrides.Append(("cellsPerRadius", cells)).ToArray()));
        var (built, warnings) = FieldAssembly.BuildReported(model);
        var field = (ITimeVaryingField)built;
        var volts = model.Parameters["rfAmplitude"].SiValue;

        var (weights, residual) = AxialMultipoles(model, field);
        var ratio = AxialCurvature(field, 0.4e-3) / RadialCurvature(field, 0.4e-3);

        return new Multipoles(
            name, cells, weights, residual, ratio, EffectiveRadius(field, volts) * 1e3,
            string.Join(" | ", warnings.Select(w => w.Code + ": " + w.Message)));
    }

    /// <summary>
    /// With both holes fixed, a stretch removes most of the octupole and then stops: the field
    /// half of the stretch series whose ejection edges are in <c>docs/device-templates.md</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five stretches from none to twice the 0.107 the other measurements use: A4/A2 goes
    /// -2.10, -1.10, -0.50, -0.18 and -0.23 percent, and A6/A2 -3.8, -2.9, -1.8, -0.5 and
    /// +0.5 percent. <b>This test was first written asserting the octupole rises at every
    /// step, and it failed at the last</b>: past one and a half times the stretch the octupole
    /// has stopped rising, and what goes on changing is the 12-pole, which crosses zero.
    /// </para>
    /// <para>
    /// Asserted: the octupole rises over the first three steps, each of which is three times
    /// or more the 9.4e-4 a finer mesh moves this weight; the 12-pole and the effective radius
    /// rise at every step. The last step of the octupole, 5e-4, is under what the mesh moves
    /// it, so its sign is printed and not pinned.
    /// </para>
    /// </remarks>
    [Fact]
    public void AtFixedHolesTheStretchRemovesMostOfTheOctupoleAndThenStops()
    {
        var rows = new[] { 0.0, 0.5 * Stretch, Stretch, 1.5 * Stretch, 2.0 * Stretch }
            .Select(stretch => Measure(
                stretch.ToString("F4", CultureInfo.InvariantCulture), 20.0,
                [("endcapEntranceHoleFraction", HoleFraction), ("endcapExitHoleFraction", HoleFraction),
                 ("endcapStretchFraction", stretch)]))
            .ToArray();

        output.WriteLine("stretch   A4/A2      A6/A2      r0_eff(mm)");

        foreach (var row in rows)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Name}  {row.Weights[4],9:E3}  {row.Weights[6],9:E2}  {row.EffectiveMm,9:F4}"));
        }

        for (var k = 1; k < rows.Length; k++)
        {
            if (k <= 3)
            {
                Assert.True(rows[k].Weights[4] > rows[k - 1].Weights[4], $"A4 did not rise from stretch {rows[k - 1].Name} to {rows[k].Name}");
            }

            Assert.True(rows[k].Weights[6] > rows[k - 1].Weights[6], $"A6 did not rise from stretch {rows[k - 1].Name} to {rows[k].Name}");
            Assert.True(rows[k].EffectiveMm > rows[k - 1].EffectiveMm, $"r0_eff did not grow from stretch {rows[k - 1].Name} to {rows[k].Name}");
        }
    }

    /// <summary>
    /// What the holes and the stretch do to the multipoles, at two meshes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asserted are the properties fixed by symmetry and by the direction of each change,
    /// not the magnitudes, which are in <c>docs/device-templates.md</c> with this test as
    /// their recorded command: two equal holes keep the trap symmetric about its center
    /// plane, so every odd weight vanishes; one hole breaks that and the odd weights appear;
    /// and a mesh twice as fine changes the octupole weight by much less than any of the
    /// geometric changes it is quoted beside.
    /// </para>
    /// </remarks>
    [Fact]
    public void HolesAndStretchMoveTheMultipoles()
    {
        var rows = new List<Multipoles>();

        foreach (var cells in new[] { 20.0, 40.0 })
        {
            foreach (var (name, overrides) in Geometries)
            {
                rows.Add(Measure(name, cells, overrides));
            }
        }

        output.WriteLine(
            "geometry           cells    A1/A2 r0^-1   A3/A2     A4/A2      A6/A2      ratio      r0_eff(mm)  fit");

        foreach (var row in rows)
        {
            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{row.Name,-18} {row.Cells,4:F0}  {row.Weights[1],11:E3}  {row.Weights[3],9:E2}  "
                + $"{row.Weights[4],9:E3}  {row.Weights[6],9:E2}  {row.Ratio,9:F5}  {row.EffectiveMm,9:F5}  "
                + $"{row.Residual:E1}"));
        }

        output.WriteLine("mesh warnings, ideal at 20 cells: " + rows[0].SharedFaceWarnings);

        Multipoles At(string name, double cells) =>
            rows.Single(r => r.Name == name && r.Cells == cells);

        foreach (var cells in new[] { 20.0, 40.0 })
        {
            // Symmetric about the center plane: odd weights are round-off.
            foreach (var name in new[] { "ideal", "both holes", "stretch only", "holes + stretch" })
            {
                Assert.True(Math.Abs(At(name, cells).Weights[3]) < 1e-4, $"{name}: A3 {At(name, cells).Weights[3]:E2}");
                Assert.True(Math.Abs(At(name, cells).Weights[1]) < 1e-4, $"{name}: A1 {At(name, cells).Weights[1]:E2}");
            }

            // One hole: the symmetry is gone, and with it the odd weights' excuse.
            Assert.True(
                Math.Abs(At("exit hole only", cells).Weights[3]) > 10.0 * Math.Abs(At("ideal", cells).Weights[3]) + 1e-4,
                "one hole should add a hexapole");

            // No warning a hole or its stop adds. The ideal template already carries one - the
            // ring's outer face lying in the grounded domain edge, recorded and harmless in
            // docs/numerics.md - and its node count moves only with the domain's length, which
            // the stretch changes; a bore adds nothing (not even one whose face is on a node row,
            // which is the gap ABoreFaceOnANodeRowIsAStepNothingWarnsOf measures), and neither do
            // a grounded stop flush with the grounded edge and one outside the mesh.
            Assert.Contains("'ring' meets the grounded top edge", At("ideal", cells).SharedFaceWarnings, StringComparison.Ordinal);
            Assert.Equal(At("ideal", cells).SharedFaceWarnings, At("both holes", cells).SharedFaceWarnings);
            Assert.Equal(At("ideal", cells).SharedFaceWarnings, At("exit hole only", cells).SharedFaceWarnings);
            Assert.Equal(At("stretch only", cells).SharedFaceWarnings, At("holes + stretch", cells).SharedFaceWarnings);
        }

        // The mesh: twice as fine moves the octupole weight by much less than the holes or the
        // stretch move it - measured at 0.03 of the change for both holes and 0.11 for holes
        // plus stretch, whose change is the smallest, so asserted under a fifth. A bound at
        // exactly the measured ratio would be asserting which side of it the arithmetic lands.
        foreach (var name in new[] { "both holes", "holes + stretch", "stretch only" })
        {
            var geometric = Math.Abs(At(name, 40.0).Weights[4] - At("ideal", 40.0).Weights[4]);
            var mesh = Math.Abs(At(name, 40.0).Weights[4] - At(name, 20.0).Weights[4]);

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{name}: octupole moved {geometric:E2} by geometry, {mesh:E2} by the mesh"));

            Assert.True(mesh < 0.2 * geometric, $"{name}: the mesh moves A4 by {mesh:E2} against {geometric:E2}");
        }
    }
}
