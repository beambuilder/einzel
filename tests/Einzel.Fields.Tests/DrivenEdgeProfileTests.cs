using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields.Solved;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// An edge profile keeps its potential in a driven geometry, where it used to be rasterised
/// at zero and vanish without a word.
/// </summary>
/// <remarks>
/// <para>
/// An edge profile carries its volts in its <em>profile</em> rather than in a scalar
/// potential, and every shipped one leaves that scalar null - the mirror pair's boards and
/// the Astral's are written that way. The channel decomposition builds a supply from the
/// scalar potential and the taps, so an edge profile joined no supply, appeared in no
/// pattern, and was weighted at zero; the rasteriser multiplies the profile by that weight,
/// so the board was simply not in the solve.
/// </para>
/// <para>
/// <b>It never showed because the two templates that use edge profiles declare no drive.</b>
/// An undriven solve takes the other arm, where the scale is 1.0 and the profile is used as
/// written, so both are correct today. The defect is reached by declaring a drive on a
/// geometry that also has a profiled board - which is what an ejection slot with an
/// extraction plate behind it needs.
/// </para>
/// <para>
/// The measurement that caught it was a flight time against a closed form: a 1 eV ion in a
/// box whose right edge was held at -500 V crossed 4 mm in 6.419 us with a drive declared,
/// against the 6.44 us of field-free flight at 621.2 m/s. The plate was exactly absent.
/// </para>
/// </remarks>
public sealed class DrivenEdgeProfileTests(ITestOutputHelper output)
{
    private const double Half = 6.0e-3;
    private const double PlateVolts = -500.0;

    /// <summary>A plate on the axis, earthed, which carries the drive when there is one.</summary>
    private static CompiledElectrode Post(IReadOnlyList<CompiledTap> taps) =>
        new()
        {
            Name = "post", Shape = ElectrodeShape.Rectangle,
            MinX = -1.0e-3, MaxX = 1.0e-3, MinY = -5.0e-3, MaxY = -3.0e-3,
            Potential = 0.0, Taps = taps,
        };

    /// <summary>The right edge held at a constant potential, written as a profile.</summary>
    private static CompiledElectrode Edge(double volts) =>
        new()
        {
            Name = "pullPlate", Shape = ElectrodeShape.EdgeProfile, Edge = GridEdge.Right,
            Profile = [(-Half, volts), (Half, volts)], Taps = [],
        };

    /// <summary>A driven field answers with an instant; a static one has none to answer with.</summary>
    private static double Sample(IElectrostaticField field, in Vec3 probe, double atSeconds) =>
        field is ITimeVaryingField driven ? driven.PotentialAt(probe, atSeconds) : field.PotentialAt(probe);

    private static CompiledSolvedField Box(double volts, bool driven) =>
        new()
        {
            MinX = -Half, MaxX = Half, MinY = -Half, MaxY = Half,
            CellSize = 0.25e-3, Tolerance = 1e-10,
            Electrodes = [Post(driven ? [new CompiledTap(0, 40.0, 0.0)] : []), Edge(volts)],
            Drives = driven
                ? [new CompiledDrive(1.0e6, DriveWaveform.Sinusoid, 0.5)]
                : [],
        };

    /// <summary>
    /// The same box, driven and undriven, sampled where the drive contributes exactly
    /// nothing. The two must agree, and they must not agree at zero.
    /// </summary>
    /// <remarks>
    /// A quarter cycle is the instant to compare at: the weight is CosPi of a half turn,
    /// which is <em>exactly</em> zero rather than 6e-17, so the driven field at that instant
    /// is its constant channel and nothing else. Any residual is the defect, not the drive.
    /// </remarks>
    [Fact]
    public void ADrivenEdgeProfileHoldsTheSamePotentialAsAnUndrivenOne()
    {
        var (undriven, _) = GeometryBuilder.Build(Box(PlateVolts, driven: false));
        var (driven, _) = GeometryBuilder.Build(Box(PlateVolts, driven: true));

        var quiet = 0.25e-6;   // a quarter of the 1 MHz cycle
        var probe = new Vec3(0.0, 2.0e-3, 0.0);

        var still = Sample(undriven, probe, 0.0);
        var swung = Sample(driven, probe, quiet);

        var channels = GeometryBuilder.SolveChannels(Box(PlateVolts, driven: true)).Count;

        output.WriteLine($"undriven {still:F4} V, driven at a quarter cycle {swung:F4} V");
        output.WriteLine($"{channels} channels in the driven solve");

        // Two channels: the constant supply carrying the profile, and the drive.
        Assert.Equal(2, channels);

        // The plate is worth a hundred volts or so at the probe. Asserting only that the two
        // agree would pass with both at zero, which is precisely the defect.
        Assert.True(still < -20.0, $"the -500 V edge is worth {still:F4} V at the probe with no drive");
        Assert.Equal(still, swung, 1e-6);
    }

