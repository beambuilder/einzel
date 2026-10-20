# QuadBatch

In-process batch driver over Einzel's own solver, interpolant, waveform and
integrator: the basis fields of a model are solved once and thousands of ions
are flown against them, each with its own voltages, RF phase, mass, launch
position, direction and energy.

`einzel run` re-solves the field for every invocation and a source cloud cannot
carry per-ion birth phases. A stability scan of an RF quadrupole against SIMION
needs about 5000 ions per scan line with a per-ion phase, and a 1 M-node 3D
solve costs seconds per basis channel, so the validation project drives the
engine in-process instead. This program does what `einzel run` does, with
Einzel's classes:

```
FieldAssembly.BuildReported(model)   ->   sum_i (direct_i + amp_i * w(f t + phi_i)) * basis_i
TrajectoryIntegrator.Integrate(launch, species, field, settings, detector)
```

except that the basis fields come from static unit-potential models solved once
and are re-weighted per ion. `WeightedDrivenField` mirrors `DrivenSolvedField`,
whose constructor is internal. The validation project checked it against
`einzel run` on its full driven 3D quadrupole model: 0.002 µm in final position
and 3e-8 in flight time over 12 ions, sinusoidal and rectangular drive.

## What `einzel run` does that this program does not

- **Gas.** A basis model that declares a gas is refused; there is no collision
  path here.
- **Field discontinuity at the domain edge.** `DrivenSolvedField` forwards
  `SignedDistanceToDiscontinuity` to its first channel so the integrator lands
  steps on the domain boundary; `WeightedDrivenField` leaves the default (no
  discontinuity). The 12-ion check above did not show a difference on the
  quadrupole models; it is kept this way so that results stay comparable with
  the validation project's recorded ones until the library replaces this tool.
- **Tolerance ladder.** With `ladder: true` the ion is flown down the same
  tolerance ladder as `einzel run`, but only the finest run is reported; the
  study's flight-time interval and its warnings are not.
- **Provenance.** No run manifest, no `Measured` envelopes. Basis-build
  warnings are written to the output under `warnings` and echoed to stderr.

## Usage

```
QuadBatch <job.json> <out.json>
```

Relative paths in the job resolve against the working directory, not the job
file. Example:

```json
{
  "bases": ["basis_rods_a.json", "basis_rods_b.json"],
  "waveform": "sinusoid",
  "frequencyHz": 1.0e6,
  "relativeTolerance": 1e-11,
  "ladder": false,
  "maxFlightUs": 200,
  "detectorPointMm": [95.25, 0, 0],
  "detectorNormal": [-1, 0, 0],
  "threads": 16,
  "ions": [
    { "id": 1, "mass": 100, "direct": [0, 0], "amp": [50, -50], "phase": 0.25,
      "pos": [-5, 0.1, 0], "dir": [1, 0, 0], "ke": 5 }
  ]
}
```

Job fields:

| Field | Required | Meaning |
| --- | --- | --- |
| `bases` | yes | Einzel model files, one per basis channel, each with one electrode group at unit potential and the rest grounded; no gas |
| `frequencyHz` | yes | RF frequency, Hz |
| `detectorPointMm`, `detectorNormal` | yes | a point on the detector plane (mm) and its normal pointing back into the flight volume; the flight stops when the ion crosses it |
| `waveform` | no, `sinusoid` | `sinusoid` or `rectangular`; any other name is an error |
| `dutyCycle` | no, `0.5` | fraction of the period at the positive level, rectangular only |
| `relativeTolerance` | no, `1e-11` | integrator relative tolerance |
| `ladder` | no, `false` | fly the tolerance ladder and report the finest run |
| `maxFlightUs` | no, `200` | maximum flight time, µs |
| `threads` | no, processor count | ions flown in parallel |
| `ions` | yes | the list below |

Per ion:

| Field | Required | Meaning |
| --- | --- | --- |
| `id` | no, the index | copied to the output |
| `mass` | yes | u; charge is +1 e |
| `direct`, `amp` | yes | DC and RF amplitude per basis channel, V; one entry per basis |
| `phase` | yes | RF phase at t = 0, cycles |
| `pos` | yes | launch position, mm |
| `dir` | yes | launch direction, any non-zero length |
| `ke` | yes | kinetic energy, eV, non-negative |
| `waveform` | no, the job's | per-ion override |

Output: `solve_s` (seconds per basis), `fly_s`, `total_s`, `warnings`
(basis-build warnings: `basis`, `severity`, `code`, `message`) and `ions`, one
entry per input ion in input order:

| Key | Meaning |
| --- | --- |
| `id` | from the input |
| `outcome` | `StopConditionMet` (reached the detector plane), `StruckElectrode`, `MaximumFlightTimeReached`, `MaximumStepsExceeded`, `StepSizeUnderflow`, or `Error`. The last three are incomplete flights, not results |
| `surface` | the electrode struck, else null |
| `final_mm` | final position, mm |
| `tof_us` | flight time, µs |
| `steps`, `rejected` | accepted and rejected integrator steps |
| `wall_s` | seconds for this ion |
| `error` | with outcome `Error` only: what was wrong with this ion's input (missing field, wrong array length, zero direction, negative energy, unknown waveform). The other ions are unaffected |

Non-finite numbers are written as `NaN` and `Infinity` literals rather than
failing the whole file.

## Status

Fork-only tool (`beambuilder/einzel`), part of the solution so that it builds in
CI and API drift fails the build rather than the next validation run. It
retires when the library offers solve-once / fly-many with explicit ion lists
through its public surface.
