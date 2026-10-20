namespace Einzel.Core.Model;

/// <summary>
/// A three-dimensional solved field, as it appears in a model document.
/// </summary>
/// <remarks>
/// The field type for a device with no symmetry to exploit. Everything a
/// <c>solved2d</c> element carries is here too - a drive, a sequence, sub-cell
/// boundaries - because none of that was ever about the dimension count; what
/// changes is that the domain is a box rather than a rectangle and an electrode is
/// a body rather than a cross-section.
/// </remarks>
public sealed record SolvedField3DDocument
{
    /// <summary>Lower x bound of the solve domain.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Lower z bound.</summary>
    public QuantityValue? MinZ { get; init; }

    /// <summary>Upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Upper z bound.</summary>
    public QuantityValue? MaxZ { get; init; }

    /// <summary>
    /// Node spacing. Each axis rounds its own interval count up to a power of two
    /// from this, so the domain is meshed exactly and no direction is coarser than
    /// asked.
    /// </summary>
    public QuantityValue? CellSize { get; init; }

    /// <summary>Relative residual the solve must reach.</summary>
    public double Tolerance { get; init; } = 1e-9;

    /// <summary>The RF drive this geometry is operated with, if any.</summary>
    public DriveDocument? Drive { get; init; }

    /// <summary>Every generator this geometry is operated with.</summary>
    /// <remarks>
    /// <para>
    /// The three-dimensional form of what the two-dimensional solve already carried.
    /// <c>CompiledSolvedField3D</c>, <c>Geometry3D</c> and the builder all held a list
    /// from the start; only the document spelled a single <c>drive</c>, so a volume
    /// geometry could not express what a cross-section could.
    /// </para>
    /// <para>
    /// Costs nothing in the solver: basis superposition is indifferent to what the
    /// weights are functions of, so two generators reaching the same electrodes in the
    /// same proportions are one solved pattern carrying two weights on two clocks.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DriveDocument>? Drives { get; init; }

    /// <summary>A timed sequence of states it is operated through, if any.</summary>
    public IReadOnlyList<StageDocument>? Stages { get; init; }

    /// <summary>The electrodes.</summary>
    public IReadOnlyList<Electrode3DDocument>? Electrodes { get; init; }

    /// <summary>Mirror the solved half about this plane along x, or omit for none.</summary>
    /// <remarks>
    /// <para>
    /// <b>Solve half an instrument and reflect it.</b> An analyser symmetric about a plane
    /// costs twice what it needs to, and in a volume solve halving the domain halves the
    /// dominant cost - on a 3-D Astral, where the solve is 94 per cent of a run, this is the
    /// cheapest factor of two there is.
    /// </para>
    /// <para>
    /// <b>Declare the mid-plane face Neumann alongside it.</b> A mirror plane is a symmetry
    /// plane; grounding it instead puts a conductor down the middle of the instrument. The
    /// plane path pairs <c>rightEdge</c> with <c>reflectAboutX</c> for exactly this reason.
    /// </para>
    /// </remarks>
    public QuantityValue? ReflectAboutX { get; init; }

    /// <summary>Lower x face: <c>dirichlet</c> (the default) or <c>neumann</c>.</summary>
    /// <remarks>
    /// <para>
    /// <b>The default is a grounded box, and a grounded box is a third electrode.</b> That
    /// is right for a device inside a housing and wrong for one whose geometry is invariant
    /// along an axis: a stripe electrode running the length of an analyser's drift makes the
    /// field independent of the drift direction, so grounding those faces imposes an axial
    /// field the real instrument does not have — and an ion drifts the wrong way.
    /// </para>
    /// <para>
    /// A Neumann face is a mirror, so declaring both faces of an axis Neumann says the
    /// structure repeats forever along it. Cheaper as well as truer: a domain that has to
    /// hold the end fields must be longer than the ones that matter.
    /// </para>
    /// </remarks>
    public string? LowerXEdge { get; init; }

    /// <summary>Upper x face: <c>dirichlet</c> (the default) or <c>neumann</c>.</summary>
    public string? UpperXEdge { get; init; }

    /// <summary>Lower y face: <c>dirichlet</c> (the default) or <c>neumann</c>.</summary>
    public string? LowerYEdge { get; init; }

    /// <summary>Upper y face: <c>dirichlet</c> (the default) or <c>neumann</c>.</summary>
    public string? UpperYEdge { get; init; }

    /// <summary>Lower z face: <c>dirichlet</c> (the default) or <c>neumann</c>.</summary>
    public string? LowerZEdge { get; init; }

    /// <summary>Upper z face: <c>dirichlet</c> (the default) or <c>neumann</c>.</summary>
    public string? UpperZEdge { get; init; }
}

