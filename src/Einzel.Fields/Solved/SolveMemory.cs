namespace Einzel.Fields.Solved;

/// <summary>What building a solved field holds in memory, known before anything is built.</summary>
/// <param name="Nodes">Nodes on the finest grid.</param>
/// <param name="Channels">Basis solves the geometry decomposes into; one for a static geometry.</param>
/// <param name="Galerkin">
/// Whether the coarse levels are formed by Galerkin coarsening. Always false for a plane solve,
/// which has only the rediscretized hierarchy.
/// </param>
/// <param name="PeakBytes">The most the build holds live at once, in bytes.</param>
/// <param name="RetainedBytes">What the finished field keeps once the build returns, in bytes.</param>
/// <remarks>
/// <para>
/// <b>The live set, not the process's working set.</b> What is counted is what the solve
/// holds <em>reachable</em> at its peak - the number that has to fit. The collector does not
/// free a large array the moment it becomes garbage, so on a machine with memory to spare the
/// process runs higher: 390 bytes a node on the Astral at a 4 mm cell against a live 242.
/// Under a hard heap limit the same solve completed at 279 MiB and ran out at 275, against a
/// live peak of 259, so the collector gives the garbage back when it has to, and the live set
/// plus a few per cent is the floor.
/// </para>
/// <para>
/// <b>Stated beside the allocations it describes</b>, in <see cref="SolveMemoryModel"/>, so a
/// new per-node array is added to the sum in the same file as the reason for it, and
/// <c>SolveMemoryTests</c> measures a real build against this number.
/// </para>
/// </remarks>
public sealed record SolveMemory(
    long Nodes, int Channels, bool Galerkin, double PeakBytes, double RetainedBytes);

