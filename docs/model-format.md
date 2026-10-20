# Model format, schema 0.9

A model is declarative, schema-validated, diffable JSON. A model file plus its
referenced artifacts fully determines a run.

Two rules govern the whole format:

**Every quantity carries a unit.** `{"value": 4, "unit": "kV"}`, never `4000`.
This is deliberately more annoying than the alternative, because unit ambiguity
is the commonest source of silent wrongness and an agent building a model from
prose is the actor most likely to introduce it. Making a quantity a two-field
object means the failure happens at parse time with a JSON Pointer rather than at
run time as a factor of 1.602e-19.

**Unit symbols are case-sensitive.** `mm` and `Mm` differ by nine orders of
magnitude. A wrong case is an error that names the right symbol, not a silent
acceptance.

## Skeleton

```json
{
  "schemaVersion": "0.2",
  "name": "...",
  "description": "...",
  "parameters": { },
  "ion":        { },
  "source":     { },
  "fields":     [ ],
  "detector":   { },
  "transport":  { }
}
```

## Quantities

```json
{ "value": 4, "unit": "kV" }
{ "expression": "mirrorDepth * 0.5", "unit": "mm" }
```

Any quantity may be an expression over the declared parameters. Vectors are
`{"value": [x, y, z], "unit": "mm"}`; directions are `{"value": [1, 0, 0]}` with
no unit, normalised on load, so `[2,0,0]` and `[1,0,0]` mean the same thing.

## Parameters

The declared parameter surface. This is what a sweep varies, an optimiser
searches, and a tolerance study draws from.

```json
"parameters": {
  "mirrorDepth": {
    "value": 90.0, "unit": "mm",
    "minimum": 20.0, "maximum": 300.0,
    "description": "Depth of the printed mirror, entrance plane to cap."
  },
  "halfGap":     { "expression": "boardGap / 2", "unit": "mm" },
  "rodRatio":    { "value": 1.1468, "unit": "1" }
}
```

A parameter declares **either** `value` **or** `expression`, never both, and
always a `unit` (`"1"` for a pure ratio). Bounds are optional and given as plain
numbers in the same unit.

Bounds are part of the declaration rather than a separate validation pass because
they carry design intent. A mirror depth that may run 20–300 mm says something a
bare nominal does not, and it is what lets a study be written as "vary everything
over its declared range" instead of restating limits the template already knows.
Bounds are **checked, not clamped**: a sweep that walks a parameter past its
declared range has found something the template author did not intend, and
clamping would hide it.

### Where a number came from

A parameter may declare `provenance`, and where that makes a claim it must say what backs it:

```json
"ionEnergy":  {
  "value": 4000.0, "unit": "V",
  "provenance": "published",
  "source": "Grinfeld et al., Nucl. Instrum. Methods Phys. Res. A 1060 (2024) 169017, table 1"
},
"boardGap":   {
  "value": 41.43, "unit": "mm",
  "provenance": "fitted",
  "source": "solved for the design paper's three-point condition"
},
"capToCap":   { "value": 740.0, "unit": "mm", "provenance": "guess" }
```

| | |
| --- | --- |
| `chosen` | a design value the model's author picked. **The default** |
| `published` | stated in a cited source |
| `drawn` | measured off a figure in a cited source |
| `fitted` | solved or optimised by this platform against a stated condition or dataset |
| `guess` | a placeholder nobody has justified, flagged as one |

**`source` is required for `published`, `drawn` and `fitted`, and refused for the other
two.** Required for the same reason section 9 requires a unit: a claim of authority with
nothing behind it is worse than no claim, because a reader cannot recompute it and cannot
tell it apart from one that is backed. Refused where there is nothing to cite, because a
document declaring a source on a value its author chose is saying two things at once — the
same rule that refuses a solve declaring both `drive` and `drives`.

**`drawn` is separate from `published` on purpose.** A number read off a figure carries a
reading error a table does not, and in a reconstruction the two behave differently: a
redrawn figure moves the geometry, a corrected table moves a voltage.

**A run reports the two that qualify its result.** `parameters.guessed` names the guesses —
the result is about a geometry somebody assumed, and it moves if a better value turns up —
and `parameters.fitted` names the fitted ones, because a model agrees with whatever it was
fitted against *by construction* and that agreement is not evidence. Both are severity
`Provenance` rather than validity violations: neither makes a result wrong. `published` and
`drawn` are silent, since a run resting on cited values is the ordinary case and a line on
every model would be noise.

**Why this is in the format rather than in prose.** It is the one thing about a parameter a
study cannot infer — a sweep can perturb one and an optimiser can fit one, and neither can
say whether the nominal was measured or invented. The shipped `astral-3d` template is the
case that forced it: three of its numbers are solved for, thirteen are read off a published
drawing, and the rest are published outright, and which results would move if a better
source turned up depends entirely on which is which. That distinction had lived only as
English inside `description` fields, where nothing could query it and it drifted.

### Expressions

Arithmetic over other parameters: `+ - * /`, parentheses, unary minus, and
`abs(x)`, `sqrt(x)`, `min(a,b)`, `max(a,b)`.

Expressions evaluate over **quantities, not numbers**, so dimensions propagate
through the arithmetic and the declared unit is checked against the dimension the
expression actually produces. That makes the evaluator a unit checker as much as
a calculator: `boardGap / 2` is a length, and a term adding a length to a voltage
fails where it is written rather than thousands of steps later. `sqrt` is
restricted to dimensionless arguments, because the square root of a length has no
representation in an integer-exponent dimension system and silently dropping the
dimension would defeat the check.

Derived parameters are evaluated in dependency order, and a cycle is refused with
the chain named rather than recursed into. Derived parameters **re-evaluate
against overrides**, which is the property that makes sweeping meaningful:
perturb `capToCap` and everything expressed in terms of it follows.

This is a sandbox in the sense that matters: the grammar is arithmetic, nothing
but a parameter can be named, and evaluation cannot loop. It is not a scripting
language and is not meant to become one — extension goes through Python behind a
process boundary, not through the model format.

## Ion, source, detector

```json
"ion":    { "massToCharge": { "value": 500, "unit": "Da" }, "chargeNumber": 1 },
"source": {
  "position":              { "value": [-100, 0, 0], "unit": "mm" },
  "direction":             { "value": [1, 0, 0] },
  "accelerationPotential": { "value": 4, "unit": "kV" },
  "energyFraction":        0
},
"detector": {
  "planePoint": { "value": [-100, 0, 0], "unit": "mm" },
  "normal":     { "value": [1, 0, 0] }
}
```

The source states an *energy*, never a velocity; the launch speed is derived from
the ion's own mass and charge, so the document cannot state two things that
disagree. `energyFraction` offsets it fractionally for acceptance studies.

The detector normal points back into the flight volume: the flight ends when the
ion crosses from the positive side to the negative side, and the integrator lands
on the crossing exactly rather than at the end of whichever step passed it,
because the crossing time is the measurement.

### The source cloud

Without a `cloud`, a source launches **one ion down the axis**, and every figure
computed from it is a property of that ion rather than of the instrument. That is
why resolving powers here have carried the caveat "energy aberration only".

```json
"source": {
  "position":              { "value": [-100, 0, 0], "unit": "mm" },
  "direction":             { "value": [1, 0, 0] },
  "accelerationPotential": { "value": 4, "unit": "kV" },
  "cloud": {
    "ions": 1000,
    "seed": 1,
    "temperature":          { "value": 300, "unit": "K" },
    "transverseSpread":     { "value": 0.3, "unit": "mm" },
    "longitudinalSpread":   { "value": 0.1, "unit": "mm" },
    "energyFractionSpread": 0.01
  }
}
```

| Field | Means |
| --- | --- |
| `ions` | How many **trajectories to compute**. A numerical setting: sampling harder only makes a statistic better. ACC-5 wants transmission to ±1% at 95%, which needs about 9,600 at the worst point |
| `population` | How many ions are **physically in the packet**, which is what pushes on itself. Defaults to `ions` |
| `seed` | So the same study gives the same answer twice |
| `temperature` | Thermal velocity, drawn per component as a Gaussian of width √(kT/m). This is the whole turn-around story |
| `transverseSpread` | Gaussian width across the direction of travel. Costs transmission |
| `longitudinalSpread` | Gaussian width along it. Costs arrival time directly — two ions a millimetre apart are a millimetre of flight path apart |
| `energyFractionSpread` | Gaussian width of the acceleration energy. Supply ripple, not temperature |

