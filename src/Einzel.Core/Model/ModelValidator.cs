using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Numerics;
using Einzel.Core.Units;

namespace Einzel.Core.Model;

/// <summary>The outcome of validating a model document.</summary>
/// <param name="Model">The compiled model, or null when validation failed.</param>
/// <param name="Errors">Every error found, in document order.</param>
public sealed record ModelValidation(CompiledModel? Model, IReadOnlyList<EinzelError> Errors)
{
    /// <summary>Whether the document validated.</summary>
    public bool IsValid => Model is not null && Errors.Count == 0;
}

/// <summary>
/// Validates a model document and compiles it to SI.
/// </summary>
/// <remarks>
/// <para>
/// Every error is collected rather than thrown on the first failure. AGT-3 makes
/// errors recovery instructions, and the recovery an agent wants is the full
/// list: fixing one unit at a time across five round trips is the behaviour this
/// avoids.
/// </para>
/// <para>
/// Checks are ordered so that later ones can assume earlier ones passed. Where a
/// prerequisite is missing the dependent check is skipped rather than reported as
/// a second failure, so one mistake produces one error.
/// </para>
/// </remarks>
public static class ModelValidator
{
    /// <summary>Validates and compiles a document.</summary>
    /// <param name="document">The document to validate.</param>
    /// <returns>The compiled model, or the errors that prevented it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    public static ModelValidation Validate(ModelDocument document) => Validate(document, overrides: null);

    /// <summary>Validates and compiles a document with parameter overrides applied.</summary>
    /// <param name="document">The document to validate.</param>
    /// <param name="overrides">
    /// Replacement values for free parameters, as a sweep or optimiser supplies.
    /// Derived parameters re-evaluate against them.
    /// </param>
    /// <returns>The compiled model, or the errors that prevented it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="document"/> is null.</exception>
    /// <param name="sourceDirectory">
    /// The directory the document was read from, which the paths it references are
    /// resolved against. Null for a document compiled from a string, and a consumer
    /// that needs to read one of those files is then refused rather than given a
    /// model whose declared gas field is silently absent.
    /// </param>
    public static ModelValidation Validate(
        ModelDocument document,
        IReadOnlyDictionary<string, Quantity>? overrides,
        string? sourceDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var errors = new List<EinzelError>();

        ValidateSchemaVersion(document, errors);

        var surface = ParameterSurface.Resolve(document.Parameters, overrides, errors);

        if (surface is null)
        {
            return new ModelValidation(null, errors);
        }

        var p = surface.Values();

        // A mixture says what it is transporting in `species`, so `ion` is neither required nor
        // permitted - and asking ValidateIon anyway would refuse every valid mixture for a
        // missing field, before the check that explains why it is missing ever ran.
        var declaresSpecies = document.Species is { Count: > 0 };

        var (mass, charge) = declaresSpecies
            ? (null, null)
            : ValidateIon(document.Ion, p, errors);
        // Fields are compiled before the source because whether a source may start
        // at rest depends on whether anything else can accelerate it: a beam
        // carries its own energy, a trapped packet is accelerated by the
        // instrument. Their errors go to a separate list and are spliced back
        // afterwards, so the reordering does not leak into the reported order -
        // ModelValidation promises errors in document order and /source precedes
        // /fields.
        var fieldErrors = new List<EinzelError>();

        // A stage sets parameters, so resolving one means resolving the whole
        // surface again with those values layered on - derived parameters and all.
        //
        // Used once, to resolve the timeline before any element is compiled. It was a
        // closure "because only the solve branch needs it, and threading the declared
        // parameters through every field kind would put the sequencer in the signature
        // of things that have nothing to do with it" - and that argument was wrong in a
        // way that cost a defect. The analytic kinds have everything to do with it: a
        // phase gives them different numbers, and while the timeline reached only the
        // solve branch they stayed frozen at baseline while the solved elements moved.
        IReadOnlyDictionary<string, Quantity>? Restage(
            IReadOnlyDictionary<string, Quantity> set, List<EinzelError> into)
        {
            var merged = new Dictionary<string, Quantity>(StringComparer.Ordinal);

            if (overrides is not null)
            {
                foreach (var (name, value) in overrides)
                {
                    merged[name] = value;
                }
            }

            foreach (var (name, value) in set)
            {
                merged[name] = value;
            }

            return ParameterSurface.Resolve(document.Parameters, merged, into)?.Values();
        }

        // Gathered before any element is compiled, and handed to all of them. A phase
        // is the instrument's rather than one element's, so a parameter it sets has to
        // reach every expression written over it - which is exactly what compiling
        // stages per element failed to do.
        var timeline = Timeline(document, Restage, fieldErrors);

        var fields = ValidateFields(document.Fields, p, timeline, fieldErrors, sourceDirectory);

        // A field that failed to compile is not evidence that nothing can
        // accelerate the ion, it is evidence that we cannot tell. Saying otherwise
        // adds a second error advising the author to declare a field they did
        // declare, and one mistake should produce one error.
        //
        // The timeline reports into the same list, so a malformed sequence suppresses
        // this too. That is the same reasoning - a document whose phases did not resolve
        // has not told us what its electrodes hold - rather than an accident of sharing
        // a list.
        var canAccelerate = fieldErrors.Count > 0 || CanAccelerate(fields);

        var source = ValidateSource(document.Source, p, errors, canAccelerate);
        errors.AddRange(fieldErrors);

        var detector = ValidateDetector(document.Detector, p, errors);
        // Every mode this run uses, not only the model's own. A phase may name a
        // different one, and the diffusive requirements - a gas, a mobility, a density
        // grid - are needed if ANY phase is diffusive. Gating them on the model's mode
        // alone let a trajectory model with a diffusive phase validate and then fail at
        // run time with the gas it never declared.
        //
        // This is the sixth time a check here has had to learn a new configuration: the
        // DC, the drive, the 3D arm, the solved stages, the analytic phases, and now the
        // phase modes. A check that asks what an instrument is doing must ask over every
        // configuration it has.
        var modes = Modes(document, timeline);
        var transport = ValidateTransport(document.Transport, p, modes, errors);

        // A ramp in a diffusive phase used to be refused here, because the density solver
        // stepped through a field it held fixed within a phase. It re-samples the field
        // every step now, so a ramped diffusive phase is the elution scan of a mobility
        // analyser rather than a thing to write as a staircase.
        if (errors.Count > 0
            || (!declaresSpecies && (mass is null || charge is null))
            || source is null || detector is null || transport is null)
        {
            return new ModelValidation(null, errors);
        }

        // After transport, because a species may derive its mobility from the gas - which is
        // the same ordering ValidateMobility already needed and for the same reason.
        var mixture = ValidateSpecies(document, transport.Gas, modes, p, errors);

        if (errors.Count > 0)
        {
            return new ModelValidation(null, errors);
        }

        var model = new CompiledModel
        {
            SourceDirectory = sourceDirectory,
            Source = document,

            // A mixture leaves these at its first population's values. Enough for the
            // diagnostics that need AN ion - a Knudsen number, a regime check - and never
            // enough to be mistaken for all of them, because `IsMixture` is what a caller
            // asks when the difference matters.
            MassSi = mixture.Count > 0 ? mixture[0].MassSi : mass!.Value,
            ChargeSi = mixture.Count > 0 ? mixture[0].ChargeSi : charge!.Value,
            Species = mixture,
            SourcePosition = source.Position,
            SourceDirection = source.Direction,
            Cloud = source.Cloud,
            AccelerationPotentialSi = source.Potential,
            EnergyFraction = source.EnergyFraction,
            Fields = fields,
            DetectorPoint = detector.Value.Point,
            DetectorNormal = detector.Value.Normal,
            TransportMode = transport.Mode,

            // A phase that names no mode keeps the model's, which is the same rule its
            // parameter overrides follow. So a model with no sequence and one whose
            // every phase runs in the declared mode are the same run.
            Phases = Schedule(timeline, transport.Mode),
            RelativeTolerance = transport.RelativeTolerance,
            MaximumFlightTimeSi = transport.MaximumFlightTime,
            SampleIntervalSi = transport.SampleInterval,
            Gas = transport.Gas,
            Mobility = transport.Mobility,
            DensityGrid = transport.DensityGrid,
            SpaceChargeMode = transport.SpaceCharge,
            SpaceChargeGrid = transport.SpaceChargeGrid,
            DensityStep = transport.DensityStep,
            LandOnEdges = transport.LandOnEdges,
            Parameters = surface,
        };

        ValidateGeometryConsistency(model, errors);

        return errors.Count > 0 ? new ModelValidation(null, errors) : new ModelValidation(model, errors);
    }