/// <summary>
/// Bytes per finest-level node that each part of a solve holds, and what they sum to at the peak.
/// </summary>
/// <remarks>
/// <para>
/// <b>Measured, then written as arithmetic over the arrays.</b> The estimate this replaces
/// quoted 48 bytes a node for every solve - potential, right-hand side, residual and "the
/// correction and restriction buffers" - and a volume solve held five times that. What it
/// left out is the geometry: a conductor mask, and cut links that were six arms of two
/// doubles a node, 96 bytes before a single field was allocated. The Galerkin hierarchy then
/// assembles the fine operator as a stored stencil of eight doubles a node, and forms the
/// first coarse level from it with three scratch arrays over the fine grid.
/// </para>
/// <para>
/// <b>The cut links are now stored over the cut arms alone</b>, which took them from 96 bytes
/// a node to 1.5 and 16 a cut arm, and the volume peaks down by 90.5 with them. That term is
/// the one here that depends on the geometry, so it is billed as an allowance; see
/// <see cref="VolumeCutArmsPerNode"/>.
/// </para>
/// <para>
/// Peak live set measured by forcing full collections through a build, Release, on the shipped
/// templates - every couple of milliseconds for the volume figures, and as densely as the
/// collections allow for the plane ones, whose peak is too brief for the coarser sampling:
/// </para>
/// <list type="table">
/// <item><description>
/// volume, Galerkin: astral-3d at 1.12 M and 8.68 M nodes, <b>150.8</b> and <b>148.2</b> bytes
/// a node; <b>241.8</b> and <b>240.9</b> with dense cut links
/// </description></item>
/// <item><description>
/// volume, rediscretized: segmented-quadrupole at 0.14 M and 1.09 M nodes, <b>47.1</b> and
/// <b>46.0</b>, C-trap at 0.14 M and 1.08 M, <b>45.7</b> and <b>45.3</b>, against 48.8 to 48.9
/// stated with the billed cut arms (<c>tools/memory-harness</c>, one process a run); and against
/// the statement with each geometry's real cut arms, a sphere 45.0 against 45.0 on a deep grid,
/// 42.50 against 42.50 with no coarse level, 44.87 against 44.71 one level deep and 43.87
/// against 43.87 on an 8193 by 9 by 9 grid two levels deep (<c>SolveMemoryTests</c>).
/// <b>141.9</b>, <b>141.8</b> and <b>141.8</b> with dense cut links, before the V-cycle's
/// residual had a frame of its own
/// </description></item>
/// <item><description>
/// plane: quadrupole at 1.05 M, 4.2 M and 16.8 M nodes, <b>135.3</b> each
/// (<c>tools/memory-harness</c>); four rods at 263 k, <b>135.4</b>, sampled as densely as the
/// collections allow, because the plane's peak is a transient lasting a fraction of a
/// millisecond: a build whose samples see only the fine level, as on a loaded four-core runner,
/// reads about 126.6
/// </description></item>
/// <item><description>
/// first-tier code throughout, in a child process with <c>DOTNET_TC_CallCounting=0</c>
/// (<c>FirstTierMemoryTests</c>): four rods at 66 k nodes <b>135.57</b> against 135.25, a sphere
/// at 36 k <b>45.25</b> against 45.19 with its real cut arms - the same as optimised code reads
/// </description></item>
/// </list>
/// <para>
/// The arithmetic below reproduces the Galerkin and plane figures to 1 percent, and the
/// rediscretized ones to within 0.4 percent with each geometry's real cut arms; with the billed
/// allowance it is 4 to 8 percent over them, conservatively - an allowance those geometries do
/// not use. It is the arithmetic that is used, rather than the measurements, so that each term
/// names the array responsible for it.
/// </para>
/// <para>
/// <b>The plane read 143.3 to 143.9, and the rediscretized volume 47.8 to 48.7, before the
/// V-cycle's fine residual had a frame of its own</b>: the cycle held it in a local of the frame
/// that recursed, which first-tier code keeps to the end of the method, so a build made before
/// the solver was promoted held one array of the fine grid more. The statements said 143.25 and
/// 51.2 then, and charged that array; "a build that misses the transient reads about 135",
/// which this record used to say, was the optimised build holding what it should.
/// </para>
/// <para>
/// <b>Superseded: "not counted, an instant".</b> A sampler now and then read about 7 bytes a node
/// over, and this record put it down to catching a new array beside the one it replaces. It was
/// the sampler counting garbage allocated after its own collection; it now reads what the
/// collection left live (<c>SolveMemoryTests</c>' sampler), and a reading over the statement is
/// an array the statement does not charge.
/// </para>
/// </remarks>
public static class SolveMemoryModel
{
    /// <summary>A volume conductor mask: a fixed flag and a held potential per node.</summary>
    public const double VolumeMask = sizeof(bool) + sizeof(double);

    /// <summary>Volume cut links per node: a byte naming the cut arms, and a running count per eight nodes.</summary>
    public const double VolumeCutIndex = sizeof(byte) + (sizeof(int) / 8.0);

    /// <summary>One cut arm, packed: its fraction and its surface potential.</summary>
    public const double VolumeCutArm = 2 * sizeof(double);

    /// <summary>Cut arms billed per node, since how many there are is not known without rasterizing.</summary>
    /// <remarks>
    /// <para>
    /// Above the densest shipped geometry: the Astral at its 4 mm cell has 0.217 cut arms a node
    /// (3.6 percent of arms) and the compact Astral, the same mesh scaled, the same; the linear ion
    /// trap 0.106, the segmented quadrupole 0.077, the C-trap 0.058 and the parallel-plate
    /// example 0.045, each at its shipped mesh.
    /// </para>
    /// <para>
    /// <b>The density falls as the mesh refines</b>, because cut arms lie on surfaces: their
    /// number goes as one over the cell size squared and the node count as one over its cube.
    /// So the allowance is tightest at the coarse meshes, where the whole solve is small, and
    /// over-bills a fine one - the Astral at 1 mm has about a quarter of its 4 mm density. A
    /// geometry denser than this is under-billed by 16 bytes for each cut arm past a quarter a
    /// node.
    /// </para>
    /// </remarks>
    public const double VolumeCutArmsPerNode = 0.25;

