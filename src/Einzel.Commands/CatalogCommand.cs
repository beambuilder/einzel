using Einzel.Core.Errors;
using Einzel.Core.Model;

namespace Einzel.Commands;

/// <summary>One entry in a catalogue of things the platform ships.</summary>
/// <param name="Name">The name to ask for it by.</param>
/// <param name="Description">What it is, from the artifact itself.</param>
public sealed record CatalogEntry(string Name, string? Description);

/// <summary>A catalogue listing.</summary>
public sealed record CatalogOutcome
{
    /// <summary>What kind of thing was listed.</summary>
    public required string Kind { get; init; }

    /// <summary>The entries, ordered by name.</summary>
    public required IReadOnlyList<CatalogEntry> Entries { get; init; }
}

/// <summary>
/// What the platform ships and can be asked for by name: the schema, the device
/// templates, and the example models.
/// </summary>
/// <remarks>
/// <para>
/// These three verbs are how an agent finds out what exists. Everything else in
/// the CLI assumes you already know the name of a thing, and an agent starting
/// from a project directory and prose does not - there are no forum posts to
/// search and no worked examples accumulated over decades to copy from. The
/// platform has to be able to describe itself.
/// </para>
/// <para>
/// Listings are ordered by name (CLI-5). Deterministic ordering matters more here
/// than it looks: a catalogue that reorders between runs makes every golden
/// comparison and every diff of agent output noisy for no reason.
/// </para>
/// </remarks>
public static class CatalogCommand
{
    /// <summary>The model format, as JSON Schema.</summary>
    /// <returns>The schema document.</returns>
    public static string Schema() => ModelSchemaWriter.Write();

    /// <summary>The study format, as JSON Schema.</summary>
    /// <returns>The schema document.</returns>
    /// <remarks>
    /// Generated the same way as the model schema and for the same reason, with
    /// the figures of merit a study may name listed alongside - a study file that
    /// names one this build does not compute is the commonest way to write an
    /// invalid study, and it is not something the shape of the document can say.
    /// </remarks>
    public static string StudySchema() =>
        ModelSchemaWriter.Write<StudyDocument>(
            "Einzel study",
            "study",
            new StudyDocument().SchemaVersion,
            "A tolerance sweep or an optimisation over a model's declared parameters.",
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["figureOfMerit"] = [.. FiguresOfMerit.All.Select(f => f.Name)],
                ["algorithm"] = ["nelderMead", "cmaEs"],
                ["sense"] = ["minimise", "maximise"],
                ["fieldBasis"] = ["patterns", StudyFieldBasis.VaryingElectrodes],
                ["MemoryDocument.unit"] = [.. StudyBinding.MemoryUnits],
            });

    /// <summary>Lists the device templates.</summary>
    /// <returns>The catalogue.</returns>
    public static CatalogOutcome Templates() => new()
    {
        Kind = "template",
        Entries = [.. Library.DeviceTemplates.Names()
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new CatalogEntry(n, DescriptionOf(Library.DeviceTemplates.Read(n))))],
    };

    /// <summary>Lists the example models.</summary>
    /// <returns>The catalogue.</returns>
    public static CatalogOutcome Examples() => new()
    {
        Kind = "example",
        Entries = [.. ExampleModels.Names
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new CatalogEntry(n, DescriptionOf(ExampleModels.Read(n))))],
    };

    /// <summary>The text of one template or example.</summary>
    /// <param name="kind">Either <c>template</c> or <c>example</c>.</param>
    /// <param name="name">Which one.</param>
    /// <returns>The model JSON.</returns>
    /// <exception cref="EinzelException">No such kind, or no such name.</exception>
    public static string Read(string kind, string name)
    {
        var available = kind switch
        {
            "template" => Library.DeviceTemplates.Names(),
            "example" => ExampleModels.Names,
            _ => throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/kind",
                Constraint = $"'{kind}' is not something the platform ships",
                Suggestion = "ask for a 'template' or an 'example'",
            }),
        };

        if (!available.Contains(name, StringComparer.Ordinal))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/name",
                Constraint = $"there is no {kind} called '{name}'",
                Suggestion = $"available: {string.Join(", ", available.OrderBy(n => n, StringComparer.Ordinal))}",
            });
        }

        return kind == "template" ? Library.DeviceTemplates.Read(name) : ExampleModels.Read(name);
    }

    /// <summary>
    /// The description a model states about itself.
    /// </summary>
    /// <remarks>
    /// Read from the document rather than kept in a table beside it, for the same
    /// reason the schema is generated rather than written twice: a catalogue whose
    /// descriptions have drifted from the artifacts it lists is worse than one
    /// with no descriptions, because it is believed.
    /// </remarks>
    private static string? DescriptionOf(string json)
    {
        try
        {
            return Io.ModelJson.Parse(json).Description;
        }
        catch (EinzelException)
        {
            return null;
        }
    }
}
