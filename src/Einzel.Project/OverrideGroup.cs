namespace Einzel.Project;

/// <summary>
/// One group of an ion list's rows that override the same model parameters with the same
/// values, as a flight's manifest records it.
/// </summary>
public sealed record OverrideGroup
{
    /// <summary>The group's number: groups are numbered in the order their first row appears.</summary>
    public required int Index { get; init; }

    /// <summary>The overridden parameters by name; empty for the rows that override nothing.</summary>
    public required IReadOnlyDictionary<string, OverrideValue> Overrides { get; init; }

    /// <summary>How many rows flew through this group's field.</summary>
    public required int Rows { get; init; }

    /// <summary>The basis channels the group's field is built from, every field element's in turn.</summary>
    public required IReadOnlyList<string> ChannelHashes { get; init; }

    /// <summary>Dry run only: how many of <see cref="ChannelHashes"/> the project's cache holds.</summary>
    public int? CachedChannels { get; init; }

    /// <summary>Channels read from the project's cache for this group.</summary>
    public int? Loaded { get; init; }

    /// <summary>Channels solved for this group.</summary>
    public int? Solved { get; init; }

    /// <summary>Channels an earlier group of the same flight had already loaded or solved.</summary>
    public int? Reused { get; init; }

    /// <summary>Seconds until this group's field was ready.</summary>
    public double? FieldReadySeconds { get; init; }

    /// <summary>The overrides as written, for a progress line: <c>U = 20.79 V, V = 140 V</c>.</summary>
    /// <returns>The text; <c>no overrides</c> for the rows that override nothing.</returns>
    public string Describe() => Overrides.Count == 0
        ? "no overrides"
        : string.Join(", ", Overrides.Select(pair => string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{pair.Key} = {(pair.Value.Unit is null ? $"{pair.Value.Si:R} (SI)" : $"{pair.Value.Value:R} {pair.Value.Unit}")}")));
}

/// <summary>An override's value: as the list wrote it, and in SI.</summary>
public sealed record OverrideValue
{
    /// <summary>The magnitude as written; absent for a CSV list, which states SI.</summary>
    public double? Value { get; init; }

    /// <summary>The unit as written; absent for a CSV list.</summary>
    public string? Unit { get; init; }

    /// <summary>The value in SI, the number the model was compiled with.</summary>
    public required double Si { get; init; }
}
