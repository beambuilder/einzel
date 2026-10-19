using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields.Solved;
using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// A grounded face of the domain is a third conductor at zero volts, so a node on it that lies
/// on the surface of a conductor holding something else is the same coin toss as a node on a
/// face two conductors share - and the geometry builders say so before solving.
/// </summary>
/// <remarks>
/// <para>
/// A plate at 100 V flush with a grounded edge is the smallest case. The claim has two halves
/// and both are tested: that the edge node really is decided by rounding (a trillionth of a
/// cell moves it between the plate's potential and zero, and the interpolated field beside the
/// contact moves by a fifth of the applied potential with it), and that the detector finds it,
/// and finds nothing once the plate is carried past the edge or stopped short of it.
/// </para>
/// <para>
/// The plate's other faces sit a quarter of a cell off the node lines, so the only nodes on its
/// surface that the edge can claim are those on the flush face - a face crossing the edge on a
/// line of nodes is a coin toss too, and gets its own test.
/// </para>
/// </remarks>
public sealed class GroundedFaceNodeTests(ITestOutputHelper output)
{
    private const double Half = 0.008;

    private const double Cell = 0.001;

    private const double Quarter = 0.25 * Cell;

    private static Grid2D PlaneGrid() => GeometryBuilder.BuildGrid(Plane([]));

    private static CompiledSolvedField Plane(
        IReadOnlyList<CompiledElectrode> electrodes, BoundaryKind right = BoundaryKind.Dirichlet) => new()
    {
        MinX = -Half, MinY = -Half, MaxX = Half, MaxY = Half,
        CellSize = Cell,
        Tolerance = 1e-12,
        RightEdge = right,
        Electrodes = electrodes,
    };

    private static CompiledElectrode Plate(double minX, double maxX, double volts = 100.0)
    {
        var grid = PlaneGrid();

        return new CompiledElectrode
        {
            Name = "plate", Shape = ElectrodeShape.Rectangle,
            MinX = minX, MaxX = maxX,
            MinY = grid.Y(4) + Quarter, MaxY = grid.Y(8) + Quarter,
            Potential = volts,
        };
    }

    /// <summary>A plate from node column 12 to the right edge, its face flush with the edge.</summary>
    private static CompiledSolvedField Flush(double face, double volts = 100.0) =>
        Plane([Plate(PlaneGrid().X(12), face, volts)]);

    [Fact]
    public void APlateFlushWithAGroundedEdgeIsFlagged()
    {
        var grid = PlaneGrid();
        var found = GeometryBuilder.NodesOnSharedFaces(Flush(Half), grid);

        Assert.NotNull(found);

        // Rows 5 to 8 of the right edge are on the flush face; rows 4 and 9 are a quarter of a
        // cell outside the plate and are the edge's by every arithmetic.
        Assert.Equal(4, found.BoundaryNodes);
        Assert.Equal(0, found.Nodes);
        Assert.Equal(0, found.Arms);
        Assert.Equal(4, found.Count);
        Assert.True(found.ExampleIsBoundary);
        Assert.Equal("plate", found.First);
        Assert.Equal("right edge", found.Second);
        Assert.Equal(grid.X(16), found.X);

        output.WriteLine(found.ToWarning().Message);
    }

