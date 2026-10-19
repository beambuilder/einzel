using System.Diagnostics;

using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The measurements here force full collections from a second thread, so they must not share
/// the process with anything else allocating: another test's arrays would be counted as this
/// build's. xUnit runs a collection with parallelization disabled after every parallel one.
/// </summary>
[CollectionDefinition(nameof(MemoryMeasurement), DisableParallelization = true)]
public sealed class MemoryMeasurement;

/// <summary>
/// What a build says it will hold, against what a build actually holds.
/// </summary>
/// <remarks>
/// <para>
/// <c>einzel estimate</c> reported 48 bytes a node for every solve, from a comment listing
/// three fields and some restriction buffers, and nothing measured it. A volume solve holds
/// five times that: the conductor mask and its six cut-link arms are 105 bytes a node before a
/// field is allocated, and the Galerkin hierarchy stores the fine operator and forms its first
/// coarse level from it. The Astral at a 1 mm cell was billed 3.1 GiB and needs 15.
/// </para>
/// <para>
/// <b>Measured as a slope between two mesh sizes</b>, so that whatever the process holds
/// regardless of the mesh - the runtime, the geometry, the JIT - cancels, and what is compared
/// is bytes a node. The live set is sampled by forcing a full collection every few
/// milliseconds through the build and keeping the largest; a sample can only miss the peak and
/// read low, which is why the band below is wider under the prediction than over it.
/// </para>
/// <para>
/// <b>The upper edge is the one that matters.</b> An estimate that runs under is discovered by
/// the run failing twenty minutes in, which is the failure GRD-8's gate exists to prevent. If
/// a solver change adds a per-node array, this is what fails, and the fix is a term in
/// <see cref="SolveMemoryModel"/> beside the reason for the array.
/// </para>
/// </remarks>
[Collection(nameof(MemoryMeasurement))]
public sealed class SolveMemoryTests(ITestOutputHelper output)
{
    /// <summary>The Galerkin hierarchy's peak, per node, against two thin slabs at two meshes.</summary>
    [Fact]
    public void AGalerkinBuildHoldsWhatItsStatementSays()
    {
        var (predicted, measured, galerkin) = Slope(Plates, 0.0005, 0.00025);

        Assert.True(galerkin, "thin slabs leave the rediscretized hierarchy a bottom the size of the mesh");
        Assert.InRange(measured / predicted, 0.90, 1.02);
    }

    /// <summary>The rediscretized hierarchy's peak, per node, against a fat sphere at two meshes.</summary>
    [Fact]
    public void ARediscretizedBuildHoldsWhatItsStatementSays()
    {
        var (predicted, measured, galerkin) = Slope(Sphere, 0.00125, 0.000625);

        Assert.False(galerkin, "a fat sphere coarsens to a small bottom and keeps the cheap hierarchy");
        Assert.InRange(measured / predicted, 0.90, 1.02);
    }

    /// <summary>
    /// A second basis channel costs one kept potential, not a second geometry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Channels are solved one at a time, so the peak is one working set plus the potentials
    /// already solved - 8 bytes a node a channel. Twice that was not true. The channel loop
    /// kept each finished channel's mask in the record it returned, 105 bytes a node; and once
    /// that was removed, a temporary in the loop still held the previous channel's cut links,
    /// because a method run once executes as first-tier JIT code and first-tier code keeps
    /// every temporary reachable until its slot is reused. The linear ion trap measured 353,
    /// then 344, bytes a node for two channels, against 248.
    /// </para>
    /// <para>
    /// Asserted as the difference between a two-channel build and the same geometry with its
    /// drive removed, at one mesh: everything else is the same build, so what is left is what
    /// the second channel costs.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASecondChannelCostsAPotentialNotAMask()
    {
        const double cell = 0.0005;

        var one = Plates(cell);
        var two = TwoChannelPlates(cell);

        var planOne = GeometryBuilder3D.Memory(one);
        var planTwo = GeometryBuilder3D.Memory(two);

        Assert.Equal(1, planOne.Channels);
        Assert.Equal(2, planTwo.Channels);

        var peakOne = PeakLive(() => GeometryBuilder3D.BuildField(one).Field);
        var peakTwo = PeakLive(() => GeometryBuilder3D.BuildField(two).Field);

        var extra = (peakTwo - peakOne) / planOne.Nodes;

        output.WriteLine(
            $"{planOne.Nodes:N0} nodes: one channel {peakOne / planOne.Nodes:F1} B/node, "
            + $"two {peakTwo / planOne.Nodes:F1}; the second costs {extra:F1} "
            + $"(predicted {(planTwo.PeakBytes - planOne.PeakBytes) / planOne.Nodes:F1})");

        // A kept mask would be 105 and a kept set of cut links 96, so anything past a few
        // tens is one of them back.
        Assert.InRange(extra, 0.0, 30.0);
    }

