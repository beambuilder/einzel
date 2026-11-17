using System.Reflection;

using Einzel.Commands;

using Xunit.Abstractions;

namespace Einzel.Cli.Tests;

/// <summary>
/// The two rules that keep the shell cheap to have (invariant 1, UI-1).
/// </summary>
/// <remarks>
/// <para>
/// <b>These run on the Linux CI runner</b>, which is the point: an invariant only ever
/// checked on a developer's Windows box is one that has already been broken by the time
/// anyone notices. This test project does not reference <c>Einzel.Shell</c> and must never
/// do so — what it checks is that nothing else does either.
/// </para>
/// <para>
/// There was a second shell, on WPF, and while both existed this was a theory over each of
/// them rather than over whichever the scan happened to pick up. It is one shell again, and
/// the names of the deleted one are still forbidden - see <see cref="ShellNames"/>. The
/// misreading to guard against is the same as it was: "the WPF shell was Windows-only" and
/// "the project is Windows-only" are a few words apart, and the second would undo the Linux
/// CI that made the cross-platform shell cheap to build.
/// </para>
/// </remarks>
public sealed class ShellBoundaryTests(ITestOutputHelper output)
{
    /// <summary>Every name the shell assembly or project goes under.</summary>
    /// <remarks>
    /// <para>
    /// Project name and assembly name both, because a <c>ProjectReference</c> is declared
    /// under the first and recorded under the second, and here they differ on purpose.
    /// </para>
    /// <para>
    /// <b>The WPF shell's two names are kept after its deletion</b>, and that is deliberate
    /// rather than tidying left undone: the invariant is that nothing below a shell may
    /// reference one, and a reference to the assembly that used to exist would be exactly as
    /// much of a violation as a reference to the one that does. Removing the names would make
    /// this test quietly narrower than the rule it enforces.
    /// </para>
    /// </remarks>
    private static readonly HashSet<string> ShellNames = new(StringComparer.Ordinal)
    {
        "Einzel.Wpf", "einzel-shell-wpf", "Einzel.Shell", "einzel-shell",
    };

    /// <summary>Assemblies that must be present, so the check cannot pass vacuously.</summary>
    /// <remarks>
    /// The scan below is over whatever is actually beside the test assembly, which is the
    /// honest thing to check - a transitive reference through a third assembly would not
    /// appear in any csproj and is exactly as much of a violation. But a scan that found
    /// nothing would pass, which is the vacuous truth this project has found four times
    /// (`einzel test` passing with no tests, `einzel solve` converging over no elements),
    /// so these must be among what it found.
    /// </remarks>
    private static readonly string[] MustBePresent =
    [
        "Einzel.Core",
        "Einzel.Fields",
        "Einzel.Transport",
        "Einzel.Render",
        "Einzel.Commands",
        "Einzel.Io",
    ];

