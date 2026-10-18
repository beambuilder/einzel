using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A node exactly on a face two disagreeing conductors share, or a stencil arm first meeting
/// metal there, is decided by rounding, and the geometry builders say so before solving.
/// </summary>
/// <remarks>
/// <para>
/// Two boxes at +100 V and -100 V abutting across a plane of nodes is the smallest case
/// there is. The claim has two halves and both are tested: that such a node really is a
/// coin toss (one ulp of the face moves the conductor it belongs to, and so moves the
/// discrete interface by a whole cell), and that the detector finds it, and finds nothing
/// once the same geometry is moved a quarter of a cell off the nodes.
/// </para>
/// <para>
/// Every coordinate is taken from the grid itself - a face "on a node" is placed at
/// <c>grid.Z(k)</c> rather than at a round number - so the tests do not depend on whether
/// some decimal happens to round onto a node.
/// </para>
/// </remarks>
public sealed class SharedFaceNodeTests(ITestOutputHelper output)
{
    private const double Half = 0.008;

    private const double Cell = 0.001;

    /// <summary>The grid every volume case here is solved on: 16 intervals an axis.</summary>
    private static Grid3D Volume() =>
        Grid3D.OverBox(-Half, -Half, -Half, Half, Half, Half, Cell);

    /// <summary>
    /// Two boxes stacked along z, meeting at <paramref name="face"/>, with their other faces
    /// on nodes 4 and 12 of the grid so the shared face spans nine by nine of them.
    /// </summary>
    private static Geometry3D Stacked(double face, double lower = 100.0, double upper = -100.0)
    {
        var grid = Volume();

        return new Geometry3D(
            -Half, -Half, -Half, Half, Half, Half, Cell,
            [
                Box("lower", grid, grid.Z(4), face, lower),
                Box("upper", grid, face, grid.Z(12), upper),
            ],
            Tolerance: 1e-9);
    }

    private static CompiledElectrode3D Box(string name, Grid3D grid, double minZ, double maxZ, double volts) => new()
    {
        Name = name,
        Shape = Electrode3DShape.Box,
        MinX = grid.X(4),
        MaxX = grid.X(12),
        MinY = grid.Y(4),
        MaxY = grid.Y(12),
        MinZ = minZ,
        MaxZ = maxZ,
        Potential = volts,
    };

    [Fact]
    public void TwoBoxesSharingAFaceOnAPlaneOfNodesAreFlagged()
    {
        var grid = Volume();
        var found = GeometryBuilder3D.NodesOnSharedFaces(Stacked(grid.Z(10)), grid);

        Assert.NotNull(found);

        // The shared face spans nodes 4 to 12 in x and in y, inclusive - and nothing else is
        // on both surfaces, because the side faces of the two boxes meet only along the rim
        // of that same face.
        Assert.Equal(81, found.Nodes);
        Assert.Equal(0, found.Arms);
        Assert.Equal("lower", found.First);
        Assert.Equal("upper", found.Second);
        Assert.Equal(grid.Z(10), found.Z);

        output.WriteLine(found.ToWarning().Message);
    }

    [Fact]
    public void TheSameBoxesAQuarterCellOffTheNodesAreNot()
    {
        var grid = Volume();
        var face = grid.Z(10) + (0.25 * grid.SpacingZ);

        Assert.Null(GeometryBuilder3D.NodesOnSharedFaces(Stacked(face), grid));
    }

