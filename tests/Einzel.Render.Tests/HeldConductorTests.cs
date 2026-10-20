using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;
using Einzel.Library;
using Einzel.Render;
using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// A plane solve's conductor as the solve holds it, which is what both the viewport and the
/// vector section trace.
/// </summary>
/// <remarks>
/// <para>
/// The einzel lens is the case: three tubes from the bore at 5 mm to the domain's outer
/// radius at 12 mm, the first and last also reaching the domain's two ends. Traced over the
/// domain alone, a face on the edge has no sample outside it, so the outline stays open there.
/// </para>
/// <para>
/// Every expectation is the template's own declared geometry, or the declared signed distance
/// itself.
/// </para>
/// </remarks>
public sealed class HeldConductorTests(ITestOutputHelper output)
{
    private const double Millimeter = 1e-3;

    /// <summary>The viewport's own sampling of a solve, as it traces a conductor.</summary>
    private const int Columns = 240;

    private static CompiledModel Compile(string template)
    {
        var document = ModelJson.Parse(DeviceTemplates.Read(template));
        var validation = ModelValidator.Validate(document, null);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    private static CompiledSolvedField Solve(string template) =>
        Compile(template).Fields.Select(f => f.Solve).OfType<CompiledSolvedField>().First();

    /// <summary>A sampling of a solve's domain, edges included, as the viewport takes it.</summary>
    private sealed record Sampling(CompiledSolvedField Solve)
    {
        public int Rows { get; } = Math.Max(
            16, (int)Math.Round(Columns * (Solve.MaxY - Solve.MinY) / (Solve.MaxX - Solve.MinX)));

        public double StepU => (Solve.MaxX - Solve.MinX) / (Columns - 1);

        public double StepV => (Solve.MaxY - Solve.MinY) / (Rows - 1);

        public double[,] Of(Func<double, double, double> distance)
        {
            var values = new double[Columns, Rows];

            for (var j = 0; j < Rows; j++)
            {
                for (var i = 0; i < Columns; i++)
                {
                    values[i, j] = distance(Solve.MinX + (i * StepU), Solve.MinY + (j * StepV));
                }
            }

            return values;
        }

        public IReadOnlyList<Contours.Run> Trace(double[,] values) =>
            Contours.Trace(values, Solve.MinX, Solve.MinY, StepU, StepV, 0.0);
    }

    /// <summary>A conductor clear of every clipped edge is traced exactly as declared.</summary>
    /// <remarks>
    /// To the bit, which is the claim that lets the change be made at all: near its surface a
    /// conductor inside the domain is nearer than any edge, so the larger of the two distances
    /// is its own. Checked on the lens's center tube stopped 2 mm short of the outer wall,
    /// sampled at five times the viewport's density over the whole domain, edges included.
    /// </remarks>
    [Fact]
    public void AConductorClearOfEveryEdgeIsTracedExactlyAsDeclared()
    {
        var solve = Solve("einzel-lens");
        var center = solve.Electrodes.Single(e => e.Name == "centre") with { MaxY = 10.0 * Millimeter };

        var held = HeldConductor.Distance(solve, center, 1e-9);

        var (columns, rows) = (1201, 301);
        var differ = 0;

        for (var j = 0; j < rows; j++)
        {
            for (var i = 0; i < columns; i++)
            {
                var x = solve.MinX + ((solve.MaxX - solve.MinX) * i / (columns - 1));
                var y = solve.MinY + ((solve.MaxY - solve.MinY) * j / (rows - 1));

                if (BitConverter.DoubleToInt64Bits(held(x, y))
                    != BitConverter.DoubleToInt64Bits(center.SignedDistance(x, y)))
                {
                    differ++;
                }
            }
        }

        output.WriteLine($"{differ} of {columns * rows} samples differ from the declared distance");

        Assert.Equal(0, differ);
    }

    /// <summary>A face on a grounded edge closes the outline, just inside the edge.</summary>
    /// <remarks>
    /// The control is the same tube traced by its declared distance, which is what the
    /// viewport did: open, with nothing on the outer radius but the gap faces' ends.
    /// </remarks>
    [Fact]
    public void AFaceOnAGroundedEdgeClosesTheOutlineJustInsideIt()
    {
        var solve = Solve("einzel-lens");
        var center = solve.Electrodes.Single(e => e.Name == "centre");
        var sampling = new Sampling(solve);
        var step = Math.Min(sampling.StepU, sampling.StepV);

        var (values, _) = HeldConductor.Sample(solve, center, step, sampling.Of);
        var held = Assert.Single(sampling.Trace(values));
        var declared = sampling.Trace(sampling.Of(center.SignedDistance));

        var top = held.Points.Max(p => p.V);
        var inset = HeldConductor.InsetFraction * step;

        output.WriteLine(
            $"held: closed {held.Closed}, {held.Points.Count} points, top at "
            + $"{top / Millimeter:F9} mm against the edge at {solve.MaxY / Millimeter:F9} mm");
        output.WriteLine(
            $"declared: {declared.Count} run(s), closed {string.Join(", ", declared.Select(r => r.Closed))}");

        Assert.True(held.Closed);
        Assert.InRange(solve.MaxY - top, 0.0, 1.000001 * inset);

        Assert.All(declared, run => Assert.False(run.Closed));
    }

    /// <summary>A conductor against a mirror is not closed there.</summary>
    /// <remarks>
    /// By symmetry a conductor continues across a mirror, so the edge is where the half-domain
    /// stops describing it. The lens with its left edge declared a mirror: the entrance tube
    /// is closed against the grounded outer radius and left open at x = 0, exactly as its
    /// declared distance leaves it there.
    /// </remarks>
    [Fact]
    public void AConductorAgainstAMirrorIsNotClosedThere()
    {
        var solve = Solve("einzel-lens") with { LeftEdge = BoundaryKind.Neumann };
        var entrance = solve.Electrodes.Single(e => e.Name == "entrance");
        var sampling = new Sampling(solve);

        var (values, _) = HeldConductor.Sample(
            solve, entrance, Math.Min(sampling.StepU, sampling.StepV), sampling.Of);
        var run = Assert.Single(sampling.Trace(values));

        var alongMirror = run.Points
            .Zip(run.Points.Skip(1))
            .Count(pair => pair.First.U == solve.MinX && pair.Second.U == solve.MinX);

        output.WriteLine(
            $"closed {run.Closed}, {run.Points.Count} points, {alongMirror} segments along the "
            + $"mirror, top at {run.Points.Max(p => p.V) / Millimeter:F6} mm");

        Assert.False(run.Closed);
        Assert.Equal(0, alongMirror);
        Assert.InRange(run.Points.Max(p => p.V), solve.MaxY - 1e-9, solve.MaxY);
    }

    /// <summary>Which edges are mirrors, asked of the function the mask is built from.</summary>
    /// <remarks>
    /// The axis of an axisymmetric solve is one whatever the document says; a declared
    /// Neumann edge is one; every other edge is held at a potential.
    /// </remarks>
    [Fact]
    public void TheMirrorsAreTheMasksMirrors()
    {
        var lens = Solve("einzel-lens");
        var pair = Solve("planar-mirror-pair");

        Assert.True(GeometryBuilder.IsMirror(lens, GridEdge.Bottom));
        Assert.False(GeometryBuilder.IsMirror(lens, GridEdge.Top));
        Assert.False(GeometryBuilder.IsMirror(lens, GridEdge.Left));
        Assert.False(GeometryBuilder.IsMirror(lens, GridEdge.Right));

        Assert.True(GeometryBuilder.IsMirror(pair, GridEdge.Right));
        Assert.False(GeometryBuilder.IsMirror(pair, GridEdge.Left));
        Assert.False(GeometryBuilder.IsMirror(pair, GridEdge.Bottom));
    }

    /// <summary>A sheet lying in a grounded edge is traced as declared.</summary>
    /// <remarks>
    /// The planar mirror pair's end cap is a rectangle of no thickness lying in the grounded
    /// left edge. Clipped and inset from the edge it holds nothing at all, so it would vanish;
    /// clipping only ever closes an interior, so a conductor with no sample inside it is
    /// traced from its declared distance, exactly as it was.
    /// </remarks>
    [Fact]
    public void ASheetInAGroundedEdgeIsTracedAsDeclared()
    {
        var solve = Solve("planar-mirror-pair");
        var cap = solve.Electrodes.Single(e => e.Name == "cap");
        var sampling = new Sampling(solve);

        var (values, _) = HeldConductor.Sample(
            solve, cap, Math.Min(sampling.StepU, sampling.StepV), sampling.Of);
        var declared = sampling.Of(cap.SignedDistance);

        var differ = 0;

        for (var j = 0; j < sampling.Rows; j++)
        {
            for (var i = 0; i < Columns; i++)
            {
                if (BitConverter.DoubleToInt64Bits(values[i, j])
                    != BitConverter.DoubleToInt64Bits(declared[i, j]))
                {
                    differ++;
                }
            }
        }

        var runs = sampling.Trace(values);

        output.WriteLine(
            $"{differ} samples differ from the declared distance; {runs.Count} run(s), "
            + $"x from {runs.Min(r => r.Points.Min(p => p.U)) / Millimeter:F3} to "
            + $"{runs.Max(r => r.Points.Max(p => p.U)) / Millimeter:F3} mm");

        Assert.Equal(0, differ);
        Assert.NotEmpty(runs);
        Assert.All(runs, run => Assert.All(run.Points, p => Assert.Equal(solve.MinX, p.U)));
    }
}
