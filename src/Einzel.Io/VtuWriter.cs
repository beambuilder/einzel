using System.Globalization;
using System.Text;
using Einzel.Fields.Solved;
using Einzel.Transport.Integration;

namespace Einzel.Io;

/// <summary>
/// Writes trajectories as VTK unstructured grid (.vtu) files.
/// </summary>
/// <remarks>
/// <para>
/// RND-12 and the scope decision in spec section 17: fields, trajectories, and
/// density clouds export to VTU, and ParaView does the rest. "One writer buys an
/// entire visualization application" — and it lands in Phase 1, before any GUI
/// exists, which is what gives the CLI a complete visualisation story a year
/// before the shell.
/// </para>
/// <para>
/// The XML form is written directly rather than through VTK. Spec section 20
/// records the intent to own this: "VTK's XML formats are documented and simple",
/// and writing it removes a dependency for about a week of work. This is the
/// ASCII variant, which is larger than the appended-binary form but diffable and
/// openable in a text editor — the same argument RND-6 makes for keeping text as
/// text in vector figures.
/// </para>
/// <para>
/// GRD-12: a rendering never looks more precise than its data. The provenance
/// comment block at the head of every file records the engine version, the model
/// hash, and the sampling that produced it, so a file that outlives the
/// conversation still says where it came from.
/// </para>
/// </remarks>
public static class VtuWriter
{
    /// <summary>Writes a trajectory as a single polyline.</summary>
    /// <param name="samples">The sampled trajectory, in flight order.</param>
    /// <param name="provenance">Provenance lines recorded as an XML comment.</param>
    /// <returns>The .vtu document text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="samples"/> is null.</exception>
    /// <exception cref="ArgumentException">The trajectory has fewer than two points.</exception>
    public static string WriteTrajectory(
        IReadOnlyList<TrajectorySample> samples,
        IReadOnlyList<string>? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (samples.Count < 2)
        {
            throw new ArgumentException(
                "a trajectory needs at least two points to form a polyline", nameof(samples));
        }

        var text = new StringBuilder(samples.Count * 96);
        var invariant = CultureInfo.InvariantCulture;

        text.Append("<?xml version=\"1.0\"?>\n");

        if (provenance is { Count: > 0 })
        {
            text.Append("<!--\n");

            foreach (var line in provenance)
            {
                // "--" would close the comment early; the only sanitising needed.
                text.Append("  ").Append(line.Replace("--", "- -", StringComparison.Ordinal)).Append('\n');
            }

            text.Append("-->\n");
        }

        text.Append("<VTKFile type=\"UnstructuredGrid\" version=\"1.0\" byte_order=\"LittleEndian\">\n");
        text.Append("  <UnstructuredGrid>\n");
        text.Append(invariant, $"    <Piece NumberOfPoints=\"{samples.Count}\" NumberOfCells=\"1\">\n");

        text.Append("      <Points>\n");
        text.Append("        <DataArray type=\"Float64\" NumberOfComponents=\"3\" format=\"ascii\">\n");

        foreach (var sample in samples)
        {
            text.Append(invariant, $"          {sample.Position.X:G17} {sample.Position.Y:G17} {sample.Position.Z:G17}\n");
        }

        text.Append("        </DataArray>\n");
        text.Append("      </Points>\n");

        text.Append("      <PointData Scalars=\"time\" Vectors=\"velocity\">\n");

        text.Append("        <DataArray type=\"Float64\" Name=\"time\" format=\"ascii\">\n");

        foreach (var sample in samples)
        {
            text.Append(invariant, $"          {sample.TimeSeconds:G17}\n");
        }

        text.Append("        </DataArray>\n");

        text.Append("        <DataArray type=\"Float64\" Name=\"speed\" format=\"ascii\">\n");

        foreach (var sample in samples)
        {
            text.Append(invariant, $"          {sample.Velocity.Length:G17}\n");
        }

        text.Append("        </DataArray>\n");

        text.Append("        <DataArray type=\"Float64\" Name=\"velocity\" NumberOfComponents=\"3\" format=\"ascii\">\n");

        foreach (var sample in samples)
        {
            text.Append(invariant, $"          {sample.Velocity.X:G17} {sample.Velocity.Y:G17} {sample.Velocity.Z:G17}\n");
        }

        text.Append("        </DataArray>\n");
        text.Append("      </PointData>\n");

        // One VTK_POLY_LINE cell (type 4) threading every point in flight order.
        text.Append("      <Cells>\n");
        text.Append("        <DataArray type=\"Int64\" Name=\"connectivity\" format=\"ascii\">\n          ");

        for (var i = 0; i < samples.Count; i++)
        {
            text.Append(invariant, $"{i} ");
        }

        text.Append("\n        </DataArray>\n");
        text.Append(invariant, $"        <DataArray type=\"Int64\" Name=\"offsets\" format=\"ascii\">\n          {samples.Count}\n        </DataArray>\n");
        text.Append("        <DataArray type=\"UInt8\" Name=\"types\" format=\"ascii\">\n          4\n        </DataArray>\n");
        text.Append("      </Cells>\n");

        text.Append("    </Piece>\n");
        text.Append("  </UnstructuredGrid>\n");
        text.Append("</VTKFile>\n");

        return text.ToString();
    }

