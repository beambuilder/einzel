using Einzel.Core.Model;
using Einzel.Core.Units;

namespace Einzel.Fields.Analytic;

/// <summary>
/// The shape of an RF drive over one cycle.
/// </summary>
/// <remarks>
/// <para>
/// A sinusoid is what a resonant circuit produces and what the Mathieu equation
/// describes. A rectangular wave is what a switching supply produces, and it
/// changes the physics rather than merely the engineering: the equation of motion
/// becomes Meissner's rather than Mathieu's, and the stability boundaries move.
/// </para>
/// <para>
/// Modelled as a shape sampled by phase rather than as a field of its own, because
/// the drive and the geometry are independent. The same waveform drives a
/// quadrupole, a trap, or an ion guide, and the same geometry accepts any drive.
/// </para>
/// </remarks>
public abstract record RfWaveform
{
    private RfWaveform()
    {
    }

    /// <summary>The waveform value at a phase, relative to the drive amplitude.</summary>
    /// <param name="phase">Phase through the cycle, in turns; any value, wrapped where the shape needs it.</param>
    /// <returns>The dimensionless drive value.</returns>
    /// <remarks>
    /// Minus one to one is the convention, not a bound, and nothing clamps to it: a
    /// <see cref="Harmonic"/> series already exceeds it between its terms' peaks, and an
    /// expression or a table says what the drive is rather than what it ought to be.
    /// </remarks>
    public abstract double At(double phase);

    /// <summary>
    /// The mean value over a cycle, which acts as a DC component.
    /// </summary>
    /// <remarks>
    /// Zero for anything symmetric. A rectangular wave whose duty cycle is not one
    /// half has a non-zero mean, and that mean does the job a DC supply would -
    /// which is the whole trick of a digital mass filter: resolution without a
    /// second supply, tuned by switching times rather than by volts.
    /// </remarks>
    public abstract double Mean { get; }

    /// <summary>
    /// The phases, in cycles, at which the shape jumps or kinks: ascending, in [0, 1).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Declared rather than detected, because a jump is a property the author knows and a
    /// sampler can only guess at. Empty for a smooth shape; a rectangular wave declares its
    /// two edges.
    /// </para>
    /// <para>
    /// Today only <see cref="Mean"/> reads them, to put its quadrature panels' edges on the
    /// jumps. The integrator does not land on them yet - a jump inside a step is held to
    /// the steps-per-cycle cap exactly as a rectangular edge is - and landing on them is
    /// the next piece of work.
    /// </para>
    /// </remarks>
    public virtual IReadOnlyList<double> Breakpoints => [];

    /// <summary>The only non-zero harmonic order, zero for no drive, or NaN for a broad spectrum.</summary>
    public double SingleHarmonicOrder => this switch
    {
        Sinusoid => 1.0,
        Harmonic harmonic => SingleOrder(harmonic),
        _ => double.NaN,
    };

    /// <remarks>
    /// <b>NaN is the sentinel, not zero.</b> Order zero is a real term - the constant of a
    /// Fourier series - so using 0.0 to mean "nothing seen yet" conflates the two: a series
    /// carrying a DC term and a fundamental would set the running order to 0, fail the
    /// mixed-spectrum test against it, and be certified monochromatic at whatever came
    /// next. It happens not to matter for the well, because the cycle mean is subtracted
    /// before the mean square is taken, but it would be reached by accident rather than by
    /// the check. A DC term is skipped explicitly instead, and says so.
    /// </remarks>
    private static double SingleOrder(Harmonic harmonic)
    {
        var order = double.NaN;

        foreach (var term in harmonic.Terms)
        {
            // A term with no amplitude contributes nothing to oscillate, and a term of
            // order zero is a constant offset rather than an oscillation - it belongs in
            // the direct part, which is where the cycle mean puts it.
            if (term.Amplitude == 0.0 || term.Order == 0.0) continue;

            if (!double.IsNaN(order) && order != term.Order) return double.NaN;

            order = term.Order;
        }

        // Every term was constant or silent, so there is no oscillating order: zero, which
        // the caller reads as "no drive" and turns into an infinite period.
        return double.IsNaN(order) ? 0.0 : order;
    }

