using Einzel.Core.Errors;
using Einzel.Core.Units;
using Einzel.Sweeps;

namespace Einzel.Commands;

/// <summary>One parameter a tolerance study varies, as it appears in a file.</summary>
public sealed record ChannelDocument
{
    /// <summary>The declared parameter this channel perturbs.</summary>
    public string? Parameter { get; init; }

    /// <summary>Half-width of the perturbation.</summary>
    public double HalfWidth { get; init; }

    /// <summary>Unit of the half-width; must match the parameter's dimension.</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Either <c>uniform</c> or <c>normal</c>.
    /// </summary>
    /// <remarks>
    /// A machining tolerance quoted as plus or minus something is a statement
    /// about a bound, not about a distribution, and which one is assumed changes
    /// the answer. Uniform treats the bound as equally likely anywhere inside,
    /// which is what a tolerance usually means when it is written on a drawing.
    /// Normal takes the half-width as one standard deviation, which puts about a
    /// third of the draws outside the stated bound - that is the point of asking
    /// for it, and it is worth being deliberate about which one a study means.
    /// </remarks>
    public string Distribution { get; init; } = "uniform";
}

/// <summary>One parameter an optimisation searches, as it appears in a file.</summary>
public sealed record VariableDocument
{
    /// <summary>The declared parameter this variable searches.</summary>
    public string? Parameter { get; init; }

    /// <summary>Lower bound. Omit to use the bound the model declares.</summary>
    public double? Minimum { get; init; }

    /// <summary>Upper bound. Omit to use the bound the model declares.</summary>
    public double? Maximum { get; init; }

    /// <summary>Unit of the bounds; required when either is given.</summary>
    public string? Unit { get; init; }
}

/// <summary>
/// A study: what to vary, how much, and what to record.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 13: "A study is a file in studies/ declaring perturbation channels
/// with distributions, a draw count, a seed, an ensemble specification, and
/// figures of merit to record."
/// </para>
/// <para>
/// One document type covers both a tolerance sweep and an optimisation, because
/// they differ only in what they do with the parameters - and keeping them in one
/// schema means a study that established which parameter binds first can be turned
/// into the search that tunes it by changing one word.
/// </para>
/// </remarks>
public sealed record StudyDocument
{
    /// <summary>The schema version this document is written against.</summary>
    public string SchemaVersion { get; init; } = "0.1";

    /// <summary>A short name, used in output and in generated file names.</summary>
    public string? Name { get; init; }

    /// <summary>Prose description, carried through to results.</summary>
    public string? Description { get; init; }

    /// <summary>The model to study, relative to this file.</summary>
    public string? Model { get; init; }

    /// <summary>Which figure of merit to record. See <see cref="FiguresOfMerit"/>.</summary>
    public string? FigureOfMerit { get; init; }

    /// <summary>Fractional energy spread for the ensemble figures of merit.</summary>
    public double EnergySpread { get; init; } = FiguresOfMerit.DefaultEnergySpread;

    /// <summary>How many ions the ensemble figures of merit launch.</summary>
    public int Ions { get; init; } = FiguresOfMerit.DefaultIons;

    /// <summary>Tolerance sweep: the parameters to perturb.</summary>
    public IReadOnlyList<ChannelDocument>? Channels { get; init; }

    /// <summary>Tolerance sweep: how many stochastic draws.</summary>
    public int Draws { get; init; } = 200;

    /// <summary>Tolerance sweep: whether to run the one-at-a-time attribution pass.</summary>
    public bool OneAtATime { get; init; } = true;

    /// <summary>Seed for anything stochastic, so a run is regenerable (PRJ-3).</summary>
    public int Seed { get; init; } = 1;

    /// <summary>Optimisation: the parameters to search.</summary>
    public IReadOnlyList<VariableDocument>? Variables { get; init; }

    /// <summary>Scan: the one parameter to vary, and over what range.</summary>
    public ScanDocument? Scan { get; init; }

    /// <summary>Boundary: the parameter and bracket a Class B search bisects.</summary>
    public BoundaryDocument? Boundary { get; init; }

