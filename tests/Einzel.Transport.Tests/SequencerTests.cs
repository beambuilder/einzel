using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;
using Xunit.Abstractions;

namespace Einzel.Transport.Tests;

/// <summary>
/// The sequencer: a geometry operated through timed states.
/// </summary>
/// <remarks>
/// <para>
/// The architecture diagram calls it a timed state machine, and it is what a trap
/// needs - fill, isolate, extract, with different potentials in each. A geometry
/// that can only be driven one way for a whole run cannot describe one.
/// </para>
/// <para>
/// The check that matters is not that a switch happens but that it happens
/// <em>cleanly</em>. A Runge-Kutta step spanning a switch averages two different
/// fields into one answer, and the result is plausible and wrong. Unlike a boundary
/// in space this needs no root-find, because the time is known in advance - so the
/// test is whether a sequenced run equals the same flight computed as two separate
/// runs stitched together, which it must to the integrator's own tolerance.
/// </para>
/// </remarks>
public sealed class SequencerTests(ITestOutputHelper output)
{
    private static IonSpecies Peptide => IonSpecies.FromMassToCharge(500.0, 1);

    /// <summary>A plate at one end of a grounded box, at a stated potential.</summary>
    private static CompiledElectrode Plate(double volts) => new()
    {
        Name = "pusher",
        Shape = ElectrodeShape.Rectangle,
        MinX = -0.0105,
        MaxX = -0.010,
        MinY = -0.01,
        MaxY = 0.01,
        Potential = volts,
    };

    private static CompiledSolvedField Geometry(double volts, IReadOnlyList<CompiledStage> stages) => new()
    {
        MinX = -0.011,
        MinY = -0.011,
        MaxX = 0.011,
        MaxY = 0.011,
        CellSize = 0.011 / 48.0,
        Tolerance = 1e-12,
        Electrodes = [Plate(volts)],
        Stages = stages,
    };

    private static TrajectoryResult Fly(
        CompiledSolvedField solve, PhaseState start, double seconds, double tolerance = 1e-11, bool land = false)
    {
        var field = GeometryBuilder.Build(solve).Field;

        return TrajectoryIntegrator.Integrate(
            start,
            Peptide,
            field,
            new IntegrationSettings { RelativeTolerance = tolerance, MaximumFlightTime = seconds, LandOnEdges = land });
    }

    private static PhaseState Start => new(new Vec3(0.0, 0.0, 0.0), new Vec3(0.0, 0.0, 0.0));

    [Fact]
    public void LandingOnTheSwitchStitchesAtLeastAsCloselyAsTheDefault()
    {
        // The test above under transport.edges = land: the landing step samples its end
        // stages one ulp before the switch, the time is set to the switch exactly and the
        // derivative is evaluated there in the new state. The stitched route is two plain
        // flights either way.
        //
        // Measured (relative gap, cap then land): 1.0e-8 and 2.3e-7 at 1e-8, 4.6e-8 and
        // 2.0e-8 at 1e-10, 1.3e-9 and 1.0e-9 at 1e-12. At 1e-8 the gap says nothing about
        // the switch: against the stitched flight at 1e-12 cap, land and the stitched route
        // itself are all 6e-7 to 8.5e-7 out, and cap agrees with the stitched route only
        // because it takes the same steps up to the switch. Land takes 22 accepted steps
        // (11 rejected) where cap takes 53 (32 rejected); both take 12 (7) before the
        // switch. Cap's landing step samples its last stages at t + h, on the far side, so
        // its error estimate sees the jump: from the switch on it rejects 25 times and
        // regrows from a small step. Land samples the near side, rejects 4 and carries its
        // step across. So the gap is bounded there as the default's is, and held to the
        // default's only where it measures the switch: at 1e-10, where land is 1.1e-8 from
        // the tight flight and cap 3.6e-8 (asserted under 3e-8 and 1e-7), and at 1e-12.
        const double Volts = 200.0;
        const double Half = 1e-6;

        var tightFirst = Fly(Geometry(Volts, []), Start, Half, 1e-12);
        var reference = Fly(Geometry(-Volts, []), tightFirst.FinalState, Half, 1e-12).FinalState.Position;

        output.WriteLine("tolerance     cap gap       land gap      cap err       land err      cap acc/rej   land acc/rej");

        foreach (var tolerance in new[] { 1e-8, 1e-10, 1e-12 })
        {
            var geometry = Geometry(
                Volts,
                [
                    new CompiledStage("push", Half, [Plate(Volts)]),
                    new CompiledStage("pull", Half, [Plate(-Volts)]),
                ]);
            var first = Fly(Geometry(Volts, []), Start, Half, tolerance);
            var second = Fly(Geometry(-Volts, []), first.FinalState, Half, tolerance);
            var cap = Fly(geometry, Start, 2.0 * Half, tolerance);
            var land = Fly(geometry, Start, 2.0 * Half, tolerance, land: true);

            var scale = Math.Abs(second.FinalState.Position.X);
            var capGap = (cap.FinalState.Position - second.FinalState.Position).Length / scale;
            var landGap = (land.FinalState.Position - second.FinalState.Position).Length / scale;

            var capError = (cap.FinalState.Position - reference).Length / scale;
            var landError = (land.FinalState.Position - reference).Length / scale;

            output.WriteLine(
                $"{tolerance,9:E0}   {capGap,10:E3}   {landGap,10:E3}   {capError,10:E3}   {landError,10:E3}   "
                + $"{cap.AcceptedSteps}/{cap.RejectedSteps}   {land.AcceptedSteps}/{land.RejectedSteps}");

            Assert.Equal(TrajectoryOutcome.MaximumFlightTimeReached, land.Outcome);
            Assert.True(landGap < 1e-6, $"at {tolerance:E0} land leaves {landGap:E3}");

            if (tolerance < 1e-9)
            {
                Assert.True(landGap <= capGap, $"at {tolerance:E0} land leaves {landGap:E3}, cap {capGap:E3}");
            }

            if (tolerance == 1e-10)
            {
                Assert.True(landError < 3e-8, $"at 1e-10 land is {landError:E3} from the tight flight");
                Assert.True(capError < 1e-7, $"at 1e-10 cap is {capError:E3} from the tight flight");
                Assert.True(landError <= capError, $"at 1e-10 land {landError:E3}, cap {capError:E3}");
            }
        }
    }

