using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Io;
using Einzel.Io.IonLists;
using Einzel.Project;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// <c>einzel fly</c> with a list whose rows override model parameters: one field per group of
/// rows, each the field of the model compiled with the group's values, built from the cache
/// once per channel, and rows that are the rows a model with those values flies.
/// </summary>
public sealed class FlyOverrideTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("einzel-flyover-").FullName;

    /// <summary>
    /// Two driven plates in a plane: the upper one at <c>U</c>, the lower one at -1 V, the drive
    /// at <c>V</c> and <c>-V</c>. So a new <c>U</c> is a new DC pattern and a solve, and a new
    /// <c>V</c> is the same two channels with other weights. The ion's mass is a parameter too,
    /// which an override must not touch.
    /// </summary>
    private const string Plates = """
        {
          "schemaVersion": "0.14",
          "name": "plates",
          "parameters": {
            "U": { "value": 2, "unit": "V" },
            "V": { "value": 5, "unit": "V" },
            "m": { "value": 100, "unit": "Da" }
          },
          "ion": { "massToCharge": { "expression": "m", "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0, 0.5, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 200, "unit": "V" }
          },
          "fields": [{
            "type": "solved2d",
            "solve": {
              "drive": { "frequency": { "value": 1, "unit": "MHz" }, "waveform": "sinusoid" },
              "minX": { "value": -6, "unit": "mm" }, "maxX": { "value": 6, "unit": "mm" },
              "minY": { "value": -5, "unit": "mm" }, "maxY": { "value": 5, "unit": "mm" },
              "cellSize": { "value": 0.25, "unit": "mm" },
              "electrodes": [
                { "name": "upper", "shape": "rectangle",
                  "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                  "minY": { "value": 2.1, "unit": "mm" }, "maxY": { "value": 3.1, "unit": "mm" },
                  "potential": { "expression": "U", "unit": "V" },
                  "driveAmplitude": { "expression": "V", "unit": "V" } },
                { "name": "lower", "shape": "rectangle",
                  "minX": { "value": -4, "unit": "mm" }, "maxX": { "value": 4, "unit": "mm" },
                  "minY": { "value": -3.1, "unit": "mm" }, "maxY": { "value": -2.1, "unit": "mm" },
                  "potential": { "value": -1, "unit": "V" },
                  "driveAmplitude": { "expression": "-V", "unit": "V" } }
              ]
            }
          }],
          "detector": { "planePoint": { "value": [0, 0, 20], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "mode": "trajectory", "relativeTolerance": 1e-9, "maximumFlightTime": { "value": 40, "unit": "us" } }
        }
        """;

    /// <summary>Row i of every list here: a position and birth phase of its own, so no two rows fly alike.</summary>
    private static JsonObject Ion(int id) => new()
    {
        ["id"] = id,
        ["rfPhase"] = 0.13 * id,
        ["position"] = new JsonObject { ["value"] = new JsonArray(0.1 * id, 0.5 - (0.15 * id), 0), ["unit"] = "mm" },
    };

    private static JsonObject Volts(string name, double value) =>
        new() { [name] = new JsonObject { ["value"] = value, ["unit"] = "V" } };

    /// <summary>
    /// The list the tests fly: V 4, nothing, V 6, U 3, V 4 (the first group again), U 3 and
    /// V 6 in one row, which is a group of its own.
    /// </summary>
    private static readonly (int Id, JsonObject? Overrides)[] Mixed =
    [
        (1, Volts("V", 4)), (2, null), (3, Volts("V", 6)), (4, Volts("U", 3)), (5, Volts("V", 4)),
        (6, new JsonObject { ["U"] = Volts("U", 3)["U"]!.DeepClone(), ["V"] = Volts("V", 6)["V"]!.DeepClone() }),
    ];

    /// <summary>Row i born at a stated time rather than a phase: what flies under any drive frequency.</summary>
    private static JsonObject Born(int id)
    {
        var ion = Ion(id);
        ion.Remove("rfPhase");
        ion["birthTime"] = new JsonObject { ["value"] = 0.13 * id, ["unit"] = "us" };
        return ion;
    }

    private static string List(IEnumerable<(int Id, JsonObject? Overrides)> rows, Func<int, JsonObject>? row = null)
    {
        var ions = new JsonArray();

        foreach (var (id, overrides) in rows)
        {
            var ion = (row ?? Ion)(id);

            if (overrides is not null)
            {
                ion["overrides"] = overrides.DeepClone();
            }

            ions.Add(ion);
        }

        return new JsonObject { ["schemaVersion"] = "0.1", ["maxParallelism"] = 4, ["ions"] = ions }.ToJsonString();
    }

    /// <summary>The plates with the drive frequency a parameter <c>f</c>, 1 MHz, and optionally a gas.</summary>
    private static string Driven(double? pressureMbar = null)
    {
        var model = JsonNode.Parse(Plates)!.AsObject();
        model["parameters"]!["f"] = new JsonObject { ["value"] = 1, ["unit"] = "MHz" };
        model["fields"]![0]!["solve"]!["drive"]!["frequency"] = new JsonObject { ["expression"] = "f", ["unit"] = "MHz" };

        if (pressureMbar is { } pressure)
        {
            model["transport"]!["gas"] = Gas(pressure);
        }

        return model.ToJsonString();
    }

    private static JsonNode Gas(double pressureMbar) => JsonNode.Parse(
        $$$"""
        { "model": "langevin", "pressure": { "value": {{{pressureMbar.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}, "unit": "mbar" },
          "temperature": { "value": 300, "unit": "K" }, "mass": { "value": 28.0134, "unit": "Da" },
          "polarizability": { "value": 1.7403e-30, "unit": "m^3" } }
        """)!;

    private static JsonObject Hertz(double megahertz) =>
        new() { ["f"] = new JsonObject { ["value"] = megahertz, ["unit"] = "MHz" } };

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

    private JsonNode Fly(string model, string list, string name, params string[] more) => Fly(0, model, list, name, more);

    private JsonNode Fly(int expected, string model, string list, string name, params string[] more)
    {
        var (exit, stdout, stderr) = Run([
            "fly", Write($"models/{name}.json", model), "--ions", Write($"ions/{name}.ions.json", list),
            "--project", _root, "--json", .. more,
        ]);

        output.WriteLine(stderr);
        Assert.True(exit == expected, $"exit {exit}: {stdout}{stderr}");
        return JsonNode.Parse(stdout)!;
    }

    private string Results(string name) => Path.Combine(_root, "results", name);

    /// <summary>The model with its parameters set to other values, as a model file would state them.</summary>
    private static string Literal(double u, double v) => Plates
        .Replace("\"U\": { \"value\": 2,", $"\"U\": {{ \"value\": {u},", StringComparison.Ordinal)
        .Replace("\"V\": { \"value\": 5,", $"\"V\": {{ \"value\": {v},", StringComparison.Ordinal);

    /// <summary>A row of a result without its group: what a model's own flight of that row reports.</summary>
    private static string Plain(JsonNode ion)
    {
        var copy = ion.DeepClone().AsObject();
        copy.Remove("group");
        return copy.ToJsonString();
    }

    /// <summary>
    /// The acceptance in small: a list whose rows override U and V flies, group by group, the
    /// rows that one model per operating point flies, to the bit. Each group is one field;
    /// the channel a group shares with an earlier one is not read from the cache again; and
    /// the manifest names every group, its channels and what the cache did for it.
    /// </summary>
    [Fact]
    public void EachGroupFliesTheRowsItsOwnModelFlies()
    {
        var fly = Fly(Plates, List(Mixed), "plates");

        Assert.Equal(5, (int)fly["groups"]!);
        var ions = fly["ions"]!.AsArray();
        Assert.Equal([0, 1, 2, 3, 0, 4], ions.Select(ion => (int)ion!["group"]!));

        var manifest = RunManifest.FromJson(File.ReadAllText(Results("plates.plates.fly.manifest.json")))!;
        var groups = manifest.OverrideGroups!;
        Assert.Equal([0, 1, 2, 3, 4], groups.Select(g => g.Index));
        Assert.Equal([2, 1, 1, 1, 1], groups.Select(g => g.Rows));
        Assert.Equal(4.0, groups[0].Overrides["V"].Value);
        Assert.Equal("V", groups[0].Overrides["V"].Unit);
        Assert.Empty(groups[1].Overrides);

        // V moves weights; U is a new DC pattern. The first group solves both channels, the
        // two other V groups reuse them, U 3 solves its DC and reuses the drive, and U 3 with
        // V 6 needs nothing new.
        Assert.Equal([2, 0, 0, 1, 0], groups.Select(g => g.Solved!.Value));
        Assert.Equal([0, 2, 2, 1, 2], groups.Select(g => g.Reused!.Value));
        Assert.All(groups, g => Assert.Equal(0, g.Loaded));
        Assert.Equal(groups[0].ChannelHashes, groups[1].ChannelHashes);
        Assert.Equal(groups[3].ChannelHashes, groups[4].ChannelHashes);
        Assert.Equal(groups[0].ChannelHashes[1], groups[3].ChannelHashes[1]);
        Assert.Equal(3, manifest.FieldCache!.Count);
        Assert.Equal(2, manifest.Solves!.Count);
        Assert.All(manifest.FieldCache, use => Assert.Equal(FieldCacheUse.Stored, use.Status));

        // Each group's rows, flown through a model file stating its values.
        (double U, double V)[] points = [(2, 4), (2, 5), (2, 6), (3, 5), (3, 6)];

        for (var g = 0; g < points.Length; g++)
        {
            var members = Mixed.Where((_, k) => (int)ions[k]!["group"]! == g).Select(row => (row.Id, (JsonObject?)null)).ToList();
            var own = Fly(Literal(points[g].U, points[g].V), List(members), $"point{g}");

            Assert.Null(own["groups"]);
            Assert.Equal(
                ions.Where(ion => (int)ion!["group"]! == g).Select(ion => Plain(ion!)),
                own["ions"]!.AsArray().Select(ion => ion!.ToJsonString()));
        }

        // The rows are not alike and reach the detector, so the comparison has something to compare.
        Assert.Equal(6, ions.Select(ion => (double)ion!["yMm"]!).Distinct().Count());
        Assert.All(ions, ion => Assert.Equal("StopConditionMet", (string?)ion!["outcome"]));
    }

    /// <summary>
    /// The CSV outputs carry the groups: <c>.fly.csv</c> adds the group and the overridden values
    /// in SI after its columns, <c>.fly.ions.csv</c> its <c>p:</c> columns, and flying that file
    /// again gives the same rows in the same groups.
    /// </summary>
    [Fact]
    public void TheCsvOutputsCarryTheGroupsAndReflyTheSame()
    {
        var fly = Fly(Plates, List(Mixed), "plates");

        var csv = File.ReadAllLines(Results("plates.plates.fly.csv"));
        Assert.Equal("id,tag,outcome,surface,x_mm,y_mm,z_mm,time_us,steps,rejected,collisions,warnings,group,p:U,p:V", csv[0]);
        Assert.EndsWith(",0,,4", csv[1], StringComparison.Ordinal);
        Assert.EndsWith(",1,,", csv[2], StringComparison.Ordinal);
        Assert.EndsWith(",4,3,6", csv[6], StringComparison.Ordinal);

        var expanded = File.ReadAllLines(Results("plates.plates.fly.ions.csv"));
        Assert.EndsWith(",p:U,p:V", expanded[0], StringComparison.Ordinal);

        var (exit, stdout, _) = Run(
            "fly", Path.Combine(_root, "models", "plates.json"), "--ions", Results("plates.plates.fly.ions.csv"),
            "--project", _root, "--json");
        Assert.Equal(0, exit);

        var again = JsonNode.Parse(stdout)!;
        Assert.Equal(fly["ions"]!.ToJsonString(), again["ions"]!.ToJsonString());
        Assert.Null(RunManifest.FromJson(File.ReadAllText(Results("plates.plates.plates.fly.fly.manifest.json")))!
            .OverrideGroups![0].Overrides["V"].Unit);
    }

    /// <summary>
    /// An override equal to the model's own value is the model: rows flown in its group are
    /// bitwise the rows of the list without overrides. Flown in a gas and in two groups, it
    /// also shows that a row draws its collisions by its index in the whole list.
    /// </summary>
    [Fact]
    public void AnOverrideEqualToTheModelsValueIsTheModelEvenInAGas()
    {
        var gas = IonListFlightTests.Quad
            .Replace("\"name\": \"quad\",", "\"name\": \"quad\", \"parameters\": { \"U\": { \"value\": 0, \"unit\": \"V\" } },", StringComparison.Ordinal)
            .Replace("\"directPotential\": { \"value\": 0, \"unit\": \"V\" }", "\"directPotential\": { \"expression\": \"U\", \"unit\": \"V\" }", StringComparison.Ordinal)
            .Replace(
                "\"transport\": {",
                """
                "transport": { "gas": { "model": "langevin", "pressure": { "value": 0.05, "unit": "mbar" },
                  "temperature": { "value": 300, "unit": "K" }, "mass": { "value": 28.0134, "unit": "Da" },
                  "polarizability": { "value": 1.7403e-30, "unit": "m^3" } },
                """,
                StringComparison.Ordinal);

        var rows = Enumerable.Range(1, 8).Select(id => (id, id % 2 == 0 ? Volts("U", 0) : null)).ToList();
        var grouped = Fly(2, gas, List(rows), "grouped");
        var plain = Fly(2, gas, List(rows.Select(row => (row.id, (JsonObject?)null))), "plain");

        Assert.Equal(2, (int)grouped["groups"]!);
        Assert.Null(plain["groups"]);
        Assert.All(grouped["ions"]!.AsArray(), ion => Assert.True((int)ion!["collisions"]! > 0));
        Assert.Equal(
            plain["ions"]!.AsArray().Select(ion => ion!.ToJsonString()),
            grouped["ions"]!.AsArray().Select(ion => Plain(ion!)));

        // One regime warning for the two groups, which fly the same gas at the same voltages.
        Assert.Equal(
            plain["warnings"]!.AsArray().Select(w => (string)w!["code"]!),
            grouped["warnings"]!.AsArray().Select(w => (string)w!["code"]!));
    }

    /// <summary>
    /// An override may change only the fields: one that moves the ion's mass is refused at the
    /// overrides that do it, before any field is built, and nothing is written.
    /// </summary>
    [Fact]
    public void AnOverrideOfTheIonIsRefusedBeforeAnythingFlies()
    {
        var list = Write("ions/mass.ions.json", List([(1, Volts("V", 4)), (2, new JsonObject { ["m"] = new JsonObject { ["value"] = 50, ["unit"] = "Da" } })]));
        var (exit, stdout, _) = Run("fly", Write("models/plates.json", Plates), "--ions", list, "--project", _root, "--json");

        Assert.Equal(1, exit);
        var error = Assert.Single(JsonNode.Parse(stdout)!["errors"]!.AsArray())!;
        output.WriteLine(error.ToJsonString());
        Assert.Equal("/ions/1/overrides", (string)error["path"]!);
        Assert.Contains("only the fields", (string)error["constraint"]!, StringComparison.Ordinal);
        Assert.Contains("ion", (string)error["constraint"]!, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));
        Assert.False(Directory.Exists(Path.Combine(_root, ".einzel", "cache")));
    }

    /// <summary>
    /// The ideal quadrupole with a parameter <c>P</c> that one part of the model reads: the
    /// source position, the detector, the sample interval, the flight-time limit, the gas
    /// pressure, a cloud's temperature, the inscribed radius, or a potential whose double is
    /// a bounded derived parameter.
    /// </summary>
    private static string Parameterised(string kind, string unit, double value)
    {
        var model = JsonNode.Parse(IonListFlightTests.Quad)!.AsObject();
        var parameters = new JsonObject { ["P"] = new JsonObject { ["value"] = value, ["unit"] = unit } };
        var transport = model["transport"]!.AsObject();
        var field = model["fields"]![0]!.AsObject();
        JsonObject Over(string over) => new() { ["expression"] = "P", ["unit"] = over };

        model["parameters"] = parameters;

        switch (kind)
        {
            case "source":
                model["source"]!["position"] = new JsonObject { ["expression"] = new JsonArray("0.3 * P", "0.2 * P", "0"), ["unit"] = "mm" };
                break;
            case "detector":
                model["detector"]!["planePoint"] = new JsonObject { ["expression"] = new JsonArray("0", "0", "20 * P"), ["unit"] = "mm" };
                break;
            case "integration settings":
                transport["sampleInterval"] = Over("ns");
                break;
            case "maximum flight time":
                transport["maximumFlightTime"] = Over("us");
                break;
            case "gas":
                transport["gas"] = Gas(0.05);
                transport["gas"]!["pressure"] = Over("mbar");
                break;
            case "cloud":
                model["source"]!["cloud"] = new JsonObject { ["ions"] = 1, ["seed"] = 1, ["temperature"] = Over("K") };
                break;
            case "radius":
                field["inscribedRadius"] = Over("mm");
                break;
            default:
                parameters["W"] = new JsonObject { ["expression"] = "2 * P", ["unit"] = "V", ["maximum"] = 20 };
                field["directPotential"] = Over("V");
                break;
        }

        return model.ToJsonString();
    }

    /// <summary>
    /// Every way a group's model is refused before a field is built, at the group's overrides
    /// and with nothing written: an override that changes the source, the detector, the
    /// integration settings, the flight-time limit or the gas ("only the fields", naming
    /// which); one that turns the source into a cloud, which <c>fly</c> refuses in the model as
    /// written; one the model's validator refuses (a negative radius); and a derived parameter
    /// out of its bounds, which a JSON row is refused for when the list expands and a CSV row,
    /// which states SI, when its group is compiled - at its row.
    /// </summary>
    [Theory]
    [InlineData("source", "mm", 1, 2, false, "/ions/1/overrides", "only the fields; these change the model's source")]
    [InlineData("detector", "mm", 1, 1.5, false, "/ions/1/overrides", "only the fields; these change the model's detector")]
    [InlineData("integration settings", "ns", 10, 20, false, "/ions/1/overrides", "only the fields; these change the model's integration settings")]
    [InlineData("maximum flight time", "us", 50, 60, false, "/ions/1/overrides", "maximum flight time")]
    [InlineData("gas", "mbar", 0.05, 0.06, false, "/ions/1/overrides", "only the fields; these change the model's gas")]
    [InlineData("cloud", "K", 0, 300, false, "/ions/1/overrides", "the model is refused at /source/cloud")]
    [InlineData("radius", "mm", 4, -1, false, "/ions/1/overrides", "the model is refused at /fields/0")]
    [InlineData("derived", "V", 0, 15, false, "/ions/1/overrides", "the model is refused at /parameters/W")]
    [InlineData("derived", "V", 0, 15, true, "/1", "the model is refused at /parameters/W")]
    public void AGroupWhoseModelIsRefusedIsRefusedAtItsOverrides(
        string kind, string unit, double value, double over, bool csv, string at, string says)
    {
        var list = csv
            ? Write("ions/p.ions.csv", IonListCsv.Header + ",p:P\n"
                + "1,,1.6605390689199998E-25,1.602176634E-19,0.0003,0.0002,0,0,0,1964.5389486782906,0,\n"
                + "2,,1.6605390689199998E-25,1.602176634E-19,0.0003,0.0002,0,0,0,1964.5389486782906,0,"
                + over.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n")
            : Write("ions/p.ions.json", new JsonObject
            {
                ["schemaVersion"] = "0.1",
                ["ions"] = new JsonArray(
                    new JsonObject { ["id"] = 1 },
                    new JsonObject { ["id"] = 2, ["overrides"] = new JsonObject { ["P"] = new JsonObject { ["value"] = over, ["unit"] = unit } } }),
            }.ToJsonString());

        var model = Write("models/p.json", Parameterised(kind, unit, value));
        var (exit, stdout, stderr) = Run("fly", model, "--ions", list, "--project", _root, "--json");

        Assert.True(exit == 1, $"{kind}: exit {exit}: {stdout}{stderr}");
        var error = Assert.Single(JsonNode.Parse(stdout)!["errors"]!.AsArray())!;
        output.WriteLine($"{kind,-22} {error["code"]} {error["path"]}: {error["constraint"]}");
        Assert.Equal(at, (string)error["path"]!);
        Assert.Contains(says, (string)error["constraint"]!, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));
    }

    /// <summary>
    /// An rfPhase is a birth time at the model's drive frequency as written, so a group that
    /// overrides the frequency is refused when it holds a row born by rfPhase, at the group's
    /// overrides. The same override on rows that state their birth time flies, and so does an
    /// rfPhase row in a group whose overrides leave the frequency alone.
    /// </summary>
    [Fact]
    public void AnRfPhaseRowIsRefusedUnderAnotherDriveFrequency()
    {
        var (exit, stdout, stderr) = Run(
            "fly", Write("models/driven.json", Driven()), "--ions", Write("ions/phase.ions.json", List([(1, null), (2, Hertz(2))])),
            "--project", _root, "--json");

        Assert.True(exit == 1, stdout + stderr);
        var error = Assert.Single(JsonNode.Parse(stdout)!["errors"]!.AsArray())!;
        output.WriteLine(error.ToJsonString());
        Assert.Equal("/ions/1/overrides", (string)error["path"]!);
        Assert.Contains("moves what rfPhase means", (string)error["constraint"]!, StringComparison.Ordinal);
        Assert.Contains("birthTime", (string)error["suggestion"]!, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));

        var born = Fly(Driven(), List([(1, null), (2, Hertz(2))], Born), "born");
        Assert.Equal(2, (int)born["groups"]!);

        var phased = Fly(Driven(), List([(1, null), (2, Volts("V", 4))]), "phased");
        Assert.Equal(2, (int)phased["groups"]!);
    }

    private JsonNode FlyInGas(string model, string list, string name)
    {
        var (exit, stdout, stderr) = Run(
            "fly", Write($"models/{name}.json", model), "--ions", Write($"ions/{name}.ions.json", list), "--project", _root, "--json");

        Assert.True(exit is 0 or 2, $"exit {exit}: {stdout}{stderr}");
        return JsonNode.Parse(stdout)!;
    }

    /// <summary>
    /// The regime is measured per group, through the group's own field and frequency: at
    /// 0.005 mbar a 1 MHz drive keeps up with the collisions and a 0.05 MHz one does not, so
    /// <c>regime.collisions-outrun-rf</c> is there when one group flies at 0.05 MHz and absent
    /// for the list without overrides. Two groups whose messages under one code differ (0.05
    /// and 0.03 MHz) each name their group; a message the groups share is reported once, bare.
    /// </summary>
    [Fact]
    public void EachGroupsRegimeIsItsOwnAndNamedWhereItDiffers()
    {
        static List<(string Code, string Message)> Warnings(JsonNode fly) =>
            [.. fly["warnings"]!.AsArray().Select(w => ((string)w!["code"]!, (string)w["message"]!))];

        var plain = Warnings(FlyInGas(Driven(0.005), List([(1, null), (2, null)], Born), "plain"));
        var slow = Warnings(FlyInGas(Driven(0.005), List([(1, null), (2, Hertz(0.05))], Born), "slow"));
        var two = Warnings(FlyInGas(Driven(0.005), List([(1, Hertz(0.05)), (2, Hertz(0.03))], Born), "two"));

        foreach (var (code, message) in two)
        {
            output.WriteLine($"{code}: {message}");
        }

        Assert.DoesNotContain(plain, w => w.Code == "regime.collisions-outrun-rf");
        Assert.Contains(slow, w => w.Code == "regime.collisions-outrun-rf");

        var outrun = two.Where(w => w.Code == "regime.collisions-outrun-rf").Select(w => w.Message).ToList();
        Assert.Equal(2, outrun.Count);
        Assert.StartsWith("group 0 (f = 0.05 MHz): ", outrun[0], StringComparison.Ordinal);
        Assert.StartsWith("group 1 (f = 0.03 MHz): ", outrun[1], StringComparison.Ordinal);

        var overlap = Assert.Single(two, w => w.Code == "regime.overlap-band");
        Assert.DoesNotContain("group", overlap.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The half-space depth is checked per group too: the scaffolded reflectron's cap equals
    /// the beam energy, so the model as written turns its ion at the declared depth, and a
    /// group that lowers the cap is told how far past the plate its ion turns. Two such groups
    /// give two messages under one code, each naming its group.
    /// </summary>
    [Fact]
    public void AHalfSpaceDepthIsCheckedPerGroup()
    {
        var project = Path.Combine(_root, "mirror");
        Assert.Equal(0, Run("init", project).ExitCode);
        var model = Path.Combine(project, "models", "reflectron.json");

        List<string> Depth(string name, string list)
        {
            var (exit, stdout, stderr) = Run(
                "fly", model, "--ions", Write($"mirror/ions/{name}.ions.json", list), "--project", project, "--json");
            Assert.True(exit == 0, stdout + stderr);
            return [.. JsonNode.Parse(stdout)!["warnings"]!.AsArray()
                .Where(w => (string)w!["code"]! == "field.beyond-declared-depth").Select(w => (string)w!["message"]!)];
        }

        static JsonObject Cap(double kilovolts) =>
            new() { ["capPotential"] = new JsonObject { ["value"] = kilovolts, ["unit"] = "kV" } };

        Assert.Empty(Depth("plain", """{ "schemaVersion": "0.1", "ions": [ { "id": 1 }, { "id": 2 } ] }"""));

        var depth = Depth("caps", new JsonObject
        {
            ["schemaVersion"] = "0.1",
            ["ions"] = new JsonArray(
                new JsonObject { ["id"] = 1 },
                new JsonObject { ["id"] = 2, ["overrides"] = Cap(3.6) },
                new JsonObject { ["id"] = 3, ["overrides"] = Cap(3.5) }),
        }.ToJsonString());

        output.WriteLine(string.Join(Environment.NewLine, depth));
        Assert.Equal(2, depth.Count);
        Assert.StartsWith("group 1 (capPotential = 3.6 kV): this ion turns 5.56 mm beyond", depth[0], StringComparison.Ordinal);
        Assert.StartsWith("group 2 (capPotential = 3.5 kV): ", depth[1], StringComparison.Ordinal);
    }

    /// <summary>
    /// The dry run is the cold-cost gate: it lists every group with how many of its channels
    /// the cache holds, before any is solved, and after a flight every one.
    /// </summary>
    [Fact]
    public void ADryRunListsTheGroupsAndWhatTheCacheHoldsOfEach()
    {
        var model = Write("models/plates.json", Plates);
        var list = Write("ions/mixed.ions.json", List(Mixed));

        var (exit, stdout, _) = Run("fly", model, "--ions", list, "--project", _root, "--dry-run");
        output.WriteLine(stdout);
        Assert.Equal(0, exit);
        Assert.Contains("field: 0 of 3 channel(s) in the cache", stdout, StringComparison.Ordinal);
        Assert.Contains("group 0 (V = 4 V): 2 ion(s), 0 of 2 channel(s) in the cache", stdout, StringComparison.Ordinal);
        Assert.Contains("group 4 (U = 3 V, V = 6 V): 1 ion(s), 0 of 2 channel(s) in the cache", stdout, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(_root, "results")));

        Fly(Plates, List(Mixed), "plates");

        var dry = JsonNode.Parse(Run("fly", model, "--ions", list, "--project", _root, "--dry-run", "--json").Stdout)!;
        Assert.Equal(3, (int)dry["cachedChannels"]!);
        Assert.All(dry["manifest"]!["overrideGroups"]!.AsArray(), g => Assert.Equal(2, (int)g!["cachedChannels"]!));
    }

    /// <summary>
    /// A list without overrides flies as it did before overrides existed: no group on any row,
    /// no groups in the outcome or the manifest, and the CSV columns of before.
    /// </summary>
    [Fact]
    public void AListWithoutOverridesWritesWhatItWroteBefore()
    {
        var fly = Fly(Plates, List(Mixed.Select(row => (row.Id, (JsonObject?)null))), "plates");

        Assert.Null(fly["groups"]);
        Assert.Null(fly["manifest"]!["overrideGroups"]);
        Assert.All(fly["ions"]!.AsArray(), ion => Assert.False(ion!.AsObject().ContainsKey("group")));
        Assert.Equal(
            "id,tag,outcome,surface,x_mm,y_mm,z_mm,time_us,steps,rejected,collisions,warnings",
            File.ReadLines(Results("plates.plates.fly.csv")).First());
        Assert.Equal(IonListCsv.Header, File.ReadLines(Results("plates.plates.fly.ions.csv")).First());
    }

    /// <summary>
    /// The memo in front of the cache: a field built twice reads each channel from the store
    /// behind it once, and a solve passes through to it.
    /// </summary>
    [Fact]
    public void TheMemoReadsEachChannelFromItsStoreOnce()
    {
        var model = ModelValidator.Validate(ModelJson.Parse(Plates)).Model!;
        var counting = new CountingStore();

        var first = new SolveMemo(counting);
        FieldAssembly.BuildReported(model, first);
        Assert.Equal((2, 2, 0), (counting.Saves, first.Solved, first.Reused));

        var second = new SolveMemo(counting);
        FieldAssembly.BuildReported(model, second);
        FieldAssembly.BuildReported(model, second);

        Assert.Equal(2 + 2, counting.Loads);
        Assert.Equal((2, 0, 2), (second.Loaded, second.Solved, second.Reused));
        Assert.Equal(2, counting.Saves);
    }

    /// <summary>A store that keeps what it is given and counts what it is asked.</summary>
    private sealed class CountingStore : ISolveStore
    {
        private readonly Dictionary<string, (double[] Potential, SolveRecord Record)> _held = [];

        public int Loads { get; private set; }

        public int Saves { get; private set; }

        public bool TryLoad(
            SolveEntry entry,
            [NotNullWhen(true)] out double[]? potential,
            [NotNullWhen(true)] out SolveRecord? record)
        {
            Loads++;
            var found = _held.TryGetValue(entry.ChannelHash, out var held);
            (potential, record) = found ? held : (null, null);
            return found;
        }

        public void Save(SolveEntry entry, ReadOnlySpan<double> potential, SolveRecord record)
        {
            Saves++;
            _held[entry.ChannelHash] = (potential.ToArray(), record);
        }
    }
}
