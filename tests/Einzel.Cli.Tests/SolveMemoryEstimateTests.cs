using Einzel.Commands;
using Einzel.Fields.Solved;
using Einzel.Library;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel estimate</c> bills memory from the solver's own statement of what it holds, not
/// from a constant of its own.
/// </summary>
/// <remarks>
/// <para>
/// The constant was 48 bytes a node for every solve, plane and volume alike, and a volume
/// solve on the Galerkin hierarchy holds 240. The Astral at a 1 mm cell was billed 3.1 GiB and
/// needs 15. What was measured against real builds is in <c>SolveMemoryTests</c>; what is
/// asserted here is that the estimate asks for that statement rather than restating it, and
/// that it counts the channels a driven geometry decomposes into - which it could not, because
/// it copied the geometry without its drives.
/// </para>
/// <para>
/// Uncalibrated, so nothing is solved or flown: the memory is arithmetic on the declaration,
/// and these run in the time it takes to validate a template.
/// </para>
/// </remarks>
public sealed class SolveMemoryEstimateTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-solve-memory", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private EstimateOutcome Estimate(string template)
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, template + ".json");
        File.WriteAllText(path, DeviceTemplates.Read(template));

        return EstimateCommand.Execute(path, calibrate: false);
    }

    /// <summary>
    /// A static volume geometry on the Galerkin hierarchy: 240 bytes a node, with its first coarse
    /// level charged at its real size.
    /// </summary>
    /// <remarks>
    /// The Astral's 17-node axis coarsens to 9, so its first level is 0.1334 of the fine grid
    /// rather than an eighth: 241.8 bytes a node, against 241.3 measured, where the flat eighth
    /// said 240.0.
    /// </remarks>
    [Fact]
    public void AStaticVolumeSolveIsBilledTheGalerkinWorkingSet()
    {
        var outcome = Estimate("astral-3d");
        var element = Assert.Single(outcome.Elements, e => e.Nodes.Count == 3);

        var perNode = element.MemoryMiB * 1024.0 * 1024.0 / element.NodeCount;

        output.WriteLine($"{element.NodeCount:N0} nodes, {element.MemoryMiB:F1} MiB, {perNode:F1} B/node");
        output.WriteLine(outcome.Basis);

        Assert.Equal(GalerkinOn(element.Nodes), perNode, 6);
        Assert.True(perNode > SolveMemoryModel.VolumeGalerkinPeak, "a thin axis makes the first level more than an eighth");
        Assert.Contains("Galerkin multigrid hierarchy", outcome.Basis, StringComparison.Ordinal);

        // The template solves two mirror cross-sections before the volume, and their
        // potentials are kept while the volume solves - so the model's peak is the volume's
        // working set plus what the elements before it retain, not the volume's alone.
        var before = outcome.Elements
            .Where(e => e.Index < element.Index)
            .Sum(e => e.NodeCount * SolveMemoryModel.RetainedPerChannel);

        Assert.True(before > 0.0, "the template's plane solves come first");
        Assert.Equal(element.MemoryMiB + (before / (1024.0 * 1024.0)), outcome.MemoryMiB, 6);
    }

    /// <summary>
    /// Two basis channels: one working set, and one kept potential for the channel solved first.
    /// </summary>
    [Fact]
    public void ADrivenVolumeSolveIsBilledItsChannels()
    {
        var outcome = Estimate("linear-ion-trap-3d");
        var element = Assert.Single(outcome.Elements, e => e.Nodes.Count == 3);

        var perNode = element.MemoryMiB * 1024.0 * 1024.0 / element.NodeCount;

        output.WriteLine($"{element.NodeCount:N0} nodes, {element.MemoryMiB:F1} MiB, {perNode:F1} B/node");

        Assert.Equal(
            GalerkinOn(element.Nodes) + SolveMemoryModel.RetainedPerChannel,
            perNode,
            6);
        Assert.Contains("2 basis channels", outcome.Basis, StringComparison.Ordinal);
    }

    /// <summary>A plane solve is billed the plane statement, which was also a third of the truth.</summary>
    [Fact]
    public void APlaneSolveIsBilledThePlaneWorkingSet()
    {
        var outcome = Estimate("quadrupole");
        var element = Assert.Single(outcome.Elements, e => e.Nodes.Count == 2);

        var perNode = element.MemoryMiB * 1024.0 * 1024.0 / element.NodeCount;

        output.WriteLine($"{element.NodeCount:N0} nodes, {element.MemoryMiB:F3} MiB, {perNode:F1} B/node");

        Assert.Equal(SolveMemoryModel.PlanePeak, perNode, 6);
    }

    /// <summary>
    /// A diffusive mixture is billed its working set once for every species, because each
    /// species steps its own density through its own coefficients and nothing is shared.
    /// </summary>
    /// <remarks>
    /// Measured on a 513 by 129 grid: one species held 248.5 bytes a node, and each species
    /// added 247.6 and then 248.3. Billed once whatever the count, a three-species run was
    /// charged a third of what it holds. The fields here are analytic, so the density is the
    /// whole of the peak and the ratio is exact.
    /// </remarks>
    [Fact]
    public void AMixtureIsBilledOnceForEverySpecies()
    {
        static string Member(string name, double mobility) =>
            $$"""
            { "name": "{{name}}", "massToCharge": { "value": 622, "unit": "Da" }, "chargeNumber": 1,
              "mobility": { "zeroField": { "value": {{mobility}}, "unit": "m^2/(V s)" } },
              "population": 1000000 }
            """;

        var one = EstimateFile(DriftTube(
            ion: "\"ion\": { \"massToCharge\": { \"value\": 622, \"unit\": \"Da\" }, \"chargeNumber\": 1 },",
            mobility: "\"mobility\": { \"zeroField\": { \"value\": 0.05, \"unit\": \"m^2/(V s)\" } },"));

        var three = EstimateFile(DriftTube(
            species: $"\"species\": [ {Member("a", 0.05)}, {Member("b", 0.04)}, {Member("c", 0.03)} ],"));

        output.WriteLine($"one species {one.MemoryMiB:F3} MiB, three {three.MemoryMiB:F3} MiB");
        output.WriteLine(three.Basis);

        Assert.True(one.MemoryMiB > 0.0, "the one-species run is billed nothing");
        Assert.Equal(3.0, three.MemoryMiB / one.MemoryMiB, 9);
        Assert.Contains("3 species", three.Basis, StringComparison.Ordinal);
    }

    /// <summary>
    /// A peak too large for the process is said to be, even when no field is solved and so no
    /// solve's statement is there to carry the sentence.
    /// </summary>
    /// <remarks>
    /// The comparison with the machine was the tail of the solved-field explanation, which an
    /// analytic-only diffusive model never reaches - so a density grid that would not fit was
    /// priced without a word. The available memory is passed in, so the case can be made on any
    /// machine; with plenty available nothing is said, which is the control.
    /// </remarks>
    [Fact]
    public void AnAnalyticDiffusionModelIsComparedWithTheMachine()
    {
        var path = DriftTube(
            ion: "\"ion\": { \"massToCharge\": { \"value\": 622, \"unit\": \"Da\" }, \"chargeNumber\": 1 },",
            mobility: "\"mobility\": { \"zeroField\": { \"value\": 0.05, \"unit\": \"m^2/(V s)\" } },");

        var plenty = EstimateCommand.Execute(path, calibrate: false, availableMemoryBytes: 1L << 40);

        // Nothing is solved, so no solve's statement is there to carry the comparison.
        Assert.DoesNotContain(plenty.Elements, e => e.NodeCount > 0);
        Assert.DoesNotContain("this process may use", plenty.Basis, StringComparison.Ordinal);

        var peak = (long)(plenty.MemoryMiB * 1024.0 * 1024.0);
        var short_ = EstimateCommand.Execute(path, calibrate: false, availableMemoryBytes: peak / 2);

        output.WriteLine(short_.Basis);

        Assert.Contains("this process may use on this machine, so it will not fit here.", short_.Basis, StringComparison.Ordinal);
    }

    /// <summary>A drift tube through an analytic field, stepped as a density.</summary>
    private string DriftTube(string ion = "", string mobility = "", string species = "")
    {
        Directory.CreateDirectory(_root);

        var path = Path.Combine(_root, $"tube-{Guid.NewGuid():N}.json");

        File.WriteAllText(path, $$"""
            {
              "schemaVersion": "0.13",
              "name": "drift-tube",
              {{ion}}{{species}}
              "source": {
                "position": { "value": [4, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 0, "unit": "V" },
                "cloud": { "ions": 1,
                           "transverseSpread": { "value": 0.4, "unit": "mm" },
                           "longitudinalSpread": { "value": 0.4, "unit": "mm" } }
              },
              "detector": {
                "planePoint": { "value": [32, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "diffusion",
                "maximumFlightTime": { "value": 60, "unit": "us" },
                {{mobility}}
                "densityGrid": {
                  "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 32, "unit": "mm" },
                  "minY": { "value": -4, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                  "intervalsX": 64, "intervalsY": 32
                },
                "gas": { "model": "hardSphere", "pressure": { "value": 2, "unit": "mbar" },
                         "mass": { "value": 28.0134, "unit": "Da" },
                         "crossSection": { "value": 250, "unit": "angstrom^2" } }
              },
              "fields": [
                { "type": "uniform", "field": { "value": [500, 0, 0], "unit": "V/m" } }
              ]
            }
            """);

        return path;
    }

    private static EstimateOutcome EstimateFile(string path) => EstimateCommand.Execute(path, calibrate: false);

    /// <summary>
    /// The Galerkin peak for a grid of these node counts, worked out here rather than asked of
    /// the model: everything but the first coarse level, and twenty-seven coefficients on each
    /// node of a grid with one more than half the intervals' nodes on every axis.
    /// </summary>
    private static double GalerkinOn(IReadOnlyList<int> nodes)
    {
        var fine = 1.0;
        var coarse = 1.0;

        foreach (var n in nodes)
        {
            fine *= n;
            coarse *= ((n - 1) / 2) + 1;
        }

        return SolveMemoryModel.VolumeGalerkinPeak - SolveMemoryModel.GalerkinFirstLevel
            + (27.0 * sizeof(double) * coarse / fine);
    }
}