    [Fact]
    public void ASequencedRunEqualsTheSameFlightStitchedFromTwoRuns()
    {
        // The whole test of a sequencer. Two stages of one microsecond each, the
        // pusher reversing between them, against the same flight computed as two
        // separate runs handed one to the next. They are the same physics written
        // two ways, so they must agree to the tolerance the integrator was asked
        // for - and they only can if the step lands exactly on the switch.
        const double Volts = 200.0;
        const double Half = 1e-6;

        // Measured at three tolerances, because the two routes take different step
        // sequences and so differ by round-off however right they both are. What
        // separates that from a step straddling the switch is that round-off falls
        // when the tolerance falls and a straddled switch does not: the error would
        // be a fixed physical mistake sitting under the controller.
        output.WriteLine("tolerance     position gap      relative");

        var gaps = new List<double>();

        foreach (var tolerance in new[] { 1e-8, 1e-10, 1e-12 })
        {
            var sequenced = Fly(
                Geometry(
                    Volts,
                    [
                        new CompiledStage("push", Half, [Plate(Volts)]),
                        new CompiledStage("pull", Half, [Plate(-Volts)]),
                    ]),
                Start,
                2.0 * Half,
                tolerance);

            var first = Fly(Geometry(Volts, []), Start, Half, tolerance);
            var second = Fly(Geometry(-Volts, []), first.FinalState, Half, tolerance);

            var gap = (sequenced.FinalState.Position - second.FinalState.Position).Length;
            var relative = gap / Math.Abs(second.FinalState.Position.X);

            gaps.Add(relative);

            output.WriteLine($"{tolerance,9:E0}   {gap,14:E3} m   {relative,10:E3}");

            if (tolerance == 1e-12)
            {
                output.WriteLine(string.Empty);
                output.WriteLine($"sequenced   x {sequenced.FinalState.Position.X * 1e6,10:F5} um, "
                    + $"vx {sequenced.FinalState.Velocity.X,10:F5} m/s");
                output.WriteLine($"stitched    x {second.FinalState.Position.X * 1e6,10:F5} um, "
                    + $"vx {second.FinalState.Velocity.X,10:F5} m/s");

                // The ion has to have actually moved and actually turned, or two
                // ways of computing nothing would agree perfectly.
                Assert.True(sequenced.FinalState.Position.X > 0.0, "the ion did not move");
                Assert.True(first.FinalState.Velocity.X > 0.0, "the ion was not pushed in the first stage");
                Assert.True(
                    sequenced.FinalState.Velocity.X < 0.5 * first.FinalState.Velocity.X,
                    "the second stage did not slow the ion, so the switch did nothing");
            }
        }

        output.WriteLine(string.Empty);
        output.WriteLine("every gap is at round-off between two different step sequences - parts per");
        output.WriteLine("billion - rather than the parts per thousand a step averaging two fields");
        output.WriteLine("across the switch would leave. It does not fall monotonically, because which");
        output.WriteLine("steps each route happens to take is luck rather than a trend.");

        Assert.All(
            gaps,
            gap => Assert.True(gap < 1e-6, $"the two routes disagree by {gap:E3} of the displacement"));

        Assert.True(gaps[^1] < 1e-8, $"at the tightest tolerance they still disagree by {gaps[^1]:E3}");
    }

