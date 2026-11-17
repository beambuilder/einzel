using Einzel.Commands;
using Einzel.Core.Results;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The window's camera: turning between named views, and keeping a watched packet in frame.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect these guard against lived in a control that could not be tested.</b> The
/// window unioned each new view's frame with the previous view's, which are boxes in
/// different view coordinates, so the frame grew on every click of a named view and never
/// shrank. Taken out into <see cref="ViewportCamera"/> it is checked here without a GL
/// context.
/// </para>
/// </remarks>
public sealed class ViewportCameraTests(ITestOutputHelper output)
{
    private static readonly (double Azimuth, double Elevation) Iso = ViewportPicture.Views["iso"];
    private static readonly (double Azimuth, double Elevation) Side = ViewportPicture.Views["side"];
    private static readonly (double Azimuth, double Elevation) Front = ViewportPicture.Views["front"];

    /// <summary>
    /// Turning to another view and back gives the frame the camera started with, however many
    /// times it is done, and each view is framed as tightly as if it had been opened there.
    /// </summary>
    /// <remarks>
    /// The side view of a cube two millimeters across is one millimeter half-height. Carrying
    /// the iso box's corners into it made that about two, and each further round trip larger
    /// again.
    /// </remarks>
    [Fact]
    public void TurningAwayAndBackGivesTheFrameYouStartedWith()
    {
        var scene = Scene(Box(-1.0, 1.0), density: null);
        var camera = new ViewportCamera(scene, Iso);
        var opening = camera.Framing;

        for (var trip = 0; trip < 5; trip++)
        {
            var side = camera.Turn(Side);

            output.WriteLine($"trip {trip}: side half height {side.HalfHeightMm:F6} mm, "
                + $"half width {side.HalfWidthMm:F6} mm");

            Assert.Equal(1.0, side.HalfHeightMm, 9);
            Assert.Equal(1.0, side.HalfWidthMm, 9);

            var iso = camera.Turn(Iso);

            Assert.Equal(opening.HalfWidthMm, iso.HalfWidthMm, 9);
            Assert.Equal(opening.HalfHeightMm, iso.HalfHeightMm, 9);
            Assert.Equal(opening.HalfDepthMm, iso.HalfDepthMm, 9);
        }
    }

    /// <summary>
    /// A packet that drifted out of the box the scene opened in stays in frame when the
    /// camera turns - the reason the window keeps anything from before a turn at all.
    /// </summary>
    [Fact]
    public void APacketThatDriftedStaysInFrameWhenTheCameraTurns()
    {
        var scene = Scene(Box(-1.0, 1.0), density: null);
        var camera = new ViewportCamera(scene, Iso);

        // A frame of the run with the packet thirty millimeters downstream of everything the
        // scene held.
        camera.Take(Scene(Box(-1.0, 1.0), Shell(30.0, 40.0)));

        foreach (var view in new[] { Side, Front, Iso, Side })
        {
            var matrix = camera.Turn(view).Matrix(1.6);

            foreach (var (x, y, z) in new[] { (30.0, -1.0, -1.0), (40.0, 1.0, 1.0), (-1.0, -1.0, -1.0) })
            {
                var cx = (matrix[0] * x) + (matrix[4] * y) + (matrix[8] * z) + matrix[12];
                var cy = (matrix[1] * x) + (matrix[5] * y) + (matrix[9] * z) + matrix[13];

                Assert.InRange(cx, -1.0, 1.0);
                Assert.InRange(cy, -1.0, 1.0);
            }
        }

        // And turning after a watch is as repeatable as turning before one.
        var first = camera.Turn(Side);
        camera.Turn(Iso);
        var again = camera.Turn(Side);

        Assert.Equal(first.HalfWidthMm, again.HalfWidthMm, 9);
        Assert.Equal(first.HalfHeightMm, again.HalfHeightMm, 9);
    }

    /// <summary>A camera nobody has touched draws exactly the fitted frame.</summary>
    /// <remarks>
    /// To the bit, because a still of the same view is drawn from <see cref="Framing.Matrix"/>
    /// and the window and the still must not frame one model differently until somebody moves
    /// the mouse.
    /// </remarks>
    [Fact]
    public void AnUntouchedCameraDrawsExactlyTheFittedFrame()
    {
        var camera = new ViewportCamera(Scene(Box(-1.0, 1.0), density: null), Iso);

        Assert.Equal(camera.Framing.Matrix(1.6), camera.Matrix(1.6));
    }

