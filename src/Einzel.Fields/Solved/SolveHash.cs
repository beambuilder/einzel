using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Einzel.Core.Model;

namespace Einzel.Fields.Solved;

/// <summary>
/// Content hashes of what a solve is handed: the mesh and conductors of an element, and the
/// pattern each of its channels holds them at.
/// </summary>
/// <remarks>
/// <para>
/// <b>Over the compiled element, not the document.</b> Compilation has already evaluated
/// every parameter and expression to SI, so a comment, a renamed electrode, or a parameter
/// written another way that evaluates to the same double reaches this unchanged. The
/// whole-text <c>ModelHash</c> changes for all of them, which is right for "which model was
/// run" and wrong for "can an earlier solve be reused". Equal means the same bits, not the
/// same quantity: 1.1 mm becomes <c>1.1 * 1e-3</c>, which is not the double 0.0011 m parses
/// to, so the two are different keys - and, bit for bit, different solves.
/// </para>
/// <para>
/// <b>What is in.</b> The box, the cell size and the tolerance; the symmetry and edge
/// conditions in a plane, the six face conditions in a volume; every electrode in
/// declaration order, its shape and every geometric property it has. All of them, whatever
/// the shape: a disc's unused vertex list costs nothing to hash, and leaving out a property
/// some shape turns out to read would make two different solves share a key. Keying on too
/// much costs a cache miss; keying on too little hands back a wrong field.
/// </para>
/// <para>
/// <b>What is out, and why.</b> Names and labels, which the solver never reads. The mirror
/// plane, the extrusion tilt and whether the boundary is reported as discontinuous, which
/// are applied to a solved field afterwards (<see cref="GeometryBuilder.Build(CompiledSolvedField, ISolveStore)"/>,
/// <c>FieldAssembly</c>) and so let one solve serve several placements. Potentials, taps,
/// drives and stages, which reach the solver only as a channel's pattern and are hashed
/// there. Declaration order stays in: it decides how a pattern is normalised (the leading
/// electrode is held at one, <c>DriveChannels.Normalise</c>), so two orderings are two
/// decompositions.
/// </para>
/// <para>
/// <b>The canonical form is a byte stream, not JSON.</b> Each value is written in a fixed
/// order behind a versioned tag: counts before lists, enumerations by name, doubles as
/// their IEEE bits with negative zero folded onto zero. By name rather than by ordinal,
/// because a member inserted ahead of another would renumber it and turn an old key for one
/// shape into a key for a different one. Formatting a double as text would
/// make the key depend on a round-trip formatter, and a reflection-ordered serialiser on the
/// order properties happen to be declared in. The tag is the version: a change to what is
/// written, or how, changes the tag, so an old key can never match a new form.
/// </para>
/// <para>
/// Solver settings that are not model inputs - the cycle caps, the smoothing, the choice of
/// a Galerkin hierarchy, which the volume solver makes from the geometry - are not hashed.
/// They are what <c>SolverBehaviourVersion</c> versions, and a cache combines the two.
/// </para>
/// </remarks>
public static class SolveHash
{
    private const string PlaneTag = "einzel/solved2d-geometry/1";
    private const string VolumeTag = "einzel/solved3d-geometry/1";
    private const string ChannelTag = "einzel/solve-channel/1";