    /// <summary>Optimisation: <c>nelderMead</c> or <c>cmaEs</c>.</summary>
    public string Algorithm { get; init; } = "nelderMead";

    /// <summary>Optimisation: <c>minimise</c>, <c>maximise</c>, or omit to use the figure's own sense.</summary>
    public string? Sense { get; init; }

    /// <summary>Optimisation: ceiling on objective evaluations.</summary>
    public int MaximumEvaluations { get; init; } = 200;

    /// <summary>
    /// How many evaluations may run at once, or null for one per processor. Each evaluation
    /// that solves holds one solve's working set while it does; one that re-weights the
    /// nominal channels shares them and holds only its flights.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Memory is the constraint, not cores, for a study whose points solve.</b> One
    /// evaluation that solves holds the solver's working set and its channels: on the 0.2 mm
    /// quadrupole (8.5 M nodes, two channels) 1.3 GiB live and 4.5 GB of working set, and a
    /// DC scan of it at two at once peaked at 9.4 GB. A study whose points reuse the nominal
    /// channels - an amplitude, a frequency, an ion - solves once and shares those channels
    /// between its evaluations, so its memory does not grow with this. <c>estimate</c> and the
    /// study itself say what the peak will be, and a study that will not fit says so before it
    /// starts.
    /// </para>
    /// <para>
    /// It changes what a study <i>costs</i> and never what it <i>says</i>: draws are made in
    /// seed order before anything is evaluated, and every row is written by index, so the
    /// result is identical at any setting. A sweep run at one and at sixteen is the same
    /// study, which is asserted rather than assumed.
    /// </para>
    /// </remarks>
    public int? MaxParallelism { get; init; }

    /// <summary>
    /// The most memory the study keeps solved channels in between evaluations, as a value and
    /// a unit (<c>B</c>, <c>kB</c>, <c>MB</c>, <c>GB</c>, <c>KiB</c>, <c>MiB</c>, <c>GiB</c>), or
    /// null for four times the nominal model's channels.
    /// </summary>
    /// <remarks>
    /// The nominal model's channels are always kept, so a budget below them keeps those alone.
    /// A channel let go is loaded from the field cache, or solved, again when a later point
    /// needs it; what the study computes does not depend on this.
    /// </remarks>
    public MemoryDocument? MemoryBudget { get; init; }

    /// <summary>Optimisation: convergence tolerance on the parameters, as a fraction of the box.</summary>
    public double ParameterTolerance { get; init; } = 1e-4;

    /// <summary>
    /// Optimisation: convergence tolerance on the objective.
    /// </summary>
    /// <remarks>
    /// Set this to the objective's own noise level and not below. Every evaluation
    /// here ends in a field solve at a finite tolerance, so the objective has grit
    /// on it; a tolerance under that asks the search to resolve noise, and it will
    /// spend its whole budget doing so and then report that it never converged.
    /// </remarks>
    public double ObjectiveTolerance { get; init; } = 1e-8;
}

/// <summary>An amount of memory, as it appears in a study file.</summary>
public sealed record MemoryDocument
{
    /// <summary>How much.</summary>
    public required double Value { get; init; }

    /// <summary><c>B</c>, <c>kB</c>, <c>MB</c>, <c>GB</c>, <c>KiB</c>, <c>MiB</c> or <c>GiB</c>.</summary>
    public required string? Unit { get; init; }
}

/// <summary>One parameter a scan varies, as it appears in a file.</summary>
/// <remarks>
/// The third thing a study can be, beside a tolerance sweep and an optimisation.
/// Section 12's Class B figures - a stability boundary, a mass filter peak against
/// its scan line, a low-mass cut-off - are all questions about how a figure behaves
/// across a range, and neither of the other two answers that: a sweep collapses a
/// range into a distribution and an optimiser reports only where it stopped.
/// </remarks>
public sealed record ScanDocument
{
    /// <summary>The declared parameter this scan varies.</summary>
    public string? Parameter { get; init; }

    /// <summary>Where the scan starts.</summary>
    public double From { get; init; }

