using System.Diagnostics.CodeAnalysis;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;

namespace Einzel.Fields.Tests;

/// <summary>
/// A channel loaded from a store is the channel solved: the same field, the same report, the
/// same warnings, and no solve.
/// </summary>
/// <remarks>
/// <para>
/// The store here is a dictionary, so these are about the builders' half of the cache - that
/// they ask before they solve, hand back what they solved, and rebuild a field from a load
/// that cannot be told from the one solved. Cached and uncached are produced in the same
/// process, never against a committed number: a solve is reproducible on one machine, not
/// bit for bit across machines.
/// </para>
/// <para>
/// "No solve" is counted as no <see cref="ISolveStore.Save"/>: every solve a builder makes
/// with a store in hand is saved, so a second build that saves nothing solved nothing.
/// </para>
/// </remarks>
public sealed class SolveStoreTests
{
    /// <summary>A store in a dictionary, counting what it is asked.</summary>
    private sealed class MemoryStore : ISolveStore
    {
        private readonly Dictionary<string, (double[] Potential, SolveRecord Record)> _entries = [];

        public int Loads { get; private set; }

        public int Hits { get; private set; }

        public int Saves { get; private set; }

        public IEnumerable<SolveRecord> Records => _entries.Values.Select(e => e.Record);

        public bool TryLoad(
            SolveEntry entry,
            [NotNullWhen(true)] out double[]? potential,
            [NotNullWhen(true)] out SolveRecord? record)
        {
            Loads++;

            if (_entries.TryGetValue(entry.ChannelHash, out var kept))
            {
                Hits++;
                potential = (double[])kept.Potential.Clone();
                record = kept.Record;
                return true;
            }

            potential = null;
            record = null;
            return false;
        }

        public void Save(SolveEntry entry, ReadOnlySpan<double> potential, SolveRecord record)
        {
            Assert.Equal(entry.NodeCount, potential.Length);
            Saves++;
            _entries[entry.ChannelHash] = (potential.ToArray(), record);
        }
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

    /// <summary>An 8 mm cube at 0.5 mm, 17 nodes a side; its lower x face a mirror.</summary>
    private static Geometry3D Volume(params CompiledElectrode3D[] electrodes) => new(
        -0.004, -0.004, -0.004, 0.004, 0.004, 0.004, 0.0005, electrodes, 1e-9)
    {
        Faces = [EdgeCondition.Neumann, .. Enumerable.Repeat(EdgeCondition.Dirichlet, 5)],
    };