    /// <summary>Nothing below the shell may reference it (invariant 1).</summary>
    /// <remarks>
    /// <para>
    /// The invariant that paid for itself. Every assembly below the shell builds and runs
    /// on Linux, which is what made the cross-platform shell a replacement of a presentation
    /// layer rather than a rewrite - 2,167 lines, no assembly below it changed - and what
    /// lets <c>Einzel.Render</c> produce a publication figure headlessly in CI with no
    /// display attached. The WPF shell it replaced has since been deleted.
    /// </para>
    /// <para>
    /// Checked against what each assembly actually references rather than against project
    /// files, because a transitive reference is exactly as much of a violation and does
    /// not appear in a csproj.
    /// </para>
    /// </remarks>
    [Fact]
    public void NothingBelowTheShellReferencesIt()
    {
        var scanned = new List<string>();

        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "Einzel.*.dll")
            .Concat(Directory.EnumerateFiles(AppContext.BaseDirectory, "einzel.dll")))
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (name.EndsWith(".Tests", StringComparison.Ordinal))
            {
                continue;
            }

            AssemblyName[] references;

            try
            {
                references = Assembly.LoadFrom(path).GetReferencedAssemblies();
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            scanned.Add(name);

            // The shell by every name it goes under, and the deleted one by its names too.
            // A reference is recorded by ASSEMBLY name rather than by project name, and the
            // shell renames its own - so checking only the project name was checking for a
            // string that can never appear in metadata, and the declared-reference test
            // below was carrying the whole invariant on its own.
            var offending = references
                .Where(r => r.Name is not null && ShellNames.Contains(r.Name))
                .Select(r => r.Name!)
                .ToArray();

            Assert.True(
                offending.Length == 0,
                $"{name} references {string.Join(", ", offending)}, which breaks invariant 1: "
                + "nothing below the shell may reference it, and every assembly above it "
                + "must build and run on Linux");
        }

        output.WriteLine($"scanned {scanned.Count}: {string.Join(", ", scanned.Order(StringComparer.Ordinal))}");

        // A scan that found nothing would have passed every assertion above.
        foreach (var required in MustBePresent)
        {
            Assert.Contains(required, scanned);
        }
    }

    /// <summary>
    /// The shell drives command objects, and holds no physics of its own (UI-1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// UI-1 says the shell owns layout, input, the interactive viewport and the update
    /// check, and owns no physics, no validation rules, no file format knowledge and no
    /// render output. A window that referenced the engine directly could grow its own
    /// idea of what a model means, and the two would part company.
    /// </para>
    /// <para>
    /// So what it may reference is the command layer, which is the seam figure 3 draws:
    /// the CLI, the MCP server and the shell are peers driving the same command objects.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <para>
    /// <b>The Avalonia shell may declare one more reference, deliberately: <c>Einzel.Update</c>.</b>
    /// UI-1 gives the shell "the update check" and section 6 names that assembly for it; it holds
    /// no physics, no validation and no format knowledge, so reaching it does not give the window
    /// an idea of what a model means. The allow-list below is exact, so a third reference fails
    /// here rather than slipping in beside the second.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheShellReachesThePlatformThroughTheCommandLayer()
    {
        const string project = "Einzel.Shell";
        const string assembly = "einzel-shell";
        const string allowed = "Einzel.Commands,Einzel.Update";

        // Found by path rather than by name: this test project deliberately does not
        // reference the shell - that is the invariant above - so there is no assembly
        // identity to resolve and nothing copies it here.
        var path = Shell(project, assembly);

        // It must be there, on both runners. While the WPF shell existed this arm was
        // skipped off Windows, which meant UI-1 was checked on one platform; the shell
        // that replaced it targets plain net10.0 and has no such excuse. A skip because
        // the file was not found would be a test that passes by not looking - the same
        // vacuous truth the check above guards against, and it would hide exactly the
        // case where the shell had grown a reference it should not have.
        Assert.True(
            path is not null,
            $"{project} was not found beside the tests or in its own build output, so "
            + "UI-1 went unchecked for it");

        var shell = Assembly.LoadFrom(path!);

        var used = shell.GetReferencedAssemblies()
            .Select(r => r.Name)
            .Where(n => n is not null && n.StartsWith("Einzel", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        output.WriteLine($"uses:     {string.Join(", ", used)}");

        Assert.Contains("Einzel.Commands", used);

        // What the project *declares*, which is the check that matters and is not the
        // same one. `GetReferencedAssemblies` reports what the compiler emitted - what
        // the code actually uses - so a ProjectReference to the whole transport engine
        // that nothing has called yet leaves no trace in the metadata at all. It passed
        // this test when it was written against emitted references only.
        //
        // Declaration is the right thing to check for UI-1: once the reference is there,
        // using it is one keystroke away, and the rule is about what the shell may reach
        // for rather than what it happens to have reached for so far.
        var declared = Declared(path!, project);

        output.WriteLine($"declares: {string.Join(", ", declared)}");

        Assert.Contains("Einzel.Commands", declared);

        // Exactly the allowed references and nothing else, so a new one is a decision made here.
        Assert.Equal(allowed.Split(',').Order(StringComparer.Ordinal), declared);

        // Not the solvers, not the integrator, not the renderer. Those are reached
        // through the command layer or not at all.
        foreach (var forbidden in new[]
        {
            "Einzel.Fields", "Einzel.Transport", "Einzel.Render", "Einzel.Analysis",
            "Einzel.Sweeps", "Einzel.Library", "Einzel.Io",
        })
        {
            Assert.DoesNotContain(forbidden, declared);
            Assert.DoesNotContain(forbidden, used);
        }
    }

    /// <summary>
    /// Only the shell reaches the updater, so nothing else can touch the network (UPD-1, UPD-2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>UPD-2 says the CLI never contacts the network, and AGT-8 rests on it</b>: an agent
    /// issuing hundreds of <c>einzel</c> commands must see one version throughout. The strongest
    /// form of that is structural - the CLI and the MCP server cannot call an updater they
    /// cannot reach. So <c>Einzel.Update</c> is referenced by the Avalonia shell alone, and
    /// Velopack by <c>Einzel.Update</c> and the shell alone (the shell for the single
    /// <c>VelopackApp.Build().Run()</c> that <c>vpk pack</c> requires in the main executable).
    /// </para>
    /// <para>
    /// Checked twice, as UI-1 is: what every project <b>declares</b>, read from its csproj, which
    /// catches a reference nothing has used yet; and what every assembly beside these tests
    /// <b>emits</b>, which catches a transitive path no csproj shows. <c>Einzel.Update</c> must
    /// itself reference no other Einzel project, so it stays a leaf with no engine or UI behind it.
    /// </para>
    /// </remarks>
    [Fact]
    public void OnlyTheShellReachesTheUpdater()
    {
        var src = SourceDirectory();

        Assert.True(src is not null, "src/ was not found above the test assembly, so UPD-2 went unchecked");

        var projects = Directory.EnumerateFiles(src!, "*.csproj", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();

        // A scan that found none of these would pass vacuously.
        foreach (var required in new[] { "Einzel.Cli", "Einzel.Mcp", "Einzel.Shell", "Einzel.Update" })
        {
            Assert.Contains(projects, p => Path.GetFileNameWithoutExtension(p) == required);
        }

        foreach (var file in projects)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var text = File.ReadAllText(file);

            var reachesUpdate = System.Text.RegularExpressions.Regex.IsMatch(
                text, @"ProjectReference\s+Include=""[^""]*Einzel\.Update\.csproj""");
            var reachesVelopack = System.Text.RegularExpressions.Regex.IsMatch(
                text, @"PackageReference\s+Include=""Velopack""");

            Assert.False(
                reachesUpdate && name != "Einzel.Shell",
                $"{name} references Einzel.Update; only the shell may check for updates (UPD-1, UPD-2)");
            Assert.False(
                reachesVelopack && name is not ("Einzel.Shell" or "Einzel.Update"),
                $"{name} references Velopack; only Einzel.Update and the shell may (UPD-2)");

            if (name == "Einzel.Update")
            {
                Assert.DoesNotMatch(@"ProjectReference\s+Include=", text);
            }
        }

        var scanned = 0;

        foreach (var path in Directory.EnumerateFiles(AppContext.BaseDirectory, "*.dll"))
        {
            var name = Path.GetFileNameWithoutExtension(path);

            if (!(name.StartsWith("Einzel", StringComparison.Ordinal) || name.StartsWith("einzel", StringComparison.Ordinal))
                || name.EndsWith(".Tests", StringComparison.Ordinal)
                || name is "Einzel.Update" or "einzel-shell")
            {
                continue;
            }

            AssemblyName[] references;

            try
            {
                references = Assembly.LoadFrom(path).GetReferencedAssemblies();
            }
            catch (BadImageFormatException)
            {
                continue;
            }

            scanned++;

            var offending = references
                .Select(r => r.Name)
                .Where(n => n is "Einzel.Update" or "Velopack")
                .ToArray();

            Assert.True(
                offending.Length == 0,
                $"{name} references {string.Join(", ", offending)}; only the shell may reach the updater (UPD-2)");
        }

        // The CLI itself is beside these tests; a scan that loaded nothing checked nothing.
        Assert.True(scanned > 5, $"only {scanned} assemblies were scanned for an updater reference");
    }

    /// <summary>The repository's <c>src</c> directory, found above the test assembly.</summary>
    private static string? SourceDirectory()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src");

            if (Directory.Exists(Path.Combine(candidate, "Einzel.Cli")))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>What the shell's project declares a reference to.</summary>
    /// <remarks>
    /// Read from the csproj beside the built assembly rather than from metadata, because
    /// a declared reference that nothing has used yet is invisible to reflection and is
    /// exactly the state UI-1 needs to catch: the rule is about what the shell may reach
    /// for, not what it has reached for so far.
    /// </remarks>
    private static string[] Declared(string assemblyPath, string project)
    {
        for (var directory = new DirectoryInfo(Path.GetDirectoryName(assemblyPath)!);
            directory is not null;
            directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, project + ".csproj");

            if (!File.Exists(file))
            {
                continue;
            }

            return [.. System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(file), @"ProjectReference\s+Include=""[^""]*?([A-Za-z.]+)\.csproj""")
                .Select(m => m.Groups[1].Value)
                .Order(StringComparer.Ordinal)];
        }

        return [];
    }

    /// <summary>Where the shell's assembly is, beside the tests or in its own output.</summary>
    /// <remarks>
    /// Nothing copies it here, because nothing may reference it. Walking to its build
    /// output is the price of the invariant being real.
    /// </remarks>
    private static string? Shell(string project, string assembly)
    {
        var beside = Path.Combine(AppContext.BaseDirectory, assembly + ".dll");

        if (File.Exists(beside))
        {
            return beside;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent)
        {
            var candidate = Path.Combine(
                directory.FullName, "src", project, "bin");

            if (Directory.Exists(candidate))
            {
                return Directory
                    .EnumerateFiles(candidate, assembly + ".dll", SearchOption.AllDirectories)
                    .FirstOrDefault();
            }
        }

        return null;
    }
}
