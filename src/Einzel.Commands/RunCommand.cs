using System.Text.Json.Serialization;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Io;
using Einzel.Project;
using Einzel.Transport;
using Einzel.Fields;
using Einzel.Analysis;
using Einzel.Core.Results;
using Einzel.Core.Units;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>The outcome of validating a model.</summary>
public sealed record ValidateOutcome
{
    /// <summary>Whether the document validated.</summary>
    public required bool Valid { get; init; }

    /// <summary>The model file, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Content hash of the model document.</summary>
    public required string ModelHash { get; init; }

    /// <summary>The declared schema version.</summary>
    public string? SchemaVersion { get; init; }

    /// <summary>Every error found, in document order.</summary>
    public required IReadOnlyList<EinzelError> Errors { get; init; }

    /// <summary>The exit code this outcome maps to (CLI-3).</summary>
    [JsonIgnore]
    public ExitCode ExitCode => Valid
        ? ExitCode.Success
        : Errors.Any(e => e.Code == ErrorCodes.RegimeInvalid)
            ? ExitCode.RegimeViolation
            : ExitCode.ValidationFailure;
}


/// <summary>One ion's outcome and end time, as written to the result document.</summary>
/// <param name="Ion">The ion's index in the cloud, from zero.</param>
/// <param name="Outcome">The integrator's outcome name.</param>
/// <param name="Surface">The electrode struck, by name, or null.</param>
/// <param name="TimeUs">When the flight ended, in microseconds from launch.</param>
/// <param name="XMm">Where the flight ended, x in millimetres: the impact point for a strike, the crossing for an arrival.</param>
/// <param name="YMm">Where the flight ended, y in millimetres.</param>
/// <param name="ZMm">Where the flight ended, z in millimetres.</param>
public sealed record IonEventJson(int Ion, string Outcome, string? Surface, double TimeUs, double XMm, double YMm, double ZMm);

/// <summary>What a cloud of ions did, when a model launches one.</summary>
/// <remarks>
/// The Class S half of a result: transmission, acceptance, efficiency, each with a
/// stated interval. Absent when the model launches a single ion, because a
/// transmission of "one out of one" is not a statistic and reporting it as one
/// would be worse than saying nothing.
/// </remarks>
public sealed record EnsembleOutcome
{
    /// <summary>Collisions the whole ensemble made. Zero in vacuum.</summary>
    public int Collisions { get; init; }

    /// <summary>
    /// How many ions collided at least once and still reached the detector or a
    /// surface.
    /// </summary>
    /// <remarks>
    /// COL-1 keeps a scattered ion that stays within acceptance rather than
    /// discarding it as a loss, so this count is the difference between a peak with
    /// a pedestal and one without - and neither is visible from a transmission
    /// figure, which counts both as arrivals.
    /// </remarks>
    public int ScatteredIons { get; init; }

    /// <summary>How many ions were launched.</summary>
    public required int Launched { get; init; }

    /// <summary>How many reached the detector.</summary>
    public required int Arrived { get; init; }

    /// <summary>Fraction arriving, with its binomial interval.</summary>
    public required MeasuredJson Transmission { get; init; }

    /// <summary>
    /// Fraction still inside at the end of the run - neither struck nor arrived.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>What a trap is measured by</b>, since a trapped ion by definition never arrives
    /// anywhere. Without it a working trap and one that lost every ion both report a
    /// transmission of zero, and the terminal shows the alarming number without the
    /// descriptive one - which is how `paul-trap-held`, an example that behaves exactly as
    /// designed, reads as a total failure.
    /// </para>
    /// <para>
    /// Counted from the flight the run already did rather than by calling the `confined`
    /// figure of merit, which re-flies the whole ensemble. Two implementations of one
    /// quantity is the defect that made `run` and `test` disagree twice here.
    /// </para>
    /// </remarks>
    public required MeasuredJson Confined { get; init; }

    /// <summary>
    /// Where the ions that did not arrive went, by surface, largest first.
    /// </summary>
    /// <remarks>
    /// ACC-5 refuses a bare transmission figure and asks for it itemised by loss
    /// surface and mechanism. Empty when everything arrived, which is a statement
    /// rather than an omission.
    /// </remarks>
    public required IReadOnlyList<LossChannel> Losses { get; init; }

    /// <summary>
    /// Every launched ion's outcome and end time, in launch order - the raw ledger
    /// the counts above are computed from. A mass scan is read off this list: each
    /// ion's ejection instant is its position on the scan's mass axis.
    /// </summary>
    public IReadOnlyList<IonEventJson> Events { get; init; } = [];

    /// <summary>
    /// The width enclosing the central half of the arrivals, in nanoseconds.
    /// </summary>
    /// <remarks>
    /// The model-free one, and the width the resolving power is computed from.
    /// Reported alongside <see cref="GaussianFwhmNs"/> rather than instead of it
    /// because the two disagree whenever the peak is not Gaussian, and a reader
    /// given only one of them beside a resolving power will try to reconcile the
    /// wrong pair.
    /// </remarks>
    public double? CentralWidthNs { get; init; }

    /// <summary>
    /// The Gaussian-equivalent full width at half maximum, in nanoseconds.
    /// </summary>
    /// <remarks>
    /// What the literature quotes, and what the turn-around closed form gives, so
    /// it is the one to compare against a published number. It exceeds the central
    /// width whenever the peak has a tail, which a second-order energy aberration
    /// always does.
    /// </remarks>
    public double? GaussianFwhmNs { get; init; }

    /// <summary>
    /// Asymmetry of the peak, which is why the two widths differ.
    /// </summary>
    /// <remarks>
    /// Zero for a symmetric peak. A mirror away from its focus produces a
    /// one-sided second-order tail, and the sign says which side.
    /// </remarks>
    public double? Skewness { get; init; }

    /// <summary>
    /// The part of the Gaussian width imposed before the ion left, by the source
    /// temperature. Zero for a cold cloud.
    /// </summary>
    /// <remarks>
    /// In the same convention as <see cref="GaussianFwhmNs"/> so the two are
    /// directly comparable: this is how much of that width the extraction is
    /// responsible for, and how much room there is to improve anything else.
    /// </remarks>
    public required double TurnAroundFwhmNs { get; init; }

    /// <summary>Arrival-time resolving power, model-free at half maximum.</summary>
    public MeasuredJson? ResolvingPower { get; init; }

    /// <summary>Ions in the physical packet, which is what pushes on itself.</summary>
    public required int Population { get; init; }

    /// <summary>
    /// The flight-time error the packet's own charge implies, as a fraction.
    /// </summary>
    /// <remarks>
    /// Reported whether or not it crosses a threshold, because a number that only
    /// appears when it is bad teaches nobody where the edge is. Zero for a single
    /// ion or a packet with no declared extent.
    /// </remarks>
    /// <remarks>
    /// Null when the packet has no beam energy for it to be a fraction of, which a
    /// packet still sitting in its trap does not.
    /// </remarks>
    public required double? SpaceChargeTimingFraction { get; init; }

    /// <summary>
    /// How many ions this packet could hold before space charge reaches the 1 ppm
    /// flight-time budget.
    /// </summary>
    public required double SpaceChargePopulationLimit { get; init; }

    /// <summary>
    /// Geometric emittance of the arriving packet in its wider transverse plane,
    /// in millimetre-milliradians.
    /// </summary>
    /// <remarks>
    /// The phase-space area the packet occupies, and what decides whether it fits
    /// through whatever comes next. Optics trade size against divergence and cannot
    /// reduce the product, so this is the number that says what no downstream lens
    /// can fix.
    /// </remarks>
    /// <remarks>
    /// Null when there was no packet to measure it on - fewer than three ions
    /// arrived. Null rather than zero, because a zero emittance is a real and
    /// meaningful answer (a perfectly parallel beam has one) and a reader cannot
    /// tell a measured zero from an absent measurement if both print as zero.
    /// </remarks>
    public required double? EmittanceMmMrad { get; init; }

    /// <summary>
    /// The same area in the narrower plane, in millimetre-milliradians.
    /// </summary>
    /// <remarks>
    /// Both planes, because a packet is rarely round and a single figure would
    /// average away the axis that is about to clip.
    /// </remarks>
    public required double? EmittanceMinorMmMrad { get; init; }

    /// <summary>
    /// Normalised emittance in the wider plane, in millimetre-milliradians.
    /// </summary>
    /// <remarks>
    /// Measured against transverse momentum rather than angle, so acceleration
    /// leaves it alone. A geometric emittance can be improved by nothing more than
    /// raising the beam energy; this one cannot, which makes it the fair way to
    /// compare two sources.
    /// </remarks>
    public required double? NormalisedEmittanceMmMrad { get; init; }

    /// <summary>
    /// Root-mean-square radius of the arriving packet in its wider plane, in
    /// millimetres.
    /// </summary>
    public required double? PacketRadiusMm { get; init; }

    /// <summary>
    /// The Twiss alpha of the arriving packet: positive while still converging,
    /// zero at a waist, negative once past it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Says which side of the focus the detector sits on, which a width alone
    /// cannot. A packet measured at twice its waist size reads the same either way
    /// and needs the opposite correction.
    /// </para>
    /// <para>
    /// Null when the packet has no phase-space area to form an ellipse from - every
    /// ion exactly parallel, which a cloud with spatial spread and no temperature
    /// is. There is no orientation to report, and reporting one anyway is how a
    /// not-a-number reaches a serialiser that has no way to write it.
    /// </para>
    /// </remarks>
    public required double? PacketTwissAlpha { get; init; }
}

/// <summary>
/// The governing dimensionless numbers of a run, on the wire.
/// </summary>
/// <remarks>
/// REG-2 requires these computed rather than assumed. Reported whether or not any
/// of them crosses a threshold, because a reader who can see a Knudsen number of
/// 40 knows something a reader who sees no warning does not: that the run was
/// checked and found comfortable, rather than not checked at all.
/// </remarks>
public sealed record RegimeJson
{
    /// <summary>Gas pressure, in millibar.</summary>
    public required double PressureMbar { get; init; }

    /// <summary>Which collision description was used.</summary>
    public required string CollisionModel { get; init; }

    /// <summary>Ion mean free path, in millimetres.</summary>
    public required double MeanFreePathMm { get; init; }

    /// <summary>The length the Knudsen number was taken against, in millimetres.</summary>
    public required double ApertureMm { get; init; }

    /// <summary>Mean free path over that length. Below 1 the gas is a continuum.</summary>
    public required double Knudsen { get; init; }

    /// <summary>Expected collisions over the flight.</summary>
    public required double CollisionsPerFlight { get; init; }

    /// <summary>Expected collisions per drive cycle, absent when undriven.</summary>
    public double? CollisionsPerRfCycle { get; init; }
}


/// <summary>What became of one ion population in a mixture run.</summary>
/// <param name="Name">The name the document gave it.</param>
/// <param name="MobilitySi">The mobility it actually ran with, in m^2/(V s).</param>
/// <param name="MobilityDerived">
/// Whether that was derived from the gas cross section rather than declared - which the reader
/// needs, because a derived mobility is Mason-Schamp for this species' mass and carries that
/// model's assumptions rather than a measurement.
/// </param>
/// <param name="Launched">Real ions of it that were seeded.</param>
/// <param name="Collected">Real ions of it that reached the detector.</param>
/// <param name="Remaining">Real ions of it still in the tracked region.</param>
/// <param name="Losses">Every other way it left, by named surface.</param>
/// <param name="Transmission">Collected over what left the region at all.</param>
/// <param name="MeanTransitUs">Its mean transit, or absent where none of it arrived.</param>
/// <param name="CentroidMm">Where it ended up along the axis.</param>
/// <param name="StableStepUs">
/// The step this population alone would have taken. A mixture runs at the shortest, so this is
/// how a reader sees what sharing one cost.
/// </param>
public sealed record MixtureSpeciesJson(
    string Name,
    double MobilitySi,
    bool MobilityDerived,
    double Launched,
    double Collected,
    double Remaining,
    IReadOnlyList<LossChannel> Losses,
    double Transmission,
    double? MeanTransitUs,
    double CentroidMm,
    double StableStepUs);

