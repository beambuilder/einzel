namespace Einzel.Core.Model;

/// <summary>
/// A model document: the declarative, schema-versioned, diffable description of
/// a modelling problem.
/// </summary>
/// <remarks>
/// <para>
/// AGT-1: "The model is text. Declarative, schema-validated, diffable JSON. A
/// model file plus referenced artifacts fully determines a run."
/// </para>
/// <para>
/// Schema v0.1 is deliberately narrow. It expresses what the analytic tier needs
/// — an ion, a source, a stack of closed-form field elements, a detector, and
/// transport settings — and nothing more. Spec section 21 makes schema stability
/// a Phase 1 review gate and section 22 lists schema churn as a risk that breaks
/// agent workflows, so the format grows by adding validated cases rather than by
/// reinterpreting existing ones. Every bump ships a migration and a test that the
/// prior corpus still loads (section 14).
/// </para>
/// <para>
/// No device class appears anywhere in this document. A reflectron is a
/// half-space field plus a drift length plus a detector placement, and that
/// arrangement is what a template in Einzel.Library will name — not something
/// the schema knows about (LIB-1).
/// </para>
/// </remarks>
public sealed record ModelDocument
{
    /// <summary>The schema version this document is written against.</summary>
    public string SchemaVersion { get; init; } = ModelSchema.CurrentVersion;

    /// <summary>A short name. Used in output and in generated file names.</summary>
    public string? Name { get; init; }

    /// <summary>Prose description, carried through to results and figures.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// The declared parameter surface (LIB-1). Names, units, bounds, and derived
    /// expressions; what a sweep varies and an optimiser searches.
    /// </summary>
    public IReadOnlyDictionary<string, ParameterDocument>? Parameters { get; init; }

    /// <summary>The ion being tracked.</summary>
    public IonDocument? Ion { get; init; }

    /// <summary>
    /// Several ion populations, where the model is of a mixture rather than of one ion.
    /// </summary>
    /// <remarks>
    /// Mutually exclusive with <see cref="Ion"/>: a document that declared both would be saying
    /// two things about what is being transported, and there is no reading under which one of
    /// them is a default for the other. Refused rather than merged, which is the same rule that
    /// refuses a geometry declaring both <c>drive</c> and <c>drives</c>.
    /// </remarks>
    public IReadOnlyList<SpeciesDocument>? Species { get; init; }


    /// <summary>Where the ion starts, and with what energy.</summary>
    public SourceDocument? Source { get; init; }

    /// <summary>
    /// The field elements, superposed. Order is irrelevant to the physics and is
    /// preserved only so a diff stays readable.
    /// </summary>
    public IReadOnlyList<FieldDocument>? Fields { get; init; }

    /// <summary>
    /// The instrument as a timed state machine: ordered phases, each with a duration
    /// and the parameter values that hold during it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 9's words are that "an instrument is a timed state machine", and the
    /// emphasis is on <i>instrument</i>. A phase holds across the whole model, so every
    /// element follows it - which is what makes setting one parameter move everything
    /// written over it, including derived parameters and including elements other than
    /// the one the timeline was written next to.
    /// </para>
    /// <para>
    /// How an element follows depends on what it is. A solved geometry re-weights the
    /// channels it has already solved; an analytic one is compiled once per phase and
    /// switched. An element no phase moves stays static rather than being wrapped.
    /// </para>
    /// <para>
    /// A phase sets <b>parameters</b>, not electrode settings. Potentials are already
    /// expressions over parameters, so this costs no new vocabulary: the same override
    /// mechanism a sweep uses to perturb a design is what a sequence uses to operate one.
    /// </para>
    /// <para>
    /// A single-element model may still declare <c>stages</c> on its solve, which is the
    /// older spelling and means the same thing. Declaring both is refused rather than
    /// merged: an instrument has one timeline.
    /// </para>
    /// </remarks>
    public IReadOnlyList<StageDocument>? Sequence { get; init; }

    /// <summary>The surface that ends the flight.</summary>
    public DetectorDocument? Detector { get; init; }

    /// <summary>Transport mode and its settings.</summary>
    public TransportDocument? Transport { get; init; }

