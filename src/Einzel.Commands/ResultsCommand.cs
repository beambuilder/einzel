using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Io;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>A GRD-1 envelope, in the shape a consumer of this command sees.</summary>
/// <param name="Value">The magnitude, expressed in <paramref name="Unit"/>.</param>
/// <param name="Unit">The unit it is expressed in.</param>
/// <param name="Lower">The bottom of the interval, in the same unit.</param>
/// <param name="Upper">The top of it.</param>
/// <param name="ConfidenceLevel">What fraction the interval is meant to contain.</param>
/// <param name="Evidence">What stands behind the value, by kind.</param>
/// <param name="Warnings">What is active on it (GRD-2).</param>
/// <remarks>
/// <b>A command-layer shape rather than the wire one, and the invariant test is why.</b>
/// The first version handed callers <c>Einzel.Io.MeasuredJson</c> directly, which is what
/// the CLI serialises - convenient, and it made the shell acquire a reference to
/// <c>Einzel.Io</c> merely by reading a property off it. UI-1 gives the shell no file
/// format knowledge, so a command's return type is part of that boundary: exposing a lower
/// assembly's type on a public surface pulls every consumer's reference along with it,
/// silently and without anybody writing a using directive.
/// </remarks>
public sealed record FigureEnvelope(
    double Value,
    string Unit,
    double Lower,
    double Upper,
    double ConfidenceLevel,
    string Evidence,
    IReadOnlyList<ValidityWarning> Warnings);

/// <summary>One figure a run produced, or the reason it did not.</summary>
/// <param name="Name">Its name in the figure-of-merit registry.</param>
/// <param name="Class">Which of §12's families it belongs to.</param>
/// <param name="Description">What it measures.</param>
/// <param name="Measured">
/// The GRD-1 envelope: value, unit, uncertainty, evidence and warnings. Absent where the
/// run did not produce this figure.
/// </param>
/// <param name="Absent">Why it is not here, when it is not.</param>
/// <remarks>
/// <b>Absent with a reason, never zero.</b> A resolving power a run could not compute
/// because two ions arrived is a different statement from a resolving power of zero, and
/// a reader cannot tell them apart if both print as a number. This project has had that
/// exact failure four times through non-finite doubles reaching a serialiser; the rule it
/// reached is that an undefined measurement is missing rather than nought.
/// </remarks>
public sealed record ReportedFigure(
    string Name,
    string Class,
    string Description,
    FigureEnvelope? Measured,
    string? Absent);

/// <summary>The figures of one §12 class.</summary>
/// <param name="Class">The class, as §12 names it.</param>
/// <param name="What">What a figure of this class is <em>of</em>.</param>
/// <param name="Figures">Its figures, in registry order.</param>
public sealed record FigureClass(
    string Class,
    string What,
    IReadOnlyList<ReportedFigure> Figures);

/// <summary>What a run produced, sorted the way §12 sorts it.</summary>
/// <param name="ModelPath">The model, as an absolute path.</param>
/// <param name="Classes">The figures, grouped.</param>
/// <param name="Preview">Whether this is the preview tier, and so tainted (GRD-5).</param>
/// <param name="Warnings">What applies to the run as a whole (GRD-2).</param>
public sealed record ResultsOutcome(
    string ModelPath,
    IReadOnlyList<FigureClass> Classes,
    bool Preview,
    IReadOnlyList<ValidityWarning> Warnings);