    /// <summary>Where it ends. Included, not a limit the last point stops short of.</summary>
    public double To { get; init; }

    /// <summary>Unit of both ends; must match the parameter's dimension.</summary>
    public string? Unit { get; init; }

    /// <summary>How many points, counting both ends.</summary>
    public int Points { get; init; } = 21;

    /// <summary><c>linear</c> or <c>logarithmic</c>.</summary>
    public string Spacing { get; init; } = "linear";
}

/// <summary>One boundary a Class B search locates, as it appears in a file.</summary>
/// <remarks>
/// ACC-6: "Class B boundary resolution &lt;= 1/500 of scan." A scan brackets a
/// transition with a grid and costs 501 evaluations to reach that; this bisects
/// onto it and costs about eleven. What it is for is §12's Class B list - a
/// stability boundary, a low-mass cut-off for a funnel or an RF guide - where the
/// question is not what the curve looks like but where exactly it crosses.
/// </remarks>
public sealed record BoundaryDocument
{
    /// <summary>The declared parameter the boundary is located along.</summary>
    public string? Parameter { get; init; }

    /// <summary>One end of the bracket. Must be on the opposite side from <see cref="To"/>.</summary>
    public double From { get; init; }

    /// <summary>The other end.</summary>
    public double To { get; init; }

    /// <summary>Unit of both ends; must match the parameter's dimension.</summary>
    public string? Unit { get; init; }

    /// <summary>
    /// The value of the figure of merit that separates inside from outside.
    /// </summary>
    /// <remarks>
    /// For a stability boundary measured by transmission, one half: an ion either
    /// gets through or does not, and a figure that has stopped existing is always
    /// outside whatever the threshold is.
    /// </remarks>
    public double Threshold { get; init; } = 0.5;

    /// <summary><c>above</c> or <c>below</c>: which side of the threshold is inside.</summary>
    public string Inside { get; init; } = "above";

    /// <summary>
    /// Bracket width to stop at, as a fraction of the range. ACC-6 asks for 0.002.
    /// </summary>
    public double Resolution { get; init; } = 1.0 / 500.0;

    /// <summary>Ceiling on evaluations, in case the figure is not monotone here.</summary>
    public int MaximumEvaluations { get; init; } = 60;
}

/// <summary>Turns a study document into the objects the sweep drivers take.</summary>
public static class StudyBinding
{
    /// <summary>The perturbation channels a tolerance sweep runs.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The channels.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">A channel is incomplete or names an unknown distribution.</exception>
    public static IReadOnlyList<PerturbationChannel> Channels(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        if (study.Channels is not { Count: > 0 })
        {
            throw Missing("channels", "a tolerance sweep needs at least one channel to perturb");
        }

        var channels = new List<PerturbationChannel>(study.Channels.Count);

        for (var index = 0; index < study.Channels.Count; index++)
        {
            var declared = study.Channels[index];

            if (string.IsNullOrWhiteSpace(declared.Parameter))
            {
                throw Missing($"channels/{index}/parameter", "every channel names the parameter it perturbs");
            }

            if (string.IsNullOrWhiteSpace(declared.Unit))
            {
                // SI internally, units explicit at every boundary. A bare number
                // here is the commonest source of silent wrongness and an agent
                // writing from prose is the likeliest to introduce it.
                throw Missing(
                    $"channels/{index}/unit",
                    "a half-width needs a unit; '0.1' could be a tenth of a millimetre or a tenth of a metre");
            }

            channels.Add(new PerturbationChannel(
                declared.Parameter,
                Quantity.From(declared.HalfWidth, declared.Unit),
                Distribution(declared.Distribution, index)));
        }

        return channels;
    }