    /// <summary>Volume cut links: the per-node index, and the billed cut arms packed beside it.</summary>
    /// <remarks>
    /// Was 96 bytes a node - six arms of two doubles each, allocated for every node although
    /// 3.6 percent of arms are cut on the Astral at 4 mm - and the largest single term. Now
    /// 5.5: the store holds the cut arms alone, and is 5.0 on that same Astral.
    /// </remarks>
    public const double VolumeCutLinks = VolumeCutIndex + (VolumeCutArmsPerNode * VolumeCutArm);

    /// <summary>The potential, the right-hand side and the residual.</summary>
    public const double Fields = 3 * sizeof(double);

    /// <summary>The fine operator as a stored stencil: diagonal, six arms, and the known term.</summary>
    public const double VolumeStencil = (1 + 6 + 1) * sizeof(double);

    /// <summary>
    /// Forming a Galerkin level: a column and its image over the fine grid, and a stamp.
    /// </summary>
    public const double GalerkinForming = (2 * sizeof(double)) + sizeof(int);

    /// <summary>
    /// The first coarse Galerkin level: twenty-seven coefficients on an eighth of the nodes,
    /// which is what it comes to on a grid many cells deep on every axis.
    /// </summary>
    /// <remarks>
    /// A coarse grid keeps one node more than half on each axis, so its first level is more
    /// than an eighth of the fine one, and much more where an axis is thin: 32769 by 5 by 5
    /// coarsens to 16385 by 3 by 3, which is 38.9 bytes a fine node rather than 27.
    /// <see cref="VolumeGalerkinPeakOn"/> charges a particular grid's real first level.
    /// </remarks>
    public const double GalerkinFirstLevel = 27.0 * sizeof(double) / 8.0;

    /// <summary>
    /// A volume solve on the Galerkin hierarchy, at its peak: while the first coarse level is
    /// being formed from the fine stencil.
    /// </summary>
    /// <remarks>
    /// 149.5 bytes a node, against a measured 150.8; 240 with the cut links stored densely. After
    /// the hierarchy is built the stencil and the scratch are garbage and the solve holds about 80.
    /// </remarks>
    public const double VolumeGalerkinPeak =
        VolumeMask + VolumeCutLinks + Fields + VolumeStencil + GalerkinForming + GalerkinFirstLevel;

    /// <summary>
    /// A volume solve on the rediscretized hierarchy, at its peak, on a grid many cells deep on
    /// every axis: the instant the fine level restricts its residual onto the first coarse
    /// level, in the second cycle or any later one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The fine level's mask, cut links and fields; the V-cycle's fine residual and the
    /// restricted residual it is becoming, on a first level an eighth the size; and every coarse
    /// level's mask - 9 bytes a coarse node summed over levels an eighth the size of the one
    /// above, because the coarsener keeps every mask it has built for the whole solve. 48.8
    /// bytes a node with the billed quarter of a cut arm a node.
    /// <see cref="VolumeRediscretizedPeakOn"/> works out every instant of a cycle on the levels a
    /// particular grid really has, the bottom solve's scratch included, and takes the largest;
    /// on a deep grid it is this one.
    /// </para>
    /// <para>
    /// <b>The second cycle, not the first.</b> <c>GeometryBuilder3D.Coarsener</c> memoises each
    /// coarse mask by grid, so the first cycle builds them on its way down - at its fine
    /// restriction only the first level's mask exists - and from the second cycle on every one
    /// of them is live throughout. A solve whose coarsener does not memoise holds fewer, and is
    /// over-billed by the masks below the first level, 0.16 bytes a node.
    /// </para>
    /// <para>
    /// <b>This said 51.2, the deepest point of a cycle with every level's residual still held,
    /// and it was right only for first-tier code.</b> The V-cycle kept each level's residual
    /// and coarse arrays as locals of the frame that recursed, which optimised code ends at
    /// their last use and first-tier code - what a method runs as until it has been called
    /// often enough to be recompiled - keeps to the end of the method. Measured with call
    /// counting off, so every method stays first-tier, the sphere's slope read 47.6 against 45.0
    /// fully optimised; a solve that finished before the solver was promoted held the larger. Both
    /// read 45.0 now that the descent is in frames of its own. It then said 48.6, which left out
    /// the masks below the first level.
    /// </para>
    /// </remarks>
    public const double VolumeRediscretizedPeak =
        VolumeMask + VolumeCutLinks + Fields + sizeof(double)
        + (sizeof(double) / 8.0) + (VolumeMask / 7.0);

