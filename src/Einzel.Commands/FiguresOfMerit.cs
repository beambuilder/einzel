using System.Text.Json.Serialization;
using Einzel.Analysis;
using Einzel.Core.Errors;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Solved;
using Einzel.Transport;
using Einzel.Transport.Integration;

namespace Einzel.Commands;

/// <summary>Which of §12's families a figure of merit belongs to.</summary>
/// <remarks>
/// <para>
/// §12 sorts figures into three, and the sort is not decoration: it says what a figure
/// is <em>of</em>. A Class T figure describes one packet's arrival, a Class S figure
/// describes a population, a Class B figure describes where a boundary in operating
/// space lies. They are computed differently, cost differently, and are read against
/// different questions - which is why §16 asks for results grouped this way rather than
/// listed.
/// </para>
/// <para>
/// <b>Two of this registry's figures are in none of them, and that is recorded rather
/// than forced.</b> §12's list is of figures of merit; the raw arrival time and a
/// numerical energy-drift diagnostic are not that, and putting them in a class they do
/// not belong to would make the grouping mean less everywhere else. See SPEC.md's
/// amendment on §12.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AccuracyClass>))]
public enum AccuracyClass
{
    /// <summary>Not one of §12's figures: a raw quantity, or a diagnostic.</summary>
    None,

    /// <summary>
    /// Class T. One packet's arrival: resolving power, peak shape, focusing order,
    /// turn-around time, emittance.
    /// </summary>
    Trajectory,

    /// <summary>
    /// Class S. A population: transmission itemised by loss surface, radial
    /// compression, transit-time distribution, thermalization.
    /// </summary>
    Statistical,

    /// <summary>
    /// Class B. Where a boundary in operating space lies: stability diagrams, cut-offs,
    /// secular spectra, isolation efficiency.
    /// </summary>
    Boundary,
}

/// <summary>How many flights one evaluation of a figure of merit costs.</summary>
/// <remarks>
/// <para>
/// The cost estimate needs this and had no way to ask for it, so it charged every figure
/// the study's declared ion count. For <c>flightTime</c> - one ion down a three-rung
/// convergence ladder - that is seven flights charged for every three flown, and a tolerance
/// sweep of a reflectron was costed at 29 s against a measured 1.08 s. GRD-8's gate then
/// reported it above the threshold, and an agent shrank its study to get past a number that
/// was wrong.
/// </para>
/// <para>
/// It is on the registry rather than computed at the call site because the registry is
/// where a new figure is added, and a figure added without a basis should not silently
/// inherit the wrong one.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<FlightBasis>))]
public enum FlightBasis
{
    /// <summary>
    /// One ion through <see cref="FlightTimeStudy"/>'s convergence ladder: three flights,
    /// whatever the study's ion count says.
    /// </summary>
    ConvergenceLadder,

    /// <summary>One ion, flown once, and its trajectory analysed.</summary>
    OneFlight,

    /// <summary>The declared ion count, flown independently through one solved field.</summary>
    Ensemble,

    /// <summary>The source's declared cloud, or the declared ion count when it declares none.</summary>
    Cloud,
}

/// <summary>One figure of merit a study may ask for by name.</summary>
/// <param name="Name">The name a study file uses.</param>
/// <param name="Unit">The unit it is reported in.</param>
/// <param name="Description">What it measures.</param>
/// <param name="LargerIsBetter">
/// Which way is better, so an optimisation need not restate it. A sign error in an
/// objective does not throw; it returns the worst design in the box and looks like
/// a result.
/// </param>
/// <param name="Class">
/// <param name="Basis">How many flights one evaluation of it costs.</param>
/// Which of §12's families it belongs to, or <see cref="AccuracyClass.None"/> where it
/// is not one of §12's figures at all.
/// </param>
public sealed record FigureOfMeritInfo(
    string Name, string Unit, string Description, bool LargerIsBetter,
    AccuracyClass Class = AccuracyClass.None,
    FlightBasis Basis = FlightBasis.Ensemble)
{
    /// <summary>
    /// The physical dimension, derived from the unit rather than stated beside it.
    /// </summary>
    /// <remarks>
    /// Two fields that must agree are one field too many. Deriving means a figure
    /// reported in microseconds cannot be declared dimensionless by an oversight,
    /// which is exactly the class of mistake GRD-1's insistence on units exists to
    /// catch.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public Core.Units.Dimension Dimension => Core.Units.Quantity.From(1.0, Unit).Dimension;
}

/// <summary>
/// The figures of merit a study or an optimisation can name in a file.
/// </summary>
/// <remarks>
/// <para>
/// A sweep driver takes a function from a validated model to a number, which is
/// what makes it device-agnostic. A study <em>file</em> cannot carry a function,
/// so it names one, and this is the registry it names into.
/// </para>
/// <para>
/// Four to begin with, chosen because they answer the questions the tolerance
/// machinery exists for. Section 12's Python objectives will register here too
/// when extensions land; the registry is the seam that keeps that from being a
/// change to the sweep drivers.
/// </para>
/// <para>
/// Everything here is Class T: deterministic trajectories through a static field,
/// no collisions, no space charge. An ensemble here is a spread of launch
/// energies, not a thermal distribution.
/// </para>
/// </remarks>
public static class FiguresOfMerit
{
    /// <summary>How wide an energy spread the ensemble figures sample, as a fraction.</summary>
    /// <remarks>
    /// <para>
    /// Plus or minus three per cent, which is the acceptance the companion memo
    /// asks a mirror to hold. A study may override it.
    /// </para>
    /// <para>
    /// It is a <em>deterministic</em> sweep of the acceptance, evenly spaced from
    /// one end to the other, not a Gaussian draw - so the seed does not enter and
    /// two runs of the same study agree exactly. A source cloud's
    /// <c>energyFractionSpread</c> is the other thing: a random draw about the
    /// nominal, which gives a different and generally lower resolving power for the
    /// same number because the ions are distributed rather than ranked. Someone
    /// comparing the two by hand read the difference as noise in the objective,
    /// which is the confusion this paragraph exists to prevent.
    /// </para>
    /// </remarks>
    public const double DefaultEnergySpread = 0.03;

    /// <summary>How many ions the ensemble figures launch.</summary>
    /// <remarks>
    /// Enough to resolve a peak width, few enough that a thousand-draw tolerance
    /// study is not a thousand ensembles too many. A study may override it.
    /// </remarks>
    public const int DefaultIons = 21;

    /// <summary>
    /// Every figure this build computes, with §12's class on each.
    /// </summary>
    /// <remarks>
    /// <b>Two are deliberately in no class.</b> <c>flightTime</c> is the raw arrival
    /// quantity the Class T figures are computed <em>from</em>, and <c>energyDrift</c>
    /// says in its own description that it is a diagnostic rather than a design target.
    /// §12 lists neither, and assigning them a class to make the grouping tidy would
    /// weaken what the grouping says about the twelve that do belong to one.
    /// </remarks>
    private static readonly FigureOfMeritInfo[] Catalogue =
    [
        new("flightTime", "us", "Arrival time at the detector, from a convergence study over three integrator tolerances.", false, AccuracyClass.Trajectory, FlightBasis.ConvergenceLadder),
        new("energyDrift", "1", "Largest relative departure of total energy over the flight. The ACC-4 budget is 1e-6; this is a diagnostic, not a design target.", false, AccuracyClass.None, FlightBasis.ConvergenceLadder),
        new("resolvingPower", "1", "Arrival-time resolving power across the energy spread, model-free at half maximum.", true, AccuracyClass.Trajectory, FlightBasis.Ensemble),
        new("transmission", "1", "Fraction of launched ions that reach the detector.", true, AccuracyClass.Statistical, FlightBasis.Ensemble),
        new("arrivalSpread", "ns", "Full width at half maximum of the arrival-time peak, from the source cloud.", false, AccuracyClass.Trajectory, FlightBasis.Ensemble),
        new("turnAroundTime", "ns", "The part of the arrival spread imposed before the ion leaves, by the thermal velocity of the source. What limits a pulsed extraction.", false, AccuracyClass.Trajectory, FlightBasis.Cloud),
        new("emittance", "um", "Geometric emittance of the arriving packet in its wider transverse plane. A micrometre is a millimetre-milliradian, so the number reads in the conventional unit. Smaller passes through a smaller aperture.", false, AccuracyClass.Trajectory, FlightBasis.Cloud),
        new("normalisedEmittance", "um", "The same area measured against transverse momentum, so it survives acceleration. The figure to compare a source by, since a geometric emittance can be improved by acceleration alone.", false, AccuracyClass.Trajectory, FlightBasis.Cloud),
        new("confined", "1", "Fraction of launched ions still inside at the end of the run: neither struck on a surface nor escaped past the detector. What a trap is measured by, since a trapped ion by definition never arrives anywhere.", true, AccuracyClass.Statistical, FlightBasis.Ensemble),
        new("transitTime", "us", "Mean time for a diffusive run's density to reach the collecting boundary, weighted by how much arrived in each bin. What a density has instead of a flight time.", false, AccuracyClass.Statistical, FlightBasis.Cloud),
        new("radialSpread", "mm", "Population-weighted standard deviation of a diffusive run's density across the direction of travel, about the packet's own centroid - radial in an axisymmetric solve, transverse in a cross-section. What confinement is measured by: a guide that holds its ions keeps this bounded, and one that does not lets it grow as the square root of time. Lower is tighter, but a floor set by the temperature and the well depth means zero is not the target.", false, AccuracyClass.Statistical, FlightBasis.Cloud),
        new("mobilityResolvingPower", "1", "Mobility resolving power K/dK of a trapped ion mobility analyser's elution scan, read as V/dV: the ramped parameter's value at the peak of the arrival-time distribution, over how far that parameter moves during the peak's full width at half maximum. The peak is the ion-weighted mean arrival and the width is the Gaussian-equivalent 2 sqrt(2 ln 2) sigma, because the arrivals are binned at the density solver's own step and a literal half-maximum of that histogram is lumpy. Equals K/dK on the assumption that the release parameter goes as 1/K, which holds when it scales a field linear in position - a tunnel's exit potential does. Needs a diffusive model whose sequence ramps exactly one parameter; a held field releases nothing and has no scan to resolve against.", true, AccuracyClass.Boundary, FlightBasis.Cloud),
        new("meanKineticEnergy", "eV", "Mean kinetic energy of the ions still in flight at the end, over the source cloud. The survivors rather than the arrivals, because a thermalised packet has no preferred direction and selecting on arrival would select the fast ones. Against a gas this is what equipartition fixes at (3/2)kT, which is the sharpest check the collision models have - and it is a target rather than something to maximise.", false, AccuracyClass.Statistical, FlightBasis.Cloud),
        new("focusingC1", "1", "Magnitude of the first-order time-energy coefficient c1 in T/T0 = 1 + c1 d + c2 d^2 + ..., where d is the fractional energy offset. Zero is a first-order energy focus, which is what a multi-reflection analyser is tuned to; a mirror with c1 uncancelled has a resolving power falling as one over the energy spread rather than as its square. Reported as a magnitude because the target is zero from either side, and because an optimiser minimising a signed coefficient would drive it to minus infinity. Measured from a deterministic energy scan, never from a declared cloud - the scan is designed rather than drawn.", false, AccuracyClass.Trajectory, FlightBasis.Ensemble),
        new("focusingC2", "1", "The same for the second-order coefficient c2. A single-stage mirror at its first-order focus has c2 of order one half; a two-stage mirror cancels it too and its resolving power falls only as the cube of the energy spread. Minimise this AFTER c1, or combine the two in a Python objective - a weighted sum of the two is a design choice rather than a figure this build should pick for you.", false, AccuracyClass.Trajectory, FlightBasis.Ensemble),
        new("focusingC3", "1", "The same for the third-order coefficient c3. With c1 and c2 both cancelled this is what binds, and a mirror pair holding all three near zero is what the literature calls a third-order temporal focus - the regime the Astral's mirrors are published as operating in. The fit is cubic, so this is its highest coefficient and the one most exposed to the fit residual; read it with focusing.fit-residual in view.", false, AccuracyClass.Trajectory, FlightBasis.Ensemble),
        new("energyPlateau", "1", "Peak-to-peak excursion of the flight time across the declared energy spread, as a fraction of the nominal flight time - the quantity a multi-reflection mirror's plateau condition bounds, and what resolving power over that window is one over twice. Read from the raw energy scan with no fit, so a plateau with a dip in the middle is measured as wide as it is. Prefer this to resolvingPower as an optimiser objective for a mirror: the half-maximum width of a parabolic peak is flat near its own optimum and a search collapses onto a point without settling. The uncertainty carried is the largest step between adjacent samples, a bound on what the grid could have missed.", false, AccuracyClass.Trajectory, FlightBasis.Ensemble),
        new("oscillationFrequencyX", "kHz", "Strongest periodic line in the ion's motion along x, over the whole record. Unlike secularFrequencyX this does not need a drive: it is the frequency of whatever the ion is actually doing, which for an electrostatic orbital trap is the axial oscillation the instrument measures mass by. In a driven field it will find the drive itself, which is why the secular figures exist separately and exclude it.", false, AccuracyClass.Boundary, FlightBasis.OneFlight),
        new("oscillationFrequencyY", "kHz", "The same along y.", false, AccuracyClass.Boundary, FlightBasis.OneFlight),
        new("oscillationFrequencyZ", "kHz", "The same along z.", false, AccuracyClass.Boundary, FlightBasis.OneFlight),
        new("secularFrequencyX", "kHz", "Strongest line in the ion's motion along x, below the drive. In a driven field an ion oscillates slowly in the effective well and quickly at the drive; this is the slow one, and it is what a resonance condition is written in. Needs a driven field - a static one has no secular motion to have a frequency.", false, AccuracyClass.Boundary, FlightBasis.OneFlight),
        new("secularFrequencyY", "kHz", "The same along y.", false, AccuracyClass.Boundary, FlightBasis.OneFlight),
        new("secularFrequencyZ", "kHz", "The same along z.", false, AccuracyClass.Boundary, FlightBasis.OneFlight),
    ];