    /// <summary>The axis a scan varies.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The axis.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">The scan block is missing or incomplete.</exception>
    public static ScanAxis Axis(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        if (study.Scan is not { } scan)
        {
            throw Missing("scan", "a scan needs a 'scan' block naming a parameter and a range");
        }

        if (string.IsNullOrWhiteSpace(scan.Parameter))
        {
            throw Missing("scan/parameter", "a scan names the parameter it varies");
        }

        if (string.IsNullOrWhiteSpace(scan.Unit))
        {
            // SI internally, units explicit at every boundary. The same rule a
            // channel's half-width is held to, for the same reason: '0.5' is a
            // millimetre or a metre depending on something nobody wrote down.
            throw Missing(
                "scan/unit",
                "a scan's range needs a unit; use '1' for a dimensionless parameter such as a "
                + "Mathieu q or a rod ratio");
        }

        return new ScanAxis(
            scan.Parameter,
            Quantity.From(scan.From, scan.Unit),
            Quantity.From(scan.To, scan.Unit),
            scan.Points,
            Spacing(scan.Spacing));
    }

    private static ScanSpacing Spacing(string declared) => declared.ToLowerInvariant() switch
    {
        "linear" => ScanSpacing.Linear,
        "logarithmic" or "log" => ScanSpacing.Logarithmic,
        _ => throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = "/scan/spacing",
            Constraint = $"'{declared}' is not a spacing",
            Suggestion = "one of: linear, logarithmic",
        }),
    };

    /// <summary>The bracket a boundary search bisects, and which side is inside.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The axis, the threshold, the sense, and the resolution.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">The boundary block is missing or incomplete.</exception>
    public static (ScanAxis Axis, double Threshold, BoundarySense Sense, double Resolution, int Budget)
        Boundary(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        if (study.Boundary is not { } boundary)
        {
            throw Missing(
                "boundary",
                "a boundary search needs a 'boundary' block naming a parameter and a bracket");
        }

        if (string.IsNullOrWhiteSpace(boundary.Parameter))
        {
            throw Missing("boundary/parameter", "a boundary search names the parameter it varies");
        }

        if (string.IsNullOrWhiteSpace(boundary.Unit))
        {
            throw Missing(
                "boundary/unit",
                "a bracket needs a unit; use '1' for a dimensionless parameter such as a Mathieu q");
        }

        var sense = boundary.Inside.ToLowerInvariant() switch
        {
            "above" => BoundarySense.Above,
            "below" => BoundarySense.Below,
            _ => throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/boundary/inside",
                Constraint = $"'{boundary.Inside}' is not a side",
                Suggestion = "one of: above, below",
            }),
        };

        // Two points, because a bracket is its ends. The count is unused by the
        // search and is here only because ScanAxis carries one.
        var axis = new ScanAxis(
            boundary.Parameter,
            Quantity.From(boundary.From, boundary.Unit),
            Quantity.From(boundary.To, boundary.Unit),
            2);

        return (axis, boundary.Threshold, sense, boundary.Resolution, boundary.MaximumEvaluations);
    }

    /// <summary>The design variables an optimisation searches.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The variables.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">A variable is incomplete.</exception>
    public static IReadOnlyList<DesignVariable> Variables(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        if (study.Variables is not { Count: > 0 })
        {
            throw Missing("variables", "an optimisation needs at least one variable to search");
        }

        var variables = new List<DesignVariable>(study.Variables.Count);

        for (var index = 0; index < study.Variables.Count; index++)
        {
            var declared = study.Variables[index];

            if (string.IsNullOrWhiteSpace(declared.Parameter))
            {
                throw Missing($"variables/{index}/parameter", "every variable names the parameter it searches");
            }

            if ((declared.Minimum is not null || declared.Maximum is not null)
                && string.IsNullOrWhiteSpace(declared.Unit))
            {
                throw Missing($"variables/{index}/unit", "a bound given here needs a unit");
            }

            variables.Add(new DesignVariable(
                declared.Parameter,
                declared.Minimum is { } low ? Quantity.From(low, declared.Unit!) : null,
                declared.Maximum is { } high ? Quantity.From(high, declared.Unit!) : null));
        }

        return variables;
    }

    /// <summary>Which search a study asked for.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The algorithm.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">The algorithm is not one this build has.</exception>
    public static OptimisationAlgorithm Algorithm(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        return study.Algorithm switch
        {
            "nelderMead" => OptimisationAlgorithm.NelderMead,
            "cmaEs" => OptimisationAlgorithm.CmaEs,
            _ => throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/algorithm",
                Constraint = $"'{study.Algorithm}' is not a search this build has",
                Suggestion = "'nelderMead' for a handful of variables, 'cmaEs' for more of them or a "
                    + "rougher objective",
            }),
        };
    }

    /// <summary>Which way an optimisation is better.</summary>
    /// <param name="study">The study.</param>
    /// <param name="figure">The figure of merit being optimised.</param>
    /// <returns>The sense.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="EinzelException">The sense is not one of the two.</exception>
    /// <remarks>
    /// Defaulted from the figure of merit rather than required, because a figure
    /// of merit knows which way is better and making the caller restate it is an
    /// invitation to state it wrongly. A study may still override, since minimising
    /// a resolving power is a legitimate thing to ask for when hunting a bug.
    /// </remarks>
    public static ObjectiveSense Sense(StudyDocument study, FigureOfMeritInfo figure)
    {
        ArgumentNullException.ThrowIfNull(study);
        ArgumentNullException.ThrowIfNull(figure);

        return study.Sense switch
        {
            null => figure.LargerIsBetter ? ObjectiveSense.Maximise : ObjectiveSense.Minimise,
            "maximise" or "maximize" => ObjectiveSense.Maximise,
            "minimise" or "minimize" => ObjectiveSense.Minimise,
            _ => throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sense",
                Constraint = $"'{study.Sense}' is not a direction",
                Suggestion = "'minimise' or 'maximise', or omit it and the figure of merit decides",
            }),
        };
    }

    /// <summary>The units a <c>memoryBudget</c> may be written in, as the schema lists them.</summary>
    public static IReadOnlyList<string> MemoryUnits { get; } = ["B", "kB", "MB", "GB", "KiB", "MiB", "GiB"];

    /// <summary>The study's memory budget, in bytes, or null when it names none.</summary>
    /// <param name="study">The study.</param>
    /// <returns>The budget in bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="study"/> is null.</exception>
    /// <exception cref="EinzelException">The unit is not an amount of memory, or the value is negative.</exception>
    public static long? MemoryBudget(StudyDocument study)
    {
        ArgumentNullException.ThrowIfNull(study);

        if (study.MemoryBudget is not { } budget)
        {
            return null;
        }

        double? scale = budget.Unit switch
        {
            "B" => 1.0,
            "kB" => 1e3,
            "MB" => 1e6,
            "GB" => 1e9,
            "KiB" => 1024.0,
            "MiB" => 1024.0 * 1024.0,
            "GiB" => 1024.0 * 1024.0 * 1024.0,
            _ => null,
        };

        if (scale is null)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/memoryBudget/unit",
                Constraint = $"{(budget.Unit is null ? "null" : $"'{budget.Unit}'")} is not an amount of memory",
                Suggestion = $"one of: {string.Join(", ", MemoryUnits)}",
            });
        }

        if (!double.IsFinite(budget.Value) || budget.Value < 0.0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/memoryBudget/value",
                Constraint = "a memory budget is a finite amount, zero or more",
                Suggestion = "for example { \"value\": 2, \"unit\": \"GiB\" }",
            });
        }

        return (long)Math.Min(budget.Value * scale.Value, long.MaxValue);
    }

    private static PerturbationDistribution Distribution(string declared, int index) => declared switch
    {
        "uniform" => PerturbationDistribution.Uniform,
        "normal" => PerturbationDistribution.Normal,
        _ => throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = $"/channels/{index}/distribution",
            Constraint = $"'{declared}' is not a distribution this build knows",
            Suggestion = "'uniform' treats the half-width as a bound; 'normal' treats it as one standard "
                + "deviation",
        }),
    };

    private static EinzelException Missing(string path, string constraint) => new(new EinzelError
    {
        Code = ErrorCodes.SchemaInvalid,
        Path = "/" + path,
        Constraint = constraint,
        Suggestion = "run 'einzel schema --study' for the shape of a study file",
    });
}
