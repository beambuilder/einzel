using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Render;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>One ion's path, as something can draw it.</summary>
/// <param name="PointsMm">The path, in millimetres, in order.</param>
/// <param name="EnergyEv">Kinetic energy at each point, in electronvolts.</param>
/// <param name="Fate">How it ended, named as the model author named the surface.</param>
/// <remarks>
/// Energy per point rather than per path, because §16 asks for trajectory bundles
/// coloured by energy - and an ion that has crossed a mirror twice has had several
/// energies, so one number per path would be a colour for a quantity that varied.
/// </remarks>
public sealed record TrajectoryPath(
    IReadOnlyList<IReadOnlyList<double>> PointsMm,
    IReadOnlyList<double> EnergyEv,
    string Fate);

/// <summary>One conductor, as a surface something can draw.</summary>
/// <param name="Name">The name the model author gave it.</param>
/// <param name="PotentialVolts">What it holds, in volts. The DC part where it is driven.</param>
/// <param name="DriveAmplitudeVolts">Its drive amplitude, or zero where it is static.</param>
/// <param name="VerticesMm">Positions, as consecutive x, y, z triples in millimetres.</param>
/// <param name="Normals">Outward unit normals, one triple per vertex.</param>
/// <param name="Triangles">Vertex indices, three per triangle.</param>
/// <remarks>
/// <b>The DC potential and the drive amplitude both, because reading only the first is a
/// mistake this project has made five times.</b> A quadrupole's rods hold zero volts of DC
/// and all of their potential as drive; an electrode coloured by its DC alone would paint
/// a mass filter as an earthed box.
/// </remarks>
public sealed record ConductorSurface(
    string Name,
    double PotentialVolts,
    double DriveAmplitudeVolts,
    IReadOnlyList<double> VerticesMm,
    IReadOnlyList<double> Normals,
    IReadOnlyList<int> Triangles);

/// <summary>Where a flight begins and where it is caught.</summary>
/// <param name="SourceMm">The launch point, as x, y, z in millimetres.</param>
/// <param name="LaunchDirection">Unit vector the ion sets off along.</param>
/// <param name="DetectorMm">A point on the detector plane, in millimetres.</param>
/// <param name="DetectorNormal">The plane normal, pointing back into the flight volume.</param>
/// <param name="SpanMm">
/// How large to draw them, in millimetres - a fraction of the instrument, so a marker is
/// visible on a 600 mm analyser and not overwhelming on a 20 mm lens.
/// </param>
/// <remarks>
/// <para>
/// <b>A drawing of an instrument with no beginning and no end is hard to read</b>, and this
/// analyser made it obvious: ions launch 10 mm into a 350 mm drift, reverse, and are caught
/// on a plane behind where they started. Nothing in the picture said which end was which,
/// so a viewer had to infer the direction of travel from the trajectory's own shape.
/// </para>
/// <para>
/// <b>Both are drawing conventions rather than dimensions</b> (GRD-12). A source is a point
/// and a detector is an unbounded plane; the sphere and the quad drawn for them have sizes
/// chosen to be visible, and <see cref="SpanMm"/> is that choice made explicit rather than
/// left in the shell where a reader could mistake it for the detector's extent.
/// </para>
/// </remarks>
public sealed record FlightEnds(
    IReadOnlyList<double> SourceMm,
    IReadOnlyList<double> LaunchDirection,
    IReadOnlyList<double> DetectorMm,
    IReadOnlyList<double> DetectorNormal,
    double SpanMm);

/// <summary>One shell of the density, at a stated level.</summary>
/// <param name="DensityPerCubicMetre">The level this surface sits at, in ions per m3.</param>
/// <param name="DecadesBelowPeak">
/// How far below the packet's peak it is, which is the number a reader actually uses.
/// </param>
/// <param name="VerticesMm">Consecutive x, y, z triples, in millimetres.</param>
/// <param name="Normals">One unit normal per vertex, outward.</param>
/// <param name="Triangles">Vertex indices, three per triangle.</param>
/// <remarks>
/// <b>The level is on the record, not implied by the order</b> (GRD-12). A shell drawn
/// without the density it stands for is a shape rather than a measurement, and a reader
/// looking at three nested surfaces has no way to tell a packet spanning one decade from
/// one spanning six.
/// </remarks>
public sealed record DensityShell(
    double DensityPerCubicMetre,
    int DecadesBelowPeak,
    IReadOnlyList<double> VerticesMm,
    IReadOnlyList<double> Normals,
    IReadOnlyList<int> Triangles);

/// <summary>One equipotential, as polylines on the section plane.</summary>
/// <param name="PotentialVolts">The level, in volts.</param>
/// <param name="PathsMm">Each path as consecutive x, y, z triples in millimetres.</param>
public sealed record Equipotential(
    double PotentialVolts,
    IReadOnlyList<IReadOnlyList<double>> PathsMm);

/// <summary>What the interactive viewport draws.</summary>
/// <param name="ModelPath">The model, as an absolute path.</param>
/// <param name="Trajectories">The paths, empty when the mode produces none.</param>
/// <param name="ProducesTrajectories">
/// Whether this model's transport mode produces trajectories at all (RND-8, TRN-2).
/// </param>
/// <param name="LowestEnergyEv">
/// The lowest kinetic energy anywhere in the bundle, or absent when there is no bundle.
/// </param>
/// <param name="HighestEnergyEv">The highest, likewise.</param>
/// <param name="Conductors">The electrodes, as surfaces.</param>
/// <param name="Equipotentials">Level sets of the potential on the section plane.</param>
/// <param name="LowestPotentialVolts">
/// The lowest potential anywhere on the section plane, or absent when there is no field.
/// </param>
/// <param name="HighestPotentialVolts">The highest, likewise.</param>
/// <param name="Density">
/// The density, as nested shells, for a mode that computes one rather than trajectories.
/// </param>
/// <param name="PeakDensityPerCubicMetre">
/// The peak the shells are measured from, or absent where there is no density.
/// </param>
/// <param name="DensityAtUs">
/// The instant the shells are the density at, or absent where there is no density.
/// </param>
/// <param name="Ends">Where the flight begins and where it is caught, or null when
/// the model has no flight to draw ends for.</param>
/// <param name="Warnings">What the viewport must show alongside (GRD-2).</param>
/// <remarks>
/// <para>
/// <b>The energy range is reported once for the whole bundle, and that is the point of
/// its being here at all.</b> §16 asks for trajectory bundles coloured by energy, and a
/// colour scale taken per path would give every ion the same colours whatever its energy
/// - so two ions a kilovolt apart would look identical and the picture would say they
/// were the same. The scale has to be anchored across everything being drawn.
/// </para>
/// <para>
/// The same failure the animation's contour levels had in the other axis: anchored per
/// frame, a film of a packet spreading showed a packet doing nothing.
/// </para>
/// </remarks>
public sealed record ViewportOutcome(
    string ModelPath,
    IReadOnlyList<TrajectoryPath> Trajectories,
    bool ProducesTrajectories,
    double? LowestEnergyEv,
    double? HighestEnergyEv,
    IReadOnlyList<ConductorSurface> Conductors,
    IReadOnlyList<Equipotential> Equipotentials,
    double? LowestPotentialVolts,
    double? HighestPotentialVolts,
    IReadOnlyList<DensityShell> Density,
    double? PeakDensityPerCubicMetre,
    double? DensityAtUs,
    FlightEnds? Ends,
    IReadOnlyList<ValidityWarning> Warnings)
{
    /// <summary>How many declared electrodes are drawn, which is not how many surfaces.</summary>
    /// <returns>The distinct electrode names among <see cref="Conductors"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>A surface is not an electrode.</b> A printed board arrives as a run of pieces,
    /// each carrying the potential at its own position along the edge, and a reflected
    /// solve arrives twice. Counting surfaces put "200 electrodes" on a mirror pair that
    /// declares three.
    /// </para>
    /// <para>
    /// Here rather than in each window, because two windows counting for themselves is how
    /// they come to say different things about one model.
    /// </para>
    /// </remarks>
    public int ElectrodeCount() =>
        Conductors.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count();
}

