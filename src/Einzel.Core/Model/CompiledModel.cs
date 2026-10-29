using Einzel.Core.Geometry;
using Einzel.Core.Units;

namespace Einzel.Core.Model;

/// <summary>
/// A validated model, with every quantity converted to SI exactly once.
/// </summary>
/// <remarks>
/// <para>
/// The boundary spec section 9 describes: units are explicit in the document and
/// resolved here, so nothing downstream re-derives them. Everything on this
/// record is SI, and every value has already been checked for dimension, range,
/// and internal consistency.
/// </para>
/// <para>
/// It holds primitives rather than engine objects because Einzel.Core is the
/// innermost assembly and cannot reference the field or transport types built
/// from it. Assembling those is the job of the layer that owns them.
/// </para>
/// </remarks>
public sealed record CompiledModel
{
    /// <summary>The directory the model document was read from, or null.</summary>
    /// <remarks>
    /// <para>
    /// A model may reference files - an imported gas velocity or pressure field - and
    /// PRJ-2 says it references them rather than embedding them, resolved against the
    /// model document's own directory so that a model means the same thing wherever
    /// the command is run from. Carrying that directory on the compiled model is what
    /// lets any consumer resolve one.
    /// </para>
    /// <para>
    /// <b>Null is the safe value, not the convenient one.</b> A model compiled from a
    /// string in memory has no directory, and a consumer handed one is refused rather
    /// than run in a gas the document does not describe. So a loader that forgets to
    /// set this degrades to the refusal rather than to a silent wrong answer, which is
    /// the direction a mistake here should fail in.
    /// </para>
    /// </remarks>
    public string? SourceDirectory { get; init; }

    /// <summary>The document this was compiled from, for hashing and round-trip.</summary>
    public required ModelDocument Source { get; init; }

    /// <summary>Ion mass, in kilograms.</summary>
    public required double MassSi { get; init; }

    /// <summary>Ion charge, in coulombs. Signed.</summary>
    public required double ChargeSi { get; init; }

    /// <summary>Starting position, in metres.</summary>
    public required Vec3 SourcePosition { get; init; }

    /// <summary>Direction of travel, normalised.</summary>
    public required Vec3 SourceDirection { get; init; }

    /// <summary>Accelerating potential, in volts.</summary>
    public required double AccelerationPotentialSi { get; init; }

    /// <summary>Fractional offset from nominal energy.</summary>
    public required double EnergyFraction { get; init; }

    /// <summary>The field elements, superposed.</summary>
    public required IReadOnlyList<CompiledField> Fields { get; init; }

    /// <summary>A point on the detector plane, in metres.</summary>
    public required Vec3 DetectorPoint { get; init; }

    /// <summary>The detector plane normal, pointing back into the flight volume.</summary>
    public required Vec3 DetectorNormal { get; init; }

    /// <summary>Transport mode.</summary>
    public required string TransportMode { get; init; }

    /// <summary>
    /// The instrument's timeline, with the mode each phase runs in. Empty when the
    /// model declares no sequence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="TransportMode"/> is the model's own, and a phase that names none keeps
    /// it - the same rule a phase's parameter overrides follow. So a model with no
    /// sequence, and one whose every phase runs in the declared mode, are the same run.
    /// </para>
    /// <para>
    /// A phase boundary where the mode <em>changes</em> is SEQ-1's subject: the packet
    /// has to be converted from one description to the other, and the conversion is
    /// lossy in one direction and inventive in the other.
    /// </para>
    /// </remarks>
    public IReadOnlyList<CompiledPhase> Phases { get; init; } = [];

    /// <summary>Whether the timeline changes transport mode at any boundary.</summary>
    /// <remarks>
    /// Distinct from having a sequence at all. A trap that holds and then extracts,
    /// both in the trajectory description, is sequenced and needs no conversion; only a
    /// boundary where the mode differs does.
    /// </remarks>
    public bool ChangesTransportMode =>
        Phases.Count > 1
        && Phases.Zip(Phases.Skip(1)).Any(
            pair => !string.Equals(pair.First.Mode, pair.Second.Mode, StringComparison.Ordinal));

