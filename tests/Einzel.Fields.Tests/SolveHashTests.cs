using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>
/// The solve key: it moves with every input the solver reads, and with nothing else.
/// </summary>
/// <remarks>
/// <para>
/// Both directions are the test. A key that misses an input hands a cache the wrong field,
/// silently; a key that includes something the solver never reads only costs a solve, but it
/// is also how a comment edit would end up invalidating a ten-minute volume. So every
/// property the canonical form writes has a mutation here that must move the hash, and every
/// property it leaves out has one that must not.
/// </para>
/// <para>
/// Mutation-checked: each of the 61 lines of <see cref="SolveHash"/> that writes a value
/// was deleted in turn, and every deletion failed a test here - a named mutation case for
/// each value, and the pins for the counts that guard list boundaries.
/// </para>
/// </remarks>
public sealed class SolveHashTests
{
    private const double TenthOfAMicron = 1e-7;

    private static readonly CompiledElectrode Plate = new()
    {
        Name = "plate",
        Shape = ElectrodeShape.Rectangle,
        MinX = -0.004,
        MinY = 0.002,
        MaxX = 0.004,
        MaxY = 0.003,
        Potential = 100.0,
    };

    private static readonly CompiledElectrode Rod = new()
    {
        Name = "rod",
        Shape = ElectrodeShape.Disc,
        CentreX = 0.0,
        CentreY = -0.004,
        Radius = 0.001,
        Potential = -50.0,
    };

    private static readonly CompiledElectrode Wedge = new()
    {
        Name = "wedge",
        Shape = ElectrodeShape.Polygon,
        MinX = 0.005,
        MinY = -0.002,
        MaxX = 0.007,
        MaxY = 0.0,
        Vertices = [(0.005, -0.002), (0.007, -0.002), (0.006, 0.0)],
        Potential = 20.0,
    };

    private static readonly CompiledElectrode Profile = new()
    {
        Name = "profile",
        Shape = ElectrodeShape.EdgeProfile,
        Edge = GridEdge.Top,
        Profile = [(-0.01, 0.0), (0.01, 10.0)],
    };

    private static CompiledSolvedField Plane(params CompiledElectrode[] electrodes) => new()
    {
        MinX = -0.01,
        MinY = -0.01,
        MaxX = 0.01,
        MaxY = 0.01,
        CellSize = 0.0005,
        Tolerance = 1e-10,
        Electrodes = electrodes.Length > 0 ? electrodes : [Plate, Rod, Wedge, Profile],
    };

    private static CompiledSolvedField Replace(int index, Func<CompiledElectrode, CompiledElectrode> edit)
    {
        var plane = Plane();
        var electrodes = plane.Electrodes.ToArray();
        electrodes[index] = edit(electrodes[index]);
        return plane with { Electrodes = electrodes };
    }