/// <summary>
/// A run's figures of merit, grouped by §12's accuracy class.
/// </summary>
/// <remarks>
/// <para>
/// <b>§16 asks for results by accuracy class, with uncertainty and warnings alongside the
/// value and never behind a disclosure control.</b> The second half of that is a layout
/// rule and belongs to whatever draws it; the first half is a question about what a figure
/// <em>is</em>, which is §12's taxonomy and belongs here. A window that decided for itself
/// which figures were Class S would be growing its own copy of §12.
/// </para>
/// <para>
/// <b>What this adds over reading a run.</b> A run reports what it happened to compute; a
/// reader wants to know what the model can be judged by, which includes the figures that
/// are <em>not</em> there and why. A trap has no flight time and a beam has no confinement
/// fraction, and both of those are statements about the instrument rather than gaps in the
/// output.
/// </para>
/// </remarks>
public static class ResultsCommand
{
    /// <summary>Runs a model and reports its figures by class.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="preview">
    /// Whether to use the preview tier (AGT-5), which is cheaper, writes nothing, and is
    /// permanently marked (GRD-5).
    /// </param>
    /// <param name="timestampUtc">When the run happened, for its manifest.</param>
    /// <returns>The figures, grouped.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="EinzelException">The model does not validate, or cannot run.</exception>
    /// <remarks>
    /// <b>A full run writes a manifest and a result, which is correct rather than a side
    /// effect to apologise for.</b> Amendment 25 requires every shell action to be
    /// expressible as a CLI invocation, and the invocation here is <c>einzel run</c> -
    /// which writes both. A view that computed the same numbers without leaving the
    /// record behind would be a capability the command line does not have.
    /// </remarks>
    public static ResultsOutcome Execute(
        string modelPath, bool preview = false, DateTimeOffset? timestampUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);

        var (figures, warnings) = preview
            ? FromPreview(absolute)
            : FromRun(absolute, timestampUtc ?? DateTimeOffset.UtcNow);

