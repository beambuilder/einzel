using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Library.Tests;

/// <summary>
/// What the endcap-hole studies share: the geometries, how a trap is compiled and solved once,
/// how an ion is flown in it at any amplitude, what counts as losing one, and the closed forms
/// the measurements are checked against.
/// </summary>
/// <remarks>
/// One copy, so the field table in <see cref="PaulTrapEndcapHoleStudy"/> and the dynamics table
/// in <see cref="PaulTrapEndcapHoleDynamics"/> are measured on the same geometries by
/// construction rather than by two lists that have to be kept in step.
/// </remarks>
internal static class PaulTrapHoles
{
    /// <summary>The hole radius the measurements use, as a fraction of the endcap's outer radius.</summary>
    public const double HoleFraction = 0.175;

    /// <summary>The stretch the measurements use: the recalled Finnigan figure, a guess.</summary>
    public const double Stretch = 0.107;

    /// <summary>The named geometries every measurement is made on.</summary>
    public static readonly (string Name, (string, double)[] Overrides)[] Geometries =
    [
        ("ideal", []),
        ("both holes", [("endcapEntranceHoleFraction", HoleFraction), ("endcapExitHoleFraction", HoleFraction)]),
        ("exit hole only", [("endcapExitHoleFraction", HoleFraction)]),
        ("stretch only", [("endcapStretchFraction", Stretch)]),
        ("holes + stretch",
            [("endcapEntranceHoleFraction", HoleFraction), ("endcapExitHoleFraction", HoleFraction),
             ("endcapStretchFraction", Stretch)]),
    ];

    /// <summary>The overrides of a named geometry.</summary>
    public static (string, double)[] Geometry(string name) =>
        Geometries.Single(g => g.Name == name).Overrides;

    /// <summary>The template with literal parameters overridden.</summary>
    public static ModelDocument Trap(params (string Name, double Value)[] overrides)
    {
        var document = Io.ModelJson.Parse(DeviceTemplates.Read("paul-trap"));

        var parameters = new Dictionary<string, ParameterDocument>(
            document.Parameters!, StringComparer.Ordinal);

        foreach (var (name, value) in overrides)
        {
            parameters[name] = parameters[name] with { Value = value };
        }

        return document with { Parameters = parameters };
    }

    /// <summary>Validates and compiles, failing the test with every error if it does not.</summary>
    public static CompiledModel Compile(ModelDocument document)
    {
        var validation = ModelValidator.Validate(document);

        Assert.True(
            validation.Model is not null,
            string.Join("; ", validation.Errors.Select(e => e.Path + ": " + e.Constraint)));

        return validation.Model!;
    }

    /// <summary>The Mathieu characteristic exponent, from its continued fraction.</summary>
    /// <remarks>
    /// The closed form the measurements are checked against, written in the tests rather than
    /// shipped: comparing the engine's beta to the engine's spectrum would test only
    /// self-consistency.
    /// </remarks>
    public static double Beta(double a, double q, int depth = 40)
    {
        var beta = Math.Sqrt(Math.Max(a + (q * q / 2.0), 1e-6));

        for (var iteration = 0; iteration < 500; iteration++)
        {
            var up = 0.0;
            var down = 0.0;

            for (var n = depth; n >= 1; n--)
            {
                up = q * q / (((beta + (2 * n)) * (beta + (2 * n))) - a - up);
                down = q * q / (((beta - (2 * n)) * (beta - (2 * n))) - a - down);
            }

            var next = Math.Sqrt(Math.Max(a + up + down, 1e-12));

            if (Math.Abs(next - beta) < 1e-14)
            {
                return next;
            }

            beta = 0.5 * (beta + next);
        }

        return beta;
    }

    /// <summary>The axial curvature at the center, dEz/dz, by a central difference.</summary>
    public static double AxialCurvature(ITimeVaryingField field, double delta) =>
        (field.ElectricFieldAt(new Vec3(delta, 0.0, 0.0), 0.0).X
            - field.ElectricFieldAt(new Vec3(-delta, 0.0, 0.0), 0.0).X)
        / (2.0 * delta);

    /// <summary>The radial curvature at the center, dEr/dr, by a central difference.</summary>
    public static double RadialCurvature(ITimeVaryingField field, double delta) =>
        (field.ElectricFieldAt(new Vec3(0.0, delta, 0.0), 0.0).Y
            - field.ElectricFieldAt(new Vec3(0.0, -delta, 0.0), 0.0).Y)
        / (2.0 * delta);

    /// <summary>
    /// The effective radius from the axial curvature at the center: the r0 an ideal trap would
    /// need to have the same q per volt.
    /// </summary>
    /// <remarks>0.4 mm, a tenth of r0, the sampling radius every study here uses.</remarks>
    public static double EffectiveRadius(ITimeVaryingField field, double volts, double delta = 0.4e-3) =>
        Math.Sqrt(2.0 * volts / AxialCurvature(field, delta));