    private static readonly Dictionary<string, Func<CompiledSolvedField>> PlaneMutations = new()
    {
        ["box min x"] = () => Plane() with { MinX = -0.01 - TenthOfAMicron },
        ["box min y"] = () => Plane() with { MinY = -0.01 - TenthOfAMicron },
        ["box max x"] = () => Plane() with { MaxX = 0.01 + TenthOfAMicron },
        ["box max y"] = () => Plane() with { MaxY = 0.01 + TenthOfAMicron },
        ["cell size"] = () => Plane() with { CellSize = 0.0005 + TenthOfAMicron },
        ["tolerance"] = () => Plane() with { Tolerance = 1e-11 },
        ["symmetry"] = () => Plane() with { Symmetry = SolveSymmetry.Cylindrical },
        ["left edge"] = () => Plane() with { LeftEdge = BoundaryKind.Neumann },
        ["right edge"] = () => Plane() with { RightEdge = BoundaryKind.Neumann },
        ["bottom edge"] = () => Plane() with { BottomEdge = BoundaryKind.Neumann },
        ["top edge"] = () => Plane() with { TopEdge = BoundaryKind.Neumann },
        ["electrode shape"] = () => Replace(0, e => e with { Shape = ElectrodeShape.Polygon }),
        ["electrode min x"] = () => Replace(0, e => e with { MinX = e.MinX + TenthOfAMicron }),
        ["electrode min y"] = () => Replace(0, e => e with { MinY = e.MinY + TenthOfAMicron }),
        ["electrode max x"] = () => Replace(0, e => e with { MaxX = e.MaxX + TenthOfAMicron }),
        ["electrode max y"] = () => Replace(0, e => e with { MaxY = e.MaxY + TenthOfAMicron }),
        ["disc centre x"] = () => Replace(1, e => e with { CentreX = e.CentreX + TenthOfAMicron }),
        ["disc centre y"] = () => Replace(1, e => e with { CentreY = e.CentreY + TenthOfAMicron }),
        ["disc radius"] = () => Replace(1, e => e with { Radius = e.Radius + TenthOfAMicron }),
        ["polygon vertex x"] = () => Replace(2, e => e with { Vertices = [(0.005 + TenthOfAMicron, -0.002), (0.007, -0.002), (0.006, 0.0)] }),
        ["polygon vertex y"] = () => Replace(2, e => e with { Vertices = [(0.005, -0.002 + TenthOfAMicron), (0.007, -0.002), (0.006, 0.0)] }),
        ["polygon vertex count"] = () => Replace(2, e => e with { Vertices = [(0.005, -0.002), (0.007, -0.002), (0.0065, -0.001), (0.006, 0.0)] }),
        ["profile edge"] = () => Replace(3, e => e with { Edge = GridEdge.Bottom }),
        ["profile position"] = () => Replace(3, e => e with { Profile = [(-0.01, 0.0), (0.01 - TenthOfAMicron, 10.0)] }),
        ["profile potential"] = () => Replace(3, e => e with { Profile = [(-0.01, 0.0), (0.01, 10.5)] }),
        ["electrode order"] = () => Plane(Rod, Plate, Wedge, Profile),
        ["electrode count"] = () => Plane(Plate, Rod, Wedge),
    };

