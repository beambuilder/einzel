namespace Einzel.Core.Model;

/// <summary>The shape of an RF drive over one cycle, as a model declares it.</summary>
/// <remarks>
/// Named here rather than reusing the field layer's waveform type, because the
/// model layer sits below the field layer and a document has no business knowing
/// how a waveform is evaluated. The mapping happens once, where the field is built.
/// </remarks>
public enum DriveWaveform
{
    /// <summary>What a resonant circuit produces. Gives the Mathieu equation.</summary>
    Sinusoid,

    /// <summary>What a switching supply produces. Gives the Meissner equation.</summary>
    Rectangular,

    /// <summary>An expression of the phase: <see cref="CompiledDrive.Expression"/>.</summary>
    Analytic,

    /// <summary>A sampled period: <see cref="CompiledDrive.Table"/>.</summary>
    Tabulated,

    /// <summary>A sum of harmonics: <see cref="CompiledDrive.Terms"/>.</summary>
    Harmonic,
}

/// <summary>One harmonic of a drive, validated.</summary>
/// <param name="Order">Which multiple of the drive frequency; zero is a constant.</param>
/// <param name="Amplitude">Relative to the drive amplitude.</param>
/// <param name="Phase">In cycles.</param>
public readonly record struct DriveHarmonic(int Order, double Amplitude, double Phase);

/// <summary>
/// The drive a solved geometry is operated with, validated and reduced to SI.
/// </summary>
/// <param name="FrequencyHz">Drive frequency, in hertz.</param>
/// <param name="Waveform">The shape of one cycle.</param>
/// <param name="DutyCycle">
/// Fraction of the cycle at the positive level, for a rectangular wave. One half is
/// a balanced square wave; anything else carries a mean, which acts as a DC offset
/// and is the whole trick of a digital mass filter.
/// </param>
/// <param name="Name">
/// What the generator is called, so an electrode can say which one it taps. Empty
/// where a geometry has only one and nothing needs naming.
/// </param>
/// <remarks>
/// <para>
/// A generator, not an electrode setting. A real instrument has a supply and
/// electrodes tapped off it at various amplitudes and phases, and what each
/// electrode does with it is on the electrode: an amplitude, and a phase as a
/// fraction of a cycle. A quadrupole is one pair at phase zero and the other at a
/// half; a travelling-wave guide is a ramp of phases along its length.
/// </para>
/// <para>
/// <strong>A geometry may have more than one, and the first version of this said it
/// may not.</strong> The original note here read "one drive per solve ... modelling
/// it the other way round would let a document declare two frequencies on one
/// structure - which is a different instrument and almost always a mistake." Two
/// devices refuted it. A real travelling-wave guide superposes a fast confining RF
/// on a slow travelling wave, which is why the shipped template confines to about
/// 0.1 mm on a 2 mm bore; and a trap performing a stored-waveform isolation runs a
/// low-frequency notched comb across its endcaps while the ring carries the main
/// drive. Two frequencies on one structure is not a mistake, it is what a trap is.
/// </para>
/// <para>
/// It costs nothing in the solver. Basis superposition is indifferent to what the
/// weights are functions of, so two generators reaching the same electrodes in the
/// same proportions are one solved pattern carrying two weights on two clocks -
/// exactly as a DC supply and an RF supply already were. What multiplies the solve
/// count is a different <em>spatial pattern</em>, never a different frequency.
/// </para>
/// </remarks>
public sealed record CompiledDrive(
    double FrequencyHz, DriveWaveform Waveform, double DutyCycle, string Name = "")
{
    /// <summary>For an analytic drive, the expression of <c>p</c>; otherwise null.</summary>
    public string? Expression { get; init; }

    /// <summary>The model parameters the expression names, with the values they had here.</summary>
    public IReadOnlyDictionary<string, Units.Quantity>? Bindings { get; init; }

    /// <summary>For a tabulated drive, the samples read from <see cref="TablePath"/>.</summary>
    public WaveformTable? Table { get; init; }

    /// <summary>For a tabulated drive, the file as the document names it.</summary>
    public string? TablePath { get; init; }

    /// <summary>For a tabulated drive, how the samples are joined.</summary>
    public WaveformInterpolation Interpolation { get; init; }

    /// <summary>For a harmonic drive, its terms.</summary>
    public IReadOnlyList<DriveHarmonic>? Terms { get; init; }

    /// <summary>Declared phases where an analytic or tabulated shape jumps or kinks.</summary>
    public IReadOnlyList<double> Breakpoints { get; init; } = [];

    /// <summary>Member-wise equality, lists and bindings element by element.</summary>
    /// <param name="other">The other drive.</param>
    /// <returns>Whether both are the same generator.</returns>
    public bool Equals(CompiledDrive? other) =>
        other is not null
        && FrequencyHz.Equals(other.FrequencyHz) && Waveform == other.Waveform
        && DutyCycle.Equals(other.DutyCycle) && Name == other.Name
        && Expression == other.Expression && Equals(Table, other.Table)
        && TablePath == other.TablePath && Interpolation == other.Interpolation
        && Same(Terms, other.Terms) && Same(Breakpoints, other.Breakpoints)
        && Same(Bindings?.OrderBy(b => b.Key, StringComparer.Ordinal).ToList(),
            other.Bindings?.OrderBy(b => b.Key, StringComparer.Ordinal).ToList());

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(FrequencyHz, Waveform, DutyCycle, Name, Expression, Table);

    private static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));
}