    /// <summary>
    /// One geometry, compiled and solved once, flown at any amplitude.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ring is the only driven electrode and holds no DC, so the field is exactly the
    /// amplitude times one solved pattern: the basis superposition the engine is built on.
    /// Flying at another amplitude is therefore a re-weighting of the same solve, done here by
    /// <see cref="AmplitudeScaledField"/> rather than by recompiling the document, which would
    /// re-solve an identical pattern for every flight.
    /// </para>
    /// </remarks>
    public sealed class Solved
    {
        public Solved(params (string Name, double Value)[] overrides)
        {
            Model = Compile(Trap(overrides));
            Field = (ITimeVaryingField)FieldAssembly.Build(Model);
            Species = IonSpecies.FromModel(Model);
            ReferenceVolts = Model.Parameters["rfAmplitude"].SiValue;
            DriveHz = Model.Parameters["driveFrequency"].SiValue;
            EffectiveRadius = PaulTrapHoles.EffectiveRadius(Field, ReferenceVolts);

            var omega = 2.0 * Math.PI * DriveHz;

            QPerVolt = 4.0 * Species.ChargeSi
                / (Species.MassSi * omega * omega * EffectiveRadius * EffectiveRadius);
            NominalQPerVolt = 4.0 * Species.ChargeSi
                / (Species.MassSi * omega * omega * Math.Pow(Model.Parameters["inscribedRadius"].SiValue, 2));
            EndcapVertex = Model.Parameters["endcapHalfSpacing"].SiValue
                + Model.Parameters["endcapStretch"].SiValue;
        }

        public CompiledModel Model { get; }

        public ITimeVaryingField Field { get; }

        public IonSpecies Species { get; }

        public double ReferenceVolts { get; }

        public double DriveHz { get; }

        public double EffectiveRadius { get; }

        /// <summary>q_z per volt at the effective radius.</summary>
        public double QPerVolt { get; }

        /// <summary>q_z per volt at the declared radius.</summary>
        public double NominalQPerVolt { get; }

        /// <summary>
        /// Where each endcap's face crosses the axis, z0 plus any stretch: the edge of the
        /// trapping region along the axis.
        /// </summary>
        public double EndcapVertex { get; }

        /// <summary>Flies one ion from rest at an axial and a radial offset.</summary>
        /// <param name="volts">The ring's drive amplitude.</param>
        /// <param name="axialMm">Starting offset along the axis, in mm.</param>
        /// <param name="radialMm">Starting offset across it, in mm.</param>
        /// <param name="cycles">How long to hold, in RF cycles; need not be whole.</param>
        /// <param name="loss">What counts as losing the ion.</param>
        /// <param name="record">Whether to record the path at sixteen samples a cycle.</param>
        /// <param name="relativeTolerance">The integrator's relative tolerance; the template's by default.</param>
        /// <param name="phaseCycles">The RF phase at launch, as a fraction of a cycle.</param>
        public Flight Fly(
            double volts, double axialMm, double radialMm, double cycles, Loss loss,
            bool record = false, double relativeTolerance = 1e-9, double phaseCycles = 0.0)
        {
            var field = volts == ReferenceVolts
                ? Field
                : new AmplitudeScaledField(Field, volts / ReferenceVolts);

            // The RF phase at launch, as a fraction of a cycle: the same field seen from that
            // much later. Zero leaves the field object untouched, so every single-ion result
            // measured before the ensemble existed is the same flight to the bit.
            if (phaseCycles != 0.0)
            {
                field = new TimeShiftedField(field, phaseCycles / DriveHz);
            }

            var launch = new PhaseState(new Vec3(axialMm * 1e-3, radialMm * 1e-3, 0.0), Vec3.Zero);

            var point = Model.DetectorPoint;
            var normal = Model.DetectorNormal;
            var vertex = EndcapVertex;

            TrajectoryStopFunction stop = loss == Loss.TrappingRegion
                ? (in PhaseState state) => Math.Min(
                    Vec3.Dot(state.Position - point, normal),
                    vertex - Math.Abs(state.Position.X))
                : (in PhaseState state) => Vec3.Dot(state.Position - point, normal);

            var recorder = record ? new TrajectoryRecorder(1.0 / (16.0 * DriveHz)) : null;

            var result = TrajectoryIntegrator.Integrate(
                launch,
                Species,
                field,
                new IntegrationSettings
                {
                    RelativeTolerance = relativeTolerance,
                    MaximumFlightTime = cycles / DriveHz,
                },
                stop,
                recorder);

            return new Flight(result, recorder?.Samples ?? [], this, loss);
        }

        /// <summary>
        /// The ejection edge by bisection: the bracket in volts between the last amplitude
        /// that held the ion and the first that lost it.
        /// </summary>
        public (double Held, double Lost) Edge(
            double axialMm, double radialMm, double cycles, Loss loss,
            double low = 600.0, double high = 1000.0, int steps = 11, double relativeTolerance = 1e-9)
        {
            bool Held(double volts) =>
                Fly(volts, axialMm, radialMm, cycles, loss, relativeTolerance: relativeTolerance).Held;

            Assert.True(Held(low), $"not held at {low} V");
            Assert.False(Held(high), $"not lost at {high} V");

            for (var k = 0; k < steps; k++)
            {
                var mid = 0.5 * (low + high);

                if (Held(mid))
                {
                    low = mid;
                }
                else
                {
                    high = mid;
                }
            }

            return (low, high);
        }
    }

