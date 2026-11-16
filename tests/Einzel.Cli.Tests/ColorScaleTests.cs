using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// What the viewport's legend says, checked without a viewport.
/// </summary>
/// <remarks>
/// <para>
/// <b>A color standing for a number is the one place a reader cannot recompute it.</b>
/// GRD-1's whole argument is that a quantity never travels without its units and its
/// envelope; a trajectory bundle colored by energy and a field colored by potential are
/// quantities drawn as colors, and without ends beside them they are bare numbers. The
/// cross-platform window shipped without them, which is what these were written for.
/// </para>
/// <para>
/// <b>In the command layer because the ends are not the observed range.</b> The potential
/// ramp is symmetric about earth, so a legend reading the lowest and highest potentials
/// would describe a ramp the picture is not painted with - and the window that worked that
/// out for itself kept the rule in a comment in its code-behind, which is how two surfaces
/// come to say different things about one model.
/// </para>
/// </remarks>
public sealed class ColorScaleTests(ITestOutputHelper output)
{
    /// <summary>The energy scale is the bundle's range, not any one path's.</summary>
    /// <remarks>
    /// <b>Anchored once across everything drawn</b>, which is the rule the paths layer is
    /// colored by: a scale taken per path gives every ion the same colors whatever its
    /// energy, so two ions a kilovolt apart look identical and the picture says they were
    /// the same. The assertion is on the ends rather than on their being unequal, because a
    /// legend built from the first path alone would also be unequal.
    /// </remarks>
    [Fact]
    public void TheEnergyScaleSpansTheWholeBundle()
    {
        var outcome = Outcome(
            trajectories: [Path(900.0, 1000.0), Path(1800.0, 2000.0)],
            lowEv: 900.0,
            highEv: 2000.0);

        var energy = Assert.Single(ViewportPicture.Scales(outcome));

        output.WriteLine($"{energy.Quantity}: {energy.Low} to {energy.High} {energy.Unit}");

        Assert.Equal("energy", energy.Quantity);
        Assert.Equal("eV", energy.Unit);
        Assert.Equal(900.0, energy.Low);
        Assert.Equal(2000.0, energy.High);
        Assert.False(energy.Diverging, "energy is sequential, not diverging");
    }

    /// <summary>The potential scale is symmetric about earth, not the observed range.</summary>
    /// <remarks>
    /// <para>
    /// <b>A lens holding 0 and 500 V is drawn on a ramp running -500 to +500</b>, because
    /// stretching the ramp across what was observed puts the neutral color at 250 V and
    /// paints an earthed tube the same as a genuinely negative one. Earth is the value a
    /// reader looks for first.
    /// </para>
    /// <para>
    /// So the discriminating assertion is that the low end is NEGATIVE on a geometry whose
    /// every electrode is at or above earth. A legend reporting the observed range passes
    /// nothing here; one reporting the span as a width passes "high is 500" and fails this.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePotentialScaleIsSymmetricAboutEarth()
    {
        // The contours stop well short of the metal, which is ordinary: levels are chosen
        // inside the domain. Well short rather than a volt short, so the two are genuinely
        // different ramps rather than one ramp measured twice.
        var outcome = Outcome(
            conductors: [Rod("entrance", 0.0, 0.0), Rod("centre", 500.0, 0.0), Rod("exit", 0.0, 0.0)],
            equipotentials: [Level(0.0), Level(200.0), Level(420.0)],
            lowVolts: 0.0,
            highVolts: 420.0);

        foreach (var scale in ViewportPicture.Scales(outcome))
        {
            output.WriteLine($"{scale.Quantity}: {scale.Low} to {scale.High} {scale.Unit}");

            Assert.True(scale.Diverging, $"{scale.Quantity} should be on the diverging ramp");
            Assert.True(scale.Low < 0.0, $"{scale.Quantity} runs {scale.Low} to {scale.High}");
            Assert.Equal(-scale.Low, scale.High);
            Assert.Equal("V", scale.Unit);
        }

        // The field's contours stop short of the metal, so the two layers anchor on
        // different spans and each is stated for what it is.
        var byName = ViewportPicture.Scales(outcome).ToDictionary(s => s.Quantity, StringComparer.Ordinal);

        Assert.Equal(420.0, byName["field"].High);
        Assert.Equal(500.0, byName["electrodes"].High);
    }

