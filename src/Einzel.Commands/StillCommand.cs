using System.Globalization;

using Einzel.Core.Results;
using Einzel.Io;
using Einzel.Project;
using Einzel.Render;

namespace Einzel.Commands;

/// <summary>What <c>einzel render still</c> drew and where it put it.</summary>
public sealed record StillOutcome
{
    /// <summary>The model, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>The model document's content hash, so the picture can be tied to it (PRJ-3).</summary>
    public required string ModelHash { get; init; }

    /// <summary>The PNG written, or that would be written under <c>--dry-run</c>.</summary>
    public required string Artifact { get; init; }

    /// <summary>Whether it was actually written.</summary>
    public required bool Written { get; init; }

    /// <summary>Width in pixels.</summary>
    public required int Width { get; init; }

    /// <summary>Height in pixels.</summary>
    public required int Height { get; init; }

    /// <summary>The named view it was drawn from.</summary>
    public required string View { get; init; }

    /// <summary>Whether the conductors were drawn translucent.</summary>
    public required bool SeeThrough { get; init; }

    /// <summary>Declared electrodes drawn - not surfaces, of which a board is many.</summary>
    public required int Electrodes { get; init; }

    /// <summary>Equipotential levels drawn.</summary>
    public required int FieldLevels { get; init; }

    /// <summary>Trajectories drawn; none for a diffusive model, by RND-8.</summary>
    public required int Trajectories { get; init; }

    /// <summary>Density contours drawn.</summary>
    public required int DensityShells { get; init; }

    /// <summary>Whether a validity violation marked the picture with a hatched band.</summary>
    public required bool Tainted { get; init; }

    /// <summary>
    /// The true length of each arm of the scale indicator, in millimeters; absent when none
    /// was drawn - because <c>--no-scale</c> asked for none, or because the picture is too small
    /// to hold one.
    /// </summary>
    public double? ScaleMm { get; init; }

    /// <summary>The model axes the scale indicator has an arm along; empty when none was drawn.</summary>
    public IReadOnlyList<string> ScaleAxes { get; init; } = [];

    /// <summary>
    /// Which corner the scale indicator sits in - the one the instrument covers least; absent
    /// when none was drawn.
    /// </summary>
    public string? ScaleCorner { get; init; }

    /// <summary>Everything the viewport reported, which the PNG also carries (GRD-2).</summary>
    public required IReadOnlyList<WarningJson> Warnings { get; init; }
}

