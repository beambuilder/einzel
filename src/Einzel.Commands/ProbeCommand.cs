using System.Globalization;
using System.Text;

using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Core.Results;
using Einzel.Fields;
using Einzel.Project;

namespace Einzel.Commands;

/// <summary>What a probe could say about one point.</summary>
public static class ProbeStatus
{
    /// <summary>An ordinary point in free space: the field there is the one a run flies through.</summary>
    public const string Ok = "ok";

    /// <summary>
    /// The point is inside a conductor. The potential reported is the field's own value
    /// there, which for a solved element is the electrode's potential, and the field is the
    /// interpolant's, which inside metal is not a physical field.
    /// </summary>
    public const string InsideConductor = "inside-conductor";

    /// <summary>
    /// The point lies outside the domain of every element (a reflected solve's mirror image and a
    /// tilted cross-section's rotated extent included). The value reported is what the field
    /// returns there - a solve's value outside its grid, nothing from a bounded analytic element -
    /// which is not a solved or declared value and need not be zero. This wins over
    /// <see cref="InsideConductor"/>: an electrode's declared shape can run on past the solve.
    /// </summary>
    public const string OutsideDomain = "outside-domain";

    /// <summary>The field declined to answer at this point (for example a singular axis).</summary>
    public const string Refused = "refused";
}

/// <summary>One probed point.</summary>
/// <param name="Index">Position in the request, from zero.</param>
/// <param name="PositionMm">Where, in millimetres (x, y, z).</param>
/// <param name="PotentialV">The potential in volts, or absent where the field refused.</param>
/// <param name="FieldVPerM">The electric field vector in volts per metre, or absent where refused.</param>
/// <param name="FieldMagnitudeVPerM">The field's magnitude in volts per metre, or absent where refused.</param>
/// <param name="Status">One of the <see cref="ProbeStatus"/> values.</param>
/// <param name="Conductor">The nearest conductor's name where the field knows conductors, else absent.</param>
/// <param name="ConductorDistanceMm">
/// Signed distance to the nearest conductor surface in millimetres, negative inside; absent where
/// the field declares no conductors.
/// </param>
/// <param name="OutsideElements">
/// Field elements whose declared domain does not contain this point, by element index. An element
/// outside its domain contributes nothing to the sum.
/// </param>
/// <param name="Note">Why a point was refused, or any other word the row needs.</param>
public sealed record ProbeSample(
    int Index,
    IReadOnlyList<double> PositionMm,
    double? PotentialV,
    IReadOnlyList<double>? FieldVPerM,
    double? FieldMagnitudeVPerM,
    string Status,
    string? Conductor,
    double? ConductorDistanceMm,
    IReadOnlyList<int> OutsideElements,
    string? Note);

/// <summary>What each field element is made of and how well it is known.</summary>
/// <param name="Index">Element index in the model.</param>
/// <param name="Kind">The element kind.</param>
/// <param name="Basis">
/// "analytic" - a closed form, exact to arithmetic - or "solved", a numerical solution on a grid.
/// </param>
/// <param name="CellMm">The solve's cell in millimetres, or absent for an analytic element.</param>
/// <param name="HasDomain">Whether an outside-domain check can be made for it.</param>
public sealed record ProbeElement(int Index, string Kind, string Basis, double? CellMm, bool HasDomain);

/// <summary>Where and when to probe.</summary>
/// <param name="FromMm">The point, or the start of the line, in millimetres.</param>
/// <param name="ToMm">The end of the line in millimetres, or absent for a single point.</param>
/// <param name="Samples">Points along a line, both ends included; ignored for a single point.</param>
/// <param name="AtUs">The instant, in microseconds, or absent when none was asked for.</param>
public sealed record ProbeRequest(Vec3 FromMm, Vec3? ToMm = null, int Samples = 2, double? AtUs = null);

/// <summary>The outcome of a probe.</summary>
public sealed record ProbeOutcome
{
    /// <summary>The model, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Whether the field varies with time (driven or sequenced).</summary>
    public required bool TimeVarying { get; init; }

    /// <summary>
    /// The instant every point was evaluated at, in microseconds, or absent for a static field. For
    /// a time-varying field with no instant asked for this is 0 and a warning says so.
    /// </summary>
    public double? InstantUs { get; init; }

