// QuadBatch: Einzel's solver, interpolant, waveform and integrator driven in-process, one solve for many ions.
//
// Why: `einzel run` re-solves the field for every invocation (a 1 M-node 3D solve is seconds per basis channel),
// and a quadrupole stability scan line is ~5000 ions with a per-ion RF phase. Einzel's own run does nothing but
//   FieldAssembly.BuildReported(model)  ->  DrivenSolvedField = sum_i (direct_i + amp_i * w(f t + phi_i)) * basis_i
//   TrajectoryIntegrator.Integrate(launch, species, field, settings, detector)
// This program does exactly that with Einzel's classes, except that the basis fields are solved once (as static
// unit-potential models, via FieldAssembly) and re-weighted per ion. The superposition wrapper below mirrors
// DrivenSolvedField (whose constructor is internal). Checked against `einzel run` on the full driven model; the
// numbers and the job file format are in README.md beside this file.
//
// usage: QuadBatch <job.json> <out.json>
//   job = { "bases": [model paths], "waveform": "sinusoid"|"rectangular", "dutyCycle": 0.5, "frequencyHz": f,
//           "relativeTolerance": 1e-11, "ladder": false, "maxFlightUs": 200, "detectorPointMm": [95.25,0,0],
//           "detectorNormal": [-1,0,0],
//           "threads": 16, "ions": [ { "id": n, "mass": u, "direct": [V per basis], "amp": [V per basis],
//           "phase": cycles, ["waveform": per-ion override,] "pos": [mm], "dir": [..], "ke": eV } ] }
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Einzel.Core.Geometry;
using Einzel.Core.Model;
using Einzel.Fields;
using Einzel.Fields.Analytic;
using Einzel.Io;
using Einzel.Transport;
using Einzel.Transport.Integration;

internal sealed class WeightedDrivenField : ITimeVaryingField, IConductorBounded
{
    private readonly IElectrostaticField[] _basis;
    private readonly IConductorBounded? _conductors;
    private readonly double[] _direct;
    private readonly double[] _amp;
    private readonly RfWaveform _waveform;
    private readonly double _f;
    private readonly double _phase;

    public WeightedDrivenField(IElectrostaticField[] basis, double[] direct, double[] amp, RfWaveform waveform,
        double frequencyHz, double phase)
    {
        _basis = basis;
        _conductors = basis[0] as IConductorBounded;
        _direct = direct;
        _amp = amp;
        _waveform = waveform;
        _f = frequencyHz;
        _phase = phase;
    }

    public double Drive(double t) => _waveform.At((_f * t) + _phase);

    public Vec3 ElectricFieldAt(in Vec3 position, double timeSeconds)
    {
        var w = Drive(timeSeconds);
        var e = Vec3.Zero;
        for (var i = 0; i < _basis.Length; i++)
        {
            var weight = _direct[i] + (_amp[i] * w);
            if (weight != 0.0)
            {
                e += _basis[i].ElectricFieldAt(in position) * weight;
            }
        }

        return e;
    }

    public double PotentialAt(in Vec3 position, double timeSeconds)
    {
        var w = Drive(timeSeconds);
        var v = 0.0;
        for (var i = 0; i < _basis.Length; i++)
        {
            v += (_direct[i] + (_amp[i] * w)) * _basis[i].PotentialAt(in position);
        }

        return v;
    }

    public Vec3 ElectricFieldAt(in Vec3 position) => ElectricFieldAt(in position, 0.0);

    public double PotentialAt(in Vec3 position) => PotentialAt(in position, 0.0);

    public double ShortestPeriodSeconds => 1.0 / _f;

    public double ResolutionLength => _basis[0].ResolutionLength;

    public double SignedDistanceToConductor(in Vec3 position) =>
        _conductors is null ? double.PositiveInfinity : _conductors.SignedDistanceToConductor(in position);

    public string? ConductorAt(in Vec3 position) => _conductors?.ConductorAt(in position);
}