    /// <summary>The sinusoid a resonant drive produces. Gives the Mathieu equation.</summary>
    public sealed record Sinusoid : RfWaveform
    {
        /// <inheritdoc/>
        public override double At(double phase) => Math.Cos(2.0 * Math.PI * phase);

        /// <inheritdoc/>
        public override double Mean => 0.0;
    }

    /// <summary>
    /// The rectangular wave a switching drive produces. Gives the Meissner equation.
    /// </summary>
    /// <param name="DutyCycle">
    /// The fraction of the cycle spent at the positive level, in (0, 1). One half
    /// is a symmetric square wave.
    /// </param>
    /// <remarks>
    /// <para>
    /// Switching between two levels rather than sweeping between them, which is
    /// what makes the drive frequency independent of any resonant circuit and the
    /// mass range a matter of switching speed. Schrader, Anderson and Russell
    /// (JASMS 2024) use exactly this on a segmented quadrupole.
    /// </para>
    /// <para>
    /// The duty cycle is the second knob. At one half the wave is balanced and the
    /// working point sits on the a = 0 line; away from one half the mean is
    /// 2d - 1, which enters the equation of motion exactly where a DC offset would
    /// and moves the working point up the stability diagram without a DC supply
    /// existing anywhere in the instrument.
    /// </para>
    /// </remarks>
    public sealed record Rectangular(double DutyCycle) : RfWaveform
    {
        /// <inheritdoc/>
        /// <exception cref="ArgumentOutOfRangeException">The duty cycle is not in (0, 1).</exception>
        public override double At(double phase)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(DutyCycle);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(DutyCycle, 1.0);

            // Wrapped rather than assumed in range: a phase arrives from an
            // accumulated flight time and will not be tidy.
            var wrapped = phase - Math.Floor(phase);
            return wrapped < DutyCycle ? 1.0 : -1.0;
        }

        /// <inheritdoc/>
        public override double Mean => (2.0 * DutyCycle) - 1.0;