    /// <summary>The field elements, with their basis.</summary>
    public required IReadOnlyList<ProbeElement> Elements { get; init; }

    /// <summary>The probed points, in request order.</summary>
    public required IReadOnlyList<ProbeSample> Samples { get; init; }

    /// <summary>What applies to every number here (GRD-2).</summary>
    public required IReadOnlyList<ValidityWarning> Warnings { get; init; }

    /// <summary>
    /// Why no uncertainty interval accompanies the values. A probed potential is an evaluation of a
    /// field already computed, not a measurement with its own ensemble: its quality is the solve's
    /// (the warnings, and each element's cell size), and inventing an interval here would state a
    /// precision nothing measured.
    /// </summary>
    public string Evidence { get; init; } =
        "evaluation of the assembled field, not a measurement: analytic elements are exact to "
        + "arithmetic; a solved element's error is its discretisation, stated by its cell size and "
        + "by the warnings, and no interval is claimed";
}

/// <summary>
/// The composite field a run flies through, sampled at points (ion-optics designers ask "what is the
/// potential, what is E, here").
/// </summary>
/// <remarks>
/// Built with <see cref="FieldAssembly.BuildReported"/>, the same assembly every run uses, so
/// superposed elements, regions, mirrors, tilts and the axisymmetric mapping are all honoured and
/// the assembly's warnings come along. A driven or sequenced field is read through its
/// time-varying interface at an explicit instant: reading it through the static one would answer
/// at t = 0 without saying so, which is a defect this project has met six times.
/// </remarks>
public sealed class FieldProbe
{
    // The model's own factor for a millimetre (UnitRegistry: "mm" is 1e-3 of a metre, applied
    // by multiplication). A face declared as 9 mm is the double 9 * 1e-3, which is not the
    // correctly rounded 0.009; converting a typed 9 mm the same way lands on that same double,
    // where a correctly rounded division would land an ulp inside or outside it. So the probe
    // converts exactly as the document does, and DomainMm hands out millimetres that come back
    // to the SI bound under this very multiplication.
    private const double MetresPerMm = 1e-3;

    private readonly IElectrostaticField _field;
    private readonly ITimeVaryingField? _driven;
    private readonly IConductorBounded? _bounded;
    private readonly CompiledModel _model;
    private readonly RotatedField?[] _tilts;
    private readonly bool _allBounded;

    private FieldProbe(
        CompiledModel model,
        IElectrostaticField field,
        IReadOnlyList<ValidityWarning> warnings,
        string modelPath,
        string modelHash)
    {
        _model = model;
        _field = field;
        _driven = field as ITimeVaryingField;
        _bounded = field as IConductorBounded;
        Warnings = warnings;
        ModelPath = modelPath;
        ModelHash = modelHash;

        // The rotation each tilted cross-section is evaluated through, built with the very
        // arguments FieldAssembly gives it, so "is this point in that solve" is asked of the
        // point the field itself asks the solve about.
        _tilts =
        [
            .. model.Fields.Select(e => e.Solve is { TiltHalfTurns: not 0.0 } plane
                ? new RotatedField(FieldFreeSpace.Instance, plane.TiltHalfTurns, plane.TiltCentreX, plane.TiltCentreZ)
                : null),
        ];

        Elements =
        [
            .. model.Fields.Select((e, i) => new ProbeElement(
                i,
                e.Kind.ToString(),
                e.Solve is not null || e.Solve3D is not null ? "solved" : "analytic",
                (e.Solve?.CellSize * 1e3) ?? (e.Solve3D?.CellSize * 1e3),
                CanCheckDomain(e))),
        ];

        _allBounded = Elements.Count > 0 && Elements.All(e => e.HasDomain);
    }

    /// <summary>The model, as an absolute path.</summary>
    public string ModelPath { get; }

    /// <summary>The content hash of the model text this probe was built from (PRJ-3).</summary>
    /// <remarks>
    /// Taken from the text that was validated and assembled, not from a second read of the file,
    /// so a header carrying it cannot name one revision of the model over another's field.
    /// </remarks>
    public string ModelHash { get; }

    /// <summary>What the assembly said about the field.</summary>
    public IReadOnlyList<ValidityWarning> Warnings { get; }

    /// <summary>The elements and how they are known.</summary>
    public IReadOnlyList<ProbeElement> Elements { get; }

