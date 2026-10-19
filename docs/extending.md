# Extending the platform

**This page is for the agent that adds a capability to Einzel, not the one that drives it.**
Those are different jobs with different surfaces, and conflating them is why this page took so
long to exist.

| role | what it has | what it uses |
| --- | --- | --- |
| **drives** an instrument design | a project directory, no source | the CLI (`--json`), the MCP server |
| **extends** the platform for a new device | the source, a toolchain | the seams on this page |
| **develops** the engine | the source, the whole history | the tests, and `docs/` |

§5 of the specification says agents must extend the platform and not only drive it, and `EXT-*`
answers that with **Python extensions** — which can compute a figure of merit and cannot add a
`polygon` primitive or a `cosPi` to the grammar. Every time a device has forced a change here,
it needed exactly the kind of change a Python extension cannot make. So this page covers the
other half.

---

## What extending has actually meant

`LIB-1` says that if a device needs a change below `Einzel.Library`, the abstraction is probably
wrong — believe the signal. It has fired about twelve times. **Every single one was
vocabulary, and none was architecture.** Nothing has ever required a change to the solver, the
integrator, or the transport core.

| device | what it needed | kind |
| --- | --- | --- |
| Kingdon trap | `log` in the grammar | function |
| multipole guide | `cosPi`, `sinPi` | function |
| C-trap | `asinPi`; a parametric launch `direction` | function; attribute |
| C-trap, rebuilt | the `revolve` electrode | primitive |
| ion funnel | `floor`, `mod`; `repeat` on an electrode | functions; attribute |
| travelling-wave guide | `drivePhase` as an expression | attribute |
| linear ion trap | the `polygon` electrode | primitive |
| linear ion trap, in 3-D | the `prism` electrode | primitive |
| Astral | a tilt on a box; Neumann faces on `solve3d` | attributes |
| TIMS analyser | `axis` on the analytic RF element | attribute |
| TIMS front end | `fringe` on a bounded element's region | attribute |
| rectilinear trap | start at rest; parametric vector placement; a dimensionless zero | attributes |

Three kinds, and that is the whole list. **Twenty-three device templates have cost fourteen
schema versions, all purely additive.** Roughly half the templates needed nothing below the
library at all.

**The useful consequence for planning**: a device that is a new *shape* costs a file and
perhaps forty lines. A device that needs a new *physics mode* — a gas, RF, space charge, the
diffusive density solver — costs weeks. Which device you pick decides which bill you get, and
that is the lever worth being deliberate about.

---

## The three kinds

### A function in the expression grammar

**Where**: `src/Einzel.Core/Model/ExpressionEvaluator.cs`, in the `switch` on function name.

**The pattern.** A function is added when a placement cannot otherwise be written — not when it
would be tidier. `log` exists because a Kingdon trap's potential is logarithmic in radius;
`cosPi` and `sinPi` exist because a 2n-pole is 2n rods at π/n intervals and there was no
trigonometry at all.

**Two conventions that are load-bearing rather than stylistic.**

- **Angles are in half turns**, so `cosPi(0.5)` is exactly zero. `Math.Cos(Math.PI / 2)` is
  6.1e-17, and a rod placed a hair off axis gives the multipole a spurious dipole made of
  rounding. The same reasoning put the drive decomposition in half turns.
- **Dimensionless arguments only**, as `sqrt` already required. There are no unit literals in
  the grammar, so a function taking a length would have no way to say which length.

**What it owes**: a test that the function's own closed form holds, and one that a device
written with it produces the geometry intended. The multipole guide's `rodFill` reproducing
Denison's 1.1468 through the derived chain is a sharp check on `sinPi` as well as on the
geometry — a function is best tested through something that would visibly break.

### A geometry primitive

**Where**: three places, and missing the third is a real bug rather than an omission.

- The **geometry** — a closed-form signed distance and first entry — in
  `src/Einzel.Core/Model/SolvedFieldDocument.cs` for a cross-section and
  `src/Einzel.Core/Model/Electrode3D.cs` for a volume, consumed by
  `src/Einzel.Fields/Solved/GeometryBuilder.cs` and
  `src/Einzel.Fields/Solved/GeometryBuilder3D.cs`.
