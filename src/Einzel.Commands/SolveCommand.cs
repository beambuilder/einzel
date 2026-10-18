using Einzel.Core.Model;
using Einzel.Fields.Solved;

namespace Einzel.Commands;

/// <summary>How one solved field element went.</summary>
public sealed record SolvedElement
{
    /// <summary>Which field element of the model this is.</summary>
    public required int Index { get; init; }

    /// <summary>How many dimensions this element was solved in.</summary>
    public int Dimensions { get; init; } = 2;

    /// <summary>
    /// Which basis channel of this element, in the order the decomposition found them.
    /// </summary>
    /// <remarks>
    /// A driven structure is not one solve but one per spatial pattern, and a
    /// residual quoted for "the field" would be quoting whichever of them ran last.
    /// Zero for a static element, which has exactly one.
    /// </remarks>
    public int Channel { get; init; }

    /// <summary>Node counts, one per axis: x then y, then z for a volume.</summary>
    public required IReadOnlyList<int> Nodes { get; init; }

    /// <summary>Node spacing along each axis, in millimetres.</summary>
    public required IReadOnlyList<double> SpacingMm { get; init; }

    /// <summary>Whether the grid has square cells.</summary>
    public required bool SquareCells { get; init; }

    /// <summary>Electrodes rasterised onto the grid.</summary>
    public required int Electrodes { get; init; }

    /// <summary>Nodes holding a fixed potential.</summary>
    public required int FixedNodes { get; init; }

    /// <summary>Stencil arms cut short by a conductor surface between nodes.</summary>
    public required int CutLinks { get; init; }

    /// <summary>V-cycles taken.</summary>
    public required int Cycles { get; init; }

    /// <summary>Coarse levels the V-cycle descended.</summary>
    /// <remarks>
    /// <b>Reported because a cycle is not a unit of work and gets read as one.</b>
    /// Coarsening stops once a coarse cell would exceed the smallest electrode
    /// dimension, which is a <em>physical</em> size and does not move when the mesh is
    /// refined - so a geometry with a thin electrode can descend zero levels, and then
    /// the whole solve is relaxation on the finest grid. Zero here means this was not
    /// multigrid.
    /// </remarks>
    public int Levels { get; init; }

    /// <summary>Smoothing sweeps over every level, summed across all cycles.</summary>
    /// <remarks>
    /// The unit of work the cycle count is usually taken to stand for. Two solves
    /// whose cycle counts differ by eight can differ by a hundredfold in sweeps, in
    /// the other direction.
    /// </remarks>
    public long Sweeps { get; init; }

    /// <summary>Nodes on the coarsest level reached.</summary>
    /// <remarks>
    /// The number that says whether this is multigrid: a true V-cycle bottoms out at a
    /// handful of nodes whatever the fine mesh, and a bottom level whose node count
    /// does not fall as the mesh refines is the cost multigrid exists to remove.
    /// </remarks>
    public long CoarsestNodes { get; init; }

    /// <summary>Residual reduction per cycle.</summary>
    public required double ConvergenceFactor { get; init; }

    /// <summary>Residual relative to the initial one.</summary>
    public required double RelativeResidual { get; init; }

    /// <summary>Whether the solve met its tolerance.</summary>
    public required bool Converged { get; init; }

    /// <summary>Largest potential magnitude anywhere on the grid, in volts.</summary>
    /// <remarks>
    /// The maximum principle in one number. No potential in a Laplace solution may
    /// exceed the largest applied value, so a peak above the applied potentials is
    /// a tolerance-free proof that the solve diverged - the cheapest check there
    /// is, and the one that caught interior-electrode coarsening reaching 1e134 V.
    /// </remarks>
    public required double PeakPotentialVolts { get; init; }
}

/// <summary>The outcome of solving a model's fields.</summary>
public sealed record SolveOutcome
{
    /// <summary>The model file, as an absolute path.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Content hash of the model document.</summary>
    public required string ModelHash { get; init; }

    /// <summary>One entry per solved field element, in document order.</summary>
    public required IReadOnlyList<SolvedElement> Elements { get; init; }