    /// <summary>Whether the assembled field varies with time.</summary>
    public bool TimeVarying => _driven is not null;

    /// <summary>Validates and assembles the field of a model file.</summary>
    /// <param name="modelPath">The model.</param>
    /// <returns>A probe over its field.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is blank.</exception>
    /// <exception cref="EinzelException">The model does not validate.</exception>
    public static FieldProbe Open(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        return Open(absolute, File.ReadAllText(absolute));
    }

    /// <summary>Validates and assembles the field of model text already read.</summary>
    /// <param name="modelPath">Where the text came from; imported files resolve against its directory.</param>
    /// <param name="modelText">The model document.</param>
    /// <returns>A probe over its field, whose <see cref="ModelHash"/> is the hash of this text.</returns>
    /// <exception cref="ArgumentException">A argument is blank.</exception>
    /// <exception cref="EinzelException">The model does not validate.</exception>
    /// <remarks>
    /// For a caller that also records the model's hash: reading the file once and handing the
    /// text here means the hash and the field are of one revision (PRJ-3), however the file is
    /// edited meanwhile.
    /// </remarks>
    public static FieldProbe Open(string modelPath, string modelText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(modelText);

        var absolute = Path.GetFullPath(modelPath);
        var validation = ModelValidator.Validate(
            Io.ModelJson.Parse(modelText), null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var (field, warnings) = FieldAssembly.BuildReported(model);

        return new FieldProbe(model, field, warnings, absolute, ContentHash.OfText(modelText));
    }

    /// <summary>The box the model's bounded elements occupy, in millimetres, when there is one.</summary>
    /// <returns>Min and max corners, or null when no element has a domain.</returns>
    /// <remarks>
    /// <para>
    /// The union of what each element actually occupies: a solve's declared box together with
    /// its mirror image where it is reflected, a tilted cross-section by the bounding box of its
    /// rotated extent, and an analytic element's declared region. A cross-section's z and an
    /// axisymmetric solve's radius are not bounded the way a box is, so a cross-section spans z
    /// from minus to plus the larger of its |y| bounds and an axisymmetric solve is the box round
    /// its cylinder.
    /// </para>
    /// <para>
    /// The corners come back to the SI bounds exactly under the probe's own millimetre
    /// conversion, so a point taken from them is on the face and not an ulp beyond it.
    /// </para>
    /// </remarks>
    public (Vec3 Min, Vec3 Max)? DomainMm() =>
        DomainSi() is { } si ? (ToMm(si.Min, inward: +1), ToMm(si.Max, inward: -1)) : null;

    /// <summary>The same box as <see cref="DomainMm"/>, in metres, taken from the bounds themselves.</summary>
    /// <returns>Min and max corners in metres, or null when no element has a domain.</returns>
    public (Vec3 Min, Vec3 Max)? DomainSi()
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity, minZ = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity, maxZ = double.NegativeInfinity;
        var any = false;

        void Take(Vec3 a, Vec3 b)
        {
            any = true;
            minX = Math.Min(minX, a.X);
            maxX = Math.Max(maxX, b.X);
            minY = Math.Min(minY, a.Y);
            maxY = Math.Max(maxY, b.Y);
            minZ = Math.Min(minZ, a.Z);
            maxZ = Math.Max(maxZ, b.Z);
        }

        for (var index = 0; index < _model.Fields.Count; index++)
        {
            foreach (var (a, b) in Occupied(index))
            {
                Take(a, b);
            }
        }

        return any ? (new Vec3(minX, minY, minZ), new Vec3(maxX, maxY, maxZ)) : null;
    }

    /// <summary>Evaluates the field at a point.</summary>
    /// <param name="index">The row number.</param>
    /// <param name="positionMm">Where, in millimetres.</param>
    /// <param name="atSeconds">The instant, used only by a time-varying field.</param>
    /// <returns>The sample; a refused point is a row with status refused, never an exception.</returns>
    public ProbeSample At(int index, Vec3 positionMm, double atSeconds)
    {
        var point = new Vec3(positionMm.X * MetresPerMm, positionMm.Y * MetresPerMm, positionMm.Z * MetresPerMm);
        var position = new[] { positionMm.X, positionMm.Y, positionMm.Z };

        var outside = new List<int>();

        for (var i = 0; i < _model.Fields.Count; i++)
        {
            if (Elements[i].HasDomain && !InsideDomain(i, in point))
            {
                outside.Add(i);
            }
        }

        var status = SampleCore(in point, atSeconds, outside.Count, out var potential, out var field, out var distance, out var note);

        if (status == ProbeStatus.Refused)
        {
            return new ProbeSample(index, position, null, null, null, status, null, null, outside, note);
        }

        string? conductor = distance is not null ? _bounded!.ConductorAt(in point) : null;
        var magnitude = Math.Sqrt((field.X * field.X) + (field.Y * field.Y) + (field.Z * field.Z));

        return new ProbeSample(
            index, position, potential, [field.X, field.Y, field.Z], magnitude, status,
            conductor, distance * 1e3, outside, null);
    }

