# Ion lists

An ion list says which ions a flight launches: one row per ion, or a set that a
seeded generator expands into rows. It is a document of its own,
`<name>.ions.json`, beside the model rather than inside it. The model describes
the instrument and keeps its hash when the ions change, so one list serves many
models (one model per scan point, one shared list) and a `run` of the model is
not reported as edited because its ions were.

The reading, checking and expansion live in `src/Einzel.Io/IonLists/IonList.cs`;
the expanded row type is `IonRow` in `src/Einzel.Transport/IonRow.cs`.

## The document

```json
{
  "schemaVersion": "0.1",
  "maxParallelism": 8,
  "ions": [
    { "id": 1, "rfPhase": 0.75, "tag": "k0",
      "position": { "value": [0, 0.02, 0.02], "unit": "mm" },
      "direction": { "value": [1, 0, 0] },
      "kineticEnergy": { "value": 2, "unit": "eV" } },
    { "id": 2, "massToCharge": { "value": 101, "unit": "Da" },
      "birthTime": { "value": 0.1, "unit": "us" } }
  ],
  "ionSets": [
    { "cloud": { "ions": 48, "seed": 1,
                 "temperature": { "value": 300, "unit": "K" },
                 "transverseSpread": { "value": 0.02, "unit": "mm" },
                 "energyFractionSpread": 0.01 },
      "tag": "cloud" }
  ]
}
```

| Key | Means |
| --- | --- |
| `schemaVersion` | Required, `"0.1"`. The ion list's own version; the model's is separate |
| `ions` | Explicit rows, in launch order |
| `ionSets` | Generated rows, appended after the explicit ones, set by set |
| `maxParallelism` | How many ions may fly at once when the list is flown; omit for one per processor. At least 1 |

A list declares at least one row or one set. **An unknown key is refused** with
its JSON pointer, as in a model: a misspelled attribute silently dropped would
launch the model's default ion and read as though it launched another.
A key written twice is refused too; before PR #13 the later one was kept.

## A row

| Attribute | Unit | Default | Notes |
| --- | --- | --- | --- |
| `id` | integer | required | Non-negative, unique within the list. Results are reported by it |
| `massToCharge` | mass, e.g. `Da` | the model's `ion.massToCharge` | Positive |
| `chargeNumber` | integer | the model's `ion.chargeNumber` | Signed, never zero. The mass is `massToCharge × |chargeNumber|`, as for the model's ion |
| `position` | length, e.g. `mm` | the model's `source.position` | Three components |
| `direction` | none | the model's `source.direction` | Normalised; zero length is refused |
| `kineticEnergy` | energy, e.g. `eV` | see below | Not negative. Not together with `accelerationPotential` |
| `accelerationPotential` | potential, e.g. `V` | see below | `|q|·|V|`, as for the model's source |
| `birthTime` | time, e.g. `us` | 0 | Not negative, and at most 1000 of the model's `maximumFlightTime`. SIMION's `tob` |
| `rfPhase` | cycles | none | `birthTime = rfPhase / f`. Only for a model with exactly one declared drive frequency; not together with `birthTime` |
| `tag` | text | empty | Carried to the results. One CSV field: no comma, double quote or line break |
| `overrides` | per parameter | none | Values for the model's free parameters this row is flown with; see [Parameter overrides](#parameter-overrides) |

Quantities are written as in a model, `{ "value": …, "unit": "…" }`, and may be
an `expression` over the model's parameters; units are required and checked
against the dimension the attribute needs.

**Energy.** A row with neither `kineticEnergy` nor `accelerationPotential` takes
the model's source energy, `|q| × |accelerationPotential| × (1 + energyFraction)`,
with the row's own charge. A row that states either one states the whole energy:
the model's `energyFraction` is an offset on the model's own potential and does
not apply to it.

**A row restating the model is the model's ion, to the bit.** Defaults are
resolved with the validator's own arithmetic: a row with only an `id` has the
species `IonSpecies.FromModel` gives and the launch `einzel run` starts from.

