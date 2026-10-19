using Einzel.Commands;
using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;
using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// No shipped model lets its mesh sample a face two disagreeing conductors share, and the ones
/// whose conductors meet a grounded face of the domain are exactly the ones measured.
/// </summary>
/// <remarks>
/// <para>
/// Every device template and every corpus example, every solved element, through the same
/// detector the geometry builders run before a solve. The survey that wrote this test found
/// all of them clean between electrodes once <c>astral-3d</c> had moved its foil mesh, which is
/// the state worth keeping: a template edit that lands a shared face on a node is a coin toss
/// nobody would see, because no refinement ladder can.
/// </para>
/// <para>
/// <b>The control is what makes a clean sweep mean anything.</b> <c>astral-3d</c> with its
/// mesh shift taken out has to trip, and on exactly the 180 stencil arms the survey found -
/// three shared foil faces on node planes, four plates, fifteen nodes across each stripe - and
/// on no node, since the stripe is thinner than a cell and holds none. A detector that asked
/// only about nodes, which is how this check was first specified, reports that case clean.
/// </para>
/// <para>
/// <b>The grounded boundary is pinned rather than cleared.</b> Once a grounded face counted as a
/// conductor at zero volts, seventeen shipped solved elements tripped - ring stacks run out to
/// the grounded outer wall, a Paul trap's ring truncated at it, the einzel lens's center tube,
/// a Kingdon wire's ends, and two mirrors whose end cap is a plate with no thickness lying in
/// the grounded edge. Moving only the face on the wall by 1e-7 of the domain's extent either
/// way leaves every figure of fifteen of them bit-identical, and every potential more than three
/// cells from the wall; for the two mirrors it decides whether the cap is in the solve at all.
/// None is cleared here: the funnels' ring corners that cross the wall on a line of nodes cannot
/// be cleared without moving geometry, and moving it would move published numbers. So the list
/// is pinned, and any change to it - a new trip, or one gone - fails until somebody measures it.
/// </para>
/// </remarks>
public sealed class SharedFaceCorpusTests(ITestOutputHelper output) : IDisposable
{
    /// <summary>
    /// Every shipped solved element with nodes on a grounded face of its domain lying on a
    /// conductor that holds something other than zero volts, and how many - as measured.
    /// </summary>
    private static readonly Dictionary<string, int> AgainstTheGroundedWall = new(StringComparer.Ordinal)
    {
        ["template astral-mirror, field 0"] = 33,
        ["template einzel-lens, field 0"] = 148,
        ["template ion-funnel, field 0"] = 78,
        ["template kingdon-trap, field 0"] = 2,
        ["template paul-trap, field 0"] = 35,
        ["template planar-mirror-pair, field 0"] = 33,
        ["template pnnl-ion-funnel, field 0"] = 234,
        ["template tims-analyzer, field 0"] = 373,
        ["template tims-front-end, field 0"] = 105,
        ["template tims-tandem, field 0"] = 187,
        ["template travelling-wave-guide, field 0"] = 178,
        ["example ion-funnel-no-rf, field 0"] = 59,
        ["example ion-funnel-rf, field 0"] = 78,
        ["example paul-trap-ejected, field 0"] = 35,
        ["example paul-trap-held, field 0"] = 35,
        ["example travelling-wave-capture, field 0"] = 178,
        ["example travelling-wave-guide, field 0"] = 178,
    };

    private readonly string _assets = Path.Combine(
        Path.GetTempPath(), "einzel-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_assets))
        {
            Directory.Delete(_assets, recursive: true);
        }
    }

    [Fact]
    public void NoShippedModelSamplesAFaceTwoElectrodesShare()
    {
        var (inspected, findings) = Survey();

        output.WriteLine($"{inspected} solved elements inspected");

        // A sweep that inspected nothing would pass; the corpus has dozens of solved elements.
        Assert.True(inspected > 30, $"only {inspected} solved elements were inspected");

        Assert.Empty(findings
            .Where(f => f.Value.Nodes + f.Value.Arms > 0)
            .Select(f => $"{f.Key}: {f.Value.ToWarning().Message}"));
    }

