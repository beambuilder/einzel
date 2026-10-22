namespace Einzel.Core.Model;

/// <summary>
/// How wide a cloud of ions a source emits.
/// </summary>
/// <remarks>
/// <para>
/// Every ion so far has started from the same point at the same speed, which makes
/// every result an answer about one ion rather than about an instrument. That is
/// why every resolving power in the documentation carries the same caveat -
/// energy aberration only, no spatial spread, no angular spread, no turn-around
/// time. Three of those four are this.
/// </para>
/// <para>
/// The knobs are deliberately few. Angular divergence is one of them
/// (<see cref="DivergenceRadians"/>), separate from the temperature because a
/// thermal draw moves speed and direction together while a divergence tilts the
/// direction alone, as an aperture does.
/// </para>
/// </remarks>
public sealed record IonCloudSettings
{
    /// <summary>
    /// How many trajectories to compute. A numerical setting, not a physical one.
    /// </summary>
    /// <remarks>
    /// Spec ACC-5 wants a transmission interval within one per cent absolute at
    /// 95%, which is what sets the floor: a binomial interval of that width needs
    /// of order ten thousand ions at the worst point, and fewer is a number with
    /// an honest error bar too wide to design against.
    /// </remarks>
    public int Ions { get; init; } = 1;

    /// <summary>
    /// How many ions are in the physical packet. Defaults to <see cref="Ions"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Separate from <see cref="Ions"/> because they answer different questions and
    /// conflating them hides a real error. <see cref="Ions"/> is how hard the
    /// source distribution is sampled, and sampling harder only ever makes a
    /// statistic better. A population is how many ions are actually there at once,
    /// and more of them push each other apart.
    /// </para>
    /// <para>
    /// The default is the conservative reading: the ions simulated are the ions
    /// present. Someone measuring an intrinsic source property one ion at a time
    /// sets a population of 1 and samples as hard as they like; someone modelling
    /// a real bunch leaves it alone. Defaulting the other way would make a dense
    /// packet silently sparse, which is the failure this exists to prevent.
    /// </para>
    /// </remarks>
    public int? Population { get; init; }

    /// <summary>Seed for the draw, so a run is regenerable (PRJ-3).</summary>
    public int Seed { get; init; } = 1;

    /// <summary>
    /// Source temperature, in kelvin. Zero for a cold, monoenergetic cloud.
    /// </summary>
    /// <remarks>
    /// Each Cartesian velocity component is drawn from a Gaussian of width
    /// sqrt(kT/m), which is what a Maxwell-Boltzmann distribution is when written
    /// component by component. It is the whole of the turn-around story: an ion
    /// moving the wrong way when the field arrives has to be stopped and brought
    /// back, and the time that takes is what limits a pulsed extraction.
    /// </remarks>
    public double TemperatureK { get; init; }

    /// <summary>
    /// Gaussian width of the cloud across the direction of travel, in metres.
    /// </summary>
    public double TransverseSpreadM { get; init; }

    /// <summary>
    /// Gaussian width of the cloud along the direction of travel, in metres.
    /// </summary>
    /// <remarks>
    /// Separate from the transverse width because they do different damage. A
    /// transverse spread costs transmission; a longitudinal one costs arrival
    /// time directly, since two ions a millimetre apart along the axis are a
    /// millimetre of flight path apart.
    /// </remarks>
    public double LongitudinalSpreadM { get; init; }

    /// <summary>
    /// Gaussian width of the acceleration energy, as a fraction of nominal.
    /// </summary>
    /// <remarks>
    /// Not thermal. This is supply ripple, or an ion that started somewhere else
    /// in the accelerating field - effects that vary the energy without varying
    /// the direction, which a temperature cannot express.
    /// </remarks>
    public double EnergyFractionSpread { get; init; }

    /// <summary>
    /// Half-angle of the cone the beam fills, in radians. Zero for a parallel beam.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Directions are drawn uniformly in solid angle inside the cone, so this is a
    /// hard limit rather than a width - an aperture truncates, and the number names
    /// the largest angle that gets through rather than a typical one.
    /// </para>
    /// <para>
    /// It composes with the directed speed rather than replacing it, so the energy
    /// is unchanged: tilting a velocity does not lengthen it. That is what makes it
    /// the counterpart of <see cref="EnergyFractionSpread"/> and what a temperature
    /// cannot do, since a thermal draw changes speed and direction together.
    /// </para>
    /// </remarks>
    public double DivergenceRadians { get; init; }

    /// <summary>Whether this describes more than a single ion on the axis.</summary>
    public bool IsCloud =>
        Ions > 1
        || TemperatureK > 0.0
        || TransverseSpreadM > 0.0
        || LongitudinalSpreadM > 0.0
        || EnergyFractionSpread > 0.0
        || DivergenceRadians > 0.0;
}
