using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;

using Xunit.Abstractions;

namespace Einzel.Fields.Tests;

/// <summary>
/// The quadro-logarithmic field: harmonic axially, logarithmic radially, and exactly
/// separable between the two.
/// </summary>
/// <remarks>
/// <para>
/// The field an orbital trap is built from, named for its mathematics because architecture
/// invariant 2 keeps device names above <c>Einzel.Library</c>.
/// </para>
/// <para>
/// <b>Its defining property is an independence rather than a value.</b> The axial motion
/// obeys <c>m z'' = -q k z</c> with no <c>r</c> anywhere in it, so the axial frequency is
/// <c>sqrt(q k / m)</c> whatever the radius, whatever the angular momentum, and whatever
/// the axial amplitude. Measuring mass by frequency rests entirely on that: every other
/// thing about the ion's motion is allowed to vary, and the number being measured does not.
/// </para>
/// <para>
/// So the tests below scan those three quantities and assert the frequency does <em>not</em>
/// move. A field that was nearly right — a real electrode geometry, say — would give a
/// frequency that drifted with amplitude, and that drift is exactly what an orbital
/// instrument is designed to avoid.
/// </para>
/// </remarks>
public sealed class QuadroLogarithmicFieldTests(ITestOutputHelper output)
{
    private const double ElementaryCharge = 1.602176634e-19;
    private const double Dalton = 1.66053906892e-27;

    /// <summary>A field with a 20 mm characteristic radius.</summary>
    private static QuadroLogarithmicField Field(double curvature = 2.0e7) =>
        QuadroLogarithmicField.Create(
            Quantity.From(curvature, "V/m^2"), Quantity.From(20.0, "mm"), Vec3.Zero);

    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>Laplace, checked by differencing the potential.</summary>
    /// <remarks>
    /// <para>
    /// <b>The check that the formula is a field at all.</b> A potential that does not
    /// satisfy Laplace is not produced by any arrangement of conductors in free space, so
    /// an ion flown in it is answering a question about nothing.
    /// </para>
    /// <para>
    /// The quadratic part contributes <c>-k</c> to the radial Laplacian and <c>+k</c> to the
    /// axial one, and the logarithm is harmonic on its own — so the cancellation is exact
    /// and any residual here is the differencing, which is why the tolerance is scaled to
    /// the second derivative rather than absolute.
    /// </para>
    /// </remarks>
    [Fact]
    public void ThePotentialIsHarmonic()
    {
        var field = Field();

        const double H = 1e-6;

        var worst = 0.0;

        foreach (var point in new[]
        {
            new Vec3(0.0, 5e-3, 0.0),
            new Vec3(3e-3, 8e-3, 2e-3),
            new Vec3(-4e-3, 12e-3, -6e-3),
            new Vec3(1e-3, 18e-3, 4e-3),
        })
        {
            var centre = field.PotentialAt(in point);
            var sum = 0.0;

            foreach (var axis in new[] { new Vec3(1, 0, 0), new Vec3(0, 1, 0), new Vec3(0, 0, 1) })
            {
                var plus = point + (axis * H);
                var minus = point - (axis * H);

                sum += (field.PotentialAt(in plus) - (2.0 * centre) + field.PotentialAt(in minus))
                    / (H * H);
            }

            // Scaled against the curvature, which is the size of each term before they
            // cancel: an absolute tolerance would say nothing about whether they did.
            var relative = Math.Abs(sum) / field.CurvatureSi;

            output.WriteLine(
                $"at ({point.X * 1e3:F1}, {point.Y * 1e3:F1}, {point.Z * 1e3:F1}) mm: "
                + $"laplacian / k = {relative:E3}");

            worst = Math.Max(worst, relative);
        }

        Assert.True(worst < 1e-6, $"the Laplacian is {worst:E3} of the curvature");
    }