    /// <summary>
    /// The claim the warning makes, checked directly: one ulp of the face decides which
    /// conductor the node belongs to, while a quarter cell off the nodes it decides nothing.
    /// </summary>
    /// <remarks>
    /// On the face the node is inside both boxes and goes to whichever was written last; one
    /// ulp above it is inside only the lower box, one ulp below only the upper. So the
    /// discrete interface sits between nodes 10 and 11 or between nodes 9 and 10 according to
    /// the last bit of a coordinate - a whole cell, which no refinement of this mesh changes,
    /// since every power-of-two refinement keeps the face on a node.
    /// </remarks>
    [Fact]
    public void OneUlpOfTheFaceDecidesWhichConductorTheNodeBelongsTo()
    {
        var grid = Volume();
        var face = grid.Z(10);

        double NodeAt(double z)
        {
            var mask = GeometryBuilder3D.BuildMask(Stacked(z), grid);
            Assert.True(mask.IsFixed(8, 8, 10));
            return mask.Value(8, 8, 10);
        }

        var below = NodeAt(Math.BitDecrement(face));
        var on = NodeAt(face);
        var above = NodeAt(Math.BitIncrement(face));

        output.WriteLine($"node (8, 8, 10) holds {below} V, {on} V, {above} V with the face one ulp below, on, one ulp above it");

        Assert.Equal(-100.0, below);
        Assert.Equal(100.0, above);

        // And the detector calls all three the same thing, which is the point: they are one
        // geometry to within a rounding, and the mesh cannot tell which it was given.
        foreach (var z in new[] { Math.BitDecrement(face), face, Math.BitIncrement(face) })
        {
            Assert.Equal(81, GeometryBuilder3D.NodesOnSharedFaces(Stacked(z), grid)?.Count);
        }

        // The control: a quarter cell off the nodes, a rounding moves nothing anywhere.
        var offset = face + (0.25 * grid.SpacingZ);
        var down = GeometryBuilder3D.BuildMask(Stacked(Math.BitDecrement(offset)), grid);
        var up = GeometryBuilder3D.BuildMask(Stacked(Math.BitIncrement(offset)), grid);

        for (var k = 0; k < grid.CountZ; k++)
        {
            Assert.Equal(down.IsFixed(8, 8, k), up.IsFixed(8, 8, k));
            Assert.Equal(down.Value(8, 8, k), up.Value(8, 8, k));
        }
    }

    /// <summary>
    /// The third outcome, and the reason the tolerance exists: a face written two ways that
    /// misses the node by an ulp on each side leaves it in neither conductor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two faces meant to coincide are two expressions over the parameter surface and agree
    /// to a few ulps rather than exactly. Here they straddle the node, so it is inside
    /// neither box, is left free, and gets a stencil arm of vanishing length to each side -
    /// floored at <see cref="CutLinks3D.MinimumFraction"/>, one holding +100 V and one -100 V.
    /// It solves to the mean of the two, a potential neither conductor holds.
    /// </para>
    /// <para>
    /// An exact test would not see it: the node is an ulp from each surface, not on either.
    /// Setting <see cref="SharedFaceNodes.ToleranceFraction"/> to zero fails this test and the
    /// three that nudge a face by a rounding, and nothing else here - an exact comparison
    /// finds a face lying exactly on a node and misses every face a rounding off one, which
    /// is most of them.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFaceWrittenTwoWaysThatMissesTheNodeLeavesItFreeBetweenBoth()
    {
        var grid = Volume();
        var face = grid.Z(10);

        var geometry = new Geometry3D(
            -Half, -Half, -Half, Half, Half, Half, Cell,
            [
                Box("lower", grid, grid.Z(4), Math.BitDecrement(face), 100.0),
                Box("upper", grid, Math.BitIncrement(face), grid.Z(12), -100.0),
            ],
            Tolerance: 1e-9);

        var channel = Assert.Single(GeometryBuilder3D.SolveChannels(geometry));

        // A channel carries its mask's counts and not the mask, so the mask is built again
        // here - the same declaration on the same grid, so the same mask.
        var mask = GeometryBuilder3D.BuildMask(geometry, GeometryBuilder3D.BuildGrid(geometry));
        var cuts = mask.Cuts!;

        Assert.False(mask.IsFixed(8, 8, 10));
        Assert.Equal(CutLinks3D.MinimumFraction, cuts.Fraction(8, 8, 10, Arm3D.Up, out var up));
        Assert.Equal(CutLinks3D.MinimumFraction, cuts.Fraction(8, 8, 10, Arm3D.Down, out var down));
        Assert.Equal(-100.0, up);
        Assert.Equal(100.0, down);

        var mixed = channel.Potential[8, 8, 10];
        output.WriteLine($"the free node solves to {mixed:G6} V between conductors at +100 V and -100 V");
        Assert.InRange(mixed, -1.0, 1.0);

        Assert.Equal(81, GeometryBuilder3D.NodesOnSharedFaces(geometry, grid)?.Count);
        Assert.Equal(SharedFaceNodes.Code, Assert.Single(channel.Report.Warnings).Code);
    }

