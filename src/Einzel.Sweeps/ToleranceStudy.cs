using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Core.Units;

namespace Einzel.Sweeps;

/// <summary>One draw of a tolerance study, and what it produced.</summary>
/// <param name="Index">Which draw this was.</param>
/// <param name="Parameters">The perturbed values, by name.</param>
/// <param name="FigureOfMerit">What the model produced, or null when the draw failed.</param>
/// <param name="Failure">Why it failed, when it did.</param>
public sealed record SweepDraw(
    int Index,
    IReadOnlyDictionary<string, Quantity> Parameters,
    double? FigureOfMerit,
    string? Failure);

/// <summary>How much one channel alone moves the figure of merit.</summary>
/// <param name="Parameter">The channel's parameter.</param>
/// <param name="Low">Figure of merit at the low end of its range.</param>
/// <param name="High">Figure of merit at the high end.</param>
/// <param name="Nominal">Figure of merit at nominal.</param>
/// <param name="Swing">
/// The larger absolute departure from nominal. The ranking quantity, because what
/// is wanted is which parameter binds first rather than which has the steepest
/// slope in some averaged sense.
/// </param>
public sealed record ChannelSensitivity(
    string Parameter,
    double? Low,
    double? High,
    double Nominal,
    double Swing);

/// <summary>The outcome of a tolerance study.</summary>
/// <param name="Draws">Every draw, in order.</param>
/// <param name="Sensitivity">
/// One-at-a-time attribution, ordered by swing, largest first. Spec section 13
/// calls this "the actual deliverable, since what is wanted is not only whether
/// 100 to 300 microns suffices but which parameter binds first".
/// </param>
/// <param name="Distribution">
/// The figure of merit across the stochastic draws, as a GRD-1 envelope. Null when
/// too few draws succeeded to characterise it.
/// </param>
/// <param name="Nominal">The figure of merit at unperturbed parameters.</param>
public sealed record ToleranceStudyResult(
    IReadOnlyList<SweepDraw> Draws,
    IReadOnlyList<ChannelSensitivity> Sensitivity,
    Measured? Distribution,
    double Nominal)
{
    /// <summary>How many draws produced a figure of merit.</summary>
    public int Succeeded => Draws.Count(d => d.FigureOfMerit is not null);

    /// <summary>The channel that binds first, or null when nothing was varied.</summary>
    public ChannelSensitivity? BindingChannel => Sensitivity.Count > 0 ? Sensitivity[0] : null;
}

