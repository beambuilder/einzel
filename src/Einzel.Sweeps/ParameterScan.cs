using Einzel.Core.Errors;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Sweeps;

/// <summary>How the points of a scan are spaced along its range.</summary>
public enum ScanSpacing
{
    /// <summary>Evenly, in the parameter's own units.</summary>
    Linear,

    /// <summary>
    /// Evenly in the logarithm, which is what a range spanning decades needs.
    /// </summary>
    /// <remarks>
    /// A pressure scan from 1e-4 to 10 mbar taken linearly puts every point but one
    /// above a millibar and says nothing at all about the thin end - and the thin
    /// end is where the transport mode changes.
    /// </remarks>
    Logarithmic,
}

/// <summary>
/// One parameter varied over a declared range, on a grid of points.
/// </summary>
/// <param name="Parameter">Name of a free parameter in the model's surface.</param>
/// <param name="From">Where the scan starts. Same dimension as the parameter.</param>
/// <param name="To">Where it ends, inclusive.</param>
/// <param name="Points">How many points, including both ends.</param>
/// <param name="Spacing">How they are distributed.</param>
/// <remarks>
/// <para>
/// The third thing a study file can say, beside a tolerance sweep and an
/// optimisation, and the one every result in this engine has so far been getting by
/// hand-written C#: the low-mass cut-off scans, the extraction-slot scan, the
/// drift-length scan. Each was a loop in a test file, so none of them wrote a
/// manifest, none could be re-run from the project, and none was reachable by an
/// agent at all.
/// </para>
/// <para>
/// It is deliberately not a sweep with one channel. A sweep asks what a
/// <em>distribution</em> of manufacturing error does to a design and reports a
/// spread; a scan asks what the figure <em>looks like</em> as a function of a knob,
/// and its output is a curve. Section 12's whole Class B - stability boundaries,
/// peak shape against scan line, low-mass cut-off - is a question about a curve,
/// and averaging one into an interval answers a different question.
/// </para>
/// </remarks>
public sealed record ScanAxis(
    string Parameter,
    Quantity From,
    Quantity To,
    int Points,
    ScanSpacing Spacing = ScanSpacing.Linear)
{
    /// <summary>The value at one point of the scan, in SI.</summary>
    /// <param name="index">Which point, from zero.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The index is outside the scan.</exception>
    /// <remarks>
    /// Both ends are included, so the step is the range over one less than the point
    /// count. The endpoints are also computed rather than accumulated: adding a step
    /// repeatedly leaves the last point short of the declared end by a rounding
    /// error, which is invisible on a plot and is exactly the sort of thing that
    /// makes two scans of the same range disagree at their edges.
    /// </remarks>
    public Quantity At(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Points);

        if (Points == 1 || index == 0)
        {
            return From;
        }

        // The ends are returned exactly rather than interpolated to. Half of
        // (0.1, 0.2) is 0.15000000000000002, and a scan written the obvious way -
        // from a parameter's declared minimum to its declared maximum - then has its
        // last point refused by the bounds check for a reason nothing on the page
        // explains. Interpolation is for the interior, where an ulp means nothing.
        if (index == Points - 1)
        {
            return To;
        }

        var fraction = (double)index / (Points - 1);

        var value = Spacing == ScanSpacing.Logarithmic
            ? From.SiValue * Math.Pow(To.SiValue / From.SiValue, fraction)
            : From.SiValue + (fraction * (To.SiValue - From.SiValue));

        return Quantity.Si(value, From.Dimension);
    }

    /// <summary>Checks that this scan names a free parameter and covers a real range.</summary>
    /// <param name="surface">The model's parameter surface.</param>
    /// <param name="path">JSON Pointer to this scan, for the error object.</param>
    /// <returns>The parameter it names.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="surface"/> is null.</exception>
    /// <exception cref="EinzelException">
    /// No such parameter, it is derived, the dimensions disagree, there are too few
    /// points, or a logarithmic scan crosses zero.
    /// </exception>
    public ResolvedParameter Bind(ParameterSurface surface, string path)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (!surface.Parameters.TryGetValue(Parameter, out var parameter))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path + "/parameter",
                Constraint = $"'{Parameter}' is not a parameter of this model",
                Suggestion = surface.FreeParameters.Count == 0
                    ? "the model declares no free parameters to scan"
                    : $"free parameters are: {string.Join(", ", surface.FreeParameters.Select(p => p.Name))}",
            });
        }

        if (parameter.IsDerived)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = path + "/parameter",
                Constraint = $"'{Parameter}' is derived from other parameters and cannot be scanned "
                    + "directly",
                Suggestion = "scan whatever it is derived from; a value written over a derived "
                    + "parameter is overwritten again the moment the model is compiled",
            });
        }

        foreach (var (end, name) in new[] { (From, "from"), (To, "to") })
        {
            if (end.Dimension != parameter.Value.Dimension)
            {
                throw new EinzelException(new EinzelError
                {
                    Code = ErrorCodes.UnitsIncompatible,
                    Path = path + "/" + name,
                    Constraint = $"'{Parameter}' has dimension {parameter.Value.Dimension}",
                    Observed = new ObservedValue(end.SiValue, end.Dimension.ToString()),
                    Suggestion = $"supply a unit of dimension {parameter.Value.Dimension}",
                });
            }
        }

        if (Points < 2)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path + "/points",
                Constraint = "a scan needs at least two points",
                Observed = new ObservedValue(Points, "points"),
                Suggestion = "one point is a run; use 'einzel run' with the parameter set instead",
            });
        }

        if (From.SiValue == To.SiValue)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path + "/to",
                Constraint = "a scan's range has zero width, so every point is the same run",
                Observed = new ObservedValue(To.SiValue, "SI"),
                Suggestion = "give 'from' and 'to' different values",
            });
        }

        if (Spacing == ScanSpacing.Logarithmic && From.SiValue * To.SiValue <= 0.0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.ValueOutOfBounds,
                Path = path + "/spacing",
                Constraint = "a logarithmic scan cannot cross or touch zero",
                Observed = new ObservedValue(From.SiValue, "SI"),
                Suggestion = "use linear spacing, or move the range entirely to one side of zero",
            });
        }

        return parameter;
    }
}