    /// <summary>Zooming keeps the point under the cursor where it is, and spreads the rest.</summary>
    /// <remarks>
    /// The point zoomed toward is the one pointed at; a zoom about the middle of the screen
    /// would slide it away under the cursor, which is what makes zooming feel like a fight.
    /// </remarks>
    [Fact]
    public void ZoomingKeepsThePointUnderTheCursorWhereItIs()
    {
        var camera = new ViewportCamera(Scene(Box(-1.0, 1.0), density: null), Iso);

        var (px, py) = Clip(camera.Matrix(1.6), (0.5, 0.3, -0.2));
        var (qx, qy) = Clip(camera.Matrix(1.6), (-0.4, -0.6, 0.7));

        camera.ZoomAbout(2.5, px, py);

        var (px2, py2) = Clip(camera.Matrix(1.6), (0.5, 0.3, -0.2));
        var (qx2, qy2) = Clip(camera.Matrix(1.6), (-0.4, -0.6, 0.7));

        output.WriteLine($"pointed at ({px:F6}, {py:F6}) -> ({px2:F6}, {py2:F6})");

        Assert.Equal(px, px2, 12);
        Assert.Equal(py, py2, 12);
        Assert.Equal(2.5 * (qx - px), qx2 - px2, 12);
        Assert.Equal(2.5 * (qy - py), qy2 - py2, 12);
        Assert.Equal(2.5, camera.Zoom, 12);
    }

    /// <summary>Panning moves every point across the screen by the same amount.</summary>
    [Fact]
    public void PanningMovesEveryPointByTheSameAmount()
    {
        var camera = new ViewportCamera(Scene(Box(-1.0, 1.0), density: null), Side);
        var points = new[] { (0.5, 0.3, -0.2), (-1.0, 1.0, 1.0), (0.0, 0.0, 0.0) };

        var before = points.Select(p => Clip(camera.Matrix(1.0), p)).ToList();

        camera.ZoomAbout(3.0, 0.2, -0.1);
        var zoomed = points.Select(p => Clip(camera.Matrix(1.0), p)).ToList();

        camera.PanBy(0.25, -0.4);
        var panned = points.Select(p => Clip(camera.Matrix(1.0), p)).ToList();

        for (var i = 0; i < points.Length; i++)
        {
            Assert.Equal(zoomed[i].X + 0.25, panned[i].X, 12);
            Assert.Equal(zoomed[i].Y - 0.4, panned[i].Y, 12);
        }

        Assert.NotEqual(before[0], panned[0]);
    }

    /// <summary>
    /// An orbit turns the instrument in place at the size it was, and loses none of it to the
    /// near or far plane.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a rod a hundred millimetres long and two across, because that is the shape a refit
    /// would mistreat: seen side-on it fills the width, and refitted end-on it is two
    /// millimetres across and would fill the screen. Turned by the mouse it must stay the
    /// size it was, which is what "turning it" means to the person holding it.
    /// </para>
    /// <para>
    /// The control is <see cref="ViewportCamera.Turn"/> to the same angle, which does refit,
    /// so the test would pass vacuously if the two happened to agree.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnOrbitTurnsInPlaceAtTheSizeItWas()
    {
        var scene = Scene(Rod(), density: null);
        var camera = new ViewportCamera(scene, Side);
        var side = camera.Framing;

        var refitted = new ViewportCamera(scene, Side).Turn(Front);

        Assert.True(
            refitted.HalfWidthMm < 0.1 * side.HalfWidthMm,
            $"the control: a refit end-on is {refitted.HalfWidthMm:F3} mm across against {side.HalfWidthMm:F3}");

        for (var step = 1; step <= 6; step++)
        {
            var turned = camera.Orbit(15.0, 7.0);

            Assert.Equal(side.HalfWidthMm, turned.HalfWidthMm, 12);
            Assert.Equal(side.HalfHeightMm, turned.HalfHeightMm, 12);
            Assert.Equal(side.CenterMm, turned.CenterMm);

            var matrix = camera.Matrix(1.6);

            foreach (var corner in RodCorners())
            {
                var z = (matrix[2] * corner.X) + (matrix[6] * corner.Y) + (matrix[10] * corner.Z) + matrix[14];

                Assert.InRange(z, -1.0, 1.0);
            }
        }

        output.WriteLine($"after the orbit: azimuth {camera.View.Azimuth}, elevation {camera.View.Elevation}");

        Assert.Equal((90.0, 42.0), camera.View);
    }