internal static class Program
{
    private static (IElectrostaticField Field, JsonArray Warnings) BuildBasis(string path)
    {
        var doc = ModelJson.Parse(File.ReadAllText(path));
        var validation = ModelValidator.Validate(doc, null, Path.GetDirectoryName(Path.GetFullPath(path)));
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                $"{path}: " + string.Join("; ", validation.Errors.Select(e => $"{e.Code} {e.Path} {e.Constraint}")));
        }

        if (validation.Model!.Gas.IsPresent)
        {
            // einzel run would fly this model through a CollisionSampler; this driver has no collision path.
            throw new InvalidOperationException($"{path}: declares a gas; QuadBatch flies in vacuum only");
        }

        var (field, warnings) = FieldAssembly.BuildReported(validation.Model!);
        var reported = new JsonArray();
        foreach (var w in warnings)
        {
            Console.Error.WriteLine($"  [{w.Severity}] {w.Code}: {w.Message}");
            reported.Add(new JsonObject
            {
                ["basis"] = Path.GetFileName(path), ["severity"] = w.Severity.ToString(), ["code"] = w.Code, ["message"] = w.Message,
            });
        }

        return (field, reported);
    }

    private static RfWaveform ParseWaveform(string name, double dutyCycle) => name switch
    {
        "sinusoid" => new RfWaveform.Sinusoid(),
        "rectangular" => new RfWaveform.Rectangular(dutyCycle),
        _ => throw new InvalidOperationException($"unknown waveform '{name}'; expected sinusoid or rectangular"),
    };

    private static double[] Required(JsonNode node, string key, int length)
    {
        var a = Arr(node[key] ?? throw new InvalidOperationException($"missing '{key}'"));
        return a.Length == length ? a : throw new InvalidOperationException($"'{key}' has {a.Length} entries, expected {length}");
    }

    private static double[] Arr(JsonNode? n) => n!.AsArray().Select(x => x!.GetValue<double>()).ToArray();

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("usage: QuadBatch <job.json> <out.json>");
            return 2;
        }

        var job = JsonNode.Parse(File.ReadAllText(args[0]))!;
        var clock = Stopwatch.StartNew();
        var bases = job["bases"]!.AsArray().Select(p => p!.GetValue<string>()).ToArray();
        var fields = new IElectrostaticField[bases.Length];
        var solveSeconds = new double[bases.Length];
        var basisWarnings = new JsonArray();
        for (var i = 0; i < bases.Length; i++)
        {
            var t = Stopwatch.StartNew();
            (fields[i], var reported) = BuildBasis(bases[i]);
            foreach (var w in reported.ToArray())
            {
                reported.Remove(w);
                basisWarnings.Add(w);
            }

            solveSeconds[i] = t.Elapsed.TotalSeconds;
            Console.Error.WriteLine($"basis {i} {Path.GetFileName(bases[i])}: {solveSeconds[i]:F1} s");
        }

        var dutyCycle = job["dutyCycle"]?.GetValue<double>() ?? 0.5;
        var waveform = ParseWaveform(job["waveform"]?.GetValue<string>() ?? "sinusoid", dutyCycle);
        var f = job["frequencyHz"]!.GetValue<double>();
        var rtol = job["relativeTolerance"]?.GetValue<double>() ?? 1e-11;
        var ladder = job["ladder"]?.GetValue<bool>() ?? false;
        var maxT = (job["maxFlightUs"]?.GetValue<double>() ?? 200.0) * 1e-6;
        // detector plane: point (mm) and normal (pointing back into the flight volume), as Einzel's detector
        var dp = Required(job, "detectorPointMm", 3);
        var dnv = Required(job, "detectorNormal", 3);
        var detPoint = new Vec3(dp[0] * 1e-3, dp[1] * 1e-3, dp[2] * 1e-3);
        var detNormal = new Vec3(dnv[0], dnv[1], dnv[2]);
        var threads = job["threads"]?.GetValue<int>() ?? Environment.ProcessorCount;
        var ions = job["ions"]!.AsArray();
        var results = new JsonObject[ions.Count];
        var flyClock = Stopwatch.StartNew();

        Parallel.For(0, ions.Count, new ParallelOptions { MaxDegreeOfParallelism = threads }, idx =>
        {
            var ion = ions[idx]!;
            var id = ion["id"]?.GetValue<int>() ?? idx;
            try
            {
                results[idx] = Fly(ion, id);
            }
            catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException or NullReferenceException or IndexOutOfRangeException)
            {
                // One bad ion must not cost the others: record it and keep the output file complete.
                results[idx] = new JsonObject { ["id"] = id, ["outcome"] = "Error", ["error"] = e.Message };
            }
        });

        JsonObject Fly(JsonNode ion, int id)
        {
            var wf = ion["waveform"] is null ? waveform : ParseWaveform(ion["waveform"]!.GetValue<string>(), dutyCycle);
            var field = new WeightedDrivenField(fields, Required(ion, "direct", bases.Length), Required(ion, "amp", bases.Length), wf, f,
                ion["phase"]?.GetValue<double>() ?? throw new InvalidOperationException("missing 'phase'"));
            var mass = ion["mass"]?.GetValue<double>() ?? throw new InvalidOperationException("missing 'mass'");
            if (!(mass > 0.0))
            {
                throw new InvalidOperationException($"mass {mass} u is not positive");
            }

            var species = IonSpecies.FromMassToCharge(mass, 1);
            var p = Required(ion, "pos", 3);
            var d = Required(ion, "dir", 3);
            var dn = Math.Sqrt((d[0] * d[0]) + (d[1] * d[1]) + (d[2] * d[2]));
            if (!(dn > 0.0) || !double.IsFinite(dn))
            {
                throw new InvalidOperationException("'dir' has no direction");
            }

            var ke = ion["ke"]?.GetValue<double>() ?? throw new InvalidOperationException("missing 'ke'");
            if (!(ke >= 0.0) || !double.IsFinite(ke))
            {
                throw new InvalidOperationException($"ke {ke} eV is not a finite non-negative energy");
            }

            var speed = Math.Sqrt(2.0 * species.ChargeSi * ke / species.MassSi);
            var launch = new PhaseState(new Vec3(p[0] * 1e-3, p[1] * 1e-3, p[2] * 1e-3),
                new Vec3(d[0] / dn, d[1] / dn, d[2] / dn) * speed);
            TrajectoryStopFunction detector = (in PhaseState s) => Vec3.Dot(s.Position - detPoint, detNormal);
            var settings = new IntegrationSettings { RelativeTolerance = rtol, MaximumFlightTime = maxT };
            var t0 = Stopwatch.StartNew();
            TrajectoryResult r;
            if (ladder)
            {
                r = FlightTimeStudy.Run(launch, species, field, settings, detector).Runs[^1];
            }
            else
            {
                r = TrajectoryIntegrator.Integrate(launch, species, field, settings, detector);
            }

            var fp = r.FinalState.Position;
            return new JsonObject
            {
                ["id"] = id,
                ["outcome"] = r.Outcome.ToString(),
                ["surface"] = r.StruckSurface,
                ["final_mm"] = new JsonArray(fp.X * 1e3, fp.Y * 1e3, fp.Z * 1e3),
                ["tof_us"] = r.FlightTimeSeconds * 1e6,
                ["steps"] = r.AcceptedSteps,
                ["rejected"] = r.RejectedSteps,
                ["wall_s"] = t0.Elapsed.TotalSeconds,
            };
        }

        var output = new JsonObject
        {
            ["solve_s"] = new JsonArray(solveSeconds.Select(s => (JsonNode)s).ToArray()),
            ["fly_s"] = flyClock.Elapsed.TotalSeconds,
            ["total_s"] = clock.Elapsed.TotalSeconds,
            ["warnings"] = basisWarnings,
            ["ions"] = new JsonArray(results.Select(x => (JsonNode)x).ToArray()),
        };
        // A non-finite number anywhere would otherwise throw here and lose every result; NaN and Infinity
        // are written as their literals, which Python's json module reads back.
        File.WriteAllText(args[1], output.ToJsonString(new JsonSerializerOptions { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals }));
        Console.Error.WriteLine($"{ions.Count} ions in {flyClock.Elapsed.TotalSeconds:F1} s; total {clock.Elapsed.TotalSeconds:F1} s");
        return 0;
    }
}
