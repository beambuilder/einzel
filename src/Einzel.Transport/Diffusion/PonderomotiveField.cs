using Einzel.Core.Geometry;
using Einzel.Fields;

namespace Einzel.Transport.Diffusion;

/// <summary>
/// A driven field presented as the time-averaged effective field an ion in a gas
/// actually drifts through.
/// </summary>
/// <remarks>
/// <para>
/// The drift-diffusion solve steps a density through one static field. A driven
/// structure has no static field, and sampling one at a chosen instant gives the RF
/// at that phase - a field that exists for no length of time. What a slow ion
/// experiences instead is the cycle average: the oscillating field pushes it back
/// and forth, and because the field is stronger at one end of that excursion than
/// the other, the round trip leaves a net force towards weaker field. That net
/// force is the gradient of an effective potential, and this presents the sum of it
/// and the DC potential as an ordinary field.
/// </para>
/// <para>
/// <b>The derivation, because the collisional form is not the one usually quoted.</b>
/// An ion at position x0 quivering by delta in a field E0(x) cos(Omega t), damped at
/// rate nu, obeys m(v' + nu v) = q E0 cos(Omega t). The steady quiver has
/// v = (q E0 / m)(nu cos + Omega sin)/(Omega^2 + nu^2), so the displacement is
/// delta = (q E0 / m)(nu sin - Omega cos)/(Omega (Omega^2 + nu^2)). The cycle-averaged
/// force is q (dE0/dx) times the average of delta cos(Omega t), which is
/// -q E0 / (2 m (Omega^2 + nu^2)). That is the gradient of
/// </para>
/// <para>
/// <c>Psi = q^2 E0^2 / (4 m (Omega^2 + nu^2))</c>
/// </para>
/// <para>
/// which reduces to Dehmelt's q^2 E0^2 / (4 m Omega^2) when collisions are rare and
/// is suppressed by Omega^2/(Omega^2 + nu^2) when they are not. That suppression is
/// the whole reason this class exists rather than a one-line addition of the
/// textbook pseudopotential: at the pressures an ion funnel runs at, the collision
/// rate is comparable to the drive frequency and the well is a fraction of what the
/// collisionless formula promises.
/// </para>
/// <para>
/// <b>The damping rate is the momentum-transfer rate, not the collision rate.</b>
/// It is taken from the mobility - nu = q/(m mu) - rather than from the number of
/// collisions per cycle, and the difference is not small. A heavy ion in a light gas
/// gives up only about the mass ratio of its momentum per collision, so for m/z 500
/// in nitrogen the collision count overstates the damping by roughly twenty times.
/// Taking it from the mobility also keeps it consistent with the drift the same
/// solve computes, which a second independent estimate would not be.
/// </para>
/// <para>
/// Written as a field wrapper so the solver needs no change at all: it asks for a
/// potential and a field at a point, and gets the effective ones. The same choice
/// <c>AxisymmetricField</c> makes.
/// </para>
/// </remarks>
public sealed class PonderomotiveField : IElectrostaticField
{
    /// <summary>
    /// Differencing step for a field with no resolution of its own, in metres.
    /// </summary>
    /// <remarks>
    /// A micrometre: below anything this engine meshes and far above the scale where
    /// cancellation in a double would start to matter.
    /// </remarks>
    private const double DefaultStepM = 1e-6;

    internal bool HasSameWellAs(PonderomotiveField other) =>
        _massSi == other._massSi && _chargeSi == other._chargeSi
        && CollisionRateSi == other.CollisionRateSi && ReferenceEquals(_rateAt, other._rateAt)
        && _samples == other._samples && _step == other._step && PeriodSeconds == other.PeriodSeconds
        && (_driven.HasSameOscillationAs(other._driven)
            || (_driven is TimeShiftedField a && other._driven is TimeShiftedField b && a.HasSameCycleAs(b)));

    private readonly ITimeVaryingField _driven;
    private readonly double _massSi;
    private readonly Func<Vec3, double>? _rateAt;
    private readonly double _chargeSi;
    private readonly double _scale;
    private readonly double _step;
    private readonly int _samples;

