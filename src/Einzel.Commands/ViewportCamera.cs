namespace Einzel.Commands;

/// <summary>
/// The viewport's camera over one scene: which named view it looks from, and a frame that
/// holds everything drawn, including what a watched run has drifted into.
/// </summary>
/// <remarks>
/// <para>
/// <b>Here rather than in the window's GL control, because the control cannot be tested.</b>
/// The first version of this bookkeeping lived in <c>SceneView</c> and unioned the frame
/// fitted for a new view with the frame of the previous view. The two are boxes in different
/// view coordinates, so the union carried the old box's rotated corners across: the frame
/// never shrank and grew on every click of a named view. A cube filled about half the frame
/// after one iso-to-side switch and less after each round trip. Nothing caught it, because
/// the only code that could was inside a control that needs a GL context.
/// </para>
/// <para>
/// <b>What a watch adds is kept as a box in the model's own coordinates</b>, which does not
/// depend on the view. Turning the camera fits the scene afresh at the new angles and takes
/// that box in at those same angles, so switching views and back gives the frame you started
/// with. Within one view a new frame of a watch is taken in exactly, since two boxes at the
/// same angles have a union that is a box.
/// </para>
/// <para>
/// <b>A person can also orbit, pan and zoom, and none of it is a named view.</b> Zoom and pan
/// are kept apart from the fitted frame, as a scale and a shift applied in clip space after
/// it, so a named view or <see cref="Fit()"/> is still exact and still repeatable whatever was
/// done with the mouse in between. An orbit keeps the scale and the centre it started from
/// and turns about them: refitting at every angle, as a named view does, would make the
/// picture swell and shrink under the cursor - a long thin analyzer turned end-on fills the
/// screen - which is the frame changing because the camera moved, not because the person
/// asked it to.
/// </para>
/// <para>
/// Locked, because a named view is chosen on the UI thread and a watched frame arrives on the
/// render thread.
/// </para>
/// </remarks>
public sealed class ViewportCamera
{
    private readonly Lock _gate = new();
    private readonly ViewportOutcome _scene;

    private (double Azimuth, double Elevation) _view;
    private Framing _framing;
    private ((double X, double Y, double Z) Low, (double X, double Y, double Z) High)? _watched;
    private double _zoom = 1.0;
    private (double X, double Y) _pan;

    /// <summary>Frames a scene from a view.</summary>
    /// <param name="scene">What the command layer measured.</param>
    /// <param name="view">Azimuth and elevation, in degrees.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is null.</exception>
    public ViewportCamera(ViewportOutcome scene, (double Azimuth, double Elevation) view)
    {
        ArgumentNullException.ThrowIfNull(scene);

        _scene = scene;
        _view = view;
        _framing = Framing.Measure(scene, view.Azimuth, view.Elevation);
    }

    /// <summary>The view the camera looks from, in degrees of azimuth and elevation.</summary>
    public (double Azimuth, double Elevation) View
    {
        get
        {
            lock (_gate)
            {
                return _view;
            }
        }
    }

    /// <summary>How far the person has zoomed in from the fitted frame; one is not at all.</summary>
    public double Zoom
    {
        get
        {
            lock (_gate)
            {
                return _zoom;
            }
        }
    }

    /// <summary>The frame, at the current view, before any zoom or pan.</summary>
    public Framing Framing
    {
        get
        {
            lock (_gate)
            {
                return _framing;
            }
        }
    }

    /// <summary>Turns the camera to another view and fits the frame to what it sees.</summary>
    /// <param name="view">Azimuth and elevation, in degrees.</param>
    /// <returns>The new frame.</returns>
    public Framing Turn((double Azimuth, double Elevation) view)
    {
        lock (_gate)
        {
            _view = view;

            var fitted = Framing.Measure(_scene, view.Azimuth, view.Elevation);

            _framing = _watched is { } box
                ? fitted.Union(Framing.Around(Corners(box), view.Azimuth, view.Elevation))
                : fitted;

            return _framing;
        }
    }