    /// <summary>Wall-clock milliseconds spent solving.</summary>
    public required double ElapsedMs { get; init; }

    /// <summary>What the solves found wrong with their meshes, once per field element.</summary>
    /// <remarks>
    /// This verb reported residuals and nothing else, so a geometry whose mesh put nodes on
    /// a face two disagreeing conductors share came back converged and clean from the one
    /// command whose job is to say how the discretization went (GRD-2). Once per element
    /// rather than per channel: every channel of an element solves on the same mesh round
    /// the same conductors, so the finding is the element's.
    /// </remarks>
    public IReadOnlyList<Core.Results.ValidityWarning> Warnings { get; init; } = [];

    /// <summary>Whether every element converged.</summary>
    /// <remarks>
    /// False for a model with nothing to solve, rather than vacuously true. An
    /// empty list once meant a converged solve here, and it was the CLI verb whose
    /// whole job is to report a residual saying "converged" about a field it had
    /// silently skipped.
    /// </remarks>
    public bool Converged => Elements.Count > 0 && Elements.All(e => e.Converged);
}

/// <summary>
/// Solves a model's fields without tracking anything through them.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <c>run</c> because the field and the trajectory fail in
/// different ways and are worth being able to look at separately. A flight time
/// that comes out wrong is either a bad field or a bad integration, and the first
/// question is always which - answering it should not require running an ion.
/// </para>
/// <para>
/// It also reports what the discretisation actually did with the geometry: how
/// many nodes an electrode claimed, how many stencil arms were cut short by a
/// surface between nodes, whether the cells came out square. Those are the numbers
/// that explain a solve, and none of them are visible from a flight time.
/// </para>
/// </remarks>
public static class SolveCommand
{
    /// <summary>Solves every solved field element in a model.</summary>
    /// <param name="modelPath">Path to the model file.</param>
    /// <returns>The outcome.</returns>
    /// <exception cref="ArgumentException"><paramref name="modelPath"/> is null or blank.</exception>
    /// <exception cref="Core.Errors.EinzelException">The model does not validate.</exception>
    public static SolveOutcome Execute(string modelPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);

        var absolute = Path.GetFullPath(modelPath);
        var text = File.ReadAllText(absolute);
        var document = Io.ModelJson.Parse(text);
        var validation = ModelValidator.Validate(
            document, null, Path.GetDirectoryName(absolute));

        if (!validation.IsValid)
        {
            throw new Core.Errors.EinzelException(validation.Errors);
        }

        var model = validation.Model!;
        var elements = new List<SolvedElement>();
        var warnings = new List<Core.Results.ValidityWarning>();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        // The first channel's, since every channel of an element carries the same finding.
        void Note(int index, string kind, IReadOnlyList<Core.Results.ValidityWarning> found)
        {
            foreach (var warning in found)
            {
                warnings.Add(warning with { Message = $"field element {index} ({kind}): {warning.Message}" });
            }
        }

