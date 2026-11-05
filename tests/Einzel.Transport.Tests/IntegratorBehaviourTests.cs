using Einzel.Core.Geometry;
using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Transport;
using Einzel.Fields;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

public sealed class IntegratorBehaviourTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    private static IdealSingleStageReflectron Reflectron() =>
        IdealSingleStageReflectron.AtFirstOrderFocus(
            Peptide, Quantity.From(4.0, "kV"), Quantity.From(50.0, "mm"));

    [Fact]
    public void ExactBoundaryLandingDominatesTheErrorBudget()
    {
        // Regression guard on the finding that motivated
        // SignedDistanceToDiscontinuity. Before the integrator landed on the
        // mirror boundary, a step straddled the field jump and the flight-time
        // error sat on a floor near 5e-10 that barely responded to tolerance.
        // Landing exactly on the boundary took the same case to 1.7e-16.
        var reflectron = Reflectron();
        var settings = reflectron.Settings() with { TurningPointStepFactor = 0.0 };

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field, settings, reflectron.DetectorPlane());

        var exact = reflectron.ExactFlightTime();
        var relative = Math.Abs(result.FlightTimeSeconds - exact) / exact;

        Assert.True(relative < 1e-13, $"relative error {relative:E3} regressed against the measured 1.7e-16");
    }

    [Fact]
    public void AnalyticDriftCoversTheWholeFieldFreePath()
    {
        var reflectron = Reflectron();

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), reflectron.DetectorPlane());

        // Out and back along the drift region, advanced exactly rather than
        // integrated. In the memo's design point B this is most of a 7.55 m path.
        var expected = 2.0 * reflectron.DriftLength;
        Assert.Equal(expected, result.AnalyticDriftDistance, expected * 1e-9);
    }

    [Fact]
    public void DisablingAnalyticDriftStillAgreesWithTheClosedForm()
    {
        // Analytic drift is an optimisation, not a correction: switching it off
        // must change cost, not the answer.
        var reflectron = Reflectron();
        var stop = reflectron.DetectorPlane();

        var withDrift = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), stop);

        var withoutDrift = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings() with { UseAnalyticDrift = false }, stop);

        Assert.Equal(0.0, withoutDrift.AnalyticDriftDistance);
        Assert.Equal(
            withDrift.FlightTimeSeconds,
            withoutDrift.FlightTimeSeconds,
            withDrift.FlightTimeSeconds * 1e-9);
    }

    [Fact]
    public void FinalStateLandsOnTheDetectorPlaneRatherThanPastIt()
    {
        var reflectron = Reflectron();

        var result = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), reflectron.DetectorPlane());

        // The crossing time is the measurement, so the flight must end on the
        // surface, not at the end of whichever step happened to pass it.
        Assert.Equal(-reflectron.DriftLength, result.FinalState.Position.X, 1e-12);
    }

    [Fact]
    public void AnUnboundedRunIsRefusedRatherThanRunForever()
    {
        var settings = new IntegrationSettings();
        var launch = new PhaseState(Vec3.Zero, new Vec3(1000.0, 0.0, 0.0));

        var error = Assert.Throws<ArgumentException>(() => TrajectoryIntegrator.Integrate(
            launch, Peptide, FieldFreeSpace.Instance, settings));

        Assert.Contains("MaximumFlightTime", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AllocationDoesNotGrowWithStepCount()
    {
        // Spec section 22 lists GC pauses in long runs as a risk "preventable
        // with allocation discipline from the start", and section 11 requires the
        // inner loop to allocate nothing. Testing a byte threshold would encode
        // today's fixed overhead; testing that cost is flat in step count tests
        // the actual property.
        var reflectron = Reflectron();
        var stop = reflectron.DetectorPlane();

        var few = reflectron.Settings() with { TurningPointStepFactor = 0.05 };
        var many = reflectron.Settings() with { TurningPointStepFactor = 0.0005 };

        // Warm up: first-call JIT and static initialisation are not per-step cost.
        var warmFew = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field, few, stop);
        var warmMany = TrajectoryIntegrator.Integrate(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field, many, stop);

        Assert.True(
            warmMany.AcceptedSteps > warmFew.AcceptedSteps * 10,
            $"the two settings should differ greatly in step count: {warmFew.AcceptedSteps} vs {warmMany.AcceptedSteps}");

        // The cheapest of several runs, not one run. What is being measured is the
        // marginal cost of a step, and the runtime charges one-off costs to whichever
        // run happens to trigger them - a tier-1 recompilation or an on-stack
        // replacement lands in the window it fires in. Those are load-dependent, so
        // this test passed alone and failed inside a full parallel suite, which is
        // the worst way for a test to be wrong: it reads as a regression in the
        // thing under test.
        //
        // The minimum is the right statistic because the property is a floor. A run
        // that allocated nothing per step proves the per-step cost is zero however
        // many other runs paid a one-off charge; averaging would fold those charges
        // back in.
        long Cheapest(IntegrationSettings settings)
        {
            var best = long.MaxValue;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var before = GC.GetAllocatedBytesForCurrentThread();

                TrajectoryIntegrator.Integrate(
                    reflectron.LaunchState(), reflectron.Species, reflectron.Field, settings, stop);

                best = Math.Min(best, GC.GetAllocatedBytesForCurrentThread() - before);
            }

            return best;
        }

        var costFew = Cheapest(few);
        var costMany = Cheapest(many);

        // Reported whether or not it fails, so a future failure is diagnosable from
        // the log rather than needing a rerun.
        output.WriteLine($"{warmFew.AcceptedSteps,6} steps: {costFew,6} bytes");
        output.WriteLine($"{warmMany.AcceptedSteps,6} steps: {costMany,6} bytes");

        Assert.True(
            costMany <= costFew + 256,
            $"allocation grew with step count: {costFew} bytes over {warmFew.AcceptedSteps} steps, "
            + $"{costMany} bytes over {warmMany.AcceptedSteps} steps");
    }

    [Fact]
    public void FlightTimeStudyReportsAConvergenceBoundedResult()
    {
        var reflectron = Reflectron();

        var study = FlightTimeStudy.Run(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            reflectron.Settings(), reflectron.DetectorPlane());

        var (value, uncertainty, evidence, warnings) = study.FlightTime;

        var exact = reflectron.ExactFlightTime();
        Assert.Equal(exact, value.SiValue, exact * 1e-8);

        // A deterministic convergence bound, not a statistical interval.
        Assert.Equal(1.0, uncertainty.ConfidenceLevel);
        Assert.True(uncertainty.WidthSi > 0.0, "an interval of exactly zero is not a bound");

        var convergence = Assert.IsType<Evidence.Convergence>(evidence);
        Assert.Equal("integrator tolerance", convergence.Measure);

        Assert.DoesNotContain(warnings, w => w.Severity == WarningSeverity.ValidityViolation);
        Assert.Equal(3, study.Runs.Count);
    }

    /// <summary>
    /// An interval that collapses to zero is reported as a floor, not as exact.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GRD-1 in its sharpest form. When the two finest refinements agree to the bit the
    /// residual between them - which is what the interval is built from - is zero.
    /// Printed, that reads as "10.180506 +/- 0 us", which a reader takes for an exact
    /// number.
    /// </para>
    /// <para>
    /// It is not exact. It is an uncertainty smaller than a comparison of two doubles can
    /// see, and the two statements are not interchangeable: one is defensible in a paper
    /// and the other is not. An agent asked to quote a result refused this one and
    /// measured its own tolerance ladder instead, which was the right instinct and should
    /// not have been necessary.
    /// </para>
    /// <para>
    /// <b>Tested against the rule rather than through a model that happens to saturate.</b>
    /// It used to run the reflectron and assert that its rungs agreed exactly - which
    /// they did, but only because the refinement ladder was scaling an absolute velocity
    /// floor down to 1e-11 m/s. That floor is held now (it made an ion starting from rest
    /// unintegrable), the reflectron's rungs differ at 1e-12, and no setting reachable
    /// through the study's own API reproduces the collapse. A rule that can only be
    /// exercised by a coincidence is a rule with no test.
    /// </para>
    /// </remarks>
    [Fact]
    public void AnIntervalThatCollapsesToZeroIsReportedAsAFloorRatherThanAsExact()
    {
        static TrajectoryResult At(double seconds) => new()
        {
            FlightTimeSeconds = seconds,
            TimeCompensation = 0.0,
            FinalState = new PhaseState(Vec3.Zero, Vec3.Zero),
            Outcome = TrajectoryOutcome.StopConditionMet,
            AcceptedSteps = 1,
            RejectedSteps = 0,
            FieldEvaluations = 1,
            MaximumRelativeEnergyDrift = 0.0,
            AnalyticDriftDistance = 0.0,
        };

        const double Flight = 1.0180505717871196e-05;

        // The two finest agree to the bit; the coarsest does not. The pair says nothing,
        // so the whole ladder is the fallback.
        var (residual, atResolution) = FlightTimeStudy.ConvergenceResidual(
            [At(Flight * (1.0 + 1e-9)), At(Flight), At(Flight)]);

        Assert.True(atResolution);
        Assert.Equal(Flight * 1e-9, residual, Flight * 1e-12);

        // And when even the whole ladder collapses, one ulp - never zero, which is the
        // number GRD-1 exists to keep off the page.
        var (floor, collapsed) = FlightTimeStudy.ConvergenceResidual(
            [At(Flight), At(Flight), At(Flight)]);

        Assert.True(collapsed);
        Assert.True(floor > 0.0, "an interval of exactly zero is not a bound");
        Assert.Equal(Math.BitIncrement(Flight) - Flight, floor);

        // A ladder that genuinely converged reports its measured residual and says
        // nothing about resolution.
        var (measured, saturated) = FlightTimeStudy.ConvergenceResidual(
            [At(Flight * 1.01), At(Flight * 1.001), At(Flight)]);

        Assert.False(saturated);
        Assert.Equal(Flight * 0.001, measured, Flight * 1e-9);
    }

    [Fact]
    public void AnIncompleteTrajectoryIsAValidityViolation()
    {
        var reflectron = Reflectron();

        var truncated = reflectron.Settings() with
        {
            MaximumFlightTime = reflectron.ExactFlightTime() * 0.25,
        };

        var study = FlightTimeStudy.Run(
            reflectron.LaunchState(), reflectron.Species, reflectron.Field,
            truncated, reflectron.DetectorPlane());

        Assert.True(study.FlightTime.HasNonSuppressibleWarnings);
        Assert.Contains(study.FlightTime.Warnings, w => w.Code == "TRAJECTORY_INCOMPLETE");
    }

    /// <summary>
    /// A flight whose last step leaves the compensated time one ulp short of its end reaches
    /// the end, not <c>StepSizeUnderflow</c>. The case is the one of 63 one-period flights of
    /// <c>RectangularBoundaryTests</c> that ended short at 2.1e-22 s (one ulp of 1 us) under
    /// the end-of-flight cut before this rule: an ideal quadrupole with a 0.3 duty rectangular
    /// drive at a = 0.1, q = 0.1156 - 0.01 (one probe below the y edge), from rest, one period
    /// under <c>land</c>. Landing cannot catch it, because the next rising edge the phase
    /// arithmetic reports is one double above T. The end time is reported exactly.
    /// </summary>
    [Fact]
    public void AFlightEndingAnUlpShortOfItsTimeReachesIt()
    {
        var mass = Quantity.From(100.0, "u");
        var charge = Quantity.From(1.0, "e");
        var field = Fields.Analytic.IdealQuadrupoleRf.FromMathieu(
            0.1, 0.1155681312 - 0.01, mass, charge, Quantity.From(1.0, "MHz"), Quantity.From(4.0, "mm"),
            new Fields.Analytic.RfWaveform.Rectangular(0.3));
        var period = field.ShortestPeriodSeconds;

        var result = TrajectoryIntegrator.Integrate(
            new PhaseState(new Vec3(1e-3, 1e-3, 0.0), Vec3.Zero),
            IonSpecies.Create(mass, charge),
            field,
            new IntegrationSettings { RelativeTolerance = 1e-11, MaximumFlightTime = period, LandOnEdges = true });

        output.WriteLine($"ended {result.Outcome} at {result.FlightTimeSeconds:R} s against {period:R} s, {result.AcceptedSteps} steps");
        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, result.Outcome);
        Assert.Equal(period, result.FlightTimeSeconds);
    }

    /// <summary>
    /// The same rule, without depending on where a controller's step sequence happens to
    /// land: in a uniform field every step is exact, so with the initial and maximum step
    /// both 2^-30 s the time after sixteen steps is 2^-26 s to the bit, and a flight time
    /// 1e-19 s beyond that leaves a remainder below the minimum step of 1e-18 s. The flight
    /// ends at its time, as a landing closer than the minimum step is taken in place; the
    /// state is the state at the sixteenth step.
    /// </summary>
    [Fact]
    public void ARemainderBelowTheMinimumStepIsTheFlightEnd()
    {
        const double H = 9.313225746154785e-10; // 2^-30
        const double End = (16.0 * H) + 1e-19;
        var start = new PhaseState(Vec3.Zero, new Vec3(1e3, 0.0, 0.0));

        var result = TrajectoryIntegrator.Integrate(
            start,
            Peptide,
            UniformField.Create(new Vec3(0.0, 1e3, 0.0)),
            new IntegrationSettings
            {
                InitialStep = H,
                MaximumStep = H,
                MinimumStep = 1e-18,
                MaximumFlightTime = End,
                TurningPointStepFactor = 0.0,
            });

        Assert.Equal(16, result.AcceptedSteps);
        Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, result.Outcome);
        Assert.Equal(End, result.FlightTimeSeconds);
        Assert.Equal(16.0 * H * 1e3, result.FinalState.Position.X, 1e-12);
    }
}
