using System.Text.Json;
using System.Text.Json.Serialization;

namespace Einzel.Core.Model;

/// <summary>
/// The shape of an RF drive over one cycle: a name, or an object with a <c>type</c>.
/// </summary>
/// <remarks>
/// <para>
/// The two names <c>sinusoid</c> and <c>rectangular</c> are the short form and read as
/// they always have, with a rectangular wave's duty cycle beside them on the drive.
/// The object form names one of <c>sinusoid</c>, <c>rectangular</c>, <c>analytic</c>,
/// <c>tabulated</c> or <c>harmonic</c> and carries that shape's own keys, so one
/// spelling covers every shape and a key that belongs to another shape is refused
/// rather than ignored.
/// </para>
/// <para>
/// Equal when every member is, lists element by element, so two reads of the same
/// text compare equal.
/// </para>
/// </remarks>
public sealed record WaveformDocument
{
    /// <summary>The short-form names, which are also object-form types.</summary>
    public static IReadOnlyList<string> Names { get; } = ["sinusoid", "rectangular"];

    /// <summary>The object-form types.</summary>
    public static IReadOnlyList<string> Types { get; } = ["sinusoid", "rectangular", "analytic", "tabulated", "harmonic"];

    /// <summary>
    /// One of <c>sinusoid</c>, <c>rectangular</c>, <c>analytic</c>, <c>tabulated</c> or
    /// <c>harmonic</c>.
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// For <c>analytic</c>: the drive as an expression of the phase <c>p</c> (cycles, in
    /// [0, 1)) and the model's parameters, relative to the amplitude; for example
    /// <c>1 - 2*floor(2*p)</c> for a balanced square wave.
    /// </summary>
    public string? Expression { get; init; }

    /// <summary>
    /// For <c>tabulated</c>: a <c>phase,value</c> table of one period, relative to the
    /// model file; its SHA-256 goes into a run's <c>inputHashes</c>.
    /// </summary>
    public string? File { get; init; }

    /// <summary>For <c>tabulated</c>: <c>linear</c> (the default) or <c>cubic</c>, the periodic spline.</summary>
    public string? Interpolation { get; init; }

    /// <summary>For <c>harmonic</c>: the terms of the series, at least one.</summary>
    public IReadOnlyList<HarmonicTermDocument>? Terms { get; init; }

    /// <summary>For <c>rectangular</c>: the fraction of the cycle at the positive level. One half when omitted.</summary>
    public double? DutyCycle { get; init; }

    /// <summary>
    /// For <c>analytic</c> and <c>tabulated</c>: phases where the shape jumps or kinks, in
    /// cycles, strictly ascending in [0, 1). The cycle mean splits its quadrature there.
    /// </summary>
    public IReadOnlyList<double>? Breakpoints { get; init; }

    /// <summary>Whether the document wrote the name alone, so it writes back the same way.</summary>
    /// <remarks>Internal, so neither the serialiser nor <c>einzel schema</c> sees it as a key.</remarks>
    internal bool IsShortForm { get; init; }

    /// <summary>Member-wise equality, lists element by element.</summary>
    /// <param name="other">The other waveform.</param>
    /// <returns>Whether both describe the same shape, spelled the same way.</returns>
    public bool Equals(WaveformDocument? other) =>
        other is not null
        && IsShortForm == other.IsShortForm
        && string.Equals(Type, other.Type, StringComparison.Ordinal)
        && string.Equals(Expression, other.Expression, StringComparison.Ordinal)
        && string.Equals(File, other.File, StringComparison.Ordinal)
        && string.Equals(Interpolation, other.Interpolation, StringComparison.Ordinal)
        && DutyCycle == other.DutyCycle
        && Same(Terms, other.Terms)
        && Same(Breakpoints, other.Breakpoints);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(Type, Expression, File, Interpolation, DutyCycle, Terms?.Count, Breakpoints?.Count);

    private static bool Same<T>(IReadOnlyList<T>? a, IReadOnlyList<T>? b) =>
        ReferenceEquals(a, b) || (a is not null && b is not null && a.SequenceEqual(b));
}

/// <summary>One term of a harmonic waveform, as a document writes it.</summary>
public sealed record HarmonicTermDocument
{
    /// <summary>Which multiple of the drive frequency: 0 a constant offset, 1 the fundamental.</summary>
    public int? Order { get; init; }

    /// <summary>The term's amplitude, relative to the drive amplitude.</summary>
    public double? Amplitude { get; init; }

    /// <summary>The term's phase, in cycles (a half is antiphase). Zero when omitted.</summary>
    public double? Phase { get; init; }
}

/// <summary>Reads and writes a <see cref="WaveformDocument"/> as a name or an object.</summary>
/// <remarks>
/// The object is read with the caller's options, so an unknown key is refused exactly as
/// it is anywhere else in a model document.
/// </remarks>
public sealed class WaveformDocumentConverter : JsonConverter<WaveformDocument>
{
    /// <inheritdoc/>
    public override WaveformDocument? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return new WaveformDocument { Type = reader.GetString(), IsShortForm = true };
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            var kind = reader.TokenType switch
            {
                JsonTokenType.Number => "a number",
                JsonTokenType.True or JsonTokenType.False => "a boolean",
                JsonTokenType.StartArray => "an array",
                _ => reader.TokenType.ToString(),
            };

            throw new JsonException(
                $"a waveform is a name such as \"sinusoid\" or an object such as {{\"type\": \"analytic\", ...}}, not {kind}");
        }

        try
        {
            return JsonSerializer.Deserialize<WaveformDocument>(ref reader, options);
        }
        catch (JsonException failure) when (failure.Path is not null)
        {
            // The inner read knows only its own path ("$.bogus"), which is not a pointer
            // into the document; rethrown without one, the outer read stamps the
            // waveform's. The inner path leaves the message too: one error, one path.
            var key = failure.Path.StartsWith("$.", StringComparison.Ordinal) ? failure.Path[2..] : failure.Path;
            var text = failure.Message;
            var cut = text.IndexOf(" Path: ", StringComparison.Ordinal);
            text = cut < 0 ? text : text[..cut];

            throw new JsonException(
                text.Contains("could not be mapped", StringComparison.OrdinalIgnoreCase)
                    ? $"'{key}' is not part of a waveform object and could not be mapped"
                    : $"{text} (at '{key}' in the waveform object)",
                failure);
        }
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, WaveformDocument value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        if (value.IsShortForm)
        {
            writer.WriteStringValue(value.Type);
        }
        else
        {
            JsonSerializer.Serialize(writer, value, options);
        }
    }
}