    /// <summary>
    /// <see cref="VolumeGalerkinPeak"/> on a particular grid, its first coarse level charged at
    /// the size that grid's coarsening really gives.
    /// </summary>
    /// <param name="grid">The finest grid.</param>
    /// <returns>Bytes per finest-level node at the peak.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> is null.</exception>
    public static double VolumeGalerkinPeakOn(Grid3D grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var firstLevel = grid.CanCoarsen ? (double)grid.Coarsen().NodeCount / grid.NodeCount : 0.0;

        return VolumeMask + VolumeCutLinks + Fields + VolumeStencil + GalerkinForming
            + (GalerkinOperator3D.Entries * sizeof(double) * firstLevel);
    }

    /// <summary>
    /// <see cref="VolumeRediscretizedPeak"/> on a particular grid: the largest instant of a
    /// cycle over the coarse levels the solve will build, at the sizes they really are, rather
    /// than a series of eighths.
    /// </summary>
    /// <param name="grid">The finest grid.</param>
    /// <param name="levels">The coarse levels, as <see cref="PoissonSolver3D.RediscretizedLevels"/> gives them.</param>
    /// <returns>Bytes per finest-level node at the peak.</returns>
    /// <exception cref="ArgumentNullException">A required argument is null.</exception>
    /// <remarks>
    /// <para>
    /// Two kinds of instant. Where level k restricts its residual: every coarse level down to k
    /// holds its right-hand side and its correction, level k its residual, and level k + 1 the
    /// restricted residual. Where the bottom level is solved: every coarse level holds its
    /// right-hand side and its correction, and the bottom a scratch residual of its own size,
    /// which <c>SolveCoarsest</c> allocates - over the fine grid itself when the solve builds no
    /// coarse level at all, which makes that case 46.5 bytes a node rather than 38.5. Every
    /// coarse mask is live at both, from the second cycle on; see
    /// <see cref="VolumeRediscretizedPeak"/>.
    /// </para>
    /// <para>
    /// Which wins depends on the depth: on a grid many cells deep the fine restriction, by
    /// about 6 bytes a node; with no coarse level, the bottom, since there is nothing else; and
    /// on a grid thin on some axis, where a level shrinks by much less than an eighth, the sums
    /// over the levels decide it - hence every instant, rather than one formula.
    /// </para>
    /// </remarks>
    public static double VolumeRediscretizedPeakOn(Grid3D grid, IReadOnlyList<Grid3D> levels)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(levels);

        // Level 0 is the fine grid, whose mask, cut links and fields are the fixed part, and
        // whose right-hand side and potential are two of those fields.
        double Size(int level) => level == 0 ? 1.0 : (double)levels[level - 1].NodeCount / grid.NodeCount;

        var masks = 0.0;

        for (var level = 1; level <= levels.Count; level++)
        {
            masks += VolumeMask * Size(level);
        }

        var held = 0.0;
        var largest = 0.0;

        for (var k = 0; k < levels.Count; k++)
        {
            held += k == 0 ? 0.0 : 2 * sizeof(double) * Size(k);
            largest = Math.Max(largest, held + (sizeof(double) * (Size(k) + Size(k + 1))));
        }

        var bottom = levels.Count;
        var atBottom = held + (bottom == 0 ? 0.0 : 2 * sizeof(double) * Size(bottom)) + (sizeof(double) * Size(bottom));

