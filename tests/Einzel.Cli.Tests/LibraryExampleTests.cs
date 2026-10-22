using System.Globalization;

using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Fields.Solved;
using Einzel.Io;
using Einzel.Project;
using Einzel.Transport;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The example on <c>docs/library.md</c> compiles, runs, and is the code on the page.
/// </summary>
/// <remarks>
/// <para>
/// The example lives here, between two marker comments, so the build compiles it and this
/// class runs it; a second test reads the page and requires its <c>csharp</c> block to be the
/// marked region, line for line after removing the common indentation. An example that has
/// drifted from the API fails the build, and one that has drifted from the page fails a test,
/// which no reviewer reading a Markdown diff can promise.
/// </para>
/// <para>
/// No compiler package is involved: the page is checked against this file, and this file is
/// checked by the compiler that builds it.
/// </para>
/// </remarks>
public sealed class LibraryExampleTests(ITestOutputHelper output) : IDisposable
{
    private const string Begin = "// --- docs/library.md example: begin ---";
    private const string End = "// --- docs/library.md example: end ---";

    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    /// <summary>
    /// A 20 mm square at 0.5 mm with a plate above the axis and a rod below it; the plate's and
    /// the rod's potentials, and the drive, substituted.
    /// </summary>
    private static string Model(string plate, string rod, string drive) => $$"""
        {
          "schemaVersion": "0.14",
          "name": "plate and rod",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [-8, 0, 0], "unit": "mm" },
            "direction": { "value": [1, 0, 0] },
            "accelerationPotential": { "value": 10, "unit": "V" }
          },
          "fields": [
            { "type": "solved2d", "solve": {
                {{drive}}
                "minX": { "value": -10, "unit": "mm" }, "maxX": { "value": 10, "unit": "mm" },
                "minY": { "value": -10, "unit": "mm" }, "maxY": { "value": 10, "unit": "mm" },
                "cellSize": { "value": 0.5, "unit": "mm" },
                "electrodes": [
                  { "name": "plate", "shape": "rectangle",
                    "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                    "minY": { "value": 2, "unit": "mm" }, "maxY": { "value": 3, "unit": "mm" },
                    {{plate}} },
                  { "name": "rod", "shape": "disc",
                    "centreX": { "value": 0, "unit": "mm" }, "centreY": { "value": -4, "unit": "mm" },
                    "radius": { "value": 1, "unit": "mm" },
                    {{rod}} }
                ] } }
          ],
          "detector": { "planePoint": { "value": [8, 0, 0], "unit": "mm" }, "normal": { "value": [-1, 0, 0] } },
          "transport": { "mode": "trajectory", "maximumFlightTime": { "value": 20, "unit": "us" } }
        }
        """;

    private static string Volts(double value) =>
        string.Create(CultureInfo.InvariantCulture, $$"""{ "value": {{value}}, "unit": "V" }""");

