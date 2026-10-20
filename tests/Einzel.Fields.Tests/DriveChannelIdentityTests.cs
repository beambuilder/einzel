using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>
/// That the channel decomposition treats a name as an electrode's identity, and adds an
/// electrode's own contributions to a supply rather than keeping the last.
/// </summary>
/// <remarks>
/// <para>
/// Both defects were one assignment: a supply's coefficients are a map from electrode name to
/// volts, and each contribution was written with <c>=</c>. Two electrodes sharing a name - every
/// pair of unnamed ones, all called <c>electrode</c> - wrote one entry and the later set both;
/// two taps of one electrode on one generator wrote one entry and the later replaced the
/// earlier. The validator now names electrodes apart and refuses a repeat, so the first is
/// refused here as well, at the seam, rather than answered with a quieter field.
/// </para>
/// </remarks>
public sealed class DriveChannelIdentityTests
{
    /// <summary>Two excitations sharing a name are refused rather than merged into one entry.</summary>
    [Fact]
    public void TwoExcitationsSharingANameAreRefused()
    {
        Excitation[] excitations =
        [
            new("plate", 2.0, 0.0, 0.0),
            new("plate", -2.0, 0.0, 0.0),
            new("rf", 0.0, 1.0, 0.0),
        ];

        var refused = Assert.Throws<ArgumentException>(() => DriveChannels.Decompose(excitations, quadrature: true));

        Assert.Contains("both called 'plate'", refused.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The same plates named apart are two entries of one pattern - the control, since a
    /// decomposition that refused everything would pass the test above.
    /// </summary>
    [Fact]
    public void TheSamePlatesNamedApartAreOnePatternHoldingBoth()
    {
        var channels = DriveChannels.Decompose(
            [new("upper", 2.0, 0.0, 0.0), new("lower", -2.0, 0.0, 0.0), new("rf", 0.0, 1.0, 0.0)],
            quadrature: true);

        var dc = Assert.Single(channels, c => c.Pattern.ContainsKey("upper"));

        Assert.Equal(1.0, dc.Pattern["upper"]);
        Assert.Equal(-1.0, dc.Pattern["lower"]);
        Assert.Equal(2.0, dc.Direct);
    }

    /// <summary>
    /// Two taps on a switched generator at one phase reach one supply, and are one tap of
    /// their sum - exactly, since 100 + 50 is 150 in floating point.
    /// </summary>
    [Fact]
    public void TwoTapsOnASwitchedGeneratorAtOnePhaseAreTheirSum()
    {
        var both = Single(DriveChannels.Decompose(
            [new("rod", 0.0, [new DriveTap(0, 100.0, 0.3), new DriveTap(0, 50.0, 0.3)])],
            quadrature: false));

        var sum = Single(DriveChannels.Decompose(
            [new("rod", 0.0, [new DriveTap(0, 150.0, 0.3)])],
            quadrature: false));

        Assert.Equal(sum.Pattern, both.Pattern);
        Assert.Equal(sum.Harmonics, both.Harmonics);
    }

    /// <summary>
    /// On a sinusoid each tap resolves into an in-phase and a quadrature supply, so two taps at
    /// two phases reach the same pair and must add in both.
    /// </summary>
    [Fact]
    public void TwoTapsOnASinusoidAtTwoPhasesAddInBothComponents()
    {
        var both = Single(DriveChannels.Decompose(
            [new("rod", 0.0, [new DriveTap(0, 100.0, 0.1), new DriveTap(0, 50.0, 0.2)])],
            quadrature: true));

        var inPhase = (100.0 * double.CosPi(0.2)) + (50.0 * double.CosPi(0.4));
        var quadrature = (100.0 * double.SinPi(0.2)) + (50.0 * double.SinPi(0.4));

        Assert.Equal(2, both.Harmonics.Count);
        Assert.Equal(inPhase, both.Harmonics.Single(h => h.Phase == 0.0).Amplitude, 12);
        Assert.Equal(quadrature, both.Harmonics.Single(h => h.Phase == 0.25).Amplitude, 12);
    }

    private static DriveChannel Single(List<DriveChannel> channels) => Assert.Single(channels);
}
