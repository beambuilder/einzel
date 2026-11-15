namespace Einzel.Commands;

/// <summary>One colored triangle mesh in a composed picture.</summary>
/// <param name="VerticesMm">Positions as consecutive x, y, z triples, in millimetres.</param>
/// <param name="Normals">Unit normals, one triple per vertex.</param>
/// <param name="Triangles">Vertex indices, three per triangle.</param>
/// <param name="R">Red, zero to one.</param>
/// <param name="G">Green, zero to one.</param>
/// <param name="B">Blue, zero to one.</param>
public sealed record PictureMesh(
    IReadOnlyList<double> VerticesMm,
    IReadOnlyList<double> Normals,
    IReadOnlyList<int> Triangles,
    double R,
    double G,
    double B);

/// <summary>One colored polyline in a composed picture.</summary>
/// <param name="PointsMm">Consecutive x, y, z triples, in millimetres.</param>
/// <param name="R">Red, zero to one.</param>
/// <param name="G">Green, zero to one.</param>
/// <param name="B">Blue, zero to one.</param>
public sealed record PictureLine(IReadOnlyList<double> PointsMm, double R, double G, double B);

/// <summary>One layer of a composed picture, drawn whole before the next.</summary>
/// <param name="Name">What the layer is: field, paths, conductors or density.</param>
/// <param name="Meshes">Its surfaces.</param>
/// <param name="Lines">Its polylines.</param>
/// <param name="Alpha">How opaque it is, from zero to one.</param>
/// <param name="WritesDepth">Whether it occludes what is drawn after it.</param>
public sealed record PictureLayer(
    string Name,
    IReadOnlyList<PictureMesh> Meshes,
    IReadOnlyList<PictureLine> Lines,
    double Alpha,
    bool WritesDepth);

/// <summary>One color scale, as the picture actually draws it.</summary>
/// <param name="Quantity">What the colors mean: <c>energy</c> or <c>potential</c>.</param>
/// <param name="Unit">The unit the ends are in, <c>eV</c> or <c>V</c>.</param>
/// <param name="Low">The value the low end of the ramp stands for.</param>
/// <param name="High">The value the high end stands for.</param>
/// <param name="Diverging">
/// Whether the ramp is the diverging one, which is symmetric about zero, rather than the
/// sequential one. A caller drawing the swatch samples
/// <see cref="ColorRamp.Diverging"/> or <see cref="ColorRamp.At"/> accordingly.
/// </param>
/// <remarks>
/// <para>
/// <b>GRD-1's argument applied to a picture.</b> A trajectory bundle and a field are drawn
/// in colors that stand for numbers, and a color with no number beside it is a bare
/// quantity in the one place a reader cannot recompute it. The viewport anchors both ramps
/// once across everything shown, so the ends are a property of the whole picture rather
/// than of any one path or contour - which is exactly what makes them statable.
/// </para>
/// <para>
/// <b>Here rather than in the window, because the ends are not the observed range.</b> The
/// potential ramp is symmetric about earth: a legend reading the lowest and highest
/// potentials would not describe the picture, since a lens holding 0 and 500 V is drawn on
/// a ramp running -500 to +500. The window that computed that for itself had the rule in a
/// comment in its code-behind, which is how two surfaces come to say different things about
/// one model.
/// </para>
/// </remarks>
public sealed record ColorScale(
    string Quantity,
    string Unit,
    double Low,
    double High,
    bool Diverging)
{
    /// <summary>True where the two ends are one value, as for a monoenergetic bundle.</summary>
    /// <remarks>
    /// Every path of such a bundle is painted at the ramp's midpoint, so the scale it states
    /// is one value on one color and not a gradient across a range nothing is drawn over.
    /// </remarks>
    public bool IsSingleValue => Low == High;
}

/// <summary>
/// What the viewport draws, decided once, for everything that draws it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window and <c>einzel render still</c> draw from this and from nothing else.</b> A
/// still is only worth having if it is the picture the window shows: what colors an electrode
/// takes, which layers exist and in what order, which are translucent and whether they hide
/// what is behind them. Each of those was a decision inside the shell's GL control, and a
/// still that made them again would drift from the window the first time one of them
/// changed - the same way <c>run</c> and <c>test</c> came to disagree twice, by computing
/// one quantity two ways.
/// </para>
/// <para>
/// <b>In the command layer because UI-1 draws the line there.</b> The shell may reach the
/// engine through <c>Einzel.Commands</c> only, and may not produce render output; the
/// rasterizer that makes a still lives in <c>Einzel.Render</c>, which may not reference this
/// assembly. So the decisions live here, and the rasterizer is handed plain triangles, lines
/// and a matrix with no choices left in them.
/// </para>
/// </remarks>
public static class ViewportPicture
{
    /// <summary>The direction the scene is lit from, in world coordinates, not yet unit length.</summary>
    /// <remarks>
    /// In world space rather than eye space, which is what the window's shader has always
    /// done: the light does not follow the camera, so turning the instrument changes how its
    /// faces are lit, the way turning a real one under a lamp does.
    /// </remarks>
    public static readonly (double X, double Y, double Z) Light = (0.4, 0.7, 1.0);

