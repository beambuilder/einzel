using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>Snapshots the external inputs before computing, not when writing the result (PRJ-3).</summary>
internal static class RunInputs
{
    private static IEnumerable<(string Path, string Pointer)> References(ModelDocument document)
    {
        if (document.Transport?.Gas?.VelocityField?.Path is string velocity && !string.IsNullOrWhiteSpace(velocity))
            yield return (velocity, "/transport/gas/velocityField/path");
        if (document.Transport?.Gas?.PressureField?.Path is string pressure && !string.IsNullOrWhiteSpace(pressure))
            yield return (pressure, "/transport/gas/pressureField/path");

        // A tabulated drive's period is read at validation, so it is an input like a gas
        // field: hashed before the run, checked after it and by verify.
        var fields = document.Fields ?? [];
        for (var i = 0; i < fields.Count; i++)
        {
            foreach (var (solve, single, several) in new[]
            {
                ("solve", fields[i].Solve?.Drive, fields[i].Solve?.Drives),
                ("solve3d", fields[i].Solve3d?.Drive, fields[i].Solve3d?.Drives),
            })
            {
                var drives = (several ?? []).Select((d, k) => (Drive: (DriveDocument?)d, Pointer: $"drives/{k}"))
                    .Prepend((Drive: single, Pointer: "drive"));
                foreach (var (drive, pointer) in drives)
                {
                    if (drive?.Waveform is { Type: "tabulated", File: string file } && !string.IsNullOrWhiteSpace(file))
                        yield return (file, $"/fields/{i}/{solve}/{pointer}/waveform/file");
                }
            }
        }
    }

    internal static IEnumerable<string> ImportedPaths(ModelDocument document) =>
        References(document).Select(reference => reference.Path);

    internal static IReadOnlyDictionary<string, string> Capture(
        ModelDocument document, string modelPath, string root, string? studyPath = null, string? ionsPath = null)
    {
        var paths = References(document)
            .Select(reference => (Path: Path.GetFullPath(reference.Path, Path.GetDirectoryName(modelPath)!), reference.Pointer));
        if (studyPath is not null) paths = paths.Append((studyPath, "/study"));
        if (ionsPath is not null) paths = paths.Append((ionsPath, "/ions"));
        var hashes = new Captured();
        foreach (var (path, pointer) in paths)
        {
            // PORTABLE, or refused. `GetRelativePath` cannot express a path on another
            // volume and returns the absolute one, so a model on D: reading a field from
            // C: would key this inventory by where the file sat on the machine that wrote
            // it - which is the defect just fixed for artifact paths, in a manifest whose
            // whole job is to determine a run somewhere else (PRJ-3). Refused at
            // introduction rather than recorded and qualified, because nothing depends on
            // it yet and a manifest that is portable for most inputs and not for one is
            // the harder thing to reason about.
            var relative = RunManifest.Portable(Path.GetRelativePath(root, path));

            if (Path.IsPathRooted(relative))
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = pointer,
                    Constraint = "an imported file must be reachable by a relative path from "
                        + "the project root so the manifest that records it travels; "
                        + $"'{path}' is on another volume",
                    Suggestion = "copy the file into the project, or put it on the same "
                        + "volume as the project root",
                });
            }

            try
            {
                hashes[relative] = ContentHash.OfFile(path);
                hashes.Pointers[relative] = pointer;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = pointer,
                    Constraint = $"the input file {path} must be readable before its contents can be recorded",
                    Suggestion = $"check the path and read permissions: {ex.Message}",
                });
            }
        }
        return hashes;
    }

    /// <summary>Re-reads what was captured, so a file that moved under the run is caught.</summary>
    /// <param name="hashes">What <see cref="Capture"/> recorded before the work began.</param>
    /// <param name="root">The project root the keys are relative to.</param>
    /// <returns>The same hashes, once every one of them still holds.</returns>
    internal static IReadOnlyDictionary<string, string> Checked(
        IReadOnlyDictionary<string, string> hashes, string root)
    {
        foreach (var (relative, expected) in hashes)
        {
            var path = Path.GetFullPath(RunManifest.Local(relative), root);
            if (!File.Exists(path) || ContentHash.OfFile(path) != expected)
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,

                    // AGT-3's path is a pointer into the DOCUMENT, which is what a caller
                    // follows to find the declaration to change - the file that moved is
                    // named in the constraint instead. The pointer is the one `Capture`
                    // recorded for the file, so a changed study or ion list is not reported
                    // against the gas.
                    Path = hashes is Captured captured && captured.Pointers.TryGetValue(relative, out var pointer)
                        ? pointer
                        : "/transport/gas",
                    Constraint = $"the input file '{relative}' changed during the calculation; "
                        + "its consumed content cannot be established",
                    Suggestion = "restore stable input files and rerun before publishing a result",
                });
        }
        return hashes;
    }

    /// <summary>The hashes, with the pointer that declared each file.</summary>
    private sealed class Captured() : SortedDictionary<string, string>(StringComparer.Ordinal)
    {
        internal Dictionary<string, string> Pointers { get; } = new(StringComparer.Ordinal);
    }
}
