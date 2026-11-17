namespace Einzel.Update;

/// <summary>The outcome of editing a PATH value.</summary>
/// <param name="Value">The PATH to write, or the original unchanged.</param>
/// <param name="Changed">Whether <paramref name="Value"/> differs from the original.</param>
public readonly record struct PathEdit(string Value, bool Changed);

/// <summary>
/// Adds and removes one directory in a PATH value, touching nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Pure string work, separate from the registry</b>, so every case that matters is tested
/// on any platform: an empty PATH, the directory already there under another case or with a
/// trailing separator, an entry written with an environment variable, and a removal that must
/// leave every other byte exactly as it was. A user's PATH is theirs; an installer that
/// reorders, deduplicates, trims or expands entries it did not add has damaged something it
/// had no business touching.
/// </para>
/// <para>
/// <b>Matching is lenient and editing is exact.</b> Windows paths are case-insensitive and a
/// trailing separator or surrounding quotes name the same directory, so an entry differing in
/// those ways counts as present (no duplicate is added) and is removed on uninstall. But only
/// the matching entries are cut out; empty segments and everything else stay byte-identical.
/// </para>
/// </remarks>
public static class PathList
{
    private const char Separator = ';';

    /// <summary>Appends <paramref name="directory"/> unless an entry already names it.</summary>
    /// <param name="path">The current PATH value; null or empty is an empty PATH.</param>
    /// <param name="directory">The directory to add.</param>
    /// <param name="expand">
    /// Expands environment variables in an entry before comparing, so an entry written as
    /// <c>%LOCALAPPDATA%\...</c> is recognised. Null compares entries as written.
    /// </param>
    /// <returns>The new value and whether it changed.</returns>
    /// <remarks>Appended rather than prepended: Einzel must not shadow a tool the user already has.</remarks>
    public static PathEdit Add(string? path, string directory, Func<string, string>? expand = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var current = path ?? string.Empty;

        if (Contains(current, directory, expand))
        {
            return new PathEdit(current, false);
        }

        if (current.Length == 0)
        {
            return new PathEdit(directory, true);
        }

        var joined = current[^1] == Separator ? current + directory : current + Separator + directory;

        return new PathEdit(joined, true);
    }

    /// <summary>Removes every entry naming <paramref name="directory"/>.</summary>
    /// <param name="path">The current PATH value; null or empty is an empty PATH.</param>
    /// <param name="directory">The directory to remove.</param>
    /// <param name="expand">As for <see cref="Add"/>.</param>
    /// <returns>The new value and whether it changed.</returns>
    public static PathEdit Remove(string? path, string directory, Func<string, string>? expand = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var current = path ?? string.Empty;
        var segments = current.Split(Separator);
        var kept = segments.Where(s => !Same(s, directory, expand)).ToArray();

        if (kept.Length == segments.Length)
        {
            return new PathEdit(current, false);
        }

        return new PathEdit(string.Join(Separator, kept), true);
    }

    /// <summary>Whether an entry of <paramref name="path"/> names <paramref name="directory"/>.</summary>
    /// <param name="path">A PATH value.</param>
    /// <param name="directory">The directory.</param>
    /// <param name="expand">As for <see cref="Add"/>.</param>
    /// <returns>True when present.</returns>
    public static bool Contains(string? path, string directory, Func<string, string>? expand = null) =>
        (path ?? string.Empty).Split(Separator).Any(s => Same(s, directory, expand));

    private static bool Same(string entry, string directory, Func<string, string>? expand)
    {
        var a = Normalise(expand is null ? entry : expand(entry));

        return a.Length > 0 && string.Equals(a, Normalise(directory), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalise(string entry) =>
        entry.Trim().Trim('"').Trim().TrimEnd('\\', '/');
}
