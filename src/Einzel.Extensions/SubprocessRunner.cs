using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Einzel.Core.Errors;
using Einzel.Core.Results;

namespace Einzel.Extensions;

/// <summary>What one extension call produced.</summary>
/// <param name="Output">The document the extension returned.</param>
/// <param name="Warnings">Warnings the result carries, per GRD-2 and GRD-6.</param>
/// <param name="ElapsedMs">Wall-clock milliseconds for the round trip.</param>
/// <param name="Diagnostics">Whatever the extension wrote to standard error.</param>
public sealed record ExtensionResult(
    JsonNode? Output,
    IReadOnlyList<ValidityWarning> Warnings,
    double ElapsedMs,
    string Diagnostics);

/// <summary>
/// Runs an extension in a subprocess: whole input in, whole output out, once.
/// </summary>
/// <remarks>
/// <para>
/// The default runner (EXT-3), and the one that makes EXT-4 structural. A
/// subprocess cannot be invoked per integration step at any useful rate, so
/// per-step scripting is not discouraged here, it is impossible - which is a much
/// stronger guarantee than a comment saying not to.
/// </para>
/// <para>
/// The transport is one JSON document on standard input and one on standard
/// output. EXT-5 wants large arrays crossing by shared memory with an Arrow or raw
/// buffer layout rather than by JSON, and that is not built; what is built is the
/// small-payload path, which is what an objective or an analysis extension needs.
/// The manifest does not mention the transport, so adding the buffer path later
/// changes no extension that does not want it.
/// </para>
/// </remarks>
public sealed class SubprocessRunner
{
    private readonly string _interpreter;

    /// <summary>Creates a runner against a Python interpreter.</summary>
    /// <param name="interpreter">Path to the interpreter, or its name on the path.</param>
    /// <exception cref="ArgumentException"><paramref name="interpreter"/> is null or blank.</exception>
    public SubprocessRunner(string interpreter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interpreter);