/// <summary>What a run of several ion populations produced.</summary>
/// <param name="Species">Each population, in the order declared.</param>
/// <param name="Steps">Shared steps taken.</param>
/// <param name="StepUs">The shared step, which is the shortest any population needed.</param>
/// <param name="StepSetBy">Which population needed it.</param>
/// <param name="SelfFieldSolves">
/// How many times the potential of their total charge was solved, or absent where the document
/// did not ask for one - in which case the populations did not interact at all, and the run is
/// what several separate ones would give.
/// </param>
/// <param name="PeakSelfPotentialVolts">The largest such potential over the run.</param>
/// <param name="NetChargeSi">
/// The net charge that potential was raised by, signed - so a mixture of opposite polarities
/// holding a great deal of charge and raising almost no field says so.
/// </param>
public sealed record MixtureJson(
    IReadOnlyList<MixtureSpeciesJson> Species,
    int Steps,
    double StepUs,
    string StepSetBy,
    int? SelfFieldSolves,
    double? PeakSelfPotentialVolts,
    double? NetChargeSi);

/// <summary>One phase of a sequenced run, as it is reported.</summary>
/// <param name="Name">What the phase is for.</param>
/// <param name="Mode">The transport mode it ran in.</param>
/// <param name="EndsAtUs">When it ended, on the instrument's timeline.</param>
/// <param name="Population">How many real ions the packet held when it ended.</param>
/// <param name="Trajectories">
/// How many trajectories carried them, or zero in a diffusive phase where there are none.
/// </param>
/// <param name="CentroidMm">Where the packet was when the phase ended.</param>
/// <param name="SpreadMm">
/// How wide it was, as one standard deviation of position along each axis in millimetres.
/// Absent where there is nothing to take a width over - a single trajectory, or a phase that
/// lost everything - because zero is a real width.
/// </param>
/// <param name="Converted">Whether the packet was converted into this description.</param>
/// <param name="Assemblies">
/// In a diffusive phase, how many times the density solver assembled its operator: once for
/// a field that held, once per step for one that changed. Zero for a trajectory phase.
/// </param>
/// <param name="WellRebuilds">
/// How many of those assemblies computed the ponderomotive well over the whole grid rather
/// than reusing the one before: one where the ramp moved only DC, one per assembly where it
/// moved an RF amplitude, zero where nothing is driven. The expensive half of a cycle
/// average, so this is what a ramp through a driven geometry actually costs.
/// </param>
/// <param name="SelfFieldSolves">
/// In a diffusive phase asked for a mean field, how many times the density's own charge was
/// solved as a potential; absent where none was asked for. Absent rather than zero, because
/// zero is a real answer - a field that never needed refreshing - and the two must not print
/// the same.
/// </param>
/// <param name="PeakSelfPotentialVolts">
/// The largest self-potential anywhere on the grid during the phase, absent on the same terms.
/// Per phase, because a hold and the pulse after it carry the same ions at very different
/// densities.
/// </param>
public sealed record SequencePhaseJson(
    string Name,
    string Mode,
    double EndsAtUs,
    double Population,
    int Trajectories,
    IReadOnlyList<double> CentroidMm,
    IReadOnlyList<double>? SpreadMm,
    bool Converted,
    int Assemblies,
    int WellRebuilds,
    int? SelfFieldSolves = null,
    double? PeakSelfPotentialVolts = null);

/// <summary>What a run across a changing transport mode did (SEQ-1).</summary>
/// <param name="Phases">Each phase, in order.</param>
/// <param name="Conversions">How many boundaries the packet was converted across.</param>
/// <param name="ArrivedIons">Real ions that reached the detector, over every phase.</param>
/// <param name="Losses">Every other way ions left, by named surface, in real ions.</param>
/// <remarks>
/// In real ions rather than trajectory counts, because a conversion re-samples: on the
/// far side of one the trajectory count is a numerical choice while the population is
/// what carries across.
/// </remarks>
public sealed record SequenceJson(
    IReadOnlyList<SequencePhaseJson> Phases,
    int Conversions,
    double ArrivedIons,
    IReadOnlyList<WeightedLoss> Losses)
{
    /// <summary>
    /// When ions reached the detector, averaged over every diffusive leg's arrivals and
    /// placed on the instrument's clock, in microseconds. Absent when nothing arrived.
    /// </summary>
    /// <remarks>
    /// For a mobility analyser this is the elution time, and the number a spectrum is
    /// calibrated on. It is the diffusive legs' arrivals only: a trajectory leg counts its
    /// arrivals but records no instants for them here.
    /// </remarks>
    public double? MeanArrivalUs { get; init; }

    /// <summary>One standard deviation of the arrival instants, in microseconds.</summary>
    public double? ArrivalSpreadUs { get; init; }
}

/// <summary>What a diffusive run produced, on the wire.</summary>
/// <remarks>
/// TRN-2: this mode emits a time-resolved density rather than trajectories, so a
/// result has no flight time and no final position. What it has instead is where
/// the ions went and when they got there, which is what a Class S figure is made
/// of - and reporting an invented flight time here would be reporting a quantity
/// the model never computed.
/// </remarks>
public sealed record DiffusionJson
{
    /// <summary>Ions in the initial density.</summary>
    public required double Launched { get; init; }

    /// <summary>Ions that reached the collecting boundary.</summary>
    public required double Collected { get; init; }

    /// <summary>Ions still in the domain when the run ended.</summary>
    public required double Remaining { get; init; }

    /// <summary>Where the rest went, by boundary, largest first (ACC-5).</summary>
    public required IReadOnlyList<LossChannel> Losses { get; init; }

    /// <summary>Fraction collected, of those that left.</summary>
    public required double Transmission { get; init; }

    /// <summary>Mean time to reach the collector, in microseconds.</summary>
    public double? MeanTransitUs { get; init; }

    /// <summary>Spread of transit times, in microseconds.</summary>
    public double? TransitSpreadUs { get; init; }

    /// <summary>Mobility used, in square metres per volt-second.</summary>
    public required double MobilitySi { get; init; }

    /// <summary>Whether that mobility was derived rather than declared (TRN-1).</summary>
    public required bool MobilityDerived { get; init; }

    /// <summary>
    /// Bulk speed of the neutral gas, in metres per second, or null where it is
    /// standing still.
    /// </summary>
    /// <remarks>
    /// GAS-1. Reported as a number as well as in a warning because a study over
    /// pressure or flow needs to read it back, and absent rather than zero because
    /// zero is a real answer - a stationary gas - and a reader cannot tell that from
    /// a field that was never consulted.
    /// </remarks>
    public double? GasSpeedSi { get; init; }

    /// <summary>
    /// How many times the density's own self-potential was solved, or null where space charge
    /// was not modelled.
    /// </summary>
    /// <remarks>
    /// Absent rather than zero, which is the rule the rest of this surface follows: zero is a
    /// real answer - a run whose density never moved enough to need a second solve - and a
    /// reader cannot tell that from a run that never modelled charge at all.
    /// </remarks>
    public int? SelfFieldSolves { get; init; }

    /// <summary>
    /// The largest self-potential anywhere in the tracked region, in volts, or null where
    /// space charge was not modelled.
    /// </summary>
    public double? PeakSelfPotentialVolts { get; init; }

    /// <summary>Grid the density was tracked on, columns then rows.</summary>
    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>Time steps taken.</summary>
    public required int Steps { get; init; }

    /// <summary>Simulated time, in microseconds.</summary>
    public required double ElapsedUs { get; init; }

    /// <summary>Final density spread, x then y, in millimetres.</summary>
    /// <remarks>
    /// What replaces a packet size when there are no particles to take a variance
    /// over. For a funnel the y figure is the radial compression the device exists
    /// to produce.
    /// </remarks>
    public required IReadOnlyList<double> SpreadMm { get; init; }
}

/// <summary>The outcome of a run.</summary>
public sealed record RunOutcome
{
    /// <summary>The manifest that determines this run (PRJ-3).</summary>
    public required RunManifest Manifest { get; init; }

    /// <summary>Flight time, as the GRD-1 envelope.</summary>
    public required MeasuredJson FlightTime { get; init; }

    /// <summary>Why the integration stopped.</summary>
    public required string Outcome { get; init; }

    /// <summary>Whether the engine computed what it was asked to.</summary>
    /// <remarks>
    /// <para>
    /// <b>Not whether the instrument performed.</b> An ion that struck an electrode, or
    /// that was still held when the declared flight time elapsed, is a result; the
    /// transmission, the itemised losses and <c>confined</c> say what became of it. Only an
    /// integrator that gave up - a step-size underflow, an exhausted step budget - leaves
    /// numbers that stop part way with no bound on how wrong they are.
    /// </para>
    /// <para>
    /// Carried here rather than derived from <see cref="Outcome"/> at the surface, because
    /// deriving it there is what produced a list of the outcome names known when the line
    /// was written. That list had to be widened twice, and still called six of the
    /// thirty-seven shipped examples failures.
    /// </para>
    /// </remarks>
    public required bool Completed { get; init; }

    /// <summary>Where the ion ended, in millimetres.</summary>
    public required IReadOnlyList<double> FinalPositionMm { get; init; }

    /// <summary>Largest relative departure of total energy over the flight (ACC-4).</summary>
    public required double MaximumRelativeEnergyDrift { get; init; }

    /// <summary>
    /// The dimensionless numbers that say whether this mode applies, or null in
    /// vacuum where the question does not arise.
    /// </summary>
    public RegimeJson? Regime { get; init; }

    /// <summary>What a diffusive run produced, or null for a trajectory run.</summary>
    public DiffusionJson? Diffusion { get; init; }

    /// <summary>
    /// What a run of several ion populations produced, or null where the model declared one ion.
    /// </summary>
    /// <remarks>
    /// Mutually exclusive with <see cref="Diffusion"/>, which reports a single density: a
    /// mixture has no single mobility, no single transit and no single density, so filling that
    /// block from the first population would report one species under the name of the run.
    /// </remarks>
    public MixtureJson? Mixture { get; init; }

    /// <summary>
    /// The per-phase result, when the run crossed a transport-mode boundary (SEQ-1).
    /// </summary>
    public SequenceJson? Sequence { get; init; }

    /// <summary>Accepted integrator steps, finest tolerance.</summary>
    public required int AcceptedSteps { get; init; }

    /// <summary>Distance advanced analytically through field-free regions, in metres.</summary>
    public required double AnalyticDriftDistanceM { get; init; }

    /// <summary>Files written by this run, relative to the project root.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>
    /// Whether this run produced a single arrival time for an ion, which is what
    /// <see cref="FlightTime"/> reports and what a reader may quote.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Required, and it is a question rather than a mode.</b> False for a density, which has
    /// no ions and so no arrival; false for a sequenced run, which ends when its sequence ends
    /// rather than when anything arrives; false for a mixture, for both reasons at once. True
    /// only where one ion was flown to a detector.
    /// </para>
    /// <para>
    /// This exists because the printer kept asking the wrong question. It was written as
    /// "is this diffusive", then "is this diffusive or sequenced", each time by adding the mode
    /// that had just been caught printing <c>flight time NaN +/- NaN</c> - and a mixture would
    /// have been the fourth. A list of the modes known when it was written is not the question
    /// being asked, and it stops being equivalent the moment a mode is added. Required rather
    /// than defaulted so that a fifth kind of run fails to compile until somebody decides which
    /// it is, which is the same reason <c>TrajectoryOutcome.Completed</c> throws on an
    /// unrecognised outcome.
    /// </para>
    /// </remarks>
    public required bool HasFlightTime { get; init; }

    /// <summary>What the cloud did, when the model launches one.</summary>
    public EnsembleOutcome? Ensemble { get; init; }
}