    /// <summary>
    /// Every transport mode this run uses: the model's own and every phase's.
    /// </summary>
    /// <remarks>
    /// A phase that names no mode keeps the model's, which the compile has already
    /// resolved, so the model's mode is always in the set. What this adds is the modes a
    /// sequence introduces - the same set the validator gathers to make "the diffusive mode
    /// needs a gas" a statement about the run rather than about one declaration in it.
    /// </remarks>
    public IReadOnlySet<string> Modes =>
        new HashSet<string>(
            Phases.Select(p => p.Mode).Append(TransportMode), StringComparer.Ordinal);

    /// <summary>
    /// Whether the run has to be walked phase by phase rather than flown in one mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The question the run fork actually needs, which is not
    /// <see cref="ChangesTransportMode"/>.</b> That one asks whether two <em>adjacent</em>
    /// phases differ, and needs at least two phases to ask it - so a single diffusive phase
    /// on a model declaring `trajectory`, or a sequence every phase of which declares
    /// `diffusion` on such a model, is not a change by that reading. Routing on it sent
    /// those down the trajectory path and ignored the timeline outright: the model asked for
    /// a density and got a single-ion flight, exit 0, with nothing saying the sequence had
    /// been skipped.
    /// </para>
    /// <para>
    /// <b>Any diffusive phase forces it</b>, because the trajectory path cannot step a
    /// density - there is no ambiguity to trade off, only a description the other path does
    /// not implement. A sequence that is trajectory throughout deliberately does NOT force
    /// it: the integrator asks the field for the instant it is at, so a trap that holds and
    /// then extracts is already correct there, and sending it through the sequenced path
    /// would swap a working route for a new one to no purpose.
    /// </para>
    /// </remarks>
    public bool NeedsSequencedTransport =>
        Phases.Count > 0
        && (ChangesTransportMode
            || Modes.Contains("diffusion"));

    /// <summary>Relative tolerance for the integrator.</summary>
    public required double RelativeTolerance { get; init; }

    /// <summary>Flight-time ceiling, in seconds.</summary>
    public required double MaximumFlightTimeSi { get; init; }

    /// <summary>Trajectory sampling interval, in seconds.</summary>
    public required double SampleIntervalSi { get; init; }

    /// <summary>The background gas; vacuum when none is declared.</summary>
    public CompiledGas Gas { get; init; } = CompiledGas.Vacuum;

    /// <summary>Ion mobility, for the diffusive mode. Null when none applies.</summary>
    public CompiledMobility? Mobility { get; init; }

    /// <summary>
    /// The ion populations, where the model declared a mixture; empty where it declared one ion.
    /// </summary>
    /// <remarks>
    /// Empty rather than a list of one for the single-ion case, so that "is this a mixture" is a
    /// question with one answer. A caller wanting the ion regardless asks
    /// <see cref="MassSi"/>/<see cref="ChargeSi"/>, which a mixture leaves at the first
    /// population's values - enough for the diagnostics that need <em>an</em> ion, and never
    /// enough to be mistaken for all of them.
    /// </remarks>
    public IReadOnlyList<CompiledSpecies> Species { get; init; } = [];

    /// <summary>Whether the model describes several ion populations at once.</summary>
    public bool IsMixture => Species.Count > 1;

    /// <summary>The density grid, for the diffusive mode. Null when none applies.</summary>
    public CompiledDensityGrid? DensityGrid { get; init; }

    /// <summary>
    /// The resolved parameter surface this model was compiled from. What a sweep
    /// perturbs and an optimiser searches.
    /// </summary>
    public required ParameterSurface Parameters { get; init; }

    /// <summary>
    /// How wide a cloud the source launches. A single ion on the axis by default.
    /// </summary>
    public IonCloudSettings Cloud { get; init; } = new();

    /// <summary>
    /// Whether the packet's ions push on each other: <c>none</c> or <c>direct</c>.
    /// </summary>
    public string SpaceChargeMode { get; init; } = "none";

    /// <summary>Whether the mutual Coulomb force is being modelled.</summary>
    public bool ModelsSpaceCharge =>
        string.Equals(SpaceChargeMode, "direct", StringComparison.Ordinal)
        || string.Equals(SpaceChargeMode, "pic", StringComparison.Ordinal);

