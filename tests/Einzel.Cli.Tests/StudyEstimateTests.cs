using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// GRD-8's cost gate, applied to the operation somebody actually plans against.
/// </summary>
/// <remarks>
/// <para>
/// <c>estimate</c> took a model, and a model is one evaluation out of a study that
/// declares hundreds - so the number it gave was short by the evaluation count, silently.
/// A study file states its own extent, so the multiplier needs no pilot and no run.
/// </para>
/// <para>
/// <b>The two terms scale differently and that is the whole arithmetic.</b> An evaluation
/// solves the field once and flies every ensemble member through it, so a study costs
/// <c>evaluations x (solve + members x flight)</c>. Costing it as
/// <c>evaluations x model total</c> would overstate a nine-member figure by most of eight
/// solves per evaluation, and costing it as <c>evaluations x solve</c> would understate it
/// by the flights.
/// </para>
/// </remarks>
public sealed class StudyEstimateTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-study-estimate", Guid.NewGuid().ToString("N"));

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

    private const string Template = "planar-mirror-pair";

    /// <summary>Writes a project holding the template and a study over it.</summary>
    private string Study(string body)
    {
        var model = Path.Combine(_root, "models", Template + ".json");

        if (!File.Exists(model))
        {
            Assert.Equal(0, Cli("init", _root));
            Assert.Equal(0, Cli("new", model, "--from-template", Template));
        }

        var path = Path.Combine(_root, "studies", Guid.NewGuid().ToString("N") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        File.WriteAllText(
            path, body.Replace("MODEL", "../models/" + Template + ".json", StringComparison.Ordinal));

        return path;
    }

    private string Scan(int points) => Study(
        "{ \"schemaVersion\": \"0.1\", \"name\": \"separation\", \"model\": \"MODEL\", "
        + "\"figureOfMerit\": \"resolvingPower\", \"ions\": 9, \"scan\": { "
        + "\"parameter\": \"capToCap\", \"from\": 700.0, \"to\": 800.0, \"unit\": \"mm\", "
        + "\"points\": " + points.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + " } }");

    /// <summary>A study costs its evaluation count, exactly.</summary>
    /// <remarks>
    /// <para>
    /// The arithmetic invariant asserted directly: a study's total <b>is</b> its evaluation
    /// count times its per-evaluation cost. Nothing here depends on how fast the machine is.
    /// </para>
    /// <para>
    /// <b>A first version compared the totals of two scans</b> and asserted their ratio was
    /// the ratio of their point counts, on the reasoning that both are calibrated on the same
    /// machine in the same run so everything machine-specific cancels. <b>It failed on CI at
    /// 1.125 against 2.</b> That reasoning holds on an idle box and not on a shared runner,
    /// where two pilot measurements can differ by more than the quantity being measured - so
    /// the test was measuring the runner's variance. SPEC.md Amendment 27, again.
    /// </para>
    /// <para>
    /// The lesson generalises past this file: <b>where a value is derived from a measurement
    /// of the machine, assert the arithmetic that consumes it, not the value.</b>
    /// </para>
    /// </remarks>
    [Fact]
    public void AStudyCostsItsEvaluationCount()
    {
        foreach (var points in new[] { 25, 50 })
        {
            var costed = EstimateCommand.ForStudy(Scan(points));
            var study = costed.Study!;

            output.WriteLine(
                $"{points,3} points  {costed.Seconds,8:F2} s  =  {study.Evaluations} x "
                + $"{study.SecondsPerEvaluation:F4} s");

            // The points and the nominal evaluation every scan makes before them.
            Assert.Equal(points + 1, study.Evaluations);
            Assert.Equal("scan", study.Kind);

            // Exact to rounding: the total is the count times the unit cost plus what the
            // study's channels cost, and is not arrived at any other way.
            Assert.Equal((study.Evaluations * study.SecondsPerEvaluation) + study.FieldSeconds, costed.Seconds, 9);
        }
    }

    /// <summary>An evaluation solves once, however many members it flies.</summary>
    /// <remarks>
    /// <para>
    /// <b>This is the arithmetic the estimate would most plausibly get wrong.</b> The obvious
    /// costing is <c>evaluations x</c> whatever <c>estimate</c> says a model costs - and a
    /// model's cost already contains one flight, so that reads as though each evaluation
    /// flies one ion. Multiplying it by the member count instead charges a whole solve per
    /// member, which is what the energy sweep used to do and no longer does.
    /// </para>
    /// <para>
    /// <b>Every number here comes from one estimate</b>, which is the point. A first version
    /// compared against a separate <c>Execute</c> call, and two calibrations of the same
    /// machine minutes apart can differ by more than the quantity under test - the mistake
    /// that failed twice on CI. The naive figure is reconstructed from this object's own
    /// terms: a single run costs <c>perEvaluation - (members - 1) x flight</c>, so the naive
    /// costing is <c>members</c> times that, and the gap between them is
    /// <c>(members - 1) x solve</c>. The assertion is therefore exactly "a solve is counted
    /// once rather than per member", with nothing measured twice.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnEvaluationSolvesOnceAndFliesMany()
    {
        var costed = EstimateCommand.ForStudy(Scan(11));

        var study = costed.Study!;
        var members = study.Members;
        var flight = costed.TrajectorySeconds;

        // A separation scan moves the geometry, so every evaluation solves its own field: the
        // field term is a solve per evaluation, and one evaluation is that and its flights.
        Assert.Equal(study.Evaluations, study.SolvingEvaluations);
        var perEvaluation = study.SecondsPerEvaluation + (study.FieldSeconds / study.Evaluations);

        // What one whole run of the model costs, out of this same estimate: the evaluation
        // less the extra members' flights.
        var single = perEvaluation - ((members - 1) * flight);

        var naive = members * single;

        output.WriteLine($"members            {members,8}");
        output.WriteLine($"one flight         {flight * 1000,8:F1} ms");
        output.WriteLine($"one whole run      {single * 1000,8:F1} ms");
        output.WriteLine($"per evaluation     {perEvaluation * 1000,8:F1} ms");
        output.WriteLine($"naive, x members   {naive * 1000,8:F1} ms");

        Assert.True(members > 1, "the study declares nine ions, so there is a distinction to make");

        Assert.True(
            perEvaluation < naive,
            $"one evaluation was costed at {perEvaluation:F3} s against "
            + $"{naive:F3} s for {members} whole model runs. A figure of merit solves the "
            + "field once and flies every member through it, so only the flight term is "
            + "multiplied");

        // And the flights are counted, not dropped: an evaluation costs at least all of them.
        Assert.True(
            perEvaluation >= members * flight,
            $"an evaluation was costed at {perEvaluation:F3} s, which is less "
            + $"than the {members} flights of {flight:F4} s it must contain");
    }

    /// <summary>A long study samples the range it will visit; a short one does not.</summary>
    /// <remarks>
    /// <para>
    /// <b>A study that varies the geometry varies its own cost.</b> A mirror separation scan
    /// crosses a focusing condition and runs 2.2x dearer at one end than the other, so an
    /// estimate taken at the model's declared values alone came out at <b>0.57x</b> of the
    /// real scan - the direction that matters, since an estimate that runs under is worse
    /// than one that runs over.
    /// </para>
    /// <para>
    /// <b>And the gate is the point of the test.</b> Sampling is not free - a sample is one
    /// solve and one flight - so it is worth doing only when the study is long enough to
    /// absorb it. Both halves are asserted, because sampling always would make the estimate
    /// a meaningful fraction of the work it is estimating, and sampling never is the defect
    /// this fixes.
    /// </para>
    /// </remarks>
    [Fact]
    public void ALongStudySamplesTheRangeAndAShortOneDoesNot()
    {
        var brief = EstimateCommand.ForStudy(Scan(4));
        var long_ = EstimateCommand.ForStudy(Scan(60));

        const string Sampled = "sampled at";

        output.WriteLine($" 4 points   sampled: {brief.Basis.Contains(Sampled, StringComparison.Ordinal)}");
        output.WriteLine($"60 points   sampled: {long_.Basis.Contains(Sampled, StringComparison.Ordinal)}");

        Assert.DoesNotContain(Sampled, brief.Basis, StringComparison.Ordinal);
        Assert.Contains(Sampled, long_.Basis, StringComparison.Ordinal);

        // And the short one says why it did not, rather than silently costing at the
        // nominal and letting that read as a measurement of the range. It names the number
        // of pilots it declined to spend, because "too short" without a cost is a judgement
        // the reader cannot check - and because there are two other reasons a range goes
        // unsampled, each with its own wording.
        Assert.Contains("too short to spend", brief.Basis, StringComparison.Ordinal);
        Assert.Contains("extra pilots sampling the range", brief.Basis, StringComparison.Ordinal);
    }

    /// <summary>A sample flies a whole flight, so nothing is extrapolated.</summary>
    /// <remarks>
    /// <b>The mistake this guards is subtle and cost 3.4x.</b> A pilot that flies a
    /// <i>fraction</i> of a flight has to scale up, and the only length available to scale
    /// against is the declared <b>maximum</b> flight time - which is a ceiling, not an
    /// expectation. The nominal ion arrived inside the fraction and the extremes did not, so
    /// they were scaled by the whole ceiling.
    /// <para>
    /// So the bound is against that ceiling: the sampled flight must be far below the
    /// declared maximum, because the real flight is. Restoring the fractional pilot puts it
    /// within a small factor of the ceiling and fails.
    /// </para>
    /// </remarks>
    [Fact]
    public void ASampledFlightIsNotExtrapolatedAgainstTheFlightTimeCeiling()
    {
        var costed = EstimateCommand.ForStudy(Scan(60));

        // What the document allows, against what one flight actually costs to compute.
        // These are different quantities - one is simulated microseconds, the other is
        // wall-clock seconds - so what is asserted is that the flight term stayed the
        // size of a flight rather than inflating toward a ceiling's worth of them.
        var perEvaluation = costed.Study!.SecondsPerEvaluation;
        var flights = costed.Study.Members * costed.TrajectorySeconds;

        output.WriteLine($"per evaluation   {perEvaluation * 1000,8:F0} ms");
        output.WriteLine($"nominal flight   {costed.TrajectorySeconds * 1000,8:F0} ms");
        output.WriteLine($"sampled term     {perEvaluation * 1000,8:F0} ms total");

        Assert.True(
            perEvaluation < 20.0 * costed.TrajectorySeconds * costed.Study.Members,
            $"one evaluation was costed at {perEvaluation:F2} s against a nominal flight of "
            + $"{costed.TrajectorySeconds:F3} s. A sampled flight that did not arrive inside "
            + "its window and was scaled against the declared maximum flight time inflates "
            + "by the ratio of the ceiling to the real flight - fly the whole flight instead");
    }

    /// <summary>A ceiling is reported as a ceiling.</summary>
    /// <remarks>
    /// A scan computes every point it declares; an optimiser stops when it converges and a
    /// bisection when its bracket closes. Reporting a ceiling as a certainty overstates a
    /// search that usually converges early - and reporting a certainty as a ceiling would
    /// let somebody plan for less work than there is, which is the worse direction.
    /// </remarks>
    [Fact]
    public void ASearchBudgetIsACeilingAndAScansPointsAreNot()
    {
        var optimisation = Study(
            "{ \"schemaVersion\": \"0.1\", \"name\": \"shape\", \"model\": \"MODEL\", "
            + "\"figureOfMerit\": \"resolvingPower\", \"ions\": 9, \"algorithm\": \"nelderMead\", "
            + "\"maximumEvaluations\": 120, \"variables\": [ { \"parameter\": \"capToCap\", "
            + "\"minimum\": 700, \"maximum\": 800, \"unit\": \"mm\" } ] }");

        var search = EstimateCommand.ForStudy(optimisation).Study!;
        var grid = EstimateCommand.ForStudy(Scan(9)).Study!;

        output.WriteLine($"{search.Kind,-13} {search.Evaluations,4} evaluations, "
            + $"ceiling {search.EvaluationsAreACeiling}");
        output.WriteLine($"{grid.Kind,-13} {grid.Evaluations,4} evaluations, "
            + $"ceiling {grid.EvaluationsAreACeiling}");

        Assert.Equal("optimisation", search.Kind);
        Assert.Equal(120, search.Evaluations);
        Assert.True(search.EvaluationsAreACeiling);

        Assert.Equal("scan", grid.Kind);
        Assert.False(grid.EvaluationsAreACeiling);
    }
}