    /// <summary>
    /// Evaluates the field at a point given in metres, with no allocation: the path for sampling
    /// a whole grid, where only the potential, the field and the status are wanted.
    /// </summary>
    /// <param name="pointMetres">Where, in metres.</param>
    /// <param name="atSeconds">The instant, used only by a time-varying field.</param>
    /// <param name="potential">The potential in volts; NaN where the point was refused.</param>
    /// <param name="field">The electric field in volts per metre; NaN components where refused.</param>
    /// <returns>One of the <see cref="ProbeStatus"/> values, the same one <see cref="At"/> gives.</returns>
    public string Sample(in Vec3 pointMetres, double atSeconds, out double potential, out Vec3 field)
    {
        var outsideCount = 0;

        for (var i = 0; i < _model.Fields.Count; i++)
        {
            if (Elements[i].HasDomain && !InsideDomain(i, in pointMetres))
            {
                outsideCount++;
            }
        }

        var status = SampleCore(in pointMetres, atSeconds, outsideCount, out potential, out field, out _, out _);

        if (status == ProbeStatus.Refused)
        {
            potential = double.NaN;
            field = new Vec3(double.NaN, double.NaN, double.NaN);
        }

        return status;
    }

    private string SampleCore(
        in Vec3 point,
        double atSeconds,
        int outsideCount,
        out double potential,
        out Vec3 field,
        out double? distance,
        out string? note)
    {
        distance = null;
        note = null;

        try
        {
            if (_driven is not null)
            {
                potential = _driven.PotentialAt(in point, atSeconds);
                field = _driven.ElectricFieldAt(in point, atSeconds);
            }
            else
            {
                potential = _field.PotentialAt(in point);
                field = _field.ElectricFieldAt(in point);
            }
        }
        catch (PointRefusedException refusal)
        {
            // A field may refuse a point rather than return a huge number there (the
            // quadro-logarithmic one does on its singular axis). That is an answer about this
            // point, and a profile crossing it must still report every other row. Only that
            // refusal: any other argument exception is a defect or a malformed request, and
            // turning it into a row would report a million refused points with exit 0.
            potential = 0.0;
            field = Vec3.Zero;
            note = refusal.Message;
            return ProbeStatus.Refused;
        }

        if (_bounded is not null)
        {
            var d = _bounded.SignedDistanceToConductor(in point);

            if (double.IsFinite(d))
            {
                distance = d;
            }
        }

        // Outside every domain wins: an electrode's declared shape runs on along a
        // cross-section's invariant axis and past the edge of its solve, so a point out there can
        // be "inside" a shape nothing was solved for, and the value reported is the field's
        // answer outside its domain, not the electrode's potential. Outside every element only
        // when every element is one whose domain can be checked: an unbounded analytic element
        // fills all space, so the point is never outside it.
        var status = _allBounded && outsideCount == Elements.Count
            ? ProbeStatus.OutsideDomain
            : distance is < 0.0
                ? ProbeStatus.InsideConductor
                : ProbeStatus.Ok;

        // Adding zero turns a negative zero (the sign of a zero component of -grad phi) into a plain
        // one, so a table does not print -0 for a component that is simply zero.
        potential += 0.0;
        field = new Vec3(field.X + 0.0, field.Y + 0.0, field.Z + 0.0);

        return status;
    }