    /// <summary>
    /// Whether the density's own charge enters its field: the diffusive mode's method.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="ModelsSpaceCharge"/> deliberately, and neither is the
    /// general question. That property is the predicate the <em>trajectory</em> path uses to
    /// reach for a packet integrator, and a mean field has no packet to integrate: widening
    /// it to mean "space charge of any kind" would send a diffusive model down a path with no
    /// method for it. A reader wanting "does this run model charge at all" wants both.
    /// </remarks>
    public bool ModelsMeanField =>
        string.Equals(SpaceChargeMode, "meanField", StringComparison.Ordinal);

    /// <summary>
    /// The grid a particle-in-cell solve uses, or null where the method is not it.
    /// </summary>
    public CompiledSpaceChargeGrid? SpaceChargeGrid { get; init; }

    /// <summary>How a diffusive run advances its density in time.</summary>
    public CompiledDensityStep DensityStep { get; init; } = new("explicit", 1.0);

    /// <summary>
    /// Whether a trajectory lands on every declared waveform edge (<c>transport.edges</c>
    /// is <c>land</c>) rather than only on stage switches (<c>cap</c>, the default).
    /// </summary>
    public bool LandOnEdges { get; init; }

    /// <summary>The ion's launch speed, in metres per second.</summary>
    /// <returns>The speed after acceleration, including the energy offset.</returns>
    /// <remarks>
    /// Derived rather than stored, so the document cannot state an energy and a
    /// velocity that disagree.
    /// </remarks>
    public double LaunchSpeedSi()
    {
        var energy = Math.Abs(ChargeSi) * Math.Abs(AccelerationPotentialSi) * (1.0 + EnergyFraction);
        return Math.Sqrt(2.0 * energy / MassSi);
    }
}

/// <summary>The kinds of field element schema v0.1 can express.</summary>
public enum CompiledFieldKind
{
    /// <summary>Empty space.</summary>
    FieldFree,

    /// <summary>A uniform field filling all space.</summary>
    Uniform,

    /// <summary>Field-free on one side of a plane, uniform and retarding on the other.</summary>
    HalfSpaceUniform,

    /// <summary>
    /// The ideal quadrupole field, driven: exactly quadrupolar by construction.
    /// </summary>
    /// <remarks>
    /// The only <em>analytic</em> driven field the format offers, and it exists for the
    /// reason the analytic tier exists at all - a closed form to check a solved geometry
    /// against. Every other driven field here is solved, so an expectation written from
    /// the ideal formula would have been asserting a modelling difference of a few per
    /// cent as though it were arithmetic.
    /// </remarks>
    IdealQuadrupoleRf,

    /// <summary>
    /// A harmonic axial well superposed on a logarithmic radial one, satisfying
    /// Laplace exactly.
    /// </summary>
    /// <remarks>
    /// The field an orbital trap is built from. Named for its mathematics rather than
    /// the instrument, because architecture invariant 2 keeps device names above
    /// <c>Einzel.Library</c>.
    /// </remarks>
    QuadroLogarithmic,

    /// <summary>
    /// A field solved from a Dirichlet geometry given in the document. The element
    /// that lets a device be a template rather than a class (LIB-1).
    /// </summary>
    Solved2D,

    /// <summary>
    /// The same, in three dimensions, for a device with no symmetry to exploit.
    /// </summary>
    Solved3D,
}

/// <summary>
/// One phase of the instrument's timeline, as the run sees it.
/// </summary>
/// <param name="Name">What the phase is for.</param>
/// <param name="DurationSeconds">How long it lasts.</param>
/// <param name="Mode">The transport mode it runs in.</param>
/// <param name="EndsAtSeconds">When it ends, cumulative from zero.</param>
/// <param name="Ramps">
/// The parameters this phase ramps, with where each starts and ends; empty for a phase
/// that holds.
/// </param>
/// <remarks>
/// <para>
/// The elements each carry their own per-phase states, which is what the field
/// assembly needs. This is what the <em>run</em> needs: the schedule, and the mode each
/// phase is described in.
/// </para>
/// <para>
/// A mode belongs here rather than on an element because it is a property of the run.
/// Two elements naming different modes for one instant is not something a superposition
/// can resolve, the way it resolves two fields.
/// </para>
/// <para>
/// The ramps are here for the same reason: a figure of merit read against a scan - a
/// mobility resolving power against an elution ramp - needs to know what the instrument
/// was doing at the instant an ion arrived, and the elements only know their own
/// weights. A reader that wants the ramped parameter's value at an instant interpolates
/// linearly between <see cref="CompiledRamp.Start"/> and <see cref="CompiledRamp.End"/>
/// across the phase, which is exactly what the field does.
/// </para>
/// </remarks>
public sealed record CompiledPhase(
    string Name,
    double DurationSeconds,
    string Mode,
    double EndsAtSeconds,
    IReadOnlyList<CompiledRamp> Ramps);

