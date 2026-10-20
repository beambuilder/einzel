using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Render;

/// <summary>
/// Draws a plane through an instrument: conductors, equipotentials, and the path
/// an ion takes through them.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here knows what a mirror or a quadrupole is (architecture invariant 2).
/// A conductor is drawn as the <em>zero level set of its own signed distance</em>,
/// which the model format already requires every electrode to supply because the
/// solver and the absorber both need it. So a rod, a plate, a ring and a sphere
/// are all drawn by one routine, and a shape added to the format needs no change
/// in this assembly.
/// </para>
/// <para>
/// Headless by construction, per RND-1: there is no drawing surface, no font
/// measurement, and no display. The output is a list of paths and text runs, which
/// is why this can produce a publication figure in CI on Linux with nothing
/// attached.
/// </para>
/// </remarks>
public static class SectionRenderer
{
    /// <summary>Ink colours, kept together so a figure reads as one drawing.</summary>
    private const string Ink = "#1a1a1a";
    private const string ConductorFill = "#c9ccd1";
    private const string ConductorEdge = "#4a4f57";
    private const string Equipotential = "#7a8390";
    private const string TrajectoryInk = "#b3452a";

    /// <summary>Ink for dimensions, distinct from both the geometry and the trajectory.</summary>
    private const string DimensionInk = "#1f6feb";

    /// <summary>Gap between a feature and the start of its extension line, in page mm.</summary>
    private const double ExtensionGapMm = 1.0;

    /// <summary>How far an extension line runs past the dimension line, in page mm.</summary>
    private const double ExtensionOvershootMm = 1.5;

    /// <summary>Arrowhead length, in page millimetres.</summary>
    private const double ArrowLengthMm = 2.0;

    /// <summary>Arrowhead half-width, in page millimetres.</summary>
    private const double ArrowHalfWidthMm = 0.6;

    /// <summary>How far the text sits off the dimension line, in page millimetres.</summary>
    private const double TextLiftMm = 1.8;

    /// <summary>Radius of the marker showing where the ion is, in page millimetres.</summary>
    private const double HeadRadiusMm = 0.9;

    /// <summary>Sides of the polygon standing in for that marker's circle.</summary>
    private const int HeadSides = 12;
    private const string DensityInk = "#2f6f8f";
    private const string TaintInk = "#a8321e";

    /// <summary>A rendered figure, and what it does not claim.</summary>
    /// <param name="Scene">The figure.</param>
    /// <param name="Warnings">Warnings carried onto the drawing, per GRD-2.</param>
    /// <param name="DecimationToleranceMm">
    /// The bound every decimated polyline in the figure respects, in page millimetres.
    /// </param>
    /// <param name="TrajectoryPoints">Points the trajectory kept after decimation.</param>
    /// <param name="TrajectoryPointsBeforeDecimation">Points it had before.</param>
    public sealed record Figure(
        Scene Scene,
        IReadOnlyList<ValidityWarning> Warnings,
        double DecimationToleranceMm,
        int TrajectoryPoints,
        int TrajectoryPointsBeforeDecimation);

    /// <summary>
    /// What a caller drawing many figures of one model computes once and reuses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An animation is a few hundred figures of the same instrument. Rendered the
    /// ordinary way each one would solve the field and fly the ion again - a multigrid
    /// solve per frame, which is unaffordable, and worse than unaffordable: two frames
    /// that flew separately are two frames that can disagree about the flight. Fly
    /// once, draw many.
    /// </para>
    /// <para>
    /// Null everywhere is the ordinary single-figure path, which computes all of this
    /// itself and is unchanged.
    /// </para>
    /// </remarks>
    public sealed record FramePlan
    {
        /// <summary>The field, already built.</summary>
        public IElectrostaticField? Field { get; init; }

        /// <summary>What building it reported, carried onto the figure per GRD-2.</summary>
        public IReadOnlyList<ValidityWarning> FieldWarnings { get; init; } = [];

        /// <summary>The <em>whole</em> flight, already flown.</summary>
        /// <remarks>
        /// <para>
        /// Whole rather than truncated, and that distinction is the difference between
        /// an animation and a camera that follows the ion. An analytic model takes its
        /// extent from the flight, so a frame handed only the part flown so far would
        /// choose its page from that part - and every frame would then have a different
        /// scale, with the ion pinned to the edge of a box that grew to meet it. Which
        /// is what the first version did.
        /// </para>
        /// <para>
        /// <see cref="AtSeconds"/> is what truncates it for drawing, so every frame
        /// draws a prefix of one flight on one page.
        /// </para>
        /// </remarks>
        public IReadOnlyList<TrajectorySample>? Trajectory { get; init; }

        /// <summary>The instant this frame shows, in seconds.</summary>
        /// <remarks>
        /// <para>
        /// One instant, used for both halves of what a frame depicts: the trajectory is
        /// drawn up to it and the field is sampled at it. Two fields that had to agree
        /// would be one too many - a frame is a moment, and the ion and the electrodes
        /// are in it together.
        /// </para>
        /// <para>
        /// Null draws the whole flight and samples the field at
        /// <see cref="RenderSpec.AtSeconds"/>. When set, the trajectory's last point is
        /// marked, because a frame of an animation shows where the ion <em>is</em> and a
        /// polyline that grows says only where it has been.
        /// </para>
        /// </remarks>
        public double? AtSeconds { get; init; }

        /// <summary>
        /// The potential range the equipotential levels are spread over, or null to take
        /// it from this frame alone.
        /// </summary>
        /// <remarks>
        /// Supplied by an animation, because a driven field's range changes through the
        /// cycle and levels chosen per frame would make the contours flicker - the same
        /// defect as a page chosen per frame, in the other axis. Fixed once over the
        /// whole animation, the contours move because the field moves.
        /// </remarks>
        public (double Low, double High)? PotentialRange { get; init; }

        /// <summary>
        /// The density peak the contour decades are measured from, or null to take it
        /// from this frame.
        /// </summary>
        /// <remarks>
        /// <b>Not the flicker problem again - a worse one.</b> Density contours are drawn
        /// at decades below the peak, and a diffusing packet's peak falls as it spreads.
        /// Anchored per frame the levels would fall with it, the contours would stay the
        /// same size, and a film of a packet spreading would show a packet doing nothing.
        /// Anchored once, later frames show fewer contours because the density really is
        /// lower, which is what happened.
        /// </remarks>
        public double? DensityPeak { get; init; }

