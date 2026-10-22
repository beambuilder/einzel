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
that. A `.csv` is also accepted wherever an ion list is (explicit rows only),
and its errors are addressed as `/<row>/<column>`, rows counted from 0 after the
header. A CSV row states its mass, charge, launch and birth time in full and
defaults nothing from the model, but the model is checked as for a JSON list: a
model with a cloud or a mixture is refused at `/`, and a row born more than 1000
maximum flight times late at `/<row>/birth_time_s`.

**The expanded `.ions.csv` reproduces the list anywhere.** A seeded set expands
through `System.Random` and the platform's maths library, which nothing promises
to be identical on every machine or runtime; the expanded rows are numbers, and
a flight started from them starts from the same states wherever it runs.
