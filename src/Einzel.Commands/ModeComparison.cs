using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Io;
using Einzel.Transport;
using Einzel.Transport.Collisions;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>What both transport modes said about one model.</summary>
public sealed record ComparisonOutcome
{
    /// <summary>The model, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Content hash of the model document.</summary>
    public required string ModelHash { get; init; }

    /// <summary>Gas pressure, in millibar.</summary>
    public required double PressureMbar { get; init; }

    /// <summary>Whether that pressure is inside the band where both modes apply.</summary>
    public required bool InOverlapBand { get; init; }

    /// <summary>Mean transit time by trajectory integration, in microseconds.</summary>
    public double? TrajectoryTransitUs { get; init; }

    /// <summary>Its standard error, in microseconds.</summary>
    public double? TrajectoryStandardErrorUs { get; init; }

    /// <summary>How many ions the trajectory side flew.</summary>
    public required int Ions { get; init; }

    /// <summary>Fraction of the trajectory ensemble that reached the detector.</summary>
    public required double TrajectoryTransmission { get; init; }

    /// <summary>Fraction of the density that reached the collecting boundary.</summary>
    public required double DiffusionTransmission { get; init; }

    /// <summary>Mean transit time by statistical diffusion, in microseconds.</summary>
    public double? DiffusionTransitUs { get; init; }

    /// <summary>The difference, in microseconds.</summary>
    public double? DifferenceUs { get; init; }

    /// <summary>The difference as a fraction of the mean of the two.</summary>
    public double? RelativeDifference { get; init; }

    /// <summary>
    /// The difference in units of the trajectory ensemble's own standard error.
    /// </summary>
    /// <remarks>
    /// The number that says whether the two descriptions disagree or merely differ.
    /// A relative difference with no error beside it cannot distinguish a real
    /// disagreement from an under-sampled ensemble, which is exactly the mistake
    /// this engine's own first mobility check made.
    /// </remarks>
    public double? StandardErrors { get; init; }

    /// <summary>Warnings both runs carry, per GRD-2.</summary>
    public required IReadOnlyList<WarningJson> Warnings { get; init; }
}

