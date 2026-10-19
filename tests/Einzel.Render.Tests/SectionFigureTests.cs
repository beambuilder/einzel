using System.Globalization;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Solved;
using Einzel.Io;
using Einzel.Library;
using Einzel.Render;
using Xunit.Abstractions;

namespace Einzel.Render.Tests;

/// <summary>
/// A section figure of a real device, drawn headlessly.
/// </summary>
/// <remarks>
/// RND-1 requires a publication figure to come out on Linux, in CI, with no
/// display attached. These run in exactly that environment, which is the point:
/// a renderer that needs a window is a shell feature no matter where its code
/// lives.
/// </remarks>
public sealed class SectionFigureTests(ITestOutputHelper output)
{
    private static CompiledModel Compile(string template)
    {
        var document = ModelJson.Parse(DeviceTemplates.Read(template));
        var validation = ModelValidator.Validate(document, null);

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }

    /// <summary>A dimension is measured from the geometry, never written down.</summary>
    /// <remarks>
    /// <para>
    /// The memo's own figures are line drawings <em>with</em> dimensions, and a section
    /// without them says what the instrument looks like and not how big any of it is.
    /// </para>
    /// <para>
    /// <b>What a dimension declares is the two points it spans.</b> The length is the
    /// distance between them, computed when the figure is drawn - so it cannot part
    /// company with the model, which is the whole reason to dimension a drawing rather
    /// than annotate it. A typed number would be a second statement of something the
    /// model already says.
    /// </para>
    /// </remarks>
    [Fact]
    public void ADimensionIsMeasuredRatherThanDeclared()
    {
        var lens = Compile("einzel-lens");

        var spec = new RenderSpec
        {
            Equipotentials = 0,
            Dimensions =
            [
                new DimensionDocument
                {
                    From = new VectorValue([-30.0, 0.0, 0.0], "mm"),
                    To = new VectorValue([30.0, 0.0, 0.0], "mm"),
                    Label = "span",
                },
            ],
        };

        var figure = SectionRenderer.Render(lens, spec);

        var text = figure.Scene.Texts.Single(t => t.Layer == "dimension");

        output.WriteLine(text.Text);

        // 60 mm between the two declared points, measured and formatted here rather than
        // taken from anything the caller wrote.
        Assert.Equal("span 60 mm", text.Text);

        // Two extension lines, a dimension line and two arrowheads.
        Assert.Equal(5, figure.Scene.Paths.Count(p => p.Layer == "dimension"));
    }

    /// <summary>A dimension over parameters follows the model when they change.</summary>
    /// <remarks>
    /// Section 9's rule for a model - "every placement is a parametric expression, never
    /// a baked number" - is not weaker for a drawing of it. A dimension written over the
    /// model's own parameters describes the geometry rather than where the geometry used
    /// to be, and this is the assertion that says so: one figure spec, two models, two
    /// different measurements and no edit in between.
    /// </remarks>
    [Fact]
    public void AParametricDimensionFollowsTheModel()
    {
        var spec = new RenderSpec
        {
            Equipotentials = 0,
            Dimensions =
            [
                new DimensionDocument
                {
                    From = new VectorValue([0.0, 0.0, 0.0], "mm") { Expression = ["0", "0", "0"] },
                    To = new VectorValue([0.0, 0.0, 0.0], "mm")
                    {
                        Expression = ["boreRadius", "0", "0"],
                    },
                    Label = "bore",
                },
            ],
        };

        static string Measured(CompiledModel model, RenderSpec spec) =>
            SectionRenderer.Render(model, spec).Scene.Texts
                .Single(t => t.Layer == "dimension").Text;

        var document = ModelJson.Parse(DeviceTemplates.Read("einzel-lens"));

        var nominal = ModelValidator.Validate(document, null).Model!;
        var widened = ModelValidator.Validate(
            document,
            new Dictionary<string, Quantity>(StringComparer.Ordinal)
            {
                ["boreRadius"] = Quantity.From(9.0, "mm"),
            }).Model!;

        var before = Measured(nominal, spec);
        var after = Measured(widened, spec);

        output.WriteLine($"{before}  ->  {after}");

        Assert.NotEqual(before, after);
        Assert.Equal("bore 9 mm", after);
    }

