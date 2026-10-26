namespace Einzel.Fields.Solved;

/// <summary>One electrode's connection to one generator.</summary>
/// <param name="Drive">Which declared drive, by index.</param>
/// <param name="Amplitude">Its share of that drive, zero to peak, in volts.</param>
/// <param name="Phase">Where in that drive's cycle it sits, as a fraction of one.</param>
public readonly record struct DriveTap(int Drive, double Amplitude, double Phase);

/// <summary>What one electrode is asked to hold, over time.</summary>
/// <param name="Name">
/// The electrode's name, which is its identity in a decomposition: no two excitations handed
/// to one may share it.
/// </param>
/// <param name="Direct">Its DC potential, in volts.</param>
/// <param name="Taps">
/// Every generator it is connected to. Usually one, and empty for an electrode held
/// at DC — but a ring in a travelling-wave guide carries a fast confining RF
/// <em>and</em> a slow travelling wave, and a trap endcap carries a supplementary
/// excitation while the ring carries the main drive.
/// </param>
public readonly record struct Excitation(
    string Name, double Direct, IReadOnlyList<DriveTap> Taps)
{
    /// <summary>An electrode tapping a single drive, the common case.</summary>
    /// <param name="name">The electrode's name.</param>
    /// <param name="direct">Its DC potential, in volts.</param>
    /// <param name="amplitude">Its share of the drive.</param>
    /// <param name="phase">Where in the cycle it sits.</param>
    public Excitation(string name, double direct, double amplitude, double phase)
        : this(name, direct, amplitude == 0.0 ? [] : [new DriveTap(0, amplitude, phase)])
    {
    }
}

/// <summary>One term in a channel's time-varying weight.</summary>
/// <param name="Drive">Which drive's clock the phase is measured on.</param>
/// <param name="Amplitude">The weight's amplitude, in volts.</param>
/// <param name="Phase">Its phase within that drive's cycle.</param>
public readonly record struct WeightTerm(int Drive, double Amplitude, double Phase);

/// <summary>One solved basis, and the time-varying weight it is multiplied by.</summary>
/// <param name="Pattern">
/// Electrode name to the relative potential it holds in this solve, normalised so
/// the leading non-zero entry is one.
/// </param>
/// <param name="Direct">The constant part of the weight, in volts.</param>
/// <param name="Harmonics">Amplitude and phase of each drive term in the weight.</param>
public sealed record DriveChannel(
    Dictionary<string, double> Pattern,
    double Direct,
    List<WeightTerm> Harmonics);

/// <summary>
/// Reduces a driven geometry to the fewest solves that can express it.
/// </summary>
/// <remarks>
/// <para>
/// Two steps, and the order matters. First every electrode's potential is split
/// into the supplies feeding it: a constant one, and one per distinct (drive,
/// phase) pair. A funnel's two hundred rings reach three supplies - a DC chain and
/// two RF phases - however many distinct voltages the chain holds, because what
/// makes a supply one supply is that every electrode on it moves <em>together</em>,
/// not that they move to the same place.
/// </para>
/// <para>
/// Then supplies are grouped by their <em>spatial pattern</em> rather than by their
/// time dependence. A quadrupole run with DC has two supplies - a steady one and an
/// oscillating one - but both put the x pair up and the y pair down by the same
/// relative amounts, so they are the same solved field with two weights, and the
/// whole filter is one solve.
/// </para>
/// <para>
/// <b>A sinusoid is a special case, and it is the one that makes a travelling wave
/// affordable.</b> A cos(2 pi (f t - phi)) is exactly
/// A cos(2 pi phi) cos(2 pi f t) + A sin(2 pi phi) sin(2 pi f t) — a fixed pair of
/// time functions with constant coefficients. So however many distinct phases a
/// structure carries, a sinusoidal drive reaches exactly <em>two</em> supplies. A
/// sixty-ring travelling-wave guide with sixty distinct phases is two solves, not
/// sixty.
/// </para>
/// <para>
/// It holds only for a sinusoid. A rectangular wave shifted by a quarter cycle is
/// not a combination of the unshifted wave and one shifted by a quarter, so there
/// each distinct phase really is its own supply and the solve count says so. That
/// is why the quadrature flag is <em>per drive</em>: an instrument may run a
/// sinusoidal confinement and a switched excitation at once, and each collapses or
/// does not on its own terms.
/// </para>
/// <para>
/// <b>Several drives cost no more than one where the geometry allows.</b> Two
/// generators reaching the same electrodes in the same proportions are one solved
/// pattern carrying two weights on two clocks, exactly as a DC supply and an RF
/// supply already were. What multiplies the solve count is a different <em>spatial
/// pattern</em>, never a different frequency.
/// </para>
/// <para>
/// Written over names and numbers rather than over an electrode type, so the same
/// decomposition serves two dimensions and three. Nothing about it is dimensional:
/// what a channel is depends on how electrodes are wired, not on where they are.
/// </para>
/// </remarks>
public static class DriveChannels
{
    /// <summary>Groups excitations into the channels a solve needs.</summary>
    /// <param name="excitations">What each electrode holds, in declaration order.</param>
    /// <param name="quadrature">
    /// Per drive, whether it is a sinusoid — in which case every phase on it
    /// resolves into two fixed components rather than into a supply of its own. A
    /// drive with no entry is treated as not sinusoidal, which costs solves and
    /// never changes the field.
    /// </param>
    /// <returns>The channels, each with a pattern and a weight.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="excitations"/> is null.</exception>
    public static List<DriveChannel> Decompose(
        IReadOnlyList<Excitation> excitations, IReadOnlyList<bool> quadrature) =>
        Decompose(excitations, quadrature, null);

