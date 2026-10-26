using System.Globalization;
using Einzel.Core.Model;
using Einzel.Core.Units;
using Einzel.Fields.Solved;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>
/// The field basis a study with <c>"fieldBasis": "varyingElectrodes"</c> builds its fields under:
/// which electrodes the study's own points move, grouped, each group a DC channel of its own.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decided from the study definition, never from the cache.</b> The nominal model and the
/// study's points are compiled - every point of a scan; every draw of a sweep, with its
/// channel extremes; the ends of a boundary search; the bounds of an optimisation's first
/// three variables - and per solved element the electrodes whose potential is not the same at
/// every sample are grouped by holding identical potentials at every sample: exact equality of
/// doubles, no tolerance, as the decomposition has it. The rest keep their potentials in one
/// fixed channel. A DC scan of a driven quadrupole then solves the fixed channel, one channel
/// per group and the drive's, once, and every point re-weights them.
/// </para>
/// <para>
/// An element whose geometry differs between samples, or which steps through stages, keeps the
/// default decomposition and is named as skipped. A point whose compiled potentials do not fit
/// the plan - an electrode the samples held fixed that this point moves, a group whose
/// electrodes part - is built with the default decomposition, solving its own pattern, and the
/// manifest names it. Only a boundary search and an optimisation can meet such a point: their
/// points are found as they run, so they are not among the samples.
/// </para>
/// </remarks>
public sealed class StudyFieldBasis
{
    /// <summary>The value of <c>fieldBasis</c> that asks for this.</summary>
    public const string VaryingElectrodes = "varyingElectrodes";

    private readonly Dictionary<int, (string GeometryHash, Dictionary<string, double> Fixed)> _elements;
    private readonly Dictionary<int, IReadOnlyList<string>> _channels;

    private StudyFieldBasis(
        FieldBasisPlan plan,
        Dictionary<int, (string GeometryHash, Dictionary<string, double> Fixed)> elements,
        Dictionary<int, IReadOnlyList<string>> channels,
        IReadOnlyList<string> skipped,
        bool everyPoint)
    {
        Plan = plan;
        _elements = elements;
        _channels = channels;
        Skipped = skipped;
        SamplesEveryPoint = everyPoint;
    }

    /// <summary>The plan, per solved element the groups of electrodes the study moves; empty when no element qualifies.</summary>
    public FieldBasisPlan Plan { get; }

    /// <summary>
    /// The solved elements the basis cannot plan, each with the reason: <c>field 0: has stages</c>,
    /// <c>field 0: geometry differs between sampled points</c>.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; }

    /// <summary>
    /// Whether the samples are every point the study will evaluate (a scan, a sweep), so no point
    /// can fall back; false for a boundary search and an optimisation, whose points are found as
    /// they run.
    /// </summary>
    public bool SamplesEveryPoint { get; }

    /// <summary>Works out a study's basis from its definition.</summary>
    /// <param name="study">The study.</param>
    /// <param name="document">The model it studies.</param>
    /// <param name="directory">The directory the model was read from.</param>
    /// <returns>
    /// The basis, or null when the study asks for the default patterns or its nominal model does
    /// not compile. A basis whose plan is empty is still returned, so the study can say so.
    /// </returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    public static StudyFieldBasis? For(StudyDocument study, ModelDocument document, string? directory)
    {
        ArgumentNullException.ThrowIfNull(study);
        ArgumentNullException.ThrowIfNull(document);

        if (study.FieldBasis != VaryingElectrodes
            || ModelValidator.Validate(document, null, directory).Model is not { } nominal)
        {
            return null;
        }

        List<CompiledModel> samples =
        [
            nominal,
            .. Points(study, nominal.Parameters)
                .Select(overrides => ModelValidator.Validate(document, overrides, directory).Model).OfType<CompiledModel>(),
        ];

        var groups = new Dictionary<int, IReadOnlyList<IReadOnlyList<string>>>();
        var elements = new Dictionary<int, (string, Dictionary<string, double>)>();
        var skipped = new List<string>();

        for (var index = 0; index < nominal.Fields.Count; index++)
        {
            if (Potentials(nominal, index) is not { } names)
            {
                continue;
            }

            var hashes = samples.Select(model => GeometryHash(model, index)).Distinct(StringComparer.Ordinal).ToList();

            if (hashes is not [{ } hash])
            {
                skipped.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"field {index}: {(hashes.Contains(null) ? "has stages" : "geometry differs between sampled points")}"));
                continue;
            }

            // Each electrode's potential at every sample, in declaration order.
            var held = names.Keys.ToDictionary(
                name => name,
                name => samples.Select(model => Potentials(model, index)![name]).ToArray(),
                StringComparer.Ordinal);

            var varying = names.Keys.Where(name => held[name].Any(value => value != held[name][0])).ToList();
            var grouped = new List<List<string>>();

            foreach (var name in varying)
            {
                var same = grouped.FirstOrDefault(group => held[group[0]].SequenceEqual(held[name]));

                if (same is null)
                {
                    grouped.Add([name]);
                }
                else
                {
                    same.Add(name);
                }
            }

            if (grouped.Count == 0)
            {
                continue;
            }