    /// <summary>The warning a time-varying field earns when no instant was asked for.</summary>
    /// <param name="requested">The instant asked for, in microseconds, or absent.</param>
    /// <returns>The warnings to add: empty for a static field or an explicit instant.</returns>
    public IReadOnlyList<ValidityWarning> InstantWarnings(double? requested) =>
        TimeVarying && requested is null
            ?
            [
                new ValidityWarning(
                    "probe.field-at-instant",
                    "this field varies with time (it is driven or sequenced) and no --at-us was "
                    + "given, so every value is the field at t = 0 us and not a field the "
                    + "instrument holds; give --at-us to choose the instant",
                    WarningSeverity.Provenance),
            ]
            : [];

    /// <summary>Converts a point typed in millimetres to metres the way the model's own lengths are.</summary>
    /// <param name="mm">The point in millimetres.</param>
    /// <returns>The point in metres.</returns>
    public static Vec3 MillimetresToMetres(Vec3 mm) =>
        new(mm.X * MetresPerMm, mm.Y * MetresPerMm, mm.Z * MetresPerMm);

    private static bool CanCheckDomain(CompiledField element) =>
        element.Region is not null || element.Solve is not null || element.Solve3D is not null;

    // Millimetres that come back to exactly `si` under * MetresPerMm, or failing that the nearest
    // value on the inside of the face (`inward` is +1 for a lower corner, -1 for an upper one), so
    // a box taken from DomainMm never reaches past the domain it was taken from.
    private static Vec3 ToMm(Vec3 si, int inward) =>
        new(ToMm(si.X, inward), ToMm(si.Y, inward), ToMm(si.Z, inward));

    private static double ToMm(double si, int inward)
    {
        if (!double.IsFinite(si))
        {
            return si * 1e3;
        }

        var mm = si * 1e3;

        for (var step = 0; step < 8 && mm * MetresPerMm != si; step++)
        {
            mm = mm * MetresPerMm < si ? double.BitIncrement(mm) : double.BitDecrement(mm);
        }

        if (mm * MetresPerMm == si)
        {
            return mm;
        }

        // No millimetre value lands on the face exactly; take the nearest one inside it.
        while (inward > 0 ? mm * MetresPerMm < si : mm * MetresPerMm > si)
        {
            mm = inward > 0 ? double.BitIncrement(mm) : double.BitDecrement(mm);
        }

        return mm;
    }

