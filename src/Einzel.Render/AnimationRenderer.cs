using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Render;

/// <summary>
/// Draws a flight as a sequence of vector frames, on a declared time mapping.
/// </summary>
/// <remarks>
/// <para>
/// <b>Fly once, draw many.</b> The single-figure path solves the field and flies the
/// ion inside the renderer, which is right for one figure and wrong for three hundred:
/// a multigrid solve per frame is unaffordable, and two frames that flew separately
/// are two frames that can disagree about the flight. Here the field is built once and
/// the trajectory flown once, and each frame is that trajectory truncated at its own
/// instant.
/// </para>
/// <para>
/// <b>Vector frames, and no video.</b> Nothing in this engine rasterises, and LIC-1
/// forbids a GPL dependency in the default build - ffmpeg is exactly the thing that
/// would be reached for. So what this produces is a numbered sequence of scenes the
/// existing writers turn into SVG or PDF, and assembling them into a video is an
/// out-of-process step with a tool the user supplies. Its absence degrades a feature
/// rather than blocking the platform, which is the rule.
/// </para>
/// <para>
/// <b>Trajectories only.</b> RND-8 forbids drawing lines through a diffusive region,
/// and the diffusive mode has no mid-run density to draw instead - a run reports the
/// density it ended with. An animation of a diffusive model is refused rather than
/// drawn as a static box repeated, which would be a film of nothing that looks like a
/// film of something.
/// </para>
/// </remarks>
public static class AnimationRenderer
{
    /// <summary>How finely the one flight is sampled, per frame of the animation.</summary>
    /// <remarks>
    /// Several samples per frame, so that truncating at a frame's instant lands close
    /// to it rather than at whatever the recorder happened to keep. The decimation that
    /// follows removes whatever this over-samples, under the same ACC-7 bound a static
    /// figure respects, so the cost of being generous here is bounded.
    /// </remarks>
    public const int SamplesPerFrame = 8;

    /// <summary>One frame: the drawing, and where in both times it sits.</summary>
    /// <param name="Frame">The instant and rate this frame shows.</param>
    /// <param name="Figure">The drawing.</param>
    public sealed record RenderedFrame(AnimationFrame Frame, SectionRenderer.Figure Figure);