    /// <summary>Writes many trajectories as one polyline cell each, with the ion's id and outcome.</summary>
    /// <param name="trajectories">
    /// Per ion its id, an integer outcome code and its samples; an ion with fewer than two
    /// samples has no line and is left out.
    /// </param>
    /// <param name="provenance">Provenance lines recorded as an XML comment.</param>
    /// <returns>The .vtu document text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="trajectories"/> is null.</exception>
    /// <remarks>
    /// One file for a whole list, so ParaView colours a beam by cell data rather than
    /// opening a file per ion. Point data is <c>time_us</c>, each ion's own flight time.
    /// </remarks>
    public static string WriteTrajectories(
        IReadOnlyList<(int Id, int Outcome, IReadOnlyList<TrajectorySample> Samples)> trajectories,
        IReadOnlyList<string>? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(trajectories);

        var lines = trajectories.Where(t => t.Samples.Count >= 2).ToList();
        var points = lines.Sum(t => t.Samples.Count);
        var text = new StringBuilder((points * 64) + 1024);
        var invariant = CultureInfo.InvariantCulture;

        text.Append("<?xml version=\"1.0\"?>\n");

        if (provenance is { Count: > 0 })
        {
            text.Append("<!--\n");

            foreach (var line in provenance)
            {
                text.Append("  ").Append(line.Replace("--", "- -", StringComparison.Ordinal)).Append('\n');
            }

            text.Append("-->\n");
        }

        text.Append("<VTKFile type=\"UnstructuredGrid\" version=\"1.0\" byte_order=\"LittleEndian\">\n  <UnstructuredGrid>\n");
        text.Append(invariant, $"    <Piece NumberOfPoints=\"{points}\" NumberOfCells=\"{lines.Count}\">\n");
        text.Append("      <Points>\n        <DataArray type=\"Float64\" NumberOfComponents=\"3\" format=\"ascii\">\n");

        foreach (var sample in lines.SelectMany(t => t.Samples))
        {
            text.Append(invariant, $"          {sample.Position.X:G17} {sample.Position.Y:G17} {sample.Position.Z:G17}\n");
        }

        text.Append("        </DataArray>\n      </Points>\n      <PointData Scalars=\"time_us\">\n");
        text.Append("        <DataArray type=\"Float64\" Name=\"time_us\" format=\"ascii\">\n");

        foreach (var sample in lines.SelectMany(t => t.Samples))
        {
            text.Append(invariant, $"          {sample.TimeSeconds * 1e6:G17}\n");
        }

        text.Append("        </DataArray>\n      </PointData>\n      <CellData Scalars=\"id\">\n");
        text.Append("        <DataArray type=\"Int32\" Name=\"id\" format=\"ascii\">\n          ");
        text.AppendJoin(' ', lines.Select(t => t.Id.ToString(invariant)));
        text.Append("\n        </DataArray>\n        <DataArray type=\"Int32\" Name=\"outcome\" format=\"ascii\">\n          ");
        text.AppendJoin(' ', lines.Select(t => t.Outcome.ToString(invariant)));
        text.Append("\n        </DataArray>\n      </CellData>\n");

        // One VTK_POLY_LINE cell (type 4) per ion, threading its own points in flight order.
        text.Append("      <Cells>\n        <DataArray type=\"Int64\" Name=\"connectivity\" format=\"ascii\">\n          ");
        text.AppendJoin(' ', Enumerable.Range(0, points).Select(i => i.ToString(invariant)));
        text.Append("\n        </DataArray>\n        <DataArray type=\"Int64\" Name=\"offsets\" format=\"ascii\">\n          ");
        var end = 0;
        text.AppendJoin(' ', lines.Select(t => (end += t.Samples.Count).ToString(invariant)));
        text.Append("\n        </DataArray>\n        <DataArray type=\"UInt8\" Name=\"types\" format=\"ascii\">\n          ");
        text.AppendJoin(' ', lines.Select(_ => "4"));
        text.Append("\n        </DataArray>\n      </Cells>\n    </Piece>\n  </UnstructuredGrid>\n</VTKFile>\n");

        return text.ToString();
    }