/// <summary>
/// Validates a model, and runs one.
/// </summary>
/// <remarks>
/// <para>
/// AGT-2: "Every capability reachable from the window is reachable from the CLI
/// and from MCP, through the same command objects." These are those objects. The
/// CLI is a thin argument parser over them; the MCP server and the shell will be
/// too. Nothing here reads the console or writes to it.
/// </para>
/// <para>
/// The result of a run is a <see cref="MeasuredJson"/>, never a bare flight time.
/// The integrator's own <c>TrajectoryResult</c> stops at the Einzel.Transport
/// boundary, and what crosses into a reportable result comes from
/// <see cref="FlightTimeStudy"/> with its convergence evidence attached.
/// </para>
/// </remarks>
public static class RunCommand
{
    /// <summary>Flies the declared cloud and reports what it did.</summary>
    private static EnsembleOutcome? Ensemble(
        CompiledModel model, IReadOnlyList<ValidityWarning> fieldWarnings)
    {
        var flight = FiguresOfMerit.FlyCloud(model);

        // Null when fewer than two ions arrived: an arrival peak needs two points to
        // have a width. The flight is still a result, and this used to return null
        // outright - so a packet that lost everything reported no ensemble at all,
        // and its itemised losses went with it. That is backwards. ACC-5's whole
        // subject is transmission by named surface, and the reading most worth
        // having is the one where the transmission is zero.
        var peak = flight.Peak;

        var launched = model.Cloud.Ions;
        var arrived = flight.Arrived.Count;

        var transmission = new Measured(
            Quantity.Si(launched > 0 ? (double)arrived / launched : 0.0, default),
            UncertaintyInterval.Symmetric(
                Quantity.Si(launched > 0 ? (double)arrived / launched : 0.0, default),
                Quantity.Si(0.0, default),
                1.0),
            new Evidence.Ensemble(launched, true),
            []);

        // Three ions to place two second moments and their covariance. Below that
        // the area is not underdetermined so much as meaningless, and reporting a
        // zero would read as a perfectly collimated beam.
        var packet = flight.Arrived.Count >= 3
            ? Emittance.FromPacket(flight.Arrived)
            : ((Emittance Wider, Emittance Narrower)?)null;

        var turnAround = FiguresOfMerit.Evaluator("turnAroundTime")(model) ?? 0.0;

        var species = IonSpecies.FromModel(model);

        // The acceleration potential is a declaration of the flight energy, and for
        // a beam it is the right scale. A pulsed extraction trap declares none -
        // its packet starts at rest and the instrument does the accelerating - so
        // the scale has to be measured instead, from the energy the ions actually
        // arrived with. Measured only when nothing was declared, so no existing
        // result moves and the estimate stays independent of transmission losses
        // wherever a source states its own energy.
        var flightPotential = model.AccelerationPotentialSi != 0.0
            ? model.AccelerationPotentialSi
            : MeanArrivalPotential(flight.Arrived, species);

        // The flight time matters to the estimate, because the dominant mechanism
        // is the packet expanding under its own charge and that goes on for as long
        // as the flight does. The peak's own position is the packet's flight time;
        // a packet that never arrived has none, and the estimate falls back to its
        // asymptotic bound and says so.
        var flightTime = peak is { Arrived: > 0 } ? peak.MeanSeconds : 0.0;

        var charge = SpaceCharge.Estimate(model.Cloud, species, flightPotential, flightTime);

        var limit = charge.EffectiveRadiusM > 0.0 && flightPotential != 0.0
            ? SpaceCharge.PopulationLimit(
                Quantity.Si(charge.EffectiveRadiusM, Quantity.From(1.0, "m").Dimension),
                species,
                flightPotential,
                AccuracyBudget,
                flightTime)
            : 0.0;

        var warnings = (IReadOnlyList<ValidityWarning>)
            [
                .. fieldWarnings,
                .. SpaceChargeWarnings(
                    charge, limit, model,
                    (double)charge.Population / Math.Max(1, model.Cloud.Ions)),
            ];

        var held = flight.Remaining.Count;

        var confinedFraction = launched > 0 ? (double)held / launched : 0.0;
        var confinedQuantity = Quantity.Number(confinedFraction);

        var confined = new Measured(
            confinedQuantity,
            UncertaintyInterval.Symmetric(
                confinedQuantity,
                Quantity.Number(
                    Math.Max(
                        Math.Sqrt(confinedFraction * (1.0 - confinedFraction) / Math.Max(1, launched)),
                        1.0 / Math.Max(1, launched))),
                0.68),
            new Evidence.Ensemble(launched, launched >= 100),
            []);

        return new EnsembleOutcome
        {
            Launched = launched,
            Arrived = arrived,
            Confined = MeasuredJson.From(confined, "1"),
            Collisions = flight.Collisions,
            ScatteredIons = flight.ScatteredIons,

            // Counted, not read off the peak, so a transmission of zero is a
            // measurement rather than a missing peak.
            Transmission = MeasuredJson.From(
                Carry(peak is null ? transmission : peak.Transmission(), warnings), "1"),

            Losses = flight.Losses,
            Events = [.. flight.Events.Select(e => new IonEventJson(e.Ion, e.Outcome, e.Surface, e.TimeSeconds * 1e6, e.Position.X * 1e3, e.Position.Y * 1e3, e.Position.Z * 1e3))],

            // Absent rather than zero where there is no peak to measure. Zero is a
            // real width and a reader cannot tell the two apart if both print as
            // zero, which is the policy the rest of this surface already follows.
            CentralWidthNs = peak is null ? null : peak.CentralWidthSeconds(0.5) * 1e9,
            GaussianFwhmNs = peak is null ? null : peak.GaussianEquivalentFwhmSeconds * 1e9,
            Skewness = peak?.Skewness,
            TurnAroundFwhmNs = turnAround * 1e9,
            ResolvingPower = peak is null
                ? null
                : MeasuredJson.From(Carry(peak.ResolvingPower(), warnings), "1"),
            Population = charge.Population,
            SpaceChargeTimingFraction = charge.TimingFraction,
            SpaceChargePopulationLimit = limit,

            // A micrometre is a millimetre-milliradian, since a radian is
            // dimensionless. The engine carries metres and the report carries the
            // unit the field is quoted in.
            EmittanceMmMrad = packet?.Wider.MillimetreMilliradian,
            EmittanceMinorMmMrad = packet?.Narrower.MillimetreMilliradian,
            NormalisedEmittanceMmMrad = packet?.Wider.NormalisedM * 1e6,
            PacketRadiusMm = packet?.Wider.RmsSizeM * 1e3,

            // A packet with no area has no ellipse and so no orientation. Left
            // null rather than passed through, because the alternative is a
            // not-a-number in a document format that cannot represent one.
            PacketTwissAlpha = packet is { Wider.GeometricM: > 0.0 } p ? p.Wider.TwissAlpha : null,
        };
    }

    /// <summary>
    /// The potential the arriving packet flew at, in volts, from its kinetic energy.
    /// </summary>
    /// <remarks>
    /// Zero when nothing arrived, which leaves the space-charge fractions
    /// unreported rather than divided by zero.
    /// </remarks>
    private static double MeanArrivalPotential(IReadOnlyList<PhaseState> arrived, IonSpecies species)
    {
        if (arrived.Count == 0)
        {
            return 0.0;
        }

        var sum = 0.0;

        foreach (var state in arrived)
        {
            sum += state.Velocity.LengthSquared;
        }

        // Mean kinetic energy over charge, which is the potential an equivalent
        // beam source would have declared.
        return 0.5 * species.MassSi * (sum / arrived.Count) / Math.Abs(species.ChargeSi);
    }

    /// <summary>The flight-time budget ACC-1 sets, as a fraction.</summary>
    private const double AccuracyBudget = 1e-6;

