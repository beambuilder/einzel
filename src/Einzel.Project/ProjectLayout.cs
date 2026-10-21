using System.Reflection;

namespace Einzel.Project;

/// <summary>
/// A project: a directory, and the conventional paths inside it.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 3: "A project is a directory. Everything defining a modelling
/// effort is files under one root, and every operation is a command run against
/// that root. This is what enables the agent loop: read files, edit files, run
/// commands, read output. No protocol, no session."
/// </para>
/// <para>
/// PRJ-4 makes a plain folder the default and fully supported case: "Every
/// feature, guardrail, and agent workflow works in a directory with no
/// repository. Requiring one would reinstate exactly the barrier section 1 exists
/// to remove." Nothing here touches git.
/// </para>
/// <para>
/// The split that matters is between what is small, text, and tracked, and what
/// is large, binary, and regenerable. Everything in <see cref="Scratch"/> can be
/// deleted without losing anything, because the manifest determines the run.
/// </para>
/// </remarks>
public sealed class ProjectLayout
{
    /// <summary>The name of the scratch directory, which is safe to discard.</summary>
    public const string ScratchDirectoryName = ".einzel";

    /// <summary>Creates a layout rooted at a directory.</summary>
    /// <param name="root">The project root.</param>
    /// <exception cref="ArgumentException"><paramref name="root"/> is null or blank.</exception>
    public ProjectLayout(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
    }

    /// <summary>
    /// The project a path sits in, by walking up until a <c>models/</c> is found.
    /// </summary>
    /// <param name="startingAt">A file or directory inside the project.</param>
    /// <returns>The layout, or null when no project encloses it.</returns>
    /// <exception cref="ArgumentException"><paramref name="startingAt"/> is null or blank.</exception>
    /// <remarks>
    /// PRJ-4: a project is a plain directory, so it is identified by its shape
    /// rather than by a marker file - there is nothing to register and nothing to
    /// corrupt. Null rather than a guess: a caller that wants the working directory
    /// as a fallback can say so, and one that does not should not get it silently.
    /// </remarks>
    public static ProjectLayout? Find(string startingAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(startingAt);

        var directory = Directory.Exists(startingAt)
            ? new DirectoryInfo(startingAt)
            : new FileInfo(startingAt).Directory;

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "models")))
            {
                return new ProjectLayout(directory.FullName);
            }

            directory = directory.Parent;
        }

        return null;
    }

    /// <summary>The project root, as an absolute path.</summary>
    public string Root { get; }

    /// <summary>Model documents. Small, text, tracked.</summary>
    public string Models => Path.Combine(Root, "models");

    /// <summary>Python extensions. Small, text, tracked.</summary>
    public string Extensions => Path.Combine(Root, "extensions");

    /// <summary>Sweep and optimisation configurations. Small, text, tracked.</summary>
    public string Studies => Path.Combine(Root, "studies");

    /// <summary>Render specifications, and the SVG and PDF they produce. Text, tracked (RND-2).</summary>
    public string Figures => Path.Combine(Root, "figures");

    /// <summary>Run manifests and figures of merit. Small, text, tracked.</summary>
    public string Results => Path.Combine(Root, "results");

    /// <summary>Assertions on expected performance, runnable as <c>einzel test</c> (PRJ-7).</summary>
    public string Tests => Path.Combine(Root, "tests");

    /// <summary>Generated platform guidance plus hand-written project guidance (AGD-1).</summary>
    public string AgentsFile => Path.Combine(Root, "AGENTS.md");

    /// <summary>Field caches, trajectories, frames. Large, binary, regenerable, ignored.</summary>
    public string Scratch => Path.Combine(Root, ScratchDirectoryName);

    /// <summary>Solved basis channels kept between runs (<see cref="Project.FieldCache"/>). Safe to delete.</summary>
    public string FieldCache => Path.Combine(Scratch, "cache");

    /// <summary>Every tracked directory, in the order <c>einzel init</c> creates them.</summary>
    public IReadOnlyList<string> TrackedDirectories => [Models, Extensions, Studies, Figures, Results, Tests];

    /// <summary>Whether the root looks like an existing project.</summary>
    public bool Exists => Directory.Exists(Models) && File.Exists(AgentsFile);

    /// <summary>Creates the directory structure. Existing directories are left alone.</summary>
    public void CreateDirectories()
    {
        Directory.CreateDirectory(Root);

        foreach (var directory in TrackedDirectories)
        {
            Directory.CreateDirectory(directory);
        }

        Directory.CreateDirectory(Scratch);
    }

    /// <summary>Path of a model by name, without requiring the extension.</summary>
    /// <param name="name">The model name or path.</param>
    /// <returns>An absolute path to the model file.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    public string ModelPath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (Path.IsPathRooted(name) || name.Contains('/', StringComparison.Ordinal)
            || name.Contains('\\', StringComparison.Ordinal))
        {
            return Path.GetFullPath(name, Root);
        }

        var withExtension = name.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? name : name + ".json";
        return Path.Combine(Models, withExtension);
    }
}

/// <summary>Identity of the running build.</summary>
public static class EngineBuild
{
    /// <summary>
    /// Bumped only when numerical behaviour changes. Part of the field-cache key
    /// (FLD-3) and recorded in every manifest.
    /// </summary>
    /// <remarks>
    /// <b>A change to what a solver computes must bump this</b>, or the field cache keeps serving
    /// fields the new solver would not produce. <c>SolverBehaviourTripwireTests</c> pins a small
    /// plane and a small volume solve beside this number and fails when the numbers move, so the
    /// bump and the new pins land in the same change.
    /// </remarks>
    public const int SolverBehaviourVersion = 2;

    /// <summary>The engine version string.</summary>
    public static string Version { get; } =
        typeof(EngineBuild).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? typeof(EngineBuild).Assembly.GetName().Version?.ToString()
        ?? "0.0.0";

    /// <summary>The compute path in use. Scalar until Einzel.Compute lands.</summary>
    public const string ComputePath = "scalar";
}
