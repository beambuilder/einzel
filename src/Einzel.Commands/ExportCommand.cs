using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields.Solved;
using Einzel.Project;
using Einzel.Render;

namespace Einzel.Commands;

/// <summary>The outcome of an export.</summary>
public sealed record ExportOutcome
{
    /// <summary>What was exported.</summary>
    public required string What { get; init; }

    /// <summary>The format written.</summary>
    public required string Format { get; init; }

    /// <summary>Files written, as absolute paths.</summary>
    public required IReadOnlyList<string> Artifacts { get; init; }

    /// <summary>Whether anything was actually written.</summary>
    /// <remarks>False under <c>--dry-run</c> (CLI-4).</remarks>
    public required bool Written { get; init; }

    /// <summary>What the solves found wrong with their meshes, once per field element.</summary>
    /// <remarks>
    /// The same list <c>einzel solve</c> reports, prefixed with the element, and the same lines
    /// each file carries in its header (GRD-2). Empty for a mesh export, which solves nothing.
    /// </remarks>
    public IReadOnlyList<ValidityWarning> Warnings { get; init; } = [];
}

/// <summary>
/// Writes a model's solved fields out for something else to look at.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 21 puts VTU export in Phase 1 deliberately, so ParaView supplies
/// the whole visualisation story a year before the shell exists. That argument
/// only works if export is reachable without running an ion through anything -
/// looking at a field is the commonest reason to want a picture, and it is the
/// step before a trajectory is worth computing.
/// </para>
/// <para>
/// Trajectories are exported by <c>run --vtu</c>, where they are produced. This
/// covers the field, which nothing else wrote out.
/// </para>
/// </remarks>
public static class ExportCommand
{
    /// <summary>Exports the conductor surfaces as a Wavefront OBJ mesh.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be written and write nothing (CLI-4).</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <para>
    /// <b>The extraction is <see cref="ViewportCommand"/>'s, not a second one.</b> It already
    /// turns each electrode's signed distance into an oriented surface and knows what a solve
    /// claims about the third dimension — a cross-section extrudes, an axisymmetric half-plane
    /// revolves, a volume solve is extracted properly. Writing that again here would be two
    /// implementations of one geometry, which is how they come to disagree; this method is a
    /// file format and nothing else.
    /// </para>
    /// <para>
    /// <b>Each electrode keeps its own name and its potential travels with it</b> as a comment,
    /// because a grey mesh of eleven identical-looking plates is not much use for a figure and
    /// the number a reader wants to colour by is the one the model declared. The drive
    /// amplitude is written too where there is one: an electrode holding zero DC and all of its
    /// potential as RF reads as earthed otherwise, which is the mistake this project has made
    /// six times.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The model does not validate, or declares no conductors.</exception>
    public static ExportOutcome Mesh(string modelPath, ProjectLayout project, bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(project);

        var absolute = Path.GetFullPath(modelPath);
        var viewport = ViewportCommand.Execute(absolute);

        if (viewport.Conductors.Count == 0)
        {
            // The two ways to get here are different problems and the message has to say
            // which. A model with no electrodes has nothing to mesh and never did; a model
            // with electrodes that produced no surface is a defect in the extraction, and
            // telling that author to "check the model declares electrodes" sends them to
            // look at the one thing that is already right.
            var declared = ModelValidator.Validate(
                Io.ModelJson.Parse(File.ReadAllText(absolute)), null,
                Path.GetDirectoryName(absolute)).Model?.Fields
                .Sum(f => (f.Solve?.Electrodes.Count ?? 0) + (f.Solve3D?.Electrodes.Count ?? 0))
                ?? 0;

            throw new EinzelException(new EinzelError
            {
                Code = "NOTHING_TO_EXPORT",
                Path = "/fields",
                Constraint = "a mesh export needs at least one conductor surface",
                Observed = new ObservedValue(0, "conductor surfaces"),
                Suggestion = declared == 0
                    ? "this model declares no electrodes. Only a solved element has any - an "
                        + "analytic field is a formula and has no geometry to mesh - so check "
                        + "that it has a 'solve', 'solve3d' or axisymmetric element with "
                        + "electrodes in it"
                    : Vanished(declared),
                Severity = ErrorSeverity.Error,
            });
        }

        var parts = viewport.Conductors
            .Select(c => new NamedSurface(
                c.Name,
                c.VerticesMm,
                c.Normals,
                c.Triangles,
                c.DriveAmplitudeVolts != 0.0
                    ? FormattableString.Invariant(
                        $"{c.Name}: {c.PotentialVolts:G6} V DC, drive amplitude {c.DriveAmplitudeVolts:G6} V")
                    : FormattableString.Invariant($"{c.Name}: {c.PotentialVolts:G6} V")))
            .ToList();

        var stem = Path.GetFileNameWithoutExtension(absolute);
        var path = Path.Combine(project.Scratch, $"{stem}.conductors.obj");

        var triangles = parts.Sum(p => p.Triangles.Count / 3);

        if (!dryRun)
        {
            Directory.CreateDirectory(project.Scratch);
            File.WriteAllText(
                path,
                ObjWriter.Write(
                    parts,
                    [
                        FormattableString.Invariant($"model: {stem}"),
                        FormattableString.Invariant(
                            $"{parts.Count} conductor(s), {triangles} triangles"),
                        "surfaces are the zero level set of each electrode's signed distance",
                    ]));
        }

        return new ExportOutcome
        {
            What = "conductors",
            Format = "obj",
            Artifacts = [path],
            Written = !dryRun,
        };
    }

