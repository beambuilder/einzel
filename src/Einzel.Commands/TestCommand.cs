using System.Text.Json;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Solved;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>One thing a test file expects of a model.</summary>
public sealed record ExpectationDocument
{
    /// <summary>Which figure of merit. See <see cref="FiguresOfMerit"/>.</summary>
    public string? FigureOfMerit { get; init; }

    /// <summary>The value it should take.</summary>
    public double Value { get; init; }

    /// <summary>Unit of <see cref="Value"/>; must match the figure's dimension.</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// How far off it may be, as a fraction of the expected value.
    /// </summary>
    /// <remarks>
    /// Relative rather than absolute, because that is how the accuracy budget is
    /// written: ACC-1 is one part in a million of a flight time, not a number of
    /// nanoseconds. A tolerance stated absolutely stops meaning the same thing the
    /// moment the geometry is scaled.
    /// </remarks>
    public double Tolerance { get; init; } = 1e-6;

    /// <summary>Fractional energy spread for the ensemble figures of merit.</summary>
    public double EnergySpread { get; init; } = FiguresOfMerit.DefaultEnergySpread;

    /// <summary>How many ions the ensemble figures of merit launch.</summary>
    public int Ions { get; init; } = FiguresOfMerit.DefaultIons;
}

/// <summary>
/// A test: a model, and what it should produce.
/// </summary>
/// <remarks>
/// <para>
/// EX-1 asks the example corpus for "a prose description, expected results, and
/// assertion tolerances". This is the shape those take in a project, and it is
/// what lets an agent establish that an edit did not break something - which is
/// the difference between editing a model and guessing at one.
/// </para>
/// <para>
/// Deliberately not a study. A study asks a question; a test asserts an answer
/// that is already known, usually from a closed form, and its value is that it
/// fails.
/// </para>
/// </remarks>
public sealed record TestDocument
{
    /// <summary>The schema version this document is written against.</summary>
    public string SchemaVersion { get; init; } = "0.1";

    /// <summary>A short name, used in reporting.</summary>
    public string? Name { get; init; }

    /// <summary>What this establishes, and where the expected value comes from.</summary>
    public string? Description { get; init; }

    /// <summary>The model under test, relative to this file.</summary>
    public string? Model { get; init; }

    /// <summary>What it should produce.</summary>
    public IReadOnlyList<ExpectationDocument>? Expect { get; init; }
}

/// <summary>One assertion, and how it went.</summary>
/// <param name="FigureOfMerit">Which figure was measured.</param>
/// <param name="Unit">The unit both values are in.</param>
/// <param name="Expected">What the test expected.</param>
/// <param name="Observed">What the model produced, or null when nothing arrived.</param>
/// <param name="RelativeError">How far off, as a fraction.</param>
/// <param name="Tolerance">How far off it was allowed to be.</param>
/// <param name="Passed">Whether it held.</param>
public sealed record Assertion(
    string FigureOfMerit,
    string Unit,
    double Expected,
    double? Observed,
    double? RelativeError,
    double Tolerance,
    bool Passed);

/// <summary>One test file, and how it went.</summary>
public sealed record TestResult
{
    /// <summary>The test file, relative to the project root.</summary>
    public required string Path { get; init; }

    /// <summary>Its name, or the file stem when it has none.</summary>
    public required string Name { get; init; }

    /// <summary>Every assertion, in document order.</summary>
    public required IReadOnlyList<Assertion> Assertions { get; init; }

    /// <summary>Why the whole file failed, when it did not get as far as asserting.</summary>
    public string? Failure { get; init; }

    /// <summary>Whether every assertion held.</summary>
    public bool Passed => Failure is null && Assertions.All(a => a.Passed);
}

/// <summary>The outcome of running a project's tests.</summary>
public sealed record TestOutcome
{
    /// <summary>The project root.</summary>
    public required string Root { get; init; }

    /// <summary>One entry per test file, ordered by path.</summary>
    public required IReadOnlyList<TestResult> Tests { get; init; }

    /// <summary>How many passed.</summary>
    public int Passed => Tests.Count(t => t.Passed);

    /// <summary>Whether every test passed.</summary>
    /// <summary>Whether every test passed, and there was at least one.</summary>
    /// <remarks>
    /// The count matters as much as the verdict. "Every test passed" over an empty
    /// list is true and useless, and it is the third place in this codebase where
    /// that shape appeared - after 'solve' answering converged for a model it had
    /// skipped, and a test asserting nothing but that a number was non-zero. A
    /// project with no tests is not a passing project; it is one nobody has checked.
    /// </remarks>
    public bool AllPassed => Tests.Count > 0 && Tests.All(t => t.Passed);
}

/// <summary>Runs the tests in a project.</summary>
public static class TestCommand
{
    private static readonly JsonSerializerOptions Reading = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Runs every test in a project.</summary>
    /// <param name="root">The project root.</param>
    /// <param name="store">
    /// Where the tests load their solved field channels from and keep them, or null to solve
    /// every one. Each expectation flies its model again, so a test file of five figures on one
    /// geometry solves it once.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    public static TestOutcome Execute(string root, ISolveStore? store = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        var layout = new ProjectLayout(Path.GetFullPath(root));
        var tests = new List<TestResult>();