Every spread defaults to zero, so a model that says nothing about a cloud launches
exactly what it launched before. That is not only backward compatibility: a spread
appearing by default would change every existing result silently, and a resolving
power quietly getting worse is indistinguishable from a bug.

**`ions` and `population` are not the same thing.** One is how hard the source
distribution is sampled; the other is how many ions are actually there at once.
Ten thousand samples of a single-ion experiment is a better statistic. Ten thousand
ions in a bunch is a different experiment, because they push each other apart.

The default is the conservative reading — the ions simulated are the ions
present — so a dense packet is never silently treated as sparse. Set
`"population": 1` when sampling an intrinsic source property one ion at a time.

**There is no angular-divergence setting, on purpose.** A thermal cloud already
has one — an ion with sideways thermal velocity is an ion launched at an angle —
and offering both would let a document say two things about the same physics and
be believed twice. Energy spread *is* separate, because supply ripple varies the
energy without varying the direction, which a temperature cannot express.

**A packet may start at rest.** `accelerationPotential` of zero used to be a
validation error - "or the ion never moves" - which is true of a beam and false of
a pulsed extraction trap, where the packet sits still until the instrument
switches a field on. Zero is accepted when the model declares a field that could
accelerate the ion, and still refused when nothing could.

For a packet at rest the `direction` is doing more work than usual. A moving ion
says which way is downstream by moving; a stationary one does not, so the declared
direction is what orients `longitudinalSpread` against `transverseSpread` - and
those are not interchangeable. Spread along the extraction converts to an energy
spread and then to arrival time; spread across it does not.

A declared cloud is what makes the emittance figures available. `transverseSpread`
and `temperature` are the two widths whose product the emittance is, so a cloud
with one and not the other has an emittance of exactly zero — correctly, since
every ion is then parallel — and a cloud with neither has no packet to measure.

## Driving a geometry

```json
"solve": {
  "drive": {
    "frequency": { "value": 1, "unit": "MHz" },
    "waveform": "sinusoid",
    "dutyCycle": 0.5
  },
  "electrodes": [
    { "name": "rodXPlus", ...,
      "potential":      { "value": 0, "unit": "V" },
      "driveAmplitude": { "value": 500, "unit": "V" },
      "drivePhase": 0 },
    { "name": "rodYPlus", ...,
      "potential":      { "value": 0, "unit": "V" },
      "driveAmplitude": { "value": -500, "unit": "V" } }
  ]
}
```

**One generator per solve.** A real instrument has a supply and electrodes tapped
off it at various amplitudes and phases; modelling it the other way round would let
a document declare two frequencies on one structure, which is a different
instrument and almost always a mistake.

| | |
| --- | --- |
| `frequency` | On the solve. The one thing every electrode shares |
| `waveform` | `sinusoid` (Mathieu) or `rectangular` (Meissner). Their stability boundaries are in different places: the square-wave cut-off is q = 0.712 against a sinusoid's 0.908 |
| `dutyCycle` | Rectangular only. Away from one half the wave carries a mean of 2d - 1, which enters the equation of motion exactly where a DC offset would - the trick of a digital mass filter |
| `driveAmplitude` | Per electrode, zero to peak, **signed**. A negative amplitude is the same as a half-cycle of phase |
| `drivePhase` | Per electrode, as a fraction of a cycle. Zero when omitted; a half is antiphase; a ramp along a structure is a travelling wave |
| `potential` | Still the DC part, and still what an undriven electrode holds |

An amplitude with no `drive` block is **refused**, naming the electrode. A document
that thinks it declared RF and did not is the expensive kind of silence.

Nothing is re-solved as the drive swings: electrodes sharing a time dependence
share one basis solve, and a quadrupole's two pairs are exact negatives, so four
rods reduce to one. See [Numerics](numerics.md).

### Drive phase is an expression

```json
"drivePhase": { "expression": "-waveDirection * ring / ringsPerWave", "unit": "1" }
```

A fraction of a cycle, dimensionless, and — like every other placement — an
expression rather than a number. It was a plain number until a travelling-wave
guide needed one, which is exactly the case the field existed for: a phase that
cannot depend on the repeat index cannot ramp along a stack.

Two things about it are worth knowing before writing one.

**The drive is evaluated as a phase lead**, `w(f·t + φ)`, so a phase that
*increases* along the axis sends the crest *upstream*. That is a convention, not a
physical fact; the travelling-wave template names a `waveDirection` parameter and
negates it in the ramp rather than leaving a reader to infer it from a minus sign.

**A sinusoidal drive costs two solves however many phases you use.** A cos(2π(ft +
φ)) is a fixed pair of quadrature components with constant coefficients, so the
decomposition resolves every phase into the same two supplies — 96 rings at 96
phases is two basis solves. A rectangular drive cannot be decomposed that way, and
there each distinct phase is its own supply and the solve count says so.

## Three dimensions

```json
{
  "type": "solved3d",
  "solve3d": {
    "minX": ..., "minY": ..., "minZ": ...,
    "maxX": ..., "maxY": ..., "maxZ": ...,
    "cellSize": { "value": 0.8, "unit": "mm" },
    "drive": { "frequency": { "value": 1, "unit": "MHz" } },
    "electrodes": [
      { "name": "rod", "shape": "cylinder", "axis": "z",
        "centreX": ..., "centreY": ..., "radius": ...,
        "lower": ..., "upper": ...,
        "potential": ..., "driveAmplitude": ... }
    ]
  }
}
```

Three primitives: `box` (a plate, a segment wall, a housing), `cylinder` (a rod, a
tube, a ring - along `x`, `y` or `z`), and `sphere` (a bead, a rounded end).
Everything else is as it is for `solved2d`: repeats, drives, stages, sub-cell
boundaries, solid electrodes.

**Three dimensions cost the cube of the resolution.** A cross-section at sixteen
cells across a bore is 128 by 128 nodes; the same in three dimensions is two
million. So a 3D model is meshed far more coarsely than a plane one, and field
quality is the thing to check first rather than to assume - see
[Numerics](numerics.md) for what the solver does and does not manage.

Use it when the geometry genuinely varies along all three axes. A device that is a
cross-section extruded, or a half-plane rotated, is enormously cheaper and more
accurate as `solved2d` with the matching symmetry.

### A prism: the polygon, given a length

The volume vocabulary was box, sphere and cylinder, and it could not extrude a slotted
hyperbolic rod into the three axial sections a linear ion trap is cut into. A `prism` is a
`polygon` outline swept along one axis between `lower` and `upper`:

```json
{
  "name": "centreXPlusUpper", "shape": "prism", "axis": "z",
  "lower": { "expression": "-centreHalfLength", "unit": "mm" },
  "upper": { "expression": "centreHalfLength", "unit": "mm" },
  "vertices": [ ...the same runs and corners a polygon takes... ],
  "potential": { "expression": "dcOffset", "unit": "V" },
  "taps": [ { "drive": "rf", "amplitude": { "expression": "-rfAmplitude", "unit": "V" } } ]
}
```

The vertices are in the cross-section plane, the two world axes other than `axis` in world
order: (y, z) for a prism along x, (x, z) along y, (x, y) along z. Its signed distance is the
outline's own combined with the slab's, exactly as a box combines its three, so its faces are
cut cells; its first crossing walks the outline's crossings and takes the first interval
that is inside both the outline and the slab, so a re-entrant outline finds a link's entry
through its notch. A square prism reproduces a box to 3e-18 m in distance and to the bit in a
solve. A prism may not be tilted: write the tilt into the outline.

### A revolve: the polygon, swept round an arc

`prism` is to a line what `revolve` is to an arc. The same outline, turned about an axis
through `fromHalfTurns` to `toHalfTurns` instead of extruded between `lower` and `upper`:

```json
{
  "name": "rodInnerUpper", "shape": "revolve", "axis": "z",
  "fromHalfTurns": { "expression": "0", "unit": "1" },
  "toHalfTurns": { "expression": "arcHalfTurns", "unit": "1" },
  "vertices": [ ...the same runs and corners a polygon takes... ],
  "potential": { "expression": "0", "unit": "V" },
  "driveAmplitude": { "expression": "rfAmplitude", "unit": "V" }
}
```

