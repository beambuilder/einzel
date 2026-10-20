using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// Building a volume mask allocates what the mask keeps and nothing per node besides.
/// </summary>
/// <remarks>
/// <para>
/// <b>The primitives' entry tests are asked once per stencil arm near the metal</b>, so
/// anything they allocate is multiplied by the arms of a mesh. The prism and revolve tests
/// gathered their crossings in a new list, and the revolve test took each profile edge's roots
/// from an iterator and its cap angles from a new array: building a mask allocated 5.5 kB a
/// node on the C-trap's revolved rods and 260 bytes on the linear ion trap's prisms, against
/// the 105 the mask keeps. Transient garbage, so it never moved the peak - but it was 690 gen0
/// collections and about 40 percent of the time of a C-trap mask. The prisms' smaller share
/// cost no time anyone could measure, and is held to zero anyway, because the next outline to
/// allocate per call is the one that will.
/// </para>
/// <para>
/// <b>Exactly zero, measured as the cheapest of five.</b> The runtime charges a one-off cost
/// - a type initializer, the shared pool's first array for a thread - to whichever window it
/// fires in, and what is being held is a floor, so the minimum is the statistic. Allocation is
/// counted on this thread alone, so tests running beside this one cannot add to it.
/// </para>
/// </remarks>
public sealed class MaskAllocationTests(ITestOutputHelper output)
{
    private const double Mm = 1e-3;

    /// <summary>Every shape's signed distance, containment and entry test allocates nothing.</summary>
    /// <remarks>
    /// Over the arms of a lattice through every electrode, so each entry test runs its whole
    /// body - past the axial slab for a prism, through every profile edge and both caps for a
    /// revolve - and is asserted to have been entered, since a test that only ever missed would
    /// allocate nothing in the old code too. Two outlines are long enough to borrow their
    /// crossings from the shared pool rather than the stack, so both paths are held.
    /// </remarks>
    [Fact]
    public void EveryEntryAndDistanceTestAllocatesNothing()
    {
        CompiledElectrode3D[] electrodes =
        [
            .. Curved(),
            new()
            {
                Name = "tilted", Shape = Electrode3DShape.Box, Potential = 5.0,
                MinX = -7.0 * Mm, MaxX = -5.0 * Mm, MinY = -7.0 * Mm, MaxY = -2.0 * Mm, MinZ = 3.0 * Mm, MaxZ = 6.0 * Mm,
                TiltAxis = CylinderAxis.Y, TiltHalfTurns = 0.03,
            },
            new()
            {
                Name = "sphere", Shape = Electrode3DShape.Sphere, Potential = 6.0,
                CentreX = 6.0 * Mm, CentreY = 6.0 * Mm, CentreZ = -6.0 * Mm, Radius = 1.5 * Mm,
            },
            new()
            {
                Name = "cylinder", Shape = Electrode3DShape.Cylinder, Potential = 7.0, Axis = CylinderAxis.X,
                CentreY = -6.0 * Mm, CentreZ = 0.0, Radius = 1.0 * Mm, Lower = -2.0 * Mm, Upper = 2.0 * Mm,
            },
        ];

        var entered = new int[electrodes.Length];
        var inside = new int[electrodes.Length];

        long Sweep()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            Array.Clear(entered);
            Array.Clear(inside);

            for (var k = 0; k <= 16; k++)
            {
                for (var j = 0; j <= 16; j++)
                {
                    for (var i = 0; i <= 16; i++)
                    {
                        // A millimetre lattice, offset so that no node or arm lies on a face.
                        var x = (i - 8.0 + 0.3) * Mm;
                        var y = (j - 8.0 + 0.2) * Mm;
                        var z = (k - 8.0 + 0.1) * Mm;

                        for (var e = 0; e < electrodes.Length; e++)
                        {
                            var electrode = electrodes[e];

                            if (electrode.Contains(x, y, z) && electrode.SignedDistance(x, y, z) <= 0.0)
                            {
                                inside[e]++;
                            }

                            entered[e] += Entered(electrode.FirstEntry(x, y, z, x + Mm, y, z))
                                + Entered(electrode.FirstEntry(x, y, z, x, y + Mm, z))
                                + Entered(electrode.FirstEntry(x, y, z, x, y, z + Mm))
                                + Entered(electrode.FirstEntry(x + Mm, y + Mm, z + Mm, x, y, z));
                        }
                    }
                }
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        var cheapest = Enumerable.Range(0, 5).Select(_ => Sweep()).Min();

        for (var e = 0; e < electrodes.Length; e++)
        {
            output.WriteLine($"{electrodes[e].Name}: {inside[e]} lattice nodes inside, {entered[e]} arms entering");

            Assert.True(entered[e] > 0, $"no arm entered {electrodes[e].Name}, so its entry test was never run");
            Assert.True(inside[e] > 0, $"no lattice node is inside {electrodes[e].Name}");
        }

        output.WriteLine($"cheapest of five sweeps allocated {cheapest} bytes");
        Assert.Equal(0L, cheapest);
    }

    /// <summary>
    /// A mask over revolved and extruded electrodes allocates what it keeps, and nothing per
    /// node besides.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the mask keeps is read off the mask: nine bytes a node of its own and what its cut
    /// links hold, which the statement the memory estimate bills against must cover. The
    /// allowance on top is per-mask bookkeeping - the bounding-box ranges and the cull's
    /// candidate lists - spread over the nodes, and is 0.37 of a byte a node here. The old
    /// entry tests allocated 12,588 bytes a node on this geometry.
    /// </para>
    /// <para>
    /// <b>And the cut links' growth, which is per cut arm rather than per node.</b> They are
    /// packed as they are found and grow by doubling, so the arrays they outgrow and the copy a
    /// trim makes come to less than four times what they finally hold: under twice the last
    /// capacity, which is under twice the count, plus the count again. On this geometry that is
    /// 3.1 times. When the cut links held every arm of every node there was nothing to grow and
    /// the bound was what the statement billed.
    /// </para>
    /// </remarks>
    [Fact]
    public void AVolumeMaskAllocatesOnlyWhatItKeeps()
    {
        var geometry = new Geometry3D(-8.0 * Mm, -8.0 * Mm, -8.0 * Mm, 8.0 * Mm, 8.0 * Mm, 8.0 * Mm, 0.5 * Mm, Curved());
        var grid = GeometryBuilder3D.BuildGrid(geometry);

        var kept = 0.0;
        var packed = 0.0;

        double PerNode()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var built = GeometryBuilder3D.BuildMask(geometry, grid);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.True(built.Cuts?.CutCount > 0, "no arm was cut, so no entry test was run");
            Assert.True(built.InteriorFixedCount > 0, "no node is inside metal");

            kept = SolveMemoryModel.VolumeMask + (built.Cuts.Bytes / (double)grid.NodeCount);
            packed = built.Cuts.CutCount * SolveMemoryModel.VolumeCutArm / grid.NodeCount;

            return allocated / (double)grid.NodeCount;
        }

        var cheapest = Enumerable.Range(0, 5).Select(_ => PerNode()).Min();

        output.WriteLine(
            $"{grid.CountX}x{grid.CountY}x{grid.CountZ} nodes: {cheapest:F2} bytes a node allocated, "
            + $"against {kept:F2} the mask keeps, {packed:F2} of it packed cut arms; "
            + $"{SolveMemoryModel.VolumeMask + SolveMemoryModel.VolumeCutLinks} billed");

        Assert.InRange(kept, 0.0, SolveMemoryModel.VolumeMask + SolveMemoryModel.VolumeCutLinks);
        Assert.InRange(cheapest, kept, kept + (4.0 * packed) + 1.0);
    }