/// <summary>
/// Draws a model to a PNG exactly as the interactive viewport draws it, with no window.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same picture, not a similar one.</b> The scene comes from
/// <see cref="ViewportCommand.Execute(string, int, double?)"/>, its colors, layers and
/// translucency from <see cref="ViewportPicture.Compose"/>, and its camera from
/// <see cref="Framing"/> - each of them the thing the window draws from. What differs is only
/// who turns triangles into pixels: a GPU in the window, <see cref="Rasterizer"/> here.
/// </para>
/// <para>
/// <b>Why it exists at all</b> is the other half of AGT-2. An agent could already run every
/// analysis the window runs; it could not see what the window shows, so a person looking at a
/// viewport and an agent reasoning about the same model were looking at different things.
/// A still is what the window would have shown, as a file either of them can open.
/// </para>
/// <para>
/// <b>Headless</b>: RND-1 puts rendering in the engine, and this runs on a CI runner with no
/// display attached - the same claim the vector section makes, now for a shaded view.
/// </para>
/// </remarks>
public static class StillCommand
{
    /// <summary>Draws a model to a PNG.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="project">Where figures go by default.</param>
    /// <param name="view">A name from <see cref="ViewportPicture.Views"/>.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="seeThrough">Whether to draw the conductors translucent.</param>
    /// <param name="outputPath">Where to write, or null to name it after the model.</param>
    /// <param name="dryRun">Whether to draw without writing.</param>
    /// <param name="scale">Whether to draw the scale indicator.</param>
    /// <returns>What was drawn and where it went.</returns>
    /// <exception cref="ArgumentException">The model path is blank, or the view has no such name.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A size is outside 16 to 8192 pixels.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    public static StillOutcome Execute(
        string modelPath,
        ProjectLayout project,
        string view = "iso",
        int width = 1600,
        int height = 1000,
        bool seeThrough = false,
        string? outputPath = null,
        bool dryRun = false,
        bool scale = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 16);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(width, 8192);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, 8192);

        if (!ViewportPicture.Views.TryGetValue(view, out var angles))
        {
            throw new ArgumentException(
                $"no view named '{view}'; the named views are "
                + string.Join(", ", ViewportPicture.Views.Keys),
                nameof(view));
        }

        var absolute = Path.GetFullPath(modelPath);
        var hash = ContentHash.OfText(File.ReadAllText(absolute));

        var outcome = ViewportCommand.Execute(absolute);
        var layers = ViewportPicture.Compose(
            outcome, seeThrough ? ViewportPicture.SeeThroughOpacity : 1.0);
        var framing = Framing.Measure(outcome, angles.Azimuth, angles.Elevation);
        var matrix = framing.Matrix((double)width / height);

        var tainted = outcome.Warnings.Any(w => w.Severity == WarningSeverity.ValidityViolation);

        // Sized from the same matrix the picture is drawn with, and kept above the hatched
        // band a tainted picture carries, so the band cannot strike the ruler through.
        var ruler = scale
            ? ScaleIndicator.For(
                matrix,
                width,
                height,
                ScaleIndicator.TextHeightFor(width, height),
                tainted ? Rasterizer.HatchHeight(height) : 0.0,
                layers)
            : null;

        var rgb = Rasterizer.Draw(
            [.. layers.Select(Raster)],
            matrix,
            new RasterLighting(ViewportPicture.Brightness, ViewportPicture.LineBrightness),
            ColorRamp.Ground,
            width,
            height,
            overlay: ruler is null ? null : Overlay(ruler));

        if (tainted)
        {
            Rasterizer.Hatch(rgb, width, height);
        }

        var path = outputPath is { } named
            ? Path.GetFullPath(named)
            : Path.Combine(project.Figures, Path.GetFileNameWithoutExtension(absolute) + ".still.png");

        if (!dryRun)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, PngWriter.Write(rgb, width, height, Provenance(
                absolute, hash, view, angles, width, height, seeThrough, outcome, ruler)));
        }

        return new StillOutcome
        {
            ModelPath = absolute,
            ModelHash = hash,
            Artifact = path,
            Written = !dryRun,
            Width = width,
            Height = height,
            View = view,
            SeeThrough = seeThrough,
            Electrodes = outcome.ElectrodeCount(),
            FieldLevels = outcome.Equipotentials.Count,
            Trajectories = outcome.Trajectories.Count,
            DensityShells = outcome.Density.Count,
            Tainted = tainted,
            ScaleMm = ruler?.LengthMm,
            ScaleAxes = ruler is null ? [] : [.. ruler.Arms.Select(a => a.Axis)],
            ScaleCorner = ruler?.Corner,
            Warnings = [.. outcome.Warnings.Select(w => new WarningJson
            {
                Code = w.Code,
                Message = w.Message,
                Severity = w.Severity.ToString(),
                Suppressible = w.IsSuppressible,
            })],
        };
    }

    /// <summary>A composed layer, as the rasterizer takes it.</summary>
    /// <remarks>
    /// A copy of shape and nothing else: every decision was made in the composition, and the
    /// two record types exist separately only because <c>Einzel.Render</c> may not reference
    /// the command layer.
    /// </remarks>
    private static RasterLayer Raster(PictureLayer layer) =>
        new(
            [.. layer.Meshes.Select(m => new RasterMesh(m.VerticesMm, m.Normals, m.Triangles, m.R, m.G, m.B))],
            [.. layer.Lines.Select(l => new RasterLine(l.PointsMm, l.R, l.G, l.B))],
            layer.Alpha,
            layer.WritesDepth);

    /// <summary>A scale indicator, as the rasterizer draws it: strokes in pixels.</summary>
    /// <remarks>
    /// Every stroke, text included, comes from <see cref="ScaleIndicator.Strokes"/>, which the
    /// window draws too; this only changes the records' shape, for the reason
    /// <see cref="Raster"/> gives.
    /// </remarks>
    private static RasterOverlay Overlay(ScaleIndicator ruler)
    {
        var (r, g, b) = ScaleIndicator.Ink;

        return new RasterOverlay(
            [.. ruler.Strokes().Select(s => new OverlayStroke(s.PointsPx, r, g, b, s.WidthPx))],
            [],
            ScaleIndicator.LineWidthPx);
    }

    private static List<(string Keyword, string Text)> Provenance(
        string model,
        string hash,
        string view,
        (double Azimuth, double Elevation) angles,
        int width,
        int height,
        bool seeThrough,
        ViewportOutcome outcome,
        ScaleIndicator? ruler)
    {
        var text = new List<(string, string)>
        {
            ("Title", $"{Path.GetFileName(model)}, {view} view"),
            ("Software", $"einzel {EngineBuild.Version}, solver behaviour {EngineBuild.SolverBehaviourVersion}"),
            ("Source", $"{Path.GetFileName(model)} {hash}"),
            ("Description", string.Create(
                CultureInfo.InvariantCulture,
                $"einzel render still, view {view} (azimuth {angles.Azimuth:G}, elevation {angles.Elevation:G}), {width} by {height} px, orthographic; {outcome.ElectrodeCount()} electrodes{(seeThrough ? " drawn translucent" : string.Empty)}, {outcome.Equipotentials.Count} equipotential levels, {outcome.Trajectories.Count} trajectories, {outcome.Density.Count} density contours")),
        };

        // The scale in words as well as in lines, because the lines are only as good as the
        // pixels they survive being resized into.
        if (ruler is not null)
        {
            text.Add(("Scale", $"each arm of the ruler in the {ruler.Corner} is {ruler.Label} along the model's {string.Join(", ", ruler.Arms.Select(a => a.Axis))} axis, drawn with the picture's own projection"));
        }

        // Every warning, not only the violations the band marks: a PNG travels without the
        // JSON that described it, so what qualified the picture has to travel inside it.
        for (var i = 0; i < outcome.Warnings.Count; i++)
        {
            var w = outcome.Warnings[i];
            text.Add(($"Warning {i + 1}", $"[{w.Severity}] {w.Code}: {w.Message}"));
        }

        return text;
    }
}