    /// <summary>Wraps a driven field for an ion of a given species in a given gas.</summary>
    /// <param name="driven">The driven field.</param>
    /// <param name="chargeSi">The ion's charge, in coulombs.</param>
    /// <param name="massSi">The ion's mass, in kilograms.</param>
    /// <param name="collisionRateSi">
    /// Momentum-transfer rate, in inverse seconds. Zero for a collisionless ion,
    /// which gives the Dehmelt pseudopotential.
    /// </param>
    /// <param name="samplesPerCycle">
    /// How finely the drive cycle is sampled when averaging. Sixteen resolves a
    /// sinusoid to well under a per cent and a rectangular wave to its duty cycle.
    /// </param>
    /// <param name="collisionRateAt">
    /// The momentum-transfer rate at a point, or null where the gas is uniform. A
    /// pressure field grades the damping, because the rate goes as the density; where
    /// this is null the constant rate above is used everywhere and the arithmetic is
    /// bit-identical to what it was before a graded gas could be declared.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="driven"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A non-positive mass or sample count, a negative collision rate, or a drive
    /// with no period.
    /// </exception>
    public PonderomotiveField(
        ITimeVaryingField driven,
        double chargeSi,
        double massSi,
        double collisionRateSi,
        int samplesPerCycle = 16,
        Func<Vec3, double>? collisionRateAt = null)
    {
        ArgumentNullException.ThrowIfNull(driven);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massSi);
        ArgumentOutOfRangeException.ThrowIfNegative(collisionRateSi);
        ArgumentOutOfRangeException.ThrowIfLessThan(samplesPerCycle, 4);

        // B is applied by single-ion trajectory integration alone; dropping it here would
        // fly the model as though the magnet were absent, with nothing to say so.
        Einzel.Fields.Magnetic.MagneticRefusal.ThrowIfMagnetic(driven, "the cycle-averaged RF well");

        if (!double.IsFinite(driven.ShortestPeriodSeconds) || driven.ShortestPeriodSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(driven), "an effective RF well requires an oscillating field; this field has no drive");
        var period = driven.MonochromaticPeriodSeconds;
        if (double.IsNaN(period))
        {
            throw new Einzel.Core.Errors.EinzelException(new Einzel.Core.Errors.EinzelError
            {
                Code = Einzel.Core.Errors.ErrorCodes.RegimeInvalid,
                Path = "/transport/mode",
                Constraint = "the single-frequency pseudopotential requires a known monochromatic RF spectrum at a held operating point",
                Suggestion = "use one sinusoidal frequency (or a single Fourier harmonic), or trajectory transport where valid; multiple frequencies require a spectrally weighted effective field",
            });
        }
        // A switched-off generator has no well, but retains a clock on which the
        // constant potential can be sampled. It must not set another generator's period.
        if (double.IsPositiveInfinity(period)) period = driven.ShortestPeriodSeconds;

