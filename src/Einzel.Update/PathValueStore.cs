using System.Runtime.Versioning;

using Microsoft.Win32;

namespace Einzel.Update;

/// <summary>What is stored under the user's PATH value, as far as editing it is concerned.</summary>
internal enum PathValueKind
{
    /// <summary>There is no PATH value at all: one may be created.</summary>
    Absent,

    /// <summary>A <c>REG_SZ</c>: edited, and written back as one.</summary>
    String,

    /// <summary>A <c>REG_EXPAND_SZ</c>: edited unexpanded, and written back as one.</summary>
    ExpandString,

    /// <summary>Anything else - <c>REG_MULTI_SZ</c>, <c>REG_BINARY</c>, a number. Never edited.</summary>
    Other,
}

/// <summary>The user PATH value as read: its kind, and its text when it is a string.</summary>
/// <param name="Kind">What is stored.</param>
/// <param name="Value">The unexpanded text, for a string kind; null otherwise.</param>
/// <param name="Description">The stored kind as the registry names it, for a log line.</param>
internal readonly record struct PathValue(PathValueKind Kind, string? Value, string Description);

/// <summary>Where the user PATH is read from and written to.</summary>
/// <remarks>
/// A seam so that the decision of what may be edited is tested without the registry: tests must
/// never write <c>HKCU\Environment</c>, and the cases that matter most - a value that is not a
/// string at all - are ones nobody wants to set up on a real machine to see what happens.
/// </remarks>
internal interface IPathValueStore
{
    /// <summary>Reads the PATH value. May throw; a failed read edits nothing.</summary>
    /// <returns>What is there.</returns>
    PathValue Read();

    /// <summary>Writes the PATH value with the given kind.</summary>
    /// <param name="value">The text.</param>
    /// <param name="kind"><see cref="PathValueKind.String"/> or <see cref="PathValueKind.ExpandString"/>.</param>
    void Write(string value, PathValueKind kind);
}

/// <content>The decision about what may be edited, apart from the registry.</content>
public static partial class UserPath
{
    /// <summary>Edits the stored PATH, if it is a value this may edit.</summary>
    /// <param name="store">Where the value lives.</param>
    /// <param name="edit">The change, from the unexpanded text (null for an absent value).</param>
    /// <param name="log">Where a value left alone, and why, is recorded.</param>
    /// <returns>Whether the value was written.</returns>
    /// <remarks>
    /// <para>
    /// <b>Only a string is edited, and the kind is read before the value.</b> The earlier
    /// version read the value <c>as string</c> first, so a <c>REG_MULTI_SZ</c> or a
    /// <c>REG_BINARY</c> read as null, was taken for an absent value, and was overwritten with
    /// the install directory alone - the user's whole PATH replaced by one entry, by the one
    /// guard that existed to leave an unusual value alone.
    /// </para>
    /// <para>
    /// <b>Absent and not-a-string are different answers.</b> An absent value is created, as
    /// <c>REG_EXPAND_SZ</c> like the one Windows makes, so a later entry using a variable keeps
    /// working. Anything that is present and is not a string, and any read that fails, leaves
    /// the PATH exactly as it was and says why in the update log.
    /// </para>
    /// </remarks>
    internal static bool Edit(IPathValueStore store, Func<string?, PathEdit> edit, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(log);

        PathValue current;

        try
        {
            current = store.Read();
        }
#pragma warning disable CA1031 // Whatever stopped the read, the PATH is left as it is and the reason logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            log($"the user PATH was left unchanged: it could not be read ({ex.GetType().Name}: {ex.Message})");

            return false;
        }

        string? existing;
        PathValueKind kind;

        switch (current.Kind)
        {
            case PathValueKind.Absent:
                existing = null;
                kind = PathValueKind.ExpandString;
                break;

            case PathValueKind.String or PathValueKind.ExpandString when current.Value is not null:
                existing = current.Value;
                kind = current.Kind;
                break;

            default:
                log($"the user PATH was left unchanged: it is stored as {current.Description}, not as a string, "
                    + "and editing it would mean guessing what it holds");

                return false;
        }

        var result = edit(existing);

        if (!result.Changed)
        {
            return false;
        }

        store.Write(result.Value, kind);

        return true;
    }
}

/// <summary>The real store: <c>HKCU\Environment\Path</c>, read unexpanded.</summary>
[SupportedOSPlatform("windows")]
internal sealed class RegistryPathValueStore : IPathValueStore
{
    private const string EnvironmentKey = "Environment";
    private const string ValueName = "Path";

    /// <inheritdoc />
    public PathValue Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(EnvironmentKey, writable: false);

        if (key is null || !key.GetValueNames().Any(n => string.Equals(n, ValueName, StringComparison.OrdinalIgnoreCase)))
        {
            return new PathValue(PathValueKind.Absent, null, "absent");
        }

        var registryKind = key.GetValueKind(ValueName);
        var description = registryKind.ToString();

        var kind = registryKind switch
        {
            RegistryValueKind.String => PathValueKind.String,
            RegistryValueKind.ExpandString => PathValueKind.ExpandString,
            _ => PathValueKind.Other,
        };

        if (kind == PathValueKind.Other)
        {
            return new PathValue(kind, null, description);
        }

        var value = key.GetValue(ValueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

        return value is null
            ? new PathValue(PathValueKind.Other, null, description)
            : new PathValue(kind, value, description);
    }

    /// <inheritdoc />
    public void Write(string value, PathValueKind kind)
    {
        var registryKind = kind switch
        {
            PathValueKind.String => RegistryValueKind.String,
            PathValueKind.ExpandString => RegistryValueKind.ExpandString,
            _ => throw new ArgumentOutOfRangeException(
                nameof(kind), kind, "only a string kind is written"),
        };

        using var key = Registry.CurrentUser.CreateSubKey(EnvironmentKey, writable: true);

        key.SetValue(ValueName, value, registryKind);
    }
}
