using System.Globalization;

namespace Einzel.Cli.Tests;

/// <summary>Diffusive drift-tube documents for the memory tests, from one builder.</summary>
/// <remarks>
/// Lifted out of <c>SolveMemoryEstimateTests</c> so the tests that bill and measure a diffusive run
/// write one document rather than several hand-written copies that drift apart - which they had
/// begun to, in field, detector and grid. The defaults are that class's document unchanged.
/// </remarks>
internal static class DriftTubes
{
    /// <summary>A drift tube through an analytic field, stepped as a density.</summary>
    /// <param name="ion">The <c>ion</c> member, or empty when <paramref name="species"/> is given.</param>
    /// <param name="mobility">The <c>transport.mobility</c> member, or empty.</param>
    /// <param name="species">The <c>species</c> member, or empty.</param>
    /// <param name="spaceCharge">The space-charge method, or null for none.</param>
    /// <param name="intervalsX">Density-grid intervals along the tube.</param>
    /// <param name="intervalsY">Density-grid intervals across it.</param>
    /// <param name="flightMicroseconds">The declared flight.</param>
    /// <returns>The model document.</returns>
    public static string Cartesian(
        string ion = "",
        string mobility = "",
        string species = "",
        string? spaceCharge = null,
        int intervalsX = 64,
        int intervalsY = 32,
        double flightMicroseconds = 60.0) =>
        string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "schemaVersion": "0.13",
              "name": "drift-tube",
              {{ion}}{{species}}
              "source": {
                "position": { "value": [4, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 0, "unit": "V" },
                "cloud": { "ions": 1,
                           "transverseSpread": { "value": 0.4, "unit": "mm" },
                           "longitudinalSpread": { "value": 0.4, "unit": "mm" } }
              },
              "detector": {
                "planePoint": { "value": [32, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "diffusion",
                "maximumFlightTime": { "value": {{flightMicroseconds}}, "unit": "us" },
                {{mobility}}
                {{SpaceCharge(spaceCharge)}}
                "densityGrid": {
                  "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 32, "unit": "mm" },
                  "minY": { "value": -4, "unit": "mm" }, "maxY": { "value": 4, "unit": "mm" },
                  "intervalsX": {{intervalsX}}, "intervalsY": {{intervalsY}}
                },
                "gas": { "model": "hardSphere", "pressure": { "value": 2, "unit": "mbar" },
                         "mass": { "value": 28.0134, "unit": "Da" },
                         "crossSection": { "value": 250, "unit": "angstrom^2" } }
              },
              "fields": [
                { "type": "uniform", "field": { "value": [500, 0, 0], "unit": "V/m" } }
              ]
            }
            """);

    /// <summary>
    /// The same tube on the axis of a solved cylindrical bore, whose earthed wall lies inside the
    /// density grid - so the run is cylindrical, and the wall absorbs.
    /// </summary>
    /// <param name="spaceCharge">The space-charge method, or null for none.</param>
    /// <param name="intervalsX">Density-grid intervals along the tube.</param>
    /// <param name="intervalsY">Density-grid intervals across the radius.</param>
    /// <param name="flightMicroseconds">The declared flight.</param>
    /// <returns>The model document.</returns>
    /// <remarks>
    /// What the Cartesian tube does not exercise: a self-field on a cylindrical grid, whose mask
    /// fixes every absorbing node of the wall, beside the plain run's absorbers.
    /// </remarks>
    public static string Bore(
        string? spaceCharge = null,
        int intervalsX = 64,
        int intervalsY = 32,
        double flightMicroseconds = 60.0) =>
        string.Create(CultureInfo.InvariantCulture, $$"""
            {
              "schemaVersion": "0.13",
              "name": "bore",
              {{Ion}}
              "source": {
                "position": { "value": [4, 0, 0], "unit": "mm" },
                "direction": { "value": [1, 0, 0] },
                "accelerationPotential": { "value": 0, "unit": "V" },
                "cloud": { "ions": 1,
                           "transverseSpread": { "value": 0.4, "unit": "mm" },
                           "longitudinalSpread": { "value": 0.4, "unit": "mm" } }
              },
              "detector": {
                "planePoint": { "value": [32, 0, 0], "unit": "mm" },
                "normal": { "value": [-1, 0, 0] }
              },
              "transport": {
                "mode": "diffusion",
                "maximumFlightTime": { "value": {{flightMicroseconds}}, "unit": "us" },
                {{Mobility}}
                {{SpaceCharge(spaceCharge)}}
                "densityGrid": {
                  "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 32, "unit": "mm" },
                  "minY": { "value": 0, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
                  "intervalsX": {{intervalsX}}, "intervalsY": {{intervalsY}}
                },
                "gas": { "model": "hardSphere", "pressure": { "value": 2, "unit": "mbar" },
                         "mass": { "value": 28.0134, "unit": "Da" },
                         "crossSection": { "value": 250, "unit": "angstrom^2" } }
              },
              "fields": [
                { "type": "uniform", "field": { "value": [500, 0, 0], "unit": "V/m" } },
                {
                  "type": "solved2d",
                  "solve": {
                    "symmetry": "cylindrical",
                    "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 32, "unit": "mm" },
                    "minY": { "value": 0, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
                    "cellSize": { "value": 0.25, "unit": "mm" },
                    "boundaryIsDiscontinuous": false,
                    "electrodes": [
                      { "name": "wall", "shape": "rectangle",
                        "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 32, "unit": "mm" },
                        "minY": { "value": 3, "unit": "mm" }, "maxY": { "value": 6, "unit": "mm" },
                        "potential": { "value": 0, "unit": "V" } }
                    ]
                  }
                }
              ]
            }
            """);

    /// <summary>One m/z 622 ion, as the Cartesian tube's callers declare it.</summary>
    public const string Ion =
        "\"ion\": { \"massToCharge\": { \"value\": 622, \"unit\": \"Da\" }, \"chargeNumber\": 1 },";

    /// <summary>Its declared mobility.</summary>
    public const string Mobility =
        "\"mobility\": { \"zeroField\": { \"value\": 0.05, \"unit\": \"m^2/(V s)\" } },";

    private static string SpaceCharge(string? method) =>
        method is null ? string.Empty : $"\"spaceCharge\": \"{method}\",";
}
