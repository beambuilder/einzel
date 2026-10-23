using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Io;
using Einzel.Project;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel fly</c>: what it writes, what it records, what it refuses, and that the files it
/// leaves behind are read by <c>verify</c> and <c>report</c> as what they are.
/// </summary>
public sealed class FlyCommandTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("einzel-fly-").FullName;

    private const string Beam = """
        {
          "schemaVersion": "0.1",
          "maxParallelism": 3,
          "ions": [
            { "id": 40, "rfPhase": 0.25, "tag": "late" },
            { "id": 41, "position": { "value": [0.1, -0.2, 0], "unit": "mm" }, "tag": "off" }
          ],
          "ionSets": [
            { "cloud": { "ions": 10, "seed": 3, "temperature": { "value": 300, "unit": "K" },
                         "transverseSpread": { "value": 0.3, "unit": "mm" } }, "tag": "cloud" }
          ]
        }
        """;

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            return (Program.Main(args), stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private string Write(string relative, string text)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        return path;
    }

    private (int ExitCode, string Stdout, string Stderr) Fly(string model, string ions, params string[] more) =>
        Run([
            "fly", Write("models/quad.json", model), "--ions", Write($"ions/{ions}", Beam),
            "--project", _root, "--json", .. more,
        ]);

    private string Results(string name) => Path.Combine(_root, "results", name);

    /// <summary>
    /// A list flies, and <c>--json</c> prints the stored result byte for byte; the manifest
    /// records the list's hash, its seeds and the parallelism; the per-ion CSV is round-trip
    /// formatted; and the expanded <c>.ions.csv</c> flown again gives the same rows.
    /// </summary>
    [Fact]
    public void AListFliesAndItsExpandedRowsFlyTheSameAgain()
    {
        var (exit, stdout, stderr) = Fly(IonListFlightTests.Quad, "beam.ions.json");
        Assert.True(exit == 0, stdout + stderr);
        Assert.Contains("flew 12 ions", stderr, StringComparison.Ordinal);

        Assert.Equal(File.ReadAllText(Results("quad.beam.fly.json")), stdout);

        var fly = JsonNode.Parse(stdout)!;
        Assert.Equal(12, (int)fly["launched"]!);
        Assert.Equal(3, (int)fly["maxParallelism"]!);
        Assert.Equal(
            ["results/quad.beam.fly.manifest.json", "results/quad.beam.fly.json", "results/quad.beam.fly.csv", "results/quad.beam.fly.ions.csv"],
            fly["artifacts"]!.AsArray().Select(a => ((string)a!).Replace('\\', '/')));

        var manifest = RunManifest.FromJson(File.ReadAllText(Results("quad.beam.fly.manifest.json")))!;
        Assert.Equal(ContentHash.OfFile(Path.Combine(_root, "ions", "beam.ions.json")), manifest.InputHashes!["ions/beam.ions.json"]);
        Assert.Equal([3L], manifest.Seeds);
        Assert.Equal(3, manifest.MaxParallelism);

        var ions = fly["ions"]!.AsArray();
        Assert.Equal([40, 41, 42, 43], ions.Take(4).Select(i => (int)i!["id"]!));
        Assert.Equal("late", (string)ions[0]!["tag"]!);

        var csv = File.ReadAllLines(Results("quad.beam.fly.csv"));
        Assert.Equal("id,tag,outcome,surface,x_mm,y_mm,z_mm,time_us,steps,rejected,collisions,warnings", csv[0]);
        Assert.Equal(13, csv.Length);

        var first = csv[1].Split(',');
        Assert.Equal("40", first[0]);
        Assert.Equal(((double)ions[0]!["yMm"]!).ToString("R", CultureInfo.InvariantCulture), first[5]);
        Assert.Equal(((double)ions[0]!["timeUs"]!).ToString("R", CultureInfo.InvariantCulture), first[7]);
        Assert.Equal((string)ions[0]!["steps"]!.ToString(), first[8]);

        // The anchor: the expanded rows reproduce the flight.
        var again = Run(
            "fly", Path.Combine(_root, "models", "quad.json"), "--ions", Results("quad.beam.fly.ions.csv"),
            "--project", _root, "--json");
        Assert.Equal(0, again.ExitCode);
        Assert.Equal(ions.ToJsonString(), JsonNode.Parse(again.Stdout)!["ions"]!.ToJsonString());

        var reflown = RunManifest.FromJson(File.ReadAllText(Results("quad.quad.beam.fly.fly.manifest.json")))!;
        Assert.Empty(reflown.Seeds);
        Assert.Equal(Environment.ProcessorCount, reflown.MaxParallelism);
    }

    /// <summary>
    /// A model with a gas records the gas seed after the list's, and each row its collisions.
    /// At 0.05 mbar the trajectory description is outside its validity, so <c>fly</c> warns
    /// as <c>run</c> does (REG-2) and exits 2.
    /// </summary>
    [Fact]
    public void AGasSeedIsRecordedAfterTheListsSeeds()
    {
        var (exit, stdout, stderr) = Fly(GasQuad(), "beam.ions.json");
        Assert.True(exit == 2, $"exit {exit}: {stderr}");

        var manifest = RunManifest.FromJson(File.ReadAllText(Results("quad.beam.fly.manifest.json")))!;
        Assert.Equal([3L, 20_240_101L], manifest.Seeds);

        var fly = JsonNode.Parse(stdout)!;
        var regime = Assert.Single(fly["warnings"]!.AsArray(), w => (string)w!["code"]! == "regime.trajectory-above-validity")!;
        Assert.Equal("ValidityViolation", (string)regime["severity"]!);
        Assert.All(fly["ions"]!.AsArray(), ion => Assert.True((int)ion!["collisions"]! > 0));

        var csv = File.ReadAllLines(Results("quad.beam.fly.csv")).Skip(1).Select(line => line.Split(','));
        Assert.Equal(fly["ions"]!.AsArray().Select(ion => ion!["collisions"]!.ToString()), csv.Select(cells => cells[10]));
    }

    /// <summary>
    /// A row's collision warnings are its own: a gas whose pressure field does not reach the
    /// rows' path warns on every row that it extrapolated, and the field file is an input.
    /// </summary>
    [Fact]
    public void ARowsCollisionWarningsAreItsOwn()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        File.Copy(
            Path.Combine(examples, "drift-tube-pressure-gradient.pressure.vti"),
            Path.Combine(Directory.CreateDirectory(Path.Combine(_root, "models")).FullName, "p.vti"));

        var model = IonListFlightTests.Quad.Replace(
            "\"transport\": {",
            """
            "transport": { "gas": { "model": "hardSphere", "pressure": { "value": 0.001, "unit": "mbar" },
              "mass": { "value": 28.0134, "unit": "Da" }, "crossSection": { "value": 250, "unit": "Å^2" },
              "pressureField": { "path": "p.vti", "array": "pressure", "unit": "mbar" } },
            """,
            StringComparison.Ordinal);

        var (_, stdout, stderr) = Fly(model, "beam.ions.json");
        var fly = JsonNode.Parse(stdout)!;
        output.WriteLine(stderr);

        Assert.All(fly["ions"]!.AsArray(), ion => Assert.Contains(
            "gas.pressure-extrapolated", ion!["warnings"]!.AsArray().Select(w => (string)w!)));
        Assert.Contains("models/p.vti", RunManifest.FromJson(File.ReadAllText(Results("quad.beam.fly.manifest.json")))!.InputHashes!.Keys);
    }

    private static string GasQuad() => IonListFlightTests.Quad.Replace(
        "\"transport\": {",
        """
        "transport": { "gas": { "model": "langevin", "pressure": { "value": 0.05, "unit": "mbar" },
          "temperature": { "value": 300, "unit": "K" }, "mass": { "value": 28.0134, "unit": "Da" },
          "polarizability": { "value": 1.7403e-30, "unit": "m^3" } },
        """,
        StringComparison.Ordinal);

    /// <summary>
    /// What <c>fly</c> cannot fly one row at a time is refused at the declaration that makes it
    /// so, as a validation failure, and nothing is written.
    /// </summary>
    [Theory]
    [InlineData("spaceCharge", "/transport/spaceCharge")]
    [InlineData("diffusion", "/transport/mode")]
    [InlineData("sequenced", "/sequence")]
    [InlineData("mixture", "/species")]
    [InlineData("cloud", "/source/cloud")]
    public void WhatFlyCannotFlyIsRefusedByItsDeclaration(string kind, string declaration)
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        var diffusive = JsonNode.Parse(File.ReadAllText(Path.Combine(examples, "drift-tube-diffusion.json")))!;
        var cloud = """
            "accelerationPotential": { "value": 2, "unit": "V" },
            "cloud": { "ions": 8, "seed": 1, "transverseSpread": { "value": 0.1, "unit": "mm" } }
            """;

        var model = kind switch
        {
            "spaceCharge" => IonListFlightTests.Quad
                .Replace("\"accelerationPotential\": { \"value\": 2, \"unit\": \"V\" }", cloud, StringComparison.Ordinal)
                .Replace("\"transport\": {", "\"transport\": { \"spaceCharge\": \"direct\",", StringComparison.Ordinal),
            "cloud" => IonListFlightTests.Quad
                .Replace("\"accelerationPotential\": { \"value\": 2, \"unit\": \"V\" }", cloud, StringComparison.Ordinal),
            "diffusion" => diffusive.ToJsonString(),
            "sequenced" => SequencedRunTests.Model,
            _ => Mixture(diffusive),
        };

        var (exit, stdout, stderr) = Fly(model, "beam.ions.json");

        Assert.True(exit == 1, $"{kind}: exit {exit}: {stdout}{stderr}");
        var errors = JsonNode.Parse(stdout)!["errors"]!.AsArray();
        Assert.Contains(errors, e => (string)e!["path"]! == declaration);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));
        output.WriteLine($"{kind,-12} {string.Join(" | ", errors.Select(e => $"{e!["path"]}: {e["constraint"]}"))}");
    }

    /// <summary>
    /// A sequence that is trajectory throughout flies, and a row born later is shifted into its
    /// timeline: in <c>sequenced-uniform</c> (a 2 us hold, then a push) a row born at 1 us waits
    /// out the rest of the hold and arrives with the row born at 0, and one born at 3 us, inside
    /// the push, flies the 3.219 us traverse from rest alone.
    /// </summary>
    [Fact]
    public void ATrajectorySequenceFliesAndABirthShiftsTheRowIntoIt()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        var model = Write("models/staged.json", File.ReadAllText(Path.Combine(examples, "sequenced-uniform.json")));
        var list = Write("ions/staged.ions.json", """
            { "schemaVersion": "0.1", "ions": [
              { "id": 1 }, { "id": 2, "birthTime": { "value": 1, "unit": "us" } }, { "id": 3, "birthTime": { "value": 3, "unit": "us" } } ] }
            """);

        var (exit, stdout, stderr) = Run("fly", model, "--ions", list, "--project", _root, "--json");
        Assert.True(exit == 0, stdout + stderr);

        var times = JsonNode.Parse(stdout)!["ions"]!.AsArray().Select(ion => (double)ion!["timeUs"]!).ToList();
        output.WriteLine(string.Join(", ", times));
        Assert.Equal(5.219358580, times[0], 1e-6);
        Assert.Equal(times[0], times[1] + 1.0, 1e-9);
        Assert.Equal(3.219358580, times[2], 1e-6);
    }

    private static string Mixture(JsonNode diffusive)
    {
        var mixture = diffusive.DeepClone().AsObject();
        var ion = mixture["ion"]!.DeepClone();
        mixture.Remove("ion");
        mixture["transport"]!.AsObject().Remove("mobility");
        mixture["source"]?["cloud"]?.AsObject().Remove("population");
        mixture["species"] = new JsonArray(
            new JsonObject
            {
                ["name"] = "fast",
                ["massToCharge"] = ion["massToCharge"]!.DeepClone(),
                ["chargeNumber"] = 1,
                ["mobility"] = JsonNode.Parse("""{ "zeroField": { "value": 0.05, "unit": "m^2/(V s)" } }"""),
                ["population"] = 1000,
            },
            new JsonObject
            {
                ["name"] = "slow",
                ["massToCharge"] = ion["massToCharge"]!.DeepClone(),
                ["chargeNumber"] = 1,
                ["mobility"] = JsonNode.Parse("""{ "zeroField": { "value": 0.025, "unit": "m^2/(V s)" } }"""),
                ["population"] = 1000,
            });
        return mixture.ToJsonString();
    }

    /// <summary>
    /// A row born far beyond the model's clock - more than 1000 maximum flight times - is
    /// refused at its row, and one born inside that bound flies.
    /// </summary>
    [Fact]
    public void ABirthBeyondTheModelsClockIsRefusedAtItsRow()
    {
        var model = Write("models/quad.json", IonListFlightTests.Quad);
        var late = Write("ions/late.ions.json", """
            { "schemaVersion": "0.1", "ions": [
              { "id": 1 },
              { "id": 2, "birthTime": { "value": 40, "unit": "ms" } },
              { "id": 3, "birthTime": { "value": 1, "unit": "s" } } ] }
            """);

        var (exit, stdout, stderr) = Run("fly", model, "--ions", late, "--project", _root);

        Assert.True(exit == 1, stdout + stderr);
        Assert.Contains("1 problem(s) in " + Path.GetFullPath(late), stderr, StringComparison.Ordinal);
        Assert.Contains("/ions/2/birthTime", stderr, StringComparison.Ordinal);
        Assert.Contains("VALUE_OUT_OF_BOUNDS", stderr, StringComparison.Ordinal);

        File.WriteAllText(late, """
            { "schemaVersion": "0.1", "ions": [ { "id": 1 }, { "id": 2, "birthTime": { "value": 40, "unit": "ms" } } ] }
            """);
        Assert.Equal(0, Run("fly", model, "--ions", late, "--project", _root).ExitCode);
    }

    /// <summary>A mistake in the list is reported against the list, by its pointer.</summary>
    [Fact]
    public void AListMistakeIsReportedAgainstTheList()
    {
        var model = Write("models/quad.json", IonListFlightTests.Quad);
        var list = Write("ions/twice.ions.json", """{ "schemaVersion": "0.1", "ions": [ { "id": 1 }, { "id": 1 } ] }""");

        var (exit, _, stderr) = Run("fly", model, "--ions", list, "--project", _root);

        Assert.Equal(1, exit);
        Assert.Contains("problem(s) in " + Path.GetFullPath(list), stderr, StringComparison.Ordinal);
        Assert.Contains("/ions/1/id", stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>verify</c> reads a fly's manifest as any other, so an ion list edited after the
    /// flight makes the result stale with no change to <c>verify</c>.
    /// </summary>
    [Fact]
    public void VerifyNoticesAnEditedIonList()
    {
        Assert.Equal(0, Fly(IonListFlightTests.Quad, "beam.ions.json").ExitCode);

        var before = Assert.Single(VerifyCommand.Execute(_root).Results);
        Assert.True(before.Current, string.Join("; ", before.Drift));

        File.AppendAllText(Path.Combine(_root, "ions", "beam.ions.json"), "\n");

        var after = Assert.Single(VerifyCommand.Execute(_root).Results);
        Assert.Contains("input file has changed: 'ions/beam.ions.json'", after.Drift);
    }

    /// <summary><c>--dry-run</c> says what would fly and writes nothing.</summary>
    [Fact]
    public void ADryRunSaysWhatWouldFlyAndWritesNothing()
    {
        var (exit, stdout, stderr) = Fly(IonListFlightTests.Quad, "beam.ions.json", "--dry-run");
        Assert.True(exit == 0, stdout + stderr);

        var fly = JsonNode.Parse(stdout)!;
        Assert.True((bool)fly["dryRun"]!);
        Assert.Equal(12, (int)fly["launched"]!);
        Assert.Empty(fly["ions"]!.AsArray());
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".einzel")));

        var text = Run(
            "fly", Path.Combine(_root, "models", "quad.json"), "--ions", Path.Combine(_root, "ions", "beam.ions.json"),
            "--project", _root, "--dry-run");
        Assert.Contains("would fly 12 ion(s)", text.Stdout, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));
    }

    /// <summary>
    /// <c>report</c> names a fly's answer as stored and not drawn, the state a study's has,
    /// rather than as an unreadable run result.
    /// </summary>
    [Fact]
    public void ReportNamesAFlyAsNotRendered()
    {
        Assert.Equal(0, Fly(IonListFlightTests.Quad, "beam.ions.json").ExitCode);

        var (exit, stdout, stderr) = Run("report", _root, "--json");
        Assert.True(exit == 0, stdout + stderr);

        var report = JsonDocument.Parse(stdout).RootElement;
        var run = Assert.Single(report.GetProperty("runs").EnumerateArray());
        Assert.Equal(1, report.GetProperty("notRendered").GetInt32());
        Assert.False(run.TryGetProperty("unreadable", out _));
        Assert.Contains("ion-list flight", run.GetProperty("notRendered").GetString()!, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>--vtu</c> writes one polyline per ion, in list order, with each ion's id as cell
    /// data, every line starting at its ion's launch.
    /// </summary>
    [Fact]
    public void TheTrajectoriesAreOnePolylinePerIonWithItsId()
    {
        var model = Write("models/quad.json", IonListFlightTests.Quad);
        var list = Write("ions/three.ions.json", """
            { "schemaVersion": "0.1", "ions": [
              { "id": 5 },
              { "id": 9, "position": { "value": [0.5, 0, 0], "unit": "mm" } },
              { "id": 2, "position": { "value": [0, -0.4, 0], "unit": "mm" }, "rfPhase": 0.5 } ] }
            """);

        var (exit, stdout, stderr) = Run("fly", model, "--ions", list, "--project", _root, "--vtu");
        Assert.True(exit == 0, stdout + stderr);

        var path = Path.Combine(_root, ".einzel", "quad.three.fly.trajectories.vtu");
        var vtu = File.ReadAllText(path);
        Assert.Contains("NumberOfCells=\"3\"", vtu, StringComparison.Ordinal);
        Assert.Contains("complete: no path reached the sample capacity", vtu, StringComparison.Ordinal);
        Assert.Contains("wrote .einzel", stdout.Replace('\\', '/'), StringComparison.Ordinal);

        string Array(string name) => Regex.Match(
            vtu, $"Name=\"{name}\"[^>]*>\\s*([^<]*)<", RegexOptions.Singleline).Groups[1].Value.Trim();

        Assert.Equal("5 9 2", Array("id"));
        Assert.Equal("0 0 0", Array("outcome"));

        var ends = Array("offsets").Split(' ').Select(int.Parse).ToList();
        var counts = ends.Select((end, k) => end - (k == 0 ? 0 : ends[k - 1])).ToList();
        Assert.All(counts, count => Assert.True(count > 1, $"a line of {count} point(s)"));

        var xyz = Regex.Match(vtu, "<Points>\\s*<DataArray[^>]*>\\s*([^<]*)<", RegexOptions.Singleline)
            .Groups[1].Value.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(ends[^1], xyz.Length);

        // Each line starts at its own ion's launch, in list order.
        double[] At(int point) => [.. xyz[point].Split(' ').Select(v => double.Parse(v, CultureInfo.InvariantCulture))];
        Assert.Equal([3e-4, 2e-4, 0.0], At(0), (a, b) => Math.Abs(a - b) < 1e-15);
        Assert.Equal([5e-4, 0.0, 0.0], At(ends[0]), (a, b) => Math.Abs(a - b) < 1e-15);
        Assert.Equal([0.0, -4e-4, 0.0], At(ends[1]), (a, b) => Math.Abs(a - b) < 1e-15);
        output.WriteLine($"3 lines of {string.Join(", ", counts)} points");

        // A later flight without --vtu leaves no path that is not its own.
        Assert.Equal(0, Run("fly", model, "--ions", list, "--project", _root).ExitCode);
        Assert.False(File.Exists(path));
    }

    /// <summary>
    /// With the ladder each row reports its finest rung and its half-width, and the paths are
    /// the first rung's: every line starts at time 0, its ion's birth, and ends at the flight
    /// time the same row reports without the ladder.
    /// </summary>
    [Fact]
    public void WithTheLadderTheRowsCarryAnIntervalAndThePathsAreTheFirstRung()
    {
        var single = JsonNode.Parse(Fly(IonListFlightTests.Quad, "beam.ions.json").Stdout)!["ions"]!.AsArray();
        var (exit, stdout, stderr) = Fly(IonListFlightTests.Quad, "beam.ions.json", "--ladder", "--vtu");
        Assert.True(exit == 0, stdout + stderr);

        var laddered = JsonNode.Parse(stdout)!["ions"]!.AsArray();
        Assert.All(laddered, ion => Assert.True((double?)ion!["timeHalfWidthUs"] > 0.0));
        Assert.All(single, ion => Assert.Null(ion!["timeHalfWidthUs"]));

        var vtu = File.ReadAllText(Path.Combine(_root, ".einzel", "quad.beam.fly.trajectories.vtu"));
        string Array(string name) => Regex.Match(
            vtu, $"Name=\"{name}\"[^>]*>\\s*([^<]*)<", RegexOptions.Singleline).Groups[1].Value.Trim();

        var ends = Array("offsets").Split(' ').Select(int.Parse).ToList();
        var times = Array("time_us").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToList();

        Assert.Equal(single.Count, ends.Count);
        Assert.Contains("the ladder's first rung", vtu, StringComparison.Ordinal);

        for (var k = 0; k < ends.Count; k++)
        {
            Assert.Equal(0.0, times[k == 0 ? 0 : ends[k - 1]]);
            Assert.Equal((double)single[k]!["timeUs"]!, times[ends[k] - 1]);
        }
    }

    /// <summary>
    /// Ions that strike an electrode are results, not failures: on the shipped slit, without its
    /// cloud, some rows stop on the slit plates and some reach the detector, the counts per
    /// surface are the rows', the exit code is 0, and a dry run afterwards finds the solved
    /// channel in the cache. With the ladder a struck row carries two warnings, joined in the
    /// CSV by a semicolon.
    /// </summary>
    [Fact]
    public void IonsOnAnElectrodeAreDataAndTheCacheIsCountedByADryRun()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        var slit = JsonNode.Parse(File.ReadAllText(Path.Combine(examples, "slit-transmission.json")))!;
        var cloud = slit["source"]!["cloud"]!.DeepClone().AsObject();
        slit["source"]!.AsObject().Remove("cloud");
        cloud.Remove("population");
        cloud["ions"] = 24;

        var model = Write("models/slit.json", slit.ToJsonString());
        var list = Write("ions/slit.ions.json", new JsonObject
        {
            ["schemaVersion"] = "0.1",
            ["ionSets"] = new JsonArray(new JsonObject { ["cloud"] = cloud }),
        }.ToJsonString());

        var (exit, stdout, stderr) = Run("fly", model, "--ions", list, "--project", _root, "--json", "--ladder");
        Assert.True(exit == 0, stdout + stderr);

        var fly = JsonNode.Parse(stdout)!;
        var ions = fly["ions"]!.AsArray();
        var struck = ions.Where(ion => (string)ion!["outcome"]! == "StruckElectrode").ToList();

        Assert.NotEmpty(struck);
        Assert.True(struck.Count < ions.Count);
        Assert.Equal(struck.Count, (int)fly["outcomes"]!["StruckElectrode"]!);
        Assert.Equal(
            ions.Where(ion => ion!["surface"] is not null).GroupBy(ion => (string)ion!["surface"]!)
                .ToDictionary(g => g.Key, g => g.Count()),
            fly["surfaces"]!.AsObject().ToDictionary(p => p.Key, p => (int)p.Value!));

        var csv = File.ReadAllLines(Results("slit.slit.fly.csv")).Skip(1).Select(line => line.Split(',')).ToList();
        Assert.All(csv, cells => Assert.Equal(12, cells.Length));
        Assert.Equal(ions.Select(ion => string.Join(';', ion!["warnings"]!.AsArray().Select(w => (string)w!))), csv.Select(cells => cells[11]));
        Assert.Contains(ions, ion => ion!["warnings"]!.AsArray().Count > 1);

        var dry = JsonNode.Parse(Run("fly", model, "--ions", list, "--project", _root, "--json", "--dry-run").Stdout)!;
        Assert.True((int)dry["channels"]! > 0);
        Assert.Equal((int)dry["channels"]!, (int)dry["cachedChannels"]!);
    }

    /// <summary>
    /// Until each override group flies through its own field, a list with overrides is refused
    /// at the first set's overrides rather than flown through the model as written, and
    /// nothing is written.
    /// </summary>
    [Fact]
    public void AListWithOverridesIsRefusedUntilGroupsFly()
    {
        var model = Write("models/quad.json", IonListFlightTests.Quad.Replace(
            "\"name\": \"quad\",", "\"name\": \"quad\", \"parameters\": { \"V\": { \"value\": 100, \"unit\": \"V\" } },",
            StringComparison.Ordinal));
        var list = Write("ions/over.ions.json", """
            { "schemaVersion": "0.1", "ions": [ { "id": 1, "overrides": { "V": { "value": 120, "unit": "V" } } }, { "id": 2 } ] }
            """);

        var (exit, stdout, stderr) = Run("fly", model, "--ions", list, "--project", _root, "--json");

        Assert.True(exit == 1, stdout + stderr);
        var error = JsonNode.Parse(stdout)!["errors"]![0]!;
        Assert.Equal("/ions/0/overrides", (string)error["path"]!);
        Assert.Contains("not flown yet", (string)error["constraint"]!, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));
    }

    /// <summary>
    /// A row that throws is a defect in the platform, not an outcome: an ion launched on the
    /// axis of a quadro-logarithmic field, where its potential has no value, ends the flight
    /// with exit 6, the exception's type and its stack, and nothing written.
    /// </summary>
    [Fact]
    public void ARowThatThrowsIsADefectAndExitsSix()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        var model = Write("models/trap.json", File.ReadAllText(Path.Combine(examples, "orbital-trap-frequency.json")));
        var list = Write("ions/axis.ions.json", """
            { "schemaVersion": "0.1", "ions": [ { "id": 1 }, { "id": 2, "position": { "value": [1, 0, 0], "unit": "mm" } } ] }
            """);

        var (exit, _, stderr) = Run("fly", model, "--ions", list, "--project", _root);

        Assert.True(exit == 6, $"exit {exit}: {stderr}");
        Assert.Contains("ArgumentOutOfRangeException", stderr, StringComparison.Ordinal);
        Assert.Contains("QuadroLogarithmicField", stderr, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));
    }

    /// <summary>
    /// Two lists with one stem write the same files; the second flight says on stderr that it
    /// replaces a flight of another list.
    /// </summary>
    [Fact]
    public void AFlightOfAnotherListWithTheSameStemSaysItReplacesIt()
    {
        Assert.Equal(0, Fly(IonListFlightTests.Quad, "beam.ions.json").ExitCode);
        File.Copy(Results("quad.beam.fly.ions.csv"), Path.Combine(_root, "ions", "beam.csv"));

        var model = Path.Combine(_root, "models", "quad.json");
        var other = Run("fly", model, "--ions", Path.Combine(_root, "ions", "beam.csv"), "--project", _root);
        var again = Run("fly", model, "--ions", Path.Combine(_root, "ions", "beam.csv"), "--project", _root);

        Assert.Equal(0, other.ExitCode);
        Assert.Contains("held a flight of ions/beam.ions.json", other.Stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("held a flight", again.Stderr, StringComparison.Ordinal);
    }

    /// <summary>
    /// An integration that gave up makes the exit code 4, as a run's does, and a regime
    /// violation 2, which outranks it; outcomes do not change it.
    /// </summary>
    [Fact]
    public void OnlyAnIntegratorFailureOrARegimeViolationChangesTheExitCode()
    {
        var outcome = new FlyOutcome
        {
            Manifest = new RunManifest
            {
                ModelHash = "sha256:0", SchemaVersion = "0.14", EngineVersion = "x", SolverBehaviourVersion = 2,
                TransportMode = "trajectory", ComputePath = "scalar", Machine = "m", CreatedUtc = "t",
            },
            IonsPath = "i", DryRun = false, Launched = 2, IntegratorFailures = 0, MaxParallelism = 1, Ladder = false,
            Outcomes = new Dictionary<string, int> { ["StruckElectrode"] = 2 }, Surfaces = new Dictionary<string, int>(),
            Warnings = [], Ions = [], Artifacts = [],
        };

        WarningJson Warning(string code, string severity) =>
            new() { Code = code, Message = "m", Severity = severity, Suppressible = false };

        Assert.Equal(ExitCode.Success, outcome.ExitCode);
        Assert.Equal(ExitCode.ConvergenceFailure, (outcome with { IntegratorFailures = 1 }).ExitCode);
        Assert.Equal(
            ExitCode.RegimeViolation,
            (outcome with { IntegratorFailures = 1, Warnings = [Warning("regime.trajectory-above-validity", "ValidityViolation")] }).ExitCode);
        Assert.Equal(ExitCode.Success, (outcome with { Warnings = [Warning("regime.knudsen-continuum", "Qualified")] }).ExitCode);
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src", "Einzel.Commands", "Examples")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("the repository root was not found above the test binaries");
    }
}
