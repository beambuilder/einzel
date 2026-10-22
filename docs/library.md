# Using Einzel as a library

`einzel run` solves a model's field, flies one ion or one source cloud, and
writes a result with its manifest. A program that needs one field for many
ions, each with its own launch, voltages or RF phase (a stability scan, an
explicit ion list), can do the same in process: reference the engine
assemblies, solve once, and fly as many ions as it likes against the field it
holds. This page is the short version of how. The fork's batch driver,
`tools/quadbatch`, is a complete program built this way.

## What to reference

| Project | What the host uses from it |
| --- | --- |
| `src/Einzel.Core/Einzel.Core.csproj` | the compiled model and its validation: `ModelValidator` (`src/Einzel.Core/Model/ModelValidator.cs`), `Vec3` |
| `src/Einzel.Io/Einzel.Io.csproj` | the model format: `ModelJson` (`src/Einzel.Io/ModelJson.cs`) |
| `src/Einzel.Fields/Einzel.Fields.csproj` | the field: `FieldAssembly` (`src/Einzel.Fields/FieldAssembly.cs`), `DrivenSolvedField` (`src/Einzel.Fields/Solved/DrivenSolvedField.cs`), `RfWaveform`, `WeightTerm` |
| `src/Einzel.Transport/Einzel.Transport.csproj` | flying ions: `IonSpecies`, `PhaseState`, `IntegrationSettings`, `TrajectoryIntegrator` (`src/Einzel.Transport/Integration/TrajectoryIntegrator.cs`), `FlightTimeStudy` (`src/Einzel.Transport/Integration/FlightTimeStudy.cs`) |
| `src/Einzel.Project/Einzel.Project.csproj` | optional: the field cache, `FieldCache` (`src/Einzel.Project/FieldCache.cs`) over a `ProjectLayout` |

The namespaces are `Einzel.Core.Geometry`, `Einzel.Core.Model`, `Einzel.Io`,
`Einzel.Fields`, `Einzel.Fields.Analytic`, `Einzel.Fields.Solved`,
`Einzel.Transport`, `Einzel.Transport.Integration` and `Einzel.Project`.

## Solve once, fly many

```csharp
// Load a model: validate it, with the files it names resolved against its directory,
// and refuse it with the first error.
static CompiledModel Load(string path)
{
    var validation = ModelValidator.Validate(
        ModelJson.Parse(File.ReadAllText(path)), null, Path.GetDirectoryName(Path.GetFullPath(path)));
    if (!validation.IsValid) throw new InvalidOperationException(validation.Errors[0].Constraint);
    return validation.Model!;
}

// Solve once: build the model's field. With a FieldCache the solved channels are kept
// in the project's .einzel/cache, and the next build loads them.
var cache = new FieldCache(new ProjectLayout(projectRoot));
var model = Load(modelPath);
var (field, warnings) = FieldAssembly.BuildReported(model, cache);

// Fly many: the field is immutable, so every ion samples it from its own thread.
var species = IonSpecies.FromModel(model);
var settings = new IntegrationSettings
{
    RelativeTolerance = model.RelativeTolerance,
    MaximumFlightTime = model.MaximumFlightTimeSi,
};
var (plane, normal) = (model.DetectorPoint, model.DetectorNormal);
TrajectoryStopFunction detector = (in PhaseState s) => Vec3.Dot(s.Position - plane, normal);
var launches = Enumerable.Range(0, 64).Select(i => new PhaseState(
    model.SourcePosition + new Vec3(0, (i - 32) * 1e-5, 0),
    model.SourceDirection * model.LaunchSpeedSi())).ToArray();
var results = new TrajectoryResult[launches.Length];
Parallel.For(0, launches.Length, i =>
    results[i] = TrajectoryIntegrator.Integrate(launches[i], species, field, settings, detector));

// A drive per ion: static basis models (one electrode at 1 V, the rest at 0 V), solved
// once, weighted with this ion's DC, RF amplitude and RF phase in cycles.
var bases = basisPaths.Select(p => FieldAssembly.BuildReported(Load(p), cache).Field).ToArray();
var perIon = DrivenSolvedField.Create(
    bases,
    direct: [30.0, -20.0],
    harmonics: [[new WeightTerm(0, 0.0, 0.25)], [new WeightTerm(0, 50.0, 0.25)]],
    frequenciesHz: [1.1e6],
    waveforms: [new RfWaveform.Sinusoid()]);
var ion = TrajectoryIntegrator.Integrate(launches[32], species, perIon, settings, detector);
```

`projectRoot`, `modelPath` and `basisPaths` are the host's. This code is
compiled and run by the test suite: it is the marked region of
`tests/Einzel.Cli.Tests/LibraryExampleTests.cs`, on a small two-dimensional
model, and a test fails when this block and that region differ.

- **The cache is the one `einzel run` uses.** A `FieldCache` over a project
  directory reads and writes the same `.einzel/cache/` entries, keyed per
  basis channel on the geometry and the solver-behaviour version, and checked
  on every load. A host and the CLI working in one project share solves, and
  a host's basis solve and a model's channel share an entry when their
  fingerprints agree: in the example the rod basis (rod at 1 V, plate at 0 V)
  is the driven model's RF channel and loads it, so the project's cache holds
  three entries, not four.
  Without a store, `BuildReported(model)` solves every time.
- **The warnings travel with the field.** `BuildReported` returns them beside
  it; a solve that missed its tolerance looks like one that met it (GRD-2).
- **`Integrate` returns bare numbers; `FlightTimeStudy.Run` returns a
  measurement.** `TrajectoryResult` is one flight at one tolerance. A flight
  time that carries its convergence evidence and warnings, as GRD-1 requires
  of a result, comes from `FlightTimeStudy.Run`, whose `FlightTime` is a
  `Measured` from flying the same ion down a tolerance ladder; that is what
  `einzel run` reports.

## A drive per ion

`DrivenSolvedField.Create` weights basis fields the host has solved: per
channel a direct weight in volts plus drive terms `WeightTerm(drive,
amplitude, phase)`, the phase in cycles of that drive. The field at time t is
the sum over channels of `(direct + amplitude * waveform.At(f t + phase)) *
channel`. For an ion list with a per-ion RF phase, solve one static basis
model per electrode group once (that group at 1 V, every other electrode at
0 V) and make one field per ion; the basis fields are shared, not copied, and
weighting them costs nothing next to a flight.

This is the same class `einzel run` builds for a driven solved element, so a
host's basis split and the model's own channels are one physics: on a plate
and rod in two dimensions the two agree to 3e-10 V
(`DrivenSolvedFieldFactoryTests`), the difference between two solves of one
linear problem. `Create` refuses what a caller can get wrong: counts that
disagree, a term naming a drive that does not exist, a non-finite weight or
phase, a frequency that is not finite and positive.

## The time-free trap

`DrivenSolvedField` is an `ITimeVaryingField`, and every time-varying field is
also a time-free `IElectrostaticField`. **The time-free
`ElectricFieldAt(position)` does not fail on a driven field: it answers at
t = 0.** The integrator asks for the instant it is at; a host sampling the
field itself has to do the same, through `ElectricFieldAt(position, t)` and
`PotentialAt(position, t)`. This project has met that trap eight times; see
*The interface seams* in [Extending the platform](extending.md).