        // Finite, not merely positive. A field that varies in time WITHOUT a drive - a DC
        // ramp - reports an infinite shortest period, and ThrowIfNegativeOrZero lets
        // infinity straight through: the angular frequency becomes zero, the well depth
        // becomes q^2/(2 m nu^2), and the cycle average samples at infinity times s over
        // the sample count, which is NaN at s = 0. A ramp is not an oscillation and has
        // no cycle to average over; the caller has to hand this a field that has one.
        if (!double.IsFinite(period) || period <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(driven),
                period,
                "a pseudopotential averages over one period of a drive, and this field has "
                + "no drive - its shortest period is not finite. A field that varies in time "
                + "because a parameter ramps is not an oscillation and is not to be averaged");
        }

        _driven = driven;
        _chargeSi = chargeSi;
        _massSi = massSi;
        _samples = samplesPerCycle;
        _rateAt = collisionRateAt;

        PeriodSeconds = period;
        AngularFrequencySi = 2.0 * Math.PI / period;
        CollisionRateSi = collisionRateSi;

        var damped = (AngularFrequencySi * AngularFrequencySi) + (collisionRateSi * collisionRateSi);

        // Psi = q^2 E0^2 / (4 m (Omega^2 + nu^2)), and the mean square of the
        // oscillating field over a cycle is E0^2 / 2 for a linear polarisation - so
        // in terms of that mean square the coefficient is q^2 / (2 m (...)), which
        // is also the right generalisation when the field traces an ellipse.
        _scale = chargeSi * chargeSi / (2.0 * massSi * damped);

        Suppression = AngularFrequencySi * AngularFrequencySi / damped;

        // Differencing the effective potential over half the resolution of whatever
        // is underneath: fine enough to follow it, coarse enough not to be
        // differencing interpolation noise.
        //
        // Finite, not merely positive. An analytic field reports a resolution of
        // positive infinity - meaning it has no resolution limit, not that it has an
        // enormous one - and reading that as a length gave a step of infinity, a
        // difference of infinity minus infinity, and a field of NaN. The potential
        // was correct throughout; only its gradient was not a number.
        var resolution = driven.ResolutionLength;

        _step = double.IsFinite(resolution) && resolution > 0.0
            ? 0.5 * resolution
            : DefaultStepM;
    }

    /// <summary>The drive period, in seconds.</summary>
    public double PeriodSeconds { get; }

    /// <summary>Angular drive frequency, in radians per second.</summary>
    public double AngularFrequencySi { get; }

    /// <summary>Momentum-transfer rate, in inverse seconds.</summary>
    /// <remarks>
    /// At the model's declared density. Where a pressure field grades the gas the
    /// rate varies with it - the momentum-transfer rate goes as the density, since
    /// mobility goes as its reciprocal - and this is the value at the reference,
    /// reported so a reader has one number to hold the range against.
    /// </remarks>
    public double CollisionRateSi { get; }

    /// <summary>Whether the damping varies from place to place.</summary>
    public bool IsGraded => _rateAt is not null;

    /// <summary>How much collisions weaken the well at a point.</summary>
    /// <param name="position">Where, in metres.</param>
    /// <returns>Omega^2 / (Omega^2 + nu(x)^2), between zero and one.</returns>
    /// <remarks>
    /// One where the drive is far faster than the collisions and the textbook
    /// Dehmelt well stands; falling toward zero as the quiver is damped out of
    /// existence. A graded gas grades this, because the momentum-transfer rate goes
    /// as the density.
    /// </remarks>
    public double SuppressionAt(in Vec3 position)
    {
        if (_rateAt is null)
        {
            return Suppression;
        }

        var rate = _rateAt(position);
        var omega = AngularFrequencySi * AngularFrequencySi;

        return omega / (omega + (rate * rate));
    }

    /// <summary>
    /// The coefficient turning a mean-square field into an effective energy, at a point.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A constant where the gas is uniform, and returned as the precomputed one so
    /// that an ungraded model is bit-identical to what it was.
    /// </para>
    /// <para>
    /// <b>The gradient of this is a real force.</b> Where the damping varies the
    /// effective well varies with it even at constant field amplitude, and
    /// differencing the potential picks that up - which is right: an ion in a funnel
    /// whose gas thins toward the exit sees a well that deepens toward the exit, and
    /// a version holding the damping at one declared value would report the well as
    /// flat where it is not. It holds to the same order the cycle average itself
    /// does, which is that the gas changes little over one quiver amplitude.
    /// </para>
    /// </remarks>
    private double ScaleAt(in Vec3 position)
    {
        if (_rateAt is null)
        {
            return _scale;
        }

        var rate = _rateAt(position);
        var damped = (AngularFrequencySi * AngularFrequencySi) + (rate * rate);

        return _chargeSi * _chargeSi / (2.0 * _massSi * damped);
    }

    /// <summary>
    /// How much collisions weaken the effective well, as
    /// Omega^2 / (Omega^2 + nu^2).
    /// </summary>
    /// <remarks>
    /// One when collisions are rare, and the factor the textbook pseudopotential
    /// overstates the well by when they are not. Reported whether or not it crosses
    /// a threshold, per REG-2: a reader who sees 0.9 knows the question was asked.
    /// </remarks>
    public double Suppression { get; }

    /// <inheritdoc/>
    public double ResolutionLength => _driven.ResolutionLength;

    /// <summary>
    /// The central-difference step the effective field is taken over, in metres.
    /// </summary>
    /// <remarks>
    /// Half the resolution of whatever is underneath, or a micrometre where that is
    /// infinite. Reported because a caller that caches the well has to cache it at
    /// exactly the points the gradient asks about, and a step it guessed at would
    /// give a different answer from the one this class computes.
    /// </remarks>
    public double DifferencingStepM => _step;

    /// <summary>
    /// The mesh the oscillating part of the wrapped field is known on, which is what the
    /// quiver has to be compared against; infinite where the drive is analytic.
    /// </summary>
    public double OscillatingResolutionLength => _driven.OscillatingResolutionLength;

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _driven.SignedDistanceToDiscontinuity(in position);

    /// <summary>The effective potential: the DC part plus the ponderomotive well.</summary>
    /// <param name="position">Where to evaluate.</param>
    /// <returns>The potential, in volts.</returns>
    /// <remarks>
    /// Exactly the sum of its two halves, and asserted to be so bit-for-bit: every
    /// diffusive number this engine has published through a driven geometry comes
    /// through here, so the split that lets a caller cache one half must not move the
    /// whole by a bit.
    /// </remarks>
    public double PotentialAt(in Vec3 position) =>
        DirectPotentialAt(in position) + WellAt(in position);

    /// <summary>
    /// The direct half of the effective potential: the cycle mean of the potential.
    /// </summary>
    /// <param name="position">Where to evaluate.</param>
    /// <returns>The potential, in volts.</returns>
    /// <remarks>
    /// The half a ramp moves. In an elution scan the RF amplitudes are held and only
    /// the DC is walked, so this changes at every step and
    /// <see cref="WellAt(in Vec3)"/> does not - which is what makes caching the well
    /// worth doing and why the two are separable at all.
    /// </remarks>
    public double DirectPotentialAt(in Vec3 position)
    {
        // Asked for rather than sampled here. Where the field can give its window mean in
        // closed form this is one evaluation per channel instead of `_samples` evaluations of
        // the whole composite, and on a driven diffusive run that is the dominant cost: this
        // is called once per density node per step. Fields that cannot fall back to the same
        // sampling loop, so the answer is unchanged either way.
        return _driven.CycleMeanPotentialAt(in position, 0.0, PeriodSeconds, _samples);
    }

    /// <summary>The ponderomotive well at a point, as a potential in volts.</summary>
    /// <param name="position">Where to evaluate.</param>
    /// <returns>The well, in volts.</returns>
    /// <remarks>
    /// <para>
    /// The expensive half: two passes of the cycle sampling over the field, against
    /// one pass over the potential for the direct term.
    /// </para>
    /// <para>
    /// Psi is an energy; divided by the charge it is a potential, which is what the
    /// drift-diffusion solve wants. The sign is such that a strong-field region is
    /// uphill for either polarity, because the ponderomotive force always pushes
    /// towards weaker field - so the charge divides out in magnitude but not in sign,
    /// and dividing by the signed charge here is what makes an anion feel the same
    /// well as a cation.
    /// </para>
    /// </remarks>
    public double WellAt(in Vec3 position) =>
        ScaleAt(in position) * MeanSquareOscillating(in position) / _chargeSi;

    /// <summary>The cycle mean square of the oscillating field, in volts squared per metre squared.</summary>
    /// <remarks>
    /// Two passes rather than one because the oscillating part is defined relative to
    /// the cycle mean, and a structure with an asymmetric duty cycle has a mean that
    /// is not zero - which is a real DC offset and belongs in the direct term, not in
    /// the well.
    /// </remarks>
    private double MeanSquareOscillating(in Vec3 position)
    {
        var mean = default(Vec3);

        for (var s = 0; s < _samples; s++)
        {
            mean += _driven.ElectricFieldAt(in position, PeriodSeconds * s / _samples);
        }

        mean *= 1.0 / _samples;

        var meanSquare = 0.0;

        for (var s = 0; s < _samples; s++)
        {
            var oscillating =
                _driven.ElectricFieldAt(in position, PeriodSeconds * s / _samples) - mean;

            meanSquare += oscillating.LengthSquared;
        }

        return meanSquare / _samples;
    }

    /// <summary>The effective field: minus the gradient of the effective potential.</summary>
    /// <param name="position">Where to evaluate.</param>
    /// <returns>The field vector, in volts per metre.</returns>
    /// <remarks>
    /// By central differences rather than analytically, because the ponderomotive
    /// term is a cycle average of a superposed interpolant and its closed-form
    /// gradient would be a second implementation of the same quantity - the kind of
    /// pair that agrees until one of them is changed.
    /// </remarks>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var h = _step;

        var x = (PotentialAt(position + new Vec3(h, 0.0, 0.0))
            - PotentialAt(position - new Vec3(h, 0.0, 0.0))) / (2.0 * h);

        var y = (PotentialAt(position + new Vec3(0.0, h, 0.0))
            - PotentialAt(position - new Vec3(0.0, h, 0.0))) / (2.0 * h);

        var z = (PotentialAt(position + new Vec3(0.0, 0.0, h))
            - PotentialAt(position - new Vec3(0.0, 0.0, h))) / (2.0 * h);

        return new Vec3(-x, -y, -z);
    }

    /// <summary>
    /// How far the ion is swept back and forth by the drive at a point, in metres.
    /// </summary>
    /// <param name="position">Where to evaluate.</param>
    /// <returns>The quiver amplitude.</returns>
    /// <remarks>
    /// The effective potential is an average over an excursion, so it only describes
    /// anything if the field is roughly linear across that excursion. When the
    /// quiver approaches the scale the field varies on, the averaging has nothing
    /// left to average and the ion's real motion is not a small wobble about a drift
    /// - it is the whole story.
    /// </remarks>
    public double QuiverAmplitude(in Vec3 position)
    {
        // The same mean square the well is built from, rather than a second copy of
        // the loops: a pair of implementations of one quantity agrees until one of
        // them is changed.
        var meanSquare = MeanSquareOscillating(in position);

        // Amplitude of a linear polarisation carrying this mean square.
        var amplitude = Math.Sqrt(2.0 * meanSquare);

        // The rate here, not the rate at the reference density: the quiver is damped
        // by the gas the ion is actually in, and rf.quiver-exceeds-mesh is checked
        // against this. Identical to CollisionRateSi where the gas is uniform, so an
        // ungraded model is bit-identical.
        var rate = _rateAt is null ? CollisionRateSi : _rateAt(position);

        var damped = Math.Sqrt((AngularFrequencySi * AngularFrequencySi) + (rate * rate));

        // delta = q E0 / (m Omega sqrt(Omega^2 + nu^2)), and q^2/(2 m (...)) is
        // already in the scale, so q/m is 2 scale (Omega^2 + nu^2) / q.
        var chargeToMass = 2.0 * ScaleAt(in position) * damped * damped / _chargeSi;

        // MAGNITUDE, because a quiver amplitude is a length. `ScaleAt` carries the charge
        // SQUARED, so the quotient above takes the sign of the charge and an anion got a
        // negative excursion - which made `rf.quiver-exceeds-mesh` unable to fire for a
        // negative ion at any amplitude, since the comparison is against a cell size.
        // Unlike the well, which is a potential and is legitimately signed by the charge.
        return Math.Abs(chargeToMass) * amplitude / (AngularFrequencySi * damped);
    }

    /// <summary>
    /// The momentum-transfer rate implied by a mobility, in inverse seconds.
    /// </summary>
    /// <param name="chargeSi">The ion's charge, in coulombs.</param>
    /// <param name="massSi">The ion's mass, in kilograms.</param>
    /// <param name="mobilitySi">Mobility, in square metres per volt second.</param>
    /// <returns>The rate, or zero for an infinite mobility.</returns>
    /// <remarks>
    /// Drude: a steady field gives a drift q E / (m nu), so mu = q/(m nu). Using the
    /// mobility the solve already has keeps the damping in the well consistent with
    /// the drift beside it, which a separately estimated collision frequency would
    /// not be - and it is the momentum-transfer rate, which is the one the equation
    /// of motion wants.
    /// </remarks>
    public static double CollisionRateFromMobility(double chargeSi, double massSi, double mobilitySi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(massSi);

        return mobilitySi > 0.0 ? Math.Abs(chargeSi) / (massSi * mobilitySi) : 0.0;
    }
}