    private static readonly Dictionary<string, Func<CompiledSolvedField>> PlaneNonInputs = new()
    {
        ["electrode name"] = () => Replace(0, e => e with { Name = "renamed" }),
        ["extrusion tilt"] = () => Plane() with { TiltHalfTurns = 0.01, TiltCentreX = 0.002, TiltCentreZ = 0.001 },
        ["mirror plane"] = () => Plane() with { ReflectAboutX = 0.02 },
        ["boundary reported discontinuous"] = () => Plane() with { BoundaryIsDiscontinuous = true },
        ["potential"] = () => Replace(0, e => e with { Potential = 250.0 }),
        ["drive tap"] = () => Replace(1, e => e with { Taps = [new CompiledTap(0, 100.0, 0.0)] }) with
        {
            Drives = [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)],
        },
        ["negative zero"] = () => Replace(1, e => e with { CentreX = -0.0 }),
    };

    public static TheoryData<string> PlaneMutationNames => [.. PlaneMutations.Keys];

    public static TheoryData<string> PlaneNonInputNames => [.. PlaneNonInputs.Keys];

    [Theory]
    [MemberData(nameof(PlaneMutationNames))]
    public void EveryInputTheSolverReadsMovesThePlaneHash(string mutation)
    {
        Assert.NotEqual(SolveHash.Geometry(Plane()), SolveHash.Geometry(PlaneMutations[mutation]()));
    }

    [Theory]
    [MemberData(nameof(PlaneNonInputNames))]
    public void WhatTheSolverDoesNotReadLeavesThePlaneHash(string mutation)
    {
        Assert.Equal(SolveHash.Geometry(Plane()), SolveHash.Geometry(PlaneNonInputs[mutation]()));
    }

    /// <summary>
    /// The hash is a function of the values, not of the object: two separately built equal
    /// geometries agree, and the format is pinned so a change to it is a decision rather
    /// than an accident.
    /// </summary>
    /// <remarks>
    /// If this value has to change, the canonical form changed: change the tag in
    /// <see cref="SolveHash"/> with it, so no key written before can match one written after.
    /// </remarks>
    [Fact]
    public void ThePlaneHashIsAPinnedFunctionOfTheValues()
    {
        var first = SolveHash.Geometry(Plane());
        var second = SolveHash.Geometry(Plane(
            Plate with { },
            Rod with { },
            Wedge with { Vertices = [.. Wedge.Vertices] },
            Profile with { Profile = [.. Profile.Profile] }));

        Assert.Equal(first, second);
        Assert.Equal(PinnedPlaneHash, first);
    }

    private const string PinnedPlaneHash =
        "sha256:0ecdeeb203a7a5253b99948d04dadc62fd4529237a277102c9050cff0718749a";

    /// <summary>
    /// A static solve is made with the declared potentials, so they are its channel: the
    /// geometry hash holds still and the channel hash moves.
    /// </summary>
    [Fact]
    public void AStaticPotentialIsTheChannelNotTheGeometry()
    {
        var before = Plane();
        var after = Replace(0, e => e with { Potential = 101.0 });

        var geometry = SolveHash.Geometry(before);
        Assert.Equal(geometry, SolveHash.Geometry(after));

        var channelBefore = Assert.Single(GeometryBuilder.Holdings(before));
        var channelAfter = Assert.Single(GeometryBuilder.Holdings(after));

        Assert.NotEqual(
            SolveHash.Channel(geometry, channelBefore),
            SolveHash.Channel(geometry, channelAfter));
    }

    /// <summary>
    /// An RF amplitude is a weight on a solved pattern, not a different solve: a quadrupole
    /// driven at 200 V and at 300 V has the same channels, while moving its DC onto a pattern
    /// of its own adds one.
    /// </summary>
    [Fact]
    public void AnRfAmplitudeIsAWeightAndNotASolve()
    {
        static CompiledSolvedField Quadrupole(double amplitude, double dc)
        {
            // The DC on one pair only, so it is a pattern of its own: on both pairs at opposite
            // signs it would be the RF's own pattern, and a weight on the same solve.
            CompiledElectrode Pole(string name, double x, double y, double phase, double direct) => new()
            {
                Name = name,
                Shape = ElectrodeShape.Disc,
                CentreX = x,
                CentreY = y,
                Radius = 0.001,
                Potential = direct,
                Taps = [new CompiledTap(0, amplitude, phase)],
            };

            return Plane(
                Pole("x+", 0.004, 0.0, 0.0, dc),
                Pole("x-", -0.004, 0.0, 0.0, dc),
                Pole("y+", 0.0, 0.004, 0.5, 0.0),
                Pole("y-", 0.0, -0.004, 0.5, 0.0)) with
            {
                Drives = [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)],
            };
        }

        string[] Channels(CompiledSolvedField solve)
        {
            var geometry = SolveHash.Geometry(solve);
            return [.. GeometryBuilder.Holdings(solve).Select(h => SolveHash.Channel(geometry, h))];
        }

        var low = Channels(Quadrupole(200.0, 0.0));
        var high = Channels(Quadrupole(300.0, 0.0));
        var withDc = Channels(Quadrupole(200.0, 10.0));

        Assert.Single(low);
        Assert.Equal(low, high);
        Assert.True(withDc.Length > low.Length, "a DC pattern distinct from the RF one is a further solve");
    }

    /// <summary>
    /// The key is computed before the solve, and must name the solve that then runs: for a
    /// static, a driven, a sequenced-with-ramp and a sequenced-without-drive plane, each
    /// channel actually solved hashes to the key worked out beforehand, in the same order.
    /// </summary>
    /// <remarks>
    /// Mutation: <see cref="GeometryBuilder.Holdings"/> returning the static holding for every
    /// geometry fails the driven and sequenced cases; reversing its channel order fails the
    /// order check on each of them.
    /// </remarks>
    [Fact]
    public void ThePlaneKeyBeforeTheSolveNamesTheSolveThatRuns()
    {
        var drive = new CompiledDrive(1.0e6, DriveWaveform.Rectangular, 0.5);
        var driven = Plane() with
        {
            Electrodes = [Plate, Rod with { Taps = [new CompiledTap(0, 100.0, 0.0)] }, Wedge, Profile],
            Drives = [drive],
        };

        // A hold, then a ramp whose end state moves the DC onto the rod: three states, and
        // patterns that only the ramp's end contributes.
        CompiledElectrode[] State(double plate, double rod) =>
            [Plate with { Potential = plate }, Rod with { Potential = rod }, Wedge, Profile];

        var stages = new[]
        {
            new CompiledStage("hold", 1e-6, State(100.0, 0.0)),
            new CompiledStage("ramp", 1e-6, State(100.0, 0.0)) { EndElectrodes = State(0.0, -40.0) },
        };

        var sequenced = driven with { Stages = stages };
        var switchedOnly = Plane() with { Stages = stages };

        foreach (var solve in new[] { Plane(), driven, sequenced, switchedOnly })
        {
            var geometry = SolveHash.Geometry(solve);
            var before = GeometryBuilder.Holdings(solve).Select(h => SolveHash.Channel(geometry, h)).ToArray();
            var solved = GeometryBuilder.SolveChannels(solve).Select(c => SolveHash.Channel(geometry, c.Holding)).ToArray();

            Assert.NotEmpty(before);
            Assert.Equal(before, solved);
        }

        Assert.True(GeometryBuilder.Holdings(driven).Count > 1, "the driven plane must have more than one channel to test the order");
    }

    private static readonly CompiledElectrode3D Slab = new()
    {
        Name = "slab",
        Shape = Electrode3DShape.Box,
        MinX = -0.002,
        MinY = -0.002,
        MinZ = 0.001,
        MaxX = 0.002,
        MaxY = 0.002,
        MaxZ = 0.0015,
        Potential = 100.0,
    };

    private static readonly CompiledElectrode3D Tube = new()
    {
        Name = "tube",
        Shape = Electrode3DShape.Cylinder,
        CentreX = 0.0,
        CentreY = 0.0,
        CentreZ = 0.0,
        Radius = 0.001,
        Axis = CylinderAxis.Z,
        Lower = -0.003,
        Upper = -0.002,
        Potential = -20.0,
    };

    private static readonly CompiledElectrode3D Ring = new()
    {
        Name = "ring",
        Shape = Electrode3DShape.Revolve,
        Axis = CylinderAxis.Z,
        Vertices = [(0.003, -0.001), (0.0035, -0.001), (0.0035, 0.0)],
        FromHalfTurns = 0.0,
        ToHalfTurns = 2.0,
        Potential = 5.0,
    };

    private static Geometry3D Volume(params CompiledElectrode3D[] electrodes) => new(
        -0.004, -0.004, -0.004, 0.004, 0.004, 0.004, 0.0005,
        electrodes.Length > 0 ? electrodes : [Slab, Tube, Ring],
        1e-9);

    private static Geometry3D Replace3D(int index, Func<CompiledElectrode3D, CompiledElectrode3D> edit)
    {
        var volume = Volume();
        var electrodes = volume.Electrodes.ToArray();
        electrodes[index] = edit(electrodes[index]);
        return volume with { Electrodes = electrodes };
    }

    private static IReadOnlyList<EdgeCondition> Faces(int neumann) =>
        [.. Enumerable.Range(0, 6).Select(f => f == neumann ? EdgeCondition.Neumann : EdgeCondition.Dirichlet)];

    private static readonly Dictionary<string, Func<Geometry3D>> VolumeMutations = new()
    {
        ["box min x"] = () => Volume() with { MinX = -0.004 - TenthOfAMicron },
        ["box min y"] = () => Volume() with { MinY = -0.004 - TenthOfAMicron },
        ["box min z"] = () => Volume() with { MinZ = -0.004 - TenthOfAMicron },
        ["box max x"] = () => Volume() with { MaxX = 0.004 + TenthOfAMicron },
        ["box max y"] = () => Volume() with { MaxY = 0.004 + TenthOfAMicron },
        ["box max z"] = () => Volume() with { MaxZ = 0.004 + TenthOfAMicron },
        ["cell size"] = () => Volume() with { CellSize = 0.0005 + TenthOfAMicron },
        ["tolerance"] = () => Volume() with { Tolerance = 1e-10 },
        ["face 0"] = () => Volume() with { Faces = Faces(0) },
        ["face 5"] = () => Volume() with { Faces = Faces(5) },
        ["electrode shape"] = () => Replace3D(0, e => e with { Shape = Electrode3DShape.Prism }),
        ["electrode min x"] = () => Replace3D(0, e => e with { MinX = e.MinX + TenthOfAMicron }),
        ["electrode min y"] = () => Replace3D(0, e => e with { MinY = e.MinY + TenthOfAMicron }),
        ["electrode min z"] = () => Replace3D(0, e => e with { MinZ = e.MinZ + TenthOfAMicron }),
        ["electrode max x"] = () => Replace3D(0, e => e with { MaxX = e.MaxX + TenthOfAMicron }),
        ["electrode max y"] = () => Replace3D(0, e => e with { MaxY = e.MaxY + TenthOfAMicron }),
        ["electrode max z"] = () => Replace3D(0, e => e with { MaxZ = e.MaxZ + TenthOfAMicron }),
        ["box tilt axis"] = () => Replace3D(0, e => e with { TiltAxis = CylinderAxis.Y }),
        ["box tilt"] = () => Replace3D(0, e => e with { TiltHalfTurns = 0.01 }),
        ["centre x"] = () => Replace3D(1, e => e with { CentreX = e.CentreX + TenthOfAMicron }),
        ["centre y"] = () => Replace3D(1, e => e with { CentreY = e.CentreY + TenthOfAMicron }),
        ["centre z"] = () => Replace3D(1, e => e with { CentreZ = e.CentreZ + TenthOfAMicron }),
        ["radius"] = () => Replace3D(1, e => e with { Radius = e.Radius + TenthOfAMicron }),
        ["axis"] = () => Replace3D(1, e => e with { Axis = CylinderAxis.X }),
        ["lower"] = () => Replace3D(1, e => e with { Lower = e.Lower + TenthOfAMicron }),
        ["upper"] = () => Replace3D(1, e => e with { Upper = e.Upper + TenthOfAMicron }),
        ["vertex x"] = () => Replace3D(2, e => e with { Vertices = [(0.003 + TenthOfAMicron, -0.001), (0.0035, -0.001), (0.0035, 0.0)] }),
        ["vertex y"] = () => Replace3D(2, e => e with { Vertices = [(0.003, -0.001 + TenthOfAMicron), (0.0035, -0.001), (0.0035, 0.0)] }),
        ["sweep start"] = () => Replace3D(2, e => e with { FromHalfTurns = 0.5 }),
        ["sweep end"] = () => Replace3D(2, e => e with { ToHalfTurns = 1.5 }),
        ["electrode order"] = () => Volume(Tube, Slab, Ring),
    };

    private static readonly Dictionary<string, Func<Geometry3D>> VolumeNonInputs = new()
    {
        ["electrode name"] = () => Replace3D(0, e => e with { Name = "renamed" }),
        ["mirror plane"] = () => Volume() with { ReflectAboutX = 0.004 },
        ["potential"] = () => Replace3D(0, e => e with { Potential = 250.0 }),
        ["six grounded faces spelled out"] = () => Volume() with { Faces = Faces(-1) },
    };

    public static TheoryData<string> VolumeMutationNames => [.. VolumeMutations.Keys];

    public static TheoryData<string> VolumeNonInputNames => [.. VolumeNonInputs.Keys];

    [Theory]
    [MemberData(nameof(VolumeMutationNames))]
    public void EveryInputTheSolverReadsMovesTheVolumeHash(string mutation)
    {
        Assert.NotEqual(SolveHash.Geometry(Volume()), SolveHash.Geometry(VolumeMutations[mutation]()));
    }

    [Theory]
    [MemberData(nameof(VolumeNonInputNames))]
    public void WhatTheSolverDoesNotReadLeavesTheVolumeHash(string mutation)
    {
        Assert.Equal(SolveHash.Geometry(Volume()), SolveHash.Geometry(VolumeNonInputs[mutation]()));
    }

    /// <summary>
    /// The volume and channel forms are pinned as the plane form is.
    /// </summary>
    /// <remarks>
    /// Not redundant with the mutations above. A count written before a list that ends its
    /// record - the electrodes of a volume, a revolve's vertices before fixed fields, a
    /// channel's values - is what keeps the stream prefix-free if a field is ever appended,
    /// and deleting one changes no comparison between two keys built by today's code. Only
    /// a pin sees it go, and a pin is what a format needs anyway.
    /// </remarks>
    [Fact]
    public void TheVolumeAndChannelHashesArePinned()
    {
        var volume = SolveHash.Geometry(Volume());

        Assert.Equal(PinnedVolumeHash, volume);
        Assert.Equal(PinnedChannelHash, SolveHash.Channel(volume, [1.0, -0.2, 0.0]));
    }

    private const string PinnedVolumeHash =
        "sha256:e76c698393eda4966c017eca2699af31469327710cfb81c3f358464f9fdd1d71";

    private const string PinnedChannelHash =
        "sha256:b8cb93ccdc21f51a1e99e3827c8c265d81ce141c76b6ca39c6312ce1c5ecdb92";

    /// <summary>The volume half of <see cref="ThePlaneKeyBeforeTheSolveNamesTheSolveThatRuns"/>.</summary>
    [Fact]
    public void TheVolumeKeyBeforeTheSolveNamesTheSolveThatRuns()
    {
        var plate = Slab;
        var driven = Volume(plate, Tube with { Taps = [new CompiledTap(0, 50.0, 0.0)] }) with
        {
            Drives = [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)],
        };

        var sequenced = driven with
        {
            Stages =
            [
                new CompiledStage3D("hold", 1e-6, [plate, Tube]),
                new CompiledStage3D("ramp", 1e-6, [plate, Tube]) { EndElectrodes = [plate with { Potential = 0.0 }, Tube with { Potential = 30.0 }] },
            ],
        };

        foreach (var geometry in new[] { Volume(plate, Tube), driven, sequenced })
        {
            var hash = SolveHash.Geometry(geometry);
            var before = GeometryBuilder3D.Holdings(geometry).Select(h => SolveHash.Channel(hash, h)).ToArray();
            var solved = GeometryBuilder3D.SolveChannels(geometry).Select(c => SolveHash.Channel(hash, c.Holding)).ToArray();

            Assert.NotEmpty(before);
            Assert.Equal(before, solved);
        }

        Assert.True(GeometryBuilder3D.Holdings(driven).Count > 1, "the driven volume must have more than one channel to test the order");
    }

    /// <summary>
    /// A channel hash moves with its geometry and with every value it holds, their order and
    /// their count; negative zero is zero.
    /// </summary>
    [Fact]
    public void AChannelHashDependsOnItsGeometryAndOnEveryValueItHolds()
    {
        var geometry = SolveHash.Geometry(Plane());
        var other = SolveHash.Geometry(Plane() with { CellSize = 0.00025 });

        Assert.NotEqual(SolveHash.Channel(geometry, [1.0, 0.0]), SolveHash.Channel(other, [1.0, 0.0]));
        Assert.NotEqual(SolveHash.Channel(geometry, [1.0, 0.0]), SolveHash.Channel(geometry, [0.0, 1.0]));
        Assert.NotEqual(SolveHash.Channel(geometry, [1.0, 0.0]), SolveHash.Channel(geometry, [1.0, 0.0, 0.0]));
        Assert.Equal(SolveHash.Channel(geometry, [1.0, 0.0]), SolveHash.Channel(geometry, [1.0, -0.0]));
    }
}