/// <summary>A three-dimensional electrode, as it appears in a model document.</summary>
public sealed record Electrode3DDocument : ITappedElectrode
{
    /// <summary>
    /// What the electrode is called: how a lost ion, a channel and an error name it, so it
    /// must differ from every other electrode's in the same solve. Omitted, it is
    /// <c>electrode-N</c> after its place in the list.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// One of <c>box</c>, <c>sphere</c>, <c>cylinder</c>, <c>prism</c> or <c>revolve</c>.
    /// </summary>
    public string? Shape { get; init; }

    /// <summary>Repeats this electrode, binding an index its expressions can name.</summary>
    public RepeatDocument? Repeat { get; init; }

    /// <summary>Box: lower x bound.</summary>
    public QuantityValue? MinX { get; init; }

    /// <summary>Box: lower y bound.</summary>
    public QuantityValue? MinY { get; init; }

    /// <summary>Box: lower z bound.</summary>
    public QuantityValue? MinZ { get; init; }

    /// <summary>Box: upper x bound.</summary>
    public QuantityValue? MaxX { get; init; }

    /// <summary>Box: upper y bound.</summary>
    public QuantityValue? MaxY { get; init; }

    /// <summary>Box: upper z bound.</summary>
    public QuantityValue? MaxZ { get; init; }

    /// <summary>Sphere or cylinder: centre x.</summary>
    public QuantityValue? CentreX { get; init; }

    /// <summary>Sphere or cylinder: centre y.</summary>
    public QuantityValue? CentreY { get; init; }

    /// <summary>Sphere or cylinder: centre z.</summary>
    public QuantityValue? CentreZ { get; init; }

    /// <summary>Sphere or cylinder: radius.</summary>
    public QuantityValue? Radius { get; init; }

    /// <summary>Cylinder: which axis it runs along, one of <c>x</c>, <c>y</c>, <c>z</c>.</summary>
    public string? Axis { get; init; }

    /// <summary>Box: which axis it is tilted about, one of <c>x</c>, <c>y</c>, <c>z</c>.</summary>
    /// <remarks>
    /// Defaults to <c>z</c> and is only read when <see cref="TiltHalfTurns"/> is non-zero,
    /// so an untilted box needs neither.
    /// </remarks>
    public string? TiltAxis { get; init; }

    /// <summary>Box: how far it is tilted about <see cref="TiltAxis"/>, in half turns.</summary>
    /// <remarks>
    /// <para>
    /// <b>Half turns, so a right angle is exact.</b> One quarter is a right angle, one half
    /// is upside down; <c>double.CosPi(0.5)</c> is exactly zero where
    /// <c>Math.Cos(Math.PI / 2)</c> is 6.1e-17, which would tilt a nominally upright plate
    /// by a rounding. The same convention as <c>cosPi</c> in the expression grammar and as
    /// a drive phase.
    /// </para>
    /// <para>
    /// <b>What it is for</b> is a plate deliberately not parallel to another - an
    /// asymmetric-track analyser's mirrors converge by a couple of hundred microns over a
    /// third of a metre, and that convergence is the mechanism rather than a tolerance.
    /// A tilt far below one cell is still resolved, because the surface is a cut cell.
    /// </para>
    /// </remarks>
    public QuantityValue? TiltHalfTurns { get; init; }


    /// <summary>
    /// Prism or revolve: the outline's vertices, in order, in the outline's own plane. Runs
    /// with <c>count</c> and <c>index</c> are allowed, as for a polygon.
    /// </summary>
    /// <remarks>
    /// <b>For a prism</b> the plane is the cross-section - the two axes other than
    /// <c>axis</c>, in world order: (y, z) along x, (x, z) along y, (x, y) along z.
    /// <b>For a revolve the plane contains the axis</b>, so <c>x</c> is the distance from it
    /// and <c>y</c> the position along it. That is the (radius, axial) half-plane an
    /// axisymmetric solve already works in, and a vertex at a negative radius is refused.
    /// </remarks>
    public IReadOnlyList<VertexDocument>? Vertices { get; init; }

    /// <summary>Revolve: where the sweep begins, in half turns about <c>axis</c>.</summary>
    /// <remarks>
    /// <para>
    /// Half turns rather than radians, the convention the drive decomposition, the expression
    /// grammar and the box tilt already use: <c>cosPi(0.5)</c> is exactly zero where
    /// <c>Math.Cos(Math.PI / 2)</c> is 6.1e-17, so a rod meant to start on an axis starts on
    /// it rather than a rounding off it.
    /// </para>
    /// <para>
    /// Absent on both means a full turn, which is the ring a reader expects from a profile
    /// revolved with nothing said about how far.
    /// </para>
    /// </remarks>
    public QuantityValue? FromHalfTurns { get; init; }

