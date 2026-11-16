namespace Einzel.Commands;

/// <summary>
/// Where the camera sits and how much of the instrument it holds, for one view.
/// </summary>
/// <param name="CenterMm">The middle of what is drawn, in millimetres, in the model's frame.</param>
/// <param name="HalfWidthMm">Half its extent across the screen, in millimetres.</param>
/// <param name="HalfHeightMm">Half its extent up the screen, in millimetres.</param>
/// <param name="HalfDepthMm">Half its extent into the screen, in millimetres.</param>
/// <param name="Azimuth">Turn about the vertical, in degrees. Zero is the side view.</param>
/// <param name="Elevation">Tilt above the horizontal, in degrees.</param>
/// <remarks>
/// <para>
/// <b>Orthographic, not perspective, and that is a decision rather than a simplification.</b>
/// An ion-optics drawing is read for where things are <em>along the axis</em> - a mirror's
/// penetration depth, where a packet turns round, how far a slit is from the rod's back -
/// and perspective is precisely what distorts that. The WPF viewport opened perspective by
/// default and the axial positions could not be read off it.
/// </para>
/// <para>
/// <b>Fitted to what this view sees, not to a sphere around the instrument.</b> The first
/// version framed half the diagonal of the bounding box, which is the same whatever the view
/// and so never jumps when the camera turns - and put a 767 mm by 30 mm mirror pair in the
/// middle third of the picture, because a long thin instrument's diagonal is its length and
/// its screen height was sized to that too. The window frames one view rather than every
/// view at once - a named view and Fit frame what that view sees, and an orbit keeps the
/// scale it was given rather than refitting - so nothing is gained by being turn-invariant.
/// The box is measured in the view's own coordinates.
/// </para>
/// </remarks>
public sealed record Framing(
    (double X, double Y, double Z) CenterMm,
    double HalfWidthMm,
    double HalfHeightMm,
    double HalfDepthMm,
    double Azimuth,
    double Elevation)
{
    /// <summary>How much room is left around the instrument, as a fraction of its extent.</summary>
    private const double Margin = 1.08;

    /// <summary>Measures a scene, including its trajectories and its density, for one view.</summary>
    /// <param name="scene">What the command layer produced.</param>
    /// <param name="azimuth">Turn about the vertical, in degrees.</param>
    /// <param name="elevation">Tilt above the horizontal, in degrees.</param>
    /// <returns>A framing that holds everything drawn.</returns>
    /// <remarks>
    /// <para>
    /// <b>The flight is measured too, not only the metal.</b> A reflectron's ion turns round
    /// outside every conductor, and a page chosen from the conductors alone puts the turning
    /// point off it - which is how the scaffolded model once drew its turning point a hundred
    /// metres off a 160 mm page.
    /// </para>
    /// <para>
    /// <b>And so is the density, for the same reason one dimension over.</b> A diffusive model
    /// has no trajectories by construction (RND-8), and it need not have electrodes either -
    /// a drift tube in a declared uniform field has neither. Measured from conductors and
    /// paths alone such a scene is empty, and the packet is drawn entirely outside the
    /// frustum: a blank viewport for exactly the model class the watch exists to serve.
    /// </para>
    /// </remarks>
    public static Framing Measure(ViewportOutcome scene, double azimuth, double elevation)
    {
        ArgumentNullException.ThrowIfNull(scene);

        return Around(Points(scene), azimuth, elevation);
    }

    /// <summary>Frames a set of points for one view.</summary>
    /// <param name="pointsMm">Positions in millimetres, in the model's frame.</param>
    /// <param name="azimuth">Turn about the vertical, in degrees.</param>
    /// <param name="elevation">Tilt above the horizontal, in degrees.</param>
    /// <returns>The smallest framing, at those angles, that holds every point.</returns>
    public static Framing Around(
        IEnumerable<(double X, double Y, double Z)> pointsMm, double azimuth, double elevation)
    {
        ArgumentNullException.ThrowIfNull(pointsMm);

        var r = Rotation(azimuth, elevation);

        double lowU = double.MaxValue, lowV = double.MaxValue, lowW = double.MaxValue;
        double highU = double.MinValue, highV = double.MinValue, highW = double.MinValue;
        var seen = false;

        foreach (var (x, y, z) in pointsMm)
        {
            var (u, v, w) = Apply(r, x, y, z);

            seen = true;
            lowU = Math.Min(lowU, u); highU = Math.Max(highU, u);
            lowV = Math.Min(lowV, v); highV = Math.Max(highV, v);
            lowW = Math.Min(lowW, w); highW = Math.Max(highW, w);
        }

        // Nothing to hold: a millimetre about the origin, so the projection is finite and a
        // viewport opened on a scene still being computed draws an empty frame, not NaN.
        if (!seen)
        {
            return new Framing((0.0, 0.0, 0.0), 1.0, 1.0, 1.0, azimuth, elevation);
        }

        return FromView(
            r,
            ((lowU + highU) / 2.0, (lowV + highV) / 2.0, (lowW + highW) / 2.0),
            (highU - lowU) / 2.0,
            (highV - lowV) / 2.0,
            (highW - lowW) / 2.0,
            azimuth,
            elevation);
    }

    /// <summary>A framing that holds this one and another, at this one's camera angles.</summary>
    /// <param name="other">The framing to take in.</param>
    /// <returns>The smallest framing at this one's angles containing both boxes.</returns>
    /// <remarks>
    /// <para>
    /// <b>A watched packet moves, and the frame is measured before it does.</b> A TIMS
    /// elution is seeded near the entrance and elutes forty millimetres away, so a camera
    /// framed from the first instant loses the packet it was opened to watch. Re-measuring
    /// per frame is the obvious answer and is worse: the box would breathe with the packet,
    /// which reads as a camera lurching rather than as a packet drifting. So the frame only
    /// ever grows.
    /// </para>
    /// <para>
    /// Exact when both were measured at the same angles, which is the only way
    /// <see cref="ViewportCamera"/> uses it: two boxes in one set of view coordinates have a
    /// union that is a box. At different angles the other box's corners are carried across,
    /// which still contains it and can be much larger than it needs to be - an iso box carried
    /// into the side view of a cube is about twice the cube's height. Applied on every turn of
    /// the camera that compounds, which is how the window's frame once grew on each click of a
    /// named view.
    /// </para>
    /// </remarks>
    public Framing Union(Framing other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return Around(Corners().Concat(other.Corners()), Azimuth, Elevation);
    }

    /// <summary>The identity, for a control with nothing to draw.</summary>
    /// <returns>A column-major 4x4 identity.</returns>
    public static float[] Identity() =>
    [
        1, 0, 0, 0,
        0, 1, 0, 0,
        0, 0, 1, 0,
        0, 0, 0, 1,
    ];

    /// <summary>The model-view-projection matrix for a viewport of this shape.</summary>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>A column-major 4x4, as OpenGL wants it.</returns>
    /// <remarks>
    /// The single-precision copy of <see cref="Matrix"/>, which is what the window hands
    /// the GPU. Both come from one computation so a still and the window cannot frame the
    /// same model differently.
    /// </remarks>
    public float[] Project(double aspect) => [.. Matrix(aspect).Select(v => (float)v)];

    /// <summary>The same matrix in double precision, for drawing without a GPU.</summary>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>A column-major 4x4: element <c>[column * 4 + row]</c>.</returns>
    /// <remarks>
    /// The instrument's box is fitted to whichever screen axis binds - a long analyzer to the
    /// width, a tall trap to the height - with the other axis given the room the window's
    /// shape leaves over.
    /// </remarks>
    public double[] Matrix(double aspect)
    {
        var r = Rotation(Azimuth, Elevation);

        // At least a micrometre, so a flat scene - a single line seen end-on, a sheet seen
        // edge-on - still has a finite scale.
        var floor = 1e-3;
        var halfY = Margin * Math.Max(Math.Max(HalfHeightMm, HalfWidthMm / aspect), floor);
        var halfX = halfY * aspect;

        // Depth spans the box and more, so nothing clips at the near or far plane.
        var depth = 2.0 * Math.Max(Math.Max(HalfDepthMm, Math.Max(halfX, halfY)), floor);

        double sx = 1.0 / halfX, sy = 1.0 / halfY, sz = -1.0 / depth;

        var (cx, cy, cz) = CenterMm;

        // Column-major: element [column * 4 + row].
        return
        [
            sx * r[0], sy * r[3], sz * r[6], 0.0,
            sx * r[1], sy * r[4], sz * r[7], 0.0,
            sx * r[2], sy * r[5], sz * r[8], 0.0,
            -sx * ((r[0] * cx) + (r[1] * cy) + (r[2] * cz)),
            -sy * ((r[3] * cx) + (r[4] * cy) + (r[5] * cz)),
            -sz * ((r[6] * cx) + (r[7] * cy) + (r[8] * cz)),
            1.0,
        ];
    }

    /// <summary>Everything in a scene that says where the instrument reaches.</summary>
    internal static IEnumerable<(double X, double Y, double Z)> Points(ViewportOutcome scene)
    {
        foreach (var conductor in scene.Conductors)
        {
            for (var i = 0; i + 2 < conductor.VerticesMm.Count; i += 3)
            {
                yield return (conductor.VerticesMm[i], conductor.VerticesMm[i + 1], conductor.VerticesMm[i + 2]);
            }
        }

        foreach (var path in scene.Trajectories)
        {
            foreach (var point in path.PointsMm)
            {
                if (point.Count >= 3)
                {
                    yield return (point[0], point[1], point[2]);
                }
            }
        }

        foreach (var shell in scene.Density)
        {
            for (var i = 0; i + 2 < shell.VerticesMm.Count; i += 3)
            {
                yield return (shell.VerticesMm[i], shell.VerticesMm[i + 1], shell.VerticesMm[i + 2]);
            }
        }

        // The field too, because it is drawn: a camera that measured everything but the
        // equipotentials ran them off the top of a drift tube's picture, since the field is
        // sampled over the whole region the tube reaches and the packet occupies less of it.
        foreach (var level in scene.Equipotentials)
        {
            foreach (var polyline in level.PathsMm)
            {
                for (var i = 0; i + 2 < polyline.Count; i += 3)
                {
                    yield return (polyline[i], polyline[i + 1], polyline[i + 2]);
                }
            }
        }
    }

    /// <summary>
    /// Turn about the vertical by the azimuth, then tilt by the elevation, as a row-major 3x3.
    /// </summary>
    /// <remarks>
    /// Written out rather than multiplied at run time: three rows is shorter than a matrix
    /// library, and the sign of every term is visible.
    /// </remarks>
    private static double[] Rotation(double azimuth, double elevation)
    {
        var a = azimuth * Math.PI / 180.0;
        var e = elevation * Math.PI / 180.0;

        double ca = Math.Cos(a), sa = Math.Sin(a);
        double ce = Math.Cos(e), se = Math.Sin(e);

        return
        [
            ca, 0.0, -sa,
            se * sa, ce, se * ca,
            ce * sa, -se, ce * ca,
        ];
    }

    private static (double U, double V, double W) Apply(double[] r, double x, double y, double z) =>
        ((r[0] * x) + (r[1] * y) + (r[2] * z),
         (r[3] * x) + (r[4] * y) + (r[5] * z),
         (r[6] * x) + (r[7] * y) + (r[8] * z));

    private static Framing FromView(
        double[] r,
        (double U, double V, double W) center,
        double halfU,
        double halfV,
        double halfW,
        double azimuth,
        double elevation)
    {
        // Back into the model's frame through the transpose, since a rotation's inverse is
        // its transpose.
        var (u, v, w) = center;

        return new Framing(
            ((r[0] * u) + (r[3] * v) + (r[6] * w),
             (r[1] * u) + (r[4] * v) + (r[7] * w),
             (r[2] * u) + (r[5] * v) + (r[8] * w)),
            halfU,
            halfV,
            halfW,
            azimuth,
            elevation);
    }

    /// <summary>The eight corners of this box, in the model's frame.</summary>
    private IEnumerable<(double X, double Y, double Z)> Corners()
    {
        var r = Rotation(Azimuth, Elevation);
        var (u, v, w) = Apply(r, CenterMm.X, CenterMm.Y, CenterMm.Z);

        foreach (var su in (double[])[-1.0, 1.0])
        {
            foreach (var sv in (double[])[-1.0, 1.0])
            {
                foreach (var sw in (double[])[-1.0, 1.0])
                {
                    var (cu, cv, cw) = (u + (su * HalfWidthMm), v + (sv * HalfHeightMm), w + (sw * HalfDepthMm));

                    yield return (
                        (r[0] * cu) + (r[3] * cv) + (r[6] * cw),
                        (r[1] * cu) + (r[4] * cv) + (r[7] * cw),
                        (r[2] * cu) + (r[5] * cv) + (r[8] * cw));
                }
            }
        }
    }
}