        return VolumeMask + VolumeCutLinks + Fields + masks + Math.Max(largest, atBottom);
    }

    /// <summary>A plane conductor mask.</summary>
    public const double PlaneMask = sizeof(bool) + sizeof(double);

    /// <summary>Plane cut links: four arms, each a fraction and a surface potential.</summary>
    public const double PlaneCutLinks = 4 * 2 * sizeof(double);

    /// <summary>
    /// A plane solve at its peak: the instant the first coarse level re-zeroes its cut links.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A coarse level solves for the error, which is zero on a conductor, so it replaces its
    /// cut links with a copy holding zero potential - and for that instant holds both. The fine
    /// level then holds its mask, cut links and three fields; the first coarse level, a quarter
    /// the size, its mask, both copies of its cut links, its restricted right-hand side and its
    /// correction. The V-cycle's fine residual is already dead, having been restricted, and
    /// its own residual and every deeper level come later, when the old copy is garbage, so
    /// every later instant holds less: 135.25 bytes a node.
    /// </para>
    /// <para>
    /// <b>This said 143.25, with the fine residual held, and that was true of first-tier code
    /// only.</b> The V-cycle kept the residual as a local of the frame that went on to recurse,
    /// and first-tier code - what a method runs as until it has been called often enough to be
    /// recompiled - keeps a local to the end of its method where optimised code ends it at its
    /// last use. The first plane build in a process read 143.6 and every later one 135.6, which
    /// the sampler's record put down to catching or missing the copy; with call counting off,
    /// so that nothing is ever promoted, every build read 143.4, and fully optimised every build
    /// read 135.4. The residual is now restricted in a frame of its own and every tier reads
    /// 135.4; <c>FirstTierMemoryTests</c> measures a solve with promotion switched off to hold it.
    /// </para>
    /// <para>
    /// <b>This said 153.3, and the sampler that checked it hid the difference.</b> It summed
    /// every coarse level and that level's residual on top of the transient, which do not
    /// exist together. Sampled every couple of milliseconds the plane build read 143 to 150,
    /// and the test's slope between two meshes read high whenever the smaller build's peak was
    /// missed; sampled densely, the largest of three builds read 143.3 to 143.9 on four
    /// templates and meshes from 17 k to 1.05 M nodes - the held residual of the paragraph above,
    /// not this transient. It reads 135.3 on the quadrupole from 1.05 M to 16.8 M nodes now.
    /// </para>
    /// </remarks>
    public const double PlanePeak =
        PlaneMask + PlaneCutLinks + Fields
        + ((PlaneMask + (2 * PlaneCutLinks) + (2 * sizeof(double))) / 4.0);

    /// <summary>What a finished field keeps per channel: its solved potential.</summary>
    /// <remarks>
    /// The interpolants reference the potential rather than tabulating coefficients, so a
    /// channel costs one double a node once it is built. Measured at 8.0 on every template.
    /// </remarks>
    public const double RetainedPerChannel = sizeof(double);

    /// <summary>What a build holds at its peak, given one channel's working set.</summary>
    /// <param name="nodes">Nodes on the finest grid.</param>
    /// <param name="channels">How many basis solves the build makes.</param>
    /// <param name="workingPerNode">One channel's working set, in bytes a node.</param>
    /// <param name="galerkin">Whether that working set is the Galerkin one.</param>
    /// <returns>The statement.</returns>
    /// <remarks>
    /// Channels are solved one at a time and each keeps only its potential, so the peak is one
    /// working set plus the potentials of every channel solved before it.
    /// </remarks>
    public static SolveMemory For(long nodes, int channels, double workingPerNode, bool galerkin)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(nodes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);

        return new SolveMemory(
            nodes,
            channels,
            galerkin,
            nodes * (workingPerNode + (RetainedPerChannel * (channels - 1))),
            nodes * RetainedPerChannel * channels);
    }
}
