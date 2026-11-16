using static Einzel.Library.Tests.PaulTrapHoles;

namespace Einzel.Library.Tests;

/// <summary>
/// The ejection edge of a quadrupole trap as a property of an ensemble of ions rather than of
/// one: the loss fraction against amplitude, where it crosses a half, and how sure that is.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why not one ion.</b> In effective q the linear stability boundary is the same for every
/// geometry by construction, so what differs between them is nonlinear: an ion has to cross the
/// anharmonic region to reach an endcap, and near the edge that motion is slow to grow and
/// sensitive to the last digits of the arithmetic. One ion's fate there is a coin - the bisected
/// edge of a holed trap moved two parts in a thousand of q with the integrator's tolerance and
/// with a micrometre of launch - and a coin cannot order two geometries 0.003 apart.
/// </para>
/// <para>
/// <b>The ensemble.</b> Ions released from rest, positions uniform in a ball of radius
/// <see cref="BallRadiusMm"/> about the trap centre, each at its own RF phase, uniform over a
/// cycle. The ball's rms is 0.058 mm an axis, against 0.060 mm for a 300 K cloud of m/z 500 in
/// this trap's well at q = 0.5 (sqrt(kT/m) over the secular angular frequency), so it is the
/// size of a cooled cloud; it is a stated ball and not a thermal distribution, and it carries
/// no velocity spread. Drawn by rejection from a cube with a seeded generator, so a cloud is
/// arithmetic on that generator's output and involves no library function that differs between
/// platforms. The field is axisymmetric and the ions start at rest, so each stays in its own
/// meridional plane and its azimuth takes no part: an ion is flown at its radius.
/// </para>
/// <para>
/// <b>The same ions at every amplitude and in every geometry</b> (common random numbers), so a
/// difference between two loss curves is a difference between the traps and not between two
/// draws.
/// </para>
/// </remarks>
internal static class PaulTrapEdgeEnsemble
{
    /// <summary>The radius of the ball the ions are released in, in mm.</summary>
    public const double BallRadiusMm = 0.13;

    /// <summary>One ion's release: where, and at what RF phase.</summary>
    public readonly record struct Launch(double AxialMm, double RadialMm, double PhaseCycles);

    /// <summary>A seeded cloud of launches.</summary>
    public static Launch[] Cloud(int count, int seed, double radiusMm = BallRadiusMm)
    {
        var random = new Random(seed);
        var cloud = new Launch[count];

        for (var k = 0; k < count;)
        {
            var x = (2.0 * random.NextDouble()) - 1.0;
            var y = (2.0 * random.NextDouble()) - 1.0;
            var z = (2.0 * random.NextDouble()) - 1.0;
            var phase = random.NextDouble();

            if ((x * x) + (y * y) + (z * z) > 1.0)
            {
                continue;
            }

            cloud[k++] = new Launch(
                radiusMm * x, radiusMm * Math.Sqrt((y * y) + (z * z)), phase);
        }

        return cloud;
    }

    /// <summary>
    /// Which ions are lost at each effective q: <c>lost[point][ion]</c>.
    /// </summary>
    /// <remarks>
    /// Flights are independent and the solved field is only read, so they run across cores;
    /// nothing is summed across flights, so the result does not depend on how many.
    /// </remarks>
    public static bool[][] Losses(
        Solved trap, Launch[] cloud, double[] qEff, double cycles, double relativeTolerance = 1e-9)
    {
        var lost = new bool[qEff.Length][];

        for (var p = 0; p < qEff.Length; p++)
        {
            lost[p] = new bool[cloud.Length];
        }

        Parallel.For(0, qEff.Length * cloud.Length, index =>
        {
            var p = index / cloud.Length;
            var k = index % cloud.Length;
            var launch = cloud[k];

            lost[p][k] = !trap.Fly(
                qEff[p] / trap.QPerVolt, launch.AxialMm, launch.RadialMm, cycles, Loss.TrappingRegion,
                relativeTolerance: relativeTolerance, phaseCycles: launch.PhaseCycles).Held;
        });

        return lost;
    }

    /// <summary>The loss fraction at each point, over the ions named (all of them by default).</summary>
    public static double[] Fractions(bool[][] lost, int[]? ions = null)
    {
        var fractions = new double[lost.Length];

        for (var p = 0; p < lost.Length; p++)
        {
            var count = 0;

            if (ions is null)
            {
                foreach (var l in lost[p])
                {
                    count += l ? 1 : 0;
                }

                fractions[p] = (double)count / lost[p].Length;
            }
            else
            {
                foreach (var k in ions)
                {
                    count += lost[p][k] ? 1 : 0;
                }

                fractions[p] = (double)count / ions.Length;
            }
        }

        return fractions;
    }