        var cloud = ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(absolute)),
            null,
            Path.GetDirectoryName(absolute)).Model?.Cloud.IsCloud ?? false;

        var classes = new List<FigureClass>();

        foreach (var (name, what) in Families)
        {
            var members = FiguresOfMerit.All
                .Where(f => Named(f.Class) == name)
                .Select(f => Report(f, figures))
                .ToList();

            if (members.Count > 0)
            {
                classes.Add(new FigureClass(name, what, members));
            }
        }

        return new ResultsOutcome(
            absolute, classes, preview, [.. warnings, .. Unenveloped(classes, cloud)]);
    }

    /// <summary>
    /// The figures this build can only report as a bare number, named (GRD-1, GRD-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A gap this view exists to expose rather than to work around.</b> GRD-1 says every
    /// quantitative result carries value, unit, uncertainty, evidence and warnings, and
    /// that the API offers no way to obtain the scalar alone. <c>FiguresOfMerit.Evaluator</c>
    /// is a deliberate, argued exception - ranking needs an ordering and an envelope has
    /// none - but the consequence is that most figures exist <em>only</em> in the excepted
    /// form: there is no way to ask this build for a turn-around time with an uncertainty
    /// on it.
    /// </para>
    /// <para>
    /// So the view reports them as absent and says why, and the reason is a property of the
    /// platform rather than of the model. Reporting them as bare numbers instead would put
    /// unqualified values in the one view whose entire purpose is showing the envelope,
    /// which is the failure GRD-1 is written against.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ValidityWarning> Unenveloped(
        IReadOnlyList<FigureClass> classes, bool hasCloud)
    {
        var missing = classes
            .SelectMany(c => c.Figures)
            .Where(f => f.Measured is null)
            .Select(f => f.Name)
            .ToList();

        return missing.Count == 0
            ? []
            : [new ValidityWarning(
                "results.no-envelope",
                $"{missing.Count} of {classes.Sum(c => c.Figures.Count)} figures are not "
                + "reported here, because this build computes them only as bare numbers for "
                + "a study to rank by and has no GRD-1 envelope for them: "
                + string.Join(", ", missing)
                + ". They are obtainable through 'einzel sweep' and 'einzel optimise', "
                + "without an uncertainty",
                WarningSeverity.Provenance),
                .. hasCloud
                    ? Array.Empty<ValidityWarning>()
                    : [new ValidityWarning(
                        "results.no-cloud",
                        "this model declares no ion cloud, so the ensemble figures have no "
                        + "sample to measure a spread over. Without one the acceptance is "
                        + "swept deterministically - evenly spaced, seed-free, the same "
                        + "every run - which is a designed scan rather than a draw from a "
                        + "population, and putting a sampling interval on it would report "
                        + "the scan's own spacing as though it were an uncertainty. Declare "
                        + "a source cloud for an ensemble interval",
                        WarningSeverity.Provenance)]];
    }

    /// <summary>§12's families, in the order §12 lists them.</summary>
    /// <remarks>
    /// Ordered rather than alphabetical, because the order is the argument: a Class T
    /// figure describes one packet, a Class S figure a population, a Class B figure where
    /// a boundary lies. Reading them in that order is reading outward from the ion.
    /// </remarks>
    private static readonly (string Name, string What)[] Families =
    [
        ("T", "one packet's arrival"),
        ("S", "a population"),
        ("B", "where a boundary in operating space lies"),
        ("-", "not one of §12's figures: a raw quantity, or a diagnostic"),
    ];

    /// <summary>§12's name for a class.</summary>
    private static string Named(AccuracyClass which) => which switch
    {
        AccuracyClass.Trajectory => "T",
        AccuracyClass.Statistical => "S",
        AccuracyClass.Boundary => "B",
        _ => "-",
    };

    /// <summary>One figure, with its envelope or the reason there is none.</summary>
    private static ReportedFigure Report(
        FigureOfMeritInfo figure, IReadOnlyDictionary<string, MeasuredJson> figures) =>
        new(figure.Name,
            Named(figure.Class),
            figure.Description,
            figures.TryGetValue(figure.Name, out var measured) ? Envelope(measured) : null,
            figures.ContainsKey(figure.Name) ? null : "this run did not produce it");

    /// <summary>The wire envelope, in the shape this command's callers see.</summary>
    private static FigureEnvelope Envelope(MeasuredJson measured) =>
        new(measured.Value,
            measured.Unit,
            measured.Uncertainty.Lower,
            measured.Uncertainty.Upper,
            measured.Uncertainty.ConfidenceLevel,
            measured.Evidence.Kind,
            [.. measured.Warnings.Select(Warning)]);

    /// <summary>What a full run produced, by figure name.</summary>
    private static (IReadOnlyDictionary<string, MeasuredJson>, IReadOnlyList<ValidityWarning>)
        FromRun(string modelPath, DateTimeOffset timestampUtc)
    {
        // The project the model sits in, or the directory it sits in. Never the working
        // directory: a study kept outside any project once wrote its results into
        // whatever tree the caller happened to be standing in.
        var project = ProjectLayout.Find(modelPath)
            ?? new ProjectLayout(Path.GetDirectoryName(modelPath)!);

        // Through the project's field cache, as `einzel run` goes: the shell and the CLI are
        // peers, and the figures below rebuild the field the run just loaded or kept.
        var cache = new FieldCache(project);

        var (run, validation) = RunCommand.Execute(
            modelPath, project, exportVtu: false, timestampUtc, fieldCache: cache);

        if (run is null)
        {
            throw new EinzelException(validation.Errors);
        }

        var figures = new Dictionary<string, MeasuredJson>(StringComparer.Ordinal);

        // Every figure this build can put an envelope on, computed rather than reported as
        // absent. The registry's own `Measure` decides which those are; a figure it has no
        // envelope for comes back null and is named below rather than printed bare, which
        // is the failure GRD-1 exists to prevent.
        var model = ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(modelPath)),
            null,
            Path.GetDirectoryName(modelPath)).Model!;

        foreach (var figure in FiguresOfMerit.All)
        {
            if (FiguresOfMerit.Measure(figure.Name, model, store: cache) is { } measured)
            {
                figures[figure.Name] = MeasuredJson.From(measured, figure.Unit);
            }
        }

        // The run's own flight time, which comes from a convergence study over three
        // integrator tolerances rather than from a resampled cloud - a different kind of
        // evidence, and the one the registry cannot produce.
        if (run.Diffusion is null)
        {
            figures["flightTime"] = run.FlightTime;
        }

        return (figures, run.FlightTime.Warnings.Select(Warning).ToList());
    }

    /// <summary>What the preview tier produced, by figure name.</summary>
    private static (IReadOnlyDictionary<string, MeasuredJson>, IReadOnlyList<ValidityWarning>)
        FromPreview(string modelPath)
    {
        var preview = PreviewCommand.Execute(modelPath);

        var figures = new Dictionary<string, MeasuredJson>(StringComparer.Ordinal)
        {
            ["flightTime"] = preview.FlightTime,
        };

        return (figures, preview.FlightTime.Warnings.Select(Warning).ToList());
    }

    /// <summary>A warning as it came off the wire.</summary>
    private static ValidityWarning Warning(WarningJson warning) =>
        new(warning.Code,
            warning.Message,
            Enum.TryParse<WarningSeverity>(warning.Severity, ignoreCase: true, out var severity)
                ? severity
                : WarningSeverity.Provenance);
}