/// <summary>One point of a scan.</summary>
/// <param name="Index">Which point, from zero.</param>
/// <param name="ValueSi">The parameter's value there, in SI.</param>
/// <param name="FigureOfMerit">
/// The figure in SI, or null where this point produced none.
/// </param>
/// <param name="Failure">Why it produced none, or null where it did.</param>
/// <remarks>
/// A point that fails is a row rather than the end of the scan, and the reason
/// matters more here than in a sweep. On a stability scan "the ion was lost" is the
/// <em>answer</em>: a cut-off is precisely the value at which the figure stops
/// existing, so a driver that stopped at the first failure would stop exactly where
/// the interesting thing is.
/// </remarks>
public sealed record ScanPoint(int Index, double ValueSi, double? FigureOfMerit, string? Failure);

/// <summary>What a scan found.</summary>
/// <param name="Points">One row per point, in scan order.</param>
/// <param name="Nominal">The figure at the model's own parameter value, or null.</param>
/// <param name="Warnings">What the scan itself has to say about its range.</param>
public sealed record ScanResult(
    IReadOnlyList<ScanPoint> Points,
    double? Nominal,
    IReadOnlyList<ValidityWarning> Warnings)
{
    /// <summary>How many points produced a figure.</summary>
    public int Succeeded => Points.Count(p => p.FigureOfMerit is not null);

    /// <summary>
    /// The adjacent pair the figure changes most between, or null where fewer than
    /// two points produced one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not a boundary, and deliberately not called one. Section 12's Class B wants
    /// stability and cut-off boundaries resolved to a stated fraction of the scan
    /// variable (ACC-6, one part in five hundred), which needs a bisection this does
    /// not do. What this reports is where on the present grid the figure moves
    /// fastest, and how wide that interval is - which is the number that says whether
    /// the scan was fine enough to be resolving anything at all.
    /// </para>
    /// <para>
    /// A pair where the figure stops existing counts, and counts as the largest
    /// possible change. On a mass filter that transition is the cut-off, and treating
    /// a vanished figure as "no change" would rank the one interesting interval last.
    /// </para>
    /// </remarks>
    public (double LowSi, double HighSi, double Change)? SteepestInterval
    {
        get
        {
            (double Low, double High, double Change)? steepest = null;

            for (var i = 0; i + 1 < Points.Count; i++)
            {
                var a = Points[i];
                var b = Points[i + 1];

                var change = a.FigureOfMerit is { } from && b.FigureOfMerit is { } to
                    ? Math.Abs(to - from)
                    : a.FigureOfMerit is null && b.FigureOfMerit is null
                        ? double.NaN
                        : double.PositiveInfinity;

                if (double.IsNaN(change))
                {
                    continue;
                }

                if (steepest is null || change > steepest.Value.Change)
                {
                    steepest = (a.ValueSi, b.ValueSi, change);
                }
            }

            return steepest;
        }
    }
}