        /// <inheritdoc/>
        /// <remarks>The rising edge at the start of the cycle and the falling one at the duty cycle.</remarks>
        public override IReadOnlyList<double> Breakpoints => [0.0, DutyCycle];
    }

    /// <summary>
    /// An arbitrary periodic waveform, as a sum of harmonics of the drive.
    /// </summary>
    /// <param name="Terms">
    /// The harmonics. Order 0 is a constant and contributes only to
    /// <see cref="Mean"/>; order 1 is the fundamental.
    /// </param>
    /// <remarks>
    /// <para>
    /// Spec section 9 lists an arbitrary waveform among the excitations an electrode
    /// may carry, and a Fourier series is not a restriction on that: every periodic
    /// waveform is one. What it buys over a sampled table is that the description is
    /// in the <em>same terms</em> as the thing being designed. The Class B figure
    /// section 12 asks for is isolation efficiency against <strong>notch
    /// width</strong>, and a notch is a statement about a spectrum - written as
    /// harmonics it is a list with a gap in it, and written as samples it is a
    /// waveform someone has to inverse-transform first and then argue about.
    /// </para>
    /// <para>
    /// <strong>Smooth by construction, which matters to the integrator.</strong> A
    /// sampled table is piecewise something, and a discontinuity in the drive is a
    /// discontinuity in the acceleration that a Runge-Kutta step will average across
    /// without complaint - the same reason a rectangular wave needs the
    /// step-per-cycle cap. A finite harmonic sum has no jumps and no corners, so the
    /// error estimator sees what it is being asked to integrate.
    /// </para>
    /// <para>
    /// <strong>The cost is one solve, however many harmonics.</strong> A supply's
    /// potential is a scalar function of time multiplying a fixed spatial pattern,
    /// and a sum of harmonics is still one scalar function of time - so basis
    /// superposition is untouched and an arbitrary waveform costs exactly what a
    /// sinusoid costs.
    /// </para>
    /// </remarks>
    public sealed record Harmonic(IReadOnlyList<HarmonicTerm> Terms) : RfWaveform
    {
        /// <inheritdoc/>
        /// <exception cref="ArgumentException">No terms were given.</exception>
        public override double At(double phase)
        {
            if (Terms is null || Terms.Count == 0)
            {
                throw new ArgumentException(
                    "an arbitrary waveform needs at least one harmonic - an empty series is a "
                    + "drive of zero volts, which is better said by not declaring a drive");
            }

            var total = 0.0;

            foreach (var term in Terms)
            {
                // CosPi rather than Cos of a scaled argument, the same choice the
                // drive decomposition makes: Math.Cos(Math.PI) is -1 to a rounding
                // and a half-turn phase should be exact, or an antiphase term picks
                // up a quadrature component made of round-off.
                total += term.Amplitude * double.CosPi(2.0 * ((term.Order * phase) + term.Phase));
            }

            return total;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Only order zero survives averaging: every other harmonic integrates to
        /// nothing over a cycle by construction. So a DC offset is expressible here
        /// as a term rather than needing a separate supply, exactly as a rectangular
        /// wave's duty cycle expresses one.
        /// </remarks>
        public override double Mean
        {
            get
            {
                var total = 0.0;

                foreach (var term in Terms)
                {
                    if (term.Order == 0)
                    {
                        total += term.Amplitude * double.CosPi(2.0 * term.Phase);
                    }
                }

                return total;
            }
        }

        /// <summary>
        /// A comb of harmonics with a band removed: what isolates one ion and ejects
        /// the rest.
        /// </summary>
        /// <param name="lowOrder">The lowest harmonic in the comb.</param>
        /// <param name="highOrder">The highest, inclusive.</param>
        /// <param name="notchLow">The first order to leave out.</param>
        /// <param name="notchHigh">The last order to leave out, inclusive.</param>
        /// <param name="amplitude">The amplitude of each surviving harmonic.</param>
        /// <returns>The waveform.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// The comb is empty, runs backwards, or starts below order one.
        /// </exception>
        /// <exception cref="ArgumentException">The notch removes every harmonic.</exception>
        /// <remarks>
        /// <para>
        /// The excitation a stored-waveform isolation applies: every secular
        /// frequency in a band is driven <em>except</em> the one the ion of interest
        /// sits at, so everything else is resonantly pumped out of the trap and it
        /// is not. The notch width is the design variable, and the trade is the one
        /// section 12 asks to be measured - too narrow and the ion of interest is
        /// excited along with its neighbours, too wide and neighbours survive.
        /// </para>
        /// <para>
        /// Phases are the <strong>Schroeder</strong> quadratic sweep,
        /// <c>φ_k = k² / N</c>, rather than all zero. That is not cosmetic: with
        /// every harmonic in phase the terms add coherently once per cycle and the
        /// waveform is a spike of amplitude <c>N</c> that is nearly zero elsewhere,
        /// so the peak voltage grows with the harmonic count while the useful
        /// excitation does not. The quadratic sweep spreads the energy evenly across
        /// the cycle, and the crest factor stops depending on how many harmonics
        /// there are.
        /// </para>
        /// </remarks>
        public static Harmonic NotchedComb(
            int lowOrder, int highOrder, int notchLow, int notchHigh, double amplitude = 1.0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(lowOrder, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(highOrder, lowOrder);

            var terms = new List<HarmonicTerm>();
            var count = highOrder - lowOrder + 1;

            for (var order = lowOrder; order <= highOrder; order++)
            {
                if (order >= notchLow && order <= notchHigh)
                {
                    continue;
                }

                terms.Add(new HarmonicTerm(order, amplitude, (double)(order * order) / count));
            }

            if (terms.Count == 0)
            {
                throw new ArgumentException(
                    $"the notch [{notchLow}, {notchHigh}] removes every harmonic of the comb "
                    + $"[{lowOrder}, {highOrder}], leaving no excitation at all - which is a "
                    + "waveform of zero rather than a narrow one");
            }

            return new Harmonic(terms);
        }
    }

    /// <summary>
    /// A waveform written as an expression of the phase <c>p</c>, in cycles.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For the shapes a switching supply makes that are neither square nor sinusoidal - a
    /// trapezoid with a finite slew, a triangle, a clipped sine, a pulse train - written in
    /// the same grammar as every other expression in a model, so the parameters a sweep
    /// moves can move the shape too. The text is compiled once
    /// (<see cref="ExpressionEvaluator.Compile"/>) and the integrator calls the compiled
    /// function at every step; interpreting the text there would re-parse it per step.
    /// </para>
    /// <para>
    /// The phase is wrapped exactly as <see cref="Rectangular"/> wraps it, so
    /// <c>1 - 2*floor(2*p)</c> is <c>Rectangular(0.5)</c> to the bit at every phase.
    /// </para>
    /// <para>
    /// <strong>Equal by what it says, not by the delegate.</strong> Two waveforms compiled
    /// from the same text, the same values of the parameters it reads and the same
    /// breakpoints are equal, which is what a field comparing its drives needs; the
    /// compiled function is a consequence of those three and is left out.
    /// </para>
    /// </remarks>
    public sealed record Analytic : RfWaveform
    {
        /// <summary>The name of the phase in an expression: <c>p</c>, in cycles, in [0, 1).</summary>
        public const string PhaseVariable = "p";

        /// <summary>The panels <see cref="Mean"/> spreads over a whole cycle.</summary>
        private const int MeanPanels = 1024;

        /// <summary>The samples a new expression must be finite at, across a cycle.</summary>
        private const int FiniteSamples = 4096;

        private static readonly double LastPhase = Math.BitDecrement(1.0);

        private readonly Func<double, double> _shape;

        /// <summary>Compiles a waveform expression.</summary>
        /// <param name="expression">An expression of <c>p</c> and the model's parameters.</param>
        /// <param name="parameters">The model's parameters, resolved; their values are bound here.</param>
        /// <param name="breakpoints">Phases where the shape jumps or kinks, or null for none.</param>
        /// <param name="path">JSON Pointer to the expression, for the error object.</param>
        /// <exception cref="Core.Errors.EinzelException">The expression does not compile (see <see cref="ExpressionEvaluator.Compile"/>).</exception>
        /// <exception cref="ArgumentException">
        /// A breakpoint is outside [0, 1) or out of order, or the expression is not finite at
        /// one of 4096 uniform phases, a breakpoint, or the phase just below one.
        /// </exception>
        /// <remarks>
        /// The finite check samples rather than proves: <c>log(p - 0.3)</c> is refused at
        /// 0.3 because a sample lands there, and a singularity between samples is not found.
        /// The breakpoints are where an author already said something happens, so those are
        /// sampled on both sides.
        /// </remarks>
        public Analytic(
            string expression,
            IReadOnlyDictionary<string, Quantity> parameters,
            IReadOnlyList<double>? breakpoints = null,
            string path = "")
        {
            ArgumentNullException.ThrowIfNull(parameters);

            _shape = ExpressionEvaluator.Compile(expression, parameters, PhaseVariable, path);
            Expression = expression;
            Bindings = ExpressionEvaluator.References(expression)
                .Where(parameters.ContainsKey)
                .ToDictionary(name => name, name => parameters[name], StringComparer.Ordinal);
            Breakpoints = CheckedBreakpoints(breakpoints);

            var probes = Enumerable.Range(0, FiniteSamples).Select(k => k / (double)FiniteSamples)
                .Concat(Breakpoints)
                .Concat(Breakpoints.Select(b => b > 0.0 ? Math.BitDecrement(b) : LastPhase));

            foreach (var phase in probes)
            {
                var value = _shape(phase);

                if (!double.IsFinite(value))
                {
                    throw new ArgumentException(
                        $"the waveform '{expression}' is {value} at phase {phase:R}; a drive must be "
                        + "finite over the whole cycle",
                        nameof(expression));
                }
            }

            Mean = CycleMean(_shape, Breakpoints);
        }

        /// <summary>The expression text, as written.</summary>
        public string Expression { get; }

        /// <summary>The parameters the expression reads, at the values compiled into it.</summary>
        public IReadOnlyDictionary<string, Quantity> Bindings { get; }

        /// <inheritdoc/>
        public override IReadOnlyList<double> Breakpoints { get; }

        /// <inheritdoc/>
        /// <remarks>
        /// Composite eight-point Gauss-Legendre over 1024 panels, computed once: the panel
        /// edges sit on the breakpoints and each stretch between two of them is summed on its
        /// own, so a declared jump costs nothing and the rule is exact to rounding for any
        /// polynomial of degree fifteen per panel. A jump left undeclared lands inside a panel
        /// and costs up to 2/1024 of the mean - the reason to declare it.
        /// </remarks>
        public override double Mean { get; }

        /// <inheritdoc/>
        public override double At(double phase)
        {
            var wrapped = phase - Math.Floor(phase);

            // A phase a hair below a whole cycle wraps to 1.0 itself in rounding. That is
            // the end of the cycle, which is where Rectangular's comparison puts it too; the
            // expression was promised [0, 1) and gets the last phase below one.
            return _shape(wrapped < 1.0 ? wrapped : LastPhase);
        }

        /// <summary>Equal when the text, the bound values and the breakpoints are.</summary>
        /// <param name="other">The other waveform.</param>
        /// <returns>Whether the two describe the same drive.</returns>
        public bool Equals(Analytic? other) =>
            other is not null
            && string.Equals(Expression, other.Expression, StringComparison.Ordinal)
            && Breakpoints.SequenceEqual(other.Breakpoints)
            && Bindings.Count == other.Bindings.Count
            && Bindings.All(pair => other.Bindings.TryGetValue(pair.Key, out var value) && value == pair.Value);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            HashCode.Combine(StringComparer.Ordinal.GetHashCode(Expression), Breakpoints.Count);

        /// <summary>Composite Gauss-Legendre over a cycle, split at the breakpoints.</summary>
        private static double CycleMean(Func<double, double> shape, IReadOnlyList<double> breakpoints)
        {
            ReadOnlySpan<double> nodes = [0.1834346424956498, 0.5255324099163290, 0.7966664774136267, 0.9602898564975363];
            ReadOnlySpan<double> weights = [0.3626837833783620, 0.3137066458778873, 0.2223810344533745, 0.1012285362903763];

            var edges = new List<double> { 0.0 };
            edges.AddRange(breakpoints.Where(b => b > 0.0));
            edges.Add(1.0);

            var total = 0.0;

            for (var s = 0; s + 1 < edges.Count; s++)
            {
                var length = edges[s + 1] - edges[s];
                var panels = (int)Math.Ceiling(MeanPanels * length);
                var half = length / panels / 2.0;
                var segment = 0.0;

                for (var j = 0; j < panels; j++)
                {
                    var middle = edges[s] + ((2 * j + 1) * half);
                    var panel = 0.0;

                    for (var k = 0; k < nodes.Length; k++)
                    {
                        panel += weights[k] * (shape(middle - (half * nodes[k])) + shape(middle + (half * nodes[k])));
                    }

                    segment += panel * half;
                }

                total += segment;
            }

            return total;
        }
    }

    /// <summary>
    /// A waveform given as samples over one period, interpolated between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a drive that is known by measurement or by a supply's datasheet rather than by
    /// formula. The table is periodic: the sample after the last is the first, one cycle
    /// on, so interpolation runs across the wrap without a seam.
    /// </para>
    /// <para>
    /// <see cref="WaveformInterpolation.Linear"/> joins the samples with straight lines and
    /// converges on a smooth drive as the square of the spacing;
    /// <see cref="WaveformInterpolation.Cubic"/> is the periodic cubic spline, continuous
    /// in slope and curvature across the wrap, and converges as the fourth power. The
    /// spline's coefficients are solved once here (a cyclic tridiagonal system), so a
    /// value is a binary search and a cubic in either case, with nothing shared written:
    /// one table serves any number of flights at once.
    /// </para>
    /// <para>
    /// Equal when the samples, the interpolation and the breakpoints are, element by
    /// element.
    /// </para>
    /// </remarks>
    public sealed record Tabulated : RfWaveform
    {
        // Knots and values carry the closure sample, one cycle after the first; the
        // per-segment coefficients make each value y + t(b + t(c + t d)).
        private readonly double[] _knots;
        private readonly double[] _values;
        private readonly double[] _slope;
        private readonly double[] _curve;
        private readonly double[] _cubic;

        /// <summary>Builds the interpolant over one period of samples.</summary>
        /// <param name="phases">Sample phases in cycles, strictly ascending in [0, 1).</param>
        /// <param name="values">The drive at each phase, relative to the amplitude; finite.</param>
        /// <param name="interpolation">How to join the samples.</param>
        /// <param name="breakpoints">Phases where the shape jumps or kinks, or null for none.</param>
        /// <exception cref="ArgumentException">
        /// Fewer than three samples, unequal counts, a phase outside [0, 1) or out of order, a
        /// value that is not finite, or a bad breakpoint.
        /// </exception>
        public Tabulated(
            IReadOnlyList<double> phases,
            IReadOnlyList<double> values,
            WaveformInterpolation interpolation = WaveformInterpolation.Linear,
            IReadOnlyList<double>? breakpoints = null)
        {
            ArgumentNullException.ThrowIfNull(phases);
            ArgumentNullException.ThrowIfNull(values);

            var n = phases.Count;

            if (n < 3 || values.Count != n)
            {
                throw new ArgumentException(
                    $"a waveform table needs at least three samples and one value per phase, but has "
                    + $"{n} phases and {values.Count} values", nameof(phases));
            }

            for (var i = 0; i < n; i++)
            {
                if (!(phases[i] >= 0.0 && phases[i] < 1.0) || (i > 0 && !(phases[i] > phases[i - 1])))
                {
                    throw new ArgumentException(
                        $"sample {i} is at phase {phases[i]:R}; phases are in cycles, strictly ascending in [0, 1)",
                        nameof(phases));
                }

                if (!double.IsFinite(values[i]))
                {
                    throw new ArgumentException($"sample {i} has the value {values[i]}; a drive must be finite", nameof(values));
                }
            }

            Phases = [.. phases];
            Values = [.. values];
            Interpolation = interpolation;
            Breakpoints = CheckedBreakpoints(breakpoints);

            _knots = [.. phases, phases[0] + 1.0];
            _values = [.. values, values[0]];
            var h = new double[n];

            for (var i = 0; i < n; i++)
            {
                h[i] = _knots[i + 1] - _knots[i];
            }

            // Second derivatives at the knots: zero for straight lines, the periodic
            // spline's otherwise.
            var m = interpolation == WaveformInterpolation.Cubic ? SplineCurvatures(h, _values) : new double[n];
            _slope = new double[n];
            _curve = new double[n];
            _cubic = new double[n];
            var mean = 0.0;

            for (var i = 0; i < n; i++)
            {
                var next = m[(i + 1) % n];
                _slope[i] = ((_values[i + 1] - _values[i]) / h[i]) - (h[i] * ((2.0 * m[i]) + next) / 6.0);
                _curve[i] = m[i] / 2.0;
                _cubic[i] = (next - m[i]) / (6.0 * h[i]);

                // The segment's exact integral: the trapezoid, less the spline's correction.
                mean += (h[i] * (_values[i] + _values[i + 1]) / 2.0) - (h[i] * h[i] * h[i] * (m[i] + next) / 24.0);
            }

            Mean = mean;
        }

        /// <summary>The sample phases, in cycles.</summary>
        public IReadOnlyList<double> Phases { get; }

        /// <summary>The sampled values.</summary>
        public IReadOnlyList<double> Values { get; }

        /// <summary>How the samples are joined.</summary>
        public WaveformInterpolation Interpolation { get; }

        /// <inheritdoc/>
        public override IReadOnlyList<double> Breakpoints { get; }

        /// <inheritdoc/>
        /// <remarks>The interpolant's exact integral over the period, not a further quadrature.</remarks>
        public override double Mean { get; }

        /// <inheritdoc/>
        public override double At(double phase)
        {
            var wrapped = phase - Math.Floor(phase);

            // A table that starts after phase zero reaches the start of the cycle through
            // its closing segment, one cycle on.
            if (wrapped < _knots[0])
            {
                wrapped += 1.0;
            }

            var i = Array.BinarySearch(_knots, wrapped);
            i = Math.Clamp(i < 0 ? ~i - 1 : i, 0, _slope.Length - 1);
            var t = wrapped - _knots[i];

            return _values[i] + (t * (_slope[i] + (t * (_curve[i] + (t * _cubic[i])))));
        }

        /// <summary>Equal when the samples, the interpolation and the breakpoints are.</summary>
        /// <param name="other">The other waveform.</param>
        /// <returns>Whether the two describe the same drive.</returns>
        public bool Equals(Tabulated? other) =>
            other is not null
            && Interpolation == other.Interpolation
            && Phases.SequenceEqual(other.Phases)
            && Values.SequenceEqual(other.Values)
            && Breakpoints.SequenceEqual(other.Breakpoints);

        /// <inheritdoc/>
        public override int GetHashCode() => HashCode.Combine(Interpolation, Phases.Count, Values[0]);

        /// <summary>
        /// The periodic spline's second derivatives: a cyclic tridiagonal system, solved by
        /// Sherman-Morrison around the Thomas algorithm.
        /// </summary>
        private static double[] SplineCurvatures(double[] h, double[] y)
        {
            var n = h.Length;
            var sub = new double[n];
            var diagonal = new double[n];
            var super = new double[n];
            var rhs = new double[n];

            for (var i = 0; i < n; i++)
            {
                var before = h[(i + n - 1) % n];
                var previous = y[(i + n - 1) % n];
                sub[i] = before;
                diagonal[i] = 2.0 * (before + h[i]);
                super[i] = h[i];
                rhs[i] = 6.0 * (((y[i + 1] - y[i]) / h[i]) - ((y[i] - previous) / before));
            }

            // The corners: M[n-1] in the first row, M[0] in the last.
            var top = sub[0];
            var bottom = super[n - 1];
            var gamma = -diagonal[0];
            diagonal[0] -= gamma;
            diagonal[n - 1] -= bottom * top / gamma;

            var x = Thomas(sub, diagonal, super, rhs);
            var u = new double[n];
            u[0] = gamma;
            u[n - 1] = bottom;
            var z = Thomas(sub, diagonal, super, u);
            var factor = (x[0] + (top * x[n - 1] / gamma)) / (1.0 + z[0] + (top * z[n - 1] / gamma));

            for (var i = 0; i < n; i++)
            {
                x[i] -= factor * z[i];
            }

            return x;
        }

        private static double[] Thomas(double[] sub, double[] diagonal, double[] super, double[] rhs)
        {
            var n = rhs.Length;
            var c = new double[n];
            var x = new double[n];
            var pivot = diagonal[0];
            x[0] = rhs[0] / pivot;

            for (var i = 1; i < n; i++)
            {
                c[i] = super[i - 1] / pivot;
                pivot = diagonal[i] - (sub[i] * c[i]);
                x[i] = (rhs[i] - (sub[i] * x[i - 1])) / pivot;
            }

            for (var i = n - 2; i >= 0; i--)
            {
                x[i] -= c[i + 1] * x[i + 1];
            }

            return x;
        }
    }

    /// <summary>A copy of declared breakpoints, refused unless ascending in [0, 1).</summary>
    private static double[] CheckedBreakpoints(IReadOnlyList<double>? breakpoints)
    {
        if (breakpoints is null)
        {
            return [];
        }

        for (var i = 0; i < breakpoints.Count; i++)
        {
            if (!(breakpoints[i] >= 0.0 && breakpoints[i] < 1.0) || (i > 0 && !(breakpoints[i] > breakpoints[i - 1])))
            {
                throw new ArgumentException(
                    $"breakpoint {i} is at phase {breakpoints[i]:R}; breakpoints are phases in cycles, "
                    + "strictly ascending in [0, 1)",
                    nameof(breakpoints));
            }
        }

        return [.. breakpoints];
    }
}

/// <summary>One harmonic of an arbitrary waveform.</summary>
/// <param name="Order">
/// Which multiple of the drive frequency. Zero is a constant offset, one the
/// fundamental.
/// </param>
/// <param name="Amplitude">Its amplitude, relative to the declared drive amplitude.</param>
/// <param name="Phase">
/// Its phase, in turns rather than radians - so a half is exactly antiphase and a
/// quarter exactly quadrature, with no rounding in either.
/// </param>
public readonly record struct HarmonicTerm(int Order, double Amplitude, double Phase);
