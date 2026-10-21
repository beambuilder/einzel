using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Project;

namespace Einzel.Cli.Tests;

/// <summary>
/// A small plane and a small volume solve, pinned beside the solver-behaviour version: when the
/// numbers move, the version has to move with them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> The field cache keys a solve on what it was handed and on
/// <see cref="EngineBuild.SolverBehaviourVersion"/>, which stands for how it was answered.
/// Before the cache, a numerics change merged without a bump only mislabelled manifests; with
/// it, every project keeps loading fields the new solver would not produce, and nothing says
/// so. This test turns that silence into a failure.
/// </para>
/// <para>
/// <b>If it fails after a change to a solver, a mask, a cut cell or an interpolant</b>: bump
/// <see cref="EngineBuild.SolverBehaviourVersion"/>, re-pin the values below from the new
/// build, and say so in the change. If it fails on a machine and not on another, the tolerance
/// is too tight for the platforms' rounding and should be widened, not the version bumped.
/// </para>
/// <para>
/// To a tolerance, not to the bit: one solve is reproducible on one machine, not across
/// machines, and this runs on every runner. The tolerance is far below what a change to the
/// smoother or the cycle makes in these solves, which converge to 1e-10 and 1e-9.
/// </para>
/// </remarks>
public sealed class SolverBehaviourTripwireTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const double Relative = 1e-12;

    private static CompiledSolvedField Plane() => new()
    {
        MinX = -0.01,
        MinY = -0.01,
        MaxX = 0.01,
        MaxY = 0.01,
        CellSize = 0.0005,
        Tolerance = 1e-10,
        Electrodes =
        [
            new CompiledElectrode
            {
                Name = "plate", Shape = ElectrodeShape.Rectangle,
                MinX = -0.004, MinY = 0.002, MaxX = 0.004, MaxY = 0.003, Potential = 100.0,
            },
            new CompiledElectrode
            {
                Name = "rod", Shape = ElectrodeShape.Disc,
                CentreX = 0.0, CentreY = -0.004, Radius = 0.001, Potential = -50.0,
            },
        ],
    };

    private static Geometry3D Volume() => new(
        -0.004, -0.004, -0.004, 0.004, 0.004, 0.004, 0.0005,
        [
            new CompiledElectrode3D
            {
                Name = "slab", Shape = Electrode3DShape.Box,
                MinX = -0.002, MinY = -0.002, MinZ = 0.001, MaxX = 0.002, MaxY = 0.002, MaxZ = 0.0015,
                Potential = 100.0,
            },
            new CompiledElectrode3D
            {
                Name = "tube", Shape = Electrode3DShape.Cylinder,
                Radius = 0.001, Axis = CylinderAxis.Z, Lower = -0.003, Upper = -0.002, Potential = -20.0,
            },
        ],
        1e-9);

    private static readonly Vec3[] Probes =
    [
        new(0.0, 0.0, 0.0),
        new(0.0011, -0.0007, 0.0023),
        new(-0.0021, 0.0015, -0.0012),
    ];

    /// <summary>
    /// A ring and a disc on the axis of a cylindrical half-plane whose left edge is a mirror:
    /// the radial operator and a Neumann edge, which the plane above has neither of.
    /// </summary>
    private static CompiledSolvedField Cylinder() => new()
    {
        MinX = -0.01,
        MinY = 0.0,
        MaxX = 0.01,
        MaxY = 0.01,
        CellSize = 0.0005,
        Tolerance = 1e-10,
        Symmetry = SolveSymmetry.Cylindrical,
        LeftEdge = BoundaryKind.Neumann,
        Electrodes =
        [
            new CompiledElectrode
            {
                Name = "ring", Shape = ElectrodeShape.Rectangle,
                MinX = -0.001, MinY = 0.004, MaxX = 0.001, MaxY = 0.006, Potential = 100.0,
            },
            new CompiledElectrode
            {
                Name = "cap", Shape = ElectrodeShape.Rectangle,
                MinX = 0.006, MinY = 0.0, MaxX = 0.007, MaxY = 0.002, Potential = -30.0,
            },
        ],
    };

    /// <summary>
    /// A 33-node cube with a plate thinner than a coarse cell: the rediscretised hierarchy
    /// cannot coarsen past the fine grid, so the solve takes the Galerkin one.
    /// </summary>
    private static Geometry3D Galerkin() => new(
        -0.008, -0.008, -0.008, 0.008, 0.008, 0.008, 0.0005,
        [
            new CompiledElectrode3D
            {
                Name = "plate", Shape = Electrode3DShape.Box,
                MinX = -0.004, MinY = -0.004, MinZ = 0.001, MaxX = 0.004, MaxY = 0.004, MaxZ = 0.0015,
                Potential = 100.0,
            },
        ],
        1e-9);

    private static readonly double[] PinnedPlane = [49.41476471518219, 32.433784892900114, 86.98101324221474];

    private static readonly double[] PinnedVolume = [57.83113206445305, 60.73448426923679, 14.826002727063704];

    private static readonly double[] PinnedCylinder = [72.95777680214243, 74.98105818818686, 69.61304849035929];

    private static readonly double[] PinnedGalerkin = [80.90352180590457, 82.9398473537217, 55.484319599763566];

    [Fact]
    public void TheSolverBehaviourVersionMovesWithTheNumbers()
    {
        Assert.Equal(2, EngineBuild.SolverBehaviourVersion);

        var (plane, _) = GeometryBuilder.Build(Plane());
        var (volume, _) = GeometryBuilder3D.Build(Volume());

        var solved = (Plane: Probes.Select(p => plane.PotentialAt(p)).ToArray(), Volume: Probes.Select(p => volume.PotentialAt(p)).ToArray());

        output.WriteLine("plane  " + string.Join(", ", solved.Plane.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
        output.WriteLine("volume " + string.Join(", ", solved.Volume.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));

        for (var i = 0; i < Probes.Length; i++)
        {
            Assert.True(
                Math.Abs(solved.Plane[i] - PinnedPlane[i]) <= Relative * Math.Abs(PinnedPlane[i]),
                $"plane probe {i}: {solved.Plane[i]:R} against {PinnedPlane[i]:R} pinned");
            Assert.True(
                Math.Abs(solved.Volume[i] - PinnedVolume[i]) <= Relative * Math.Abs(PinnedVolume[i]),
                $"volume probe {i}: {solved.Volume[i]:R} against {PinnedVolume[i]:R} pinned");
        }
    }

    /// <summary>
    /// The paths the first pins do not reach: the cylindrical operator with a Neumann edge, and
    /// a volume solved through the Galerkin hierarchy, pinned the same way and for the same
    /// reason.
    /// </summary>
    /// <remarks>
    /// No tilt, arc or revolve, so nothing here goes through a trigonometric function: the
    /// argument that the pins hold on every runner is the one above - no fused multiply-add,
    /// no <c>Vector&lt;T&gt;</c>, no parallel sum in these solvers.
    /// </remarks>
    [Fact]
    public void TheCylindricalAndGalerkinPathsMoveWithTheVersionToo()
    {
        Assert.Equal(2, EngineBuild.SolverBehaviourVersion);

        var (cylinder, _) = GeometryBuilder.Build(Cylinder());
        var (galerkin, report) = GeometryBuilder3D.Build(Galerkin());

        Assert.True(report.Galerkin, "the volume should take the Galerkin hierarchy");

        var solved = (Cylinder: Probes.Select(p => cylinder.PotentialAt(p)).ToArray(), Galerkin: Probes.Select(p => galerkin.PotentialAt(p)).ToArray());

        output.WriteLine("cylinder " + string.Join(", ", solved.Cylinder.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
        output.WriteLine("galerkin " + string.Join(", ", solved.Galerkin.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));

        for (var i = 0; i < Probes.Length; i++)
        {
            Assert.True(
                Math.Abs(solved.Cylinder[i] - PinnedCylinder[i]) <= Relative * Math.Abs(PinnedCylinder[i]),
                $"cylinder probe {i}: {solved.Cylinder[i]:R} against {PinnedCylinder[i]:R} pinned");
            Assert.True(
                Math.Abs(solved.Galerkin[i] - PinnedGalerkin[i]) <= Relative * Math.Abs(PinnedGalerkin[i]),
                $"galerkin probe {i}: {solved.Galerkin[i]:R} against {PinnedGalerkin[i]:R} pinned");
        }
    }
}