    /// <summary>Tilting stops at straight up and straight down.</summary>
    [Fact]
    public void TiltingStopsAtTheVertical()
    {
        var camera = new ViewportCamera(Scene(Box(-1.0, 1.0), density: null), Side);

        camera.Orbit(0.0, 400.0);
        Assert.Equal(90.0, camera.View.Elevation);

        camera.Orbit(0.0, -1000.0);
        Assert.Equal(-90.0, camera.View.Elevation);

        camera.Orbit(370.0, 0.0);
        Assert.Equal(10.0, camera.View.Azimuth, 12);
    }

    /// <summary>A fit, or a named view, leaves nothing of the mouse behind.</summary>
    /// <remarks>
    /// Compared with a camera opened at that view and never touched, to the bit: a named view
    /// is exact, and one that kept a little of an earlier zoom would be a different picture
    /// every time it was pressed.
    /// </remarks>
    [Fact]
    public void AFitLeavesNothingOfTheMouseBehind()
    {
        var scene = Scene(Rod(), density: null);
        var camera = new ViewportCamera(scene, Iso);

        camera.Orbit(33.0, -12.0);
        camera.ZoomAbout(4.0, 0.3, 0.2);
        camera.PanBy(-0.5, 0.1);

        camera.Fit(Front);

        Assert.Equal(new ViewportCamera(scene, Front).Matrix(1.6), camera.Matrix(1.6));
        Assert.Equal(1.0, camera.Zoom);

        camera.ZoomAbout(2.0, 0.0, 0.0);
        camera.Fit();

        Assert.Equal(new ViewportCamera(scene, Front).Matrix(1.6), camera.Matrix(1.6));
    }

    /// <summary>An edit's new camera keeps where the person was looking.</summary>
    [Fact]
    public void AnEditKeepsTheZoom()
    {
        var scene = Scene(Box(-1.0, 1.0), density: null);
        var before = new ViewportCamera(scene, Side);

        before.ZoomAbout(5.0, 0.4, -0.3);

        var after = new ViewportCamera(scene, Side);
        after.KeepZoomFrom(before);

        Assert.Equal(before.Matrix(1.2), after.Matrix(1.2));
        Assert.Equal(5.0, after.Zoom, 12);
    }

    private static (double X, double Y) Clip(double[] m, (double X, double Y, double Z) p) =>
        ((m[0] * p.X) + (m[4] * p.Y) + (m[8] * p.Z) + m[12],
         (m[1] * p.X) + (m[5] * p.Y) + (m[9] * p.Z) + m[13]);

    private static (double X, double Y, double Z)[] RodCorners() =>
    [
        (-50.0, -1.0, -1.0), (50.0, -1.0, -1.0), (50.0, 1.0, -1.0), (-50.0, 1.0, -1.0),
        (-50.0, -1.0, 1.0), (50.0, -1.0, 1.0), (50.0, 1.0, 1.0), (-50.0, 1.0, 1.0),
    ];

    private static ConductorSurface Rod() =>
        new(
            Name: "rod",
            PotentialVolts: 0.0,
            DriveAmplitudeVolts: 0.0,
            VerticesMm: [.. RodCorners().SelectMany(c => new[] { c.X, c.Y, c.Z })],
            Normals: [.. Enumerable.Repeat(0.0, 24)],
            Triangles: [0, 1, 2, 0, 2, 3]);

    private static DensityShell Shell(double low, double high) =>
        new(
            DensityPerCubicMetre: 1e10,
            DecadesBelowPeak: 0,
            VerticesMm: [low, -1.0, -1.0, high, -1.0, -1.0, high, 1.0, 1.0, low, 1.0, 1.0],
            Normals: [.. Enumerable.Repeat(0.0, 12)],
            Triangles: [0, 1, 2, 0, 2, 3]);

    private static ConductorSurface Box(double low, double high) =>
        new(
            Name: "box",
            PotentialVolts: 0.0,
            DriveAmplitudeVolts: 0.0,
            VerticesMm:
            [
                low, low, low, high, low, low, high, high, low, low, high, low,
                low, low, high, high, low, high, high, high, high, low, high, high,
            ],
            Normals: [.. Enumerable.Repeat(0.0, 24)],
            Triangles: [0, 1, 2, 0, 2, 3]);

    private static ViewportOutcome Scene(ConductorSurface conductor, DensityShell? density) =>
        new(
            ModelPath: "probe.json",
            Trajectories: [],
            ProducesTrajectories: density is null,
            LowestEnergyEv: null,
            HighestEnergyEv: null,
            Conductors: [conductor],
            Equipotentials: [],
            LowestPotentialVolts: null,
            HighestPotentialVolts: null,
            Density: density is null ? [] : [density],
            PeakDensityPerCubicMetre: null,
            DensityAtUs: null,
            Ends: null,
            Warnings: Array.Empty<ValidityWarning>());
}