**The vertex coordinates are not the two world axes a prism's are.** `x` is the distance
from the axis of revolution and `y` is the position along it — the half-plane an
axisymmetric solve already works in, so a rod profile written for `solved2d` transfers
unchanged. A vertex at negative radius is refused rather than reflected: it means the
author is thinking in world coordinates, and the solid it would produce is not the one
they drew.

Half turns for the same reason everything angular here uses them — `double.CosPi` and
`double.SinPi` are exact at quarter turns, where `Math.Cos(Math.PI / 2)` is 6.1e-17 and a
cap at a right angle lands a hair off the axis. `2.0` is a full revolution and is the only
value for which the ends close; the sweep must be positive and no more than that.
`lower`/`upper` are refused, since the sweep is what bounds it, and a `tilt` is refused for
the same reason a prism's is: write it into the outline.

**The signed distance reduces to the outline's own inside the sweep.** Resolve the query
into (radius, along) and the distance to the solid of revolution *is* `PolygonDistance` —
exactly, not approximately, because a revolution takes every point to the profile plane
without distortion. Outside the sweep, project onto the nearer cap half-plane and combine
the in-plane and out-of-plane parts in quadrature. Checked against the closed form for a
torus at 32, 64 and 128 profile vertices: **14.446, 3.614 and 0.904 µm**, which is the
inscribed-polygon bound `r(1 − cos(π/n))` to the digit and falls exactly 4.00×, so what
is left is the sampling of the circle rather than the operator.

**The first crossing is closed form, because every revolved edge is a quadric.** An edge
whose radius varies linearly with the axial coordinate sweeps a cone; one parallel to the
axis a cylinder; one perpendicular an annular disc. So a link's crossings are roots of a
quadratic — or, for the disc, a single linear crossing — and the intervals between them are
decided by `Contains` at their midpoints. No bisection anywhere, which is what a cut cell
needs.

**One trap, and it cost an eleven percent field asymmetry.** A face meant to be flat is
flat only to rounding, because an outline is written as expressions: the C-trap's inner rod
has its last hyperbola vertex at 3.0000000000000009 mm meeting a corner written
3.0000000000000001, an edge sloping by 8.7e-19 m over three millimeters of radius. Tested
against zero that is not a disc but a cone of slope 2e15, whose quadratic is so
ill-conditioned that the root is lost or badly displaced. The same rod's other face *was*
exactly flat, because `-rodHalfWidth` and the run's first vertex round the same way — so one
side of a mirror-symmetric electrode got its cut cells and the other did not, the conductor
mask was symmetric either way, and the solved field came out eleven percent asymmetric
across a plane the geometry is symmetric about. The flatness test is relative to what the
coordinates carry, and `AFaceFlatOnlyToRoundingIsStillCutWhereItIs` guards it.

Far from its axis a revolve is a prism, which is the limit the two primitives share and the
one a reader can check: the same outline at a kilometer of radius, swept over the arc length
it covers there, agrees with the extruded version to **under 100 nm** over 500 probes — the
sag a kilometer radius has over the sampled span.

Schema **0.14** carries `revolve`.

## Operating an instrument through a sequence

The sequencer the architecture calls a timed state machine. A trap fills,
isolates, then extracts, and what the electrodes hold differs in each:

```json
"sequence": [
  { "name": "fill",    "duration": { "value": 200, "unit": "us" } },
  { "name": "isolate", "duration": { "value": 50,  "unit": "us" },
    "set": { "rfAmplitude": { "value": 300, "unit": "V" } } },
  { "name": "extract", "duration": { "value": 10,  "unit": "us" },
    "set": { "rfAmplitude":   { "value": 0, "unit": "V" },
             "pushPotential": { "value": 1000, "unit": "V" } } }
]
```

**The timeline belongs to the instrument**, which is §9's own wording: "an
instrument is a timed state machine: ordered phases with durations, excitation
overrides, transport mode, and transition conditions". It sits on the model, not
inside one field element, because a phase holds across the whole thing.

### A phase sets parameters, not electrode settings

That is the whole design. Electrode potentials are already expressions over
parameters, so setting one moves everything that depends on it at once —
including *derived* parameters. Listing electrode settings instead would let a
phase change an amplitude while leaving the quantity it was derived from behind,
and the two would disagree silently.

It also costs no new vocabulary: the same override mechanism a sweep or an
optimiser uses to *perturb* a design is what a sequence uses to *operate* one.

Anything a phase does not name keeps the value it has outside the sequence.

### A phase may ramp

```json
"sequence": [
  { "name": "cool", "duration": { "value": 300, "unit": "us" },
    "set":  { "rfAmplitude": { "value": 500, "unit": "V" } } },
  { "name": "scan", "duration": { "value": 5.9, "unit": "ms" },
    "ramp": { "rfAmplitude": { "value": 532, "unit": "V" } } }
]
```

`ramp` names where each parameter **ends**, and it gets there linearly in time from
wherever it stood at the phase's start - the value the phase `set`, or the value in force
before it. A mass-selective instability scan is exactly this: the RF amplitude rises
steadily and every ion leaves when its own q reaches the ejection point. Written as
phases that each hold a value, the scan is a staircase of thousands of steps; the
linear-ion-trap studies were run that way before the ramp existed, at 4 µs a step, and a
step of 4 µs is a fifth of a peak width at 200 kDa/s. Written with a ramp it is one phase.

**Exact where the potentials are linear in the ramped parameter, and checked.** A
solved geometry's field is a sum of solved patterns with time-varying weights, and the
weights are linear in the electrode excitations; a ramp interpolates the weights between
the phase's start and its end, so it is exact whenever every potential and drive
amplitude is linear in the parameter. Nothing stops a potential being written as the
square root of one, so the validator compiles the electrodes at the phase's midpoint too
and refuses a ramp whose midpoint is not the mean of its ends, naming the electrode and
the two numbers. A ramp that moves a drive *phase* is refused as well - that is a
frequency shift, not an amplitude - and so is one that changes the geometry, as any phase
that moves metal is.

**Where it does not reach.** A ramp is supported on solved geometries in both dimensions -
a volume solve weighs its channels at the phase's two ends exactly as a cross-section does,
and its end state is one of the states gathered for the channel decomposition, so a ramp
from zero amplitude has its pattern solved. An analytic element that a ramped parameter reaches is refused rather than left frozen at its
start value while the solved elements move.

A **diffusive phase may ramp too**: the density solver re-samples the field and rebuilds its
face operator every step while the field is changing, and keeps the assemble-once path where
it is not, so a held phase is bit-identical to what it was. This is what makes a mobility
analyser's elution scan expressible - the axial gradient walked down while the density is
held - and it used to be refused, because the solver stepped through a field it held fixed
within a phase. In each remaining case the refusal says to write the curve as phases that
hold.

Two things had to be true underneath for that to work, and neither was. A geometry with
stages and no drive was given a placeholder clock at one hertz, so its "shortest period" was
one second rather than infinite, and the pseudopotential wrapper - gated on a finite period,
because a DC ramp has no cycle to average over - averaged the ramp over a one-second cycle
that is mostly zero field. And a diffusive model whose sequence never left diffusion was
routed to the plain diffusive path, which steps a snapshot of the field, so the ramp ran
ignored. Both produced a clean answer; both are fixed, and each has a test that fails with
the defect restored.

### Every element follows it, and how depends on what it is

A **solved** geometry follows a phase by re-weighting the channels it has already
solved — the geometry is untouched, so nothing is re-solved. An **analytic**
element has no channels to re-weight, so it is compiled once per phase and
switched. An element whose expressions do not depend on any parameter a phase sets
stays static, which is a distinction rather than an optimisation: wrapping it
would hand the integrator switch instants to land on for a field that is the same
on both sides of them.

It was not always so, twice over, and both are in `docs/lessons.md`. Stages used
to be compiled per element, so two electrodes in different elements written as the
*same expression* over the same parameter came out at 900 V and 300 V during a
phase. And the first fix reached only the solved branch, so an analytic element
stayed frozen at baseline while the solved ones moved.

### The older spelling, and the refusals

`stages` on a solve still works and means the same thing — the shipped
`sequenced-extraction` example is written in it, and a single-element model has no
ambiguity to resolve. Two refusals cover the ways a document can say two things at
once: **two elements each declaring stages** is two timelines over one instrument,
and **declaring both `sequence` and `stages`** is refused rather than merged, the
same argument that refuses a geometry declaring both `drive` and `drives`. An
explicitly empty `"sequence": []` is refused too, since an empty timeline reads
exactly like no timeline.