/// <summary>One parameter a phase ramps linearly from where it stands to a declared end.</summary>
/// <param name="Parameter">The parameter's name, as the document declares it.</param>
/// <param name="Start">Its value when the phase begins, in SI.</param>
/// <param name="End">Its value when the phase ends, in SI.</param>
/// <remarks>
/// The start is whatever was in force - the phase's own <c>set</c>, or the value
/// inherited from the phase before - so a document ramping "from wherever it was" and
/// one setting the start explicitly compile to the same record.
/// </remarks>
public sealed record CompiledRamp(string Parameter, Quantity Start, Quantity End);

/// <summary>An axis-aligned box in metres, outside which a field element is silent.</summary>
/// <param name="MinX">Lower bound along x.</param>
/// <param name="MaxX">Upper bound along x.</param>
/// <param name="MinY">Lower bound along y.</param>
/// <param name="MaxY">Upper bound along y.</param>
/// <param name="MinZ">Lower bound along z.</param>
/// <param name="MaxZ">Upper bound along z.</param>
/// <param name="FringeSi">
/// Depth inside every face over which the element rises from nothing to full strength, in
/// metres; zero for a hard edge.
/// </param>
public sealed record FieldRegion(
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    double MinZ,
    double MaxZ,
    double FringeSi = 0.0)
{
    /// <summary>
    /// How much of the element applies at a point: one deep inside, nothing outside, and
    /// rising linearly through a band <see cref="FringeSi"/> deep inside every face.
    /// </summary>
    /// <param name="position">Where to evaluate, in metres.</param>
    /// <returns>A factor between zero and one.</returns>
    /// <remarks>
    /// A region with no fringe is a step, and a step in the potential at the face is what a
    /// bounded element has always cost (the <c>field.region-potential-step</c> warning). A
    /// fringe makes the potential continuous - zero at the face, the element's own a fringe
    /// inside - and gives the field there a gradient it can act through, which for a
    /// confining RF is the difference between a wall and an entrance: an ion approaching
    /// the hard edge of a pseudopotential well at any radius meets the whole well at once
    /// and is held there, while one approaching a ramp is squeezed toward the axis as the
    /// well grows under it. It stands in for the decay of a real electrode's field over
    /// about a bore radius, and is a modelling choice rather than anything solved.
    /// </remarks>
    public double Scale(in Vec3 position)
    {
        var depth = -SignedDistance(in position);

        if (depth < 0.0)
        {
            return 0.0;
        }

        return FringeSi <= 0.0 ? 1.0 : Math.Min(1.0, depth / FringeSi);
    }

    /// <summary>
    /// The gradient of <see cref="Scale"/>: zero outside and deep inside, and one over the
    /// fringe along the inward normal of the nearest face across the fringe band.
    /// </summary>
    /// <param name="position">Where to evaluate, in metres.</param>
    /// <returns>The gradient, in inverse metres.</returns>
    public Vec3 ScaleGradient(in Vec3 position)
    {
        if (FringeSi <= 0.0)
        {
            return Vec3.Zero;
        }

        var depth = -SignedDistance(in position);

        if (depth < 0.0 || depth >= FringeSi)
        {
            return Vec3.Zero;
        }

        // The depth is the distance to the nearest face, so its gradient is that face's
        // inward normal. Ties at an edge take the first axis, which is a measure-zero set.
        var toMinX = position.X - MinX;
        var toMaxX = MaxX - position.X;
        var toMinY = position.Y - MinY;
        var toMaxY = MaxY - position.Y;
        var toMinZ = position.Z - MinZ;
        var toMaxZ = MaxZ - position.Z;
        var nearest = Math.Min(Math.Min(Math.Min(toMinX, toMaxX), Math.Min(toMinY, toMaxY)), Math.Min(toMinZ, toMaxZ));
        var inward =
            nearest == toMinX ? new Vec3(1.0, 0.0, 0.0)
            : nearest == toMaxX ? new Vec3(-1.0, 0.0, 0.0)
            : nearest == toMinY ? new Vec3(0.0, 1.0, 0.0)
            : nearest == toMaxY ? new Vec3(0.0, -1.0, 0.0)
            : nearest == toMinZ ? new Vec3(0.0, 0.0, 1.0)
            : new Vec3(0.0, 0.0, -1.0);

        return inward * (1.0 / FringeSi);
    }

    /// <summary>Signed distance to the boundary: negative inside, positive outside.</summary>
    /// <param name="position">Where to evaluate, in metres.</param>
    /// <returns>The signed distance in metres.</returns>
    /// <remarks>
    /// The exact box distance rather than a cheaper approximation, because the
    /// integrator finds the boundary by bracketing this function's zero and lands on it:
    /// a distance that is only right near the faces would put the landing in the wrong
    /// place near an edge.
    /// </remarks>
    public double SignedDistance(in Vec3 position)
    {
        var dx = Math.Max(MinX - position.X, position.X - MaxX);
        var dy = Math.Max(MinY - position.Y, position.Y - MaxY);
        var dz = Math.Max(MinZ - position.Z, position.Z - MaxZ);

        // Outside along at least one axis: the distance is the length of the positive
        // part, which is exact at faces, edges and corners alike.
        if (dx > 0.0 || dy > 0.0 || dz > 0.0)
        {
            var ox = Math.Max(dx, 0.0);
            var oy = Math.Max(dy, 0.0);
            var oz = Math.Max(dz, 0.0);

            return Math.Sqrt((ox * ox) + (oy * oy) + (oz * oz));
        }

        // Inside: the least distance to any face, negative.
        return Math.Max(dx, Math.Max(dy, dz));
    }

    /// <summary>Whether a point is inside, boundary included.</summary>
    /// <param name="position">Where to test, in metres.</param>
    /// <returns>True when the point is within the box.</returns>
    public bool Contains(in Vec3 position) => SignedDistance(in position) <= 0.0;
}