    /// <summary>
    /// The control: the drive still works. At the top of the cycle the driven field differs
    /// from the undriven one, so the agreement above is the drive being zero rather than the
    /// drive being lost as well.
    /// </summary>
    [Fact]
    public void TheDriveIsStillThereWhenTheCycleIsNotAtItsZero()
    {
        var (undriven, _) = GeometryBuilder.Build(Box(PlateVolts, driven: false));
        var (driven, _) = GeometryBuilder.Build(Box(PlateVolts, driven: true));

        var probe = new Vec3(0.0, -2.0e-3, 0.0);   // under the post, where its 40 V tells
        var still = Sample(undriven, probe, 0.0);
        var top = Sample(driven, probe, 0.0);

        output.WriteLine($"undriven {still:F4} V, driven at the top of the cycle {top:F4} V");
        Assert.True(Math.Abs(top - still) > 5.0,
            $"the 40 V drive moves the potential by {Math.Abs(top - still):F4} V");
    }

    /// <summary>
    /// An undriven geometry reports, through <see cref="GeometryBuilder.SolveChannels"/>, the
    /// same field it runs in - profiled board included.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The driven fix above reached the decomposition and not the static arm beside it, which
    /// built its one pattern from each electrode's scalar <c>Potential</c> - zero for every
    /// shipped edge profile - and handed it to a rasterizer that multiplies the profile
    /// through. So <c>einzel solve</c> on the planar mirror pair solved a mirror with no
    /// boards, and reported it converged with a peak of 4800 V, the cap's, which looks right.
    /// <c>einzel export</c> escaped only because it built its mask a third way.
    /// </para>
    /// <para>
    /// Compared node by node against <see cref="GeometryBuilder.Build"/>, the path every run
    /// and study takes, because "the channels agree with the field" is the property: a check
    /// on the peak alone passes with the board missing, as the mirror pair shows.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnUndrivenEdgeProfileIsInTheChannelItReportsAgainst()
    {
        var box = Box(PlateVolts, driven: false);
        var (field, _) = GeometryBuilder.Build(box);
        var channel = Assert.Single(GeometryBuilder.SolveChannels(box));

        var probe = new Vec3(0.0, 2.0e-3, 0.0);
        var grid = channel.Potential.Grid;
        var i = (int)Math.Round((probe.X - grid.OriginX) / grid.SpacingX);
        var j = (int)Math.Round((probe.Y - grid.OriginY) / grid.SpacingY);

        var reported = channel.Potential[i, j];
        var flown = field.PotentialAt(new Vec3(grid.X(i), grid.Y(j), 0.0));

        output.WriteLine($"channel {reported:F6} V, field {flown:F6} V at ({grid.X(i) * 1e3:F2}, {grid.Y(j) * 1e3:F2}) mm");

        Assert.True(flown < -20.0, $"the -500 V edge is worth {flown:F4} V at the probe");
        Assert.Equal(flown, reported, 1e-9);
    }

    /// <summary>
    /// A profile that is everywhere zero is a grounded board, and gets no channel of its own:
    /// a constant supply whose field is nothing is a solve nobody needs.
    /// </summary>
    [Fact]
    public void AGroundedEdgeProfileConjuresNoChannel()
    {
        var live = GeometryBuilder.SolveChannels(Box(PlateVolts, driven: true)).Count;
        var earthed = GeometryBuilder.SolveChannels(Box(0.0, driven: true)).Count;

        output.WriteLine($"{live} channels at {PlateVolts} V, {earthed} at earth");
        Assert.Equal(2, live);
        Assert.Equal(1, earthed);
    }
}