/// <summary>
/// Runs both transport modes on one model and reports the disagreement.
/// </summary>
/// <remarks>
/// <para>
/// REG-3: in the overlap band both modes run on the same model and the comparison
/// is a supported operation with its own report. Spec figure 4 calls that band the
/// dangerous one - both descriptions run there, neither is obviously right, and the
/// engine must run both and report the disagreement rather than silently choosing.
/// </para>
/// <para>
/// The comparison is only meaningful because both modes describe the same gas: the
/// event-driven side scatters off the declared cross section and the diffusive side
/// takes its mobility from that same cross section. A model that declares a mobility
/// independently is compared as declared, and any disagreement then includes
/// whatever the two inputs disagree about.
/// </para>
/// </remarks>
public static class ModeComparison
{
    /// <summary>Runs both modes and differences them.</summary>
    /// <param name="modelPath">Path to the model.</param>
    /// <param name="ions">How many ions the trajectory side flies.</param>
    /// <returns>The comparison.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="EinzelException">The model does not validate, or declares no gas.</exception>
    public static ComparisonOutcome Execute(string modelPath, int ions = 60)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var text = File.ReadAllText(absolute);
        var validation = ModelValidator.Validate(
            ModelJson.Parse(text), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors);
        }

        var model = validation.Model!;

        // Resolved rather than taken from the document alone: REG-3 only means
        // something if the two modes are asked about the same gas, and a compare that
        // dropped an imported field would be comparing them in a gas neither model
        // declares.
        var gas = Io.GasFlowImport.Resolve(
            model.Gas, Path.GetDirectoryName(absolute) ?? Directory.GetCurrentDirectory());

        if (!gas.IsPresent)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.RegimeInvalid,
                Path = "/transport/gas",
                Constraint = "there is nothing to compare in vacuum: statistical diffusion "
                    + "describes ions moving through a gas, and this model declares none",
                Suggestion = "add a gas block. The comparison is worth making between about 1e-3 "
                    + "and 1e-2 mbar, where both descriptions run and neither is obviously right",
            });
        }

        var (field, fieldWarnings) = FieldAssembly.BuildReported(model);

        var pressureMbar = gas.PressureSi / 1e2;

        var inBand = pressureMbar >= RegimeDiagnostics.OverlapMbar
            && pressureMbar <= RegimeDiagnostics.DiffusiveMbar;

        var (byTrajectory, standardError, arrived) = ByTrajectory(model, field, gas, ions);

        // Diffusion, on the same model, through the same wiring `run` uses.
        var diffusive = DiffusionRun.Execute(model, field, fieldWarnings);
        var result = diffusive.Result;

        double? byDiffusion = result.Arrivals.Count > 0 && result.Collected > 0.0
            ? result.Arrivals.Sum(a => a.TimeSeconds * a.Ions) / result.Collected * 1e6
            : null;

        double? difference = byTrajectory is { } t && byDiffusion is { } d ? Math.Abs(t - d) : null;

        var warnings = new List<Core.Results.ValidityWarning>(diffusive.Warnings);

        // The comparison is only meaningful if both sides describe the same
        // scattering. Mason-Schamp with a collision cross section is the rigid-sphere
        // mobility; Langevin capture is a different mechanism with a different
        // mobility. A model that declares polarization capture for the event-driven
        // side and lets the diffusive side derive its mobility from a cross section
        // is comparing two instruments, and any disagreement is theirs rather than
        // the numerics'.
        if (diffusive.Mobility.Derived && model.Gas.Model == "langevin")
        {
            warnings.Add(new Core.Results.ValidityWarning(
                "regime.comparison-mismatched-mechanism",
                "the gas declares Langevin capture for the event-driven mode, and the diffusive "
                + "mode derived its mobility from the collision cross section, which is the "
                + "rigid-sphere value. The two sides are describing different scattering, so a "
                + "disagreement here is between the two inputs rather than between the two "
                + "descriptions. Declare a mobility measured in this gas, or set the collision "
                + "model to 'hardSphere' so both sides use the cross section",
                Core.Results.WarningSeverity.ValidityViolation));
        }

        // A transit time is only a transit time when nearly everything arrives.
        // Below that both numbers are conditional means over whichever subset got
        // there, and the two subsets are not the same ions - so a disagreement says
        // the run was too short rather than that the descriptions differ. That
        // mistake produced a 139% disagreement here before this guard existed.
        var trajectoryTransmission = ions > 0 ? arrived / (double)ions : 0.0;

        var diffusionTransmission = diffusive.Launched > 0.0
            ? result.Collected / diffusive.Launched
            : 0.0;

        // The density grid has edges and a bare trajectory model does not. Where a
        // model declares no geometry, an ion flies to the detector however far off
        // axis it wanders, while the density is absorbed the moment it reaches the
        // edge of the box it is tracked in. That is not a numerical difference - the
        // two modes are being asked about different instruments, one with walls and
        // one without.
        var wallLosses = result.Lost
            .Where(p => p.Key is "maxY" or "minY")
            .Sum(p => p.Value);

        var hasGeometry = model.Fields.Any(f => f.Solve is { Electrodes.Count: > 0 });

        if (!hasGeometry && diffusive.Launched > 0.0 && wallLosses > 0.05 * diffusive.Launched)
        {
            warnings.Add(new Core.Results.ValidityWarning(
                "regime.comparison-unmatched-boundaries",
                $"{wallLosses / diffusive.Launched:P0} of the density was absorbed at the edge of "
                + "the region it is tracked over, and this model declares no electrodes for a "
                + "trajectory to hit. So the diffusive run has walls and the event-driven run does "
                + "not, and the two are being asked about different instruments. Declare the "
                + "geometry, or widen the density grid until the edges stop taking ions",
                Core.Results.WarningSeverity.ValidityViolation));
        }

        if (trajectoryTransmission < 0.5 || diffusionTransmission < 0.5)
        {
            warnings.Add(new Core.Results.ValidityWarning(
                "regime.comparison-incomplete",
                $"only {trajectoryTransmission:P0} of the flown ions and {diffusionTransmission:P0} "
                + "of the density reached the collector inside the flight-time ceiling. A mean "
                + "transit over the subset that arrived is not a transit time, and the two subsets "
                + "are not the same ions, so the difference below is mostly the ceiling. Raise "
                + "maximumFlightTime, or raise the field so the ions are driven rather than "
                + "diffusing there",
                Core.Results.WarningSeverity.ValidityViolation));
        }

        if (!inBand)
        {
            warnings.Add(new Core.Results.ValidityWarning(
                "regime.comparison-outside-band",
                $"{pressureMbar:G3} mbar is outside the {RegimeDiagnostics.OverlapMbar:G1} to "
                + $"{RegimeDiagnostics.DiffusiveMbar:G1} mbar band where both descriptions apply. "
                + "One of these two numbers is being computed by a mode outside its own validity, "
                + "so a disagreement here says which mode is wrong rather than that they disagree",
                Core.Results.WarningSeverity.ValidityViolation));
        }

        return new ComparisonOutcome
        {
            ModelPath = absolute,
            ModelHash = Project.ContentHash.OfText(text),
            PressureMbar = pressureMbar,
            InOverlapBand = inBand,
            TrajectoryTransitUs = byTrajectory,
            TrajectoryStandardErrorUs = standardError,
            Ions = ions,
            TrajectoryTransmission = trajectoryTransmission,
            DiffusionTransmission = diffusionTransmission,
            DiffusionTransitUs = byDiffusion,
            DifferenceUs = difference,
            RelativeDifference = byTrajectory is { } a && byDiffusion is { } b && a + b > 0.0
                ? difference / (0.5 * (a + b))
                : null,
            StandardErrors = difference is { } gap && standardError is { } error && error > 0.0
                ? gap / error
                : null,
            Warnings =
            [
                .. warnings.Select(w => new WarningJson
                {
                    Code = w.Code,
                    Message = w.Message,
                    Severity = w.Severity.ToString(),
                    Suppressible = w.IsSuppressible,
                }),
            ],
        };
    }

    /// <summary>Mean transit time by flying ions and colliding them.</summary>
    private static (double? Mean, double? StandardError, int Arrived) ByTrajectory(
        CompiledModel model, IElectrostaticField field, BackgroundGas gas, int ions)
    {
        var species = IonSpecies.FromModel(model);

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = Math.Max(model.RelativeTolerance, 1e-8),
            LandOnEdges = model.LandOnEdges,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        var cloud = IonCloud.Draw(
            new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi()),
            species,
            model.Cloud with { Ions = ions },
            model.SourceDirection);

        var arrivals = new List<double>(ions);

        for (var i = 0; i < cloud.Length; i++)
        {
            var sampler = new CollisionSampler(gas, species.MassSi, species.ChargeSi, model.Gas.Seed + i);

            var result = TrajectoryIntegrator.Integrate(
                cloud[i], species, field, settings, detector, collisions: sampler);

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrivals.Add(result.FlightTimeSeconds * 1e6);
            }
        }

        if (arrivals.Count < 2)
        {
            return (null, null, arrivals.Count);
        }

        var mean = arrivals.Average();
        var variance = arrivals.Sum(a => (a - mean) * (a - mean)) / (arrivals.Count - 1.0);

        return (mean, Math.Sqrt(variance / arrivals.Count), arrivals.Count);
    }
}