    /// <summary>Writes a solved scalar field as VTK ImageData.</summary>
    /// <param name="field">The field, on its grid.</param>
    /// <param name="name">The name the array takes in ParaView.</param>
    /// <param name="provenance">Provenance lines recorded as an XML comment.</param>
    /// <returns>The .vti document text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <remarks>
    /// <para>
    /// ImageData rather than an unstructured grid, because that is what a uniform
    /// Cartesian grid is. An unstructured grid would carry every node coordinate
    /// and every cell's connectivity explicitly - several times the bytes to say
    /// what an origin, two spacings, and an extent already say - and it would give
    /// up ParaView's structured-grid paths for slicing and contouring.
    /// </para>
    /// <para>
    /// The two spacings are written separately. A grid meshes its declared domain
    /// exactly and its cells need not be square, so assuming one spacing here
    /// would stretch the picture relative to the geometry it was solved on.
    /// </para>
    /// </remarks>

    public static string WriteScalarField(
        ScalarField2D field, string name, IReadOnlyList<string>? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(field);

        return WritePlane(field.Grid, name, provenance, (i, j) => field[i, j]);
    }

    /// <summary>Writes an ion density as VTK ImageData.</summary>
    /// <param name="density">The density, on its grid.</param>
    /// <param name="name">The name the array takes in ParaView.</param>
    /// <param name="provenance">Provenance lines recorded as an XML comment.</param>
    /// <returns>The .vti document text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="density"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <remarks>
    /// <para>
    /// TRN-2 makes a density the output of diffusive transport the way a trajectory
    /// is the output of integration, and RND-8 forbids drawing lines through one.
    /// Until this existed the prohibition had nothing on the other side of it: the
    /// mode's principal result could not be looked at in any form, only summarised
    /// into a transmission and a transit time. Section 21's argument for VTU in
    /// Phase 1 - that ParaView supplies the whole visualisation story before any
    /// shell exists - applies to a density at least as strongly as to a field.
    /// </para>
    /// <para>
    /// The same ImageData a potential is written as, because it is the same grid.
    /// What it is <em>not</em> is a scalar field on the solver's own type: a density
    /// is per unit volume, and in a cylindrical solve the volume of a cell grows
    /// with radius, so the array here is a density and not a count. Contouring it
    /// gives surfaces of equal concentration, which is what a reader of a funnel
    /// picture wants; integrating it needs the cell volumes and is why the ion
    /// totals are reported as numbers rather than left to be measured off a picture.
    /// </para>
    /// </remarks>
    public static string WriteDensityField(
        Transport.Diffusion.DensityField density,
        string name,
        IReadOnlyList<string>? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(density);

        return WritePlane(density.Grid, name, provenance, (i, j) => density[i, j]);
    }