    /// <summary>The field is the negative gradient of the potential.</summary>
    /// <remarks>
    /// Written separately in the implementation, so they can disagree — and a field that
    /// does not derive from its own potential conserves no energy, which would show up much
    /// later as a mysterious drift.
    /// </remarks>
    [Fact]
    public void TheFieldIsTheGradientOfThePotential()
    {
        var field = Field();

        const double H = 1e-6;

        var worst = 0.0;

        foreach (var point in new[]
        {
            new Vec3(2e-3, 6e-3, 0.0),
            new Vec3(-5e-3, 11e-3, 3e-3),
            new Vec3(0.0, 16e-3, -8e-3),
        })
        {
            var got = field.ElectricFieldAt(in point);

            foreach (var (axis, component) in new (Vec3, double)[]
            {
                (new Vec3(1, 0, 0), got.X),
                (new Vec3(0, 1, 0), got.Y),
                (new Vec3(0, 0, 1), got.Z),
            })
            {
                var plus = point + (axis * H);
                var minus = point - (axis * H);

                var numeric =
                    -(field.PotentialAt(in plus) - field.PotentialAt(in minus)) / (2.0 * H);

                worst = Math.Max(worst, Math.Abs(numeric - component) / Math.Max(1.0, got.Length));
            }
        }

        output.WriteLine($"worst relative departure from -grad U: {worst:E3}");

        Assert.True(worst < 1e-6);
    }

    /// <summary>Flies an ion and returns the axial samples.</summary>
    private static IReadOnlyList<TrajectorySample> Fly(
        QuadroLogarithmicField field,
        IonSpecies species,
        double radiusMm,
        double axialMm,
        double tangential,
        double microseconds)
    {
        var launch = new PhaseState(
            new Vec3(axialMm * 1e-3, radiusMm * 1e-3, 0.0),
            new Vec3(0.0, 0.0, tangential));

        var recorder = new TrajectoryRecorder(microseconds * 1e-6 / 2000.0, capacity: 8192);

        TrajectoryIntegrator.Integrate(
            launch,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-11,
                MaximumFlightTime = microseconds * 1e-6,
            },
            stopWhenNegative: null,
            recorder);