    /// <summary>
    /// Where the field and the metal reach the same potential, one row says so.
    /// </summary>
    /// <remarks>
    /// Two rows reading all but the same numbers under one picture invite the reader to go
    /// looking for the difference between them. The tolerance is relative and perceptual
    /// rather than a rounding, because the two routes do not agree to the last bit even when
    /// they describe one ramp: on the shipped einzel lens the metal reaches 500 V and the
    /// field 502, the interpolant overshooting a conductor's step as it does by
    /// construction, and four parts in a thousand is not a difference an eleven-pixel swatch
    /// can show. The wider is reported, so nothing drawn falls outside the stated scale.
    /// </remarks>
    [Fact]
    public void OneRowWhereTheFieldAndTheMetalAgree()
    {
        // The lens as shipped: the interpolant overshoots the conductor's step, so the field
        // reaches a little PAST the metal. The wider is what is stated, since a scale has to
        // contain everything drawn on it.
        var outcome = Outcome(
            conductors: [Rod("plate", -400.0, 0.0), Rod("earth", 0.0, 0.0)],
            equipotentials: [Level(-401.6), Level(-200.0), Level(0.0)],
            lowVolts: -401.6,
            highVolts: 0.0);

        var scale = Assert.Single(ViewportPicture.Scales(outcome));

        output.WriteLine($"{scale.Quantity}: {scale.Low} to {scale.High} {scale.Unit}");

        Assert.Equal("potential", scale.Quantity);
        Assert.Equal(-401.6, scale.Low);
        Assert.Equal(401.6, scale.High);
    }

    /// <summary>A driven electrode is on the scale by the peak it reaches.</summary>
    /// <remarks>
    /// <b>The seventh place this has had to be said.</b> A quadrupole's rods hold zero volts
    /// of DC and all of their potential as drive, so a scale built from the DC would report
    /// a mass filter's metal as an earthed box - and the picture beside it would be four
    /// rods in four colors against a legend saying they are all at zero.
    /// </remarks>
    [Fact]
    public void ADrivenElectrodeSetsTheScaleByItsPeak()
    {
        var outcome = Outcome(
            conductors:
            [
                Rod("rodXPlus", dc: 0.0, drive: +500.0),
                Rod("rodYPlus", dc: 0.0, drive: -500.0),
            ]);

        var scale = Assert.Single(ViewportPicture.Scales(outcome));

        output.WriteLine($"{scale.Quantity}: {scale.Low} to {scale.High} {scale.Unit}");

        Assert.Equal("electrodes", scale.Quantity);
        Assert.Equal(500.0, scale.High);
    }

    /// <summary>A scale is shown only where it means something.</summary>
    /// <remarks>
    /// <para>
    /// <b>Three cases, and each is a different way of having nothing to say.</b> A diffusive
    /// model computes a density and has no trajectories, so an energy row under it would
    /// state a range over an empty set - the same argument RND-8 makes about drawing the
    /// paths themselves. A geometry whose every electrode sits at earth has no potential
    /// range. And a bundle with neither has no legend at all.
    /// </para>
    /// <para>
    /// The earthed case is the one a plausible implementation gets wrong: the span the
    /// shading divides by falls back to one volt so the fraction cannot divide by zero, and
    /// a legend reading that would invent a range of -1 to 1 V for a model that has none.
    /// </para>
    /// </remarks>
    [Fact]
    public void NothingIsStatedForAQuantityThatIsNotDrawn()
    {
        var diffusive = Outcome(
            conductors: [Rod("ring", 300.0, 0.0)],
            equipotentials: [Level(300.0)],
            lowVolts: 0.0,
            highVolts: 300.0);

        Assert.DoesNotContain(
            ViewportPicture.Scales(diffusive), s => s.Quantity == "energy");

        var earthed = Outcome(conductors: [Rod("housing", 0.0, 0.0), Rod("wall", 0.0, 0.0)]);

        output.WriteLine($"earthed geometry: {ViewportPicture.Scales(earthed).Count} scales");

        Assert.Empty(ViewportPicture.Scales(earthed));
        Assert.Empty(ViewportPicture.Scales(Outcome()));
    }