    /// <summary>
    /// A thin volume is charged its first coarse level at the size it really is, not an eighth.
    /// </summary>
    /// <remarks>
    /// Coarsening halves interval counts, not node counts, so each axis keeps one node more than
    /// half: 32769 by 5 by 5 coarsens to 16385 by 3 by 3, which is 0.18 of the fine grid. At
    /// twenty-seven coefficients a node that is 38.9 bytes a fine node where an eighth is 27.
    /// The expectation is worked out here from the node counts, not asked of the model.
    /// </remarks>
    [Fact]
    public void AThinVolumeIsChargedItsRealFirstCoarseLevel()
    {
        // 32.768 m by 4 mm by 4 mm at 1 mm: 32768 intervals by 4 by 4. A box filling the cross
        // section is 2 mm in half-width, so it is still resolved at the first coarse level and
        // the bottom - 147,465 nodes - leaves the Galerkin hierarchy as the cheaper one.
        var thin = new Geometry3D(
            0.0, 0.0, 0.0, 32.768, 0.004, 0.004, 0.001,
            [Box("rail", 0.0, 0.0, 0.0, 0.010, 0.004, 0.004, 100.0)]);

        var plan = GeometryBuilder3D.Memory(thin);

        Assert.Equal(32769L * 5 * 5, plan.Nodes);
        Assert.True(plan.Galerkin, "a 147,465-node bottom is above the rediscretized limit");

        const double firstLevel = (16385.0 * 3 * 3) / (32769.0 * 5 * 5);
        const double expected = 9 + 96 + 24 + 64 + 20 + (27.0 * 8 * firstLevel);

        output.WriteLine(
            $"first level {firstLevel:F4} of the fine grid: {plan.PeakBytes / plan.Nodes:F2} B/node, "
            + $"against {SolveMemoryModel.VolumeGalerkinPeak:F2} for an eighth");

        Assert.Equal(expected, plan.PeakBytes / plan.Nodes, 9);
        Assert.InRange(expected - SolveMemoryModel.VolumeGalerkinPeak, 11.8, 11.9);
    }

    /// <summary>
    /// The statement picks the hierarchy the solver picks, which is what makes its figure the
    /// right one of the two.
    /// </summary>
    [Fact]
    public void TheStatementAndTheSolverChooseTheSameHierarchy()
    {
        foreach (var (name, geometry) in new[]
        {
            ("slabs", Plates(0.0005)),
            ("sphere", Sphere(0.000625)),
            ("two-channel slabs", TwoChannelPlates(0.0005)),
        })
        {
            var plan = GeometryBuilder3D.Memory(geometry);
            var (_, report) = GeometryBuilder3D.BuildField(geometry);

            output.WriteLine($"{name}: statement {(plan.Galerkin ? "galerkin" : "rediscretized")}, solve {(report.Galerkin ? "galerkin" : "rediscretized")}");

            Assert.Equal(report.Galerkin, plan.Galerkin);
        }
    }

