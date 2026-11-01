namespace Einzel.Commands;

/// <summary>The folding, period, trigger, edge and noise numerics behind <see cref="WaveformImportCommand"/>.</summary>
internal static class WaveformImportAnalysis
{
    /// <summary>
    /// Bin sums and counts of samples <paramref name="from"/> to <paramref name="to"/>
    /// (exclusive), each at the phase <c>frac((t - origin) / period)</c> on bin
    /// <c>floor(phase · bins)</c>.
    /// </summary>
    public static (double[] Sum, int[] Count) Fold(
        IReadOnlyList<double> t, IReadOnlyList<double> y, int from, int to, double origin, double period, int bins)
    {
        var sum = new double[bins];
        var count = new int[bins];

        for (var i = from; i < to; i++)
        {
            var k = Bin(t[i], origin, period, bins);
            sum[k] += y[i];
            count[k]++;
        }

        return (sum, count);
    }

    /// <summary>The bin of <paramref name="time"/> in a fold of <paramref name="bins"/> bins from <paramref name="origin"/>.</summary>
    public static int Bin(double time, double origin, double period, int bins)
    {
        var p = (time - origin) / period;
        return Math.Min((int)((p - Math.Floor(p)) * bins), bins - 1);
    }

    /// <summary>Bin means, an empty bin interpolated around the cycle between its non-empty neighbours.</summary>
    public static double[] Means(double[] sum, int[] count)
    {
        var n = sum.Length;
        var mean = new double[n];
        var known = Enumerable.Range(0, n).Where(k => count[k] > 0).ToArray();

        for (var j = 0; j < known.Length; j++)
        {
            var a = known[j];
            var b = known[(j + 1) % known.Length];
            var gap = ((b - a + n - 1) % n) + 1;
            var ya = sum[a] / count[a];
            var yb = sum[b] / count[b];

            for (var g = 0; g < gap; g++)
            {
                mean[(a + g) % n] = ya + ((yb - ya) * g / gap);
            }
        }

        return mean;
    }

    /// <summary>
    /// The period near <paramref name="nominal"/> at which the fold is tightest: the pooled
    /// variance of the samples about their bin mean, scanned at 201 periods over
    /// <c>nominal (1 ± 2 tolerance)</c>, then golden section to 1e-7 of the nominal. A scan
    /// folds only as many periods as its step resolves (a quarter cycle of drift over them);
    /// each further scan, a hundred times finer about the last best, folds a hundred times
    /// more, so a long record's tight fold, narrower than the first step, is not stepped over.
    /// </summary>
    public static double Refine(IReadOnlyList<double> t, IReadOnlyList<double> y, double nominal, double tolerance, int bins)
    {
        var centre = y.Average();
        var centred = y.Select(v => v - centre).ToArray();
        double Spread(double period, int count) => Enumerable.Range(0, 4).Average(j => Shifted(period, count, j / 4.0));

        double Shifted(double period, int count, double shift)
        {
            // Each sample shared between the two nearest bin centres by its distance to them, so
            // the spread changes smoothly with the period, not only when a sample changes bin.
            var sum = new double[bins];
            var weight = new double[bins];

            for (var i = 0; i < count; i++)
            {
                var p = (t[i] - t[0]) / period;
                var x = ((p - Math.Floor(p)) * bins) - 0.5 + shift;
                var k = (int)Math.Floor(x);
                var f = x - k;
                var (a, b) = ((k + bins) % bins, (k + 1) % bins);
                (sum[a], weight[a]) = (sum[a] + ((1.0 - f) * centred[i]), weight[a] + 1.0 - f);
                (sum[b], weight[b]) = (sum[b] + (f * centred[i]), weight[b] + f);
            }

            return -Enumerable.Range(0, bins).Where(k => weight[k] > 0.0).Sum(k => sum[k] * sum[k] / weight[k]);
        }

        var step = 4.0 * tolerance * nominal / 200.0;
        var best = nominal;
        var count = 0;

        while (count < t.Count)
        {
            var span = nominal * nominal / (4.0 * step);
            count = Enumerable.Range(0, t.Count).FirstOrDefault(i => t[i] - t[0] > span, t.Count);
            var (middle, used) = (best, count);

            // From the centre outwards, so of equal spreads (a flat trace) the nearest the middle wins.
            best = Enumerable.Range(0, 201)
                .OrderBy(i => Math.Abs(i - 100))
                .Select(i => middle + ((i - 100) * step))
                .MinBy(p => Spread(p, used));
            step = count < t.Count ? step / 100.0 : step;
        }
        var lo = best - step;
        var hi = best + step;
        var ratio = (Math.Sqrt(5.0) - 1.0) / 2.0;
        var c = hi - (ratio * (hi - lo));
        var d = lo + (ratio * (hi - lo));
        var fc = Spread(c, t.Count);
        var fd = Spread(d, t.Count);

        while (hi - lo > 1e-7 * nominal)
        {
            if (fc <= fd)
            {
                (hi, d, fd) = (d, c, fc);
                c = hi - (ratio * (hi - lo));
                fc = Spread(c, t.Count);
            }
            else
            {
                (lo, c, fc) = (c, d, fd);
                d = lo + (ratio * (hi - lo));
                fd = Spread(d, t.Count);
            }
        }

        return (lo + hi) / 2.0;
    }