    /// <summary>Metal that is not drawn does not widen the scale stated for what is.</summary>
    /// <remarks>
    /// The legend must describe exactly what is painted. A conductor with no triangles is
    /// skipped by the composition, so a 900 V one beside a drawn 500 V one leaves the scale
    /// at 500 - and the paint's own span agrees, which is asserted through the colors.
    /// </remarks>
    [Fact]
    public void UndrawnMetalDoesNotWidenTheScale()
    {
        var outcome = Outcome(conductors: [Rod("drawn", 500.0, 0.0), Undrawn("hidden", 900.0, 0.0)]);

        var scale = Assert.Single(ViewportPicture.Scales(outcome));

        Assert.Equal("electrodes", scale.Quantity);
        Assert.Equal(500.0, scale.High);

        var mesh = Assert.Single(ViewportPicture.Compose(outcome).Single(l => l.Name == "conductors").Meshes);
        var (r, g, b) = ColorRamp.Diverging(1.0);

        Assert.Equal((r, g, b), (mesh.R, mesh.G, mesh.B));

        Assert.Empty(ViewportPicture.Scales(Outcome(conductors: [Undrawn("hidden", 900.0, 0.0)])));
    }

    /// <summary>A monoenergetic bundle is one value, said as one value.</summary>
    [Fact]
    public void AMonoenergeticBundleIsASingleValue()
    {
        var outcome = Outcome(trajectories: [Path(4000.0, 4000.0)], lowEv: 4000.0, highEv: 4000.0);

        var energy = Assert.Single(ViewportPicture.Scales(outcome));

        Assert.True(energy.IsSingleValue);
        Assert.False(new ColorScale("energy", "eV", 1.0, 2.0, false).IsSingleValue);
    }

    /// <summary>No picture has no scales.</summary>
    [Fact]
    public void NoPictureHasNoScales() => Assert.Empty(ViewportPicture.Scales(null));

    /// <summary>The legend's ends are written so they differ whenever they do.</summary>
    [Theory]
    [InlineData(4000.0, 4000.05, "4000", "4000.05")]
    [InlineData(-12.5, 12.5, "-12.5", "12.5")]
    [InlineData(0.0, 0.001, "0", "0.001")]
    [InlineData(1000000.0, 1000100.0, "1000000", "1000100")]
    [InlineData(-900.0, -100.0, "-900", "-100")]
    [InlineData(900.0, 2000.0, "900", "2000")]
    [InlineData(5.0, 5.0, "5.00", "5.00")]
    public void ScaleEndsAreWrittenToBeToldApart(double low, double high, string lowText, string highText)
    {
        var (l, h) = ScaleText.Ends(low, high);

        output.WriteLine($"{low} .. {high} -> {l} .. {h}");

        Assert.Equal(lowText, l);
        Assert.Equal(highText, h);

        if (low != high)
        {
            Assert.NotEqual(l, h);
        }
    }

    private static ViewportOutcome Outcome(
        IReadOnlyList<TrajectoryPath>? trajectories = null,
        IReadOnlyList<ConductorSurface>? conductors = null,
        IReadOnlyList<Equipotential>? equipotentials = null,
        double? lowEv = null,
        double? highEv = null,
        double? lowVolts = null,
        double? highVolts = null) =>
        new(
            ModelPath: "model.json",
            Trajectories: trajectories ?? [],
            ProducesTrajectories: trajectories is { Count: > 0 },
            LowestEnergyEv: lowEv,
            HighestEnergyEv: highEv,
            Conductors: conductors ?? [],
            Equipotentials: equipotentials ?? [],
            LowestPotentialVolts: lowVolts,
            HighestPotentialVolts: highVolts,
            Density: [],
            PeakDensityPerCubicMetre: null,
            DensityAtUs: null,
            Ends: null,
            Warnings: []);

    private static TrajectoryPath Path(double fromEv, double toEv) =>
        new([[0.0, 0.0, 0.0], [1.0, 0.0, 0.0]], [fromEv, toEv], "arrived");

    /// <summary>A conductor that is drawn: it has a triangle.</summary>
    private static ConductorSurface Rod(string name, double dc, double drive) =>
        new(name, dc, drive, VerticesMm: [0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0, 0.0],
            Normals: [0.0, 0.0, 1.0, 0.0, 0.0, 1.0, 0.0, 0.0, 1.0], Triangles: [0, 1, 2]);

    /// <summary>A conductor the viewport skips, because it has nothing to draw.</summary>
    private static ConductorSurface Undrawn(string name, double dc, double drive) =>
        new(name, dc, drive, VerticesMm: [], Normals: [], Triangles: []);

    private static Equipotential Level(double volts) => new(volts, []);
}