    /// <summary>
    /// Groups excitations into channels, with the DC of the named electrodes split off the one
    /// constant supply into a supply per group.
    /// </summary>
    /// <param name="excitations">What each electrode holds, in declaration order.</param>
    /// <param name="quadrature">Per drive, whether it is a sinusoid.</param>
    /// <param name="directGroups">
    /// Electrode name to DC group, or null for none: every electrode's DC then feeds the one
    /// constant supply, as it always has. An electrode in group <c>g</c> feeds a supply of its
    /// own group instead, so its DC is a channel whose pattern holds the group alone, at one
    /// volt when the group's electrodes hold equal potentials, and whose weight is that
    /// potential. Electrodes in no group keep the constant supply between them.
    /// </param>
    /// <returns>The channels, each with a pattern and a weight.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="excitations"/> is null.</exception>
    /// <remarks>
    /// What a study's field basis is made of: a group whose potential changes from one point to
    /// the next keeps its pattern and changes only its weight, so the points share their
    /// solves. Exact like everything else here: a group's pattern is the same at two points
    /// exactly when its electrodes' potentials are in the same ratio there.
    /// </remarks>
    public static List<DriveChannel> Decompose(
        IReadOnlyList<Excitation> excitations,
        IReadOnlyList<bool> quadrature,
        IReadOnlyDictionary<string, int>? directGroups)
    {
        ArgumentNullException.ThrowIfNull(excitations);
        ArgumentNullException.ThrowIfNull(quadrature);

        // A supply's coefficients are keyed by name, so a name has to be an identity here: two
        // electrodes sharing one would write the same entry and the later would win, putting
        // both at its potential. That is what happened to every pair of unnamed electrodes,
        // all called "electrode", in every driven or sequenced solve. The validator now names
        // them apart and refuses a repeat; this is the same rule at the seam, so a caller that
        // did not come through the validator is stopped rather than given a quieter field.
        var named = new HashSet<string>(StringComparer.Ordinal);

        foreach (var excitation in excitations)
        {
            if (!named.Add(excitation.Name))
            {
                throw new ArgumentException(
                    $"two excitations are both called '{excitation.Name}'. A channel is a map "
                    + "from electrode name to potential, so the second would overwrite the first "
                    + "and both would be solved holding its potential. Name every electrode in a "
                    + "solve distinctly.",
                    nameof(excitations));
            }
        }

        var supplies = new List<Supply>();

        Supply Reach(double direct, int drive, double amplitude, double phase, int group = -1)
        {
            var existing = supplies.FirstOrDefault(t =>
                t.Direct == direct && t.Drive == drive
                && t.Amplitude == amplitude && t.Phase == phase && t.Group == group);

            if (existing is null)
            {
                existing = new Supply([], direct, drive, amplitude, phase, group);
                supplies.Add(existing);
            }

            return existing;
        }

        // Added rather than assigned, because one electrode can reach one supply more than
        // once: two taps on the same generator at the same phase, or at two phases of a
        // sinusoid, each resolve into the same supply. Assigned, the later tap replaced the
        // earlier, so 100 V and 50 V on one generator flew exactly as 50 V alone. Where an
        // electrode reaches a supply once, zero plus its volts is its volts to the bit.
        void Feed(Supply supply, string electrode, double volts) =>
            supply.Coefficients[electrode] = supply.Coefficients.GetValueOrDefault(electrode) + volts;

        foreach (var excitation in excitations)
        {
            // The constant supply reaches every electrode that holds a DC
            // potential, each at its own volts. One supply, however many voltages.
            // Drive -1 marks it: no clock, so no phase to distinguish it by.
            if (excitation.Direct != 0.0)
            {
                var group = directGroups is not null && directGroups.TryGetValue(excitation.Name, out var g) ? g : -1;
                Feed(Reach(1.0, -1, 0.0, 0.0, group), excitation.Name, excitation.Direct);
            }

            foreach (var tap in excitation.Taps)
            {
                if (tap.Amplitude == 0.0)
                {
                    continue;
                }

                var phase = tap.Phase - Math.Floor(tap.Phase);
                var sinusoidal = tap.Drive >= 0 && tap.Drive < quadrature.Count
                    && quadrature[tap.Drive];

                if (!sinusoidal)
                {
                    Feed(Reach(0.0, tap.Drive, 1.0, phase), excitation.Name, tap.Amplitude);
                    continue;
                }

                // CosPi and SinPi rather than Cos and Sin of a scaled argument,
                // because they are exact at the quarter turns. Math.Sin(Math.PI) is
                // 1.2e-16, not zero, and an antiphase electrode would otherwise
                // acquire a quadrature component made entirely of round-off - which
                // becomes a spurious third channel carrying a field of nothing.
                var inPhase = tap.Amplitude * double.CosPi(2.0 * phase);
                var outOfPhase = tap.Amplitude * double.SinPi(2.0 * phase);

                if (inPhase != 0.0)
                {
                    Feed(Reach(0.0, tap.Drive, 1.0, 0.0), excitation.Name, inPhase);
                }

                if (outOfPhase != 0.0)
                {
                    // A quarter cycle late: cos(2 pi (f t - 1/4)) is sin(2 pi f t).
                    Feed(Reach(0.0, tap.Drive, 1.0, 0.25), excitation.Name, outOfPhase);
                }
            }
        }

        var channels = new List<DriveChannel>();

        foreach (var supply in supplies)
        {
            var (pattern, scale) = Normalise(supply.Coefficients, excitations);

            // Exact comparison. Two supplies share a solve when their applied
            // potentials are exactly proportional, which is what a real instrument
            // produces because the electrodes are the same metal in the same places.
            // A tolerance would merge two shapes that were meant to differ, and the
            // field would be plausible.
            var existing = channels.FirstOrDefault(c => SamePattern(c.Pattern, pattern));

            if (existing is null)
            {
                existing = new DriveChannel(pattern, 0.0, []);
                channels.Add(existing);
            }

            var index = channels.IndexOf(existing);

            channels[index] = supply.Amplitude == 0.0
                ? existing with { Direct = existing.Direct + (scale * supply.Direct) }
                : existing with
                {
                    Harmonics =
                    [
                        .. existing.Harmonics,
                        new WeightTerm(supply.Drive, scale * supply.Amplitude, supply.Phase),
                    ],
                };
        }

        // A geometry whose every electrode is grounded still needs one solve, or
        // there is no field object to return at all.
        if (channels.Count == 0)
        {
            channels.Add(new DriveChannel([], 0.0, []));
        }

        return channels;
    }