    /// <summary>What to say when electrodes were declared and none of them meshed.</summary>
    /// <remarks>
    /// Split into its own method because <c>FormattableString.Invariant</c> takes one
    /// interpolated string and not a concatenation of one with several literals, which is
    /// the shape a long message naturally has.
    /// </remarks>
    private static string Vanished(int declared)
    {
        var count = FormattableString.Invariant($"this model declares {declared} electrode(s)");

        return count
            + " and none of them produced a surface, which is a defect in the extraction "
            + "rather than in the model. It has happened twice for sub-cell geometry: an "
            + "electrode thinner than the sampling step falls between lattice planes and "
            + "vanishes silently";
    }

    /// <summary>Exports a model's solved potential fields as VTK ImageData.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <param name="project">Where artifacts belong.</param>
    /// <param name="dryRun">Report what would be written and write nothing (CLI-4).</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// <para>
    /// <b>One file per basis channel, in both dimensions</b>, because that is what was solved.
    /// A driven structure has no single potential to draw - what an ion sees is a weighted sum
    /// that changes within an RF cycle - and one file called "the field" would be picking a
    /// phase without saying which. The two-dimensional path used to build one mask from each
    /// electrode's DC potential, so the shipped RF quadrupole, whose every rod holds zero DC
    /// and all of its potential as drive, exported the field of a grounded box. That is the
    /// defect <c>einzel solve</c> had and lost; this path was the one left holding it.
    /// </para>
    /// <para>
    /// <b>The solve's warnings travel into the file</b> (GRD-2), in its provenance header and
    /// on the outcome, because a volume is the artifact most likely to be opened by somebody
    /// who never saw the command that wrote it. The header also says what the numbers
    /// <em>are</em>: the declared potentials for a static geometry, and for a driven one a
    /// spatial pattern with its leading electrode at one volt - which read as a field is
    /// out by the drive amplitude, and plausibly so.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The model does not validate, or has no solved field.</exception>
    public static ExportOutcome Vtu(string modelPath, ProjectLayout project, bool dryRun = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(project);

        var absolute = Path.GetFullPath(modelPath);
        var text = File.ReadAllText(absolute);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(text), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var stem = Path.GetFileNameWithoutExtension(absolute);
        var hash = ContentHash.OfText(text);
        var artifacts = new List<string>();
        var warnings = new List<ValidityWarning>();
        var solved = 0;

        var solvedElements = model.Fields.Count(f => f.Solve is not null || f.Solve3D is not null);

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var solve = model.Fields[index].Solve;
            var solve3d = model.Fields[index].Solve3D;

            if (solve is null && solve3d is null)
            {
                continue;
            }

            solved++;

            // Named by element index as well as model, because a model with two
            // solved elements would otherwise write one file twice. Without the extension:
            // a channel's suffix goes before it, and finding where by searching for ".vti"
            // also finds it in the stem or in any directory above the project.
            var name = solvedElements > 1
                ? $"{stem}.field{index}"
                : $"{stem}.field";

            Element element;

            if (solve3d is not null)
            {
                var geometry = Geometry3D.Of(solve3d);

                element = new Element(
                    index,
                    "solved3d",
                    geometry.Drives.Count > 0 || geometry.Stages.Count > 0,
                    Mirrored("a volume", solve3d.ReflectAboutX),
                    [.. solve3d.Electrodes.Select(e => (e.Name, false))],
                    [
                        .. GeometryBuilder3D.SolveChannels(geometry).Select(c => new Channel(
                            c.Index, c.Report, c.Holding,
                            (array, provenance) => Io.VtuWriter.WriteScalarField(c.Potential, array, provenance))),
                    ]);
            }
            else
            {
                // Through SolveChannels, as einzel solve does, so the two verbs cannot solve
                // one geometry two ways again.
                element = new Element(
                    index,
                    "solved2d",
                    solve!.Drives.Count > 0 || solve.Stages.Count > 0,
                    Mirrored(
                        solve.Symmetry == SolveSymmetry.Cylindrical
                            ? "axisymmetric: x is the axis of rotation and y the radius, and this is "
                                + "the half-plane that was solved"
                            : "a cross-section: the geometry is taken to repeat unchanged along z",
                        solve.ReflectAboutX),
                    [.. solve.Electrodes.Select(e => (e.Name, e.Shape == ElectrodeShape.EdgeProfile))],
                    [
                        .. GeometryBuilder.SolveChannels(solve).Select(c => new Channel(
                            c.Index, c.Report, c.Holding,
                            (array, provenance) => Io.VtuWriter.WriteScalarField(c.Potential, array, provenance))),
                    ]);
            }

            artifacts.AddRange(Write(element, project.Scratch, name, hash, project, dryRun, warnings));
        }