    [Fact]
    public void TheShippedModelsAgainstAGroundedFaceAreTheOnesMeasured()
    {
        var (_, findings) = Survey();

        var found = findings
            .Where(f => f.Value.BoundaryNodes > 0)
            .ToDictionary(f => f.Key, f => f.Value.BoundaryNodes, StringComparer.Ordinal);

        foreach (var (label, count) in found.OrderBy(f => f.Key, StringComparer.Ordinal))
        {
            output.WriteLine($"{label}: {count}");
        }

        Assert.Equal(
            AgainstTheGroundedWall.OrderBy(f => f.Key, StringComparer.Ordinal),
            found.OrderBy(f => f.Key, StringComparer.Ordinal));
    }

    /// <summary>
    /// The survey's reading of the harmless kind, on the lens: the center tube's outer face is
    /// flush with the grounded wall, and moving that face alone a hair short of the wall or a
    /// hair past it flips the edge nodes between zero and 500 V and changes the flight time not
    /// at all.
    /// </summary>
    /// <remarks>
    /// Only the face on the wall moves, which is the one thing moving the domain face would
    /// change relative to the conductor - without the domain's interval count jumping when an
    /// extent is an exact power of two cells, which moving the domain itself does.
    /// </remarks>
    [Fact]
    public void TheLensDoesNotSeeItsCoin()
    {
        var model = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read("einzel-lens"))).Model!;
        var index = FieldIndex(model);
        var solve = model.Fields[index].Solve!;
        var nudge = 1e-7 * (solve.MaxY - solve.MinY);

        CompiledModel Moved(double by) => With(model, index, solve with
        {
            Electrodes = [.. solve.Electrodes.Select(e => e.Name == "centre" ? e with { MaxY = e.MaxY + by } : e)],
        });

        var grid = GeometryBuilder.BuildGrid(solve);
        var j = grid.CountY - 1;
        var i = (int)Math.Round((0.048 - grid.OriginX) / grid.SpacingX);

        Assert.Equal(0.0, GeometryBuilder.BuildMask(Moved(-nudge).Fields[index].Solve!, grid).ValueAt(i, j));
        Assert.Equal(500.0, GeometryBuilder.BuildMask(Moved(+nudge).Fields[index].Solve!, grid).ValueAt(i, j));

        var flight = FiguresOfMerit.Evaluator("flightTime", report: _ => { });
        var (shipped, short_, past) = (flight(model), flight(Moved(-nudge)), flight(Moved(+nudge)));

        output.WriteLine($"flight time {shipped:R} s shipped, {short_:R} short of the wall, {past:R} past it");

        Assert.NotNull(shipped);
        Assert.Equal(shipped, short_);
        Assert.Equal(shipped, past);
    }

    /// <summary>
    /// And the kind that matters: a mirror's end cap is a plate with no thickness lying in the
    /// grounded edge, so it is in the solve only through the edge nodes it holds - and 1e-7 of the
    /// domain's extent outside the edge, it is not in the solve at all.
    /// </summary>
    /// <remarks>
    /// The shipped document writes the cap and the domain's edge as the same expression, so the
    /// arithmetic lands on the side with a cap, deterministically; the warning is what says the
    /// cap exists on the last bit. Just inside the edge the cap is a cut surface and the flight
    /// is the shipped one to 6e-8; just outside it the ion is never reflected to the detector.
    /// </remarks>
    [Fact]
    public void AMirrorCapLyingInTheGroundedEdgeIsInTheSolveOnlyByTheCoin()
    {
        var model = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read("planar-mirror-pair"))).Model!;
        var index = FieldIndex(model);
        var solve = model.Fields[index].Solve!;
        var nudge = 1e-7 * (solve.MaxX - solve.MinX);

        CompiledModel Moved(double by) => With(model, index, solve with
        {
            Electrodes = [.. solve.Electrodes.Select(e => e.Name == "cap" ? e with { MinX = e.MinX + by, MaxX = e.MaxX + by } : e)],
        });

        var transmission = FiguresOfMerit.Evaluator("transmission", ions: 1, report: _ => { });
        var (shipped, inside, outside) = (transmission(model), transmission(Moved(+nudge)), transmission(Moved(-nudge)));

        output.WriteLine($"transmission {shipped} shipped, {inside} with the cap just inside the edge, {outside} just outside");

        Assert.Equal(1.0, shipped);
        Assert.Equal(1.0, inside);
        Assert.Equal(0.0, outside);
    }

    [Fact]
    public void TheAstralWithItsMeshShiftRemovedTrips()
    {
        var document = ModelJson.Parse(DeviceTemplates.Read("astral-3d"));
        var parameters = new Dictionary<string, ParameterDocument>(document.Parameters!, StringComparer.Ordinal);
        parameters["meshShiftZ"] = parameters["meshShiftZ"] with { Value = 0.0 };

        var validation = ModelValidator.Validate(document with { Parameters = parameters });
        Assert.True(validation.IsValid);

        var solve = validation.Model!.Fields.Single(f => f.Solve3D is not null).Solve3D!;
        var geometry = Volume(solve);
        var found = GeometryBuilder3D.NodesOnSharedFaces(geometry, GeometryBuilder3D.BuildGrid(geometry));

        Assert.NotNull(found);
        output.WriteLine(found.ToWarning().Message);

        Assert.Equal(0, found.Nodes);
        Assert.Equal(180, found.Arms);
        Assert.StartsWith("foil", found.First, StringComparison.Ordinal);
    }

    private (int Inspected, Dictionary<string, SharedFaceNodes> Findings) Survey()
    {
        var inspected = 0;
        var findings = new Dictionary<string, SharedFaceNodes>(StringComparer.Ordinal);

        foreach (var name in DeviceTemplates.Names())
        {
            inspected += Inspect($"template {name}", DeviceTemplates.Read(name), null, findings);
        }

        foreach (var name in ExampleModels.Names)
        {
            var directory = Path.Combine(_assets, name);
            Directory.CreateDirectory(directory);
            ExampleModels.WriteAssets(name, directory);
            inspected += Inspect($"example {name}", ExampleModels.Read(name), directory, findings);
        }

        return (inspected, findings);
    }

    private static int Inspect(
        string label, string json, string? directory, Dictionary<string, SharedFaceNodes> findings)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(json), null, directory);

        Assert.True(
            validation.IsValid,
            $"{label}: {(validation.IsValid ? string.Empty : validation.Errors[0].Constraint)}");

        var inspected = 0;

        for (var index = 0; index < validation.Model!.Fields.Count; index++)
        {
            var element = validation.Model.Fields[index];
            SharedFaceNodes? found;

            if (element.Solve is { } plane)
            {
                found = GeometryBuilder.NodesOnSharedFaces(plane, GeometryBuilder.BuildGrid(plane));
            }
            else if (element.Solve3D is { } volume)
            {
                var geometry = Volume(volume);
                found = GeometryBuilder3D.NodesOnSharedFaces(geometry, GeometryBuilder3D.BuildGrid(geometry));
            }
            else
            {
                continue;
            }

            inspected++;

            if (found is not null)
            {
                findings[$"{label}, field {index}"] = found;
            }
        }

        return inspected;
    }

    private static int FieldIndex(CompiledModel model) =>
        model.Fields.ToList().FindIndex(f => f.Solve is not null);

    private static CompiledModel With(CompiledModel model, int index, CompiledSolvedField solve) => model with
    {
        Fields = [.. model.Fields.Select((f, k) => k == index ? f with { Solve = solve } : f)],
    };

    private static Geometry3D Volume(CompiledSolvedField3D solve) => new(
        solve.MinX, solve.MinY, solve.MinZ,
        solve.MaxX, solve.MaxY, solve.MaxZ,
        solve.CellSize, solve.Electrodes, solve.Tolerance)
    {
        Drives = solve.Drives,
        Stages = solve.Stages,
        Faces = Geometry3D.FacesOf(solve.Faces),
        ReflectAboutX = solve.ReflectAboutX,
    };
}