    /// <summary>The plane statement against a plane build.</summary>
    /// <remarks>
    /// <para>
    /// <b>Per node of one build, not a slope between two.</b> The plane's peak is a transient -
    /// the instant the first coarse level holds two copies of its cut links - which lasts a
    /// fraction of a millisecond, so a sample can miss it, and a build that misses it reads
    /// about 135 rather than 143. A slope between two builds turns that into an error either
    /// way: it read 0.78 of the statement on one CI runner. One build at a quarter of a
    /// million nodes, where the fixed cost of a build is a tenth of a byte a node, can only
    /// read low.
    /// </para>
    /// <para>
    /// <b>How low depends on how loaded the machine is, so the lower edge is 0.85.</b> The plane
    /// spends most of a build smoothing its fine level, and its coarse levels exist only during
    /// each cycle's descent. A sampler reads 143.3 to 143.9 when it catches the copy, about 135
    /// when it catches a descent but not the copy, and about 126.6 when it sees only the fine
    /// level and the first coarse mask - which a four-core CI runner did in two builds of three,
    /// reading 0.884. The upper edge, which is what guards against under-billing, is unchanged.
    /// </para>
    /// <para>
    /// The smaller build is kept, and printed, as a control on the fixed cost: it reads the
    /// same per node.
    /// </para>
    /// </remarks>
    [Fact]
    public void APlaneBuildHoldsWhatItsStatementSays()
    {
        var small = Rods(0.0001);
        var large = Rods(0.00005);

        var planSmall = GeometryBuilder.Memory(small);
        var planLarge = GeometryBuilder.Memory(large);

        var peakSmall = PeakLive(() => GeometryBuilder.Build(small).Field);
        var peakLarge = PeakLive(() => GeometryBuilder.Build(large).Field);

        var predicted = planLarge.PeakBytes / planLarge.Nodes;
        var measured = peakLarge / planLarge.Nodes;

        output.WriteLine(
            $"plane {planLarge.Nodes:N0} nodes: predicted {predicted:F1} B/node, measured {measured:F1} "
            + $"({planSmall.Nodes:N0} nodes: {peakSmall / planSmall.Nodes:F1})");

        // 143.3 when a sample catches the transient, 135 when it catches only a descent, 126.6
        // when it sees only the fine level.
        Assert.InRange(measured / predicted, 0.85, 1.02);
    }

    /// <summary>A second plane channel costs one kept potential, not a second geometry.</summary>
    /// <remarks>
    /// The driven plane build kept its first channel's mask for the shared-face check after
    /// the channel loop: 73 bytes a node, through every later channel's solve, which the
    /// statement does not count. Asserted as the volume case is, as the difference between a
    /// two-channel build and the same geometry undriven.
    /// </remarks>
    [Fact]
    public void ASecondPlaneChannelCostsAPotentialNotAMask()
    {
        // 66 k nodes: a kept mask is still 73 bytes a node against a limit of 30, and a quarter
        // of the nodes keeps this to seconds in Debug, where the sampler halves a build's speed.
        const double cell = 0.0001;

        var one = Rods(cell);
        var two = Rods(cell, driven: true);

        var planOne = GeometryBuilder.Memory(one);
        var planTwo = GeometryBuilder.Memory(two);

        Assert.Equal(1, planOne.Channels);
        Assert.Equal(2, planTwo.Channels);

        var peakOne = PeakLive(() => GeometryBuilder.Build(one).Field);
        var peakTwo = PeakLive(() => GeometryBuilder.Build(two).Field);

        var extra = (peakTwo - peakOne) / planOne.Nodes;

        output.WriteLine(
            $"{planOne.Nodes:N0} nodes: one channel {peakOne / planOne.Nodes:F1} B/node, "
            + $"two {peakTwo / planOne.Nodes:F1}; the second costs {extra:F1} "
            + $"(predicted {(planTwo.PeakBytes - planOne.PeakBytes) / planOne.Nodes:F1})");

        // A kept mask is 73; the plane's own transient can be caught in one build and missed
        // in the other, which is worth about 8 either way.
        Assert.InRange(extra, -10.0, 30.0);
    }