    /// <summary>The share of each color a surface keeps however it faces the light.</summary>
    /// <remarks>
    /// A quarter, so a face turned edge-on to the light is dark rather than black and a
    /// conductor never disappears into the ground behind it.
    /// </remarks>
    public const double Ambient = 0.25;

    /// <summary>How opaque the conductors are when a reader asks to see through them.</summary>
    public const double SeeThroughOpacity = 0.35;

    /// <summary>How opaque the density shells are.</summary>
    /// <remarks>
    /// Translucent always, because they are nested: an opaque outer contour would hide the
    /// core, which is the part of a packet a reader is looking for.
    /// </remarks>
    public const double DensityOpacity = 0.30;

    /// <summary>The named views, as azimuth and elevation in degrees.</summary>
    /// <remarks>
    /// <para>
    /// <b>Named rather than only the gestures.</b> Ion optics is read as an axial section and
    /// two transverse ones, and getting to one by dragging is approximate where a name is
    /// exact.
    /// </para>
    /// <para>
    /// <b>Iso first, because a cross-section is uncapped.</b> A translational solve's
    /// conductors are prisms with no ends, so looking straight down the invariant axis - the
    /// side view - shows none of them. The window opens here and a still defaults here.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, (double Azimuth, double Elevation)> Views =
        new Dictionary<string, (double, double)>(StringComparer.Ordinal)
        {
            ["iso"] = (-32.0, 24.0),
            ["side"] = (0.0, 0.0),
            ["top"] = (0.0, 90.0),
            ["front"] = (90.0, 0.0),
        };