    /// <summary>
    /// An analytic model's whole flight fits on its own page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A model with no declared solve domain takes its extent from the instrument's own
    /// points, and until now those were the source and the detector. In a reflectron
    /// they are the <em>same point</em> - the ion is caught where it launched - so the
    /// page was a box a tenth of a millimetre across while the ion travelled 1.3 m into
    /// the mirror and back. The scaffolded reflectron, which is the first thing anybody
    /// renders, drew its turning point 105 metres off a 160 mm page.
    /// </para>
    /// <para>
    /// The flight is now included in the extent, which costs nothing: the trajectory had
    /// to be flown to be drawn, and it is now flown before the page is chosen rather
    /// than after.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnAnalyticFlightFitsOnItsOwnPage()
    {
        var figure = SectionRenderer.Render(
            AnalyticModels.Reflectron(), new RenderSpec { Equipotentials = 6 });

        var drawn = figure.Scene.Paths
            .Where(p => p.Layer == "trajectory")
            .SelectMany(p => p.Points)
            .ToList();

        Assert.NotEmpty(drawn);

        var minX = drawn.Min(p => p.X);
        var maxX = drawn.Max(p => p.X);
        var minY = drawn.Min(p => p.Y);
        var maxY = drawn.Max(p => p.Y);

        output.WriteLine(
            $"page {figure.Scene.WidthMm:F1} by {figure.Scene.HeightMm:F1} mm; trajectory "
            + $"x {minX:F2}..{maxX:F2}, y {minY:F2}..{maxY:F2}");

        Assert.InRange(minX, 0.0, figure.Scene.WidthMm);
        Assert.InRange(maxX, 0.0, figure.Scene.WidthMm);
        Assert.InRange(minY, 0.0, figure.Scene.HeightMm);
        Assert.InRange(maxY, 0.0, figure.Scene.HeightMm);

        // And it fills the page rather than sitting in a corner of one that was made
        // large enough to contain it by accident.
        Assert.True(
            maxX - minX > 0.5 * figure.Scene.WidthMm,
            $"the flight spans only {maxX - minX:F1} of {figure.Scene.WidthMm:F1} mm");
    }