/// <summary>A validated field element, in SI.</summary>
public sealed record CompiledField
{
    /// <summary>Which kind of element this is.</summary>
    public required CompiledFieldKind Kind { get; init; }

    /// <summary>Steady potential on the x pair, in volts. The y pair takes its negative.</summary>
    public double DirectPotentialSi { get; init; }

    /// <summary>Zero-to-peak drive amplitude on the x pair, in volts.</summary>
    public double DriveAmplitudeSi { get; init; }

    /// <summary>Drive frequency, in hertz.</summary>
    public double DriveFrequencySi { get; init; }

    /// <summary>Axis to nearest electrode surface, in metres.</summary>
    public double InscribedRadiusSi { get; init; }

    /// <summary>
    /// Ideal quadrupole only: the axis the field is invariant along. Z unless declared,
    /// which keeps every document written before the attribute existed bit-identical.
    /// </summary>
    public CylinderAxis Axis { get; init; } = CylinderAxis.Z;

    /// <summary>Axial potential curvature, in volts per metre squared.</summary>
    /// <remarks>
    /// The one number an orbital analyser's frequency comes from: the axial motion is
    /// <c>m z'' = -q k z</c>, so <c>omega = sqrt(q k / m)</c> and nothing else enters.
    /// </remarks>
    public double CurvatureSi { get; init; }

    /// <summary>The radius at which the radial field vanishes, in metres.</summary>
    public double CharacteristicRadiusSi { get; init; }

    /// <summary>Where the axial well is centred, on the axis of symmetry.</summary>
    public Vec3 Centre { get; init; }