        if (solved == 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/fields",
                Constraint = "this model has no solved field to export",
                Suggestion = "only a 'solved2d' or 'solved3d' field element has a grid to write; "
                    + "analytic fields are formulas and have nothing to sample",
            });
        }

        return new ExportOutcome
        {
            What = "field",
            Format = "vti",
            Artifacts = artifacts,
            Written = !dryRun,
            Warnings = warnings,
        };
    }

    /// <summary>One solved field element, whatever its dimension, ready to write.</summary>
    /// <param name="Index">Which field element of the model.</param>
    /// <param name="Kind">The element type, as a document spells it.</param>
    /// <param name="Basis">
    /// Whether the channels are spatial patterns normalized to a leading electrode rather than
    /// the declared potentials - true for any driven or sequenced geometry.
    /// </param>
    /// <param name="Symmetry">What the solve claims about the directions it did not mesh.</param>
    /// <param name="Electrodes">
    /// Names in declaration order, and whether each is an edge profile - whose entry in a
    /// pattern is a scale on its profile rather than a potential. Matched to each channel's
    /// holding by position - the order the mask was built in, which is what the holding
    /// describes.
    /// </param>
    /// <param name="Channels">One per basis channel.</param>
    private sealed record Element(
        int Index,
        string Kind,
        bool Basis,
        string Symmetry,
        IReadOnlyList<(string Name, bool Profile)> Electrodes,
        IReadOnlyList<Channel> Channels);

    /// <summary>One basis channel, with a way to write it that knows its dimension.</summary>
    /// <param name="Index">Which channel, in the order the decomposition found them.</param>
    /// <param name="Report">How its solve went.</param>
    /// <param name="Holding">What each declared electrode held, in declaration order.</param>
    /// <param name="Write">Writes the channel's potential as ImageData.</param>
    private sealed record Channel(
        int Index,
        SolveReport Report,
        IReadOnlyList<double> Holding,
        Func<string, IReadOnlyList<string>, string> Write);

    /// <summary>
    /// Refuses an unconverged element, then writes one file per channel with the element's
    /// provenance and warnings in its header.
    /// </summary>
    /// <returns>The paths written, or that would be under a dry run.</returns>
    private static List<string> Write(
        Element element,
        string directory,
        string name,
        string hash,
        ProjectLayout project,
        bool dryRun,
        List<ValidityWarning> warnings)
    {
        var channels = element.Channels;

        foreach (var channel in channels)
        {
            if (!channel.Report.Converged)
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.ConvergenceFailed,
                    Path = $"/fields/{element.Index}",
                    Constraint = channels.Count > 1
                        ? $"basis channel {channel.Index} did not converge in {channel.Report.Cycles} cycles"
                        : $"the solve did not converge in {channel.Report.Cycles} cycles",
                    Suggestion = "a picture of an unconverged field would look like a result; run "
                        + "'einzel solve' to see the residual and the convergence factor for every "
                        + "channel",
                });
            }
        }

        // Once per element, from the first channel: every channel of an element solves on the
        // same mesh round the same conductors, so the finding is the element's - the rule
        // einzel solve follows, and the same prefix, so the two verbs read alike.
        var found = channels.Count > 0 ? channels[0].Report.Warnings : [];

        warnings.AddRange(found.Select(w =>
            w with { Message = $"field element {element.Index} ({element.Kind}): {w.Message}" }));

        var written = new List<string>(channels.Count);

        foreach (var channel in channels)
        {
            var channelPath = Path.Combine(
                directory,
                channels.Count > 1 ? $"{name}.channel{channel.Index}.vti" : $"{name}.vti");

            // A basis channel is named as one even when it is the only channel, because a
            // single-pattern RF quadrupole's rods sit at one volt and the array must not be
            // called a potential when it is a potential per volt of weight.
            var array = element.Basis
                ? $"channel{channel.Index}_V_per_V"
                : "potential_V";

            written.Add(channelPath);

            if (dryRun)
            {
                continue;
            }

            var provenance = new List<string>
            {
                $"engine: {EngineBuild.Version}",
                $"model: {hash}",
                FormattableString.Invariant(
                    $"field element {element.Index} ({element.Kind}), channel {channel.Index} of {channels.Count} (numbered from 0)"),
                element.Symmetry,
                element.Basis
                    ? "units: volts per volt of this channel's weight, at grid nodes. A driven or "
                        + "sequenced geometry is solved once per spatial pattern, and what an ion sees "
                        + "at an instant is the sum over this element's channels of each pattern times "
                        + "its weight at that instant - so no one file here is the field at any time"
                    : "units: volts at grid nodes, every electrode at its declared potential",
                Holding(element, channel),
            };

            // The severity is on the line, not only the code, as run --vtu writes it: a reader
            // deciding whether to trust a volume needs to know which is a note and which is the
            // engine saying the numbers may not describe the document (GRD-3).
            provenance.AddRange(found.Select(w => $"{w.Severity}: {w.Code}: {w.Message}"));

            Directory.CreateDirectory(project.Scratch);
            File.WriteAllText(channelPath, channel.Write(array, provenance));
        }

        return written;
    }

    /// <summary>What a solve claims about the space it did not mesh, for the header.</summary>
    /// <remarks>
    /// A reflected solve writes only the half it solved, and a reader who does not know that
    /// sees an instrument cut off at a plane it does not have.
    /// </remarks>
    private static string Mirrored(string symmetry, double? plane) =>
        plane is { } x
            ? FormattableString.Invariant(
                $"{symmetry}; only the solved half is written, and the field continues as its mirror image about x = {x * 1e3:G6} mm")
            : symmetry;

    /// <summary>What each electrode holds in one channel, for the header.</summary>
    /// <remarks>
    /// Named because two files called <c>channel0</c> and <c>channel1</c> say nothing about
    /// which is the DC chain and which the RF. Declaration order, so the output is deterministic
    /// (CLI-5); grounded electrodes left out, since in a pattern they are most of the list and
    /// none of the information; capped, since a funnel has a hundred rings.
    /// </remarks>
    private static string Holding(Element element, Channel channel)
    {
        const int Shown = 8;

        // By position, the order the mask was built in: the holding is a list over the same
        // electrodes, so pairing it by name would be a second account of something one already gives.
        var held = element.Electrodes
            .Select((e, i) => (e.Name, e.Profile, Value: channel.Holding[i]))
            .Where(e => e.Value != 0.0)
            .Select(e => e.Profile
                ? FormattableString.Invariant($"{e.Name} {e.Value:G6} x its profile")
                : FormattableString.Invariant($"{e.Name} {e.Value:G6} V"))
            .ToList();

        if (held.Count == 0)
        {
            return "holding: every electrode at 0 V";
        }

        var listed = string.Join(", ", held.Take(Shown));
        var more = held.Count > Shown
            ? FormattableString.Invariant($", and {held.Count - Shown} more")
            : string.Empty;

        var leading = element.Basis ? " (the pattern's leading electrode at 1 V)" : string.Empty;

        return $"holding{leading}: {listed}{more}";
    }
}