    /// <summary>
    /// Says so when the packet is dense enough that ignoring its own charge
    /// changes the answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The engine flies every ion through a field that does not know about the
    /// others. For a sparse beam that is exactly right; for a dense packet it is
    /// wrong, and wrong invisibly - the answer looks the same. Spec section 7 asks
    /// for the governing dimensionless number to be computed and a
    /// non-suppressible warning raised when the model is outside its validity,
    /// and this is that.
    /// </para>
    /// <para>
    /// Two tiers, because they mean different things. Past the accuracy budget the
    /// flight time is wrong by more than it claims to be right by, which is a
    /// validity violation. Past a tenth of it the number still stands but the
    /// headroom is nearly gone, and someone about to raise the ion count should
    /// hear that before they do rather than after.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Reports the grid's cell against the mean spacing between macroparticles, and
    /// warns where that ratio puts the answer outside the band it was measured in.
    /// </summary>
    /// <remarks>
    /// <para>
    /// REG-2's rule, applied to a different quantity: reported whether or not it
    /// crosses a threshold, because a reader who sees the ratio knows the run was
    /// checked and one who sees nothing cannot tell that from its not having been.
    /// </para>
    /// <para>
    /// <b>The accuracy here has an optimum rather than a floor</b>, which is the part
    /// that needs saying out loud. Against the direct sum taken to its own point
    /// limit: 3.68 cells per spacing gives -15.1%, 1.84 gives -4.2%, 0.92 gives
    /// +0.08%, 0.46 gives +4.4%. So the intuitive move - raise the node count for a
    /// better answer - makes it worse past the match, and does so silently. Confirmed
    /// as a sampling artefact rather than a resolution one by holding the cell fixed
    /// and raising the macroparticle count: at 128 nodes the error falls 4.42% to
    /// 1.55% to 0.93% as macroparticles per cell go 0.012 to 0.049 to 0.195.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ValidityWarning> GridResolutionWarnings(
        CompiledSpaceChargeGrid? grid, int macroparticles)
    {
        if (grid is null || macroparticles < 2)
        {
            return [];
        }

        var matched = 2.0 * grid.Padding * Math.Cbrt(macroparticles);
        var ratio = matched / grid.Nodes;

        var matchedNodes = (int)Math.Pow(2.0, Math.Max(3.0, Math.Ceiling(Math.Log2(matched))));

        // NEVER PAST WHAT A SOLVE CAN HOLD. The matched count can be past it - padding 4 and
        // 40,000 trajectories match at 512 - and validation refuses a grid over 256 across, so
        // advice naming the matched count walked straight into a refusal. What cannot come
        // from nodes has to come from the other two numbers in the ratio.
        var advice = Math.Min(matchedNodes, Core.Numerics.VolumeMesh.MostNodesAcrossACube);
        var unreachable = advice < matchedNodes
            ? $" - the most a volume solve can hold, where matching the packet would need "
                + $"{matchedNodes}, so the rest has to come from fewer trajectories or a smaller padding"
            : string.Empty;

        var band = ratio is >= 0.7 and <= 2.0
            ? "which is the band this method was measured in"
            : ratio < 0.7
                ? $"which is finer than the packet has structure. Below about 0.7 the cells hold "
                    + "less than one macroparticle each, so the deposit resolves lumps rather than "
                    + $"a density and the mutual force comes out too strong. Try {advice} nodes, or "
                    + "raise \"ions\""
                : $"which over-smooths the packet: the gathered force is too weak and the packet "
                    + $"comes out narrow. Try {advice} nodes{unreachable}";

        return
        [
            new ValidityWarning(
                "spacecharge.grid-resolution",
                $"the grid's cell is {ratio:F2} of the mean spacing between macroparticles, {band}. "
                + "Accuracy here has an optimum at about one rather than improving with refinement, "
                + "so this number is reported whether or not it is a problem",
                ratio is >= 0.7 and <= 2.0
                    ? WarningSeverity.Provenance
                    : WarningSeverity.ValidityViolation),
        ];
    }

    /// <summary>
    /// The direct sum softens the force between two macroparticles closer than the mean
    /// spacing, and the mean spacing is set by the packet's RMS radius - so a packet that is
    /// long in one direction and thin in the others is softened at a scale larger than its
    /// thin dimension, and the mutual force across that dimension is nearly switched off.
    /// </summary>
    /// <remarks>
    /// Found on a linear ion trap's cloud: 40 macroparticles standing for 4,000 ions along
    /// 10 mm of axis and 0.05 mm across it. The RMS radius is 5.8 mm, the softening 1.7 mm,
    /// thirty-four times the transverse size, and the space-charge scan came back identical
    /// to the one without space charge, to the last digit. Nothing reported it. REG-2's rule
    /// - a resolution limit is reported whether or not it is crossed - applied to the third
    /// such limit in this engine, after the coarsening guard and the particle-in-cell cell.
    /// </remarks>
    /// <summary>
    /// A pushed packet in a gas: each macroparticle collides as one ion and carries
    /// the momentum of many, so the packet relaxes to the ion's velocity distribution
    /// sampled at the macroparticle count. Reported whether or not it matters (REG-2),
    /// because a cooled, space-charge-limited cloud is exactly what such a run is for
    /// and its fluctuations are those of the smaller sample.
    /// </summary>
    /// <summary>
    /// A half-space's ramp does not stop at the depth the document declares it to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// `halfSpaceUniform` takes a cap potential and a turning depth, uses them to compute a
    /// gradient, and then extends that gradient for ever - so an ion with more axial energy
    /// than the cap turns round *behind* the plate the document describes, in a region the
    /// model does not claim to represent. The arithmetic is right for a ramp that never
    /// ends, the flight time matches its own closed form, and nothing looks wrong.
    /// </para>
    /// <para>
    /// An agent tuning the shipped reflectron was told to search 3600-3900 V against a
    /// 4000 V beam. Every point in that window turns past the back plate - 5.6 mm past it at
    /// 3600 V - and it took the agent a deliberate investigation to notice, because the
    /// numbers are self-consistent. This is the one thing the model could have said.
    /// </para>
    /// </remarks>
    /// <summary>What this run's numbers rest on, where that is not the author's own choice.</summary>
    /// <param name="model">The compiled model, for its parameter surface.</param>
    /// <returns>One warning naming the guessed parameters, one naming the fitted, or none.</returns>
    /// <remarks>
    /// <para>
    /// REG-2's rule applied to provenance: reported whether or not anything is wrong with it,
    /// because a reader who sees which numbers are guesses knows the run was checked, and one
    /// who sees nothing cannot tell that from its not having been checked.
    /// </para>
    /// <para>
    /// <see cref="WarningSeverity.Provenance"/> rather than a validity violation, and the
    /// severity's name is the argument: a guessed dimension does not make a result invalid,
    /// it makes it a result about a geometry somebody assumed. Published and drawn parameters
    /// are deliberately silent - a run resting on cited values is the ordinary case, and a
    /// line on every model is noise rather than information.
    /// </para>
    /// </remarks>
    private static List<ValidityWarning> ProvenanceWarnings(CompiledModel model)
    {
        var warnings = new List<ValidityWarning>();

        foreach (var (provenance, code, what) in new[]
        {
            (ParameterProvenance.Guess, "parameters.guessed",
                "are guesses: the result is about a geometry somebody assumed, and moves if a "
                + "better value turns up"),
            (ParameterProvenance.Fitted, "parameters.fitted",
                "were fitted rather than declared, so the model agrees with whatever they were "
                + "fitted against by construction and that agreement is not evidence"),
        })
        {
            var named = model.Parameters.Parameters.Values
                .Where(p => p.Provenance == provenance)
                .Select(p => p.Name)
                .Order(StringComparer.Ordinal)
                .ToList();

            if (named.Count == 0)
            {
                continue;
            }

            warnings.Add(new ValidityWarning(
                code,
                $"{named.Count} of {model.Parameters.Parameters.Count} declared parameters "
                + $"{what}: {string.Join(", ", named)}",
                WarningSeverity.Provenance));
        }

        return warnings;
    }

    /// <summary>
    /// Whether any ion this run launches turns round behind the plate a half-space declares.
    /// </summary>
    /// <param name="model">The compiled model, for its fields and its launch energy.</param>
    /// <returns>One warning per half-space the beam overshoots, and none otherwise.</returns>
    /// <remarks>
    /// <para>
    /// A <c>halfSpaceUniform</c> field is a ramp with no far side: the cap potential is what
    /// the model says the plate holds at the declared turning depth, and the arithmetic
    /// continues past it. An ion carrying more energy along the normal than the cap therefore
    /// turns round <em>behind</em> the plate, self-consistently, in a region the document does
    /// not describe - and the run reports a flight time to full precision either way.
    /// </para>
    /// <para>
    /// Found by an agent that lowered a cap below the beam energy while exploring a
    /// reflectron's focus. Every number stayed plausible; the ion was turning 5.6 mm inside
    /// the metal. This is the ACC-4 counterpart of the region-boundary warning, and
    /// <see cref="WarningSeverity.Qualified"/> rather than a violation because the model is
    /// arithmetically what its author wrote - what is wrong is the reading of it.
    /// </para>
    /// <para>
    /// A cloud is measured at three sigma of its declared energy spread rather than at
    /// nominal, because the tail is the population that overshoots first and a warning that
    /// waits for the centre of the distribution to cross would stay silent through the whole
    /// range where the effect is a selective loss rather than a total one.
    /// </para>
    /// </remarks>
    private static List<ValidityWarning> HalfSpaceDepthWarnings(CompiledModel model)
    {
        var warnings = new List<ValidityWarning>();

        // Three sigma of the declared supply ripple, since the tail is what crosses first.
        // Zero for everything that declares no energy spread, which is most models.
        var tail = 3.0 * model.Cloud.EnergyFractionSpread;
        var fastest = (1.0 + model.EnergyFraction) * (1.0 + tail);

        foreach (var element in model.Fields)
        {
            if (element.Kind != CompiledFieldKind.HalfSpaceUniform
                || !(element.TurningDepthSi > 0.0)
                || !(element.PotentialGradientSi > 0.0))
            {
                continue;
            }

            // The energy the ion carries into the region, along the normal. A launch at an
            // angle carries less - v_n = v cos(theta), so the energy goes as cos squared -
            // and that is what decides how deep it goes rather than the beam energy itself.
            var along = Math.Abs(Core.Geometry.Vec3.Dot(
                model.SourceDirection.Normalized(), element.InwardNormal));
            var axialVolts = Math.Abs(model.AccelerationPotentialSi) * fastest * along * along;
            var capVolts = element.PotentialGradientSi * element.TurningDepthSi;
            var reached = axialVolts / element.PotentialGradientSi;

            // A MODEL PLACED DELIBERATELY AT THE BOUNDARY MUST NOT TRIP ON ROUND-OFF. The
            // scaffolded reflectron sets the cap equal to the acceleration potential and
            // says so in its own description - the ion turns exactly at the declared depth -
            // and the gradient is a division the model format performs, so cap over depth
            // times depth is the same number only to within an ulp or two. Requiring the
            // overshoot to clear a millionth of the depth (50 nm on a 50 mm mirror) makes
            // the test independent of which way that rounding fell, and nothing physical is
            // lost: an ion turning 50 nm past a plate is not the finding this exists to
            // report.
            if (reached - element.TurningDepthSi <= 1.0e-6 * element.TurningDepthSi)
            {
                continue;
            }

            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            var which = tail > 0.0 ? "the fastest ion this run launches turns" : "this ion turns";
            var past = string.Create(invariant, $"{(reached - element.TurningDepthSi) * 1e3:F2}");
            var declared = string.Create(invariant, $"{element.TurningDepthSi * 1e3:F2}");
            var carried = string.Create(invariant, $"{axialVolts:F1}");
            var cap = string.Create(invariant, $"{capVolts:F1}");
            var basis = tail > 0.0
                ? string.Create(invariant, $" at three sigma of the declared energy spread")
                : string.Empty;

            warnings.Add(new ValidityWarning(
                "field.beyond-declared-depth",
                $"{which} {past} mm beyond the {declared} mm turning depth the half-space "
                + $"declares, because it carries {carried} V along the normal{basis} against a "
                + $"cap of {cap} V. The ramp is extrapolated past the plate rather than stopped "
                + "at it, so the arithmetic is self-consistent and describes a region the model "
                + "does not claim. Raise the cap potential, lower the beam energy, or bound the "
                + "element with a region",
                WarningSeverity.Qualified));
        }

        return warnings;
    }

    private static IReadOnlyList<ValidityWarning> MacroparticleCollisionWarnings(CompiledModel model, double weight)
    {
        if (!model.Gas.IsPresent)
        {
            return [];
        }

        return
        [
            new ValidityWarning(
                "spacecharge.macroparticle-collisions",
                $"the packet collides with the gas: each of {model.Cloud.Ions} macroparticles scatters as one "
                + $"ion and carries the charge and mass of {weight:F1}, so the drag and diffusion are the ion's "
                + $"and the collisional fluctuations are those of {model.Cloud.Ions} samples rather than "
                + $"{model.Cloud.Population ?? model.Cloud.Ions}. The step is cut to land on every collision in the "
                + "packet, so a dense gas costs steps in proportion to the packet's total collision rate",
                WarningSeverity.Provenance),
        ];
    }

    private static IReadOnlyList<ValidityWarning> SofteningWarnings(CompiledSpaceChargeGrid? grid, CompiledModel model)
    {
        if (grid is not null || model.Cloud.Ions < 2)
        {
            return [];
        }

        var transverse = model.Cloud.TransverseSpreadM;
        var longitudinal = model.Cloud.LongitudinalSpreadM;
        var extents = new[] { transverse, longitudinal }.Where(v => v > 0.0).ToArray();
        if (extents.Length == 0)
        {
            return [];
        }

        // The same rule the flight uses, from the declared spreads rather than the drawn
        // packet, so the warning and the run cannot disagree about which softening applied.
        var softening = Transport.Interaction.CoulombInteraction.SpacingSoftening(
            transverse, transverse, longitudinal, model.Cloud.Ions);
        var rms = Math.Sqrt((2.0 * transverse * transverse) + (longitudinal * longitudinal));
        var smallest = extents.Min();
        var ratio = softening / smallest;
        var needed = (int)Math.Ceiling(model.Cloud.Ions * Math.Pow(ratio, 3));

        return
        [
            new ValidityWarning(
                "spacecharge.softening",
                $"the mutual force is softened below {softening * 1e3:F3} mm, the mean spacing of "
                + $"{model.Cloud.Ions} macroparticles in a packet of RMS radius {rms * 1e3:F2} mm and extents "
                + $"{transverse * 1e3:F3} by {transverse * 1e3:F3} by {longitudinal * 1e3:F3} mm, against a "
                + $"smallest declared extent of {smallest * 1e3:F3} mm"
                + (ratio > 1.0
                    ? $". The softening exceeds the packet's thin dimension {ratio:F1}-fold, so the force across it is "
                      + $"suppressed and the packet's own charge is largely not being felt; about {needed} macroparticles "
                      + "would bring the spacing below that dimension, or a grid method resolves it at the cell"
                    : ", inside it, so the force between neighbours is Coulombic at the scale the packet has"),
                ratio > 1.0 ? WarningSeverity.ValidityViolation : WarningSeverity.Provenance),
        ];
    }

    private static IReadOnlyList<ValidityWarning> SpaceChargeWarnings(
        SpaceChargeEstimate charge, double limit, CompiledModel model, double weight)
    {
        if (model.ModelsSpaceCharge)
        {
            // The estimate exists to say "this matters and the engine is not doing
            // it". Here the engine is doing it, so repeating the warning would be
            // false - and staying silent would leave a reader unable to tell a run
            // that modelled the mutual force from one that found it negligible.
            //
            // What replaces it is provenance: the method used, and the fact that
            // the trajectories are macroparticles rather than ions wherever the
            // population exceeds them. A reader who does not know that is reading a
            // sampled packet as a real one.
            //
            // The two methods approximate different things and each says which,
            // because "space charge was modelled" is not enough to read a number by:
            // the direct sum softens at short range, and the grid method smooths at
            // the cell and stands the packet in an earthed box.
            var grid = model.SpaceChargeGrid;

            var method = grid is null
                ? "by direct summation over every pair"
                : $"on a grid of {grid.Nodes} nodes across a box {grid.Padding:F1} RMS radii wide, "
                    + $"re-solved when the packet's RMS radius moves {grid.RefreshTolerance:P0}";

            var approximation = grid is null
                ? "the force between two of them closer together than the mean macroparticle "
                    + "spacing is softened rather than Coulombic"
                : "the mutual force is smoothed at the scale of one cell, and the packet is solved "
                    + "in an earthed box centred on itself rather than in free space";

            return
            [
                new ValidityWarning(
                    "spacecharge.modelled",
                    $"the ions in this packet push on each other, {method}. "
                    + $"The screening estimate for the same packet is "
                    + $"{charge.TimingFraction / 1e-6:F2} ppm and is reported alongside as a cross-check, "
                    + $"not as the answer. Each trajectory carries the charge and the mass of "
                    + $"{weight:F1} real ions, and {approximation}",
                    WarningSeverity.Provenance),

                .. GridResolutionWarnings(grid, model.Cloud.Ions),
                .. SofteningWarnings(grid, model),
                .. MacroparticleCollisionWarnings(model, weight),
            ];
        }

        if (charge.IsPointLike)
        {
            return
            [
                new ValidityWarning(
                    "spacecharge.point-packet",
                    $"{charge.Population} ions are declared at a single point: the cloud has no spatial "
                    + "extent, so its self-field is unbounded and no estimate of space charge is possible. "
                    + "Give the cloud a transverse or longitudinal spread, or set a population of 1",
                    WarningSeverity.ValidityViolation),
            ];
        }

        if (charge.TimingFraction is not > 0.0)
        {
            return [];
        }

        if (charge.TimingFraction > AccuracyBudget)
        {
            return
            [
                new ValidityWarning(
                    "spacecharge.ignored",
                    $"a packet of {charge.Population} ions in {charge.EffectiveRadiusM * 1e3:F3} mm carries "
                    + $"{charge.PotentialVolts * 1e3:F1} mV across itself, which is a flight-time error of "
                    + $"{charge.TimingFraction / 1e-6:F1} ppm against the 1 ppm budget. The ions here do not "
                    + $"push on each other: this run does not model space charge. This packet holds about "
                    + $"{Population(limit)} within budget, and the estimate is an upper bound - a mirror at a "
                    + "first-order energy focus measurably suppresses it, because the push correlates position "
                    + "with energy in the sign the mirror corrects",
                    WarningSeverity.ValidityViolation),
            ];
        }

        if (charge.TimingFraction > 0.1 * AccuracyBudget)
        {
            return
            [
                new ValidityWarning(
                    "spacecharge.approaching",
                    $"a packet of {charge.Population} ions implies a flight-time error of "
                    + $"{charge.TimingFraction / 1e-6:F2} ppm from its own charge, which this run does not "
                    + $"model. Still inside the 1 ppm budget, but this packet reaches it at about "
                    + $"{Population(limit)}",
                    WarningSeverity.Qualified),
            ];
        }

        return [];
    }

    /// <summary>A population limit, rendered so a limit below one ion does not print as none.</summary>
    /// <remarks>
    /// The corrected estimate puts a dense half-millimetre packet's 1 ppm capacity
    /// in the single figures, and "holds about 0 ions" reads as a defect in the
    /// message rather than as the finding it is.
    /// </remarks>
    private static string Population(double limit) => limit switch
    {
        < 1.0 => $"{limit:F2} ions",
        < 10.0 => $"{limit:F1} ions",
        _ => $"{limit:N0} ions",
    };

    /// <summary>Runs a model in the diffusive mode and writes its result.</summary>
    /// <summary>A run whose phases are not all in one transport description (SEQ-1).</summary>
    /// <remarks>
    /// <para>
    /// A third case beside the two modes rather than a variation of either, for the same
    /// reason the diffusive fork exists: what comes out is not the same result with
    /// fields left empty. A sequenced run ends when its <em>sequence</em> ends, not when
    /// an ion arrives, so there is no single flight time to report - what it has instead
    /// is a per-phase account and, across the whole run, how many ions reached the
    /// detector.
    /// </para>
    /// <para>
    /// The conversions' warnings ride out on the result, which is GRD-2 doing its work at
    /// the seam that matters most here: a reader who takes a number from after a boundary
    /// without knowing the velocities were invented there has been misled by the
    /// platform.
    /// </para>
    /// </remarks>
    /// <summary>Whether a collected population is a transmitted packet or a numerical tail.</summary>
    /// <param name="collected">Real ions that reached the detector.</param>
    /// <param name="launched">Real ions the packet started with.</param>
    /// <returns>Whether there is an arrival time worth reporting.</returns>
    /// <remarks>
    /// <para>
    /// <b>A named function because the number it returns decides whether two numbers get
    /// published.</b> The guard was <c>collected &gt; 0.0</c>, and the shipped TIMS
    /// front-end sequence passed it with 7.74e-245 ions - reporting a mean arrival of
    /// 11,366 us and a spread of 4,024 us for a run whose whole packet was still in the
    /// tunnel. Two plausible numbers in microseconds, and nothing to tell a reader they
    /// describe nothing.
    /// </para>
    /// <para>
    /// <b>Scharfetter-Gummel's flux across a collecting face behind a barrier IS the
    /// Boltzmann factor of that barrier</b>, so a held packet emits values from its first
    /// step: 60 V against a thermal <c>kT/q</c> of 0.026 V is exp(-2300). This project has
    /// been caught by the same tail once already, when an elution onset read as the first
    /// non-empty bin came out during the hold; the answer there was a quantile and the
    /// answer here is a floor.
    /// </para>
    /// <para>
    /// <b>A fraction of the launched population rather than an absolute count</b>, because a
    /// model may launch few ions deliberately and a density is continuous - "less than one
    /// ion" is a real answer for a low transmission, while 1e-250 of one is not an answer at
    /// all. The bar sits enormously above the tail and enormously below anything a reader
    /// would call a transmission.
    /// </para>
    /// </remarks>
    public static bool Eluted(double collected, double launched)
        => collected >= 1e-6 * Math.Max(1.0, launched);

    /// <summary>One phase of a sequenced run, on the wire.</summary>
    /// <param name="phase">What the phase measured.</param>
    /// <returns>The same, in the shape a document carries.</returns>
    /// <remarks>
    /// One conversion, because a checkpoint written mid-run and the result written at the
    /// end have to describe a phase the same way - a reader comparing a killed run's
    /// checkpoint against a finished run's result is the whole point of the checkpoint,
    /// and two spellings of one record is how this project made `run` and `test` disagree
    /// about a flight time twice.
    /// </remarks>
    internal static SequencePhaseJson Phase(PhaseOutcome phase)
    {
        ArgumentNullException.ThrowIfNull(phase);

        return new SequencePhaseJson(
            phase.Name,
            phase.Mode,
            phase.EndsAtSeconds * 1e6,
            phase.Population,
            phase.Trajectories,
            phase.CentroidMm,
            phase.SpreadMm,
            phase.Converted,
            phase.Assemblies,
            phase.WellRebuilds,
            phase.SelfFieldSolves,
            phase.PeakSelfPotentialVolts);
    }

    private static RunOutcome Sequenced(
        CompiledModel model,
        Fields.IElectrostaticField field,
        IReadOnlyList<ValidityWarning> fieldWarnings,
        ValidateOutcome validation,
        ProjectLayout project,
        DateTimeOffset timestampUtc,
        IReadOnlyDictionary<string, string> inputHashes,
        bool exportVtu,
        RunProgress? progress)
    {
        // The one place that knows where the model file is, so the one place that can
        // resolve a declared gas field.
        var resolved = Io.GasFlowImport.Resolve(
            model.Gas, Path.GetDirectoryName(validation.ModelPath) ?? ".");

        // THE CHECKPOINT, IF ANYBODY IS WATCHING. Created before the run rather than after
        // it, which is the whole point: this is the path a TIMS study takes, and it is the
        // one that has failed to finish three times with nothing on disk to say how far it
        // got. Removed on success, so its presence means the run did not finish.
        var watcher = Watch(progress, project, validation);

        var outcome = SequencedRun.Execute(model, field, resolved, watcher);

        watcher?.Discard();

        var manifest = new RunManifest
        {
            ModelHash = validation.ModelHash,
            InputHashes = RunInputs.Checked(inputHashes, project.Root),

            // Which model, as distinct from which content. Without it verify has to find
            // the model by searching for one that still hashes to the recorded value, and
            // editing the model that was actually run then makes its drift disappear -
            // the result re-attaches to whatever else still matches.
            ModelPath = RunManifest.Portable(
                Path.GetRelativePath(project.Root, validation.ModelPath)),
            SchemaVersion = ModelJson.Parse(File.ReadAllText(validation.ModelPath)).SchemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            Solves = Fields.Solved.SolveHash.Of(model),

            // Every mode the run used, in the order the phases used them. One mode
            // recorded here would make a manifest claim to determine a run it does not
            // describe, which is PRJ-3's whole subject.
            TransportMode = string.Join(
                " -> ", outcome.Phases.Select(p => p.Mode).Distinct(StringComparer.Ordinal)),
            ComputePath = EngineBuild.ComputePath,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(validation.ModelPath);
        var manifestPath = Path.Combine(project.Results, $"{stem}.manifest.json");

        File.WriteAllText(manifestPath, manifest.ToJson());

        var last = outcome.Phases[^1];

        // The arrival instants from the diffusive legs, which for a mobility analyser are
        // the spectrum. Written whole as an artifact - one line per step that collected
        // anything - because a spectrum reduced to a mean and a width has lost its shape,
        // and the shape is what a resolving power is read off.
        //
        // RELATIVE TO THE PROJECT ROOT, like the other three paths. This one stored
        // absolute paths, so its manifest named files by where they sat on the machine
        // that wrote them - which a result document PRJ-3 says should determine its run
        // cannot do, and which a report reading a project from anywhere else cannot
        // resolve.
        var artifacts = new List<string> { Path.GetRelativePath(project.Root, manifestPath) };
        var sequenceWarnings = new List<ValidityWarning>();
        double? meanArrivalUs = null;
        double? arrivalSpreadUs = null;

        var collected = outcome.Arrivals.Sum(a => a.Ions);

        // WHAT COUNTS AS HAVING ARRIVED. The guard was `collected > 0.0`, and the shipped
        // front-end sequence passed it with 7.74e-245 ions - reporting a mean arrival of
        // 11366 us and a spread of 4024 us for a run whose whole packet was still in the
        // tunnel at the end. A reader sees two numbers in microseconds and believes them.
        //
        // Scharfetter-Gummel's flux across a collecting face behind a barrier IS the
        // Boltzmann factor of that barrier, so a held packet emits a stream of values
        // hundreds of orders below one ion from its first step. The bar is a fraction of
        // the launched population rather than an absolute count, because a model may launch
        // few ions on purpose and 1e-6 of anything is still enormously above a tail that
        // runs at 1e-245.
        var launched = outcome.Phases.Count > 0 ? outcome.Phases[0].Population : 0.0;
        var meaningful = Eluted(collected, launched);

        if (outcome.Arrivals.Count > 0 && !meaningful)
        {
            // Said rather than left as an absence, because "no mean arrival" and "this
            // model does not elute" are the same fact and only one of them is actionable.
            sequenceWarnings.Add(new ValidityWarning(
                "sequence.nothing-eluted",
                $"{collected:G6} ions reached the detector of {launched:G6} launched, which "
                + "is the collecting face's Boltzmann tail rather than a transmitted packet "
                + "- so there is no arrival time to report and none is. The sequence "
                + "completed; the packet is where the phase table's last row says it is",
                WarningSeverity.ValidityViolation));
        }

        if (outcome.Arrivals.Count > 0 && meaningful)
        {
            var weighted = outcome.Arrivals.Sum(a => a.TimeSeconds * a.Ions) / collected;
            var variance = outcome.Arrivals.Sum(
                a => a.Ions * (a.TimeSeconds - weighted) * (a.TimeSeconds - weighted)) / collected;

            meanArrivalUs = weighted * 1e6;
            arrivalSpreadUs = Math.Sqrt(Math.Max(0.0, variance)) * 1e6;

            var arrivalsPath = Path.Combine(project.Results, $"{stem}.arrivals.csv");
            var lines = new List<string>(outcome.Arrivals.Count + 1) { "time_us,ions" };
            lines.AddRange(outcome.Arrivals.Select(a => string.Create(
                System.Globalization.CultureInfo.InvariantCulture, $"{a.TimeSeconds * 1e6:R},{a.Ions:R}")));
            File.WriteAllLines(arrivalsPath, lines);
            artifacts.Add(Path.GetRelativePath(project.Root, arrivalsPath));
        }

        // EVERY CAVEAT THIS RUN EARNED, in one list, built before anything reads it.
        //
        // THE DENSITY FILE AND THE RESULT DOCUMENT MUST NOT BE ABLE TO DISAGREE about
        // what was wrong with a run, and the first version of the export below let them:
        // it gathered `outcome.Warnings` and `sequenceWarnings` by hand and so carried
        // the FIELD's warnings not at all, while the note saying a sequenced run has no
        // flight time was constructed inline in the result's own envelope a hundred lines
        // further down - below the export, and therefore invisible to it. So the volume
        // came out with an empty caveat block on a run that had earned one, which is
        // precisely GRD-2's subject and precisely the artifact it is most about: a `.vti`
        // is the thing most likely to be opened by somebody who never saw the envelope it
        // came from.
        //
        // One list, two readers. A caveat added in future reaches both by being added
        // here, rather than by somebody remembering there are two places.
        var sequencedNote = new ValidityWarning(
            "transport.sequenced-no-flight-time",
            "this run ends when its sequence ends, not when an ion arrives, so there is "
            + "no single flight time. What each phase did is reported under 'sequence', "
            + "and the ions that reached the detector are counted there",
            WarningSeverity.Provenance);

        List<ValidityWarning> runWarnings =
            [.. fieldWarnings, .. outcome.Warnings, .. sequenceWarnings];

        // WHAT --vtu MEANS FOR A SEQUENCED RUN, which until now was nothing at all: it
        // wrote a manifest, an arrivals file and a result, and no density - so a sequenced
        // packet could be summarised into a centroid and a width and looked at in no other
        // form. That is the state the wholly diffusive path was in before RND-8's argument
        // was answered for it, and this is the same answer: the file it writes is the thing
        // it actually computed.
        //
        // Only where the run ended as a density. A sequence finishing in the trajectory
        // description has none, which is a different statement from having an empty one.
        if (exportVtu && outcome.FinalDensity is { } finalDensity)
        {
            Directory.CreateDirectory(project.Scratch);

            var densityPath = Path.Combine(project.Scratch, $"{stem}.density.vti");

            // GRD-2: the warnings travel with the file, because a volume is the artifact
            // most likely to be opened by somebody who never saw the result envelope.
            var provenance = new List<string>
            {
                $"engine: {EngineBuild.Version}",
                $"model: {validation.ModelHash}",
                $"transport: {manifest.TransportMode}, {outcome.Phases.Count} phase(s) over "
                    + $"{last.EndsAtSeconds * 1e6:G6} us",
                $"ions: {outcome.Arrived:G6} collected, {last.Population:G6} still tracked",
                $"the density at the end of '{last.Name}', where the sequence left it",
                "units: ions per cubic metre, at grid nodes",
            };

            // The severity is on the line, not only the code. A reader deciding whether
            // to trust a volume needs to know which of these is a note about how the run
            // was framed and which is the engine saying the numbers may not describe the
            // document - and above advisory they cannot be suppressed anywhere (GRD-3).
            provenance.AddRange(
                runWarnings.Append(sequencedNote)
                    .Select(w => $"{w.Severity}: {w.Code}: {w.Message}"));

            File.WriteAllText(
                densityPath,
                Io.VtuWriter.WriteDensityField(finalDensity, "density_per_m3", provenance));

            artifacts.Add(Path.GetRelativePath(project.Root, densityPath));
        }

        var run = new RunOutcome
        {
            Manifest = manifest,

            FlightTime = MeasuredJson.From(
                Carry(
                    new Measured(
                        Quantity.Si(double.NaN, Dimension.TimeDimension),
                        UncertaintyInterval.Symmetric(
                            Quantity.Si(double.NaN, Dimension.TimeDimension),
                            Quantity.Si(0.0, Dimension.TimeDimension),
                            1.0),
                        new Evidence.Convergence("sequenced transport", double.NaN, 0.0, double.NaN),
                        [sequencedNote]),
                    runWarnings),
                "us"),

            Outcome = "SequenceCompleted",
            Completed = true,

            // The packet's centre where the sequence left it. Not a single ion's final
            // position - there is no single ion - which is why this is a centroid and is
            // labelled as one in the sequence block.
            FinalPositionMm = last.CentroidMm,

            // A driven or switched field does work deliberately, so energy drift stops
            // being a diagnostic; a sequenced run is switched by construction.
            MaximumRelativeEnergyDrift = double.NaN,
            AcceptedSteps = 0,
            AnalyticDriftDistanceM = 0.0,
            Artifacts = artifacts,
            HasFlightTime = false, // a sequence ends on its own clock rather than on an arrival

            Sequence = new SequenceJson(
                [.. outcome.Phases.Select(Phase)],
                outcome.Conversions,
                outcome.Arrived,
                outcome.Losses)
            {
                MeanArrivalUs = meanArrivalUs,
                ArrivalSpreadUs = arrivalSpreadUs,
            },
        };

        // WRITTEN LIKE EVERY OTHER RUN'S. Three of the four run paths wrote a result
        // document beside their manifest; this one wrote the manifest and no result, so a
        // sequenced run left behind provenance and no answer - and a sequenced run is what
        // every TIMS study is. PRJ-3's claim is that a manifest determines its run, which
        // stands either way; what was missing is the stored answer that determination is
        // *for*, so nothing could be regenerated and compared, and no reader downstream had
        // numbers to read for exactly the runs most worth reading.
        //
        // `einzel verify` was not the thing broken, and the first version of this comment
        // said it was. Verify enumerates manifests and checks the model hash and the
        // solver-behaviour version without reading a result at all, so it reported a
        // sequenced run as current all along - correctly, about drift, over an answer that
        // was not there. Found while building the report Amendment 43 asks for, which does
        // read these files, on a project holding four manifests and no results.
        var resultPath = Path.Combine(project.Results, $"{stem}.result.json");
        File.WriteAllText(resultPath, CommandJson.Write(run));

        return run with
        {
            Artifacts = [.. run.Artifacts, Path.GetRelativePath(project.Root, resultPath)],
        };
    }


    /// <summary>Runs a model that declares several ion populations.</summary>
    /// <remarks>
    /// Beside <see cref="Diffusive"/> rather than folded into it. The shared reporting - the
    /// manifest, the regime, the warnings - is the same because it is about the run rather than
    /// about any one population; what differs is that there is no single density, no single
    /// mobility and no single transit, so the block that carries those has to be per species.
    /// A single `diffusion` block filled from the first population would be a report of one
    /// species wearing the name of the run.
    /// </remarks>
    private static RunOutcome Mixture(
        CompiledModel model,
        Fields.IElectrostaticField field,
        IReadOnlyList<ValidityWarning> fieldWarnings,
        ValidateOutcome validation,
        ProjectLayout project,
        DateTimeOffset timestampUtc,
        IReadOnlyDictionary<string, string> inputHashes,
        bool exportVtu,
        RunProgress? progress)
    {
        var resolved = Io.GasFlowImport.Resolve(
            model.Gas, Path.GetDirectoryName(validation.ModelPath) ?? ".");

        var watcher = Watch(progress, project, validation);

        var outcome = DiffusionRun.ExecuteMixture(
            model, field, fieldWarnings, resolved, progress: watcher);

        watcher?.Discard();
        var result = outcome.Result;

        var manifest = new RunManifest
        {
            ModelHash = validation.ModelHash,
            InputHashes = RunInputs.Checked(inputHashes, project.Root),
            ModelPath = RunManifest.Portable(
                Path.GetRelativePath(project.Root, validation.ModelPath)),
            SchemaVersion = ModelJson.Parse(File.ReadAllText(validation.ModelPath)).SchemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            Solves = Fields.Solved.SolveHash.Of(model),
            TransportMode = model.TransportMode,
            ComputePath = EngineBuild.ComputePath,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(validation.ModelPath);

        var manifestPath = Path.Combine(project.Results, $"{stem}.manifest.json");

        File.WriteAllText(manifestPath, manifest.ToJson());

        var species = new List<MixtureSpeciesJson>(result.Species.Count);

        for (var s = 0; s < result.Species.Count; s++)
        {
            var member = result.Species[s];
            var left = member.Collected + member.Losses.Values.Sum();

            double? transit = null;

            if (member.Arrivals.Count > 0 && member.Collected > 0.0)
            {
                transit = member.Arrivals.Sum(a => a.AtSeconds * a.Ions) / member.Collected * 1e6;
            }

            var (centroidX, _) = member.Density.Centroid();

            species.Add(new MixtureSpeciesJson(
                member.Name,
                outcome.Mobilities[s].ZeroFieldSi,
                outcome.Mobilities[s].Derived,
                outcome.Launched[s],
                member.Collected,
                member.Population,
                [
                    .. member.Losses
                        .OrderByDescending(pair => pair.Value)
                        .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                        .Select(pair => new LossChannel(pair.Key, (int)Math.Round(pair.Value))),
                ],
                left > 0.0 ? member.Collected / left : 0.0,
                transit,
                centroidX * 1e3,
                member.StableStepSeconds * 1e6));
        }

        var gas = resolved;

        var regime = Transport.Collisions.RegimeDiagnostics.Measure(
            gas, IonSpecies.FromModel(model), 1.0, result.ElapsedSeconds, SmallestAperture(model));

        var artifacts = new List<string> { Path.GetRelativePath(project.Root, manifestPath) };

        // One volume per population, named by it. A mixture has no single density, so a single
        // file called the density would be a picture of whichever species happened to be first
        // - and writing nothing at all, which is what this did before the flag was threaded
        // through, is the worst of the three: `--vtu` asked for a file and got silence.
        if (exportVtu)
        {
            Directory.CreateDirectory(project.Scratch);

            foreach (var member in result.Species)
            {
                var densityPath = Path.Combine(project.Scratch, $"{stem}.{member.Name}.density.vti");

                // GRD-2: the warnings travel with the file, because a volume is the artifact
                // most likely to be looked at by someone who never saw the result envelope.
                var provenance = new List<string>
                {
                    $"engine: {EngineBuild.Version}",
                    $"model: {validation.ModelHash}",
                    $"species: {member.Name}",
                    $"transport: diffusion, {result.Steps} shared steps over "
                        + $"{result.ElapsedSeconds:G6} s, step set by '{result.StepSetBy}'",
                    $"ions: {member.Collected:G6} collected, {member.Population:G6} still in the domain",
                    "units: ions per cubic metre, at grid nodes",
                };

                // Named, because a mixture's whole point is that the populations differ and a
                // reader opening one file needs to know which of them raised the field.
                provenance.Add(
                    result.SelfFieldSolves > 0
                        ? $"space charge: the potential of ALL populations, {result.SelfFieldSolves} "
                          + $"solve(s), peak {result.PeakSelfPotentialVolts:G4} V"
                        : "space charge: not modelled; the populations did not feel one another");

                provenance.AddRange(outcome.Warnings.Select(w => $"{w.Severity}: {w.Code}: {w.Message}"));

                File.WriteAllText(
                    densityPath,
                    VtuWriter.WriteDensityField(member.Density, "density_per_m3", provenance));

                artifacts.Add(Path.GetRelativePath(project.Root, densityPath));
            }
        }

        var run = new RunOutcome
        {
            Manifest = manifest,

            FlightTime = MeasuredJson.From(
                Carry(
                    new Measured(
                        Quantity.Si(double.NaN, Dimension.TimeDimension),
                        UncertaintyInterval.Symmetric(
                            Quantity.Si(double.NaN, Dimension.TimeDimension),
                            Quantity.Si(0.0, Dimension.TimeDimension),
                            1.0),
                        new Evidence.Convergence("diffusive transport", double.NaN, 0.0, double.NaN),
                        [
                            new ValidityWarning(
                                "transport.no-flight-time",
                                "this run computed densities, not trajectories, so there is no flight "
                                + "time. What each population did is reported under 'mixture'",
                                WarningSeverity.Provenance),
                        ]),
                    outcome.Warnings),
                "us"),

            Outcome = "DensityEvolved",
            Completed = true,
            FinalPositionMm = [],
            MaximumRelativeEnergyDrift = double.NaN,
            AcceptedSteps = result.Steps,
            AnalyticDriftDistanceM = 0.0,
            Artifacts = artifacts,
            HasFlightTime = false, // several densities: no ions to arrive, and no single one of them if there were

            Regime = gas.IsPresent
                ? new RegimeJson
                {
                    PressureMbar = regime.PressureMbar,
                    CollisionModel = model.Gas.Model,
                    MeanFreePathMm = regime.MeanFreePathM * 1e3,
                    ApertureMm = regime.ApertureM * 1e3,
                    Knudsen = regime.Knudsen,
                    CollisionsPerFlight = regime.CollisionsPerFlight,
                    CollisionsPerRfCycle = regime.CollisionsPerRfCycle,
                }
                : null,

            Mixture = new MixtureJson(
                species,
                result.Steps,
                result.StepSeconds * 1e6,
                result.StepSetBy,
                model.ModelsMeanField ? result.SelfFieldSolves : null,
                model.ModelsMeanField ? result.PeakSelfPotentialVolts : null,
                model.ModelsMeanField ? result.NetChargeSi : null),
        };

        // Written like every other run's, so `einzel verify` has something to check this
        // against. A path that produced no result document would be regenerable and
        // unverifiable, which is half of PRJ-3.
        var resultPath = Path.Combine(project.Results, $"{stem}.result.json");

        File.WriteAllText(resultPath, CommandJson.Write(run));

        return run with
        {
            Artifacts = [.. run.Artifacts, Path.GetRelativePath(project.Root, resultPath)],
        };
    }

    /// <summary>The checkpoint for this run, or null where nobody asked to watch.</summary>
    /// <remarks>
    /// <b>Beside the manifest, under the manifest's own stem</b>, which is the rule the
    /// report reached for finding a stored answer - a checkpoint somewhere else would be a
    /// fourth convention in one directory.
    /// </remarks>
    private static RunCheckpointWriter? Watch(
        RunProgress? progress, ProjectLayout project, ValidateOutcome validation)
    {
        if (progress is null || progress.IntervalSeconds <= 0.0)
        {
            return null;
        }

        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(validation.ModelPath);

        return new RunCheckpointWriter(
            Path.Combine(project.Results, $"{stem}.progress.json"),
            progress,

            // WHAT A MANIFEST WOULD SAY, written before the first step. The manifest itself
            // is written after the run returns - it records the modes the run actually used
            // - so an interrupted run has none, and every consumer here enumerates
            // manifests. None of this is derived from the outcome, which is what makes it
            // available this early.
            new RunCheckpointProvenance(
                RunManifest.Portable(
                    Path.GetRelativePath(project.Root, validation.ModelPath)),
                validation.ModelHash,
                EngineBuild.Version,
                EngineBuild.SolverBehaviourVersion,
                Environment.MachineName,
                DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static RunOutcome Diffusive(
        CompiledModel model,
        Fields.IElectrostaticField field,
        IReadOnlyList<ValidityWarning> fieldWarnings,
        ValidateOutcome validation,
        ProjectLayout project,
        DateTimeOffset timestampUtc,
        IReadOnlyDictionary<string, string> inputHashes,
        bool exportVtu,
        RunProgress? progress)
    {
        // Asked of the model rather than of the caller: a mixture is a property of the document,
        // and a fork the caller had to remember is one a caller will forget.
        if (model.IsMixture)
        {
            return Mixture(
                model, field, fieldWarnings, validation, project, timestampUtc, inputHashes,
                exportVtu, progress);
        }

        // The one place that knows where the model file is, so the one place that can
        // resolve a declared velocity field.
        var resolved = Io.GasFlowImport.Resolve(
            model.Gas, Path.GetDirectoryName(validation.ModelPath) ?? ".");

        var watcher = Watch(progress, project, validation);

        var outcome = DiffusionRun.Execute(
            model, field, fieldWarnings, resolved, progress: watcher);

        watcher?.Discard();

        var result = outcome.Result;

        var left = result.Collected + result.Lost.Values.Sum();

        // Transit time from the arrivals series, which is what a density has instead
        // of a list of arrival times. Weighted by how many ions arrived in each bin.
        double? mean = null;
        double? spread = null;

        if (result.Arrivals.Count > 0 && result.Collected > 0.0)
        {
            var weighted = result.Arrivals.Sum(a => a.TimeSeconds * a.Ions) / result.Collected;

            var variance = result.Arrivals.Sum(
                a => a.Ions * (a.TimeSeconds - weighted) * (a.TimeSeconds - weighted)) / result.Collected;

            mean = weighted * 1e6;
            spread = Math.Sqrt(Math.Max(0.0, variance)) * 1e6;
        }

        var (spreadX, spreadY) = result.Density.Spread();

        var manifest = new RunManifest
        {
            ModelHash = validation.ModelHash,
            InputHashes = RunInputs.Checked(inputHashes, project.Root),

            // Which model, as distinct from which content. Without it verify has to find
            // the model by searching for one that still hashes to the recorded value, and
            // editing the model that was actually run then makes its drift disappear -
            // the result re-attaches to whatever else still matches.
            ModelPath = RunManifest.Portable(
                Path.GetRelativePath(project.Root, validation.ModelPath)),
            SchemaVersion = ModelJson.Parse(File.ReadAllText(validation.ModelPath)).SchemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            Solves = Fields.Solved.SolveHash.Of(model),
            TransportMode = model.TransportMode,
            ComputePath = EngineBuild.ComputePath,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        Directory.CreateDirectory(project.Results);

        var stem = Path.GetFileNameWithoutExtension(validation.ModelPath);
        var manifestPath = Path.Combine(project.Results, $"{stem}.manifest.json");

        File.WriteAllText(manifestPath, manifest.ToJson());

        // The resolved gas, not a fresh one built from the document. Rebuilding here
        // would report a model with an imported velocity field as standing still,
        // which is the same silent drop the resolution exists to prevent - one line
        // further down the pipe.
        var gas = resolved;

        var regime = Transport.Collisions.RegimeDiagnostics.Measure(
            gas, IonSpecies.FromModel(model), 1.0, result.ElapsedSeconds, SmallestAperture(model));

        var run = new RunOutcome
        {
            Manifest = manifest,

            // No flight time: a density does not have one. The transit time is in the
            // diffusion block, where it is a distribution rather than a number.
            FlightTime = MeasuredJson.From(
                Carry(
                    new Measured(
                        Quantity.Si(double.NaN, Dimension.TimeDimension),
                        UncertaintyInterval.Symmetric(
                            Quantity.Si(double.NaN, Dimension.TimeDimension),
                            Quantity.Si(0.0, Dimension.TimeDimension),
                            1.0),
                        new Evidence.Convergence("diffusive transport", double.NaN, 0.0, double.NaN),
                        [
                            new ValidityWarning(
                                "transport.no-flight-time",
                                "this run computed a density, not trajectories, so there is no "
                                + "flight time. The transit-time distribution is reported under "
                                + "'diffusion' instead",
                                WarningSeverity.Provenance),
                        ]),
                    outcome.Warnings),
                "us"),

            Outcome = "DensityEvolved",
            Completed = true,
            FinalPositionMm = [],
            MaximumRelativeEnergyDrift = double.NaN,
            AcceptedSteps = result.Steps,
            AnalyticDriftDistanceM = 0.0,

            Regime = gas.IsPresent
                ? new RegimeJson
                {
                    PressureMbar = regime.PressureMbar,
                    CollisionModel = model.Gas.Model,
                    MeanFreePathMm = regime.MeanFreePathM * 1e3,
                    ApertureMm = regime.ApertureM * 1e3,
                    Knudsen = regime.Knudsen,
                    CollisionsPerFlight = regime.CollisionsPerFlight,
                    CollisionsPerRfCycle = regime.CollisionsPerRfCycle,
                }
                : null,

            Diffusion = new DiffusionJson
            {
                Launched = outcome.Launched,
                Collected = result.Collected,
                Remaining = result.Remaining,
                Losses =
                [
                    .. result.Lost
                        .OrderByDescending(p => p.Value)
                        .ThenBy(p => p.Key, StringComparer.Ordinal)
                        .Select(p => new LossChannel(p.Key, (int)Math.Round(p.Value))),
                ],
                Transmission = left > 0.0 ? result.Collected / left : 0.0,
                MeanTransitUs = mean,
                TransitSpreadUs = spread,
                MobilitySi = outcome.Mobility.ZeroFieldSi,
                MobilityDerived = outcome.Mobility.Derived,
                GasSpeedSi = gas.IsFlowing ? gas.FastestBulkSpeedSi : null,
                SelfFieldSolves = model.ModelsMeanField ? result.SelfFieldSolves : null,
                PeakSelfPotentialVolts = model.ModelsMeanField ? result.PeakSelfPotentialVolts : null,
                Nodes = [outcome.Grid.CountX, outcome.Grid.CountY],
                Steps = result.Steps,
                ElapsedUs = result.ElapsedSeconds * 1e6,
                SpreadMm = [spreadX * 1e3, spreadY * 1e3],
            },

            Artifacts = [Path.GetRelativePath(project.Root, manifestPath)],
            HasFlightTime = false, // a density has no arrival time; the transit distribution is under `diffusion`
        };

        var artifacts = new List<string> { Path.GetRelativePath(project.Root, manifestPath) };

        // What --vtu means for this mode. A diffusive run has no trajectory to write
        // and RND-8 forbids inventing one, so the file it writes is the thing it
        // actually computed: the density at the end of the run, on the grid it was
        // tracked on. Before this the flag was accepted and silently did nothing,
        // which is the worst of the three options - the result of the mode could not
        // be looked at in any form, and nothing said so.
        if (exportVtu)
        {
            Directory.CreateDirectory(project.Scratch);

            var densityPath = Path.Combine(project.Scratch, $"{stem}.density.vti");

            // GRD-2: the warnings travel with the file. A figure or a volume is the
            // artifact most likely to be looked at by someone who never saw the
            // result envelope it came from.
            var provenance = new List<string>
            {
                $"engine: {EngineBuild.Version}",
                $"model: {validation.ModelHash}",
                $"transport: diffusion, {result.Steps} steps over {result.ElapsedSeconds:G6} s",
                $"ions: {outcome.Launched:G6} launched, {result.Collected:G6} collected, "
                    + $"{result.Remaining:G6} still in the domain",
                "units: ions per cubic metre, at grid nodes",
            };

            provenance.AddRange(outcome.Warnings.Select(w => $"{w.Severity}: {w.Code}: {w.Message}"));

            File.WriteAllText(
                densityPath,
                VtuWriter.WriteDensityField(result.Density, "density_per_m3", provenance));

            artifacts.Add(Path.GetRelativePath(project.Root, densityPath));
        }

        run = run with { Artifacts = artifacts };

        var resultPath = Path.Combine(project.Results, $"{stem}.result.json");
        File.WriteAllText(resultPath, CommandJson.Write(run));

        return run with
        {
            Artifacts = [.. run.Artifacts, Path.GetRelativePath(project.Root, resultPath)],
        };
    }

    /// <summary>
    /// The tightest constriction an ion must pass, in metres.
    /// </summary>
    /// <remarks>
    /// What the Knudsen number is taken against. The honest choice is the narrowest
    /// thing in the model rather than the size of the whole instrument: a gas is a
    /// continuum on the scale of an aperture long before it is one on the scale of a
    /// flight tube, and the aperture is where that matters.
    ///
    /// Falls back to the source-to-detector distance where no geometry declares a
    /// smaller feature, which is the largest defensible length and so the most
    /// conservative Knudsen number.
    /// </remarks>
    /// <summary>
    /// The tightest constriction the ion has to pass through, in metres.
    /// </summary>
    /// <remarks>
    /// Internal rather than private because the regime inspector needs the same length:
    /// a Knudsen number is meaningless without one, and two implementations of "the
    /// tightest constriction" would let a run and an inspection of that run disagree
    /// about whether the gas is a continuum.
    /// </remarks>
    internal static double SmallestAperture(CompiledModel model)
    {
        var smallest = double.PositiveInfinity;

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } flat)
            {
                smallest = Math.Min(smallest, Math.Min(flat.MaxX - flat.MinX, flat.MaxY - flat.MinY));
            }

            if (element.Solve3D is { } volume)
            {
                smallest = Math.Min(
                    smallest,
                    Math.Min(
                        volume.MaxX - volume.MinX,
                        Math.Min(volume.MaxY - volume.MinY, volume.MaxZ - volume.MinZ)));
            }
        }

        if (double.IsPositiveInfinity(smallest))
        {
            smallest = (model.DetectorPoint - model.SourcePosition).Length;
        }

        return smallest > 0.0 ? smallest : 1.0;
    }

    /// <summary>The drive frequency, or zero when the model is not driven.</summary>
    internal static double DriveFrequency(CompiledModel model)
    {
        var highest = 0.0;

        foreach (var element in model.Fields)
        {
            if (element.Solve?.Drive is { } flat)
            {
                highest = Math.Max(highest, flat.FrequencyHz);
            }

            if (element.Solve3D?.Drive is { } volume)
            {
                highest = Math.Max(highest, volume.FrequencyHz);
            }
        }

        return highest;
    }

    /// <summary>Attaches warnings to a result, since GRD-2 makes them travel with it.</summary>
    private static Measured Carry(Measured measured, IReadOnlyList<ValidityWarning> warnings)
    {
        foreach (var warning in warnings)
        {
            measured = measured.WithWarning(warning);
        }

        return measured;
    }

    /// <summary>Validates a model document on disk.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <returns>The validation outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="FileNotFoundException">The model file does not exist.</exception>
    public static ValidateOutcome Validate(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var full = Path.GetFullPath(modelPath);

        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"model file not found: {full}", full);
        }

        var text = File.ReadAllText(full);
        var hash = ContentHash.OfText(text);

        ModelDocument document;

        try
        {
            document = ModelJson.Parse(text);
        }
        catch (EinzelException failure)
        {
            return new ValidateOutcome
            {
                Valid = false,
                ModelPath = full,
                ModelHash = hash,
                Errors = [failure.Error],
            };
        }

        var validation = ModelValidator.Validate(
            document, null, Path.GetDirectoryName(full));

        return new ValidateOutcome
        {
            Valid = validation.IsValid,
            ModelPath = full,
            ModelHash = hash,
            SchemaVersion = document.SchemaVersion,
            Errors = validation.Errors,
        };
    }

    /// <summary>Runs a model, writing a manifest, a result, and optionally a VTU trajectory.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <param name="project">The project the outputs belong to.</param>
    /// <param name="exportVtu">Whether to write the trajectory for ParaView.</param>
    /// <param name="timestampUtc">The run timestamp, supplied so the caller owns the clock.</param>
    /// <param name="progress">
    /// How often the run should say where it has got to, and who to tell, or null to run
    /// silently until it finishes. A run measured in hours has to be able to say something
    /// before it ends - and one that stores nothing until then loses everything to a
    /// reboot, which is how three attempts at the TIMS front-end sequence were lost.
    /// </param>
    /// <returns>The run outcome, or the validation failure that prevented it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    public static (RunOutcome? Run, ValidateOutcome Validation) Execute(
        string modelPath,
        ProjectLayout project,
        bool exportVtu,
        DateTimeOffset timestampUtc,
        RunProgress? progress = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        var validation = Validate(modelPath);

        if (!validation.Valid)
        {
            return (null, validation);
        }

        var document = ModelJson.Parse(File.ReadAllText(validation.ModelPath));
        var inputHashes = RunInputs.Capture(document, validation.ModelPath, project.Root);
        var model = ModelValidator.Validate(
            document, null, Path.GetDirectoryName(validation.ModelPath)).Model!;

        // THE SOLVE IS THE LONGEST SILENT STRETCH OF A LONG RUN, so it is announced
        // before it starts rather than after. On the TIMS front end - a sixteen-plate
        // funnel and a twenty-seven-ring analyzer - it is minutes on its own, and until
        // this line existed a watched run said nothing at all until its first step, which
        // reads exactly like a run that has hung. The wall clock is reported with it,
        // because the cycles and the convergence factor already on the result say how
        // well the solve went and not how long a person waited for it.
        var announce = progress?.Announce;
        var solving = System.Diagnostics.Stopwatch.StartNew();

        announce?.Invoke("  solving the field, which for a large geometry is the "
            + "longest single step of a run");

        // Reported, not bare. A solve that missed its tolerance produces a field
        // indistinguishable from one that met it, so the evidence has to travel
        // alongside and land on every number computed through it (GRD-2).
        var (field, built) = FieldAssembly.BuildReported(model);
        var fieldWarnings = built;

        announce?.Invoke(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "  the field is solved in {0:F1} s; stepping now",
            solving.Elapsed.TotalSeconds));

        // A run whose phases are not all in one description is a third case, and it
        // comes first: a model may declare "diffusion" as its own mode and still have a
        // sequence that leaves it, and the sequence is the more specific statement.
        //
        // And a diffusive model with a sequence that STAYS diffusive goes the same way,
        // because the plain diffusive path reads the field through the time-free
        // interface and steps a snapshot of it - so a TIMS elution ramp declared on such
        // a model ran with the ramp silently ignored, the sixth time here a time-varying
        // quantity reached through a time-free interface has answered at an arbitrary
        // instant rather than failing. A trajectory model needs no such routing: its
        // integrator asks the field for the instant it is at.
        if (model.NeedsSequencedTransport)
        {
            return (
                Sequenced(
                    model, field, fieldWarnings, validation, project, timestampUtc, inputHashes,
                    exportVtu, progress),
                validation);
        }

        // REG-1's two modes are peers, so this is a fork rather than a special case
        // inside one path. A diffusive run has no flight time and no final position -
        // there are no trajectories in it - so it produces a different result rather
        // than the same result with fields left empty.
        if (model.TransportMode == "diffusion")
        {
            return (
                Diffusive(
                    model, field, fieldWarnings, validation, project, timestampUtc, inputHashes,
                    exportVtu, progress),
                validation);
        }

        var species = IonSpecies.FromModel(model);

        var launch = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;
        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        // REG-1's seam, on the execution path rather than beside it. The validator
        // refuses an unbuilt mode earlier with the same message; this is what stops
        // a model built in code - by a sweep, a study, or a test - from falling
        // through to whichever mode happened to be implemented first.
        _ = TransportModes.Resolve(model.TransportMode);

        // Resolved rather than merely built from the model, so a declared velocity
        // field reaches the event-driven models too. FromModel alone would produce a
        // gas with no flow in it and no complaint - which is the failure the sampler
        // used to refuse a flow outright to prevent, and removing that refusal
        // without this would have reintroduced it exactly.
        var gas = Io.GasFlowImport.Resolve(
            model.Gas, Path.GetDirectoryName(validation.ModelPath) ?? ".");

        // The reportable number comes from the convergence study, not from a
        // single integration: one run has no honest uncertainty to quote.
        //
        // In a gas the study still flies the gas - reporting a vacuum flight time
        // for a model that declares a pressure would be the same silent substitution
        // the validator refuses elsewhere - but the interval it produces measures
        // the integrator and not the stochastic spread, which is said below.
        var study = FlightTimeStudy.Run(
            launch, species, field, settings, detector,
            collisions: gas.IsPresent
                ? () => new Transport.Collisions.CollisionSampler(
                    gas, species.MassSi, species.ChargeSi, model.Gas.Seed)
                : null);

        var finest = study.Runs[^1];

        // REG-2: the governing dimensionless numbers, computed rather than assumed,
        // and a non-suppressible warning where the selected mode is outside its
        // validity. Measured at the launch speed and the flight this model actually
        // produced, so the numbers describe the run rather than a nominal case.

        var regime = Transport.Collisions.RegimeDiagnostics.Measure(
            gas,
            species,
            Math.Max(launch.Velocity.Length, 1.0),
            finest.FlightTimeSeconds,
            SmallestAperture(model),
            DriveFrequency(model));

        var regimeWarnings = Transport.Collisions.RegimeDiagnostics.ForTrajectoryMode(gas, regime);

        if (gas.IsPresent)
        {
            regimeWarnings =
            [
                .. regimeWarnings,
                new ValidityWarning(
                    "collisions.single-ion-interval",
                    "this flight time is one collisional history. The interval on it is the "
                    + "integrator's own convergence and not the spread of arrival times a packet "
                    + "would have: refining the tolerance does not average over the collisions, it "
                    + "reruns the same draws against a slightly different trajectory. Declare an "
                    + "ion cloud for a number whose interval means what it looks like",
                    WarningSeverity.Qualified),
            ];
        }

        // A half-space's ramp does not stop where the document says the plate is, and an
        // ion with more energy than the cap turns round behind it - self-consistently, and
        // in a region the model does not describe.
        fieldWarnings =
        [
            .. fieldWarnings, .. regimeWarnings, .. HalfSpaceDepthWarnings(model),
            .. ProvenanceWarnings(model),
        ];

        var manifest = new RunManifest
        {
            ModelHash = validation.ModelHash,
            InputHashes = RunInputs.Checked(inputHashes, project.Root),

            // Which model, as distinct from which content. Without it verify has to find
            // the model by searching for one that still hashes to the recorded value, and
            // editing the model that was actually run then makes its drift disappear -
            // the result re-attaches to whatever else still matches.
            ModelPath = RunManifest.Portable(
                Path.GetRelativePath(project.Root, validation.ModelPath)),
            SchemaVersion = document.SchemaVersion,
            EngineVersion = EngineBuild.Version,
            SolverBehaviourVersion = EngineBuild.SolverBehaviourVersion,
            Solves = Fields.Solved.SolveHash.Of(model),
            TransportMode = model.TransportMode,
            ComputePath = EngineBuild.ComputePath,
            Machine = Environment.MachineName,
            CreatedUtc = timestampUtc.ToUniversalTime().ToString("O"),
        };

        var stem = Path.GetFileNameWithoutExtension(validation.ModelPath);
        var artifacts = new List<string>();

        // Assembled here rather than at the result, because the exported file must carry
        // the same set (GRD-2) and two expressions for "the warnings on this run" is how
        // they come to differ. This is the seventh place in this engine where evidence
        // about a computation was dropped at a seam, and the sixth was the same writer.
        var flightTime = Carry(study.FlightTime, fieldWarnings);

        Directory.CreateDirectory(project.Results);
        var manifestPath = Path.Combine(project.Results, $"{stem}.manifest.json");
        File.WriteAllText(manifestPath, manifest.ToJson());
        artifacts.Add(Path.GetRelativePath(project.Root, manifestPath));

        if (exportVtu)
        {
            var recorder = new TrajectoryRecorder(model.SampleIntervalSi);

            // The drawn flight has to be the flown flight. This integration once omitted the
            // collision sampler, so a --vtu of any collisional run was a vacuum flight - an
            // ion crossing a 2.5 mbar funnel in 10 us on the axis, beside a result that said
            // 589 us and a strike on the exit plate. Same gas, same seed: the same scheduled
            // collision instants and the same velocity draws as the reported flight.
            TrajectoryIntegrator.Integrate(
                launch, species, field,
                settings with { RelativeTolerance = model.RelativeTolerance },
                detector, recorder,
                collisions: gas.IsPresent
                    ? new Transport.Collisions.CollisionSampler(
                        gas, species.MassSi, species.ChargeSi, model.Gas.Seed)
                    : null);

            if (recorder.Samples.Count >= 2)
            {
                Directory.CreateDirectory(project.Scratch);
                var vtuPath = Path.Combine(project.Scratch, $"{stem}.trajectory.vtu");

                // GRD-2: the warnings travel with the file. A .vtu is the artifact that
                // travels furthest - opened in ParaView, months later, by someone who never
                // saw the result envelope it came from - so it is the layer where a dropped
                // warning does the most damage. The density path beside this one has always
                // carried them; the trajectory path did not, through the same writer and the
                // same optional parameter.
                var provenance = new List<string>
                {
                    $"engine: {EngineBuild.Version}",
                    $"model: {validation.ModelHash}",
                    $"samples: {recorder.Samples.Count} at {model.SampleIntervalSi:G6} s nominal interval",
                    recorder.Truncated
                        ? "TRUNCATED: sample capacity reached; the tail of this trajectory is missing"
                        : "complete",
                };

                provenance.AddRange(
                    flightTime.Warnings.Select(w => $"{w.Severity}: {w.Code}: {w.Message}"));

                File.WriteAllText(
                    vtuPath, VtuWriter.WriteTrajectory(recorder.Samples, provenance));

                artifacts.Add(Path.GetRelativePath(project.Root, vtuPath));
            }
        }

        var run = new RunOutcome
        {
            Manifest = manifest,
            FlightTime = MeasuredJson.From(flightTime, "us"),
            Outcome = finest.Outcome.ToString(),
            Completed = finest.Outcome.Completed(),
            FinalPositionMm =
            [
                finest.FinalState.Position.X * 1e3,
                finest.FinalState.Position.Y * 1e3,
                finest.FinalState.Position.Z * 1e3,
            ],
            MaximumRelativeEnergyDrift = finest.MaximumRelativeEnergyDrift,
            Regime = gas.IsPresent
                ? new RegimeJson
                {
                    PressureMbar = regime.PressureMbar,
                    CollisionModel = model.Gas.Model,
                    MeanFreePathMm = regime.MeanFreePathM * 1e3,
                    ApertureMm = regime.ApertureM * 1e3,
                    Knudsen = regime.Knudsen,
                    CollisionsPerFlight = regime.CollisionsPerFlight,
                    CollisionsPerRfCycle = regime.CollisionsPerRfCycle,
                }
                : null,
            AcceptedSteps = finest.AcceptedSteps,
            AnalyticDriftDistanceM = finest.AnalyticDriftDistance,
            Artifacts = artifacts,
            HasFlightTime = true, // one ion, flown to a detector
        };

        // A model that launches a cloud gets the Class S half of a result too. The
        // single-ion flight time stays: it is the centre of the peak and it is the
        // number with a convergence study behind it, so removing it would trade a
        // measured uncertainty for a sampled one.
        if (model.Cloud.IsCloud)
        {
            run = run with { Ensemble = Ensemble(model, fieldWarnings) };
        }

        var resultPath = Path.Combine(project.Results, $"{stem}.result.json");
        File.WriteAllText(resultPath, CommandJson.Write(run));

        return (run with { Artifacts = [.. artifacts, Path.GetRelativePath(project.Root, resultPath)] }, validation);
    }
}
