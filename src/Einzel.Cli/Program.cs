using System.Globalization;
using Einzel.Commands;
using Einzel.Core.Errors;
using Einzel.Core.Results;
using Einzel.Project;
using Einzel.Render;

namespace Einzel.Cli;

/// <summary>The CLI entry point.</summary>
/// <remarks>
/// <para>
/// Spec section 15 makes this the primary surface, and section 6 makes it a peer
/// of the MCP server and the shell rather than a layer under them. Everything
/// here is argument parsing and formatting; the work happens in Einzel.Commands,
/// which the other two surfaces call identically (AGT-2).
/// </para>
/// <para>
/// Arguments are parsed by hand rather than with a library. The surface is small,
/// and PERF-8 budgets 500 ms from cold start to first output with no network call
/// in that path — a budget worth not spending on a parser's reflection.
/// </para>
/// </remarks>
public static class Program
{
    /// <summary>Runs the CLI.</summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>A process exit code (CLI-3).</returns>
    public static int Main(string[] args)
    {
        try
        {
            return Dispatch(args);
        }
        catch (EinzelException failure)
        {
            // Every error, not the first: a command that validated on its way to something
            // else found them all, and a caller fixing one per round trip is a caller who was
            // told less than the platform knew.
            foreach (var error in failure.Errors)
            {
                Console.Error.WriteLine(error.ToString());
            }

            // CLI-3 wants a distinct exit code per failure class, and the class is
            // in the error rather than in how it reached here. A regime violation
            // raised as a refusal is the same finding as one raised as a warning on
            // a completed run, and a caller branching on the exit code should not
            // have to know which route it took.
            return (int)(failure.Error.Code switch
            {
                ErrorCodes.RegimeInvalid => ExitCode.RegimeViolation,
                ErrorCodes.ConvergenceFailed => ExitCode.ConvergenceFailure,
                _ => ExitCode.ValidationFailure,
            });
        }
        catch (FileNotFoundException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return (int)ExitCode.ValidationFailure;
        }
        catch (DirectoryNotFoundException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return (int)ExitCode.ValidationFailure;
        }
        catch (IOException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return (int)ExitCode.InternalError;
        }
        catch (UnauthorizedAccessException failure)
        {
            Console.Error.WriteLine(failure.Message);
            return (int)ExitCode.InternalError;
        }
#pragma warning disable CA1031 // The process boundary is exactly where a catch-all belongs.
        catch (Exception failure)
#pragma warning restore CA1031
        {
            // A defect in the platform, which AGT-3 classes as always a bug
            // report. The type and the stack are what makes it reportable, so they
            // are printed rather than swallowed - but on stderr, and behind a
            // distinct exit code, so a caller can tell an engine defect from a bad
            // model without parsing anything.
            Console.Error.WriteLine($"{ErrorCodes.InternalError}: {failure.GetType().Name}: {failure.Message}");
            Console.Error.WriteLine();
            Console.Error.WriteLine("This is a defect in einzel, not in your model. Please report it with the");
            Console.Error.WriteLine($"command you ran and this trace, against engine {EngineBuild.Version}:");
            Console.Error.WriteLine();
            Console.Error.WriteLine(failure.StackTrace);
            return (int)ExitCode.InternalError;
        }
    }

    private static int Dispatch(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Console.Out.Write(Usage);
            return (int)ExitCode.Success;
        }

        if (args[0] is "--version")
        {
            Console.Out.WriteLine(EngineBuild.Version);
            return (int)ExitCode.Success;
        }

        var options = CommandLine.Parse(args);

