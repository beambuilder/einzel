namespace Einzel.Fields;

/// <summary>A field's own phase arithmetic, in cycles, as it evaluates its waveform.</summary>
internal interface IPhaseClock
{
    /// <summary>The phase the field hands its waveform at an instant, computed bit for bit as the field does.</summary>
    double PhaseAt(double timeSeconds);
}

/// <summary>A solved drive: <c>(f * t) + phase</c>, as <c>DrivenSolvedField</c> weights a term.</summary>
internal readonly record struct DriveClock(double FrequencyHz, double PhaseCycles) : IPhaseClock
{
    public double PhaseAt(double timeSeconds) => (FrequencyHz * timeSeconds) + PhaseCycles;
}

/// <summary>An angular drive: <c>omega * t / (2 pi)</c>, as <c>IdealQuadrupoleRf.DriveAt</c> computes it.</summary>
internal readonly record struct AngularClock(double AngularFrequency) : IPhaseClock
{
    public double PhaseAt(double timeSeconds) => AngularFrequency * timeSeconds / (2.0 * Math.PI);
}

/// <summary>A uniform drive: <c>t * f</c>, as <c>OscillatingUniformField</c> computes it.</summary>
internal readonly record struct TimesFrequencyClock(double FrequencyHz) : IPhaseClock
{
    public double PhaseAt(double timeSeconds) => timeSeconds * FrequencyHz;
}

/// <summary>
/// The exact instants at which a field's evaluation crosses a declared waveform breakpoint.
/// </summary>
/// <remarks>
/// <para>
/// The nominal instant <c>(n + b - phase) / f</c> is not where the field flips: the field
/// computes its phase in doubles, and the phase it computes at the nominal instant can sit
/// one or two ulps short of <c>n + b</c>, so a step that ends there still sees the old level.
/// This finds the first double at which the field's own phase arithmetic is at or past the
/// breakpoint, so the instant one ulp before it is pre-edge and the instant itself post-edge.
/// </para>
/// <para>
/// The search starts at the nominal instant and widens in ulps, doubling, until it brackets
/// the flip, then bisects on the ordered bit patterns. Near the nominal instant that is two
/// or three evaluations. It is not a fixed walk of a few ulps because one is not always
/// enough: when <c>f t</c> is small against the phase the phase changes once per many ulps
/// of <c>t</c>, and a bounded walk would stop short. The predicate is monotone in <c>t</c>
/// because every rounding step is, and generic over a struct so nothing is allocated.
/// </para>
/// </remarks>
internal static class EdgeInstants
{
    /// <summary>The first instant after <paramref name="afterSeconds"/> where the clock's phase reaches cycle <c>n</c> plus <paramref name="breakpoint"/>, for the first such <c>n</c>.</summary>
    /// <param name="clock">The field's phase arithmetic.</param>
    /// <param name="frequencyHz">The frequency, for the nominal instant only.</param>
    /// <param name="phaseCycles">The phase offset, for the nominal instant only.</param>
    /// <param name="breakpoint">The breakpoint, in [0, 1).</param>
    /// <param name="afterSeconds">The instant to look forward from.</param>
    /// <returns>The instant, strictly after <paramref name="afterSeconds"/>; infinity if none is finite.</returns>
    public static double Next<TClock>(in TClock clock, double frequencyHz, double phaseCycles, double breakpoint, double afterSeconds)
        where TClock : struct, IPhaseClock
    {
        // The crossing of cycle n is behind iff phase >= n + b exactly, so the first one
        // ahead is cycle floor(phase - b) + 1. Rounding `phase - b` up to an integer can
        // only raise the floor by one (rounding is monotone and integers are doubles), so
        // the floor computed here is that cycle or the one before, and one test says which.
        var n = Math.Floor(clock.PhaseAt(afterSeconds) - breakpoint);
        if (new Crossing<TClock>(clock, n, breakpoint).IsPast(afterSeconds))
        {
            n++;
        }

        // Not yet past at `afterSeconds`, so the first instant past it lies after it.
        return FirstPast(new Crossing<TClock>(clock, n, breakpoint), (n + breakpoint - phaseCycles) / frequencyHz);
    }

    /// <summary>The smallest double at which a monotone test holds, searched from a nominal instant.</summary>
    /// <param name="test">The test; false then true as the instant grows.</param>
    /// <param name="nominal">Where to start looking.</param>
    /// <returns>The instant.</returns>
    public static double FirstPast<TTest>(in TTest test, double nominal)
        where TTest : struct, IInstantTest
    {
        if (!double.IsFinite(nominal))
        {
            return nominal;
        }

        // Keys order the doubles as integers, so `key + 1` is the next double up.
        var start = Key(nominal);
        long below, above;

        if (test.IsPast(nominal))
        {
            (above, below) = Widen(test, start, up: false);
        }
        else
        {
            (below, above) = Widen(test, start, up: true);
        }

        // The bracket is one widening step wide at most, so its width fits in a long.
        while (above - below > 1)
        {
            var middle = below + ((above - below) / 2);

            if (test.IsPast(FromKey(middle)))
            {
                above = middle;
            }
            else
            {
                below = middle;
            }
        }

        return FromKey(above);
    }

    /// <summary>
    /// From a key on one side of the crossing, the last key found on that side and the
    /// first on the other, moving away from it by doubling steps.
    /// </summary>
    private static (long Same, long Other) Widen<TTest>(in TTest test, long start, bool up)
        where TTest : struct, IInstantTest
    {
        // Each step starts from the last key still on the near side and moves at least
        // one, and the distances are compared unsigned because they can exceed a long.
        // Infinity is past every finite crossing and minus infinity before it, so the
        // widening ends by the time it reaches either: at most about 64 doublings.
        var same = start;
        long step = 1;
        while (true)
        {
            var key = up
                ? ((ulong)step < (ulong)(MaximumKey - same) ? same + step : MaximumKey)
                : ((ulong)step < (ulong)(same - MinimumKey) ? same - step : MinimumKey);

            if (key == MaximumKey || key == MinimumKey || test.IsPast(FromKey(key)) == up)
            {
                return (same, key);
            }

            same = key;
            step = step <= long.MaxValue / 2 ? step * 2 : long.MaxValue;
        }
    }

    private static readonly long MaximumKey = Key(double.PositiveInfinity);
    private static readonly long MinimumKey = Key(double.NegativeInfinity);

    private static long Key(double value)
    {
        var bits = BitConverter.DoubleToInt64Bits(value);
        return bits >= 0 ? bits : long.MinValue - bits;
    }

    private static double FromKey(long key) =>
        BitConverter.Int64BitsToDouble(key >= 0 ? key : long.MinValue - key);

    /// <summary>The phase is at or past <c>n + b</c>, judged as the waveform judges it.</summary>
    private readonly record struct Crossing<TClock>(TClock Clock, double Cycle, double Breakpoint) : IInstantTest
        where TClock : struct, IPhaseClock
    {
        public bool IsPast(double timeSeconds)
        {
            // The waveform wraps as `phase - Floor(phase)`, which is exact, so this is
            // `phase >= n + b` in exact arithmetic: the comparison the waveform makes.
            var phase = Clock.PhaseAt(timeSeconds);
            var cycle = Math.Floor(phase);
            return cycle > Cycle || (cycle == Cycle && phase - cycle >= Breakpoint);
        }
    }
}

/// <summary>A test on an instant that is false before some double and true from it on.</summary>
internal interface IInstantTest
{
    /// <summary>Whether the instant is at or past the crossing.</summary>
    bool IsPast(double timeSeconds);
}