    /// <summary>
    /// The claim the warning makes, checked directly: a trillionth of a cell decides whether
    /// the edge node holds the plate or the ground - and the field beside the contact moves
    /// with it by a fifth of the applied potential, while no free node's solution moves at all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nudged by a trillionth of a cell rather than one ulp, because the plane rasterizes a
    /// rectangle by a floored quotient and one ulp can vanish in the subtraction before it; a
    /// trillionth is still a thousandth of the detector's tolerance.
    /// </para>
    /// <para>
    /// Why the solve does not see it: every neighbour of a flipped node is another edge node or
    /// inside the plate, so no free node reaches one through an uncut arm. The interpolant does:
    /// a bicubic stencil beside the contact reads the edge node, and half a cell off the corner
    /// the potential moves 21.9 V, over a fifth of what the plate holds. So the coin is confined
    /// to about a cell of where the plate
    /// meets the ground - which is also why it is qualified rather than a violation.
    /// </para>
    /// </remarks>
    [Fact]
    public void ATrillionthOfACellDecidesWhetherTheEdgeNodeHoldsThePlate()
    {
        var grid = PlaneGrid();
        var nudge = 1e-12 * grid.SpacingX;

        var inside = Flush(Half + nudge);
        var outside = Flush(Half - nudge);

        var maskIn = GeometryBuilder.BuildMask(inside, grid);
        var maskOut = GeometryBuilder.BuildMask(outside, grid);

        Assert.True(maskIn.IsFixed(16, 6));
        Assert.True(maskOut.IsFixed(16, 6));
        Assert.Equal(100.0, maskIn.ValueAt(16, 6));
        Assert.Equal(0.0, maskOut.ValueAt(16, 6));

        var (potentialIn, _) = PoissonSolver2D.Solve(
            maskIn, 1e-12, maximumCycles: 400, coarsen: coarse => GeometryBuilder.BuildMask(inside, coarse));
        var (potentialOut, _) = PoissonSolver2D.Solve(
            maskOut, 1e-12, maximumCycles: 400, coarsen: coarse => GeometryBuilder.BuildMask(outside, coarse));

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                if (!maskIn.IsFixed(i, j))
                {
                    Assert.Equal(potentialIn[i, j], potentialOut[i, j]);
                }
            }
        }

        var (fieldIn, _) = GeometryBuilder.Build(inside);
        var (fieldOut, _) = GeometryBuilder.Build(outside);

        var h = grid.SpacingX;
        var corner = new Vec3(grid.X(15) + (0.5 * h), grid.Y(8) + (0.5 * h), 0.0);
        var away = new Vec3(grid.X(12) + (0.5 * h), grid.Y(11) + (0.5 * h), 0.0);

        var moved = fieldIn.PotentialAt(corner) - fieldOut.PotentialAt(corner);

        output.WriteLine(
            $"edge node (16, 6) holds {maskOut.ValueAt(16, 6)} V with the face a trillionth of a cell short "
            + $"and {maskIn.ValueAt(16, 6)} V a trillionth past; half a cell off the corner the potential "
            + $"moves {moved:F3} V, and three cells away {fieldIn.PotentialAt(away) - fieldOut.PotentialAt(away):G3} V");

        Assert.InRange(Math.Abs(moved), 5.0, 100.0);
        Assert.Equal(fieldIn.PotentialAt(away), fieldOut.PotentialAt(away));

        foreach (var face in new[] { Half - nudge, Half, Half + nudge })
        {
            Assert.Equal(4, GeometryBuilder.NodesOnSharedFaces(Flush(face), grid)?.BoundaryNodes);
        }
    }

    [Fact]
    public void APlateCarriedPastTheEdgeOrStoppedShortIsNot()
    {
        var grid = PlaneGrid();

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(Flush(Half + Quarter), grid));
        Assert.Null(GeometryBuilder.NodesOnSharedFaces(Flush(Half - Quarter), grid));
    }

    /// <summary>
    /// A conductor lying outside the domain against its edge is in the solve through the edge
    /// nodes alone, so there the coin decides whether it is in the solve at all.
    /// </summary>
    /// <remarks>
    /// The arm from the free column beside the edge reaches the conductor at exactly its far
    /// end, which the cut links do not record, so the stencil reads the edge node's value: the
    /// plate's potential when the edge node is on it, zero when it is a trillionth of a cell
    /// clear. Every free node in the domain moves.
    /// </remarks>
    [Fact]
    public void AConductorOutsideTheDomainAgainstTheEdgeIsInTheSolveOnlyByTheCoin()
    {
        var grid = PlaneGrid();
        var nudge = 1e-12 * grid.SpacingX;

        CompiledSolvedField Outside(double face) => Plane([Plate(face, face + 0.005)]);

        var (touching, _) = GeometryBuilder.Build(Outside(Half - nudge));
        var (clear, _) = GeometryBuilder.Build(Outside(Half + nudge));

        var center = new Vec3(0.0, grid.Y(6), 0.0);

        output.WriteLine(
            $"at the center of the domain: {touching.PotentialAt(center):F4} V with the plate touching the edge, "
            + $"{clear.PotentialAt(center):F4} V with it a trillionth of a cell clear");

        Assert.InRange(touching.PotentialAt(center), 1.0, 100.0);
        Assert.Equal(0.0, clear.PotentialAt(center));

        foreach (var face in new[] { Half - nudge, Half, Half + nudge })
        {
            Assert.Equal(4, GeometryBuilder.NodesOnSharedFaces(Outside(face), grid)?.BoundaryNodes);
        }
    }

    [Fact]
    public void AFaceCrossingTheEdgeOnALineOfNodesIsACoinToo()
    {
        // Carried past the edge, so its end is not flush - but its lower face lies on row 4,
        // and the edge node there is on the plate's surface and on the ground at once.
        var grid = PlaneGrid();

        var crossing = Plane(
        [
            Plate(grid.X(12), Half + Quarter) with { MinY = grid.Y(4) },
        ]);

        var found = GeometryBuilder.NodesOnSharedFaces(crossing, grid);

        Assert.Equal(1, found?.BoundaryNodes);
        Assert.Equal(grid.Y(4), found?.Y);
    }

    [Fact]
    public void AGroundedConductorMayMeetAGroundedEdge()
    {
        // Either answer to the coin toss is zero volts.
        Assert.Null(GeometryBuilder.NodesOnSharedFaces(Flush(Half, 0.0), PlaneGrid()));
    }

    [Fact]
    public void AConductorAtZeroDcWithADriveStillCounts()
    {
        // The overlap check's reading of "holds something": the potential and every tap.
        var solve = Flush(Half, 0.0);
        solve = solve with { Electrodes = [solve.Electrodes[0] with { Taps = [new CompiledTap(0, 50.0, 0.0)] }] };

        Assert.Equal(4, GeometryBuilder.NodesOnSharedFaces(solve, PlaneGrid())?.BoundaryNodes);
    }

    [Fact]
    public void AConductorEnergisedDuringOneStageCounts()
    {
        var grid = PlaneGrid();
        var held = Flush(Half, 0.0);

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(held, grid));

        var pushed = held with
        {
            Stages =
            [
                new CompiledStage("hold", 1e-6, held.Electrodes),
                new CompiledStage("push", 1e-6, [held.Electrodes[0] with { Potential = 500.0 }]),
            ],
        };

        Assert.Equal(4, GeometryBuilder.NodesOnSharedFaces(pushed, grid)?.BoundaryNodes);
    }

    /// <summary>
    /// A Neumann edge is a mirror: the node flips between fixed at the plate's potential and
    /// free beside it, and the free node solves to nearly the plate's potential through a cut of
    /// vanishing length - an ordinary cut cell, not a choice between two values.
    /// </summary>
    [Fact]
    public void ANeumannEdgeTakesNoPart()
    {
        var grid = PlaneGrid();
        var nudge = 1e-12 * grid.SpacingX;

        CompiledSolvedField Mirrored(double face) =>
            Plane([Plate(grid.X(12), face)], right: BoundaryKind.Neumann);

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(Mirrored(Half), grid));

        // Nor does a face crossing the mirror on a line of nodes: the node where the plate's lower
        // face meets it flips between fixed and free, which is a cut cell, not a coin.
        var crossing = Plane(
            [Plate(grid.X(12), Half) with { MinY = grid.Y(4) }], right: BoundaryKind.Neumann);

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(crossing, grid));

        var nudged = Mirrored(Half - nudge);
        var free = GeometryBuilder.SolveChannels(nudged).Single();

        // A channel carries its mask's counts and not the mask, so the mask is built again
        // here - the same declaration on the same grid, so the same mask.
        Assert.False(GeometryBuilder.BuildMask(nudged, GeometryBuilder.BuildGrid(nudged)).IsFixed(16, 6));
        Assert.InRange(free.Potential[16, 6], 99.0, 100.0);

        output.WriteLine(
            $"on a mirror edge the node left free beside the plate solves to {free.Potential[16, 6]:F4} V "
            + "against the plate's 100 V");
    }

    [Fact]
    public void TheAxisOfAnAxisymmetricSolveTakesNoPart()
    {
        // A wire along the axis, declared on a Dirichlet bottom edge. The axis is a mirror
        // whatever the document says, so the wire's nodes on it are not a coin toss; its ends are
        // a quarter of a cell clear of the left and right edges.
        var grid = PlaneGrid();

        var wire = new CompiledSolvedField
        {
            MinX = -Half, MinY = 0.0, MaxX = Half, MaxY = 2.0 * Half,
            CellSize = Cell,
            Tolerance = 1e-10,
            Symmetry = SolveSymmetry.Cylindrical,
            BottomEdge = BoundaryKind.Dirichlet,
            Electrodes =
            [
                new CompiledElectrode
                {
                    Name = "wire", Shape = ElectrodeShape.Rectangle,
                    MinX = grid.X(4) + Quarter, MaxX = grid.X(12) + Quarter, MinY = 0.0, MaxY = 0.3 * Cell,
                    Potential = -100.0,
                },
            ],
        };

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(wire, GeometryBuilder.BuildGrid(wire)));

        // The same wire run out to the grounded left edge does count: its end there is flush,
        // and the corner node on the axis holds -100 V or zero on the last bit.
        var reaching = wire with { Electrodes = [wire.Electrodes[0] with { MinX = -Half }] };

        Assert.Equal(1, GeometryBuilder.NodesOnSharedFaces(reaching, GeometryBuilder.BuildGrid(reaching))?.BoundaryNodes);

        // Carried past the edge it is clean, as the warning says it will be: the corner node is
        // on the wire's face along the axis, which is not a surface, since in space the axis is
        // inside the wire.
        var past = wire with { Electrodes = [wire.Electrodes[0] with { MinX = -Half - Quarter }] };

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(past, GeometryBuilder.BuildGrid(past)));
    }

    [Fact]
    public void AFaceLyingInAMirrorEdgeIsNotASurface()
    {
        // A plate running out to the mirror on the right and carried past the grounded top edge:
        // every top-edge node across it is inside it by any arithmetic, and the corner node lies
        // on its right face - which is in the mirror, and by symmetry is not a face at all.
        var grid = PlaneGrid();

        var plate = new CompiledElectrode
        {
            Name = "plate", Shape = ElectrodeShape.Rectangle,
            MinX = grid.X(12) + Quarter, MaxX = Half, MinY = grid.Y(12) + Quarter, MaxY = Half + Quarter,
            Potential = 100.0,
        };

        var solve = Plane([plate], right: BoundaryKind.Neumann);

        Assert.Null(GeometryBuilder.NodesOnSharedFaces(solve, grid));

        // With the top face flush instead, the top-edge nodes across it are the coin, the corner
        // among them.
        var flush = Plane([plate with { MaxY = Half }], right: BoundaryKind.Neumann);

        Assert.Equal(4, GeometryBuilder.NodesOnSharedFaces(flush, grid)?.BoundaryNodes);
    }

    [Fact]
    public void TheWarningNamesTheFaceAndTheFix()
    {
        var (_, report) = GeometryBuilder.Build(Flush(Half));
        var warning = Assert.Single(report.Warnings);

        Assert.Equal(SharedFaceNodes.Code, warning.Code);
        Assert.Equal(WarningSeverity.Qualified, warning.Severity);
        Assert.Contains("4 mesh nodes on a grounded face of the solve domain", warning.Message, StringComparison.Ordinal);
        Assert.Contains("'plate' meets the grounded right edge", warning.Message, StringComparison.Ordinal);
        Assert.Contains("a third conductor, at zero volts", warning.Message, StringComparison.Ordinal);
        Assert.Contains("Carry the conductor past the grounded face", warning.Message, StringComparison.Ordinal);
        Assert.Contains("is an edge profile", warning.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("two arms of vanishing length", warning.Message, StringComparison.Ordinal);

        Assert.Empty(GeometryBuilder.Build(Flush(Half - Quarter)).Report.Warnings);
    }

    [Fact]
    public void BothKindsInOneGeometryAreReportedTogether()
    {
        // Two plates sharing a face on node column 14, the right one flush with the edge.
        var grid = PlaneGrid();

        var solve = Plane(
        [
            Plate(grid.X(10), grid.X(14)) with { Name = "left" },
            Plate(grid.X(14), Half, -100.0) with { Name = "right" },
        ]);

        var found = GeometryBuilder.NodesOnSharedFaces(solve, grid);

        // The shared face spans rows 5 to 8, and the arms from rows 4 and 9 run in its plane
        // into the corners where it meets the plates' long faces - one above, one below.
        Assert.NotNull(found);
        Assert.Equal(4, found.Nodes);
        Assert.Equal(2, found.Arms);
        Assert.Equal(4, found.BoundaryNodes);
        Assert.Equal(10, found.Count);
        Assert.False(found.ExampleIsBoundary);

        var message = found.ToWarning().Message;
        output.WriteLine(message);

        Assert.Contains("4 mesh nodes lie on, and 2 stencil arms first meet metal on, a face shared", message, StringComparison.Ordinal);
        Assert.Contains("4 mesh nodes on a grounded face", message, StringComparison.Ordinal);
        Assert.Contains("leave a gap between the conductors. At the grounded face, carry the conductor past it", message, StringComparison.Ordinal);
    }

    private static Grid3D Volume() => Grid3D.OverBox(-Half, -Half, -Half, Half, Half, Half, Cell);

    private static Geometry3D Box(double minZ, double maxZ, IReadOnlyList<EdgeCondition>? faces = null)
    {
        var grid = Volume();

        return new Geometry3D(
            -Half, -Half, -Half, Half, Half, Half, Cell,
            [
                new CompiledElectrode3D
                {
                    Name = "block", Shape = Electrode3DShape.Box,
                    MinX = grid.X(4) + Quarter, MaxX = grid.X(12) + Quarter,
                    MinY = grid.Y(4) + Quarter, MaxY = grid.Y(12) + Quarter,
                    MinZ = minZ, MaxZ = maxZ,
                    Potential = 100.0,
                },
            ],
            Tolerance: 1e-10)
        {
            Faces = faces ?? [],
        };
    }

    [Fact]
    public void ABoxFlushWithAGroundedFaceIsFlaggedAndTheCoinIsReal()
    {
        var grid = Volume();
        var nudge = 1e-12 * grid.SpacingZ;

        var found = GeometryBuilder3D.NodesOnSharedFaces(Box(grid.Z(12), Half), grid);

        // Nodes 5 to 12 across in x and in y lie on the flush face.
        Assert.NotNull(found);
        Assert.Equal(64, found.BoundaryNodes);
        Assert.Equal("upper z face", found.Second);
        Assert.Equal(grid.Z(16), found.Z);

        var inside = GeometryBuilder3D.BuildMask(Box(grid.Z(12), Half + nudge), grid);
        var outside = GeometryBuilder3D.BuildMask(Box(grid.Z(12), Half - nudge), grid);

        Assert.Equal(100.0, inside.Value(8, 8, 16));
        Assert.Equal(0.0, outside.Value(8, 8, 16));

        Assert.Null(GeometryBuilder3D.NodesOnSharedFaces(Box(grid.Z(12), Half + Quarter), grid));
        Assert.Null(GeometryBuilder3D.NodesOnSharedFaces(Box(grid.Z(12), Half - Quarter), grid));

        var (_, report) = GeometryBuilder3D.Build(Box(grid.Z(12), Half));
        Assert.Equal(SharedFaceNodes.Code, Assert.Single(report.Warnings).Code);
    }

    [Fact]
    public void ANeumannFaceTakesNoPartInAVolumeEither()
    {
        var grid = Volume();

        EdgeCondition[] faces =
        [
            EdgeCondition.Dirichlet, EdgeCondition.Dirichlet,
            EdgeCondition.Dirichlet, EdgeCondition.Dirichlet,
            EdgeCondition.Dirichlet, EdgeCondition.Neumann,
        ];

        Assert.Null(GeometryBuilder3D.NodesOnSharedFaces(Box(grid.Z(12), Half, faces), grid));

        // Nor does a face crossing the mirror on a plane of nodes.
        var flush = Box(grid.Z(12), Half, faces);
        var crossing = flush with { Electrodes = [flush.Electrodes[0] with { MinX = grid.X(4) }] };

        Assert.Null(GeometryBuilder3D.NodesOnSharedFaces(crossing, grid));

        // And a face lying in that mirror is not a surface where it meets a grounded face: the
        // block carried past the upper x face, running up to the mirror in z, is clean along the
        // line where the two faces meet. Its lower z face is a quarter cell off the nodes, so it
        // does not cross the grounded face on a line of them.
        var geometry = Box(grid.Z(12) + Quarter, Half, faces);
        var reaching = geometry with
        {
            Electrodes = [geometry.Electrodes[0] with { MaxX = Half + Quarter }],
        };

        Assert.Null(GeometryBuilder3D.NodesOnSharedFaces(reaching, grid));
    }

    /// <summary>
    /// The face node is where the mesh puts it, origin plus index times spacing, and that need
    /// not be the declared bound - which is how the compact analyzer's upper face landed
    /// 1.4e-14 mm outside its own domain. A box flush with the declared bound is still found.
    /// </summary>
    [Fact]
    public void TheFaceIsWhereTheMeshPutsItNotWhereTheDocumentDoes()
    {
        // Two ordinary decimals: sixteen intervals over them land the last node an ulp outside.
        const double minZ = 0.002;
        const double maxZ = 0.018;

        var grid = Grid3D.OverBox(-Half, -Half, minZ, Half, Half, maxZ, Cell);

        Assert.NotEqual(maxZ, grid.Z(grid.CountZ - 1));

        output.WriteLine($"last node plane {grid.Z(grid.CountZ - 1):R} m against a declared bound of {maxZ:R} m");

        var box = new CompiledElectrode3D
        {
            Name = "block", Shape = Electrode3DShape.Box,
            MinX = grid.X(4) + Quarter, MaxX = grid.X(12) + Quarter,
            MinY = grid.Y(4) + Quarter, MaxY = grid.Y(12) + Quarter,
            MinZ = grid.Z(12), MaxZ = maxZ,
            Potential = 100.0,
        };

        var geometry = new Geometry3D(-Half, -Half, minZ, Half, Half, maxZ, Cell, [box]);

        Assert.Equal(64, GeometryBuilder3D.NodesOnSharedFaces(geometry, grid)?.BoundaryNodes);
    }
}