- The **validation** cases in `ModelValidator`, which is where a malformed one is refused by
  name.
- The **pairwise overlap check**, `src/Einzel.Core/Model/ElectrodeOverlap.cs`. Two conductors
  occupying the same space at different potentials give a field of a geometry nobody described
  — the Dirichlet mask is written electrode by electrode, so the last one wins — and that check
  exists to refuse it. It switches on a *pair* of shapes, so a new cross-section shape adds a
  row of cases rather than one. A pair it does not recognise is the failure this project
  already met: a hexapole written with a quadrupole's rod ratio put its rods through one
  another, and the engine **solved it, converged in eight cycles, and returned a field**.

  **A volume primitive has no rows to add here, and needs none.**
  `src/Einzel.Core/Model/ElectrodeOverlap3D.cs` is the volume half, and it does not switch on
  the pair of shapes at all: it asks each primitive only for its **bounding box** and its
  **signed distance**, both of which this list already requires. So a sixth primitive is
  checked the day it compiles, which is invariant 2 in a new place — the fifteen exact pair
  tests five shapes would otherwise need include a tilted box against a revolved hyperbola,
  which has no closed form worth writing.

  **It searches for a witness rather than proving disjointness, and that is the design.** A
  point strictly inside both conductors *proves* the geometry is ill-posed, so a refusal is
  never wrong; the opposite claim is far more expensive, because two conductors sharing a face
  have `max(dA, dB)` equal to zero over a whole surface and a subdivision would have to cover
  it. Since the doctrine is *miss rather than falsely refuse*, the expensive half is the half
  not worth buying: a budget running out means "no violation found", not "refuse anyway".
  Branch and bound on `max(dA, dB)` over the intersection of the two boxes, pruning on the
  1-Lipschitz bound and expanding deepest-bound-first, with both electrode centers probed
  first as a seed — for a convex primitive that settles containment in two
  evaluations, and for a concave outline it costs one probe.

  **A bounding-box screen alone will not do**, which the C-trap says: its five rods are nested
  arcs about one axis, so `rodInnerUpper`'s box lies wholly inside `rodOuter`'s while the metal
  is nowhere near. The screen is still worth having in front of the search — on the shipped
  Astral it settles 3,204 of 3,328 disagreeing pairs at no cost — because it settles them the
  safe way, by proving disjointness.

  **The tangency tolerance is load-bearing, and for the reason the flat-face defect taught.**
  A witness must be `1e-9` of the pair's extent inside both. Faces meant to coincide are
  written as two different expressions over the parameter surface and agree to a few ulps
  rather than exactly, so an exact test would refuse a geometry whose author did nothing
  wrong — and tangency is not a corner case, it is how every segmented chain in this library
  is written.

  **Tangency is allowed; a mesh sampling the tangent face is warned about.** A face two
  disagreeing conductors share is legitimate geometry and an ill-posed place for a node, or
  for the point where a stencil arm first meets metal: which conductor it is credited to is
  decided by rounding, and no refinement ladder sees it. The geometry builders check that
  before every solve (`mesh.node-on-shared-face`, `docs/numerics.md`) using the overlap
  checks' own `StatesOf` and `Agrees`, so a new primitive is covered by the same two queries
  it already owes - a bounding box and a signed distance - plus the first entry the cut links
  already need. A primitive whose bounding box does not contain its surface would make that
  check miss, silently, which is one more reason the box has to be right. A grounded face of
  the domain counts as a conductor at zero volts in the same check, asked with the same two
  queries, so a new primitive flush with a wall is covered too.

  **Both checks ask about every state the instrument has.** A stage changes what an electrode
  *holds* and may not change where it *is*, so "do these two agree" has as many answers as
  there are states. Each gathers the base state, every stage, and every ramping stage's far
  end — the arm that matters, since a ramp's opening excitations are usually the previous
  phase's. The geometry is fixed across states, so the expensive half is still done once per
  pair.

  **How thin a sliver it reaches, measured two ways.** Two boxes sharing a face overlap
  over their whole area, so the search only has to find a **slab**: it finds 1 µm of shared
  metal on a 10 mm box. The realistic case is two *curved* surfaces just interpenetrating,
  which share a **lens** and are harder — two tori whose profile circles of radius r have
  centers d apart share a lens of width `2r - d`, a closed form, so the sliver is a
  controlled quantity. On a 3 mm tube the deepest lens found at the shipped 3,000 probes is
  **5.8 µm**; at 30,000 it is 4.8 µm and 300,000 buys nothing further, so **the limit is a
  budget rather than a wall** and resolving a thinner lens costs steeply, as a halving in
  three dimensions should. The threshold is the same at 64 and 256 profile vertices, which
  is what says it measures the search and not the polygon. 3,000 is kept because 5.8 µm of
  penetration into a 3 mm conductor is 0.2 percent — far below any modeling mistake anyone
  makes — and a tenfold budget would cost the linear ion trap 43 ms of validation instead of
  4.

  **The two checks were cross-validated against each other, once.** Running the volume
  witness search over every shipped cross-section — a completely different algorithm on the
  same geometries — found **0 witnesses**, agreeing with the plane check's exact pair tests
  everywhere. That is corroboration rather than coverage, which is why it is recorded here
  and not kept as a test: `CompiledElectrode.SignedDistance` is defined for `Disc`,
  `Rectangle` and `Polygon`, exactly the three the plane switch already tests exactly, so the
  search shares the plane check's blind spot for an **edge profile** and could not have
  caught it. The volume check has no matching gap, because there is no volume analogue of an
  edge profile.