    /// <summary>
    /// Two stripes thinner than a cell, stacked along z and meeting on a plane of nodes: the
    /// arrangement of <c>astral-3d</c>'s foil slices, which hold no node at all.
    /// </summary>
    /// <remarks>
    /// Each stripe spans nodes 4 to 12 in x and sits between node rows 10 and 11 in y, three
    /// to six tenths of a cell above row 10 - so the only way the mesh can see the stripes is
    /// through the y arms of rows 10 and 11, and those in the plane of the shared face enter
    /// both stripes at the same point.
    /// </remarks>
    private static Geometry3D Stripes(double face)
    {
        var grid = Volume();
        var y0 = grid.Y(10) + (0.3 * grid.SpacingY);
        var y1 = grid.Y(10) + (0.6 * grid.SpacingY);

        CompiledElectrode3D Stripe(string name, double minZ, double maxZ, double volts) => new()
        {
            Name = name,
            Shape = Electrode3DShape.Box,
            MinX = grid.X(4),
            MaxX = grid.X(12),
            MinY = y0,
            MaxY = y1,
            MinZ = minZ,
            MaxZ = maxZ,
            Potential = volts,
        };

        return new Geometry3D(
            -Half, -Half, -Half, Half, Half, Half, Cell,
            [Stripe("near", grid.Z(4), face, 100.0), Stripe("far", face, grid.Z(12), -100.0)],
            Tolerance: 1e-9);
    }

    /// <summary>
    /// The case a node test cannot see: no node is in either stripe, and the coin is tossed
    /// on the stencil arms lying in the plane of the face they share.
    /// </summary>
    /// <remarks>
    /// Nine nodes of row 10 reach up into the stripes and nine of row 11 reach down, all in
    /// the face's plane - eighteen arms, and not one node. With the face one ulp to either
    /// side, each arm enters one stripe and grazes the other, and the cut it records carries
    /// +100 V or -100 V accordingly: a whole stripe's worth of potential on the last bit.
    /// </remarks>
    [Fact]
    public void StripesThinnerThanACellTossTheCoinOnTheirArms()
    {
        var grid = Volume();
        var face = grid.Z(10);

        double ArmAt(double z)
        {
            var mask = GeometryBuilder3D.BuildMask(Stripes(z), grid);

            Assert.False(mask.IsFixed(8, 10, 10));
            Assert.Equal(0.3, mask.Cuts!.Fraction(8, 10, 10, Arm3D.North, out var potential), 12);
            return potential;
        }

        var below = ArmAt(Math.BitDecrement(face));
        var above = ArmAt(Math.BitIncrement(face));

        output.WriteLine($"the arm from node (8, 10, 10) is cut against {below} V with the face one ulp below it and {above} V one ulp above");

        Assert.Equal(-100.0, below);
        Assert.Equal(100.0, above);

        foreach (var z in new[] { Math.BitDecrement(face), face, Math.BitIncrement(face) })
        {
            var found = GeometryBuilder3D.NodesOnSharedFaces(Stripes(z), grid);

            Assert.NotNull(found);
            Assert.Equal(0, found.Nodes);
            Assert.Equal(18, found.Arms);
            Assert.True(found.ExampleIsArm);
        }

        output.WriteLine(GeometryBuilder3D.NodesOnSharedFaces(Stripes(face), grid)!.ToWarning().Message);

        // A quarter cell off the plane of nodes, no arm meets the face.
        Assert.Null(GeometryBuilder3D.NodesOnSharedFaces(Stripes(face + (0.25 * grid.SpacingZ)), grid));
    }

    [Fact]
    public void AnArmWhoseTieIsBehindANearerConductorDecidesNothing()
    {
        // A grounded plate across row 10's arms, a tenth of a cell up: every arm is cut
        // against it first, so the tie between the stripes behind it is in shadow.
        var grid = Volume();
        var stripes = Stripes(grid.Z(10));

        var shield = new CompiledElectrode3D
        {
            Name = "shield",
            Shape = Electrode3DShape.Box,
            MinX = grid.X(2),
            MaxX = grid.X(14),
            MinY = grid.Y(10) + (0.1 * grid.SpacingY),
            MaxY = grid.Y(10) + (0.2 * grid.SpacingY),
            MinZ = grid.Z(2),
            MaxZ = grid.Z(14),
            Potential = 0.0,
        };

        var found = GeometryBuilder3D.NodesOnSharedFaces(
            stripes with { Electrodes = [.. stripes.Electrodes, shield] }, grid);

        // Row 11's arms reach down into the stripes before the shield; row 10's are shaded.
        Assert.Equal(9, found?.Arms);
    }