    /// <summary>
    /// A box outside which this element contributes nothing, in metres. Null when the
    /// element is unbounded, which is what an analytic field is by default.
    /// </summary>
    public FieldRegion? Region { get; init; }

    /// <summary>
    /// This element as it stands during each phase of the instrument's timeline, when
    /// the timeline changes it. Empty otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>For the analytic kinds, which have nowhere else to put a phase.</b> A solved
    /// geometry carries its phases in <see cref="CompiledSolvedField.Stages"/>, because
    /// there a phase re-weights channels that are already solved. An analytic element has
    /// no channels: a phase simply gives it different numbers, so it needs a whole
    /// compiled copy per phase.
    /// </para>
    /// <para>
    /// <b>Empty when the timeline does not reach this element</b>, which is a real
    /// distinction rather than an optimisation. An element whose expressions do not
    /// depend on any parameter a phase sets is genuinely static, and wrapping it in a
    /// switch would give the assembly extra switch instants to land on and make a
    /// static element answer a time-varying interface for no reason.
    /// </para>
    /// </remarks>
    public IReadOnlyList<CompiledField> Phases { get; init; } = [];

    /// <summary>
    /// The instant each phase ends, cumulative from zero. Empty when there is no
    /// timeline, and otherwise one entry per phase.
    /// </summary>
    public IReadOnlyList<double> PhaseBoundariesSeconds { get; init; } = [];

    /// <summary>Uniform only: the field vector, in volts per metre.</summary>
    public Vec3 Field { get; init; }

    /// <summary>Half-space only: a point on the boundary plane, in metres.</summary>
    public Vec3 PlanePoint { get; init; }

    /// <summary>Half-space only: unit normal pointing into the field region.</summary>
    public Vec3 InwardNormal { get; init; }

    /// <summary>Half-space only: potential gradient inside the region, in volts per metre.</summary>
    public double PotentialGradientSi { get; init; }

    /// <summary>Half-space only: the turning depth the gradient was derived from, in metres.</summary>
    public double TurningDepthSi { get; init; }

    /// <summary>Solved-3D only: the geometry to solve.</summary>
    public CompiledSolvedField3D? Solve3D { get; init; }

    /// <summary>Solved only: the geometry to solve.</summary>
    public CompiledSolvedField? Solve { get; init; }
}

/// <summary>
/// The background gas, compiled to SI.
/// </summary>
/// <remarks>
/// Lives in Einzel.Core because the model format declares it and validation has to
/// check it; the collision machinery that consumes it lives in Einzel.Transport,
/// which is where an <c>ITransportMode</c> implementation belongs.
/// </remarks>
public sealed record CompiledGas
{
    /// <summary>No gas at all.</summary>
    public static CompiledGas Vacuum { get; } = new();

    /// <summary><c>none</c>, <c>hardSphere</c>, or <c>langevin</c>.</summary>
    public string Model { get; init; } = "none";

    /// <summary>Pressure, in pascals.</summary>
    public double PressureSi { get; init; }

    /// <summary>Temperature, in kelvin.</summary>
    public double TemperatureK { get; init; } = 300.0;

    /// <summary>Mass of one neutral, in kilograms.</summary>
    public double MassSi { get; init; }

    /// <summary>Collision cross section, in square metres.</summary>
    public double CrossSectionSi { get; init; }

    /// <summary>Polarizability volume, in cubic metres.</summary>
    public double PolarizabilitySi { get; init; }

    /// <summary>Bulk gas velocity, in metres per second.</summary>
    public Vec3 DriftVelocitySi { get; init; }

    /// <summary>
    /// Path to an imported velocity field, as declared, or null.
    /// </summary>
    /// <remarks>
    /// Carried as written rather than resolved, because resolving it needs the
    /// model file's own directory and validation does not read files. Whoever loads
    /// the model resolves it; a caller that cannot is refused rather than quietly
    /// given a gas that stands still - see <c>DiffusionRun.Execute</c>.
    /// </remarks>
    public string? VelocityFieldPath { get; init; }

    /// <summary>Which array in that file holds the velocity, or null for the first.</summary>
    public string? VelocityFieldArray { get; init; }

    /// <summary>Whether a velocity field was declared.</summary>
    public bool HasVelocityField => !string.IsNullOrWhiteSpace(VelocityFieldPath);