    /// <summary>Every figure of merit that can be named, ordered by name.</summary>
    public static IReadOnlyList<FigureOfMeritInfo> All =>
        [.. Catalogue.OrderBy(f => f.Name, StringComparer.Ordinal)];

    /// <summary>How many flights one evaluation of a named figure costs.</summary>
    /// <param name="figureOfMerit">The figure the study names, or null for the default.</param>
    /// <param name="ions">The study's declared ion count.</param>
    /// <param name="cloud">The source's declared cloud size, or null when it declares none.</param>
    /// <returns>The number of trajectories one evaluation flies.</returns>
    /// <remarks>
    /// The estimate charged <paramref name="ions"/> for every figure, which is right for an
    /// ensemble and wrong by sevenfold for a convergence ladder. An unknown name falls back
    /// to the ensemble count, which is the conservative direction: over-charging a figure
    /// nobody has classified is better than under-charging it, since the gate exists to stop
    /// a surprise rather than to permit one.
    /// </remarks>
    public static int FlightsPerEvaluation(string? figureOfMerit, int ions, int? cloud)
    {
        var info = figureOfMerit is null
            ? null
            : Catalogue.FirstOrDefault(
                f => string.Equals(f.Name, figureOfMerit, StringComparison.Ordinal));

        return (info?.Basis ?? FlightBasis.Ensemble) switch
        {
            FlightBasis.ConvergenceLadder => FlightTimeStudy.DefaultRefinements,
            FlightBasis.OneFlight => 1,
            FlightBasis.Cloud => cloud ?? ions,
            _ => ions,
        };
    }