    /// <summary>Four curved electrodes, disjoint, two of them long enough to borrow from the pool.</summary>
    private static CompiledElectrode3D[] Curved() =>
    [
        // A quarter-turn sector about z, so both caps are crossed, with a slanted profile edge
        // (a cone), a flat one (an annular disc) and a vertical one (a cylinder).
        new()
        {
            Name = "sector", Shape = Electrode3DShape.Revolve, Potential = 1.0, Axis = CylinderAxis.Z,
            Vertices = [(2.0 * Mm, -2.0 * Mm), (4.0 * Mm, -2.0 * Mm), (5.0 * Mm, 2.0 * Mm), (2.0 * Mm, 2.0 * Mm)],
            FromHalfTurns = 0.0, ToHalfTurns = 0.5,
        },

        // A whole torus about x with a 96-vertex profile: more crossings than the stack holds.
        new()
        {
            Name = "torus", Shape = Electrode3DShape.Revolve, Potential = 2.0, Axis = CylinderAxis.X,
            Vertices = Circle(96, 5.5 * Mm, -5.0 * Mm, 1.5 * Mm),
            FromHalfTurns = 0.0, ToHalfTurns = 2.0,
        },

        // A concave outline along y, so an arm can leave its cross-section and come back.
        new()
        {
            Name = "channel", Shape = Electrode3DShape.Prism, Potential = 3.0, Axis = CylinderAxis.Y,
            Lower = -6.0 * Mm, Upper = 6.0 * Mm,
            Vertices =
            [
                (-1.0 * Mm, 4.0 * Mm), (3.0 * Mm, 4.0 * Mm), (3.0 * Mm, 7.0 * Mm), (2.0 * Mm, 7.0 * Mm),
                (2.0 * Mm, 5.0 * Mm), (0.0, 5.0 * Mm), (0.0, 7.0 * Mm), (-1.0 * Mm, 7.0 * Mm),
            ],
        },

        // A rod along z whose 160-vertex outline is more crossings than the stack holds.
        new()
        {
            Name = "rod", Shape = Electrode3DShape.Prism, Potential = 4.0, Axis = CylinderAxis.Z,
            Lower = -7.0 * Mm, Upper = -3.0 * Mm,
            Vertices = Circle(160, 4.0 * Mm, -4.0 * Mm, 1.5 * Mm),
        },
    ];

    /// <summary>A closed outline of a circle, as a run of vertices.</summary>
    private static (double X, double Y)[] Circle(int vertices, double centerA, double centerB, double radius) =>
    [
        .. Enumerable.Range(0, vertices).Select(n => (
            centerA + (radius * double.CosPi(2.0 * n / vertices)),
            centerB + (radius * double.SinPi(2.0 * n / vertices)))),
    ];

    private static int Entered(double? entry) => entry.HasValue ? 1 : 0;
}
