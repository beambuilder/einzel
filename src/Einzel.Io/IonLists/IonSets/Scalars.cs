using Einzel.Core.Errors;

namespace Einzel.Io.IonLists;

/// <summary>The scalar generators: each gives the i-th ion's value, drawing from the set's stream.</summary>
internal static class Scalars
{
    /// <summary>Uniform on [min, max): one draw.</summary>
    internal static Func<int, double> Uniform(UniformDocument uniform, string path, Random random)
    {
        IonList.Require(uniform.Max >= uniform.Min, ErrorCodes.ValueOutOfBounds, path + "/max",
            "a uniform range's max is not below its min");

        return _ => uniform.Min + ((uniform.Max - uniform.Min) * random.NextDouble());
    }

    /// <summary>Normal: two draws per attempt, as many attempts as the polar method needs.</summary>
    internal static Func<int, double> Gaussian(GaussianDocument gaussian, string path, Random random)
    {
        IonList.Require(gaussian.Sigma >= 0.0, ErrorCodes.ValueOutOfBounds, path + "/sigma",
            "a standard deviation is not negative");

        return _ => gaussian.Mean + (gaussian.Sigma * StandardNormal(random));
    }

    /// <summary><c>first + i * step</c>; no draw.</summary>
    internal static Func<int, double> Sequence(SequenceDocument sequence) =>
        i => sequence.First + (i * sequence.Step);

    /// <summary>Both ends exact; no draw. Not prefix-stable: the spacing depends on n.</summary>
    internal static Func<int, double> Linspace(LinspaceDocument linspace, int n) => i =>
    {
        var t = n == 1 ? 0.0 : (double)i / (n - 1);
        return (linspace.First * (1.0 - t)) + (linspace.Last * t);
    };

    /// <summary>A standard normal deviate by the polar Box-Muller method, as <c>IonCloud</c> draws one.</summary>
    internal static double StandardNormal(Random random)
    {
        double u, v, s;

        do
        {
            u = (2.0 * random.NextDouble()) - 1.0;
            v = (2.0 * random.NextDouble()) - 1.0;
            s = (u * u) + (v * v);
        }
        while (s is <= 0.0 or >= 1.0);

        return u * Math.Sqrt(-2.0 * Math.Log(s) / s);
    }
}
