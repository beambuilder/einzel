using Einzel.Core.Geometry;
using Einzel.Fields;
using Einzel.Fields.Magnetic;
using Einzel.Fields.Solved;
using Einzel.Transport.Collisions;
using Einzel.Transport.Diffusion;

namespace Einzel.Transport.Tests;

/// <summary>
/// The mixture solve builds its time-dependent field once at the start, as the single-species
/// solve does, and refuses a magnetised one there.
/// </summary>
public sealed class MixtureFieldCallTests
{
    private const double Dalton = 1.66053906892e-27;

    private static readonly BackgroundGas Nitrogen = new()
    {
        Model = CollisionModel.Langevin,
        PressureSi = 100.0,
        TemperatureK = 300.0,
        MassSi = 28.0134 * Dalton,
        PolarizabilitySi = 1.74e-30,
        CrossSectionSi = 250e-20,
    };

    private static readonly DriftDiffusion.DomainEdges Closed = new(
        Escape.Reflecting, Escape.Reflecting, Escape.Reflecting, Escape.Reflecting);

    private static MixtureMember Member()
    {
        var grid = Grid2D.OverBox(-0.01, -0.003, 0.01, 0.003, 32, 8);
        var species = IonSpecies.FromMassToCharge(500.0, 1);
        var density = new DensityField(grid);
        density[16, 4] = 1.0 / density.CellVolume(4);

        return new MixtureMember("one", species, Mobility.FromCrossSection(Nitrogen, species), density);
    }

    [Fact]
    public void TheFieldAtTheStartIsBuiltOnce()
    {
        var field = UniformField.Create(new Vec3(10.0, 0.0, 0.0));
        var atStart = 0;

        MixtureDiffusion.Run(
            [Member()], field, Nitrogen, 1e-7, Closed,
            fieldAt: t =>
            {
                if (t == 0.0)
                {
                    atStart++;
                }

                return field;
            });

        Assert.Equal(1, atStart);
    }

    [Fact]
    public void AMagnetisedFieldFromTheRampIsStillRefused()
    {
        var field = UniformField.Create(new Vec3(10.0, 0.0, 0.0));
        var magnetised = MagnetizedField.Around(field, new UniformMagneticField(new Vec3(0.0, 0.0, 0.1)));

        var refused = Assert.Throws<Core.Errors.EinzelException>(() => MixtureDiffusion.Run(
            [Member()], field, Nitrogen, 1e-7, Closed, fieldAt: _ => magnetised));

        Assert.Contains("mixture drift-diffusion", refused.Error.Constraint, StringComparison.Ordinal);
    }
}