### Two rules enforced rather than documented

**A phase may change what an electrode holds, not where it is.** Moving metal
between phases would change the mask, so each phase would need its own solve and
its own grid — and the field would still be computed, and it would be wrong in a
way nothing else catches. Refused, naming the electrode and the phase.

**The last phase holds after the sequence ends.** An instrument left alone stays
where it was put. A field that switched off at the end of the declared sequence
would make every ion still in flight suddenly coast, which is a physics change
disguised as a bookkeeping one.

A sequence needs no drive: a pulsed extraction is DC that switches, and a model
with a sequence and no `drive` block is exactly that. `sequenced-uniform` in the
examples corpus is the smallest one — an ion at rest in nothing until a phase
switches a uniform field on, arriving at `hold + sqrt(2 d m / (q E))`.

Schema **0.6** carries `sequence`.

## Repeating an electrode

A stack of rings is one ring, written once:

```json
{
  "name": "ring",
  "shape": "rectangle",
  "repeat": { "count": { "expression": "ringCount" }, "index": "ring" },
  "minX": { "expression": "ring * ringPitch", "unit": "mm" },
  "minY": { "expression": "entranceRadius - ring * taper", "unit": "mm" },
  "potential":      { "expression": "-dcGradient * ring / (ringCount - 1)", "unit": "V" },
  "driveAmplitude": { "expression": "rfAmplitude * (1 - 2 * mod(ring, 2))", "unit": "V" }
}
```

The index runs from zero and is bound as an ordinary parameter, so every expression
on the electrode sees it. The copies are named by position - `ring-0`, `ring-17` -
so an error, a loss itemisation or a channel report says *which* one.

This is the discrete periodicity SYM-1 lists beside cylindrical symmetry and mirror
planes, and it is what keeps a two-hundred-ring stack a **parametric** document
rather than a generated one: the placements are still expressions, so "move every
ring 50 microns and re-solve" is still sayable, which is what the whole tolerance
apparatus rests on.

An index name that collides with a declared parameter is refused rather than
shadowing it.

### Two more functions

`floor(x)` and `mod(a, b)` join `abs`, `sqrt`, `min` and `max`. Indexed geometry
needs them: the alternating sign of a two-phase stack is `1 - 2 * mod(index, 2)`.

Both are dimensionless-only, for the reason `sqrt` is - the floor of a length
depends on which unit you take it in, and that is precisely the ambiguity the
evaluator exists to refuse. `mod` is Euclidean rather than truncated, so
`mod(-1, 2)` is 1: an index counted backwards still alternates the way it should.

`cosPi(x)` and `sinPi(x)` join them, and take **half turns rather than radians**.
A multipole needs them: 2n rods at pi/n intervals is
`rodCentre * cosPi(2 * pole / poleCount)`, and without trigonometry that geometry
cannot be written at all.

Half turns because `Math.Cos(Math.PI / 2)` is 6.1e-17 rather than zero, so a rod
placed at a quarter turn would land a hair off axis and the multipole would carry a
spurious dipole made of rounding. `cosPi(0.5)` is exactly zero. This is the same
convention, for the same reason, that the drive decomposition already uses to keep
an antiphase electrode from picking up a quadrature component of pure round-off.

## A polygon electrode

Rectangle and disc build every device the templates shipped for two years, and cannot
say one thing: a rod whose face is a hyperbola with a slot cut through it, which is what
a radial-ejection linear ion trap is. A `polygon` is the general cross-section - any
closed outline, convex or not, at one potential:

```json
{
  "name": "rodXPlusUpper", "shape": "polygon",
  "vertices": [
    { "count": { "value": 25, "unit": "1" }, "index": "k",
      "x": { "expression": "xStretch + inscribedRadius * sqrt(1 + (s / inscribedRadius) * (s / inscribedRadius))", "unit": "mm" },
      "y": { "expression": "slotXPlus + (rodHalfWidth - slotXPlus) * k / 24", "unit": "mm" } },
    { "x": { "expression": "xStretch + rodDepth", "unit": "mm" }, "y": { "expression": "rodHalfWidth", "unit": "mm" } },
    { "x": { "expression": "xStretch + rodDepth", "unit": "mm" }, "y": { "expression": "slotXPlus", "unit": "mm" } }
  ],
  "potential": { "expression": "dcOffset", "unit": "V" },
  "taps": [ { "drive": "rf", "amplitude": { "expression": "-rfAmplitude", "unit": "V" } } ]
}
```

(In the shipped template `s` is written out in full; it is abbreviated here.) The outline
closes itself from the last vertex back to the first, the winding direction does not
matter, and the inside is decided by the even-odd rule.

**A curved face is a run.** A vertex entry with a `count` stands for that many vertices:
the same pair of expressions evaluated with `index` bound from zero to `count - 1`. That
is `repeat`'s idea applied inside one electrode, and it is what keeps the document
parametric - the hyperbola above is twenty-five points written once, and every one of
them moves when `inscribedRadius` or `slotXPlus` does. Without it the linear-ion-trap
template was 116 KB of generated expressions nobody could read; with it, 24 KB anyone
can. The chord error is the sagitta of one segment, about two microns on a 4 mm rod
sampled every quarter millimetre, two orders below the cell it sits in.

**Exact where it matters.** The signed distance is closed form (nearest edge, signed by
even-odd) and so is the first crossing of a grid link, so a polygon face is a cut cell
like a disc's - a slot a quarter of a millimetre wide is located below one cell rather
than rasterised onto whichever row of nodes it falls on. A square written as four
vertices solves to the rectangle's field to 1e-13 of the applied potential (the two
compute a cut fraction by different arithmetic, and round differently in the last bit),
and a rod written as two halves meeting on a line is the whole rod to the bit - which is
how a slotted rod is written, so that a slot of zero height is simply a rod.

**Refused rather than solved:** fewer than three distinct vertices, zero enclosed area
(the polygon has collapsed to a line and would vanish from the solve, as an inverted
rectangle does), and an outline that crosses itself (a bow-tie has two insides by one rule
and one by another). Consecutive vertices in the same place are **merged**, not refused: a
parametric outline produces them whenever a feature collapses - a slot of zero height puts
its channel's two corners on one point - exactly as a zero-extent rectangle is a legitimate
thin plate. Two polygons that
share an edge are tangent, not overlapping; two that share interior at different
excitations are refused as any other pair of conductors is.

**Two things it does not do.** A polygon is a cross-section, so it lives in `solved2d`;
the volume solver's primitives are box, sphere and cylinder, and an extruded polygon in
three dimensions is not yet expressible. And a polygon is a polyline, not a curve: a
conductor that is an equipotential of a transcendental field can be approximated to any
tolerance, but the document is then sampling a curve it cannot name, and the tolerance is
the author's to state.

Schema **0.9** carries `polygon`.

## What a solve is a cross-section of

```json
"solve": {
  "symmetry": "cylindrical",
  "minY": { "value": 0, "unit": "mm" },
  ...
}
```

`translational` (the default) extrudes the plane along the third axis: a
cross-section, which is what a mass filter or a rectilinear trap is.
`cylindrical` rotates it about the x axis, with **y as the radius**, so the domain
must lie at y >= 0 and a negative `minY` is refused.

This is not presentation. It changes the operator - in cylindrical coordinates the
radial part of the Laplacian gains a term from the shrinking circumference of a
ring near the axis - and a field solved with the wrong one converges perfectly well
to the wrong answer. See [Numerics](numerics.md) for the discretisation and what it
is checked against.

Everything else behaves as before, and that is the point of SYM-1: an electrode is
declared the same way, and a rectangle that was a bar becomes a ring. The ion is
launched and tracked in ordinary three-dimensional space; nothing above the field
knows the solve happened in a half-plane.

The axis is a symmetry plane whatever `bottomEdge` says, and is forced to be one.
Requiring the author to declare it would be an opportunity to get it wrong, and
there is no second thing it could be.

## Placements are expressions, including vectors

Spec section 9: every placement is a parametric expression, never a baked number.
Scalars take `expression` in place of `value`; vectors take three of them, one per
component, because the components are independent.

```json
"detector": {
  "planePoint": { "expression": ["plateOuter + driftLength", "0", "0"], "unit": "mm" },
  "normal": { "value": [-1, 0, 0] }
}
```