    /// <summary>The matrix the scene is drawn with: the frame, then the person's zoom and pan.</summary>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>A column-major 4x4: element <c>[column * 4 + row]</c>.</returns>
    /// <remarks>
    /// Exactly <see cref="Framing.Matrix"/> until somebody zooms or pans, so a camera nobody
    /// has touched draws what a still of the same view draws.
    /// </remarks>
    public double[] Matrix(double aspect)
    {
        lock (_gate)
        {
            var matrix = _framing.Matrix(aspect);

            if (_zoom == 1.0 && _pan == (0.0, 0.0))
            {
                return matrix;
            }

            // Clip x' = zoom * x + pan * w, written into the rows of the matrix so the GPU and
            // the ruler both get it from one place. w is the bottom row, one for an
            // orthographic projection, but written generally rather than assumed.
            for (var column = 0; column < 4; column++)
            {
                var w = matrix[(column * 4) + 3];

                matrix[(column * 4) + 0] = (_zoom * matrix[(column * 4) + 0]) + (_pan.X * w);
                matrix[(column * 4) + 1] = (_zoom * matrix[(column * 4) + 1]) + (_pan.Y * w);
            }

            return matrix;
        }
    }

    /// <summary>The same matrix in single precision, as the GPU wants it.</summary>
    /// <param name="aspect">Viewport width over height.</param>
    /// <returns>A column-major 4x4.</returns>
    public float[] Project(double aspect) => [.. Matrix(aspect).Select(v => (float)v)];

    /// <summary>Zooms by a factor, keeping the point under the cursor where it is.</summary>
    /// <param name="factor">Above one zooms in.</param>
    /// <param name="clipX">The cursor, in clip coordinates from minus one to one.</param>
    /// <param name="clipY">The cursor, in clip coordinates, upward.</param>
    /// <remarks>
    /// About the cursor rather than the middle of the screen, because what a person zooms
    /// toward is what they are pointing at. Bounded at a thousandth and ten thousand times the
    /// fit, so a wheel spun far enough cannot drive the scale to zero or to infinity.
    /// </remarks>
    public void ZoomAbout(double factor, double clipX, double clipY)
    {
        lock (_gate)
        {
            var zoom = Math.Clamp(_zoom * factor, 1e-3, 1e4);
            var ratio = zoom / _zoom;

            _pan = (clipX - (ratio * (clipX - _pan.X)), clipY - (ratio * (clipY - _pan.Y)));
            _zoom = zoom;
        }
    }

    /// <summary>Moves the picture across the screen.</summary>
    /// <param name="clipX">How far, in clip coordinates.</param>
    /// <param name="clipY">How far, in clip coordinates, upward.</param>
    public void PanBy(double clipX, double clipY)
    {
        lock (_gate)
        {
            _pan = (_pan.X + clipX, _pan.Y + clipY);
        }
    }

    /// <summary>Turns the camera by an amount, keeping the scale and the centre.</summary>
    /// <param name="azimuth">How far to turn about the vertical, in degrees.</param>
    /// <param name="elevation">How far to tilt, in degrees; held between straight up and down.</param>
    /// <returns>The new frame.</returns>
    /// <remarks>
    /// <para>
    /// <b>Not a refit.</b> The frame keeps its centre and its half-extents, so the instrument
    /// turns in place at the size it was; <see cref="Turn"/> and <see cref="Fit()"/> refit.
    /// </para>
    /// <para>
    /// <b>The depth needs no change.</b> <see cref="Framing.Matrix"/> gives the frame a depth of
    /// twice its largest half-extent, and a box with those half-extents fits inside a sphere
    /// of at most the square root of three times the largest, so nothing that was in the
    /// frame can be carried past the near or far plane by turning about its centre. A first
    /// version measured the scene's reach and widened the depth to it; breaking that changed
    /// no test, which is how it was found to be redundant.
    /// </para>
    /// </remarks>
    public Framing Orbit(double azimuth, double elevation)
    {
        lock (_gate)
        {
            _view = (
                Wrap(_view.Azimuth + azimuth),
                Math.Clamp(_view.Elevation + elevation, -90.0, 90.0));

            _framing = _framing with { Azimuth = _view.Azimuth, Elevation = _view.Elevation };

            return _framing;
        }
    }

