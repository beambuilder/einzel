using System.Text.Json.Nodes;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Extensions;
using Einzel.Fields.Solved;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>
/// An extension used as a figure of merit.
/// </summary>
/// <remarks>
/// <para>
/// Section 13: an optimiser composes objectives from section 12, and those may be
/// Python extensions. This is the seam that makes that sentence true - a study
/// naming <c>ext:name</c> gets the extension by that name, handed whichever
/// built-in figures its manifest asks for plus the model's own parameters, and
/// expected to return a scalar.
/// </para>
/// <para>
/// A prefix rather than a new field in the study format, because a figure of merit
/// is already selected by name and <c>ext:</c> is a namespace rather than a second
/// mechanism. A study that names an extension reads the same as one naming a
/// built-in figure, which is what keeps the optimiser from having to know.
/// </para>
/// </remarks>
public static class ExtensionObjective
{
    /// <summary>The prefix that selects an extension rather than a built-in figure.</summary>
    public const string Prefix = "ext:";

    /// <summary>Whether a figure-of-merit name selects an extension.</summary>
    /// <param name="name">The declared name.</param>
    /// <returns><see langword="true"/> when it names an extension.</returns>
    public static bool Names(string? name) =>
        name is not null && name.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// What an extension contributed to a run, for the manifest.
    /// </summary>
    /// <param name="Identity">Name and version, per GRD-6.</param>
    /// <param name="Interpreter">The Python the extension ran against.</param>
    /// <remarks>
    /// Returned rather than accumulated in a static, because a static would be
    /// shared by every study running in one process and a manifest would then
    /// record extensions a different run used. Provenance that can be wrong is
    /// worse than none.
    /// </remarks>
    public sealed record Provenance(string Identity, string Interpreter);

    /// <summary>Builds an evaluator that calls an extension once per model.</summary>
    /// <param name="name">The figure-of-merit name, including the prefix.</param>
    /// <param name="extensionsRoot">The project's extensions directory.</param>
    /// <param name="scratch">A directory the extension may run in.</param>
    /// <param name="energySpread">Fractional energy spread for the ensemble figures.</param>
    /// <param name="ions">How many ions the ensemble figures launch.</param>
    /// <param name="store">
    /// Where the built-in figures handed to the extension load their solved field channels
    /// from and keep them, or null to solve them.
    /// </param>
    /// <returns>A function from a validated model to the objective.</returns>
    /// <exception cref="ArgumentException">A required path is null or blank.</exception>
    /// <exception cref="EinzelException">
    /// No extension by that name, it does not produce a number, it declares an
    /// incompatible engine range, or no interpreter was found.
    /// </exception>
    public static Func<CompiledModel, double?> Evaluator(
        string name,
        string extensionsRoot,
        string scratch,
        double energySpread = FiguresOfMerit.DefaultEnergySpread,
        int ions = FiguresOfMerit.DefaultIons,
        ISolveStore? store = null) =>
        Evaluator(name, extensionsRoot, scratch, out _, energySpread, ions, store);

    /// <summary>Builds an evaluator, and says what it will be attributed to.</summary>
    /// <param name="name">The figure-of-merit name, including the prefix.</param>
    /// <param name="extensionsRoot">The project's extensions directory.</param>
    /// <param name="scratch">A directory the extension may run in.</param>
    /// <param name="provenance">What to record in a manifest (PRJ-3, GRD-6).</param>
    /// <param name="energySpread">Fractional energy spread for the ensemble figures.</param>
    /// <param name="ions">How many ions the ensemble figures launch.</param>
    /// <param name="store">
    /// Where the built-in figures handed to the extension load their solved field channels
    /// from and keep them, or null to solve them.
    /// </param>
    /// <returns>A function from a validated model to the objective.</returns>
    /// <exception cref="ArgumentException">A required path is null or blank.</exception>
    /// <exception cref="EinzelException">
    /// No extension by that name, it does not produce a number, it declares an
    /// incompatible engine range, or no interpreter was found.
    /// </exception>
    public static Func<CompiledModel, double?> Evaluator(
        string name,
        string extensionsRoot,
        string scratch,
        out Provenance provenance,
        double energySpread = FiguresOfMerit.DefaultEnergySpread,
        int ions = FiguresOfMerit.DefaultIons,
        ISolveStore? store = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(extensionsRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratch);

        var wanted = name[Prefix.Length..];

        var installed = ExtensionCatalogue.Discover(extensionsRoot)
            .FirstOrDefault(e => string.Equals(e.Manifest.Name, wanted, StringComparison.Ordinal))
            ?? throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/figureOfMerit",
                Constraint = $"no extension named '{wanted}' is installed in {extensionsRoot}",
                Suggestion = "run 'einzel ext list' to see what is installed, or "
                    + $"'einzel ext register {wanted}' to scaffold one",
            });

        if (installed.Manifest.Kind is not (ExtensionKind.Objective or ExtensionKind.Analysis))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/kind",
                Constraint =
                    $"'{wanted}' declares kind '{installed.Manifest.Kind}', which does not produce a number",
                Suggestion = "a figure of merit comes from an 'objective' or an 'analysis' extension",
            });
        }

        if (ExtensionCatalogue.Incompatibility(installed.Manifest, EngineBuild.Version) is { } why)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.EnginePinMismatch,
                Path = "/engineMinimum",
                Constraint = $"'{wanted}' {why}",
                Suggestion = "widen the extension's engine range once you have checked it still "
                    + "works, or pin the engine it was written against",
            });
        }

        var interpreter = SubprocessRunner.Discover()
            ?? throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.InternalError,
                Path = "/",
                Constraint = "no Python 3 interpreter was found",
                Suggestion = "EXT-6 wants a vendored interpreter and this build discovers one "
                    + "instead, so extensions need python3 on the path. 'einzel doctor' reports "
                    + "what was found",
            });

        provenance = new Provenance(
            $"{installed.Manifest.Name} {installed.Manifest.Version}", interpreter);

        var runner = new SubprocessRunner(interpreter);

        return model =>
        {
            var payload = Payload(model, installed.Manifest, energySpread, ions, store);
            var result = runner.Run(installed.Manifest, installed.Directory, payload, scratch);

            return result.Output is JsonObject document
                && document.TryGetPropertyValue("value", out var value)
                && value is JsonValue scalar
                && scalar.TryGetValue<double>(out var number)
                && double.IsFinite(number)
                    ? number
                    : null;
        };
    }

    /// <summary>What an objective extension is handed.</summary>
    /// <remarks>
    /// The model's declared parameters in SI, and whichever built-in figures the
    /// manifest asked for. A figure that could not be computed - because the ion
    /// never arrived, say - is present and <c>null</c> rather than absent, so an
    /// extension can tell "this design loses its beam" from "you did not ask for
    /// that figure".
    /// </remarks>
    private static JsonObject Payload(
        CompiledModel model, ExtensionManifest manifest, double energySpread, int ions, ISolveStore? store)
    {
        var parameters = new JsonObject();

        foreach (var (name, resolved) in model.Parameters.Parameters
            .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            parameters[name] = resolved.Value.SiValue;
        }

        var figures = new JsonObject();

        foreach (var figure in manifest.Figures)
        {
            var value = FiguresOfMerit.Evaluator(figure, energySpread, ions, store: store)(model);

            figures[figure] = value is { } scalar && double.IsFinite(scalar) ? scalar : null;
        }

        return new JsonObject
        {
            ["engineVersion"] = EngineBuild.Version,
            ["parameters"] = parameters,
            ["figures"] = figures,
        };
    }
}