When expressions are present the `unit` is not consulted - each expression carries
its own dimension, exactly as a scalar expression does.

**A dimensionless zero satisfies any dimension.** The grammar has no unit literals,
so a bare `0` is dimensionless and there would otherwise be no way to write "on
axis" for the two components that are. This is the one safe exception: zero is the
only value whose unit conversion is the identity, and the ambiguity that makes
units mandatory here - is 4000 volts or kilovolts - does not exist at zero. A
dimensionless *one* is still refused, with the offending component named:

```
UNITS_INCOMPATIBLE
  at         /detector/planePoint/expression/1
  constraint this field requires a vector of dimension m
  observed   1 1
  try        the expression '1' produces dimension 1
```

## Fields

A list, superposed. Superposition is exact for electrostatics.

```json
{ "type": "fieldFree" }

{ "type": "uniform",
  "field": { "value": [0, -80000, 0], "unit": "V/m" } }

{ "type": "halfSpaceUniform",
  "planePoint":   { "value": [0, 0, 0], "unit": "mm" },
  "inwardNormal": { "value": [1, 0, 0] },
  "capPotential": { "value": 4, "unit": "kV" },
  "turningDepth": { "value": 50, "unit": "mm" } }
```

`halfSpaceUniform` is field-free on one side of a plane and uniformly retarding on
the other — the primitive an ideal single-stage ion mirror is built from. It is
named for what it is rather than what it builds, because no device class may
appear below the template library.

```json
{ "type": "idealQuadrupoleRf",
  "directPotential":  { "value": 0,   "unit": "V" },
  "driveAmplitude":   { "value": 200, "unit": "V" },
  "driveFrequency":   { "value": 1,   "unit": "MHz" },
  "inscribedRadius":  { "value": 4,   "unit": "mm" },
  "axis": "x" }
```

`idealQuadrupoleRf` is **the only analytic driven field**, and it exists for the
reason the analytic tier exists at all: something exact to check a solved geometry
against. Every other driven field here is solved, so an expectation written from
the ideal formula against solved rods would be asserting a few per cent of
modelling difference as though it were arithmetic — which is what blocked the
driven diffusive corpus examples until this existed.

The x pair takes `directPotential` and `driveAmplitude`; the y pair takes their
negatives, which is what makes the field a quadrupole rather than a quadrupole plus
an offset. **A zero frequency is refused** — that is a static field wearing a
drive's clothes, and it would run quietly and give a quadrupole with no RF. A zero
*amplitude* is allowed and is the honest way to say the generator is off.

Note the field amplitude convention when writing closed forms against it:
`E0(r) = 2 V r / r0^2`, because the potential is `V(x^2 - y^2)/r0^2` and its
gradient carries the factor of two. Dropping it makes a pseudopotential exactly
four times too small.

`axis` (schema 0.11) names the axis the field is invariant along — `x`, `y` or `z`,
with `z` when absent so every earlier document compiles to the same field. The
transverse pair follows the cyclic order (`z`: x-y, `x`: y-z, `y`: z-x) and the
potential is `V(u^2 - v^2)/r0^2` in that pair, as a permutation rather than a
rotation so a point on the axis is at zero field to the bit. It exists because every
axisymmetric tunnel here is solved in a half-plane whose axis of rotation is x, and
the confining RF of a trapped-ion-mobility analyser — a quadrupole alternating
between the four segments of each ring — has to lie *across* that axis. The
quadrupole is not axisymmetric, but its pseudopotential is (the field magnitude
depends on radius alone), which is what lets it enter the r-z density solve. An
`axis` on any other element kind is refused rather than ignored.

### Solved fields

```json
{ "type": "solved2d",
  "solve": {
    "minX": { "expression": "-mirrorDepth", "unit": "mm" },
    "minY": { "expression": "-halfGap",     "unit": "mm" },
    "maxX": { "expression": "midPlane",     "unit": "mm" },
    "maxY": { "expression": "halfGap",      "unit": "mm" },
    "cellSize": { "expression": "boardGap / 32", "unit": "mm" },
    "rightEdge": "neumann",
    "boundaryIsDiscontinuous": false,
    "reflectAboutX": { "expression": "midPlane", "unit": "mm" },
    "electrodes": [ ]
  } }
```

| Key | Meaning |
| --- | --- |
| `minX`…`maxY` | The solve domain |
| `cellSize` | Requested node spacing. Each axis rounds its interval count **up** to a power of two independently, so the spacing is never coarser than asked in either direction and the grid spans exactly the declared box. Cells need not be square; the worst ratio is two to one |
| `leftEdge`, `rightEdge`, `bottomEdge`, `topEdge` | `dirichlet` (default) or `neumann`, a symmetry plane |
| `boundaryIsDiscontinuous` | Whether the field genuinely jumps at the domain edge. **Set false when the domain was drawn wide enough that the field has decayed** — see below |
| `reflectAboutX` | Reflect the solved field through a plane and superpose, for a symmetric pair |
| `tolerance` | Relative residual for the solve, default 1e-12 |

`boundaryIsDiscontinuous` is worth understanding before setting it. Declaring a
jump that is not there is worse than declaring none: two such phantom surfaces a
few microns apart — which is what two abutting solve domains produce — defeat the
superposition's sign tracking, so a step crossing both is treated as crossing
neither. That cost an ion 2.6e-4 of its energy, four orders above the ACC-4
budget, and presented as an intermittent transmission loss rather than a fault.

### Electrodes

Three primitives, chosen for coverage rather than convenience.

```json
{ "name": "plate", "shape": "rectangle",
  "minX": …, "minY": …, "maxX": …, "maxY": …,
  "potential": { "value": 0, "unit": "V" } }

{ "name": "rodXPlus", "shape": "disc",
  "centreX": …, "centreY": …, "radius": …,
  "potential": { "expression": "rodPotential", "unit": "V" } }

{ "name": "topBoard", "shape": "edgeProfile", "edge": "top",
  "profile": [
    { "at": …, "potential": … },
    { "at": …, "potential": … }
  ] }
```

A **rectangle** gives plates, apertures, and any stripe of a printed board. A
**disc** is a rod in cross-section, which is what a quadrupole, hexapole, or
octopole is. An **edge profile** is a domain edge whose potential varies along it
by piecewise-linear interpolation, which is how a printed-circuit mirror applies
its ramp — one electrode spanning many nodes, because that is how it is driven:
one supply feeding a resistive divider.

The test of whether these are the right primitives is LIB-1's: if a new device
needs a change below the template library, either it is genuinely novel physics
or the abstraction is wrong, and almost always the second. A mirror and a
quadrupole differ only in which primitives they use and where they put them.

**A name is an electrode's identity within its solve.** It is optional, and an
unnamed electrode is called `electrode-N` after its place in the list, so
`electrode-3` is the one an error locates at `/electrodes/3`; a repeated one's
copies are `name-0`, `name-1` and so on. Two electrodes in one solve may not share
a name, and the second is refused at its `name`. That is not tidiness: a driven or
sequenced geometry is solved through channels built from the names, so two sharing
one were solved holding the same potential, and an ion lost on either is reported
as striking one surface. Every unnamed electrode used to be called `electrode`, and
two of them in a driven solve were one entry holding the later's potential - a
deflector of two unnamed plates was switched off. Two *elements* may share names,
and the Astral's do: its two mirror solves each declare every board, because it is
the same metal at one mirror's potentials or the other's. The cost of allowing it is
that unnamed electrodes in two elements share default names too - each element has
an `electrode-0` - and a loss on either is tallied against one surface, so name the
electrodes of a model with more than one solved element.

## Transport

```json
"transport": {
  "mode": "trajectory",
  "relativeTolerance": 1e-11,
  "maximumFlightTime": { "value": 1, "unit": "ms" },
  "sampleInterval":    { "value": 20, "unit": "ns" }
}
```

`maximumFlightTime` is **required**, as a runaway guard. `sampleInterval` sets the
cadence of the trajectory stream written for rendering and export, which is
independent of integration steps — though only in one direction: the stream can
be coarser than the steps, never finer, because there is no dense output to
interpolate within a step.

`statisticalDiffusion` is a declared peer mode that this build does not implement.
Asking for it is a regime violation with a distinct exit code, not a silent
substitution.

### How the density is stepped

