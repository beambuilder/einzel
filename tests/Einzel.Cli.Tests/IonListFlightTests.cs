using System.Text.Json.Nodes;
using Einzel.Commands;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Io.IonLists;
using Einzel.Project;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The per-row flight core: a cloud flown through it is the cloud <c>run</c> always flew, the
/// answer does not depend on how many rows fly at once, a row at the source is the single-ion
/// flight, a birth time is a shifted clock, and a row's collisions follow its index.
/// </summary>
/// <remarks>
/// Every equality is on the bits. The core is the only flight loop a list and a cloud share,
/// so anything it did differently from the loop it replaced would move a stored result.
/// </remarks>
public sealed class IonListFlightTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("einzel-ionflight-").FullName;

    /// <summary>An ideal RF quadrupole at 1.1 MHz, q about 0.5, with an off-axis source.</summary>
    internal const string Quad = """
        {
          "schemaVersion": "0.14",
          "name": "quad",
          "ion": { "massToCharge": { "value": 100, "unit": "Da" }, "chargeNumber": 1 },
          "source": {
            "position": { "value": [0.3, 0.2, 0], "unit": "mm" },
            "direction": { "value": [0, 0, 1] },
            "accelerationPotential": { "value": 2, "unit": "V" }
          },
          "detector": { "planePoint": { "value": [0, 0, 20], "unit": "mm" }, "normal": { "value": [0, 0, -1] } },
          "transport": { "maximumFlightTime": { "value": 50, "unit": "us" } },
          "fields": [
            { "type": "idealQuadrupoleRf",
              "directPotential": { "value": 0, "unit": "V" },
              "driveAmplitude": { "value": 100, "unit": "V" },
              "driveFrequency": { "value": 1.1, "unit": "MHz" },
              "inscribedRadius": { "value": 4, "unit": "mm" } }
          ]
        }
        """;

    private const string QuadList = """
        {
          "schemaVersion": "0.1",
          "ionSets": [
            { "cloud": { "ions": 240, "seed": 3, "temperature": { "value": 300, "unit": "K" },
                         "transverseSpread": { "value": 0.3, "unit": "mm" }, "energyFractionSpread": 0.05 } },
            { "n": 64, "rfPhase": { "sequence": { "first": 0, "step": 0.015625 } } }
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

    private static CompiledModel Compile(string json, string? directory = null)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null, directory);
        Assert.True(validation.IsValid, string.Join("; ", validation.Errors.Select(e => e.Constraint)));
        return validation.Model!;
    }

    private static string WithGas(string model) => model.Replace(
        "\"transport\": {",
        """
        "transport": { "gas": { "model": "langevin", "pressure": { "value": 0.05, "unit": "mbar" },
          "temperature": { "value": 300, "unit": "K" }, "mass": { "value": 28.0134, "unit": "Da" },
          "polarizability": { "value": 1.7403e-30, "unit": "m^3" } },
        """,
        StringComparison.Ordinal);

    private static IReadOnlyList<IonRow> Rows(string list, CompiledModel model)
    {
        var expansion = IonList.Expand(IonList.Parse(list), model);
        Assert.True(expansion.IsValid, string.Join("; ", expansion.Errors.Select(e => $"{e.Path}: {e.Constraint}")));
        return expansion.Rows!;
    }

    /// <summary>The field, settings, detector and gas a flight of this model uses, as a cloud's are built.</summary>
    private static IReadOnlyList<IonFlight> Fly(
        CompiledModel model, IReadOnlyList<IonRow> rows, int parallelism, bool ladder = false, Einzel.Fields.Solved.ISolveStore? store = null,
        IReadOnlyList<int>? indices = null)
    {
        var (field, _) = FieldAssembly.BuildReported(model, store);
        var (point, normal) = (model.DetectorPoint, model.DetectorNormal);
        TrajectoryStopFunction detector = (in PhaseState state) => Vec3.Dot(state.Position - point, normal);
        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        return IonListFlight.Fly(rows, field, settings, detector, Gas(model), model.Gas.Seed, parallelism, ladder, indices: indices);
    }

    private static BackgroundGas Gas(CompiledModel model) => model.SourceDirectory is { } directory
        ? GasFlowImport.Resolve(model.Gas, directory)
        : BackgroundGas.FromModel(model.Gas);

    private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);

    private static void Same(TrajectoryResult expected, TrajectoryResult actual, string where)
    {
        Assert.True(expected.Outcome == actual.Outcome, $"{where}: outcome {expected.Outcome} vs {actual.Outcome}");
        Assert.True(expected.StruckSurface == actual.StruckSurface, $"{where}: surface");
        Assert.True(Bits(expected.FlightTimeSeconds) == Bits(actual.FlightTimeSeconds), $"{where}: flight time");
        Assert.True(Bits(expected.FinalState.Position.X) == Bits(actual.FinalState.Position.X), $"{where}: x");
        Assert.True(Bits(expected.FinalState.Position.Y) == Bits(actual.FinalState.Position.Y), $"{where}: y");
        Assert.True(Bits(expected.FinalState.Position.Z) == Bits(actual.FinalState.Position.Z), $"{where}: z");
        Assert.True(Bits(expected.FinalState.Velocity.Z) == Bits(actual.FinalState.Velocity.Z), $"{where}: vz");
        Assert.True(expected.AcceptedSteps == actual.AcceptedSteps, $"{where}: steps");
        Assert.True(expected.RejectedSteps == actual.RejectedSteps, $"{where}: rejected");
        Assert.True(
            Bits(expected.MaximumRelativeEnergyDrift) == Bits(actual.MaximumRelativeEnergyDrift), $"{where}: drift");
    }

    /// <summary>
    /// A cloud flown through the core is, ion for ion, the cloud the sequential loop it replaced
    /// flew, on every shipped trajectory example that declares a cloud, two of them in gas.
    /// </summary>
    /// <remarks>
    /// The reference is the loop as it stood, verbatim: one collision stream seeded with the gas
    /// seed plus the ion's index, one integration at the model tolerance. Each cloud is cut to
    /// its first 24 ions, which the draw keeps unchanged.
    /// </remarks>
    [Fact]
    public void ACloudFlownThroughTheCoreIsTheCloudTheLoopFlew()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        var store = new FieldCache(new ProjectLayout(_root));
        var compared = 0;
        var collided = 0;

        foreach (var path in Directory.EnumerateFiles(examples, "*.json").Order(StringComparer.Ordinal))
        {
            var text = File.ReadAllText(path);

            if (path.EndsWith(".test.json", StringComparison.Ordinal)
                || JsonNode.Parse(text)!["source"]?["cloud"] is null)
            {
                continue;
            }

            var declared = Compile(text, examples);

            if (declared.TransportMode != "trajectory" || declared.ModelsSpaceCharge)
            {
                continue;
            }

            var model = declared with { Cloud = declared.Cloud with { Ions = Math.Min(declared.Cloud.Ions, 24) } };
            var flight = FiguresOfMerit.FlyCloud(model, store: store);

            // The loop as it stood before the core existed.
            var field = FieldAssembly.Build(model, store);
            var species = IonSpecies.FromModel(model);
            var nominal = new PhaseState(
                model.SourcePosition, model.SourceDirection * (model.LaunchSpeedSi() * Math.Sqrt(1.0 + 0.0)));
            var cloud = IonCloud.Draw(in nominal, species, model.Cloud, model.SourceDirection);
            var (point, normal) = (model.DetectorPoint, model.DetectorNormal);
            TrajectoryStopFunction detector = (in PhaseState state) => Vec3.Dot(state.Position - point, normal);
            var settings = new IntegrationSettings
            {
                RelativeTolerance = model.RelativeTolerance,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            };
            var gas = Gas(model);
            var collisions = 0;

            Assert.Equal(cloud.Length, flight.Events.Count);

            for (var index = 0; index < cloud.Length; index++)
            {
                var sampler = gas.IsPresent
                    ? new CollisionSampler(gas, species.MassSi, species.ChargeSi, model.Gas.Seed + index)
                    : null;
                var result = TrajectoryIntegrator.Integrate(
                    cloud[index], species, field, settings, detector, collisions: sampler);
                collisions += sampler?.Collisions ?? 0;

                var flown = flight.Events[index];
                var where = $"{Path.GetFileName(path)} ion {index}";
                Assert.Equal(index, flown.Ion);
                Assert.True(result.Outcome.ToString() == flown.Outcome, $"{where}: outcome");
                Assert.True(result.StruckSurface == flown.Surface, $"{where}: surface");
                Assert.True(Bits(result.FlightTimeSeconds) == Bits(flown.TimeSeconds), $"{where}: time");
                Assert.True(Bits(result.FinalState.Position.X) == Bits(flown.Position.X), $"{where}: x");
                Assert.True(Bits(result.FinalState.Position.Y) == Bits(flown.Position.Y), $"{where}: y");
                Assert.True(Bits(result.FinalState.Position.Z) == Bits(flown.Position.Z), $"{where}: z");
            }

            Assert.Equal(collisions, flight.Collisions);
            collided += collisions;
            compared++;
            output.WriteLine($"{Path.GetFileName(path),-36} {cloud.Length,3} ions, {collisions,5} collisions: identical");
        }

        Assert.True(compared >= 8, $"only {compared} clouds were compared");
        Assert.True(collided > 0, "no compared cloud collided, so the seeding went untested");
    }

    /// <summary>
    /// Flown one at a time or eight at once, every row of a 304-row list - a 240-ion cloud and a
    /// 64-step birth-phase grid - gives the same flight to the bit, in vacuum and in a gas.
    /// </summary>
    [Fact]
    public void HowManyRowsFlyAtOnceChangesNothing()
    {
        foreach (var json in new[] { Quad, WithGas(Quad) })
        {
            var model = Compile(json);
            var rows = Rows(QuadList, model);
            Assert.Equal(304, rows.Count);
            Assert.Contains(rows, row => row.BirthTimeSeconds > 0.0);

            var one = Fly(model, rows, parallelism: 1);
            var eight = Fly(model, rows, parallelism: 8);

            for (var k = 0; k < rows.Count; k++)
            {
                Assert.Null(one[k].Failure);
                Assert.Same(rows[k], eight[k].Row);
                Same(one[k].Result!, eight[k].Result!, $"row {k}");
                Assert.Equal(one[k].Collisions, eight[k].Collisions);
            }

            output.WriteLine(
                $"{(model.Gas.IsPresent ? "gas" : "vacuum"),-6}: 304 rows identical at 1 and 8, "
                + $"{one.Sum(f => f.Collisions)} collisions, "
                + $"{one.Count(f => f.Result!.Outcome == TrajectoryOutcome.StopConditionMet)} arrived");
        }
    }

    /// <summary>
    /// A row restating the model's source, born at 0, is the first rung of the single-ion
    /// ladder to the bit; with the ladder it is the flight <c>einzel run</c> reports.
    /// </summary>
    [Fact]
    public void ARowAtTheSourceIsTheSingleIonFlight()
    {
        var modelPath = Path.Combine(_root, "models", "quad.json");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllText(modelPath, Quad);

        var model = Compile(Quad);
        var rows = Rows("""{ "schemaVersion": "0.1", "ions": [ { "id": 7 } ] }""", model);

        var (field, _) = FieldAssembly.BuildReported(model, null);
        var (point, normal) = (model.DetectorPoint, model.DetectorNormal);
        TrajectoryStopFunction detector = (in PhaseState state) => Vec3.Dot(state.Position - point, normal);
        var study = FlightTimeStudy.Run(
            new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi()),
            IonSpecies.FromModel(model),
            field,
            new IntegrationSettings
            {
                RelativeTolerance = model.RelativeTolerance,
                MaximumFlightTime = model.MaximumFlightTimeSi,
            },
            detector);

        var single = Assert.Single(Fly(model, rows, parallelism: 1));
        Same(study.Runs[0], single.Result!, "rung 0");
        Assert.Null(single.FlightTimeHalfWidthSeconds);

        var laddered = Assert.Single(Fly(model, rows, parallelism: 1, ladder: true));
        Same(study.Runs[^1], laddered.Result!, "finest rung");
        Assert.NotNull(laddered.FlightTimeHalfWidthSeconds);

        var run = RunCommand.Execute(modelPath, new ProjectLayout(_root), false, DateTimeOffset.UtcNow).Run!;
        var finest = laddered.Result!;

        Assert.Equal(run.Outcome, finest.Outcome.ToString());
        Assert.Equal(run.AcceptedSteps, finest.AcceptedSteps);
        Assert.Equal(Bits(run.FinalPositionMm[0]), Bits(finest.FinalState.Position.X * 1e3));
        Assert.Equal(Bits(run.FinalPositionMm[1]), Bits(finest.FinalState.Position.Y * 1e3));
        Assert.Equal(Bits(run.FinalPositionMm[2]), Bits(finest.FinalState.Position.Z * 1e3));
        Assert.Equal(
            Bits(run.FlightTime.Value),
            Bits(Quantity.Si(finest.FlightTimeSeconds, Dimension.TimeDimension).In("us")));

        output.WriteLine(
            $"run and the laddered row: {run.Outcome}, {run.AcceptedSteps} steps, "
            + $"{run.FlightTime.Value:R} us, x {run.FinalPositionMm[0]:R} mm");
    }

    /// <summary>
    /// A birth time is a shifted clock: born one drive period late, an ion flies the flight it
    /// would have flown at 0, to the integrator's tolerance; born half a period late it does not.
    /// </summary>
    [Fact]
    public void ABirthOnePeriodLateIsTheSameFlightAndHalfAPeriodIsNot()
    {
        var model = Compile(Quad);
        var period = 1.0 / 1.1e6;
        var at = Rows("""{ "schemaVersion": "0.1", "ions": [ { "id": 1 } ] }""", model)[0];
        IonRow[] rows = [at, at with { Id = 2, BirthTimeSeconds = period }, at with { Id = 3, BirthTimeSeconds = period / 2 }];

        var flights = Fly(model, rows, parallelism: 1);
        var (zero, full, half) = (flights[0].Result!, flights[1].Result!, flights[2].Result!);

        Assert.Equal(TrajectoryOutcome.StopConditionMet, zero.Outcome);

        var radius = Math.Max(Math.Abs(zero.FinalState.Position.X), Math.Abs(zero.FinalState.Position.Y));
        var fullShift = (full.FinalState.Position - zero.FinalState.Position).Length / radius;
        var fullTime = Math.Abs(full.FlightTimeSeconds - zero.FlightTimeSeconds) / zero.FlightTimeSeconds;
        var halfShift = (half.FinalState.Position - zero.FinalState.Position).Length / radius;

        output.WriteLine(
            $"one period: final position moved {fullShift:G3} of its radius, time {fullTime:G3} relative; "
            + $"half a period: {halfShift:G3}");

        Assert.True(fullShift < 1e-6, $"born a period late, the ion landed {fullShift:G3} of its radius away");
        Assert.True(fullTime < 1e-9, $"born a period late, the flight time moved {fullTime:G3}");
        Assert.True(halfShift > 1e-2, $"born half a period late, the ion landed only {halfShift:G3} away");
    }

    /// <summary>
    /// The ladder flies the row at its birth time too: half a period late, its finest rung
    /// lands elsewhere than the same row born at 0, and one period late it does not.
    /// </summary>
    [Fact]
    public void ALadderedRowIsFlownFromItsBirth()
    {
        var model = Compile(Quad);
        var period = 1.0 / 1.1e6;
        var at = Rows("""{ "schemaVersion": "0.1", "ions": [ { "id": 1 } ] }""", model)[0];
        IonRow[] rows = [at, at with { Id = 2, BirthTimeSeconds = period / 2 }];

        var flights = Fly(model, rows, parallelism: 1, ladder: true);
        var (zero, half) = (flights[0].Result!, flights[1].Result!);
        var radius = Math.Max(Math.Abs(zero.FinalState.Position.X), Math.Abs(zero.FinalState.Position.Y));
        var shift = (half.FinalState.Position - zero.FinalState.Position).Length / radius;

        output.WriteLine($"laddered, half a period late: final position moved {shift:G3} of its radius");
        Assert.True(shift > 1e-2, $"a laddered row born half a period late landed only {shift:G3} away");
        Assert.All(flights, flight => Assert.NotNull(flight.FlightTimeHalfWidthSeconds));
    }

    /// <summary>
    /// A cloud whose flight throws rethrows what was thrown, with its own type, as the loop
    /// before the core did: a thrown ion is a defect to report, not a cloud member to count.
    /// </summary>
    [Fact]
    public void ACloudWhoseFlightThrowsRethrowsWhatWasThrown()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        var trap = Compile(File.ReadAllText(Path.Combine(examples, "orbital-trap-frequency.json")), examples);

        // On the quadro-logarithmic field's axis, where its potential has no value.
        var onAxis = trap with { SourcePosition = new Vec3(0.001, 0.0, 0.0), Cloud = new IonCloudSettings { Ions = 3 } };

        var failure = Assert.Throws<ArgumentOutOfRangeException>(() => FiguresOfMerit.FlyCloud(onAxis));

        output.WriteLine(failure.Message);
        Assert.Contains("QuadroLogarithmicField", failure.StackTrace, StringComparison.Ordinal);
    }

    /// <summary>
    /// A row's collisions are drawn from the gas seed plus its index in the list, not its id:
    /// row k of a <c>cloud</c> set is ion k of the cloud <c>run</c> flies, collisions included.
    /// </summary>
    [Fact]
    public void ARowsCollisionsFollowItsIndex()
    {
        var examples = Path.Combine(RepositoryRoot(), "src", "Einzel.Commands", "Examples");
        var declared = Compile(File.ReadAllText(Path.Combine(examples, "thermalisation.json")), examples);
        var withCloud = declared with { Cloud = declared.Cloud with { Ions = 40 } };
        var cloud = FiguresOfMerit.FlyCloud(withCloud);

        var block = JsonNode.Parse(File.ReadAllText(Path.Combine(examples, "thermalisation.json")))!["source"]!["cloud"]!
            .DeepClone().AsObject();
        block["ions"] = 40;
        var model = withCloud with { Cloud = new IonCloudSettings() };
        var rows = Rows(
            new JsonObject
            {
                ["schemaVersion"] = "0.1",
                ["ionSets"] = new JsonArray(new JsonObject { ["cloud"] = block }),
            }.ToJsonString(),
            model);

        var flights = Fly(model, rows, parallelism: 4);

        for (var k = 0; k < rows.Count; k++)
        {
            var result = flights[k].Result!;
            Assert.Equal(k + 1, rows[k].Id);
            Assert.True(Bits(cloud.Events[k].TimeSeconds) == Bits(result.FlightTimeSeconds), $"row {k}: time");
            Assert.True(Bits(cloud.Events[k].Position.X) == Bits(result.FinalState.Position.X), $"row {k}: x");
        }

        Assert.Equal(cloud.Collisions, flights.Sum(f => f.Collisions));
        Assert.True(cloud.Collisions > 0, "the cloud never collided, so the seed went untested");

        // The same row alone is row 0, and draws another stream.
        var alone = Assert.Single(Fly(model, [rows[5]], parallelism: 1)).Result!;
        Assert.NotEqual(Bits(flights[5].Result!.FinalState.Position.X), Bits(alone.FinalState.Position.X));

        output.WriteLine($"40 rows equal the run's 40 ions, {cloud.Collisions} collisions");
    }

    /// <summary>
    /// Rows flown as part of a longer list, as an override group's are, draw the collisions
    /// they draw in the whole list when given their indices in it, and come back in their own
    /// order. Without the indices the same rows draw other streams.
    /// </summary>
    [Fact]
    public void RowsGivenTheirIndicesInTheListFlyAsTheyDoInIt()
    {
        var model = Compile(WithGas(Quad));
        var rows = Rows(QuadList, model).Take(48).ToList();
        var whole = Fly(model, rows, parallelism: 4);

        int[] picked = [.. Enumerable.Range(0, rows.Count).Where(k => k % 3 == 1)];
        var unplaced = Fly(model, [.. picked.Select(k => rows[k])], parallelism: 4);
        var part = Fly(model, [.. picked.Select(k => rows[k])], parallelism: 4, indices: picked);

        // On one thread too, where the rows fly in a plain loop.
        foreach (var flown in new[] { part, Fly(model, [.. picked.Select(k => rows[k])], parallelism: 1, indices: picked) })
        {
            for (var j = 0; j < picked.Length; j++)
            {
                Assert.Equal(rows[picked[j]].Id, flown[j].Row.Id);
                Same(whole[picked[j]].Result!, flown[j].Result!, $"row {picked[j]}");
                Assert.Equal(whole[picked[j]].Collisions, flown[j].Collisions);
            }
        }

        Assert.True(whole.Sum(f => f.Collisions) > 0, "no collisions, so the seed went untested");
        Assert.Contains(Enumerable.Range(0, picked.Length), j => unplaced[j].Collisions != part[j].Collisions
            || Bits(unplaced[j].Result!.FinalState.Position.X) != Bits(part[j].Result!.FinalState.Position.X));

        Assert.Throws<ArgumentException>(() => Fly(model, rows, parallelism: 1, indices: picked));
    }

    /// <summary>
    /// A row whose integration throws is that row's result: the others fly, and the failure
    /// is kept for the caller rather than ending the list.
    /// </summary>
    [Fact]
    public void ARowThatThrowsIsThatRowsResult()
    {
        var model = Compile(Quad);
        var at = Rows("""{ "schemaVersion": "0.1", "ions": [ { "id": 1 } ] }""", model)[0];
        IonRow[] rows = [at, at with { Id = 2, Launch = at.Launch with { Position = new Vec3(0, 0.001, 0) } }, at with { Id = 3 }];
        var (field, _) = FieldAssembly.BuildReported(model, null);
        TrajectoryStopFunction detector = (in PhaseState state) => state.Position.Y > 0.0009
            ? throw new InvalidOperationException("this row's stop function refused it")
            : 0.02 - state.Position.Z;

        var flights = IonListFlight.Fly(
            rows, field, new IntegrationSettings { MaximumFlightTime = 5e-5 }, detector, BackgroundGas.Vacuum, 1, 2);

        Assert.NotNull(flights[0].Result);
        Assert.Null(flights[1].Result);
        Assert.Equal("this row's stop function refused it", flights[1].Failure!.Message);
        Assert.NotNull(flights[2].Result);
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