    /// <summary>Refits the frame at the current view and undoes any zoom and pan.</summary>
    /// <returns>The new frame.</returns>
    public Framing Fit() => Fit(View);

    /// <summary>Turns to a view, refits the frame there, and undoes any zoom and pan.</summary>
    /// <param name="view">Azimuth and elevation, in degrees.</param>
    /// <returns>The new frame.</returns>
    /// <remarks>
    /// What a named view button does: a named view is exact, so arriving at one leaves nothing
    /// of the mouse behind.
    /// </remarks>
    public Framing Fit((double Azimuth, double Elevation) view)
    {
        lock (_gate)
        {
            _zoom = 1.0;
            _pan = (0.0, 0.0);
        }

        return Turn(view);
    }

    /// <summary>Carries a person's zoom and pan over from the camera this one replaces.</summary>
    /// <param name="previous">The camera of the scene before an edit.</param>
    /// <exception cref="ArgumentNullException"><paramref name="previous"/> is null.</exception>
    /// <remarks>
    /// An edit refits the frame to the new scene, and a person zoomed in on a slit to watch
    /// what an edit does to it wants to stay zoomed in on the slit. Where they were looking is
    /// kept as a scale and a shift of the fitted frame, so a small edit leaves the same
    /// region on screen and a large one at least the same zoom.
    /// </remarks>
    public void KeepZoomFrom(ViewportCamera previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        double zoom;
        (double X, double Y) pan;

        lock (previous._gate)
        {
            zoom = previous._zoom;
            pan = previous._pan;
        }

        lock (_gate)
        {
            _zoom = zoom;
            _pan = pan;
        }
    }

    /// <summary>Takes in a frame of a run that is still going.</summary>
    /// <param name="frame">The run as it stands.</param>
    /// <returns>The frame, grown to hold it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="frame"/> is null.</exception>
    /// <remarks>
    /// The frame only grows. A packet that drifts out of the box it was framed in leaves an
    /// empty viewport, and re-measuring per frame would make the camera breathe with the
    /// packet instead of letting the packet move.
    /// </remarks>
    public Framing Take(ViewportOutcome frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        lock (_gate)
        {
            foreach (var (x, y, z) in Framing.Points(frame))
            {
                _watched = _watched is { } box
                    ? ((Math.Min(box.Low.X, x), Math.Min(box.Low.Y, y), Math.Min(box.Low.Z, z)),
                       (Math.Max(box.High.X, x), Math.Max(box.High.Y, y), Math.Max(box.High.Z, z)))
                    : ((x, y, z), (x, y, z));
            }

            _framing = _framing.Union(Framing.Measure(frame, _view.Azimuth, _view.Elevation));

            return _framing;
        }
    }

    /// <summary>An angle in degrees, brought into the half-open range from minus 180 to 180.</summary>
    private static double Wrap(double degrees)
    {
        var wrapped = Math.IEEERemainder(degrees, 360.0);

        return wrapped == -180.0 ? 180.0 : wrapped;
    }

    private static IEnumerable<(double X, double Y, double Z)> Corners(
        ((double X, double Y, double Z) Low, (double X, double Y, double Z) High) box)
    {
        foreach (var x in (double[])[box.Low.X, box.High.X])
        {
            foreach (var y in (double[])[box.Low.Y, box.High.Y])
            {
                foreach (var z in (double[])[box.Low.Z, box.High.Z])
                {
                    yield return (x, y, z);
                }
            }
        }
    }
}