    /// <summary>Groups excitations for a geometry with a single drive.</summary>
    /// <param name="excitations">What each electrode holds, in declaration order.</param>
    /// <param name="quadrature">Whether that drive is a sinusoid.</param>
    /// <returns>The channels, each with a pattern and a weight.</returns>
    public static List<DriveChannel> Decompose(
        IReadOnlyList<Excitation> excitations, bool quadrature = false) =>
        Decompose(excitations, [quadrature]);

    /// <summary>
    /// The channels a whole sequence needs: every state decomposed on its own, and a pattern
    /// that recurs across states solved once.
    /// </summary>
    /// <param name="states">What each electrode holds, one list per stage state.</param>
    /// <param name="quadrature">Per drive, whether it is a sinusoid.</param>
    /// <returns>The distinct channels, in the order the states first reach them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="states"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>One state at a time, and that is the whole point.</b> A supply's coefficients are
    /// keyed by electrode name, so handing the concatenation of every state to
    /// <see cref="Decompose(IReadOnlyList{Excitation}, IReadOnlyList{bool})"/> makes the same
    /// electrode appearing in two states collide, and the state written last wins. What comes
    /// out is then one pattern that belongs to no stage: a stage whose own pattern is a
    /// uniform scaling of it still weighs correctly, which is why a sequence that scales its
    /// whole electrode set was right and one that <em>reshapes</em> it was silently
    /// field-free.
    /// </para>
    /// <para>
    /// The sharing the flattened version was reaching for is kept, and is what the union does:
    /// a trap that fills and then extracts holds most of its electrodes at the same potentials
    /// in both, so those patterns appear once and carry a weight each, and only the patterns
    /// that genuinely differ cost a solve.
    /// </para>
    /// </remarks>
    public static List<DriveChannel> DecomposeStates(
        IReadOnlyList<IReadOnlyList<Excitation>> states, IReadOnlyList<bool> quadrature) =>
        DecomposeStates(states, quadrature, null);