        _interpreter = interpreter;
    }

    /// <summary>
    /// Finds an interpreter to run extensions with.
    /// </summary>
    /// <returns>The interpreter, or null when none was found.</returns>
    /// <remarks>
    /// EXT-6 says a vendored interpreter ships with the application, so that an
    /// extension behaves the same on every machine and does not depend on what
    /// somebody happened to install. Nothing is vendored yet, so this discovers one
    /// - which is the honest interim and is reported as such rather than passed off
    /// as the vendored path.
    /// </remarks>
    public static string? Discover()
    {
        foreach (var candidate in OperatingSystem.IsWindows()
            ? new[] { "python.exe", "python3.exe", "py.exe" }
            : ["python3", "python"])
        {
            try
            {
                using var probe = Process.Start(new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = "-c \"import sys; print(sys.version_info[0])\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                });

                if (probe is null)
                {
                    continue;
                }

                var major = probe.StandardOutput.ReadToEnd().Trim();
                probe.WaitForExit(5000);

                if (probe.ExitCode == 0 && major == "3")
                {
                    return candidate;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // Not on the path. Try the next spelling.
            }
        }

        return null;
    }

    /// <summary>
    /// What a sandboxed child is started with, built from the parent's environment.
    /// </summary>
    /// <param name="parent">The parent's environment.</param>
    /// <param name="windows">Whether the child runs on Windows.</param>
    /// <returns>
    /// On Windows, <c>SYSTEMROOT</c> as the parent spells it, when the parent has it, and
    /// nothing else. Elsewhere, nothing at all.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The scrub was a bare <c>Clear()</c>, and on an ordinary Windows desktop that killed
    /// every extension before its first line: "Fatal Python error:
    /// _Py_HashRandomization_Init: failed to get random numbers to initialize Python".
    /// CPython seeds its hash randomisation from the operating system's random source at
    /// start-up, and up to 3.10 it reaches that source through CryptoAPI
    /// (<c>CryptAcquireContext</c>), which does not work in a process whose environment
    /// has no <c>SYSTEMROOT</c>. Measured with the sandbox's own <c>-I -B</c>: with an
    /// empty environment 3.9.13 dies and 3.11.5 does not (3.11 moved to
    /// <c>BCryptGenRandom</c>); with <c>SYSTEMROOT</c> alone 3.9.13 starts; with
    /// <c>WINDIR</c> alone, <c>TEMP</c> and <c>TMP</c> alone, or <c>PATHEXT</c> alone it
    /// still dies. So <c>SYSTEMROOT</c> is kept and the three others are not. CI never saw
    /// the failure because the Windows runner's Python is 3.12.
    /// </para>
    /// <para>
    /// What is kept is the path of the Windows directory. It is not a credential, a proxy
    /// setting or an import path, and Python's own <c>subprocess</c> documentation asks for
    /// the same thing from the other side: an environment handed to a Windows child must
    /// include a valid <c>SystemRoot</c>. Linux and macOS keep nothing, which is what the
    /// Linux runner measures on every run.
    /// </para>
    /// </remarks>
    internal static Dictionary<string, string> ChildEnvironment(
        IDictionary<string, string?> parent, bool windows)
    {
        ArgumentNullException.ThrowIfNull(parent);

        var child = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!windows)
        {
            return child;
        }

        foreach (var (name, value) in parent)
        {
            // By name and not by key lookup, so the answer does not depend on which
            // comparer the parent dictionary was built with. Windows spells it SystemRoot.
            if (KeptOnWindows.Contains(name, StringComparer.OrdinalIgnoreCase)
                && !string.IsNullOrEmpty(value))
            {
                child[name] = value;
            }
        }

        return child;
    }

    /// <summary>The variables a Windows child keeps: one, and the reason is on <see cref="ChildEnvironment"/>.</summary>
    private static readonly string[] KeptOnWindows = ["SYSTEMROOT"];

    /// <summary>Calls an extension once.</summary>
    /// <param name="manifest">The extension's manifest.</param>
    /// <param name="directory">The folder the manifest lives in.</param>
    /// <param name="input">The document to hand it.</param>
    /// <param name="scratch">A directory the child may run in.</param>
    /// <returns>What it returned, with the warnings that travel with it.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">The extension failed, timed out, or returned nonsense.</exception>
    public ExtensionResult Run(
        ExtensionManifest manifest, string directory, JsonNode? input, string scratch)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(scratch);

        var entry = Path.Combine(directory, manifest.Entry ?? "extension.py");

        if (!File.Exists(entry))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/entry",
                Constraint = $"the manifest names '{manifest.Entry}', which is not in {directory}",
                Suggestion = "set 'entry' to the Python file, relative to the manifest",
            });
        }

        Directory.CreateDirectory(scratch);

        var start = new ProcessStartInfo
        {
            FileName = _interpreter,

            // -I is isolated mode: no user site-packages, no PYTHON* variables, and
            // the script's own directory kept off sys.path. -B keeps it from
            // littering the project with bytecode caches.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = scratch,
        };

        start.ArgumentList.Add("-I");
        start.ArgumentList.Add("-B");
        start.ArgumentList.Add(Host);
        start.ArgumentList.Add(Path.GetFullPath(entry));
        start.ArgumentList.Add(manifest.Function);

        // Scrubbed, not inherited. A child that starts with the parent's environment
        // starts with its credentials, its proxy settings, and its PYTHONPATH. What it
        // keeps is what the interpreter cannot start without: SYSTEMROOT on Windows and
        // nothing anywhere else. ChildEnvironment says why, with the measurement.
        var kept = ChildEnvironment(start.Environment, OperatingSystem.IsWindows());

        start.Environment.Clear();

        foreach (var (name, value) in kept)
        {
            start.Environment[name] = value;
        }

        var clock = Stopwatch.StartNew();

        using var process = Process.Start(start)
            ?? throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.InternalError,
                Path = "/",
                Constraint = $"could not start '{_interpreter}'",
                Suggestion = "run 'einzel doctor' to see which interpreter was found",
            });

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        var reading = ReadCapped(process.StandardOutput, stdout, manifest.Resources.MaximumOutputBytes);
        var diagnosing = ReadCapped(process.StandardError, stderr, 256 * 1024);

        process.StandardInput.Write(input?.ToJsonString() ?? "null");
        process.StandardInput.Close();

        if (!process.WaitForExit(manifest.Resources.TimeoutMs))
        {
            Kill(process);

            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.CostGateRefused,
                Path = "/resources/timeoutMs",
                Constraint =
                    $"'{manifest.Name}' did not finish within {manifest.Resources.TimeoutMs} ms",
                Suggestion = "EXT-4 gives an extension one call per run, not one per step, so a "
                    + "call that needs longer than this is usually one that is being asked to do "
                    + "the engine's job. Raise 'resources.timeoutMs' if it genuinely needs it",
            });
        }

        reading.Wait(2000);
        diagnosing.Wait(2000);
        clock.Stop();

        var diagnostics = stderr.ToString();

        if (process.ExitCode != 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.InternalError,
                Path = "/",
                Constraint = $"'{manifest.Name}' exited with code {process.ExitCode}",

                // The extension's own traceback, which is the only thing that says
                // what went wrong. Truncated rather than dropped: an error message
                // that omits the error is not a recovery instruction (AGT-3).
                Suggestion = diagnostics.Length > 0
                    ? Tail(diagnostics, 2000)
                    : "the extension wrote nothing to standard error",
            });
        }

        JsonNode? output;

        try
        {
            output = JsonNode.Parse(stdout.ToString());
        }
        catch (JsonException malformed)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/",
                Constraint = $"'{manifest.Name}' did not return a JSON document: {malformed.Message}",
                Suggestion = "an extension returns its result from its entry function; the host "
                    + "writes it to standard output. Anything printed instead of returned lands "
                    + "here, so use standard error for diagnostics",
            });
        }

        var warnings = new List<ValidityWarning>
        {
            // GRD-6: an extension result carries the extension's identity and cannot
            // present itself as first-party.
            new(
                "extension.attributed",
                $"computed by extension '{manifest.Name}' version {manifest.Version}, not by the "
                + "engine",
                WarningSeverity.Provenance),
        };

        if (manifest.Trust == ExtensionTrust.Sandboxed)
        {
            warnings.Add(Sandbox.IncompleteIsolation);
        }

        SchemaCheck.Validate(output, manifest.OutputSchema, manifest.Name ?? "extension");

        return new ExtensionResult(output, warnings, clock.Elapsed.TotalMilliseconds, diagnostics);
    }

    /// <summary>
    /// The host script: reads one document, calls the entry function, writes one back.
    /// </summary>
    /// <remarks>
    /// Written to a temporary file rather than passed with -c, so a traceback names
    /// real line numbers in a real file and an extension author can read it.
    /// </remarks>
    private static string Host => HostScript.Value;

    private static readonly Lazy<string> HostScript = new(() =>
    {
        var path = Path.Combine(Path.GetTempPath(), "einzel-ext-host.py");

        File.WriteAllText(path, """
        import importlib.util
        import json
        import sys


        def main():
            entry, function = sys.argv[1], sys.argv[2]

            spec = importlib.util.spec_from_file_location("einzel_extension", entry)
            module = importlib.util.module_from_spec(spec)
            spec.loader.exec_module(module)

            if not hasattr(module, function):
                print(f"{entry} has no function named {function!r}", file=sys.stderr)
                return 2

            payload = sys.stdin.read()
            result = getattr(module, function)(json.loads(payload) if payload.strip() else None)

            json.dump(result, sys.stdout, allow_nan=False)
            return 0


        if __name__ == "__main__":
            sys.exit(main())
        """);

        return path;
    });

    private static Task ReadCapped(StreamReader reader, StringBuilder into, int ceiling) =>
        Task.Run(() =>
        {
            var buffer = new char[8192];

            while (true)
            {
                var read = reader.Read(buffer, 0, buffer.Length);

                if (read <= 0)
                {
                    return;
                }

                if (into.Length + read > ceiling)
                {
                    into.Append(buffer, 0, Math.Max(0, ceiling - into.Length));
                    return;
                }

                into.Append(buffer, 0, read);
            }
        });

    private static void Kill(Process process)
    {
        try
        {
            // The tree, not the process. An extension that spawned something is
            // otherwise still running after its parent is gone.
            process.Kill(entireProcessTree: true);

            // And then wait for it to actually be gone. Kill only asks: it returns
            // before the operating system has finished, so a timeout that does not
            // wait has not bounded anything - the extension is still running, still
            // holding its working directory, and still whatever else it had open.
            //
            // On Windows that shows up immediately as a directory that cannot be
            // deleted, which is how this was found.
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Exited between the timeout and the kill.
        }
    }

    private static string Tail(string text, int keep) =>
        text.Length <= keep ? text : "..." + text[^keep..];
}