    [Fact]
    public void TheStepLandsOnTheSwitchRatherThanStraddlingIt()
    {
        // Directly: the field asked when it next changes, and the integrator is
        // required not to step past it. Without the clamp a step of the natural
        // size would span the boundary here, because the flight is far longer than
        // one stage and nothing else limits the step in a smooth field.
        var solve = Geometry(
            200.0,
            [
                new CompiledStage("a", 1e-6, [Plate(200.0)]),
                new CompiledStage("b", 1e-6, [Plate(-200.0)]),
            ]);

        var driven = Assert.IsType<DrivenSolvedField>(GeometryBuilder.Build(solve).Field);

        output.WriteLine($"{driven.StageCount} stages, sequence ends at {driven.SequenceEndsAt * 1e6:F3} us");
        output.WriteLine($"next switch after 0      {driven.NextSwitchAfter(0.0) * 1e6:F3} us");
        output.WriteLine($"next switch after 1.5 us {driven.NextSwitchAfter(1.5e-6) * 1e6:F3} us");
        output.WriteLine($"next switch after 2.5 us {driven.NextSwitchAfter(2.5e-6)}");

        Assert.Equal(2, driven.StageCount);
        Assert.Equal(1e-6, driven.NextSwitchAfter(0.0), 15);
        Assert.Equal(2e-6, driven.NextSwitchAfter(1.5e-6), 15);
        Assert.True(double.IsPositiveInfinity(driven.NextSwitchAfter(2.5e-6)));

        // And the weight really is different either side, or there is nothing to
        // land on.
        Assert.True(driven.WeightAt(0, 0.5e-6) > 0.0);
        Assert.True(driven.WeightAt(0, 1.5e-6) < 0.0);
    }

    [Fact]
    public void TheLastStageHoldsAfterTheSequenceEnds()
    {
        // A sequence describes what the instrument does, and an instrument left
        // alone stays where it was put. A field that switched off at the end of the
        // declared sequence would make every ion still in flight suddenly coast,
        // which is a physics change disguised as a bookkeeping one.
        var solve = Geometry(
            200.0,
            [
                new CompiledStage("a", 1e-6, [Plate(200.0)]),
                new CompiledStage("b", 1e-6, [Plate(-50.0)]),
            ]);

        var driven = Assert.IsType<DrivenSolvedField>(GeometryBuilder.Build(solve).Field);

        var during = driven.WeightAt(0, 1.5e-6);
        var after = driven.WeightAt(0, 9.0e-6);

        output.WriteLine($"weight during the last stage {during:F3} V");
        output.WriteLine($"weight long after it ends    {after:F3} V");

        Assert.Equal(during, after, 12);
    }

    [Fact]
    public void StagesSharingAPatternShareTheirSolve()
    {
        // A trap that fills and then extracts usually energises the same electrodes
        // at different voltages, and paying for the same solve twice would be
        // paying for the sequencer rather than for the physics.
        var solve = Geometry(
            200.0,
            [
                new CompiledStage("hold", 1e-6, [Plate(200.0)]),
                new CompiledStage("push", 1e-6, [Plate(1000.0)]),
                new CompiledStage("rest", 1e-6, [Plate(-30.0)]),
            ]);

        var driven = Assert.IsType<DrivenSolvedField>(GeometryBuilder.Build(solve).Field);

        output.WriteLine($"three stages on one electrode reduced to {driven.ChannelCount} solve(s)");

        for (var k = 0; k < 3; k++)
        {
            output.WriteLine($"  stage {k}: weight {driven.WeightAt(0, (k + 0.5) * 1e-6),8:F1} V");
        }

        Assert.Equal(1, driven.ChannelCount);
        Assert.Equal(200.0, driven.WeightAt(0, 0.5e-6), 9);
        Assert.Equal(1000.0, driven.WeightAt(0, 1.5e-6), 9);
        Assert.Equal(-30.0, driven.WeightAt(0, 2.5e-6), 9);
    }
}