    private string Write(string name, string text)
    {
        var directory = Path.Combine(_root, "models");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, name);
        File.WriteAllText(path, text);
        return path;
    }

    /// <summary>
    /// The example runs: one driven model solved once and 64 ions flown against it in
    /// parallel, then one ion against its own drive made from two basis models.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The per-ion drive is the driven model's own (plate 30 V; rod -20 V with 50 V at a
    /// quarter cycle), so the last flight has to land where the first of the 64 did: the two
    /// halves of the page describe one instrument two ways. The solved fields agree to about
    /// 3e-10 V (<c>DrivenSolvedFieldFactoryTests</c>), so the flights agree far inside the
    /// bounds asserted here.
    /// </para>
    /// <para>
    /// Mutations: building the per-ion field with the RF phase dropped, or with the rod's RF on
    /// the plate, moves the last flight's end by far more than the bound and fails here; a
    /// <c>FieldCache</c> not handed to the model's build leaves the project's cache without the
    /// model's channels and fails here.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheExampleSolvesOnceAndFliesMany()
    {
        var projectRoot = _root;
        var modelPath = Write("plate-and-rod.json", Model(
            $"\"potential\": {Volts(30.0)}",
            $"\"potential\": {Volts(-20.0)}, \"driveAmplitude\": {Volts(50.0)}, "
            + "\"drivePhase\": { \"value\": 0.25, \"unit\": \"1\" }",
            """ "drive": { "frequency": { "value": 1.1, "unit": "MHz" }, "waveform": "sinusoid" }, """));
        string[] basisPaths =
        [
            Write("basis-plate.json", Model($"\"potential\": {Volts(1.0)}", $"\"potential\": {Volts(0.0)}", "")),
            Write("basis-rod.json", Model($"\"potential\": {Volts(0.0)}", $"\"potential\": {Volts(1.0)}", "")),
        ];

        // --- docs/library.md example: begin ---
        // Load a model: validate it, with the files it names resolved against its directory,
        // and refuse it with the first error.
        static CompiledModel Load(string path)
        {
            var validation = ModelValidator.Validate(
                ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(Path.GetFullPath(path)));
            if (!validation.IsValid) throw new InvalidOperationException(validation.Errors[0].Constraint);
            return validation.Model!;
        }

        // Solve once: build the model's field. With a FieldCache the solved channels are kept
        // in the project's .einzel/cache, and the next build loads them.
        var cache = new FieldCache(new ProjectLayout(projectRoot));
        var model = Load(modelPath);
        var (field, warnings) = FieldAssembly.BuildReported(model, cache);

        // Fly many: the field is immutable, so every ion samples it from its own thread.
        var species = IonSpecies.FromModel(model);
        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };
        var (plane, normal) = (model.DetectorPoint, model.DetectorNormal);
        TrajectoryStopFunction detector = (in PhaseState s) => Vec3.Dot(s.Position - plane, normal);
        var launches = Enumerable.Range(0, 64).Select(i => new PhaseState(
            model.SourcePosition + new Vec3(0, (i - 32) * 1e-5, 0),
            model.SourceDirection * model.LaunchSpeedSi())).ToArray();
        var results = new TrajectoryResult[launches.Length];
        Parallel.For(0, launches.Length, i =>
            results[i] = TrajectoryIntegrator.Integrate(launches[i], species, field, settings, detector));

        // A drive per ion: static basis models (one electrode at 1 V, the rest at 0 V), solved
        // once, weighted with this ion's DC, RF amplitude and RF phase in cycles.
        var bases = basisPaths.Select(p => FieldAssembly.BuildReported(Load(p), cache).Field).ToArray();
        var perIon = DrivenSolvedField.Create(
            bases,
            direct: [30.0, -20.0],
            harmonics: [[new WeightTerm(0, 0.0, 0.25)], [new WeightTerm(0, 50.0, 0.25)]],
            frequenciesHz: [1.1e6],
            waveforms: [new RfWaveform.Sinusoid()]);
        var ion = TrajectoryIntegrator.Integrate(launches[32], species, perIon, settings, detector);
        // --- docs/library.md example: end ---

        Assert.Empty(warnings);
        Assert.All(results, r => Assert.True(r.FlightTimeSeconds > 0.0));
        // Three entries: the model's two channels (its DC pattern, plate and rod together, and
        // the rod's RF at 1 V) and the plate basis. The rod basis is the same geometry with the
        // same unit pattern as the model's RF channel, so it has that channel's hash and loads
        // its entry; only the plate basis is a separate solve.
        var kept = FieldCache.Status(new ProjectLayout(projectRoot)).Entries;
        Assert.Equal(3, kept);

        var reference = results[32];
        var apart = (ion.FinalState.Position - reference.FinalState.Position).Length;

        output.WriteLine(
            $"64 ions: {string.Join(", ", results.GroupBy(r => r.Outcome).Select(g => $"{g.Count()} {g.Key}"))}; "
            + $"ion 32 {reference.Outcome} at {reference.FlightTimeSeconds * 1e6:F6} us, its own drive "
            + $"{ion.Outcome} at {ion.FlightTimeSeconds * 1e6:F6} us, {apart * 1e6:E2} um apart");

        Assert.Equal(reference.Outcome, ion.Outcome);
        Assert.True(apart < 1e-7, $"the per-ion flight ends {apart * 1e6:E2} um from the model's");
        Assert.Equal(reference.FlightTimeSeconds, ion.FlightTimeSeconds, reference.FlightTimeSeconds * 1e-6);
    }

    /// <summary>
    /// The <c>csharp</c> block on <c>docs/library.md</c> is the marked region of this file.
    /// </summary>
    /// <remarks>
    /// Mutation: one character changed in the page's block fails here, as does one changed in
    /// the region; the control is that the region is found and not empty, so a marker that was
    /// renamed cannot make two empty lists agree.
    /// </remarks>
    [Fact]
    public void ThePageShowsTheCodeThisClassRuns()
    {
        var root = RepositoryRoot();
        var source = File.ReadAllLines(Path.Combine(root, "tests", "Einzel.Cli.Tests", "LibraryExampleTests.cs"));
        var page = File.ReadAllLines(Path.Combine(root, "docs", "library.md"));

        var begin = Array.FindIndex(source, l => l.Trim() == Begin);
        var end = Array.FindIndex(source, l => l.Trim() == End);
        Assert.True(begin >= 0 && end > begin + 10, "the example's markers are not where they were");

        var fence = Array.FindIndex(page, l => l.Trim() == "```csharp");
        Assert.True(fence >= 0, "docs/library.md has no csharp block");
        var close = Array.FindIndex(page, fence + 1, l => l.Trim() == "```");
        Assert.True(close > fence, "docs/library.md's csharp block is not closed");

        var region = Dedent(source[(begin + 1)..end]);
        var shown = Dedent(page[(fence + 1)..close]);

        for (var i = 0; i < Math.Min(region.Length, shown.Length); i++)
        {
            Assert.True(
                region[i] == shown[i],
                $"line {i + 1} of the example differs:\n  test: {region[i]}\n  page: {shown[i]}");
        }

        Assert.Equal(region.Length, shown.Length);
    }

    /// <summary>The lines with their common leading whitespace and their trailing whitespace removed.</summary>
    private static string[] Dedent(string[] lines)
    {
        var indent = lines
            .Where(l => l.Trim().Length > 0)
            .Min(l => l.Length - l.TrimStart().Length);

        return [.. lines.Select(l => l.Trim().Length == 0 ? "" : l[indent..].TrimEnd())];
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SPEC.md")))
            {
                return directory.FullName;
            }
        }

        throw new FileNotFoundException("SPEC.md is not above the test binary");
    }
}