    private static readonly Dictionary<string, Func<Geometry3D>> Volumes = new()
    {
        ["static"] = () => Volume(Slab, Tube),
        ["driven"] = () => Volume(Slab, Tube with { Taps = [new CompiledTap(0, 50.0, 0.0)] }) with
        {
            Drives = [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)],
        },
        ["sequenced"] = () => Volume(Slab, Tube with { Taps = [new CompiledTap(0, 50.0, 0.0)] }) with
        {
            Drives = [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)],
            Stages =
            [
                new CompiledStage3D("hold", 1e-6, [Slab, Tube]),
                new CompiledStage3D("ramp", 1e-6, [Slab, Tube])
                {
                    EndElectrodes = [Slab with { Potential = 0.0 }, Tube with { Potential = 30.0 }],
                },
            ],
        },
    };

    public static TheoryData<string> VolumeNames => [.. Volumes.Keys];

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

    /// <summary>A 20 mm square at 0.5 mm; its left edge a mirror.</summary>
    private static CompiledSolvedField Plane() => new()
    {
        MinX = -0.01,
        MinY = -0.01,
        MaxX = 0.01,
        MaxY = 0.01,
        CellSize = 0.0005,
        Tolerance = 1e-10,
        LeftEdge = BoundaryKind.Neumann,
        Electrodes = [Plate, Rod],
    };

    private static readonly Dictionary<string, Func<CompiledSolvedField>> Planes = new()
    {
        ["static"] = Plane,
        ["driven"] = () => Plane() with
        {
            Electrodes = [Plate, Rod with { Taps = [new CompiledTap(0, 100.0, 0.0)] }],
            Drives = [new CompiledDrive(1.0e6, DriveWaveform.Rectangular, 0.5)],
        },
    };

    public static TheoryData<string> PlaneNames => [.. Planes.Keys];

    /// <summary>Points inside the cube, one a fifth of a cell off its mirror face.</summary>
    private static readonly Vec3[] VolumeProbes =
    [
        new(0.0, 0.0, 0.0),
        new(0.0011, -0.0007, 0.0023),
        new(-0.0021, 0.0015, -0.0012),
        new(-0.0039, 0.0003, 0.0005),
    ];

    /// <summary>Points inside the square, one two fifths of a cell off its mirror edge.</summary>
    private static readonly Vec3[] PlaneProbes =
    [
        new(0.0, 0.0, 0.0),
        new(0.0011, -0.0067, 0.0),
        new(-0.0021, 0.0035, 0.0),
        new(-0.0098, 0.0001, 0.0),
    ];

    private static readonly double[] Times = [0.0, 0.31e-6, 1.47e-6];

    /// <summary>The potential and field everywhere the probes reach, at every probe time.</summary>
    private static List<double> Sample(IElectrostaticField field, Vec3[] probes)
    {
        var values = new List<double>();

        foreach (var probe in probes)
        {
            foreach (var time in Times)
            {
                var (potential, e) = field is ITimeVaryingField driven
                    ? (driven.PotentialAt(probe, time), driven.ElectricFieldAt(probe, time))
                    : (field.PotentialAt(probe), field.ElectricFieldAt(probe));

                values.AddRange([potential, e.X, e.Y, e.Z]);
            }
        }

        return values;
    }

    private static (bool, int, double, double, double, int, long, long, bool) Scalars(SolveReport report) =>
        (report.Converged, report.Cycles, report.InitialResidual, report.FinalResidual, report.ConvergenceFactor,
            report.Levels, report.Sweeps, report.CoarsestNodes, report.Galerkin);

    /// <summary>
    /// A static, a driven and a sequenced volume built a second time through the store solve
    /// nothing and give the field, the report and the warnings of a build without one.
    /// </summary>
    /// <remarks>
    /// Mutations: never asking the store (the second build solves again, so it saves); handing
    /// back a zeroed potential, or one without its face conditions (the probe a quarter cell
    /// off the mirror face reads the interpolant's ghost node, which a Dirichlet face makes
    /// a ramp); and a rebuilt report with another cycle count. Each fails here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(VolumeNames))]
    public void AVolumeLoadedFromTheStoreIsTheVolumeSolved(string name)
    {
        var geometry = Volumes[name]();
        var channels = GeometryBuilder3D.Holdings(geometry).Count;
        var store = new MemoryStore();

        var (solved, solvedReport) = GeometryBuilder3D.BuildField(geometry);
        _ = GeometryBuilder3D.BuildField(geometry, store);

        Assert.Equal((channels, 0, channels), (store.Loads, store.Hits, store.Saves));

        var (loaded, loadedReport) = GeometryBuilder3D.BuildField(geometry, store);

        Assert.Equal((2 * channels, channels, channels), (store.Loads, store.Hits, store.Saves));
        Assert.Equal(Sample(solved, VolumeProbes), Sample(loaded, VolumeProbes));
        Assert.Equal(Scalars(solvedReport), Scalars(loadedReport));
        Assert.Equal(solvedReport.Warnings, loadedReport.Warnings);
    }

    /// <summary>The same for a static and a driven plane.</summary>
    /// <remarks>
    /// Mutations: handing back a potential without its edge conditions (the probe a quarter
    /// cell off the mirror edge moves), or never asking the store. Each fails here.
    /// </remarks>
    [Theory]
    [MemberData(nameof(PlaneNames))]
    public void APlaneLoadedFromTheStoreIsThePlaneSolved(string name)
    {
        var solve = Planes[name]();
        var channels = GeometryBuilder.Holdings(solve).Count;
        var store = new MemoryStore();

        var (solved, solvedReport) = GeometryBuilder.Build(solve);
        _ = GeometryBuilder.Build(solve, store);
        var (loaded, loadedReport) = GeometryBuilder.Build(solve, store);

        Assert.Equal((2 * channels, channels, channels), (store.Loads, store.Hits, store.Saves));
        Assert.Equal(Sample(solved, PlaneProbes), Sample(loaded, PlaneProbes));
        Assert.Equal(Scalars(solvedReport), Scalars(loadedReport));
        Assert.Equal(solvedReport.Warnings, loadedReport.Warnings);
    }

    /// <summary>
    /// What <c>einzel solve</c> reports of a channel - the mask's two counts and the holding -
    /// is the same from a load as from a solve.
    /// </summary>
    /// <remarks>Mutation: a load reporting zero fixed nodes or cut links fails here.</remarks>
    [Fact]
    public void AChannelLoadedReportsTheCountsOfTheChannelSolved()
    {
        var geometry = Volumes["driven"]();
        var store = new MemoryStore();

        var solved = GeometryBuilder3D.SolveChannels(geometry, store);
        var loaded = GeometryBuilder3D.SolveChannels(geometry, store);

        Assert.Equal(solved.Count, store.Hits);
        Assert.Equal(
            solved.Select(c => (c.Index, c.FixedNodes, c.CutLinks, string.Join(",", c.Holding))),
            loaded.Select(c => (c.Index, c.FixedNodes, c.CutLinks, string.Join(",", c.Holding))));
        Assert.All(solved, c => Assert.True(c.FixedNodes > 0 && c.CutLinks > 0));
    }

    /// <summary>Two plates meeting at <paramref name="face"/> millimetres, from a document.</summary>
    /// <remarks>The model of <c>SharedFaceWarningTests</c>: three nodes on the face at 2 mm, none at 2.25.</remarks>
    private static CompiledModel Abutting(string face) => Compile($$"""
    {
      "schemaVersion": "0.3",
      "name": "abutting",
      "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [0, -7, 0], "unit": "mm" },
        "direction": { "value": [0, 1, 0] },
        "accelerationPotential": { "value": 10, "unit": "V" }
      },
      "fields": [
        {
          "type": "solved2d",
          "solve": {
            "minX": { "value": -8, "unit": "mm" },
            "minY": { "value": -8, "unit": "mm" },
            "maxX": { "value": 8, "unit": "mm" },
            "maxY": { "value": 8, "unit": "mm" },
            "cellSize": { "value": 1, "unit": "mm" },
            "tolerance": 1e-9,
            "electrodes": [
              {
                "name": "left", "shape": "rectangle",
                "minX": { "value": -4, "unit": "mm" }, "minY": { "value": 2, "unit": "mm" },
                "maxX": { "value": {{face}}, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                "potential": { "value": 100, "unit": "V" }
              },
              {
                "name": "right", "shape": "rectangle",
                "minX": { "value": {{face}}, "unit": "mm" }, "minY": { "value": 2, "unit": "mm" },
                "maxX": { "value": 4, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                "potential": { "value": -100, "unit": "V" }
              }
            ]
          }
        }
      ],
      "detector": { "planePoint": { "value": [0, 7, 0], "unit": "mm" }, "normal": { "value": [0, -1, 0] } },
      "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
    }
    """);

    private static CompiledModel Compile(string json)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null);
        Assert.True(validation.IsValid, validation.IsValid ? string.Empty : validation.Errors[0].Constraint);
        return validation.Model!;
    }

    /// <summary>
    /// The shared-face warning is not kept with a solve, since it depends on stage states the
    /// key does not cover; a hit asks the geometry again and earns the warning a miss earned,
    /// and the quarter-cell-off control stays quiet either way.
    /// </summary>
    /// <remarks>
    /// Mutation: a hit returning no mesh warnings fails the first case; one that adds the
    /// warning whatever the geometry fails the control.
    /// </remarks>
    [Theory]
    [InlineData("2", 1)]
    [InlineData("2.25", 0)]
    public void APlaneHitEarnsTheSharedFaceWarningAMissEarns(string face, int expected)
    {
        var model = Abutting(face);
        var store = new MemoryStore();

        var (_, uncached) = FieldAssembly.BuildReported(model);
        var (_, missed) = FieldAssembly.BuildReported(model, store);
        var (_, hit) = FieldAssembly.BuildReported(model, store);

        Assert.Equal(1, store.Hits);
        Assert.Equal(expected, uncached.Count(w => w.Code == SharedFaceNodes.Code));
        Assert.Equal(uncached, missed);
        Assert.Equal(uncached, hit);
    }

    /// <summary>
    /// The volume half: two boxes at opposite potentials meeting on a plane of nodes. A hit
    /// builds no mask, and the warning it earns from the geometry is the one the solve earned
    /// from its mask; boxes a quarter cell apart earn none either way.
    /// </summary>
    /// <remarks>Mutation: a volume hit returning no mesh warnings fails here.</remarks>
    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.000125, false)]
    public void AVolumeHitEarnsTheSharedFaceWarningAMissEarns(double gap, bool warned)
    {
        var geometry = Volume(
            Slab with { Name = "left", MinX = -0.002, MaxX = 0.0, MinZ = -0.001, MaxZ = 0.001 },
            Slab with { Name = "right", MinX = gap, MaxX = 0.002, MinZ = -0.001, MaxZ = 0.001, Potential = -100.0 });
        var store = new MemoryStore();

        var (_, uncached) = GeometryBuilder3D.BuildField(geometry);
        _ = GeometryBuilder3D.BuildField(geometry, store);
        var (_, hit) = GeometryBuilder3D.BuildField(geometry, store);

        Assert.Equal(1, store.Hits);
        Assert.Equal(warned, uncached.Warnings.Any(w => w.Code == SharedFaceNodes.Code));
        Assert.Equal(uncached.Warnings, hit.Warnings);
    }

    /// <summary>
    /// A solve that missed its tolerance is kept with its report, so the field it produced
    /// warns <c>field.not-converged</c> on every reuse, as it did when it was solved.
    /// </summary>
    /// <remarks>
    /// Mutations: saving only converged solves (the second build solves again) and a hit
    /// rebuilding its report as converged (the warning goes) each fail here.
    /// </remarks>
    [Fact]
    public void AnUnconvergedSolveIsKeptWithItsReportAndStillWarns()
    {
        var model = Compile("""
        {
          "schemaVersion": "0.3",
          "name": "strained",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, -8, 0], "unit": "mm" },
            "direction": { "value": [0, 1, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            {
              "type": "solved2d",
              "solve": {
                "minX": { "value": -10, "unit": "mm" }, "minY": { "value": -10, "unit": "mm" },
                "maxX": { "value": 10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
                "cellSize": { "value": 0.4, "unit": "mm" },
                "tolerance": 1e-30,
                "electrodes": [
                  {
                    "name": "plate", "shape": "rectangle",
                    "minX": { "value": -4, "unit": "mm" }, "minY": { "value": -1, "unit": "mm" },
                    "maxX": { "value": 4, "unit": "mm" }, "maxY": { "value": 1, "unit": "mm" },
                    "potential": { "value": 100, "unit": "V" }
                  }
                ]
              }
            }
          ],
          "detector": { "planePoint": { "value": [0, 9, 0], "unit": "mm" }, "normal": { "value": [0, -1, 0] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 1, "unit": "ms" } }
        }
        """);
        var store = new MemoryStore();

        var (_, solved) = FieldAssembly.BuildReported(model, store);
        var (_, loaded) = FieldAssembly.BuildReported(model, store);

        Assert.Equal((1, 1), (store.Hits, store.Saves));
        Assert.False(Assert.Single(store.Records).Report.Converged);
        Assert.Equal("field.not-converged", Assert.Single(solved).Code);
        Assert.Equal(solved, loaded);
    }
}
