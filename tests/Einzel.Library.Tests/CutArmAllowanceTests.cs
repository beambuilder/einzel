using Einzel.Core.Model;
using Einzel.Fields.Solved;
using Einzel.Io;

using Xunit.Abstractions;

namespace Einzel.Library.Tests;

/// <summary>
/// The cut arms a memory statement bills for, against the cut arms each shipped volume geometry has.
/// </summary>
/// <remarks>
/// <para>
/// The cut links are stored over the cut arms alone now, so what they cost depends on the
/// geometry - and the statement is worked out from the declaration without rasterizing, so it
/// cannot know. It bills <see cref="SolveMemoryModel.VolumeCutArmsPerNode"/> instead, and its
/// remarks name the density of every shipped volume template as the reason that is enough.
/// This is where those densities are measured rather than quoted.
/// </para>
/// <para>
/// <b>Every template, by enumeration</b>, so a new volume geometry is covered the day it ships
/// and the one that is denser than the allowance is found here rather than by a solve running
/// out of memory under an estimate that said it would fit.
/// </para>
/// </remarks>
public sealed class CutArmAllowanceTests(ITestOutputHelper output)
{
    /// <summary>The shipped templates that declare a volume solve.</summary>
    /// <returns>One row per template.</returns>
    public static TheoryData<string> VolumeTemplates()
    {
        var rows = new TheoryData<string>();

        foreach (var name in DeviceTemplates.Names())
        {
            if (Compile(name).Fields.Any(f => f.Solve3D is not null))
            {
                rows.Add(name);
            }
        }

        return rows;
    }

    /// <summary>Each shipped volume geometry, at its shipped mesh, has fewer cut arms than are billed.</summary>
    /// <param name="template">The template.</param>
    [Theory]
    [MemberData(nameof(VolumeTemplates))]
    public void TheBilledCutArmsCoverAShippedVolumeGeometry(string template)
    {
        foreach (var solve in Compile(template).Fields.Select(f => f.Solve3D).OfType<CompiledSolvedField3D>())
        {
            var geometry = new Geometry3D(
                solve.MinX, solve.MinY, solve.MinZ,
                solve.MaxX, solve.MaxY, solve.MaxZ,
                solve.CellSize,
                solve.Electrodes,
                solve.Tolerance)
            {
                Drives = solve.Drives,
                Stages = solve.Stages,
                Faces = Geometry3D.FacesOf(solve.Faces),
                ReflectAboutX = solve.ReflectAboutX,
            };

            var grid = GeometryBuilder3D.BuildGrid(geometry);
            var cuts = GeometryBuilder3D.BuildMask(geometry, grid).Cuts;

            var nodes = (double)grid.NodeCount;
            var arms = cuts?.CutCount ?? 0;
            var bytes = cuts?.Bytes ?? 0;

            output.WriteLine(
                $"{template}: {grid.NodeCount:N0} nodes, {arms:N0} cut arms, {arms / nodes:F4} a node "
                + $"against {SolveMemoryModel.VolumeCutArmsPerNode} billed; the store holds "
                + $"{bytes / nodes:F2} bytes a node against {SolveMemoryModel.VolumeCutLinks} billed");

            Assert.True(arms > 0, "a shipped volume geometry with no cut arms is not testing the allowance");
            Assert.InRange(arms / nodes, 0.0, SolveMemoryModel.VolumeCutArmsPerNode);

            // The arm bytes are padded to a whole block of eight, a few bytes over the grid.
            Assert.InRange(bytes, 0L, (long)(SolveMemoryModel.VolumeCutLinks * nodes) + 12L);

            // And trimmed: the packed arms grow by doubling, and a mask builder that forgot to
            // release the slack would hold up to twice them for the whole solve. Exact, because
            // the slack can be small enough to hide inside the allowance - 7 percent on the Astral.
            var blocks = (grid.NodeCount + 7) / 8;

            Assert.Equal((blocks * 12) + (arms * (long)SolveMemoryModel.VolumeCutArm), bytes);
        }
    }

    private static CompiledModel Compile(string template)
    {
        var validation = ModelValidator.Validate(ModelJson.Parse(DeviceTemplates.Read(template)));

        Assert.True(
            validation.IsValid,
            validation.IsValid ? string.Empty : validation.Errors[0].Constraint);

        return validation.Model!;
    }
}
