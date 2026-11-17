using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// A run's figures of merit, grouped the way §12 groups them (§16).
/// </summary>
public sealed class ResultsTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "einzel-results", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) Cli(params string[] args)
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

    private string Example(string name)
    {
        Assert.Equal(0, Cli("init", _root).ExitCode);

        var path = Path.Combine(_root, "models", $"{name}.json");

        Assert.Equal(0, Cli("new", path, "--from-example", name).ExitCode);

        return path;
    }

    /// <summary>
    /// A model edited while it runs gives the results of the revision that ran, not a
    /// mixture, and not a failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The results pipeline read the file three times - the run, then the figures, then
    /// whether it declares a cloud - so an edit landing while the run solved gave a flight
    /// time of one revision beside figures of another, and an edit that left the model
    /// invalid failed after the run had finished. Found by a review of the window, where
    /// results run in the background while a person can type.
    /// </para>
    /// <para>
    /// <b>The edit is made at a known moment rather than after a delay</b>: the run announces
    /// that it is solving, after its one read, and the test edits from inside that
    /// announcement. A delay would be a race the test could lose on a fast machine and then
    /// pass while measuring nothing.
    /// </para>
    /// </remarks>
    [Fact]
    public void AModelEditedWhileItRunsGivesTheResultsOfTheRevisionThatRan()
    {
        var model = Example("single-stage-reflectron");
        var original = File.ReadAllText(model);
        var outOfBounds = original.Replace("\"value\": 50,", "\"value\": 500,", StringComparison.Ordinal);

        // The edit is asserted to be one, so a replacement that matched nothing cannot leave
        // the test passing over an unchanged model.
        Assert.NotEqual(original, outOfBounds);

        var edited = false;

        var outcome = ResultsCommand.Execute(
            model,
            progress: new RunProgress(0.0, line =>
            {
                if (!edited && line.Contains("solving", StringComparison.Ordinal))
                {
                    File.WriteAllText(model, outOfBounds);
                    edited = true;
                }
            }));

        Assert.True(edited, "the run never announced its solve, so nothing was edited during it");

        var figures = outcome.Classes.SelectMany(c => c.Figures).ToList();
        var flight = Assert.Single(figures, f => f.Name == "flightTime");

        output.WriteLine($"flight time {flight.Measured!.Value} {flight.Measured.Unit}; "
            + $"{figures.Count(f => f.Measured is not null)} figures reported");

        Assert.NotNull(flight.Measured);
        Assert.Contains(figures, f => f.Name != "flightTime" && f.Measured is not null);
    }

    /// <summary>A run's manifest describes the text it ran, however the file moves under it.</summary>
    /// <remarks>
    /// A run read its model to validate and hash it, again to compile it, and a third time
    /// at the end for the manifest's schema version - so a file that stopped parsing during a
    /// run failed a run that had finished, and a manifest's hash could be of a document other
    /// than the one that ran (PRJ-3). One read now; the file here is made unparseable while
    /// the run solves, and the run must still store a manifest whose hash is of the text it
    /// was given. The end-of-run read lived in three of the run's paths - a sequence that
    /// changes transport mode, a diffusive run, and a mixture of species - and not in the
    /// ordinary one, so the reflectron alone passed with the defect in place; the diffusive
    /// example is what discriminates. No shipped example reaches the other two paths, which
    /// had the same line and the same one-line fix.
    /// </remarks>
    [Theory]
    [InlineData("single-stage-reflectron")]
    [InlineData("drift-tube-diffusion")]
    public void ARunsManifestIsOfTheTextItRan(string example)
    {
        var model = Example(example);
        var original = File.ReadAllText(model);
        var project = new Einzel.Project.ProjectLayout(_root);

        var (run, validation) = RunCommand.Execute(
            model,
            project,
            exportVtu: false,
            DateTimeOffset.UtcNow,
            new RunProgress(0.0, line =>
            {
                if (line.Contains("solving", StringComparison.Ordinal))
                {
                    File.WriteAllText(model, "{ this stopped being a model while it ran");
                }
            }));

        Assert.True(validation.Valid);
        Assert.NotNull(run);
        Assert.Equal(Einzel.Project.ContentHash.OfText(original), run.Manifest.ModelHash);
        Assert.Equal(validation.SchemaVersion, run.Manifest.SchemaVersion);
    }

    /// <summary>§12's order: one packet, then a population, then a boundary.</summary>
    private static readonly string[] OutwardFromTheIon = ["T", "S", "B", "-"];

    /// <summary>Every figure is placed in exactly one of §12's classes.</summary>
    /// <remarks>
    /// <para>
    /// The grouping is not decoration: a Class T figure describes one packet's arrival, a
    /// Class S figure a population, a Class B figure where a boundary in operating space
    /// lies. They are computed differently, cost differently, and answer different
    /// questions, which is why §16 asks for results sorted this way rather than listed.
    /// </para>
    /// <para>
    /// A figure appearing twice, or in none, would make the sort mean less — so the
    /// assertion is a partition, not a lookup.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryFigureIsInExactlyOneClass()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var placed = outcome.Classes.SelectMany(c => c.Figures).Select(f => f.Name).ToList();

        foreach (var group in outcome.Classes)
        {
            output.WriteLine(
                $"Class {group.Class} - {group.What}: "
                + string.Join(", ", group.Figures.Select(f => f.Name)));
        }

        Assert.Equal(FiguresOfMerit.All.Count, placed.Count);
        Assert.Equal(placed.Distinct(StringComparer.Ordinal).Count(), placed.Count);

        Assert.Equal(
            FiguresOfMerit.All.Select(f => f.Name).OrderBy(n => n, StringComparer.Ordinal),
            placed.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>The classes are ordered outward from the ion, not alphabetically.</summary>
    /// <remarks>
    /// §12 lists T, then S, then B, and the order is the argument: one packet, then a
    /// population, then where a boundary lies. Alphabetical would put B first and read as
    /// though the sort were arbitrary.
    /// </remarks>
    [Fact]
    public void TheClassesAreOrderedOutwardFromTheIon()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var order = outcome.Classes.Select(c => c.Class).ToList();

        output.WriteLine(string.Join(" then ", order));

        Assert.Equal(OutwardFromTheIon, order);
    }

    /// <summary>A figure with an envelope carries all of GRD-1's parts.</summary>
    /// <remarks>
    /// Value, unit, uncertainty, what stands behind it, and any warnings — §16's rule is
    /// that these sit alongside the value and never behind a disclosure control, which
    /// only means anything if they are all present to sit there.
    /// </remarks>
    [Fact]
    public void AReportedFigureCarriesItsWholeEnvelope()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var flight = Assert.Single(
            outcome.Classes.SelectMany(c => c.Figures),
            f => f.Name == "flightTime");

        var measured = Assert.IsType<FigureEnvelope>(flight.Measured);

        output.WriteLine(
            $"{flight.Name} = {measured.Value:F6} {measured.Unit} "
            + $"[{measured.Lower:G8}, {measured.Upper:G8}] at {measured.ConfidenceLevel:P0} "
            + $"on {measured.Evidence}");

        Assert.Equal("T", flight.Class);
        Assert.Equal("us", measured.Unit);
        Assert.True(measured.Value > 0.0);
        Assert.True(measured.Upper >= measured.Lower);
        Assert.False(string.IsNullOrWhiteSpace(measured.Evidence));
        Assert.Null(flight.Absent);
    }

    /// <summary>
    /// A figure this build has no envelope for is absent and says so (GRD-1, GRD-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The gap this view exists to expose.</b> GRD-1 says the API offers no way to
    /// obtain a scalar alone, and <c>FiguresOfMerit.Evaluator</c> is a deliberate, argued
    /// exception for ranking. The consequence is that most figures exist <em>only</em> in
    /// the excepted form: there is no way to ask this build for a turn-around time with an
    /// uncertainty on it.
    /// </para>
    /// <para>
    /// Reporting them as bare numbers would put unqualified values in the one view whose
    /// whole purpose is showing the envelope. So they are absent, and the reason — which
    /// is a property of the platform, not of the model — is on the outcome where a reader
    /// will meet it.
    /// </para>
    /// </remarks>
    [Fact]
    public void AFigureWithNoEnvelopeIsAbsentAndTheGapIsNamed()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var figures = outcome.Classes.SelectMany(c => c.Figures).ToList();

        var withEnvelope = figures.Where(f => f.Measured is not null).ToList();
        var without = figures.Where(f => f.Measured is null).ToList();

        output.WriteLine(
            $"{withEnvelope.Count} of {figures.Count} figures carry a GRD-1 envelope: "
            + string.Join(", ", withEnvelope.Select(f => f.Name)));

        Assert.NotEmpty(withEnvelope);
        Assert.NotEmpty(without);

        // Absent with a reason, never a zero that reads as a measurement.
        Assert.All(without, f => Assert.False(string.IsNullOrWhiteSpace(f.Absent)));

        var named = Assert.Single(outcome.Warnings, w => w.Code == "results.no-envelope");

        output.WriteLine(named.Message);

        foreach (var figure in without)
        {
            Assert.Contains(figure.Name, named.Message, StringComparison.Ordinal);
        }
    }

    /// <summary>A declared ion cloud earns its ensemble figures an interval (GRD-1).</summary>
    /// <remarks>
    /// <para>
    /// The gap this closes. A width at half maximum has no standard-error formula, so it
    /// was reported bare or not at all; resampling the cloud gives one for any statistic of
    /// it, and assumes nothing about the distribution — which matters because an
    /// arrival-time peak is measurably skew.
    /// </para>
    /// <para>
    /// The interval is the <em>sampling</em> uncertainty and nothing else, so the evidence
    /// names the ensemble size rather than claiming a confidence in the answer.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADeclaredCloudEarnsItsEnsembleFiguresAnInterval()
    {
        var outcome = ResultsCommand.Execute(Example("turn-around-time"));

        var figures = outcome.Classes.SelectMany(c => c.Figures).ToList();
        var enveloped = figures.Where(f => f.Measured is not null).ToList();

        output.WriteLine(
            $"{enveloped.Count} of {figures.Count} figures carry an envelope:");

        foreach (var figure in enveloped)
        {
            output.WriteLine(
                $"  {figure.Name,-18} {figure.Measured!.Value:G6} {figure.Measured.Unit,-3} "
                + $"[{figure.Measured.Lower:G6}, {figure.Measured.Upper:G6}] "
                + $"on {figure.Measured.Evidence}");
        }

        // The three that come off the cloud, beyond the flight time a run already had.
        foreach (var name in (string[])["arrivalSpread", "turnAroundTime"])
        {
            var figure = Assert.Single(figures, f => f.Name == name);

            Assert.NotNull(figure.Measured);
            Assert.True(
                figure.Measured!.Upper > figure.Measured.Lower,
                $"{name} reported a zero-width interval, which is a claim of certainty");
        }

        Assert.DoesNotContain(outcome.Warnings, w => w.Code == "results.no-cloud");
    }

    /// <summary>
    /// A model with no cloud gets no ensemble intervals, and is told why (GRD-1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The distinction this exists to keep.</b> Without a declared cloud the acceptance
    /// is swept <em>deterministically</em> — evenly spaced from one end to the other, so the
    /// seed does not enter and two runs agree exactly. That is a designed scan, not a draw
    /// from a population, and resampling it would report the scan's own spacing as though it
    /// were a sampling error.
    /// </para>
    /// <para>
    /// The two have been confused here before: <c>DefaultEnergySpread</c>'s remarks exist
    /// because somebody compared a deterministic sweep with a cloud's random draw and read
    /// the difference as noise in the objective. Putting an interval on the sweep would make
    /// that mistake structural, so the figure is absent and the reason is stated.
    /// </para>
    /// </remarks>
    [Fact]
    public void AModelWithNoCloudGetsNoEnsembleIntervalsAndIsToldWhy()
    {
        var outcome = ResultsCommand.Execute(Example("single-stage-reflectron"));

        var said = Assert.Single(outcome.Warnings, w => w.Code == "results.no-cloud");

        output.WriteLine(said.Message);

        Assert.Contains("deterministic", said.Message, StringComparison.Ordinal);

        // The flight time still has one: it comes from a convergence study over three
        // integrator tolerances, which is a different kind of evidence and needs no cloud.
        var flight = Assert.Single(
            outcome.Classes.SelectMany(c => c.Figures), f => f.Name == "flightTime");

        Assert.NotNull(flight.Measured);

        foreach (var name in (string[])["arrivalSpread", "resolvingPower"])
        {
            var figure = Assert.Single(
                outcome.Classes.SelectMany(c => c.Figures), f => f.Name == name);

            Assert.Null(figure.Measured);
        }

        // And turn-around IS reported, which caught this test being wrong. A source with no
        // temperature has exactly no thermal turn-around - that is an analytic statement
        // about the model rather than a measurement over ions, so it needs no cloud and
        // carries no sampling interval. Absent would have been the wrong answer: it is not
        // that the figure could not be computed, it is that it is nought.
        var turnAround = Assert.Single(
            outcome.Classes.SelectMany(c => c.Figures), f => f.Name == "turnAroundTime");

        Assert.NotNull(turnAround.Measured);
        Assert.Equal(0.0, turnAround.Measured!.Value, 12);

        output.WriteLine(
            $"turnAroundTime {turnAround.Measured.Value:F3} {turnAround.Measured.Unit} "
            + $"on {turnAround.Measured.Evidence}");

        Assert.Contains("nalytic", turnAround.Measured.Evidence, StringComparison.Ordinal);
    }

    /// <summary>The preview tier is marked as one (GRD-5).</summary>
    /// <remarks>
    /// AGT-5's cheap loop is what a window wants while somebody drags a slider, and GRD-5
    /// marks the result permanently. A preview number that looks like a run number is the
    /// failure the tier exists to prevent, so the flag is on the outcome rather than left
    /// for the caller to remember.
    /// </remarks>
    [Fact]
    public void ThePreviewTierIsMarkedAsOne()
    {
        var model = Example("single-stage-reflectron");

        var previewed = ResultsCommand.Execute(model, preview: true);
        var ran = ResultsCommand.Execute(model);

        Assert.True(previewed.Preview);
        Assert.False(ran.Preview);

        var previewFlight = Assert.Single(
            previewed.Classes.SelectMany(c => c.Figures), f => f.Name == "flightTime");

        output.WriteLine($"preview warnings: "
            + string.Join(", ", previewFlight.Measured!.Warnings.Select(w => w.Code)));

        // The taint is on the figure itself, not only on the outcome - a figure copied out
        // of this view carries it.
        Assert.Contains(
            previewFlight.Measured.Warnings,
            w => w.Code.Contains("preview", StringComparison.Ordinal));
    }
}