    // The boxes element `index` occupies in the world, in metres: none for an unbounded analytic
    // element, one for a solve or a region, two for a reflected solve.
    private IEnumerable<(Vec3 Min, Vec3 Max)> Occupied(int index)
    {
        var element = _model.Fields[index];

        if (element.Region is { } region)
        {
            yield return (new Vec3(region.MinX, region.MinY, region.MinZ), new Vec3(region.MaxX, region.MaxY, region.MaxZ));
        }
        else if (element.Solve3D is { } v)
        {
            yield return (new Vec3(v.MinX, v.MinY, v.MinZ), new Vec3(v.MaxX, v.MaxY, v.MaxZ));

            if (v.ReflectAboutX is { } p)
            {
                yield return (new Vec3((2.0 * p) - v.MaxX, v.MinY, v.MinZ), new Vec3((2.0 * p) - v.MinX, v.MaxY, v.MaxZ));
            }
        }
        else if (element.Solve is { } plane)
        {
            var half = Math.Max(Math.Abs(plane.MinY), Math.Abs(plane.MaxY));
            var (y0, y1, z0, z1) = plane.Symmetry == SolveSymmetry.Cylindrical
                ? (-plane.MaxY, plane.MaxY, -plane.MaxY, plane.MaxY)
                : (plane.MinY, plane.MaxY, -half, half);

            var boxes = new List<(double X0, double X1)> { (plane.MinX, plane.MaxX) };

            if (plane.ReflectAboutX is { } p)
            {
                boxes.Add(((2.0 * p) - plane.MaxX, (2.0 * p) - plane.MinX));
            }

            foreach (var (x0, x1) in boxes)
            {
                if (_tilts[index] is not { } tilt)
                {
                    yield return (new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
                    continue;
                }

                // The bounding box of the rotated extent: the rotation is about an axis along y,
                // so the four x-z corners carry it.
                double ax = double.PositiveInfinity, az = double.PositiveInfinity;
                double bx = double.NegativeInfinity, bz = double.NegativeInfinity;

                foreach (var (cx, cz) in (ReadOnlySpan<(double, double)>)[(x0, z0), (x0, z1), (x1, z0), (x1, z1)])
                {
                    var world = tilt.FromLocal(new Vec3(cx, 0.0, cz));
                    ax = Math.Min(ax, world.X);
                    bx = Math.Max(bx, world.X);
                    az = Math.Min(az, world.Z);
                    bz = Math.Max(bz, world.Z);
                }

                yield return (new Vec3(ax, y0, az), new Vec3(bx, y1, bz));
            }
        }
    }

    // Whether element `index` has a solved or declared value at p. A reflected solve holds the
    // point if it or its mirror image is in the declared half; a tilted cross-section is asked
    // about the point in its own unrotated frame. Faces are inside: a face node holds a solved
    // value.
    private bool InsideDomain(int index, in Vec3 p)
    {
        var element = _model.Fields[index];

        if (element.Region is { } region)
        {
            return !(region.SignedDistance(in p) > 0.0);
        }

        if (element.Solve is { } plane)
        {
            var q = _tilts[index] is { } tilt ? tilt.ToLocal(in p) : p;

            return InPlane(plane, q.X, q.Y, q.Z)
                || (plane.ReflectAboutX is { } mirror && InPlane(plane, (2.0 * mirror) - q.X, q.Y, q.Z));
        }

        if (element.Solve3D is { } v)
        {
            return InVolume(v, p.X, p.Y, p.Z)
                || (v.ReflectAboutX is { } mirror && InVolume(v, (2.0 * mirror) - p.X, p.Y, p.Z));
        }

        return true;
    }

    private static bool InPlane(CompiledSolvedField plane, double x, double y, double z)
    {
        if (x < plane.MinX || x > plane.MaxX)
        {
            return false;
        }

        // Cylindrical: x is the axis, y the radius, so the second coordinate is the distance from
        // the axis in the y-z plane; a cross-section keeps y and ignores z.
        var second = plane.Symmetry == SolveSymmetry.Cylindrical ? Math.Sqrt((y * y) + (z * z)) : y;

        return second >= plane.MinY && second <= plane.MaxY;
    }

    private static bool InVolume(CompiledSolvedField3D v, double x, double y, double z) =>
        x >= v.MinX && x <= v.MaxX && y >= v.MinY && y <= v.MaxY && z >= v.MinZ && z <= v.MaxZ;
}

/// <summary>Reads the potential and electric field of a model at a point or along a line.</summary>
public static class ProbeCommand
{
    /// <summary>The most samples one line may ask for.</summary>
    public const int MaximumSamples = 1_000_000;

    /// <summary>Probes a model.</summary>
    /// <param name="modelPath">The model.</param>
    /// <param name="request">Where and when.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="EinzelException">The model does not validate, or the request is malformed.</exception>
    public static ProbeOutcome Execute(string modelPath, ProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Check(request);

        var probe = FieldProbe.Open(modelPath);
        var at = (request.AtUs ?? 0.0) * 1e-6;
        var samples = new List<ProbeSample>();

        if (request.ToMm is not { } to)
        {
            samples.Add(probe.At(0, request.FromMm, at));
        }
        else
        {
            var from = request.FromMm;
            var last = request.Samples - 1;

            for (var i = 0; i < request.Samples; i++)
            {
                // Ends returned exactly: an interpolated end lands an ulp off and a profile whose
                // last row is not the point asked for cannot be compared with another tool's.
                var position = i == 0
                    ? from
                    : i == last
                        ? to
                        : Lerp(from, to, (double)i / last);

                samples.Add(probe.At(i, position, at));
            }
        }

        return new ProbeOutcome
        {
            ModelPath = probe.ModelPath,
            TimeVarying = probe.TimeVarying,
            InstantUs = probe.TimeVarying ? request.AtUs ?? 0.0 : null,
            Elements = probe.Elements,
            Samples = samples,
            Warnings = [.. probe.Warnings, .. probe.InstantWarnings(request.AtUs), .. SampleWarnings(samples)],
        };
    }

