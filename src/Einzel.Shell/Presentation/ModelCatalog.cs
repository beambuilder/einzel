using Einzel.Commands;

namespace Einzel.Shell.Presentation;

/// <summary>Where a new model comes from.</summary>
public enum ModelSource
{
    /// <summary>A device template: a starting point whose parameters are meant to be changed.</summary>
    Template,

    /// <summary>An example: a reference model that brings its own test.</summary>
    Example,
}

/// <summary>What a new model can be made from, as the window lists it.</summary>
/// <remarks>
/// <para>
/// <b>The same lists <c>einzel templates</c> and <c>einzel examples</c> print.</b> The window
/// does not know what templates exist and must not learn - UI-1 puts the model format outside
/// the shell, and a list kept here would be a second catalog that fell behind the first the
/// day a template was added.
/// </para>
/// <para>
/// Read once, on first use, because the catalog is embedded in the command assembly and cannot
/// change while the process runs.
/// </para>
/// </remarks>
public sealed class ModelCatalog
{
    private IReadOnlyList<CatalogEntry>? _templates;
    private IReadOnlyList<CatalogEntry>? _examples;

    /// <summary>The device templates, in the command layer's order.</summary>
    public IReadOnlyList<CatalogEntry> Templates => _templates ??= CatalogCommand.Templates().Entries;

    /// <summary>The examples, in the command layer's order.</summary>
    public IReadOnlyList<CatalogEntry> Examples => _examples ??= CatalogCommand.Examples().Entries;

    /// <summary>The entries of one source.</summary>
    /// <param name="source">Templates or examples.</param>
    /// <returns>Its entries.</returns>
    public IReadOnlyList<CatalogEntry> Of(ModelSource source) =>
        source == ModelSource.Template ? Templates : Examples;

    /// <summary>The file name a save dialog should offer for a new model.</summary>
    /// <param name="name">The template or example.</param>
    /// <returns>The name with a <c>.json</c> extension, which is what <c>einzel new</c> expects.</returns>
    public static string SuggestedFileName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return name + ".json";
    }
}
