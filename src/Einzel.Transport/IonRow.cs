namespace Einzel.Transport;

/// <summary>
/// One ion of an explicit ion list, compiled: who it is, what it is, and where and when
/// it starts.
/// </summary>
/// <param name="Id">The ion's identifier, unique within its list.</param>
/// <param name="Species">Its mass and charge.</param>
/// <param name="Launch">Its position and velocity at birth, in SI.</param>
/// <param name="BirthTimeSeconds">
/// When it starts, in seconds on the model's clock. Zero is the instant a single-ion run
/// starts at.
/// </param>
/// <param name="Tag">A free label carried through to the results; empty when none.</param>
/// <remarks>
/// <para>
/// The form a flight takes, with every default already resolved against a model: a row
/// carries no reference to the document it came from, so an expanded list written as
/// <c>.ions.csv</c> and read back is the same list.
/// </para>
/// <para>
/// The birth time is SIMION's <c>tob</c>: the ion sees the field as it stands at
/// <see cref="BirthTimeSeconds"/> plus its own flight time.
/// </para>
/// </remarks>
public sealed record IonRow(int Id, IonSpecies Species, PhaseState Launch, double BirthTimeSeconds, string Tag);