    [Fact]
    public void ConductorsThatAgreeMayShareAFaceOnANode()
    {
        // Either answer to the coin toss is the same potential, so there is nothing to report.
        var grid = Volume();

        Assert.Null(GeometryBuilder3D.NodesOnSharedFaces(Stacked(grid.Z(10), 100.0, 100.0), grid));
    }

    [Fact]
    public void ConductorsThatDifferOnlyInTheirDriveStillCount()
    {
        // The overlap check's own reading of "different excitations": the potential and
        // every tap. Same DC, one of them driven, is a disagreement.
        var grid = Volume();
        var geometry = Stacked(grid.Z(10), 0.0, 0.0);

        geometry = geometry with
        {
            Electrodes =
            [
                geometry.Electrodes[0],
                geometry.Electrodes[1] with { Taps = [new CompiledTap(0, 50.0, 0.0)] },
            ],
        };

        Assert.Equal(81, GeometryBuilder3D.NodesOnSharedFaces(geometry, grid)?.Count);
    }

    [Fact]
    public void TheSolveReportCarriesItAsAQualifiedWarning()
    {
        var grid = Volume();

        var (_, flagged) = GeometryBuilder3D.Build(Stacked(grid.Z(10)));
        var (_, clean) = GeometryBuilder3D.Build(Stacked(grid.Z(10) + (0.25 * grid.SpacingZ)));

        var warning = Assert.Single(flagged.Warnings);

        Assert.Equal(SharedFaceNodes.Code, warning.Code);
        Assert.Equal(WarningSeverity.Qualified, warning.Severity);
        Assert.False(warning.IsSuppressible);
        Assert.Contains("81 mesh nodes", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'lower' and 'upper'", warning.Message, StringComparison.Ordinal);
        Assert.Contains("fraction of a cell", warning.Message, StringComparison.Ordinal);

        Assert.Empty(clean.Warnings);

        // And every channel of a solve reports it, since they share the mesh.
        foreach (var channel in GeometryBuilder3D.SolveChannels(Stacked(grid.Z(10))))
        {
            Assert.Equal(SharedFaceNodes.Code, Assert.Single(channel.Report.Warnings).Code);
        }
    }

    /// <summary>The plane grid: 16 intervals an axis over the same box.</summary>
    private static CompiledSolvedField Plane(
        double face, double left = 100.0, double right = -100.0, IReadOnlyList<CompiledStage>? stages = null)
    {
        var grid = PlaneGrid();

        return new CompiledSolvedField
        {
            MinX = -Half, MinY = -Half, MaxX = Half, MaxY = Half,
            CellSize = Cell,
            Tolerance = 1e-10,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "left", Shape = ElectrodeShape.Rectangle,
                    MinX = grid.X(4), MaxX = face, MinY = grid.Y(4), MaxY = grid.Y(12), Potential = left,
                },
                new CompiledElectrode
                {
                    Name = "right", Shape = ElectrodeShape.Rectangle,
                    MinX = face, MaxX = grid.X(12), MinY = grid.Y(4), MaxY = grid.Y(12), Potential = right,
                },
            ],
            Stages = stages ?? [],
        };
    }

    private static Grid2D PlaneGrid() => GeometryBuilder.BuildGrid(new CompiledSolvedField
    {
        MinX = -Half, MinY = -Half, MaxX = Half, MaxY = Half, CellSize = Cell, Electrodes = [],
    });

    [Fact]
    public void TwoRectanglesSharingAFaceOnALineOfNodesAreFlagged()
    {
        var grid = PlaneGrid();

        var found = GeometryBuilder.NodesOnSharedFaces(Plane(grid.X(10)), grid);

        Assert.NotNull(found);
        Assert.Equal(9, found.Nodes);
        Assert.Null(found.Z);

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(Plane(grid.X(10) + (0.25 * grid.SpacingX)), grid));

        var (_, report) = GeometryBuilder.Build(Plane(grid.X(10)));
        Assert.Equal(SharedFaceNodes.Code, Assert.Single(report.Warnings).Code);
    }

    /// <summary>
    /// The plane's coin: a rectangle is rasterized by index rather than by signed distance,
    /// and the node on the face still goes one way or the other with the rounding.
    /// </summary>
    /// <remarks>
    /// Nudged by a trillionth of a cell rather than one ulp, because here the coin is tossed
    /// in a quotient - the face's offset from the origin over the spacing, floored or
    /// ceilinged - and one ulp of the face can vanish in the subtraction before it. A
    /// trillionth of a cell is still a thousandth of the detector's tolerance, so the
    /// geometry is the same one to anything but the rasterizer.
    /// </remarks>
    [Fact]
    public void ThePlaneRasterizerTossesTheSameCoin()
    {
        var grid = PlaneGrid();
        var face = grid.X(10);
        var nudge = 1e-12 * grid.SpacingX;

        double NodeAt(double x) => GeometryBuilder.BuildMask(Plane(x), grid).ValueAt(10, 8);

        var below = NodeAt(face - nudge);
        var above = NodeAt(face + nudge);

        output.WriteLine($"node (10, 8) holds {below} V with the face a trillionth of a cell below it and {above} V as far above");

        foreach (var x in new[] { face - nudge, face, face + nudge })
        {
            Assert.Equal(9, GeometryBuilder.NodesOnSharedFaces(Plane(x), grid)?.Count);
        }

        Assert.Equal(-100.0, below);
        Assert.Equal(100.0, above);
    }

    [Fact]
    public void ADisagreementDuringOneStageCounts()
    {
        // Both earthed as declared, so the base state agrees - and a stage that pushes one of
        // them makes the node on their face a coin toss for exactly the duration of the push.
        var grid = PlaneGrid();
        var held = Plane(grid.X(10), 0.0, 0.0);

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(held, grid));

        var push = new CompiledStage(
            "push",
            1e-6,
            [held.Electrodes[0], held.Electrodes[1] with { Potential = 500.0 }]);

        var pushed = held with { Stages = [new CompiledStage("hold", 1e-6, held.Electrodes), push] };

        Assert.Equal(9, GeometryBuilder.NodesOnSharedFaces(pushed, grid)?.Count);
    }

    [Fact]
    public void ThinPlaneStripesTossTheCoinOnTheirArmsToo()
    {
        // The plane version of the foil: two strips meeting on a column of nodes, between
        // two node rows, so no node is inside either and the column's y arms enter both.
        var grid = PlaneGrid();
        var face = grid.X(10);
        var y0 = grid.Y(10) + (0.3 * grid.SpacingY);
        var y1 = grid.Y(10) + (0.6 * grid.SpacingY);

        CompiledSolvedField Strips(double at) => new()
        {
            MinX = -Half, MinY = -Half, MaxX = Half, MaxY = Half,
            CellSize = Cell,
            Tolerance = 1e-10,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "near", Shape = ElectrodeShape.Rectangle,
                    MinX = grid.X(4), MaxX = at, MinY = y0, MaxY = y1, Potential = 100.0,
                },
                new CompiledElectrode
                {
                    Name = "far", Shape = ElectrodeShape.Rectangle,
                    MinX = at, MaxX = grid.X(12), MinY = y0, MaxY = y1, Potential = -100.0,
                },
            ],
        };

        var found = GeometryBuilder.NodesOnSharedFaces(Strips(face), grid);

        Assert.NotNull(found);
        Assert.Equal(0, found.Nodes);
        Assert.Equal(2, found.Arms);

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(Strips(face + (0.25 * grid.SpacingX)), grid));
    }

    [Fact]
    public void AnEdgeProfileTakesNoPart()
    {
        // A profile lies along a domain edge and has no surface to be near; an interior
        // electrode flush against that edge is decided by declaration order, not rounding.
        var grid = PlaneGrid();

        var solve = new CompiledSolvedField
        {
            MinX = -Half, MinY = -Half, MaxX = Half, MaxY = Half,
            CellSize = Cell,
            Tolerance = 1e-10,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "board", Shape = ElectrodeShape.EdgeProfile, Edge = GridEdge.Bottom,
                    Profile = [(-Half, 0.0), (Half, 200.0)],
                },
                new CompiledElectrode
                {
                    Name = "block", Shape = ElectrodeShape.Rectangle,
                    MinX = grid.X(4), MaxX = grid.X(12), MinY = -Half, MaxY = grid.Y(4), Potential = 100.0,
                },
            ],
        };

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(solve, grid));
    }
}