```json
"transport": {
  "mode": "diffusion",
  "densityStep": { "scheme": "implicit", "gain": 64 }
}
```

`explicit`, the default, is forward Euler, bounded by the faster of two limits:
diffusion, and the Courant condition on how fast the drift crosses a cell. `implicit`
is backward Euler, which has **no stability limit** and charges Gauss-Seidel sweeps
instead. `gain` is how many times the explicit stability limit to step, and is refused
against the explicit scheme rather than ignored — that scheme cannot take a longer step,
and honouring half a block would leave an author concluding the solver is slow rather
than that the request went nowhere.

**Which to use is a property of the model, which is why it is in the document.** The
Gauss-Seidel iteration's difficulty is set by the *diffusive* part of the operator, so a
step long by Courant's standard but still short by diffusion's converges in about three
sweeps — while a problem already at its diffusion limit needs tens and comes out slower
than stepping explicitly.

The driven case is what this is for. A ponderomotive well's gradient is steepest at an
electrode edge, which is exactly where the density is almost zero, so the explicit step
is set by a region where nothing is happening: on the shipped ion funnel at 2 mbar,
195 ps against a diffusion limit of 747 ns.

| gain | steps | sweeps/step | speedup | error |
| --- | --- | --- | --- | --- |
| 4 | 6,404 | 3.0 | 1.4× | 0.008% |
| 16 | 1,601 | 3.0 | 4.7× | 0.028% |
| 64 | 401 | 3.0 | **10.8×** | **0.108%** |
| 256 | 101 | 4.0 | 17.7× | 0.427% |
| 1024 | 26 | 4.9 | 21.4× | 1.673% |

**Backward Euler is first order, so the error is linear in the gain** — which is what
the table shows, and there is no default above one because what gain is acceptable is an
accuracy question and nothing here measures the accuracy of a step it has not taken. A
run reports the step it took, the sweeps it took per step, and the fraction of the
explicit work it did, whether or not any of those crosses a threshold; and
`diffusion.implicit-not-paying` says so when the sweeps cost more than the step saved.

**Positivity survives a partial solve**, which is what makes the scheme usable at all:
every term in the Gauss-Seidel update is non-negative, so the iterate is a valid density
however far from converged it is. What an unconverged solve costs is *conservation*, and
`diffusion.implicit-unconverged` reports the residual the ledger is closed to.

### Space charge

```json
"transport": {
  "spaceCharge": "direct"
}
```

`none`, the default, flies each ion through a field that does not know the others
exist. That is exactly right for a sparse beam and wrong for a dense packet, and
a run says which it is either way — the screening estimate is reported whether or
not it crosses a threshold.

`direct` sums every pair. It is the reference method SC-1 names, and it is a
string rather than a flag because there is a third value and a boolean would have
had to be replaced rather than extended.

`pic` deposits the packet's charge onto its own grid, solves Poisson once, and
gathers the field back. It costs one solve plus O(N) rather than O(N²) — but the
solve is paid for whatever the cloud, so **the crossing is near 850 trajectories**
and below that the reference method is simply faster.

```json
"transport": {
  "spaceCharge": "pic",
  "spaceChargeGrid": { "nodes": 32, "padding": 4.0, "refreshTolerance": 0.05 }
}
```

Every field is optional and every default is shown. A `spaceChargeGrid` declared
against any other method is **refused rather than ignored**, the same rule an
unrecognised property follows: a document that configures a solve it is not
running has been misunderstood by its author.

**`nodes` has an optimum rather than a floor**, and it is the one thing to know
before turning it. The grid smooths the mutual force at the scale of one cell, so
too coarse under-pushes and too fine stops representing a density at all —
measured against the direct sum taken to its own point limit at **−15.1%, −4.2%,
+0.08% and +4.4%** for cells of 3.68, 1.84, 0.92 and 0.46 mean macroparticle
spacings. Raising it past the match buys a worse answer *and* a cubic cost, so
`spacecharge.grid-resolution` reports the ratio on every run whether or not it
crosses a threshold, and names the node count that would match.

`padding` sets the box half-width in packet RMS radii. A packet in flight is in
free space and this puts it in an earthed box; centring the box on the packet is
what makes that cheap, since a centred distribution induces almost no field at its
own centre.

`refreshTolerance` is the fractional change in RMS radius that forces a new solve.
The grid travels with the packet, so uniform translation is **exact** (1e-11 across
250 mm) and free — which is why the criterion is written on shape rather than on a
step count, shape being the only thing that ages. Tightening it converges:
**+12.68%, +6.16%, +1.01%, −0.54%** at 0.30, 0.15, 0.05 and 0.02, always wide at
the coarse end because a field held across a refresh is the field of a *denser*
packet than the one being pushed.

**The weighting is the cloud's own two fields.** `ions` is how many trajectories
are computed; `population` is how many ions are physically present. Each computed
trajectory therefore stands in for `population / ions` real ions and carries their
charge *and* their mass together — so charge-to-mass is unchanged, motion in the
applied field is bit-identical to the unweighted case, and only the pairwise sum
notices. Lowering `ions` while keeping `population` is how a dense packet becomes
affordable, and it is a declared approximation rather than a hidden one.

**The direct sum's cost is quadratic in `ions`**, and `einzel estimate` says so in
words as well as in a number, because the linear intuition is exactly wrong: 150
trajectories through the shipped trap took 87 seconds and 2,000 would take about
four hours. **The grid's is linear in `ions` and cubic in `nodes`** — 200
macroparticles take 0.99 s at 16 nodes and 124 s at 128 — so the estimate costs
both in the same currency and states their ratio *at this cloud* rather than the
asymptotics. Quoting the asymptotics alone would recommend the approximation
everywhere, including the majority of clouds where it loses to the method it
approximates. Trajectory integration is otherwise excluded from the estimate — its
cost depends on a path that depends on a field not yet solved — so this is the one
transport cost stated in advance.

Two ways to ask for it and not get it are **refused rather than run**, because
each would produce a result that looks like the one asked for:

| | |
| --- | --- |
| Fewer than two trajectories | nobody to push on |
| A cloud with no spatial spread | an unbounded self-field, not a large one |

A declared gas was a third refusal, because the packet integrator advanced everything in
lockstep and had no collision hook. **It has one now**: each macroparticle carries its own
collision sampler on its own schedule, seeded exactly as the independent-ion path seeds
them, and the shared step is cut to land on whichever collision comes next anywhere in
the packet - the same landing a single trajectory does, so a scattered velocity is applied
where the ion actually was rather than a fraction of a step away. What that costs is
steps in proportion to the packet's total collision rate, and what it means is stated
on every such run as `spacecharge.macroparticle-collisions`: a macroparticle scatters as
one ion and carries the momentum of many, so the drag and diffusion are the ion's and the
collisional fluctuations are those of the macroparticle count rather than the population.
It is the only way a cooled, space-charge-limited cloud can form in a run, which is what
a trap's capacity question needs.

What it gives up, stated rather than discovered: the packet integrator **cannot
land exactly on a declared field discontinuity**, because a shared step cannot land
on a surface each macroparticle reaches at its own instant. It caps the step short
of the first arrival instead. That is why this is the reference method for space
charge rather than a replacement for the path that carries ACC-1.

## Bounding an analytic element, so two instruments can share a document

An analytic field has no extent, **because a formula does not**. That is harmless while
such a field is an idealisation of a whole instrument — a uniform field, a retarding
half-space — and stops being harmless the moment one is an exact statement of a real device
sitting *next to* another. A quadro-logarithmic potential grows as `z^2`, so an orbital
analyser declared beside the trap that injects it puts an enormous field across that trap.

Superposition is exact and the sequencer can express a handover, so nothing else about
composing two devices was ever in doubt. **And the obvious escape does not exist:**
declaring the analyser as solved geometry, so its own domain bounds it, fails because its
electrodes are equipotentials of the field they produce — the profile satisfies
`-r^2/2 + Rm^2 ln(r/Rm) = A - z^2`, transcendental in `r` and invertible only through
Lambert W — and no 2-D shape is a curve a document can name. (A `polygon` can now
approximate one to a stated tolerance, which moves this from impossible to a sampling
choice; it does not make the profile exact.)

So an analytic element may declare a **region**: a box outside which it contributes nothing.

