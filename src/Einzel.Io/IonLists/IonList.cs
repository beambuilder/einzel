using System.Text.Json;
using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Transport;

namespace Einzel.Io.IonLists;

/// <summary>An ion list expanded against a model: the rows, or the errors that stopped it.</summary>
/// <param name="Rows">The rows in launch order, or null when the list was refused.</param>
/// <param name="Errors">Every refusal, pointer-addressed into the ion list.</param>
public sealed record IonListExpansion(IReadOnlyList<IonRow>? Rows, IReadOnlyList<EinzelError> Errors)
{
    /// <summary>Whether the list expanded.</summary>
    public bool IsValid => Rows is not null && Errors.Count == 0;

    /// <summary>The seed of every generated set, in document order.</summary>
    public IReadOnlyList<long> Seeds { get; init; } = [];

    /// <summary>The document's <c>maxParallelism</c>; null when it gave none.</summary>
    public int? MaxParallelism { get; init; }
}

/// <summary>Reads ion lists and expands them into <see cref="IonRow"/>s against a model.</summary>
/// <remarks>
/// Rows come out in a fixed order - the explicit <c>ions</c> as written, then each set's
/// rows set by set - and depend on nothing but the document, the model and the seeds, so
/// a list expands to the same rows every time.
/// </remarks>
public static class IonList
{
    /// <summary>The ion-list schema version this build reads and writes.</summary>
    public const string SchemaVersion = "0.1";

