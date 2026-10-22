using Einzel.Core.Model;

namespace Einzel.Io.IonLists;

/// <summary>
/// An ion list: the ions a flight launches, one row each or drawn by a seeded set.
/// </summary>
/// <remarks>
/// <para>
/// A document of its own rather than a member of the model, as a study is. The model
/// describes the instrument and keeps its hash when the ions change, so one list serves
/// many models and a run of the model alone is not reported as edited.
/// </para>
/// <para>
/// Unknown keys are refused, as in a model: a misspelled attribute silently dropped
/// would launch the model's default ion and read as though it launched another.
/// </para>
/// </remarks>
public sealed record IonListDocument
{
    /// <summary>The ion-list schema version, <c>0.1</c>. Required.</summary>
    public string? SchemaVersion { get; init; }

    /// <summary>Explicit rows, one per ion, in launch order.</summary>
    public IReadOnlyList<IonRowDocument>? Ions { get; init; }

    /// <summary>Generated rows, appended after the explicit ones, set by set.</summary>
    public IReadOnlyList<IonSetDocument>? IonSets { get; init; }

    /// <summary>
    /// How many ions may fly at once, or null for one per processor. A cost, never an
    /// input: every row is flown and written by index, so the answer is the same at any
    /// setting.
    /// </summary>
    public int? MaxParallelism { get; init; }
}

/// <summary>One ion, as an ion list declares it. Every attribute but the id is optional.</summary>
/// <remarks>
/// An omitted attribute takes the model's value: <c>massToCharge</c> and
/// <c>chargeNumber</c> from <c>ion</c>, <c>position</c>, <c>direction</c> and the energy
/// from <c>source</c>, so a row with only an id is the model's own ion.
/// </remarks>
public sealed record IonRowDocument
{
    /// <summary>Identifier, a non-negative integer unique within the document. Required.</summary>
    public int? Id { get; init; }

    /// <summary>Mass-to-charge ratio, a mass per elementary charge such as <c>Da</c>.</summary>
    public QuantityValue? MassToCharge { get; init; }

    /// <summary>Charge number, signed, never zero.</summary>
    public int? ChargeNumber { get; init; }

    /// <summary>Starting position.</summary>
    public VectorValue? Position { get; init; }

    /// <summary>Direction of travel; normalised.</summary>
    public DirectionValue? Direction { get; init; }

    /// <summary>Kinetic energy at birth. Not together with <see cref="AccelerationPotential"/>.</summary>
    public QuantityValue? KineticEnergy { get; init; }

    /// <summary>
    /// The potential the ion was accelerated through, as the model's source states it; the
    /// model's <c>energyFraction</c> does not apply to it.
    /// </summary>
    public QuantityValue? AccelerationPotential { get; init; }

    /// <summary>When the ion starts on the model's clock; zero when omitted.</summary>
    public QuantityValue? BirthTime { get; init; }

    /// <summary>
    /// The birth time as a fraction of the drive's cycle, for a model with exactly one drive
    /// frequency: <c>birthTime = rfPhase / f</c>. Not together with <see cref="BirthTime"/>.
    /// </summary>
    public double? RfPhase { get; init; }

    /// <summary>A free label carried to the results. No comma, quote or line break.</summary>
    public string? Tag { get; init; }
}

/// <summary>A set of ions a generator draws, as an ion list declares it.</summary>
/// <remarks>
/// Either a <see cref="Cloud"/>, or attributes that are constants and generators; an omitted
/// attribute takes the model's value, as in a row.
/// </remarks>
public sealed record IonSetDocument
{
    /// <summary>
    /// The model's source cloud, drawn around the model's own ion and launch: the same
    /// keys as <c>source.cloud</c>, and the same ions in the same order.
    /// </summary>
    public CloudDocument? Cloud { get; init; }

    /// <summary>How many ions; for a cloud set, in place of <c>cloud.ions</c>.</summary>
    public int? N { get; init; }

    /// <summary>The seed of the set's one stream; required when an attribute draws at random.</summary>
    public int? Seed { get; init; }

    /// <summary>Mass-to-charge ratio.</summary>
    public ScalarDraw? MassToCharge { get; init; }

    /// <summary>Charge number, a constant.</summary>
    public int? ChargeNumber { get; init; }

    /// <summary>Kinetic energy; not together with <see cref="AccelerationPotential"/>.</summary>
    public ScalarDraw? KineticEnergy { get; init; }

    /// <summary>Acceleration potential.</summary>
    public ScalarDraw? AccelerationPotential { get; init; }

    /// <summary>Birth time; not together with <see cref="RfPhase"/>.</summary>
    public ScalarDraw? BirthTime { get; init; }

    /// <summary>Birth time in cycles of the model's one drive; no unit.</summary>
    public ScalarDraw? RfPhase { get; init; }

    /// <summary>Starting position.</summary>
    public PositionDraw? Position { get; init; }

    /// <summary>Direction of travel.</summary>
    public DirectionDraw? Direction { get; init; }

    /// <summary>
    /// The first row's id; the rest follow by one. Defaults to one more than the largest
    /// id before the set in the document, or 1 for the first.
    /// </summary>
    public int? FirstId { get; init; }

    /// <summary>A label given to every row of the set.</summary>
    public string? Tag { get; init; }
}