    /// <summary>Element-wise equality, including the field list.</summary>
    /// <param name="other">The document to compare against.</param>
    /// <returns><see langword="true"/> when the documents describe the same model.</returns>
    /// <remarks>
    /// The compiler-generated equality would compare <see cref="Fields"/> by
    /// reference, so two documents parsed from identical text would be unequal.
    /// Note that model identity for drift detection is the content hash of the
    /// text (PRJ-2, GRD-10), not this — but an equality operator that silently
    /// means something other than what it reads as is worth not shipping.
    /// </remarks>
    public bool Equals(ModelDocument? other) =>
        other is not null
        && ParametersEqual(Parameters, other.Parameters)
        && string.Equals(SchemaVersion, other.SchemaVersion, StringComparison.Ordinal)
        && string.Equals(Name, other.Name, StringComparison.Ordinal)
        && string.Equals(Description, other.Description, StringComparison.Ordinal)
        && Equals(Ion, other.Ion)
        && (ReferenceEquals(Species, other.Species)
            || (Species is not null && other.Species is not null && Species.SequenceEqual(other.Species)))
        && Equals(Source, other.Source)
        && Equals(Detector, other.Detector)
        && Equals(Transport, other.Transport)
        && (ReferenceEquals(Fields, other.Fields)
            || (Fields is not null && other.Fields is not null && Fields.SequenceEqual(other.Fields)));

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaVersion, StringComparer.Ordinal);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(Description, StringComparer.Ordinal);
        hash.Add(Ion);

        foreach (var member in Species ?? [])
        {
            hash.Add(member);
        }

        hash.Add(Source);
        hash.Add(Detector);
        hash.Add(Transport);

        foreach (var field in Fields ?? [])
        {
            hash.Add(field);
        }

        foreach (var parameter in (Parameters ?? new Dictionary<string, ParameterDocument>())
            .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            hash.Add(parameter.Key, StringComparer.Ordinal);
            hash.Add(parameter.Value);
        }

        return hash.ToHashCode();
    }

    private static bool ParametersEqual(
        IReadOnlyDictionary<string, ParameterDocument>? left,
        IReadOnlyDictionary<string, ParameterDocument>? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Count != right.Count)
        {
            return false;
        }

        foreach (var (name, value) in left)
        {
            if (!right.TryGetValue(name, out var other) || !Equals(value, other))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Schema identity and version policy.</summary>
public static class ModelSchema
{
    /// <summary>The schema version this build writes.</summary>
    /// <remarks>
    /// 0.3 adds the source cloud, 0.5 the mutual Coulomb force, 0.6 the model-level
    /// sequence, 0.8 a tilt on a cross-section's extrusion axis, 0.9 the polygon
    /// electrode. All additive, so every
    /// earlier document still reads - but a document whose ions push on each other
    /// genuinely is not a 0.4 document, and saying so is cheaper than an older build
    /// reading it, ignoring the field it does not know, and reporting a different flight
    /// with no indication that anything was dropped. A tilted extrusion axis is the same
    /// case in a sharper form: an older build would solve the untilted cross-section and
    /// report a converging analyser as a parallel one.
    /// </remarks>
    public const string CurrentVersion = "0.14";

    /// <summary>Versions this build can read.</summary>
    /// <remarks>
    /// Membership, not ordering: a version is supported because it is in this list and a
    /// test asserts that every entry actually reads. So <c>0.10</c> follows <c>0.9</c> even
    /// though it sorts before it as text, and nothing here compares two versions.
    /// </remarks>
    public static IReadOnlyList<string> SupportedVersions { get; } =
        ["0.1", "0.2", "0.3", "0.4", "0.5", "0.6", "0.7", "0.8", "0.9", "0.10", "0.11", "0.12",
         "0.13", "0.14"];
}

/// <summary>The ion being tracked.</summary>
public sealed record IonDocument
{
    /// <summary>
    /// Mass-to-charge ratio, in daltons per elementary charge. The form mass
    /// spectrometry quotes.
    /// </summary>
    public QuantityValue? MassToCharge { get; init; }

    /// <summary>
    /// Charge number. Positive for cations, negative for anions, never zero.
    /// </summary>
    public int ChargeNumber { get; init; } = 1;
}


/// <summary>
/// One ion population in a mixture: the plural of <see cref="IonDocument"/>, carrying what it
/// is, how it moves and how much of it there is.
/// </summary>
/// <remarks>
/// <para>
/// A model declares either <c>ion</c> or <c>species</c>, never both. One ion is the case almost
/// every model is, and a list of one would be a worse way to say it; a mixture is what a real
/// source produces and what a mobility analyser exists to separate, and it cannot be said at
/// all with a singular field.
/// </para>
/// <para>
/// <b>The mobility lives here rather than under <c>transport</c>.</b> Mass, charge and mobility
/// are one ion's three properties and a mixture needs a set of them per population; leaving the
/// mobility where it was would make a document say which ions are present in one place and how
/// each of them moves in another, with nothing tying the two lists together. So
/// <c>transport.mobility</c> beside <c>species</c> is refused rather than treated as a default.
/// </para>
/// </remarks>
public sealed record SpeciesDocument
{
    /// <summary>
    /// What to call this population. Results are reported by it, so it must be distinct.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>Mass-to-charge ratio, in daltons per elementary charge.</summary>
    public QuantityValue? MassToCharge { get; init; }

    /// <summary>Charge number. Positive for cations, negative for anions, never zero.</summary>
    public int ChargeNumber { get; init; } = 1;

    /// <summary>
    /// This population's mobility. Omit it to derive one from the gas cross section, which is
    /// then Mason-Schamp for <em>this</em> species' mass rather than a number shared with the
    /// others.
    /// </summary>
    public MobilityDocument? Mobility { get; init; }

    /// <summary>
    /// How many real ions of this population there are. Required, because what a mixture is
    /// for is that the populations push on one another, and a population with no size cannot.
    /// </summary>
    public double Population { get; init; }
}

/// <summary>Where the ion starts, and with what energy.</summary>
public sealed record SourceDocument
{
    /// <summary>Starting position.</summary>
    public VectorValue? Position { get; init; }

    /// <summary>Direction of travel; normalised on load.</summary>
    public DirectionValue? Direction { get; init; }

    /// <summary>
    /// The potential the ion was accelerated through. Converted to a speed using
    /// the ion's own mass and charge, so the document never states a velocity
    /// that could disagree with the energy.
    /// </summary>
    public QuantityValue? AccelerationPotential { get; init; }

    /// <summary>
    /// Fractional offset from the nominal energy, for acceptance studies. Zero by
    /// default; the memo asks for plus or minus 3 to 5 percent.
    /// </summary>
    public double EnergyFraction { get; init; }

    /// <summary>
    /// How wide a cloud of ions to launch. Omit for a single ion on the axis.
    /// </summary>
    public CloudDocument? Cloud { get; init; }
}

/// <summary>
/// The spread of a launched ion cloud, as it appears in a model document.
/// </summary>
/// <remarks>
/// <para>
/// Without this every result is an answer about one ion rather than about an
/// instrument, which is why every resolving power in this project has carried the
/// same caveat: energy aberration only, no spatial spread, no angular spread, no
/// turn-around time.
/// </para>
/// <para>
/// A temperature and <see cref="Divergence"/> are separate settings and may be
/// declared together. A thermal draw moves speed and direction in a fixed ratio;
/// a divergence tilts the direction and leaves the speed alone, as an aperture
/// does. (This remark once said there was no divergence setting.)
/// </para>
/// </remarks>
public sealed record CloudDocument
{
    /// <summary>
    /// How many trajectories to compute. A numerical setting: sampling harder only
    /// makes a statistic better.
    /// </summary>
    public int Ions { get; init; } = 1;

    /// <summary>
    /// How many ions are in the physical packet, which is what pushes on itself.
    /// Defaults to <c>ions</c>.
    /// </summary>
    /// <remarks>
    /// Set this to 1 when sampling an intrinsic source property one ion at a time,
    /// so that launching ten thousand samples is not read as a bunch of ten
    /// thousand ions. Leave it alone when modelling a real packet.
    /// </remarks>
    public int? Population { get; init; }

    /// <summary>Seed for the draw, so a run is regenerable.</summary>
    public int Seed { get; init; } = 1;

    /// <summary>
    /// Source temperature. Sets the thermal velocity, and with it the turn-around
    /// time that limits a pulsed extraction.
    /// </summary>
    public QuantityValue? Temperature { get; init; }

    /// <summary>Gaussian width of the cloud across the direction of travel.</summary>
    public QuantityValue? TransverseSpread { get; init; }

    /// <summary>Gaussian width of the cloud along the direction of travel.</summary>
    public QuantityValue? LongitudinalSpread { get; init; }

    /// <summary>
    /// Gaussian width of the acceleration energy, as a fraction of nominal.
    /// </summary>
    /// <remarks>
    /// Supply ripple rather than temperature: it varies the energy without varying
    /// the direction, which a temperature cannot express.
    /// </remarks>
    public double EnergyFractionSpread { get; init; }

    /// <summary>
    /// Half-angle of the cone the beam fills, as an angle. Directions are drawn
    /// uniformly in solid angle within it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mirror of <see cref="EnergyFractionSpread"/>: that varies the energy
    /// without varying the direction, and this varies the direction without varying
    /// the energy. A temperature can express neither, because it does both at once
    /// in a fixed ratio.
    /// </para>
    /// <para>
    /// <b>A cone rather than a Gaussian</b>, which is the one decision here worth
    /// arguing. Every other spread on a cloud is Gaussian, because every other one
    /// describes a source. This one describes what an <em>aperture</em> or an
    /// upstream optic left behind, and an aperture truncates rather than weights -
    /// there is a hard largest angle and nothing beyond it. Drawing it Gaussian
    /// would put a tail outside the acceptance the number is naming.
    /// </para>
    /// <para>
    /// <b>Uniform in solid angle, not in angle.</b> A beam filling a round aperture
    /// is uniform over its area, which maps to uniform solid angle - so the density
    /// per unit polar angle goes as sin(theta) and most rays sit near the edge of
    /// the cone. Uniform in theta would concentrate them on the axis and understate
    /// the aberration the cone exists to probe.
    /// </para>
    /// <para>
    /// Declaring this and a temperature together is allowed and they add: a warm
    /// source behind a defining aperture is an ordinary thing to have.
    /// </para>
    /// </remarks>
    public QuantityValue? Divergence { get; init; }
}

/// <summary>An imported neutral velocity field, as it appears in a model.</summary>
/// <remarks>
/// VTK ImageData, which is the format this engine already writes and the one every
/// CFD code can export. Einzel <em>consumes</em> a velocity field and does not
/// compute one - that boundary is deliberate, and is the same one §17 draws around
/// visualisation.
/// </remarks>
public sealed record GasFlowDocument
{
    /// <summary>Path to the .vti file, relative to the model document.</summary>
    public string? Path { get; init; }

    /// <summary>
    /// Which array in the file holds the velocity, or null for the first one.
    /// </summary>
    /// <remarks>
    /// A CFD export usually carries several - pressure, density, velocity - so
    /// naming it is the difference between reading the flow and reading whatever
    /// happened to be written first.
    /// </remarks>
    public string? Array { get; init; }
}

/// <summary>An imported field of gas pressure.</summary>
/// <remarks>
/// Referenced rather than embedded (PRJ-2), like the velocity field: a CFD result is
/// thousands of numbers and a model document is meant to stay small, text and
/// diffable.
/// </remarks>
public sealed record GasPressureFieldDocument
{
    /// <summary>Path to the .vti file, relative to the model document.</summary>
    public string? Path { get; init; }

    /// <summary>
    /// Which array in the file holds the pressure, or null for the first one.
    /// </summary>
    /// <remarks>
    /// A CFD export usually carries several - pressure, density, velocity, a
    /// temperature - so naming it is the difference between reading the pressure and
    /// reading whatever happened to be written first.
    /// </remarks>
    public string? Array { get; init; }

    /// <summary>The unit the file's numbers are in - "Pa", "mbar", "torr".</summary>
    /// <remarks>
    /// <para>
    /// <b>Required, and for the reason a scalar's unit is.</b> Section 9 makes
    /// <c>{"energy": 4000}</c> a validation error on purpose, because unit ambiguity
    /// is the commonest source of silent wrongness and an agent building from prose
    /// is the actor most likely to introduce it. Nothing about that argument weakens
    /// when the number becomes a hundred thousand numbers: vacuum work is quoted in
    /// mbar and torr at least as often as in pascals, and a file read as pascals when
    /// it holds mbar is a gas a hundred times too thin, which looks entirely
    /// plausible.
    /// </para>
    /// <para>
    /// The velocity field has no such field because a CFD velocity is metres per
    /// second essentially always. That is an asymmetry with a reason rather than an
    /// oversight.
    /// </para>
    /// </remarks>
    public string? Unit { get; init; }
}

/// <summary>A field element. The discriminator is <see cref="Type"/>.</summary>
/// <remarks>
/// A single record with a discriminator rather than a polymorphic hierarchy, so
/// that an unknown or misspelled type produces one clear AGT-3 error naming the
/// permitted values, instead of a deserialiser exception that names a .NET type
/// an agent has never heard of.
/// </remarks>
public sealed record FieldDocument
{
    /// <summary>
    /// One of <c>fieldFree</c>, <c>uniform</c>, <c>halfSpaceUniform</c>, or
    /// <c>solved2d</c>.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>Solved only: the domain, electrodes, and boundary conditions.</summary>
    public SolvedFieldDocument? Solve { get; init; }

    /// <summary>
    /// Solved-3D only: the box, the electrodes, and how they are driven.
    /// </summary>
    /// <remarks>
    /// Named with a lower-case d so the camel-case naming policy produces
    /// <c>solve3d</c> rather than <c>solve3D</c>. The generated schema, the
    /// documents and this property have to agree on one spelling, and the one a
    /// person would type is the one worth keeping.
    /// </remarks>
    public SolvedField3DDocument? Solve3d { get; init; }

    /// <summary>Uniform only: the field vector.</summary>
    public VectorValue? Field { get; init; }

    /// <summary>Half-space only: a point on the boundary plane.</summary>
    public VectorValue? PlanePoint { get; init; }

    /// <summary>Half-space only: unit normal pointing into the field region.</summary>
    public DirectionValue? InwardNormal { get; init; }

    /// <summary>Half-space only: the potential reached at <see cref="TurningDepth"/>.</summary>
    public QuantityValue? CapPotential { get; init; }

    /// <summary>
    /// Half-space only: the depth at which <see cref="CapPotential"/> is reached.
    /// An ion accelerated through that potential turns exactly here, which is how
    /// an ion mirror is actually designed.
    /// </summary>
    public QuantityValue? TurningDepth { get; init; }

    /// <summary>
    /// Ideal quadrupole only: the steady potential on the x pair. The y pair takes its
    /// negative, which is what makes the field a quadrupole rather than a quadrupole plus
    /// an offset.
    /// </summary>
    public QuantityValue? DirectPotential { get; init; }

    /// <summary>
    /// Ideal quadrupole only: zero-to-peak drive amplitude on the x pair.
    /// </summary>
    public QuantityValue? DriveAmplitude { get; init; }

    /// <summary>Ideal quadrupole only: the drive frequency.</summary>
    public QuantityValue? DriveFrequency { get; init; }

    /// <summary>
    /// Ideal quadrupole only: the inscribed radius, axis to nearest electrode surface.
    /// </summary>
    /// <remarks>
    /// It sets the field gradient and so the Mathieu parameters, which go as its square -
    /// so this is the one length the working point is most sensitive to.
    /// </remarks>
    public QuantityValue? InscribedRadius { get; init; }

    /// <summary>
    /// Ideal quadrupole only: the axis the field is invariant along, <c>x</c>, <c>y</c> or
    /// <c>z</c>. Absent means <c>z</c>, the transverse plane being x-y as it always was.
    /// </summary>
    /// <remarks>
    /// A mass filter's cross-section lies across z by convention, but a device whose beam
    /// axis is x - every axisymmetric tunnel here, since the half-plane solve puts the axis
    /// of rotation along x - needs the quadrupole across x. The transverse pair follows the
    /// cyclic order: z gives (x, y), x gives (y, z), y gives (z, x), so the potential is
    /// always <c>drive (u^2 - v^2) / r0^2</c> in that pair.
    /// </remarks>
    public string? Axis { get; init; }

    /// <summary>
    /// Quadro-logarithmic only: the axial potential curvature, of dimension volts per
    /// metre squared.
    /// </summary>
    /// <remarks>
    /// It sets the axial oscillation frequency and nothing else, which is the whole
    /// point of the field: <c>omega = sqrt(q k / m)</c> carries no radius, no angular
    /// momentum and no amplitude.
    /// </remarks>
    public QuantityValue? Curvature { get; init; }

    /// <summary>
    /// Quadro-logarithmic only: the radius at which the radial field vanishes. Bound
    /// orbits live inside it.
    /// </summary>
    public QuantityValue? CharacteristicRadius { get; init; }

    /// <summary>
    /// Quadro-logarithmic only: where the axial well is centred. Absent means the
    /// origin.
    /// </summary>
    public VectorValue? Centre { get; init; }

    /// <summary>
    /// A box outside which this element contributes nothing. Analytic elements only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An analytic field has no extent, because a formula does not — it is defined
    /// everywhere. That is harmless while such a field is an idealisation of a whole
    /// instrument, and stops being harmless the moment one is an exact statement of a
    /// real device sitting <b>next to</b> another. A quadro-logarithmic potential grows
    /// as z squared, so an orbital trap declared beside the trap that injects it puts an
    /// enormous field across that trap.
    /// </para>
    /// <para>
    /// A solved element needs no region and may not declare one: it is already bounded by
    /// its own solve domain, and two statements about the same extent is one too many.
    /// </para>
    /// </remarks>
    public RegionDocument? Region { get; init; }
}

/// <summary>An axis-aligned box, given as its two opposite corners.</summary>
/// <remarks>
/// <b>The boundary is a field discontinuity and the potential does not generally match
/// across it</b>, because a box is not an equipotential of anything interesting. The
/// assembly measures the largest potential on the boundary and reports it: that is the
/// energy an ion gains or loses per crossing, and placing the box where the field has
/// decayed is what makes it small. The region says where the idealisation is meant to
/// apply; it is not a conductor.
/// </remarks>
public sealed record RegionDocument
{
    /// <summary>Lower bound along x.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Upper bound along x.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Lower bound along y.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Upper bound along y.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Lower bound along z.</summary>
    public QuantityValue? MinZ { get; init; }

    /// <summary>Upper bound along z.</summary>
    public QuantityValue? MaxZ { get; init; }

    /// <summary>
    /// Optional: a distance inside every face over which the element rises linearly from
    /// nothing to full strength, so the potential is continuous across the face instead of
    /// stepping. Absent means a hard edge, which is what every earlier document had.
    /// </summary>
    /// <remarks>
    /// It stands in for the decay of a real electrode's field over about a bore radius, and
    /// is a modelling choice rather than anything solved - the run says so. For a confining
    /// RF it is the difference between a wall and an entrance: at a hard edge an ion at any
    /// radius meets the whole pseudopotential well at once and is held against it, while a
    /// fringe squeezes it toward the axis as the well grows under it. Must not exceed half
    /// the region's smallest extent, or the element never reaches full strength anywhere.
    /// </remarks>
    public QuantityValue? Fringe { get; init; }
}

/// <summary>The surface that ends the flight.</summary>
public sealed record DetectorDocument
{
    /// <summary>A point on the detector plane.</summary>
    public VectorValue? PlanePoint { get; init; }

    /// <summary>
    /// The plane normal, pointing back toward the flight volume. The flight ends
    /// when the ion crosses from the positive side to the negative side.
    /// </summary>
    public DirectionValue? Normal { get; init; }
}

/// <summary>Transport mode and its settings.</summary>
public sealed record TransportDocument
{
    /// <summary>
    /// <c>trajectory</c> or <c>statisticalDiffusion</c>. REG-1 makes these peer
    /// implementations; only the first exists in this build, and asking for the
    /// second is a clear error rather than a silent substitution.
    /// </summary>
    public string Mode { get; init; } = "trajectory";

    /// <summary>Relative tolerance for the adaptive integrator.</summary>
    public double RelativeTolerance { get; init; } = 1e-11;

    /// <summary>Ceiling on simulated flight time. Required, as a runaway guard.</summary>
    public QuantityValue? MaximumFlightTime { get; init; }

    /// <summary>
    /// Interval at which the trajectory is sampled for rendering and export.
    /// TRJ-1: this stream has its own cadence, independent of integration steps.
    /// </summary>
    public QuantityValue? SampleInterval { get; init; }

    /// <summary>The background gas, or null for vacuum.</summary>
    public GasDocument? Gas { get; init; }

    /// <summary>
    /// Ion mobility, for the diffusive transport mode.
    /// </summary>
    /// <remarks>
    /// TRN-1 makes this an explicit input with stated field dependence, because it
    /// is the one number a diffusive calculation rests on entirely - the drift
    /// velocity, the diffusion coefficient through Einstein, and therefore the
    /// residence time and the spread all come from it. Omitted, it is derived from
    /// the gas cross section by Mason-Schamp and the result says so, since a derived
    /// value carries the cross section's uncertainty plus a first-order
    /// Chapman-Enskog approximation on top.
    /// </remarks>
    public MobilityDocument? Mobility { get; init; }

    /// <summary>
    /// The grid the density is tracked on, for the diffusive mode.
    /// </summary>
    /// <remarks>
    /// Defaults to the domain of the model's solved field, which is nearly always
    /// what is wanted. Declared separately when it is not: a field may be solved
    /// over a larger box than the region ions are followed through, and tracking a
    /// density over the whole of it would spend the run on empty space.
    /// </remarks>
    public DensityGridDocument? DensityGrid { get; init; }

    /// <summary>
    /// Whether the ions in a packet push on each other: <c>none</c> or <c>direct</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>none</c>, the default, flies each ion through a field that does not know
    /// the others exist. That is exactly right for a sparse beam and wrong for a
    /// dense packet, and the run says which it is either way.
    /// </para>
    /// <para>
    /// <c>direct</c> sums every pair, which is the reference method SC-1 names. The
    /// cost is quadratic in the trajectory count and the whole packet must be
    /// advanced in lockstep, so it is opt-in rather than a default: a thousand-ion
    /// cloud costs about half a million pair evaluations per stage, seven stages per
    /// step.
    /// </para>
    /// <para>
    /// <c>pic</c> deposits the packet's charge onto its own grid, solves Poisson once,
    /// and gathers the field back - SC-1's approximate method, validated against
    /// <c>direct</c>. It costs one solve plus O(N) rather than O(N squared), but the
    /// solve is not free: <strong>the crossing is near 850 trajectories</strong>, and
    /// below that the reference method is simply faster. Configure it with
    /// <see cref="SpaceChargeGrid"/>.
    /// </para>
    /// </remarks>
    public string SpaceCharge { get; init; } = "none";

    /// <summary>
    /// How the density is stepped in time, or null for the explicit default.
    /// </summary>
    /// <remarks>
    /// Only the diffusive mode has a density to step. Refused against a trajectory
    /// model rather than ignored, for the reason <see cref="SpaceChargeGrid"/> is.
    /// </remarks>
    public DensityStepDocument? DensityStep { get; init; }

    /// <summary>
    /// The grid <c>"spaceCharge": "pic"</c> deposits onto, or null for its defaults.
    /// </summary>
    /// <remarks>
    /// Refused where the method is not <c>pic</c>, rather than ignored: a block that
    /// configures nothing is a document that thinks it configured something.
    /// </remarks>
    public SpaceChargeGridDocument? SpaceChargeGrid { get; init; }

}

/// <summary>Ion mobility as it appears in a model document.</summary>
/// <remarks>
/// The field dependence is stated rather than assumed constant because it is not.
/// Above a few tens of townsend an ion is heated by the field, its collision rate
/// rises, and its mobility falls; treating it as constant there overestimates the
/// drift by tens of per cent, which for a funnel is the difference between ions
/// arriving and ions not.
/// </remarks>
public sealed record MobilityDocument
{
    /// <summary>Mobility as the field goes to zero, at the declared gas density.</summary>
    public QuantityValue? ZeroField { get; init; }

    /// <summary>
    /// The quadratic coefficient of the reduced-field expansion:
    /// K(E/N) = K0 (1 + a (E/N)^2), with E/N in townsend.
    /// </summary>
    public double Alpha { get; init; }

    /// <summary>
    /// The reduced field, in townsend, this expansion was fitted to. Past it the
    /// value is an extrapolation and every result says so.
    /// </summary>
    public double ValidToTownsend { get; init; } = 50.0;
}

/// <summary>The region a density is tracked over, and how finely.</summary>
public sealed record DensityGridDocument
{
    /// <summary>Lower x bound.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Cells across x. Rounded up to a power of two.</summary>
    public int IntervalsX { get; init; } = 128;

    /// <summary>Cells across y. Rounded up to a power of two.</summary>
    public int IntervalsY { get; init; } = 64;
}

/// <summary>
/// The neutral gas the ions fly through.
/// </summary>
/// <remarks>
/// <para>
/// Absent means vacuum, and vacuum is bit-for-bit what this engine did before gas
/// existed - so adding the field changes no number in any model that does not
/// declare one.
/// </para>
/// <para>
/// A neutral is described by two numbers for collision purposes, its mass and its
/// polarizability, plus a collision cross section where the hard-sphere
/// description is used. Nothing here says what device the gas is in.
/// </para>
/// </remarks>
public sealed record GasDocument
{
    /// <summary>
    /// <c>none</c>, <c>hardSphere</c>, or <c>langevin</c>.
    /// </summary>
    /// <remarks>
    /// Spec figure 4 puts hard-sphere scattering below about 1e-5 mbar, where an
    /// ion may not collide at all and each collision is a glance off a residual
    /// molecule, and polarization capture between 1e-5 and 1e-2 mbar, where the ion
    /// draws the neutral onto itself. Above 1e-2 mbar neither applies: the mobility
    /// description with no discrete events does, and it is not built.
    /// </remarks>
    public string Model { get; init; } = "none";

    /// <summary>Gas pressure.</summary>
    public QuantityValue? Pressure { get; init; }

    /// <summary>Gas temperature. 300 K when omitted.</summary>
    public QuantityValue? Temperature { get; init; }

    /// <summary>Mass of one neutral, in daltons.</summary>
    public QuantityValue? Mass { get; init; }

    /// <summary>
    /// Collision cross section, for the hard-sphere model. Quoted in square
    /// angstroms, as a measured collision cross section is.
    /// </summary>
    public QuantityValue? CrossSection { get; init; }

    /// <summary>
    /// Polarizability volume, for the Langevin model. Nitrogen is 1.74 cubic
    /// angstroms, helium 0.205, argon 1.64.
    /// </summary>
    public QuantityValue? Polarizability { get; init; }

    /// <summary>
    /// Bulk velocity of the neutral gas. Stationary when omitted, which spec
    /// figure 4 marks adequate below about 1e-2 mbar.
    /// </summary>
    public VectorValue? DriftVelocity { get; init; }

    /// <summary>
    /// An imported neutral velocity <em>field</em>, which is what GAS-1 asks for and
    /// what spec figure 4 requires above about 1e-2 mbar.
    /// </summary>
    /// <remarks>
    /// Referenced, never embedded (PRJ-2): a CFD field is thousands of numbers, and
    /// a model document is meant to stay small, text and diffable. Overrides
    /// <see cref="DriftVelocity"/> where both are given, because a field is the more
    /// specific statement.
    /// </remarks>
    public GasFlowDocument? VelocityField { get; init; }

    /// <summary>
    /// An imported <em>pressure</em> field, which is the other half of a
    /// differentially pumped instrument (GAS-1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A velocity field on its own gives the neutrals a velocity everywhere and the
    /// same number of them everywhere. A funnel behind an inlet capillary spans
    /// decades of pressure between its entrance and its exit, and every collision
    /// rate, mean free path, mobility and diffusion coefficient in it varies with
    /// that.
    /// </para>
    /// <para>
    /// <see cref="Pressure"/> stays required and becomes the <em>reference</em>: it
    /// is the density the declared or derived mobility belongs to, and the field
    /// grades away from it. Both are reported on every run so a reader can see how
    /// far apart they are.
    /// </para>
    /// </remarks>
    public GasPressureFieldDocument? PressureField { get; init; }

    /// <summary>
    /// Seed for the collision random stream, so a collisional run is reproducible
    /// from its manifest (PRJ-3).
    /// </summary>
    public int Seed { get; init; } = 20_240_101;
}

/// <summary>The grid a particle-in-cell space-charge solve uses.</summary>
/// <remarks>
/// Both numbers are approximation knobs rather than conveniences, so both are
/// declarable and both are reported on the result: the node count sets how well the
/// packet is resolved, and the padding sets how nearly the earthed box stands in for
/// free space. They pull against each other at a fixed cost, which is what makes them
/// worth stating rather than burying.
/// </remarks>
public sealed record SpaceChargeGridDocument
{
    /// <summary>
    /// Nodes across the box. Rounded up to a power of two, so 32 and 48 are the same
    /// mesh.
    /// </summary>
    public int? Nodes { get; init; }

    /// <summary>Box half-width as a multiple of the packet's RMS radius.</summary>
    /// <remarks>
    /// A packet in flight is in free space and this puts it in an earthed box.
    /// Centring the box on the packet is what keeps that cheap - a centred
    /// distribution induces almost no field at its own centre - and this buys the
    /// residual down, at the cost of resolving the packet with fewer cells.
    /// </remarks>
    public double? Padding { get; init; }

    /// <summary>
    /// Fractional change in the packet's RMS radius that forces a new solve.
    /// </summary>
    /// <remarks>
    /// The grid travels with the packet, so uniform translation is exact and costs
    /// nothing; the only thing that ages between solves is the packet's shape. That
    /// is why the criterion is written on shape rather than on a step count.
    /// </remarks>
    public double? RefreshTolerance { get; init; }
}

/// <summary>How a diffusive run advances its density in time.</summary>
/// <remarks>
/// <para>
/// The explicit scheme is bounded by the faster of two limits: diffusion, and the
/// Courant condition on how fast the drift crosses a cell. In a driven structure the
/// second is severe for a reason worth knowing - the ponderomotive well's gradient is
/// steepest at an electrode edge, which is exactly where the density is almost zero,
/// so <strong>the step is set by a region where nothing is happening</strong>. On the
/// shipped ion funnel at 2 mbar that is 195 ps against a diffusion limit of 747 ns.
/// </para>
/// <para>
/// The implicit scheme has no stability limit and charges Gauss-Seidel sweeps instead.
/// <strong>Whether that is a bargain depends on which limit was binding</strong>: the
/// iteration's difficulty is set by the diffusive part of the operator, so a step long
/// by Courant's standard but still short by diffusion's converges in about three
/// sweeps - 10.8x the speed for 0.108% on that funnel - while a problem already at its
/// diffusion limit needs tens of sweeps and comes out slower than stepping explicitly.
/// </para>
/// </remarks>
public sealed record DensityStepDocument
{
    /// <summary>
    /// <c>explicit</c>, the default, or <c>implicit</c>.
    /// </summary>
    public string Scheme { get; init; } = "explicit";

    /// <summary>
    /// How many times the explicit stability limit to step, for the implicit scheme.
    /// </summary>
    /// <remarks>
    /// Backward Euler is first order, so <strong>the error is linear in this</strong>:
    /// measured on the shipped funnel over 5 us at 0.008, 0.028, 0.108, 0.427 and
    /// 1.673 per cent for gains of 4, 16, 64, 256 and 1024. Over a longer flight it
    /// <em>falls</em> rather than accumulating - 0.057 per cent at gain 64 over 50 us
    /// against 0.108 over 5 - because the error is concentrated in the initial
    /// transient, while the speedup grows from 10.8x to 21.1x because the explicit
    /// cost is linear in the window and the sweeps per step are not. There is no
    /// default above one because what gain is acceptable is an accuracy question, and
    /// nothing here measures the accuracy of a step it has not taken.
    /// </remarks>
    public double? Gain { get; init; }
}