```json
{
  "type": "quadroLogarithmic",
  "curvature": { "value": 20, "unit": "V/mm^2" },
  "characteristicRadius": { "value": 20, "unit": "mm" },
  "region": {
    "minX": { "value": -30, "unit": "mm" }, "maxX": { "value": 30, "unit": "mm" },
    "minY": { "value": -30, "unit": "mm" }, "maxY": { "value": 30, "unit": "mm" },
    "minZ": { "value": -30, "unit": "mm" }, "maxZ": { "value": 30, "unit": "mm" }
  }
}
```

Measured on a two-element document — an orbital analyser at the origin and an ordinary
1 kV/m accelerating section 75 mm downstream:

| in the second device | field along x |
| --- | --- |
| analyser unbounded | **−1,499,000 V/m** |
| analyser bounded | **1,000.0 V/m**, exactly its own |

**And on the axis it is worse than swamping.** Without a region the second device has a
line through it at which the model cannot be asked a question at all, because that line is
the analyser's singular axis and a quadro-logarithmic field refuses a point there rather
than returning a large one.

Inside the region nothing changes — asserted to the bit against the same field built alone,
which is the control that makes the rest mean anything. All six bounds are required: a
half-open region is a legitimate thing to want, but "the axes I left out" is not how anyone
reads a partly-filled box. A **solved** element may not declare one, refused rather than
ignored, because a solve is already bounded by its own domain and a document that says a
thing twice can say it two ways. A region with no extent is refused too.

### The boundary is a step, and the step costs less than it looks like it should

**A box is not an equipotential of anything interesting**, so the potential does not match
across a region boundary. The first version of this write-up concluded from that that an ion
crossing gains or loses the potential it left. **It does not**, and measuring it is what
settled it.

An ion is moved by the **field**, and the field is exactly the declared one on each side. So
a uniform field bounded to a box is an accelerating gap followed by a field-free drift —
which is an ordinary instrument with a closed form:

| | |
| --- | --- |
| `sqrt(2 m L / (q E)) + (D - L) / v` | **13.658582 us** |
| measured, bounded | **13.658582 us** |
| the same model with no region | 10.180506 us, accelerating the whole way |

The control matters as much as the agreement: without the region the field reaches the
detector and the flight is a third shorter, so a region that silently did nothing would not
pass.

**What the step does cost**, stated rather than overstated:

- the **energy-drift diagnostic** jumps at the boundary, because that is computed from the
  potential;
- and the piecewise field is **not conservative across the boundary**, so an ion that
  crosses more than once — in by one face and out by another — can gain or lose energy no
  electrode supplied. A single straight crossing has no such path.

Every bounded element reports the largest potential on its boundary in volts and as a
fraction of what the ion is accelerated through, whether or not it crosses a threshold
(REG-2), at severity `Qualified`: the result is usable, and here is the thing about it worth
knowing.

The field discontinuity itself needs no apology. The boundary is presented as a signed
distance whose zero the integrator brackets and lands on exactly — the same first-class
event a declared discontinuity already is (§11).

**`FieldAssembly.Build`'s contract narrowed when regions arrived**, and the line it draws is
about *who knows the thing* rather than about how bad it is. It used to refuse a field
carrying any warning. An unconverged solve is evidence only the engine has: nothing in the
document says the residual missed, the field looks identical either way, and a bare field
has no envelope to carry it on — so refusing is the only honest option, and this project has
lost numbers at exactly that seam. A region's step is a consequence of geometry the author
wrote down and can see. Refusing every one would make `Build` unusable for the composed
beamlines a region exists to enable, in exchange for repeating what the document already
says.

`bounded-accelerating-gap` is the corpus example (EX-1), so the feature is in the release
gate rather than only in a unit test. Its expectation is the arithmetic above, and it has
teeth: mutating the expected value fails the gate, which is how the coverage was confirmed
rather than assumed.

### The limitation, and the better design it points at

The step is still large for the fields one most wants to bound — a uniform potential never
decays (100 V at 50 mm from its own zero) and a quadro-logarithmic one *grows* (7,000 V at
30 mm) — so "place the boundary where the field has decayed" has nowhere to point for
either. That is tolerable for a beam passing through, and it is not what a real device does.

A real device's field is bounded by a **conductor**, and a conductor is an equipotential —
of the very field it produces. Bounding an analytic element by one of its own level sets
rather than by a box would make the potential continuous *by construction*, offset so it is
zero outside, with the field discontinuous exactly as `halfSpaceUniform` already is and the
geometry exactly a real electrode. That is the next refinement, and it is not what was built
here.

## Versioning

Schema 0.1 through 0.14 all load, and a test reads a document at every version the
build claims. Every bump ships a migration and a test that the prior corpus still
loads. Codes and field names are a compatibility surface that agent workflows bind
to: they are added, never reworded or repurposed.

0.3 adds the source cloud, 0.4 the gas, 0.5 the mutual Coulomb force, 0.6 the
model-level sequence, 0.7 parametric directions, 0.8 a tilt on a cross-section's
extrusion axis, 0.9 the polygon electrode, 0.10 parameter provenance, 0.11 an
`axis` on the analytic RF quadrupole, 0.12 a `fringe` on a bounded element's
region, 0.13 several ion species in one run and 0.14 the revolved
outline. All purely additive, so every earlier
document still reads — but a document whose ions push on each other genuinely is
not a 0.4 document, and saying so is cheaper than an older build reading it,
ignoring the field it does not know, and reporting a different flight with nothing
to indicate that anything was dropped.

## Several generators on one geometry

A `solve` may declare `drives` instead of `drive`, and each electrode names which ones
it taps and with what amplitude and phase:

```json
"drives": [
  { "name": "wave",    "frequency": { "value": 0.5, "unit": "MHz" } },
  { "name": "confine", "frequency": { "value": 3.0, "unit": "MHz" } }
],
"electrodes": [
  {
    "name": "ring",
    "repeat": { "count": { "expression": "ringCount" }, "index": "ring" },
    "potential": { "value": 0, "unit": "V" },
    "taps": [
      { "drive": "wave",
        "amplitude": { "expression": "rfAmplitude", "unit": "V" },
        "phase": { "expression": "-waveDirection * ring / ringsPerWave", "unit": "1" } },
      { "drive": "confine",
        "amplitude": { "expression": "confineAmplitude", "unit": "V" },
        "phase": { "expression": "mod(ring, 2) / 2", "unit": "1" } }
    ]
  }
]
```

`drive` and `driveAmplitude`/`drivePhase` remain the short form for the common case of
one generator, and declaring both spellings is **refused** rather than merged: a
document that says a geometry has one drive and also says it has three is not a
document with a default to fall back on. Every generator needs a `name` once there is
more than one, since a tap resolves by name; duplicates and unknown names are refused,
and the error lists the generators that exist.

**Why the format said "one" and what changed its mind.** The original note in
`CompiledDrive` read: *"one drive per solve ... modelling it the other way round would
let a document declare two frequencies on one structure — which is a different
instrument and almost always a mistake."* Two devices refuted it. A real
travelling-wave guide superposes a fast confining RF on a slow travelling wave, and a
trap performing a stored-waveform isolation runs a low-frequency notched comb across
its endcaps while the ring carries the main drive. **Two frequencies on one structure
is not a mistake; it is what a trap is.**

### It costs nothing in the solver

Basis superposition is indifferent to what the weights are functions of. Two
generators reaching the same electrodes in the same proportions are **one solved
pattern carrying two weights on two clocks**, exactly as a DC supply and an RF supply
already were. What multiplies the solve count is a different *spatial pattern*, never
a different frequency.

Measured on the shipped travelling-wave guide, whose 24 rings each tap both
generators: **3 basis solves**. Two for the wave — a sinusoidal phase ramp collapses
into a fixed quadrature pair however many rings there are — and one for the
alternating confinement.

The quadrature collapse is decided **per generator**, because an instrument may run a
sinusoidal confinement and a switched excitation at once and each collapses or does
not on its own terms.

### It does change the step control

A field with two timescales must cap its step by the faster one.
`DrivenSolvedField.ShortestPeriodSeconds` is the minimum over generators, and for a
harmonic waveform it is the period of the **highest term** rather than of the
fundamental — a comb reaching order 120 carries information a hundred and twenty times
faster than its own repeat rate, and a controller told only the fundamental would step
over every one of those oscillations while its error estimator agreed the step was
accurate. For the field the step was shown. It was not shown the field.