**The pattern.** A primitive carries a **closed-form signed distance** (negative inside) and a
**closed-form first entry** along a segment. Those two are what make it a cut cell in the
solver, an absorbing surface for an ion, and a drawable outline in the renderer — one
implementation, three consumers, and the renderer needs no change at all because a conductor is
drawn as the zero level set of its own signed distance.

**The traps, both of which have bitten.**

- **Every arm of every switch must be named, with a throw for the rest.** `Electrode3D`'s
  switches once disagreed about their default arms — size fell through to a box and centre to a
  sphere — so a fourth shape would have been sized as one thing and centred as another, with no
  diagnostic. Now a new shape fails to compile until every switch has decided about it, which
  is the point.
- **A polygon is written once and repeated, not written out.** The first linear-ion-trap
  template was 116 KB of longhand vertices: parametric in the letter and unreadable in fact. A
  vertex entry takes `count` and `index` and stands for a run, which took it to 24 KB. **When a
  generator script is needed to write a document, the format is missing what the script
  supplies.**

- **An exact test on a computed quantity is a test that will be wrong.** The `revolve`
  primitive decides whether a profile edge is an annular disc or a cone by asking whether its
  two endpoints share an axial coordinate, and the first version asked `dh == 0.0`. An outline
  is written as expressions, so a face meant to be flat is flat only to rounding: the C-trap's
  inner rod has its last hyperbola vertex at 3.0000000000000009 mm meeting a corner written
  3.0000000000000001, sloping by 8.7e-19 m. Against zero that is a cone of slope 2e15, whose
  quadratic is so ill-conditioned the root is lost or badly displaced. The same rod's other
  face **was** exactly flat, so one side of a mirror-symmetric electrode got its cut cells and
  the other did not — the conductor mask symmetric either way — and the field came out eleven
  percent asymmetric across a plane the geometry is symmetric about. Compare against a
  relative scale built from the coordinates in play.

**What it owes**: the primitive against a shape it should reduce to (a square polygon solving to
the rectangle's field to 1e-13; a square prism a box to 3e-18 m; a revolve far from its axis a
prism, to under 100 nm at a kilometer of radius), and a convergence check, since a curved
boundary is where second order is won or lost — a circular profile revolved converges on the
closed-form torus at exactly the inscribed-polygon rate, 14.446 / 3.614 / 0.904 um at 32 / 64 /
128 vertices.

**And it owes a symmetry check the shape itself cannot fail.** A mirror-symmetric electrode
must be cut symmetrically, which is a property of the *entry* code rather than of the distance,
and the conductor mask stays symmetric whether or not it holds.

### An attribute on an existing element