        /// <summary>A line stamped across the top of the page.</summary>
        /// <remarks>
        /// RND-7's rate display. Written by the renderer rather than offered as a
        /// styling option, and placed in the top margin where it cannot collide with the
        /// provenance row or a taint rule - a frame is the artifact most likely to be
        /// shown with none of its apparatus attached, and this is the apparatus that
        /// makes a compressed timeline honest.
        /// </remarks>
        public string? Banner { get; init; }
    }

    /// <summary>Renders a section of a model.</summary>
    /// <param name="model">The validated model.</param>
    /// <param name="spec">What to draw.</param>
    /// <param name="provenance">Lines to record in the output and stamp on the page.</param>
    /// <param name="densities">
    /// The densities a diffusive run produced, drawn in place of the trajectories that mode
    /// does not have, or null when there are none. A list because a model may declare several
    /// ion populations, and drawing one of them would be a picture of a different experiment.
    /// </param>
    /// <returns>The figure.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The model has no extent to draw.</exception>
    /// <remarks>
    /// The density is passed in rather than computed here. Running the transport is
    /// the command layer's job - it owns turning a model document into a density
    /// problem - and a renderer that could do it would be a renderer that decides
    /// how long a run lasts.
    /// </remarks>
    /// <param name="plan">
    /// What a caller drawing many figures of one model has already computed, or null to
    /// compute it here.
    /// </param>
    /// <param name="gas">
    /// The gas the drawn ion flies through, already resolved. Omitted, it is built from
    /// the model - which <b>refuses</b> a declared but unresolved imported field rather
    /// than falling back to a vacuum, because a figure drawn in the wrong gas is a
    /// drawing of a different instrument and nothing on it would say so.
    /// </param>
    /// <param name="transportWarnings">
    /// REG-2's verdict on the flight being drawn, computed by the caller. It is the
    /// caller's because the numbers need the instrument's smallest aperture and its drive
    /// frequency, which are model-format knowledge this assembly deliberately does not
    /// have - and writing a second copy of those two here is how they would come to
    /// disagree with the ones a run reports.
    /// </param>
    public static Figure Render(
        CompiledModel model,
        RenderSpec spec,
        IReadOnlyList<string>? provenance = null,
        IReadOnlyList<Transport.Diffusion.DensityField>? densities = null,
        FramePlan? plan = null,
        Transport.Collisions.BackgroundGas? gas = null,
        IReadOnlyList<ValidityWarning>? transportWarnings = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(spec);

        // Reported, not bare: a field that stopped short of its tolerance must taint
        // the picture drawn from it as surely as it taints a number (GRD-2). A
        // figure is the artifact most likely to be shown with none of the
        // uncertainty apparatus attached, which is RND-10's argument for video and
        // applies here for the same reason.
        var (field, warnings) = plan?.Field is { } prebuilt
            ? (prebuilt, plan.FieldWarnings)
            : FieldAssembly.BuildReported(model);

        var plane = PlaneFor(model, spec);

        // Flown before the extent is chosen, not after, because an analytic model has
        // no declared domain and the flight is then the only thing that says how big
        // the instrument is. The scaffolded reflectron launches and is caught within a
        // few millimetres of the same place while the ion travels 1.3 m into the mirror
        // and back - so an extent taken from the source and the detector alone put the
        // turning point 105 metres off a 160 mm page. It is one flight either way: this
        // is the same scout the trajectory drawing needed.
        var mode = Transport.TransportModes.All.FirstOrDefault(
            m => string.Equals(m.Name, model.TransportMode, StringComparison.OrdinalIgnoreCase));

        var drawable = mode?.ProducesTrajectories ?? true;

        var flown = spec.Trajectory && drawable
            ? plan?.Trajectory ?? FlyForDrawing(model, spec, field, gas)
            : null;

        // GRD-2: whatever the caller established about the validity of this flight
        // taints the drawing of it, with the same hatched rule a short-converged field
        // earns. A figure of an ion flying at a millibar is a figure of a description
        // that may not apply, and the page is the one place that is never read beside
        // the result envelope.
        if (transportWarnings is { Count: > 0 })
        {
            warnings = [.. warnings, .. transportWarnings];
        }

        var (minU, minV, maxU, maxV) = Extent(model, plane, flown);

        var spanU = maxU - minU;
        var spanV = maxV - minV;

        if (!(spanU > 0.0) || !(spanV > 0.0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(model), "this model has no extent in the section plane to draw");
        }

        var drawWidth = spec.WidthMm - (2.0 * spec.MarginMm);

        // A page narrower than its two margins has no drawing area, and everything
        // downstream - the scale, the decimation tolerance - goes negative and fails
        // deep inside as an internal error. It is the caller's number, so it is
        // refused as one: "--width-mm" is the page, not a window onto the model.
        if (!(drawWidth > 0.0))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/widthMm",
                Constraint = $"the page width must exceed twice the margin, {2.0 * spec.MarginMm:G3} mm, to leave any drawing area",
                Observed = new ObservedValue(spec.WidthMm, "mm"),
                Suggestion = "widthMm is the width of the page in millimetres of paper, not a window in the model; "
                    + "160 mm is a full-page figure and 80 mm a column. To show part of a model, choose the "
                    + "section plane and offset rather than a narrower page",
            });
        }

        var scale = drawWidth / spanU;
        var drawHeight = spanV * scale;

        var captionRoom = (spec.Caption is null ? 0.0 : 5.0) + 9.0;
        var pageHeight = drawHeight + (2.0 * spec.MarginMm) + captionRoom;

        // ACC-7: a fraction of the drawing's extent, converted once into the page
        // units every polyline is decimated in.
        var tolerance = spec.DecimationFraction * Math.Max(drawWidth, drawHeight);

        PagePoint ToPage(double u, double v) => new(
            spec.MarginMm + ((u - minU) * scale),
            spec.MarginMm + ((maxV - v) * scale));

        var paths = new List<ScenePath>();
        var texts = new List<SceneText>();

        var equipotentialStyle = new PathStyle(Equipotential, 0.13, Dash: DashStyle.Solid, Opacity: 0.85);

        // The instant this figure is of. A static field ignores it; a driven or
        // sequenced one does not, and until now was sampled through the time-free
        // interface it also implements - which answers at t = 0 without failing. That
        // is the same defect that had `einzel solve` reporting the DC pattern of a
        // driven geometry and the diffusive mode stepping a density through a snapshot
        // of the RF, met a third time in the renderer.
        var at = plan?.AtSeconds ?? spec.AtSeconds;

        if (field is ITimeVaryingField)
        {
            warnings =
            [
                .. warnings,
                new ValidityWarning(
                    "render.field-at-instant",
                    $"this field varies with time, so the equipotentials are the field at "
                    + $"t = {at * 1e6:G6} us and not a field the instrument holds. A figure of a "
                    + "driven structure is a frame of a film whether or not it is drawn as one",
                    WarningSeverity.Provenance),
            ];
        }

        if (spec.Equipotentials > 0)
        {
            DrawEquipotentials(
                paths,
                field,
                at,
                plan?.PotentialRange,
                plane,
                spec,
                minU,
                minV,
                spanU,
                spanV,
                tolerance,
                ToPage,
                equipotentialStyle);
        }

        var (declaredConductors, cutConductors, nearestConductor) =
            DrawConductors(paths, model, plane, spec, minU, minV, spanU, spanV, tolerance, ToPage);

        // A figure of an instrument with no instrument in it, and until now the only sign
        // was a missing key in the path counts. A plane that misses the metal, a device
        // whose electrodes are all off this plane, and an extraction that failed all drew
        // exactly nothing and looked identical.
        if (declaredConductors > 0 && cutConductors == 0)
        {
            var gap = double.IsFinite(nearestConductor)
                ? FormattableString.Invariant(
                    $" The nearest of them is {nearestConductor * 1e3:G4} mm from this plane.")
                : string.Empty;

            warnings =
            [
                .. warnings,
                new ValidityWarning(
                    "render.plane-misses-conductors",
                    $"none of the {declaredConductors} declared conductor(s) intersect the "
                    + "plane being drawn, so this figure shows the field and the ion with no "
                    + "instrument in it." + gap
                    + " Move the plane onto the metal with a render spec's 'plane' block - a "
                    + "device whose electrodes all sit off the beam plane, which is any "
                    + "stripe-on-boards analyser, has nothing to draw on the plane the ion "
                    + "flies in",
                    WarningSeverity.Provenance),
            ];
        }

        var kept = 0;
        var raw = 0;

        // RND-8 and TRN-2: a diffusive region emits a density field, and there are no
        // trajectories in it to draw. Lines through a funnel would depict something
        // the model never computed - which is a stronger objection than inaccuracy,
        // because a reader cannot tell an invented line from a computed one.
        //
        // Asked of the transport mode rather than inferred from the pressure, so the
        // rule holds wherever a mode says it produces no trajectories.
        var densityLevels = Array.Empty<double>();

        if (spec.Trajectory && drawable)
        {
            (kept, raw) = flown is null
                ? (0, 0)
                : DrawSupplied(
                    paths,
                    plan?.AtSeconds is { } until ? Until(flown, until) : flown,
                    plane,
                    tolerance,
                    ToPage,
                    plan?.AtSeconds is not null);
        }

        // Drawn whenever there is a density and contours were asked for. A density is
        // not a trajectory, so the trajectory toggle has no say in it - and nesting
        // this inside the "trajectories were requested" branch, which is how it was
        // written, made --no-trajectory silently suppress the one output a diffusive
        // model has. Two independent questions, asked independently.
        if (densities is { Count: > 0 } && spec.DensityContours > 0)
        {
            // One contour ladder across every population, anchored on the largest peak among
            // them. Per-population levels would draw a small population with as many rings as
            // a large one and make them look equally dense - the same argument that anchors an
            // animation's levels once across its frames rather than per frame.
            var peak = plan?.DensityPeak ?? densities.Max(d => d.Peak());

            foreach (var one in densities)
            {
                var levels = DrawDensity(
                    paths, one, plane, spec, minU, minV, spanU, spanV, tolerance, ToPage, peak);

                if (levels.Length > densityLevels.Length)
                {
                    densityLevels = levels;
                }
            }

            if (densityLevels.Length == 0)
            {
                warnings =
                [
                    .. warnings,
                    new ValidityWarning(
                        "render.density-empty",
                        "the density had nothing left in it to draw: by the end of the run "
                        + "every ion had reached a boundary. What a figure of the end state "
                        + "shows in that case is an empty box, correctly. Draw an earlier "
                        + "instant with '--at-us', which records the density there and lets "
                        + "the run finish - shortening 'maximumFlightTime' would get a packet "
                        + "too, by throwing away everything after the moment being looked at",
                        WarningSeverity.Provenance),
                ];
            }
        }

        // RND-8, and it is a statement about what was asked for: a trajectory was
        // requested and this mode does not produce one. A caller who never asked has
        // nothing to be told.
        if (spec.Trajectory && !drawable)
        {
            warnings =
            [
                .. warnings,
                new ValidityWarning(
                    "render.no-trajectories",
                    $"this model declares '{model.TransportMode}' transport, which computes a "
                    + "density rather than trajectories, so none were drawn. RND-8 forbids drawing "
                    + "lines through a diffusive region: they would depict something the model "
                    + "never produced"
                    + (densityLevels.Length > 0
                        ? ". The contours are the density itself, at decades below its peak"
                        : ". No density was drawn either, so this figure shows the geometry "
                        + "and the field alone"),
                    WarningSeverity.Provenance),
            ];
        }

        DrawDimensions(paths, texts, DimensionCompiler.Compile(spec, model), plane, ToPage);

        DrawFrame(paths, texts, spec, minU, maxU, minV, maxV, scale, drawWidth, drawHeight);

        var lines = new List<string>(provenance ?? []);
        lines.Add($"decimation tolerance {tolerance:G3} mm of a {drawWidth:F1} by {drawHeight:F1} mm drawing");

        if (densityLevels.Length > 0)
        {
            lines.Add(
                "density contours, ions per cubic metre: "
                + string.Join(", ", densityLevels.Select(v => v.ToString("G4", System.Globalization.CultureInfo.InvariantCulture))));
        }

        foreach (var warning in warnings)
        {
            lines.Add($"WARNING {warning.Code}: {warning.Message}");
        }

        DrawStamp(paths, texts, spec, pageHeight, warnings, tolerance);

        // In the top margin, above the drawing, so it cannot collide with the
        // provenance row or a taint rule along the bottom - and so it is the first
        // thing on the page rather than the last. RND-7 asks for the rate to be
        // displayed THROUGHOUT playback, which means on the frame rather than in the
        // documentation beside it.
        if (plan?.Banner is { } banner)
        {
            texts.Add(new SceneText(
                banner,
                new PagePoint(spec.MarginMm, spec.MarginMm - 3.0),
                6.0,
                TextAnchor.Start,
                Ink,
                "timebase"));
        }

        if (spec.Caption is { } caption)
        {
            texts.Add(new SceneText(
                caption,
                new PagePoint(spec.MarginMm, spec.MarginMm + drawHeight + 6.0),
                7.5,
                TextAnchor.Start,
                Ink,
                "caption"));
        }

        var scene = new Scene(spec.WidthMm, pageHeight, paths, texts, lines);

        return new Figure(scene, warnings, tolerance, kept, raw);
    }

    private static SectionPlane PlaneFor(CompiledModel model, RenderSpec spec)
    {
        if (spec.Plane is { Normal.Count: 3 } declared)
        {
            var normal = new Vec3(declared.Normal[0], declared.Normal[1], declared.Normal[2]);

            var across = declared.AcrossMm is { Count: 3 } a
                ? new Vec3(a[0], a[1], a[2])
                : (Vec3?)null;

            return new SectionPlane(normal * (declared.OffsetMm * 1e-3), normal, across);
        }

        // A two-dimensional solve is invariant along z, so its own plane is the
        // section and no cut has to be chosen. Nothing here needs to know that a
        // model is two-dimensional other than that its field does not vary along z.
        var flat = model.Fields.All(f => f.Solve3D is null);

        return flat
            ? new SectionPlane(Vec3.Zero, new Vec3(0.0, 0.0, 1.0), new Vec3(1.0, 0.0, 0.0))
            : new SectionPlane(Vec3.Zero, new Vec3(0.0, 1.0, 0.0), new Vec3(0.0, 0.0, 1.0));
    }

    private static (double MinU, double MinV, double MaxU, double MaxV) Extent(
        CompiledModel model, SectionPlane plane, IReadOnlyList<TrajectorySample>? flown)
    {
        var minU = double.PositiveInfinity;
        var minV = double.PositiveInfinity;
        var maxU = double.NegativeInfinity;
        var maxV = double.NegativeInfinity;

        void Include(Vec3 point)
        {
            var (u, v) = plane.Project(point);

            minU = Math.Min(minU, u);
            maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v);
            maxV = Math.Max(maxV, v);
        }

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } flat)
            {
                // A cylindrical solve is a half-plane in (axial, radial), and a
                // section through the axis of one shows both halves - a ring is two
                // conductors on the page, not one. SYM-1, declared on the solve, so
                // this is symmetry knowledge rather than device knowledge; the
                // renderer still has no idea what the rings are for.
                var mirrored = flat.Symmetry == SolveSymmetry.Cylindrical;

                foreach (var x in new[] { flat.MinX, flat.MaxX })
                {
                    foreach (var y in mirrored
                        ? [-flat.MaxY, flat.MaxY]
                        : new[] { flat.MinY, flat.MaxY })
                    {
                        Include(new Vec3(x, y, 0.0));
                    }
                }
            }

            if (element.Solve3D is { } volume)
            {
                foreach (var x in new[] { volume.MinX, volume.MaxX })
                {
                    foreach (var y in new[] { volume.MinY, volume.MaxY })
                    {
                        foreach (var z in new[] { volume.MinZ, volume.MaxZ })
                        {
                            Include(new Vec3(x, y, z));
                        }
                    }
                }
            }
        }

        // An analytic model has no declared domain, so the instrument's own points
        // are the extent: where the ion starts, where it is caught, and - the part
        // that was missing - everywhere it went in between. A reflectron is launched
        // and caught in nearly the same place, so source and detector alone describe a
        // box the flight leaves immediately and by three orders of magnitude.
        if (double.IsInfinity(minU))
        {
            Include(model.SourcePosition);
            Include(model.DetectorPoint);

            if (flown is not null)
            {
                foreach (var sample in flown)
                {
                    Include(sample.Position);
                }
            }

            // From the extent actually gathered, not from the separation of source and
            // detector. Those two coincide in any instrument that catches the ion where
            // it launched - the scaffolded reflectron does exactly that - and a pad
            // taken from their separation is then a tenth of a millimetre around a
            // flight of 1.3 metres.
            var pad = 0.1 * Math.Max(1e-3, Math.Max(maxU - minU, maxV - minV));

            minU -= pad;
            maxU += pad;
            minV -= pad;
            maxV += pad;
        }

        // A section exactly on the axis of a symmetric instrument has no height at
        // all. Give it one rather than dividing by zero later.
        if (maxV - minV < 1e-12)
        {
            var pad = 0.1 * Math.Max(1e-6, maxU - minU);

            minV -= pad;
            maxV += pad;
        }

        return (minU, minV, maxU, maxV);
    }

    private static void DrawEquipotentials(
        List<ScenePath> paths,
        IElectrostaticField field,
        double atSeconds,
        (double Low, double High)? range,
        SectionPlane plane,
        RenderSpec spec,
        double minU,
        double minV,
        double spanU,
        double spanV,
        double tolerance,
        Func<double, double, PagePoint> toPage,
        PathStyle style)
    {
        var columns = Math.Max(8, spec.SampleColumns);
        var rows = Math.Max(8, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        var values = Contours.Sample(
            plane, minU, minV, stepU, stepV, columns, rows, Sampler(field, atSeconds));

        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;

        if (range is { } fixedRange)
        {
            (low, high) = fixedRange;
        }
        else
        {
            foreach (var value in values)
            {
                if (!double.IsFinite(value))
                {
                    continue;
                }

                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }
        }

        if (!double.IsFinite(low) || high - low <= 0.0)
        {
            return;
        }

        for (var k = 1; k <= spec.Equipotentials; k++)
        {
            var level = low + ((high - low) * k / (spec.Equipotentials + 1.0));

            foreach (var run in Contours.Trace(values, minU, minV, stepU, stepV, level))
            {
                Emit(paths, run, tolerance, toPage, style, "equipotentials");
            }
        }
    }

    /// <summary>
    /// The density, as contour lines at decades below its peak.
    /// </summary>
    /// <returns>
    /// The levels actually drawn, in ions per cubic metre, highest first - empty
    /// when there was no density left to draw.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Decades rather than even fractions. A density is not a quantity with a scale
    /// - the core of a packet and its tail differ by six orders of magnitude - so
    /// evenly spaced levels put every line inside the core and draw the extent,
    /// which is the part a reader most wants, not at all.
    /// </para>
    /// <para>
    /// Lines rather than filled bands. Marching squares gives runs, and a filled
    /// band needs the runs nested into rings and holes, which is a different
    /// algorithm and one whose failures are silent - a hole drawn as a solid reads
    /// as a denser region rather than as a bug.
    /// </para>
    /// </remarks>
    private static double[] DrawDensity(
        List<ScenePath> paths,
        Transport.Diffusion.DensityField density,
        SectionPlane plane,
        RenderSpec spec,
        double minU,
        double minV,
        double spanU,
        double spanV,
        double tolerance,
        Func<double, double, PagePoint> toPage,
        double? fixedPeak)
    {
        var peak = fixedPeak ?? density.Peak();

        // Not merely zero: a run that collected everything leaves a residue many
        // orders below one ion in the whole domain, and contouring that would draw
        // the shape of the round-off.
        var floor = 1e-6 * Math.Max(1.0, density.Population()) / Math.Max(spanU * spanV, 1e-12);

        if (!(peak > floor))
        {
            return [];
        }

        var columns = Math.Max(8, spec.SampleColumns);
        var rows = Math.Max(8, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        var values = Contours.Sample(
            plane, minU, minV, stepU, stepV, columns, rows,
            point => density.SampleAt(point.X, point.Y));

        var levels = new List<double>();

        for (var k = 1; k <= spec.DensityContours; k++)
        {
            var level = peak * Math.Pow(10.0, -k);

            if (level <= floor)
            {
                break;
            }

            // Fainter with each decade, so the core reads as the core. The eye takes
            // line weight for concentration whatever the caption says, and a tail
            // drawn as heavily as a peak is a picture that lies about where the ions
            // are.
            var style = new PathStyle(
                DensityInk, 0.30 - (0.03 * k), Dash: DashStyle.Solid, Opacity: 0.95 - (0.09 * k));

            var drawn = false;

            foreach (var run in Contours.Trace(values, minU, minV, stepU, stepV, level))
            {
                Emit(paths, run, tolerance, toPage, style, "density");
                drawn = true;
            }

            if (drawn)
            {
                levels.Add(level);
            }
        }

        return [.. levels];
    }

    private static (int Declared, int Cut, double Nearest) DrawConductors(
        List<ScenePath> paths,
        CompiledModel model,
        SectionPlane plane,
        RenderSpec spec,
        double minU,
        double minV,
        double spanU,
        double spanV,
        double tolerance,
        Func<double, double, PagePoint> toPage)
    {
        // Finer than the equipotentials, because a conductor edge is a hard line and
        // the eye reads a polygonal circle immediately where it forgives a polygonal
        // equipotential.
        var columns = Math.Max(16, (int)(spec.SampleColumns * 1.5));
        var rows = Math.Max(16, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        var fill = new PathStyle(ConductorEdge, 0.18, ConductorFill);

        // Counted so the caller can tell "this plane misses the metal" from "this device
        // has no metal" and from "the extraction failed". All three drew nothing, and
        // omitting the key left them indistinguishable.
        var declared = 0;
        var cut = 0;
        var nearest = double.PositiveInfinity;

        foreach (var element in model.Fields)
        {
            // The half-plane of a cylindrical solve is mirrored about the axis, so a
            // ring drawn from its own signed distance appears on both sides. The
            // field needs no such treatment: a cylindrical solve is already wrapped
            // as an axisymmetric field, which samples at the radius of the point it
            // is asked about and so answers for a negative one already.
            var mirrored = element.Solve?.Symmetry == SolveSymmetry.Cylindrical;

            foreach (var electrode in element.Solve?.Electrodes ?? [])
            {
                declared++;

                // As the solve holds it, ended against every domain edge that is not a
                // mirror. The page is the domain, so a face lying on a domain edge lies on
                // the page's last row with nothing sampled past it, and was left open - and
                // an open path filled is closed by a chord, which drew the einzel lens's
                // tubes as wedges.
                var (values, _) = HeldConductor.Sample(
                    element.Solve!,
                    electrode,
                    Math.Min(stepU, stepV),
                    distance => Contours.Sample(
                        plane, minU, minV, stepU, stepV, columns, rows,
                        point => distance(point.X, mirrored ? Math.Abs(point.Y) : point.Y)));

                nearest = Math.Min(nearest, Nearest(values));

                foreach (var run in Contours.Trace(values, minU, minV, stepU, stepV, 0.0))
                {
                    cut++;
                    Emit(paths, run, tolerance, toPage, fill, "conductors");
                }
            }

            foreach (var electrode in element.Solve3D?.Electrodes ?? [])
            {
                declared++;

                var values = Contours.Sample(
                    plane, minU, minV, stepU, stepV, columns, rows,
                    point => electrode.SignedDistance(point.X, point.Y, point.Z));

                nearest = Math.Min(nearest, Nearest(values));

                foreach (var run in Contours.Trace(values, minU, minV, stepU, stepV, 0.0))
                {
                    cut++;
                    Emit(paths, run, tolerance, toPage, fill, "conductors");
                }
            }
        }

        return (declared, cut, nearest);
    }

    /// <summary>The smallest signed distance anywhere on the sampled plane, in metres.</summary>
    /// <remarks>
    /// <b>This is the actionable half of the warning.</b> "No conductor lies on this plane"
    /// sends a reader looking for a bug; "the nearest conductor is 1.0 mm away" tells them
    /// they aimed a plane at a 20-to-24 mm board and cut at 25. The samples are taken on the
    /// plane and the distance is a distance in space, so it is the real gap rather than an
    /// in-plane one.
    /// </remarks>
    private static double Nearest(double[,] values)
    {
        var least = double.PositiveInfinity;

        foreach (var value in values)
        {
            least = Math.Min(least, value);
        }

        return least;
    }

    /// <summary>Draws the declared dimensions, measuring each as it goes.</summary>
    /// <remarks>
    /// <para>
    /// Extension lines from the two points, a dimension line between them offset
    /// perpendicular, an arrowhead at each end, and the measured length at the middle.
    /// The convention is the ordinary drafting one, which is what makes a drawing
    /// readable by somebody who has never seen this program.
    /// </para>
    /// <para>
    /// <b>Drawn after everything else and before the frame</b>, so a dimension sits over
    /// the geometry it measures rather than under it - the one thing on the page that is
    /// an annotation rather than a depiction, and the one that has to stay legible.
    /// </para>
    /// </remarks>
    private static void DrawDimensions(
        List<ScenePath> paths,
        List<SceneText> texts,
        IReadOnlyList<CompiledDimension> dimensions,
        SectionPlane plane,
        Func<double, double, PagePoint> toPage)
    {
        foreach (var dimension in dimensions)
        {
            var (fromU, fromV) = plane.Project(dimension.FromSi);
            var (toU, toV) = plane.Project(dimension.ToSi);

            var a = toPage(fromU, fromV);
            var b = toPage(toU, toV);

            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));

            // A span that projects to a point on this plane has no direction to offset
            // along and no length to show. Skipped rather than drawn as a dot, which
            // would look like a dimension of zero rather than like a dimension seen end
            // on.
            if (!(length > 1e-9))
            {
                continue;
            }

            // Anticlockwise perpendicular, so a positive offset is consistently one side
            // of the span whichever way round the ends were given.
            var nx = -dy / length;
            var ny = dx / length;

            var offsetX = nx * dimension.OffsetMm;
            var offsetY = ny * dimension.OffsetMm;

            var lineA = new PagePoint(a.X + offsetX, a.Y + offsetY);
            var lineB = new PagePoint(b.X + offsetX, b.Y + offsetY);

            var style = new PathStyle(DimensionInk, 0.15);

            // Extension lines, starting clear of the feature and running a little past
            // the dimension line, which is the drafting convention and is what stops
            // them reading as part of the geometry.
            paths.Add(new ScenePath(
                [
                    new PagePoint(a.X + (nx * ExtensionGapMm), a.Y + (ny * ExtensionGapMm)),
                    new PagePoint(
                        a.X + (nx * (dimension.OffsetMm + ExtensionOvershootMm)),
                        a.Y + (ny * (dimension.OffsetMm + ExtensionOvershootMm))),
                ],
                false, style, "dimension"));

            paths.Add(new ScenePath(
                [
                    new PagePoint(b.X + (nx * ExtensionGapMm), b.Y + (ny * ExtensionGapMm)),
                    new PagePoint(
                        b.X + (nx * (dimension.OffsetMm + ExtensionOvershootMm)),
                        b.Y + (ny * (dimension.OffsetMm + ExtensionOvershootMm))),
                ],
                false, style, "dimension"));

            paths.Add(new ScenePath([lineA, lineB], false, style, "dimension"));

            Arrow(paths, lineA, dx / length, dy / length);
            Arrow(paths, lineB, -dx / length, -dy / length);

            // Above the line, on the side away from the feature, so the text never sits
            // on top of what it measures.
            texts.Add(new SceneText(
                dimension.Text(),
                new PagePoint(
                    ((lineA.X + lineB.X) / 2.0) + (nx * TextLiftMm),
                    ((lineA.Y + lineB.Y) / 2.0) + (ny * TextLiftMm)),
                5.5,
                TextAnchor.Middle,
                DimensionInk,
                "dimension"));
        }
    }

    /// <summary>A filled arrowhead at a point, pointing along a unit direction.</summary>
    private static void Arrow(List<ScenePath> paths, PagePoint at, double ux, double uy)
    {
        var backX = at.X + (ux * ArrowLengthMm);
        var backY = at.Y + (uy * ArrowLengthMm);

        paths.Add(new ScenePath(
            [
                at,
                new PagePoint(backX - (uy * ArrowHalfWidthMm), backY + (ux * ArrowHalfWidthMm)),
                new PagePoint(backX + (uy * ArrowHalfWidthMm), backY - (ux * ArrowHalfWidthMm)),
            ],
            true,
            new PathStyle(null, 0.0, DimensionInk),
            "dimension"));
    }

    /// <summary>Reads a field at an instant, whether or not it has one.</summary>
    /// <remarks>
    /// A driven field implements the time-free interface too and answers at t = 0
    /// through it, silently. Asking for the instant explicitly is the difference between
    /// a figure of a moment and a figure of an arbitrary moment.
    /// </remarks>
    private static Func<Vec3, double> Sampler(IElectrostaticField field, double atSeconds) =>
        field is ITimeVaryingField driven
            ? point => driven.PotentialAt(in point, atSeconds)
            : point => field.PotentialAt(in point);

    /// <summary>The potential range a field covers over a set of instants.</summary>
    /// <param name="model">The validated model.</param>
    /// <param name="spec">What would be drawn.</param>
    /// <param name="field">The field, already built.</param>
    /// <param name="instants">The instants to cover, in seconds.</param>
    /// <returns>The lowest and highest potential over all of them, or null if none.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <para>
    /// What an animation fixes its contour levels from. A driven field's range changes
    /// through the cycle, so levels taken per frame would make the contours flicker -
    /// which reads as the field being noisy rather than as the levels moving. Fixed
    /// once, they move because the field moves.
    /// </para>
    /// <para>
    /// Sampled on a coarser grid than the drawing, because the extremes of a Laplace
    /// solution are on its boundaries and a coarse grid finds those perfectly well.
    /// This is a range, not a contour.
    /// </para>
    /// </remarks>
    public static (double Low, double High)? PotentialRange(
        CompiledModel model,
        RenderSpec spec,
        IElectrostaticField field,
        IReadOnlyList<double> instants)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(instants);

        if (instants.Count == 0)
        {
            return null;
        }

        var plane = PlaneFor(model, spec);
        var (minU, minV, maxU, maxV) = Extent(model, plane, null);

        var spanU = maxU - minU;
        var spanV = maxV - minV;

        if (!(spanU > 0.0) || !(spanV > 0.0))
        {
            return null;
        }

        const int Columns = 96;

        var rows = Math.Max(8, (int)Math.Round(Columns * spanV / spanU));
        var stepU = spanU / (Columns - 1);
        var stepV = spanV / (rows - 1);

        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;

        foreach (var instant in instants)
        {
            foreach (var value in Contours.Sample(
                plane, minU, minV, stepU, stepV, Columns, rows, Sampler(field, instant)))
            {
                if (!double.IsFinite(value))
                {
                    continue;
                }

                low = Math.Min(low, value);
                high = Math.Max(high, value);
            }
        }

        return double.IsFinite(low) && high > low ? (low, high) : null;
    }

    /// <summary>The samples up to an instant, with the last one landing exactly on it.</summary>
    /// <remarks>
    /// <para>
    /// Interpolated at the end rather than truncated to the nearest kept sample. A
    /// marker that jumps from sample to sample stutters at exactly the rate the recorder
    /// happened to keep points, which in an adaptive integrator is fastest where the
    /// physics is hardest - so the ion would appear to hesitate in precisely the places
    /// an animation exists to show.
    /// </para>
    /// <para>
    /// Linear between two samples, which is what the drawing already is: the polyline
    /// through the recorded points is a straight line between each pair, so a head
    /// interpolated the same way sits exactly on the line it terminates.
    /// </para>
    /// </remarks>
    private static List<TrajectorySample> Until(
        IReadOnlyList<TrajectorySample> samples, double seconds)
    {
        var kept = new List<TrajectorySample>(Math.Min(samples.Count, 16));

        if (samples.Count == 0)
        {
            return kept;
        }

        var last = 0;

        while (last + 1 < samples.Count && samples[last + 1].TimeSeconds <= seconds)
        {
            last++;
        }

        for (var i = 0; i <= last; i++)
        {
            kept.Add(samples[i]);
        }

        if (last + 1 < samples.Count)
        {
            var a = samples[last];
            var b = samples[last + 1];
            var span = b.TimeSeconds - a.TimeSeconds;
            var f = span > 0.0 ? (seconds - a.TimeSeconds) / span : 0.0;

            if (f > 0.0)
            {
                kept.Add(new TrajectorySample(
                    seconds,
                    a.Position + ((b.Position - a.Position) * f),
                    a.Velocity + ((b.Velocity - a.Velocity) * f)));
            }
        }

        return kept;
    }

    /// <summary>Draws a trajectory somebody else flew, and marks where the ion is.</summary>
    /// <remarks>
    /// The marker is a small closed polygon rather than a circle primitive, because the
    /// scene has paths and text and nothing else - which is what lets the same scene go
    /// to SVG and to a hand-written PDF without either backend knowing about shapes.
    /// </remarks>
    private static (int Kept, int Raw) DrawSupplied(
        List<ScenePath> paths,
        IReadOnlyList<TrajectorySample> samples,
        SectionPlane plane,
        double tolerance,
        Func<double, double, PagePoint> toPage,
        bool markHead)
    {
        if (samples.Count == 0)
        {
            return (0, 0);
        }

        var page = new List<PagePoint>(samples.Count);

        foreach (var sample in samples)
        {
            var (u, v) = plane.Project(sample.Position);
            page.Add(toPage(u, v));
        }

        var kept = 0;

        if (page.Count >= 2)
        {
            var reduced = Decimation.Reduce(page, tolerance);

            paths.Add(new ScenePath(
                reduced, false, new PathStyle(TrajectoryInk, 0.28), "trajectory"));

            kept = reduced.Count;
        }

        if (!markHead)
        {
            return (kept, page.Count);
        }

        var head = page[^1];
        var marker = new List<PagePoint>(HeadSides);

        for (var i = 0; i < HeadSides; i++)
        {
            var angle = 2.0 * Math.PI * i / HeadSides;

            marker.Add(new PagePoint(
                head.X + (HeadRadiusMm * Math.Cos(angle)),
                head.Y + (HeadRadiusMm * Math.Sin(angle))));
        }

        paths.Add(new ScenePath(
            marker, true, new PathStyle(null, 0.0, TrajectoryInk), "ion"));

        return (kept, page.Count);
    }

    /// <summary>Flies the ion once, finely enough to draw.</summary>
    /// <remarks>
    /// <para>
    /// Twice: once at the model's own cadence to learn how long the flight is, then at
    /// a cadence chosen from that. The alternative - drawing whatever the model happens
    /// to sample for its VTU export - gave the einzel lens a three-segment curve
    /// through a focusing element, which is a drawing of the sampling interval rather
    /// than of the optics.
    /// </para>
    /// <para>
    /// Sample finely and decimate to a stated bound is the whole shape of RND-5.
    /// Sampling coarsely and drawing it undecimated loses the guarantee and the curve
    /// at once.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<TrajectorySample>? FlyForDrawing(
        CompiledModel model,
        RenderSpec spec,
        IElectrostaticField field,
        Transport.Collisions.BackgroundGas? gas)
    {
        var species = IonSpecies.FromModel(model);

        // The drawn trajectory has to be the one the run computes, and before this it
        // was not: both integrations went through the `collisions` parameter's default,
        // so a figure of a model declaring a gas drew the vacuum flight. On the
        // thermalisation example that is 2778 mm against the 155 mm the ion reaches -
        // eighteen times too far, silently, in the artifact most likely to be shown
        // detached from the result that would have contradicted it.
        //
        // A fresh sampler per integration, from the model's declared seed, so the two
        // passes below fly the same collisional history and the fine one is a refinement
        // of the scout rather than a different flight.
        var collisions = gas ?? Transport.Collisions.BackgroundGas.FromModel(model.Gas);

        Transport.Collisions.CollisionSampler? Sampler() =>
            collisions.IsPresent
                ? new Transport.Collisions.CollisionSampler(
                    collisions, species.MassSi, species.ChargeSi, model.Gas.Seed)
                : null;
        var launch = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        var scout = new TrajectoryRecorder(model.SampleIntervalSi);

        TrajectoryIntegrator.Integrate(
            launch, species, field, settings, detector, scout, Sampler());

        if (scout.Samples.Count < 2)
        {
            return null;
        }

        var flight = scout.Samples[^1].TimeSeconds - scout.Samples[0].TimeSeconds;
        var samples = Math.Max(16, spec.TrajectorySamples);

        if (!(flight > 0.0))
        {
            return scout.Samples;
        }

        var recorder = new TrajectoryRecorder(flight / samples, capacity: 4 * samples);

        TrajectoryIntegrator.Integrate(
            launch, species, field, settings, detector, recorder, Sampler());

        return recorder.Samples.Count >= 2 ? recorder.Samples : scout.Samples;
    }

    private static void DrawFrame(
        List<ScenePath> paths,
        List<SceneText> texts,
        RenderSpec spec,
        double minU,
        double maxU,
        double minV,
        double maxV,
        double scale,
        double drawWidth,
        double drawHeight)
    {
        var border = new PathStyle(Ink, 0.15);

        paths.Add(new ScenePath(
            [
                new PagePoint(spec.MarginMm, spec.MarginMm),
                new PagePoint(spec.MarginMm + drawWidth, spec.MarginMm),
                new PagePoint(spec.MarginMm + drawWidth, spec.MarginMm + drawHeight),
                new PagePoint(spec.MarginMm, spec.MarginMm + drawHeight),
            ],
            true,
            border,
            "frame"));

        if (spec.DrawAxis)
        {
            // The axis of rotation, which for an axisymmetric device is the line the
            // whole drawing is about and the line an on-axis ion runs along. Dashed,
            // because it is a construction line rather than a part.
            var axisY = spec.MarginMm + ((maxV - 0.0) * scale);

            if (axisY > spec.MarginMm && axisY < spec.MarginMm + drawHeight)
            {
                paths.Add(new ScenePath(
                    [
                        new PagePoint(spec.MarginMm, axisY),
                        new PagePoint(spec.MarginMm + drawWidth, axisY),
                    ],
                    false,
                    new PathStyle(Ink, 0.12, Dash: DashStyle.Dashed, Opacity: 0.55),
                    "axis"));
            }
        }

        if (!spec.ScaleBar)
        {
            return;
        }

        // A round number of millimetres that occupies roughly a fifth of the
        // drawing. A scale bar of 23.7 mm is arithmetically correct and useless.
        var wanted = 0.2 * (maxU - minU) * 1e3;
        var decade = Math.Pow(10.0, Math.Floor(Math.Log10(wanted)));
        var lengthMm = decade * (wanted / decade >= 5.0 ? 5.0 : wanted / decade >= 2.0 ? 2.0 : 1.0);

        var barPage = lengthMm * 1e-3 * scale;

        var y = spec.MarginMm + drawHeight - 4.0;
        var x = spec.MarginMm + 4.0;

        paths.Add(new ScenePath(
            [new PagePoint(x, y), new PagePoint(x + barPage, y)],
            false,
            new PathStyle(Ink, 0.4),
            "scale"));

        texts.Add(new SceneText(
            lengthMm >= 1.0 ? $"{lengthMm:G3} mm" : $"{lengthMm * 1e3:G3} um",
            new PagePoint(x + (0.5 * barPage), y - 1.2),
            6.5,
            TextAnchor.Middle,
            Ink,
            "scale"));
    }

    private static void DrawStamp(
        List<ScenePath> paths,
        List<SceneText> texts,
        RenderSpec spec,
        double pageHeight,
        IReadOnlyList<ValidityWarning> warnings,
        double tolerance)
    {
        var baseline = pageHeight - 3.5;

        texts.Add(new SceneText(
            $"decimated to {tolerance:G3} mm",
            new PagePoint(spec.WidthMm - spec.MarginMm, baseline),
            5.5,
            TextAnchor.End,
            "#6b7280",
            "provenance"));

        if (warnings.Count == 0)
        {
            return;
        }

        // RND-11 and GRD-5: a figure carrying a warning has to be visually
        // distinguishable, not merely annotated in a corner nobody reads. A hatched
        // rule the width of the page is hard to crop out by accident and survives
        // being pasted into a slide.
        var y = pageHeight - 8.0;

        for (var x = 0.0; x < spec.WidthMm; x += 3.0)
        {
            paths.Add(new ScenePath(
                [new PagePoint(x, y + 1.6), new PagePoint(Math.Min(x + 1.6, spec.WidthMm), y)],
                false,
                new PathStyle(TaintInk, 0.35),
                "taint"));
        }

        texts.Add(new SceneText(
            warnings.Count == 1
                ? $"QUALIFIED: {warnings[0].Code}"
                : $"QUALIFIED: {warnings.Count} warnings, first {warnings[0].Code}",
            new PagePoint(spec.MarginMm, baseline),
            5.5,
            TextAnchor.Start,
            TaintInk,
            "taint"));
    }

    private static void Emit(
        List<ScenePath> paths,
        Contours.Run run,
        double tolerance,
        Func<double, double, PagePoint> toPage,
        PathStyle style,
        string layer)
    {
        if (run.Points.Count < 2)
        {
            return;
        }

        var page = new List<PagePoint>(run.Points.Count);

        foreach (var (u, v) in run.Points)
        {
            page.Add(toPage(u, v));
        }

        var reduced = Decimation.Reduce(page, tolerance);

        if (reduced.Count < 2)
        {
            return;
        }

        paths.Add(new ScenePath(reduced, run.Closed, style, layer));
    }
}