Measured: the guide's wave repeats at 0.5 MHz and its confinement at 3 MHz, and the
assembled field reports **333.33 ns** — the confinement's period, not the wave's.

### What is still one drive

**Three-dimensional solves.** `CompiledSolvedField3D` and `Geometry3D` carry a list
and the builder uses it, so nothing below the document is limited; the `solved3d`
document form has not been given the `drives` spelling. It is the same change and it
has not been needed yet.

**Analytic elements.** An `OscillatingUniformField` is its own generator with its own
frequency, superposed through `DrivenSuperposedField`, which is how the notch-width
measurement in `docs/validation.md` is done. A solved geometry with a supplementary
uniform excitation would be that superposition, and nothing in the format declares
one.

### And on a volume geometry

A `solve3d` takes the same two forms. That was one-sided until recently:
`CompiledSolvedField3D`, `Geometry3D` and the three-dimensional builder all carried a
list of drives from the start, while the document spelled a single `drive` - so a volume
geometry could not express what a cross-section already could.

**The validation is shared rather than copied.** Both electrode documents implement one
`ITappedElectrode` interface and the tap compilation is one function, so the refusals
above - `drive` with `drives`, `driveAmplitude` with `taps` - arrived in three dimensions
by *being* the same code. That was chosen against duplication deliberately: a computation
copied across a seam is how a declared gas came to take part in a run and not in a figure
of merit.

Measured on a volume geometry: two generators reaching the same electrodes in the same
proportions collapse to **one** basis solve carrying two weights on two clocks, and two
distinct spatial patterns give **two**.

## A box may be tilted

Every three-dimensional primitive is axis-aligned — a box, a sphere, a cylinder along an
axis — which builds the devices §1's table asks for and cannot express one thing: a plate
that is deliberately **not** parallel to another.

That is not an exotic case. An asymmetric-track analyser's two ion mirrors converge by a
couple of hundred microns over a third of a metre, and the convergence is the **mechanism**
rather than a tolerance: it is what makes the drift decelerate and reverse. Without it the
model is a generic multi-reflection analyser wearing the right dimensions.

```json
{
  "name": "upperBoard",
  "shape": "box",
  "minX": { "value": 0,   "unit": "mm" },
  "maxX": { "value": 635, "unit": "mm" },
  "minY": { "value": 20,  "unit": "mm" },
  "maxY": { "value": 24,  "unit": "mm" },
  "minZ": { "value": 0,   "unit": "mm" },
  "maxZ": { "value": 350, "unit": "mm" },
  "tiltAxis": "x",
  "tiltHalfTurns": { "expression": "asinPi(convergence / driftLength)", "unit": "1" },
  "potential": { "value": 0, "unit": "V" }
}
```

**Half turns, so `1.0` is 180° and a right angle is `0.5`** — the convention `cosPi` and a
drive phase already use, and for the same reason: `double.CosPi(0.5)` is exactly zero where
`Math.Cos(Math.PI / 2)` is 6.1e-17, which would tilt a nominally upright plate by a rounding
and give a symmetric geometry a spurious asymmetry made of floating point. A tilt beyond
half a turn is refused rather than wrapped, because every orientation of a box is reachable
within that and a document meaning 0.05 and writing 5 has made a unit mistake.

The tilt is about the box's own centre, and applies to boxes only.

### It is a rotation of the query, not a fourth shape

A tilted box *is* a box. Signed distance and first entry both rotate the query into the
box's own frame and then run the formulas they always ran — a rotation is rigid, so a
distance measured in that frame is the distance in the world, and affine, so the fraction
along a segment is preserved exactly. Giving a tilted box its own geometry code would be two
implementations of one solid, and two implementations of one solid disagree eventually.

The one query that genuinely changes is the **bounding box**, which is a statement in world
coordinates and grows: rotating a half-extent mixes the other two, and the result is exact
rather than a bound because a box's corners realise it. The shell extracts a conductor's
surface over exactly that box, so one that was too small would lose the tilted corners
silently.

### A sub-cell tilt survives, measured

Shortley–Weller stores how far a conductor is as a fraction of a cell, so a tilt far below
one cell is carried rather than rasterised away. On a 0.5 mm mesh, a convergence swept over
a two-hundred-fold range:

| convergence | of a cell (each end) | asymmetry | step |
| --- | --- | --- | --- |
| 1 µm | 0.0010 | 1.6577e-3 V | — |
| 2 µm | 0.0020 | 3.3154e-3 V | **2.0000** |
| 5 µm | 0.0050 | 8.2886e-3 V | **2.5000** |
| 25 µm | 0.0250 | 4.1445e-2 V | 2.0001 |
| 100 µm | 0.1000 | 1.6590e-1 V | 2.0011 |
| 200 µm | 0.2000 | 3.3254e-1 V | 2.0045 |

Proportional to a thousandth of a cell. The control is a parallel pair, which is symmetric
in x and reports **7.1e-15 V** — so the mesh introduces no asymmetry of its own and the
tilted numbers are the tilt.

**A ladder rather than two points, and that is what found something.** Two points cannot
tell proportionality from an affine response, and the first version of this measurement had
an offset in it.

### A conductor face exactly on the node lattice is degenerate

With the plate faces landing exactly on grid nodes, the proportional response above acquires
a **fixed offset worth about seventeen microns of convergence**: a tilt of one micron and a
tilt of ten report nearly the same asymmetry, and the step ratios come out 1.05, 1.16, 1.34
instead of 2. Moving every face a quarter cell off the lattice removes it completely.

This is not about tilting — it is about where a conductor sits relative to the nodes. The
mechanism is recorded as a hypothesis: a node lying exactly on a Dirichlet surface is
classified inside, so an arbitrarily small tilt moves the surface off that node on one side
and not the other, which is a whole node's change in classification rather than a small
change in a cut length. What is established is the measurement and the cure.

**The modelling rule:** do not place a conductor face exactly on a cell boundary when the
quantity of interest is a small geometric perturbation. Same shape as the parallel-plate
example's two mistakes — the geometry, not the discretisation, was what needed fixing.

## Several ion populations (schema 0.13)

A model declares either one `ion` or a list of `species`, never both.

```json
"species": [
  { "name": "peptide-2+",
    "massToCharge": { "value": 622.0, "unit": "Da" },
    "chargeNumber": 2,
    "mobility": { "zeroField": { "value": 0.0428, "unit": "m^2/(V s)" } },
    "population": 3e6 },
  { "name": "peptide-1+",
    "massToCharge": { "value": 1244.0, "unit": "Da" },
    "chargeNumber": 1,
    "population": 1e6 }
]
```

**The mobility lives on the species, and that is the substantive choice.** Mass, charge
and mobility are one ion's three properties. Leaving the mobility under `transport` would
make a document say which ions are present in one place and how each of them moves in
another, with nothing tying the two lists together — so `transport.mobility` beside
`species` is refused rather than treated as a default. A species that declares none, like
`peptide-1+` above, derives its own from the gas cross section, and that derivation is
Mason–Schamp for **that species' mass** rather than a number shared across the mixture. A
shared derived mobility would separate nothing while looking like a converged answer.

`population` is required per species. What a mixture is *for* is that the populations act
on one another, and a population defaulted to zero would be present in the separation and
absent from the space charge — the two halves of one run disagreeing about whether it is
there.

### What is refused, and why each is a document saying two things

| Declared together | Why it is refused |
| --- | --- |
| `ion` and `species` | two different statements about what is being transported |
| `transport.mobility` and `species` | how "the" ion moves, when there is no "the" ion |
| `source.cloud.population` and species populations | how many ions there are, twice |
| a `species` list of one | that is an ion, and the document should say so |
| `species` on a trajectory-only run | no mode in this build steps several trajectory species |

None of these has a reading under which one is a default for the other, so none is merged —
the same rule that refuses a geometry declaring both `drive` and `drives`.

The cloud's spatial spreads still shape every species' seed. That is geometry rather than
identity, and one source really does emit them all from one place; only its `population`
is refused, because that is the singular form of what each species now declares.

### What a mixture run reports

Under `mixture` rather than `diffusion`, because a mixture has no single mobility, no
single transit and no single density — filling that block from the first population would
report one species wearing the name of the run. Per species: the mobility it actually ran
with and whether it was derived, launched / collected / remaining ions, named losses,
transit, final centroid, and the step that species alone would have taken. Shared: the
step actually used, which species set it, and what the self-field solved.