    /// <summary>How brightly a surface is drawn, given its normal.</summary>
    /// <param name="nx">Normal, x.</param>
    /// <param name="ny">Normal, y.</param>
    /// <param name="nz">Normal, z.</param>
    /// <returns>The factor its color is multiplied by, from <see cref="Ambient"/> to one.</returns>
    /// <remarks>
    /// <b>Two-sided</b>: the absolute value of the lambert term, because a surface's winding
    /// says which way is out and a viewport should not go dark because one normal is
    /// backwards - and because a printed board and a zero-width cap are sheets, lit from
    /// whichever side a reader looks at them. A zero normal is no direction at all and gets
    /// the ambient term alone.
    /// </remarks>
    public static double Brightness(double nx, double ny, double nz)
    {
        var length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));

        if (!(length > 0.0))
        {
            return Ambient;
        }

        var (lx, ly, lz) = Light;
        var light = Math.Sqrt((lx * lx) + (ly * ly) + (lz * lz));
        var lambert = Math.Abs(((nx * lx) + (ny * ly) + (nz * lz)) / (length * light));

        return Ambient + ((1.0 - Ambient) * lambert);
    }

    /// <summary>How brightly a line is drawn.</summary>
    /// <remarks>
    /// A line has no surface, so it is given the constant normal the window has always
    /// uploaded with its paths - one shader for both, rather than two to keep in step.
    /// </remarks>
    public static double LineBrightness => Brightness(0.0, 0.0, 1.0);

    /// <summary>Composes what the viewport draws, in the order it is drawn.</summary>
    /// <param name="outcome">What the command layer measured.</param>
    /// <param name="conductorOpacity">How opaque the conductors are, from zero to one.</param>
    /// <returns>The field, the paths, the conductors and the density, as layers.</returns>
    /// <remarks>
    /// <para>
    /// <b>The paths and the field first, and the order is the point.</b> An ion flies down
    /// the bore, so it is inside every electrode it passes; drawn after opaque metal it sits
    /// behind the near wall at every pixel. Drawn first, into depth, the metal blends over it
    /// and the flight reads through. The field is drawn on the section plane through the
    /// bore, for the same reason.
    /// </para>
    /// <para>
    /// <b>Translucent layers write no depth.</b> A translucent surface that writes depth hides
    /// everything drawn behind it afterwards, so "see through the metal" would reveal only
    /// what happened to be drawn first. They are still depth-TESTED, so the paths drawn
    /// before them still read through.
    /// </para>
    /// <para>
    /// <b>RND-8 is not decided here.</b> A diffusive model has no paths in its bundle at all,
    /// so an empty layer is the transport mode's answer carried through.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<PictureLayer> Compose(ViewportOutcome outcome, double conductorOpacity = 1.0)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var opacity = Math.Clamp(conductorOpacity, 0.0, 1.0);

        return
        [
            new PictureLayer("field", [], Field(outcome), 1.0, WritesDepth: true),
            new PictureLayer("paths", [], Paths(outcome), 1.0, WritesDepth: true),
            new PictureLayer("conductors", Conductors(outcome), [], opacity, WritesDepth: opacity >= 1.0),
            new PictureLayer("density", Density(outcome), [], DensityOpacity, WritesDepth: false),
        ];
    }

    /// <summary>The density layer alone, for a frame of a run that is still going.</summary>
    /// <param name="outcome">The frame.</param>
    /// <returns>The layer <see cref="Compose"/> would give for it.</returns>
    /// <remarks>
    /// Only the density changes between frames - the sequencer refuses a stage that moves an
    /// electrode - so re-composing a whole trap's metal per frame would be work the window
    /// then throws away.
    /// </remarks>
    public static PictureLayer DensityLayer(ViewportOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new PictureLayer("density", Density(outcome), [], DensityOpacity, WritesDepth: false);
    }

    /// <summary>The scales the colors in this picture mean, each one that means something.</summary>
    /// <param name="outcome">The bundle the picture is composed from.</param>
    /// <returns>
    /// Energy where there are trajectories, then the potential scales, in drawing order;
    /// empty for a picture whose layers carry no quantity, such as bare geometry at earth.
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>A null outcome, meaning no picture, has no scales.</b>
    /// </para>
    /// <para>
    /// <b>Shown only where it means something</b>, which is the rule the viewport draws by: a
    /// diffusive model has no trajectories, so an energy scale under it would state a range
    /// over an empty set. Every end here comes from the helper the matching layer is colored
    /// with, so a legend cannot describe a ramp the picture is not using.
    /// </para>
    /// <para>
    /// <b>One row per span rather than one row per quantity</b>, because the field and the
    /// metal anchor separately: contour levels are chosen inside the domain and generally do
    /// not reach the electrodes, and a driven electrode is colored by the peak it reaches
    /// rather than the DC it sits at. Where the two agree this collapses to one row saying
    /// <c>potential</c>; where they differ, saying one number for both would be describing a
    /// ramp neither layer uses. Whether the two layers should share a span is a separate
    /// question about the picture and is recorded in <c>docs/shell.md</c>.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ColorScale> Scales(ViewportOutcome? outcome)
    {
        // No picture, no scales: what a window shows when it has cleared its viewport or has
        // not drawn one yet. Decided here so it is not a second rule in code-behind.
        if (outcome is null)
        {
            return [];
        }

        var scales = new List<ColorScale>(3);

        if (outcome.Trajectories.Count > 0 && outcome.LowestEnergyEv is not null)
        {
            var (low, high) = EnergyEnds(outcome);

            scales.Add(new ColorScale("energy", "eV", low, high, Diverging: false));
        }

        var field = outcome.Equipotentials.Count > 0 ? PotentialSpan(outcome) : 0.0;

        // The TRUE widest rather than Shading.Span, whose fallback of one volt exists so the
        // fraction it feeds cannot divide by zero. A geometry entirely at earth has no
        // potential scale to state, and stating a one-volt one would invent a range. Over the
        // conductors that are DRAWN, as the paint's span is: metal with no triangles is not
        // on the screen and must not widen a scale stated for what is.
        var metal = Shading.Widest(Drawn(outcome));

        // ONE ROW WHERE THE TWO ARE THE SAME RAMP TO LOOK AT, which is what a legend is
        // read against. The tolerance is relative and perceptual rather than a rounding: on
        // the shipped einzel lens the metal reaches 500 V and the field 502 - the interpolant
        // overshooting a conductor's step, which it does by construction - and two rows
        // apart by four parts in a thousand are two rows nobody can tell apart, inviting a
        // reader to go looking for the difference between them. The WIDER is reported, so
        // nothing drawn falls outside the scale that is stated.
        var wider = Math.Max(field, metal);

        if (field > 0.0 && metal > 0.0 && Math.Abs(field - metal) <= 0.01 * wider)
        {
            scales.Add(new ColorScale("potential", "V", -wider, wider, Diverging: true));

            return scales;
        }

        if (field > 0.0)
        {
            scales.Add(new ColorScale("field", "V", -field, field, Diverging: true));
        }

        if (metal > 0.0)
        {
            scales.Add(new ColorScale("electrodes", "V", -metal, metal, Diverging: true));
        }

        return scales;
    }

    /// <summary>
    /// How far either way the diverging ramp runs: symmetric about earth, because stretching
    /// it across the observed range puts the neutral color at the arithmetic middle and an
    /// earthed electrode would be painted like a negative one.
    /// </summary>
    private static double PotentialSpan(ViewportOutcome outcome) => Math.Max(
        Math.Abs(outcome.LowestPotentialVolts ?? 0.0),
        Math.Abs(outcome.HighestPotentialVolts ?? 0.0));

    /// <summary>The ends of the sequential ramp, anchored once across the whole bundle.</summary>
    private static (double Low, double High) EnergyEnds(ViewportOutcome outcome)
    {
        var low = outcome.LowestEnergyEv ?? 0.0;

        return (low, outcome.HighestEnergyEv ?? low);
    }

    private static List<PictureLine> Field(ViewportOutcome outcome)
    {
        var span = PotentialSpan(outcome);

        var lines = new List<PictureLine>();

        foreach (var level in outcome.Equipotentials)
        {
            var fraction = span > 0.0
                ? 0.5 + (0.5 * Math.Clamp(level.PotentialVolts / span, -1.0, 1.0))
                : 0.5;

            var (r, g, b) = ColorRamp.Diverging(fraction);

            foreach (var polyline in level.PathsMm)
            {
                if (polyline.Count >= 6)
                {
                    lines.Add(new PictureLine(polyline, r, g, b));
                }
            }
        }

        return lines;
    }

    private static List<PictureLine> Paths(ViewportOutcome outcome)
    {
        var (low, high) = EnergyEnds(outcome);

        // A degenerate range gives a half, not a division: a monoenergetic beam in a
        // field-free drift is the simplest model anyone writes, and dividing by a zero width
        // paints the bundle NaN.
        var width = high - low;

        var lines = new List<PictureLine>();

        foreach (var path in outcome.Trajectories)
        {
            if (path.PointsMm.Count < 2)
            {
                continue;
            }

            var mean = path.EnergyEv.Count > 0 ? path.EnergyEv.Average() : low;
            var fraction = width > 0.0 ? Math.Clamp((mean - low) / width, 0.0, 1.0) : 0.5;
            var (r, g, b) = ColorRamp.At(fraction);

            var flat = new double[3 * path.PointsMm.Count];

            for (var p = 0; p < path.PointsMm.Count; p++)
            {
                flat[3 * p] = path.PointsMm[p][0];
                flat[(3 * p) + 1] = path.PointsMm[p][1];
                flat[(3 * p) + 2] = path.PointsMm[p][2];
            }

            lines.Add(new PictureLine(flat, r, g, b));
        }

        return lines;
    }

    /// <summary>The conductors that have anything to draw: what both the paint and the legend span.</summary>
    private static List<ConductorSurface> Drawn(ViewportOutcome outcome) =>
        outcome.Conductors.Where(c => c.Triangles.Count > 0).ToList();

    private static List<PictureMesh> Conductors(ViewportOutcome outcome)
    {
        var drawn = Drawn(outcome);
        var span = Shading.Span(drawn);
        var meshes = new List<PictureMesh>();

        foreach (var conductor in drawn)
        {
            var (r, g, b) = ColorRamp.Diverging(Shading.Fraction(conductor, span));

            meshes.Add(new PictureMesh(conductor.VerticesMm, conductor.Normals, conductor.Triangles, r, g, b));
        }

        return meshes;
    }

    private static List<PictureMesh> Density(ViewportOutcome outcome)
    {
        // Anchored on the decade rather than on this frame's own peak. A diffusing packet's
        // peak falls as it spreads, so levels taken per frame would fall with it and a film
        // of a packet spreading would show a packet doing nothing.
        var deepest = outcome.Density.Count == 0 ? 1 : outcome.Density.Max(d => d.DecadesBelowPeak);
        var meshes = new List<PictureMesh>();

        foreach (var shell in outcome.Density)
        {
            if (shell.Triangles.Count == 0)
            {
                continue;
            }

            var fraction = deepest > 0
                ? 1.0 - (Math.Clamp(shell.DecadesBelowPeak, 0, deepest) / (double)deepest)
                : 1.0;

            var (r, g, b) = ColorRamp.At(fraction);

            meshes.Add(new PictureMesh(shell.VerticesMm, shell.Normals, shell.Triangles, r, g, b));
        }

        return meshes;
    }
}