**The commonest kind, and usually the right one.** `axis` on the analytic RF quadrupole,
`fringe` on a region, a `tilt` on a box, `repeat` on an electrode, a `drivePhase` that may be an
expression.

**The pattern that keeps recurring**: something was a plain `double` or a fixed choice, and a
device needs it to be an expression over the parameter surface, or to be permuted, or to be
softened. §9's rule — *every placement is a parametric expression, never a baked number* — is
the test to apply. `drivePhase` was a `double` while every other placement was an expression,
and a phase that cannot depend on the repeat index cannot ramp, so the one device the
travelling-wave guide existed to model could not be expressed.

**What it owes, and this is the part most easily missed**: a **default that leaves every
existing document computing what it did, to the bit**, asserted rather than assumed. `axis`
defaults to z; a region with no `fringe` is bit-identical to a bounded element before fringes
existed. A new attribute whose default changes an answer is a silent revision of every model in
the corpus.

---

## The interface seams

Rarer, larger, and each is a genuine design decision rather than vocabulary.

| seam | where | what implementing one means |
| --- | --- | --- |
| `ITransportMode` | `src/Einzel.Transport/ITransportMode.cs` | a third way to move ions. Declare `IsAvailable` and `ProducesTrajectories`; `REG-2` will ask about validity |
| `IElectrostaticField` / `ITimeVaryingField` | `src/Einzel.Fields/ITimeVaryingField.cs` | a new kind of field. See below — the time-free interface is a trap |
| `RfWaveform` | `src/Einzel.Fields/Analytic/RfWaveform.cs` | a new excitation shape. Needs a `Mean`, which the cycle average now asks for in closed form |
| the figure-of-merit catalogue | `src/Einzel.Commands/FiguresOfMerit.cs` | a new measurable. Carries a unit, an accuracy class per §12, and a `FlightBasis` the cost gate asks |
| `ISelfField` | `src/Einzel.Transport/Interaction/ISelfField.cs` | a space-charge method. The direct sum is the reference it is validated against |

**One trap on the field seam is worth stating on its own, because this project has met it eight
times.** `ITimeVaryingField` also implements the time-free `IElectrostaticField`, and **a
time-varying quantity reached through a time-free interface does not fail — it answers at an
arbitrary instant.** That has produced: `einzel solve` reporting the DC of a driven geometry;
the diffusive mode stepping a density through a snapshot of the RF; `SuperposedField` becoming a
snapshot when a driven member was summed in; the renderer drawing one instant on every frame; a
sequenced leg reading the phase's first instant; and a sequenced analytic element reading its
states at t = 0 in both transport modes. If you implement either interface, ask what the
time-free arm answers and whether that is a statement anybody wants.

---

## When your change fits none of these

**Say so, and treat it as evidence.** `LIB-1`'s claim is that a device forcing a change below
`Einzel.Library` means the abstraction is wrong; eleven times it has been narrow and the
abstraction has held, and a twelfth that is *not* narrow is the most interesting thing that
could happen to this design. Record it in SPEC.md's **Amendments** with what forced it, rather
than making the change quietly — r06 stays unchanged as the record of intent and every
disagreement is written down as a disagreement.

---

## What every change owes, whatever kind it is

- **A schema bump, purely additive.** Thirteen versions so far and every earlier document still
  reads — asserted by a test that reads one of each, with an unknown version refused as the
  control.
- **Nothing to edit in `einzel schema`.** It is generated by reflection over the document
  records with descriptions from the XML doc comments the build already requires (`AGT-7`), so a
  new field appears there by existing. If it does not, the record is the thing to fix.
- **A test with teeth, established by mutation rather than by assertion count.** Break the thing
  deliberately and check that a test fails; a test that passes a mutation was exercising a path
  that does not contain the mutated line. That has caught several tests here that asserted
  nothing.
- **`SPEC.md` updated in the same change** — the register row and its evidence, or an amendment.
  A status page that has drifted is worse than none, because it is trusted.
- **The reason, in the code.** This repository's comments record why a thing is the way it is,
  including the version that was wrong first. Several decisions here cost real time to reach and
  the argument is worth more than the outcome.