    /// <summary>Renders every frame a declared mapping calls for.</summary>
    /// <param name="model">The validated model.</param>
    /// <param name="spec">What to draw.</param>
    /// <param name="animation">The declared time mapping.</param>
    /// <param name="provenance">Lines recorded in each frame's output.</param>
    /// <returns>The frames, in order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// The mapping is not declared, or this model's transport mode produces no
    /// trajectories.
    /// </exception>
    /// <param name="densities">
    /// One density per frame, for a diffusive model, or null for a trajectory one.
    /// </param>
    /// <param name="gas">
    /// The gas the animated ion flies through, already resolved. Omitted, it is built
    /// from the model - which <b>refuses</b> a declared but unresolved imported field
    /// rather than falling back to a vacuum.
    /// </param>
    public static IReadOnlyList<RenderedFrame> Render(
        CompiledModel model,
        RenderSpec spec,
        AnimationSpec animation,
        IReadOnlyList<string>? provenance = null,
        IReadOnlyList<Transport.Diffusion.DensityField>? densities = null,
        Transport.Collisions.BackgroundGas? gas = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(animation);

        if (densities is null)
        {
            RefuseDiffusive(model);
        }

        // The same argument as the diffusive refusal, one step further in: with the
        // trajectory switched off every frame is the same drawing, and a sequence of
        // identical frames is a film of nothing that looks like a film of something.
        //
        // Not asked of a diffusive animation, which has no trajectory by definition -
        // what moves between its frames is the density.
        if (densities is null && !spec.Trajectory)
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.SchemaInvalid,
                Path = "/trajectory",
                Constraint = "this spec draws no trajectory, and the trajectory is the only "
                    + "thing in a frame that changes between frames",
                Suggestion = "leave \"trajectory\" true for an animation, or draw a single "
                    + "figure with 'einzel render section' - the geometry and the field are the "
                    + "same on every frame, so with the ion left out the sequence would be one "
                    + "drawing repeated",
            });
        }

        var frames = TimeMapping.Frames(animation);

        if (densities is not null && densities.Count != frames.Count)
        {
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.SchemaInvalid,
                Path = "/animation",
                Constraint = $"{densities.Count} densities were supplied for "
                    + $"{frames.Count} frames",
                Suggestion = "a diffusive animation records one density per frame, at the "
                    + "frames' own instants. A run that ended before the mapping did records "
                    + "fewer, and the frames after it would otherwise repeat the last one - "
                    + "which would show a packet sitting still rather than a run that finished",
            });
        }

        var (field, fieldWarnings) = FieldAssembly.BuildReported(model);
        var samples = densities is null ? Fly(model, field, frames.Count, gas) : [];

        // Anchored once across the animation. The decades are measured from the peak,
        // and a diffusing packet's peak falls as it spreads - so levels taken per frame
        // would fall with it and a film of a packet spreading would show a packet doing
        // nothing.
        var peak = densities is null
            ? (double?)null
            : densities.Max(d => d.Peak());

        // The declared mapping and the flight need not agree, and a reader cannot tell
        // which they are looking at. Frames past the arrival show a finished flight and
        // an ion that has stopped moving, which reads as the instrument doing nothing
        // rather than as the animation having outlived it; frames that stop short leave
        // the ion in mid-air, which reads as a loss. Both are legitimate things to ask
        // for and neither should be silent.
        var flight = samples.Count >= 2
            ? samples[^1].TimeSeconds - samples[0].TimeSeconds
            : 0.0;

        var declared = frames.Count > 0 ? frames[^1].SimulatedSeconds : 0.0;

        var coverage = new List<ValidityWarning>();

        if (flight > 0.0 && declared > flight * 1.001)
        {
            coverage.Add(new ValidityWarning(
                "animation.past-arrival",
                $"the declared mapping runs to {declared * 1e6:G4} us and the flight ends at "
                + $"{flight * 1e6:G4} us, so the last "
                + $"{frames.Count(f => f.SimulatedSeconds > flight)} frames show a flight that "
                + "has already finished. The ion is not stationary there; the animation has "
                + "outlived it",
                WarningSeverity.Provenance));
        }

        if (flight > 0.0 && declared < flight * 0.999)
        {
            coverage.Add(new ValidityWarning(
                "animation.stops-short",
                $"the declared mapping runs to {declared * 1e6:G4} us and the flight ends at "
                + $"{flight * 1e6:G4} us, so the last frame shows the ion in mid-flight. That is "
                + "a choice rather than a loss, and it does not look like one",
                WarningSeverity.Provenance));
        }

        // Fixed once over the whole animation, because a driven field's range changes
        // through the cycle and levels taken per frame would make the contours flicker -
        // which reads as a noisy field rather than as moving levels. A couple of dozen
        // instants is enough for a range: the extremes of a Laplace solution are on its
        // boundaries.
        var wanted = Math.Min(frames.Count, 24);
        var probes = new List<double>(wanted);

        for (var k = 0; k < wanted; k++)
        {
            var index = wanted == 1 ? 0 : k * (frames.Count - 1) / (wanted - 1);

            probes.Add(frames[index].SimulatedSeconds);
        }

        var range = SectionRenderer.PotentialRange(model, spec, field, probes);

        var rendered = new List<RenderedFrame>(frames.Count);

        foreach (var frame in frames)
        {
            // The whole flight, every frame, with the instant beside it. Handing over
            // the part flown so far instead made each frame choose its page from that
            // part - so the scale changed between frames and the ion sat pinned to the
            // edge of a box that grew to meet it, which reads as a camera following the
            // ion rather than as an instrument being flown through.
            var plan = new SectionRenderer.FramePlan
            {
                Field = field,
                FieldWarnings = fieldWarnings,
                Trajectory = densities is null ? samples : null,
                AtSeconds = frame.SimulatedSeconds,
                PotentialRange = range,
                DensityPeak = peak,
                Banner = Banner(frame),
            };

            var figure = SectionRenderer.Render(
                model,
                spec,
                provenance,

                // One density per frame, wrapped: the renderer takes a list because a model
                // may declare several ion populations, and an animation of a mixture is a
                // separate question from drawing one.
                densities is null ? null : [densities[frame.Index]],
                plan);

            rendered.Add(new RenderedFrame(
                frame,
                coverage.Count == 0
                    ? figure
                    : figure with { Warnings = [.. figure.Warnings, .. coverage] }));
        }

        return rendered;
    }

    /// <summary>The line stamped on a frame: the rate, and what stretch it belongs to.</summary>
    /// <param name="frame">The frame.</param>
    /// <returns>The banner.</returns>
    /// <remarks>
    /// The instant is on it too. A viewer watching a compressed timeline needs to know
    /// not only how fast it is running but where it has got to, and a frame that says
    /// only the rate leaves them integrating it by eye.
    /// </remarks>
    public static string Banner(AnimationFrame frame)
    {
        var instant = TimeMapping.Describe(frame.RateSiPerPlaybackSecond);
        var at = frame.SimulatedSeconds * 1e6;

        return frame.PhaseLabel is { } label
            ? $"t = {at:F3} µs  ·  {label}  ·  {instant}"
            : $"t = {at:F3} µs  ·  {instant}";
    }

    private static IReadOnlyList<TrajectorySample> Fly(
        CompiledModel model,
        IElectrostaticField field,
        int frames,
        Transport.Collisions.BackgroundGas? gas)
    {
        var species = IonSpecies.FromModel(model);

        // The animated ion flies the gas the model declares, which before this it did
        // not: both integrations took the `collisions` parameter's default and drew the
        // vacuum flight. A film of a thermalisation in which nothing thermalises is the
        // worst form of this defect, because the whole subject of the film is the thing
        // that has been left out.
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
            LandOnEdges = model.LandOnEdges,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        // Twice, for the reason the static figure does it twice: once at the model's
        // own cadence to learn how long the flight is, then at a cadence chosen from
        // that. Drawing whatever the model samples for its VTU export gave the einzel
        // lens a three-segment curve through a focusing element.
        var scout = new TrajectoryRecorder(model.SampleIntervalSi);

        TrajectoryIntegrator.Integrate(
            launch, species, field, settings, detector, scout, Sampler());

        if (scout.Samples.Count < 2)
        {
            return scout.Samples;
        }

        var flight = scout.Samples[^1].TimeSeconds - scout.Samples[0].TimeSeconds;

        if (!(flight > 0.0))
        {
            return scout.Samples;
        }

        var wanted = Math.Max(64, frames * SamplesPerFrame);
        var recorder = new TrajectoryRecorder(flight / wanted, capacity: 2 * wanted);

        TrajectoryIntegrator.Integrate(
            launch, species, field, settings, detector, recorder, Sampler());

        return recorder.Samples.Count >= 2 ? recorder.Samples : scout.Samples;
    }

    private static void RefuseDiffusive(CompiledModel model)
    {
        var mode = TransportModes.All.FirstOrDefault(
            m => string.Equals(m.Name, model.TransportMode, StringComparison.OrdinalIgnoreCase));

        if (mode?.ProducesTrajectories ?? true)
        {
            return;
        }

        throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
        {
            Code = Core.Errors.ErrorCodes.RegimeInvalid,
            Path = "/transport/mode",
            Constraint = $"this model declares '{model.TransportMode}' transport, which computes a "
                + "density rather than trajectories, and there is nothing here to animate",
            Suggestion = "RND-8 forbids drawing lines through a diffusive region, and a run "
                + "reports the density it ENDED with rather than a snapshot per instant - so the "
                + "frames would all be the same box and the film would show motion that was never "
                + "computed. Render a section instead with 'einzel render section', or shorten "
                + "'maximumFlightTime' to see the packet at a chosen moment",
        });
    }
}