    private static void ValidateSchemaVersion(ModelDocument document, List<EinzelError> errors)
    {
        if (string.IsNullOrWhiteSpace(document.SchemaVersion))
        {
            errors.Add(Missing("/schemaVersion", "a model document must declare its schema version",
                $"add \"schemaVersion\": \"{ModelSchema.CurrentVersion}\""));
            return;
        }

        if (!ModelSchema.SupportedVersions.Contains(document.SchemaVersion, StringComparer.Ordinal))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/schemaVersion",
                Constraint = $"this build reads schema versions {string.Join(", ", ModelSchema.SupportedVersions)}",
                Observed = new ObservedValue(0.0, document.SchemaVersion),
                Suggestion = $"this build writes version {ModelSchema.CurrentVersion}",
            });
        }
    }

    private static (double? Mass, double? Charge) ValidateIon(IonDocument? ion, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (ion is null)
        {
            errors.Add(Missing("/ion", "a model must declare the ion being tracked",
                "add an \"ion\" object with \"massToCharge\" and \"chargeNumber\""));
            return (null, null);
        }

        if (ion.ChargeNumber == 0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/ion/chargeNumber",
                Constraint = "an ion cannot have zero charge",
                Observed = new ObservedValue(0, "1"),
                Suggestion = "use 1 for a singly charged cation",
            });
        }

        if (ion.MassToCharge is null)
        {
            errors.Add(Missing("/ion/massToCharge", "the ion's mass-to-charge ratio is required",
                "add {\"value\": 500, \"unit\": \"Da\"}"));
            return (null, null);
        }

        var massToCharge = TryQuantity(ion.MassToCharge, "/ion/massToCharge", Dimension.MassDimension, p, errors);

        if (massToCharge is null || ion.ChargeNumber == 0)
        {
            return (null, null);
        }

        if (massToCharge.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/ion/massToCharge",
                Constraint = "mass-to-charge must be positive",
                Observed = new ObservedValue(ion.MassToCharge.Value, ion.MassToCharge.Unit),
                Suggestion = "supply a positive mass-to-charge ratio",
            });
            return (null, null);
        }

        var mass = massToCharge.Value.SiValue * Math.Abs(ion.ChargeNumber);
        var charge = Quantity.From(ion.ChargeNumber, "e").SiValue;

        return (mass, charge);
    }

    private sealed record SourceValues(
        Vec3 Position, Vec3 Direction, double Potential, double EnergyFraction, IonCloudSettings Cloud);

    /// <summary>
    /// Whether any declared field could put energy into an ion that starts at rest.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Magnitude rather than kind. A uniform field of zero, or a solved geometry
    /// whose every electrode is grounded, is field-free in everything but its type
    /// discriminator - and an ion at rest in one sits there until the flight-time
    /// ceiling expires, which is exactly the outcome the zero-potential check
    /// exists to prevent. Testing the kind alone would narrow that check into
    /// uselessness rather than narrowing it correctly.
    /// </para>
    /// <para>
    /// Also the predicate <see cref="ValidateGeometryConsistency"/> needs for
    /// "something can turn the ion around", so it is written once.
    /// </para>
    /// </remarks>
    internal static bool CanAccelerate(IReadOnlyList<CompiledField>? fields) =>
        fields is not null && fields.Any(CanDoWork);

    private static bool CanDoWork(CompiledField field) =>
        Energised(field) || field.Phases.Any(Energised);

    /// <summary>Whether one compiled state of an element could put energy into an ion.</summary>
    /// <remarks>
    /// Separated from <see cref="CanDoWork"/> so the phases can be asked the same
    /// question. An analytic element energised only by a phase - zero at baseline, a
    /// kilovolt per metre once the instrument switches - is the fifth configuration this
    /// check has had to learn, after the DC, the drive, the 3D arm, and the solved
    /// stages. The pattern is the same every time: a check that asks what an instrument
    /// is doing must ask over every configuration it has, and a new way to hold a
    /// potential is a new configuration.
    /// </remarks>
    private static bool Energised(CompiledField field) => field.Kind switch
    {
        CompiledFieldKind.FieldFree => false,
        CompiledFieldKind.Uniform => field.Field.LengthSquared > 0.0,
        CompiledFieldKind.HalfSpaceUniform => field.PotentialGradientSi != 0.0,

        // A solve with every electrode at the same potential has no gradient
        // anywhere, and grounded boundaries make that potential zero.
        //
        // Three things have to count, and each was found by a device that the
        // previous version declared incapable of moving an ion:
        //
        //  - the DC, obviously;
        //  - the DRIVE, because a Paul trap and an RF-only mass filter hold zero
        //    volts of DC on every electrode and all of their potential as drive;
        //  - the STAGES, because a pulsed-extraction trap holds everything at zero
        //    until it switches, which is what makes it the archetypal start-at-rest
        //    device in the first place. Reading only the base potentials asks what
        //    the instrument is doing before it has been told to do anything.
        CompiledFieldKind.Solved2D =>
            field.Solve is { } solve
            && (Energised(solve.Electrodes)
                || solve.Stages.Any(stage => Energised(stage.Electrodes))),

        CompiledFieldKind.Solved3D =>
            field.Solve3D is { } volume
            && (Energised3D(volume.Electrodes)
                || volume.Stages.Any(stage => Energised3D(stage.Electrodes))),

        _ => true,
    };

    /// <summary>Whether any of these electrodes can move an ion.</summary>
    /// <remarks>
    /// The <b>edge profile</b> is the sixth configuration, and it arrived exactly as the
    /// remarks on <see cref="Energised(CompiledField)"/> predict one would: it is a new way
    /// to hold a potential. Its volts live in its profile rather than in a scalar, and every
    /// shipped one leaves that scalar null - so a mirror whose boards are the only thing
    /// energised in the whole model was refused as an instrument in which nothing could move
    /// an ion. Asking the profile is the same question, asked where this shape keeps its
    /// answer.
    /// </remarks>
    private static bool Energised(IReadOnlyList<CompiledElectrode> electrodes) =>
        electrodes.Any(e => e.Potential != 0.0 || e.IsDriven || HoldsAProfile(e));

    /// <summary>Whether an edge profile is held anywhere above earth along its edge.</summary>
    private static bool HoldsAProfile(CompiledElectrode electrode) =>
        electrode.Shape == ElectrodeShape.EdgeProfile
        && electrode.Profile.Any(point => point.Potential != 0.0);

    /// <summary>Whether any of these electrodes can move an ion.</summary>
    private static bool Energised3D(IReadOnlyList<CompiledElectrode3D> electrodes) =>
        electrodes.Any(e => e.Potential != 0.0 || e.IsDriven);

    private static SourceValues? ValidateSource(
        SourceDocument? source,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors,
        bool canAccelerate)
    {
        if (source is null)
        {
            errors.Add(Missing("/source", "a model must declare where the ion starts",
                "add a \"source\" object with \"position\", \"direction\", and \"accelerationPotential\""));
            return null;
        }

        var position = TryVector(source.Position, "/source/position", Dimension.LengthDimension, p, errors);
        var direction = TryDirection(source.Direction, "/source/direction", errors, p);
        var potential = TryQuantity(
            source.AccelerationPotential, "/source/accelerationPotential", Dimension.ElectricPotential, p, errors);

        if (source.EnergyFraction <= -1.0 || !double.IsFinite(source.EnergyFraction))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/energyFraction",
                Constraint = "the energy offset must be finite and greater than -1, so the ion has positive energy",
                Observed = new ObservedValue(source.EnergyFraction, "1"),
                Suggestion = "use 0 for nominal energy, or 0.05 for 5 percent high",
            });
            return null;
        }

        if (position is null || direction is null || potential is null)
        {
            return null;
        }

        // Zero is legal when the instrument does the accelerating. A pulsed
        // extraction trap holds its packet at rest and then switches a field on,
        // which is the entire mechanism, and a model that cannot say so cannot
        // describe one. It stays an error when nothing in the model could move
        // the ion, because then it really does sit there.
        if (potential.Value.SiValue == 0.0 && !canAccelerate)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/accelerationPotential",
                Constraint =
                    "the accelerating potential may only be zero when a field can accelerate the ion, "
                    + "and this model declares none that can",
                Observed = new ObservedValue(source.AccelerationPotential!.Value, source.AccelerationPotential.Unit),
                Suggestion =
                    "supply a non-zero potential, for example {\"value\": 4, \"unit\": \"kV\"}, "
                    + "or declare a field that accelerates the ion from rest",
            });
            return null;
        }

        var cloud = ValidateCloud(source.Cloud, "/source/cloud", p, errors);

        return cloud is null
            ? null
            : new SourceValues(
                position.Value, direction.Value, potential.Value.SiValue, source.EnergyFraction, cloud);
    }

    /// <summary>
    /// Reads the source cloud, or returns the single-ion default when none is
    /// declared.
    /// </summary>
    /// <remarks>
    /// Every spread is optional and every default is zero, so a model that says
    /// nothing about a cloud launches exactly what it launched before. That is not
    /// only backward compatibility: a spread that appeared by default would change
    /// every existing result silently, and a resolving power quietly getting worse
    /// is indistinguishable from a bug.
    /// Public so an ion list's <c>cloud</c> set is held to the same checks and the same
    /// conversion as <c>source.cloud</c>, and draws exactly what a model's cloud draws.
    /// </remarks>
    /// <param name="cloud">The cloud block, or null for a single ion.</param>
    /// <param name="path">JSON Pointer to the block, prefixed to every error.</param>
    /// <param name="p">Resolved parameters its quantities may name.</param>
    /// <param name="errors">Where the errors go.</param>
    /// <returns>The settings, or null when the block was refused.</returns>
    public static IonCloudSettings? ValidateCloud(
        CloudDocument? cloud, string path, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(errors);

        if (cloud is null)
        {
            return new IonCloudSettings();
        }

        if (cloud.Ions < 1)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/ions",
                Constraint = "a cloud launches at least one ion",
                Observed = new ObservedValue(cloud.Ions, "1"),
                Suggestion = "ACC-5 wants a transmission interval within one per cent at 95%, which needs of "
                    + "order a thousand ions; fewer gives an honest error bar too wide to design against",
            });

            return null;
        }

        if (cloud.EnergyFractionSpread < 0.0 || !double.IsFinite(cloud.EnergyFractionSpread))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/energyFractionSpread",
                Constraint = "an energy spread is a non-negative fraction of the nominal energy",
                Observed = new ObservedValue(cloud.EnergyFractionSpread, "1"),
                Suggestion = "use 0.01 for one per cent, or omit it for a monoenergetic cloud",
            });

            return null;
        }

        var temperature = Optional(
            cloud.Temperature, $"{path}/temperature", Dimension.TemperatureDimension, p, errors);

        var transverse = Optional(
            cloud.TransverseSpread, $"{path}/transverseSpread", Dimension.LengthDimension, p, errors);

        var longitudinal = Optional(
            cloud.LongitudinalSpread, $"{path}/longitudinalSpread", Dimension.LengthDimension, p, errors);

        // Dimensionless, because that is what an angle is in SI - `deg` and `mrad` are
        // conversions to radians, not a separate dimension. So the unit is required and
        // is the whole of the meaning: 20 and 20 deg differ by a factor of 57.
        var divergence = Optional(
            cloud.Divergence, $"{path}/divergence", Dimension.Dimensionless, p, errors);

        if (temperature is null || transverse is null || longitudinal is null || divergence is null)
        {
            return null;
        }

        // A half-angle, so a right angle is already a hemisphere and anything at or past
        // it is not a beam. Refused rather than clamped: a document asking for 120 degrees
        // of divergence has confused a half-angle for a full one, and silently halving it
        // would answer a question nobody asked.
        if (divergence.Value >= Math.PI / 2.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/divergence",
                Constraint = "divergence is the half-angle of the cone the beam fills, so it "
                    + "must be under 90 degrees",
                Observed = new ObservedValue(divergence.Value * 180.0 / Math.PI, "deg"),
                Suggestion = "halve it if you meant the full opening angle",
            });

            return null;
        }

        foreach (var (value, where) in new[]
        {
            (temperature.Value, $"{path}/temperature"),
            (transverse.Value, $"{path}/transverseSpread"),
            (longitudinal.Value, $"{path}/longitudinalSpread"),
            (divergence.Value, $"{path}/divergence"),
        })
        {
            if (value < 0.0)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = where,
                    Constraint = "a spread cannot be negative",
                    Observed = new ObservedValue(value, "SI"),
                    Suggestion = "omit it for zero",
                });

                return null;
            }
        }

        if (cloud.Population is { } declared && declared < 1)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/population",
                Constraint = "a packet holds at least one ion",
                Observed = new ObservedValue(declared, "1"),
                Suggestion = "omit it to model the ions you launch as the ions that are there, or set 1 to "
                    + "sample an intrinsic source property one ion at a time",
            });

            return null;
        }

        return new IonCloudSettings
        {
            Ions = cloud.Ions,
            Population = cloud.Population,
            Seed = cloud.Seed,
            TemperatureK = temperature.Value,
            TransverseSpreadM = transverse.Value,
            LongitudinalSpreadM = longitudinal.Value,
            EnergyFractionSpread = cloud.EnergyFractionSpread,
            DivergenceRadians = divergence.Value,
        };
    }

    /// <summary>A quantity that may be absent, reading as zero when it is.</summary>
    private static double? Optional(
        QuantityValue? value,
        string path,
        Dimension expected,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (value is null)
        {
            return 0.0;
        }

        var quantity = TryQuantity(value, path, expected, p, errors);
        return quantity?.SiValue;
    }

    private static List<CompiledField> ValidateFields(
        IReadOnlyList<FieldDocument>? fields,
        IReadOnlyDictionary<string, Quantity> p,
        IReadOnlyList<PhaseSurface> timeline,
        List<EinzelError> errors,
        string? directory)
    {
        if (fields is null || fields.Count == 0)
        {
            errors.Add(Missing("/fields", "a model must declare at least one field element",
                "add [{\"type\": \"fieldFree\"}] for a drift-only model"));
            return [];
        }

        var compiled = new List<CompiledField>(fields.Count);

        for (var i = 0; i < fields.Count; i++)
        {
            var element = CompileField(fields[i], $"/fields/{i}", p, timeline, errors, directory);

            if (element is not null)
            {
                compiled.Add(element);
            }
        }

        return compiled;
    }

    /// <summary>
    /// A sequence belongs to the instrument, so only one element may declare one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A stage sets a model parameter, but only its own element is recompiled
    /// against it.</b> That is the defect this refuses. Two electrodes whose potentials
    /// are the *same expression* over the same parameter end up holding different
    /// voltages when one of them is in a staged element and the other is not - measured
    /// at 900 V against 300 V, on a model that validated cleanly with no diagnostic
    /// anywhere.
    /// </para>
    /// <para>
    /// The stage design's own rationale is the claim that fails: setting a parameter
    /// "moves everything that depends on it at once, including the derived parameters".
    /// It moves everything in one element.
    /// </para>
    /// <para>
    /// Refused rather than patched, because the two coherent readings need work this
    /// does not do. Either the timeline is the instrument's and every element recompiles
    /// against each stage - which is what the rationale describes and what SEQ-1's
    /// transport mode per phase requires, since a mode is a property of the run and not
    /// of one electrode assembly - or a stage is scoped to its element, which is a
    /// different feature and not the one documented. Making the incoherent case
    /// inexpressible is the honest state until that is settled.
    /// </para>
    /// <para>
    /// No shipped model or template has more than one field element, so this refuses
    /// nothing that exists. It is a latent defect being closed rather than a live one.
    /// </para>
    /// </remarks>
    private static void Sequenced(IReadOnlyList<FieldDocument> fields, List<EinzelError> errors)
    {
        var staged = new List<int>();

        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].Solve?.Stages is { Count: > 0 }
                || fields[i].Solve3d?.Stages is { Count: > 0 })
            {
                staged.Add(i);
            }
        }

        if (staged.Count == 0)
        {
            return;
        }

        if (staged.Count > 1)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = StagePath(fields[staged[1]], staged[1]),
                Constraint = "more than one element declares a sequence, and an instrument "
                    + $"has one timeline: elements {string.Join(", ", staged)} each declare "
                    + "stages",
                Suggestion = "declare the sequence on one element. Two timelines over the "
                    + "same parameters would each switch at their own instants, and the "
                    + "document would say two things about what the instrument is doing",
            });

            return;
        }

        if (fields.Count > 1)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"/fields/{staged[0]}/solve/stages",
                Constraint = $"element {staged[0]} declares a sequence and the model has "
                    + $"{fields.Count} field elements, of which only the sequenced one is "
                    + "recomputed at each stage",
                Suggestion = "a stage sets a model parameter, so an electrode in another "
                    + "element written over that same parameter would keep its baseline "
                    + "value while this element followed the stage - two electrodes with "
                    + "identical expressions holding different voltages, with nothing to "
                    + "say so. Put the sequenced geometry in one element, or drop the "
                    + "sequence",
            });
        }
    }

    /// <summary>Resolves the parameter surface as it stands during one stage.</summary>
    /// <param name="set">The values the stage holds, with units.</param>
    /// <param name="into">Where to report a value that does not resolve.</param>
    /// <returns>The surface, or null when the stage could not be resolved.</returns>
    internal delegate IReadOnlyDictionary<string, Quantity>? StageResolver(
        IReadOnlyDictionary<string, Quantity> set, List<EinzelError> into);

    /// <summary>One element, and its per-phase states where the timeline reaches it.</summary>
    /// <remarks>
    /// <para>
    /// <b>The analytic kinds have nowhere to put a phase, so they get compiled copies.</b>
    /// A solved geometry carries its phases inside its own <c>Stages</c>, because there a
    /// phase re-weights channels that are already solved and the geometry is untouched. An
    /// analytic element has no channels - a phase simply gives it different numbers - so
    /// the whole element is compiled once per phase.
    /// </para>
    /// <para>
    /// This is the half the first lift missed. Threading the timeline to the solved branch
    /// alone left a model whose sequence set a parameter used by a <c>halfSpaceUniform</c>
    /// cap potential with the solved elements following and the analytic one frozen at
    /// baseline - the same silent wrong answer, in the elements nobody thought of because
    /// they have no stages of their own.
    /// </para>
    /// <para>
    /// <b>Identical phases produce no states</b>, which is a distinction rather than an
    /// optimisation: an element whose expressions do not depend on any parameter the
    /// timeline sets really is static, and wrapping it would hand the assembly switch
    /// instants to land on and make a static element answer a time-varying interface for
    /// nothing.
    /// </para>
    /// </remarks>
    private static CompiledField? CompileField(
        FieldDocument field,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        IReadOnlyList<PhaseSurface> timeline,
        List<EinzelError> errors,
        string? directory)
    {
        // Before the element itself, so that "a solved element may not declare a region"
        // is reported even when the solve is also wrong. Hiding one mistake behind another
        // makes a document take two rounds to fix and gives no reason for the second.
        //
        // Compiled once off the base surface and deliberately not re-derived per phase: a
        // region is geometry, and the sequencer already refuses to let a stage move
        // geometry. Moving one would change which element is silent where, which is a
        // different instrument rather than a different setting of one.
        var region = CompileRegion(field, path, p, errors);

        // An axis means one thing on one element kind. On any other it would be a
        // declared property that nothing reads, which is the shape of `transverseWidth`
        // being accepted in place of `transverseSpread` and running a point source.
        if (field.Axis is not null && field.Type != "idealQuadrupoleRf")
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/axis",
                Constraint = $"only an idealQuadrupoleRf element has an axis; a {field.Type} "
                    + "element is oriented by its own coordinates or its normal",
                Observed = new ObservedValue(0.0, field.Axis),
                Suggestion = "remove axis, or make the element an idealQuadrupoleRf",
            });

            return null;
        }

        var baseline = CompileOnce(field, path, p, timeline, errors, directory);

        if (baseline is null)
        {
            return null;
        }

        if (region is not null)
        {
            baseline = baseline with { Region = region };
        }

        if (timeline.Count == 0 || !NeedsPhases(field.Type))
        {
            return baseline;
        }

        var phases = new List<CompiledField>(timeline.Count);
        var boundaries = new List<double>(timeline.Count);
        var elapsed = 0.0;
        var moved = false;

        foreach (var phase in timeline)
        {
            // Errors are swallowed here on purpose: the baseline compile above already
            // reported anything wrong with this element against the same document, and
            // reporting it once per phase as well would turn one mistake into a wall.
            var ignored = new List<EinzelError>();
            var state = CompileOnce(field, path, phase.Surface, timeline, ignored, directory);
            if (state is null)
            {
                return baseline;
            }

            // An analytic element is compiled once per phase and switched; there is no
            // weight to interpolate. A ramp whose parameter reaches one would leave the
            // element frozen at its start value while the solved elements ramp, which is
            // the silent half-instrument the model-level timeline was built to prevent.
            if (phase.EndSurface is not null)
            {
                var endState = CompileOnce(field, path, phase.EndSurface, timeline, ignored, directory);
                if (endState is not null && !Same(state, endState))
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{phase.Path}/ramp",
                        Constraint = $"stage '{phase.Name}' ramps a parameter that the analytic element at {path} depends on, and a ramp is supported on solved geometries only",
                        Suggestion = "switch the analytic element in more phases, or describe it as a solved geometry",
                    });
                    return baseline;
                }
            }

            elapsed += phase.DurationSeconds;
            boundaries.Add(elapsed);
            phases.Add(state);
            moved |= !Same(baseline, state);
        }

        return moved
            ? baseline with { Phases = phases, PhaseBoundariesSeconds = boundaries }
            : baseline;
    }

    /// <summary>The box outside which an element is silent, if it declares one.</summary>
    /// <remarks>
    /// <para>
    /// Refused on a solved element rather than ignored. A solve is already bounded by its
    /// own domain, so a region would be a second statement about the same extent, and a
    /// document that says a thing twice can say it two ways.
    /// </para>
    /// <para>
    /// All six bounds are required rather than defaulted to infinity on the missing axes.
    /// A half-open region is a legitimate thing to want, but "the axes I left out" is not
    /// how anyone reads a partly-filled box, and the failure would be silent.
    /// </para>
    /// </remarks>
    private static FieldRegion? CompileRegion(
        FieldDocument field,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (field.Region is not { } region)
        {
            return null;
        }

        if (field.Type is "solved2d" or "solved3d")
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/region",
                Constraint = $"a {field.Type} element is already bounded by its own solve "
                    + "domain, so it may not also declare a region",
                Suggestion = "remove the region, or move the solve domain if the extent is "
                    + "what you meant to change",
            });

            return null;
        }

        var bounds = new[]
        {
            (region.MinX, "minX"), (region.MaxX, "maxX"),
            (region.MinY, "minY"), (region.MaxY, "maxY"),
            (region.MinZ, "minZ"), (region.MaxZ, "maxZ"),
        };

        var si = new double[6];

        for (var k = 0; k < 6; k++)
        {
            var value = TryQuantity(
                bounds[k].Item1, $"{path}/region/{bounds[k].Item2}",
                Dimension.LengthDimension, p, errors);

            if (value is null)
            {
                return null;
            }

            si[k] = value.Value.In("m");
        }

        for (var axis = 0; axis < 3; axis++)
        {
            if (si[(2 * axis) + 1] > si[2 * axis])
            {
                continue;
            }

            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/region/{bounds[(2 * axis) + 1].Item2}",
                Constraint = $"a region's upper bound must exceed its lower one, but "
                    + $"{bounds[(2 * axis) + 1].Item2} is {si[(2 * axis) + 1]:G6} m against "
                    + $"{bounds[2 * axis].Item2} at {si[2 * axis]:G6} m",
                Observed = new ObservedValue(si[(2 * axis) + 1], "m"),
                Suggestion = "a region with no extent silences the element everywhere, "
                    + "which is what removing the element does more clearly",
            });

            return null;
        }

        var fringe = 0.0;

        if (region.Fringe is not null)
        {
            var value = TryQuantity(region.Fringe, $"{path}/region/fringe", Dimension.LengthDimension, p, errors);

            if (value is null)
            {
                return null;
            }

            fringe = value.Value.In("m");
            var smallest = Math.Min(si[1] - si[0], Math.Min(si[3] - si[2], si[5] - si[4]));

            if (fringe < 0.0 || fringe > 0.5 * smallest)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/region/fringe",
                    Constraint = $"a region's fringe must lie between zero and half its smallest extent, "
                        + $"{0.5 * smallest:G6} m here, or the element never reaches full strength anywhere",
                    Observed = new ObservedValue(fringe, "m"),
                    Suggestion = "a fringe of about a bore radius stands in for a real electrode's "
                        + "field decaying at its end; omit it for a hard edge",
                });

                return null;
            }
        }

        return new FieldRegion(si[0], si[1], si[2], si[3], si[4], si[5], fringe);
    }

    /// <summary>Whether a kind needs whole compiled copies to follow a phase.</summary>
    /// <remarks>
    /// The solved kinds do not: their phases live in their own <c>Stages</c>, already
    /// compiled against the same timeline, and compiling the geometry again per phase
    /// would solve every field twice over.
    /// </remarks>
    private static bool NeedsPhases(string? type) =>
        type is "uniform" or "halfSpaceUniform" or "idealQuadrupoleRf"
            or "quadroLogarithmic";

    /// <summary>Whether two compilations of one analytic element hold the same numbers.</summary>
    private static bool Same(CompiledField a, CompiledField b) =>
        a.Kind == b.Kind
        && a.Field == b.Field
        && a.PlanePoint == b.PlanePoint
        && a.InwardNormal == b.InwardNormal
        && a.PotentialGradientSi.Equals(b.PotentialGradientSi)
        && a.TurningDepthSi.Equals(b.TurningDepthSi)
        && a.DirectPotentialSi.Equals(b.DirectPotentialSi)
        && a.DriveAmplitudeSi.Equals(b.DriveAmplitudeSi)
        && a.DriveFrequencySi.Equals(b.DriveFrequencySi)
        && a.InscribedRadiusSi.Equals(b.InscribedRadiusSi)
        && a.Axis == b.Axis
        && a.CurvatureSi.Equals(b.CurvatureSi)
        && a.CharacteristicRadiusSi.Equals(b.CharacteristicRadiusSi)
        && a.Centre == b.Centre;

    private static CompiledField? CompileOnce(
        FieldDocument field,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        IReadOnlyList<PhaseSurface> timeline,
        List<EinzelError> errors,
        string? directory)
    {
        switch (field.Type)
        {
            case "fieldFree":
                return new CompiledField { Kind = CompiledFieldKind.FieldFree };

            case "uniform":
            {
                var vector = TryVector(field.Field, $"{path}/field", Dimension.ElectricField, p, errors);
                return vector is null ? null : new CompiledField
                {
                    Kind = CompiledFieldKind.Uniform,
                    Field = vector.Value,
                };
            }

            case "quadroLogarithmic":
            {
                var curvature = TryQuantity(
                    field.Curvature, $"{path}/curvature",
                    Dimension.ElectricFieldGradient, p, errors);

                var characteristic = TryQuantity(
                    field.CharacteristicRadius, $"{path}/characteristicRadius",
                    Dimension.LengthDimension, p, errors);

                var centre = field.Centre is null
                    ? Vec3.Zero
                    : TryVector(field.Centre, $"{path}/centre", Dimension.LengthDimension, p, errors)
                        ?? Vec3.Zero;

                if (curvature is null || characteristic is null)
                {
                    return null;
                }

                // Both refused rather than defaulted. A zero curvature is no axial well
                // at all - the frequency this field exists to define would be zero - and
                // a zero characteristic radius puts the logarithm's singularity nowhere.
                foreach (var (value, where, what) in new[]
                {
                    (curvature.Value.In("V/m^2"), $"{path}/curvature", "a curvature"),
                    (characteristic.Value.In("m"), $"{path}/characteristicRadius",
                        "a characteristic radius"),
                })
                {
                    if (!(value > 0.0))
                    {
                        errors.Add(new EinzelError
                        {
                            Code = ErrorCodes.ValueOutOfBounds,
                            Path = where,
                            Constraint = $"{what} must be positive",
                            Observed = new ObservedValue(value, "SI"),
                            Suggestion = "an orbital well needs both; use a static field "
                                + "element if nothing is meant to oscillate",
                        });

                        return null;
                    }
                }

                return new CompiledField
                {
                    Kind = CompiledFieldKind.QuadroLogarithmic,
                    CurvatureSi = curvature.Value.In("V/m^2"),
                    CharacteristicRadiusSi = characteristic.Value.In("m"),
                    Centre = centre,
                };
            }

            case "idealQuadrupoleRf":
            {
                var direct = TryQuantity(
                    field.DirectPotential, $"{path}/directPotential",
                    Dimension.ElectricPotential, p, errors);

                var amplitude = TryQuantity(
                    field.DriveAmplitude, $"{path}/driveAmplitude",
                    Dimension.ElectricPotential, p, errors);

                var frequency = TryQuantity(
                    field.DriveFrequency, $"{path}/driveFrequency",
                    Dimension.Frequency, p, errors);

                var inscribed = TryQuantity(
                    field.InscribedRadius, $"{path}/inscribedRadius",
                    Dimension.LengthDimension, p, errors);

                if (direct is null || amplitude is null || frequency is null || inscribed is null)
                {
                    return null;
                }

                // Both refused rather than defaulted. A zero radius is a division and a
                // zero frequency is a static field wearing a drive's clothes - and the
                // second is the one that would run, quietly, giving a quadrupole with no
                // RF and no complaint.
                foreach (var (value, where, what) in new[]
                {
                    (inscribed.Value.In("m"), $"{path}/inscribedRadius", "an inscribed radius"),
                    (frequency.Value.In("Hz"), $"{path}/driveFrequency", "a drive frequency"),
                })
                {
                    if (!(value > 0.0))
                    {
                        errors.Add(new EinzelError
                        {
                            Code = ErrorCodes.ValueOutOfBounds,
                            Path = where,
                            Constraint = $"{what} must be positive",
                            Observed = new ObservedValue(value, "SI"),
                            Suggestion = "use a static field element if nothing is driven",
                        });

                        return null;
                    }
                }

                // The axis the quadrupole is invariant along; z unless declared, so every
                // document written before the attribute existed compiles to the same field.
                var axis = field.Axis?.ToLowerInvariant() switch
                {
                    null or "z" => CylinderAxis.Z,
                    "x" => CylinderAxis.X,
                    "y" => CylinderAxis.Y,
                    _ => (CylinderAxis?)null,
                };

                if (axis is null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/axis",
                        Constraint = "an axis must be 'x', 'y' or 'z': the axis the quadrupole "
                            + "is invariant along, the transverse plane being the other two",
                        Observed = new ObservedValue(0.0, field.Axis ?? "(none)"),
                        Suggestion = "'z' when omitted, which puts the quadrupole across x-y; "
                            + "an axisymmetric tunnel solved in the half-plane has its axis "
                            + "along x and wants 'x'",
                    });

                    return null;
                }

                return new CompiledField
                {
                    Kind = CompiledFieldKind.IdealQuadrupoleRf,
                    DirectPotentialSi = direct.Value.In("V"),
                    DriveAmplitudeSi = amplitude.Value.In("V"),
                    DriveFrequencySi = frequency.Value.In("Hz"),
                    InscribedRadiusSi = inscribed.Value.In("m"),
                    Axis = axis.Value,
                };
            }

            case "halfSpaceUniform":
            {
                var point = TryVector(field.PlanePoint, $"{path}/planePoint", Dimension.LengthDimension, p, errors);
                var normal = TryDirection(field.InwardNormal, $"{path}/inwardNormal", errors);
                var cap = TryQuantity(field.CapPotential, $"{path}/capPotential", Dimension.ElectricPotential, p, errors);
                var depth = TryQuantity(field.TurningDepth, $"{path}/turningDepth", Dimension.LengthDimension, p, errors);

                if (point is null || normal is null || cap is null || depth is null)
                {
                    return null;
                }

                if (depth.Value.SiValue <= 0.0)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/turningDepth",
                        Constraint = "the turning depth must be positive",
                        Observed = new ObservedValue(field.TurningDepth!.Value, field.TurningDepth.Unit),
                        Suggestion = "supply a positive depth, for example {\"value\": 50, \"unit\": \"mm\"}",
                    });
                    return null;
                }

                return new CompiledField
                {
                    Kind = CompiledFieldKind.HalfSpaceUniform,
                    PlanePoint = point.Value,
                    InwardNormal = normal.Value,
                    PotentialGradientSi = cap.Value.SiValue / depth.Value.SiValue,
                    TurningDepthSi = depth.Value.SiValue,
                };
            }

            case "solved2d":
                return CompileSolvedField(field.Solve, $"{path}/solve", p, timeline, errors, directory);

            case "solved3d":
                return CompileSolved3D(field.Solve3d, $"{path}/solve3d", p, timeline, errors, directory);

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/type",
                    Constraint =
                        "a field element must declare one of: fieldFree, uniform, halfSpaceUniform, "
                        + "idealQuadrupoleRf, quadroLogarithmic, solved2d, solved3d",
                    Observed = new ObservedValue(0.0, field.Type ?? "null"),
                    Suggestion = "use \"halfSpaceUniform\" for an ideal single-stage ion mirror",
                });
                return null;
        }
    }

    /// <summary>
    /// Compiles the stages a solve is operated through, checking that they only
    /// change what a sequence is allowed to change.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A stage sets parameters, and parameters reach everything - which is what
    /// makes them the right vocabulary and also what makes a check necessary. Move
    /// a plate between stages and the mask changes, so every stage would need its
    /// own basis solves and its own grid; the field would still be computed, and it
    /// would be wrong in a way nothing else would catch.
    /// </para>
    /// <para>
    /// So the rule is exact and stated: a stage may change what an electrode
    /// <em>holds</em> - its potential, its drive amplitude, its phase - and nothing
    /// about where it is. Anything else is refused, naming the electrode and the
    /// stage.
    /// </para>
    /// </remarks>
    /// <summary>
    /// An empty surface, for values that are literals rather than expressions.
    /// </summary>
    /// <remarks>
    /// A stage duration is a wall-clock time, not a design dimension, so it does
    /// not name parameters - and giving it the surface would let it name one that
    /// the stage itself is about to change.
    /// </remarks>
    private static readonly Dictionary<string, Quantity> NoParameters = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, QuantityValue> NoOverrides = new(StringComparer.Ordinal);

    /// <summary>
    /// One phase of the instrument's timeline: what it is called, how long it lasts,
    /// and the parameter surface that holds during it.
    /// </summary>
    /// <param name="Name">What the phase is for.</param>
    /// <param name="DurationSeconds">How long it lasts.</param>
    /// <param name="Surface">Every parameter, as it stands during the phase.</param>
    /// <param name="Path">Where it was declared, for reporting.</param>
    /// <param name="Mode">The transport mode it names, or null to keep the model's.</param>
    /// <param name="EndSurface">Where the ramped parameters end, when the phase ramps; null when it holds.</param>
    /// <param name="MidSurface">The surface midway through a ramp, for the linearity check; null when the phase holds.</param>
    /// <param name="Ramps">The parameters the phase declares a ramp on, with their start and end values; null when it holds.</param>
    /// <remarks>
    /// <b>The surface is resolved once, for the whole instrument.</b> That is the fix for
    /// the defect this replaced: stages used to be compiled per element, so a stage
    /// setting a model parameter moved only its own element's electrodes and left every
    /// other element at its baseline - two electrodes written as the same expression
    /// holding 900 V and 300 V, on a model that validated cleanly.
    /// </remarks>
    internal sealed record PhaseSurface(
        string Name,
        double DurationSeconds,
        IReadOnlyDictionary<string, Quantity> Surface,
        string Path,
        string? Mode,
        IReadOnlyDictionary<string, Quantity>? EndSurface = null,
        IReadOnlyDictionary<string, Quantity>? MidSurface = null,
        IReadOnlyList<CompiledRamp>? Ramps = null);

    /// <summary>
    /// The instrument's timeline, resolved once, from wherever it is declared.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 9 says an instrument is a timed state machine, and the emphasis is on
    /// <i>instrument</i>: a phase holds across the whole model, not across one electrode
    /// assembly. So this is gathered before any element is compiled and handed to all of
    /// them, which is what makes a stage's parameter reach every expression written over
    /// it.
    /// </para>
    /// <para>
    /// Errors are reported here rather than per element. A stage whose set is malformed is
    /// one mistake in the document, and reporting it once per field element would turn a
    /// single typo into a wall of identical complaints.
    /// </para>
    /// </remarks>
    internal static List<PhaseSurface> Timeline(
        ModelDocument document,
        StageResolver restage,
        List<EinzelError> errors)
    {
        var phases = new List<PhaseSurface>();
        var declared = Declared(document, errors, out var path);

        if (declared is null)
        {
            return phases;
        }

        for (var k = 0; k < declared.Count; k++)
        {
            var stage = declared[k];
            var stagePath = $"{path}/{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            var name = stage.Name ?? $"stage {k}";

            var duration = TryQuantity(
                stage.Duration,
                $"{stagePath}/duration",
                Dimension.TimeDimension,
                NoParameters,
                errors);

            if (duration is null || duration.Value.SiValue <= 0.0)
            {
                if (duration is not null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{stagePath}/duration",
                        Constraint = "a stage must last a positive time",
                        Observed = new ObservedValue(duration.Value.SiValue, "s"),
                        Suggestion = "give the stage a duration, for example {\"value\": 100, \"unit\": \"us\"}",
                    });
                }

                continue;
            }

            var set = new Dictionary<string, Quantity>(StringComparer.Ordinal);

            foreach (var (parameter, value) in stage.Set ?? NoOverrides)
            {
                // Refused rather than read as its absent literal, which is what
                // happened: only Value was consulted, so an expression here resolved
                // silently to zero and a stage that was supposed to apply a kilovolt
                // applied nothing. The model still validated, still solved, and the run
                // reported an ion that never moved.
                //
                // Refused rather than supported, because what a stage set should mean
                // when it is an expression is a design question - the surface it would
                // evaluate against is the one the stage is in the middle of changing.
                if (value.Expression is not null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{stagePath}/set/{parameter}",
                        Constraint = "a stage sets a parameter to a value, not to an expression",
                        Suggestion = "write the number and its unit. An expression here would "
                            + "have to be evaluated against the parameter surface the stage is "
                            + "itself changing, and what that should mean is not settled",
                    });

                    continue;
                }

                try
                {
                    set[parameter] = Quantity.From(value.Value, value.Unit);
                }
                catch (EinzelException failure)
                {
                    errors.Add(failure.Error with { Path = $"{stagePath}/set/{parameter}" });
                }
            }

            var surface = restage(set, errors);

            if (surface is null)
            {
                continue;
            }

            // A ramp names where each parameter ends; where it starts is whatever the
            // start surface says, whether the phase set it or inherited it. The end
            // and midpoint surfaces are resolved here so the derived parameters follow,
            // exactly as they do for a set.
            IReadOnlyDictionary<string, Quantity>? endSurface = null;
            IReadOnlyDictionary<string, Quantity>? midSurface = null;
            List<CompiledRamp>? ramps = null;
            if (stage.Ramp is { Count: > 0 } ramp)
            {
                var endSet = new Dictionary<string, Quantity>(set, StringComparer.Ordinal);
                var midSet = new Dictionary<string, Quantity>(set, StringComparer.Ordinal);
                ramps = new List<CompiledRamp>(ramp.Count);
                var rampOk = true;
                foreach (var (parameter, value) in ramp)
                {
                    if (value.Expression is not null)
                    {
                        errors.Add(new EinzelError
                        {
                            Code = ErrorCodes.SchemaInvalid,
                            Path = $"{stagePath}/ramp/{parameter}",
                            Constraint = "a ramp ends a parameter at a value, not at an expression",
                            Suggestion = "write the number and its unit, as a set does",
                        });
                        rampOk = false;
                        continue;
                    }
                    if (!surface.TryGetValue(parameter, out var start))
                    {
                        errors.Add(new EinzelError
                        {
                            Code = ErrorCodes.SchemaInvalid,
                            Path = $"{stagePath}/ramp/{parameter}",
                            Constraint = $"'{parameter}' is not a declared parameter, so there is nothing to ramp",
                            Observed = new ObservedValue(0.0, parameter),
                            Suggestion = "ramp a parameter the document declares; a derived parameter follows the ones it is written over",
                        });
                        rampOk = false;
                        continue;
                    }
                    try
                    {
                        var end = Quantity.From(value.Value, value.Unit);
                        if (end.Dimension != start.Dimension)
                        {
                            errors.Add(new EinzelError
                            {
                                Code = ErrorCodes.SchemaInvalid,
                                Path = $"{stagePath}/ramp/{parameter}",
                                Constraint = $"'{parameter}' has dimension {start.Dimension} and the ramp's end value has {end.Dimension}",
                                Suggestion = "give the end value in the parameter's own unit",
                            });
                            rampOk = false;
                            continue;
                        }
                        endSet[parameter] = end;
                        midSet[parameter] = Quantity.Si(0.5 * (start.SiValue + end.SiValue), start.Dimension);

                        // Recorded by the name the document used, in declaration order, so a
                        // consumer reading the schedule can tell a ramped parameter from a
                        // derived one that merely moved with it.
                        ramps.Add(new CompiledRamp(parameter, start, end));
                    }
                    catch (EinzelException failure)
                    {
                        errors.Add(failure.Error with { Path = $"{stagePath}/ramp/{parameter}" });
                        rampOk = false;
                    }
                }
                if (!rampOk)
                {
                    continue;
                }
                endSurface = restage(endSet, errors);
                midSurface = restage(midSet, errors);
                if (endSurface is null || midSurface is null)
                {
                    continue;
                }
            }

            if (stage.Mode is not null and not ("trajectory" or "diffusion"))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{stagePath}/mode",
                    Constraint = "a transport mode is one of 'trajectory' or 'diffusion'",
                    Observed = new ObservedValue(0.0, stage.Mode),
                    Suggestion = "omit it to keep the model's own transport mode, which is "
                        + "what a phase does with anything it does not name",
                });

                continue;
            }

            phases.Add(new PhaseSurface(
                name, duration.Value.SiValue, surface, stagePath, stage.Mode, endSurface, midSurface, ramps));
        }

        return phases;
    }

    /// <summary>Where the timeline is declared, and a refusal if it is in two places.</summary>
    /// <remarks>
    /// <para>
    /// <c>sequence</c> on the model is the spelling that says what it means - the
    /// timeline belongs to the instrument. <c>stages</c> on a solve is the older one and
    /// still works, because it is what the shipped sequenced example is written in and
    /// because a single-element model has no ambiguity to resolve.
    /// </para>
    /// <para>
    /// <b>Declaring both is refused rather than merged</b>, and two elements each
    /// declaring stages likewise. A document that says the instrument has one timeline
    /// and also another is not a document with a default to fall back on - the same
    /// argument that refuses a geometry declaring both <c>drive</c> and <c>drives</c>.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<StageDocument>? Declared(
        ModelDocument document, List<EinzelError> errors, out string path)
    {
        path = "/sequence";

        var staged = new List<int>();
        var fields = document.Fields ?? [];

        for (var i = 0; i < fields.Count; i++)
        {
            if (fields[i].Solve?.Stages is { Count: > 0 }
                || fields[i].Solve3d?.Stages is { Count: > 0 })
            {
                staged.Add(i);
            }
        }

        if (document.Sequence is { Count: 0 })
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "the model declares a sequence with no phases in it",
                Suggestion = "give the sequence at least one phase with a duration, or "
                    + "remove it. An empty timeline reads exactly like no timeline, and a "
                    + "generator that filtered every phase out should not look the same as "
                    + "a document that never had one",
            });

            return null;
        }

        var sequence = document.Sequence is { Count: > 0 } ? document.Sequence : null;

        if (sequence is not null && staged.Count > 0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "the model declares a sequence and element "
                    + $"{staged[0]} also declares stages",
                Suggestion = "an instrument has one timeline. Keep the model's "
                    + "\"sequence\", or the element's \"stages\", not both",
            });

            return null;
        }

        if (sequence is not null)
        {
            return sequence;
        }

        if (staged.Count == 0)
        {
            return null;
        }

        if (staged.Count > 1)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"/fields/{staged[1]}/solve/stages",
                Constraint = "more than one element declares stages, and an instrument "
                    + $"has one timeline: elements {string.Join(", ", staged)} each declare "
                    + "them",
                Suggestion = "move the timeline to the model's \"sequence\", which is where "
                    + "it belongs when more than one element is involved. Two timelines over "
                    + "the same parameters would each switch at their own instants, and the "
                    + "document would say two things about what the instrument is doing",
            });

            return null;
        }

        var only = staged[0];

        // Whichever actually has phases, not whichever is non-null. An element carrying
        // both a solve and a solve3d - with an explicitly empty "stages": [] on one of
        // them - would otherwise return the empty list through the ?? and drop the real
        // timeline silently, since an empty list is not null.
        if (fields[only].Solve3d?.Stages is { Count: > 0 } volume)
        {
            path = $"/fields/{only}/solve3d/stages";

            return volume;
        }

        path = $"/fields/{only}/solve/stages";

        return fields[only].Solve?.Stages;
    }

    /// <summary>The timeline as the run sees it: durations, modes, and instants.</summary>
    /// <remarks>
    /// Cumulative, because what the integrator needs is when each phase <em>ends</em>
    /// rather than how long it lasts, and computing that once here keeps every consumer
    /// from accumulating it again and rounding differently.
    /// </remarks>
    private static List<CompiledPhase> Schedule(
        List<PhaseSurface> timeline, string modelMode)
    {
        var phases = new List<CompiledPhase>(timeline.Count);
        var elapsed = 0.0;

        foreach (var phase in timeline)
        {
            elapsed += phase.DurationSeconds;

            phases.Add(new CompiledPhase(
                phase.Name, phase.DurationSeconds, phase.Mode ?? modelMode, elapsed,
                phase.Ramps ?? []));
        }

        return phases;
    }

    /// <summary>Where an element declares its stages, for an error path (AGT-3).</summary>
    private static string StagePath(FieldDocument field, int index) =>
        field.Solve3d?.Stages is { Count: > 0 }
            ? $"/fields/{index}/solve3d/stages"
            : $"/fields/{index}/solve/stages";

    /// <summary>This element's electrodes, as they stand during each phase.</summary>
    /// <remarks>
    /// Every element gets the same timeline, so a stage's parameter reaches every
    /// expression written over it rather than only those in the element that happened to
    /// declare the stage.
    /// </remarks>
    private static List<CompiledStage> CompileStages(
        SolvedFieldDocument solve,
        IReadOnlyList<PhaseSurface> timeline,
        IReadOnlyList<CompiledElectrode> baseline,
        IReadOnlyList<CompiledDrive> drives,
        List<EinzelError> errors)
    {
        var stages = new List<CompiledStage>();

        if (timeline.Count == 0 || solve.Electrodes is not { } declaredElectrodes)
        {
            return stages;
        }

        foreach (var phase in timeline)
        {
            var electrodes = new List<CompiledElectrode>();

            for (var i = 0; i < declaredElectrodes.Count; i++)
            {
                Expand(
                    declaredElectrodes[i], i, $"{phase.Path}/electrodes/{i}", drives,
                    phase.Surface, electrodes, errors);
            }

            if (!SameGeometry(baseline, electrodes, phase.Name, phase.Path, errors))
            {
                continue;
            }

            List<CompiledElectrode>? endElectrodes = null;
            if (phase.EndSurface is not null && phase.MidSurface is not null)
            {
                endElectrodes = [];
                var midElectrodes = new List<CompiledElectrode>();
                var ignored = new List<EinzelError>();
                for (var i = 0; i < declaredElectrodes.Count; i++)
                {
                    Expand(declaredElectrodes[i], i, $"{phase.Path}/electrodes/{i}", drives, phase.EndSurface, endElectrodes, ignored);
                    Expand(declaredElectrodes[i], i, $"{phase.Path}/electrodes/{i}", drives, phase.MidSurface, midElectrodes, ignored);
                }
                if (!SameGeometry(baseline, endElectrodes, phase.Name, phase.Path, errors)
                    || !RampIsLinear(Excitations(electrodes), Excitations(midElectrodes), Excitations(endElectrodes), phase.Name, phase.Path, errors))
                {
                    continue;
                }
            }

            stages.Add(new CompiledStage(phase.Name, phase.DurationSeconds, electrodes) { EndElectrodes = endElectrodes });
        }

        return stages;
    }

    /// <summary>
    /// Whether every electrode's potential and drive amplitude at the phase's midpoint is
    /// the mean of its start and end values - which is what makes interpolating the
    /// solved channel weights linearly in time exact - and no drive phase moves.
    /// </summary>
    /// <remarks>
    /// Checked rather than assumed because a potential is an expression over the ramped
    /// parameter and nothing stops it being its square root. A ramp of such a parameter
    /// would still run, with the field at every instant between the ends but on the
    /// wrong curve, and nothing in the result would say so.
    /// </remarks>
    private static List<(string Name, double Potential, IReadOnlyList<CompiledTap> Taps)> Excitations(List<CompiledElectrode> electrodes) =>
        [.. electrodes.Select(e => (e.Name, e.Potential, e.Taps))];

    private static List<(string Name, double Potential, IReadOnlyList<CompiledTap> Taps)> Excitations(List<CompiledElectrode3D> electrodes) =>
        [.. electrodes.Select(e => (e.Name, e.Potential, e.Taps))];

    private static bool RampIsLinear(
        List<(string Name, double Potential, IReadOnlyList<CompiledTap> Taps)> start,
        List<(string Name, double Potential, IReadOnlyList<CompiledTap> Taps)> mid,
        List<(string Name, double Potential, IReadOnlyList<CompiledTap> Taps)> end,
        string stage,
        string path,
        List<EinzelError> errors)
    {
        if (mid.Count != start.Count || end.Count != start.Count)
        {
            return true;   // SameGeometry has already complained
        }

        for (var i = 0; i < start.Count; i++)
        {
            var a = start[i];
            var m = mid[i];
            var b = end[i];

            static bool Linear(double s, double m, double e)
            {
                var scale = Math.Max(Math.Max(Math.Abs(s), Math.Abs(e)), 1e-12);
                return Math.Abs(m - (0.5 * (s + e))) <= 1e-9 * scale;
            }

            if (!Linear(a.Potential, m.Potential, b.Potential))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/ramp",
                    Constraint =
                        $"stage '{stage}' ramps a parameter that electrode '{a.Name}' depends on non-linearly: "
                        + $"its potential at the midpoint is {m.Potential:G6} V, not the {0.5 * (a.Potential + b.Potential):G6} V midway between its ends",
                    Observed = new ObservedValue(m.Potential, "V"),
                    Suggestion =
                        "a ramp is linear in time and exact only where the potentials are linear in the ramped "
                        + "parameter; write this curve as more phases, or ramp the quantity the potential is linear in",
                });
                return false;
            }

            if (a.Taps.Count != b.Taps.Count || a.Taps.Count != m.Taps.Count)
            {
                return true;   // a differing tap structure is caught by the drive validation
            }

            for (var k = 0; k < a.Taps.Count; k++)
            {
                if (a.Taps[k].Phase != b.Taps[k].Phase)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/ramp",
                        Constraint = $"stage '{stage}' moves the drive phase of electrode '{a.Name}' from {a.Taps[k].Phase:G6} to {b.Taps[k].Phase:G6}",
                        Observed = new ObservedValue(b.Taps[k].Phase, "cycles"),
                        Suggestion = "a ramped phase is a frequency shift, which a linear interpolation of amplitudes cannot express; ramp amplitudes and potentials only",
                    });
                    return false;
                }

                if (!Linear(a.Taps[k].Amplitude, m.Taps[k].Amplitude, b.Taps[k].Amplitude))
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/ramp",
                        Constraint =
                            $"stage '{stage}' ramps a parameter that the drive amplitude of electrode '{a.Name}' depends on non-linearly: "
                            + $"{m.Taps[k].Amplitude:G6} V at the midpoint against {0.5 * (a.Taps[k].Amplitude + b.Taps[k].Amplitude):G6} V midway between its ends",
                        Observed = new ObservedValue(m.Taps[k].Amplitude, "V"),
                        Suggestion = "write this curve as more phases, or ramp the quantity the amplitude is linear in",
                    });
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Whether two compilations put the same metal in the same places.</summary>
    private static bool SameGeometry(
        IReadOnlyList<CompiledElectrode> baseline,
        List<CompiledElectrode> staged,
        string stage,
        string path,
        List<EinzelError> errors)
    {
        if (baseline.Count != staged.Count)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/set",
                Constraint =
                    $"stage '{stage}' changes how many electrodes there are, from {baseline.Count} to {staged.Count}",
                Observed = new ObservedValue(staged.Count, "electrodes"),
                Suggestion = "a stage may change what an electrode holds, not whether it exists",
            });

            return false;
        }

        for (var i = 0; i < baseline.Count; i++)
        {
            var a = baseline[i];
            var b = staged[i];

            // An edge profile keeps its volts in its profile, and the channel decomposition
            // weights a solved pattern by a scalar - so a profile that differs between two
            // stages is two spatial patterns wearing one coefficient, and the second stage
            // would silently run the first one's field. Refused rather than approximated:
            // scaling every point by one factor WOULD be expressible, and is not detected
            // here, so the message names the whole restriction rather than half of it.
            if (!a.Profile.SequenceEqual(b.Profile))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/set",
                    Constraint =
                        $"stage '{stage}' changes the profile on edge electrode '{a.Name}', which a "
                        + "sequenced solve cannot express: a profile is a spatial pattern and a stage "
                        + "may only re-weight one",
                    Observed = new ObservedValue(b.Profile.Count, "profile points"),
                    Suggestion =
                        "hold the profile fixed and put what the stage changes on an ordinary "
                        + "electrode, whose potential a stage may set freely",
                });

                return false;
            }

            var moved = a.Shape != b.Shape
                || a.MinX != b.MinX || a.MaxX != b.MaxX
                || a.MinY != b.MinY || a.MaxY != b.MaxY
                || a.CentreX != b.CentreX || a.CentreY != b.CentreY || a.Radius != b.Radius
                || !a.Vertices.SequenceEqual(b.Vertices);

            if (moved)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/set",
                    Constraint =
                        $"stage '{stage}' moves electrode '{a.Name}', and a sequence may only change "
                        + "what an electrode holds, not where it is",
                    Observed = new ObservedValue(0.0, a.Name),
                    Suggestion =
                        "set only the parameters that reach potentials, amplitudes and phases; a stage that "
                        + "moves metal would need its own solve and its own grid",
                });

                return false;
            }
        }

        return true;
    }

    /// <summary>Compiles one declared 3D electrode into the electrodes it stands for.</summary>
    /// <remarks>
    /// <paramref name="position"/> is where the declaration sits in its solve's list, which is
    /// what an unnamed electrode is called after - see <see cref="ElectrodeName"/>.
    /// </remarks>
    private static void Expand3D(
        Electrode3DDocument declared,
        int position,
        string path,
        IReadOnlyList<CompiledDrive> drives,
        IReadOnlyDictionary<string, Quantity> p,
        List<CompiledElectrode3D> into,
        List<EinzelError> errors)
    {
        var name = ElectrodeName(declared.Name, position);

        if (declared.Repeat is not { } repeat)
        {
            var single = CompileElectrode3D(declared, name, path, drives, p, errors);

            if (single is not null)
            {
                into.Add(single);
            }

            return;
        }

        var count = TryQuantity(repeat.Count, $"{path}/repeat/count", Dimension.Dimensionless, p, errors);

        if (count is null)
        {
            return;
        }

        var copies = (int)Math.Round(count.Value.SiValue);

        if (copies < 1 || Math.Abs(count.Value.SiValue - copies) > 1e-9)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/repeat/count",
                Constraint = "a repeat count must be a whole number of at least one",
                Observed = new ObservedValue(count.Value.SiValue, "1"),
                Suggestion = "use a parameter that evaluates to a whole number, for example 3",
            });

            return;
        }

        var index = repeat.Index ?? "index";

        if (p.ContainsKey(index))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/repeat/index",
                Constraint = $"'{index}' is already a declared parameter, and binding it here would shadow it",
                Observed = new ObservedValue(0.0, index),
                Suggestion = "choose another index name, such as 'rod' or 'section'",
            });

            return;
        }

        for (var k = 0; k < copies; k++)
        {
            var scoped = new Dictionary<string, Quantity>(p, StringComparer.Ordinal)
            {
                [index] = Quantity.Si(k, Dimension.Dimensionless),
            };

            var copy = CompileElectrode3D(
                declared with { Repeat = null },
                $"{name}-{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"{path}/repeat/{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                drives,
                scoped,
                errors);

            if (copy is not null)
            {
                into.Add(copy);
            }
        }
    }

    /// <summary>Compiles one 3D electrode, its repeat already applied.</summary>
    /// <remarks>
    /// The name is handed in rather than read from the declaration, so that
    /// <see cref="ElectrodeName"/> is the one place an unnamed electrode is named.
    /// </remarks>
    private static CompiledElectrode3D? CompileElectrode3D(
        Electrode3DDocument electrode,
        string name,
        string path,
        IReadOnlyList<CompiledDrive> drives,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        var length = Dimension.LengthDimension;
        var volt = Dimension.ElectricPotential;

        var potential = TryQuantity(electrode.Potential, $"{path}/potential", volt, p, errors);

        if (potential is null)
        {
            return null;
        }

        var common = new CompiledElectrode3D
        {
            Name = name,
            Shape = Electrode3DShape.Box,
            Potential = potential.Value.SiValue,
            Taps = Taps(electrode, drives, path, p, errors),
        };

        switch (electrode.Shape)
        {
            case "box":
            {
                var minX = TryQuantity(electrode.MinX, $"{path}/minX", length, p, errors);
                var minY = TryQuantity(electrode.MinY, $"{path}/minY", length, p, errors);
                var minZ = TryQuantity(electrode.MinZ, $"{path}/minZ", length, p, errors);
                var maxX = TryQuantity(electrode.MaxX, $"{path}/maxX", length, p, errors);
                var maxY = TryQuantity(electrode.MaxY, $"{path}/maxY", length, p, errors);
                var maxZ = TryQuantity(electrode.MaxZ, $"{path}/maxZ", length, p, errors);

                if (minX is null || minY is null || minZ is null
                    || maxX is null || maxY is null || maxZ is null)
                {
                    return null;
                }

                if (minX.Value.SiValue > maxX.Value.SiValue
                    || minY.Value.SiValue > maxY.Value.SiValue
                    || minZ.Value.SiValue > maxZ.Value.SiValue)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = path,
                        Constraint =
                            $"box '{name}' is inverted, and an inverted box vanishes from the solve "
                            + "rather than failing",
                        Observed = new ObservedValue(maxX.Value.SiValue - minX.Value.SiValue, "m"),
                        Suggestion = "check the expressions that derive the bounds",
                    });

                    return null;
                }

                // Dimensionless, because a half turn is an angle expressed as a ratio and
                // the grammar has no unit for one - the same treatment a drive phase gets.
                var tilt = electrode.TiltHalfTurns is null
                    ? Quantity.Si(0.0, Dimension.Dimensionless)
                    : TryQuantity(
                        electrode.TiltHalfTurns, $"{path}/tiltHalfTurns",
                        Dimension.Dimensionless, p, errors);

                if (tilt is null)
                {
                    return null;
                }

                var tiltAxis = electrode.TiltAxis switch
                {
                    null or "z" => CylinderAxis.Z,
                    "x" => CylinderAxis.X,
                    "y" => CylinderAxis.Y,
                    _ => (CylinderAxis?)null,
                };

                if (tiltAxis is null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/tiltAxis",
                        Constraint = "a tilt axis must be 'x', 'y' or 'z'",
                        Observed = new ObservedValue(0.0, electrode.TiltAxis ?? "(none)"),
                        Suggestion =
                            "'z' when omitted; the tilt is about that axis through the box's "
                            + "own centre, in half turns",
                    });

                    return null;
                }

                // A tilt is stated as an angle, and an angle beyond a half turn describes a
                // box already describable the short way round. Refused rather than wrapped,
                // because a document meaning 0.05 and writing 5 has made a unit mistake -
                // and the whole point of half turns is that the unit is not guessable from
                // the number.
                if (Math.Abs(tilt.Value.SiValue) > 0.5)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/tiltHalfTurns",
                        Constraint =
                            $"box '{name}' declares a tilt of {tilt.Value.SiValue:G4} half "
                            + "turns, and every orientation is reachable within half a turn "
                            + "either way",
                        Observed = new ObservedValue(tilt.Value.SiValue, "1"),
                        Suggestion =
                            "half turns, not degrees or radians: 1.0 is half a turn, so a "
                            + "right angle is 0.5 and a 200 um convergence over 350 mm is "
                            + "1.8e-4",
                    });

                    return null;
                }

                return common with
                {
                    Shape = Electrode3DShape.Box,
                    MinX = minX.Value.SiValue,
                    MinY = minY.Value.SiValue,
                    MinZ = minZ.Value.SiValue,
                    MaxX = maxX.Value.SiValue,
                    MaxY = maxY.Value.SiValue,
                    MaxZ = maxZ.Value.SiValue,
                    TiltAxis = tiltAxis.Value,
                    TiltHalfTurns = tilt.Value.SiValue,
                };
            }

            case "sphere":
            case "cylinder":
            {
                // A TILT IS A BOX'S PROPERTY, AND IS REFUSED RATHER THAN DROPPED HERE.
                // The properties live on the shared electrode document, so a sphere or a
                // cylinder declaring one binds cleanly and the unrecognised-property
                // refusal never fires - leaving a document that validates, solves, and
                // answers a different question than the one it appears to ask. That is the
                // failure the strict key checking exists to prevent, reached through a
                // recognised key that is meaningless for this shape.
                if (electrode.TiltHalfTurns is not null || electrode.TiltAxis is not null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/{(electrode.TiltHalfTurns is not null ? "tiltHalfTurns" : "tiltAxis")}",
                        Constraint =
                            $"'{name}' is a {electrode.Shape} and only a box may be tilted",
                        Observed = new ObservedValue(0.0, electrode.Shape ?? "(none)"),
                        Suggestion =
                            "remove the tilt, or describe this electrode as a box - a sphere "
                            + "is unchanged by rotation and a cylinder is oriented by 'axis'",
                    });

                    return null;
                }

                var centreX = TryQuantity(electrode.CentreX, $"{path}/centreX", length, p, errors);
                var centreY = TryQuantity(electrode.CentreY, $"{path}/centreY", length, p, errors);
                var radius = TryQuantity(electrode.Radius, $"{path}/radius", length, p, errors);

                var centreZ = electrode.CentreZ is null
                    ? Quantity.Si(0.0, length)
                    : TryQuantity(electrode.CentreZ, $"{path}/centreZ", length, p, errors);

                if (centreX is null || centreY is null || centreZ is null || radius is null)
                {
                    return null;
                }

                if (radius.Value.SiValue <= 0.0)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/radius",
                        Constraint = $"'{name}' must have positive radius",
                        Observed = new ObservedValue(radius.Value.SiValue, "m"),
                        Suggestion = "supply a positive radius",
                    });

                    return null;
                }

                if (electrode.Shape == "sphere")
                {
                    return common with
                    {
                        Shape = Electrode3DShape.Sphere,
                        CentreX = centreX.Value.SiValue,
                        CentreY = centreY.Value.SiValue,
                        CentreZ = centreZ.Value.SiValue,
                        Radius = radius.Value.SiValue,
                    };
                }

                var lower = TryQuantity(electrode.Lower, $"{path}/lower", length, p, errors);
                var upper = TryQuantity(electrode.Upper, $"{path}/upper", length, p, errors);

                if (lower is null || upper is null)
                {
                    return null;
                }

                var axis = electrode.Axis switch
                {
                    null or "z" => CylinderAxis.Z,
                    "x" => CylinderAxis.X,
                    "y" => CylinderAxis.Y,
                    _ => (CylinderAxis?)null,
                };

                if (axis is null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/axis",
                        Constraint = "a cylinder axis must be 'x', 'y' or 'z'",
                        Observed = new ObservedValue(0.0, electrode.Axis ?? "(none)"),
                        Suggestion = "'z' when omitted; the centre is then given as centreX and centreY",
                    });

                    return null;
                }

                if (upper.Value.SiValue <= lower.Value.SiValue)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = path,
                        Constraint = $"cylinder '{name}' must have upper above lower along its axis",
                        Observed = new ObservedValue(upper.Value.SiValue - lower.Value.SiValue, "m"),
                        Suggestion = "check the expressions that derive the ends",
                    });

                    return null;
                }

                return common with
                {
                    Shape = Electrode3DShape.Cylinder,
                    CentreX = centreX.Value.SiValue,
                    CentreY = centreY.Value.SiValue,
                    CentreZ = centreZ.Value.SiValue,
                    Radius = radius.Value.SiValue,
                    Axis = axis.Value,
                    Lower = lower.Value.SiValue,
                    Upper = upper.Value.SiValue,
                };
            }

            case "prism":
            {
                if (electrode.TiltHalfTurns is not null || electrode.TiltAxis is not null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/{(electrode.TiltHalfTurns is not null ? "tiltHalfTurns" : "tiltAxis")}",
                        Constraint = $"'{name}' is a prism and only a box may be tilted",
                        Observed = new ObservedValue(0.0, "prism"),
                        Suggestion = "a prism is oriented by 'axis' and shaped by its vertices; write the tilt into the outline",
                    });
                    return null;
                }

                var vertices = CompileVertices(electrode.Vertices, name, path, p, errors);
                var lower = TryQuantity(electrode.Lower, $"{path}/lower", length, p, errors);
                var upper = TryQuantity(electrode.Upper, $"{path}/upper", length, p, errors);

                if (vertices is null || lower is null || upper is null)
                {
                    return null;
                }

                var axis = electrode.Axis switch
                {
                    null or "z" => CylinderAxis.Z,
                    "x" => CylinderAxis.X,
                    "y" => CylinderAxis.Y,
                    _ => (CylinderAxis?)null,
                };

                if (axis is null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/axis",
                        Constraint = "a prism axis must be 'x', 'y' or 'z'",
                        Observed = new ObservedValue(0.0, electrode.Axis ?? "(none)"),
                        Suggestion = "'z' when omitted; the vertices are then (x, y)",
                    });
                    return null;
                }

                if (upper.Value.SiValue <= lower.Value.SiValue)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = path,
                        Constraint = $"prism '{name}' must have upper above lower along its axis",
                        Observed = new ObservedValue(upper.Value.SiValue - lower.Value.SiValue, "m"),
                        Suggestion = "check the expressions that derive the ends",
                    });
                    return null;
                }

                return common with
                {
                    Shape = Electrode3DShape.Prism,
                    Vertices = vertices,
                    Axis = axis.Value,
                    Lower = lower.Value.SiValue,
                    Upper = upper.Value.SiValue,
                };
            }

            case "revolve":
            {
                if (electrode.TiltHalfTurns is not null || electrode.TiltAxis is not null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/{(electrode.TiltHalfTurns is not null ? "tiltHalfTurns" : "tiltAxis")}",
                        Constraint = $"'{name}' is a revolve and only a box may be tilted",
                        Observed = new ObservedValue(0.0, "revolve"),
                        Suggestion =
                            "a revolve is oriented by 'axis' and swept by 'fromHalfTurns' and 'toHalfTurns'",
                    });

                    return null;
                }

                if (electrode.Lower is not null || electrode.Upper is not null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/{(electrode.Lower is not null ? "lower" : "upper")}",
                        Constraint =
                            $"'{name}' is a revolve, which is swept through an angle rather than extruded along a length",
                        Observed = new ObservedValue(0.0, "revolve"),
                        Suggestion =
                            "use 'fromHalfTurns' and 'toHalfTurns'; a profile extruded along a line is a 'prism'",
                    });

                    return null;
                }

                var vertices = CompileVertices(electrode.Vertices, name, path, p, errors);

                // Dimensionless, because a half turn is an angle expressed as a ratio - the
                // same reading the box tilt takes.
                var from = electrode.FromHalfTurns is null
                    ? Quantity.Si(0.0, Dimension.Dimensionless)
                    : TryQuantity(
                        electrode.FromHalfTurns, $"{path}/fromHalfTurns",
                        Dimension.Dimensionless, p, errors);

                var to = electrode.ToHalfTurns is null
                    ? Quantity.Si(2.0, Dimension.Dimensionless)
                    : TryQuantity(
                        electrode.ToHalfTurns, $"{path}/toHalfTurns",
                        Dimension.Dimensionless, p, errors);

                if (vertices is null || from is null || to is null)
                {
                    return null;
                }

                var axis = electrode.Axis switch
                {
                    null or "z" => CylinderAxis.Z,
                    "x" => CylinderAxis.X,
                    "y" => CylinderAxis.Y,
                    _ => (CylinderAxis?)null,
                };

                if (axis is null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/axis",
                        Constraint = "a revolve axis must be 'x', 'y' or 'z'",
                        Observed = new ObservedValue(0.0, electrode.Axis ?? "(none)"),
                        Suggestion = "'z' when omitted; the vertices are then (radius, z)",
                    });

                    return null;
                }

                // A PROFILE MAY NOT CROSS ITS OWN AXIS. Revolving a vertex at a negative
                // radius sweeps it back through the solid the positive side already made, so
                // the result overlaps itself and no signed distance describes it - the
                // surface would be inside the body. Refused rather than folded to the
                // absolute value, because a profile written across the axis is a mistake
                // about what the coordinates mean rather than a shape somebody wanted.
                if (vertices.Any(v => v.X < 0.0))
                {
                    var worst = vertices.Min(v => v.X);

                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/vertices",
                        Constraint =
                            $"revolve '{name}' has a vertex at a negative radius, and a profile revolved "
                            + "across its own axis sweeps a solid that overlaps itself",
                        Observed = new ObservedValue(worst, "m"),
                        Suggestion =
                            "the first vertex coordinate is the distance from the axis and the second the "
                            + "position along it; a profile straddling the axis is two electrodes, or a prism",
                    });

                    return null;
                }

                var sweep = Math.Abs(to.Value.SiValue - from.Value.SiValue);

                if (sweep <= 0.0 || sweep > 2.0)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = path,
                        Constraint =
                            $"revolve '{name}' must sweep more than nothing and no more than a full turn",
                        Observed = new ObservedValue(sweep, "half turns"),
                        Suggestion =
                            "two half turns is the whole ring; past that the solid would wrap over itself",
                    });

                    return null;
                }

                return common with
                {
                    Shape = Electrode3DShape.Revolve,
                    Vertices = vertices,
                    Axis = axis.Value,
                    FromHalfTurns = Math.Min(from.Value.SiValue, to.Value.SiValue),
                    ToHalfTurns = Math.Max(from.Value.SiValue, to.Value.SiValue),
                };
            }

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/shape",
                    Constraint =
                        "an electrode shape must be 'box', 'sphere', 'cylinder', 'prism' or 'revolve'",
                    Observed = new ObservedValue(0.0, electrode.Shape ?? "(none)"),
                    Suggestion =
                        "a box is a plate or a housing, a cylinder is a rod or a tube, a sphere is a bead, "
                        + "a prism is any outline given a length, a revolve is one given a turn",
                });

                return null;
        }
    }

    private static CompiledField? CompileSolved3D(
        SolvedField3DDocument? solve,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        IReadOnlyList<PhaseSurface> timeline,
        List<EinzelError> errors,
        string? directory)
    {
        if (solve is null)
        {
            errors.Add(Missing(path, "a solved3d field needs a solve3d block",
                "give it a box, a cell size, and at least one electrode"));
            return null;
        }

        var length = Dimension.LengthDimension;

        var minX = TryQuantity(solve.MinX, $"{path}/minX", length, p, errors);
        var minY = TryQuantity(solve.MinY, $"{path}/minY", length, p, errors);
        var minZ = TryQuantity(solve.MinZ, $"{path}/minZ", length, p, errors);
        var maxX = TryQuantity(solve.MaxX, $"{path}/maxX", length, p, errors);
        var maxY = TryQuantity(solve.MaxY, $"{path}/maxY", length, p, errors);
        var maxZ = TryQuantity(solve.MaxZ, $"{path}/maxZ", length, p, errors);
        var cell = TryQuantity(solve.CellSize, $"{path}/cellSize", length, p, errors);

        if (minX is null || minY is null || minZ is null
            || maxX is null || maxY is null || maxZ is null || cell is null)
        {
            return null;
        }

        // FINITE FIRST. A literal is refused as non-finite when it is read, but a derived
        // bound or cell size is an expression's result, and the square root of a negative
        // ratio or a division by a parameter set to zero gives NaN or infinity. Every
        // comparison with NaN is false, so `max <= min` and `cell <= 0` below both let it
        // through - and the mesh arithmetic then threw from inside validation, which the CLI
        // can only report as a defect in the engine. An infinite cell size was quieter and
        // worse: it validated and solved on a three-node mesh.
        var nonFinite = false;

        foreach (var (name, bound) in new[]
        {
            ("minX", minX.Value), ("maxX", maxX.Value), ("minY", minY.Value),
            ("maxY", maxY.Value), ("minZ", minZ.Value), ("maxZ", maxZ.Value), ("cellSize", cell.Value),
        })
        {
            if (!double.IsFinite(bound.SiValue))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/{name}",
                    Constraint = "a solve domain bound and its cell size must be finite numbers",
                    Observed = new ObservedValue(bound.SiValue, "m"),
                    Suggestion =
                        "check the expression it is computed from: a division by a parameter set to "
                        + "zero, or the square root of a negative ratio, gives no number at all",
                });

                nonFinite = true;
            }
        }

        if (nonFinite)
        {
            return null;
        }

        // Finite bounds can still be an infinite extent apart - a subtraction of two very
        // large numbers of opposite sign overflows - and an infinite span is no more a mesh
        // than an infinite bound is.
        if (maxX.Value.SiValue <= minX.Value.SiValue
            || maxY.Value.SiValue <= minY.Value.SiValue
            || maxZ.Value.SiValue <= minZ.Value.SiValue
            || !double.IsFinite(maxX.Value.SiValue - minX.Value.SiValue)
            || !double.IsFinite(maxY.Value.SiValue - minY.Value.SiValue)
            || !double.IsFinite(maxZ.Value.SiValue - minZ.Value.SiValue))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "a solve domain must have positive, finite extent on every axis",
                Observed = new ObservedValue(maxX.Value.SiValue - minX.Value.SiValue, "m"),
                Suggestion = "check that each max exceeds its min",
            });

            return null;
        }

        if (cell.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/cellSize",
                Constraint = "a cell size must be positive",
                Observed = new ObservedValue(cell.Value.SiValue, "m"),
                Suggestion = "supply a positive spacing, for example {\"value\": 0.2, \"unit\": \"mm\"}",
            });

            return null;
        }

        // THE MESH IS ARITHMETIC ON THE DOCUMENT, so a mesh too large to solve is refused
        // here rather than discovered by the solver. It was discovered by the solver, and
        // late: the guard sat in the field constructor, a solve builds its conductor mask
        // first, and the refusal then arrived as an argument exception the CLI could only
        // call a defect in the engine - after `validate` had passed the model and `estimate`
        // had priced it. The same rounding rule and the same limit the grid uses, so the
        // three verbs cannot disagree. Accumulated rather than returned, so a document with
        // this and another mistake is told about both in one pass.
        var spanX = maxX.Value.SiValue - minX.Value.SiValue;
        var spanY = maxY.Value.SiValue - minY.Value.SiValue;
        var spanZ = maxZ.Value.SiValue - minZ.Value.SiValue;
        var (countX, countY, countZ) = VolumeMesh.Counts(spanX, spanY, spanZ, cell.Value.SiValue);

        if (VolumeMesh.Product(countX, countY, countZ) > VolumeMesh.MaximumNodes)
        {
            errors.Add(VolumeMesh.Refusal(
                $"{path}/cellSize",
                countX, countY, countZ,
                spanX, spanY, spanZ,
                cell.Value.SiValue,
                solve.CellSize?.Unit));
        }

        // Before the electrodes, because an electrode's taps name the generators they
        // are taps on. It read the other way round when a solve could only have one.
        var drives = Drives(solve.Drive, solve.Drives, path, p, errors, directory);

        var electrodes = new List<CompiledElectrode3D>();
        var taken = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < (solve.Electrodes?.Count ?? 0); i++)
        {
            var before = electrodes.Count;
            Expand3D(solve.Electrodes![i], i, $"{path}/electrodes/{i}", drives, p, electrodes, errors);
            DistinctNames(electrodes.Skip(before).Select(e => e.Name), i, taken, $"{path}/electrodes", errors);
        }

        if (electrodes.Count == 0 && errors.Count == 0)
        {
            errors.Add(Missing($"{path}/electrodes",
                "a solved3d field needs at least one electrode",
                "add a box, a sphere or a cylinder"));
        }

        var stages = CompileStages3D(solve, timeline, electrodes, drives, errors);

        // The same refusal the plane path makes, and for the same reason: two conductors
        // in one place at two potentials is ill-posed, and the mask keeps whichever was
        // written last. Checked after expansion, because a repeated electrode only
        // overlaps itself once its copies exist - which is exactly the case this found
        // on its first run, a repeated drift stripe buried in the board behind it. And
        // after the stages, because what an electrode holds is what a stage changes.
        ElectrodeOverlap3D.Check(electrodes, stages, path, errors);

        if (drives.Count == 0 && electrodes.Any(e => e.IsDriven))
        {
            var driven = electrodes.First(e => e.IsDriven);

            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/drive",
                Constraint =
                    $"electrode '{driven.Name}' declares a drive amplitude, but the solve declares no drive",
                Observed = new ObservedValue(driven.DriveAmplitude, "V"),
                Suggestion = "add a drive block with a frequency, or use 'potential' for a static electrode",
            });
        }

        if (errors.Count > 0)
        {
            return null;
        }

        // Six faces, each Dirichlet unless declared otherwise. A grounded box is the safe
        // default and is a third electrode; a document whose geometry is invariant along an
        // axis says so by declaring that axis's faces Neumann.
        //
        // THE SAME PARSER THE PLANE PATH USES. `Boundary` already reads exactly this
        // vocabulary, with the same error code and suggestion, and it accumulates rather
        // than returning on the first bad face - so a document with two wrong face names is
        // told about both in one pass instead of taking two rounds to fix.
        var faces = new[]
        {
            Boundary(solve.LowerXEdge, $"{path}/lowerXEdge", errors),
            Boundary(solve.UpperXEdge, $"{path}/upperXEdge", errors),
            Boundary(solve.LowerYEdge, $"{path}/lowerYEdge", errors),
            Boundary(solve.UpperYEdge, $"{path}/upperYEdge", errors),
            Boundary(solve.LowerZEdge, $"{path}/lowerZEdge", errors),
            Boundary(solve.UpperZEdge, $"{path}/upperZEdge", errors),
        };

        // Mirroring is optional, and a mid-plane outside the solved box is a mistake worth
        // naming: the reflected half would sit somewhere other than against the solved one.
        double? reflect = null;

        if (solve.ReflectAboutX is not null)
        {
            var plane = TryQuantity(solve.ReflectAboutX, $"{path}/reflectAboutX", length, p, errors);

            if (plane is null)
            {
                return null;
            }

            if (plane.Value.SiValue < minX.Value.SiValue || plane.Value.SiValue > maxX.Value.SiValue)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/reflectAboutX",
                    Constraint =
                        $"the mirror plane is at {plane.Value.SiValue:G6} m, outside the solved "
                        + $"box [{minX.Value.SiValue:G6}, {maxX.Value.SiValue:G6}] - the reflected "
                        + "half would not meet the solved one",
                    Observed = new ObservedValue(plane.Value.SiValue, "m"),
                    Suggestion =
                        "put it at the edge of the domain the half is solved over, usually maxX, "
                        + "and declare that face neumann so the plane is a symmetry plane",
                });

                return null;
            }

            reflect = plane.Value.SiValue;
        }

        return new CompiledField
        {
            Kind = CompiledFieldKind.Solved3D,
            Solve3D = new CompiledSolvedField3D
            {
                MinX = minX.Value.SiValue,
                MinY = minY.Value.SiValue,
                MinZ = minZ.Value.SiValue,
                MaxX = maxX.Value.SiValue,
                MaxY = maxY.Value.SiValue,
                MaxZ = maxZ.Value.SiValue,
                CellSize = cell.Value.SiValue,
                Tolerance = solve.Tolerance,
                Drives = drives,
                Stages = stages,
                Electrodes = electrodes,
                Faces = faces,
                ReflectAboutX = reflect,
            },
        };
    }

    /// <summary>This volume element's electrodes, as they stand during each phase.</summary>
    /// <remarks>
    /// The same timeline the plane elements get. A sequence is the instrument's, so a
    /// model mixing a cross-section and a volume switches both at the same instants and
    /// against the same parameter values.
    /// </remarks>
    private static List<CompiledStage3D> CompileStages3D(
        SolvedField3DDocument solve,
        IReadOnlyList<PhaseSurface> timeline,
        IReadOnlyList<CompiledElectrode3D> baseline,
        IReadOnlyList<CompiledDrive> drives,
        List<EinzelError> errors)
    {
        var stages = new List<CompiledStage3D>();

        if (timeline.Count == 0 || solve.Electrodes is not { } declaredElectrodes)
        {
            return stages;
        }

        foreach (var phase in timeline)
        {
            var electrodes = new List<CompiledElectrode3D>();

            for (var i = 0; i < declaredElectrodes.Count; i++)
            {
                Expand3D(
                    declaredElectrodes[i], i, $"{phase.Path}/electrodes/{i}", drives,
                    phase.Surface, electrodes, errors);
            }

            if (!SameGeometry3D(baseline, electrodes, phase.Name, phase.Path, errors))
            {
                continue;
            }

            List<CompiledElectrode3D>? endElectrodes = null;
            if (phase.EndSurface is not null && phase.MidSurface is not null)
            {
                endElectrodes = [];
                var midElectrodes = new List<CompiledElectrode3D>();
                var ignored = new List<EinzelError>();
                for (var i = 0; i < declaredElectrodes.Count; i++)
                {
                    Expand3D(declaredElectrodes[i], i, $"{phase.Path}/electrodes/{i}", drives, phase.EndSurface, endElectrodes, ignored);
                    Expand3D(declaredElectrodes[i], i, $"{phase.Path}/electrodes/{i}", drives, phase.MidSurface, midElectrodes, ignored);
                }

                if (!SameGeometry3D(baseline, endElectrodes, phase.Name, phase.Path, errors)
                    || !RampIsLinear(Excitations(electrodes), Excitations(midElectrodes), Excitations(endElectrodes), phase.Name, phase.Path, errors))
                {
                    continue;
                }
            }

            stages.Add(new CompiledStage3D(phase.Name, phase.DurationSeconds, electrodes) { EndElectrodes = endElectrodes });
        }

        return stages;
    }

    /// <summary>Whether two compilations put the same metal in the same places.</summary>
    private static bool SameGeometry3D(
        IReadOnlyList<CompiledElectrode3D> baseline,
        List<CompiledElectrode3D> staged,
        string stage,
        string path,
        List<EinzelError> errors)
    {
        if (baseline.Count != staged.Count)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/set",
                Constraint =
                    $"stage '{stage}' changes how many electrodes there are, from {baseline.Count} to {staged.Count}",
                Observed = new ObservedValue(staged.Count, "electrodes"),
                Suggestion = "a stage may change what an electrode holds, not whether it exists",
            });

            return false;
        }

        for (var i = 0; i < baseline.Count; i++)
        {
            var a = baseline[i];
            var b = staged[i];

            var moved = a.Shape != b.Shape || a.Axis != b.Axis
                || a.MinX != b.MinX || a.MaxX != b.MaxX
                || a.MinY != b.MinY || a.MaxY != b.MaxY
                || a.MinZ != b.MinZ || a.MaxZ != b.MaxZ
                || a.CentreX != b.CentreX || a.CentreY != b.CentreY || a.CentreZ != b.CentreZ
                || a.Radius != b.Radius || a.Lower != b.Lower || a.Upper != b.Upper
                || !a.Vertices.SequenceEqual(b.Vertices);

            if (moved)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{path}/set",
                    Constraint =
                        $"stage '{stage}' moves electrode '{a.Name}', and a sequence may only change "
                        + "what an electrode holds, not where it is",
                    Observed = new ObservedValue(0.0, a.Name),
                    Suggestion =
                        "set only the parameters that reach potentials, amplitudes and phases; a stage that "
                        + "moves metal would need its own solve and its own grid",
                });

                return false;
            }
        }

        return true;
    }

    private static CompiledField? CompileSolvedField(
        SolvedFieldDocument? solve,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        IReadOnlyList<PhaseSurface> timeline,
        List<EinzelError> errors,
        string? directory)
    {
        if (solve is null)
        {
            errors.Add(Missing(path, "a solved field must declare its domain and electrodes",
                "add a \"solve\" object with bounds, cellSize, and electrodes"));
            return null;
        }

        var length = Dimension.LengthDimension;
        var minX = TryQuantity(solve.MinX, $"{path}/minX", length, p, errors);
        var minY = TryQuantity(solve.MinY, $"{path}/minY", length, p, errors);
        var maxX = TryQuantity(solve.MaxX, $"{path}/maxX", length, p, errors);
        var maxY = TryQuantity(solve.MaxY, $"{path}/maxY", length, p, errors);
        var cell = TryQuantity(solve.CellSize, $"{path}/cellSize", length, p, errors);

        if (minX is null || minY is null || maxX is null || maxY is null || cell is null)
        {
            return null;
        }

        if (maxX.Value.SiValue <= minX.Value.SiValue || maxY.Value.SiValue <= minY.Value.SiValue)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "the solve domain must have positive extent in both directions",
                Suggestion = "check that maxX exceeds minX and maxY exceeds minY",
            });
            return null;
        }

        if (cell.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/cellSize",
                Constraint = "the cell size must be positive",
                Observed = new ObservedValue(cell.Value.SiValue, "m"),
                Suggestion = "about a thirtieth of the smallest feature is a reasonable start",
            });
            return null;
        }

        if (solve.Electrodes is null || solve.Electrodes.Count == 0)
        {
            errors.Add(Missing($"{path}/electrodes", "a solved field needs at least one electrode",
                "add an electrode with a shape and a potential"));
            return null;
        }

        // Before the electrodes, because a tap names the generator it is on and the
        // name has to resolve to something.
        var drives = Drives(solve.Drive, solve.Drives, path, p, errors, directory);

        var electrodes = new List<CompiledElectrode>();
        var taken = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < solve.Electrodes.Count; i++)
        {
            var before = electrodes.Count;
            Expand(solve.Electrodes[i], i, $"{path}/electrodes/{i}", drives, p, electrodes, errors);
            DistinctNames(electrodes.Skip(before).Select(e => e.Name), i, taken, $"{path}/electrodes", errors);
        }

        var reflect = solve.ReflectAboutX is null
            ? (double?)null
            : TryQuantity(solve.ReflectAboutX, $"{path}/reflectAboutX", length, p, errors)?.SiValue;

        var stages = CompileStages(solve, timeline, electrodes, drives, errors);

        // Two conductors in one place at two potentials is ill-posed, and the mask
        // keeps whichever was written last - so the solve would return the field of
        // a geometry nobody described. Checked after expansion, because a repeated
        // electrode only overlaps itself once its copies exist; and after the stages,
        // because what an electrode holds is what a stage changes.
        ElectrodeOverlap.Check(electrodes, stages, path, errors);

        var symmetry = Symmetry(solve.Symmetry, $"{path}/symmetry", errors);

        // Reported here, before the electrodes are compiled, so that a geometry which is
        // also invalid for some other reason cannot hide it. Burying a refusal behind an
        // unrelated one makes a document take two rounds to fix and gives no reason for
        // the second - the same mistake the region check made on a solved element.
        if (solve.TiltHalfTurns is not null && symmetry == SolveSymmetry.Cylindrical)
        {
            var declared = TryQuantity(
                solve.TiltHalfTurns, $"{path}/tiltHalfTurns", Dimension.Dimensionless, p, errors);
            if (declared is not null && declared.Value.SiValue != 0.0)
            {
                errors.Add(new EinzelError
                {
                    Code = "TILT_NOT_AVAILABLE",
                    Path = $"{path}/tiltHalfTurns",
                    Constraint = "an axisymmetric solve may not tilt its extrusion axis - it "
                        + "stands for a body of revolution about x, and tilting the half-plane "
                        + "about y does not rotate that body",
                    Observed = new ObservedValue(declared.Value.SiValue, "1"),
                    Suggestion = "use a translational cross-section, or tilt the electrodes",
                    Severity = ErrorSeverity.Error,
                });
            }
        }

        // The axis is at y = 0 and a radius cannot be negative. Refused rather
        // than folded, because a domain drawn across the axis is a different
        // intent from one drawn beside it and guessing which would be worse than
        // asking.
        if (symmetry == SolveSymmetry.Cylindrical && minY is { } radialFloor && radialFloor.SiValue < 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/minY",
                Constraint = "a cylindrical solve has y as the radius, so the domain cannot reach below zero",
                Observed = new ObservedValue(radialFloor.SiValue, "m"),
                Suggestion =
                    "set minY to 0 to include the axis, or to a positive radius for an annulus; "
                    + "x is the axis of rotation",
            });
        }

        var left = Boundary(solve.LeftEdge, $"{path}/leftEdge", errors);
        var right = Boundary(solve.RightEdge, $"{path}/rightEdge", errors);
        var bottom = Boundary(solve.BottomEdge, $"{path}/bottomEdge", errors);
        var top = Boundary(solve.TopEdge, $"{path}/topEdge", errors);

        // An amplitude with no generator behind it is a document that thinks it
        // declared RF and did not. Silence here is the expensive kind.
        if (drives.Count == 0 && electrodes.Any(e => e.IsDriven))
        {
            var driven = electrodes.First(e => e.IsDriven);

            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/drive",
                Constraint =
                    $"electrode '{driven.Name}' declares a drive amplitude, but the solve declares no drive",
                Observed = new ObservedValue(driven.DriveAmplitude, "V"),
                Suggestion =
                    "add a drive block with a frequency, or remove the amplitude and use "
                    + "'potential' for a static electrode",
            });
        }

        if (errors.Count > 0)
        {
            return null;
        }

        // The extrusion-axis tilt. Refused on a cylindrical solve rather than ignored:
        // rotating a half-plane about y is not a rotation of the body it stands for, so a
        // document asking for it is asking for something that does not exist.
        var tilt = solve.TiltHalfTurns is null
            ? Quantity.Si(0.0, Dimension.Dimensionless)
            : TryQuantity(solve.TiltHalfTurns, $"{path}/tiltHalfTurns", Dimension.Dimensionless, p, errors);
        var tiltCentreX = solve.TiltCentreX is null
            ? Quantity.Si(0.5 * (minX.Value.SiValue + maxX.Value.SiValue), Dimension.LengthDimension)
            : TryQuantity(solve.TiltCentreX, $"{path}/tiltCentreX", Dimension.LengthDimension, p, errors);
        var tiltCentreZ = solve.TiltCentreZ is null
            ? Quantity.Si(0.0, Dimension.LengthDimension)
            : TryQuantity(solve.TiltCentreZ, $"{path}/tiltCentreZ", Dimension.LengthDimension, p, errors);

        if (tilt is null || tiltCentreX is null || tiltCentreZ is null)
        {
            return null;
        }

        if (errors.Count > 0)
        {
            return null;
        }

        return new CompiledField
        {
            Kind = CompiledFieldKind.Solved2D,
            Solve = new CompiledSolvedField
            {
                TiltHalfTurns = tilt.Value.SiValue,
                TiltCentreX = tiltCentreX.Value.SiValue,
                TiltCentreZ = tiltCentreZ.Value.SiValue,
                MinX = minX.Value.SiValue,
                MinY = minY.Value.SiValue,
                MaxX = maxX.Value.SiValue,
                MaxY = maxY.Value.SiValue,
                CellSize = cell.Value.SiValue,
                Symmetry = symmetry,
                Drives = drives,
                Stages = stages,
                LeftEdge = left,
                RightEdge = right,
                BottomEdge = bottom,
                TopEdge = top,
                Electrodes = electrodes,
                BoundaryIsDiscontinuous = solve.BoundaryIsDiscontinuous,
                Tolerance = solve.Tolerance,
                ReflectAboutX = reflect,
            },
        };
    }


    /// <summary>
    /// Every generator a solve declares, from either the singular or the plural form.
    /// </summary>
    /// <remarks>
    /// Both spellings exist because nearly every device has one drive and making it
    /// a list of one would be ceremony on every template. Declaring both is refused
    /// rather than merged: a document that says a geometry has one drive and also
    /// says it has three is not a document with a default to fall back on.
    /// </remarks>
    private static List<CompiledDrive> Drives(
        DriveDocument? single,
        IReadOnlyList<DriveDocument>? several,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors,
        string? directory)
    {
        if (single is not null && several is not null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/drives",
                Constraint = "a solve declares either 'drive' or 'drives', not both",
                Suggestion = "keep 'drives' and move the single drive into it as the first entry, "
                    + "or delete 'drives' if one generator is all this geometry has",
            });

            return [];
        }

        if (single is not null)
        {
            var one = Drive(single, $"{path}/drive", p, errors, directory);

            return one is null ? [] : [one];
        }

        if (several is null)
        {
            return [];
        }

        var compiled = new List<CompiledDrive>(several.Count);

        for (var k = 0; k < several.Count; k++)
        {
            var built = Drive(several[k], $"{path}/drives/{k}", p, errors, directory);

            if (built is null)
            {
                continue;
            }

            var name = several[k].Name ?? string.Empty;

            if (name.Length == 0 && several.Count > 1)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/drives/{k}/name",
                    Constraint =
                        "every generator needs a name when a geometry declares more than one, "
                        + "because an electrode taps them by name",
                    Suggestion = "add a \"name\", for example \"main\" or \"excitation\"",
                });

                continue;
            }

            if (compiled.Exists(d => d.Name == name))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/drives/{k}/name",
                    Constraint = $"two generators are both called '{name}'",
                    Observed = new ObservedValue(k, "index"),
                    Suggestion = "give each generator a distinct name; an electrode taps them by "
                        + "name and a duplicate makes the tap ambiguous",
                });

                continue;
            }

            compiled.Add(built with { Name = name });
        }

        return compiled;
    }

    /// <summary>
    /// How one electrode is connected to the generators, from either spelling.
    /// </summary>
    /// <remarks>
    /// Read for every electrode whether or not the geometry declares a drive, so
    /// that an amplitude on a static solve is caught where it is written rather than
    /// silently ignored - which is the failure mode that makes someone spend an
    /// afternoon wondering why the RF is not doing anything.
    /// </remarks>
    private static List<CompiledTap> Taps(
        ITappedElectrode electrode,
        IReadOnlyList<CompiledDrive> drives,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (electrode.Taps is null)
        {
            var (amplitude, phase) = Tap(electrode, path, p, errors);

            return amplitude == 0.0 ? [] : [new CompiledTap(0, amplitude, phase)];
        }



        if (electrode.DriveAmplitude is not null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/taps",
                Constraint =
                    "an electrode declares either 'driveAmplitude' or 'taps', not both",
                Suggestion = "move the amplitude and phase into the first tap, or delete 'taps' "
                    + "if this electrode is fed by one generator",
            });

            return [];
        }

        var taps = new List<CompiledTap>(electrode.Taps.Count);

        for (var k = 0; k < electrode.Taps.Count; k++)
        {
            var tap = electrode.Taps[k];
            var at = $"{path}/taps/{k}";

            var index = 0;

            if (tap.Drive is { } named)
            {
                index = -1;

                for (var d = 0; d < drives.Count; d++)
                {
                    if (string.Equals(drives[d].Name, named, StringComparison.Ordinal))
                    {
                        index = d;
                        break;
                    }
                }

                if (index < 0)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{at}/drive",
                        Constraint = $"no generator is called '{named}'",
                        Observed = new ObservedValue(drives.Count, "declared generator(s)"),
                        Suggestion = drives.Count == 0
                            ? "declare a 'drives' block naming the generators before tapping one"
                            : "the declared generators are: "
                                + string.Join(", ", drives.Select(d => $"'{d.Name}'")),
                    });

                    continue;
                }
            }
            else if (drives.Count > 1)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{at}/drive",
                    Constraint =
                        $"this geometry declares {drives.Count} generators, so a tap must name "
                        + "which one it is on",
                    Suggestion = "add \"drive\": with one of "
                        + string.Join(", ", drives.Select(d => $"'{d.Name}'")),
                });

                continue;
            }

            var amplitude = tap.Amplitude is null
                ? 0.0
                : TryQuantity(
                    tap.Amplitude, $"{at}/amplitude",
                    Dimension.ElectricPotential, p, errors)?.SiValue ?? 0.0;

            if (amplitude == 0.0)
            {
                // A tap of zero volts is a wire that carries nothing. Dropped rather
                // than refused, because a template that ramps an amplitude over a
                // repeat index will legitimately produce one at the ends.
                continue;
            }

            taps.Add(new CompiledTap(index, amplitude, Phase(tap.Phase, $"{at}/phase", p, errors)));
        }

        return taps;
    }

    /// <summary>How one electrode taps the drive: amplitude and phase.</summary>
    /// <remarks>
    /// Read for every electrode whether or not the geometry declares a drive, so
    /// that an amplitude on a static solve is caught where it is written rather
    /// than silently ignored - which is the failure mode that makes someone spend
    /// an afternoon wondering why the RF is not doing anything.
    /// </remarks>
    private static (double Amplitude, double Phase) Tap(
        ITappedElectrode electrode,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        var amplitude = electrode.DriveAmplitude is null
            ? 0.0
            : TryQuantity(
                electrode.DriveAmplitude, $"{path}/driveAmplitude",
                Dimension.ElectricPotential, p, errors)?.SiValue ?? 0.0;

        return (amplitude, Phase(electrode.DrivePhase, $"{path}/drivePhase", p, errors));
    }

    /// <summary>
    /// A drive phase, as a fraction of a cycle, resolved through the parameters.
    /// </summary>
    /// <remarks>
    /// Dimensionless, because a fraction of a cycle is. That makes it one of the
    /// few places a bare number is legitimate, and the units grammar already treats
    /// "1" as the dimensionless unit - so an expression over the repeat index reads
    /// as <c>{"expression": "ring / ringsPerWave"}</c> with no unit to argue about.
    /// </remarks>
    private static double Phase(
        QuantityValue? declared,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (declared is null)
        {
            return 0.0;
        }

        var resolved = TryQuantity(declared, path, Dimension.Dimensionless, p, errors);

        if (resolved is null)
        {
            return 0.0;
        }

        var phase = resolved.Value.SiValue;

        if (!double.IsFinite(phase))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "a drive phase is a fraction of a cycle and must be finite",
                Observed = new ObservedValue(phase, "1"),
                Suggestion = "use 0 for in phase and 0.5 for antiphase",
            });
        }

        return phase;
    }

    private static CompiledDrive? Drive(
        DriveDocument? drive,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors,
        string? directory)
    {
        if (drive is null)
        {
            return null;
        }

        var frequency = TryQuantity(drive.Frequency, $"{path}/frequency", Dimension.Frequency, p, errors);

        if (frequency is null)
        {
            return null;
        }

        if (frequency.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/frequency",
                Constraint = "a drive frequency must be positive",
                Observed = new ObservedValue(frequency.Value.SiValue, "Hz"),
                Suggestion = "supply a positive frequency, for example {\"value\": 1, \"unit\": \"MHz\"}",
            });

            return null;
        }

        if (drive.Waveform is { IsShortForm: false } shape)
        {
            return Shaped(drive, shape, frequency.Value.SiValue, path, p, errors, directory);
        }

        var waveform = drive.Waveform?.Type switch
        {
            null or "sinusoid" => DriveWaveform.Sinusoid,
            "rectangular" => DriveWaveform.Rectangular,
            _ => (DriveWaveform?)null,
        };

        if (waveform is null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/waveform",
                Constraint = "a drive waveform must be 'sinusoid' or 'rectangular'",
                Observed = new ObservedValue(0.0, drive.Waveform?.Type ?? "(none)"),
                Suggestion =
                    "'sinusoid' is what a resonant circuit produces and gives the Mathieu equation; "
                    + "'rectangular' is what a switching supply produces and gives Meissner's",
            });

            return null;
        }

        var duty = Duty(drive.DutyCycle, $"{path}/dutyCycle", errors);

        return duty is null ? null : new CompiledDrive(frequency.Value.SiValue, waveform.Value, duty.Value);
    }

    private static double? Duty(double? declared, string path, List<EinzelError> errors)
    {
        var duty = declared ?? 0.5;

        if (duty is <= 0.0 or >= 1.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path,
                Constraint = "a duty cycle is the fraction of a cycle spent high, strictly between 0 and 1",
                Observed = new ObservedValue(duty, "1"),
                Suggestion = "0.5 is a balanced square wave; 0.61 is a typical digital mass-filter setting",
            });

            return null;
        }

        return duty;
    }

    /// <summary>A drive whose waveform is the object form: one of five shapes and its own keys.</summary>
    private static CompiledDrive? Shaped(
        DriveDocument drive,
        WaveformDocument shape,
        double frequencyHz,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors,
        string? directory)
    {
        var at = $"{path}/waveform";
        DriveWaveform? kind = shape.Type switch
        {
            "sinusoid" => DriveWaveform.Sinusoid,
            "rectangular" => DriveWaveform.Rectangular,
            "analytic" => DriveWaveform.Analytic,
            "tabulated" => DriveWaveform.Tabulated,
            "harmonic" => DriveWaveform.Harmonic,
            _ => null,
        };

        if (kind is null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{at}/type",
                Constraint = "a waveform type must be one of " + string.Join(", ", WaveformDocument.Types),
                Observed = new ObservedValue(0.0, shape.Type ?? "(none)"),
                Suggestion = "write 'analytic' with an expression of the phase p, 'tabulated' with a file "
                    + "of one sampled period, or 'harmonic' with terms; 'sinusoid' and 'rectangular' "
                    + "are the two supplies a short name already spells",
            });

            return null;
        }

        // A key of another shape is refused rather than ignored: an expression beside a
        // table would otherwise read as a drive it does not describe.
        (string Key, bool Present, DriveWaveform Owner)[] keys =
        [
            ("expression", shape.Expression is not null, DriveWaveform.Analytic),
            ("file", shape.File is not null, DriveWaveform.Tabulated),
            ("interpolation", shape.Interpolation is not null, DriveWaveform.Tabulated),
            ("terms", shape.Terms is not null, DriveWaveform.Harmonic),
            ("dutyCycle", shape.DutyCycle is not null, DriveWaveform.Rectangular),
        ];
        var valid = true;

        foreach (var (key, _, owner) in keys.Where(k => k.Present && k.Owner != kind))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{at}/{key}",
                Constraint = $"'{key}' belongs to a {owner.ToString().ToLowerInvariant()} waveform, not a {shape.Type} one",
                Suggestion = key == "dutyCycle"
                    ? "a duty cycle belongs to a rectangular wave; any other shape carries its asymmetry in "
                        + "its own expression, table or terms"
                    : "remove the key, or change the type to the shape it describes",
            });
            valid = false;
        }

        // A sinusoid and a harmonic sum are smooth, and a rectangular wave declares its own
        // edges at 0 and its duty cycle; a breakpoint there would be a second, conflicting say.
        if (shape.Breakpoints is not null && kind is not (DriveWaveform.Analytic or DriveWaveform.Tabulated))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{at}/breakpoints",
                Constraint = $"a {shape.Type} waveform takes no breakpoints",
                Suggestion = "a sinusoid and a harmonic sum have no jumps or kinks, and a rectangular wave "
                    + "declares its own edges; breakpoints are for an analytic or tabulated shape",
            });
            valid = false;
        }

        if (drive.DutyCycle is not null && (kind != DriveWaveform.Rectangular || shape.DutyCycle is not null))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/dutyCycle",
                Constraint = kind == DriveWaveform.Rectangular
                    ? "the duty cycle is declared both beside the waveform and inside it"
                    : "a duty cycle belongs to a rectangular wave",
                Observed = new ObservedValue(drive.DutyCycle.Value, "1"),
                Suggestion = "declare the duty cycle once, inside a rectangular waveform; any other shape "
                    + "carries its asymmetry in its own expression, table or terms",
            });
            valid = false;
        }

        var breakpoints = shape.Breakpoints ?? [];

        for (var i = 0; i < breakpoints.Count; i++)
        {
            if (!(breakpoints[i] >= 0.0 && breakpoints[i] < 1.0) || (i > 0 && !(breakpoints[i] > breakpoints[i - 1])))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{at}/breakpoints/{i}",
                    Constraint = "breakpoints are phases in cycles, strictly ascending in [0, 1)",
                    Observed = new ObservedValue(breakpoints[i], "1"),
                    Suggestion = "list each jump or kink of the shape once, in order; phase 1 is phase 0 "
                        + "of the next cycle",
                });
                valid = false;
            }
        }

        if (!valid)
        {
            return null;
        }

        var compiled = new CompiledDrive(frequencyHz, kind.Value, 0.5) { Breakpoints = [.. breakpoints] };

        switch (kind)
        {
            case DriveWaveform.Rectangular:
                var inside = shape.DutyCycle is not null;
                var duty = Duty(shape.DutyCycle ?? drive.DutyCycle, inside ? $"{at}/dutyCycle" : $"{path}/dutyCycle", errors);
                return duty is null ? null : compiled with { DutyCycle = duty.Value };

            case DriveWaveform.Analytic:
                return Analytic(shape.Expression, $"{at}/expression", breakpoints, p, errors) is { } bindings
                    ? compiled with { Expression = shape.Expression, Bindings = bindings }
                    : null;

            case DriveWaveform.Tabulated:
                return Tabulated(shape, at, compiled, errors, directory);

            case DriveWaveform.Harmonic:
                return Harmonics(shape.Terms, $"{at}/terms", errors) is { } terms ? compiled with { Terms = terms } : null;

            default:
                return compiled;
        }
    }

    /// <summary>
    /// Compiles an analytic shape and probes it where the field layer will: 4096 uniform
    /// phases, each breakpoint and the phase just below it. Returns the bound parameters.
    /// </summary>
    private static Dictionary<string, Quantity>? Analytic(
        string? expression,
        string path,
        IReadOnlyList<double> breakpoints,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        Func<double, double> shape;

        try
        {
            shape = ExpressionEvaluator.Compile(expression ?? string.Empty, p, "p", path);
        }
        catch (EinzelException failure)
        {
            errors.Add(failure.Error);
            return null;
        }

        var last = Math.BitDecrement(1.0);
        var probes = Enumerable.Range(0, 4096).Select(k => k / 4096.0)
            .Concat(breakpoints)
            .Concat(breakpoints.Select(b => b > 0.0 ? Math.BitDecrement(b) : last));

        foreach (var phase in probes)
        {
            var value = shape(phase);

            if (!double.IsFinite(value))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = path,
                    Constraint = $"the waveform is {value} at phase {phase:R}; a drive must be finite over the whole cycle",
                    Observed = new ObservedValue(phase, "1"),
                    Suggestion = "keep a logarithm's or a square root's argument positive and every divisor "
                        + "away from zero; a supply cannot deliver an infinite voltage at any instant",
                });
                return null;
            }
        }

        return ExpressionEvaluator.References(expression!)
            .Where(p.ContainsKey)
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(name => name, name => p[name], StringComparer.Ordinal);
    }

    /// <summary>Reads a tabulated shape's file, relative to the model's directory.</summary>
    private static CompiledDrive? Tabulated(
        WaveformDocument shape, string at, CompiledDrive compiled, List<EinzelError> errors, string? directory)
    {
        WaveformInterpolation? interpolation = shape.Interpolation switch
        {
            null or "linear" => WaveformInterpolation.Linear,
            "cubic" => WaveformInterpolation.Cubic,
            _ => null,
        };

        if (interpolation is null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{at}/interpolation",
                Constraint = "a table's interpolation is 'linear' or 'cubic'",
                Observed = new ObservedValue(0.0, shape.Interpolation!),
                Suggestion = "'linear' joins the samples with straight lines and keeps every corner; "
                    + "'cubic' is the periodic spline, smooth and accurate on a smooth shape",
            });
            return null;
        }

        if (string.IsNullOrWhiteSpace(shape.File))
        {
            errors.Add(Missing($"{at}/file", "a tabulated waveform needs a file",
                "name a 'phase,value' table of one period, relative to the model file"));
            return null;
        }

        try
        {
            // Relative to the model file, or to the working directory for a document read
            // from a string - the rule gas imports follow.
            var text = File.ReadAllText(Path.GetFullPath(shape.File, directory ?? Directory.GetCurrentDirectory()));
            var table = WaveformTable.Parse(text, $"{at}/file");
            return compiled with { Table = table, TablePath = shape.File, Interpolation = interpolation.Value };
        }
        catch (EinzelException failure)
        {
            errors.Add(failure.Error);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{at}/file",
                Constraint = $"the waveform table '{shape.File}' could not be read: {ex.Message}",
                Observed = new ObservedValue(0.0, shape.File),
                Suggestion = "give the path relative to the model file, and check that the file exists and is readable",
            });
            return null;
        }
    }

    private static List<DriveHarmonic>? Harmonics(
        IReadOnlyList<HarmonicTermDocument>? terms, string at, List<EinzelError> errors)
    {
        if (terms is null || terms.Count == 0)
        {
            errors.Add(Missing(at, "a harmonic waveform needs at least one term",
                "add {\"order\": 1, \"amplitude\": 1} for the fundamental; an empty series is a drive "
                    + "of zero volts, better said by not declaring a drive"));
            return null;
        }

        var compiled = new List<DriveHarmonic>(terms.Count);

        for (var i = 0; i < terms.Count; i++)
        {
            var (order, amplitude, phase) = (terms[i].Order, terms[i].Amplitude, terms[i].Phase ?? 0.0);
            var key = order is not >= 0 ? "order"
                : amplitude is not { } a || !double.IsFinite(a) ? "amplitude"
                : !double.IsFinite(phase) ? "phase"
                : null;

            if (key is not null)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{at}/{i}/{key}",
                    Constraint = "a harmonic term needs an order of 0 or more and a finite amplitude and phase",
                    Suggestion = "order 1 is the drive frequency and order 0 a constant offset; the phase is "
                        + "in cycles, so 0.5 is antiphase",
                });
                return null;
            }

            compiled.Add(new DriveHarmonic(order!.Value, amplitude!.Value, phase));
        }

        return compiled;
    }

    private static SolveSymmetry Symmetry(string? declared, string path, List<EinzelError> errors)
    {
        switch (declared)
        {
            case null or "translational":
                return SolveSymmetry.Translational;

            case "cylindrical":
                return SolveSymmetry.Cylindrical;

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = path,
                    Constraint = "a solve symmetry must be 'translational' or 'cylindrical'",
                    Observed = new ObservedValue(0.0, declared),
                    Suggestion =
                        "'translational' extrudes the cross-section along the third axis and is the "
                        + "default; 'cylindrical' rotates it about the x axis, with y as the radius",
                });

                return SolveSymmetry.Translational;
        }
    }

    private static BoundaryKind Boundary(string? declared, string path, List<EinzelError> errors)
    {
        switch (declared)
        {
            case null or "dirichlet":
                return BoundaryKind.Dirichlet;

            case "neumann":
                return BoundaryKind.Neumann;

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = path,
                    Constraint = "an edge condition must be 'dirichlet' or 'neumann'",
                    Observed = new ObservedValue(0.0, declared),
                    Suggestion = "'neumann' is a symmetry plane; omit the field for 'dirichlet'",
                });
                return BoundaryKind.Dirichlet;
        }
    }

    /// <summary>
    /// Compiles one declared electrode into the electrodes it stands for.
    /// </summary>
    /// <remarks>
    /// One, unless it declares a repeat, in which case the index is bound as an
    /// ordinary parameter and every expression on the electrode sees it. That is
    /// what keeps a stack of two hundred rings a parametric document rather than a
    /// generated one: the placements are still expressions, so a sweep can still
    /// move them. <paramref name="position"/> is where the declaration sits in its
    /// solve's list, which is what an unnamed electrode is called after.
    /// </remarks>
    private static void Expand(
        ElectrodeDocument declared,
        int position,
        string path,
        IReadOnlyList<CompiledDrive> drives,
        IReadOnlyDictionary<string, Quantity> p,
        List<CompiledElectrode> into,
        List<EinzelError> errors)
    {
        var name = ElectrodeName(declared.Name, position);

        if (declared.Repeat is not { } repeat)
        {
            var single = CompileElectrode(declared, name, path, drives, p, errors);

            if (single is not null)
            {
                into.Add(single);
            }

            return;
        }

        var count = TryQuantity(repeat.Count, $"{path}/repeat/count", Dimension.Dimensionless, p, errors);

        if (count is null)
        {
            return;
        }

        var copies = (int)Math.Round(count.Value.SiValue);

        if (copies < 1 || Math.Abs(count.Value.SiValue - copies) > 1e-9)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/repeat/count",
                Constraint = "a repeat count must be a whole number of at least one",
                Observed = new ObservedValue(count.Value.SiValue, "1"),
                Suggestion = "use a parameter that evaluates to a whole number, for example 24",
            });

            return;
        }

        var index = repeat.Index ?? "index";

        if (p.ContainsKey(index))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/repeat/index",
                Constraint = $"'{index}' is already a declared parameter, and binding it here would shadow it",
                Observed = new ObservedValue(0.0, index),
                Suggestion = "choose another index name, such as 'ring' or 'plate'",
            });

            return;
        }

        for (var k = 0; k < copies; k++)
        {
            var scoped = new Dictionary<string, Quantity>(p, StringComparer.Ordinal)
            {
                [index] = Quantity.Si(k, Dimension.Dimensionless),
            };

            // Named by position so a loss itemisation, a channel report or an error
            // says which one - "ring-17" rather than "ring", seventeen times.
            var copy = CompileElectrode(
                declared with { Repeat = null },
                $"{name}-{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                $"{path}/repeat/{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                drives,
                scoped,
                errors);

            if (copy is not null)
            {
                into.Add(copy);
            }
        }
    }

    /// <summary>What an electrode is called: its own name, or one made from its position.</summary>
    /// <remarks>
    /// <para>
    /// <b>A name is an electrode's identity within its solve, so an unnamed one cannot share
    /// a default.</b> Every unnamed electrode used to be called <c>electrode</c>. A driven or
    /// sequenced geometry builds its channels from a map keyed by name, so two unnamed plates
    /// at +2 V and -2 V became one entry holding -2 V and were solved at the same potential:
    /// the deflector they make was switched off, on a model that validated, solved and
    /// converged. A static geometry was right only because it builds its mask electrode by
    /// electrode; an ion lost on either plate was still reported as striking one surface.
    /// </para>
    /// <para>
    /// After the position in the list rather than a counter of unnamed ones, so the name
    /// matches the path an error gives (<c>electrode-3</c> is <c>/electrodes/3</c>) and does
    /// not change when another electrode gains or loses a name.
    /// </para>
    /// </remarks>
    private static string ElectrodeName(string? declared, int position) =>
        declared ?? $"electrode-{position.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    /// <summary>
    /// Refuses a name already taken in this solve by an earlier declaration.
    /// </summary>
    /// <param name="names">What declaration <paramref name="position"/> compiled to, its repeat copies included.</param>
    /// <param name="position">Where that declaration sits in the solve's list.</param>
    /// <param name="taken">Every name compiled so far, and the declaration it came from.</param>
    /// <param name="path">The solve's electrode list, for an error path (AGT-3).</param>
    /// <param name="errors">Where a refusal goes.</param>
    /// <remarks>
    /// <para>
    /// <b>Within one solve, not across the model.</b> Two field elements may declare the same
    /// metal - the Astral's two mirror solves each carry all twenty-two boards, one holding
    /// the near mirror's potentials and one the far's - and there a shared name is right,
    /// because it is one conductor and an ion striking it struck one surface. The channels
    /// are built per solve, so it is only within one that a shared name changes a field.
    /// </para>
    /// <para>
    /// Refused for a static geometry too, although its field is right: ACC-5 itemizes losses
    /// by name, so two plates sharing one are reported as one surface, and a static model
    /// would otherwise change from valid to wrong the day somebody drives it.
    /// </para>
    /// <para>
    /// One complaint per pair of declarations however many of their names collide, so two
    /// repeated rings of two hundred sharing a base name read as one mistake rather than two
    /// hundred.
    /// </para>
    /// </remarks>
    private static void DistinctNames(
        IEnumerable<string> names,
        int position,
        Dictionary<string, int> taken,
        string path,
        List<EinzelError> errors)
    {
        var collisions = new Dictionary<int, (string First, int Count)>();

        foreach (var name in names)
        {
            if (taken.TryGetValue(name, out var earlier))
            {
                collisions[earlier] = collisions.TryGetValue(earlier, out var seen)
                    ? (seen.First, seen.Count + 1)
                    : (name, 1);

                continue;
            }

            taken[name] = position;
        }

        foreach (var (earlier, (first, count)) in collisions)
        {
            var more = count > 1
                ? $", and {(count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture)} more of their names collide"
                : string.Empty;

            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = $"{path}/{position.ToString(System.Globalization.CultureInfo.InvariantCulture)}/name",
                Constraint =
                    $"electrodes {earlier.ToString(System.Globalization.CultureInfo.InvariantCulture)} and "
                    + $"{position.ToString(System.Globalization.CultureInfo.InvariantCulture)} in this solve are "
                    + $"both called '{first}'{more}. A name is what identifies an electrode in a solve: a "
                    + "driven or sequenced geometry builds its channels from it, so the two would be solved "
                    + "holding the same potential, and an ion lost on either would be reported as one surface",
                Observed = new ObservedValue(earlier, "index"),
                Suggestion =
                    "give each a distinct name. An unnamed electrode is called 'electrode-N' after its "
                    + "place in the list, and the copies of a repeated one 'name-0', 'name-1' and so on",
            });
        }
    }

    /// <summary>Compiles one electrode, its repeat already applied.</summary>
    /// <remarks>
    /// The name is handed in rather than read from the declaration, so that
    /// <see cref="ElectrodeName"/> is the one place an unnamed electrode is named.
    /// </remarks>
    private static CompiledElectrode? CompileElectrode(
        ElectrodeDocument electrode,
        string name,
        string path,
        IReadOnlyList<CompiledDrive> drives,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        var length = Dimension.LengthDimension;
        var volt = Dimension.ElectricPotential;

        switch (electrode.Shape)
        {
            case "rectangle":
            {
                var minX = TryQuantity(electrode.MinX, $"{path}/minX", length, p, errors);
                var minY = TryQuantity(electrode.MinY, $"{path}/minY", length, p, errors);
                var maxX = TryQuantity(electrode.MaxX, $"{path}/maxX", length, p, errors);
                var maxY = TryQuantity(electrode.MaxY, $"{path}/maxY", length, p, errors);
                var potential = TryQuantity(electrode.Potential, $"{path}/potential", volt, p, errors);
                var taps = Taps(electrode, drives, path, p, errors);

                if (minX is null || minY is null || maxX is null || maxY is null || potential is null)
                {
                    return null;
                }

                // An inverted rectangle is not an empty electrode, it is a
                // disappeared one: the rasteriser walks from a higher index to a
                // lower one, marks nothing, and the solve proceeds as though the
                // electrode were never declared. That is reachable from ordinary
                // parameter arithmetic - a derived half-width that goes negative
                // when a gap grows past a radius - so it is exactly the kind of
                // silent geometry failure a tolerance sweep would attribute to
                // physics. The disc case has always rejected a non-positive
                // radius; this is the same check for the other primitive.
                //
                // Equality is allowed. A rectangle of zero extent in one axis is a
                // line segment, which is how an infinitely thin plate is written -
                // the mirror template's cap is one - and cut cells resolve it
                // exactly. Only min strictly greater than max is nonsense.
                if (minX.Value.SiValue > maxX.Value.SiValue
                    || minY.Value.SiValue > maxY.Value.SiValue)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = path,
                        Constraint =
                            $"rectangle '{name}' is inverted: it needs minX <= maxX and minY <= maxY, "
                            + "and an inverted one vanishes from the solve rather than failing",
                        Observed = new ObservedValue(
                            maxX.Value.SiValue - minX.Value.SiValue,
                            "m of width, by "
                            + (maxY.Value.SiValue - minY.Value.SiValue).ToString(
                                "G6", System.Globalization.CultureInfo.InvariantCulture)
                            + " m of height"),
                        Suggestion =
                            "check the expressions that derive the bounds; a derived half-width "
                            + "can go negative when one parameter grows past another",
                    });

                    return null;
                }

                return new CompiledElectrode
                    {
                        Name = name,
                        Shape = ElectrodeShape.Rectangle,
                        MinX = minX.Value.SiValue,
                        MinY = minY.Value.SiValue,
                        MaxX = maxX.Value.SiValue,
                        MaxY = maxY.Value.SiValue,
                        Potential = potential.Value.SiValue,
                        Taps = taps,
                    };
            }

            case "disc":
            {
                var centreX = TryQuantity(electrode.CentreX, $"{path}/centreX", length, p, errors);
                var centreY = TryQuantity(electrode.CentreY, $"{path}/centreY", length, p, errors);
                var radius = TryQuantity(electrode.Radius, $"{path}/radius", length, p, errors);
                var potential = TryQuantity(electrode.Potential, $"{path}/potential", volt, p, errors);
                var taps = Taps(electrode, drives, path, p, errors);

                if (centreX is null || centreY is null || radius is null || potential is null)
                {
                    return null;
                }

                if (radius.Value.SiValue <= 0.0)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.ValueOutOfBounds,
                        Path = $"{path}/radius",
                        Constraint = "a disc electrode must have positive radius",
                        Observed = new ObservedValue(radius.Value.SiValue, "m"),
                        Suggestion = "supply a positive radius",
                    });
                    return null;
                }

                return new CompiledElectrode
                {
                    Name = name,
                    Shape = ElectrodeShape.Disc,
                    CentreX = centreX.Value.SiValue,
                    CentreY = centreY.Value.SiValue,
                    Radius = radius.Value.SiValue,
                    Potential = potential.Value.SiValue,
                    Taps = taps,
                };
            }

            case "polygon":
            {
                var potential = TryQuantity(electrode.Potential, $"{path}/potential", volt, p, errors);
                var taps = Taps(electrode, drives, path, p, errors);

                var vertices = CompileVertices(electrode.Vertices, name, path, p, errors);

                if (vertices is null || potential is null)
                {
                    return null;
                }

                return new CompiledElectrode
                {
                    Name = name,
                    Shape = ElectrodeShape.Polygon,
                    Vertices = vertices,
                    MinX = vertices.Min(v => v.X),
                    MinY = vertices.Min(v => v.Y),
                    MaxX = vertices.Max(v => v.X),
                    MaxY = vertices.Max(v => v.Y),
                    Potential = potential.Value.SiValue,
                    Taps = taps,
                };
            }

            case "edgeProfile":
            {
                if (electrode.Profile is null || electrode.Profile.Count < 2)
                {
                    errors.Add(Missing($"{path}/profile",
                        "an edge profile needs at least two points to interpolate between",
                        "add a list of {at, potential} pairs"));
                    return null;
                }

                var edge = electrode.Edge switch
                {
                    "left" => GridEdge.Left,
                    "right" => GridEdge.Right,
                    "bottom" => GridEdge.Bottom,
                    "top" => GridEdge.Top,
                    _ => (GridEdge?)null,
                };

                if (edge is null)
                {
                    errors.Add(new EinzelError
                    {
                        Code = ErrorCodes.SchemaInvalid,
                        Path = $"{path}/edge",
                        Constraint = "an edge profile must name one of: left, right, bottom, top",
                        Observed = new ObservedValue(0.0, electrode.Edge ?? "null"),
                        Suggestion = "'top' and 'bottom' are the facing boards of a planar geometry",
                    });
                    return null;
                }

                var points = new List<(double At, double Potential)>(electrode.Profile.Count);

                for (var k = 0; k < electrode.Profile.Count; k++)
                {
                    var at = TryQuantity(electrode.Profile[k].At, $"{path}/profile/{k}/at", length, p, errors);
                    var potential = TryQuantity(
                        electrode.Profile[k].Potential, $"{path}/profile/{k}/potential", volt, p, errors);

                    if (at is not null && potential is not null)
                    {
                        points.Add((at.Value.SiValue, potential.Value.SiValue));
                    }
                }

                if (points.Count != electrode.Profile.Count)
                {
                    return null;
                }

                points.Sort((a, b) => a.At.CompareTo(b.At));

                return new CompiledElectrode
                {
                    Name = name,
                    Shape = ElectrodeShape.EdgeProfile,
                    Edge = edge.Value,
                    Profile = points,
                };
            }

            default:
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{path}/shape",
                    Constraint = "an electrode must declare one of: rectangle, disc, polygon, edgeProfile",
                    Observed = new ObservedValue(0.0, electrode.Shape ?? "null"),
                    Suggestion = "'disc' is a round rod in cross-section, 'polygon' is any other outline, 'edgeProfile' is a printed board",
                });
                return null;
        }
    }

    /// <summary>
    /// Compiles a declared outline - single vertices and runs - into distinct vertices in
    /// metres, refusing what would vanish or be ambiguous in a solve. Shared by the polygon
    /// and the prism, which are the same outline with and without a length.
    /// </summary>
    private static List<(double X, double Y)>? CompileVertices(
        IReadOnlyList<VertexDocument>? declared,
        string name,
        string path,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        var length = Dimension.LengthDimension;
        if (declared is null || declared.Count == 0)
        {
            errors.Add(Missing($"{path}/vertices",
                "a polygon needs at least three vertices to enclose anything",
                "add a list of {x, y} vertices in order round the outline"));
            return null;
        }

        var vertices = new List<(double X, double Y)>(declared.Count);
        var complete = true;
        for (var k = 0; k < declared.Count; k++)
        {
            var declaredVertex = declared[k];
            var vertexPath = $"{path}/vertices/{k.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

            if (declaredVertex.Count is null)
            {
                var vx = TryQuantity(declaredVertex.X, $"{vertexPath}/x", length, p, errors);
                var vy = TryQuantity(declaredVertex.Y, $"{vertexPath}/y", length, p, errors);
                if (vx is not null && vy is not null)
                {
                    vertices.Add((vx.Value.SiValue, vy.Value.SiValue));
                }
                else
                {
                    complete = false;
                }

                continue;
            }

            // A run: the same pair of expressions evaluated with an index bound
            // from zero to count - 1. This is how a curved face is written -
            // the hyperbola of a quadrupole rod is one run of twenty-five points
            // rather than twenty-five copies of the same formula - and it is the
            // repeat mechanism's idea applied inside one electrode.
            var count = TryQuantity(declaredVertex.Count, $"{vertexPath}/count", Dimension.Dimensionless, p, errors);
            if (count is null)
            {
                complete = false;
                continue;
            }

            var points = (int)Math.Round(count.Value.SiValue);
            if (points < 2 || Math.Abs(count.Value.SiValue - points) > 1e-9)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{vertexPath}/count",
                    Constraint = "a vertex run needs a whole count of at least two",
                    Observed = new ObservedValue(count.Value.SiValue, "1"),
                    Suggestion = "a run of one point is a point - write it without a count",
                });
                complete = false;
                continue;
            }

            var index = declaredVertex.Index ?? "index";
            if (p.ContainsKey(index))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{vertexPath}/index",
                    Constraint = $"'{index}' is already a declared parameter, and binding it here would shadow it",
                    Observed = new ObservedValue(0.0, index),
                    Suggestion = "choose another index name, such as 'k' or 'point'",
                });
                complete = false;
                continue;
            }

            for (var i = 0; i < points; i++)
            {
                var scoped = new Dictionary<string, Quantity>(p, StringComparer.Ordinal)
                {
                    [index] = Quantity.Si(i, Dimension.Dimensionless),
                };
                var vx = TryQuantity(declaredVertex.X, $"{vertexPath}/x", length, scoped, errors);
                var vy = TryQuantity(declaredVertex.Y, $"{vertexPath}/y", length, scoped, errors);
                if (vx is null || vy is null)
                {
                    // One report per run rather than one per point: the same
                    // expression fails the same way at every index.
                    complete = false;
                    break;
                }

                vertices.Add((vx.Value.SiValue, vy.Value.SiValue));
            }
        }

        if (!complete)
        {
            return null;
        }

        if (vertices.Count < 3)
        {
            errors.Add(Missing($"{path}/vertices",
                "a polygon needs at least three vertices to enclose anything",
                "add a list of {x, y} vertices in order round the outline"));
            return null;
        }

        // Consecutive vertices in the same place are merged. A parametric outline
        // produces them whenever a feature collapses - a slot of zero height puts
        // the channel's two corners on one point - exactly as a rectangle of zero
        // extent is a legitimate infinitely thin plate. A zero-length edge is
        // harmless to the signed distance (it is a point) and to the crossing test
        // (its determinant is zero and it is skipped), so nothing downstream
        // needs it gone; it is removed so the vertex count below means distinct
        // vertices.
        for (var k = vertices.Count - 1; k >= 0 && vertices.Count > 1; k--)
        {
            var next = vertices[(k + 1) % vertices.Count];
            if (vertices[k].X == next.X && vertices[k].Y == next.Y)
            {
                vertices.RemoveAt(k);
            }
        }

        if (vertices.Count < 3)
        {
            errors.Add(Missing($"{path}/vertices",
                "a polygon needs at least three distinct vertices to enclose anything",
                "add a list of {x, y} vertices in order round the outline"));
            return null;
        }

        // Zero area is a polygon that has collapsed to a line: it fixes no
        // node and cuts no link, so it vanishes from the solve exactly as an
        // inverted rectangle does.
        var twiceArea = 0.0;
        for (var k = 0; k < vertices.Count; k++)
        {
            var a = vertices[k];
            var b = vertices[(k + 1) % vertices.Count];
            twiceArea += (a.X * b.Y) - (b.X * a.Y);
        }

        if (twiceArea == 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/vertices",
                Constraint = $"polygon '{name}' encloses no area, and a polygon with no area vanishes from the solve rather than failing",
                Observed = new ObservedValue(0.0, "m^2"),
                Suggestion = "check the expressions that place the vertices; a derived width can go to zero when one parameter reaches another",
            });
            return null;
        }

        // A self-intersecting outline has no single inside: the even-odd rule
        // gives one answer and a winding rule another, and a solver handed
        // either would return the field of a geometry nobody described.
        if (SelfIntersects(vertices, out var edgeA, out var edgeB))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = $"{path}/vertices",
                Constraint = $"polygon '{name}' crosses itself: the edge from vertex {edgeA} crosses the edge from vertex {edgeB}",
                Observed = new ObservedValue(vertices.Count, "vertices"),
                Suggestion = "order the vertices round the outline; a bow-tie is two triangles, not one polygon",
            });
            return null;
        }

        return vertices;
    }

    /// <summary>
    /// Whether any two non-adjacent edges of a closed outline cross properly.
    /// </summary>
    /// <remarks>
    /// Proper crossings only: two edges meeting at a shared vertex are adjacent and
    /// skipped, and an edge touching another at an endpoint is a degenerate outline
    /// rather than a self-crossing one. Quadratic in the vertex count, which for the
    /// tens of vertices a curved face needs costs nothing.
    /// </remarks>
    private static bool SelfIntersects(List<(double X, double Y)> v, out int edgeA, out int edgeB)
    {
        var n = v.Count;
        for (var i = 0; i < n; i++)
        {
            var (ax, ay) = v[i];
            var (bx, by) = v[(i + 1) % n];
            for (var j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                {
                    continue;   // the closing edge is adjacent to the first
                }

                var (cx, cy) = v[j];
                var (dx, dy) = v[(j + 1) % n];
                var d1 = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
                var d2 = ((bx - ax) * (dy - ay)) - ((by - ay) * (dx - ax));
                var d3 = ((dx - cx) * (ay - cy)) - ((dy - cy) * (ax - cx));
                var d4 = ((dx - cx) * (by - cy)) - ((dy - cy) * (bx - cx));
                if (((d1 > 0.0 && d2 < 0.0) || (d1 < 0.0 && d2 > 0.0))
                    && ((d3 > 0.0 && d4 < 0.0) || (d3 < 0.0 && d4 > 0.0)))
                {
                    edgeA = i;
                    edgeB = j;
                    return true;
                }
            }
        }

        edgeA = -1;
        edgeB = -1;
        return false;
    }


    private static (Vec3 Point, Vec3 Normal)? ValidateDetector(DetectorDocument? detector, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (detector is null)
        {
            errors.Add(Missing("/detector", "a model must declare the surface that ends the flight",
                "add a \"detector\" object with \"planePoint\" and \"normal\""));
            return null;
        }

        var point = TryVector(detector.PlanePoint, "/detector/planePoint", Dimension.LengthDimension, p, errors);
        var normal = TryDirection(detector.Normal, "/detector/normal", errors, p);

        return point is null || normal is null ? null : (point.Value, normal.Value);
    }

    private sealed record TransportValues(
        string Mode,
        double RelativeTolerance,
        double MaximumFlightTime,
        double SampleInterval,
        CompiledGas Gas,
        CompiledMobility? Mobility,
        CompiledDensityGrid? DensityGrid,
        string SpaceCharge,
        CompiledSpaceChargeGrid? SpaceChargeGrid,
        CompiledDensityStep DensityStep,
        bool LandOnEdges);

    /// <summary>Every transport mode this run uses, the model's and every phase's.</summary>
    /// <remarks>
    /// A phase that names no mode keeps the model's, so the model's is always in the set.
    /// What this adds is the modes a sequence introduces - which is what makes a
    /// requirement like "the diffusive mode needs a gas" attach to the run rather than to
    /// one declaration in it.
    /// </remarks>
    private static HashSet<string> Modes(
        ModelDocument document, IReadOnlyList<PhaseSurface> timeline)
    {
        var modes = new HashSet<string>(StringComparer.Ordinal);

        if (document.Transport?.Mode is { } declared)
        {
            modes.Add(declared);
        }

        foreach (var phase in timeline)
        {
            modes.Add(phase.Mode ?? document.Transport?.Mode ?? "trajectory");
        }

        return modes;
    }

    private static TransportValues? ValidateTransport(TransportDocument? transport, IReadOnlyDictionary<string, Quantity> p, HashSet<string> modes, List<EinzelError> errors)
    {
        if (transport is null)
        {
            errors.Add(Missing("/transport", "a model must declare its transport mode and limits",
                "add a \"transport\" object with \"mode\" and \"maximumFlightTime\""));
            return null;
        }

        if (transport.Mode is not ("trajectory" or "diffusion"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/mode",
                Constraint = "a transport mode is one of 'trajectory' or 'diffusion'",
                Observed = new ObservedValue(0.0, transport.Mode),
                Suggestion = transport.Mode == "statisticalDiffusion"
                    ? "statistical diffusion is spelled \"diffusion\""
                    : "'trajectory' below about 1e-2 mbar, 'diffusion' above about 1e-3",
            });
            return null;
        }

        if (transport.RelativeTolerance is <= 0.0 or > 1e-3 || !double.IsFinite(transport.RelativeTolerance))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/relativeTolerance",
                Constraint = "the relative tolerance must lie in (0, 1e-3]",
                Observed = new ObservedValue(transport.RelativeTolerance, "1"),
                Suggestion = "1e-11 is the default and meets ACC-1 with margin on an analytic mirror",
            });
            return null;
        }

        if (transport.MaximumFlightTime is null)
        {
            errors.Add(Missing("/transport/maximumFlightTime",
                "a flight-time ceiling is required, so a mis-specified model cannot run forever",
                "add {\"value\": 1, \"unit\": \"ms\"}"));
            return null;
        }

        var ceiling = TryQuantity(
            transport.MaximumFlightTime, "/transport/maximumFlightTime", Dimension.TimeDimension, p, errors);

        if (ceiling is null)
        {
            return null;
        }

        if (ceiling.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/maximumFlightTime",
                Constraint = "the flight-time ceiling must be positive",
                Observed = new ObservedValue(transport.MaximumFlightTime.Value, transport.MaximumFlightTime.Unit),
                Suggestion = "supply a positive ceiling, for example {\"value\": 1, \"unit\": \"ms\"}",
            });
            return null;
        }

        var sample = ceiling.Value.SiValue / 2000.0;

        if (transport.SampleInterval is not null)
        {
            var declared = TryQuantity(
                transport.SampleInterval, "/transport/sampleInterval", Dimension.TimeDimension, p, errors);

            if (declared is null)
            {
                return null;
            }

            if (declared.Value.SiValue <= 0.0)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = "/transport/sampleInterval",
                    Constraint = "the trajectory sampling interval must be positive",
                    Observed = new ObservedValue(transport.SampleInterval.Value, transport.SampleInterval.Unit),
                    Suggestion = "omit it to sample the flight at about two thousand points",
                });
                return null;
            }

            sample = declared.Value.SiValue;
        }

        var gas = ValidateGas(transport.Gas, p, errors);

        if (gas is null)
        {
            return null;
        }

        var mobility = ValidateMobility(transport.Mobility, "/transport/mobility", gas, p, errors);
        var densityGrid = ValidateDensityGrid(transport.DensityGrid, p, errors);

        if (modes.Contains("diffusion"))
        {
            if (!gas.IsPresent)
            {
                errors.Add(Missing("/transport/gas",
                    "the diffusive mode describes ions moving through a gas, so there has to be one",
                    "add a gas block, or use \"mode\": \"trajectory\" for vacuum"));
                return null;
            }

            if (mobility is null)
            {
                errors.Add(Missing("/transport/mobility",
                    "the diffusive mode needs a mobility, and the gas declares no cross section "
                    + "to derive one from",
                    "add {\"zeroField\": {\"value\": 2.0, \"unit\": \"cm^2/(V s)\"}}, or give the "
                    + "gas a crossSection"));
                return null;
            }
        }

        if (transport.SpaceCharge is not ("none" or "direct" or "pic" or "meanField"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/spaceCharge",
                Constraint = "space charge is modelled by one of the methods this build has",
                Observed = new ObservedValue(0.0, transport.SpaceCharge),
                Suggestion = "\"none\" flies each ion through a field that does not know the others "
                    + "exist; \"direct\" sums every pair, which is the reference method and costs "
                    + "the square of the trajectory count; \"pic\" deposits the packet onto its own "
                    + "grid and solves once, which is cheaper above about 850 trajectories and "
                    + "dearer below; \"meanField\" solves the density's own charge as a potential on "
                    + "the grid it is already tracked on, which is the diffusive mode's method",
            });

            return null;
        }

        // A method that cannot act is refused rather than ignored. "direct" and "pic" fly
        // particles and there are none in a density; "meanField" is a continuum and there is
        // no continuum in a set of trajectories. A sequenced model may legitimately use both
        // modes, so the test is against the modes the run actually reaches rather than
        // against the declared one - the same question the diffusive requirements ask.
        var particleMethod = transport.SpaceCharge is "direct" or "pic";
        var meanFieldMethod = transport.SpaceCharge is "meanField";
        var anyTrajectory = modes.Contains("trajectory");
        var anyDiffusion = modes.Contains("diffusion");

        if ((particleMethod && !anyTrajectory) || (meanFieldMethod && !anyDiffusion))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/spaceCharge",
                Constraint = particleMethod
                    ? $"'{transport.SpaceCharge}' pushes trajectories on one another and this run has "
                        + "none: every phase is diffusive, which computes a density"
                    : "'meanField' solves the charge of a density and this run has none: every phase "
                        + "is a trajectory",
                Observed = new ObservedValue(0.0, transport.SpaceCharge),
                Suggestion = particleMethod
                    ? "use \"meanField\" for a diffusive run, or give the model a trajectory phase"
                    : "use \"direct\" or \"pic\" for a trajectory run, or set "
                        + "\"transport\": { \"mode\": \"diffusion\" }",
            });

            return null;
        }

        // Case-sensitive like every other enumerated value in a document: "Land" is a
        // typo, and reading it as either value would fly a document its author did not
        // write.
        if (transport.Edges is not (null or "cap" or "land"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/edges",
                Constraint = "a waveform edge is met by one of the methods this build has",
                Observed = new ObservedValue(0.0, transport.Edges),
                Suggestion = "\"cap\", the default, caps each step at a twentieth of the drive period "
                    + "and lands on stage switches only, as every earlier version did; \"land\" also "
                    + "lands on every declared waveform breakpoint, at the instant the field flips",
            });

            return null;
        }

        var chargeGrid = SpaceChargeGrid(transport, errors);
        var densityStep = DensityStep(transport, modes, errors);

        return new TransportValues(
            transport.Mode, transport.RelativeTolerance, ceiling.Value.SiValue, sample,
            gas, mobility, densityGrid, transport.SpaceCharge, chargeGrid, densityStep,
            transport.Edges is "land");
    }

    /// <summary>
    /// Refuses a space-charge model that has nothing to compute or cannot be run.
    /// </summary>
    /// <remarks>
    /// Two ways to ask for the mutual force and not get it, both of which would
    /// otherwise run and report a number: a single trajectory has nobody to push
    /// on, and a packet with no spatial extent has an unbounded self-field rather
    /// than a large one. Refusing is better than either, because each would produce
    /// a result that looks like the one asked for. A declared gas used to be a third
    /// refusal, because the packet integrator had no collision hook; it has one now,
    /// and the run reports what a macroparticle's collision stands for.
    /// </remarks>
    /// <summary>
    /// Validates the density time-stepping choice, and refuses one that does nothing.
    /// </summary>
    /// <remarks>
    /// Only the diffusive mode has a density to step, so a block against a trajectory
    /// model is refused rather than ignored - the same rule an unrecognised property
    /// follows. A gain above one against the explicit scheme is refused for a sharper
    /// reason: the explicit scheme is bounded by its own stability limit and cannot
    /// take a longer step, so honouring the block would mean silently ignoring half of
    /// it, and the author would conclude the scheme is slow rather than that the
    /// request went nowhere.
    /// </remarks>
    private static CompiledDensityStep DensityStep(
        TransportDocument transport, HashSet<string> modes, List<EinzelError> errors)
    {
        var fallback = new CompiledDensityStep("explicit", 1.0);

        if (transport.DensityStep is not { } step)
        {
            return fallback;
        }

        if (!modes.Contains("diffusion"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/densityStep",
                Constraint =
                    "only the diffusive mode has a density to step, and no phase of this "
                    + $"run is diffusive - the model asks for '{transport.Mode}'",
                Observed = new ObservedValue(0.0, transport.Mode),
                Suggestion = "set \"mode\": \"diffusion\" to use this block, or remove it",
            });

            return fallback;
        }

        if (step.Scheme is not ("explicit" or "implicit"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/densityStep/scheme",
                Constraint = "a density is stepped by one of the schemes this build has",
                Observed = new ObservedValue(0.0, step.Scheme),
                Suggestion = "\"explicit\" is forward Euler, bounded by the faster of the "
                    + "diffusion and Courant limits; \"implicit\" is backward Euler, which has "
                    + "no stability limit and charges Gauss-Seidel sweeps instead",
            });

            return fallback;
        }

        var gain = step.Gain ?? 1.0;

        if (gain < 1.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/densityStep/gain",
                Constraint = "a gain below one asks for a shorter step than stability needs, "
                    + "which costs accuracy nothing and time everything",
                Observed = new ObservedValue(gain, "1"),
                Suggestion = "1 steps at the stability limit; the shipped funnel runs 10.8x "
                    + "faster at 64 for 0.108% error",
            });

            return fallback;
        }

        if (gain > 1.0 && !string.Equals(step.Scheme, "implicit", StringComparison.Ordinal))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/densityStep/gain",
                Constraint =
                    "the explicit scheme is bounded by its own stability limit and cannot take "
                    + "a longer step",
                Observed = new ObservedValue(gain, "1"),
                Suggestion = "set \"scheme\": \"implicit\" to use a gain, or remove it",
            });

            return fallback;
        }

        return errors.Count > 0 ? fallback : new CompiledDensityStep(step.Scheme, gain);
    }

    /// <summary>Validates the particle-in-cell grid, and refuses one that does nothing.</summary>
    /// <remarks>
    /// A block declared against a method that cannot use it is refused rather than
    /// ignored, which is the same rule an unrecognised property already follows: a
    /// document that configures a solve it is not running has been misunderstood by
    /// its author, and silence is the expensive answer.
    /// </remarks>
    private static CompiledSpaceChargeGrid? SpaceChargeGrid(
        TransportDocument transport, List<EinzelError> errors)
    {
        if (transport.SpaceChargeGrid is not { } grid)
        {
            return string.Equals(transport.SpaceCharge, "pic", StringComparison.Ordinal)
                ? new CompiledSpaceChargeGrid(32, 4.0, 0.05)
                : null;
        }

        if (!string.Equals(transport.SpaceCharge, "pic", StringComparison.Ordinal))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/spaceChargeGrid",
                Constraint =
                    "only the particle-in-cell method uses a grid, and this model asks for "
                    + $"'{transport.SpaceCharge}'",
                Observed = new ObservedValue(0.0, transport.SpaceCharge),
                Suggestion = "set \"spaceCharge\": \"pic\" to use this block, or remove it",
            });

            return null;
        }

        var nodes = grid.Nodes ?? 32;
        var padding = grid.Padding ?? 4.0;
        var refresh = grid.RefreshTolerance ?? 0.05;

        if (nodes < 8)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/spaceChargeGrid/nodes",
                Constraint = "a packet needs at least eight nodes across its box to be resolved at all",
                Observed = new ObservedValue(nodes, "1"),
                Suggestion = "32 is the default and resolves a packet's radius with a few cells",
            });
        }
        else if (PicNodes(nodes) > VolumeMesh.MaximumNodes)
        {
            // The same limit a solve3d meets, and for the same reason it is refused here: the
            // packet's grid is a volume solve, allocated by the same field type, so past the
            // limit it would have reached the same argument exception mid-run - after its mask.
            var intervals = VolumeMesh.Intervals(2.0, 2.0 / nodes);
            var most = VolumeMesh.MostNodesAcrossACube;

            errors.Add(new EinzelError
            {
                Code = ErrorCodes.GridTooLarge,
                Path = "/transport/spaceChargeGrid/nodes",
                Constraint = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"a volume solve may hold at most {VolumeMesh.MaximumNodes:N0} nodes, and "
                    + $"{nodes} nodes across rounds up to {intervals} intervals a side, which is "
                    + $"{PicNodes(nodes):N0}"),
                Observed = new ObservedValue(PicNodes(nodes), "nodes"),
                Suggestion = string.Create(
                    System.Globalization.CultureInfo.InvariantCulture,
                    $"{most} is the most that fits, {PicNodes(most):N0} nodes; above it the count "
                    + $"rounds to the next power of two"),
            });
        }

        if (padding <= 1.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/spaceChargeGrid/padding",
                Constraint =
                    "the box must be wider than the packet, or its earthed walls are inside the charge",
                Observed = new ObservedValue(padding, "1"),
                Suggestion = "4 is the default: a box four RMS radii across, which stands in for "
                    + "free space well because the packet sits at its centre",
            });
        }

        if (refresh <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/spaceChargeGrid/refreshTolerance",
                Constraint = "a tolerance of zero re-solves at every stage, which is what the grid "
                    + "method exists to avoid",
                Observed = new ObservedValue(refresh, "1"),
                Suggestion = "0.05 is the default: re-solve when the packet's RMS radius has moved "
                    + "five per cent",
            });
        }

        return errors.Count > 0 ? null : new CompiledSpaceChargeGrid(nodes, padding, refresh);
    }

    /// <summary>Nodes in a particle-in-cell grid declared with this many nodes across.</summary>
    /// <remarks>
    /// The packet's grid is a cube meshed at its width over the declared count, by the same
    /// rounding every volume grid uses - so this asks that rule, rather than cubing the count,
    /// which would be the declared number and not the one allocated.
    /// </remarks>
    private static long PicNodes(int nodes) => VolumeMesh.Nodes(2.0, 2.0, 2.0, 2.0 / nodes);

    private static void ValidateSpaceChargeIsComputable(CompiledModel model, List<EinzelError> errors)
    {
        if (!model.ModelsSpaceCharge)
        {
            return;
        }

        if (model.Cloud.Ions < 2)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/spaceCharge",
                Constraint = "the mutual force needs at least two trajectories to act between",
                Observed = new ObservedValue(model.Cloud.Ions, "1"),
                Suggestion = "declare a source cloud with \"ions\" of 2 or more, or set "
                    + "\"spaceCharge\": \"none\"",
            });
        }

        var extent = (model.Cloud.TransverseSpreadM * model.Cloud.TransverseSpreadM * 2.0)
            + (model.Cloud.LongitudinalSpreadM * model.Cloud.LongitudinalSpreadM);

        if (extent <= 0.0 && model.Cloud.Ions >= 2)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/cloud",
                Constraint = "a packet at a single point has an unbounded self-field, not a large one",
                Observed = new ObservedValue(0.0, "m"),
                Suggestion = "give the cloud a transverseSpread or a longitudinalSpread",
            });
        }

    }


    /// <summary>Validates a declared mixture, and the ways of declaring one twice.</summary>
    /// <remarks>
    /// <para>
    /// A mixture is not a convenience over running a model several times: the populations are
    /// coupled through the potential their total charge raises, which is a thing no sequence of
    /// separate runs computes. So what has to be true of the document is that each population is
    /// completely described - what it is, how it moves, how much of it there is - and that
    /// nothing anywhere else claims to describe the same thing.
    /// </para>
    /// <para>
    /// <b>Every refusal here is a document saying two things.</b> <c>ion</c> beside
    /// <c>species</c> says two different things about what is being transported;
    /// <c>transport.mobility</c> beside <c>species</c> says how "the" ion moves when there
    /// is no "the" ion; a cloud population beside species populations says how many ions
    /// there are twice. None has a reading under which one is a default for the other, so
    /// none is merged.
    /// </para>
    /// </remarks>
    private static List<CompiledSpecies> ValidateSpecies(
        ModelDocument document,
        CompiledGas gas,
        IReadOnlyCollection<string> modes,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        var declared = document.Species;

        if (declared is null || declared.Count == 0)
        {
            return [];
        }

        if (document.Ion is not null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/species",
                Constraint = "a model declares either one ion or several species, never both",
                Suggestion = "remove \"ion\", or remove \"species\" and describe the single ion with "
                    + "\"ion\" and \"transport\": { \"mobility\": ... }",
            });
            return [];
        }

        if (document.Transport?.Mobility is not null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/mobility",
                Constraint = "a mixture carries a mobility per species, so there is no single one "
                    + "for this to be",
                Suggestion = "move it onto the species it belongs to, or omit it and let each "
                    + "species derive its own from the gas cross section",
            });
            return [];
        }

        // A density per species is what a mixture is stepped as, and there is no trajectory
        // equivalent in this build: the packet integrator flies one species at a time. Refused
        // by name rather than run as the first population, which would separate nothing and
        // look exactly like a result.
        if (!modes.Contains("diffusion"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/species",
                Constraint = "several ion populations are stepped as several densities, which is the "
                    + "diffusive mode; this run has no diffusive phase",
                Suggestion = "set \"transport\": { \"mode\": \"diffusion\" }, or give the sequence a "
                    + "diffusive phase, or model one ion at a time with \"ion\"",
            });
            return [];
        }

        // The cloud's spatial spread still shapes every species' seed - that is geometry, and one
        // source really does emit them all from one place. Its POPULATION is the singular form of
        // exactly what each species now declares, so a document carrying both says how many ions
        // there are twice.
        if (document.Source?.Cloud?.Population is not null)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/source/cloud/population",
                Constraint = "a mixture's populations are declared per species, so the source cannot "
                    + "also declare one",
                Observed = new ObservedValue(document.Source.Cloud.Population.Value, "1"),
                Suggestion = "remove it; the cloud's spreads still set the shape every species is "
                    + "seeded with",
            });
            return [];
        }

        // A sequenced run takes its own path, and that path steps ONE density: it would
        // run the first population's mass against a derived mobility, produce one packet
        // and report nothing about the rest. Refused by name rather than run, because a
        // mixture with an elution ramp is precisely what this feature is for and getting
        // one population back would look exactly like getting the right answer.
        if (document.Sequence is { Count: > 0 }
            || (document.Fields?.Any(f => f?.Solve?.Stages is { Count: > 0 }
                || f?.Solve3d?.Stages is { Count: > 0 }) ?? false))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/species",
                Constraint = "a sequenced run steps one density, so it cannot carry several ion "
                    + "populations in this build",
                Suggestion = "run the sequence with a single \"ion\", or run the mixture without a "
                    + "sequence. Stepping several populations through a timeline needs the "
                    + "sequenced path to take the mixture stepper, which it does not yet",
            });
            return [];
        }

        if (declared.Count < 2)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/species",
                Constraint = "a mixture is two or more populations; one is an ion",
                Observed = new ObservedValue(declared.Count, "1"),
                Suggestion = "add the other populations, or describe this one with \"ion\" and "
                    + "\"transport\": { \"mobility\": ... }",
            });
            return [];
        }

        var compiled = new List<CompiledSpecies>(declared.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < declared.Count; i++)
        {
            var member = declared[i];
            var at = $"/species/{i}";

            if (string.IsNullOrWhiteSpace(member.Name))
            {
                errors.Add(Missing($"{at}/name",
                    "every species needs a name, because a mixture's results are reported by it",
                    "add {\"name\": \"peptide-2+\"}"));
                continue;
            }

            if (!seen.Add(member.Name))
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.SchemaInvalid,
                    Path = $"{at}/name",
                    Constraint = "species names have to be distinct, because results are reported "
                        + "under them",
                    Observed = new ObservedValue(0.0, member.Name),
                    Suggestion = "give this one a different name",
                });
                continue;
            }

            if (member.ChargeNumber == 0)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{at}/chargeNumber",
                    Constraint = "an ion cannot have zero charge",
                    Observed = new ObservedValue(0, "1"),
                    Suggestion = "use 1 for a singly charged cation",
                });
                continue;
            }

            if (member.MassToCharge is null)
            {
                errors.Add(Missing($"{at}/massToCharge",
                    "the species' mass-to-charge ratio is required",
                    "add {\"value\": 500, \"unit\": \"Da\"}"));
                continue;
            }

            var massToCharge = TryQuantity(
                member.MassToCharge, $"{at}/massToCharge", Dimension.MassDimension, p, errors);

            if (massToCharge is null)
            {
                continue;
            }

            if (massToCharge.Value.SiValue <= 0.0)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{at}/massToCharge",
                    Constraint = "mass-to-charge must be positive",
                    Observed = new ObservedValue(member.MassToCharge.Value, member.MassToCharge.Unit),
                    Suggestion = "supply a positive mass-to-charge ratio",
                });
                continue;
            }

            // Required, not defaulted. What a mixture is for is that the populations push on one
            // another, and a population defaulted to zero would be present in the separation and
            // absent from the space charge - the two halves of the same run disagreeing about
            // whether it is there.
            if (member.Population <= 0.0)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = $"{at}/population",
                    Constraint = "every species in a mixture needs a population, because the "
                        + "populations are what act on one another",
                    Observed = new ObservedValue(member.Population, "1"),
                    Suggestion = "add {\"population\": 1e6}",
                });
                continue;
            }

            var mobility = ValidateMobility(member.Mobility, $"{at}/mobility", gas, p, errors);

            if (mobility is null)
            {
                errors.Add(Missing($"{at}/mobility",
                    $"'{member.Name}' has no mobility and none can be derived: the gas declares no "
                    + "cross section",
                    "declare a mobility on the species, or give the gas a cross section"));
                continue;
            }

            compiled.Add(new CompiledSpecies(
                member.Name,
                massToCharge.Value.SiValue * Math.Abs(member.ChargeNumber),
                Quantity.From(member.ChargeNumber, "e").SiValue,
                mobility,
                member.Population));
        }

        return compiled;
    }

    /// <summary>Validates the declared mobility, or derives one from the gas.</summary>
    /// <remarks>
    /// TRN-1 wants it declared. Deriving it from a cross section is offered because a
    /// model that already declares one for the event-driven mode should not have to
    /// declare a second independent number to run the diffusive one - and because the
    /// two modes then describe the same gas, which is what REG-3's comparison needs
    /// to mean anything. The derivation is marked, so a result computed from it can
    /// say so.
    /// </remarks>
    private static CompiledMobility? ValidateMobility(
        MobilityDocument? declaredMobility,
        string path,
        CompiledGas gas,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors)
    {
        if (declaredMobility?.ZeroField is null)
        {
            // Mason-Schamp, from the cross section, when there is one.
            if (!gas.IsPresent || gas.CrossSectionSi <= 0.0)
            {
                return null;
            }

            return new CompiledMobility(0.0, 0.0, 50.0, Derived: true);
        }

        var declared = declaredMobility;

        var zeroField = TryQuantity(
            declared.ZeroField, path + "/zeroField", Dimension.Mobility, p, errors);

        if (zeroField is null)
        {
            return null;
        }

        if (zeroField.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path + "/zeroField",
                Constraint = "a mobility must be positive",
                Suggestion = "an ion drifts along the field, not against it; the charge sign is "
                    + "carried by the ion rather than by the mobility",
            });
            return null;
        }

        return new CompiledMobility(
            zeroField.Value.SiValue, declared.Alpha, declared.ValidToTownsend, Derived: false);
    }

    /// <summary>Validates the declared density grid, if there is one.</summary>
    private static CompiledDensityGrid? ValidateDensityGrid(
        DensityGridDocument? grid, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (grid is null)
        {
            return null;
        }

        var minX = TryQuantity(grid.MinX, "/transport/densityGrid/minX", Dimension.LengthDimension, p, errors);
        var minY = TryQuantity(grid.MinY, "/transport/densityGrid/minY", Dimension.LengthDimension, p, errors);
        var maxX = TryQuantity(grid.MaxX, "/transport/densityGrid/maxX", Dimension.LengthDimension, p, errors);
        var maxY = TryQuantity(grid.MaxY, "/transport/densityGrid/maxY", Dimension.LengthDimension, p, errors);

        if (minX is null || minY is null || maxX is null || maxY is null)
        {
            return null;
        }

        if (maxX.Value.SiValue <= minX.Value.SiValue || maxY.Value.SiValue <= minY.Value.SiValue)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/densityGrid",
                Constraint = "a density grid needs a positive extent in both directions",
                Suggestion = "check that maxX exceeds minX and maxY exceeds minY",
            });
            return null;
        }

        return new CompiledDensityGrid(
            minX.Value.SiValue, minY.Value.SiValue, maxX.Value.SiValue, maxY.Value.SiValue,
            Math.Max(4, grid.IntervalsX), Math.Max(4, grid.IntervalsY));
    }

    /// <summary>Validates the background gas, if one is declared.</summary>
    /// <remarks>
    /// Every field a model may omit has a defensible default except the ones that
    /// decide the physics. A gas with a model and no pressure, or a hard-sphere
    /// model and no cross section, is refused rather than silently treated as
    /// vacuum - a run that quietly ignores a declared gas is the failure that
    /// looks most like success.
    /// </remarks>
    private static CompiledGas? ValidateGas(
        GasDocument? gas, IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (gas is null || gas.Model == "none")
        {
            return CompiledGas.Vacuum;
        }

        if (gas.Model is not ("hardSphere" or "langevin"))
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/transport/gas/model",
                Constraint = "a collision model is one of 'none', 'hardSphere', or 'langevin'",
                Observed = new ObservedValue(0.0, gas.Model),
                Suggestion = "'hardSphere' below about 1e-5 mbar, 'langevin' from there to about "
                    + "1e-2 mbar; the mobility description above that is not built",
            });
            return null;
        }

        var pressure = Required(
            gas.Pressure, "/transport/gas/pressure", Dimension.Pressure, p, errors,
            "a gas must declare its pressure", "add {\"value\": 1e-6, \"unit\": \"mbar\"}");

        var mass = Required(
            gas.Mass, "/transport/gas/mass", Dimension.MassDimension, p, errors,
            "a gas must declare the mass of one neutral",
            "add {\"value\": 28.0134, \"unit\": \"Da\"} for nitrogen");

        var temperature = gas.Temperature is null
            ? Quantity.Si(300.0, Dimension.TemperatureDimension)
            : TryQuantity(gas.Temperature, "/transport/gas/temperature",
                Dimension.TemperatureDimension, p, errors);

        var crossSection = gas.CrossSection is null
            ? Quantity.Si(0.0, Dimension.Area)
            : TryQuantity(gas.CrossSection, "/transport/gas/crossSection", Dimension.Area, p, errors);

        var polarizability = gas.Polarizability is null
            ? Quantity.Si(0.0, Dimension.Volume)
            : TryQuantity(gas.Polarizability, "/transport/gas/polarizability", Dimension.Volume, p, errors);

        if (pressure is null || mass is null || temperature is null
            || crossSection is null || polarizability is null)
        {
            return null;
        }

        if (pressure.Value.SiValue <= 0.0 || temperature.Value.SiValue <= 0.0 || mass.Value.SiValue <= 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/transport/gas",
                Constraint = "a gas needs a positive pressure, temperature, and neutral mass",
                Suggestion = "set \"model\": \"none\" for vacuum rather than a pressure of zero",
            });
            return null;
        }

        if (gas.Model == "hardSphere" && crossSection.Value.SiValue <= 0.0)
        {
            errors.Add(Missing("/transport/gas/crossSection",
                "the hard-sphere model needs a collision cross section",
                "add {\"value\": 250, \"unit\": \"Å^2\"}, which is a mid-size peptide in nitrogen"));
            return null;
        }

        if (gas.Model == "langevin" && polarizability.Value.SiValue <= 0.0)
        {
            errors.Add(Missing("/transport/gas/polarizability",
                "the Langevin model needs the neutral's polarizability volume",
                "add {\"value\": 1.74, \"unit\": \"Å^3\"} for nitrogen"));
            return null;
        }

        var drift = gas.DriftVelocity is null
            ? Vec3.Zero
            : TryVector(gas.DriftVelocity, "/transport/gas/driftVelocity", Dimension.Velocity, p, errors)
                ?? Vec3.Zero;

        if (gas.VelocityField is { } flow && string.IsNullOrWhiteSpace(flow.Path))
        {
            errors.Add(Missing(
                "/transport/gas/velocityField/path",
                "a velocity field names the file it is read from",
                "add {\"path\": \"flow.vti\"}, relative to this model. VTK ImageData with ASCII "
                + "data, which is what 'einzel export' writes and what ParaView saves with the "
                + "Ascii box ticked"));

            return null;
        }

        var pressureScale = 1.0;

        if (gas.PressureField is { } graded)
        {
            if (string.IsNullOrWhiteSpace(graded.Path))
            {
                errors.Add(Missing(
                    "/transport/gas/pressureField/path",
                    "a pressure field names the file it is read from",
                    "add {\"path\": \"pressure.vti\", \"unit\": \"Pa\"}, relative to this "
                    + "model. VTK ImageData with ASCII data, which is what 'einzel export' writes "
                    + "and what ParaView saves with the Ascii box ticked"));

                return null;
            }

            // Required rather than defaulted to pascals, and this is section 9's own
            // argument rather than a new one: a file read as pascals when it holds
            // mbar is a gas a hundred times too thin, which is entirely plausible and
            // never announces itself. Vacuum work is quoted in mbar and torr at least
            // as often as in pascals.
            if (string.IsNullOrWhiteSpace(graded.Unit))
            {
                errors.Add(Missing(
                    "/transport/gas/pressureField/unit",
                    "a pressure field states what its numbers are in",
                    "add \"unit\": \"Pa\", or \"mbar\", or \"torr\". A whole array is no less "
                    + "ambiguous than a single number, and this is the same rule that makes "
                    + "{\"energy\": 4000} a validation error"));

                return null;
            }

            if (!UnitRegistry.TryResolve(graded.Unit!, out var unit) || unit is null)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.UnitsUnknown,
                    Path = "/transport/gas/pressureField/unit",
                    Constraint = $"'{graded.Unit}' is not a unit this engine knows",
                    Suggestion = "use 'Pa', 'mbar' or 'torr'",
                });

                return null;
            }

            if (unit.Dimension != Dimension.Pressure)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.UnitsIncompatible,
                    Path = "/transport/gas/pressureField/unit",
                    Constraint = $"'{graded.Unit}' is not a pressure",
                    Suggestion = "use 'Pa', 'mbar' or 'torr'. The field holds pressures, which "
                        + "become number densities through n = p/kT at the declared temperature",
                });

                return null;
            }

            pressureScale = unit.SiFactor;
        }

        return new CompiledGas
        {
            Model = gas.Model,
            PressureSi = pressure.Value.SiValue,
            TemperatureK = temperature.Value.SiValue,
            MassSi = mass.Value.SiValue,
            CrossSectionSi = crossSection.Value.SiValue,
            PolarizabilitySi = polarizability.Value.SiValue,
            DriftVelocitySi = drift,
            VelocityFieldPath = gas.VelocityField?.Path,
            VelocityFieldArray = gas.VelocityField?.Array,
            PressureFieldPath = gas.PressureField?.Path,
            PressureFieldArray = gas.PressureField?.Array,
            PressureFieldScale = pressureScale,
            Seed = gas.Seed,
        };
    }

    private static Quantity? Required(
        QuantityValue? value,
        string path,
        Dimension dimension,
        IReadOnlyDictionary<string, Quantity> p,
        List<EinzelError> errors,
        string constraint,
        string suggestion)
    {
        if (value is null)
        {
            errors.Add(Missing(path, constraint, suggestion));
            return null;
        }

        return TryQuantity(value, path, dimension, p, errors);
    }

    /// <summary>
    /// Refuses a source that starts on or inside a conductor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// GRD-4: validity is checked, not assumed. This is knowable from the declared
    /// geometry alone - an electrode's signed distance is arithmetic on the numbers
    /// in the document - so there is no reason to leave it to the integrator, which
    /// finds out by absorbing the ion at step zero.
    /// </para>
    /// <para>
    /// Left to the integrator it produced the worst shape of answer this project
    /// has: <c>validate</c> said OK and exit 0, <c>solve</c> said converged and
    /// exit 0, and only <c>run</c> objected. An agent asked to produce a model that
    /// validates and solves would have shipped one whose ion dies immediately and
    /// had two clean bills of health saying otherwise.
    /// </para>
    /// <para>
    /// Found by an agent attempting the acceptance suite, on the shipped quadrupole
    /// template, by changing the one parameter that template exists to have changed.
    /// </para>
    /// </remarks>
    private static void ValidateSourceIsNotInsideMetal(CompiledModel model, List<EinzelError> errors)
    {
        var source = model.SourcePosition;

        foreach (var element in model.Fields)
        {
            foreach (var electrode in element.Solve?.Electrodes ?? [])
            {
                if (!electrode.Contains(source.X, source.Y))
                {
                    continue;
                }

                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = "/source/position",
                    Constraint =
                        $"the source starts inside electrode '{electrode.Name}', so the ion is "
                        + "absorbed before it moves",
                    Observed = new ObservedValue(source.X, "m"),
                    Suggestion = "move the source into the space the ions fly through. If this "
                        + "model came from a template, check whether a placement is written as a "
                        + "bare length while the geometry around it is parametric - changing the "
                        + "geometry then moves the metal and leaves the source behind",
                });

                return;
            }

            foreach (var electrode in element.Solve3D?.Electrodes ?? [])
            {
                if (!electrode.Contains(source.X, source.Y, source.Z))
                {
                    continue;
                }

                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = "/source/position",
                    Constraint =
                        $"the source starts inside electrode '{electrode.Name}', so the ion is "
                        + "absorbed before it moves",
                    Observed = new ObservedValue(source.X, "m"),
                    Suggestion = "move the source into the space the ions fly through",
                });

                return;
            }
        }
    }

    private static void ValidateGeometryConsistency(CompiledModel model, List<EinzelError> errors)
    {
        ValidateSourceIsNotInsideMetal(model, errors);
        ValidateSpaceChargeIsComputable(model, errors);

        // GRD-4: validity is checked, not assumed. An ion launched on the wrong
        // side of its own detector never flies, and the resulting zero flight time
        // looks like a physics answer rather than a geometry mistake.
        var signedDistance = Vec3.Dot(model.SourcePosition - model.DetectorPoint, model.DetectorNormal);

        if (signedDistance < 0.0)
        {
            errors.Add(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = "/source/position",
                Constraint = "the source must start on the positive side of the detector plane",
                Observed = new ObservedValue(signedDistance, "m"),
                Suggestion = "reverse /detector/normal, or move the source to the other side of the plane",
            });
        }

        if (Vec3.Dot(model.SourceDirection, model.DetectorNormal) > 0.0 && signedDistance >= 0.0)
        {
            // Launching directly away from the detector is legal — a reflectron
            // does exactly that — but only if something turns the ion around.
            var hasMirror = CanAccelerate(model.Fields);

            if (!hasMirror)
            {
                errors.Add(new EinzelError
                {
                    Code = ErrorCodes.ValueOutOfBounds,
                    Path = "/source/direction",
                    Constraint =
                        "the ion is launched away from the detector and no field element can turn it around",
                    Observed = new ObservedValue(Vec3.Dot(model.SourceDirection, model.DetectorNormal), "1"),
                    Suggestion = "reverse /source/direction, or add a retarding field element",
                });
            }
        }
    }

    private static Quantity? TryQuantity(
        QuantityValue? value, string path, Dimension expected,
        IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (value is null)
        {
            errors.Add(Missing(path, $"a quantity of dimension {expected} is required here",
                "supply {\"value\": ..., \"unit\": \"...\"}"));
            return null;
        }

        try
        {
            return value.ToQuantity(path, expected, p);
        }
        catch (EinzelException failure)
        {
            errors.Add(failure.Error.At(path));
            return null;
        }
    }

    private static Vec3? TryVector(
        VectorValue? value, string path, Dimension expected,
        IReadOnlyDictionary<string, Quantity> p, List<EinzelError> errors)
    {
        if (value is null)
        {
            errors.Add(Missing(path, $"a vector of dimension {expected} is required here",
                "supply {\"value\": [x, y, z], \"unit\": \"...\"}"));
            return null;
        }

        try
        {
            return value.ToVec3(path, expected, p);
        }
        catch (EinzelException failure)
        {
            errors.Add(failure.Error.At(path));
            return null;
        }
    }

    private static Vec3? TryDirection(
        DirectionValue? value,
        string path,
        List<EinzelError> errors,
        IReadOnlyDictionary<string, Quantity>? parameters = null)
    {
        if (value is null)
        {
            errors.Add(Missing(path, "a direction is required here", "supply {\"value\": [1, 0, 0]}"));
            return null;
        }

        try
        {
            return parameters is null
                ? value.ToUnitVector(path)
                : value.ToUnitVector(path, parameters);
        }
        catch (EinzelException failure)
        {
            errors.Add(failure.Error.At(path));
            return null;
        }
    }

    private static EinzelError Missing(string path, string constraint, string suggestion) => new()
    {
        Code = ErrorCodes.SchemaInvalid,
        Path = path,
        Constraint = constraint,
        Suggestion = suggestion,
    };
}
