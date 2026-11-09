namespace Einzel.Extensions;

/// <summary>
/// One containment measure, and whether this build actually applies it.
/// </summary>
/// <param name="Name">What is being contained.</param>
/// <param name="Enforced">Whether this build enforces it.</param>
/// <param name="How">How it is enforced, or what it would take.</param>
public sealed record Containment(string Name, bool Enforced, string How);

/// <summary>
/// What the subprocess runner contains, and what it does not.
/// </summary>
/// <remarks>
/// <para>
/// EXT-3 asks for job objects and a restricted token on Windows, and namespaces
/// and seccomp on Linux. Neither is built. What is built is everything that can be
/// done portably from managed code, and this type exists so that the difference is
/// <em>stated</em> rather than implied by the word "sandbox".
/// </para>
/// <para>
/// That distinction is the whole reason for the type. A containment measure that
/// is claimed and not applied is worse than one that is absent and known to be:
/// the first makes someone run untrusted code they would not otherwise have run.
/// So <see cref="Unenforced"/> is reported by <c>einzel ext</c>, attached to every
/// extension result, and non-suppressible.
/// </para>
/// </remarks>
public static class Sandbox
{
    /// <summary>Every measure, enforced or not.</summary>
    public static IReadOnlyList<Containment> Measures { get; } =
    [
        new("wall-clock timeout", true,
            "the process and its children are killed at the declared timeout"),

        new("output size ceiling", true,
            "stdout is read to a declared byte ceiling and the process killed past it"),

        new("no inherited environment", true,
            "the child starts with a scrubbed environment - SYSTEMROOT alone on Windows, "
            + "which CPython needs to start, and nothing elsewhere - so no credential, "
            + "proxy, or PYTHONPATH reaches it from the parent"),

        new("interpreter isolation", true,
            "python -I: user site-packages, PYTHON* variables, and the script's own "
            + "directory are all kept off the import path"),

        new("no stdin beyond the payload", true,
            "the child reads one JSON document and sees end of file"),

        new("working directory", true,
            "the child runs in a scratch directory, not the project"),

        new("no network", false,
            "needs a restricted token and a job object on Windows, or a network "
            + "namespace on Linux. Not built: an extension can open a socket"),

        new("filesystem confinement", false,
            "needs a job object or a mount namespace. Not built: an extension can "
            + "read and write anything the user can"),

        new("memory and CPU ceilings", false,
            "needs a job object or cgroups. Not built: the wall-clock timeout is the "
            + "only resource bound"),
    ];

    /// <summary>The measures this build does not enforce.</summary>
    public static IReadOnlyList<Containment> Unenforced { get; } =
        [.. Measures.Where(m => !m.Enforced)];

    /// <summary>
    /// The warning every sandboxed extension result carries.
    /// </summary>
    /// <remarks>
    /// A validity violation rather than an advisory, so GRD-3 forbids suppressing
    /// it. Somebody deciding whether to run an agent-authored extension needs this
    /// in front of them, and a warning that can be turned off is a warning that will
    /// be, by the person least able to judge the consequence.
    /// </remarks>
    public static Core.Results.ValidityWarning IncompleteIsolation { get; } =
        new(
            "extension.isolation-incomplete",
            "this extension ran in a subprocess with a scrubbed environment, an isolated "
            + "interpreter, a scratch working directory, and a wall-clock timeout - but "
            + "with no network, filesystem, or memory confinement, which need OS "
            + "primitives this build does not use. Treat it as code you have read, not as "
            + "code that has been contained",
            Core.Results.WarningSeverity.ValidityViolation);
}