    /// <summary>The profile as CSV, one row per point, with the unit in each column's name.</summary>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The CSV text.</returns>
    public static string ToCsv(ProbeOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();

        if (outcome.InstantUs is { } instant)
        {
            text.Append(invariant, $"# field at t = {instant:G17} us\n");
        }

        foreach (var warning in outcome.Warnings)
        {
            text.Append(invariant, $"# {warning.Severity}: {warning.Code}: {warning.Message}\n");
        }

        text.Append("x_mm,y_mm,z_mm,potential_V,Ex_V_per_m,Ey_V_per_m,Ez_V_per_m,status,conductor\n");

        foreach (var s in outcome.Samples)
        {
            static string Cell(double? value) =>
                value is { } v ? v.ToString("G17", CultureInfo.InvariantCulture) : string.Empty;

            text.Append(
                invariant,
                $"{s.PositionMm[0]:G17},{s.PositionMm[1]:G17},{s.PositionMm[2]:G17},{Cell(s.PotentialV)},"
                + $"{Cell(s.FieldVPerM?[0])},{Cell(s.FieldVPerM?[1])},{Cell(s.FieldVPerM?[2])},"
                + $"{s.Status},{s.Conductor}\n");
        }

        return text.ToString();
    }

    private static IEnumerable<ValidityWarning> SampleWarnings(List<ProbeSample> samples)
    {
        // Said once, with a count: a row's status carries the detail, and a thousand-row profile
        // should not repeat one sentence a thousand times.
        var inside = samples.Count(s => s.Status == ProbeStatus.InsideConductor);
        var outside = samples.Count(s => s.Status == ProbeStatus.OutsideDomain);
        var refused = samples.Count(s => s.Status == ProbeStatus.Refused);

        if (inside > 0)
        {
            yield return new ValidityWarning(
                "probe.inside-conductor",
                $"{inside} of {samples.Count} point(s) are inside a conductor: the potential there is "
                + "the electrode's and the field is the interpolant's, which is not a physical field "
                + "inside metal",
                WarningSeverity.Qualified);
        }

        if (outside > 0)
        {
            yield return new ValidityWarning(
                "probe.outside-domain",
                $"{outside} of {samples.Count} point(s) lie outside every element's domain: the "
                + "value reported there is what the field returns outside what was solved or "
                + "declared (not necessarily zero), not a solved value and not a measurement of vacuum",
                WarningSeverity.Qualified);
        }

        if (refused > 0)
        {
            yield return new ValidityWarning(
                "probe.refused",
                $"{refused} of {samples.Count} point(s) were refused by the field; the row's note says why",
                WarningSeverity.Qualified);
        }
    }

    private static Vec3 Lerp(Vec3 a, Vec3 b, double f) =>
        new(a.X + ((b.X - a.X) * f), a.Y + ((b.Y - a.Y) * f), a.Z + ((b.Z - a.Z) * f));

    private static void Check(ProbeRequest request)
    {
        static bool Finite(Vec3 v) =>
            double.IsFinite(v.X) && double.IsFinite(v.Y) && double.IsFinite(v.Z);

        if (!Finite(request.FromMm) || (request.ToMm is { } t && !Finite(t)))
        {
            throw Refusal("/probe", "coordinates must be finite numbers in millimetres");
        }

        CheckInstant(request.AtUs);

        if (request.ToMm is not null
            && (request.Samples < 2 || request.Samples > MaximumSamples))
        {
            throw Refusal(
                "/probe/samples",
                $"a line needs between 2 and {MaximumSamples} samples, both ends included; got {request.Samples}");
        }
    }

    /// <summary>Refuses an instant no field could be asked about, once, before anything is sampled.</summary>
    /// <param name="atUs">The instant in microseconds, or absent.</param>
    /// <exception cref="EinzelException">The instant is not finite, or is before the run's clock starts.</exception>
    /// <remarks>
    /// Checked up front so that a bad instant is one AGT-3 error naming the flag, rather than an
    /// exception raised inside the field stack at every one of a million points. Negative is
    /// refused because a run's clock starts at zero: a sequenced field has no phase before it,
    /// and an answer there would be the first phase's under a time the instrument never reaches.
    /// </remarks>
    internal static void CheckInstant(double? atUs)
    {
        if (atUs is { } instant && (!double.IsFinite(instant) || instant < 0.0))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/probe/at-us",
                Constraint = "--at-us must be a finite, non-negative number of microseconds; a run's clock starts at 0",
                Observed = new ObservedValue(instant, "us"),
                Suggestion = "give --at-us 0 or a later instant, in microseconds",
            });
        }
    }

    private static EinzelException Refusal(string path, string constraint) =>
        new(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = path,
            Constraint = constraint,
            Suggestion = "see 'einzel probe' for the flags",
        });
}