        return args[0] switch
        {
            "init" => Init(options),
            "new" => New(options),
            "validate" => Validate(options),
            "outline" => Outline(options),
            "project" => Project(options),
            "estimate" => Estimate(options),
            "solve" => Solve(options),
            "run" => Run(options),
            "fly" => Fly(options),
            "sweep" => Sweep(options),
            "optimise" or "optimize" => Optimise(options),
            "scan" => Scan(options),
            "boundary" => Boundary(options),
            "preview" => Preview(options),
            "test" => Test(options),
            "verify" => Verify(options),
            "report" => Report(options),
            "export" => Export(options),
            "render" => Render(args, options),
            "ext" => Ext(args, options),
            "cache" => Cache(args, options),
            "compare" => Compare(options),
            "schema" => Schema(options),
            "templates" => Catalog(options, "template"),
            "examples" => Catalog(options, "example"),
            "agents-md" => AgentsMd(options),
            "agents" => Agents(args, options),
            "doctor" => Doctor(options),
            _ => Unknown(args[0]),
        };
    }

    private static int Unknown(string verb)
    {
        Console.Error.WriteLine($"unknown command '{verb}'");
        Console.Error.Write(Usage);
        return (int)ExitCode.ValidationFailure;
    }

    /// <summary>
    /// Writes a command result as JSON, for CLI-2.
    /// </summary>
    /// <remarks>
    /// Results on stdout so a caller may pipe it straight to a parser without
    /// filtering. Diagnostics go to stderr everywhere else in this file for the
    /// same reason.
    /// </remarks>
    private static int Emit<T>(T outcome, ExitCode code = ExitCode.Success)
    {
        Console.Out.Write(CommandJson.Write(outcome));
        return (int)code;
    }

    private static int Schema(CommandLine options)
    {
        // The schema is JSON whether or not --json was asked for; the flag is
        // accepted and ignored rather than refused, because an agent that passes
        // it to every verb should not have to special-case this one.
        if (options.Has("study"))
        {
            Console.Out.Write(CatalogCommand.StudySchema());
            return (int)ExitCode.Success;
        }

        Console.Out.Write(CatalogCommand.Schema());
        return (int)ExitCode.Success;
    }

    private static int Sweep(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel sweep <study.json> [--project <dir>] [--dry-run] [--no-cache] [--json]");
            Console.Error.WriteLine("run 'einzel schema --study' for the shape of a study file");
            return (int)ExitCode.ValidationFailure;
        }

        var studyPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(studyPath);
        var project = new ProjectLayout(root);
        var outcome = StudyCommand.Sweep(
            studyPath, project, options.Has("dry-run"), CacheFor(project, options), Console.Error.WriteLine);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Artifacts.Count == 0)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"would sweep {outcome.Draws} draws of {outcome.FigureOfMerit.Name} over {outcome.ModelPath}"));
            WriteFieldPlan(outcome.Field);

            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.FigureOfMerit.Name}  nominal {outcome.Nominal:G8} {outcome.FigureOfMerit.Unit}, "
            + $"{outcome.Succeeded} of {outcome.Draws} draws arrived"));

        if (outcome.Distribution is { } distribution)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {distribution.Value:G6} +/- "
                + $"{(distribution.Uncertainty.Upper - distribution.Uncertainty.Lower) / 2.0:G3} "
                + $"{distribution.Unit} ({distribution.Uncertainty.ConfidenceLevel:P0} CI)"));

            // GRD-2: warnings travel to every surface, including this one.
            foreach (var warning in distribution.Warnings)
            {
                var stream = warning.Suppressible ? Console.Out : Console.Error;
                stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
            }
        }

        // What the draws themselves earned, which is a different set: the
        // distribution's warnings are about the distribution, and these are about
        // the flights it was computed from.
        Warn(outcome.Warnings);

        if (outcome.Sensitivity.Count > 0)
        {
            // Section 13 calls this the actual deliverable: not whether the
            // tolerance suffices, but which parameter binds first.
            Console.Out.WriteLine();
            Console.Out.WriteLine(
                $"which parameter binds first (figure in {outcome.FigureOfMerit.Unit}, "
                + "swing is the larger departure from nominal):");

            foreach (var channel in outcome.Sensitivity)
            {
                // The two ends as well as the swing, because a swing alone does not
                // say which direction hurts, and a channel whose low end is missing
                // is one where the ion stopped arriving - which is the finding, not
                // a gap in the table.
                var band = channel is { Low: { } low, High: { } high }
                    ? string.Create(invariant, $"{low:G8} .. {high:G8}")
                    : "one end did not arrive";

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  {channel.Parameter,-24} swing {channel.Swing:G4}   [{band}]"));
            }
        }

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return (int)ExitCode.Success;
    }

    private static int Scan(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel scan <study.json> [--project <dir>] [--dry-run] [--no-cache] [--json]");
            Console.Error.WriteLine("run 'einzel schema --study' for the shape of a study file");
            return (int)ExitCode.ValidationFailure;
        }

        var studyPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(studyPath);
        var project = new ProjectLayout(root);
        var outcome = StudyCommand.Scan(
            studyPath, project, options.Has("dry-run"), CacheFor(project, options), Console.Error.WriteLine);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Artifacts.Count == 0)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"would scan {outcome.Parameter} over {outcome.FigureOfMerit.Name} in "
                + $"{outcome.ModelPath}"));
            WriteFieldPlan(outcome.Field);

            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.Parameter} / {outcome.Unit}      {outcome.FigureOfMerit.Name} / "
            + $"{outcome.FigureOfMerit.Unit}"));

        foreach (var point in outcome.Points)
        {
            // A point with no figure prints its reason rather than a blank. On a
            // stability scan that reason is the result - the ion was lost, and where.
            var figure = point.FigureOfMerit is { } value
                ? string.Create(invariant, $"{value,18:G8}")
                : $"{"-",18}  {Short(point.Failure)}";

            Console.Out.WriteLine(string.Create(invariant, $"{point.Value,14:G8}  {figure}"));
        }

        Console.Out.WriteLine();

        if (outcome.Nominal is { } nominal)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"nominal {nominal:G8} {outcome.FigureOfMerit.Unit} at the model's own value"));
        }

        if (outcome.Steepest is { } steepest)
        {
            // Not a boundary: where the figure moves fastest on the grid actually
            // computed, and how coarse that grid is there. ACC-6 wants one part in
            // five hundred of the scan, so the fraction is what says whether this
            // scan resolved anything.
            var change = steepest.FigureVanishes
                ? "the figure stops existing"
                : string.Create(invariant, $"the figure moves {steepest.Change:G4}");

            Console.Out.WriteLine(string.Create(
                invariant,
                $"steepest between {steepest.Low:G8} and {steepest.High:G8} {outcome.Unit}: {change}"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"  that interval is 1 part in {1.0 / Math.Max(steepest.WidthFraction, 1e-12):F0} of the "
                + $"scan; ACC-6 asks for 1 in 500"));
        }

        Warn(outcome.Warnings);

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return (int)ExitCode.Success;
    }

    private static int Boundary(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel boundary <study.json> [--project <dir>] [--dry-run] [--no-cache] [--json]");
            Console.Error.WriteLine("run 'einzel schema --study' for the shape of a study file");
            return (int)ExitCode.ValidationFailure;
        }

        var studyPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(studyPath);
        var project = new ProjectLayout(root);
        var outcome = StudyCommand.Boundary(
            studyPath, project, options.Has("dry-run"), CacheFor(project, options), Console.Error.WriteLine);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Artifacts.Count == 0)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"would bisect for {outcome.Parameter} where {outcome.FigureOfMerit.Name} crosses "
                + $"{outcome.Threshold:G6} in {outcome.ModelPath}"));
            WriteFieldPlan(outcome.Field);

            return (int)ExitCode.Success;
        }

        if (outcome.Boundary is { } boundary)
        {
            // The bracket, not a value with an error bar. A bisection proves
            // containment; the midpoint is a convention laid over it, and printing
            // the midpoint alone would quote the boundary more precisely than it was
            // measured.
            Console.Out.WriteLine(string.Create(
                invariant,
                $"{outcome.Parameter,-14} {boundary.Value:G8} {outcome.Unit}, "
                + $"bracketed [{boundary.Uncertainty.Lower:G8}, {boundary.Uncertainty.Upper:G8}]"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{"where",-14} {outcome.FigureOfMerit.Name} crosses {outcome.Threshold:G6} "
                + $"{outcome.FigureOfMerit.Unit} (inside is {outcome.Inside})"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{"resolved",-14} 1 part in {1.0 / Math.Max(outcome.ResolvedFraction, 1e-12):F0} of "
                + $"the range in {outcome.Evaluations} evaluations; ACC-6 asks for 1 in 500, and a "
                + $"grid would cost 501"));

            // Whether or not the check found anything. A reader who sees the line
            // knows the single-crossing assumption was tested; one who sees nothing
            // cannot tell that from its never having been tested (REG-2).
            Console.Out.WriteLine(outcome.SecondCrossingSi is { } flip
                ? string.Create(
                    invariant,
                    $"{"but",-14} the figure crosses back at {flip:G8} SI, so this is one edge of "
                    + $"several rather than the edge of the region ({outcome.Probes} probe(s))")
                : string.Create(
                    invariant,
                    $"{"confirmed",-14} {outcome.Probes} probe(s) outside the bracket found no second "
                    + $"crossing"));
        }

        Warn(outcome.Warnings);

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return outcome.Converged ? (int)ExitCode.Success : (int)ExitCode.ConvergenceFailure;
    }

    /// <summary>What a study's dry run says about its fields, as <c>fly --dry-run</c> does.</summary>
    /// <remarks>
    /// The cold cost before it is paid: which channels the nominal model and the study's ends
    /// need, how many of them the cache holds, and whether the points solve channels of their
    /// own or only re-weight the nominal ones.
    /// </remarks>
    private static void WriteFieldPlan(StudyFieldSurvey? survey)
    {
        if (survey is null)
        {
            return;
        }

        var points = survey.PointChannels == 0
            ? "the points re-weight the nominal channels"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"each point adds up to {survey.PointChannels} channel(s) of its own");

        Console.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"  field: {survey.CachedChannels} of {survey.Channels} channel(s) in the cache at the nominal model and the study's ends; "
            + $"{points}; {survey.SolvingEvaluations} of {survey.Evaluations} evaluation(s) solve; "
            + $"peak {EstimateCommand.MemorySize(survey.PeakMiB)} at up to {survey.MaxParallelism} at once"
            + $"{(survey.BasisNote is { } note ? "; " + note : string.Empty)}"));
    }

    /// <summary>The first line of a failure, for a table cell.</summary>
    private static string Short(string? failure)
    {
        if (string.IsNullOrWhiteSpace(failure))
        {
            return "no figure";
        }

        var line = failure.ReplaceLineEndings(" ").Trim();

        return line.Length <= 72 ? line : line[..69] + "...";
    }

    private static int Optimise(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel optimise <study.json> [--project <dir>] [--dry-run] [--no-cache] [--json]");
            Console.Error.WriteLine("run 'einzel schema --study' for the shape of a study file");
            return (int)ExitCode.ValidationFailure;
        }

        var studyPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(studyPath);
        var project = new ProjectLayout(root);
        var outcome = StudyCommand.Optimise(
            studyPath, project, options.Has("dry-run"), CacheFor(project, options), Console.Error.WriteLine);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        if (outcome.Artifacts.Count == 0)
        {
            Console.Out.WriteLine(
                $"would {outcome.Sense.ToLowerInvariant()} {outcome.FigureOfMerit.Name} "
                + $"by {outcome.Algorithm} over {outcome.ModelPath}");
            WriteFieldPlan(outcome.Field);

            return (int)ExitCode.Success;
        }

        foreach (var (name, measured) in outcome.Best)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"{name,-24} {measured.Value:G8} +/- "
                + $"{(measured.Uncertainty.Upper - measured.Uncertainty.Lower) / 2.0:G3} {measured.Unit}"));
        }

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.FigureOfMerit.Name,-24} {outcome.Objective.Value:G6} {outcome.Objective.Unit} "
            + $"({outcome.Sense.ToLowerInvariant()}d)"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.Algorithm}: {outcome.Evaluations} evaluations, {outcome.Failures} failed, "
            + $"converged {outcome.Converged}"));

        foreach (var warning in outcome.Objective.Warnings)
        {
            var stream = warning.Suppressible ? Console.Out : Console.Error;
            stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        // What the evaluations earned on the way. An optimiser walks towards
        // whatever scores best, so a corner of the box where the solve stops
        // converging is somewhere it will actively go - and it used to arrive there
        // silently.
        Warn(outcome.Warnings);

        Console.Out.WriteLine();

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }

        return (int)(outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
    }

    /// <summary>A population limit, rendered so a limit below ten ions keeps a figure.</summary>
    /// <remarks>
    /// The same rendering the warning uses, so the summary line and the warning
    /// beneath it do not quote the same quantity as 5 and as 4.7.
    /// </remarks>
    private static string Capacity(double limit) => limit switch
    {
        < 1.0 => limit.ToString("F2", CultureInfo.InvariantCulture),
        < 10.0 => limit.ToString("F1", CultureInfo.InvariantCulture),
        _ => limit.ToString("N0", CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Prints warnings, advisories on stdout and everything else on stderr.
    /// </summary>
    /// <remarks>
    /// CLI-2 puts results on stdout and diagnostics on stderr, and GRD-3 makes only
    /// an advisory suppressible - so the split by severity is the same split by
    /// stream, and a caller redirecting stdout still sees what qualified the number.
    /// </remarks>
    private static void Warn(IReadOnlyList<ValidityWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            var stream = warning.IsSuppressible ? Console.Out : Console.Error;
            stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }
    }

    private static int Catalog(CommandLine options, string kind)
    {
        // With a name, emit that artifact; without, list what there is.
        if (options.Positional.Count > 0)
        {
            Console.Out.Write(CatalogCommand.Read(kind, options.Positional[0]));
            return (int)ExitCode.Success;
        }

        var outcome = kind == "template" ? CatalogCommand.Templates() : CatalogCommand.Examples();

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        foreach (var entry in outcome.Entries)
        {
            Console.Out.WriteLine(entry.Name);

            if (!string.IsNullOrEmpty(entry.Description))
            {
                Console.Out.WriteLine($"    {Wrap(entry.Description)}");
            }
        }

        return (int)ExitCode.Success;
    }

    /// <summary>Truncates a description to one readable terminal line.</summary>
    private static string Wrap(string text)
    {
        const int Width = 96;

        if (text.Length <= Width)
        {
            return text;
        }

        var cut = text.LastIndexOf(' ', Width);
        return text[..(cut > 0 ? cut : Width)] + "...";
    }

    private static int New(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel new <model.json> --from-template <name> | --from-example <name> [--dry-run]");
            return (int)ExitCode.ValidationFailure;
        }

        var (kind, name) = options.Value("from-template") is { } template
            ? ("template", template)
            : options.Value("from-example") is { } example
                ? ("example", example)
                : (null, null);

        if (kind is null || name is null)
        {
            Console.Error.WriteLine("give one of --from-template <name> or --from-example <name>");
            Console.Error.WriteLine("run 'einzel templates' or 'einzel examples' to see what there is");
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = ProjectCommands.New(options.Positional[0], kind, name, options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        Console.Out.WriteLine(outcome.Written
            ? $"wrote {outcome.Path} from {outcome.Source}"
            : $"would write {outcome.Path} from {outcome.Source}");

        // An example brings its own assertion, so say where it landed - otherwise
        // the file appears in tests/ with nothing having mentioned it.
        if (outcome.TestPath is { } test)
        {
            Console.Out.WriteLine(outcome.Written
                ? $"wrote {test} - run 'einzel test' to check it"
                : $"would write {test}");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// The agent acceptance suite: list the tasks, set one up, score an attempt.
    /// </summary>
    /// <remarks>
    /// A development and release tool rather than something a modelling session
    /// reaches for, but it lives behind the same command objects as everything
    /// else (AGT-2) so a harness can drive it the same way. The agent under test
    /// is never given this verb - it gets a project directory and the rest of the
    /// CLI, which is the situation being measured.
    /// </remarks>
    private static int Agents(string[] args, CommandLine options)
    {
        var action = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1]
            : "tasks";

        return action switch
        {
            "tasks" => AgentTasks(options),
            "setup" => AgentSetup(options),
            "score" => AgentScore(options),
            _ => AgentUsage(action),
        };
    }

    private static int AgentUsage(string action)
    {
        Console.Error.WriteLine($"unknown agents action '{action}'");
        Console.Error.WriteLine("usage: einzel agents tasks [name]");
        Console.Error.WriteLine("       einzel agents setup <task> <dir>");
        Console.Error.WriteLine("       einzel agents score <task> <dir>");
        return (int)ExitCode.ValidationFailure;
    }

    private static int AgentTasks(CommandLine options)
    {
        // "agents tasks" leaves the action in the positional list, so a named
        // task is the second one.
        var name = options.Positional.Count > 1 ? options.Positional[1] : null;

        if (name is not null)
        {
            var task = AgentSuite.Find(name);

            if (options.Has("json"))
            {
                return Emit(new
                {
                    task.Name,
                    Track = task.Track.ToString(),
                    task.Deliverable,
                    task.Rationale,
                    task.Prompt,
                });
            }

            // The prompt alone on stdout, so a harness can pipe it straight to an
            // agent without stripping anything.
            Console.Out.Write(task.Prompt);
            return (int)ExitCode.Success;
        }

        if (options.Has("json"))
        {
            return Emit(AgentSuite.All.Select(t => new
            {
                t.Name,
                Track = t.Track.ToString(),
                t.Deliverable,
                t.Rationale,
                t.Prompt,
            }));
        }

        foreach (var task in AgentSuite.All)
        {
            Console.Out.WriteLine($"{task.Name,-28} {task.Track,-11} -> {task.Deliverable}");
        }

        return (int)ExitCode.Success;
    }

    private static int AgentSetup(CommandLine options)
    {
        if (options.Positional.Count < 3)
        {
            Console.Error.WriteLine("usage: einzel agents setup <task> <dir>");
            return (int)ExitCode.ValidationFailure;
        }

        var task = AgentSuite.Find(options.Positional[1]);
        var root = Path.GetFullPath(options.Positional[2]);

        if (options.Has("dry-run"))
        {
            Console.Out.WriteLine($"would prepare {root} for '{task.Name}'");
            return (int)ExitCode.Success;
        }

        var layout = AgentSuite.Prepare(task, root);

        Console.Out.WriteLine($"prepared {layout.Root} for '{task.Name}'");
        Console.Out.WriteLine($"the agent should leave {task.Deliverable}");
        return (int)ExitCode.Success;
    }

    private static int AgentScore(CommandLine options)
    {
        if (options.Positional.Count < 3)
        {
            Console.Error.WriteLine("usage: einzel agents score <task> <dir>");
            return (int)ExitCode.ValidationFailure;
        }

        var score = AgentSuite.Score(options.Positional[1], options.Positional[2]);

        if (options.Has("json"))
        {
            return Emit(score, score.Passed ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        foreach (var check in score.Checks)
        {
            var mark = check.Passed ? "ok  " : "FAIL";
            var stream = check.Passed ? Console.Out : Console.Error;
            stream.WriteLine($"{mark} {check.Name}");
            stream.WriteLine($"       {check.Detail}");
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine($"{score.Task} ({score.Track}): {(score.Passed ? "passed" : "failed")}");

        return (int)(score.Passed ? ExitCode.Success : ExitCode.ValidationFailure);
    }

    private static int AgentsMd(CommandLine options)
    {
        var root = options.Value("project") ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");
        var outcome = ProjectCommands.AgentsMd(root, options.Has("dry-run"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        Console.Out.WriteLine(outcome.Written
            ? $"wrote {outcome.Path} ({outcome.Source})"
            : $"would write {outcome.Path} ({outcome.Source})");

        return (int)ExitCode.Success;
    }

    private static int Doctor(CommandLine options)
    {
        var root = options.Value("project") ?? (options.Positional.Count > 0 ? options.Positional[0] : null);

        if (root is null && Directory.Exists("models"))
        {
            root = ".";
        }

        var outcome = ProjectCommands.Doctor(root);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Healthy ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        foreach (var check in outcome.Checks)
        {
            var mark = check.Ok ? "ok  " : "WARN";
            var stream = check.Ok ? Console.Out : Console.Error;
            stream.WriteLine($"{mark} {check.Check,-20} {check.Detail}");
        }

        return (int)(outcome.Healthy ? ExitCode.Success : ExitCode.ValidationFailure);
    }

    /// <summary>Whether a file is a study rather than a model.</summary>
    /// <remarks>
    /// A study names the model it studies; a model names no model. That one property is
    /// an exact discriminator and needs no schema version, so a file from any earlier
    /// version is classified the same way.
    /// </remarks>
    private static bool IsStudy(string path)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("model", out var model)
                && model.ValueKind == System.Text.Json.JsonValueKind.String;
        }
        catch (Exception failure) when (
            failure is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            // Not readable or not JSON: let the model path report it properly, with
            // AGT-3's code and suggestion, rather than failing here as "not a study".
            return false;
        }
    }

    private static int Estimate(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel estimate <model.json|study.json> [--threshold <seconds>] [--project <dir>] [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var path = options.Positional[0];

        // GRD-8 asks for a *configurable* threshold and it was a constant. Somebody who
        // knows their study is worth an hour needs a way to say so; without one the
        // observed response was to make the study smaller.
        var threshold = EstimateCommand.ThresholdSeconds;

        if (options.Value("threshold") is { } declared)
        {
            if (!double.TryParse(
                    declared, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
                || seconds <= 0.0)
            {
                Console.Error.WriteLine(
                    $"'{declared}' is not a positive number of seconds");

                return (int)ExitCode.ValidationFailure;
            }

            threshold = seconds;
        }

        // Calibration is on unless refused: an estimate is worth having about the
        // machine that will do the work. --no-calibrate keeps PERF-8's cold-start
        // budget, which a pilot solve on a volume geometry does not.
        var calibrate = !options.Has("no-calibrate");

        // A study is costed as a study. Detected rather than declared by a flag,
        // because the file already says which it is and a flag would let the two
        // disagree - the cost of getting that wrong is a plan short by a factor of
        // the evaluation count.
        // A study is costed against the project's field cache: a channel it holds is loaded,
        // not solved.
        var outcome = IsStudy(path)
            ? EstimateCommand.ForStudy(
                path, calibrate, threshold,
                new ProjectLayout(options.Value("project") ?? InferProjectRoot(Path.GetFullPath(path))))
            : EstimateCommand.Execute(path, calibrate, threshold);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        foreach (var element in outcome.Elements)
        {
            // Any rank, not just two. A volume element used to fall through to
            // "analytic" here - which is not a shape, it is a claim that there is
            // nothing to solve, printed beside the largest solve in the model.
            var size = element.Nodes.Count > 0
                ? string.Join("x", element.Nodes)
                : "analytic";

            Console.Out.WriteLine(string.Create(
                invariant,
                $"field {element.Index}  {element.Type,-14} {size,-12} {element.Seconds,8:F2} s  {EstimateCommand.MemorySize(element.MemoryMiB),11}"));
        }

        if (outcome.Study is { } study)
        {
            var flown = study.Members == 1 ? "trajectory" : "trajectories";

            Console.Out.WriteLine(string.Create(
                invariant,
                $"per eval      {study.SecondsPerEvaluation:F2} s  ({study.Members} {flown}, besides the field)"));

            // The field by channel rather than by evaluation: a point that keeps the nominal
            // channels shares them and solves nothing.
            Console.Out.WriteLine(string.Create(
                invariant,
                $"field         {study.FieldSeconds:F2} s  ({study.SolvedChannels} channel solve(s), {study.LoadedChannels} load(s); "
                + $"{study.SolvingEvaluations} of {study.Evaluations} evaluations solve, up to {study.MaxParallelism} at once)"));

            var bound = study.EvaluationsAreACeiling ? " at most" : string.Empty;

            Console.Out.WriteLine(string.Create(
                invariant, $"{study.Kind,-13} {study.Evaluations} evaluations{bound}"));
        }

        Console.Out.WriteLine(string.Create(
            invariant, $"total         {EstimateCommand.Duration(outcome.Seconds)}, peak {EstimateCommand.MemorySize(outcome.MemoryMiB)}"));

        Console.Out.WriteLine();
        Console.Out.WriteLine($"basis: {outcome.Basis}");

        if (outcome.AboveThreshold)
        {
            // GRD-8: above the threshold this is a refusal to proceed *silently*, not a
            // refusal to proceed. `sweep` and the other study verbs run whatever this says;
            // what the gate buys is that the cost was seen first.
            //
            // Saying only "this is above the threshold" and exiting on a code named
            // cost-gate refusal reads as a prohibition, and an agent in the acceptance run
            // obeyed it: it cut a 2000-draw study to 1500 to get past a number that was
            // itself 26x too high. A gate that makes somebody shrink their work is doing
            // the opposite of its job, so it now says what it means and how to move it.
            var limit = string.Create(invariant, $"{outcome.ThresholdSeconds:F0}");

            Console.Error.WriteLine(
                $"this is above the {limit} s cost threshold, which is why this exits 3. "
                + "The study itself is not blocked - sweep, scan, boundary and optimise will "
                + "run it. Pass --threshold <seconds> to say the cost is expected");

            return (int)ExitCode.CostGateRefused;
        }

        return (int)ExitCode.Success;
    }

    private static int Solve(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel solve <model.json> [--json] [--project <dir>] [--no-cache]");
            return (int)ExitCode.ValidationFailure;
        }

        // Written, never read: solve exists to measure a solve, and a load would make its
        // numbers meaningless. What it keeps, the next run of the model loads.
        var modelPath = Path.GetFullPath(options.Positional[0]);
        var project = new ProjectLayout(options.Value("project") ?? InferProjectRoot(modelPath));
        var outcome = SolveCommand.Execute(
            modelPath,
            options.Has("no-cache") ? null : new FieldCache(project, read: false, announce: Console.Error.WriteLine));

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        // No empty case to print: SolveCommand refuses a model with nothing to solve
        // rather than returning an empty list, and a branch here answering exit 0 for
        // one would disagree with --json, whose Converged is false over no elements.

        // A driven element is one solve per basis channel, and --json says which is
        // which. Here each channel prints its own block, so without its number two
        // channels of one element read as the same solve printed twice, and a
        // "did not converge" cannot say which spatial pattern failed. The number is
        // the one --json carries, counted from zero. A static element has a single
        // channel and prints no number, since there is nothing to tell apart.
        var channels = outcome.Elements
            .GroupBy(e => e.Index)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var element in outcome.Elements)
        {
            var label = channels[element.Index] > 1
                ? string.Create(invariant, $"field {element.Index} channel {element.Channel}")
                : string.Create(invariant, $"field {element.Index}");

            var shape = !element.SquareCells ? "stretched"
                : element.Dimensions == 3 ? "cubic"
                : "square";

            // Every axis the element has, not the first two. A volume printed as its
            // x and y alone reads as a plane: a 257x17x257 solve reported as 257x17
            // understates the node count by the whole third axis, and drops the z
            // spacing that says whether the cells are the size that was asked for.
            var nodes = string.Join("x", element.Nodes.Select(n => n.ToString(invariant)));
            var spacing = string.Join(
                " x ", element.SpacingMm.Select(s => s.ToString("F4", invariant)));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{label}  {nodes} at {spacing} mm ({shape})"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {element.Electrodes} electrode(s), {element.FixedNodes} fixed node(s), "
                + $"{element.CutLinks} cut link(s)"));

            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {element.Cycles} cycles at factor {element.ConvergenceFactor:F4}, "
                + $"residual {element.RelativeResidual:E2} of initial"));

            // A cycle is not a unit of work, and it reads as one. At zero levels the
            // V-cycle never coarsened and the whole solve was relaxation on the finest
            // grid, which is a different answer to "how did it go" than a factor
            // alone suggests.
            var descent = element.Levels == 0
                ? "  <- not multigrid: it never coarsened"
                : string.Empty;

            Console.Out.WriteLine(string.Create(
                invariant,
                $"          {element.Levels} coarse level(s), {element.Sweeps:N0} sweep(s), "
                + $"coarsest {element.CoarsestNodes:N0} node(s){descent}"));

            Console.Out.WriteLine(string.Create(
                invariant, $"          peak |phi| {element.PeakPotentialVolts:G6} V"));

            // What a cache would key this solve on: the mesh and conductors, then this
            // channel's pattern on them. Printed in full, since a hash is compared, not read.
            Console.Out.WriteLine($"          geometry {element.GeometryHash}");
            Console.Out.WriteLine($"          channel  {element.ChannelHash}");

            if (!element.Converged)
            {
                Console.Error.WriteLine($"{label} did not converge");
            }
        }

        Console.Out.WriteLine(string.Create(
            invariant,
            $"solved in {outcome.ElapsedMs:F0} ms, solver behaviour {outcome.SolverBehaviourVersion}"));

        Warn(outcome.Warnings);

        return (int)(outcome.Converged ? ExitCode.Success : ExitCode.ConvergenceFailure);
    }

    private static int Preview(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel preview <model.json> [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = PreviewCommand.Execute(options.Positional[0]);

        if (options.Has("json"))
        {
            return Emit(
                outcome,
                outcome.Completed ? ExitCode.Success : ExitCode.ConvergenceFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        Console.Out.WriteLine(string.Create(
            invariant,
            $"flight time   {outcome.FlightTime.Value:F4} {outcome.FlightTime.Unit} (preview)"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"              {outcome.AcceptedSteps} steps at tolerance {outcome.RelativeTolerance:G3}, "
            + $"{outcome.ElapsedMs:F0} ms"));

        // GRD-5: the mark is not suppressible and does not depend on the caller
        // having thought to look for it.
        foreach (var warning in outcome.FlightTime.Warnings)
        {
            Console.Error.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)(outcome.Completed ? ExitCode.Success : ExitCode.ConvergenceFailure);
    }

    private static int Test(CommandLine options)
    {
        var root = options.Value("project")
            ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");

        var outcome = TestCommand.Execute(root, CacheFor(new ProjectLayout(root), options));

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.AllPassed ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        if (outcome.Tests.Count == 0)
        {
            // A green tick standing for no evidence at all - which this said in a
            // comment while returning success anyway. A caller that gates on the
            // exit code cannot tell "everything passed" from "there was nothing to
            // run", and those are opposite states.
            Console.Error.WriteLine($"no tests under {Path.Combine(outcome.Root, "tests")}");
            Console.Error.WriteLine(
                "a project with no tests is not a passing project, so this is not exit 0. "
                + "'einzel init' scaffolds one; 'einzel schema' describes what a test file holds");

            return (int)ExitCode.ValidationFailure;
        }

        var invariant = CultureInfo.InvariantCulture;

        foreach (var test in outcome.Tests)
        {
            var mark = test.Passed ? "ok  " : "FAIL";
            var stream = test.Passed ? Console.Out : Console.Error;
            stream.WriteLine($"{mark} {test.Name}");

            if (test.Failure is { } failure)
            {
                Console.Error.WriteLine($"       {failure}");
                continue;
            }

            foreach (var assertion in test.Assertions)
            {
                if (assertion.Observed is not { } observed)
                {
                    Console.Error.WriteLine(string.Create(
                        invariant,
                        $"       {assertion.FigureOfMerit}: nothing arrived, so there is no value to compare"));

                    continue;
                }

                var line = string.Create(
                    invariant,
                    $"       {assertion.FigureOfMerit} {observed:G8} {assertion.Unit}, expected "
                    + $"{assertion.Expected:G8}, off by {assertion.RelativeError:E2} of "
                    + $"{assertion.Tolerance:E2} allowed");

                (assertion.Passed ? Console.Out : Console.Error).WriteLine(line);
            }
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine($"{outcome.Passed} of {outcome.Tests.Count} tests passed");

        return (int)(outcome.AllPassed ? ExitCode.Success : ExitCode.ValidationFailure);
    }

    /// <summary>What a project holds, and the state of each part of it.</summary>
    /// <remarks>
    /// <para>
    /// A peer of <c>verify</c> rather than a replacement for it. Verify answers "do the
    /// stored results still stand" and walks the manifests to do it, which makes it
    /// structurally blind to a model nobody has run - the state most models in a working
    /// project are in.
    /// </para>
    /// <para>
    /// Exit code follows the same rule verify uses: a drifted result is a project that has
    /// moved on rather than an engine failure, and a model that does not validate is a
    /// thing to fix. A model that has simply never been run is neither, so it does not
    /// change the exit code.
    /// </para>
    /// </remarks>
    private static int Project(CommandLine options)
    {
        var root = options.Value("project")
            ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");

        var outcome = ProjectCommand.Execute(root);

        var wrong = outcome.Models.Count(m => !m.Valid) + outcome.Drifted;
        var code = wrong == 0 ? ExitCode.Success : ExitCode.ValidationFailure;

        if (options.Has("json"))
        {
            // Warnings go out in the wire form every other verb uses: the severity as a
            // name and GRD-3's suppressibility spelled out. Serialising the engine's own
            // record here would emit the severity as a bare integer and drop
            // `suppressible` entirely - a reader could not tell a note from a violation,
            // which is the one thing GRD-3 is about.
            //
            // The outcome keeps the engine record rather than the wire one because the
            // shell reads it too, and a property typed from Einzel.Io would pull the file
            // format into the window (UI-1). Same reason ResultsCommand has FigureEnvelope.
            return Emit(
                new
                {
                    outcome.Root,
                    outcome.Models,
                    outcome.Studies,
                    outcome.Figures,
                    outcome.Tests,
                    outcome.ExtensionNames,
                    outcome.Orphans,
                    outcome.NeverRun,
                    outcome.Drifted,
                    Warnings = outcome.Warnings.Select(w => new Io.WarningJson
                    {
                        Code = w.Code,
                        Message = w.Message,
                        Severity = w.Severity.ToString(),
                        Suppressible = w.IsSuppressible,
                    }).ToList(),
                },
                code);
        }

        Console.Out.WriteLine(outcome.Root);
        Console.Out.WriteLine();

        if (outcome.Models.Count == 0)
        {
            Console.Out.WriteLine("no models");
        }

        foreach (var model in outcome.Models)
        {
            // Three states, kept apart because they call for different things: invalid is
            // a thing to fix, stale is a thing to re-run, and never-run is neither - it is
            // simply where a model starts.
            var mark =
                !model.Valid ? "INVALID"
                : !model.Ran ? "not run"
                : model.Current ? "ok     "
                : "STALE  ";

            var stream = model.Valid && (model.Current || !model.Ran)
                ? Console.Out
                : Console.Error;

            stream.WriteLine(
                $"{mark} {model.Path}"
                + (model.TransportMode is { } mode and not "trajectory" ? $"  [{mode}]" : string.Empty));

            if (model.Problem is { } why)
            {
                Console.Error.WriteLine($"        {why}");
            }

            foreach (var drift in model.Drift)
            {
                Console.Error.WriteLine($"        {drift}");
            }

            foreach (var note in model.Notes)
            {
                Console.Out.WriteLine($"        note: {note}");
            }
        }

        foreach (var orphan in outcome.Orphans)
        {
            Console.Error.WriteLine($"ORPHAN  {orphan.Manifest} - {orphan.Model}");
        }

        Console.Out.WriteLine();

        foreach (var (label, items) in new[]
        {
            ("studies", outcome.Studies),
            ("figures", outcome.Figures),
            ("tests", outcome.Tests),
            ("extensions", outcome.ExtensionNames),
        })
        {
            if (items.Count > 0)
            {
                Console.Out.WriteLine($"{items.Count} {label}: {string.Join(", ", items)}");
            }
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine(
            $"{outcome.Models.Count} models, {outcome.NeverRun} never run, "
            + $"{outcome.Drifted} with a result that no longer stands");

        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)code;
    }

    private static int Verify(CommandLine options)
    {
        var root = options.Value("project")
            ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");

        var outcome = VerifyCommand.Execute(root);

        if (options.Has("json"))
        {
            return Emit(outcome, outcome.AllCurrent ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        if (outcome.Results.Count == 0)
        {
            Console.Out.WriteLine($"no stored results under {outcome.Root}");
            return (int)ExitCode.Success;
        }

        foreach (var result in outcome.Results)
        {
            var mark = result.Current ? "ok  " : "STALE";
            var stream = result.Current ? Console.Out : Console.Error;
            stream.WriteLine($"{mark} {result.Manifest}");

            foreach (var drift in result.Drift)
            {
                Console.Error.WriteLine($"       {drift}");
            }

            foreach (var note in result.Notes)
            {
                Console.Out.WriteLine($"       note: {note}");
            }
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine($"{outcome.Current} of {outcome.Results.Count} results still stand");

        // A stale result is not an engine failure, it is a project that has moved
        // on; the exit code says so without a caller parsing anything.
        return (int)(outcome.AllCurrent ? ExitCode.Success : ExitCode.ValidationFailure);
    }

    /// <summary>An account of a project's runs, for a person to read (Amendment 43).</summary>
    /// <remarks>
    /// <para>
    /// A verb rather than a shell view, so AGT-2 holds: nothing exists only in the window,
    /// and an agent asking what a night of runs produced gets the same account through
    /// <c>--json</c> that a person gets as a page.
    /// </para>
    /// <para>
    /// <b>The page is written and not opened.</b> Launching a browser would make this the
    /// one verb with a side effect outside the project, and a report generated on a
    /// headless machine over ssh is exactly the case that wants the file and not the
    /// window.
    /// </para>
    /// </remarks>
    private static int Report(CommandLine options)
    {
        var root = options.Value("project")
            ?? (options.Positional.Count > 0 ? options.Positional[0] : ".");

        var outcome = ReportCommand.Execute(root);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        // The page goes at the project root rather than into results/, and that is not
        // cosmetic: results/ is what `verify` walks and what this reads, so a report
        // written there would become an input to the next one.
        var target = options.Value("out")
            ?? Path.Combine(outcome.Root, "report.html");

        var page = ReportPage.Write(outcome, Path.GetFileName(outcome.Root.TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));

        if (options.Has("dry-run"))
        {
            Console.Out.WriteLine(
                $"would write {target} ({outcome.Runs.Count} run(s), {page.Length:N0} bytes)");

            return (int)ExitCode.Success;
        }

        // A named directory is created rather than refused, since `--out reports/tonight.html`
        // is the obvious way to keep a series of them and failing on the first is a poor
        // answer. The default target's directory is the project root and always exists.
        if (Path.GetDirectoryName(Path.GetFullPath(target)) is { } directory)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(target, page);

        foreach (var run in outcome.Runs)
        {
            var mark = run.Unreadable is not null ? "BROKEN"
                : run.NotRendered is not null ? "study"
                : run.Result is null ? "NOANSWER"
                : run.Current ? "ok" : "STALE";

            // A study's answer is not a fault, so it does not go to stderr with the ones
            // that need doing something about.
            var stream = mark is "ok" or "study" ? Console.Out : Console.Error;

            stream.WriteLine(
                $"{mark,-9}{run.Manifest,-46}{run.Numbers.Count,3} number(s), "
                + $"{run.Warnings.Count} warning(s)"
                // Only where there is a timeline, since a beamline has none and a count of
                // zero phases on every ordinary run is the kind of line a reader learns to
                // skip past - taking the ones that mean something with it.
                + (run.Phases.Count > 0 ? $", {run.Phases.Count} phase(s)" : ""));
        }

        // Diagnostics on stderr and the result on stdout (CLI-2), so a caller piping this
        // gets the path and nothing else.
        if (outcome.Runs.Count > 0)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                $"{outcome.Current} of {outcome.Runs.Count} run(s) still stand"
                + (outcome.WithoutResult > 0
                    ? $"; {outcome.WithoutResult} stored no result document"
                    : "")
                + (outcome.Unreadable > 0
                    ? $"; {outcome.Unreadable} stored one this build cannot read"
                    : "")
                + (outcome.NotRendered > 0
                    ? $"; {outcome.NotRendered} study answer(s) this page does not draw"
                    : "")
                + (outcome.Interrupted > 0
                    ? $"; {outcome.Interrupted} did not finish, or has not yet"
                    : ""));
        }

        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        Console.Out.WriteLine(target);

        return (int)ExitCode.Success;
    }

    private static int Export(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel export <model.json> [--mesh] [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("writes the solved potential field as VTK ImageData for ParaView");
            Console.Error.WriteLine("  --mesh  write the conductor surfaces as Wavefront OBJ instead,");
            Console.Error.WriteLine("          one named object per electrode, for any 3D renderer");
            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(modelPath);
        var layout = new ProjectLayout(root);
        var dryRun = options.Has("dry-run");

        // A mesh and a field volume are different artifacts about the same model, so this
        // is a choice of what to export rather than of how - which is why it is a flag on
        // 'export' and not a verb of its own.
        var outcome = options.Has("mesh")
            ? ExportCommand.Mesh(modelPath, layout, dryRun)
            : ExportCommand.Vtu(modelPath, layout, dryRun);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine(outcome.Written ? $"wrote {artifact}" : $"would write {artifact}");
        }

        // The files carry these in their headers; the terminal says them too, on stderr, so
        // somebody who never opens the header in a text editor still hears them (GRD-2).
        Warn(outcome.Warnings);

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// Draws a model, headlessly: vector sections and animations, and raster stills.
    /// </summary>
    /// <remarks>
    /// <c>still</c> was named here and refused for as long as nothing rasterized, because
    /// "not built yet" and "you spelled it wrong" are different problems. It is built now,
    /// and it draws what the interactive viewport draws.
    /// </remarks>
    private static int Render(string[] args, CommandLine options)
    {
        var kind = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : null;

        if (kind is "still")
        {
            return Still(options);
        }

        if (kind is "animation")
        {
            return Animation(options);
        }

        var positional = kind is null ? options.Positional : options.Positional.Skip(1).ToList();

        if (kind is not (null or "section") || positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel render section <model.json | figures/spec.json> [--out <file>] "
                + "[--at-us <t>] [--progress <seconds>]");
            Console.Error.WriteLine(
                "       [--format svg|pdf] [--equipotentials N] [--width-mm W] [--no-trajectory]");
            Console.Error.WriteLine(
                "       [--caption <text>] [--at-us <t>] [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine("draws a plane through the instrument as line work");
            Console.Error.WriteLine(
                "  --at-us  the instant to draw a driven field, or a diffusive density, at");
            Console.Error.WriteLine(
                "  --width-mm  the page width in millimetres of paper (160 is a page, 80 a column), not a window onto the model");

            return (int)ExitCode.ValidationFailure;
        }

        var given = Path.GetFullPath(positional[0]);

        // A render spec names its own model (RND-2), so either may be handed in and
        // the spec is the one that travels with the paper.
        var isSpec = ReadsAsSpec(given);

        var (spec, modelPath) = isSpec
            ? RenderCommand.ReadSpec(given)
            : (new RenderSpec { Model = given }, ModelPath: given);

        spec = spec with
        {
            Format = (options.Value("format") ?? spec.Format.ToString()).ToUpperInvariant() switch
            {
                "PDF" => FigureFormat.Pdf,
                _ => FigureFormat.Svg,
            },
            WidthMm = options.Value("width-mm") is { } width
                ? double.Parse(width, CultureInfo.InvariantCulture)
                : spec.WidthMm,
            Equipotentials = options.Value("equipotentials") is { } count
                ? int.Parse(count, CultureInfo.InvariantCulture)
                : spec.Equipotentials,
            Trajectory = !options.Has("no-trajectory") && spec.Trajectory,
            Caption = options.Value("caption") ?? spec.Caption,

            // The unit is in the flag's name, which is how --width-mm already does it.
            // A bare --at would be ambiguous between microseconds and seconds by a
            // factor of a million, and this is the same rule that makes {"energy": 4000}
            // a validation error in a model document.
            AtSeconds = options.Value("at-us") is { } at
                ? double.Parse(at, CultureInfo.InvariantCulture) * 1e-6
                : spec.AtSeconds,
        };

        var root = options.Value("project") ?? InferProjectRoot(modelPath);

        if (!RenderProgress(options, out var sectionProgress))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = RenderCommand.Section(
            modelPath, new ProjectLayout(root), spec, options.Value("out"), options.Has("dry-run"),
            sectionProgress);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        foreach (var artifact in outcome.Artifacts)
        {
            Console.Out.WriteLine(outcome.Written ? $"wrote {artifact}" : $"would write {artifact}");
        }

        Console.Out.WriteLine(
            $"{outcome.PageMm[0]:F0} by {outcome.PageMm[1]:F0} mm, "
            + $"{outcome.Paths.Values.Sum()} paths, {outcome.TextRuns} labels");

        Console.Out.WriteLine(
            $"decimated to {outcome.DecimationToleranceMm:G3} mm; trajectory "
            + $"{outcome.TrajectoryPointsSampled} points to {outcome.TrajectoryPoints}");

        // GRD-2: onto stderr, so a warning is not lost in a pipe that keeps stdout.
        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>Draws a model to a PNG, as the interactive viewport would show it.</summary>
    /// <remarks>
    /// <para>
    /// The pixel sizes carry their unit in the flag's name, the way <c>--width-mm</c> and
    /// <c>--at-us</c> do: a bare <c>--width</c> beside a section's millimetres would be one
    /// word meaning two things in one verb.
    /// </para>
    /// <para>
    /// Warnings go to stderr (CLI-2) and into the PNG itself (GRD-2), and a validity
    /// violation also puts a hatched band across the bottom of the picture (RND-11).
    /// </para>
    /// </remarks>
    private static int Still(CommandLine options)
    {
        var positional = options.Positional.Skip(1).ToList();

        if (positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel render still <model.json> [--out <file.png>] "
                + "[--view " + string.Join("|", ViewportPicture.Views.Keys) + "]");
            Console.Error.WriteLine(
                "       [--width-px N] [--height-px N] [--see-through] [--no-scale] [--dry-run] [--json]");

            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = positional[0];
        var view = options.Value("view") ?? "iso";

        if (!ViewportPicture.Views.ContainsKey(view))
        {
            Console.Error.WriteLine(
                $"no view named '{view}'; the named views are "
                + string.Join(", ", ViewportPicture.Views.Keys));

            return (int)ExitCode.ValidationFailure;
        }

        // TryParse rather than Parse: a typo such as "1600px" is the caller's mistake, and
        // Parse would throw it through to the handler for defects in the engine, exit 6.
        int? Pixels(string flag, int otherwise)
        {
            if (options.Value(flag) is not { } given)
            {
                return otherwise;
            }

            if (int.TryParse(given, System.Globalization.NumberStyles.None, CultureInfo.InvariantCulture, out var pixels))
            {
                return pixels;
            }

            Console.Error.WriteLine($"--{flag} '{given}' is not a whole number of pixels; a still is between 16 and 8192 pixels on each side");

            return null;
        }

        if (Pixels("width-px", 1600) is not { } width || Pixels("height-px", 1000) is not { } height)
        {
            return (int)ExitCode.ValidationFailure;
        }

        if (width is < 16 or > 8192 || height is < 16 or > 8192)
        {
            Console.Error.WriteLine("a still is between 16 and 8192 pixels on each side");

            return (int)ExitCode.ValidationFailure;
        }

        var root = options.Value("project") ?? InferProjectRoot(modelPath);

        var outcome = StillCommand.Execute(
            modelPath,
            new ProjectLayout(root),
            view,
            width,
            height,
            options.Has("see-through"),
            options.Value("out"),
            options.Has("dry-run"),
            scale: !options.Has("no-scale"));

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        Console.Out.WriteLine(outcome.Written ? $"wrote {outcome.Artifact}" : $"would write {outcome.Artifact}");

        Console.Out.WriteLine(
            $"{outcome.Width} by {outcome.Height} px, {outcome.View} view"
            + (outcome.SeeThrough ? ", conductors translucent" : string.Empty)
            + $"; {outcome.Electrodes} electrodes, {outcome.FieldLevels} equipotential levels, "
            + $"{outcome.Trajectories} trajectories, {outcome.DensityShells} density contours"
            + (outcome.ScaleMm is { } arm
                ? $"; ruler arms {ScaleIndicator.Format(arm)} along {string.Join(", ", outcome.ScaleAxes)}, {outcome.ScaleCorner}"
                : string.Empty)
            + (outcome.Tainted ? "; marked with a hatched band for a validity violation" : string.Empty));

        // GRD-2: onto stderr, so a warning is not lost in a pipe that keeps stdout.
        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>
    /// Draws a flight as a numbered sequence of vector frames (RND-7).
    /// </summary>

    /// <summary>
    /// Draws a flight as a numbered sequence of vector frames (RND-7).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A render spec, never a bare model, and that is the requirement enforcing
    /// itself through the interface.</b> RND-7 says an animation "declares an explicit
    /// non-linear time mapping ... neither part is optional", so there is deliberately
    /// no <c>--rate</c> flag: the only way to ask for an animation is to have written
    /// down how it compresses time. A convenience flag would have made the hidden
    /// compression the easy case.
    /// </para>
    /// <para>
    /// <c>--fps</c> is offered because a frame rate is a property of the playback
    /// device rather than of the physics, and changing it changes no claim the
    /// animation makes.
    /// </para>
    /// </remarks>
    private static int Animation(CommandLine options)
    {
        var positional = options.Positional.Skip(1).ToList();

        if (positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel render animation <spec.json> [--out <dir>] [--format svg|pdf] "
                + "[--fps <n>] [--progress <seconds>]");
            Console.Error.WriteLine(
                "       [--fps N] [--width-mm W] [--project <dir>] [--dry-run] [--json]");
            Console.Error.WriteLine(
                "draws a flight as numbered vector frames on the spec's declared time mapping");
            Console.Error.WriteLine();
            Console.Error.WriteLine(
                "An animation needs a render spec, not a bare model: RND-7 requires the time "
                + "mapping to be declared, so there is no flag that supplies one.");

            return (int)ExitCode.ValidationFailure;
        }

        var given = Path.GetFullPath(positional[0]);

        if (!ReadsAsSpec(given))
        {
            Console.Error.WriteLine(
                $"{given} reads as a model, and an animation needs a render spec that declares "
                + "its time mapping.");

            Console.Error.WriteLine(
                "Write one with an \"animation\" block: {\"model\": \"...\", \"animation\": "
                + "{\"framesPerSecond\": 30, \"phases\": [{\"until\": {\"value\": 10, "
                + "\"unit\": \"us\"}, \"rate\": {\"value\": 1, \"unit\": \"us/s\"}}]}}");

            return (int)ExitCode.ValidationFailure;
        }

        var (spec, modelPath) = RenderCommand.ReadSpec(given);

        spec = spec with
        {
            Format = (options.Value("format") ?? spec.Format.ToString()).ToUpperInvariant() switch
            {
                "PDF" => FigureFormat.Pdf,
                _ => FigureFormat.Svg,
            },
            WidthMm = options.Value("width-mm") is { } width
                ? double.Parse(width, CultureInfo.InvariantCulture)
                : spec.WidthMm,
            Caption = options.Value("caption") ?? spec.Caption,
            Animation = options.Value("fps") is { } fps && spec.Animation is { } declared
                ? declared with { FramesPerSecond = int.Parse(fps, CultureInfo.InvariantCulture) }
                : spec.Animation,
        };

        var root = options.Value("project") ?? InferProjectRoot(modelPath);

        if (!RenderProgress(options, out var animationProgress))
        {
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = RenderCommand.Animation(
            modelPath, new ProjectLayout(root), spec, options.Value("out"), options.Has("dry-run"),
            animationProgress);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        // The directory and a count, not three hundred lines of "wrote". A frame list
        // is what --json is for.
        var directory = Path.GetDirectoryName(outcome.Artifacts[0]);

        Console.Out.WriteLine(
            outcome.Written
                ? $"wrote {outcome.Frames} frames to {directory}"
                : $"would write {outcome.Frames} frames to {directory}");

        Console.Out.WriteLine(
            $"{outcome.PlaybackSeconds:F2} s of playback, "
            + $"{outcome.PageMm[0]:F0} by {outcome.PageMm[1]:F0} mm each");

        // GRD-2: onto stderr, so a warning is not lost in a pipe that keeps stdout.
        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)ExitCode.Success;
    }

    /// <summary>Whether a file is a render spec rather than a model.</summary>
    /// <remarks>
    /// By what it declares, not by where it sits: a spec carries
    /// <c>renderSpecVersion</c> and a model carries <c>schemaVersion</c>. Guessing
    /// from the folder would make <c>figures/</c> load-bearing, and a file moved is
    /// then a file broken.
    /// </remarks>
    private static bool ReadsAsSpec(string path)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));

            return document.RootElement.TryGetProperty("renderSpecVersion", out _)
                || document.RootElement.TryGetProperty("kind", out _);
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>The field cache: what it holds, and deleting it (FLD-3, PRJ-4).</summary>
    /// <remarks>
    /// <c>clear</c> and <c>prune</c> mutate, so both take <c>--dry-run</c> (CLI-4). Deleting
    /// the cache never loses anything: every entry is a solve the next run can repeat.
    /// </remarks>
    private static int Cache(string[] args, CommandLine options)
    {
        var action = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : "status";

        // Found as run finds it, by walking up to the directory that holds models/, so the
        // cache a run in models/ wrote is the cache this sees from there.
        var start = Path.GetFullPath(options.Positional.Count > 1 ? options.Positional[1] : ".");
        var project = options.Value("project") is { } named
            ? new ProjectLayout(named)
            : ProjectLayout.Find(start) ?? new ProjectLayout(start);
        var dryRun = options.Has("dry-run");
        var invariant = CultureInfo.InvariantCulture;

        switch (action)
        {
            case "status":
            {
                var status = FieldCache.Status(project);

                if (options.Has("json"))
                {
                    return Emit(status);
                }

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"{status.Directory}, solver-behaviour version {status.SolverBehaviourVersion}, {status.ComputePath} path"));
                Console.Out.WriteLine(string.Create(
                    invariant, $"  {status.Entries} channel(s), {EstimateCommand.MemorySize(status.Bytes / 1048576.0)}"));
                var others = EstimateCommand.MemorySize(status.OtherVersionBytes / 1048576.0);
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  other versions or paths: {status.OtherVersionEntries} channel(s), {others}; orphaned files {status.Orphans}, temporary files {status.Temporary}"));

                return (int)ExitCode.Success;
            }

            case "clear":
            case "prune":
            {
                var cleanup = action == "clear"
                    ? FieldCache.Clear(project, dryRun)
                    : FieldCache.Prune(project, dryRun);

                if (options.Has("json"))
                {
                    return Emit(cleanup);
                }

                foreach (var file in cleanup.Removed)
                {
                    Console.Out.WriteLine($"{(dryRun ? "would delete" : "deleted")} {file}");
                }

                var size = EstimateCommand.MemorySize(cleanup.Bytes / 1048576.0);
                var dry = dryRun ? "; nothing deleted (--dry-run)" : string.Empty;
                Console.Out.WriteLine(string.Create(invariant, $"{cleanup.Removed.Count} file(s), {size}{dry}"));

                return (int)ExitCode.Success;
            }

            default:
                Console.Error.WriteLine("usage: einzel cache status|clear|prune [dir] [--dry-run] [--json]");
                return (int)ExitCode.ValidationFailure;
        }
    }

    /// <summary>The extension authoring loop: list, test, register.</summary>
    private static int Ext(string[] args, CommandLine options)
    {
        var action = args.Length > 1 && !args[1].StartsWith('-') ? args[1] : "list";
        var rest = options.Positional.Skip(1).ToList();

        var root = options.Value("project") ?? InferProjectRoot(Directory.GetCurrentDirectory());
        var project = new ProjectLayout(root);

        switch (action)
        {
            case "list":
            {
                var outcome = ExtensionCommand.List(project);

                if (options.Has("json"))
                {
                    return Emit(outcome);
                }

                Console.Out.WriteLine(
                    $"engine {outcome.EngineVersion}, interpreter {outcome.Interpreter ?? "NOT FOUND"}");

                if (outcome.Extensions.Count == 0)
                {
                    Console.Out.WriteLine("no extensions installed");
                }

                foreach (var entry in outcome.Extensions)
                {
                    // LIC-2. An extension is third-party code run against this engine,
                    // and a missing licence is printed as MISSING rather than left off the
                    // line: the one extension worth asking about must not look like the
                    // ones that answered.
                    Console.Out.WriteLine(
                        $"  {entry.Name} {entry.Version}  {entry.Kind}/{entry.Trust}"
                        + $"  licence {entry.Licence ?? "NOT DECLARED"}"
                        + $"{(entry.Incompatibility is null ? string.Empty : "  INCOMPATIBLE: " + entry.Incompatibility)}");
                }

                // EXT-3 asks for OS-level isolation this build does not apply, and
                // somebody deciding whether to run agent-authored code needs to know
                // that before they run it rather than after.
                Console.Error.WriteLine();
                Console.Error.WriteLine("the subprocess runner does NOT enforce:");

                foreach (var gap in outcome.UnenforcedContainment)
                {
                    Console.Error.WriteLine($"  - {gap}");
                }

                return (int)ExitCode.Success;
            }

            case "test":
            {
                if (rest.Count == 0)
                {
                    Console.Error.WriteLine(
                        "usage: einzel ext test <name> [--input <file.json>] [--project <dir>] [--json]");
                    return (int)ExitCode.ValidationFailure;
                }

                var payload = options.Value("input") is { } file
                    ? System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.GetFullPath(file)))
                    : new System.Text.Json.Nodes.JsonObject();

                var outcome = ExtensionCommand.Test(project, rest[0], payload);

                if (options.Has("json"))
                {
                    return Emit(outcome);
                }

                Console.Out.WriteLine(
                    $"{outcome.Name} {outcome.Version} returned in {outcome.ElapsedMs:F0} ms:");

                Console.Out.WriteLine(outcome.Output?.ToJsonString() ?? "null");

                if (outcome.Diagnostics is { } diagnostics)
                {
                    Console.Error.Write(diagnostics);
                }

                foreach (var warning in outcome.Warnings)
                {
                    Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
                }

                return (int)ExitCode.Success;
            }

            case "register":
            {
                if (rest.Count == 0)
                {
                    Console.Error.WriteLine(
                        "usage: einzel ext register <name> [--kind objective|analysis|geometry|"
                        + "sequence|interchange] [--project <dir>] [--dry-run] [--json]");
                    return (int)ExitCode.ValidationFailure;
                }

                var kind = Enum.TryParse<Einzel.Extensions.ExtensionKind>(
                    options.Value("kind") ?? "objective", ignoreCase: true, out var parsed)
                    ? parsed
                    : Einzel.Extensions.ExtensionKind.Objective;

                var created = ExtensionCommand.Register(project, rest[0], kind, options.Has("dry-run"));

                foreach (var file in created)
                {
                    Console.Out.WriteLine(options.Has("dry-run") ? $"would write {file}" : $"wrote {file}");
                }

                return (int)ExitCode.Success;
            }

            default:
                Console.Error.WriteLine($"unknown 'ext' action '{action}'");
                Console.Error.WriteLine("usage: einzel ext list | test <name> | register <name>");
                return (int)ExitCode.ValidationFailure;
        }
    }

    /// <summary>
    /// Runs both transport modes on one model and reports the disagreement.
    /// </summary>
    /// <remarks>
    /// REG-3 makes this a supported operation with its own report rather than
    /// something a careful user assembles by hand. In the overlap band both
    /// descriptions run and neither is obviously right; the engine's job is to say
    /// by how much they differ, not to pick one.
    /// </remarks>
    private static int Compare(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel compare <model.json> [--ions N] [--json]");
            Console.Error.WriteLine(
                "runs trajectory integration and statistical diffusion on the same model");
            return (int)ExitCode.ValidationFailure;
        }

        var ions = options.Value("ions") is { } count
            ? int.Parse(count, CultureInfo.InvariantCulture)
            : 60;

        var outcome = ModeComparison.Execute(Path.GetFullPath(options.Positional[0]), ions);

        if (options.Has("json"))
        {
            return Emit(outcome);
        }

        var invariant = CultureInfo.InvariantCulture;

        Console.Out.WriteLine(string.Create(
            invariant,
            $"{outcome.PressureMbar:G3} mbar, "
            + $"{(outcome.InOverlapBand ? "inside" : "OUTSIDE")} the band where both modes apply"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"trajectory   {outcome.TrajectoryTransitUs:F3} +/- {outcome.TrajectoryStandardErrorUs:F3} us "
            + $"({outcome.Ions} ions, {outcome.TrajectoryTransmission:P0} arrived)"));

        Console.Out.WriteLine(string.Create(
            invariant,
            $"diffusion    {outcome.DiffusionTransitUs:F3} us "
            + $"({outcome.DiffusionTransmission:P0} arrived)"));

        if (outcome.DifferenceUs is not null)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"disagreement {outcome.DifferenceUs:F3} us, {outcome.RelativeDifference:P2}, "
                + $"{outcome.StandardErrors:F2} standard errors"));
        }

        foreach (var warning in outcome.Warnings)
        {
            Console.Error.WriteLine($"[{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        return (int)ExitCode.Success;
    }

    private static int Init(CommandLine options)
    {
        var root = options.Positional.Count > 0 ? options.Positional[0] : ".";

        if (options.Has("dry-run"))
        {
            Console.Out.WriteLine($"would create a project at {Path.GetFullPath(root)}");
            return (int)ExitCode.Success;
        }

        var outcome = InitCommand.Execute(root, withGit: options.Value("vcs") == "git");

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(outcome));
            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine(outcome.AlreadyExisted
            ? $"Project already present at {outcome.Root}; missing pieces added."
            : $"Created project at {outcome.Root}");

        foreach (var item in outcome.Created)
        {
            Console.Out.WriteLine($"  {item}");
        }

        Console.Out.WriteLine();
        Console.Out.WriteLine("Next: einzel run models/reflectron.json --vtu");
        return (int)ExitCode.Success;
    }

    /// <summary>The model's declared parameter surface, and whether it validates.</summary>
    /// <remarks>
    /// <para>
    /// <b>A verb rather than a shell method, because AGT-2 says nothing exists only in
    /// the shell.</b> The window needs this to build a model tree without parsing the
    /// document itself, which UI-1 forbids it from knowing how to do - and an agent needs
    /// exactly the same thing for exactly the same reason. `einzel schema` says what a
    /// model <em>may</em> contain; this says what one <em>does</em>.
    /// </para>
    /// <para>
    /// It answers for a document that does not validate too, with the errors alongside.
    /// That is what live validation needs: a person editing a parameter into an invalid
    /// state must still see the tree, rather than have it vanish until they undo what
    /// they typed.
    /// </para>
    /// </remarks>
    private static int Outline(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel outline <model.json> [--set <parameter>=<value>]... [--json] [--dry-run]");

            return (int)ExitCode.ValidationFailure;
        }

        var model = options.Positional[0];

        // Setting one is the same verb, because reading the knobs and turning one are the
        // same conversation. The value is in the parameter's own declared unit: a person
        // editing a 5 mm radius types 7, and demanding 0.007 would ask them to do the
        // conversion the format exists to make unnecessary.
        // `--set` with nothing after it is a mistake, not a request to do nothing. The
        // malformed `--set name` case below is already refused, so silently listing the
        // outline here would make one kind of malformed set loud and the other silent.
        if (options.Has("set") && options.Value("set") is null)
        {
            Console.Error.WriteLine(
                "--set takes <parameter>=<value>, for example --set inscribedRadius=7");

            return (int)ExitCode.ValidationFailure;
        }

        // Every occurrence, not the last. `--set a=1 --set b=2` under a last-wins read
        // applies b, drops a, exits 0 and says nothing - which is how an agent that
        // batched two edits lost one and believed both had landed.
        var assignments = options.Values("set");

        if (assignments.Count > 0)
        {
            var edited = File.ReadAllText(Path.GetFullPath(model));
            var applied = new List<(string Name, double Value)>(assignments.Count);

            foreach (var assignment in assignments)
            {
                var split = assignment?.IndexOf('=', StringComparison.Ordinal) ?? -1;

                if (assignment is null || split <= 0)
                {
                    Console.Error.WriteLine(
                        "--set takes <parameter>=<value>, for example --set inscribedRadius=7");

                    return (int)ExitCode.ValidationFailure;
                }

                var name = assignment[..split];

                if (!double.TryParse(
                    assignment[(split + 1)..],
                    System.Globalization.NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var value))
                {
                    Console.Error.WriteLine(
                        $"'{assignment[(split + 1)..]}' is not a number. The value is in the "
                        + "parameter's own declared unit, so write the magnitude alone");

                    return (int)ExitCode.ValidationFailure;
                }

                edited = OutlineCommand.WithParameter(model, name, value, edited);
                applied.Add((name, value));
            }

            // CLI-4: --dry-run says what would be written and writes nothing. This wrote,
            // and two agents in the acceptance run hit it - one of them lost a model
            // mid-task to the flag they had reached for in order to be careful. A
            // --dry-run that mutates is worse than none, because it is the flag people
            // use when they want to be safe.
            if (options.Has("dry-run"))
            {
                foreach (var (name, value) in applied)
                {
                    Console.Error.WriteLine(
                        string.Create(CultureInfo.InvariantCulture, $"would set {name} to {value:G6}"));
                }

                Console.Error.WriteLine($"would write {model} - nothing was written");
            }
            else
            {
                File.WriteAllText(Path.GetFullPath(model), edited);

                // A mutating command whose output is indistinguishable from a read-only
                // one is how an edit goes unnoticed, and it is what made the dry-run bug
                // invisible for three commands.
                foreach (var (name, value) in applied)
                {
                    Console.Error.WriteLine(
                        string.Create(CultureInfo.InvariantCulture, $"set {name} to {value:G6}"));
                }

                Console.Error.WriteLine($"wrote {model}");
            }
        }

        var outline = OutlineCommand.Execute(model);

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(outline));

            return (int)(outline.Valid ? ExitCode.Success : ExitCode.ValidationFailure);
        }

        var invariant = CultureInfo.InvariantCulture;

        Console.Out.WriteLine($"{outline.Name ?? "(unnamed)"}  schema {outline.SchemaVersion}");

        foreach (var parameter in outline.Parameters)
        {
            // The value and its unit are two halves of one statement and never appear
            // apart, which is GRD-1's habit applied to an input rather than a result.
            var shown = parameter.Expression is { } expression
                ? expression
                : string.Create(invariant, $"{parameter.Value:G6}");

            var bounds = OutlineCommand.BoundsText(parameter);

            Console.Out.WriteLine(string.Create(
                invariant,
                $"  {parameter.Name,-20} {shown,-32} {parameter.Unit,-8}"
                + $"{(bounds is null ? string.Empty : "  " + bounds)}"
                + $"{(parameter.Editable ? string.Empty : "  (derived)")}"));
        }

        if (outline.Valid)
        {
            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine();

        foreach (var error in outline.Errors)
        {
            Console.Error.WriteLine($"  [{error.Code}] {error.Path}: {error.Constraint}");
        }

        return (int)ExitCode.ValidationFailure;
    }

    private static int Validate(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine("usage: einzel validate <model.json> [--json]");
            return (int)ExitCode.ValidationFailure;
        }

        var outcome = RunCommand.Validate(options.Positional[0]);

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(outcome));
            return (int)outcome.ExitCode;
        }

        if (outcome.Valid)
        {
            Console.Out.WriteLine($"OK  {outcome.ModelPath}");
            Console.Out.WriteLine($"    schema {outcome.SchemaVersion}, {outcome.ModelHash}");
            return (int)ExitCode.Success;
        }

        Console.Error.WriteLine($"{outcome.Errors.Count} problem(s) in {outcome.ModelPath}:");
        Console.Error.WriteLine();

        foreach (var error in outcome.Errors)
        {
            WriteError(error);
        }

        return (int)outcome.ExitCode;
    }

    private static int Run(CommandLine options)
    {
        if (options.Positional.Count == 0)
        {
            Console.Error.WriteLine(
                "usage: einzel run <model.json> [--vtu] [--json] [--project <dir>] "
                + "[--progress <seconds>] [--no-cache]");
            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = Path.GetFullPath(options.Positional[0]);
        var root = options.Value("project") ?? InferProjectRoot(modelPath);
        var project = new ProjectLayout(root);

        // WATCHED BY DEFAULT, and that is the decision rather than the plumbing. A flag
        // somebody has to remember is a flag that is not set on the run that gets killed -
        // and three attempts at the TIMS front-end sequence were lost with nothing on disk
        // to say how far they had got, the last of them to a Windows update. Thirty
        // seconds, so a run of a few seconds says one thing and an eight-hour run says a
        // thousand; `--progress 0` asks for silence.
        var interval = 30.0;

        if (options.Value("progress") is { } asked)
        {
            if (!double.TryParse(
                    asked, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out interval))
            {
                Console.Error.WriteLine(
                    $"--progress takes an interval in seconds, and '{asked}' is not one. "
                    + "Use 0 to turn it off.");
                return (int)ExitCode.ValidationFailure;
            }
        }

        var (run, validation) = RunCommand.Execute(
            modelPath,
            project,
            exportVtu: options.Has("vtu"),
            timestampUtc: DateTimeOffset.UtcNow,

            // Diagnostics on stderr (CLI-2), so a caller piping `--json` gets the result
            // document and nothing else however long the run took to produce it.
            progress: new RunProgress(interval, Console.Error.WriteLine),

            // On by default: a second run of an unchanged geometry loads its solved channels
            // from .einzel/cache instead of solving them again (FLD-3).
            fieldCache: CacheFor(project, options));

        if (run is null)
        {
            if (options.Has("json"))
            {
                Console.Out.Write(CommandJson.Write(validation));
            }
            else
            {
                Console.Error.WriteLine($"{validation.Errors.Count} problem(s) in {validation.ModelPath}:");
                Console.Error.WriteLine();

                foreach (var error in validation.Errors)
                {
                    WriteError(error);
                }
            }

            return (int)validation.ExitCode;
        }

        if (options.Has("json"))
        {
            Console.Out.Write(CommandJson.Write(run));
        }
        else
        {
            WriteRun(run);
        }

        // A regime violation outranks an incomplete flight, because it explains it:
        // an ion that never reaches the detector at 1 mbar has not failed to
        // converge, it has been described by the wrong physics. CLI-3 gives the two
        // distinct codes so a caller can tell them apart without parsing anything,
        // and REG-2 is what code 2 is for.
        if (run.FlightTime.Warnings.Any(
            w => w.Code.StartsWith("regime.", StringComparison.Ordinal)
                && w.Severity == nameof(WarningSeverity.ValidityViolation)))
        {
            return (int)ExitCode.RegimeViolation;
        }

        // A run that ended anywhere but the detector is a convergence failure, not
        // a success with a caveat. A diffusive run has no detector to end at - it
        // evolves a density for the declared time and reports where the ions went -
        // so it succeeds by finishing, and what it transmitted is a figure rather
        // than an outcome. A sequenced run is the same: it succeeds by completing its
        // sequence, and where the ions went is in the phase table.
        //
        // This list is now three long, and every entry was added after a working run
        // reported itself as a failure - the diffusive one, then this. A run that
        // finished what it was asked to do is a success, and the thing to ask is
        // whether it finished rather than which mode it was in.
        // The question, rather than a list of the outcome names that answered it when
        // this line was written. That list had to be widened once for diffusive runs and
        // once for sequenced ones, and still called six of the thirty-seven shipped
        // examples failures - three traps and thermalisations ending at their declared
        // hold, and three deliberate losses that are the control halves of pairs.
        return run.Completed ? (int)ExitCode.Success : (int)ExitCode.ConvergenceFailure;
    }

    private static int Fly(CommandLine options)
    {
        if (options.Positional.Count == 0 || options.Value("ions") is not { } ions)
        {
            Console.Error.WriteLine(
                "usage: einzel fly <model.json> --ions <list.ions.json|.csv> [--json] [--project <dir>] "
                + "[--no-cache] [--dry-run] [--ladder] [--vtu]");
            return (int)ExitCode.ValidationFailure;
        }

        var modelPath = Path.GetFullPath(options.Positional[0]);
        var project = new ProjectLayout(options.Value("project") ?? InferProjectRoot(modelPath));

        var (fly, validation, errorsIn) = FlyCommand.Execute(modelPath, ions, project, new FlyOptions
        {
            DryRun = options.Has("dry-run"),
            Ladder = options.Has("ladder"),
            ExportVtu = options.Has("vtu"),
            FieldCache = CacheFor(project, options),
            TimestampUtc = DateTimeOffset.UtcNow,

            // Diagnostics on stderr (CLI-2), as a run's are.
            Announce = Console.Error.WriteLine,
        });

        if (fly is null)
        {
            if (options.Has("json"))
            {
                Console.Out.Write(CommandJson.Write(validation));
            }
            else
            {
                Console.Error.WriteLine($"{validation.Errors.Count} problem(s) in {errorsIn}:");
                Console.Error.WriteLine();

                foreach (var error in validation.Errors)
                {
                    WriteError(error);
                }
            }

            return (int)validation.ExitCode;
        }

        if (options.Has("json"))
        {
            // The same bytes as the stored result, which lists itself among its artifacts.
            Console.Out.Write(CommandJson.Write(fly));
            return (int)fly.ExitCode;
        }

        var invariant = CultureInfo.InvariantCulture;

        if (fly.DryRun)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"would fly {fly.Launched} ion(s) from {fly.IonsPath} through {fly.Manifest.ModelPath} on up to {fly.MaxParallelism} thread(s)"));
            Console.Out.WriteLine(string.Create(
                invariant,
                $"  seeds [{string.Join(", ", fly.Manifest.Seeds)}]; field: {fly.CachedChannels} of {fly.Channels} channel(s) in the cache"));

            // The cold cost before it is paid: a group whose channels are not cached solves them.
            foreach (var group in fly.Manifest.OverrideGroups ?? [])
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  group {group.Index} ({group.Describe()}): {group.Rows} ion(s), {group.CachedChannels} of {group.ChannelHashes.Count} channel(s) in the cache"));
            }

            return (int)ExitCode.Success;
        }

        Console.Out.WriteLine(
            string.Create(invariant, $"flew {fly.Launched} ion(s): ")
            + string.Join(", ", fly.Outcomes.Select(pair => string.Create(invariant, $"{pair.Value} {pair.Key}"))));

        if (fly.Surfaces.Count > 0)
        {
            Console.Out.WriteLine("  surfaces: " + string.Join(", ", fly.Surfaces.Select(pair => string.Create(invariant, $"{pair.Value} {pair.Key}"))));
        }

        foreach (var warning in fly.Warnings)
        {
            var stream = warning.Suppressible ? Console.Out : Console.Error;
            stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        foreach (var artifact in fly.Artifacts)
        {
            Console.Out.WriteLine($"  wrote {artifact}");
        }

        return (int)fly.ExitCode;
    }

    private static void WriteRun(RunOutcome run)
    {
        var invariant = CultureInfo.InvariantCulture;
        var flight = run.FlightTime;
        var halfWidth = (flight.Uncertainty.Upper - flight.Uncertainty.Lower) / 2.0;

        // A density has no flight time, no energy drift and no final position: it
        // is a field over a whole grid, not an ion with a history. Those three
        // lines used to be printed anyway - the flight time as "NaN +/- NaN", and
        // the final position by indexing an empty list, which threw and reported a
        // diffusive run as a defect in einzel.
        //
        // Absent rather than not-a-number, which is the rule the rest of this surface
        // already follows: a reader cannot tell a missing measurement from a failed one
        // if both print the same way.
        //
        // Asked of the run rather than derived from which blocks it filled. This line was
        // `run.Diffusion is null`, then `run.Diffusion is null && run.Sequence is null` -
        // each revision adding the mode that had just been caught printing
        // `flight time NaN +/- NaN`, and a mixture would have been the third addition. A
        // list of the modes known when the line was written is not the question it is
        // asking, so the run answers the question instead.
        var trajectory = run.HasFlightTime;

        if (trajectory)
        {
            // GRD-1 in the terminal: the value never appears without what qualifies
            // it. ASCII rather than a plus-minus sign, because the console encoding
            // is not ours to assume and a mangled character in a reported
            // uncertainty is exactly the wrong place to be clever.
            Console.Out.WriteLine(string.Create(
                invariant,
                $"flight time   {flight.Value:F6} +/- {halfWidth:G3} {flight.Unit}"));
        }

        // A null observed order means the refinements agreed to the last bit, so
        // there is no order to report. Saying "converged to round-off" is the
        // honest rendering; printing an empty number would read as a missing
        // measurement rather than a perfect one.
        var convergence = flight.Evidence.ObservedOrder is { } order
            ? string.Create(invariant, $"observed order {order:G3} of {flight.Evidence.NominalOrder:G3}")
            : "residual at round-off, no order to resolve";

        if (trajectory)
        {
            Console.Out.WriteLine(
                $"              {flight.Evidence.Kind} in {flight.Evidence.Measure}, {convergence}");

            // Said in words rather than printed as NaN. There are two ways to have no
            // energy invariant to measure - a driven field does work deliberately, and an
            // ion launched at rest at zero potential has no scale to be relative to - and
            // both used to reach this line as a number that meant nothing.
            Console.Out.WriteLine(
                double.IsNaN(run.MaximumRelativeEnergyDrift)
                    ? "energy drift  not measured: no conserved scale, because the field "
                      + "does work or the ion started at rest at zero potential"
                    : string.Create(
                        invariant,
                        $"energy drift  {run.MaximumRelativeEnergyDrift:E2} relative (ACC-4 budget 1e-6)"));
        }

        if (run.Sequence is null)
        {
            Console.Out.WriteLine(string.Create(
                invariant,
                $"steps         {run.AcceptedSteps}, {run.AnalyticDriftDistanceM:F4} m advanced analytically"));
        }

        if (run.FinalPositionMm.Count > 0)
        {
            // A sequenced run has no single ion, so what this is is the packet's centre
            // - said in the label rather than left for the reader to assume.
            var what = run.Sequence is null ? "final x      " : "packet centre";

            Console.Out.WriteLine(string.Create(
                invariant,
                $"{what} {run.FinalPositionMm[0]:F6} mm"));
        }

        if (run.Sequence is { } sequence)
        {
            Console.Out.WriteLine();

            foreach (var phase in sequence.Phases)
            {
                // The trajectory count and the population are different quantities, and
                // a conversion re-samples the first while carrying the second - so both
                // are shown, and a diffusive phase shows a dash rather than a zero,
                // because it has no trajectories rather than none left.
                var carried = phase.Mode == "trajectory"
                    ? phase.Trajectories.ToString(invariant)
                    : "-";
                // The width beside the centre, because for a mobility analyser the two
                // together are the measurement: where a packet parks is which mobility it
                // has, and how wide it is when the ramp releases it is the floor on the
                // resolving power. A dash rather than a zero where there is nothing to take
                // a width over, on the same argument as the trajectory count above.
                var wide = phase.SpreadMm is { Count: > 0 } spread
                    ? string.Create(invariant, $"{spread[0],7:F3}")
                    : "      -";

                // A phase the packet never reached has no centre to report, which is a
                // different statement from a centre at the origin - the same argument the
                // dash above makes for a trajectory count. Padded to the width of the
                // populated form so the `converted` marker stays in one column: this is a
                // table, and a row that is shorter than its neighbours moves the column.
                var centre = phase.CentroidMm.Count > 0
                    ? string.Create(
                        invariant, $"x {phase.CentroidMm[0],8:F3} +- {wide} mm")
                    : "packet empty";

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  {phase.Name,-12} {phase.Mode,-11} to {phase.EndsAtUs,8:F2} us  "
                    + $"{phase.Population,10:G6} ions in {carried,5} trajectories  "
                    + $"{centre,-24}"
                    + $"{(phase.Converted ? "  converted" : string.Empty)}"));
            }

            Console.Out.WriteLine();

            Console.Out.WriteLine(string.Create(
                invariant,
                $"sequence      {sequence.Conversions} mode conversion(s), "
                + $"{sequence.ArrivedIons:G6} ions arrived"));

            foreach (var loss in sequence.Losses)
            {
                Console.Out.WriteLine(string.Create(
                    invariant, $"  lost on {loss.Surface}: {loss.Ions:G6} ions"));
            }
        }

        if (run.Mixture is { } mixture)
        {
            Console.Out.WriteLine();

            // Per species, because that is the whole of what a mixture run has to say: one
            // line of totals would be a report of the sum of two populations that were kept
            // apart on purpose.
            foreach (var member in mixture.Species)
            {
                var derived = member.MobilityDerived ? " (derived)" : string.Empty;

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  {member.Name,-14} K {member.MobilitySi:G6} m2/(V s){derived,-10}  "
                    + $"{member.Remaining:G6} of {member.Launched:G6} ions held, "
                    + $"{member.Transmission:P1} through, x {member.CentroidMm:F3} mm"));

                foreach (var loss in member.Losses)
                {
                    Console.Out.WriteLine(string.Create(
                        invariant, $"                 lost on {loss.Surface}: {loss.Ions} ions"));
                }
            }

            Console.Out.WriteLine();

            Console.Out.WriteLine(string.Create(
                invariant,
                $"mixture       {mixture.Species.Count} populations, {mixture.Steps} shared steps of "
                + $"{mixture.StepUs * 1e3:F3} ns set by '{mixture.StepSetBy}'"));

            // Absent rather than zero: a run that did not model the populations' charge and one
            // that modelled it and found none are different runs, and the second is worth
            // knowing about.
            Console.Out.WriteLine(
                mixture.SelfFieldSolves is { } solves
                    ? string.Create(
                        invariant,
                        $"space charge  {solves} solve(s), peak {mixture.PeakSelfPotentialVolts:G4} V "
                        + $"over a net {mixture.NetChargeSi:G4} C")
                    : "space charge  not modelled: the populations do not feel one another, so this "
                      + "is what separate runs would give");
        }

        if (run.Ensemble is { } ensemble)
        {
            Console.Out.WriteLine();

            Console.Out.WriteLine(string.Create(
                invariant,
                $"cloud         {ensemble.Arrived} of {ensemble.Launched} ions arrived, transmission "
                + $"{ensemble.Transmission.Value:P1} +/- "
                + $"{(ensemble.Transmission.Uncertainty.Upper - ensemble.Transmission.Uncertainty.Lower) / 2.0:P1}"));

            // Two widths, both named. They agree only for a Gaussian peak, and the
            // gap between them is the skew - so printing one of them beside a
            // resolving power computed from the other invites exactly the wrong
            // reconciliation.
            //
            // Absent, not zero, where fewer than two ions arrived: a peak needs two
            // points to have a width, and a printed 0.000 ns reads as an infinitely
            // sharp one. The transmission and the itemised losses above are still
            // reported, and they are the whole of what such a run has to say.
            if (ensemble.CentralWidthNs is { } central && ensemble.GaussianFwhmNs is { } gaussian)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"peak          {central:F3} ns central half, "
                    + $"{gaussian:F3} ns Gaussian FWHM, skew {ensemble.Skewness:+0.00;-0.00;0.00}"));

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"turn-around   {ensemble.TurnAroundFwhmNs:F3} ns of that Gaussian width"));
            }
            else
            {
                Console.Out.WriteLine(
                    "peak          none: fewer than two ions arrived, so there is no width to "
                    + "measure");
            }

            // Only when there is something to say. A beamline holds nothing at the end
            // of its run, and a line of zeros on every ordinary model would be noise -
            // but a TRAP arrives nowhere by design, and without this it reports a
            // transmission of zero and nothing else, which is what a trap that lost
            // every ion reports too.
            if (ensemble.Confined.Value is > 0.0)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"confined      {ensemble.Confined.Value:P1} still inside at the end "
                    + $"of the run, having struck nothing"));
            }

            if (ensemble.ResolvingPower is { } resolving)
            {
                // Said in words when the peak has no width to resolve. The value is NaN
                // there rather than a number, because the honest answer is unbounded and
                // the previous one - zero - was the worst conceivable value standing in
                // for the best.
                Console.Out.WriteLine(
                    double.IsNaN(resolving.Value)
                        ? "resolving     unbounded: the arrivals carry no spread to resolve"
                        : string.Create(
                            invariant,
                            $"resolving     {resolving.Value:G6} +/- "
                            + $"{(resolving.Uncertainty.Upper - resolving.Uncertainty.Lower) / 2.0:G3}"
                            + $" (from the central half)"));
            }

            // ACC-5: never a bare percentage. A named surface says which one to
            // move; "transmission is 51 percent" says only that something is wrong.
            foreach (var loss in ensemble.Losses)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"  lost        {loss.Ions:N0} on {loss.Surface} "
                    + $"({(double)loss.Ions / ensemble.Launched:P1})"));
            }

            if (ensemble.EmittanceMmMrad is { } major)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"emittance     {major:G4} x {ensemble.EmittanceMinorMmMrad:G4} mm.mrad, "
                    + $"normalised {ensemble.NormalisedEmittanceMmMrad:G4}"));

                // No alpha means no ellipse: every ion exactly parallel, so there
                // is no waist to be on one side of.
                var orientation = ensemble.PacketTwissAlpha switch
                {
                    > 0.0 => "still converging",
                    < 0.0 => "past the waist",
                    _ => "parallel",
                };

                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"packet        {ensemble.PacketRadiusMm:F3} mm rms, alpha "
                    + $"{ensemble.PacketTwissAlpha:+0.00;-0.00;0.00} ({orientation})"));
            }

            // Reported whether or not it crosses a threshold: a number that only
            // appears when it is bad teaches nobody where the edge is.
            if (ensemble.SpaceChargePopulationLimit > 0.0
                && ensemble.SpaceChargeTimingFraction is { } timingFraction)
            {
                Console.Out.WriteLine(string.Create(
                    invariant,
                    $"space charge  {timingFraction / 1e-6:F2} ppm from "
                    + $"{ensemble.Population:N0} ions; this packet holds "
                    + $"{Capacity(ensemble.SpaceChargePopulationLimit)} within the 1 ppm budget"));
            }

            foreach (var warning in (ensemble.ResolvingPower?.Warnings ?? [])
                .Concat(ensemble.Transmission.Warnings)
                .GroupBy(w => w.Code, StringComparer.Ordinal)
                .Select(g => g.First()))
            {
                var stream = warning.Suppressible ? Console.Out : Console.Error;
                stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
            }

            Console.Out.WriteLine();
        }

        Console.Out.WriteLine($"engine        {run.Manifest.EngineVersion}, model {run.Manifest.ModelHash[..14]}...");

        // GRD-2: warnings travel with the result to every surface, including this
        // one, and are never folded away behind a flag.
        foreach (var warning in flight.Warnings)
        {
            var stream = warning.Suppressible ? Console.Out : Console.Error;
            stream.WriteLine($"  [{warning.Severity}] {warning.Code}: {warning.Message}");
        }

        Console.Out.WriteLine();

        foreach (var artifact in run.Artifacts)
        {
            Console.Out.WriteLine($"wrote {artifact}");
        }
    }

    private static void WriteError(EinzelError error)
    {
        Console.Error.WriteLine($"  {error.Code}");
        Console.Error.WriteLine($"    at         {error.Path}");
        Console.Error.WriteLine($"    constraint {error.Constraint}");

        if (error.Observed is not null)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"    observed   {error.Observed.Value:G6} {error.Observed.Unit}"));
        }

        if (!string.IsNullOrEmpty(error.Suggestion))
        {
            Console.Error.WriteLine($"    try        {error.Suggestion}");
        }

        Console.Error.WriteLine();
    }

    /// <summary>
    /// Walks up from a model file looking for a project root, so that
    /// <c>einzel run models/x.json</c> works from anywhere inside a project.
    /// </summary>
    /// <summary>
    /// The project a file belongs to: the nearest ancestor holding a
    /// <c>models</c> directory, or failing that the file's own directory.
    /// </summary>
    /// <remarks>
    /// The fallback used to be the working directory, which put results wherever
    /// the caller happened to be standing rather than anywhere near the thing they
    /// ran. A study kept in a scratch folder and run from a source tree wrote
    /// <c>results/</c> into the source tree and said only "wrote results\x.json",
    /// which is both surprising and hard to trace back. A file's own directory is
    /// the closest thing to a project when it is not in one - PRJ-1's unit of work
    /// is a directory, and this picks the directory that at least contains the
    /// input.
    /// </remarks>
    private static string InferProjectRoot(string modelPath)
    {
        var directory = new FileInfo(modelPath).Directory;

        for (var at = directory; at is not null; at = at.Parent)
        {
            if (Directory.Exists(Path.Combine(at.FullName, "models")))
            {
                return at.FullName;
            }
        }

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }

    /// <summary>The project's field cache, or null when asked <c>--no-cache</c>.</summary>
    /// <remarks>Its notices - an entry not used, a cache not writable - go to stderr (CLI-2).</remarks>
    private static FieldCache? CacheFor(ProjectLayout project, CommandLine options) =>
        options.Has("no-cache") ? null : new FieldCache(project, announce: Console.Error.WriteLine);

    private const string Usage =
        """
        einzel - open-source, agent-native ion optics

        usage: einzel <command> [arguments]

        starting out:
          doctor [dir]                  check the installation, and a project if given
          schema [--study]              the model format, or the study format, as JSON Schema
          templates [name]              list device templates, or print one
          examples [name]               list example models, or print one

        working:
          init [dir] [--vcs git]        create a project directory
          new <model.json>              create a model from a template or an example
                --from-template <name> | --from-example <name>
          validate <model.json>         check units, bounds, and regime validity
          outline <model.json>          the declared parameters, with units and bounds
                --set <name>=<value>    change one, in its own declared unit
          estimate <model.json>         what a run will cost, without running it
          solve <model.json>            solve the fields only, and report how they went
          run <model.json> [--vtu]      run a model; --vtu also writes a ParaView trajectory
          fly <model.json> --ions <f>   fly an ion list, one trajectory per row [--ladder] [--vtu]
          preview <model.json>          a fast, deliberately inexact look
          test [dir]                    run the project's tests
          verify [dir]                  are the stored results still the answer?
          project [dir]                 what the project holds, and the state of each model
          report [dir] [--out <f>]      an account of what has been run, as one HTML page
          sweep <study.json>            tolerance Monte Carlo, and which parameter binds first
          scan <study.json>             one parameter across a range, one row per point
          boundary <study.json>         bisect onto a stability boundary (Class B, ACC-6)
          optimise <study.json>         search the declared parameters for a better design
          export <model.json>           write the solved field as VTK ImageData
          compare <model.json>          run both transport modes and report the disagreement
          render section <model.json>   draw a plane through the instrument as SVG or PDF
          render animation <spec.json>  frames on a declared playback mapping (RND-7)
          ext list | test | register    the extension authoring loop
          cache status [dir]            the solved fields .einzel/cache keeps for the next run
          cache clear | prune [dir]     delete them all, or only what no run will load
          agents-md [dir]               regenerate the platform layer of AGENTS.md

        release tooling:
          agents tasks [name]           the acceptance suite: list tasks, or print one's prompt
          agents setup <task> <dir>     prepare a project for an agent to attempt a task
          agents score <task> <dir>     score what the agent left behind
          --version                     print the engine version

        options:
          --json                        machine-readable output
          --dry-run                     say what would be written, and write nothing
          --project <dir>               project root; inferred from the model path otherwise

        Results go to stdout, diagnostics to stderr. Exit codes: 0 success,
        1 validation failure, 2 regime violation, 3 cost-gate refusal,
        4 convergence failure, 5 engine-pin mismatch, 6 internal error.

        """;

    /// <summary>How often a render's own transport should report, from the command line.</summary>
    /// <remarks>
    /// <b>Watched by default, at the same thirty seconds a run uses.</b> A render of a
    /// sequenced model runs that model's whole sequence, so the flag that is not set is the
    /// flag that is missing from the render that gets killed - the same argument that made
    /// `einzel run` watch by default. Diagnostics on stderr (CLI-2), so `--json` still gets
    /// the result document and nothing else. `--progress 0` asks for silence.
    /// </remarks>
    /// <param name="options">The parsed command line.</param>
    /// <param name="progress">The interval and where to send it.</param>
    /// <returns>True where the flag parsed, false where it did not and was complained about.</returns>
    private static bool RenderProgress(CommandLine options, out RunProgress? progress)
    {
        var interval = 30.0;
        progress = null;

        if (options.Value("progress") is { } asked
            && !double.TryParse(
                asked, NumberStyles.Float, CultureInfo.InvariantCulture, out interval))
        {
            Console.Error.WriteLine(
                $"--progress takes an interval in seconds, and '{asked}' is not one. "
                + "Use 0 to turn it off.");
            return false;
        }

        progress = new RunProgress(interval, Console.Error.WriteLine);
        return true;
    }

}
