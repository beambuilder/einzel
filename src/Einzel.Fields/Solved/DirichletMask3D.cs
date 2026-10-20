using System.Buffers.Binary;
using System.Numerics;

namespace Einzel.Fields.Solved;

/// <summary>Which of the six stencil arms a cut is recorded on.</summary>
public enum Arm3D
{
    /// <summary>Toward increasing x.</summary>
    East,

    /// <summary>Toward decreasing x.</summary>
    West,

    /// <summary>Toward increasing y.</summary>
    North,

    /// <summary>Toward decreasing y.</summary>
    South,

    /// <summary>Toward increasing z.</summary>
    Up,

    /// <summary>Toward decreasing z.</summary>
    Down,
}

/// <summary>
/// Where a conductor surface cuts between nodes, and what potential it holds there.
/// </summary>
/// <remarks>
/// <para>
/// The three-dimensional form of the Shortley-Weller data. Without it a boundary
/// snaps to the nearest node, which makes the discrete operator a staircase
/// function of where an electrode sits: invisible below one cell and percent-level
/// above one, with no step size in between. That failure was measured in two
/// dimensions and it is not a property of the dimension count.
/// </para>
/// <para>
/// <b>Stored over the cut arms alone.</b> This held a fraction and a potential for every arm
/// of every node, 96 bytes a node, and was the largest thing a volume solve allocated - for
/// 3.6 percent of arms cut on the Astral at its shipped 4 mm cell, 1.0 on the C-trap. Now a
/// node carries one byte saying which of its six arms are cut, every eight nodes share an
/// <see langword="int"/> counting the cut arms before them, and the fractions and potentials
/// are packed in node order: 1.5 bytes a node and 16 a cut arm, 5.0 a node on the Astral.
/// </para>
/// <para>
/// <b>The uncut arm is the case that has to stay cheap</b>, because the smoother asks about
/// every arm of every free node on every sweep and almost none are cut. It costs one byte
/// read per node, which <see cref="CutArms"/> hands out so a caller can ask once for all six.
/// A cut arm costs a rank - the eight-node block's count plus a population count over the
/// block's bytes - and two reads.
/// </para>
/// <para>
/// <b>The values are the ones the dense store held</b>, so a solve over them is bit-identical
/// to one over the old arrays: an uncut arm reads as a fraction of exactly one and a potential
/// of exactly zero, as it did, and a cut arm reads the double it was given.
/// </para>
/// </remarks>
public sealed class CutLinks3D
{
    /// <summary>Nodes sharing one entry of the running count: one <see langword="ulong"/> of arm bytes.</summary>
    private const int Block = 8;

    /// <summary>The first allocation for the packed arms, grown by doubling from there.</summary>
    private const int InitialCapacity = 256;

    // Which arms of each node are cut, bit (int)arm. Padded to whole blocks, so a block's
    // eight bytes can always be read as one word.
    private readonly byte[] _arms;

    // How many arms are cut in the nodes before each block. Exact below _blocksCounted; every
    // block from there on holds no cut yet, so its count is CutCount and is written when the
    // first cut reaches it.
    private readonly int[] _before;

    private int _blocksCounted;

    // The cut arms, in (node, arm) order.
    private double[] _fraction;
    private double[] _potential;

    /// <summary>Creates an uncut set of links.</summary>
    /// <param name="grid">The grid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// The grid has more nodes than a volume solve may hold; see
    /// <see cref="Core.Errors.ErrorCodes.GridTooLarge"/>.
    /// </exception>
    public CutLinks3D(Grid3D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // The mask's refusal too, so no store is made for a grid no solve could hold. This was
        // twelve doubles a node and the largest per-node allocation a solve made, until it held
        // its cut arms alone.
        grid.ThrowIfTooLargeToSolve();

        Grid = grid;

        var blocks = (grid.NodeCount + Block - 1) / Block;

        _arms = new byte[blocks * Block];
        _before = new int[blocks];
        _fraction = new double[InitialCapacity];
        _potential = new double[InitialCapacity];
    }

    /// <summary>The grid these links cover.</summary>
    public Grid3D Grid { get; }

    /// <summary>How many arms are cut.</summary>
    public int CutCount { get; private set; }

    /// <summary>What the store holds, in bytes: the arm bytes, the running counts and the packed arms.</summary>
    /// <remarks>
    /// Array payloads only, which is what the memory statement counts. After
    /// <see cref="TrimExcess"/> it is 1.5 bytes a node and 16 a cut arm.
    /// </remarks>
    public long Bytes =>
        _arms.LongLength
        + (_before.LongLength * sizeof(int))
        + ((_fraction.LongLength + _potential.LongLength) * sizeof(double));

    /// <summary>The shortest fraction recorded, for reporting how close to a node a surface came.</summary>
    public double SmallestFraction { get; private set; } = 1.0;