    /// <summary>Parses an ion-list document, refusing unknown keys.</summary>
    /// <param name="json">The document text.</param>
    /// <returns>The document.</returns>
    /// <exception cref="EinzelException">The text is not an ion-list document.</exception>
    public static IonListDocument Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IonListDocument>(json, ModelJson.Options)
                ?? throw Refuse(ErrorCodes.SchemaInvalid, "/", "an ion list must be a JSON object");
        }
        catch (JsonException failure)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = ModelJson.PointerFrom(failure.Path),
                Constraint = failure.Message,
                Suggestion = "docs/ion-lists.md lists every key; an unknown one is refused rather "
                    + "than ignored, as in a model",
            }, failure);
        }
    }

    /// <summary>Reads an ion list from a file: <c>.csv</c> as expanded rows, anything else as JSON.</summary>
    /// <param name="path">The file.</param>
    /// <param name="model">The model the list launches into.</param>
    /// <returns>The rows, or the errors.</returns>
    /// <remarks>
    /// A CSV states every row in full and defaults nothing from the model, but it is
    /// checked against the model as a JSON list is: the model must be one a list can
    /// launch into (<see cref="CheckModel"/>), and every birth must be on the model's clock.
    /// </remarks>
    public static IonListExpansion Load(string path, CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var text = File.ReadAllText(path);

        try
        {
            if (!path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            {
                return Expand(Parse(text), model);
            }

            var read = IonListCsv.Read(text);
            List<EinzelError> errors = [.. CheckModel(model), .. read.Errors];

            for (var k = 0; k < (read.Rows?.Count ?? 0); k++)
            {
                if (Late(read.Rows![k].BirthTimeSeconds, model, $"/{k}/birth_time_s") is { } late)
                {
                    errors.Add(late);
                }
            }

            return errors.Count == 0 ? read : new IonListExpansion(null, errors);
        }
        catch (EinzelException failure)
        {
            return new IonListExpansion(null, [failure.Error]);
        }
    }

    /// <summary>
    /// Refuses a model a list cannot launch into: one without a single ion to default to,
    /// or one whose source is a cloud.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>The refusals, each at <c>/</c>; empty when the model takes a list.</returns>
    /// <remarks>
    /// Defaults come from one ion and one launch; a mixture has neither, and a model cloud
    /// would be a second statement about the launch that the list silently replaced.
    /// </remarks>
    public static IReadOnlyList<EinzelError> CheckModel(CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var errors = new List<EinzelError>();

        Check(model.Source.Ion is not null && !model.IsMixture, errors, ErrorCodes.SchemaInvalid, "/",
            "the model declares a mixture under 'species'; an ion list takes its defaults from one 'ion'");
        Check(!model.Cloud.IsCloud, errors, ErrorCodes.SchemaInvalid, "/",
            "the model declares a source cloud; an ion list states the launch itself, so the "
            + "model's cloud belongs in the list as a 'cloud' set");

        return errors;
    }

    /// <summary>Expands a document into rows, taking every omitted attribute from the model.</summary>
    /// <param name="document">The ion list.</param>
    /// <param name="model">The validated model.</param>
    /// <returns>The rows, or every refusal.</returns>
    public static IonListExpansion Expand(IonListDocument document, CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(model);

        var errors = new List<EinzelError>();
        var ions = document.Ions ?? [];
        var sets = document.IonSets ?? [];

        Check(document.SchemaVersion == SchemaVersion, errors, ErrorCodes.SchemaInvalid, "/schemaVersion",
            $"an ion list declares \"schemaVersion\": \"{SchemaVersion}\"");
        Check(document.MaxParallelism is null or > 0, errors, ErrorCodes.ValueOutOfBounds, "/maxParallelism",
            "at least one ion flies at a time");
        Check(ions.Count + sets.Count > 0, errors, ErrorCodes.SchemaInvalid, "/",
            "an ion list declares at least one row in 'ions' or one set in 'ionSets'");

        var refused = CheckModel(model);
        errors.AddRange(refused);

        var context = new Context(model, model.Parameters.Values());
        var rows = new List<IonRow>();
        var ids = new HashSet<int>();
        var seeds = new List<long>();
        long next = 1;

        // One error per row or set, and every row and set is tried, so one pass lists them all.
        void Each(string path, string idKey, Func<IEnumerable<IonRow>> produce)
        {
            try
            {
                foreach (var row in produce())
                {
                    Require(ids.Add(row.Id), ErrorCodes.SchemaInvalid, path + idKey, $"id {row.Id} is used twice");
                    rows.Add(row);
                    next = Math.Max(next, (long)row.Id + 1);
                }
            }
            catch (EinzelException failure)
            {
                errors.Add(failure.Error);
            }
        }

        // A model that cannot supply defaults is refused before any row reads them. A mistake
        // in the document's own keys does not stop the rows being checked.
        var (rowCount, setCount) = refused.Count > 0 ? (0, 0) : (ions.Count, sets.Count);

        for (var k = 0; k < rowCount; k++)
        {
            var path = $"/ions/{k}";
            Each(path, "/id", () => [Row(ions[k] ?? throw Refuse(ErrorCodes.SchemaInvalid, path, "a row is an object"), path, context)]);
        }

        for (var s = 0; s < setCount; s++)
        {
            var path = $"/ionSets/{s}";
            Each(path, "/firstId", () => Set(sets[s] ?? throw Refuse(ErrorCodes.SchemaInvalid, path, "a set is an object"), path, next, context, seeds));
        }

        return new IonListExpansion(errors.Count > 0 ? null : rows, errors)
        {
            Seeds = seeds,
            MaxParallelism = document.MaxParallelism,
        };
    }

    /// <summary>The model and its resolved parameters, which every row reads its defaults from.</summary>
    internal sealed record Context(CompiledModel Model, IReadOnlyDictionary<string, Quantity> Parameters);

    internal static IonRow Row(IonRowDocument row, string path, Context context)
    {
        var model = context.Model;
        var id = row.Id ?? throw Refuse(ErrorCodes.SchemaInvalid, path + "/id", "every row has an id");

        Require(id >= 0, ErrorCodes.ValueOutOfBounds, path + "/id", "an id is a non-negative integer");

        var species = IonSpecies.FromModel(model);

        if (row.MassToCharge is not null || row.ChargeNumber is not null)
        {
            var z = row.ChargeNumber ?? model.Source.Ion!.ChargeNumber;
            Require(z != 0, ErrorCodes.ValueOutOfBounds, path + "/chargeNumber", "an ion cannot have zero charge");

            var massToCharge = Si(
                row.MassToCharge ?? model.Source.Ion!.MassToCharge!, path + "/massToCharge",
                Dimension.MassDimension, context);
            Require(massToCharge > 0.0, ErrorCodes.ValueOutOfBounds, path + "/massToCharge",
                "mass-to-charge must be positive");

            // The validator's own arithmetic, so a row restating the model's ion is that ion.
            species = IonSpecies.Create(
                Quantity.Si(massToCharge * Math.Abs(z), Dimension.MassDimension), Quantity.From(z, "e"));
        }

        var position = row.Position?.ToVec3(path + "/position", Dimension.LengthDimension, context.Parameters)
            ?? model.SourcePosition;
        var direction = row.Direction?.ToUnitVector(path + "/direction", context.Parameters)
            ?? model.SourceDirection;

        Require(row.KineticEnergy is null || row.AccelerationPotential is null, ErrorCodes.SchemaInvalid,
            path + "/accelerationPotential", "state the energy once: 'kineticEnergy' or 'accelerationPotential'");

        var energy = row.KineticEnergy is { } kinetic
            ? Si(kinetic, path + "/kineticEnergy", Dimension.Energy, context)
            : Math.Abs(species.ChargeSi)
                * Math.Abs(row.AccelerationPotential is { } potential
                    ? Si(potential, path + "/accelerationPotential", Dimension.ElectricPotential, context)
                    : model.AccelerationPotentialSi)
                * (1.0 + (row.AccelerationPotential is null ? model.EnergyFraction : 0.0));

        Require(energy >= 0.0, ErrorCodes.ValueOutOfBounds, path + "/kineticEnergy", "an energy cannot be negative");

        // CompiledModel.LaunchSpeedSi's expression, term for term.
        var launch = new PhaseState(position, direction * Math.Sqrt(2.0 * energy / species.MassSi));

        return new IonRow(id, species, launch, BirthTime(row, path, context), Tag(row.Tag, path));
    }

    private static double BirthTime(IonRowDocument row, string path, Context context)
    {
        var birth = Birth(row, path, context);

        if (Late(birth, context.Model, path + (row.RfPhase is null ? "/birthTime" : "/rfPhase")) is { } late)
        {
            throw new EinzelException(late);
        }

        return birth;
    }

    /// <summary>
    /// Refuses a birth more than 1000 of the model's maximum flight times late. A birth time
    /// is added to the flight's own clock, so a row born at 1e300 s would have its flight
    /// time resolved to a unit in the last place of 1e300.
    /// </summary>
    internal static EinzelError? Late(double birth, CompiledModel model, string path)
    {
        var bound = 1000.0 * model.MaximumFlightTimeSi;

        return birth <= bound ? null : new EinzelError
        {
            Code = ErrorCodes.ValueOutOfBounds,
            Path = path,
            Constraint = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"a birth at {birth:G6} s is more than 1000 times the model's maximum flight time ({bound:G6} s); ")
                + "its flight time would be resolved no better than one unit in the last place of the birth time",
            Suggestion = "state the birth time within the drive's period, or raise maximumFlightTime",
        };
    }

    private static double Birth(IonRowDocument row, string path, Context context)
    {
        if (row.RfPhase is not { } phase)
        {
            var birth = row.BirthTime is { } time ? Si(time, path + "/birthTime", Dimension.TimeDimension, context) : 0.0;
            Require(birth >= 0.0, ErrorCodes.ValueOutOfBounds, path + "/birthTime", "a birth time cannot be negative");
            return birth;
        }

        Require(row.BirthTime is null, ErrorCodes.SchemaInvalid, path + "/rfPhase",
            "state the birth once: 'birthTime' or 'rfPhase'");
        Require(double.IsFinite(phase) && phase >= 0.0, ErrorCodes.ValueOutOfBounds, path + "/rfPhase",
            "an RF phase is a non-negative number of cycles");

        var frequencies = Frequencies(context.Model);
        Require(frequencies.Count == 1, ErrorCodes.SchemaInvalid, path + "/rfPhase",
            $"an RF phase needs exactly one drive frequency, and the model has {frequencies.Count}; "
            + "state 'birthTime' instead");

        return phase / frequencies[0];
    }

    /// <summary>Every distinct drive frequency the model's elements declare, in hertz.</summary>
    internal static IReadOnlyList<double> Frequencies(CompiledModel model)
    {
        static IEnumerable<double> Of(CompiledField field) =>
            (field.Kind == CompiledFieldKind.IdealQuadrupoleRf ? [field.DriveFrequencySi] : Array.Empty<double>())
                .Concat(field.Solve?.Drives.Select(d => d.FrequencyHz) ?? [])
                .Concat(field.Solve3D?.Drives.Select(d => d.FrequencyHz) ?? [])
                .Concat(field.Phases.SelectMany(Of));

        return [.. model.Fields.SelectMany(Of).Distinct()];
    }

    private static IEnumerable<IonRow> Set(
        IonSetDocument set, string path, long next, Context context, List<long> seeds)
    {
        if (set.Cloud is not { } cloud)
        {
            return GeneratedSet.Expand(set, path, next, context, seeds);
        }

        Require(
            set is { Seed: null, MassToCharge: null, ChargeNumber: null, KineticEnergy: null, AccelerationPotential: null,
                BirthTime: null, RfPhase: null, Position: null, Direction: null },
            ErrorCodes.SchemaInvalid, path + "/cloud", "a cloud set takes only n, firstId and tag beside its cloud");
        Require(set.N is null || cloud.Ions == 1, ErrorCodes.SchemaInvalid, path + "/n",
            "state the count once: 'n' or 'cloud.ions'");
        Require(set.N is null or > 0, ErrorCodes.ValueOutOfBounds, path + "/n", "a set draws at least one ion");
        Require(cloud.Population is null, ErrorCodes.SchemaInvalid, path + "/cloud/population",
            "an ion list flies its ions one at a time, so a packet population has nothing to act on");

        var errors = new List<EinzelError>();
        var settings = ModelValidator.ValidateCloud(cloud, path + "/cloud", context.Parameters, errors);

        if (settings is null)
        {
            throw new EinzelException(errors[0]);
        }

        settings = settings with { Ions = set.N ?? settings.Ions };

        var first = FirstId(set.FirstId, next, settings.Ions, path);
        var tag = Tag(set.Tag, path);
        var model = context.Model;

        // What FiguresOfMerit.FlyCloud draws for `run`: the nominal launch, the model's
        // species, the declared direction as the axis of a packet at rest.
        var species = IonSpecies.FromModel(model);
        var nominal = new PhaseState(model.SourcePosition, model.SourceDirection * model.LaunchSpeedSi());
        var states = IonCloud.Draw(in nominal, species, settings, model.SourceDirection);

        seeds.Add(settings.Seed);

        return states.Select((state, k) => new IonRow(first + k, species, state, 0.0, tag));
    }

    /// <summary>The first id of a set, checked so that every id of it fits.</summary>
    internal static int FirstId(int? declared, long next, int count, string path)
    {
        var first = declared ?? next;

        Require(first >= 0 && first + count - 1 <= int.MaxValue, ErrorCodes.ValueOutOfBounds, path + "/firstId",
            "a set's ids are non-negative integers");

        return (int)first;
    }

    /// <summary>A tag, checked to be one CSV field.</summary>
    internal static string Tag(string? tag, string path)
    {
        Require(tag is null || tag.AsSpan().IndexOfAny(",\"\r\n") < 0, ErrorCodes.SchemaInvalid, path + "/tag",
            "a tag is one CSV field: no comma, double quote or line break");

        return tag ?? string.Empty;
    }

    private static double Si(QuantityValue value, string path, Dimension dimension, Context context) =>
        value.ToQuantity(path, dimension, context.Parameters).SiValue;

    internal static void Require(bool holds, string code, string path, string constraint)
    {
        if (!holds)
        {
            throw Refuse(code, path, constraint);
        }
    }

    private static void Check(bool holds, List<EinzelError> errors, string code, string path, string constraint)
    {
        if (!holds)
        {
            errors.Add(Error(code, path, constraint));
        }
    }

    private static EinzelException Refuse(string code, string path, string constraint) =>
        new(Error(code, path, constraint));

    private static EinzelError Error(string code, string path, string constraint) => new()
    {
        Code = code,
        Path = path,
        Constraint = constraint,
        Suggestion = "docs/ion-lists.md describes every attribute and its default",
    };
}
