using Einzel.Fields.Solved;

namespace Einzel.Fields.Tests;

/// <summary>
/// The cut-link store held over its cut arms alone, against a plain dictionary of the same cuts.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CutLinks3D"/> held a fraction and a potential for every arm of every node, and a
/// solve over it is the reference every volume number here was computed on. It now packs the
/// cut arms in node order behind a byte a node and a running count per eight nodes, and the
/// claim is that every read is the one the dense store gave - an uncut arm reads one and zero,
/// a cut one the double it was handed. The reference here is the dense rule written as a
/// dictionary, which shares no arithmetic with the packing.
/// </para>
/// <para>
/// <b>Out of order on purpose.</b> The geometry builder records cuts in node order, which is the
/// append path and the only one a solve exercises. A cut on an earlier node has to move every
/// later arm and every later block's count; a store that got that wrong would pass anything fed
/// in order and read the wrong arm for the rest of the grid.
/// </para>
/// </remarks>
public sealed class CutLinks3DTests
{
    /// <summary>Cuts in random order, repeated and out of range, read back as the dense rule says.</summary>
    [Theory]
    [InlineData(5, 7, 3, 1)]
    [InlineData(9, 9, 9, 2)]
    [InlineData(17, 3, 4, 3)]
    public void EveryArmReadsWhatTheDenseStoreWouldHave(int nx, int ny, int nz, int seed)
    {
        var grid = new Grid3D(0.0, 0.0, 0.0, 1e-3, 1e-3, 1e-3, nx, ny, nz);
        var random = new Random(seed);
        var cuts = new CutLinks3D(grid);
        var reference = new Dense(grid);

        for (var call = 0; call < 6 * (int)grid.NodeCount; call++)
        {
            var i = random.Next(nx);
            var j = random.Next(ny);
            var k = random.Next(nz);
            var arm = (Arm3D)random.Next(6);

            // Past a whole cell, inside the clamp, and ordinary: each takes a different branch.
            var fraction = random.Next(5) switch
            {
                0 => 1.0 + random.NextDouble(),
                1 => random.NextDouble() * 1e-3,
                _ => random.NextDouble(),
            };

            var potential = (random.NextDouble() - 0.5) * 2000.0;

            cuts.Cut(i, j, k, arm, fraction, potential);
            reference.Cut(i, j, k, arm, fraction, potential);
        }

        AssertSame(grid, reference, cuts);

        cuts.TrimExcess();

        AssertSame(grid, reference, cuts);
    }

    /// <summary>The same cuts in node order and shuffled give the same store.</summary>
    [Fact]
    public void TheOrderCutsArriveInChangesNothing()
    {
        var grid = new Grid3D(0.0, 0.0, 0.0, 1e-3, 1e-3, 1e-3, 11, 6, 5);
        var random = new Random(4);
        var calls = new List<(int I, int J, int K, Arm3D Arm, double Fraction, double Potential)>();

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    foreach (var arm in Enum.GetValues<Arm3D>())
                    {
                        if (random.NextDouble() < 0.3)
                        {
                            calls.Add((i, j, k, arm, random.NextDouble(), random.NextDouble() * 100.0));
                        }
                    }
                }
            }
        }

        var ordered = new CutLinks3D(grid);
        var shuffled = new CutLinks3D(grid);

        foreach (var (i, j, k, arm, fraction, potential) in calls)
        {
            ordered.Cut(i, j, k, arm, fraction, potential);
        }

        foreach (var (i, j, k, arm, fraction, potential) in calls.OrderBy(_ => random.Next()))
        {
            shuffled.Cut(i, j, k, arm, fraction, potential);
        }

        Assert.Equal(ordered.CutCount, shuffled.CutCount);

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    Assert.Equal(ordered.CutArms(i, j, k), shuffled.CutArms(i, j, k));

                    foreach (var arm in Enum.GetValues<Arm3D>())
                    {
                        var a = ordered.Fraction(i, j, k, arm, out var heldA);
                        var b = shuffled.Fraction(i, j, k, arm, out var heldB);

                        Assert.Equal(BitConverter.DoubleToInt64Bits(a), BitConverter.DoubleToInt64Bits(b));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(heldA), BitConverter.DoubleToInt64Bits(heldB));
                    }
                }
            }
        }
    }

    /// <summary>A trimmed store holds a byte and a half a node and sixteen bytes a cut arm.</summary>
    /// <remarks>
    /// What <see cref="SolveMemoryModel.VolumeCutIndex"/> and
    /// <see cref="SolveMemoryModel.VolumeCutArm"/> state, read off the store itself; the
    /// statement's slope tests cannot see a term this small next to the fields.
    /// </remarks>
    [Fact]
    public void ATrimmedStoreHoldsWhatTheStatementCounts()
    {
        var grid = new Grid3D(0.0, 0.0, 0.0, 1e-3, 1e-3, 1e-3, 33, 33, 33);
        var cuts = new CutLinks3D(grid);

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                // One plane of x-arms cut, as a flat conductor face would.
                cuts.Cut(16, j, k, Arm3D.East, 0.4, 100.0);
            }
        }

        cuts.TrimExcess();

        var blocks = (grid.NodeCount + 7) / 8;

        Assert.Equal(33 * 33, cuts.CutCount);
        Assert.Equal(
            (blocks * 8 * SolveMemoryModel.VolumeCutIndex) + (cuts.CutCount * SolveMemoryModel.VolumeCutArm),
            cuts.Bytes);
    }

    private static void AssertSame(Grid3D grid, Dense reference, CutLinks3D cuts)
    {
        Assert.Equal(reference.CutCount, cuts.CutCount);
        Assert.Equal(reference.SmallestFraction, cuts.SmallestFraction);

        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                for (var i = 0; i < grid.CountX; i++)
                {
                    var bits = 0;

                    foreach (var arm in Enum.GetValues<Arm3D>())
                    {
                        var expected = reference.Fraction(i, j, k, arm, out var expectedHeld);
                        var actual = cuts.Fraction(i, j, k, arm, out var actualHeld);

                        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
                        Assert.Equal(BitConverter.DoubleToInt64Bits(expectedHeld), BitConverter.DoubleToInt64Bits(actualHeld));

                        if (expected < 1.0)
                        {
                            bits |= 1 << (int)arm;
                        }
                    }

                    Assert.Equal(bits, cuts.CutArms(i, j, k));
                }
            }
        }
    }

    /// <summary>The dense store's rule, as it was written, over a dictionary.</summary>
    private sealed class Dense(Grid3D grid)
    {
        private readonly Dictionary<(int Node, Arm3D Arm), (double Fraction, double Potential)> _cut = [];

        public int CutCount { get; private set; }

        public double SmallestFraction { get; private set; } = 1.0;

        public void Cut(int i, int j, int k, Arm3D arm, double fraction, double potential)
        {
            var clamped = Math.Max(CutLinks3D.MinimumFraction, Math.Min(1.0, fraction));
            var key = (grid.Index(i, j, k), arm);
            var held = _cut.TryGetValue(key, out var existing) ? existing.Fraction : 1.0;

            if (clamped >= held)
            {
                return;
            }

            if (held == 1.0)
            {
                CutCount++;
            }

            _cut[key] = (clamped, potential);

            SmallestFraction = Math.Min(SmallestFraction, clamped);
        }

        public double Fraction(int i, int j, int k, Arm3D arm, out double potential)
        {
            if (_cut.TryGetValue((grid.Index(i, j, k), arm), out var held))
            {
                potential = held.Potential;
                return held.Fraction;
            }

            potential = 0.0;
            return 1.0;
        }
    }
}