    /// <summary>
    /// The shortest arm a cut is allowed to be, as a fraction of a cell.
    /// </summary>
    /// <remarks>
    /// A surface passing within a thousandth of a node makes that arm's coefficient
    /// enormous and the iteration ill-conditioned. Clamping trades a sub-cell
    /// placement error for a conditioning one, and at this size the clamp is the
    /// only residual error left in a swept-boundary test.
    /// </remarks>
    public const double MinimumFraction = 1e-3;

    /// <summary>Records a cut on one arm.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <param name="arm">Which arm.</param>
    /// <param name="fraction">Distance to the surface, as a fraction of a cell.</param>
    /// <param name="potential">The potential the surface holds, in volts.</param>
    /// <remarks>
    /// <para>
    /// Cheapest in node order, which is the order the geometry builder visits: a cut on a node
    /// at or past the last one recorded is an append. A cut anywhere earlier is still recorded
    /// correctly, by moving every later arm up one place, so the order is a matter of speed and
    /// never of the answer. Call <see cref="TrimExcess"/> once the last cut is in.
    /// </para>
    /// </remarks>
    public void Cut(int i, int j, int k, Arm3D arm, double fraction, double potential)
    {
        var clamped = Math.Max(MinimumFraction, Math.Min(1.0, fraction));
        var node = Grid.Index(i, j, k);
        var bit = 1 << (int)arm;

        if ((_arms[node] & bit) != 0)
        {
            var slot = Slot(node, (int)arm);

            if (clamped >= _fraction[slot])
            {
                // An arm already cut closer wins: the nearest surface is the one the
                // stencil must reach, and a second electrode further along the same arm
                // is behind the first.
                return;
            }

            _fraction[slot] = clamped;
            _potential[slot] = potential;

            SmallestFraction = Math.Min(SmallestFraction, clamped);
            return;
        }

        if (clamped >= 1.0)
        {
            // An uncut arm reaches a whole cell, so a surface at or past the neighbour is
            // behind it - the comparison the dense store made against its fill of one.
            return;
        }

        var block = node / Block;

        if (block >= _blocksCounted)
        {
            // Every cut so far lies in an earlier block, so each block up to this one has
            // all of them before it.
            Array.Fill(_before, CutCount, _blocksCounted, block + 1 - _blocksCounted);
            _blocksCounted = block + 1;
        }

        // The new arm's bit is not set yet, so this counts exactly the arms that precede it.
        var at = Slot(node, (int)arm);

        Reserve(CutCount + 1);

        if (at < CutCount)
        {
            Array.Copy(_fraction, at, _fraction, at + 1, CutCount - at);
            Array.Copy(_potential, at, _potential, at + 1, CutCount - at);

            for (var later = block + 1; later < _blocksCounted; later++)
            {
                _before[later]++;
            }
        }

        _fraction[at] = clamped;
        _potential[at] = potential;
        _arms[node] |= (byte)bit;

        CutCount++;

        SmallestFraction = Math.Min(SmallestFraction, clamped);
    }

    /// <summary>Releases the packed arrays' unused capacity, once the last cut is recorded.</summary>
    /// <remarks>
    /// The arrays grow by doubling while cuts are recorded, so up to half of them can be
    /// spare, and they are held for the whole solve. A later cut grows them again.
    /// </remarks>
    public void TrimExcess()
    {
        if (_fraction.Length == CutCount)
        {
            return;
        }

        Array.Resize(ref _fraction, CutCount);
        Array.Resize(ref _potential, CutCount);
    }

    /// <summary>Which of a node's arms are cut.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <returns>A bit per arm, bit <c>(int)arm</c>; zero for a node no surface comes near.</returns>
    /// <remarks>
    /// One read for all six arms, so a stencil can settle the common case - nothing cut - before
    /// asking about any arm, and then ask <see cref="Fraction"/> only for those set here.
    /// </remarks>
    public int CutArms(int i, int j, int k) => _arms[Grid.Index(i, j, k)];

    /// <summary>Reads one arm.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <param name="arm">Which arm.</param>
    /// <param name="potential">The potential at the cut, when there is one; zero when there is not.</param>
    /// <returns>The fraction of a cell to the surface; one when the arm is not cut.</returns>
    public double Fraction(int i, int j, int k, Arm3D arm, out double potential)
    {
        var node = Grid.Index(i, j, k);

        if ((_arms[node] & (1 << (int)arm)) == 0)
        {
            potential = 0.0;
            return 1.0;
        }

        var slot = Slot(node, (int)arm);

        potential = _potential[slot];
        return _fraction[slot];
    }

    /// <summary>
    /// Where an arm sits among the packed ones: the cut arms of every earlier node, and of this
    /// node's earlier arms.
    /// </summary>
    /// <remarks>
    /// The block's count gives the earlier blocks; the earlier nodes of this block and the earlier
    /// arms of this node are the set bits below this arm's bit, when the block's eight bytes are
    /// read as one little-endian word - node <c>n</c> of the block in bits <c>8n</c> to
    /// <c>8n + 7</c>.
    /// </remarks>
    private int Slot(int node, int arm)
    {
        var block = node / Block;
        var word = BinaryPrimitives.ReadUInt64LittleEndian(_arms.AsSpan(block * Block, Block));
        var below = (1UL << (((node % Block) * 8) + arm)) - 1UL;

        return _before[block] + BitOperations.PopCount(word & below);
    }