    /// <summary>The geometry hash of a two-dimensional element.</summary>
    /// <param name="solve">The compiled solve.</param>
    /// <returns><c>sha256:</c> and 64 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="solve"/> is null.</exception>
    public static string Geometry(CompiledSolvedField solve)
    {
        ArgumentNullException.ThrowIfNull(solve);

        using var stream = new Canonical(PlaneTag);

        stream.Real(solve.MinX);
        stream.Real(solve.MinY);
        stream.Real(solve.MaxX);
        stream.Real(solve.MaxY);
        stream.Real(solve.CellSize);
        stream.Real(solve.Tolerance);
        stream.Name(solve.Symmetry);
        stream.Name(solve.LeftEdge);
        stream.Name(solve.RightEdge);
        stream.Name(solve.BottomEdge);
        stream.Name(solve.TopEdge);

        stream.Integer(solve.Electrodes.Count);

        foreach (var electrode in solve.Electrodes)
        {
            stream.Name(electrode.Shape);
            stream.Real(electrode.MinX);
            stream.Real(electrode.MinY);
            stream.Real(electrode.MaxX);
            stream.Real(electrode.MaxY);
            stream.Real(electrode.CentreX);
            stream.Real(electrode.CentreY);
            stream.Real(electrode.Radius);
            stream.Name(electrode.Edge);

            // An edge profile's volts are in its profile, and a channel holds it at a scale;
            // the profile is therefore the shape of a boundary value, and part of the mesh.
            stream.Integer(electrode.Profile.Count);

            foreach (var (at, potential) in electrode.Profile)
            {
                stream.Real(at);
                stream.Real(potential);
            }

            stream.Integer(electrode.Vertices.Count);

            foreach (var (x, y) in electrode.Vertices)
            {
                stream.Real(x);
                stream.Real(y);
            }
        }

        return stream.Finish();
    }

    /// <summary>The geometry hash of a three-dimensional element.</summary>
    /// <param name="geometry">The geometry, as the volume solver is handed it.</param>
    /// <returns><c>sha256:</c> and 64 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="geometry"/> is null.</exception>
    public static string Geometry(Geometry3D geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        using var stream = new Canonical(VolumeTag);

        stream.Real(geometry.MinX);
        stream.Real(geometry.MinY);
        stream.Real(geometry.MinZ);
        stream.Real(geometry.MaxX);
        stream.Real(geometry.MaxY);
        stream.Real(geometry.MaxZ);
        stream.Real(geometry.CellSize);
        stream.Real(geometry.Tolerance);

        // No declared faces is six grounded ones, which is what the mask is left holding; the
        // two spellings are one solve and get one key.
        for (var face = 0; face < 6; face++)
        {
            stream.Name(geometry.Faces.Count == 6 ? geometry.Faces[face] : EdgeCondition.Dirichlet);
        }

        stream.Integer(geometry.Electrodes.Count);

        foreach (var electrode in geometry.Electrodes)
        {
            stream.Name(electrode.Shape);
            stream.Real(electrode.MinX);
            stream.Real(electrode.MinY);
            stream.Real(electrode.MinZ);
            stream.Real(electrode.MaxX);
            stream.Real(electrode.MaxY);
            stream.Real(electrode.MaxZ);
            stream.Real(electrode.CentreX);
            stream.Real(electrode.CentreY);
            stream.Real(electrode.CentreZ);
            stream.Real(electrode.Radius);
            stream.Name(electrode.Axis);

            stream.Integer(electrode.Vertices.Count);

            foreach (var (x, y) in electrode.Vertices)
            {
                stream.Real(x);
                stream.Real(y);
            }

            stream.Real(electrode.FromHalfTurns);
            stream.Real(electrode.ToHalfTurns);

            // A box's own tilt, which is part of its shape and cut into the mesh - unlike the
            // plane path's extrusion tilt, which rotates a solved field afterwards.
            stream.Name(electrode.TiltAxis);
            stream.Real(electrode.TiltHalfTurns);
            stream.Real(electrode.Lower);
            stream.Real(electrode.Upper);
        }

        return stream.Finish();
    }