/// <summary>
/// Evaluates a figure of merit across a range of one parameter.
/// </summary>
/// <remarks>
/// The third driver, beside <see cref="ToleranceStudy"/> and <see cref="Optimiser"/>,
/// and it takes the same function from a validated model to a number - which is what
/// keeps all three device-agnostic.
/// </remarks>
public static class ParameterScan
{
    /// <summary>Runs a scan.</summary>
    /// <param name="document">The model to vary.</param>
    /// <param name="axis">The parameter, the range, and the points.</param>
    /// <param name="evaluate">
    /// Produces the figure of merit from a validated model, or null where this point
    /// produces none.
    /// </param>
    /// <param name="maxParallelism">
    /// How many points may be evaluated at once, or null for one per processor.
    /// <para>
    /// Lower it when a single solve is large and the points solve: each point in flight that
    /// solves holds the solver's working set, so peak memory is this times one solve. A point
    /// that only re-weights fields another point solved holds nothing of its own when the
    /// evaluator shares them, as the study verbs do. Cores are rarely the binding constraint;
    /// memory is.
    /// </para>
    /// </param>
    /// <returns>One row per point, and what the scan has to say about its own range.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">
    /// The model does not validate, or the axis does not name a free parameter.
    /// </exception>
    /// <param name="sourceDirectory">
    /// The directory the model document was read from, which any file it references is
    /// resolved against. Null when the caller has none, and a model declaring an
    /// imported gas field is then refused rather than run in a gas it does not
    /// describe.
    /// </param>
    public static ScanResult Run(
        ModelDocument document,
        ScanAxis axis,
        Func<CompiledModel, double?> evaluate,
        string? sourceDirectory = null,
        int? maxParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(axis);
        ArgumentNullException.ThrowIfNull(evaluate);

        var baseline = Compile(document, sourceDirectory);
        var parameter = axis.Bind(baseline.Parameters, "/scan");

        var warnings = new List<ValidityWarning>();

        // Said up front rather than discovered as a run of failed rows. A scan that
        // walks past what the template says is buildable is a legitimate thing to
        // ask for - finding out where a design stops working is the point - but a
        // reader who sees half the rows empty and no explanation will read it as the
        // solver failing rather than as the model refusing.
        foreach (var (end, name) in new[] { (axis.From, "from"), (axis.To, "to") })
        {
            if (parameter.IsWithinBounds(end))
            {
                continue;
            }

            warnings.Add(new ValidityWarning(
                "scan.outside-declared-bounds",
                $"the scan's '{name}' end puts {axis.Parameter} at {end.SiValue:G6} SI, outside the "
                + $"[{parameter.Minimum?.SiValue.ToString("G6") ?? "-inf"}, "
                + $"{parameter.Maximum?.SiValue.ToString("G6") ?? "+inf"}] the model declares. Points "
                + "beyond the bound are refused by validation and appear as rows with no figure, "
                + "which is the model saying the geometry is not buildable rather than the solver "
                + "failing to evaluate it",
                WarningSeverity.Qualified));
        }

        // Evaluated once at the model's own value, so a curve can be read against the
        // design it came from rather than only against itself.
        double? nominal;

        try
        {
            nominal = evaluate(baseline);
        }
        catch (EinzelException)
        {
            nominal = null;
        }

        // THE POINTS ARE INDEPENDENT, SO THEY RUN AT ONCE. Every point compiles its own
        // model from the same immutable document and builds its own field; nothing is
        // shared but the evaluator, whose mutable state is the warning ledger, scoped per
        // evaluation and merged under a lock, and whatever store it builds fields through.
        // The nominal evaluation above runs first and alone, so a point whose channels are
        // the nominal model's finds them stored rather than solving them again.
        //
        // WRITTEN BY INDEX, NOT APPENDED, so the curve comes back in scan order whatever
        // order the points finish in. CLI-6 asks for deterministic output, and a scan whose
        // rows arrive in completion order would reorder itself run to run on the same
        // machine - which also breaks `verify`, since the result would no longer match.
        //
        // The seeds do not depend on the order either: a cloud draws from its own declared
        // seed and a collision sampler from the gas seed plus the ion's index, so a point
        // computes the same number wherever it runs.
        //
        // MEMORY IS THE CONSTRAINT, NOT CORES, WHEN THE POINTS SOLVE. A point that solves
        // holds the solver's working set while it does: a DC scan of the 0.2 mm quadrupole
        // (8.5 M nodes) peaked at 4.5 GB one point at a time and 9.4 GB two at a time. A
        // point that re-weights the nominal channels holds only its flights when the
        // evaluator shares those channels. The caller lowers `maxParallelism` when the solve
        // is large; the default is one per processor, which is right for the plane
        // geometries most studies scan.
        var rows = new ScanPoint[axis.Points];

        var parallelism = Math.Max(1, maxParallelism ?? Environment.ProcessorCount);

        Parallel.For(
            0,
            axis.Points,
            new ParallelOptions { MaxDegreeOfParallelism = parallelism },
            index =>
            {
                var value = axis.At(index);

                var overrides = new Dictionary<string, Quantity>(StringComparer.Ordinal)
                {
                    [axis.Parameter] = value,
                };

                rows[index] = Evaluate(
                    document, overrides, evaluate, index, value.SiValue, sourceDirectory);
            });

        var points = new List<ScanPoint>(rows);

        if (points.All(p => p.FigureOfMerit is null))
        {
            warnings.Add(new ValidityWarning(
                "scan.no-figure-anywhere",
                "no point of this scan produced a figure of merit, so there is no curve here. A "
                + "scan of all-empty rows and one of a genuinely flat response look nothing alike "
                + "in the data and identical on a plot",
                WarningSeverity.ValidityViolation));
        }

        return new ScanResult(points, nominal, warnings);
    }

    private static ScanPoint Evaluate(
        ModelDocument document,
        Dictionary<string, Quantity> overrides,
        Func<CompiledModel, double?> evaluate,
        int index,
        double valueSi,
        string? sourceDirectory)
    {
        try
        {
            var validation = ModelValidator.Validate(document, overrides, sourceDirectory);

            if (!validation.IsValid)
            {
                return new ScanPoint(index, valueSi, null, validation.Errors[0].ToString());
            }

            return new ScanPoint(index, valueSi, evaluate(validation.Model!), null);
        }
        catch (EinzelException failure)
        {
            return new ScanPoint(index, valueSi, null, failure.Error.ToString());
        }
        catch (InvalidOperationException failure)
        {
            return new ScanPoint(index, valueSi, null, failure.Message);
        }
    }

    private static CompiledModel Compile(ModelDocument document, string? sourceDirectory)
    {
        var validation = ModelValidator.Validate(document, null, sourceDirectory);

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors[0]);
        }

        return validation.Model!;
    }
}