    /// <summary>
    /// Four round rods in a 16 mm box; driven, the y pair carries an RF tap as well, which makes
    /// a second spatial pattern.
    /// </summary>
    private static CompiledSolvedField Rods(double cell, bool driven = false)
    {
        var tap = driven ? [new CompiledTap(0, 50.0, 0.0)] : Array.Empty<CompiledTap>();

        return new()
        {
            MinX = -0.008,
            MaxX = 0.008,
            MinY = -0.008,
            MaxY = 0.008,
            CellSize = cell,
            Tolerance = 1e-10,
            Electrodes =
            [
                Disc("xPlus", 0.0045, 0.0, 100.0),
                Disc("xMinus", -0.0045, 0.0, 100.0),
                Disc("yPlus", 0.0, 0.0045, -100.0) with { Taps = tap },
                Disc("yMinus", 0.0, -0.0045, -100.0) with { Taps = tap },
            ],
            Drives = driven ? [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)] : [],
        };
    }

    private (double Predicted, double Measured, bool Galerkin) Slope(
        Func<double, Geometry3D> geometry, double coarse, double fine)
    {
        var small = geometry(coarse);
        var large = geometry(fine);

        var planSmall = GeometryBuilder3D.Memory(small);
        var planLarge = GeometryBuilder3D.Memory(large);

        var peakSmall = PeakLive(() => GeometryBuilder3D.BuildField(small).Field);
        var peakLarge = PeakLive(() => GeometryBuilder3D.BuildField(large).Field);

        var nodes = (double)(planLarge.Nodes - planSmall.Nodes);
        var predicted = (planLarge.PeakBytes - planSmall.PeakBytes) / nodes;
        var measured = (peakLarge - peakSmall) / nodes;

        output.WriteLine(
            $"{planSmall.Nodes:N0} -> {planLarge.Nodes:N0} nodes, {(planLarge.Galerkin ? "galerkin" : "rediscretized")}: "
            + $"predicted {predicted:F1} B/node, measured {measured:F1} "
            + $"(peaks {peakSmall / 1048576.0:F1} and {peakLarge / 1048576.0:F1} MiB)");

        Assert.Equal(planSmall.Galerkin, planLarge.Galerkin);

        return (predicted, measured, planLarge.Galerkin);
    }

    /// <summary>The most a build holds reachable at once, above what was live before it.</summary>
    /// <remarks>
    /// <para>
    /// A forced, blocking, full collection and then the heap size, from a second thread. The
    /// collection is what makes this the live set rather than the heap: without it the reading
    /// includes garbage the collector has not yet bothered to free, which on the Astral ran to
    /// 1.6 times the live peak.
    /// </para>
    /// <para>
    /// <b>A sample can only miss the peak, so this reads low, and a slope between two builds can
    /// read either way</b> - low when the larger build's peak is missed, high when the smaller
    /// one's is. It passed on a sixteen-core workstation and failed on both four-core CI runners
    /// in Release, where a build is eight times faster than in Debug: the sampler slept 2 ms
    /// between collections, which is a timer tick of about 15 ms on Windows, and the smallest
    /// build here finishes in about 10 ms. So the sampler now waits only as long as its last
    /// collection took - the build keeps at least half the time, and a sample lands every few
    /// hundred microseconds on a small heap - and each build is measured three times.
    /// </para>
    /// <para>
    /// <b>The middle of three, not the largest.</b> Sampled this densely a build's peak is
    /// caught nearly every time, and what is left are outliers in both directions: a build
    /// whose samples miss its peak, and, now and then, one that reads about 7 bytes a node
    /// over the statement - once on the Linux CI runner in the Galerkin build, once in WSL in
    /// the two-channel plane, never on demand. That is about one grid-sized array, consistent
    /// with catching the instant a new array exists beside the one it replaces. The largest of
    /// three kept that outlier and failed CI on it; the middle discards a lone outlier either
    /// way, while an array a build really keeps is in all three readings and still fails.
    /// </para>
    /// </remarks>
    private static double PeakLive(Func<object> build)
    {
        static long Live()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);

            return GC.GetTotalMemory(forceFullCollection: false);
        }

        static double Once(Func<object> build)
        {
            var baseline = Live();
            var peak = baseline;
            var stop = 0;

            var sampler = new Thread(() =>
            {
                var clock = Stopwatch.StartNew();

                while (Volatile.Read(ref stop) == 0)
                {
                    var started = clock.Elapsed;

                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: false);

                    peak = Math.Max(peak, GC.GetTotalMemory(forceFullCollection: false));

                    // As long again as the collection took: the build runs at least half the time.
                    var resume = clock.Elapsed + (clock.Elapsed - started);

                    while (clock.Elapsed < resume && Volatile.Read(ref stop) == 0)
                    {
                        Thread.SpinWait(32);
                    }
                }
            })
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
            };

            sampler.Start();

            object? built = null;

            try
            {
                built = build();
            }
            finally
            {
                // Whatever the build did: a sampler left running forces a full collection every
                // few hundred microseconds for the rest of the test process.
                Volatile.Write(ref stop, 1);
                sampler.Join();
            }

            GC.KeepAlive(built);

            return peak - baseline;
        }

        double[] readings = [Once(build), Once(build), Once(build)];
        Array.Sort(readings);

        return readings[1];
    }

    /// <summary>Two 1 mm slabs across a 5 mm gap: the geometry the Galerkin hierarchy exists for.</summary>
    private static Geometry3D Plates(double cell) => new(
        -0.006, -0.006, -0.005, 0.006, 0.006, 0.005, cell,
        [
            Box("lower", -0.004, -0.004, -0.0035, 0.004, 0.004, -0.0025, 100.0),
            Box("upper", -0.004, -0.004, 0.0025, 0.004, 0.004, 0.0035, 0.0),
        ]);

    /// <summary>The same slabs with the upper one driven, which makes a second spatial pattern.</summary>
    private static Geometry3D TwoChannelPlates(double cell) => new(
        -0.006, -0.006, -0.005, 0.006, 0.006, 0.005, cell,
        [
            Box("lower", -0.004, -0.004, -0.0035, 0.004, 0.004, -0.0025, 100.0),
            Box("upper", -0.004, -0.004, 0.0025, 0.004, 0.004, 0.0035, 0.0) with
            {
                Taps = [new CompiledTap(0, 50.0, 0.0)],
            },
        ])
    {
        Drives = [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)],
    };

    /// <summary>A 3 mm sphere in a 20 mm box: coarsens far enough that the cheap hierarchy wins.</summary>
    private static Geometry3D Sphere(double cell) => new(
        -0.010, -0.010, -0.010, 0.010, 0.010, 0.010, cell,
        [
            new CompiledElectrode3D
            {
                Name = "ball",
                Shape = Electrode3DShape.Sphere,
                CentreX = 0.0,
                CentreY = 0.0,
                CentreZ = 0.0,
                Radius = 0.003,
                Potential = 100.0,
            },
        ]);

    private static CompiledElectrode3D Box(
        string name, double x0, double y0, double z0, double x1, double y1, double z1, double v) =>
        new()
        {
            Name = name,
            Shape = Electrode3DShape.Box,
            MinX = x0,
            MinY = y0,
            MinZ = z0,
            MaxX = x1,
            MaxY = y1,
            MaxZ = z1,
            Potential = v,
        };

    private static CompiledElectrode Disc(string name, double x, double y, double volts) => new()
    {
        Name = name,
        Shape = ElectrodeShape.Disc,
        CentreX = x,
        CentreY = y,
        Radius = 0.002,
        Potential = volts,
    };
}
