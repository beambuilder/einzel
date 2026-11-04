using Einzel.Analysis;
using Einzel.Core.Geometry;
using Einzel.Core.Units;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The arbitrary waveform, checked against two things it must reduce to.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 9 lists an arbitrary waveform among the excitations an electrode may
/// carry, and section 12's remaining Class B figure — isolation efficiency against
/// notch width — cannot be measured without one. A Fourier series is not a
/// restriction on "arbitrary": every periodic waveform is one, and a notch is more
/// naturally written as a list of harmonics with a gap in it than as the samples of
/// whatever waveform has that spectrum.
/// </para>
/// <para>
/// What makes it checkable is that two waveforms already here are special cases. A
/// single term of order one <em>is</em> a sinusoid, and it must give the same
/// trajectory to the bit. And the Fourier series of a square wave must converge on
/// the square wave — not approximately, but far enough to recover the published
/// digital-mass-filter cut-off at q = 0.712, which is a number this engine has no
/// part in and already reproduces by the direct route.
/// </para>
/// </remarks>
public sealed class ArbitraryWaveformTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private const double DriveHz = 1.0e6;

    /// <summary>
    /// The Fourier series of a unit square wave, to a given number of terms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Odd harmonics only, amplitude 4/(pi n). Written out here rather than produced
    /// by the engine, because it is the closed form the reduction is being checked
    /// against.
    /// </para>
    /// <para>
    /// A quarter turn of phase on every term, because the square wave's series is a
    /// <em>sine</em> series and a HarmonicTerm is a cosine. Writing it with zero
    /// phase is the obvious thing to do and gives a completely different waveform -
    /// a square wave shifted a quarter cycle - which converged perfectly well and
    /// moved the digital cut-off from 0.712 to about 0.703. A reduction that
    /// converges to the wrong thing is worse than one that does not converge.
    /// </para>
    /// </remarks>
    private static RfWaveform.Harmonic SquareSeries(int terms)
    {
        var list = new List<HarmonicTerm>();

        for (var k = 0; k < terms; k++)
        {
            var order = (2 * k) + 1;

            list.Add(new HarmonicTerm(order, 4.0 / (Math.PI * order), -0.25));
        }

        return new RfWaveform.Harmonic(list);
    }

    [Fact]
    public void OneTermOfOrderOneIsExactlyASinusoid()
    {
        // The reduction that must be exact, not close. A single cosine at the
        // fundamental with zero phase is the sinusoid, and if the harmonic path
        // introduced any scaling or phase convention of its own this is where it
        // would show - at every phase, not on average.
        var sinusoid = new RfWaveform.Sinusoid();
        var series = new RfWaveform.Harmonic([new HarmonicTerm(1, 1.0, 0.0)]);

        var worst = 0.0;

        for (var k = 0; k <= 720; k++)
        {
            var phase = k / 720.0;

            worst = Math.Max(worst, Math.Abs(series.At(phase) - sinusoid.At(phase)));
        }

        output.WriteLine($"worst departure over 721 phases: {worst:E3}");

        Assert.Equal(0.0, worst, 1e-15);
        Assert.Equal(0.0, series.Mean, 1e-15);
    }

    [Fact]
    public void PhasesAreInTurnsSoAHalfIsExactlyAntiphase()
    {
        // Same reason the drive decomposition uses CosPi: Math.Cos(Math.PI) is -1 to
        // a rounding, so a half-turn phase written in radians leaves an antiphase
        // term carrying a quadrature component made of round-off.
        var forward = new RfWaveform.Harmonic([new HarmonicTerm(1, 1.0, 0.0)]);
        var reversed = new RfWaveform.Harmonic([new HarmonicTerm(1, 1.0, 0.5)]);

        // Exactly, where the argument is exactly representable. 2*(k/16) is k/8 and
        // 2*(k/16 + 0.5) is k/8 + 1, both exact in binary, and CosPi of arguments
        // one apart is an exact negation. This is the claim the convention buys.
        for (var k = 0; k <= 16; k++)
        {
            var phase = k / 16.0;

            Assert.Equal(0.0, forward.At(phase) + reversed.At(phase));
        }

        // And to round-off elsewhere. It cannot be exact at an arbitrary phase and
        // saying so is the point: the argument 2*(n*t + 1/2) is itself rounded, and
        // the error in it grows with the harmonic order, so the exactness is a
        // property of the *convention* at representable phases rather than a
        // guarantee at every instant of a flight.
        var worst = 0.0;

        foreach (var order in new[] { 1, 3, 17 })
        {
            var a = new RfWaveform.Harmonic([new HarmonicTerm(order, 1.0, 0.0)]);
            var b = new RfWaveform.Harmonic([new HarmonicTerm(order, 1.0, 0.5)]);

            for (var k = 0; k <= 360; k++)
            {
                worst = Math.Max(worst, Math.Abs(a.At(k / 360.0) + b.At(k / 360.0)));
            }
        }

        output.WriteLine($"worst residual at arbitrary phases, orders 1 to 17: {worst:E3}");

        Assert.True(worst < 1e-13, $"the antiphase residual should stay at round-off: {worst:E3}");
    }

    [Fact]
    public void TheSeriesConvergesOnTheSquareWaveAwayFromItsEdges()
    {
        // Gibbs is why this is measured away from the edges rather than everywhere:
        // a truncated series overshoots a discontinuity by about nine per cent
        // however many terms it has, and that overshoot never goes away, it only
        // narrows. So the claim is convergence in the interior, which is what the
        // physics depends on - and the persistence of the edge overshoot is asserted
        // too, because a series that did NOT show it would not be a Fourier series.
        var square = new RfWaveform.Rectangular(0.5);

        output.WriteLine("terms   interior worst   edge overshoot");

        var previous = double.MaxValue;

        foreach (var terms in new[] { 5, 20, 80 })
        {
            var series = SquareSeries(terms);
            var interior = 0.0;
            var overshoot = 0.0;

            for (var k = 0; k <= 4000; k++)
            {
                var phase = k / 4000.0;
                var distance = Math.Min(
                    Math.Min(phase, Math.Abs(phase - 0.5)), Math.Abs(phase - 1.0));

                var departure = Math.Abs(series.At(phase) - square.At(phase));

                if (distance > 0.05)
                {
                    interior = Math.Max(interior, departure);
                }

                overshoot = Math.Max(overshoot, series.At(phase) - 1.0);
            }

            output.WriteLine($"{terms,5}   {interior,14:F6}   {overshoot,14:F6}");

            Assert.True(
                interior < previous,
                $"{terms} terms should be closer in the interior than the previous count");

            // The Gibbs constant: about 8.9 per cent, and it does not fall.
            Assert.InRange(overshoot, 0.05, 0.20);

            previous = interior;
        }
    }

    [Fact]
    public void TheSeriesRecoversThePublishedDigitalCutOff()
    {
        // The literature-grade check, and the one that says the arbitrary-waveform
        // path drives an ion rather than merely evaluating to the right numbers.
        // Schrader, Anderson and Russell (JASMS 2024) put the square-wave low-mass
        // cut-off at q = 0.712; the direct rectangular route's 4 mm-aperture
        // bisection gives 0.71113, and the boundary itself is 0.71249366
        // (RectangularBoundaryTests). Driving the same geometry with the Fourier
        // series of that square wave has to land in the same place.
        //
        // Eighty terms rather than five: what matters is not the waveform's
        // appearance but the sharpness of the boundary it produces, and a truncated
        // series is a smoother wave whose cut-off sits slightly differently.
        output.WriteLine("waveform          q through   q lost");

        foreach (var (name, waveform) in new (string, RfWaveform)[]
                 {
                     ("rectangular", new RfWaveform.Rectangular(0.5)),
                     ("series, 80 terms", SquareSeries(80)),
                 })
        {
            double? through = null;
            double? lost = null;

            for (var q = 0.68; q <= 0.75; q += 0.005)
            {
                if (Survives(q, waveform))
                {
                    through = q;
                }
                else
                {
                    lost ??= q;
                }
            }

            output.WriteLine($"{name,-18}{through,10:F3}{lost,9:F3}");

            Assert.NotNull(through);
            Assert.NotNull(lost);

            // The published boundary is bracketed by the last survivor and the first
            // loss, whichever route produced them.
            Assert.True(
                through < 0.712 && lost > 0.712,
                $"{name} should bracket the published 0.712, and it gives "
                + $"({through:F3}, {lost:F3})");
        }
    }

    private static bool Survives(double q, RfWaveform waveform)
    {
        var species = Peptide;
        var radius = Quantity.From(4.0, "mm");

        var field = IdealQuadrupoleRf.FromMathieu(
            0.0, q, species.Mass(), species.Charge(),
            Quantity.From(DriveHz, "Hz"), radius, waveform);

        var start = new PhaseState(new Vec3(0.1e-3, 0.1e-3, 0.0), Vec3.Zero);
        var aperture = radius.In("m");

        TrajectoryStopFunction inside = (in PhaseState s) =>
            aperture - Math.Max(Math.Abs(s.Position.X), Math.Abs(s.Position.Y));

        var result = TrajectoryIntegrator.Integrate(
            start,
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = 200.0 / DriveHz,
            },
            inside);

        return result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached;
    }

    [Fact]
    public void ASupplementaryDipoleEjectsAnIonAtItsOwnSecularFrequencyAndNotAtOthers()
    {
        // Resonant ejection, which is the mechanism a notch works by. The main RF
        // confines; a small uniform field oscillating at the ion's own secular
        // frequency pumps it. Driven off resonance the same amplitude does almost
        // nothing, and that contrast is the whole measurement - an excitation that
        // ejected at every frequency would be a bad trap rather than a resonance.
        const double Q = 0.4;

        var species = Peptide;
        var radius = Quantity.From(4.0, "mm");

        var quadrupole = IdealQuadrupoleRf.FromMathieu(
            0.0, Q, species.Mass(), species.Charge(), Quantity.From(DriveHz, "Hz"), radius);

        // The secular frequency, measured rather than assumed - which is what the
        // spectrum is for.
        var secular = MeasureSecular(quadrupole, species, radius);

        output.WriteLine($"secular {secular / 1e3:F2} kHz");
        output.WriteLine("  excitation      excursion (mm)   outcome");

        var excursions = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var (name, hertz) in new[]
                 {
                     ("on resonance", secular),
                     ("half", secular * 0.5),
                     ("double", secular * 2.0),
                 })
        {
            // Small enough that the forced response off resonance is a fraction of
            // a millimetre. Resonant growth is linear in time and forced response is
            // not, so the way to separate them is a weak drive for a long time
            // rather than a strong one - a drive strong enough to eject in a few
            // cycles ejects off resonance too, and measures the amplitude.
            var dipole = OscillatingUniformField.Create(
                new Vec3(4.0e2, 0.0, 0.0), Quantity.From(hertz, "Hz"));

            var field = new DrivenSuperposedField([quadrupole, dipole]);

            var (excursion, outcome) = FlyAndMeasure(field, species, radius, 1200);

            output.WriteLine($"  {name,-14}{excursion * 1e3,14:F4}   {outcome}");

            excursions[name] = excursion;
        }

        // Resonant excitation grows without bound until something stops it; off
        // resonance the ion is pushed and returns. An order of magnitude is a
        // conservative statement of that difference.
        Assert.True(
            excursions["on resonance"] > 10.0 * excursions["half"],
            $"on resonance {excursions["on resonance"] * 1e3:F4} mm against "
            + $"{excursions["half"] * 1e3:F4} mm at half the frequency");

        Assert.True(
            excursions["on resonance"] > 10.0 * excursions["double"],
            $"on resonance {excursions["on resonance"] * 1e3:F4} mm against "
            + $"{excursions["double"] * 1e3:F4} mm at twice the frequency");
    }

    private static double MeasureSecular(
        IdealQuadrupoleRf quadrupole, IonSpecies species, Quantity radius)
    {
        var recorder = new TrajectoryRecorder(quadrupole.ShortestPeriodSeconds / 16.0);

        TrajectoryIntegrator.Integrate(
            new PhaseState(new Vec3(0.2e-3, 0.0, 0.0), Vec3.Zero),
            species,
            quadrupole,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-10,
                MaximumFlightTime = 200.0 / DriveHz,
            },
            (in PhaseState s) => radius.In("m") - Math.Abs(s.Position.X),
            recorder);

        var spectrum = SecularSpectrum.From(
            recorder.Samples, 0, 0.02 * DriveHz, 0.60 * DriveHz, 6000);

        var peak = spectrum.Peak();

        Assert.NotNull(peak);

        var (value, _, _, _) = peak;

        return value.In("Hz");
    }

    private static (double Excursion, TrajectoryOutcome Outcome) FlyAndMeasure(
        IElectrostaticField field, IonSpecies species, Quantity radius, int cycles)
    {
        var aperture = radius.In("m");
        var recorder = new TrajectoryRecorder(1.0 / (16.0 * DriveHz));

        var result = TrajectoryIntegrator.Integrate(
            new PhaseState(new Vec3(0.05e-3, 0.0, 0.0), Vec3.Zero),
            species,
            field,
            new IntegrationSettings
            {
                RelativeTolerance = 1e-9,
                MaximumFlightTime = cycles / DriveHz,
            },
            (in PhaseState s) => aperture - Math.Abs(s.Position.X),
            recorder);

        var excursion = 0.0;

        foreach (var sample in recorder.Samples)
        {
            excursion = Math.Max(excursion, Math.Abs(sample.Position.X));
        }

        return (excursion, result.Outcome);
    }

    [Fact]
    public void ASuperposedDrivenFieldIsStillDriven()
    {
        // The defect this type exists for. A SuperposedField satisfies only
        // IElectrostaticField, and a driven member answers that interface at t = 0 -
        // so summing a driven element with anything else used to produce a snapshot
        // of the RF at the top of its cycle, silently, with nothing in the result to
        // say so. The check is simply that the field at two instants differs.
        var species = Peptide;

        var quadrupole = IdealQuadrupoleRf.FromMathieu(
            0.0, 0.4, species.Mass(), species.Charge(),
            Quantity.From(DriveHz, "Hz"), Quantity.From(4.0, "mm"));

        var field = new DrivenSuperposedField(
            [quadrupole, UniformField.Create(new Vec3(0.0, 0.0, 100.0))]);

        var at = new Vec3(1.0e-3, 0.0, 0.0);

        var start = field.ElectricFieldAt(in at, 0.0);
        var quarter = field.ElectricFieldAt(in at, 0.25 / DriveHz);

        output.WriteLine($"t = 0        {start}");
        output.WriteLine($"t = T/4      {quarter}");

        Assert.NotEqual(start.X, quarter.X, 1e-9);

        // The static member is carried at every instant, unchanged.
        Assert.Equal(100.0, start.Z, 1e-12);
        Assert.Equal(100.0, quarter.Z, 1e-12);

        // And the shortest period is the driven member's, so the integrator caps
        // its step by the fastest thing in the sum.
        Assert.Equal(quadrupole.ShortestPeriodSeconds, field.ShortestPeriodSeconds, 1e-15);

        // A superposition with nothing driven in it is refused rather than
        // pretending: a field that reports a drive it does not have makes an
        // integrator cap its step for nothing.
        Assert.Throws<ArgumentException>(
            () => new DrivenSuperposedField([UniformField.Create(new Vec3(1.0, 0.0, 0.0))]));
    }
}
