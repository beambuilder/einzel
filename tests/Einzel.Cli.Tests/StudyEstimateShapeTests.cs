using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// What one evaluation of a study <i>is</i> depends on the transport, and costing every
/// study the same way double-counts two of the three cases.
/// </summary>
/// <remarks>
/// <para>
/// The ordinary case flies <c>members</c> independent ions through one solved field, so the
/// solve is paid once and the flight <c>members</c> times — which is the arithmetic
/// <c>ForStudy</c> was written around.
/// </para>
/// <para>
/// <b>But a diffusive run steps a density and a space-charge run advances the whole packet
/// in lockstep.</b> In both, what <c>Execute</c> already costed <i>is</i> one whole
/// evaluation, flights included, so adding <c>members x flight</c> on top counts the same
/// work twice. For a diffusive model it is worse than double-counting: that mode produces no
/// trajectories at all (TRN-2, RND-8), so the added term describes work that does not exist,
/// and the pilot flights taken to measure it fly an ion through a model that has none.
/// </para>
/// </remarks>
public sealed class StudyEstimateShapeTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-study-shape", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static int Cli(params string[] args)
    {
        var previousOut = Console.Out;
        var previousError = Console.Error;

        try
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
            return Program.Main(args);
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private string Model(string example)
    {
        var model = Path.Combine(_root, "models", example + ".json");

        if (!File.Exists(model))
        {
            Assert.Equal(0, Cli("init", _root));
            Assert.Equal(0, Cli("new", model, "--from-example", example));
        }

        return model;
    }

    private string Write(string body)
    {
        var path = Path.Combine(_root, "studies", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, body);

        return path;
    }

    private static string Number(double value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A scan long enough to cross the range-sampling threshold.</summary>
    private string Scan(string example, string parameter, double from, double to, int points)
    {
        Model(example);

        return Write(
            "{ \"schemaVersion\": \"0.1\", \"name\": \"shape\", \"model\": \"../models/"
            + example + ".json\", \"figureOfMerit\": \"flightTime\", \"ions\": 9, \"scan\": { "
            + "\"parameter\": \"" + parameter + "\", \"from\": " + Number(from)
            + ", \"to\": " + Number(to) + ", \"unit\": \"mm\", \"points\": "
            + points.ToString(System.Globalization.CultureInfo.InvariantCulture) + " } }");
    }

    /// <summary>The reported flight term is the one the arithmetic used.</summary>
    /// <remarks>
    /// <para>
    /// <b>The record used to contradict itself.</b> Above the sampling threshold the flight
    /// is re-measured across the study's own range — full window, real cell size — but the
    /// returned outcome kept the <i>unsampled</i> coarsened pilot in
    /// <c>TrajectorySeconds</c>. A caller reconciling the record then found terms that did
    /// not add up, silently, because both fields look equally authoritative.
    /// </para>
    /// <para>
    /// The MR-TOF example is analytic, so there is no solve and an evaluation is the compile
    /// plus the flights alone. That makes the reconciliation tight: whatever is left of the
    /// per-evaluation figure after taking out <c>members</c> flights must be the compile,
    /// which is milliseconds. Under the old behaviour the residual was
    /// <c>members x (sampled - nominal)</c> instead.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheReportedFlightIsTheFlightTheArithmeticUsed()
    {
        var costed = EstimateCommand.ForStudy(
            Scan("mr-tof-oscillations", "halfDrift", 80.0, 120.0, 40), calibrate: false);

        var study = costed.Study!;

        var solve = costed.Elements.Sum(e => e.Seconds);
        var residual = study.SecondsPerEvaluation - solve - (study.Members * costed.TrajectorySeconds);

        output.WriteLine($"per evaluation      {study.SecondsPerEvaluation * 1000,10:F2} ms");
        output.WriteLine($"elements (solve)    {solve * 1000,10:F2} ms");
        output.WriteLine($"members             {study.Members,10}");
        output.WriteLine($"trajectorySeconds   {costed.TrajectorySeconds * 1000,10:F2} ms");
        output.WriteLine($"residual (compile)  {residual * 1000,10:F2} ms");

        Assert.True(study.Evaluations >= 20, "the study must cross the sampling threshold");
        Assert.Contains("sampled at", costed.Basis, StringComparison.Ordinal);

        // The only term unaccounted for is the compile, which is milliseconds. A reported
        // flight that was not the one used leaves members x the difference here instead.
        Assert.True(
            residual >= 0.0 && residual < 0.25 * study.SecondsPerEvaluation,
            $"reconciling the record left {residual * 1000:F2} ms unaccounted for out of "
            + $"{study.SecondsPerEvaluation * 1000:F2} ms per evaluation. The reported "
            + "trajectorySeconds is not the flight the per-evaluation figure was built from");

        // And the total is still exactly the count times the unit cost, plus the fields: none
        // here, since nothing is solved.
        Assert.Equal(0.0, study.FieldSeconds);
        Assert.Equal(study.Evaluations * study.SecondsPerEvaluation, costed.Seconds, 9);
    }

    /// <summary>A diffusive study is not charged for trajectories it never flies.</summary>
    /// <remarks>
    /// <para>
    /// <c>Execute</c> already declines to fly a pilot for a diffusive model — its cost is the
    /// density stepping, estimated from the stability limits — but <c>ForStudy</c> went on to
    /// sample flights anyway, flying an ion through a model whose transport produces none,
    /// and stood ready to add <c>members x flight</c> to a figure that already contained the
    /// whole run.
    /// </para>
    /// <para>
    /// Asserted on the record rather than on a time: a density has no flight term, the
    /// evaluation is one whole run, and the basis says so rather than reporting a range it
    /// declined to sample as merely too short.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADiffusiveEvaluationIsOneWholeRunAndFliesNothing()
    {
        var model = Model("drift-tube-diffusion");

        var costed = EstimateCommand.ForStudy(
            Write(
                "{ \"schemaVersion\": \"0.1\", \"name\": \"shape\", \"model\": "
                + "\"../models/drift-tube-diffusion.json\", \"figureOfMerit\": \"transitTime\", "
                + "\"ions\": 9, \"scan\": { \"parameter\": \"pressure\", \"from\": 0.8, "
                + "\"to\": 1.2, \"unit\": \"mbar\", \"points\": 40 } }"),
            calibrate: false);

        var whole = EstimateCommand.Execute(model, calibrate: false);

        output.WriteLine($"model whole run     {whole.Seconds,10:F3} s");
        output.WriteLine($"per evaluation      {costed.Study!.SecondsPerEvaluation,10:F3} s");
        output.WriteLine($"members             {costed.Study.Members,10}");
        output.WriteLine($"trajectorySeconds   {costed.TrajectorySeconds,10:F3} s");

        // A density has no trajectories, so there is no flight term to report.
        Assert.Equal(0.0, costed.TrajectorySeconds);

        // The evaluation is the whole run, not the run plus nine flights of it.
        Assert.True(
            costed.Study.SecondsPerEvaluation < 2.0 * whole.Seconds,
            $"a diffusive evaluation was costed at {costed.Study.SecondsPerEvaluation:F3} s "
            + $"against {whole.Seconds:F3} s for one whole run of the same model. A density is "
            + "stepped once per evaluation; multiplying it by the ion count charges for "
            + "trajectories this transport mode does not produce");

        Assert.Contains("one whole run", costed.Basis, StringComparison.Ordinal);

        // And it declined to sample the range for the right reason.
        Assert.Contains("no separable flight term", costed.Basis, StringComparison.Ordinal);
        Assert.DoesNotContain("too short", costed.Basis, StringComparison.Ordinal);
    }

    /// <summary>A space-charge evaluation is one whole run too.</summary>
    /// <remarks>
    /// <para>
    /// The other half of the same branch, and it needs its own fixture because no corpus
    /// example declares space charge. A packet advances in <b>lockstep</b> — the mutual force
    /// between ions at different times is not a force between anything — so the estimate's
    /// self-field term already covers every macroparticle's flight. Multiplying by the member
    /// count charges for those flights a second time.
    /// </para>
    /// <para>
    /// <b>The assertion is on the basis, not on a time, and deliberately so.</b> The
    /// self-field term is quadratic in the packet: at 200 trajectories it is 139 s while one
    /// flight is 0.01 ms, so the member multiplication being removed is two milliseconds of
    /// it and no numeric bound could distinguish the two arithmetics. This test asserts that
    /// the whole-run branch was taken; the diffusive test above asserts the arithmetic, and
    /// both go through the same branch.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASpaceChargeEvaluationIsOneWholeRunToo()
    {
        // A drift with a real packet in it: two hundred trajectories carrying a thousand
        // ions' worth of charge, which is what makes the mutual force non-trivial.
        var model = Path.Combine(_root, "models", "packet.json");
        Directory.CreateDirectory(Path.GetDirectoryName(model)!);

        File.WriteAllText(model, """
            {
              "schemaVersion": "0.7",
              "name": "packet",
              "parameters": { "drift": { "value": 200, "unit": "mm", "minimum": 50, "maximum": 400 } },
              "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
              "source": {
                "position": { "value": [0, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 4000, "unit": "V" },
                "cloud": {
                  "ions": 200,
                  "population": 1000,
                  "seed": 1,
                  "transverseSpread": { "value": 0.5, "unit": "mm" },
                  "longitudinalSpread": { "value": 0.5, "unit": "mm" }
                }
              },
              "fields": [ { "type": "fieldFree" } ],
              "detector": {
                "planePoint": { "expression": ["drift", "0", "0"], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "maximumFlightTime": { "value": 200, "unit": "us" },
                "relativeTolerance": 1e-10,
                "spaceCharge": "direct"
              }
            }
            """);

        var costed = EstimateCommand.ForStudy(
            Write(
                "{ \"schemaVersion\": \"0.1\", \"name\": \"shape\", \"model\": "
                + "\"../models/packet.json\", \"figureOfMerit\": \"flightTime\", "
                + "\"ions\": 9, \"scan\": { \"parameter\": \"drift\", \"from\": 150, "
                + "\"to\": 250, \"unit\": \"mm\", \"points\": 40 } }"),
            calibrate: true);

        // CALIBRATED, so the flight term is non-zero and the assertion below can tell the
        // two arithmetics apart. Uncalibrated it would be zero, and multiplying zero by the
        // member count gives the right answer for the wrong reason. The pilot is one ion
        // through a field-free drift, so it costs microseconds.
        var whole = EstimateCommand.Execute(model, calibrate: true);

        output.WriteLine($"model whole run     {whole.Seconds,12:F3} s");
        output.WriteLine($"one flight          {whole.TrajectorySeconds * 1000,12:F2} ms");
        output.WriteLine($"per evaluation      {costed.Study!.SecondsPerEvaluation,12:F3} s");
        output.WriteLine($"members             {costed.Study.Members,12}");

        // WHAT HAS TEETH HERE IS THE BASIS, NOT THE ARITHMETIC, and that is worth being
        // exact about. The self-field term is quadratic in the packet, so at 200
        // trajectories it is 139 s while one flight is 0.01 ms - the member multiplication
        // this fix removes is 2 ms of it and no numeric bound can see that. The branch is
        // shared with the diffusive case, which is asserted with teeth above; here the
        // claim is that the same branch was taken.
        Assert.Contains("one whole run", costed.Basis, StringComparison.Ordinal);

        // And the flights really are inside the figure rather than beside it.
        Assert.Equal(0.0, costed.TrajectorySeconds);
        Assert.True(whole.Seconds > 0.0, "the self-field term must be costed at all");
    }

    /// <summary>An unsampled range says which of three reasons applied.</summary>
    /// <remarks>
    /// A 500-draw tolerance sweep is not "too short to be worth sampling" — its channels
    /// perturb <i>around</i> the nominal, so there is no range to sample and the nominal is
    /// already the right place to measure. The first version said the same wrong thing for
    /// every unsampled case, which invites somebody to lengthen a study that would gain
    /// nothing from it.
    /// </remarks>
    [Fact]
    public void AnUnsampledRangeSaysWhyRatherThanGuessing()
    {
        Model("mr-tof-oscillations");

        var costed = EstimateCommand.ForStudy(
            Write(
                "{ \"schemaVersion\": \"0.1\", \"name\": \"tolerance\", \"model\": "
                + "\"../models/mr-tof-oscillations.json\", \"figureOfMerit\": \"flightTime\", "
                + "\"ions\": 9, \"draws\": 500, \"oneAtATime\": false, \"channels\": [ "
                + "{ \"parameter\": \"halfDrift\", \"sigma\": { \"value\": 0.1, \"unit\": \"mm\" } } ] }"),
            calibrate: false);

        output.WriteLine($"{costed.Study!.Kind}, {costed.Study.Evaluations} evaluations");
        output.WriteLine(costed.Basis[costed.Basis.IndexOf("This is a study", StringComparison.Ordinal)..]);

        Assert.True(costed.Study.Evaluations > 100, "the point is that this study is long");

        // Long, and still unsampled - because it declares no range, not because it is short.
        Assert.Contains("declares none", costed.Basis, StringComparison.Ordinal);
        Assert.DoesNotContain("too short", costed.Basis, StringComparison.Ordinal);
    }
}