        for (var index = 0; index < model.Fields.Count; index++)
        {
            var solve = model.Fields[index].Solve;
            var solve3d = model.Fields[index].Solve3D;

            if (solve is null && solve3d is null)
            {
                // An analytic element has nothing to solve; it is not an error and
                // not something to report a residual for.
                continue;
            }

            if (solve3d is not null)
            {
                var geometry = new Geometry3D(
                    solve3d.MinX, solve3d.MinY, solve3d.MinZ,
                    solve3d.MaxX, solve3d.MaxY, solve3d.MaxZ,
                    solve3d.CellSize,
                    solve3d.Electrodes,
                    solve3d.Tolerance)
                {
                    Drives = solve3d.Drives,
                    Stages = solve3d.Stages,
                    Faces = Geometry3D.FacesOf(solve3d.Faces),
                    ReflectAboutX = solve3d.ReflectAboutX,
                };

                var volume = GeometryBuilder3D.BuildGrid(geometry);
                var volumeChannels = GeometryBuilder3D.SolveChannels(geometry);

                // Guarded as the plane path is: a driven geometry with nothing energised has no
                // channels, and indexing the first would turn "nothing to solve" into a crash.
                if (volumeChannels.Count > 0)
                {
                    Note(index, "solved3d", volumeChannels[0].Report.Warnings);
                }

                foreach (var channel in volumeChannels)
                {
                    var volumePeak = 0.0;

                    foreach (var value in channel.Potential.Values)
                    {
                        volumePeak = Math.Max(volumePeak, Math.Abs(value));
                    }

                    elements.Add(new SolvedElement
                    {
                        Index = index,
                        Dimensions = 3,
                        Channel = channel.Index,
                        Nodes = [volume.CountX, volume.CountY, volume.CountZ],
                        SpacingMm =
                            [volume.SpacingX * 1e3, volume.SpacingY * 1e3, volume.SpacingZ * 1e3],
                        SquareCells = volume.IsCubic,
                        Electrodes = solve3d.Electrodes.Count,
                        FixedNodes = channel.FixedNodes,
                        CutLinks = channel.CutLinks,
                        Cycles = channel.Report.Cycles,
                        Levels = channel.Report.Levels,
                        Sweeps = channel.Report.Sweeps,
                        CoarsestNodes = channel.Report.CoarsestNodes,
                        ConvergenceFactor = channel.Report.ConvergenceFactor,
                        RelativeResidual = channel.Report.InitialResidual > 0.0
                            ? channel.Report.FinalResidual / channel.Report.InitialResidual
                            : 0.0,
                        Converged = channel.Report.Converged,
                        PeakPotentialVolts = volumePeak,
                    });
                }

                continue;
            }

            var grid = GeometryBuilder.BuildGrid(solve!);

            // One entry per basis channel, the same as the three-dimensional path.
            // A driven structure is one solve per spatial pattern, and reporting
            // "the field" meant reporting the DC pattern alone - which for the RF
            // quadrupole is a grounded box, and came back converged with a peak
            // potential of zero volts and exit 0.
            var planeChannels = GeometryBuilder.SolveChannels(solve!);

            if (planeChannels.Count > 0)
            {
                Note(index, "solved2d", planeChannels[0].Report.Warnings);
            }

            foreach (var channel in planeChannels)
            {
                var peak = 0.0;

                foreach (var value in channel.Potential.Values)
                {
                    peak = Math.Max(peak, Math.Abs(value));
                }

                elements.Add(new SolvedElement
                {
                    Index = index,
                    Dimensions = 2,
                    Channel = channel.Index,
                    Nodes = [grid.CountX, grid.CountY],
                    SpacingMm = [grid.SpacingX * 1e3, grid.SpacingY * 1e3],
                    SquareCells = grid.IsSquare,
                    Electrodes = solve!.Electrodes.Count,
                    FixedNodes = channel.FixedNodes,
                    CutLinks = channel.CutLinks,
                    Cycles = channel.Report.Cycles,
                    Levels = channel.Report.Levels,
                    Sweeps = channel.Report.Sweeps,
                    CoarsestNodes = channel.Report.CoarsestNodes,
                    ConvergenceFactor = channel.Report.ConvergenceFactor,
                    RelativeResidual = channel.Report.InitialResidual > 0.0
                        ? channel.Report.FinalResidual / channel.Report.InitialResidual
                        : 0.0,
                    Converged = channel.Report.Converged,
                    PeakPotentialVolts = peak,
                });
            }
        }

        if (elements.Count == 0)
        {
            // Rather than a converged solve of nothing. An empty element list once
            // satisfied "every element converged" vacuously, so the verb whose whole
            // job is to report a residual answered "converged: true, exit 0" for a
            // model it had silently skipped every field of.
            throw new Core.Errors.EinzelException(new Core.Errors.EinzelError
            {
                Code = Core.Errors.ErrorCodes.SchemaInvalid,
                Path = "/fields",
                Constraint = "this model has no field to solve",
                Suggestion = "only a 'solved2d' or 'solved3d' field element has a discretisation to "
                    + "report; analytic fields are formulas, and 'einzel run' will fly through them "
                    + "without a solve",
            });
        }

        return new SolveOutcome
        {
            ModelPath = absolute,
            ModelHash = Project.ContentHash.OfText(text),
            Elements = elements,
            ElapsedMs = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            Warnings = warnings,
        };
    }
}
