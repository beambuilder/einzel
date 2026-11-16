using System.Globalization;

using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Io;

namespace Einzel.Commands;

/// <summary>One parameter of a model, as something can display and edit it.</summary>
/// <param name="Name">What it is called.</param>
/// <param name="Value">Its nominal magnitude, or null when it is derived.</param>
/// <param name="Expression">What derives it, or null when it is free.</param>
/// <param name="Unit">The unit the value or the expression's result is in.</param>
/// <param name="ResolvedSi">
/// What it currently works out to, in SI. Present for a derived parameter too, which is
/// the point: a reader wants to see what the expression came to, not only what it says.
/// </param>
/// <param name="Minimum">Lower bound in the declared unit, when one is declared.</param>
/// <param name="Maximum">Upper bound in the declared unit, when one is declared.</param>
/// <param name="Description">What it means.</param>
/// <param name="Editable">
/// Whether a caller may set it. False for a derived parameter, whose value is its
/// expression's - editing it would be editing a consequence.
/// </param>
/// <param name="Provenance">Where the number came from.</param>
/// <param name="Source">The citation or the fitted condition, or null.</param>
public sealed record ParameterOutline(
    string Name,
    double? Value,
    string? Expression,
    string? Unit,
    double? ResolvedSi,
    double? Minimum,
    double? Maximum,
    string? Description,
    bool Editable,
    ParameterProvenance Provenance = ParameterProvenance.Chosen,
    string? Source = null);

/// <summary>A model's declared surface, as something can show it.</summary>
/// <param name="ModelPath">The model, as an absolute path.</param>
/// <param name="Name">What the model calls itself.</param>
/// <param name="SchemaVersion">The schema version it declares.</param>
/// <param name="Description">What it says it is.</param>
/// <param name="Parameters">Every declared parameter, in document order.</param>
/// <param name="Valid">Whether the document validates as it stands.</param>
/// <param name="Errors">What is wrong with it, if anything.</param>
public sealed record OutlineOutcome(
    string ModelPath,
    string? Name,
    string? SchemaVersion,
    string? Description,
    IReadOnlyList<ParameterOutline> Parameters,
    bool Valid,
    IReadOnlyList<Core.Errors.EinzelError> Errors);

/// <summary>
/// A model's parameter surface, for anything that needs to show it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This exists because UI-1 forbids the shell from having file format knowledge.</b>
/// §16 wants a "model tree with parameter editing, live validation, units on every
/// field", and a window that parsed the document to build that tree would be growing its
/// own idea of what a model is - which is the thing UI-1 exists to prevent, and the way
/// the window and the engine would come to disagree.
/// </para>
/// <para>
/// So the command layer answers the question instead, and the shell renders whatever it
/// is handed. Every field a tree needs is already declared: LIB-1 gave parameters units,
/// bounds and descriptions so a study could perturb them, and those are exactly what an
/// editor needs to show a person.
/// </para>
/// <para>
/// <b>And it is a CLI verb, not a shell method.</b> AGT-2: nothing exists only in the
/// shell. `einzel outline --json` returns this same record, so an agent can read a
/// model's knobs without parsing the document either - which is the same service, and
/// arguably the one that matters more.
/// </para>
/// </remarks>
public static class OutlineCommand
{
    /// <summary>Reads a model's declared surface.</summary>
    /// <param name="modelPath">The model to read.</param>
    /// <returns>Its parameters, and whether it validates.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The document does not parse.</exception>
    /// <remarks>
    /// <para>
    /// <b>A document that does not validate still has an outline.</b> That is deliberate
    /// and it is what "live validation" needs: a person editing a parameter into an
    /// invalid state must still see the tree, with the error against it, rather than have
    /// the tree vanish until they undo what they typed.
    /// </para>
    /// <para>
    /// A document that does not <em>parse</em> is different and throws, because there is
    /// no surface to describe - the reader cannot tell which parameters exist.
    /// </para>
    /// </remarks>
    public static OutlineOutcome Execute(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var document = ModelJson.Parse(File.ReadAllText(absolute));

        var validation = ModelValidator.Validate(document, null, Path.GetDirectoryName(absolute));

        // The resolved surface when there is one. A model that does not validate has no
        // compiled parameters, and the outline then shows what was declared without what
        // it works out to - which is the honest state rather than a guess.
        var resolved = validation.Model?.Parameters.Values();

        var parameters = new List<ParameterOutline>();

        var surface = document.Parameters
            ?? new Dictionary<string, ParameterDocument>(StringComparer.Ordinal);

        foreach (var (name, declared) in surface)
        {
            parameters.Add(new ParameterOutline(
                name,
                declared.Value,
                declared.Expression,
                declared.Unit,
                resolved is not null && resolved.TryGetValue(name, out var si)
                    ? si.SiValue
                    : null,
                declared.Minimum,
                declared.Maximum,
                declared.Description,

                // A derived parameter's value is its expression's. Offering it for edit
                // would offer to edit a consequence, and the two would disagree at the
                // next resolve.
                Editable: declared.Expression is null,
                declared.Provenance ?? ParameterProvenance.Chosen,
                declared.Source));
        }

        return new OutlineOutcome(
            absolute,
            document.Name,
            document.SchemaVersion,
            document.Description,
            parameters,
            validation.IsValid,
            validation.Errors);
    }