/// <summary>
/// Tolerance Monte Carlo over a model's declared parameters, with one-at-a-time
/// attribution alongside.
/// </summary>
/// <remarks>
/// <para>
/// Spec section 13 requires both modes: "stochastic, for the distribution of
/// achieved performance, and one-at-a-time, to attribute variance". They answer
/// different questions and neither substitutes for the other. The stochastic pass
/// says what fraction of built instruments would meet a target; the
/// one-at-a-time pass says which tolerance to tighten first, which is the
/// actionable half.
/// </para>
/// <para>
/// The study knows nothing about what it is evaluating. It takes a model, a set
/// of channels, and a function from an overridden model to a number — so the same
/// driver sweeps a mirror's resolving power, a lens's spot size, or a quadrupole's
/// transmission without alteration.
/// </para>
/// <para>
/// Draws are seeded and the sequence is reproducible. A tolerance study whose
/// result changes between runs cannot be compared against itself, and the whole
/// point of recording the seed in the manifest is that it can.
/// </para>
/// </remarks>
public static class ToleranceStudy
{
    /// <summary>Runs a study.</summary>
    /// <param name="document">The model to perturb.</param>
    /// <param name="channels">The parameters to vary, and by how much.</param>
    /// <param name="evaluate">
    /// Produces the figure of merit from a validated model, or null when the
    /// geometry does not work. Exceptions are caught and recorded as failures
    /// rather than ending the study: one impossible draw in a thousand is a
    /// result, not a crash.
    /// </param>
    /// <param name="draws">How many stochastic draws to take.</param>
    /// <param name="seed">Seed for the draw sequence.</param>
    /// <param name="oneAtATime">Whether to run the attribution pass.</param>
    /// <param name="figureDimension">
    /// The dimension of the figure of merit, for the reported envelope. Dimensionless
    /// by default, which covers resolving power and transmission; a flight time has
    /// to say so, because GRD-1 reports a quantity and a quantity without its
    /// dimension is the bare number the rule exists to prevent.
    /// </param>
    /// <param name="maxParallelism">
    /// How many draws may be evaluated at once, or null for one per processor.
    /// <para>
    /// Lower it when a single solve is large and the draws solve: each draw in flight that
    /// solves holds the solver's working set, so peak memory is this times one solve. A draw
    /// that only re-weights fields another evaluation solved holds nothing of its own when
    /// the evaluator shares them. The draws themselves are always made in seed order, so this
    /// changes what the study costs and never what it says.
    /// </para>
    /// </param>
    /// <returns>The draws, the attribution, and the distribution.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The draw count is negative.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// The model does not validate, or a channel does not name a free parameter.
    /// </exception>
    /// <param name="sourceDirectory">
    /// The directory the model document was read from, which any file it references is
    /// resolved against. Null when the caller has none, and a model declaring an
    /// imported gas field is then refused rather than run in a gas it does not
    /// describe.
    /// </param>
    public static ToleranceStudyResult Run(
        ModelDocument document,
        IReadOnlyList<PerturbationChannel> channels,
        Func<CompiledModel, double?> evaluate,
        int draws = 1000,
        int seed = 1,
        bool oneAtATime = true,
        Dimension figureDimension = default,
        string? sourceDirectory = null,
        int? maxParallelism = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentOutOfRangeException.ThrowIfNegative(draws);

        var baseline = Compile(document, null, sourceDirectory);
        var surface = baseline.Parameters;

        for (var i = 0; i < channels.Count; i++)
        {
            channels[i].Bind(surface, $"/channels/{i}");
        }

        var nominal = evaluate(baseline)
            ?? throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.ConvergenceFailed,
                Path = "/",
                Constraint = "the unperturbed model did not produce a figure of merit",
                Suggestion = "a study cannot attribute variance about a nominal that does not exist",
            });

        // DRAWN IN ORDER, EVALUATED AT ONCE. The draw sequence *is* the study: `Random` is
        // consumed one call at a time and a seeded sweep has to reproduce from its manifest
        // (PRJ-3), so drawing inside a parallel loop would race the generator and, worse,
        // make the same seed give a different study on every run.
        //
        // Splitting the two costs nothing, because the draw is arithmetic and the
        // evaluation is a field and its flights. The perturbations are identical to what the
        // sequential version produced, draw for draw.
        var perturbations = Draws(channels, surface, draws, seed);

        var parallelism = new ParallelOptions
        {
            // Memory is the constraint, not cores, when the draws solve: every draw in flight
            // that solves holds the solver's working set. See ParameterScan.Run.
            MaxDegreeOfParallelism = Math.Max(1, maxParallelism ?? Environment.ProcessorCount),
        };

        var drawn = new SweepDraw[draws];

        Parallel.For(
            0,
            draws,
            parallelism,
            d => drawn[d] = Evaluate(document, perturbations[d], evaluate, d, sourceDirectory));

        var rows = new List<SweepDraw>(drawn);

        var sensitivity = oneAtATime
            ? Attribute(document, channels, surface, evaluate, nominal, sourceDirectory, parallelism)
            : [];

        return new ToleranceStudyResult(rows, sensitivity, Distribution(rows, draws, figureDimension), nominal);
    }

    /// <summary>
    /// The draws a seeded sweep makes, in the order it makes them, before any is evaluated:
    /// what <see cref="Run"/> evaluates, for a caller that needs to know the points first.
    /// </summary>
    /// <param name="channels">The channels, bound to <paramref name="surface"/>.</param>
    /// <param name="surface">The nominal model's parameters, each channel's draw centred on its value.</param>
    /// <param name="draws">How many draws.</param>
    /// <param name="seed">The sweep's seed.</param>
    /// <returns>One override set per draw, each channel's parameter drawn in channel order.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The draw count is negative.</exception>
    public static Dictionary<string, Quantity>[] Draws(
        IReadOnlyList<PerturbationChannel> channels, ParameterSurface surface, int draws, int seed)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentOutOfRangeException.ThrowIfNegative(draws);

        var random = new Random(seed);
        var perturbations = new Dictionary<string, Quantity>[draws];

        for (var d = 0; d < draws; d++)
        {
            var overrides = new Dictionary<string, Quantity>(StringComparer.Ordinal);

            foreach (var channel in channels)
            {
                overrides[channel.Parameter] = channel.Draw(surface[channel.Parameter], random);
            }

            perturbations[d] = overrides;
        }

        return perturbations;
    }

    private static List<ChannelSensitivity> Attribute(
        ModelDocument document,
        IReadOnlyList<PerturbationChannel> channels,
        ParameterSurface surface,
        Func<CompiledModel, double?> evaluate,
        double nominal,
        string? sourceDirectory,
        ParallelOptions parallelism)
    {
        // Two evaluations per channel, all independent, and each as expensive as a draw -
        // so attribution over a dozen channels is two dozen evaluations, two dozen solves
        // when the channels move the geometry, and runs at once like the draws do. Written by index so the pre-sort order does not depend on which
        // channel finished first; the sort below is by swing and would hide a reordering
        // rather than surface it.
        var measured = new (double? Low, double? High)[channels.Count];

        Parallel.For(
            0,
            channels.Count,
            parallelism,
            index =>
            {
                var channel = channels[index];
                var (low, high) = channel.Extremes(surface[channel.Parameter]);

                double? At(Quantity value) => Evaluate(
                    document,
                    new Dictionary<string, Quantity>(StringComparer.Ordinal) { [channel.Parameter] = value },
                    evaluate,
                    -1,
                    sourceDirectory).FigureOfMerit;

                measured[index] = (At(low), At(high));
            });

        var results = new List<ChannelSensitivity>(channels.Count);

        for (var index = 0; index < channels.Count; index++)
        {
            var (atLow, atHigh) = measured[index];

            // A channel whose extreme fails outright is maximally sensitive, not
            // insensitive: it has found a geometry that does not work at all.
            var swing = atLow is null || atHigh is null
                ? double.PositiveInfinity
                : Math.Max(Math.Abs(atLow.Value - nominal), Math.Abs(atHigh.Value - nominal));

            results.Add(new ChannelSensitivity(
                channels[index].Parameter, atLow, atHigh, nominal, swing));
        }

        results.Sort((a, b) => b.Swing.CompareTo(a.Swing));
        return results;
    }

    private static SweepDraw Evaluate(
        ModelDocument document,
        Dictionary<string, Quantity> overrides,
        Func<CompiledModel, double?> evaluate,
        int index,
        string? sourceDirectory)
    {
        try
        {
            var validation = ModelValidator.Validate(document, overrides, sourceDirectory);

            if (!validation.IsValid)
            {
                // A draw outside a declared bound is a legitimate outcome to
                // record, not an error to raise: it says the tolerance range
                // reaches past what the template says is buildable.
                return new SweepDraw(index, overrides, null, validation.Errors[0].ToString());
            }

            return new SweepDraw(index, overrides, evaluate(validation.Model!), null);
        }
        catch (Core.Errors.EinzelException failure)
        {
            return new SweepDraw(index, overrides, null, failure.Error.ToString());
        }
        catch (InvalidOperationException failure)
        {
            return new SweepDraw(index, overrides, null, failure.Message);
        }
    }

    private static CompiledModel Compile(
        ModelDocument document,
        IReadOnlyDictionary<string, Quantity>? overrides,
        string? sourceDirectory)
    {
        var validation = ModelValidator.Validate(document, overrides, sourceDirectory);

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors[0]);
        }

        return validation.Model!;
    }

    private static Measured? Distribution(List<SweepDraw> rows, int attempted, Dimension dimension)
    {
        var values = rows.Where(r => r.FigureOfMerit is not null).Select(r => r.FigureOfMerit!.Value).ToArray();

        if (values.Length < 2)
        {
            return null;
        }

        var mean = values.Average();
        var deviation = Math.Sqrt(values.Sum(v => (v - mean) * (v - mean)) / (values.Length - 1));

        // A 95 percent interval about the mean of the achieved distribution, which
        // is what "what fraction of built instruments meet the target" needs.
        var half = 1.96 * deviation;
        var quantity = Quantity.Si(mean, dimension);

        var warnings = new List<ValidityWarning>();

        if (values.Length < attempted)
        {
            warnings.Add(new ValidityWarning(
                "DRAWS_FAILED",
                $"{attempted - values.Length} of {attempted} draws produced no figure of merit; "
                + "this distribution describes the survivors, which biases it toward geometries that work",
                WarningSeverity.ValidityViolation));
        }

        if (values.Length < 100)
        {
            warnings.Add(new ValidityWarning(
                "DRAWS_FEW",
                $"{values.Length} draws is a thin basis for a distribution",
                WarningSeverity.Qualified));
        }

        return new Measured(
            quantity,
            UncertaintyInterval.Symmetric(quantity, Quantity.Si(half, dimension), confidenceLevel: 0.95),
            new Evidence.Ensemble(values.Length, Converged: values.Length >= 100),
            warnings);
    }
}