**`rfPhase` is a birth time**, not a phase offset on one electrode: the ion is
born `rfPhase / f` into the model's timeline and sees every drive, stage and
switch as they stand then. Phase 0 is the instant a single-ion `run` starts at.
A model that declares no drive, or two frequencies, has no single cycle to count
in, so `rfPhase` is refused there with the reason; state `birthTime` instead.
The count is of declared frequencies, on every element that carries one (an ideal
quadrupole, a solved 2D or 3D drive, an analytic element a sequence phase
re-compiles), each distinct value once; a drive declared at zero amplitude still
counts.

**A birth far beyond the model's clock is refused.** The birth time is added to
the flight's own time, so a row born at 10^300 s would have its flight time
resolved to a unit in the last place of 10^300. A row born more than 1000 of the
model's maximum flight times late is refused at its `birthTime` or `rfPhase`.

## The model the list launches into

The model supplies every default, so it must have one ion and one launch:

- a mixture (`species`) is refused, since there is no single ion to default to;
- a model whose `source.cloud` is a cloud is refused, since the model and the
  list would be two statements about the launch. Move the cloud into the list
  as a `cloud` set.

Both are refused at `/`, for a `.csv` as for a JSON list.

## Parameter overrides

A row or a set may fly through the model with some of its parameters set to
other values:

```json
{ "id": 7, "rfPhase": 0.25, "overrides": { "U": { "value": 20.79, "unit": "V" } } }
```

`overrides` maps the name of a **free model parameter** to a quantity in that
parameter's dimension. The model is compiled again with those values, exactly as
a sweep compiles it: every expression over the parameter follows, derived
parameters included, and the declared bounds still hold. This is the model
format's own rule for changing an operating point ([a phase sets parameters, not
electrode settings](model-format.md#a-phase-sets-parameters-not-electrode-settings)),
so a model whose electrode potentials and drive amplitudes are expressions over
named parameters can be flown at many operating points from one list:

```json
"parameters": {
  "axis": { "value": -8, "unit": "V" },
  "U": { "value": 22.41050655, "unit": "V" },
  "V": { "value": 137.6526976, "unit": "V" }
},
…
{ "name": "rodZp", …, "potential": { "expression": "axis + U", "unit": "V" },
  "driveAmplitude": { "expression": "V", "unit": "V" }, "drivePhase": { "value": 0.75, "unit": "1" } }
```

**What may be overridden.** A free parameter (`value`, not `expression`) the
model declares. Refused, each at `/ions/<k>/overrides/<name>` or
`/ionSets/<s>/overrides/<name>`: a name the model does not declare, a derived
parameter (override what it is derived from), a unit of another dimension, a
value outside the parameter's `minimum`/`maximum`, an `expression` (an override
is a number), a missing unit, and a name written twice in one object (which a
model's own parser would let the later one win; the ion list refuses a key
written twice anywhere). A value that makes a derived parameter leave its bounds
is refused at the row's or set's `overrides`. A set's `overrides` is one constant for every row of
the set, generated or `cloud`; it draws nothing, so a set's rows are the same
with and without it.

**What an override is for.** The fields. An override that would change the ion,
the source, the detector, the integration settings, the flight-time limit or the
gas is refused when the list is flown: a row's defaults (`massToCharge`,
`position`, the energy) are resolved against the model as written, and an
override that moved them would leave the row and its field disagreeing. State
those on the row instead. A row's or set's own expressions (an attribute written
as `{ "expression": "Ek", "unit": "eV" }`) are evaluated with its overrides
layered on the model's parameters, as its field is: a row that overrides `Ek`
and states its energy as `Ek` launches at the overridden value.