    private void Reserve(int count)
    {
        if (count <= _fraction.Length)
        {
            return;
        }

        var doubled = Math.Max(InitialCapacity, 2L * _fraction.Length);
        var grown = (int)Math.Min(Array.MaxLength, Math.Max(count, doubled));

        Array.Resize(ref _fraction, grown);
        Array.Resize(ref _potential, grown);
    }
}

/// <summary>Which nodes hold a fixed potential, and the conditions on the six faces.</summary>
public sealed class DirichletMask3D
{
    private readonly bool[] _fixed;
    private readonly double[] _value;

    /// <summary>Creates a mask with nothing fixed and every face Dirichlet at zero.</summary>
    /// <param name="grid">The grid.</param>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    /// <exception cref="Core.Errors.EinzelException">
    /// The grid has more nodes than a volume solve may hold; see
    /// <see cref="Core.Errors.ErrorCodes.GridTooLarge"/>.
    /// </exception>
    public DirichletMask3D(Grid3D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // The first thing a solve allocates, so the place a refusal has to fire - after
        // the mask has been assembled is after the cost it exists to prevent.
        grid.ThrowIfTooLargeToSolve();

        Grid = grid;
        _fixed = new bool[grid.NodeCount];
        _value = new double[grid.NodeCount];
    }

    /// <summary>The grid this mask covers.</summary>
    public Grid3D Grid { get; }

    /// <summary>Where conductor surfaces cut between nodes, when the geometry carried them.</summary>
    public CutLinks3D? Cuts { get; set; }

    /// <summary>
    /// The smallest half-extent of any interior electrode, in metres.
    /// </summary>
    /// <remarks>
    /// How far a V-cycle may coarsen. Set from the geometry rather than counted
    /// from the nodes, because a node count cannot distinguish an electrode that is
    /// merely coarsely represented from one that has stopped being represented at
    /// all - and with sub-cell surfaces the count stays positive long after the
    /// arms have become too short to condition anything.
    /// </remarks>
    public double SmallestFeature { get; set; } = double.PositiveInfinity;

    /// <summary>How many nodes hold a fixed potential.</summary>
    public int FixedCount { get; private set; }

    /// <summary>How many fixed nodes lie away from the domain faces.</summary>
    /// <remarks>
    /// The number that decides how far a V-cycle may coarsen. A boundary surface
    /// keeps a fraction of its nodes per level and survives; an interior electrode
    /// is a volume and loses seven eighths of its nodes each time, which a total
    /// count would call healthy right up to the level where it vanishes.
    /// </remarks>
    public int InteriorFixedCount { get; private set; }

    /// <summary>Condition on the x-minimum face.</summary>
    public EdgeCondition LowerX { get; set; }

    /// <summary>Condition on the x-maximum face.</summary>
    public EdgeCondition UpperX { get; set; }

    /// <summary>Condition on the y-minimum face.</summary>
    public EdgeCondition LowerY { get; set; }

    /// <summary>Condition on the y-maximum face.</summary>
    public EdgeCondition UpperY { get; set; }

    /// <summary>Condition on the z-minimum face.</summary>
    public EdgeCondition LowerZ { get; set; }

    /// <summary>Condition on the z-maximum face.</summary>
    public EdgeCondition UpperZ { get; set; }

    /// <summary>Whether a node holds a fixed potential.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <returns><see langword="true"/> when fixed.</returns>
    public bool IsFixed(int i, int j, int k) => _fixed[Grid.Index(i, j, k)];

    /// <summary>The potential a fixed node holds.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <returns>The potential, in volts.</returns>
    public double Value(int i, int j, int k) => _value[Grid.Index(i, j, k)];

    /// <summary>Fixes a node.</summary>
    /// <param name="i">Node index along x.</param>
    /// <param name="j">Node index along y.</param>
    /// <param name="k">Node index along z.</param>
    /// <param name="potential">The potential to hold, in volts.</param>
    public void Fix(int i, int j, int k, double potential)
    {
        var index = Grid.Index(i, j, k);

        if (!_fixed[index])
        {
            FixedCount++;

            var onFace = i == 0 || j == 0 || k == 0
                || i == Grid.CountX - 1 || j == Grid.CountY - 1 || k == Grid.CountZ - 1;

            if (!onFace)
            {
                InteriorFixedCount++;
            }
        }

        _fixed[index] = true;
        _value[index] = potential;
    }

    /// <summary>Writes every fixed value into a field.</summary>
    /// <param name="field">The field to stamp.</param>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    public void ApplyTo(ScalarField3D field)
    {
        ArgumentNullException.ThrowIfNull(field);

        var values = field.Values;

        for (var index = 0; index < _fixed.Length; index++)
        {
            if (_fixed[index])
            {
                values[index] = _value[index];
            }
        }
    }
}