/// <summary>
/// The data an interactive viewport draws, for anything that needs it.
/// </summary>
/// <remarks>
/// <para>
/// <b>The third time the window has needed something no command returned.</b> After the
/// model tree needed <c>outline</c>, and for the same reason: UI-1 puts file format
/// knowledge and physics outside the shell, so a viewport cannot fly its own ions any
/// more than a model tree can parse its own document.
/// </para>
/// <para>
/// <b>RND-8 is enforced here rather than trusted to the caller.</b> Above about 1e-2 mbar
/// the model computes a density and no trajectories exist; lines through a funnel then
/// depict something the model never computed. The renderer already asks the mode whether
/// it produces trajectories, and so does this - a viewport that asked the pressure
/// instead would be re-deriving a decision the transport mode already owns.
/// </para>
/// <para>
/// <b>Fly twice, sample for the display.</b> The model's own cadence is chosen for VTU
/// and gives a focusing element three segments; so the flight is scouted once to learn
/// how long it is, then re-flown at a cadence chosen from that. The same pattern the
/// section renderer uses, and for the same reason.
/// </para>
/// </remarks>
public static class ViewportCommand
{
    /// <summary>Reads what a viewport should draw.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="samplesPerPath">How finely to sample each path.</param>
    /// <param name="densityAtSeconds">
    /// The instant to draw a diffusive model's density at, or null to choose one. The
    /// end of a run is the wrong default: a packet whose ions arrived leaves nothing
    /// there, so a viewport anchored to it shows an empty box for every model that works.
    /// </param>
    /// <returns>The paths, or none with a reason.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    public static ViewportOutcome Execute(
        string modelPath, int samplesPerPath = 256, double? densityAtSeconds = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerPath, 2);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(absolute)), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var warnings = new List<ValidityWarning>();

        // The field's own warnings ride out with the picture, because a viewport is a
        // number a person reads with their eyes: a bundle drawn through a field that
        // never converged looks exactly like one drawn through a field that did.
        var (field, built) = FieldAssembly.BuildReported(model);

        warnings.AddRange(built);

        var mode = TransportModes.All.FirstOrDefault(
            m => string.Equals(m.Name, model.TransportMode, StringComparison.Ordinal));

        // Asked of the set of modes the RUN uses rather than the one the document
        // declares. A model declaring `trajectory` with a diffusive phase computes a
        // density and was drawn with none; one declaring `diffusion` whose sequence never
        // leaves it was drawn by the wholly diffusive solver, which reads the field once
        // and never looks at the timeline. `einzel run` and both render verbs were
        // corrected the same way; this is the third surface it had to reach.
        if (!(mode?.ProducesTrajectories ?? true) || model.Modes.Contains("diffusion"))
        {
            // RND-8: not an omission to be filled in later, a statement that there is
            // nothing of this kind to draw. A viewport that drew lines here would be
            // depicting something the model never computed.
            warnings.Add(new ValidityWarning(
                "render.no-trajectories",
                $"the '{model.TransportMode}' transport mode computes a density rather "
                + "than trajectories, so there are no paths to draw. What this model has "
                + "instead is a density field, drawn here as nested shells at decades "
                + "below its peak",
                WarningSeverity.Provenance));

            // A MIXED SEQUENCE IS DRAWN AS HALF OF ITSELF, AND SAYS SO. This arm draws the
            // density and no paths - which is a real improvement on what it did before,
            // since the trajectory arm integrates straight through without knowing a
            // sequence exists and so drew lines across the diffusive stretch, the exact
            // thing RND-8 forbids. But a packet that flies for part of its timeline has
            // paths worth seeing, and they are not here. Stated rather than left as an
            // absence, because an empty half looks the same as a half that was not asked
            // for.
            if (model.Modes.Contains("trajectory") && model.Phases.Count > 0)
            {
                warnings.Add(new ValidityWarning(
                    "render.mixed-sequence",
                    "this sequence crosses between the two transport descriptions, and only "
                    + "its diffusive phases are drawn. The trajectory phases have paths and "
                    + "they are not shown: drawing them would need the viewport to walk the "
                    + "timeline, where it currently steps the density alone",
                    WarningSeverity.Provenance));
            }

            // The geometry is still drawn. RND-8 forbids trajectories through a
            // diffusive region, not the instrument they would have flown through - and a
            // funnel with no rings shown is a picture of nothing at all.
            var (conductors, low, high) = Geometry(model, field, warnings);

            var (shells, peak, at) = Cloud(model, field, warnings, densityAtSeconds);

            return new ViewportOutcome(
                absolute, [], false, null, null,
                conductors, Levels(model, field, low, high), low, high,
                shells, peak, at, Ends(model, []), warnings);
        }

        var species = IonSpecies.FromModel(model);

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;
        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        var nominal = new PhaseState(
            model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());

        var cloud = IonCloud.Draw(in nominal, species, model.Cloud, model.SourceDirection);
        var paths = new List<TrajectoryPath>(cloud.Length);

        foreach (var launch in cloud)
        {
            if (Fly(launch, species, field, settings, detector,
                    model.SampleIntervalSi, samplesPerPath) is { } path)
            {
                paths.Add(path);
            }
        }

        if (paths.Count == 0)
        {
            warnings.Add(new ValidityWarning(
                "render.no-path",
                "no ion produced a path with two points in it, so there is nothing to "
                + "draw. An ion that fails at its first step has a position and no "
                + "trajectory",
                WarningSeverity.Provenance));
        }

        // Anchored over everything drawn, not per path. Absent rather than zero when
        // there is no bundle, because zero is a real energy and a reader cannot tell the
        // two apart if both print as zero.
        double? lowest = null;
        double? highest = null;

        foreach (var energy in paths.SelectMany(p => p.EnergyEv))
        {
            lowest = lowest is { } low ? Math.Min(low, energy) : energy;
            highest = highest is { } high ? Math.Max(high, energy) : energy;
        }

        var (surfaces, floor, ceiling) = Geometry(model, field, warnings, paths);

        return new ViewportOutcome(
            absolute, paths, true, lowest, highest,
            surfaces, Levels(model, field, floor, ceiling, paths), floor, ceiling,
            [], null, null, Ends(model, paths), warnings);
    }

    /// <summary>
    /// Handed a drawable bundle while the run that produced it is still going.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A viewport bundle rather than a density, deliberately.</b> UI-1 keeps physics and
    /// file-format knowledge out of the shell, and a window handed a <c>DensityField</c>
    /// would have to contour it - which is the whole of what <see cref="ViewportCommand"/>
    /// exists to do on the other side of that line. What crosses is the same
    /// <see cref="ViewportOutcome"/> a finished run produces, so a frame of a run in flight
    /// and the same instant drawn afterwards are the same record drawn by the same code.
    /// </para>
    /// <para>
    /// <b>Two methods for the same reason <c>IDensityProgress</c> has two.</b>
    /// <see cref="Wants"/> is asked every step and must be cheap; <see cref="Reached"/>
    /// carries the expensive part, which here is contouring the packet and revolving its
    /// shells - hundreds of times what a centroid costs, and wasted on a step nobody will
    /// look at.
    /// </para>
    /// </remarks>
    public interface IViewportProgress
    {
        /// <summary>Whether a frame is wanted now.</summary>
        /// <param name="steps">Steps taken so far.</param>
        /// <param name="timeSeconds">Where the run has reached on its own clock.</param>
        /// <returns>Whether to build a frame for this step.</returns>
        bool Wants(int steps, double timeSeconds);

        /// <summary>A frame of the run as it stands.</summary>
        /// <param name="frame">Everything a viewport draws, at this instant.</param>
        void Reached(ViewportOutcome frame);
    }

    /// <summary>Runs a model's transport, handing out a drawable frame as it goes.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="watcher">Told each frame.</param>
    /// <param name="phaseLimit">
    /// How many phases of a sequence to walk, or null for all of them. Unlike a redraw,
    /// which stops after the first phase because somebody is waiting on it, a watched run
    /// defaults to the whole timeline - watching is what one does instead of waiting.
    /// </param>
    /// <returns>The bundle the finished run leaves, with every warning it earned.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="watcher"/> is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// The model does not validate, or it computes no density and so has nothing to watch.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>The geometry is built once and every frame shares it.</b> The sequencer already
    /// refuses a stage that moves an electrode - it may change what one holds, not where it
    /// is - so the conductor meshes are the same at every instant of a run by construction,
    /// and extracting them per frame would be the dominant cost of watching. What does move
    /// is the packet, which is the thing being watched.
    /// </para>
    /// <para>
    /// <b>Trajectories are refused rather than watched</b>, and not because they could not
    /// be: an ion flight is seconds where a driven diffusive window is hundreds of thousands
    /// of steps, so there is nothing to watch that is not over before a window could draw
    /// it. A caller who asks is told to use <see cref="Execute"/>, which is the same answer
    /// arriving sooner.
    /// </para>
    /// <para>
    /// <b>Watching does not change the answer.</b> The densities in a report are the
    /// solver's own buffers; they are read to build a frame and nothing goes back, so a run
    /// with a watcher and one without are bit-identical. Asserted rather than assumed, the
    /// same way the checkpoint writer's are.
    /// </para>
    /// </remarks>
    public static ViewportOutcome Watch(
        string modelPath, IViewportProgress watcher, int? phaseLimit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(watcher);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(File.ReadAllText(absolute)), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors);
        }

        var model = validation.Model!;

        if (!model.Modes.Contains("diffusion"))
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.RegimeInvalid,
                Path = "/transport/mode",
                Constraint = "this model computes trajectories rather than a density, and a "
                    + "flight finishes faster than a viewport could draw it part way through",
                Suggestion = "draw it with the ordinary viewport instead - the whole bundle "
                    + "arrives at once, which for a trajectory model is sooner than the first "
                    + "frame of a watch would be",
            });
        }

        var warnings = new List<ValidityWarning>();
        var (field, built) = FieldAssembly.BuildReported(model);

        warnings.AddRange(built);

        warnings.Add(new ValidityWarning(
            "render.no-trajectories",
            $"the '{model.TransportMode}' transport mode computes a density rather than "
            + "trajectories, so there are no paths to draw. What this model has instead is "
            + "a density field, drawn here as nested shells at decades below its peak",
            WarningSeverity.Provenance));

        // ONCE, AND SHARED BY EVERY FRAME. A stage may change what an electrode holds and
        // not where it is, so the meshes are the same at every instant of the run.
        var (conductors, low, high) = Geometry(model, field, warnings);
        var levels = Levels(model, field, low, high);
        var ends = Ends(model, []);

        var relay = new ViewportRelay(
            watcher, absolute, conductors, levels, low, high, ends, warnings);

        IReadOnlyList<ValidityWarning> runWarnings;
        Transport.Diffusion.DensityField? last;
        double elapsedSeconds;

        if (model.NeedsSequencedTransport)
        {
            var walk = SequencedRun.Execute(
                model,
                field,
                Io.GasFlowImport.Resolve(model.Gas, model.SourceDirectory ?? "."),
                relay,
                phaseLimit: phaseLimit);

            runWarnings = walk.Warnings;
            last = walk.FinalDensity;
            elapsedSeconds = walk.Phases.Count > 0 ? walk.Phases[^1].EndsAtSeconds : 0.0;
        }
        else
        {
            var run = DiffusionRun.Execute(model, field, [], progress: relay);

            runWarnings = run.Warnings;
            last = run.Result.Density;
            elapsedSeconds = run.Result.ElapsedSeconds;
        }

        warnings.AddRange(runWarnings);

        IReadOnlyList<DensityShell> shells = [];
        double? peak = null;
        double? at = null;

        if (last is not null)
        {
            (shells, peak, at) = Shells(last, elapsedSeconds, elapsedSeconds, true, warnings);
        }

        return new ViewportOutcome(
            absolute, [], false, null, null,
            conductors, levels, low, high,
            shells, peak, at, ends, warnings);
    }

    /// <summary>Turns each density report into a frame a viewport can draw.</summary>
    /// <remarks>
    /// <para>
    /// <b>Each frame carries its own warning list.</b> A frame is a whole
    /// <see cref="ViewportOutcome"/>, and every one earns the provenance line saying which
    /// instant it is - appending those to one shared list would grow it without bound over a
    /// run of hundreds of thousands of steps, and would put every earlier frame's instant on
    /// the current frame's picture.
    /// </para>
    /// <para>
    /// <b>The phase methods do nothing here.</b> They are on <c>IRunProgress</c> because a
    /// checkpoint file records which of eight phases a run is in; a viewport shows the
    /// packet, and which phase it belongs to is the shell's to display from the sequence it
    /// already has. Implemented rather than thrown from, because a sequenced run calls them.
    /// </para>
    /// </remarks>
    private sealed class ViewportRelay(
        IViewportProgress watcher,
        string modelPath,
        IReadOnlyList<ConductorSurface> conductors,
        IReadOnlyList<Equipotential> levels,
        double? low,
        double? high,
        FlightEnds ends,
        IReadOnlyList<ValidityWarning> standing) : IRunProgress
    {
        /// <summary>Where the phase now running began, on the run's own clock.</summary>
        /// <remarks>
        /// <b>The transport reports its own leg's clock, and it is right to.</b> A leg does
        /// not know it is one of eight - the checkpoint writer prints "phase 2/3, 12.0 of
        /// 40.0 us", which is exactly what a reader wants when the phase is named on the
        /// same line. A picture has no such line: a frame carrying a leg's clock would send
        /// the instant back to zero at every phase boundary, and the provenance stamped on
        /// it would name an instant the packet passed through long before.
        /// </remarks>
        private double _started;

        private int _index;
        private int _count;
        private string _phase = string.Empty;

        public bool Wants(int steps, double timeSeconds) => watcher.Wants(steps, timeSeconds);

        public void Reached(Transport.Diffusion.DensityProgressReport report)
        {
            if (report.Species.Count == 0)
            {
                return;
            }

            var frame = new List<ValidityWarning>(standing);

            if (_count > 0)
            {
                // WHICH PHASE THIS IS, because on a sequenced instrument the instant alone
                // does not say what the instrument is doing at it - and a person watching a
                // hold-then-ramp wants to know which of the two they are looking at.
                frame.Add(new ValidityWarning(
                    "render.phase",
                    $"phase {_index} of {_count}, '{_phase}'",
                    WarningSeverity.Provenance));
            }

            if (report.Species.Count > 1)
            {
                // Drawn as one packet because the viewport draws one density. Said rather
                // than left to be inferred from a picture that looks complete.
                frame.Add(new ValidityWarning(
                    "render.first-species",
                    $"this run carries {report.Species.Count} populations and the frame draws "
                    + $"the first, '{report.Species[0].Name}'. The others are being computed "
                    + "and are not on this picture",
                    WarningSeverity.Provenance));
            }

            var (shells, peak, at) = Shells(
                report.Species[0].Density,
                _started + report.TimeSeconds,
                _started + report.UntilSeconds,
                true,
                frame);

            watcher.Reached(new ViewportOutcome(
                modelPath, [], false, null, null,
                conductors, levels, low, high,
                shells, peak, at, ends, frame));
        }

        public void Entering(string phase, int index, int count, double ofSeconds)
        {
            _phase = phase ?? string.Empty;
            _index = index;
            _count = count;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The offset comes from what the phase <em>did</em> rather than from what it
        /// declared: a phase can end early - everything collected, or a stop condition met -
        /// and accumulating declared durations would then run the clock ahead of the packet
        /// for the whole rest of the sequence.
        /// </remarks>
        public void Completed(PhaseOutcome phase)
        {
            ArgumentNullException.ThrowIfNull(phase);

            _started = phase.EndsAtSeconds;
        }
    }

    /// <summary>Where the flight begins and ends, with a size to draw them at.</summary>
    /// <remarks>
    /// <b>The span is a fraction of what is drawn, not a fixed length.</b> A marker sized in
    /// millimetres is invisible on a 600 mm analyser and swallows a 20 mm lens; taken from
    /// the extent, one number works for both. Measured over the trajectories where there are
    /// any and the geometry otherwise, which is the same extent the camera frames.
    /// </remarks>
    private static FlightEnds Ends(CompiledModel model, IReadOnlyList<TrajectoryPath> paths)
    {
        var low = new[] { double.MaxValue, double.MaxValue, double.MaxValue };
        var high = new[] { double.MinValue, double.MinValue, double.MinValue };

        void Cover(double x, double y, double z)
        {
            Span<double> at = [x, y, z];

            for (var i = 0; i < 3; i++)
            {
                low[i] = Math.Min(low[i], at[i]);
                high[i] = Math.Max(high[i], at[i]);
            }
        }

        foreach (var point in paths.SelectMany(p => p.PointsMm))
        {
            Cover(point[0], point[1], point[2]);
        }

        Cover(model.SourcePosition.X * 1e3, model.SourcePosition.Y * 1e3, model.SourcePosition.Z * 1e3);
        Cover(model.DetectorPoint.X * 1e3, model.DetectorPoint.Y * 1e3, model.DetectorPoint.Z * 1e3);

        var extent = 0.0;

        for (var i = 0; i < 3; i++)
        {
            extent = Math.Max(extent, high[i] - low[i]);
        }

        // A fortieth of the instrument: large enough to find, small enough not to hide the
        // geometry it sits in. Floored so a degenerate extent still draws something.
        var span = Math.Max(extent / 40.0, 0.5);

        return new FlightEnds(
            [model.SourcePosition.X * 1e3, model.SourcePosition.Y * 1e3, model.SourcePosition.Z * 1e3],
            [model.SourceDirection.X, model.SourceDirection.Y, model.SourceDirection.Z],
            [model.DetectorPoint.X * 1e3, model.DetectorPoint.Y * 1e3, model.DetectorPoint.Z * 1e3],
            [model.DetectorNormal.X, model.DetectorNormal.Y, model.DetectorNormal.Z],
            span);
    }

    /// <summary>The density, as nested shells at decades below its peak.</summary>
    /// <remarks>
    /// <para>
    /// <b>The half of TRN-2 the window was missing.</b> RND-8 says never to draw
    /// trajectories through a diffusive region, which the viewport already honoured - and
    /// on its own that is entirely negative. The mode's principal result could be
    /// summarised into a transmission and a transit time and looked at in no other form,
    /// so the honest picture of a funnel at a millibar was an empty box. The 2-D section
    /// has drawn density contours since the mode was wired up; the viewport drew nothing,
    /// while its own warning said the density "is drawn as contours".
    /// </para>
    /// <para>
    /// <b>Decades, and the same rule the section uses</b>, because a density spans orders
    /// of magnitude - a packet's tail is a millionth of its core, not a small fraction of
    /// it - so evenly spaced shells draw the top decade several times over and the extent
    /// not at all.
    /// </para>
    /// <para>
    /// <b>Extruded or revolved by what the solve claims about the third dimension</b>,
    /// which is the rule the conductors already follow: a cross-section says the geometry
    /// repeats along z, an axisymmetric half-plane says it repeats all the way round. A
    /// density computed on a half-plane grid really is a solid of revolution, and drawing
    /// it flat would understate where the ions are by a whole dimension. The flag is taken
    /// from the density the solver produced rather than re-derived from the model, so the
    /// drawing cannot disagree with the thing it draws.
    /// </para>
    /// <para>
    /// <b>Drawn at an instant, and the default is not the end of the run.</b> A diffusive
    /// run reports the density it <em>ended</em> with, which for any model whose packet
    /// actually arrives is empty: the shipped drift tube collects 9,999.76 of 10,000 ions
    /// and leaves 1.8e-302 behind. Drawing that is drawing nothing, for exactly the models
    /// that work. The 2-D section learned this and gained an instant to draw at; this is
    /// the same lesson met a second time, caught by its own test rather than shipped.
    /// </para>
    /// <para>
    /// So the run is asked for snapshots across its flight and the drawing is taken from
    /// the <b>middle of those that hold a drawable density</b> - mid-flight while there is
    /// still a packet to see. The instant is reported rather than implied, because a
    /// density drawn without saying when is a shape rather than a measurement, and the
    /// caller may name its own to scrub through the flight.
    /// </para>
    /// <para>
    /// <b>Recording does not perturb what it records</b> - asserted where the snapshots
    /// were built, not assumed here - so asking for them costs the memory of the clones
    /// and nothing in the answer.
    /// </para>
    /// <para>
    /// <b>This runs the transport</b>, which a camera move must not. It is on the same
    /// explicit redraw the rest of the viewport is, and the cost is the run's - a number
    /// GRD-8 makes available before the work starts for exactly this mode.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<DensityShell> Shells, double? Peak, double? AtUs) Cloud(
        CompiledModel model,
        IElectrostaticField field,
        List<ValidityWarning> warnings,
        double? atSeconds)
    {
        // A SEQUENCED MODEL IS WALKED ALONG ITS OWN TIMELINE, and the window is the
        // sequence's rather than `maximumFlightTime` - which for a timed instrument is a
        // ceiling on a leg and says nothing about how long the instrument runs. Spread
        // over the wrong window every requested instant lands outside the run, and the
        // viewport draws the geometry beside an empty box.
        var sequenced = model.NeedsSequencedTransport;

        // AS FAR AS IT HAS TO GO AND NO FURTHER. A viewport redraw is a thing somebody is
        // waiting on and a TIMS elution is twenty minutes of transport, so a walk that ran
        // the whole timeline would hang the window on the models this most exists for -
        // including when an instant early in the sequence was named, where every phase
        // after it is paid for and thrown away.
        //
        // The default is the FIRST phase, which is the right one rather than merely the
        // cheap one: a timed instrument prepares its packet in its first phase, and a
        // packet parked against the gas at its balance point is the state worth opening on.
        var limit = sequenced
            ? (atSeconds is { } target ? Reaching(model, target) : 1)
            : (int?)null;

        var ceiling = sequenced
            ? model.Phases[limit!.Value - 1].EndsAtSeconds
            : model.MaximumFlightTimeSi;

        var wanted = atSeconds is { } named
            ? (IReadOnlyList<double>)[Math.Clamp(named, 0.0, ceiling)]
            : [.. Enumerable.Range(1, DensityInstants).Select(
                i => ceiling * i / (DensityInstants + 1.0))];

        // What the drawing needs, whichever transport produced it. Locals rather than two
        // copies of everything below: this file's own history is that writing a helper a
        // second time got two of four wrong.
        IReadOnlyList<ValidityWarning> runWarnings;
        IReadOnlyList<Transport.Diffusion.DensitySnapshot> snapshots;
        Transport.Diffusion.DensityField last;
        double elapsedSeconds;

        try
        {
            if (sequenced)
            {
                var walk = SequencedRun.Execute(
                    model,
                    field,
                    Io.GasFlowImport.Resolve(model.Gas, model.SourceDirectory ?? "."),
                    snapshotSeconds: wanted,
                    phaseLimit: limit);

                // THE END STATE IS THE FALLBACK, NOT THE SOURCE. A sequence ending in the
                // trajectory description has no final density - and may still have had one
                // at the instant that was asked for, which is the whole reason an instant
                // can be named. Bailing on a null end discarded exactly the packet the
                // caller wanted to look at, on the one kind of model where naming an
                // instant is the only way to see it.
                if (walk.FinalDensity is null && walk.Snapshots.Count == 0)
                {
                    warnings.Add(new ValidityWarning(
                        "render.no-density",
                        "this sequence holds no density at any instant asked for, and ends "
                        + "in the trajectory description. Name an instant inside a "
                        + "diffusive phase to see the packet while it exists",
                        WarningSeverity.Provenance));

                    return ([], null, null);
                }

                runWarnings = walk.Warnings;
                snapshots = walk.Snapshots;
                last = walk.FinalDensity ?? walk.Snapshots[^1].Density;
                elapsedSeconds = walk.Phases.Count > 0 ? walk.Phases[^1].EndsAtSeconds : 0.0;

                if (walk.Phases.Count < walk.PhasesDeclared)
                {
                    // GRD-12 again: the reader is looking at part of an instrument, and
                    // nothing else on the screen would say so.
                    warnings.Add(new ValidityWarning(
                        "render.sequence-truncated",
                        $"drawn after {walk.Phases.Count} of this model's "
                        + $"{walk.PhasesDeclared} phases - through '{walk.Phases[^1].Name}' "
                        + "and no further. A viewport redraw runs the transport, and a "
                        + "whole timed sequence is minutes to hours of it; name a later "
                        + "instant to walk further, or use 'einzel render section --at-us' "
                        + "for a figure of one",
                        WarningSeverity.Provenance));
                }
            }
            else
            {
                var run = DiffusionRun.Execute(model, field, [], snapshotSeconds: wanted);

                runWarnings = run.Warnings;
                snapshots = run.Result.Snapshots;
                last = run.Result.Density;
                elapsedSeconds = run.Result.ElapsedSeconds;
            }
        }
        catch (Core.Errors.EinzelException failure)
        {
            // A refusal is a thing to say, not a thing to draw nothing about. The
            // viewport is where somebody looks when they cannot tell why a model shows
            // them an empty box, so the reason has to arrive with the emptiness.
            warnings.Add(new ValidityWarning(
                "render.no-density",
                "this model computes a density but the transport refused, so there is "
                + $"nothing to draw: {failure.Error.Constraint}",
                WarningSeverity.Provenance));

            return ([], null, null);
        }

        warnings.AddRange(runWarnings);

        // The middle of the instants that hold something, so the packet is as far along
        // as it can be while still being a packet. The final density is the fallback
        // rather than the default, which is the way round this was wrong first.
        var usable = snapshots
            .Where(snapshot => snapshot.Density.Peak() > Floor(snapshot.Density))
            .ToList();

        var chosen = usable.Count > 0 ? usable[usable.Count / 2] : null;

        return Shells(
            chosen?.Density ?? last,
            chosen?.AtSeconds ?? elapsedSeconds,
            elapsedSeconds,
            atSeconds is not null,
            warnings);
    }

    /// <summary>How faint a density has to be before contouring it draws the round-off.</summary>
    /// <param name="density">The field to judge.</param>
    /// <returns>A density per cubic metre below which there is nothing to draw.</returns>
    /// <remarks>
    /// Not merely positive: a run that collected everything leaves a residue orders below
    /// one ion in the whole domain, and contouring that draws the shape of the round-off.
    /// The same floor the section renderer uses, for the same reason.
    /// </remarks>
    private static double Floor(Transport.Diffusion.DensityField density)
    {
        var grid = density.Grid;
        var area = Math.Max(
            (grid.MaxX - grid.OriginX) * (grid.MaxY - grid.OriginY), 1e-12);

        return 1e-6 * Math.Max(1.0, density.Population()) / area;
    }

    /// <summary>Draws one density as nested shells at decades below its peak.</summary>
    /// <param name="density">The packet to draw.</param>
    /// <param name="atSecondsDrawn">The instant it is, on the run's own clock.</param>
    /// <param name="elapsedSeconds">How far the run goes, for the provenance line.</param>
    /// <param name="asked">Whether the caller named the instant or it was chosen.</param>
    /// <param name="warnings">Where the provenance and the empty cases are recorded.</param>
    /// <returns>The shells, the peak they are measured from, and the instant in microseconds.</returns>
    /// <remarks>
    /// <b>Separated from choosing WHICH density to draw, because a run being watched has
    /// no choice to make</b> - a live report hands over the packet as it stands at that
    /// step, where a finished run has a list of instants to pick the most informative
    /// from. Two callers, one drawing, so a frame of a run in flight cannot come out
    /// looking different from the same instant drawn afterwards.
    /// </remarks>
    private static (IReadOnlyList<DensityShell> Shells, double? Peak, double? AtUs) Shells(
        Transport.Diffusion.DensityField density,
        double atSecondsDrawn,
        double elapsedSeconds,
        bool asked,
        List<ValidityWarning> warnings)
    {
        var grid = density.Grid;

        var spanU = grid.MaxX - grid.OriginX;
        var spanV = grid.MaxY - grid.OriginY;

        var peak = density.Peak();
        var floor = Floor(density);

        if (!(peak > floor))
        {
            warnings.Add(new ValidityWarning(
                "render.no-density",
                "no instant of this run holds a density worth drawing, so there are no "
                + "shells. A packet that has been collected or has left the grid is a "
                + "result rather than a failure - what it means is that the ions arrived",
                WarningSeverity.Provenance));

            return ([], peak, atSecondsDrawn * 1e6);
        }

        // GRD-12: the instant is part of the measurement. Three shells of a packet say
        // nothing at all without it, since a density that has spread for a microsecond
        // and one that has spread for a millisecond are the same three shells at
        // different sizes.
        warnings.Add(new ValidityWarning(
            "render.density-at-instant",
            $"the density is drawn at t = {atSecondsDrawn * 1e6:G6} us of a "
            + $"{elapsedSeconds * 1e6:G6} us run"
            + (asked
                ? ", as asked for"
                : ", chosen as the middle of the instants that still hold a packet. The "
                  + "end of a run is empty whenever the ions arrived"),
            WarningSeverity.Provenance));

        var columns = Math.Max(8, DensityColumns);
        var rows = Math.Max(8, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        // [column, row] - u first, then v - which is the convention Contours.Sample
        // builds and Contours.Trace reads. Transposed, the contour comes out somewhere
        // the density is not, and every normal is exactly zero because the field being
        // differenced is flat there. That is what the unit-normal test caught.
        var values = new double[columns, rows];

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                values[column, row] = density.SampleAt(
                    grid.OriginX + (column * stepU), grid.OriginY + (row * stepV));
            }
        }

        var cylindrical = density.Cylindrical;
        var shells = new List<DensityShell>();

        for (var k = 1; k <= DensityShellCount; k++)
        {
            var level = peak * Math.Pow(10.0, -k);

            if (level <= floor)
            {
                break;
            }

            var traced = Contours.Trace(
                values, grid.OriginX, grid.OriginY, stepU, stepV, level);

            if (traced.Count == 0)
            {
                continue;
            }

            // The run itself rather than its points, so a closed shell keeps its closing
            // segment: Contours does not repeat a closed run's first point.
            var mesh = Join(traced.Select(contour => cylindrical
                ? Surfaces.Revolve(contour, RevolutionFacets)
                : Surfaces.Extrude(contour, grid.OriginY, grid.MaxY)));

            if (mesh.Triangles.Count == 0)
            {
                continue;
            }

            // Outward means away from the denser side, so a shell is lit from the side a
            // viewer is on. Negative inside, which is the convention a conductor's own
            // signed distance already uses, so Orient needs no second rule.
            //
            // The step is the density's own cell, not the metre-scale one a conductor
            // uses. Orient differences the function it is given, and a signed distance
            // changes by the step itself while a density changes by whatever it happens
            // to change by - so a 1 um step across a packet spanning millimetres lands on
            // the rounding floor, the gradient comes back exactly zero, and Orient leaves
            // the normal at zero. That renders as a black facet, which reads as structure
            // in the density. Same shape as reading an analytic field's infinite
            // ResolutionLength as a differencing step.
            var oriented = Surfaces.Orient(
                mesh,
                cylindrical
                    ? (x, y, z) => level - density.SampleAt(x, Math.Sqrt((y * y) + (z * z)))
                    : (x, y, _) => level - density.SampleAt(x, y),
                0.5 * Math.Min(grid.SpacingX, grid.SpacingY));

            shells.Add(new DensityShell(
                level,
                k,
                [.. oriented.Vertices.Select(v => v * 1e3)],
                oriented.Normals,
                oriented.Triangles));
        }

        return (shells, peak, atSecondsDrawn * 1e6);
    }

    /// <summary>The box the drawing covers, in metres.</summary>
    /// <remarks>
    /// The union of every solve domain and the flight itself. The flight matters because
    /// an analytic model declares no domain at all - the scaffolded reflectron's only
    /// declared points are its source and its detector, which in a reflectron are the same
    /// point, and a page chosen from those alone put its turning point 105 metres off.
    /// </remarks>
    private static (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ)?
        Extent(CompiledModel model, IReadOnlyList<TrajectoryPath> paths)
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        var any = false;

        void Cover(double x, double y, double z)
        {
            minX = Math.Min(minX, x);
            minY = Math.Min(minY, y);
            minZ = Math.Min(minZ, z);
            maxX = Math.Max(maxX, x);
            maxY = Math.Max(maxY, y);
            maxZ = Math.Max(maxZ, z);
            any = true;
        }

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } plane)
            {
                // A cylindrical half-plane is a solid of revolution, so the instrument
                // reaches its outer radius in every transverse direction rather than only
                // in the positive half of one.
                var radius = plane.Symmetry == SolveSymmetry.Cylindrical
                    ? Math.Max(Math.Abs(plane.MinY), Math.Abs(plane.MaxY))
                    : 0.0;

                Cover(plane.MinX, Math.Min(plane.MinY, -radius), -radius);
                Cover(plane.MaxX, Math.Max(plane.MaxY, radius), radius);

                // The mirrored half is part of the instrument, so it is part of what is
                // drawn - and the field is sampled over this box, so leaving it out would
                // draw equipotentials through one mirror of a pair.
                if (plane.ReflectAboutX is { } mirror)
                {
                    Cover((2.0 * mirror) - plane.MaxX, Math.Min(plane.MinY, -radius), -radius);
                    Cover((2.0 * mirror) - plane.MinX, Math.Max(plane.MaxY, radius), radius);
                }
            }

            if (element.Solve3D is { } volume)
            {
                Cover(volume.MinX, volume.MinY, volume.MinZ);
                Cover(volume.MaxX, volume.MaxY, volume.MaxZ);

                if (volume.ReflectAboutX is { } mirror)
                {
                    Cover((2.0 * mirror) - volume.MaxX, volume.MinY, volume.MinZ);
                    Cover((2.0 * mirror) - volume.MinX, volume.MaxY, volume.MaxZ);
                }
            }
        }

        // A diffusive run has no paths and its region is the density grid, which is the
        // one thing that says where such a model reaches. Without it a drift tube in a
        // uniform field - no solve domain, no trajectories - has no extent at all, and
        // the viewport would draw the field over nothing.
        if (model.DensityGrid is { } density)
        {
            Cover(density.MinX, density.MinY, 0.0);
            Cover(density.MaxX, density.MaxY, 0.0);
        }

        foreach (var path in paths)
        {
            foreach (var point in path.PointsMm)
            {
                Cover(point[0] * 1e-3, point[1] * 1e-3, point[2] * 1e-3);
            }
        }

        if (!any)
        {
            return null;
        }

        // A degenerate axis is padded rather than left flat. An analytic reflectron flies
        // straight down x, so its transverse extent is exactly zero and the field would be
        // sampled on a grid one row deep - the equipotentials of such a model are vertical
        // lines, which is correct and informative, and there has to be somewhere to draw
        // them.
        var widest = Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ));
        var floor = 0.05 * widest;

        void Widen(ref double low, ref double high)
        {
            if (high - low >= floor)
            {
                return;
            }

            var middle = 0.5 * (low + high);

            low = middle - (0.5 * floor);
            high = middle + (0.5 * floor);
        }

        Widen(ref minX, ref maxX);
        Widen(ref minY, ref maxY);
        Widen(ref minZ, ref maxZ);

        return (minX, minY, minZ, maxX, maxY, maxZ);
    }

    /// <summary>The conductors, and the potential range they and the field cover.</summary>
    /// <remarks>
    /// <para>
    /// <b>Every conductor is the zero level set of its own signed distance</b>, which is
    /// what keeps this free of device knowledge (architecture invariant 2). What differs
    /// between a cross-section, an axisymmetric half-plane and a volume is not the shape
    /// but what the solve claims about the third dimension - repeats along it, repeats
    /// round it, or says nothing - so the same contour becomes a prism, a solid of
    /// revolution, or a genuine extracted surface.
    /// </para>
    /// <para>
    /// The potential range is anchored across every conductor and the sampled field at
    /// once, for the same reason the energy scale is: a colour scale taken per electrode
    /// would paint a rod at +500 V and one at -500 V identically.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<ConductorSurface> Conductors, double? Low, double? High)
        Geometry(
            CompiledModel model,
            IElectrostaticField field,
            List<ValidityWarning> warnings,
            IReadOnlyList<TrajectoryPath>? paths = null)
    {
        var surfaces = new List<ConductorSurface>();
        double? low = null;
        double? high = null;

        // Once, not once per element: a bundle of four thousand ions is a million points
        // to walk, and every translational element would walk all of them again.
        var flight = Along(paths);

        void Cover(double potential)
        {
            low = low is { } l ? Math.Min(l, potential) : potential;
            high = high is { } h ? Math.Max(h, potential) : potential;
        }

        foreach (var element in model.Fields)
        {
            if (element.Solve is { } plane)
            {
                // A prism has to stop somewhere and the model does not say where: a
                // translational solve asserts the geometry is invariant along z, so the
                // conductor genuinely extends past anything drawn.
                //
                // Drawn as far as the ions go, because that is the part of an infinite
                // structure anybody is looking at - the invariant axis is the one the beam
                // travels along, so a quadrupole's rods should run the length of the
                // flight. The transverse span is the fallback for a model with no flight
                // to measure, and it is a poor one: it made the rods 32 mm of a 200 mm
                // instrument and put them in the corner of the picture.
                //
                // Either way it is a drawing convention and not a dimension, which is what
                // the warning says (GRD-12).
                var half = 0.5 * (plane.MaxY - plane.MinY);
                var (nearZ, farZ) = flight ?? (-half, half);

                if (plane.Symmetry == SolveSymmetry.Translational && plane.Electrodes.Count > 0)
                {
                    warnings.Add(new ValidityWarning(
                        "render.extruded-depth",
                        "this is a cross-section, so its electrodes have no declared extent "
                        + $"along z. They are drawn from {nearZ * 1e3:F1} to {farZ * 1e3:F1} mm, "
                        + (flight is null
                            ? "which is the transverse span of the solve domain because "
                                + "nothing was flown to measure"
                            : "which is as far as the ions reach")
                        + " - a drawing convention rather than a dimension of the instrument",
                        WarningSeverity.Provenance));
                }

                var drawn = new List<Drawn>();

                foreach (var electrode in plane.Electrodes)
                {
                    // A printed board has no interior, so the level-set extraction every
                    // other shape goes through finds nothing - which is how the planar
                    // mirror pair was drawn as its end cap and nothing else. It is drawn
                    // from its declaration instead: a sheet lying on the domain edge.
                    if (electrode.Shape == ElectrodeShape.EdgeProfile)
                    {
                        foreach (var (mesh, potential) in Board(electrode, plane, nearZ, farZ))
                        {
                            Cover(potential);
                            Cover(potential + electrode.DriveAmplitude);
                            Cover(potential - electrode.DriveAmplitude);

                            drawn.Add(new Drawn(electrode.Name, potential, electrode.DriveAmplitude, mesh));
                        }

                        continue;
                    }

                    Cover(electrode.Potential);
                    Cover(electrode.Potential + electrode.DriveAmplitude);
                    Cover(electrode.Potential - electrode.DriveAmplitude);

                    var solid = plane.Symmetry == SolveSymmetry.Cylindrical
                        ? Revolved(electrode, plane)
                        : Extruded(electrode, plane, nearZ, farZ);

                    if (solid.TriangleCount > 0)
                    {
                        drawn.Add(new Drawn(electrode.Name, electrode.Potential, electrode.DriveAmplitude, solid));
                    }
                }

                AddWithReflection(drawn, plane.ReflectAboutX, surfaces);
            }

            if (element.Solve3D is { } volume)
            {
                var drawn = new List<Drawn>();

                foreach (var electrode in volume.Electrodes)
                {
                    Cover(electrode.Potential);
                    Cover(electrode.Potential + electrode.DriveAmplitude);
                    Cover(electrode.Potential - electrode.DriveAmplitude);

                    // The electrode's own box, not the solve domain. Extracting over the
                    // domain misses anything thinner than a cell - a 1 mm plate in a
                    // 60 mm box at 48 cells falls between lattice planes and produces no
                    // surface at all, silently. Padded so the surface is strictly inside
                    // and the extraction is not clipped by its own box.
                    var around = electrode.Bounds;

                    // Padded per axis by a fraction of THAT axis, not of the largest.
                    // A stripe 4 mm thick and 635 mm long was being padded by 8 per cent
                    // of 635 - inflating its thinnest direction twenty-six-fold, so the
                    // metal occupied a fortieth of the box being sampled and disappeared.
                    // The padding exists to keep the surface strictly inside the box, and
                    // for that each axis only needs a fraction of itself.
                    var padX = Pad(around.MaxX - around.MinX);
                    var padY = Pad(around.MaxY - around.MinY);
                    var padZ = Pad(around.MaxZ - around.MinZ);

                    var mesh = Surfaces.FromSignedDistance(
                        electrode.SignedDistance,
                        around.MinX - padX, around.MinY - padY, around.MinZ - padZ,
                        around.MaxX + padX, around.MaxY + padY, around.MaxZ + padZ,
                        VolumeCells);

                    if (mesh.TriangleCount > 0)
                    {
                        drawn.Add(new Drawn(electrode.Name, electrode.Potential, electrode.DriveAmplitude, mesh));
                    }
                }

                AddWithReflection(drawn, volume.ReflectAboutX, surfaces);
            }
        }

        // An analytic field has no electrodes and still has a potential, so the range has
        // to come from the field as well - otherwise a reflectron's equipotentials would
        // have no scale to sit on.
        if (Extent(model, paths ?? []) is { } box)
        {
            foreach (var value in Plane(field, box, PlaneColumns))
            {
                if (double.IsFinite(value))
                {
                    Cover(value);
                }
            }
        }

        return (surfaces, low, high);
    }

    /// <summary>Where the ions reach along the invariant axis, in metres.</summary>
    /// <remarks>
    /// <para>
    /// The range they actually occupy, not a half-span about the origin: a flight running
    /// from zero to 400 mm would otherwise be drawn as a rod from -420 to +420, with half
    /// its length somewhere no ion goes. Nothing says an instrument is centred on the
    /// origin, and several are not.
    /// </para>
    /// <para>
    /// Padded past the outermost ion by a twentieth of the span, so a rod ends beyond the
    /// beam rather than flush with it. Absent when nothing was flown.
    /// </para>
    /// </remarks>
    private static (double Min, double Max)? Along(IReadOnlyList<TrajectoryPath>? paths)
    {
        if (paths is null || paths.Count == 0)
        {
            return null;
        }

        var low = double.MaxValue;
        var high = double.MinValue;

        foreach (var point in paths.SelectMany(p => p.PointsMm))
        {
            low = Math.Min(low, point[2] * 1e-3);
            high = Math.Max(high, point[2] * 1e-3);
        }

        var pad = 0.05 * (high - low);

        return high - low > 0.0 ? (low - pad, high + pad) : null;
    }

    /// <summary>One conductor surface on its way out, before the reflection is taken.</summary>
    private sealed record Drawn(string Name, double Potential, double Amplitude, SurfaceMesh Mesh);

    /// <summary>Adds an element's surfaces, and their mirror image if the solve declares one.</summary>
    /// <remarks>
    /// <para>
    /// <b>A reflected solve is the instrument and its mirror image, and the field already
    /// says so.</b> <c>GeometryBuilder</c> composes the half with <c>ReflectedField</c>
    /// about the declared plane, so the ion flies through both mirrors of a mirror pair -
    /// and the viewport drew only the one that was declared, a mirror pair with one mirror
    /// in it. The two templates that reflect are the two whose whole subject is the pair.
    /// </para>
    /// <para>
    /// Mirrored exactly as the field is, <c>x -> 2p - x</c>, so the drawing and the solve
    /// cannot disagree about where the second half sits. A reflection reverses orientation,
    /// so each triangle's winding is reversed with it and the normal's x component flips -
    /// otherwise the image is lit from inside.
    /// </para>
    /// </remarks>
    private static void AddWithReflection(
        List<Drawn> drawn, double? reflectAboutX, List<ConductorSurface> surfaces)
    {
        foreach (var d in drawn)
        {
            surfaces.Add(Surface(d.Name, d.Potential, d.Amplitude, d.Mesh));
        }

        if (reflectAboutX is not { } plane)
        {
            return;
        }

        foreach (var d in drawn)
        {
            surfaces.Add(Surface(d.Name, d.Potential, d.Amplitude, Mirrored(d.Mesh, plane)));
        }
    }

    /// <summary>A mesh reflected about the plane <c>x = plane</c>.</summary>
    /// <param name="mesh">The mesh, in metres.</param>
    /// <param name="plane">Where the mirror plane crosses the x axis, in metres.</param>
    /// <returns>The image, with its winding and normals reversed to match.</returns>
    internal static SurfaceMesh Mirrored(SurfaceMesh mesh, double plane)
    {
        var vertices = new double[mesh.Vertices.Count];
        var normals = new double[mesh.Normals.Count];

        for (var i = 0; i + 2 < vertices.Length; i += 3)
        {
            vertices[i] = (2.0 * plane) - mesh.Vertices[i];
            vertices[i + 1] = mesh.Vertices[i + 1];
            vertices[i + 2] = mesh.Vertices[i + 2];
        }

        for (var i = 0; i + 2 < normals.Length; i += 3)
        {
            normals[i] = -mesh.Normals[i];
            normals[i + 1] = mesh.Normals[i + 1];
            normals[i + 2] = mesh.Normals[i + 2];
        }

        var triangles = new int[mesh.Triangles.Count];

        for (var t = 0; t + 2 < triangles.Length; t += 3)
        {
            triangles[t] = mesh.Triangles[t];
            triangles[t + 1] = mesh.Triangles[t + 2];
            triangles[t + 2] = mesh.Triangles[t + 1];
        }

        return new SurfaceMesh(vertices, normals, triangles);
    }

    /// <summary>
    /// A printed board, drawn as the sheet on the domain edge it is, in pieces colored by
    /// the potential along it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Over the whole edge, because that is what the solve holds.</b>
    /// <c>RasteriseEdgeProfile</c> fixes every node of the edge from
    /// <see cref="CompiledElectrode.ProfileAt"/>, which continues the end values past the
    /// first and last profile points - so a board drawn only between its knots would be
    /// shorter than the boundary condition actually applied.
    /// </para>
    /// <para>
    /// <b>In pieces, because one surface carries one potential.</b> A board is a resistive
    /// divider whose potential varies continuously, and the surface record a window draws
    /// holds a single value. Broken at every profile knot - where the slope changes - and
    /// subdivided between them, each piece taking the profile at its midpoint, so a
    /// mirror's ramp reads as a ramp rather than as one flat color at its mean.
    /// </para>
    /// <para>
    /// <b>A sheet with no thickness, because the model declares none.</b> Drawing a slab
    /// would invent a dimension; the edge is where the metal begins and the model says
    /// nothing about where it ends. Its normal faces into the domain, which is the side a
    /// reader is looking at it from.
    /// </para>
    /// </remarks>
    private static IEnumerable<(SurfaceMesh Mesh, double Potential)> Board(
        CompiledElectrode electrode, CompiledSolvedField plane, double nearZ, double farZ)
    {
        var alongX = electrode.Edge is GridEdge.Top or GridEdge.Bottom;
        var (start, end) = alongX ? (plane.MinX, plane.MaxX) : (plane.MinY, plane.MaxY);

        var fixedAt = electrode.Edge switch
        {
            GridEdge.Top => plane.MaxY,
            GridEdge.Bottom => plane.MinY,
            GridEdge.Left => plane.MinX,
            GridEdge.Right => plane.MaxX,
            _ => throw new InvalidOperationException($"unhandled edge {electrode.Edge}"),
        };

        // Zero on the sheet, positive on the domain's side of it, so Orient points each
        // normal into the domain.
        Func<double, double, double> distance = electrode.Edge switch
        {
            GridEdge.Top => (_, y) => plane.MaxY - y,
            GridEdge.Bottom => (_, y) => y - plane.MinY,
            GridEdge.Left => (x, _) => x - plane.MinX,
            _ => (x, _) => plane.MaxX - x,
        };

        var breaks = new List<double> { start, end };
        breaks.AddRange(electrode.Profile.Select(k => k.At).Where(at => at > start && at < end));
        breaks.Sort();

        var longest = (end - start) / BoardPieces;
        var cylindrical = plane.Symmetry == SolveSymmetry.Cylindrical;

        for (var k = 0; k + 1 < breaks.Count; k++)
        {
            var span = breaks[k + 1] - breaks[k];

            if (span <= 0.0)
            {
                continue;
            }

            var pieces = Math.Max(1, (int)Math.Ceiling(span / longest));

            for (var n = 0; n < pieces; n++)
            {
                var a = breaks[k] + (span * n / pieces);
                var b = breaks[k] + (span * (n + 1) / pieces);

                (double, double)[] line = alongX
                    ? [(a, fixedAt), (b, fixedAt)]
                    : [(fixedAt, a), (fixedAt, b)];

                var raw = cylindrical
                    ? Surfaces.Revolve(line, RevolutionFacets)
                    : Surfaces.Extrude(line, nearZ, farZ);

                // A board on the axis of an axisymmetric solve revolves into a line and
                // has no area; Revolve drops the degenerate triangles, and so does this.
                if (raw.TriangleCount == 0)
                {
                    continue;
                }

                var mesh = cylindrical
                    ? Surfaces.Orient(raw, (x, y, z) => distance(x, Math.Sqrt((y * y) + (z * z))), OrientStepMetres)
                    : Surfaces.Orient(raw, (x, y, _) => distance(x, y), OrientStepMetres);

                yield return (mesh, electrode.ProfileAt(0.5 * (a + b)));
            }
        }
    }

    /// <summary>A cross-section's conductor, drawn as the prism the solve says it is.</summary>
    /// <remarks>
    /// Oriented by the same distance it was traced from: for a conductor ended against the
    /// domain edge only that distance points outward through the face on the edge, while the
    /// electrode's own may point back into the metal there.
    /// </remarks>
    private static SurfaceMesh Extruded(
        CompiledElectrode electrode, CompiledSolvedField plane, double nearZ, double farZ)
    {
        var (runs, distance) = Trace(electrode, plane);

        var mesh = Join(runs.Select(run => Surfaces.Extrude(run, nearZ, farZ)));

        return Surfaces.Orient(mesh, (x, y, _) => distance(x, y), OrientStepMetres);
    }

    /// <summary>An axisymmetric conductor, drawn as the solid of revolution it is.</summary>
    private static SurfaceMesh Revolved(CompiledElectrode electrode, CompiledSolvedField plane)
    {
        var (runs, distance) = Trace(electrode, plane);

        var mesh = Join(runs.Select(run => Surfaces.Revolve(run, RevolutionFacets)));

        return Surfaces.Orient(
            mesh,
            (x, y, z) => distance(x, Math.Sqrt((y * y) + (z * z))),
            OrientStepMetres);
    }

    /// <summary>
    /// The zero contour of one electrode in the section plane, as its solve holds it, and the
    /// distance it was traced from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Ended against the domain edge, not left open at it.</b> Traced over the domain alone,
    /// a face lying on a grounded edge has no sample outside it to cross to, so the einzel
    /// lens's tubes - which reach the domain's outer radius and both its ends - were drawn as
    /// their bores with an annulus at each gap. <see cref="HeldConductor"/> clips the electrode
    /// to the domain at every edge that is not a mirror, the same intersection the solve's
    /// mask is.
    /// </para>
    /// <para>
    /// <b>An axisymmetric domain reaching below the axis needs no guard here</b>, which was
    /// worth finding out rather than assuming. Revolving a profile found at a negative
    /// radius would draw the same surface a second time, coincident with the first - but
    /// <c>ModelValidator</c> refuses such a document outright, naming the path and saying
    /// that y is the radius. Clamping here as well would be a second, weaker copy of a rule
    /// that already holds for every consumer.
    /// </para>
    /// </remarks>
    private static (IReadOnlyList<Contours.Run> Runs, Func<double, double, double> Distance) Trace(
        CompiledElectrode electrode, CompiledSolvedField plane)
    {
        var minV = plane.MinY;

        var spanU = plane.MaxX - plane.MinX;
        var spanV = plane.MaxY - minV;

        var columns = SectionColumns;
        var rows = Math.Max(16, (int)Math.Round(columns * spanV / spanU));

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        double[,] Sample(Func<double, double, double> distance)
        {
            var values = new double[columns, rows];

            for (var j = 0; j < rows; j++)
            {
                for (var i = 0; i < columns; i++)
                {
                    values[i, j] = distance(plane.MinX + (i * stepU), minV + (j * stepV));
                }
            }

            return values;
        }

        var (sampled, traced) = HeldConductor.Sample(
            plane, electrode, Math.Min(stepU, stepV), Sample);

        return (Contours.Trace(sampled, plane.MinX, minV, stepU, stepV, 0.0), traced);
    }

    /// <summary>Equipotentials on the section plane, at evenly spaced levels.</summary>
    /// <remarks>
    /// <para>
    /// <b>Lines on a plane rather than surfaces in space</b>, which is the half of section
    /// 16's "equipotential surfaces or slices" a reader can see through. A nest of closed
    /// surfaces hides everything inside the outermost one, including the trajectories the
    /// viewport exists to show.
    /// </para>
    /// <para>
    /// The levels are fixed from the same anchored range the conductors are coloured on,
    /// so a contour and an electrode at the same potential are the same colour.
    /// </para>
    /// </remarks>
    private static List<Equipotential> Levels(
        CompiledModel model,
        IElectrostaticField field,
        double? low,
        double? high,
        IReadOnlyList<TrajectoryPath>? paths = null)
    {
        if (low is not { } floor || high is not { } ceiling || ceiling - floor <= 0.0
            || Extent(model, paths ?? []) is not { } box)
        {
            return [];
        }

        var values = Plane(field, box, PlaneColumns);

        var columns = values.GetLength(0);
        var rows = values.GetLength(1);

        var stepU = (box.MaxX - box.MinX) / (columns - 1);
        var stepV = (box.MaxY - box.MinY) / (rows - 1);

        var levels = new List<Equipotential>();

        for (var n = 1; n <= EquipotentialCount; n++)
        {
            var level = floor + ((ceiling - floor) * n / (EquipotentialCount + 1.0));

            var traced = new List<IReadOnlyList<double>>();

            foreach (var run in Contours.Trace(values, box.MinX, box.MinY, stepU, stepV, level))
            {
                var flat = new List<double>(3 * (run.Points.Count + 1));

                foreach (var (u, v) in run.Points)
                {
                    flat.Add(u * 1e3);
                    flat.Add(v * 1e3);
                    flat.Add(0.0);
                }

                if (run.Closed && run.Points.Count > 0)
                {
                    flat.Add(run.Points[0].U * 1e3);
                    flat.Add(run.Points[0].V * 1e3);
                    flat.Add(0.0);
                }

                traced.Add(flat);
            }

            if (traced.Count > 0)
            {
                levels.Add(new Equipotential(level, traced));
            }
        }

        return levels;
    }

    /// <summary>The potential sampled over the section plane, z = 0.</summary>
    /// <remarks>
    /// <b>Asked at an instant explicitly.</b> A driven field implements the time-free
    /// interface too and answers at t = 0 through it without failing - which is how a
    /// section, a solve report, a summed field and a diffusive run have each ended up
    /// describing an arbitrary moment of an RF cycle. Zero is still the instant drawn; what
    /// differs is that it is chosen rather than fallen into, and said so on the figure.
    /// </remarks>
    private static double[,] Plane(
        IElectrostaticField field,
        (double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ) box,
        int columns)
    {
        var spanU = Math.Max(box.MaxX - box.MinX, 1e-9);
        var spanV = Math.Max(box.MaxY - box.MinY, 1e-9);

        var rows = Math.Clamp((int)Math.Round(columns * spanV / spanU), 8, columns);

        var stepU = spanU / (columns - 1);
        var stepV = spanV / (rows - 1);

        var values = new double[columns, rows];

        for (var j = 0; j < rows; j++)
        {
            for (var i = 0; i < columns; i++)
            {
                var point = new Vec3(box.MinX + (i * stepU), box.MinY + (j * stepV), 0.0);

                values[i, j] = field is ITimeVaryingField driven
                    ? driven.PotentialAt(in point, 0.0)
                    : field.PotentialAt(in point);
            }
        }

        return values;
    }

    /// <summary>Concatenates meshes into one, offsetting each one's indices.</summary>
    private static SurfaceMesh Join(IEnumerable<SurfaceMesh> parts)
    {
        var vertices = new List<double>();
        var triangles = new List<int>();

        foreach (var part in parts)
        {
            var offset = vertices.Count / 3;

            vertices.AddRange(part.Vertices);
            triangles.AddRange(part.Triangles.Select(i => i + offset));
        }

        return new SurfaceMesh(vertices, new double[vertices.Count], triangles);
    }

    /// <summary>A mesh in metres, as a conductor in millimetres.</summary>
    private static ConductorSurface Surface(
        string name, double potential, double amplitude, SurfaceMesh mesh) =>
        new(name,
            potential,
            amplitude,
            [.. mesh.Vertices.Select(v => v * 1e3)],
            mesh.Normals,
            mesh.Triangles);

    /// <summary>Flies one ion and returns its path, sampled for display.</summary>
    private static TrajectoryPath? Fly(
        PhaseState launch,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction detector,
        double scoutInterval,
        int samples)
    {
        // Scouted at the model's own cadence to learn how long the flight is. Drawing at
        // that cadence is what gave an einzel lens a three-segment curve through a
        // focusing element - it is chosen for VTU, not for a picture.
        var scout = new TrajectoryRecorder(scoutInterval);

        var result = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector, scout);

        if (scout.Samples.Count < 2)
        {
            return null;
        }

        var flight = scout.Samples[^1].TimeSeconds - scout.Samples[0].TimeSeconds;

        var recorded = flight > 0.0
            ? Resample(launch, species, field, settings, detector, flight / samples, samples)
            : scout.Samples;

        var points = new List<IReadOnlyList<double>>(recorded.Count);
        var energies = new List<double>(recorded.Count);

        foreach (var sample in recorded)
        {
            points.Add([
                sample.Position.X * 1e3,
                sample.Position.Y * 1e3,
                sample.Position.Z * 1e3,
            ]);

            energies.Add(
                0.5 * species.MassSi * sample.Velocity.LengthSquared / ElementaryCharge);
        }

        return new TrajectoryPath(points, energies, Fate(result));
    }

    private static IReadOnlyList<TrajectorySample> Resample(
        PhaseState launch,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction detector,
        double interval,
        int samples)
    {
        var recorder = new TrajectoryRecorder(interval, capacity: 4 * samples);

        TrajectoryIntegrator.Integrate(launch, species, field, settings, detector, recorder);

        return recorder.Samples;
    }

    /// <summary>How an ion ended, by the name the model author wrote where there is one.</summary>
    /// <remarks>
    /// §16 asks for bundles coloured by fate, and "struck rodYPlus" is a thing to move
    /// while "lost" is not - which is ACC-5's argument applied to a picture.
    /// </remarks>
    private static string Fate(TrajectoryResult result) => result.Outcome switch
    {
        TrajectoryOutcome.StopConditionMet => "arrived",
        TrajectoryOutcome.StruckElectrode => result.StruckSurface ?? "an electrode",
        _ => result.Outcome.ToString(),
    };

    /// <summary>The elementary charge, in coulombs.</summary>
    private const double ElementaryCharge = 1.602176634e-19;

    /// <summary>Cells along the longest axis when extracting a volume conductor.</summary>
    private const int VolumeCells = 48;

    /// <summary>How far to pad one axis of an electrode's box, in metres.</summary>
    /// <remarks>
    /// A fraction of the axis's own span, with a floor so a degenerate axis still gets a
    /// box with something in it: a perfectly flat electrode has zero extent one way, and
    /// zero padding there would ask for a sample box of no volume, which the extractor
    /// refuses rather than meshes.
    /// </remarks>
    private static double Pad(double span) => Math.Max(0.08 * span, 1e-6);

    /// <summary>Columns when contouring a conductor in the section plane.</summary>
    /// <remarks>
    /// Finer than the field sampling, because a conductor edge is a hard line and the eye
    /// reads a polygonal circle immediately where it forgives a polygonal equipotential.
    /// </remarks>
    private const int SectionColumns = 240;

    /// <summary>Columns when sampling the potential over the section plane.</summary>
    private const int PlaneColumns = 160;

    /// <summary>Facets around a solid of revolution.</summary>
    private const int RevolutionFacets = 48;

    /// <summary>
    /// How many pieces a printed board's edge is cut into at most, before its knots are
    /// added. Enough that a mirror's potential ramp reads as a ramp.
    /// </summary>
    private const int BoardPieces = 96;

    /// <summary>Columns the density is resampled onto before contouring.</summary>
    /// <remarks>
    /// The density solver's own grid is usually coarser than this and is what the values
    /// come from; resampling is about the smoothness of the extracted shell, not about
    /// inventing resolution the run does not have.
    /// </remarks>
    private const int DensityColumns = 160;

    /// <summary>How many decades below the peak to draw.</summary>
    /// <remarks>
    /// Three shells reach a thousandth of the peak, which is the useful extent of a
    /// packet; a fourth is mostly the numerical tail and, drawn as a solid, hides the
    /// three inside it.
    /// </remarks>
    private const int DensityShellCount = 3;

    /// <summary>How many instants of a diffusive run to look at.</summary>
    /// <remarks>
    /// Enough that the middle of the usable ones is somewhere near mid-flight whatever
    /// fraction of the run the packet survives, and few enough that the clones they cost
    /// are not worth counting.
    /// </remarks>
    private const int DensityInstants = 7;

    /// <summary>Equipotential levels drawn between the lowest and highest potential.</summary>
    private const int EquipotentialCount = 12;

    /// <summary>Differencing step for the outward normal, in metres.</summary>
    /// <remarks>
    /// A micron: small against any electrode this platform models and large against the
    /// rounding in a signed distance built from a chain of arithmetic.
    /// </remarks>
    private const double OrientStepMetres = 1e-6;

    /// <summary>How many phases must run for an instant to have happened.</summary>
    /// <param name="model">The model, whose phases carry the timeline.</param>
    /// <param name="atSeconds">The instant, on the instrument's own clock.</param>
    /// <returns>A phase count, at least one and at most the whole sequence.</returns>
    /// <remarks>
    /// Counted rather than clamped to the end, because the phases after the one holding the
    /// instant are work whose result is discarded - and on a timed instrument each of them
    /// is minutes.
    /// </remarks>
    private static int Reaching(CompiledModel model, double atSeconds)
    {
        for (var i = 0; i < model.Phases.Count; i++)
        {
            if (atSeconds <= model.Phases[i].EndsAtSeconds)
            {
                return i + 1;
            }
        }

        return model.Phases.Count;
    }

}