    /// <summary>Path to an imported pressure field, as declared, or null.</summary>
    /// <remarks>
    /// Carried as written, for the reason the velocity field's path is: resolving it
    /// needs the model file's own directory and validation does not read files.
    /// </remarks>
    public string? PressureFieldPath { get; init; }

    /// <summary>Which array in that file holds the pressure, or null for the first.</summary>
    public string? PressureFieldArray { get; init; }

    /// <summary>
    /// What one of the file's numbers is in pascals - 1 for Pa, 100 for mbar.
    /// </summary>
    /// <remarks>
    /// Resolved at validation, where the unit registry is, so that a bad unit is an
    /// AGT-3 error against the document rather than a surprise at load time. The
    /// array itself stays in whatever the file wrote.
    /// </remarks>
    public double PressureFieldScale { get; init; } = 1.0;

    /// <summary>Whether a pressure field was declared.</summary>
    public bool HasPressureField => !string.IsNullOrWhiteSpace(PressureFieldPath);

    /// <summary>Seed for the collision random stream.</summary>
    public int Seed { get; init; } = 20_240_101;

    /// <summary>Whether this gas does anything.</summary>
    public bool IsPresent => Model != "none" && PressureSi > 0.0;
}

/// <summary>Ion mobility, compiled to SI.</summary>
/// <param name="ZeroFieldSi">Zero-field mobility, in square metres per volt-second.</param>
/// <param name="Alpha">Quadratic coefficient of the reduced-field expansion.</param>
/// <param name="ValidToTownsend">The reduced field the expansion was fitted to.</param>
/// <param name="Derived">
/// Whether this was derived from a cross section rather than declared. A derived
/// value carries the cross section's uncertainty plus a first-order Chapman-Enskog
/// approximation, and TRN-1 wants the number declared, so a result computed from a
/// derived one says which it was.
/// </param>
public sealed record CompiledMobility(
    double ZeroFieldSi,
    double Alpha,
    double ValidToTownsend,
    bool Derived);

/// <summary>One ion population of a mixture, validated and in SI.</summary>
/// <param name="Name">What it is called, and what its results are reported under.</param>
/// <param name="MassSi">Mass, in kilograms.</param>
/// <param name="ChargeSi">Charge, in coulombs, signed.</param>
/// <param name="Mobility">
/// Its mobility, or a derived marker: Mason-Schamp needs the ion assembled and validation does
/// not have it, so the derivation happens where the run does.
/// </param>
/// <param name="Population">How many real ions of it there are.</param>
public sealed record CompiledSpecies(
    string Name,
    double MassSi,
    double ChargeSi,
    CompiledMobility Mobility,
    double Population);


/// <summary>The region a density is tracked over, compiled to SI.</summary>
/// <param name="MinX">Lower x, in metres.</param>
/// <param name="MinY">Lower y, in metres.</param>
/// <param name="MaxX">Upper x, in metres.</param>
/// <param name="MaxY">Upper y, in metres.</param>
/// <param name="IntervalsX">Cells across x.</param>
/// <param name="IntervalsY">Cells across y.</param>
public sealed record CompiledDensityGrid(
    double MinX, double MinY, double MaxX, double MaxY, int IntervalsX, int IntervalsY);

/// <summary>A particle-in-cell space-charge grid, validated and in SI.</summary>
/// <param name="Nodes">Nodes across the box.</param>
/// <param name="Padding">Box half-width as a multiple of the packet's RMS radius.</param>
/// <param name="RefreshTolerance">Fractional change in RMS radius that forces a solve.</param>
public sealed record CompiledSpaceChargeGrid(int Nodes, double Padding, double RefreshTolerance);

/// <summary>A density time-stepping choice, validated.</summary>
/// <param name="Scheme"><c>explicit</c> or <c>implicit</c>.</param>
/// <param name="Gain">How many times the explicit stability limit to step.</param>
public sealed record CompiledDensityStep(string Scheme, double Gain)
{
    /// <summary>Whether the implicit scheme was asked for.</summary>
    public bool IsImplicit => string.Equals(Scheme, "implicit", StringComparison.Ordinal);
}