    /// <summary>Sets one free parameter, and reports what the document then is.</summary>
    /// <param name="modelPath">The model to edit.</param>
    /// <param name="parameter">Which parameter.</param>
    /// <param name="value">Its new magnitude, in its own declared unit.</param>
    /// <returns>The document as it would be, for a caller to apply.</returns>
    /// <exception cref="ArgumentException">An argument is blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// The parameter is not declared, is derived, or the document does not parse.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Returns the text rather than writing it</b>, which is what lets the shell put
    /// it through the shared journal and the CLI write it directly. A command that wrote
    /// the file itself would make a shell edit invisible to the agent connected to the
    /// same session - the one-sided session GRD-9 is about.
    /// </para>
    /// <para>
    /// <b>In the parameter's own declared unit</b>, not SI. A person editing a 4 mm
    /// inscribed radius types 5, and a surface that demanded 0.005 would be asking them
    /// to do the conversion the format exists to make unnecessary.
    /// </para>
    /// </remarks>
    public static string WithParameter(string modelPath, string parameter, double value) =>
        WithParameter(modelPath, parameter, value, from: null);

    /// <summary>The model with one parameter changed, starting from text already in hand.</summary>
    /// <param name="modelPath">The model, for its path and for resolving what it references.</param>
    /// <param name="parameter">The declared parameter to change.</param>
    /// <param name="value">Its new value, in its own declared unit.</param>
    /// <param name="from">
    /// The document text to edit, or null to read the file. Passing it is what lets several
    /// edits chain: applying each to the file's own text would apply them to the same
    /// starting point and keep only the last.
    /// </param>
    /// <returns>The edited document.</returns>
    /// <exception cref="Core.Errors.EinzelException">No such parameter, or it is derived.</exception>
    public static string WithParameter(string modelPath, string parameter, double value, string? from)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(parameter);

        var absolute = Path.GetFullPath(modelPath);
        var text = from ?? File.ReadAllText(absolute);
        var document = ModelJson.Parse(text);

