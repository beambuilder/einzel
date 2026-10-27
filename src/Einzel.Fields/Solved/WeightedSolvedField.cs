using Einzel.Core.Geometry;

namespace Einzel.Fields.Solved;

/// <summary>
/// A static field that is a fixed weighted sum of solved channels on one mesh: what a static
/// element is under a <see cref="FieldBasisPlan"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Time-free, as the one channel it stands for is.</b> A <see cref="DrivenSolvedField"/>
/// weights channels too, but it is a time-varying field, and a static model built as one would
/// take the integrator's time-varying path - a different step control for the same physics.
/// This answers only the static interface, so a static model under a plan flies as a static
/// model.
/// </para>
/// <para>
/// One interpolation per channel with a non-zero weight, summed in channel order. Every
/// channel is the same mesh round the same conductors, so where the metal is, how fine the
/// mesh is and where a field-free run ends are the first channel's answers.
/// </para>
/// </remarks>
public sealed class WeightedSolvedField : IElectrostaticField, IConductorBounded
{
    private readonly IElectrostaticField[] _channels;
    private readonly double[] _weights;

    /// <summary>Weights solved channels into one static field.</summary>
    /// <param name="channels">The channels, each a field on the same mesh.</param>
    /// <param name="weights">One weight per channel, in volts per unit of that channel.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">There is no channel, or not one weight per channel.</exception>
    public WeightedSolvedField(IReadOnlyList<IElectrostaticField> channels, IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(weights);

        if (channels.Count == 0 || channels.Count != weights.Count)
        {
            throw new ArgumentException(
                $"a weighted field needs one weight per channel and at least one channel; got {channels.Count} and {weights.Count}",
                nameof(weights));
        }

        _channels = [.. channels];
        _weights = [.. weights];
    }

    /// <summary>The channels, in the order they are summed.</summary>
    public IReadOnlyList<IElectrostaticField> Channels => _channels;

    /// <summary>The weights, one per channel.</summary>
    public IReadOnlyList<double> Weights => _weights;

    /// <inheritdoc/>
    public Vec3 ElectricFieldAt(in Vec3 position)
    {
        var total = Vec3.Zero;

        for (var k = 0; k < _channels.Length; k++)
        {
            if (_weights[k] != 0.0)
            {
                total += _channels[k].ElectricFieldAt(in position) * _weights[k];
            }
        }

        return total;
    }

    /// <inheritdoc/>
    public double PotentialAt(in Vec3 position)
    {
        var total = 0.0;

        for (var k = 0; k < _channels.Length; k++)
        {
            if (_weights[k] != 0.0)
            {
                total += _channels[k].PotentialAt(in position) * _weights[k];
            }
        }

        return total;
    }

    /// <inheritdoc/>
    public double FieldFreeRunLength(in Vec3 position, in Vec3 direction) =>
        _channels[0].FieldFreeRunLength(in position, in direction);

    /// <inheritdoc/>
    public double SignedDistanceToDiscontinuity(in Vec3 position) =>
        _channels[0].SignedDistanceToDiscontinuity(in position);

    /// <inheritdoc/>
    public double ResolutionLength => _channels[0].ResolutionLength;

    /// <inheritdoc/>
    public double SignedDistanceToConductor(in Vec3 position) =>
        _channels[0] is IConductorBounded bounded
            ? bounded.SignedDistanceToConductor(in position)
            : double.PositiveInfinity;

    /// <inheritdoc/>
    public string? ConductorAt(in Vec3 position) =>
        _channels[0] is IConductorBounded bounded ? bounded.ConductorAt(in position) : null;
}
