using System.Collections.Frozen;
using Einzel.Core.Errors;

namespace Einzel.Core.Units;

/// <summary>
/// A unit symbol: the factor that converts it to coherent SI, and the dimension
/// it belongs to.
/// </summary>
/// <param name="Symbol">The canonical symbol.</param>
/// <param name="SiFactor">Multiply by this to reach SI.</param>
/// <param name="Dimension">The physical dimension.</param>
public sealed record UnitDefinition(string Symbol, double SiFactor, Dimension Dimension);

/// <summary>
/// The known unit symbols, and the conversion into SI that happens once at the
/// model boundary.
/// </summary>
/// <remarks>
/// <para>
/// AGT-7 requires the platform to be self-describing at runtime, with units on
/// every schema field. <see cref="All"/> is the enumeration behind that: the
/// schema generator, the CLI's unit help, and the MCP self-description all read
/// from here rather than from three separate hand-maintained lists.
/// </para>
/// <para>
/// Lookup is ordinal and case-sensitive on purpose. <c>mm</c> and <c>Mm</c>
/// differ by nine orders of magnitude, and a case-insensitive registry would
/// silently accept the wrong one.
/// </para>
/// </remarks>
public static class UnitRegistry
{
    // Exact by the 2019 SI redefinition.
    private const double ElementaryCharge = 1.602176634e-19;      // C
    private const double ElectronVolt = 1.602176634e-19;          // J
    private const double StandardAtmosphere = 101325.0;           // Pa

    // CODATA 2022 recommended value, 1.66053906892(52)e-27 kg. Measured, not
    // exact; re-check against the published table before a release that claims
    // ACC-2. The 2018-to-2022 revision moved this by about 1.4e-10 relative,
    // which is three orders below the ACC-1 budget, so neither value threatens
    // the flight-time target.
    private const double AtomicMassConstant = 1.66053906892e-27;  // kg

    private static readonly FrozenDictionary<string, UnitDefinition> Definitions = Build();