    /// <summary>The hash of one channel: its element's geometry, and what each electrode held.</summary>
    /// <param name="geometryHash">The element's geometry hash.</param>
    /// <param name="holding">
    /// What each declared electrode held in the solve, in declaration order: the
    /// <c>Holding</c> of a channel solve.
    /// </param>
    /// <returns><c>sha256:</c> and 64 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public static string Channel(string geometryHash, IReadOnlyList<double> holding)
    {
        ArgumentNullException.ThrowIfNull(geometryHash);
        ArgumentNullException.ThrowIfNull(holding);

        using var stream = new Canonical(ChannelTag);

        stream.Text(geometryHash);
        stream.Integer(holding.Count);

        foreach (var value in holding)
        {
            stream.Real(value);
        }

        return stream.Finish();
    }

    /// <summary>The keys of every solved element of a model, without solving anything.</summary>
    /// <param name="model">The compiled model.</param>
    /// <returns>One key per solved element, in document order; empty when none is solved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <remarks>
    /// <para>
    /// <b>Before the solve, from the decomposition the solve itself makes.</b> A cache has to
    /// know the key before it can decide not to solve, so these cannot be read off the solved
    /// channels; they come from the same pattern list the builders solve
    /// (<see cref="GeometryBuilder.Holdings(CompiledSolvedField)"/>, <see cref="GeometryBuilder3D.Holdings(Geometry3D)"/>), and
    /// a test holds them equal to the keys of the channels that were actually solved.
    /// </para>
    /// <para>
    /// The decomposition is the cheap part of a build: it compares electrode excitations and
    /// touches no mesh.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<SolveKey> Of(CompiledModel model) => Of(model, null);

    /// <summary>The keys of every solved element of a model built under a plan, without solving anything.</summary>
    /// <param name="model">The compiled model.</param>
    /// <param name="plan">The plan the field is built under (<see cref="FieldBasisPlan"/>), or null for the default decomposition.</param>
    /// <returns>One key per solved element, in document order; empty when none is solved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The plan names an element the model does not solve, or an electrode an element does not declare.
    /// </exception>
    public static IReadOnlyList<SolveKey> Of(CompiledModel model, FieldBasisPlan? plan)
    {
        ArgumentNullException.ThrowIfNull(model);
        plan?.CheckElements(model);

        var keys = new List<SolveKey>();

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var element = model.Fields[index];

            if (element.Solve3D is { } volume)
            {
                var geometry = Geometry3D.Of(volume);
                keys.Add(Key(index, 3, Geometry(geometry), GeometryBuilder3D.Holdings(geometry, plan?.GroupsOf(index))));
            }
            else if (element.Solve is { } plane)
            {
                keys.Add(Key(index, 2, Geometry(plane), GeometryBuilder.Holdings(plane, plan?.GroupsOf(index))));
            }
        }

        return keys;
    }

    private static SolveKey Key(
        int index, int dimensions, string geometryHash, IReadOnlyList<IReadOnlyList<double>> holdings) =>
        new()
        {
            Field = index,
            Dimensions = dimensions,
            GeometryHash = geometryHash,
            ChannelHashes = [.. holdings.Select(holding => Channel(geometryHash, holding))],
        };

    /// <summary>The byte stream a hash is taken over.</summary>
    private sealed class Canonical : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private readonly byte[] _buffer = new byte[8];

        public Canonical(string tag) => Text(tag);

        public void Integer(int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(_buffer, value);
            _hash.AppendData(_buffer, 0, 4);
        }

        public void Real(double value)
        {
            // Negative zero and zero are the same potential and the same coordinate; their
            // bits differ, and an electrode at -0.0 mm is not a different solve.
            BinaryPrimitives.WriteInt64LittleEndian(
                _buffer, BitConverter.DoubleToInt64Bits(value == 0.0 ? 0.0 : value));
            _hash.AppendData(_buffer, 0, 8);
        }

        public void Name<T>(T value)
            where T : struct, Enum => Text(value.ToString());

        public void Text(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            Integer(bytes.Length);
            _hash.AppendData(bytes);
        }

        public string Finish() => "sha256:" + Convert.ToHexStringLower(_hash.GetHashAndReset());

        public void Dispose() => _hash.Dispose();
    }
}