    /// <summary>
    /// The first crossing of <paramref name="level"/> in the direction <paramref name="sense"/>
    /// (+1 up, -1 down) in phase order from phase 0, once the fold has been beyond
    /// <paramref name="hysteresis"/> on the near side and before it is beyond it on the far
    /// side; the phase in [0, 1), interpolated between bin centres, or null.
    /// </summary>
    public static double? Crossing(double[] fold, double level, double hysteresis, int sense)
    {
        var n = fold.Length;
        var armed = false;
        double? found = null;

        for (var j = 1; j <= 2 * n; j++)
        {
            var before = sense * (fold[(j - 1) % n] - level);
            var now = sense * (fold[j % n] - level);

            if (now < -hysteresis)
            {
                (armed, found) = (true, null);
            }
            else if (armed && found is null && before < 0.0 && now >= 0.0)
            {
                found = (j - 0.5 + (before / (before - now))) / n;
            }

            if (found is { } phase && now > hysteresis)
            {
                return phase - Math.Floor(phase);
            }
        }

        return null;
    }

    /// <summary>
    /// The transitions between the 10 % and 90 % levels (midline ∓ 0.8 h) narrower than
    /// <paramref name="width"/> cycles, as (rising, leave, enter, middle) in bin coordinates
    /// unwrapped from the fold's minimum: the fold leaves one level at <c>leave</c>, reaches
    /// the other at <c>enter</c> without returning, and crosses the midline at <c>middle</c>.
    /// </summary>
    public static List<(bool Rising, double Leave, double Enter, double Middle)> Edges(double[] fold, double width)
    {
        var n = fold.Length;
        var mid = (fold.Max() + fold.Min()) / 2.0;
        var band = 0.8 * (fold.Max() - fold.Min()) / 2.0;
        var start = Array.IndexOf(fold, fold.Min());
        var edges = new List<(bool, double, double, double)>();
        var high = false;
        var leave = (double)start;

        for (var u = start + 1; u <= start + n; u++)
        {
            var near = high ? mid + band : mid - band;
            var far = high ? mid - band : mid + band;
            var sign = high ? -1.0 : 1.0;

            if (sign * (At(fold, u - 1) - near) <= 0.0 && sign * (At(fold, u) - near) > 0.0)
            {
                leave = Cross(fold, u, near);
            }

            if (sign * (At(fold, u) - far) >= 0.0)
            {
                var enter = Cross(fold, u, far);
                var middle = Enumerable.Range((int)Math.Floor(leave) + 1, u - (int)Math.Floor(leave))
                    .Where(k => sign * (At(fold, k - 1) - mid) < 0.0 && sign * (At(fold, k) - mid) >= 0.0)
                    .Select(k => Cross(fold, k, mid)).DefaultIfEmpty(enter).First();

                if (enter - leave < width * n)
                {
                    edges.Add((!high, leave, enter, middle));
                }

                high = !high;
            }
        }

        return edges;
    }