        return recorder.Samples;
    }

    /// <summary>The axial period, from the zero crossings of the axial coordinate.</summary>
    /// <remarks>
    /// Crossings rather than a peak fit, because a crossing is where the signal moves
    /// fastest and is therefore the best-determined point on it — and averaging over many
    /// of them divides the timing error by their number.
    /// </remarks>
    private static double AxialPeriod(IReadOnlyList<TrajectorySample> samples)
    {
        var crossings = new List<double>();

        for (var k = 1; k < samples.Count; k++)
        {
            var before = samples[k - 1].Position.X;
            var now = samples[k].Position.X;

            if (before < 0.0 && now >= 0.0)
            {
                var t0 = samples[k - 1].TimeSeconds;
                var t1 = samples[k].TimeSeconds;

                crossings.Add(t0 + ((t1 - t0) * (0.0 - before) / (now - before)));
            }
        }

        Assert.True(crossings.Count >= 3, $"only {crossings.Count} axial crossings were seen");

        return (crossings[^1] - crossings[0]) / (crossings.Count - 1);
    }

    /// <summary>
    /// The axial frequency is what the closed form says, and does not care about the
    /// radius, the angular momentum or the amplitude.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The whole instrument, in one property.</b> An orbital trap measures mass by
    /// measuring this frequency, which works only because the frequency depends on nothing
    /// else. Each row below changes something the ion is doing — how far out it orbits, how
    /// fast it goes round, how far it swings axially — and the frequency has to stay put.
    /// </para>
    /// <para>
    /// The tangential speed is set to the circular-orbit value for each radius, from
    /// <c>v^2 = q k (Rm^2 - r^2) / (2 m)</c>, so the ion genuinely orbits rather than
    /// falling inward. That is a different closed form from the one being tested, which is
    /// what makes this a check rather than a tautology.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(6.0, 1.0)]
    [InlineData(6.0, 3.0)]
    [InlineData(10.0, 1.0)]
    [InlineData(10.0, 4.0)]
    [InlineData(14.0, 2.0)]
    public void TheAxialFrequencyDependsOnNothingButTheIon(double radiusMm, double axialMm)
    {
        var field = Field();
        var species = Peptide;

        var expected = field.AxialAngularFrequency(species.ChargeSi, species.MassSi);

        // The circular-orbit speed at this radius, from the radial force balance. A
        // different closed form from the one under test.
        var r = radiusMm * 1e-3;
        var rm = field.CharacteristicRadiusSi;

        var tangential = Math.Sqrt(
            species.ChargeSi * field.CurvatureSi * ((rm * rm) - (r * r)) / (2.0 * species.MassSi));

        var samples = Fly(field, species, radiusMm, axialMm, tangential, 40.0);
        var measured = 2.0 * Math.PI / AxialPeriod(samples);

        var relative = Math.Abs(measured - expected) / expected;

        output.WriteLine(
            $"r = {radiusMm,5:F1} mm, z0 = {axialMm,4:F1} mm, v_t = {tangential,8:F1} m/s: "
            + $"measured {measured:F4} rad/s, closed form {expected:F4}, "
            + $"relative {relative:E3}");

        // Relative, because the quantity is of order 1e6 and a decimal-place assertion on
        // it would be asserting round-off. The floor is set by locating the crossings on a
        // recorded trajectory rather than by the field, which is exact.
        Assert.True(relative < 1e-6, $"the axial frequency is {relative:E3} off the closed form");
    }

    /// <summary>The frequency goes as the inverse square root of mass to charge.</summary>
    /// <remarks>
    /// The calibration law an orbital analyser runs on. Asserted across a factor of ten in
    /// mass so that a linear or otherwise wrong dependence could not pass — the frequencies
    /// differ by more than three times across the range.
    /// </remarks>
    [Theory]
    [InlineData(200.0)]
    [InlineData(500.0)]
    [InlineData(2000.0)]
    public void TheFrequencyGoesAsTheInverseRootOfMassToCharge(double massToCharge)
    {
        var field = Field();
        var species = IonSpecies.FromMassToCharge(massToCharge, 1);

        var r = 10.0e-3;
        var rm = field.CharacteristicRadiusSi;

        var tangential = Math.Sqrt(
            species.ChargeSi * field.CurvatureSi * ((rm * rm) - (r * r)) / (2.0 * species.MassSi));

        var samples = Fly(field, species, 10.0, 2.0, tangential, 40.0 * Math.Sqrt(massToCharge / 500.0));
        var measured = 2.0 * Math.PI / AxialPeriod(samples);

        // sqrt(q k / m) written out from the declared mass-to-charge, which is arithmetic
        // this field had no part in.
        var expected = Math.Sqrt(field.CurvatureSi / (massToCharge * Dalton / ElementaryCharge));

        var relative = Math.Abs(measured - expected) / expected;

        output.WriteLine(
            $"m/z {massToCharge,6:F0}: measured {measured:F3} rad/s, expected {expected:F3}, "
            + $"relative {relative:E3}");

        Assert.True(relative < 1e-6, $"the frequency is {relative:E3} off sqrt(q k / m)");
    }

    /// <summary>Angular momentum and energy are both conserved.</summary>
    /// <remarks>
    /// The field has no azimuthal component by construction, so angular momentum is an
    /// identity rather than an accuracy — the same tolerance-free check the Kingdon trap
    /// rests on. Energy is the ordinary electrostatic one and is here to catch a field that
    /// does not derive from its own potential.
    /// </remarks>
    [Fact]
    public void AngularMomentumAndEnergyAreConserved()
    {
        var field = Field();
        var species = Peptide;

        var r = 10.0e-3;
        var rm = field.CharacteristicRadiusSi;

        var tangential = Math.Sqrt(
            species.ChargeSi * field.CurvatureSi * ((rm * rm) - (r * r)) / (2.0 * species.MassSi));

        var samples = Fly(field, species, 10.0, 3.0, tangential, 40.0);

        static double Angular(in TrajectorySample s) =>
            (s.Position.Y * s.Velocity.Z) - (s.Position.Z * s.Velocity.Y);

        double Energy(in TrajectorySample s) =>
            (0.5 * species.MassSi * s.Velocity.LengthSquared)
            + (species.ChargeSi * field.PotentialAt(s.Position));

        var l0 = Angular(samples[0]);
        var e0 = Energy(samples[0]);

        // Against the kinetic energy, not against the total. In a well this deep the two
        // terms nearly cancel - about 3000 eV of kinetic against -3182 eV of potential -
        // so a drift divided by the total is divided by almost nothing and reports a
        // catastrophe for a part-per-billion error. The kinetic scale is what ACC-4 is
        // written against everywhere else here.
        var scale = 0.5 * species.MassSi * samples[0].Velocity.LengthSquared;

        var worstL = samples.Max(s => Math.Abs(Angular(s) - l0)) / Math.Abs(l0);
        var worstE = samples.Max(s => Math.Abs(Energy(s) - e0)) / scale;

        output.WriteLine($"angular momentum drifts {worstL:E3}");
        output.WriteLine($"energy drifts           {worstE:E3}");

        // Round-off, not physics. The identity lives in the field and is asserted exactly
        // in the test below; what a flown trajectory measures is how faithfully the
        // integrator carries it, and at a relative tolerance of 1e-11 over a 40 us flight
        // this is where that lands. Asserting tighter here would be asserting the
        // integrator's arithmetic rather than the field's symmetry.
        Assert.True(worstL < 1e-8, $"angular momentum moved by {worstL:E3}");
        Assert.True(worstE < 1e-8, $"energy moved by {worstE:E3}");
    }

    /// <summary>The azimuthal field is exactly zero, which is where the identity lives.</summary>
    /// <remarks>
    /// <para>
    /// <b>The conservation law asserted at its source rather than through an ion.</b> A
    /// surface of revolution can exert no torque about its own axis, so the azimuthal
    /// component of the field is not small — it is zero, and it stays zero at every point
    /// and every radius. That is checkable directly and exactly, where a flown trajectory
    /// can only ever show the integrator's fidelity to it.
    /// </para>
    /// <para>
    /// Separating the two matters: the trajectory check above drifts at 1e-10 and would
    /// keep passing if the field acquired a small azimuthal component, since that is what a
    /// small drift looks like. This one would not.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheAzimuthalFieldIsExactlyZero()
    {
        var field = Field();

        var worst = 0.0;

        foreach (var radius in new[] { 2e-3, 7e-3, 13e-3, 19e-3 })
        {
            foreach (var turns in new[] { 0.0, 0.17, 0.4, 0.75 })
            {
                var point = new Vec3(
                    3e-3,
                    radius * double.CosPi(2.0 * turns),
                    radius * double.SinPi(2.0 * turns));

                var got = field.ElectricFieldAt(in point);

                // The azimuthal direction: perpendicular to both the axis and the radius.
                var azimuth = new Vec3(
                    0.0, -double.SinPi(2.0 * turns), double.CosPi(2.0 * turns));

                worst = Math.Max(worst, Math.Abs(Vec3.Dot(got, azimuth)) / got.Length);
            }
        }

        output.WriteLine($"worst azimuthal component, as a fraction of the field: {worst:E3}");

        // Exactly, not nearly: the field is built from an axial component and a radial one
        // and there is no third term for an azimuthal part to come from.
        Assert.Equal(0.0, worst, 15);
    }

    /// <summary>The axis is refused rather than clamped.</summary>
    /// <remarks>
    /// A logarithm has no value at zero, and the region is where the central electrode is.
    /// Returning a large number instead would let an ion be launched inside metal and flown
    /// there, which is the shape of silent wrongness this project refuses elsewhere.
    /// </remarks>
    [Fact]
    public void TheAxisIsRefusedRatherThanClamped()
    {
        var field = Field();
        var onAxis = new Vec3(1e-3, 0.0, 0.0);

        Assert.Throws<PointRefusedException>(() => field.PotentialAt(in onAxis));
        Assert.Throws<PointRefusedException>(() => field.ElectricFieldAt(in onAxis));
    }
}