        if (document.Parameters is not { } declared
            || !declared.TryGetValue(parameter, out var existing))
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.SchemaInvalid,
                Path = $"/parameters/{parameter}",
                Constraint = $"the model declares no parameter called '{parameter}'",
                Suggestion = document.Parameters is { Count: > 0 } any
                    ? $"it declares {string.Join(", ", any.Keys.Order(StringComparer.Ordinal))}"
                    : "the model declares no parameters at all",
            });
        }

        if (existing.Expression is not null)
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.SchemaInvalid,
                Path = $"/parameters/{parameter}",
                Constraint = $"'{parameter}' is derived from '{existing.Expression}', so its "
                    + "value is that expression's",
                Suggestion = "set one of the parameters the expression is over, or replace "
                    + "the expression with a value. Editing a derived parameter would edit "
                    + "a consequence, and the two would disagree at the next resolve",
            });
        }

        if (!double.IsFinite(value))
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.ValueOutOfBounds,
                Path = $"/parameters/{parameter}",
                Constraint = "a parameter's value must be a finite number",
                Suggestion = "JSON has no NaN or infinity, so such a value could not be "
                    + "written to the document even if it could be computed",
            });
        }

        var edited = document with
        {
            Parameters = declared.ToDictionary(
                pair => pair.Key,
                pair => string.Equals(pair.Key, parameter, StringComparison.Ordinal)
                    ? pair.Value with { Value = value }
                    : pair.Value,
                StringComparer.Ordinal),
        };

        return ModelJson.Write(edited);
    }

    /// <summary>The SI unit a declared unit reduces to.</summary>
    /// <param name="declaredUnit">The unit the document declares.</param>
    /// <returns>Its SI symbol, or empty when the unit is not one the registry knows.</returns>
    /// <remarks>
    /// <para>
    /// <b>Here rather than in the shell, because which SI unit a dimension has is format
    /// knowledge</b> and UI-1 puts that outside the window. A window showing a resolved
    /// value as "0.007 SI" leaves a reader to work out which SI unit that row is in,
    /// which for a tree mixing lengths, voltages and dimensionless ratios is exactly the
    /// inference the format exists to remove.
    /// </para>
    /// <para>
    /// The SI unit of a dimension is the registered unit whose factor is exactly one -
    /// asked of the registry rather than mapped here, so a unit added there needs no
    /// change and a unit it does not know yields nothing rather than a guess.
    /// </para>
    /// </remarks>
    public static string SiUnitOf(string declaredUnit)
    {
        if (string.IsNullOrWhiteSpace(declaredUnit)
            || !UnitRegistry.TryResolve(declaredUnit, out var definition)
            || definition is null)
        {
            return string.Empty;
        }

        foreach (var candidate in UnitRegistry.All)
        {
            if (candidate.SiFactor == 1.0 && candidate.Dimension == definition.Dimension)
            {
                return candidate.Symbol;
            }
        }

        return string.Empty;
    }

    /// <summary>The bounds a parameter declares, as a person reads them.</summary>
    /// <param name="parameter">The parameter.</param>
    /// <returns>A phrase, or null when it declares no bounds.</returns>
    /// <remarks>
    /// Formatting belongs here rather than in the window for the same reason the outline
    /// does: it is the one place that knows the value and its unit are two halves of one
    /// statement, and GRD-1's habit is that they never appear apart.
    /// </remarks>
    public static string? BoundsText(ParameterOutline parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);

        var unit = string.IsNullOrWhiteSpace(parameter.Unit) ? string.Empty : $" {parameter.Unit}";

        // Invariant, and said so at the site rather than left to the build flag: the CLI runs
        // globalization-invariant, but the shells turn that off because a window cannot run
        // without it, and there a German locale printed this as "0,1 to 20 kV" beside a box
        // that parses "0.1" and refuses "0,1".
        return (parameter.Minimum, parameter.Maximum) switch
        {
            (null, null) => null,
            ({ } low, null) => string.Create(CultureInfo.InvariantCulture, $"at least {low:G6}{unit}"),
            (null, { } high) => string.Create(CultureInfo.InvariantCulture, $"at most {high:G6}{unit}"),
            ({ } low, { } high) => string.Create(CultureInfo.InvariantCulture, $"{low:G6} to {high:G6}{unit}"),
        };
    }
}