    /// <summary>
    /// The channels a whole sequence needs, with the DC of the named electrodes in a supply per
    /// group.
    /// </summary>
    /// <param name="states">What each electrode holds, one list per stage state.</param>
    /// <param name="quadrature">Per drive, whether it is a sinusoid.</param>
    /// <param name="directGroups">Electrode name to DC group, or null for none.</param>
    /// <returns>The distinct channels, in the order the states first reach them.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="states"/> is null.</exception>
    public static List<DriveChannel> DecomposeStates(
        IReadOnlyList<IReadOnlyList<Excitation>> states,
        IReadOnlyList<bool> quadrature,
        IReadOnlyDictionary<string, int>? directGroups)
    {
        ArgumentNullException.ThrowIfNull(states);

        var channels = new List<DriveChannel>();

        foreach (var state in states)
        {
            foreach (var group in Decompose(state, quadrature, directGroups))
            {
                if (!channels.Any(existing => SamePattern(existing.Pattern, group.Pattern)))
                {
                    channels.Add(group);
                }
            }
        }

        return channels;
    }

    /// <summary>The weight each already-solved channel carries for a given set of excitations.</summary>
    /// <param name="channels">The channels the whole sequence was decomposed into.</param>
    /// <param name="excitations">What each electrode holds during this stage.</param>
    /// <param name="quadrature">
    /// Per drive, whether it is a sinusoid. Must match what <c>Decompose</c>
    /// was given, or the patterns this looks up were built a different way and none
    /// of them will be found.
    /// </param>
    /// <returns>The constant and oscillating parts of each channel's weight.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static (List<double> Direct, List<IReadOnlyList<WeightTerm>> Harmonics)
        Weigh(
            List<DriveChannel> channels,
            IReadOnlyList<Excitation> excitations,
            IReadOnlyList<bool> quadrature)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(excitations);

        var direct = new List<double>(new double[channels.Count]);

        var harmonics = new List<IReadOnlyList<WeightTerm>>(
            Enumerable.Range(0, channels.Count).Select(_ => (IReadOnlyList<WeightTerm>)[]));

        foreach (var group in Decompose(excitations, quadrature))
        {
            var index = channels.FindIndex(c => SamePattern(c.Pattern, group.Pattern));

            if (index < 0)
            {
                // This used to `continue`, on the strength of a comment saying it could
                // not happen because the patterns were gathered from every stage. They
                // were gathered from every stage *flattened together*, which is not the
                // same thing, and the case did happen: a stage that reshaped its electrode
                // potentials rather than scaling them found no channel and ran with no
                // field at all. Silence is what made that expensive to find, so a lost
                // supply now says so. After DecomposeStates it cannot happen, and if it
                // ever does again the run stops instead of quietly describing a different
                // instrument.
                throw new InvalidOperationException(
                    "a stage holds a supply pattern that is not among the channels the "
                    + "sequence was decomposed into, so its field would be incomplete. "
                    + "The channels must be built with DecomposeStates, one state at a "
                    + $"time, rather than from every state flattened together. Pattern over "
                    + $"{group.Pattern.Count} electrode(s): "
                    + string.Join(", ", group.Pattern.OrderBy(p => p.Key, StringComparer.Ordinal)
                        .Take(6).Select(p => $"{p.Key} {p.Value:G6}")));
            }

            direct[index] += group.Direct;
            harmonics[index] = [.. harmonics[index], .. group.Harmonics];
        }

        return (direct, harmonics);
    }

    private sealed record Supply(
        Dictionary<string, double> Coefficients,
        double Direct,
        int Drive,
        double Amplitude,
        double Phase,
        int Group);

    /// <summary>
    /// Scales a supply's potentials so the leading non-zero one is exactly one, and
    /// returns the factor taken out.
    /// </summary>
    /// <remarks>
    /// Leading rather than largest, and in the order the document declared, so the
    /// same shape always normalises the same way. Dividing a value by itself is
    /// exactly one in floating point, which is what lets two supplies that really
    /// are proportional compare equal without a tolerance.
    /// </remarks>
    private static (Dictionary<string, double> Pattern, double Scale) Normalise(
        Dictionary<string, double> coefficients, IReadOnlyList<Excitation> order)
    {
        var leading = 0.0;

        foreach (var excitation in order)
        {
            if (coefficients.TryGetValue(excitation.Name, out var value) && value != 0.0)
            {
                leading = value;
                break;
            }
        }

        if (leading == 0.0)
        {
            return (coefficients, 1.0);
        }

        var pattern = new Dictionary<string, double>(coefficients.Count, StringComparer.Ordinal);

        foreach (var (name, value) in coefficients)
        {
            pattern[name] = value / leading;
        }

        return (pattern, leading);
    }

    private static bool SamePattern(Dictionary<string, double> left, Dictionary<string, double> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var (name, value) in left)
        {
            if (!right.TryGetValue(name, out var other) || other != value)
            {
                return false;
            }
        }

        return true;
    }
}