Until `fly` flies the groups (PR #14), it refuses a list with overrides at the
first row's or set's `overrides`, rather than fly every row through the model as
written.

**Groups.** Rows are grouped by their overrides: same parameter names and the same
bits of their SI values (`1 V`, `1.0 V` and `1000 mV` are one group; key order
inside the object does not matter; `-0` and `0` are one value). Groups are
numbered in the order their first row appears, and the rows without overrides
are a group of their own. Rows keep their place in the list; the group is a
property of the row. Values that are equal only after unit conversion can differ
in the last bit (a value in `kV` and the same value in `mV` are each rounded
once to SI) and then fly as two groups, each correct, the second at the cost of
another field; write one unit throughout a list to share the field.

**What a group costs.** Each group is one compiled model and one field, built
through the [field cache](cli.md#a-second-run-of-the-same-geometry-loads-its-field), so what it costs is what the
override changes in the solved channels:

| Override changes | Cost per new value |
| --- | --- |
| a drive amplitude (`V` on an RF pair) | none: the same channels, other weights; a cache load |
| a phase common to every tap of a drive (`drivePhase` over a parameter) | none for a single-phase structure such as a quadrupole; two new quadrature channels for a multi-phase sinusoid (a travelling wave) |
| the DC pattern (`U` on a quadrupole, any non-uniform DC change) | one solve of the new DC channel, then cached |
| any potential of a static, undriven model (a lens) | one solve, since a static model is one channel at its absolute potentials |

**An RF phase that is not a birth time.** `rfPhase` moves the ion's birth, so
the ion sees the drive *and* any `sequence` timeline from that instant. A
phase on the drive alone is written as `drivePhase` over a parameter and
overridden per row (`"drivePhase": { "expression": "phi", "unit": "1" }` on
every tap, `"overrides": { "phi": … }`). For a single-phase drive this is a
weight change, solve-free. On the corpus quadrupole a common shift of 1/16
written either way gives the same outcome for 144 of 144 ions; the two are not
bit-equal (different arithmetic), and they differ only where a sequence runs.

## The `cloud` set

```json
{ "cloud": { …the keys of source.cloud… }, "n": 48, "firstId": 100, "tag": "cloud" }
```

`cloud` takes the keys of the model's `source.cloud` (see
[Model format](model-format.md)) and is checked by the same validator. The set
draws exactly the ions `einzel run` draws for a model carrying that cloud: the
same nominal launch, species, axis, seed and order, bit for bit. A test holds
this on every shipped example with a cloud.

| Key | Means |
| --- | --- |
| `cloud` | The cloud block. `population` is refused: a list flies its ions one at a time, so a packet population has nothing to act on |
| `n` | The ion count, in place of `cloud.ions`; state one or the other (`cloud.ions` of 1 is its default and counts as unstated). Raising it keeps the earlier ions unchanged (the draw is prefix-stable) |
| `firstId` | The first row's id; the rest follow by one |
| `tag` | Given to every row of the set |
| `overrides` | [Parameter overrides](#parameter-overrides) for every row of the set |

## Generated sets

A set without `cloud` builds its rows from constants and seeded generators:

```json
{ "n": 48, "seed": 7, "firstId": 1, "tag": "grid",
  "massToCharge": { "value": 100, "unit": "Da" },
  "kineticEnergy": { "gaussian": { "mean": 2, "sigma": 0.05 }, "unit": "eV" },
  "rfPhase": { "sequence": { "first": 0, "step": 0.125 } },
  "position": { "x": { "value": 0 },
                "y": { "uniform": { "min": -0.05, "max": 0.05 } },
                "z": { "gaussian": { "mean": 0, "sigma": 0.02 } }, "unit": "mm" },
  "direction": { "cone": { "axis": [1, 0, 0], "halfAngle": { "value": 5, "unit": "deg" }, "fill": true } } }
```

| Key | Means |
| --- | --- |
| `n` | Required; how many ions, at least 1 |
| `seed` | The seed of the set's one `System.Random`. Required when any attribute draws at random (`uniform`, `gaussian`, `disc`, `cone`); recorded in the expansion's seeds. A negative seed draws the same stream as its absolute value (the seeded `Random` takes \|seed\|, as a model's cloud does) and is recorded as written |
| `firstId`, `tag` | As for a `cloud` set |
| `chargeNumber` | A constant integer |
| `massToCharge`, `kineticEnergy` or `accelerationPotential`, `birthTime` or `rfPhase` | A scalar: `{ "value": … }` or one generator, with a `unit` (none for `rfPhase`, which is in cycles) |
| `position` | `{ "value": [x, y, z] }`, `{ "disc": … }`, or all three of `x`, `y`, `z` as scalars; `unit` applies to all of it and to an axis that names none |
| `direction` | `{ "value": [x, y, z] }` or `{ "cone": … }` |

Generator parameters (`min`, `max`, `mean`, `sigma`, `first`, `step`, `last`,
`centre`, `radius`, …) are plain numbers in the attribute's `unit`; unlike a row's
quantities they take no `expression`. Only `cone.halfAngle` is a quantity with its
own unit.

An omitted attribute takes the model's value, as in a row, and every generated ion
is then compiled exactly as a written row: a set of constants gives the same rows
as the same constants written out, and a drawn value outside an attribute's
bounds (a negative mass, a negative birth time, a position that overflows) refuses
the set at that attribute's pointer, such as `/ionSets/0/position`.

### Generators

| Generator | On | Draws per ion | Gives |
| --- | --- | --- | --- |
| `uniform { min, max }` | scalars, position axes | 1 | uniform on [min, max); `max` not below `min` |
| `gaussian { mean, sigma }` | scalars, position axes | 2 per attempt | normal, by the polar Box-Muller method `IonCloud` uses; `sigma` not negative |
| `sequence { first, step }` | scalars, position axes | 0 | `first + i·step` for the i-th ion of the set |
| `linspace { first, last }` | scalars, position axes | 0 | `n` values, both ends exact (`first·(1−t) + last·t`, t = i/(n−1)) |
| `disc { centre, normal, radius }` | `position` | 2 | uniform over the disc's area: radius `R·√u`, then azimuth; `radius` positive, `normal` not zero |
| `cone { axis, halfAngle, fill }` | `direction` | 2 | `fill: true` (default): uniform in solid angle inside the cone, cos θ uniform; `fill: false`: on the cone's surface, uniform in azimuth. `halfAngle` an angle with a unit, above 0 and at most 90° |

A generator where it does not belong (`cone` on an energy, `disc` on a direction)
is an unknown key and refused on reading.

A `cone` may open to 90°, where a model's `cloud.divergence` stops short of it: a
divergence is the spread of a beam, and at 90° it is no longer one, while a cone set
is a set of directions, and a hemisphere (`fill: true`) or a ring at right angles
to the axis (`fill: false`) is a legitimate one.

### Draw order

One `System.Random(seed)` per set. Ions are drawn one after another, and within an
ion the attributes in this order: `massToCharge`, the energy, the birth
(`birthTime` or `rfPhase`), `position` (x, y, z for a per-axis position),
`direction`. A constant, `sequence` and `linspace` draw nothing; `cone` draws
twice whether it fills or not. The first k ions of a set are therefore the same
whatever its `n` (prefix-stable), except under `linspace`, whose spacing depends
on `n`.

### A birth-phase grid

The 06c RF validation cases (project side) fly 8 birth phases per launch
variant. As a set over one launch, that is a `sequence` on `rfPhase`:

```json
{ "n": 8, "rfPhase": { "sequence": { "first": 0, "step": 0.125 } } }
```

which gives birth times 0, T/8, …, 7T/8 for the model's one drive period T, and
needs no seed. The same grid in time is `{ "birthTime": { "sequence": { "first":
0, "step": 0.11363636363636363 }, "unit": "us" } }` at 1.1 MHz. A grid over phases
and launch variants together (48 rows) is written as explicit `ions`, as the
06c cases' list is.

### Reproducibility of seeded sets

`System.Random(seed)` with a fixed seed is the legacy algorithm .NET keeps for
seeded construction. The test suite compares one seeded 50-ion set per generator
with stored golden rows (`tests/Einzel.Io.Tests/Golden/ion-sets/`, recorded on
Windows):

- `uniform`, `sequence` and `linspace` use only IEEE addition, multiplication and
  division, and are compared bit for bit on every runner;
- `gaussian`, `disc`, `cone` (filled and surface) and a per-axis position call
  `Math.Log`, `Math.Sqrt`, `Math.Cos` or `Math.Sin`, which the platform's maths
  library computes; they are compared bit for bit on Windows and within 1e-12
  relative elsewhere.

Where a seeded expansion differs between machines, the expanded `.ions.csv` is the
anchor: it gives the same starting states anywhere.

## Order and ids

Rows come out in a fixed order: the explicit `ions` as written, then each set's
rows set by set. A set's ids start at `firstId` or, when it is omitted, at one
more than the largest id before the set in the document (1 for a list that
starts with a set). Ids are therefore a function of the document alone. An id
used twice is refused at the row or set that repeats it.

## Errors

Every error carries a code, a JSON pointer into the ion list and the violated
constraint, as the model validator's do. Each row and each set is checked, one
error per row or set, so one pass lists every mistake. A mistake in the
document's own keys (`schemaVersion`, `maxParallelism`) does not stop the rows
being checked; only a model the list cannot launch into does, since every row
reads its defaults from it.

| Code | Example pointer | Cause |
| --- | --- | --- |
| `SCHEMA_INVALID` | `/ions/3/id` | missing or repeated id |
| `SCHEMA_INVALID` | `/ions/3` | a row or set that is not an object (`null`) |
| `SCHEMA_INVALID` | `/ions/0/accelerationPotential` | both energy forms |
| `SCHEMA_INVALID` | `/ions/0/rfPhase` | with `birthTime`, or the model has no single drive frequency |
| `SCHEMA_INVALID` | `/ions/0/energy` | unknown key |
| `SCHEMA_INVALID` | `/` | empty list; mixture model; model with a cloud |
| `VALUE_OUT_OF_BOUNDS` | `/ions/0/birthTime` | negative birth time, phase, energy, id; a birth more than 1000 maximum flight times late; zero charge or direction; non-positive mass |
| `UNITS_INCOMPATIBLE` | `/ions/0/kineticEnergy` | a unit of the wrong dimension |
| `UNITS_REQUIRED` | `/ionSets/0/birthTime/unit` | a generated attribute without its unit |
| `VALUE_OUT_OF_BOUNDS` | `/ionSets/0/position` | a generated value the row refuses (a non-positive mass, a negative or late birth, a position that overflows), at the set's attribute |
| `SCHEMA_INVALID` | `/ionSets/0/seed` | a set that draws at random without a seed; two forms in one attribute; a generator where it does not belong |
| `VALUE_OUT_OF_BOUNDS` | `/ionSets/0/birthTime/gaussian/sigma` | negative `sigma`, `max` below `min`, `radius` not positive, `halfAngle` outside (0°, 90°], `n` below 1 |
| `SCHEMA_INVALID` | `/ions/0/overrides/U` | not a parameter of the model; a derived one; an `expression`; the key written twice |
| `SCHEMA_INVALID` | `/ions/0/id` | any key written twice: "a key written twice" |
| `UNITS_INCOMPATIBLE` | `/ions/0/overrides/U` | a unit of another dimension than the parameter's |
| `UNITS_REQUIRED` | `/ions/0/overrides/U` | an override without its unit |
| `VALUE_OUT_OF_BOUNDS` | `/ionSets/0/overrides/U` | outside the parameter's declared bounds |
| `VALUE_OUT_OF_BOUNDS` | `/ions/0/overrides` | "with these overrides the model is refused at /parameters/W": a derived parameter leaves its bounds |
| `SCHEMA_INVALID` | `/ions/0/overrides` | (fly) "parameter overrides are not flown yet (PR #14)": any list with overrides, until PR #14 |
| `SCHEMA_INVALID` | `/3/p:X` | a `.csv` column naming no free parameter, at the first row of its group |

## The expanded `.ions.csv`

The expanded rows are written as CSV, one line per ion, every number in SI. Two
rows of a 48-ion list (8 birth phases × 6 launch variants, 2 eV) expanded against
an m/z 100 quadrupole model driven at 1.1 MHz, `rfPhase` 0.75:

```
id,tag,mass_kg,charge_C,x_m,y_m,z_m,vx_m_per_s,vy_m_per_s,vz_m_per_s,birth_time_s
37,k6v0,1.6605390689199998E-25,1.602176634E-19,0,2E-05,2E-05,1964.5389486782906,0,0,6.818181818181818E-07
40,k6v3,1.6605390689199998E-25,1.602176634E-19,0,2E-05,2E-05,1957.0632848680448,171.22085122875993,0,6.818181818181818E-07
```

Numbers use round-trip (`R`) formatting and the invariant culture, lines end in
`\n`, so reading the file back gives the same rows to the bit; a test holds
that. A list with overrides adds one column per overridden parameter after the
eleven, `p:<name>` in ordinal name order, holding the SI value (volts for a
potential, cycles for a phase) or nothing where the row does not override it:

```
id,tag,mass_kg,charge_C,x_m,y_m,z_m,vx_m_per_s,vy_m_per_s,vz_m_per_s,birth_time_s,p:U,p:V
1,k0v0,1.6605390689199998E-25,1.602176634E-19,0,2E-05,2E-05,1964.5389486782906,0,0,0,20.793253499922816,
```

Read back, the rows group as they did. A list without overrides writes exactly
the eleven columns, as before overrides existed, and a file with the eleven is
read as a list without overrides. A `.csv` is also accepted wherever an ion list is (explicit rows only),
and its errors are addressed as `/<row>/<column>`, rows counted from 0 after the
header. A CSV row states its mass, charge, launch and birth time in full and
defaults nothing from the model, but the model is checked as for a JSON list: a
model with a cloud or a mixture is refused at `/` (and by `fly` before the list
is read, at the model's own declaration), and a row born more than 1000 maximum
flight times late at `/<row>/birth_time_s`.

**The expanded `.ions.csv` reproduces the list anywhere.** A seeded set expands
through `System.Random` and the platform's maths library, which nothing promises
to be identical on every machine or runtime; the expanded rows are numbers, and
a flight started from them starts from the same states wherever it runs.

## How rows are flown

Every row is one independent trajectory, flown by `IonListFlight.Fly` in
`src/Einzel.Transport/Integration/IonListFlight.cs`, the same loop that flies
the cloud of a `run`:

- **Birth time.** A row born at `t > 0` flies the model's field through
  `TimeShiftedField`, so it sees every drive, stage and switch as they stand at
  its birth plus its own flight time. A row born at 0 flies the field itself,
  unwrapped, and is the flight a single-ion `run` makes to the bit. A static
  field has no clock and is flown as it is. Flight times and the model's
  `maximumFlightTime` count from the row's birth.
- **Tolerance.** One integration at the model's `relativeTolerance` per row,
  which is bit for bit the first rung of the three-rung ladder a `run` reports
  its flight time from. Asked for the ladder, each row runs all three and reports
  the finest with the residual as its interval.
- **Parallelism.** Rows fly at once on up to `maxParallelism` threads and are
  written back by index, so the order they finish in changes nothing and
  `maxParallelism` is a cost, never an input: a test flies a 304-row list at one
  thread and at eight, in vacuum and in a gas, and gets the same bits. Every field kind the CLI builds reads only state fixed at
  construction while an ion is flown.
- **Seeds.** Row k's collisions are drawn from the gas seed plus k, its index
  in the list, not its id: row k of a `cloud` set is ion k of the cloud `run`
  flies, collisions included, and appending rows leaves the earlier ones alone.
- **A row that throws** does not stop the others: the core carries the exception
  as that row's failure and flies the rest. The integrator itself throws nothing
  in flight, so a thrown row is a defect, and the caller rethrows the first one
  with its own type and stack, as the cloud of a `run` does; `fly` then exits 6.

## Flying a list

`einzel fly` (see [the CLI](cli.md#flying-an-ion-list)) flies a list through one
model. The 06c RF validation cases (project side) fly 48 ions per scan point - 8
birth phases times 6 launch variants - through a model whose rods are driven at
1.1 MHz with `drivePhase` 0.75:

```json
{
  "schemaVersion": "0.1",
  "ions": [
    { "id": 1, "rfPhase": 0, "tag": "k0v0",
      "position": { "value": [0, 0.02, 0.02], "unit": "mm" },
      "direction": { "value": [1, 0, 0] },
      "kineticEnergy": { "value": 2, "unit": "eV" } },
    { "id": 2, "rfPhase": 0, "tag": "k0v1",
      "position": { "value": [0, 0.05, 0.02], "unit": "mm" },
      "direction": { "value": [1, 0, 0] },
      "kineticEnergy": { "value": 2, "unit": "eV" } }
  ]
}
```

(the full file has 48 such rows, `rfPhase` k/8 for k = 0..7). Then

```
einzel fly models/quad.json --ions ions/corpus48.ions.json --vtu
```

writes:

| File | Holds |
| --- | --- |
| `results/quad.corpus48.fly.manifest.json` | the run manifest: model hash, the list's hash in `inputHashes`, the seeds (none here: nothing is drawn), `maxParallelism` |
| `results/quad.corpus48.fly.json` | per ion: id, tag, outcome, surface, final position in mm, flight time in µs since birth, steps, rejected steps, collisions, energy drift, warnings; counts per outcome and surface; timings |
| `results/quad.corpus48.fly.csv` | the same rows as `id,tag,outcome,surface,x_mm,y_mm,z_mm,time_us,steps,rejected,collisions,warnings` |
| `results/quad.corpus48.fly.ions.csv` | the expanded rows; `fly --ions` on it flies the same ions again |
| `.einzel/quad.corpus48.fly.trajectories.vtu` | with `--vtu`: every ion's path as one polyline, `id` and `outcome` as cell data, `time_us` as point data; large and regenerable, so beside the cache as `run`'s trajectory is |

The files are named by the model and the list (`a.ions.json` and `a.csv` share a
name, and the later flight replaces the earlier one, saying so on stderr), and a
flight without `--vtu` removes a path an earlier `--vtu` flight left. See
[the CLI](cli.md#flying-an-ion-list) for the exit codes, the regime warnings a gas
model earns as under `run`, and what a row that throws does.

An ion's RF phase in this list is its birth time: the ion born at `rfPhase` k/8 sees
the drive's argument `f·(t + k/(8f)) + 0.75`, which is the argument
`f·t + (k/8 − 1/4)` of the harness (`tools/quadbatch`) modulo one cycle. Flown so,
the 48 ions agree with the harness in outcome and surface, and their final positions differ by less than a nanometre
against both matching harness blocks (the two-channel field against the harness's
four bases). An ion is transmitted, in the 06c cases' terms, when its final x is
above 90 mm and its outcome is `StruckElectrode` (on the detector disc) or
`StopConditionMet`.

## SIMION `.fly2` parity

How the `.fly2` constructs used in this project map onto an ion list in this
build. "Later" marks what later milestones add.

| `.fly2` | SIMION | Ion list | Status |
| --- | --- | --- | --- |
| `particles{coordinates=0}` | workbench mm | quantities with a unit each | parity |
| `standard_beam{n}` | n particles | a set's `n` | parity |
| `mass` (u), `charge` (e) | per beam | `massToCharge` with a unit, `chargeNumber` | parity (m/z rather than mass: `mass = massToCharge × |charge|`) |
| `ke` (eV) | kinetic energy | `kineticEnergy` | parity |
| `tob` (µs) | birth time | `birthTime`, or `rfPhase` in cycles | parity; flown through a time-shifted field |
| `x, y, z` / `position = vector()` | start | `position` | parity |
| `direction = vector()` | | `direction` | parity |
| `az`, `el` | angles | none; convert to `direction` | outside the engine (an importer, project side) |
| `velocity` | velocity | none; convert to `direction` and `kineticEnergy` | outside the engine |
| `cwf` | charge weight for repulsion | none | gap; later (B2.4) |
| `color` | display group | `tag` | parity |
| `uniform_distribution{min, max}` | uniform | `uniform` | parity |
| `gaussian_distribution` | normal | `gaussian` | parity |
| `line_sequence` / `arithmetic_sequence` | deterministic | `sequence`, `linspace` | parity |
| `disc_distribution` | uniform in a disc | `disc` | parity (SIMION semantics Not verified: its name and any fill or ring option) |
| `cone_direction_distribution{axis, half_angle, fill}` | cone | `cone` | parity (SIMION semantics Not verified: the list caps the half-angle at 90°, SIMION's range unchecked) |
| `distribution(fn)` | Lua function | none | gap; expression-defined distributions deferred |
| Lua loops and tables | a program builds the list | explicit `ions` | parity through rows; no Lua |
| `.ion` | explicit list | `ions`, or a `.ions.csv` | parity in content; importer project side |
| clock-seeded random | not reproducible | `seed`, and the expanded `.ions.csv` | beyond |
| `fast_adjust` per ion | per-ion voltages | `overrides` of model parameters on a row or set | parity; a DC change costs one cached solve per distinct pattern where SIMION recombines unit solutions for free |
| `init_p_values` per ion | per-ion static instance voltages | `overrides`, as above | parity; on a static model every distinct voltage set is one solve |
| `adjustable` + `phase_angle_deg` | a global phase | `drivePhase` over a parameter, overridden per row | parity; solve-free for a single-phase drive |
| grouped flight, repulsion | common time step | none for a list | gap; later (B2.4/B2.5) |
| `terminate` record | splat per ion | a `fly` row: id, tag, outcome, surface, final position, time since birth, steps | parity |
