using Einzel.Core.Geometry;
using Einzel.Core.Units;

namespace Einzel.Fields.Analytic;

/// <summary>
/// A uniform field whose magnitude follows a waveform: the supplementary dipole
/// excitation a trap uses to eject an ion resonantly.
/// </summary>
/// <remarks>
/// <para>
/// The other half of what a trap does. The main RF confines every ion in the mass
/// range at once; a small AC voltage applied <em>across</em> a pair of electrodes
/// adds a spatially uniform field that pushes the whole cloud one way and then the
/// other. An ion whose secular frequency matches a component of that excitation
/// absorbs energy from it and grows until it hits something; an ion whose frequency
/// is not in the excitation does not. That is resonant ejection, and with a notched
/// broadband excitation it is isolation — spec section 12's Class B figure,
/// isolation efficiency against notch width.
/// </para>
/// <para>
/// <strong>Uniform is the right idealisation and not a shortcut.</strong> A dipolar
/// excitation across two electrodes produces, near the axis, a field that is
/// constant to first order — that is what makes it a <em>dipole</em> rather than a
/// second quadrupole. Modelling it as uniform is therefore the leading term of the
/// real thing rather than a different thing, and a solved geometry supplies the
/// corrections when one is available.
/// </para>
/// <para>
/// The frequency is declared independently of any main drive, because a
/// supplementary excitation is a separate supply at a separate frequency; that is
/// the whole point of it. Superposing this with a driven quadrupole gives a field
/// with two timescales, which
/// <see cref="DrivenSuperposedField.ShortestPeriodSeconds"/> resolves by taking the
/// faster.
/// </para>
/// </remarks>
public sealed class OscillatingUniformField : ITimeVaryingField
{
    private readonly Vec3 _amplitude;
    private readonly double _frequencyHz;
    private readonly RfWaveform _waveform;
    private readonly double[] _breakpoints;

    private OscillatingUniformField(Vec3 amplitude, double frequencyHz, RfWaveform waveform)
    {
        _amplitude = amplitude;
        _frequencyHz = frequencyHz;
        _waveform = waveform;
        _breakpoints = [.. waveform.Breakpoints];
    }

    /// <summary>Creates an oscillating uniform field.</summary>
    /// <param name="amplitude">The peak field vector, in volts per metre.</param>
    /// <param name="frequency">The fundamental frequency of the waveform.</param>
    /// <param name="waveform">The shape over one cycle. Defaults to a sinusoid.</param>
    /// <returns>The field.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The frequency is not positive.</exception>
    public static OscillatingUniformField Create(
        Vec3 amplitude, Quantity frequency, RfWaveform? waveform = null)
    {
        var hertz = frequency.In("Hz");

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);

        return new OscillatingUniformField(amplitude, hertz, waveform ?? new RfWaveform.Sinusoid());
    }

    /// <summary>The peak field vector, in volts per metre.</summary>
    public Vec3 AmplitudeSi => _amplitude;

    /// <summary>The fundamental frequency, in hertz.</summary>
    public double FrequencyHz => _frequencyHz;

    /// <inheritdoc/>
    public double MonochromaticPeriodSeconds => _amplitude.LengthSquared == 0.0
        ? double.PositiveInfinity : 1.0 / (_frequencyHz * _waveform.SingleHarmonicOrder);

    /// <summary>The waveform.</summary>
    public RfWaveform Waveform => _waveform;

    /// <inheritdoc/>
    /// <remarks>
    /// The period of the <em>highest</em> harmonic present, not of the fundamental.
    /// A comb reaching order 40 carries information forty times faster than its own
    /// repeat rate, and a step controller told only the fundamental would step over
    /// every one of those oscillations while its error estimator agreed the step was
    /// accurate - for the field the step was shown. It was not shown the field.
    /// </remarks>
    public double ShortestPeriodSeconds
    {
        get
        {
            var highest = 1;

            if (_waveform is RfWaveform.Harmonic harmonic)
            {
                foreach (var term in harmonic.Terms)
                {
                    highest = Math.Max(highest, term.Order);
                }
            }

            return 1.0 / (_frequencyHz * highest);
        }
    }

    /// <inheritdoc/>
    /// <remarks>Infinite for a waveform constant between its breakpoints.</remarks>
    public double ShortestSmoothPeriodSeconds =>
        _waveform.ConstantBetweenBreakpoints ? double.PositiveInfinity : ShortestPeriodSeconds;

    /// <inheritdoc/>
    /// <remarks>Judged on the phase the field computes, <c>t * f</c>.</remarks>
    public double NextEdgeAfter(double timeSeconds)
    {
        var clock = new TimesFrequencyClock(_frequencyHz);
        var next = double.PositiveInfinity;

        foreach (var breakpoint in _breakpoints)
        {
            next = Math.Min(next, EdgeInstants.Next(clock, _frequencyHz, 0.0, breakpoint, timeSeconds));
        }

        return next;
    }

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds) =>
        _amplitude * _waveform.At(timeSeconds * _frequencyHz);

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position, double timeSeconds) =>
        -Vec3.Dot(_amplitude, position) * _waveform.At(timeSeconds * _frequencyHz);

    /// <inheritdoc/>
    /// <remarks>The value at the start of the cycle, as every driven field reports.</remarks>
    public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

    /// <inheritdoc/>
    /// <remarks>The value at the start of the cycle, as every driven field reports.</remarks>
    public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);
}