        if (!Directory.Exists(layout.Tests))
        {
            return new TestOutcome { Root = layout.Root, Tests = tests };
        }

        // Recursive, because a project that groups its tests into folders was
        // getting silence and exit 0 rather than the tests it wrote. A discovery
        // rule that quietly matches less than the author meant is the same failure
        // as a vacuous pass, arriving by a different route.
        var files = Directory.GetFiles(layout.Tests, "*.json", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        foreach (var file in files)
        {
            tests.Add(RunOne(layout, file, store));
        }

        return new TestOutcome { Root = layout.Root, Tests = tests };
    }

    private static TestResult RunOne(ProjectLayout layout, string path, ISolveStore? store)
    {
        var relative = Path.GetRelativePath(layout.Root, path);
        var name = Path.GetFileNameWithoutExtension(path);

        TestDocument? test;

        try
        {
            test = JsonSerializer.Deserialize<TestDocument>(File.ReadAllText(path), Reading);
        }
        catch (JsonException failure)
        {
            return Broken(relative, name, $"not valid JSON: {failure.Message}");
        }

        if (test is null || string.IsNullOrWhiteSpace(test.Model))
        {
            return Broken(relative, name, "a test names the model it tests");
        }

        if (test.Expect is not { Count: > 0 })
        {
            // A test that asserts nothing passes, which is worse than failing: it
            // is a green tick standing for no evidence at all.
            return Broken(relative, name, "a test with no expectations asserts nothing and would always pass");
        }

        var modelPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path) ?? ".", test.Model));

        if (!File.Exists(modelPath))
        {
            return Broken(relative, test.Name ?? name, $"the model is missing: {test.Model}");
        }

        ModelDocument document;
        CompiledModel model;

        try
        {
            document = Io.ModelJson.Parse(File.ReadAllText(modelPath));
            var validation = ModelValidator.Validate(
                document, null, Path.GetDirectoryName(modelPath));

            if (!validation.IsValid)
            {
                return Broken(relative, test.Name ?? name, $"the model does not validate: {validation.Errors[0].Constraint}");
            }

            model = validation.Model!;
        }
        catch (EinzelException failure)
        {
            return Broken(relative, test.Name ?? name, $"the model does not load: {failure.Error.Constraint}");
        }

        var assertions = new List<Assertion>(test.Expect.Count);

        foreach (var expectation in test.Expect)
        {
            assertions.Add(Check(model, expectation, store));
        }

        return new TestResult
        {
            Path = relative,
            Name = test.Name ?? name,
            Assertions = assertions,
        };
    }

    private static Assertion Check(CompiledModel model, ExpectationDocument expectation, ISolveStore? store)
    {
        var figure = FiguresOfMerit.Describe(
            expectation.FigureOfMerit ?? throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/expect/figureOfMerit",
                Constraint = "an expectation names the figure of merit it is about",
                Suggestion = $"one of: {string.Join(", ", FiguresOfMerit.All.Select(f => f.Name))}",
            }));

        // The unit is the test's to state and the figure's to agree with. A
        // mismatch is a dimensional error rather than a failed assertion, because
        // an expectation in millimetres about a flight time is not a wrong answer,
        // it is a wrong question.
        var unit = expectation.Unit ?? figure.Unit;
        var expected = Quantity.From(expectation.Value, unit);

        if (expected.Dimension != figure.Dimension)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsIncompatible,
                Path = "/expect/unit",
                Constraint = $"'{figure.Name}' is reported in {figure.Unit}, and '{unit}' is not of that dimension",
                Suggestion = $"express the expected value in {figure.Unit} or another unit of its dimension",
            });
        }

        var evaluate = FiguresOfMerit.Evaluator(
            figure.Name, expectation.EnergySpread, expectation.Ions, store: store);
        var observed = evaluate(model);

        if (observed is not { } value)
        {
            return new Assertion(
                figure.Name, unit, expectation.Value, null, null, expectation.Tolerance, Passed: false);
        }

        var scale = 1.0 / Quantity.From(1.0, unit).SiValue;
        var inUnit = value * scale;

        // Relative to the expected value, with an absolute floor so an expectation
        // of zero is testable at all rather than dividing by it.
        var denominator = Math.Abs(expectation.Value);
        var error = denominator > 0.0
            ? Math.Abs(inUnit - expectation.Value) / denominator
            : Math.Abs(inUnit);

        return new Assertion(
            figure.Name,
            unit,
            expectation.Value,
            inUnit,
            error,
            expectation.Tolerance,
            error <= expectation.Tolerance);
    }

    private static TestResult Broken(string path, string name, string failure) => new()
    {
        Path = path,
        Name = name,
        Assertions = [],
        Failure = failure,
    };
}