    /// <summary>What counts as losing an ion.</summary>
    public enum Loss
    {
        /// <summary>
        /// Lost when it first passes the plane of either endcap's vertex - whether it then
        /// strikes metal, enters a bore, or leaves - or strikes anything, or reaches the
        /// detector. The same criterion for every geometry, decided at the instant of
        /// crossing rather than at the end of the hold.
        /// </summary>
        TrappingRegion,

        /// <summary>
        /// The criterion the first version of the study used: still flying at the end of the
        /// hold and, at that final instant, inside |z| &lt; z0 and r &lt; r0. In a solid trap
        /// the two agree, because an ion reaching the endcap vertex strikes it; in a holed
        /// one an ion can swing into the bore and back, and this reads it by its phase when
        /// the hold ends.
        /// </summary>
        FinalPosition,
    }

    /// <summary>One flight and whether it counts as held under its criterion.</summary>
    public sealed record Flight(
        TrajectoryResult Result, IReadOnlyList<TrajectorySample> Samples, Solved Trap, Loss Loss)
    {
        /// <summary>Whether the ion was held for the whole hold under the flight's criterion.</summary>
        public bool Held => HeldUnder(Loss);

        /// <summary>Whether the ion was held for the whole hold under a named criterion.</summary>
        /// <remarks>
        /// A flight that ran to the end of its hold can be judged under either criterion,
        /// whichever it was flown with: a stop surface that never fired changed nothing, so it
        /// is the trajectory the other criterion would have flown. A flight the vertex plane
        /// ended cannot say where the ion would have been at the end of the hold, and asking
        /// it throws rather than guessing.
        /// </remarks>
        public bool HeldUnder(Loss criterion)
        {
            if (Result.Outcome != TrajectoryOutcome.MaximumFlightTimeReached)
            {
                if (criterion == Loss.FinalPosition && Loss == Loss.TrappingRegion
                    && Result.Outcome == TrajectoryOutcome.StopConditionMet)
                {
                    throw new InvalidOperationException(
                        "a flight ended at the vertex plane cannot be judged by its final position");
                }

                return false;
            }

            if (criterion == Loss.TrappingRegion)
            {
                // Flown without the vertex-plane stop, a completed flight does not say
                // whether it crossed that plane on the way.
                if (Loss != Loss.TrappingRegion)
                {
                    throw new InvalidOperationException(
                        "a flight flown without the vertex-plane stop cannot be judged by it");
                }

                return true;
            }

            var r0 = Trap.Model.Parameters["inscribedRadius"].SiValue;
            var p = Result.FinalState.Position;

            return Math.Abs(p.X) < Trap.EndcapVertex && Math.Sqrt((p.Y * p.Y) + (p.Z * p.Z)) < r0;
        }
    }

    /// <summary>
    /// A driven field multiplied by a constant: the same solved pattern at another amplitude.
    /// </summary>
    /// <remarks>
    /// Exact for a field linear in one amplitude, which the trap's is - the ring is its only
    /// driven electrode and nothing holds DC - and wrong for anything else, so it is private
    /// to these tests rather than offered as engine machinery.
    /// </remarks>
    private sealed class AmplitudeScaledField(ITimeVaryingField inner, double factor)
        : ITimeVaryingField, IConductorBounded
    {
        public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
            inner.ElectricFieldAt(position, timeSeconds) * factor;

        public double PotentialAt(in Vec3 position, double timeSeconds) =>
            inner.PotentialAt(position, timeSeconds) * factor;

        public Vec3 ElectricFieldAt(in Vec3 position) => inner.ElectricFieldAt(position) * factor;

        public double PotentialAt(in Vec3 position) => inner.PotentialAt(position) * factor;

        public double ResolutionLength => inner.ResolutionLength;

        public double OscillatingResolutionLength => inner.OscillatingResolutionLength;

        public double ShortestPeriodSeconds => inner.ShortestPeriodSeconds;

        public double MonochromaticPeriodSeconds => inner.MonochromaticPeriodSeconds;

        public double SignedDistanceToConductor(in Vec3 position) => inner is IConductorBounded bounded
            ? bounded.SignedDistanceToConductor(in position) : double.PositiveInfinity;

        public string? ConductorAt(in Vec3 position) => inner is IConductorBounded bounded
            ? bounded.ConductorAt(in position) : null;

        public double NextSwitchAfter(double timeSeconds) => inner.NextSwitchAfter(timeSeconds);

        public double SignedDistanceToDiscontinuity(in Vec3 position) =>
            inner.SignedDistanceToDiscontinuity(position);

        public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
            inner.FieldFreeRunLength(position, direction);
    }
}