    /// <summary>
    /// The samples just outside an edge's corners, for a walk that ended at
    /// <paramref name="first"/> and <paramref name="last"/> (sample indices, unwrapped) from the
    /// 10/90 crossings at <paramref name="from"/> and <paramref name="to"/> (in the same index
    /// coordinates): the least-squares line through the samples between the crossings meets
    /// the median of the plateau samples beyond each walk end. The walk's ends stand when fewer
    /// than four samples lie between the crossings; a walk end stands when fewer than three
    /// plateau samples lie beyond it, or the line puts its corner more than three samples away.
    /// </summary>
    public static (int First, int Last) Corners(double[] values, int first, int last, double from, double to)
    {
        var (lo, hi) = ((int)Math.Ceiling(from), (int)Math.Floor(to));

        if (hi - lo + 1 < 4)
        {
            return (first, last);
        }

        var inside = Enumerable.Range(lo, hi - lo + 1).ToArray();
        var (mk, mv) = (inside.Average(), inside.Average(k => At(values, k)));
        var slope = inside.Sum(k => (k - mk) * (At(values, k) - mv)) / inside.Sum(k => (k - mk) * (k - mk));
        var (mid, band) = ((values.Max() + values.Min()) / 2.0, 0.8 * (values.Max() - values.Min()) / 2.0);

        // The plateau beyond a walk end: up to eight samples, while they stay beyond its 10/90
        // level, so a short plateau does not reach into the next edge; fewer than three keep
        // the walk's end.
        double? Plateau(int end, int outward)
        {
            var side = Math.Sign(slope) * outward;
            var run = Enumerable.Range(1, 8).Select(j => At(values, end + (outward * j)))
                .TakeWhile(v => side * (v - (mid + (side * band))) >= 0.0).Order().ToArray();
            return run.Length < 3 ? null : (run[(run.Length - 1) / 2] + run[run.Length / 2]) / 2.0;
        }

        int Corner(int end, int outward)
        {
            var corner = Plateau(end, outward) is { } level ? mk + ((level - mv) / slope) : double.NaN;
            var k = outward < 0 ? Math.Floor(corner) : Math.Ceiling(corner);
            return double.IsFinite(k) && Math.Abs(k - end) <= 3 ? (int)k : end;
        }

        return (Corner(first, -1), Corner(last, 1));
    }

    /// <summary>The fraction of the cycle above the fold's midline, crossings interpolated between bin centres.</summary>
    public static double Duty(double[] fold)
    {
        var mid = (fold.Max() + fold.Min()) / 2.0;
        var above = 0.0;

        for (var k = 0; k < fold.Length; k++)
        {
            var a = fold[k] - mid;
            var b = At(fold, k + 1) - mid;
            above += (a > 0.0, b > 0.0) switch
            {
                (true, true) => 1.0,
                (false, false) => 0.0,
                (true, false) => a / (a - b),
                _ => b / (b - a),
            };
        }

        return above / fold.Length;
    }

    /// <summary>
    /// Each output bin's least-squares line in phase through its samples (about the bin
    /// <paramref name="mean"/>): the line's value at the bin centre, the mean itself where the
    /// bin holds fewer than three samples; and the noise, the median over the bins of the rms
    /// deviation of a bin's samples about their line, so a slope across the bin is not counted
    /// as noise. A bin's samples are not symmetric about its centre (their phases drift across
    /// the fold), so its mean carries a sample-lattice jitter that the line at the centre does not.
    /// </summary>
    public static (double[] Centre, double Noise) Lines(
        IReadOnlyList<double> t, IReadOnlyList<double> y, int from, int to, double origin, double period, double[] mean)
    {
        var n = mean.Length;
        var s = new double[n, 5];

        for (var i = from; i < to; i++)
        {
            var p = (t[i] - origin) / period;
            var k = Bin(t[i], origin, period, n);
            var x = ((p - Math.Floor(p)) * n) - k - 0.5;
            var dy = y[i] - mean[k];
            (s[k, 0], s[k, 1], s[k, 2], s[k, 3], s[k, 4]) = (s[k, 0] + 1, s[k, 1] + x, s[k, 2] + (x * x), s[k, 3] + (x * dy), s[k, 4] + (dy * dy));
        }

        var centre = (double[])mean.Clone();
        var rms = new List<double>();

        foreach (var k in Enumerable.Range(0, n).Where(k => s[k, 0] > 2))
        {
            // The deviations about the mean sum to zero, so the slope is s3 / xx and the line at
            // the centre (x = 0) is the mean less the slope times the samples' mean phase offset.
            var c = s[k, 0];
            var xx = s[k, 2] - (s[k, 1] * s[k, 1] / c);
            var slope = xx > 0.0 ? s[k, 3] / xx : 0.0;
            centre[k] = mean[k] - (slope * s[k, 1] / c);
            rms.Add(Math.Sqrt(Math.Max(s[k, 4] - (slope * s[k, 3]), 0.0) / (c - 2.0)));
        }

        rms.Sort();
        return (centre, rms.Count == 0 ? 0.0 : rms[rms.Count / 2]);
    }

    private static double At(double[] fold, int u) => fold[((u % fold.Length) + fold.Length) % fold.Length];

    /// <summary>Where the fold crosses <paramref name="level"/> between bins <c>u - 1</c> and <c>u</c>.</summary>
    private static double Cross(double[] fold, int u, double level)
    {
        var a = At(fold, u - 1);
        var b = At(fold, u);
        return b == a ? u : u - 1 + ((level - a) / (b - a));
    }
}