    /// <summary>Writes one scalar over a plane grid as VTK ImageData.</summary>
    private static string WritePlane(
        Grid2D grid, string name, IReadOnlyList<string>? provenance, Func<int, int, double> at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder(grid.NodeCount * 24);

        text.Append("<?xml version=\"1.0\"?>\n");

        if (provenance is { Count: > 0 })
        {
            text.Append("<!--\n");

            foreach (var line in provenance)
            {
                text.Append("  ").Append(line.Replace("--", "- -", StringComparison.Ordinal)).Append('\n');
            }

            text.Append("-->\n");
        }

        var extent = $"0 {grid.CountX - 1} 0 {grid.CountY - 1} 0 0";

        text.Append("<VTKFile type=\"ImageData\" version=\"1.0\" byte_order=\"LittleEndian\">\n");
        text.Append(
            invariant,
            $"  <ImageData WholeExtent=\"{extent}\" Origin=\"{grid.OriginX:G17} {grid.OriginY:G17} 0\" Spacing=\"{grid.SpacingX:G17} {grid.SpacingY:G17} 1\">\n");
        text.Append(invariant, $"    <Piece Extent=\"{extent}\">\n");
        text.Append(invariant, $"      <PointData Scalars=\"{name}\">\n");
        text.Append(invariant, $"        <DataArray type=\"Float64\" Name=\"{name}\" format=\"ascii\">\n");

        // Row-major, x fastest: the order VTK reads an extent in, and the order
        // the field already stores its nodes.
        for (var j = 0; j < grid.CountY; j++)
        {
            text.Append("          ");

            for (var i = 0; i < grid.CountX; i++)
            {
                text.Append(invariant, $"{at(i, j):G17} ");
            }

            text.Append('\n');
        }

        text.Append("        </DataArray>\n");
        text.Append("      </PointData>\n");
        text.Append("      <CellData/>\n");
        text.Append("    </Piece>\n");
        text.Append("  </ImageData>\n");
        text.Append("</VTKFile>\n");

        return text.ToString();
    }

    /// <summary>Writes a solved three-dimensional field as VTK ImageData.</summary>
    /// <param name="field">The potential, on its grid.</param>
    /// <param name="name">The name the array takes in ParaView.</param>
    /// <param name="provenance">Provenance lines recorded as an XML comment.</param>
    /// <returns>The .vti document text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="field"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <remarks>
    /// The same format the plane writes, with the third extent actually spanning
    /// something. A volume is where ParaView earns its place: a section through a
    /// solved quadrupole is a thing you look at once, and a volume is a thing you
    /// cut, contour and re-cut - which is most of why VTU export lands a phase
    /// before any shell does.
    /// </remarks>
    public static string WriteScalarField(
        ScalarField3D field, string name, IReadOnlyList<string>? provenance = null)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var grid = field.Grid;
        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder((int)Math.Min(grid.NodeCount * 24L, 1 << 28));

        text.Append("<?xml version=\"1.0\"?>\n");

        if (provenance is { Count: > 0 })
        {
            text.Append("<!--\n");

            foreach (var line in provenance)
            {
                text.Append("  ").Append(line.Replace("--", "- -", StringComparison.Ordinal)).Append('\n');
            }

            text.Append("-->\n");
        }

        var extent = $"0 {grid.CountX - 1} 0 {grid.CountY - 1} 0 {grid.CountZ - 1}";

        var origin = $"{grid.OriginX:G17} {grid.OriginY:G17} {grid.OriginZ:G17}";
        var spacing = $"{grid.SpacingX:G17} {grid.SpacingY:G17} {grid.SpacingZ:G17}";

        text.Append("<VTKFile type=\"ImageData\" version=\"1.0\" byte_order=\"LittleEndian\">\n");
        text.Append(
            invariant,
            $"  <ImageData WholeExtent=\"{extent}\" Origin=\"{origin}\" Spacing=\"{spacing}\">\n");
        text.Append(invariant, $"    <Piece Extent=\"{extent}\">\n");
        text.Append(invariant, $"      <PointData Scalars=\"{name}\">\n");
        text.Append(invariant, $"        <DataArray type=\"Float64\" Name=\"{name}\" format=\"ascii\">\n");

        // x fastest, then y, then z: the order VTK reads an extent in.
        for (var k = 0; k < grid.CountZ; k++)
        {
            for (var j = 0; j < grid.CountY; j++)
            {
                text.Append("          ");

                for (var i = 0; i < grid.CountX; i++)
                {
                    text.Append(invariant, $"{field[i, j, k]:G17} ");
                }

                text.Append('\n');
            }
        }

        text.Append("        </DataArray>\n");
        text.Append("      </PointData>\n");
        text.Append("      <CellData/>\n");
        text.Append("    </Piece>\n");
        text.Append("  </ImageData>\n");
        text.Append("</VTKFile>\n");

        return text.ToString();
    }


}