    /// <summary>Revolve: where the sweep ends, in half turns about <c>axis</c>.</summary>
    public QuantityValue? ToHalfTurns { get; init; }

    /// <summary>Cylinder: lower end along its axis.</summary>
    public QuantityValue? Lower { get; init; }

    /// <summary>Cylinder: upper end along its axis.</summary>
    public QuantityValue? Upper { get; init; }

    /// <summary>The potential held. The DC part when driven.</summary>
    public QuantityValue? Potential { get; init; }

    /// <summary>This electrode's share of the drive, zero to peak. Signed.</summary>
    public QuantityValue? DriveAmplitude { get; init; }

    /// <summary>
    /// Where in the cycle this electrode sits, as a fraction of one. Zero when
    /// omitted; a half is antiphase.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A fraction rather than radians or degrees, because every use of it is a
    /// fraction: a quadrupole pair is a half out, a three-phase guide is a third,
    /// and a travelling wave is a ramp from zero to one along its length.
    /// </para>
    /// <para>
    /// <b>An expression, like every other placement.</b> It was a plain number
    /// until a travelling-wave guide needed one, and that is precisely the case a
    /// ramp along the length exists for - a phase that cannot depend on the repeat
    /// index cannot ramp, so the device this field was documented for was the one
    /// device it could not express. Section 9 already says every placement is an
    /// expression; this was the one that had been missed.
    /// </para>
    /// </remarks>
    public QuantityValue? DrivePhase { get; init; }

    /// <summary>One term per generator this electrode is fed by.</summary>
    /// <remarks>
    /// The long form, for a structure driven by more than one generator - a trap whose
    /// ring carries the main drive while its endcaps carry a supplementary excitation,
    /// or a guide superposing a fast confining field on a slow travelling wave. The
    /// short <c>driveAmplitude</c> and <c>drivePhase</c> above stay for the common
    /// single-generator case, and declaring both is refused rather than merged: a
    /// document that says an electrode taps one generator and also three has no default
    /// to fall back on.
    /// </remarks>
    public IReadOnlyList<TapTermDocument>? Taps { get; init; }
}

/// <summary>A three-dimensional solved field, validated and reduced to SI.</summary>
public sealed record CompiledSolvedField3D
{
    /// <summary>Solve domain, in metres.</summary>
    public required double MinX { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MinY { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MinZ { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxX { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxY { get; init; }

    /// <summary>Solve domain, in metres.</summary>
    public required double MaxZ { get; init; }

    /// <summary>Node spacing, in metres.</summary>
    public required double CellSize { get; init; }

    /// <summary>Relative residual the solve must reach.</summary>
    public double Tolerance { get; init; } = 1e-9;

    /// <summary>The generators, empty when static.</summary>
    public IReadOnlyList<CompiledDrive> Drives { get; init; } = [];

    /// <summary>The primary drive - the first declared - or null when static.</summary>
    public CompiledDrive? Drive => Drives.Count > 0 ? Drives[0] : null;

    /// <summary>The timed sequence, or empty for one state.</summary>
    public IReadOnlyList<CompiledStage3D> Stages { get; init; } = [];

    /// <summary>The electrodes.</summary>
    public required IReadOnlyList<CompiledElectrode3D> Electrodes { get; init; }

    /// <summary>
    /// What each face of the domain is, in the order lower/upper x, y, z. Empty means all
    /// six are Dirichlet, which is a grounded box.
    /// </summary>
    /// <remarks>
    /// A grounded box is a third electrode - right for a device inside a housing, wrong for
    /// one whose geometry is invariant along an axis. See the document's own remarks.
    /// </remarks>
    public IReadOnlyList<BoundaryKind> Faces { get; init; } = [];

    /// <summary>Where to mirror the solved half, in metres along x, or null for none.</summary>
    public double? ReflectAboutX { get; init; }
}

/// <summary>One state of a timed sequence in three dimensions, validated.</summary>
/// <param name="Name">What the stage is for.</param>
/// <param name="DurationSeconds">How long it lasts.</param>
/// <param name="Electrodes">The electrodes as they stand during it.</param>
public sealed record CompiledStage3D(
    string Name, double DurationSeconds, IReadOnlyList<CompiledElectrode3D> Electrodes)
{
    /// <summary>
    /// The electrodes as they stand at the end of the stage when it ramps, or null when
    /// it holds; the same metal in the same places as <see cref="Electrodes"/>.
    /// </summary>
    public IReadOnlyList<CompiledElectrode3D>? EndElectrodes { get; init; }
}