    /// <summary>
    /// Where a loss curve crosses a level: the curve is first made non-decreasing (pool
    /// adjacent violators, the least-squares monotone fit), then interpolated linearly.
    /// </summary>
    /// <remarks>
    /// A loss curve near the edge of a holed trap need not be monotone point by point - that is
    /// the strip - and "the first point above a half" would again be decided by one point. The
    /// monotone fit uses every point. NaN if the curve does not bracket the level, which the
    /// callers assert against rather than extrapolate.
    /// </remarks>
    public static double Crossing(double[] q, double[] fraction, double level)
    {
        var fit = Monotone(fraction);

        if (fit[0] >= level || fit[^1] < level)
        {
            return double.NaN;
        }

        for (var p = 1; p < fit.Length; p++)
        {
            if (fit[p] >= level)
            {
                return q[p - 1] + ((q[p] - q[p - 1]) * (level - fit[p - 1]) / (fit[p] - fit[p - 1]));
            }
        }

        return double.NaN;
    }

    /// <summary>The non-decreasing least-squares fit to a sequence of equally weighted values.</summary>
    public static double[] Monotone(double[] values)
    {
        var sums = new List<double>();
        var counts = new List<int>();

        foreach (var value in values)
        {
            sums.Add(value);
            counts.Add(1);

            while (sums.Count > 1 && sums[^2] / counts[^2] > sums[^1] / counts[^1])
            {
                sums[^2] += sums[^1];
                counts[^2] += counts[^1];
                sums.RemoveAt(sums.Count - 1);
                counts.RemoveAt(counts.Count - 1);
            }
        }

        var fit = new double[values.Length];
        var at = 0;

        for (var b = 0; b < sums.Count; b++)
        {
            for (var k = 0; k < counts[b]; k++)
            {
                fit[at++] = sums[b] / counts[b];
            }
        }

        return fit;
    }

    /// <summary>An ensemble edge: the 50 percent crossing, its interval, and the 10 and 90 percent crossings.</summary>
    /// <param name="Q50">Effective q at which half the ensemble is lost within the hold.</param>
    /// <param name="Low">Lower end of the 95 percent bootstrap interval on <paramref name="Q50"/>.</param>
    /// <param name="High">Upper end of it.</param>
    /// <param name="Q10">Effective q at which a tenth is lost.</param>
    /// <param name="Q90">Effective q at which nine tenths are lost.</param>
    public readonly record struct Edge(double Q50, double Low, double High, double Q10, double Q90)
    {
        /// <summary>The 10 to 90 percent width of the loss curve.</summary>
        public double Width => Q90 - Q10;
    }

    /// <summary>
    /// The edge of a measured loss table, with a percentile bootstrap over ions.
    /// </summary>
    /// <remarks>
    /// Ions are resampled with replacement and the whole curve re-read for each resample, so
    /// the interval is the sampling error of this cloud size: what another draw of as many
    /// ions would give. It says nothing about the integrator, the mesh or the hold, which are
    /// measured as controls. An interval cannot be read finer than the q grid resolves a
    /// curve, so the grid step is stated beside every edge.
    /// </remarks>
    public static Edge EdgeOf(double[] q, bool[][] lost, int resamples = 1000, int seed = 20261115)
    {
        var all = Fractions(lost);
        var random = new Random(seed);
        var ions = new int[lost[0].Length];
        var crossings = new List<double>(resamples);

        for (var r = 0; r < resamples; r++)
        {
            for (var k = 0; k < ions.Length; k++)
            {
                ions[k] = random.Next(ions.Length);
            }

            var crossing = Crossing(q, Fractions(lost, ions), 0.5);

            if (!double.IsNaN(crossing))
            {
                crossings.Add(crossing);
            }
        }

        Assert.True(crossings.Count == resamples, "a resampled loss curve did not bracket a half: widen the q range");

        crossings.Sort();

        return new Edge(
            Crossing(q, all, 0.5),
            crossings[(int)(0.025 * resamples)],
            crossings[(int)(0.975 * resamples) - 1],
            Crossing(q, all, 0.1),
            Crossing(q, all, 0.9));
    }

    /// <summary>An evenly spaced grid including both ends, each point computed and not accumulated.</summary>
    public static double[] Grid(double from, double to, int points) =>
        Enumerable.Range(0, points).Select(k => from + ((to - from) * k / (points - 1))).ToArray();
}