            groups[index] = [.. grouped];
            elements[index] = (hash, names.Where(pair => !varying.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal));
        }

        var plan = new FieldBasisPlan(groups);

        // The plan's channels per element, from every sample rather than from one evaluation,
        // so a group the nominal holds at 0 V - a driven group with no channel there - is
        // recorded all the same.
        var channels = groups.Keys.ToDictionary(
            index => index,
            index => (IReadOnlyList<string>)[.. samples
                .SelectMany(model => SolveHash.Of(model, plan).Where(key => key.Field == index).SelectMany(key => key.ChannelHashes))
                .Distinct(StringComparer.Ordinal)]);

        return new StudyFieldBasis(
            plan, elements, channels, skipped, study.Boundary is null && study.Variables is not { Count: > 0 });
    }

    /// <summary>Why a compiled point does not fit the plan, or null when it does.</summary>
    /// <param name="model">The point's compiled model.</param>
    /// <returns>A sentence naming the electrode or the geometry that departs from the plan.</returns>
    public string? Misfit(CompiledModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        foreach (var (index, (hash, fixedAt)) in _elements)
        {
            if (index >= model.Fields.Count || GeometryHash(model, index) != hash || Potentials(model, index) is not { } held)
            {
                return string.Create(CultureInfo.InvariantCulture, $"field {index} has another geometry than the study's sampled points");
            }

            foreach (var (name, value) in fixedAt)
            {
                if (held[name] != value)
                {
                    return string.Create(
                        CultureInfo.InvariantCulture,
                        $"electrode '{name}' of field {index} holds {held[name]:R} V, where every sampled point held {value:R} V");
                }
            }

            foreach (var group in Plan.Groups[index])
            {
                if (group.Any(name => held[name] != held[group[0]]))
                {
                    return string.Create(
                        CultureInfo.InvariantCulture,
                        $"the electrodes {string.Join(", ", group.Select(name => $"'{name}'"))} of field {index} hold different potentials");
                }
            }
        }

        return null;
    }

    /// <summary>The plan for one point: this basis's when the point fits it, none otherwise.</summary>
    /// <param name="model">The point's compiled model.</param>
    /// <returns>The plan, or null for the default decomposition - also when the plan is empty.</returns>
    public FieldBasisPlan? PlanFor(CompiledModel model) => Plan.Groups.Count > 0 && Misfit(model) is null ? Plan : null;

    /// <summary>What a manifest records of this basis.</summary>
    /// <param name="fallbacks">The points that did not fit, each named.</param>
    /// <returns>The record.</returns>
    public FieldBasisRecord Record(IReadOnlyList<string> fallbacks) => new()
    {
        Kind = VaryingElectrodes,
        Elements = [.. Plan.Groups.OrderBy(pair => pair.Key).Select(pair => new FieldBasisElement
        {
            Field = pair.Key,
            Groups = pair.Value,
            ChannelHashes = _channels[pair.Key],
        })],
        Skipped = Skipped,
        Fallbacks = fallbacks,
    };

    /// <summary>
    /// The overrides of the study's points the basis is decided from: every point of a scan, the
    /// draws and channel extremes of a sweep, the survey's samples otherwise.
    /// </summary>
    private static IEnumerable<IReadOnlyDictionary<string, Quantity>> Points(StudyDocument study, ParameterSurface surface)
    {
        if (study.Scan is not null)
        {
            var axis = StudyBinding.Axis(study);

            return Enumerable.Range(0, Math.Max(study.Scan.Points, 0)).Select(index => (IReadOnlyDictionary<string, Quantity>)
                new Dictionary<string, Quantity>(StringComparer.Ordinal) { [axis.Parameter] = axis.At(index) });
        }

        var samples = StudyFields.Samples(study, surface);

        if (study.Boundary is not null || study.Variables is { Count: > 0 } || study.Channels is not { Count: > 0 })
        {
            return samples;
        }

        // A sweep's draws come from its seed before anything is evaluated - the draws it makes,
        // in its order - so every one is a sample and none can fall back.
        try
        {
            var channels = StudyBinding.Channels(study);

            return channels.All(channel => surface.Parameters.ContainsKey(channel.Parameter))
                ? [.. samples, .. Sweeps.ToleranceStudy.Draws(channels, surface, Math.Max(study.Draws, 0), study.Seed)]
                : samples;
        }
        catch (Core.Errors.EinzelException)
        {
            // A channel the sweep itself will refuse; the sweep says why.
            return samples;
        }
    }

    /// <summary>A solved element's geometry hash, or null for an element that is not solved or steps through stages.</summary>
    private static string? GeometryHash(CompiledModel model, int index) => model.Fields[index] switch
    {
        { Solve3D: { Stages.Count: 0 } volume } => SolveHash.Geometry(Geometry3D.Of(volume)),
        { Solve: { Stages.Count: 0 } plane } => SolveHash.Geometry(plane),
        _ => null,
    };

    /// <summary>Each electrode's DC potential, by name in declaration order; an edge profile, whose volts are its profile, is left out.</summary>
    private static Dictionary<string, double>? Potentials(CompiledModel model, int index) => model.Fields[index] switch
    {
        { Solve3D: { } volume } => volume.Electrodes.ToDictionary(e => e.Name, e => e.Potential, StringComparer.Ordinal),
        { Solve: { } plane } => plane.Electrodes
            .Where(e => e.Shape != ElectrodeShape.EdgeProfile)
            .ToDictionary(e => e.Name, e => e.Potential, StringComparer.Ordinal),
        _ => null,
    };
}