    [Fact]
    public void TheEinzelLensDrawsAsLineWork()
    {
        var spec = new RenderSpec
        {
            WidthMm = 170.0,
            Equipotentials = 14,
            SampleColumns = 300,
            Caption = "Einzel lens: three coaxial tubes, 500 V on the centre one.",
        };

        var figure = SectionRenderer.Render(Compile("einzel-lens"), spec);

        output.WriteLine($"page {figure.Scene.WidthMm:F1} x {figure.Scene.HeightMm:F1} mm");
        output.WriteLine($"{figure.Scene.Paths.Count} paths, {figure.Scene.Texts.Count} text runs");
        output.WriteLine(
            $"trajectory {figure.TrajectoryPointsBeforeDecimation} points decimated to "
            + $"{figure.TrajectoryPoints} at {figure.DecimationToleranceMm:G3} mm");

        foreach (var layer in figure.Scene.Paths.Select(p => p.Layer).Distinct().Order())
        {
            output.WriteLine($"  layer {layer}: {figure.Scene.Paths.Count(p => p.Layer == layer)} paths");
        }

        // Conductors, equipotentials and a trajectory: the three things a section
        // figure of an ion-optical element is for.
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "conductors");
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "equipotentials");
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "trajectory");

        // Three tubes, each appearing above and below the axis because a
        // cylindrical solve is a half-plane and a ring is two conductors on the
        // page. Drawn from the signed distance rather than from any knowledge of
        // what a tube is.
        var conductors = figure.Scene.Paths.Count(p => p.Layer == "conductors");

        output.WriteLine($"conductor runs {conductors}, of which closed "
            + $"{figure.Scene.Paths.Count(p => p.Layer == "conductors" && p.Closed)}");

        Assert.Equal(6, conductors);
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "axis");

        // One warning and no other. The 500 V center tube runs out to the grounded outer
        // wall, so the edge nodes on its outer face hold 500 V or zero on the last bit - and
        // the equipotentials drawn within a cell or two of that contact depend on which. The
        // lens itself does not: moving that face a hair either side leaves the flight time
        // bit-identical and every potential more than three cells from the wall unchanged.
        var warning = Assert.Single(figure.Warnings);

        Assert.Equal(SharedFaceNodes.Code, warning.Code);
        Assert.Contains("'centre' meets the grounded top edge", warning.Message, StringComparison.Ordinal);
    }

    /// <summary>A drift tube declaring diffusive transport, so no path exists to draw.</summary>
    private const string DiffusiveTube = """
    {
      "schemaVersion": "0.4",
      "name": "diffusive-tube",
      "ion": { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
      "source": {
        "position": { "value": [2, 0, 0], "unit": "mm" },
        "direction": { "value": [1, 0, 0] },
        "accelerationPotential": { "value": 0.001, "unit": "V" }
      },
      "fields": [
        { "type": "uniform", "field": { "value": [2000, 0, 0], "unit": "V/m" } }
      ],
      "detector": {
        "planePoint": { "value": [40, 0, 0], "unit": "mm" },
        "normal": { "value": [-1, 0, 0] }
      },
      "transport": {
        "mode": "diffusion",
        "maximumFlightTime": { "value": 100, "unit": "us" },
        "densityGrid": {
          "minX": { "value": -2, "unit": "mm" }, "maxX": { "value": 40, "unit": "mm" },
          "minY": { "value": -6, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
          "intervalsX": 128, "intervalsY": 32
        },
        "gas": {
          "model": "hardSphere",
          "pressure": { "value": 1, "unit": "mbar" },
          "mass": { "value": 28.0134, "unit": "Da" },
          "crossSection": { "value": 250, "unit": "Å^2" }
        }
      }
    }
    """;

    /// <summary>A Gaussian blob of ions in the middle of the tube.</summary>
    private static Transport.Diffusion.DensityField Blob()
    {
        var grid = Fields.Solved.Grid2D.OverBox(-0.002, -0.006, 0.040, 0.006, 128, 32);
        var density = new Transport.Diffusion.DensityField(grid);

        for (var j = 0; j < grid.CountY; j++)
        {
            for (var i = 0; i < grid.CountX; i++)
            {
                var dx = (grid.X(i) - 0.020) / 0.004;
                var dy = grid.Y(j) / 0.002;

                density[i, j] = 1e9 * Math.Exp(-0.5 * ((dx * dx) + (dy * dy)));
            }
        }

        return density;
    }

    [Fact]
    public void ADiffusiveModelDrawsItsDensityInsteadOfATrajectory()
    {
        // RND-8's other half. Refusing to draw lines through a diffusive region was
        // always right and, on its own, entirely negative: the mode's principal
        // output could not be drawn at all, so the honest figure was an empty box.
        var document = ModelJson.Parse(DiffusiveTube);
        var validation = ModelValidator.Validate(document, null);

        Assert.True(validation.IsValid, validation.IsValid ? "" : validation.Errors[0].Constraint);

        var spec = new RenderSpec { WidthMm = 160.0, Equipotentials = 6, DensityContours = 5 };

        var figure = SectionRenderer.Render(validation.Model!, spec, null, [Blob()]);

        foreach (var layer in figure.Scene.Paths.Select(p => p.Layer).Distinct().Order())
        {
            output.WriteLine($"  layer {layer}: {figure.Scene.Paths.Count(p => p.Layer == layer)} paths");
        }

        Assert.Contains(figure.Scene.Paths, p => p.Layer == "density");
        Assert.DoesNotContain(figure.Scene.Paths, p => p.Layer == "trajectory");

        Assert.Contains(figure.Warnings, w => w.Code == "render.no-trajectories");
        Assert.DoesNotContain(figure.Warnings, w => w.Code == "render.density-empty");

        // GRD-12's argument applied to a contour set: a reader has to be able to
        // tell what the lines are worth, and a density plotted without its levels is
        // a shape rather than a measurement.
        var levels = Assert.Single(
            figure.Scene.Provenance, line => line.StartsWith("density contours", StringComparison.Ordinal));

        output.WriteLine(levels);

        // Decades, so five levels span five orders of magnitude below the peak.
        Assert.Equal(5, levels.Split(':')[1].Split(',').Length);
    }

    [Fact]
    public void AnEmptyDensityIsSaidToBeEmptyRatherThanDrawnAsNothing()
    {
        // A run whose ions have all reached a boundary leaves a residue many orders
        // below one ion in the whole domain. Contouring that draws the shape of the
        // round-off; drawing nothing and saying nothing looks identical to a figure
        // where the density was never computed. So it is named, with the change that
        // would produce a picture.
        var document = ModelJson.Parse(DiffusiveTube);
        var validation = ModelValidator.Validate(document, null);

        var grid = Fields.Solved.Grid2D.OverBox(-0.002, -0.006, 0.040, 0.006, 128, 32);
        var empty = new Transport.Diffusion.DensityField(grid);

        var figure = SectionRenderer.Render(
            validation.Model!, new RenderSpec { DensityContours = 5 }, null, [empty]);

        Assert.DoesNotContain(figure.Scene.Paths, p => p.Layer == "density");
        Assert.Contains(figure.Warnings, w => w.Code == "render.density-empty");

        output.WriteLine(figure.Warnings.Single(w => w.Code == "render.density-empty").Message);
    }

    [Fact]
    public void EverySvgCoordinateLandsOnThePage()
    {
        // The cheapest exact check that the world-to-page map is right. A sign error
        // or a mismatched origin puts geometry off the page, where it is invisible
        // in a viewer that clips and mysterious in one that does not.
        var spec = new RenderSpec { WidthMm = 150.0, Equipotentials = 8, SampleColumns = 200 };
        var figure = SectionRenderer.Render(Compile("quadrupole"), spec);

        foreach (var path in figure.Scene.Paths)
        {
            foreach (var point in path.Points)
            {
                Assert.InRange(point.X, -0.001, figure.Scene.WidthMm + 0.001);
                Assert.InRange(point.Y, -0.001, figure.Scene.HeightMm + 0.001);
            }
        }

        output.WriteLine(
            $"{figure.Scene.Paths.Sum(p => p.Points.Count)} vertices, all inside "
            + $"{figure.Scene.WidthMm:F1} x {figure.Scene.HeightMm:F1} mm");
    }

    [Fact]
    public void LabelsStayTextInBothFormats()
    {
        // RND-6. A figure has to be relabellable for a different venue without
        // regenerating it, which means the characters are characters in the output
        // and not outlines that happen to look like letters.
        var spec = new RenderSpec { WidthMm = 120.0, Caption = "Quadrupole section" };
        var figure = SectionRenderer.Render(Compile("quadrupole"), spec);

        var svg = SvgWriter.Write(figure.Scene);
        var pdf = System.Text.Encoding.Latin1.GetString(PdfWriter.Write(figure.Scene));

        Assert.Contains("<text ", svg, StringComparison.Ordinal);
        Assert.Contains("Quadrupole section</text>", svg, StringComparison.Ordinal);

        Assert.Contains("(Quadrupole section) Tj", pdf, StringComparison.Ordinal);
        Assert.Contains("/BaseFont /Helvetica", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePdfIsStructurallyValid()
    {
        // Written by hand, so the cross-reference table is the part most likely to be
        // wrong and the part a reader refuses on. Every offset must name the object
        // it claims to.
        var spec = new RenderSpec { WidthMm = 120.0, Equipotentials = 6, SampleColumns = 150 };
        var figure = SectionRenderer.Render(Compile("quadrupole"), spec);

        var bytes = PdfWriter.Write(figure.Scene);
        var text = System.Text.Encoding.Latin1.GetString(bytes);

        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);

        var xref = text.LastIndexOf("startxref", StringComparison.Ordinal);
        var declared = int.Parse(
            text[(xref + 9)..text.IndexOf("%%EOF", xref, StringComparison.Ordinal)].Trim(),
            CultureInfo.InvariantCulture);

        Assert.Equal("xref", text.Substring(declared, 4));

        // Each offset in the table has to land on that object's header.
        var table = text[(declared + 4)..];
        var lines = table.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var found = 0;

        for (var i = 2; i < lines.Length && lines[i].EndsWith(" n ", StringComparison.Ordinal); i++)
        {
            var offset = int.Parse(lines[i][..10], CultureInfo.InvariantCulture);

            Assert.Equal($"{i - 1} 0 obj", text.Substring(offset, $"{i - 1} 0 obj".Length));
            found++;
        }

        output.WriteLine($"{bytes.Length:N0} bytes, {found} objects, xref at {declared}");

        Assert.Equal(6, found);
    }

    [Fact]
    public void AFigureFromATaintedFieldSaysSoOnTheFace()
    {
        // RND-11 and GRD-5: a preview-tier or otherwise qualified result must be
        // visually distinguishable in rendered output, not merely noted in metadata
        // nobody opens. A figure is the artifact most likely to be shown to an
        // audience with none of the uncertainty apparatus attached.
        var document = ModelJson.Parse(DeviceTemplates.Read("quadrupole"));

        // A tolerance below round-off: the solve stalls and reports not-converged.
        var strained = document with
        {
            Fields =
            [
                document.Fields![0] with { Solve = document.Fields[0].Solve! with { Tolerance = 1e-30 } },
            ],
        };

        var validation = ModelValidator.Validate(strained, null);
        Assert.True(validation.IsValid);

        var figure = SectionRenderer.Render(
            validation.Model!, new RenderSpec { WidthMm = 120.0, Equipotentials = 4, SampleColumns = 120 });

        var warning = Assert.Single(figure.Warnings);
        Assert.Equal("field.not-converged", warning.Code);

        // On the page, not only in the comment block.
        Assert.Contains(figure.Scene.Paths, p => p.Layer == "taint");
        Assert.Contains(figure.Scene.Texts, t => t.Text.StartsWith("QUALIFIED", StringComparison.Ordinal));

        var svg = SvgWriter.Write(figure.Scene);
        Assert.Contains("field.not-converged", svg, StringComparison.Ordinal);

        output.WriteLine(figure.Scene.Provenance[^1]);
    }

    /// <summary>
    /// A page narrower than its two margins is refused as the caller's number, with a
    /// code and a suggestion, rather than failing inside the renderer as an internal error.
    /// </summary>
    /// <remarks>
    /// Found by asking for <c>--width-mm 12</c> in the belief that it was a window onto the
    /// model. The drawing area came out at minus eight millimetres, the decimation
    /// tolerance went negative, and the run ended in <c>INTERNAL_ERROR</c> - "a defect in
    /// einzel, not in your model" - for what was a mistaken flag.
    /// </remarks>
    [Fact]
    public void APageNarrowerThanItsMarginsIsRefusedNotCrashed()
    {
        var lens = Compile("einzel-lens");
        var spec = new RenderSpec { WidthMm = 12.0, Equipotentials = 4 };

        var refusal = Assert.Throws<Core.Errors.EinzelException>(() => SectionRenderer.Render(lens, spec));

        output.WriteLine($"{refusal.Error.Code} {refusal.Error.Path}: {refusal.Error.Constraint}");
        Assert.Equal(Core.Errors.ErrorCodes.ValueOutOfBounds, refusal.Error.Code);
        Assert.Equal("/widthMm", refusal.Error.Path);
        Assert.Contains("twice the margin", refusal.Error.Constraint, StringComparison.Ordinal);
    }
}