    /// <summary>Looks one up.</summary>
    /// <param name="name">The name a study file used.</param>
    /// <returns>What it measures.</returns>
    /// <exception cref="EinzelException">No figure of merit by that name.</exception>
    public static FigureOfMeritInfo Describe(string name)
    {
        foreach (var candidate in Catalogue)
        {
            if (string.Equals(candidate.Name, name, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = "/figureOfMerit",
            Constraint = $"'{name}' is not a figure of merit this build computes",
            Suggestion = $"available: {string.Join(", ", All.Select(f => f.Name))}",
        });
    }

    /// <summary>Computes one figure with its GRD-1 envelope, where this build can.</summary>
    /// <param name="name">Which figure of merit.</param>
    /// <param name="model">The validated model.</param>
    /// <param name="energySpread">Fractional energy spread for the ensemble figures.</param>
    /// <param name="ions">How many ions the ensemble figures launch.</param>
    /// <param name="report">Where the warnings the evaluation earns are sent, or null.</param>
    /// <param name="store">Where solved field channels are loaded from and kept, or null to solve them.</param>
    /// <returns>The figure with its interval, or null where this build has no envelope for it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="EinzelException">No figure of merit by that name.</exception>
    /// <remarks>
    /// <para>
    /// <b>The counterpart to <see cref="Evaluator"/>, and the reason it needed one.</b> The
    /// evaluator hands a driver a bare double because ranking needs an ordering and an
    /// envelope has none - a deliberate exception to GRD-1, argued where it is taken. The
    /// consequence nobody had written down is that most figures existed <em>only</em> in the
    /// excepted form: there was no way to ask this build for a turn-around time with an
    /// uncertainty on it.
    /// </para>
    /// <para>
    /// <b>The interval is the sampling uncertainty, by resampling the cloud.</b> Most of
    /// these figures are statistics of an ion cloud and only the fractions had a closed
    /// form; <see cref="Bootstrap"/> covers a width, a mean and a ratio alike, and assumes
    /// nothing about the distribution - which matters because an arrival-time peak is
    /// measurably skew. What it does <em>not</em> measure is the discretisation, the
    /// integrator or the model, so the evidence names the sample size rather than claiming a
    /// confidence in the answer.
    /// </para>
    /// <para>
    /// <b>Null where there is no envelope, never a bare number dressed as one.</b> A figure
    /// this build can only rank by comes back absent, and the caller says so - which is the
    /// whole point, since printing an unqualified value would be the failure GRD-1 exists to
    /// prevent.
    /// </para>
    /// </remarks>
    public static Core.Results.Measured? Measure(
        string name,
        CompiledModel model,
        double energySpread = DefaultEnergySpread,
        int ions = DefaultIons,
        Action<Core.Results.ValidityWarning>? report = null,
        ISolveStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        _ = Describe(name);

        return name switch
        {
            // The arrival-time peak's own statistics, resampled over the arrivals that
            // produced it. One flight serves all three.
            "arrivalSpread" => OverArrivals(
                model, store, energySpread, ions, report,
                peak => peak.GaussianEquivalentFwhmSeconds * 1e9, "ns"),

            "resolvingPower" => OverArrivals(
                model, store, energySpread, ions, report,
                peak => peak.Arrived >= 3 ? Resolving(peak) : null, "1"),

            // The fit's own residual is the uncertainty, divided by the span it was fitted
            // over: a coefficient multiplies d, so a relative flight-time residual of r across a
            // scan of half-width s constrains the coefficient to about r/s. GRD-1 wants an
            // envelope and this is the one the fit actually produces.
            "focusingC1" => FocusingMeasured(model, store, energySpread, ions, 0, report),
            "focusingC2" => FocusingMeasured(model, store, energySpread, ions, 1, report),
            "focusingC3" => FocusingMeasured(model, store, energySpread, ions, 2, report),
            "energyPlateau" => PlateauMeasured(model, store, energySpread, ions, report),

            "turnAroundTime" => TurnAroundMeasured(model, store, report),

            // A fraction, so a binomial standard error rather than a resampling - the
            // closed form is exact and the bootstrap would only approximate it.
            "transmission" => Fraction(
                model, store, energySpread, ions, report,
                peak => (peak.Arrived, peak.Launched), "1"),

            _ => null,
        };
    }

    /// <summary>A peak's model-free resolving power, as a bare number for resampling.</summary>
    /// <remarks>
    /// The envelope on <c>ArrivalTimePeak.ResolvingPower</c> is the binomial one for the
    /// arrival count; what is wanted per replicate is the value alone, so the deconstruction
    /// GRD-1 permits is used rather than a second implementation of the ratio.
    /// </remarks>
    private static double? Resolving(ArrivalTimePeak peak)
    {
        var (value, _, _, _) = peak.ResolvingPower();

        return double.IsFinite(value.SiValue) ? value.SiValue : null;
    }

    /// <summary>Resamples a statistic of the arrival-time peak.</summary>
    /// <remarks>
    /// <para>
    /// <b>Only where the model declares an ion cloud, and that is not a limitation but the
    /// distinction.</b> Without one, the ensemble helper sweeps the energy acceptance
    /// <em>deterministically</em> - evenly spaced from one end to the other, so the seed
    /// does not enter and two runs agree exactly. That is a designed scan, not a sample
    /// drawn from a population, and resampling it would report the scan's own spacing as
    /// though it were a sampling error.
    /// </para>
    /// <para>
    /// The two have been confused here before: <c>DefaultEnergySpread</c>'s remarks exist
    /// because somebody compared a deterministic sweep with a cloud's random draw and read
    /// the difference as noise in the objective. Putting an interval on the sweep would
    /// make that mistake structural.
    /// </para>
    /// </remarks>
    private static Core.Results.Measured? OverArrivals(
        CompiledModel model,
        ISolveStore? store,
        double spread,
        int ions,
        Action<Core.Results.ValidityWarning>? report,
        Func<ArrivalTimePeak, double?> statistic,
        string unit)
    {
        if (!model.Cloud.IsCloud
            || Ensemble(model, store, spread, ions, report) is not { } peak
            || peak.Arrived < 2)
        {
            return null;
        }

        var launched = peak.Launched;

        return Bootstrap.Measure(
            [.. peak.Arrivals],
            draw => draw.Count >= 2
                ? statistic(ArrivalTimePeak.FromArrivals(draw, launched))
                : null,
            unit);
    }

    /// <summary>A fraction of the launched ions, with its binomial error.</summary>
    private static Core.Results.Measured? Fraction(
        CompiledModel model,
        ISolveStore? store,
        double spread,
        int ions,
        Action<Core.Results.ValidityWarning>? report,
        Func<ArrivalTimePeak, (int Of, int Out)> counts,
        string unit)
    {
        if (!model.Cloud.IsCloud || Ensemble(model, store, spread, ions, report) is not { } peak)
        {
            return null;
        }

        var (of, outOf) = counts(peak);

        if (outOf <= 0)
        {
            return null;
        }

        _ = unit;

        // The peak's own transmission envelope, which already floors the interval so a
        // perfect ensemble does not claim certainty from a finite sample.
        return peak.Transmission();
    }

    /// <summary>Turn-around time, resampled over the thermal-only cloud that measures it.</summary>
    /// <remarks>
    /// Zero with no interval where the source has no temperature: that is a measurement of
    /// something absent rather than a failure to measure, and resampling nothing would
    /// report a spread on a quantity that is exactly nought by construction.
    /// </remarks>
    private static Core.Results.Measured? TurnAroundMeasured(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report)
    {
        if (model.Cloud.TemperatureK <= 0.0)
        {
            var zero = Core.Units.Quantity.From(0.0, "ns");

            return new Core.Results.Measured(
                zero,
                Core.Results.UncertaintyInterval.Symmetric(zero, zero, 0.68),
                new Core.Results.Evidence.Analytic(
                    "no source temperature, so no thermal turn-around"));
        }

        if (!model.Cloud.IsCloud)
        {
            return null;
        }

        var thermalOnly = ThermalOnly(model);

        if (FromCloud(thermalOnly, report, store) is not { } peak || peak.Arrived < 2)
        {
            return null;
        }

        var launched = peak.Launched;

        return Bootstrap.Measure(
            [.. peak.Arrivals],
            draw => draw.Count >= 2
                ? ArrivalTimePeak.FromArrivals(draw, launched).GaussianEquivalentFwhmSeconds * 1e9
                : null,
            "ns");
    }

    /// <summary>Builds the evaluator a sweep or an optimiser drives.</summary>
    /// <param name="name">Which figure of merit.</param>
    /// <param name="energySpread">Fractional energy spread for the ensemble figures.</param>
    /// <param name="ions">How many ions the ensemble figures launch.</param>
    /// <param name="report">
    /// Where the warnings each evaluation earns are sent, or null to refuse rather
    /// than hide them.
    /// </param>
    /// <param name="store">Where solved field channels are loaded from and kept, or null to solve them.</param>
    /// <returns>A function from a validated model to the figure, or null when it does not arrive.</returns>
    /// <exception cref="EinzelException">No figure of merit by that name.</exception>
    /// <remarks>
    /// GRD-2: an evaluator hands a driver a bare double to rank by, and every
    /// warning the flight behind it earned used to stop here. A study could be
    /// outside the validity of its own transport mode on every draw and report a
    /// distribution with nothing attached to say so. The sink is how they cross.
    /// </remarks>
    public static Func<CompiledModel, double?> Evaluator(
        string name,
        double energySpread = DefaultEnergySpread,
        int ions = DefaultIons,
        Action<Core.Results.ValidityWarning>? report = null,
        ISolveStore? store = null)
    {
        var info = Describe(name);

        return info.Name switch
        {
            "flightTime" => model => Single(model, store, report)?.FlightTimeSeconds,
            "energyDrift" => model => Single(model, store, report)?.MaximumRelativeEnergyDrift,
            "resolvingPower" => model => Ensemble(model, store, energySpread, ions, report) is { Arrived: >= 3 } peak
                ? Magnitude(peak.ResolvingPower(), report)
                : null,
            "transmission" => model => Transmitted(model, store, energySpread, ions, report),
            "arrivalSpread" => model => Ensemble(model, store, energySpread, ions, report) is { Arrived: >= 3 } peak
                ? peak.GaussianEquivalentFwhmSeconds
                : null,
            "focusingC1" => model => Focusing(model, store, energySpread, ions, 0, report),
            "focusingC2" => model => Focusing(model, store, energySpread, ions, 1, report),
            "focusingC3" => model => Focusing(model, store, energySpread, ions, 2, report),
            "energyPlateau" => model => Plateau(model, store, energySpread, ions, report),
            "turnAroundTime" => model => TurnAround(model, store, report),
            "emittance" => model => PacketEmittance(model, store, report)?.Wider.GeometricM,
            "normalisedEmittance" => model => PacketEmittance(model, store, report)?.Wider.NormalisedM,
            "confined" => model => Confined(model, store, energySpread, ions, report),
            "meanKineticEnergy" => model => MeanKineticEnergy(model, store, report),
            "transitTime" => model => Transit(model, store, report),
            "radialSpread" => model => RadialSpread(model, store, report),
            "mobilityResolvingPower" => model => MobilityResolvingPower(model, store, report),
            "oscillationFrequencyX" => model => Oscillation(model, store, 0, report),
            "oscillationFrequencyY" => model => Oscillation(model, store, 1, report),
            "oscillationFrequencyZ" => model => Oscillation(model, store, 2, report),
            "secularFrequencyX" => model => Secular(model, store, 0, report),
            "secularFrequencyY" => model => Secular(model, store, 1, report),
            "secularFrequencyZ" => model => Secular(model, store, 2, report),
            _ => throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.InternalError,
                Path = "/figureOfMerit",
                Constraint = $"'{info.Name}' is catalogued but has no evaluator",
                Suggestion = "this is a defect in einzel; please report it",
            }),
        };
    }

    /// <summary>
    /// The magnitude out of a GRD-1 envelope, at the one place that is legitimate.
    /// </summary>
    /// <remarks>
    /// A sweep driver needs a scalar to rank draws by, and there is no ordering on
    /// an envelope. The discard is deliberate and visible, which is exactly the
    /// act GRD-1 is designed to make greppable rather than impossible - and it is
    /// only the <em>interval</em> that is dropped. The warnings go to the sink,
    /// because a ranking has no use for them and a study does.
    /// </remarks>
    private static double Magnitude(
        Core.Results.Measured measured, Action<Core.Results.ValidityWarning>? report = null)
    {
        var (value, _, _, warnings) = measured;

        Forward(warnings, report);

        return value.SiValue;
    }

    /// <summary>Sends warnings to the sink, if there is one.</summary>
    private static void Forward(
        IReadOnlyList<Core.Results.ValidityWarning> warnings,
        Action<Core.Results.ValidityWarning>? report)
    {
        if (report is null)
        {
            return;
        }

        foreach (var warning in warnings)
        {
            report(warning);
        }
    }

    /// <summary>One trajectory, with its convergence study.</summary>
    private static TrajectoryResult? Single(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report = null)
    {
        var (launch, species, field, settings, detector, collisions) = Setup(model, store, report: report);

        // The same convergence study 'run' reports from, and the finest level of it,
        // because that is the number 'run' publishes.
        //
        // This used to be one integration at the declared tolerance while 'run' did
        // three and reported the finest. Both were defensible and they disagreed:
        // 1.3e-10 in flight time on the shipped reflectron, and five orders in
        // energy drift - 3.1e-15 against 1.0e-9. So the most obvious workflow there
        // is, quote what 'run' prints and pin it with 'einzel test', failed, and the
        // failure said nothing about why. An agent attempting the acceptance suite
        // fell into it, and only caught it because it had already derived the closed
        // form by hand.
        //
        // Richardson is why this direction and not the other: the best estimate is
        // the finest level and the uncertainty is how far the next-coarsest sits
        // from it, so reporting the coarse value with an interval around it would be
        // centring the answer on the least accurate point available. The cost is
        // three integrations per evaluation instead of one, which a sweep pays.
        var study = FlightTimeStudy.Run(
            launch, species, field, settings, detector, collisions: collisions);

        var (_, _, _, studyWarnings) = study.FlightTime;
        Forward(studyWarnings, report);

        var result = study.Runs[^1];

        // A draw whose ion never reaches the detector has no flight time. That is
        // a result about the geometry, not a failure of the study, and the sweep
        // records it as such.
        return result.Outcome == TrajectoryOutcome.StopConditionMet ? result : null;
    }

    /// <summary>
    /// The ensemble a figure of merit is computed over.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A model that declares a source cloud is asking for that cloud, and gets it:
    /// a real spread in where the ions were, which way they were going, and how
    /// fast. That is what makes a resolving power a property of the instrument
    /// rather than of one ion.
    /// </para>
    /// <para>
    /// A model that declares none falls back to a deterministic sweep of launch
    /// energy, which is what these figures have always meant and what every
    /// existing study is calibrated against. Evenly spaced rather than random,
    /// because a tolerance study is already a Monte Carlo over geometry and
    /// sampling the energy randomly inside it would put noise on every draw and
    /// call it physics.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The energy scan the focusing fit needs: fractional offset paired with flight time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Deliberately ignores a declared cloud.</b> Every other ensemble figure here flies
    /// the cloud when there is one, because those figures are properties of a packet. A
    /// focusing coefficient is a property of the <i>optics</i>: it is the derivative of
    /// flight time with respect to energy, and measuring it needs ions placed at known
    /// offsets, not drawn from a distribution. Flying a cloud would give a scatter to fit
    /// through instead of a curve, which is the same confusion
    /// <see cref="DefaultEnergySpread"/>'s remarks exist to prevent for the bootstrap.
    /// </para>
    /// <para>
    /// One solve for the whole scan, since an energy offset changes how fast the ion is
    /// launched and nothing about the field.
    /// </para>
    /// </remarks>
    private static List<(double EnergyFraction, double FlightTime)> EnergyScan(
        CompiledModel model, ISolveStore? store,
        double spread, int ions, Action<Core.Results.ValidityWarning>? report)
    {
        var samples = new List<(double, double)>(ions);
        var members = Math.Max(3, ions);
        var (_, species, field, settings, detector, collisions) = Setup(model, store, report: report);

        for (var k = 0; k < members; k++)
        {
            var fraction = (2.0 * k / (members - 1.0)) - 1.0;
            var offset = spread * fraction;

            var result = TrajectoryIntegrator.Integrate(
                LaunchAt(model, offset), species, field, settings, detector,
                collisions: collisions?.Invoke());

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                samples.Add((offset, result.FlightTimeSeconds));
            }
        }

        return samples;
    }

    /// <summary>Fits the energy scan and returns one coefficient's magnitude.</summary>
    private static double? Focusing(
        CompiledModel model, ISolveStore? store, double spread, int ions, int index,
        Action<Core.Results.ValidityWarning>? report)
    {
        if (spread <= 0.0)
        {
            return null;
        }

        var samples = EnergyScan(model, store, spread, ions, report);

        // A fit needs both signs and more points than coefficients. Fewer means ions were
        // lost across the scan, which is a transmission finding rather than a focusing one.
        if (samples.Count < 5 || samples[0].EnergyFraction >= 0.0 || samples[^1].EnergyFraction <= 0.0)
        {
            return null;
        }

        var fit = FocusingAnalysis.Fit(samples);
        return index < fit.Coefficients.Count ? Math.Abs(fit.Coefficients[index]) : null;
    }

    /// <summary>
    /// The plateau excursion: (max T - min T) / T0 over the raw energy scan, and the
    /// largest adjacent step as a bound on what the grid missed.
    /// </summary>
    private static (double Excursion, double GridBound, int Count)? PlateauScan(
        CompiledModel model, ISolveStore? store,
        double spread, int ions, Action<Core.Results.ValidityWarning>? report)
    {
        if (spread <= 0.0)
        {
            return null;
        }

        var samples = EnergyScan(model, store, spread, ions, report);

        // The whole scan has to have arrived, or the excursion is over a different population
        // than the one asked about - the incomplete-arrival trap this project already records.
        if (samples.Count < Math.Max(3, ions))
        {
            return null;
        }

        var nominal = samples.MinBy(sample => Math.Abs(sample.EnergyFraction)).FlightTime;
        if (nominal <= 0.0)
        {
            return null;
        }

        var times = samples.Select(sample => sample.FlightTime).ToList();
        var excursion = (times.Max() - times.Min()) / nominal;
        var gridBound = 0.0;
        for (var k = 1; k < times.Count; k++)
        {
            gridBound = Math.Max(gridBound, Math.Abs(times[k] - times[k - 1]) / nominal);
        }

        return (excursion, gridBound, samples.Count);
    }

    /// <summary>The plateau excursion as a bare ordering for the study drivers.</summary>
    private static double? Plateau(
        CompiledModel model, ISolveStore? store,
        double spread, int ions, Action<Core.Results.ValidityWarning>? report)
        => PlateauScan(model, store, spread, ions, report)?.Excursion;

    /// <summary>The plateau excursion with the grid bound as its GRD-1 envelope.</summary>
    private static Core.Results.Measured? PlateauMeasured(
        CompiledModel model, ISolveStore? store,
        double spread, int ions, Action<Core.Results.ValidityWarning>? report)
    {
        var scan = PlateauScan(model, store, spread, ions, report);
        if (scan is null)
        {
            return null;
        }

        var (excursion, gridBound, count) = scan.Value;
        var quantity = Core.Units.Quantity.Number(excursion);
        return new Core.Results.Measured(
            quantity,
            Core.Results.UncertaintyInterval.Symmetric(
                quantity, Core.Units.Quantity.Number(gridBound), confidenceLevel: 1.0),
            new Core.Results.Evidence.Ensemble(count, Converged: count >= 9));
    }

    /// <summary>The same with the fit's residual carried as the GRD-1 envelope.</summary>
    private static Core.Results.Measured? FocusingMeasured(
        CompiledModel model, ISolveStore? store, double spread, int ions, int index,
        Action<Core.Results.ValidityWarning>? report)
    {
        if (spread <= 0.0)
        {
            return null;
        }

        var samples = EnergyScan(model, store, spread, ions, report);

        if (samples.Count < 5 || samples[0].EnergyFraction >= 0.0 || samples[^1].EnergyFraction <= 0.0)
        {
            return null;
        }

        var fit = FocusingAnalysis.Fit(samples);

        if (index >= fit.Coefficients.Count)
        {
            return null;
        }

        var value = Math.Abs(fit.Coefficients[index]);
        var quantity = Core.Units.Quantity.Number(value);
        var warnings = new List<Core.Results.ValidityWarning>();

        // A residual comparable to the coefficient means the cubic did not capture the
        // behaviour, so the order reported is not the order binding. Said rather than
        // hidden: this is the one way a focusing fit misleads.
        if (fit.ResidualOfFit > 0.1 * spread * Math.Max(value, 1e-12))
        {
            warnings.Add(new Core.Results.ValidityWarning(
                "focusing.fit-residual",
                $"the energy scan fits T/T0 = 1 + c1 d + ... to an RMS residual of "
                + $"{fit.ResidualOfFit:E3}, which is not small against the coefficient being "
                + "reported - the expansion may not have captured the behaviour, and the "
                + "binding order should not be believed",
                Core.Results.WarningSeverity.Qualified));
        }

        return new Core.Results.Measured(
            quantity,
            Core.Results.UncertaintyInterval.Symmetric(
                quantity,
                Core.Units.Quantity.Number(fit.ResidualOfFit / spread),
                confidenceLevel: 0.68),
            new Core.Results.Evidence.Ensemble(samples.Count, Converged: fit.ResidualOfFit < 1e-6),
            warnings);
    }

    private static ArrivalTimePeak? Ensemble(
        CompiledModel model, ISolveStore? store,
        double spread, int ions, Action<Core.Results.ValidityWarning>? report = null)
    {
        if (model.Cloud.IsCloud)
        {
            return FromCloud(model, report, store);
        }

        var arrivals = new List<double>(ions);

        // Collapsed where the spread varies nothing. Twenty-one identical flight
        // times would otherwise form a peak of exactly zero width and a resolving
        // power of infinity, which is a confident answer to a question that was
        // never asked; one arrival is no peak at all, and says so.
        var members = Distinct(model, spread, ions, report);

        // SOLVED ONCE, NOT ONCE PER MEMBER. An energy offset changes how fast the ion
        // is launched and nothing whatever about the field it flies through, so the
        // solve inside this loop was the same solve every time. On the shipped mirror
        // pair that was 249 ms per ion of which the flight is about 50: twenty-one ions
        // cost 5.23 s where the work is one solve and twenty-one flights.
        //
        // The saving grows with the solve, so it is largest exactly where it matters -
        // a volume geometry whose solve is seconds and whose flight is milliseconds.
        // `FlyCloud` has always shared one field across every ion in a cloud, so this
        // is that established arrangement applied to the sweep beside it.
        var (_, species, field, settings, detector, collisions) = Setup(model, store, report: report);

        for (var k = 0; k < members; k++)
        {
            var fraction = members == 1 ? 0.0 : (2.0 * k / (members - 1.0)) - 1.0;
            var offset = spread * fraction;

            var result = TrajectoryIntegrator.Integrate(
                LaunchAt(model, offset), species, field, settings, detector,
                collisions: collisions?.Invoke());

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrivals.Add(result.FlightTimeSeconds);
            }
        }

        return arrivals.Count >= 2 ? ArrivalTimePeak.FromArrivals(arrivals, members) : null;
    }

    /// <summary>Flies the cloud a model declares.</summary>
    /// <param name="model">The validated model.</param>
    /// <returns>The arrival-time peak it forms.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <param name="report">Where the warnings the flight earns are sent, or null.</param>
    /// <param name="store">Where solved field channels are loaded from and kept, or null to solve them.</param>
    /// <exception cref="ArgumentException">Fewer than two ions arrived.</exception>
    public static ArrivalTimePeak? FromCloud(
        CompiledModel model, Action<Core.Results.ValidityWarning>? report = null, ISolveStore? store = null) =>
        FlyCloud(model, report, store).Peak;

    /// <summary>
    /// Flies the cloud a model declares, keeping both when the ions arrived and
    /// where they were going when they did.
    /// </summary>
    /// <param name="model">The validated model.</param>
    /// <param name="report">Where the warnings the flight earns are sent, or null.</param>
    /// <param name="store">Where solved field channels are loaded from and kept, or null to solve them.</param>
    /// <returns>The arrival-time peak and the arriving ions.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="model"/> is null.</exception>
    /// <exception cref="ArgumentException">Fewer than two ions arrived.</exception>
    /// <remarks>
    /// The arrival times answer how a peak is shaped and the final states answer
    /// whether the packet would survive the next aperture. Both come out of one
    /// flight because flying twice for them would double the cost of every
    /// ensemble run to recompute something already in hand.
    /// </remarks>
    public static CloudFlight FlyCloud(
        CompiledModel model, Action<Core.Results.ValidityWarning>? report = null, ISolveStore? store = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var (nominal, species, field, settings, detector, _) = Setup(model, store, report: report);
        // The declared direction, so a packet at rest still knows which way is
        // downstream. For a moving ion it is redundant and ignored.
        var cloud = IonCloud.Draw(in nominal, species, model.Cloud, model.SourceDirection);

        if (model.ModelsSpaceCharge)
        {
            return FlyTogether(model, cloud, species, field, settings, detector);
        }

        var arrivals = new List<double>(cloud.Length);
        var arrived = new List<PhaseState>(cloud.Length);
        var losses = new Dictionary<string, int>(StringComparer.Ordinal);

        var gas = DiffusionRun.GasFor(model);
        var collisions = 0;
        var scattered = 0;
        var remaining = new List<PhaseState>();
        var events = new List<IonEvent>(cloud.Length);

        // One stream per ion, derived from the declared seed and the ion's position in the
        // cloud, so a run is reproducible from its manifest and raising the ion count does
        // not change the flight of any ion already drawn. The ion-list core keeps that rule
        // and flies one at a time here: a figure of merit is evaluated inside a study's own
        // parallel loop, and a run's cloud stays the flight it always was.
        var rows = new IonRow[cloud.Length];

        for (var index = 0; index < cloud.Length; index++)
        {
            rows[index] = new IonRow(index, species, cloud[index], 0.0, string.Empty);
        }

        var flights = IonListFlight.Fly(
            rows, field, settings, detector, gas, model.Gas.Seed, maxParallelism: 1);

        for (var index = 0; index < cloud.Length; index++)
        {
            var flight = flights[index];

            if (flight.Failure is { } failure)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
            }

            var result = flight.Result!;
            events.Add(new IonEvent(index, result.Outcome.ToString(), result.StruckSurface, result.FlightTimeSeconds) { Position = result.FinalState.Position });

            if (gas.IsPresent)
            {
                collisions += flight.Collisions;

                if (flight.Collisions > 0)
                {
                    scattered++;
                }
            }

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                // COL-1: an ion that scattered and still reached the detector is
                // tracked to it with its arrival time recorded, not discarded as a
                // loss. Those late arrivals are the pedestal under the peak, and
                // dropping them would make an instrument look cleaner than it is.
                arrivals.Add(result.FlightTimeSeconds);
                arrived.Add(result.FinalState);
                continue;
            }

            // Named by surface where a surface is known, and by mechanism where it
            // is not - an ion that ran out of flight time was lost as surely as one
            // that hit metal, and ACC-5 asks for both.
            var channel = result.Outcome switch
            {
                TrajectoryOutcome.StruckElectrode => result.StruckSurface ?? "an electrode",
                TrajectoryOutcome.MaximumFlightTimeReached => "still in flight at the time limit",
                TrajectoryOutcome.MaximumStepsExceeded => "step limit reached",
                TrajectoryOutcome.StepSizeUnderflow => "step size underflow",
                _ => "unknown",
            };

            losses[channel] = losses.GetValueOrDefault(channel) + 1;

            if (result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached
                && result.StruckSurface is null)
            {
                remaining.Add(result.FinalState);
            }
        }

        return new CloudFlight(
            // Null rather than a throw when fewer than two arrived. A peak needs two
            // points to have a width; the flight around it is still a result, and its
            // itemised losses are most worth reading precisely when nothing arrived.
            arrivals.Count >= 2 ? ArrivalTimePeak.FromArrivals(arrivals, model.Cloud.Ions) : null,
            [.. arrived],
            [.. losses
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new LossChannel(pair.Key, pair.Value))],
            collisions,
            scattered)
        {
            Remaining = [.. remaining],
            Events = events,
        };
    }

    /// <summary>
    /// Flies the whole packet in lockstep, with its ions pushing on each other.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The ordinary loop flies ion 1 to its detector before ion 2 is launched, so
    /// there is no instant at which both exist. This one advances them together and
    /// recomputes their shared field at every stage, which is the only way the
    /// mutual force means anything.
    /// </para>
    /// <para>
    /// <b>The weighting is the cloud's own two fields.</b> <c>ions</c> is how many
    /// trajectories are computed and <c>population</c> is how many ions are
    /// physically present, which is exactly the macroparticle split - each computed
    /// trajectory stands in for <c>population / ions</c> real ions and carries their
    /// charge and their mass together. No third field was needed, and two fields
    /// that must agree would have been one too many.
    /// </para>
    /// <para>
    /// The softening length is the mean spacing between macroparticles, from the
    /// packet's own measured extent rather than from its declared spreads - a drawn
    /// cloud is a sample, and its realised radius is what the sum is actually over.
    /// </para>
    /// <para>
    /// Which method computes the mutual force is the model's choice and nothing here
    /// depends on the answer: both are <c>ISelfField</c>, which is what makes SC-1's
    /// "validated against the reference" a thing that can be done at all.
    /// </para>
    /// </remarks>
    private static CloudFlight FlyTogether(
        CompiledModel model,
        PhaseState[] cloud,
        IonSpecies species,
        IElectrostaticField field,
        IntegrationSettings settings,
        TrajectoryStopFunction detector)
    {
        var population = model.Cloud.Population ?? model.Cloud.Ions;

        var interaction = PacketInteraction(model, cloud, species, population);

        // The same gas, the same one-stream-per-ion seeding, as the independent path:
        // a run is reproducible from its manifest and raising the ion count does not
        // change the flight of any ion already drawn. A pushed packet used to refuse a
        // gas because this integrator had nowhere to put one; now the two act together,
        // which is the only way a cooled, space-charge-limited cloud can form.
        var gas = DiffusionRun.GasFor(model);
        var samplers = gas.IsPresent
            ? Enumerable.Range(0, cloud.Length)
                .Select(index => new Transport.Collisions.CollisionSampler(
                    gas, species.MassSi, species.ChargeSi, model.Gas.Seed + index))
                .ToList()
            : null;

        var result = Transport.Interaction.PacketIntegrator.Fly(
            cloud, species, field, interaction, settings, detector, samplers);

        var arrivals = new List<double>(cloud.Length);
        var arrived = new List<PhaseState>(cloud.Length);
        var losses = new Dictionary<string, int>(StringComparer.Ordinal);
        var events = new List<IonEvent>(cloud.Length);
        var collisions = 0;
        var scattered = 0;

        if (samplers is not null)
        {
            foreach (var sampler in samplers)
            {
                collisions += sampler.Collisions;

                if (sampler.Collisions > 0)
                {
                    scattered++;
                }
            }
        }

        foreach (var member in result.Members)
        {
            events.Add(new IonEvent(events.Count, member.Outcome.ToString(), member.StruckSurface, member.FlightTimeSeconds) { Position = member.FinalState.Position });

            if (member.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrivals.Add(member.FlightTimeSeconds);
                arrived.Add(member.FinalState);
                continue;
            }

            // The same channels the independent path names, so a loss itemisation
            // reads the same whether or not the ions pushed on each other.
            var channel = member.Outcome switch
            {
                TrajectoryOutcome.StruckElectrode => member.StruckSurface ?? "an electrode",
                TrajectoryOutcome.MaximumFlightTimeReached => "still in flight at the time limit",
                TrajectoryOutcome.MaximumStepsExceeded => "step limit reached",
                TrajectoryOutcome.StepSizeUnderflow => "step size underflow",
                _ => "unknown",
            };

            losses[channel] = losses.GetValueOrDefault(channel) + 1;
        }

        // Guarded exactly as the independent-ion path guards it: a packet that pushed
        // itself into the electrodes has no peak, and that is a result rather than a
        // defect. This path had the unguarded call, so a space-charge scan in which every
        // ion left through the rods ended in INTERNAL_ERROR - the failure Amendment 15
        // fixed on the other loop, reintroduced here.
        return new CloudFlight(
            arrivals.Count >= 2 ? ArrivalTimePeak.FromArrivals(arrivals, model.Cloud.Ions) : null,
            [.. arrived],
            [.. losses
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new LossChannel(pair.Key, pair.Value))],
            Collisions: collisions,
            ScatteredIons: scattered)
        {
            Events = events,
        };
    }

    /// <summary>Constructs the declared interaction from the packet actually entering a leg.</summary>
    /// <param name="model">The compiled model, for the declared method and its grid.</param>
    /// <param name="cloud">The macroparticles entering this leg, which set the softening.</param>
    /// <param name="species">The ion, for charge and mass.</param>
    /// <param name="population">How many real ions the packet represents.</param>
    /// <returns>The self-field the document asked for.</returns>
    internal static Transport.Interaction.ISelfField PacketInteraction(
        CompiledModel model, PhaseState[] cloud, IonSpecies species, double population) =>
        string.Equals(model.SpaceChargeMode, "pic", StringComparison.Ordinal)
            ? new Transport.Interaction.ParticleInCell(population, cloud.Length,
                species.ChargeSi, species.MassSi, model.SpaceChargeGrid?.Nodes ?? 32,
                model.SpaceChargeGrid?.Padding ?? 4.0, model.SpaceChargeGrid?.RefreshTolerance ?? 0.05)
            : new Transport.Interaction.CoulombInteraction(population, cloud.Length,
                species.ChargeSi, species.MassSi, RealisedSoftening(cloud));

    /// <summary>
    /// Softening from the packet's standard deviation along each axis, rather than
    /// from its longest dimension (SC-1).
    /// </summary>
    private static double RealisedSoftening(PhaseState[] cloud)
    {
        if (cloud.Length < 2)
        {
            return double.Epsilon;
        }

        var centre = default(Vec3);
        foreach (var one in cloud)
        {
            centre += one.Position;
        }

        centre *= 1.0 / cloud.Length;

        double sx = 0.0, sy = 0.0, sz = 0.0;
        foreach (var one in cloud)
        {
            var d = one.Position - centre;
            sx += d.X * d.X;
            sy += d.Y * d.Y;
            sz += d.Z * d.Z;
        }

        return Transport.Interaction.CoulombInteraction.SpacingSoftening(
            Math.Sqrt(sx / cloud.Length), Math.Sqrt(sy / cloud.Length), Math.Sqrt(sz / cloud.Length), cloud.Length);
    }

    private static double RealisedRadius(PhaseState[] cloud)
    {
        if (cloud.Length < 2)
        {
            return 0.0;
        }

        var centre = default(Vec3);

        foreach (var one in cloud)
        {
            centre += one.Position;
        }

        centre *= 1.0 / cloud.Length;

        var meanSquare = 0.0;

        foreach (var one in cloud)
        {
            meanSquare += (one.Position - centre).LengthSquared;
        }

        return Math.Sqrt(5.0 / 3.0) * Math.Sqrt(meanSquare / cloud.Length);
    }

    /// <summary>
    /// The emittance of the packet that arrives, or null when too few ions do.
    /// </summary>
    /// <remarks>
    /// Null rather than an exception, because a geometry that loses its beam is a
    /// result a sweep records rather than a failure that stops it - the same
    /// convention the single-trajectory figures use for an ion that never lands.
    /// </remarks>
    private static (Emittance Wider, Emittance Narrower)? PacketEmittance(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report = null)
    {
        if (!model.Cloud.IsCloud)
        {
            // A single deterministic ion has no spread to occupy an area with, and
            // a swept launch energy is not a packet either - the ions are not
            // simultaneous, they are one ion under different conditions.
            return null;
        }

        var flight = FlyCloud(model, report, store);

        return flight.Arrived.Count >= 3 ? Emittance.FromPacket(flight.Arrived) : null;
    }

    /// <summary>
    /// The arrival spread a source temperature alone would impose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured rather than derived, by flying the same cloud twice: once as
    /// declared, and once with everything except the temperature switched off. The
    /// difference is what the thermal velocity contributed, and it is the quantity
    /// a pulsed extraction is designed around.
    /// </para>
    /// <para>
    /// Two clouds rather than one closed form, because the closed form only holds
    /// for a uniform extraction field. Measuring it works in any geometry, and
    /// where the closed form does apply the two agree - which is what the
    /// transport tests check.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The mean transit time of a diffusive run, in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// TRN-2: a density has no flight time, and `transport.no-flight-time` says so
    /// rather than filling one in. What it has instead is a transit-time
    /// <em>distribution</em>, and this is its mean - weighted by how many ions
    /// arrived in each bin, because an unweighted mean over bins is a mean over the
    /// solver's step schedule rather than over the ions.
    /// </para>
    /// <para>
    /// Added because without it the diffusive mode's principal scalar output could
    /// not be asserted at all: no study could rank by it and no project test could
    /// pin it, so half of REG-1's peer pair was outside the machinery that keeps the
    /// other half honest.
    /// </para>
    /// </remarks>
    private static double? Transit(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report = null)
    {
        // Not a failure to measure - a wrong question. A trajectory run has a
        // flight time, which is a different quantity computed a different way,
        // and quietly returning it here would let a test pass against the mode
        // it was not written for.
        RefuseUnlessDiffusive(
            model, "transitTime", "the transit of a density", "use 'flightTime' for a trajectory run");

        var run = DiffusiveRun(model, store, report);

        if (run.Arrivals.Count == 0 || run.Collected <= 0.0)
        {
            // Nothing arrived, so there is no transit to average. Null rather than
            // zero: zero is a real answer and a caller cannot tell the two apart.
            return null;
        }

        return MeanArrival(run);
    }

    /// <summary>What a diffusive run hands the figures that read it.</summary>
    /// <param name="Arrivals">
    /// When density reached the collecting boundary, on the instrument's clock, one entry
    /// per step that collected anything, in ions.
    /// </param>
    /// <param name="Collected">The ions those arrivals sum to.</param>
    /// <param name="Final">
    /// The density the run ended with, where the run has one to give; null on the sequenced
    /// path, whose outcome does not carry it.
    /// </param>
    private sealed record DiffusiveArrivals(
        IReadOnlyList<(double TimeSeconds, double Ions)> Arrivals,
        double Collected,
        Transport.Diffusion.DensityField? Final);

    /// <summary>The ion-weighted mean arrival of a diffusive run, in seconds.</summary>
    /// <remarks>
    /// Weighted by how many ions arrived in each bin, because an unweighted mean over bins
    /// is a mean over the solver's step schedule rather than over the ions. The same
    /// arithmetic <c>einzel run</c> reports as <c>meanArrivalUs</c>, so the two agree.
    /// </remarks>
    private static double MeanArrival(DiffusiveArrivals run) =>
        run.Arrivals.Sum(a => a.TimeSeconds * a.Ions) / run.Collected;

    /// <summary>Refuses a diffusive figure asked of a model that is not diffusive.</summary>
    /// <remarks>
    /// One refusal for the three figures that read a density, so the wording cannot drift
    /// between them. The path is the transport mode, because that is the value in the
    /// document that makes the question the wrong one.
    /// </remarks>
    private static void RefuseUnlessDiffusive(
        CompiledModel model, string figure, string whatItIs, string alternative)
    {
        if (string.Equals(model.TransportMode, "diffusion", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        throw new EinzelException(new EinzelError
        {
            Code = ErrorCodes.SchemaInvalid,
            Path = "/transport/mode",
            Constraint = $"'{figure}' is {whatItIs}, and this model declares "
                + $"'{model.TransportMode}' transport",
            Suggestion = $"{alternative}, or set \"transport\": {{ \"mode\": \"diffusion\" }}",
        });
    }

    /// <summary>Runs a diffusive model the way <c>einzel run</c> would, and returns its arrivals.</summary>
    /// <remarks>
    /// <para>
    /// <b>A sequenced model takes the sequenced path, and that is the whole reason this
    /// helper exists.</b> <see cref="DiffusionRun.Execute"/> reads the field through the
    /// time-free interface, and for a model with a <c>sequence</c> that is a snapshot of the
    /// field at one instant: an elution ramp declared on such a model ran with the ramp
    /// silently ignored - exit 0, a density, no warning. <c>einzel run</c> was routed to
    /// <see cref="SequencedRun"/> for exactly that reason, and the figures of merit were
    /// not, so <c>run</c> and <c>test</c> disagreed on every sequenced diffusive model. The
    /// same seam has now split three times (a flight time, then a gas, now a sequence), each
    /// time by one path gaining a capability the other did not. See "A single-mode sequence
    /// took the path that steps a snapshot" in <c>docs/lessons.md</c>.
    /// </para>
    /// <para>
    /// The gas is resolved through <see cref="DiffusionRun.GasFor"/>, which is what the
    /// plain path already did through <c>DiffusionRun.Execute</c>'s own default, so an
    /// imported gas field reaches a sequenced figure exactly as it reaches a plain one.
    /// </para>
    /// <para>
    /// Every warning the run produces is forwarded - the field's and the run's, both
    /// paths. The sequenced outcome does not fold the field's warnings into its own, where
    /// the plain one does, so they are sent separately there rather than dropped at the
    /// seam this file has already dropped evidence at once.
    /// </para>
    /// </remarks>
    private static DiffusiveArrivals DiffusiveRun(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report)
    {
        // Neither path below knows about `species`, and neither fails when handed one:
        // they would run the FIRST population's mass against a mobility derived from the
        // gas cross section and report a number about an ion the document never declared.
        // A project test pinning that number would pass while measuring the wrong thing,
        // which is worse than a refusal by exactly the margin that makes it believable.
        if (model.IsMixture)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/species",
                Constraint = "a figure of merit is one number and a mixture has one per "
                    + "population, so this build cannot compute one for a mixture",
                Suggestion = "measure a single \"ion\" model, or read the per-population "
                    + "results from `einzel run --json` under 'mixture'",
            });
        }

        var (field, fieldWarnings) = Fields.FieldAssembly.BuildReported(model, store, PlanFor(model, store));

        if (model.Phases.Count > 0)
        {
            var sequenced = SequencedRun.Execute(model, field, DiffusionRun.GasFor(model));

            Forward(fieldWarnings, report);
            Forward(sequenced.Warnings, report);

            return new DiffusiveArrivals(
                sequenced.Arrivals, sequenced.Arrivals.Sum(a => a.Ions), null);
        }

        var plain = DiffusionRun.Execute(model, field, fieldWarnings);

        Forward(plain.Warnings, report);

        return new DiffusiveArrivals(
            plain.Result.Arrivals, plain.Result.Collected, plain.Result.Density);
    }

    /// <summary>
    /// The mobility resolving power of an elution scan: how far apart two mobilities must
    /// be for a trapped ion mobility analyser to release them as separate peaks.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The definition.</b> A trapped ion mobility analyser holds ions against a gas flow
    /// and releases them by ramping a parameter - here the tunnel's exit potential, which
    /// scales the axial field - linearly downward. An ion of mobility <c>K</c> is released
    /// when the field can no longer hold it, at a parameter value <c>V_e</c> proportional to
    /// <c>1/K</c>. So <c>R = K/dK = V/dV</c>, where <c>V</c> is the ramped parameter's value
    /// at the peak of the arrival-time distribution and <c>dV</c> is how far it moves during
    /// the peak's full width at half maximum: <c>dV = beta * FWHM_t</c> with
    /// <c>beta = |p_end - p_start| / ramp duration</c>. Hence
    /// <c>R = |p(t_peak)| / (beta * FWHM_t)</c>.
    /// </para>
    /// <para>
    /// <b>The peak is the ion-weighted mean and the width is Gaussian-equivalent</b> -
    /// <c>FWHM_t = 2 sqrt(2 ln 2) sigma_t</c> with <c>sigma_t</c> the ion-weighted standard
    /// deviation of the arrival instants. The arrivals are binned at the density solver's
    /// own step, which under an implicit scheme is coarse, and a literal half-maximum read
    /// off that histogram would be a property of the step rather than of the peak. The
    /// Gaussian-equivalent width is the honest estimate from a stepped record, and the
    /// provenance warning says that is what was used.
    /// </para>
    /// <para>
    /// <b>What it assumes, stated on every result.</b> <c>K</c> proportional to
    /// <c>1/parameter</c> holds when the parameter scales a field that is linear in
    /// position, which the tunnel's quadratic ring profile gives. A parameter that moved the
    /// field some other way would still produce a number here, and the number would be
    /// <c>V/dV</c> and not <c>K/dK</c>; the <c>mobility.resolving-power-definition</c>
    /// warning is how a reader tells which they have.
    /// </para>
    /// <para>
    /// <b>Refused, not measured</b>, where the question is the wrong one: a model that is not
    /// diffusive, one with no sequence, a sequence in which nothing ramps, or one ramping
    /// more than one parameter - the figure would not know which of them sets <c>K</c>. A
    /// ramp cut into several phases is refused too, because each has its own slope and the
    /// figure is defined against one. Null, never zero, where nothing arrived, and null with
    /// <c>mobility.peak-outside-ramp</c> where the peak fell before the ramp began or after it
    /// ended, since there the release parameter is undefined - a peak during the drain is
    /// ions that were released at the ramp's end and took that long to arrive, and reading
    /// the drain's held value as their release voltage would be wrong in a way the number
    /// cannot show.
    /// </para>
    /// </remarks>
    private static double? MobilityResolvingPower(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report = null)
    {
        RefuseUnlessDiffusive(
            model,
            "mobilityResolvingPower",
            "the resolving power of a density's elution against a ramp",
            "use 'resolvingPower' for a trajectory run's arrival-time peak");

        if (model.Phases.Count == 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "'mobilityResolvingPower' is read against a ramped parameter, and "
                    + "this model declares no sequence",
                Suggestion = "add a \"sequence\" with a phase that ramps the parameter the "
                    + "release depends on - for a tunnel, its exit potential walked to zero",
            });
        }

        var ramping = model.Phases.Where(p => p.Ramps.Count > 0).ToList();

        if (ramping.Count == 0)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "'mobilityResolvingPower' is read against a ramped parameter, and "
                    + $"no phase of this {model.Phases.Count}-phase sequence ramps anything - "
                    + "every phase holds",
                Suggestion = "give one phase a \"ramp\" naming where the release parameter "
                    + "ends. A held field releases nothing and has no scan to resolve against",
            });
        }

        var parameters = ramping
            .SelectMany(p => p.Ramps.Select(r => r.Parameter))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (parameters.Count > 1)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "'mobilityResolvingPower' needs one ramped parameter to read the "
                    + $"release against, and this sequence ramps {parameters.Count}: "
                    + string.Join(", ", parameters),
                Suggestion = "ramp only the parameter that sets which mobility is released. "
                    + "With two ramping at once the figure has no way to know which one the "
                    + "peak is a function of",
            });
        }

        if (ramping.Count > 1)
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "'mobilityResolvingPower' is defined against one linear ramp, and "
                    + $"this sequence ramps '{parameters[0]}' in {ramping.Count} phases: "
                    + string.Join(", ", ramping.Select(p => $"'{p.Name}'")),
                Suggestion = "write the scan as one ramp phase. A ramp in segments has a slope "
                    + "per segment, and the figure would be a different resolving power in each",
            });
        }

        var phase = ramping[0];
        var ramp = phase.Ramps[0];
        var rampStart = phase.EndsAtSeconds - phase.DurationSeconds;
        var rampEnd = phase.EndsAtSeconds;
        var travel = ramp.End.SiValue - ramp.Start.SiValue;
        var beta = Math.Abs(travel) / phase.DurationSeconds;

        if (!(beta > 0.0))
        {
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = $"phase '{phase.Name}' ramps '{ramp.Parameter}' from "
                    + $"{ramp.Start} to {ramp.End}, so the parameter does not move and "
                    + "there is no scan to read a resolving power against",
                Suggestion = "end the ramp somewhere other than where it starts",
            });
        }

        var run = DiffusiveRun(model, store, report);

        if (run.Arrivals.Count == 0 || run.Collected <= 0.0)
        {
            // Nothing arrived, so there is no peak. Null rather than zero: zero is a real
            // answer here (a ramp that reached nought exactly at the peak) and a caller
            // cannot tell the two apart.
            return null;
        }

        var peak = MeanArrival(run);
        var variance = run.Arrivals.Sum(
            a => a.Ions * (a.TimeSeconds - peak) * (a.TimeSeconds - peak)) / run.Collected;
        var fwhm = 2.0 * Math.Sqrt(2.0 * Math.Log(2.0)) * Math.Sqrt(Math.Max(0.0, variance));

        var inside = peak >= rampStart && peak <= rampEnd;
        var at = inside
            ? ramp.Start.SiValue + (travel * (peak - rampStart) / phase.DurationSeconds)
            : double.NaN;

        var unit = ramp.Start.Dimension.IsDimensionless ? string.Empty : $" {ramp.Start.Dimension}";
        var culture = System.Globalization.CultureInfo.InvariantCulture;

        // Provenance rather than a caveat: the definition is a choice, and the number cannot
        // carry it. Reported whether or not the peak landed inside the ramp, so the reader
        // who gets a null below still sees the numbers that produced it.
        report?.Invoke(new Core.Results.ValidityWarning(
            "mobility.resolving-power-definition",
            $"mobility resolving power is read as V/dV against the ramp of '{ramp.Parameter}' in "
            + $"phase '{phase.Name}': R = |p(t_peak)| / (beta * FWHM_t), with t_peak the "
            + $"ion-weighted mean arrival ({peak * 1e6:F1} us), FWHM_t the Gaussian-equivalent "
            + $"width 2 sqrt(2 ln 2) sigma_t of the arrivals ({fwhm * 1e6:F1} us) rather than a "
            + "literal half-maximum of the stepped histogram, beta = |p_end - p_start| / ramp "
            + $"duration ({beta.ToString("G4", culture)}{unit}/s), and p(t_peak) = "
            + $"{(inside ? at.ToString("G4", culture) + unit : "undefined, the peak being outside the ramp")}. "
            + "It equals K/dK on the assumption that the release parameter goes as 1/K, which "
            + "holds when the parameter scales a field that is linear in position",
            Core.Results.WarningSeverity.Provenance));

        if (!inside)
        {
            report?.Invoke(new Core.Results.ValidityWarning(
                "mobility.peak-outside-ramp",
                $"the arrival peak at {peak * 1e6:F1} us fell "
                + (peak < rampStart
                    ? $"before the ramp began at {rampStart * 1e6:F1} us"
                    : $"after the ramp ended at {rampEnd * 1e6:F1} us")
                + $", so the value of '{ramp.Parameter}' that released those ions is "
                + "undefined and no resolving power is reported. Ions arriving after the "
                + "ramp were released at its end and took that long to reach the detector; "
                + "lengthen the ramp, or slow it, so the release falls inside it",
                Core.Results.WarningSeverity.Qualified));

            return null;
        }

        if (!(fwhm > 0.0))
        {
            // Every arrival fell in one step, so the peak has no width to read a resolving
            // power off - which is a width below the step, not an infinite resolving power.
            report?.Invoke(new Core.Results.ValidityWarning(
                "mobility.peak-unresolved",
                "every arrival fell within one step of the density solver, so the peak has no "
                + "measurable width and no resolving power is reported. Refine the density "
                + "step - a smaller implicit gain - so the peak spans several steps",
                Core.Results.WarningSeverity.Qualified));

            return null;
        }

        return Math.Abs(at) / (beta * fwhm);
    }

    /// <summary>
    /// How wide the density is across the direction of travel, at the end of the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The figure a driven diffusive run could not be asserted by.</b> An RF guide's
    /// whole effect is radial: it holds ions off the walls, and lets them spread when it is
    /// off. Neither of the two figures a density already had could see that. The transit is
    /// contaminated - a ring stack has an axial ponderomotive corrugation, deepest at each
    /// ring edge where the field is largest, which at a weak axial field nearly doubles the
    /// time - and the transmission has no closed form. So the effect the mode exists to
    /// model had no measurable quantity, which is the same gap <c>transitTime</c> and
    /// <c>meanKineticEnergy</c> were added to close.
    /// </para>
    /// <para>
    /// <b>Both regimes have a closed form, which is what makes it worth having.</b>
    /// Unconfined, a packet spreads by diffusion alone and the width goes as
    /// <c>sqrt(2 D t)</c> with <c>D</c> from the Einstein relation - arithmetic the solver
    /// has no part in, and already the sharpest check it has. Confined in a harmonic
    /// effective well, it settles at the thermal width <c>sqrt(kT / (m omega^2))</c>. A
    /// figure that could only be compared against itself would not be worth adding.
    /// </para>
    /// <para>
    /// About the packet's own centroid rather than about the axis, so it is a width rather
    /// than a radius. That is the quantity with the closed forms, and it does not assume
    /// the model put its axis anywhere in particular.
    /// </para>
    /// </remarks>
    private static double? RadialSpread(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report = null)
    {
        // The same refusal `transitTime` makes, for the same reason: a trajectory run
        // has a packet width too, computed a different way over particles, and quietly
        // returning that here would let a test pass against the mode it was not
        // written for.
        RefuseUnlessDiffusive(
            model, "radialSpread", "the width of a density",
            "use 'emittance' for a trajectory run's packet");

        if (model.Phases.Count > 0)
        {
            // Refused rather than run, and refused rather than run the wrong way. The
            // sequenced path is the right one for a model with a sequence, and its outcome
            // carries the arrivals and not the density it ended with, so there is nothing
            // here to take a width of. The alternative - stepping a snapshot of the field
            // through the plain path - would report a width of a run the document does not
            // describe, which is the defect that sent every other sequenced figure through
            // SequencedRun.
            throw new EinzelException(new EinzelError
            {
                Code = ErrorCodes.SchemaInvalid,
                Path = "/sequence",
                Constraint = "'radialSpread' is the width of the density a run ends with, and a "
                    + "sequenced run does not hand its final density out - so this figure "
                    + "cannot yet be read off a model with a sequence",
                Suggestion = "remove the sequence to measure the width of a held run, or use "
                    + "'transitTime' or 'mobilityResolvingPower', which read the sequenced "
                    + "run's arrivals",
            });
        }

        var run = DiffusiveRun(model, store, report);

        // The plain path always ends with a density; only the sequenced path, refused
        // above, does not.
        var density = run.Final
            ?? throw new InvalidOperationException("a plain diffusive run ends with a density");

        if (!(density.Population() > 0.0))
        {
            // Nothing left to have a width. Null rather than zero, which is a real answer
            // and would read as a perfectly confined packet rather than an absent one -
            // the exact inversion, on the figure whose whole subject is confinement.
            return null;
        }

        return density.Spread().Y;
    }

    /// <summary>
    /// The fraction of launched ions that arrive, in its own right.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Counted rather than read off an arrival-time peak, because a peak needs at
    /// least two arrivals to have a width and a transmission needs none at all.
    /// Going through the peak meant <strong>a transmission of zero could not be
    /// expressed</strong>: a mass filter above its cut-off, or an ion lost on a ring,
    /// raised an internal error saying "a peak needs at least two arrivals" and the
    /// whole run reported itself as a defect in the engine.
    /// </para>
    /// <para>
    /// That is exactly backwards for ACC-5, whose entire subject is transmission as
    /// a measured, itemised quantity: an instrument that loses everything is the
    /// case a reader most wants reported, and it was the one case the figure could
    /// not report. Found by the example corpus, where a quadrupole above its
    /// low-mass cut-off is half of a two-model pair that brackets a published
    /// boundary.
    /// </para>
    /// <para>
    /// Zero is a measurement and null is a failure to measure, and the two are kept
    /// apart: nothing arrived gives 0.0, while a model that could not be flown at
    /// all gives null.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The strongest line below the drive in one component of the ion's motion.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Section 12 asks for the secular frequency spectrum as a Class B figure, and a
    /// figure of merit needs one number out of it: the dominant line. What makes it
    /// worth having as a scannable quantity rather than a diagnostic is that a
    /// nonlinear resonance is <em>defined</em> by a condition on these frequencies,
    /// so a scan that reports where the ion is lost and a scan that reports its
    /// secular frequency can be read against each other.
    /// </para>
    /// <para>
    /// The search band is 2 to 90 per cent of the drive, taken from the field's own
    /// shortest period rather than from a parameter with a guessable name. The upper
    /// end stops below the drive on purpose: the micromotion at the drive frequency
    /// is the largest line in most spectra and is not the secular motion, so
    /// including it would report the drive back to the caller as a discovery.
    /// </para>
    /// <para>
    /// Null for a static field, with a warning. That is not a failed measurement - a
    /// static field has no secular motion to have a frequency, and reporting an
    /// ion's ordinary oscillation in a DC well under this name would be answering a
    /// different question.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The strongest periodic line in the ion's motion along one axis, over the record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The measurand of an entire instrument class, and it had no figure.</b> An
    /// electrostatic orbital trap reads mass from the frequency of an ion's axial
    /// oscillation - `omega = sqrt(q k / m)`, exact, and independent of the radius, the
    /// angular momentum and the amplitude. Nothing here could measure it: the secular
    /// figures refuse a static field, correctly, because a secular frequency is by
    /// definition the slow motion in an RF field's effective well and a static field has
    /// no such well.
    /// </para>
    /// <para>
    /// So the refusal was right and the gap was real. This is the same Lomb-Scargle
    /// machinery over a different band: everything from two cycles per record - fewer
    /// than that is not a frequency, it is a trend - up to half the sampling rate.
    /// </para>
    /// <para>
    /// <b>Lomb-Scargle rather than a transform</b>, for the reason the secular spectrum
    /// gives: the trajectory is sampled at accepted integration steps, which cluster where
    /// the physics is hard, so the series is not uniform. Resampling it first would be
    /// inventing values the integrator never computed and then measuring them.
    /// </para>
    /// <para>
    /// In a driven field this will find the drive, which is usually the largest line in
    /// the spectrum. That is not a defect - it is what "the strongest line" means - and it
    /// is exactly why <see cref="Secular"/> exists separately and searches below the drive.
    /// </para>
    /// </remarks>
    private static double? Oscillation(
        CompiledModel model, ISolveStore? store,
        int axis, Action<Core.Results.ValidityWarning>? report = null)
    {
        var (launch, species, field, settings, detector, collisions) = Setup(model, store, report: report);

        // Finely enough that the line is well inside the band rather than near its edge,
        // and capped so a long record does not cost more samples than it needs.
        var flight = settings.MaximumFlightTime;
        var interval = Math.Min(model.SampleIntervalSi, flight / 4000.0);

        var recorder = new TrajectoryRecorder(interval, capacity: 16384);

        TrajectoryIntegrator.Integrate(
            launch, species, field, settings, detector, recorder, collisions?.Invoke());

        if (recorder.Samples.Count < 8)
        {
            return null;
        }

        var span = recorder.Samples[^1].TimeSeconds - recorder.Samples[0].TimeSeconds;

        if (!(span > 0.0))
        {
            return null;
        }

        try
        {
            // Two cycles per record at the low end: a record holding one cycle cannot
            // tell an oscillation from a drift, and Lomb-Scargle will happily fit the
            // drift. At the high end, half the mean sampling rate.
            var lowest = 2.0 / span;
            var highest = 0.5 * (recorder.Samples.Count - 1) / span;

            if (!(highest > lowest))
            {
                return null;
            }

            var spectrum = Analysis.SecularSpectrum.From(
                recorder.Samples, axis, lowest, highest, 4000);

            var peak = spectrum.Peak();

            if (peak is null)
            {
                return null;
            }

            Forward(peak.Warnings, report);

            var (value, _, _, _) = peak;

            return value.SiValue;
        }
        catch (EinzelException)
        {
            return null;
        }
    }

    private static double? Secular(
        CompiledModel model, ISolveStore? store,
        int axis, Action<Core.Results.ValidityWarning>? report = null)
    {
        var (launch, species, field, settings, detector, collisions) = Setup(model, store, report: report);

        // Gated on having a drive, not on varying in time: a geometry with DC stages and no
        // RF is time-varying and has no secular motion, and its shortest period is infinite.
        if (field is not Fields.ITimeVaryingField driven || !double.IsFinite(driven.ShortestPeriodSeconds))
        {
            report?.Invoke(new Core.Results.ValidityWarning(
                "secular.no-drive",
                "this model declares no time-varying field, so there is no secular motion to have a "
                + "frequency. A secular frequency is the slow oscillation an ion makes in the "
                + "effective well of an RF field, and a static field has no such well - the ion "
                + "simply moves in the field it is in",
                Core.Results.WarningSeverity.ValidityViolation));

            return null;
        }

        var period = driven.ShortestPeriodSeconds;
        var recorder = new TrajectoryRecorder(period / 16.0);

        TrajectoryIntegrator.Integrate(launch, species, field, settings, detector, recorder);

        if (recorder.Samples.Count < 4)
        {
            return null;
        }

        try
        {
            var spectrum = Analysis.SecularSpectrum.From(
                recorder.Samples, axis, 0.02 / period, 0.90 / period, 4000);

            var peak = spectrum.Peak();

            if (peak is null)
            {
                return null;
            }

            Forward(peak.Warnings, report);

            var (value, _, _, _) = peak;

            return value.SiValue;
        }
        catch (ArgumentException)
        {
            // No variance along this axis: the ion never moved in this direction, so
            // it has no spectrum here. Absent rather than zero - zero hertz is a real
            // answer and a reader cannot tell the two apart if both print as zero.
            return null;
        }
    }

    /// <summary>
    /// How many distinct ions an energy-spread ensemble actually has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An ensemble here is a sweep of <em>launch energy</em>, and the launch speed
    /// goes as the square root of it - so when the source starts at rest, every
    /// member is <c>0 * sqrt(1 + offset)</c> and the ensemble is one ion flown
    /// <c>n</c> times. That is not merely wasteful (each member rebuilds the field,
    /// so a trap held for two hundred RF cycles paid twenty-one times over for one
    /// answer): it is <em>misleading</em>, because the result is then reported as a
    /// fraction over an ensemble that has no spread in it.
    /// </para>
    /// <para>
    /// A resting source is not an edge case - it is what every trap and every
    /// pulsed extraction declares, and it is exactly the population a
    /// <c>confined</c> figure is asked about. So the collapse is reported rather
    /// than silently applied: the caller learns that <c>ions</c> bought nothing and
    /// what would have to change for it to buy something, which for a trap is a
    /// thermal cloud rather than a wider energy spread.
    /// </para>
    /// </remarks>
    /// <summary>How many genuinely different ions an energy scan of this model has.</summary>
    /// <remarks>
    /// <para>
    /// <b>Every caller must offer a declared cloud first.</b> This is the deterministic,
    /// seed-free energy scan a study wants when nothing else is stated; it is NOT the
    /// population a document declares, and a figure that reaches here without checking
    /// answers a different question from the one <c>einzel run</c> answers over the same
    /// model. That divergence is not hypothetical - <c>confined</c> did exactly this while
    /// its sibling <c>transmission</c> did not, and the two disagreed 20-fold on a trap
    /// holding part of a hot cloud.
    /// </para>
    /// <para>
    /// There are three call sites and all three check <c>model.Cloud.IsCloud</c> first, in
    /// <see cref="Ensemble"/>, <see cref="Counted"/> and <see cref="Confined"/>. A fourth
    /// belongs in that list before it belongs here.
    /// </para>
    /// </remarks>
    /// <param name="model">The validated model.</param>
    /// <param name="spread">Fractional energy spread the scan covers.</param>
    /// <param name="ions">How many members were asked for.</param>
    /// <param name="report">Sink for validity warnings.</param>
    /// <returns>How many distinct trajectories the scan actually produces.</returns>
    private static int Distinct(
        CompiledModel model, double spread, int ions, Action<Core.Results.ValidityWarning>? report)
    {
        if (ions <= 1)
        {
            return ions;
        }

        var reason =
            model.LaunchSpeedSi() == 0.0 ? "the source starts at rest, so every launch speed is zero"
            : spread == 0.0 ? "the energy spread is zero"
            : null;

        if (reason is null)
        {
            return ions;
        }

        report?.Invoke(new Core.Results.ValidityWarning(
            "ensemble.degenerate",
            $"{ions} ions were asked for and one was flown: {reason} whatever the offset, so every "
            + "member of the ensemble is the same ion. The figure is a fraction over one trajectory "
            + "rather than over a distribution. To vary the population of a source at rest, declare a "
            + "cloud with a temperature or a spatial spread - an energy spread cannot move an ion "
            + "that has no energy",
            Core.Results.WarningSeverity.Provenance));

        return 1;
    }

    private static double? Transmitted(
        CompiledModel model,
        ISolveStore? store,
        double spread,
        int ions,
        Action<Core.Results.ValidityWarning>? report = null)
    {
        var (arrived, launched) = Counted(model, store, spread, ions, report);

        return launched > 0 ? (double)arrived / launched : null;
    }

    /// <summary>How many of the ensemble arrived, and how many were launched.</summary>
    private static (int Arrived, int Launched) Counted(
        CompiledModel model,
        ISolveStore? store,
        double spread,
        int ions,
        Action<Core.Results.ValidityWarning>? report = null)
    {
        if (model.Cloud.IsCloud)
        {
            return (FlyCloud(model, report, store).Arrived.Count, model.Cloud.Ions);
        }

        var arrived = 0;
        var members = Distinct(model, spread, ions, report);

        // ONE FIELD FOR THE WHOLE ENSEMBLE, as the energy scan has it: an energy offset changes
        // how fast an ion is launched and nothing about the field. Built once per member, the
        // same cached channels were read again for every ion - 83 per cent of a warm 21-ion
        // evaluation of the 0.2 mm quadrupole. A field is never written after it is built, so
        // every member flies through the same bits it flew through before.
        var (_, species, field, settings, detector, collisions) = Setup(model, store, report: report);

        for (var k = 0; k < members; k++)
        {
            var fraction = members == 1 ? 0.0 : (2.0 * k / (members - 1.0)) - 1.0;

            var result = TrajectoryIntegrator.Integrate(
                LaunchAt(model, spread * fraction), species, field, settings, detector,
                collisions: collisions?.Invoke());

            if (result.Outcome == TrajectoryOutcome.StopConditionMet)
            {
                arrived++;
            }
        }

        return (arrived, members);
    }

    /// <summary>
    /// The fraction of launched ions still inside at the end of the run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What a trap is measured by, and it is the complement of everything else here:
    /// a trapped ion by definition never arrives anywhere, so transmission is zero
    /// for a working trap and zero again for one that lost everything. The two are
    /// not the same instrument, and no figure that counted arrivals could tell them
    /// apart.
    /// </para>
    /// <para>
    /// Confined means the run ended at its flight-time ceiling with the ion neither
    /// struck on a surface nor past the detector. So a model measured this way puts
    /// its detector <em>outside</em> the trap, where reaching it means having
    /// escaped - which makes the three outcomes distinct: struck, escaped, held.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The mean kinetic energy the packet ends the flight with.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Equipartition is the sharpest check the event-driven collision models have,
    /// and until now it was outside the machinery that keeps every other figure
    /// honest.</b> An ion left in a gas long enough must arrive at <c>(3/2)kT</c>
    /// whatever it started with - a closed form this engine has no part in, which tests
    /// the scattering kinematics, the Maxwellian draw and the isotropy of the deflection
    /// at once. It was measured in a unit test and could not be asserted by a project
    /// test or ranked by a study, which is the same gap <c>transitTime</c> was added to
    /// close for the diffusive mode.
    /// </para>
    /// <para>
    /// <b>Over the ions still in flight, not the ones that arrived.</b> A thermalised
    /// packet has no preferred direction, so which members reach a detector is a
    /// question about geometry rather than about temperature - and selecting on arrival
    /// would select the fast ones and report a temperature that is too high.
    /// </para>
    /// <para>
    /// Through the cloud path rather than the single-ion one, so the ion count is the
    /// document's and each member gets its own collision stream from the declared seed.
    /// A temperature is a statistic, and taking it over the twenty-one ions a study
    /// happens to default to would give it an eighteen per cent standard error whatever
    /// the model asked for.
    /// </para>
    /// <para>
    /// In joules, because an evaluator returns SI and the catalogue's unit is applied
    /// above it. Returning electronvolts here converted twice and reported 3e17 eV,
    /// which is the same rule - SI internally, units at the boundary - broken from the
    /// inside.
    /// </para>
    /// </remarks>
    private static double? MeanKineticEnergy(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report = null)
    {
        var species = IonSpecies.FromModel(model);

        var flight = FlyCloud(model, report, store);

        var total = 0.0;

        foreach (var state in flight.Remaining)
        {
            total += 0.5 * species.MassSi * state.Velocity.LengthSquared;
        }

        // Nobody survived to have a temperature. Absent rather than zero: zero is a
        // real answer and a reader cannot tell the two apart if both print as zero.
        return flight.Remaining.Count == 0 ? null : total / flight.Remaining.Count;
    }

    private static double? Confined(
        CompiledModel model,
        ISolveStore? store,
        double spread,
        int ions,
        Action<Core.Results.ValidityWarning>? report = null)
    {
        if (ions <= 0)
        {
            return null;
        }

        // A DECLARED CLOUD IS THE POPULATION, exactly as `transmission` already had it.
        // Without this the figure flew a deterministic energy scan while `einzel run`
        // reported the cloud, so `einzel test` and `einzel run` would answer a question
        // about confinement over two different populations - the same divergence this
        // project has already found twice, once in a flight time and once because a
        // declared gas reached only one of the two paths.
        //
        // It did not show up on `paul-trap-held`, which declares no cloud: its source is
        // at rest, so `Distinct` collapses to one ion and both routes fly the same one.
        // The example being green is what let this sit.
        if (model.Cloud.IsCloud)
        {
            var cloud = FlyCloud(model, report, store);

            return model.Cloud.Ions > 0
                ? (double)cloud.Remaining.Count / model.Cloud.Ions
                : null;
        }

        var held = 0;
        var members = Distinct(model, spread, ions, report);

        // One field for every member, as in Counted.
        var (_, species, field, settings, detector, collisions) = Setup(model, store, report: report);

        for (var k = 0; k < members; k++)
        {
            var fraction = members == 1 ? 0.0 : (2.0 * k / (members - 1.0)) - 1.0;

            var result = TrajectoryIntegrator.Integrate(
                LaunchAt(model, spread * fraction), species, field, settings, detector,
                collisions: collisions?.Invoke());

            if (result.Outcome == TrajectoryOutcome.MaximumFlightTimeReached
                && result.StruckSurface is null)
            {
                held++;
            }
        }

        return (double)held / members;
    }

    private static double? TurnAround(
        CompiledModel model, ISolveStore? store, Action<Core.Results.ValidityWarning>? report = null)
    {
        if (model.Cloud.TemperatureK <= 0.0)
        {
            // No temperature, no turn-around. Zero rather than null: it is a
            // measurement of something absent, not a failure to measure.
            return 0.0;
        }

        try
        {
            return FromCloud(ThermalOnly(model), report, store)?.GaussianEquivalentFwhmSeconds;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The same model with every spread but the source temperature switched off.</summary>
    /// <remarks>
    /// <para>
    /// Turn-around is the spread the source temperature alone imposes, and the whole method
    /// is to switch everything else off and measure what is left.
    /// </para>
    /// <para>
    /// <b>The packet's own charge is one of the things being switched off</b>, and it has to
    /// be. Leaving it on would measure temperature plus space charge and report it as
    /// temperature - and it also cannot run: the thermal-only cloud has no spatial spread by
    /// construction, so its self-field is unbounded rather than large, which is exactly the
    /// case the validator refuses. Reached here by a model built in code rather than read
    /// from a file, it came back as a turn-around of 0.000 ns, which reads like a
    /// measurement.
    /// </para>
    /// <para>
    /// One implementation, because the bare figure and the enveloped one must measure the
    /// same thing - two constructions of "everything else off" would drift, and the drift
    /// would look like a disagreement about the physics.
    /// </para>
    /// </remarks>
    private static CompiledModel ThermalOnly(CompiledModel model) => model with
    {
        Cloud = new IonCloudSettings
        {
            Ions = model.Cloud.Ions,
            Seed = model.Cloud.Seed,
            TemperatureK = model.Cloud.TemperatureK,
        },

        SpaceChargeMode = "none",
    };

    /// <summary>
    /// The field basis plan a study's store builds this model under, or null - the default
    /// decomposition - for any other store and for a point that does not fit the study's plan.
    /// </summary>
    private static Fields.Solved.FieldBasisPlan? PlanFor(CompiledModel model, ISolveStore? store) =>
        (store as StudyFieldRun.IPlannedStore)?.PlanFor(model);

    /// <summary>Where and how fast an ion at a given fractional energy offset starts.</summary>
    /// <remarks>
    /// <b>Energy scales as the square of speed</b>, so a fractional energy offset is a
    /// square root in velocity. Treating it as a velocity fraction is a factor of two in
    /// the linear term and four in the quadratic, and it has been got wrong here before -
    /// which is why this is one function rather than the same three lines in the two
    /// places that need them.
    /// </remarks>
    private static PhaseState LaunchAt(CompiledModel model, double energyOffset) =>
        new(model.SourcePosition,
            model.SourceDirection * (model.LaunchSpeedSi() * Math.Sqrt(1.0 + energyOffset)));

    private static (PhaseState Launch, IonSpecies Species, IElectrostaticField Field,
        IntegrationSettings Settings, TrajectoryStopFunction Detector,
        Func<Transport.Collisions.CollisionSampler>? Collisions) Setup(
        CompiledModel model,
        ISolveStore? store,
        double energyOffset = 0.0,
        Action<Core.Results.ValidityWarning>? report = null)
    {
        IElectrostaticField field;

        if (report is null)
        {
            // Nowhere to carry a taint onto, so the only honest options are the two
            // FieldAssembly offers - refuse, or hide it - and hiding it is how the
            // segmented quadrupole sat at the wrong working point for a revision.
            field = FieldAssembly.Build(model, store, PlanFor(model, store));
        }
        else
        {
            var (built, fieldWarnings) = FieldAssembly.BuildReported(model, store, PlanFor(model, store));

            Forward(fieldWarnings, report);
            field = built;
        }

        var species = IonSpecies.FromModel(model);

        var launch = LaunchAt(model, energyOffset);

        var detectorPoint = model.DetectorPoint;
        var detectorNormal = model.DetectorNormal;

        TrajectoryStopFunction detector =
            (in PhaseState state) => Vec3.Dot(state.Position - detectorPoint, detectorNormal);

        var settings = new IntegrationSettings
        {
            RelativeTolerance = model.RelativeTolerance,
            LandOnEdges = model.LandOnEdges,
            MaximumFlightTime = model.MaximumFlightTimeSi,
        };

        // A DECLARED GAS TAKES PART, which it did not before: every figure of merit
        // reached through here flew in vacuum however much gas the document declared,
        // so `einzel run` and `einzel test` disagreed on any model with one. The corpus
        // example whose entire subject is a gas carrying an ion passed with the gas
        // block deleted, to the last digit.
        //
        // A factory rather than an instance because the flight-time study integrates
        // three times at three tolerances and each pass needs its own stream - sharing
        // one would let the second refinement continue the first's draws.
        var gas = DiffusionRun.GasFor(model);

        var collisions = gas.IsPresent
            ? () => new Transport.Collisions.CollisionSampler(
                gas, species.MassSi, species.ChargeSi, model.Gas.Seed)
            : (Func<Transport.Collisions.CollisionSampler>?)null;

        return (launch, species, field, settings, detector, collisions);
    }
}