    private static FrozenDictionary<string, UnitDefinition> Build()
    {
        var map = new Dictionary<string, UnitDefinition>(StringComparer.Ordinal);

        void Add(string symbol, double factor, Dimension dimension) =>
            map.Add(symbol, new UnitDefinition(symbol, factor, dimension));

        var length = Dimension.LengthDimension;
        Add("m", 1.0, length);
        Add("km", 1e3, length);
        Add("cm", 1e-2, length);
        Add("mm", 1e-3, length);
        Add("um", 1e-6, length);
        Add("µm", 1e-6, length);
        Add("nm", 1e-9, length);

        var time = Dimension.TimeDimension;
        Add("s", 1.0, time);
        Add("ms", 1e-3, time);
        Add("us", 1e-6, time);
        Add("µs", 1e-6, time);
        Add("ns", 1e-9, time);
        Add("ps", 1e-12, time);
        Add("min", 60.0, time);
        Add("h", 3600.0, time);

        var mass = Dimension.MassDimension;
        Add("kg", 1.0, mass);
        Add("g", 1e-3, mass);
        Add("u", AtomicMassConstant, mass);
        Add("Da", AtomicMassConstant, mass);

        Add("A", 1.0, Dimension.CurrentDimension);
        Add("K", 1.0, Dimension.TemperatureDimension);
        Add("mol", 1.0, Dimension.AmountDimension);

        var energy = Dimension.Energy;
        Add("J", 1.0, energy);
        Add("meV", ElectronVolt * 1e-3, energy);
        Add("eV", ElectronVolt, energy);
        Add("keV", ElectronVolt * 1e3, energy);
        Add("MeV", ElectronVolt * 1e6, energy);

        var potential = Dimension.ElectricPotential;
        Add("V", 1.0, potential);
        Add("mV", 1e-3, potential);
        Add("kV", 1e3, potential);

        var charge = Dimension.Charge;
        Add("C", 1.0, charge);
        Add("e", ElementaryCharge, charge);

        var pressure = Dimension.Pressure;
        Add("Pa", 1.0, pressure);
        Add("kPa", 1e3, pressure);
        Add("bar", 1e5, pressure);
        Add("mbar", 1e2, pressure);
        Add("Torr", StandardAtmosphere / 760.0, pressure);
        Add("mTorr", StandardAtmosphere / 760.0 * 1e-3, pressure);

        Add("m/s", 1.0, Dimension.Velocity);
        Add("km/s", 1e3, Dimension.Velocity);
        Add("m/s^2", 1.0, Dimension.Acceleration);
        Add("N", 1.0, Dimension.Force);

        var frequency = Dimension.Frequency;
        Add("Hz", 1.0, frequency);
        Add("kHz", 1e3, frequency);
        Add("MHz", 1e6, frequency);
        Add("GHz", 1e9, frequency);

        var area = Dimension.Area;
        Add("m^2", 1.0, area);
        Add("cm^2", 1e-4, area);

        // Collision cross sections are quoted in square angstroms; the memo uses
        // 300 A^2 for a mid-size peptide fragment. Deliberately not spelled
        // "A^2", which would collide with ampere squared.
        Add("Å^2", 1e-20, area);
        Add("angstrom^2", 1e-20, area);

        var volume = Dimension.Volume;
        Add("m^3", 1.0, volume);
        Add("cm^3", 1e-6, volume);

        // Polarizability is quoted as a volume, in cubic angstroms: nitrogen is
        // 1.74, helium 0.205. The Langevin collision rate depends on it and on
        // nothing else about the shape of the neutral, which is why a gas can be
        // described by two numbers.
        Add("Å^3", 1e-30, volume);
        Add("angstrom^3", 1e-30, volume);

        Add("m^-3", 1.0, Dimension.NumberDensity);
        Add("cm^-3", 1e6, Dimension.NumberDensity);

        Add("V/m", 1.0, Dimension.ElectricField);
        Add("V/mm", 1e3, Dimension.ElectricField);

        // Potential curvature: what a harmonic well is characterised by, and the one
        // number an orbital analyser's frequency comes from. V/mm^2 is a millionfold,
        // not a thousandfold, which is the kind of slip a unit registry exists to make
        // impossible to write.
        Add("V/m^2", 1.0, Dimension.ElectricFieldGradient);
        Add("V/mm^2", 1e6, Dimension.ElectricFieldGradient);

        // Magnetic flux density. Gauss is the CGS unit and still how a bench magnet and a
        // Hall probe are quoted, so it is offered rather than left for someone to convert:
        // 1 G is exactly 1e-4 T. "G" collides with nothing here (giga appears only inside
        // GHz and GeV-style compound symbols, never alone).
        var flux = Dimension.MagneticFluxDensity;
        Add("T", 1.0, flux);
        Add("mT", 1e-3, flux);
        Add("uT", 1e-6, flux);
        Add("µT", 1e-6, flux);
        Add("G", 1e-4, flux);
        Add("kG", 1e-1, flux);

        Add("m^2/(V s)", 1.0, Dimension.Mobility);
        Add("cm^2/(V s)", 1e-4, Dimension.Mobility);

        var none = Dimension.Dimensionless;
        Add("1", 1.0, none);
        Add("ratio", 1.0, none);
        Add("rad", 1.0, none);
        Add("mrad", 1e-3, none);
        Add("deg", Math.PI / 180.0, none);
        Add("percent", 1e-2, none);
        Add("ppm", 1e-6, none);
        Add("ppb", 1e-9, none);

        // Playback rates: how much simulated time passes per second of playback
        // (RND-7). Dimensionless because they are a time over a time, which is the
        // honest reading rather than a convenience - what makes them a *rate* is the
        // second in the denominator being playback rather than flight, and no
        // dimension can carry that distinction. The animation spec names the field
        // "rate" so the document says which second is which.
        Add("s/s", 1.0, none);
        Add("ms/s", 1e-3, none);
        Add("us/s", 1e-6, none);
        Add("µs/s", 1e-6, none);
        Add("ns/s", 1e-9, none);
        Add("ps/s", 1e-12, none);

        return map.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>Every known unit, for schema generation and self-description.</summary>
    public static IReadOnlyCollection<UnitDefinition> All => Definitions.Values;

    /// <summary>Every known unit symbol.</summary>
    public static IReadOnlyCollection<string> KnownSymbols => Definitions.Keys;

    /// <summary>
    /// Resolves a unit symbol. The Greek small letter mu is normalised to the
    /// micro sign, so <c>μs</c> and <c>µs</c> both resolve; nothing else
    /// is normalised, because guessing at a caller's intent is how a factor of
    /// 1000 gets in.
    /// </summary>
    /// <param name="symbol">The unit symbol.</param>
    /// <returns>The matching definition.</returns>
    /// <exception cref="EinzelException">
    /// <see cref="ErrorCodes.UnitsRequired"/> when the symbol is null or blank;
    /// <see cref="ErrorCodes.UnitsUnknown"/> when it is not recognised.
    /// </exception>
    public static UnitDefinition Resolve(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.UnitsRequired,
                Path = "/",
                Constraint = "every quantity must carry an explicit unit",
                Suggestion = "supply a unit symbol, for example 'mm', 'keV', or 'mbar'",
            });
        }

        var normalised = symbol.Replace('μ', 'µ');

        if (Definitions.TryGetValue(normalised, out var definition))
        {
            return definition;
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.UnitsUnknown,
            Path = "/",
            Constraint = $"'{symbol}' is not a known unit symbol",
            Suggestion = Suggest(normalised),
        });
    }

    /// <summary>Tries to resolve a unit symbol without throwing.</summary>
    /// <param name="symbol">The unit symbol.</param>
    /// <param name="definition">The matching definition, when found.</param>
    /// <returns><see langword="true"/> when the symbol is known.</returns>
    public static bool TryResolve(string symbol, out UnitDefinition? definition)
    {
        definition = null;

        if (string.IsNullOrWhiteSpace(symbol))
        {
            return false;
        }

        return Definitions.TryGetValue(symbol.Replace('μ', 'µ'), out definition);
    }

    private static string Suggest(string symbol)
    {
        // Case is the commonest mistake, so check for a case-insensitive match
        // before falling back to listing candidates.
        foreach (var known in Definitions.Keys)
        {
            if (string.Equals(known, symbol, StringComparison.OrdinalIgnoreCase))
            {
                return $"unit symbols are case-sensitive; did you mean '{known}'?";
            }
        }

        return "see the unit list from 'einzel schema --units' for known symbols";
    }
}
