# Einzel — living specification

**Status of this document.** `einzel-software-spec-r06.html` is the original
specification, written before any of this existed. It stays in the repository
unchanged, as the record of what was intended. **This page is the working
specification**: the same requirements, plus what building them established, plus
the places where the original turned out to be wrong or incomplete.

Where the two disagree, this page states the reality and says what the original
said. Nothing here silently overrides r06; every divergence is in
[Amendments](#amendments-to-the-specification) with the evidence that forced it.

**How to keep it current.** Update the register row when a requirement's status
changes, and add to Amendments when the *requirement itself* turns out to be wrong
rather than merely unbuilt. Both should happen in the same commit as the code, for
the same reason `AGENTS.md` is generated rather than hand-written: a status page
that has drifted is worse than none, because it is trusted.

---

## Where the project is

**1,695 tests across thirteen assemblies, green on Windows and (bar the WPF project) Linux.** Warnings are errors; XML documentation is required on public API. Build clean. The EX-1 example corpus runs as a gate inside that suite (EX-2): 39 examples, every expectation a closed form, a published value, or an exact invariant.

| | Requirements |
| --- | --- |
| **Met**, with evidence | 83 |
| **Partial**, with a stated gap | 13 |
| **Not built** | 20 |
| **Unverified** — plausible but unmeasured | 2 |
| Total tagged in r06 | 118 |

A count is a weak summary and it flatters the project: **fourteen of the 20 not
built are the update mechanism and distribution**, which is one assembly that does
not exist, while the 83 met are spread across the parts that carry numbers. The
useful reading is the register, not the total.

**This table had drifted badly, and a test now counts it.** It read 64 / 14 / 37 / 3
against an actual 78 / 14 / 22 / 4, and described the shell and MCP as "whole
assemblies that do not exist" long after both were built. Six individual rows had
drifted with it, one of them — RND-9 — registered wrong in *both* its requirement
and its evidence column, so it read Not built while the thing it asks for had
shipped. `SpecRegisterTests` now asserts that every number here is the count of the
register below, that the four buckets account for every row, and that no tag appears
twice; a table that disagrees with what it summarises fails the build. That does not
make a row's *evidence* current, which is still a matter of discipline — it makes the
arithmetic over the rows impossible to get wrong.

**And the register under-counts whole sections, because it is tag-driven.** §16
gives eleven required shell views one tag between them, so the GUI appears as a
single row; §6's architecture invariants and §17's scope boundary carry none at
all. [The shell](#the-shell-and-the-rest-of-16) has its own section below for that
reason. Where a section of r06 is prose rather than tagged requirements, absence
from the register means nothing.

### What exists

```
Einzel.Core  Fields  Transport  Analysis  Library  Sweeps
Einzel.Io  Project  Extensions  Render  Commands  Cli
Einzel.Mcp  Wpf
```

### What does not

```
Einzel.Compute      the SIMD and ILGPU dispatch layer (CMP-1, PERF-5)
Einzel.Update       release check, staging, version policy (all of UPD, DST)
```

The second is load-bearing for requirements that are otherwise met on paper:
without `Einzel.Update`, GRD-11's defect taint has no published floor to compare a
version against, and none of §18 can be exercised. The shell exists with nine of
§16's eleven views (see [the shell section](#the-shell-and-the-rest-of-16)); its
two missing views are the extension manager pane and the update notice, the latter
waiting on `Einzel.Update`.

`Einzel.Wpf` is a **deliverable rather than a permission**, and the Windows GUI
capability was part of why the toolchain is C# - a rationale r06 never records. See
[the shell section](#the-shell-and-the-rest-of-16) and Amendment 25.

---

## Delivery: planned against actual

The original plans five phases in sequence. **The project has not run that way**,
and the divergence is worth stating plainly rather than pretending the phases were
followed.

| Phase | Planned scope | Actual |
| --- | --- | --- |
| **1** · Spine, project, CLI | Model, units, symmetry, DC solver, superposition, tricubic, integrator, schema, errors, result objects, manifests, CLI, VTU | **Complete**, and its acceptance is met: ACC-1 on a reflectron, the memo's mirror pair tracked end to end, GRD-1 enforced with no bypass, an agent building a DC model from prose |
| **2** · Extensions, sweeps, shell, figures | Both extension runners, examples corpus v1, sensitivity fields, tolerance MC, optimisation, ILGPU, WPF shell, `Einzel.Render`, installer, update mechanism | **Split.** Sweeps, sensitivity fields, both optimisers, the sandboxed extension runner and `Einzel.Render` are done. The in-process runner, ILGPU, the shell, the installer, the update mechanism and **the examples corpus** are not |
| **3** · RF and pressure | Time-domain RF, statistical diffusion, collision models, gas velocity import, sequencer, space charge, Class B analysis, density export | **Scope complete.** Every named deliverable is built, gas velocity import included. What is left is on the acceptance side: the funnel benchmark needs a §23 decision and an affordable driven diffusive step. **Class B is complete** - the secular spectrum against the Mathieu characteristic exponent, and isolation efficiency against notch width on an arbitrary waveform that recovers the published digital cut-off |
| **4** · Traps, animation, MCP | Waveform excitation, multi-notch isolation, trap sequences, device library, animation, MCP | Trap sequences and the device library are largely done ahead of schedule. Waveforms, animation and MCP are not started |
| **5** · Generalise and release | BEM, MSH interchange, CAD import, public repository | Not started |

**Why it went this way, and whether that was right.** The order has been driven by
*what makes the physics real* rather than by phase boundaries — each increment
picked the thing that would most change what the engine can honestly say. That has
worked: the numbers in [Validation](docs/validation.md) are the return on it, and
several were only reachable because an earlier increment removed an artefact
(§19's coaxial check needed cut cells; a multipole measurement needed cut-cell rod
surfaces; a turn-around time needed a source that can start at rest).

**What it cost for a long while was the agent thesis.** §21's own sequencing
principle is that "the schema and the CLI are Phase 1 deliverables… which de-risks
the thesis early", and the corpus EX-1 asks for is the other half of that: an agent
has no Einzel forum posts or example files in its training data, and shipping
models it can pull into context is the counter. For most of the project one model
of thirty existed; the corpus now holds thirty-nine, gated on every change (the examples-corpus
entry of *What to do next*). **What remains is distribution**: **82 of the 118
requirements are met**, and fourteen of the twenty-one that are not built at all
are the update mechanism and distribution. Nobody can install this.

### The phase ORDER no longer describes the work, and that is deliberate

The table below says how each phase's acceptance criteria stand, and they are largely met. What
it does not say is that **the phases were not done in order**. r06 puts the installer and the
update mechanism in Phase 2; they are the largest unbuilt block in the register, while Phases 3
and 4 are substantially done and pieces of 5 are too.

**The reason is that distribution is worth nothing until there is a second user.** Packaging an
engine whose numbers are still being established would have spent Phase 2 on the one deliverable
no one could yet use, and the sequencing principle r06 states for itself - seams first, the
agent thesis de-risked early - is what actually governed. Fourteen of the twenty-one not-built
requirements are `UPD-*` and `DST-*`, which is one assembly that does not exist rather than
fourteen scattered gaps.

**What this note is for.** A reader who takes the phase order literally concludes the project is
behind on Phase 2 and goes to build an installer. It is not behind; it deferred one block on
purpose, and the block moves to the front the moment somebody other than its author needs to run
Einzel. **That, rather than a date, is the trigger.**

### Phase acceptance, checked

| Phase | Acceptance criterion | State |
| --- | --- | --- |
| 1 | Analytic and convergence tiers green; ACC-1 on a reflectron | Met — 1e-10, four orders inside |
| 1 | The memo's mirror pair tracked end to end | Met |
| 1 | GRD-1 enforced with no bypass | Met, by reflection over the public surface |
| 1 | An agent builds and runs a DC model from prose | Met, and measured — six tasks, 6 of 6 |
| 2 | An agent authors, tests and registers a working extension | Met |
| 2 | PERF-6 with the FLD-2 residual inside ACC-1 | Partial — the residual behaves (quadratic, (δ/L)²), the full campaign has not been run |
| 2 | Memo §6 items 1, 2 and 9 answered | Item 1 answered. 2 and 9 not worked up |
| 2 | A publication-quality vector figure generated headlessly in CI | Met |
| 2 | An update offered, deferred, later accepted | **Not met** — no update mechanism |
| 3 | Mathieu diagram reproduced | Met twice — ideal field q = 0.90684, solved round rods q = 0.90525 |
| 3 | Quadrupole transmission against resolution | **Met** — the band closes onto the tabulated apex q = 0.70600, R rising 1.6 to 15.6, both edges bisected to ACC-6 |
| 3 | Funnel transmission against a published benchmark | **Met, with stated caveats, on the PNNL 100-electrode funnel.** Both published curves on one template, one per transport mode: Kim et al. 2000's transmission against RF amplitude in the diffusive mode (threshold ~14 Vpp against a measured 16, nothing tuned; the plateau's absolute 65 % is not compared, since space charge and the inlet are not modelled), and Page et al. 2006's low-m/z cutoff in the collision-by-collision mode (mechanism, shape and gradient ordering reproduced; hard spheres 17–22 % low in frequency, Langevin on the measurement, the two bracketing the curve). `docs/literature-targets.md` §5; handoff 76–78. |
| 3 | Cross-mode agreement in the overlap band | Met — 0.43 standard errors |
| 4 | Trap sequences (§21 lists them under Phase 4) | **Partly met on a published trap.** The 2002 LTQ cross-section as `linear-ion-trap`: resonance ejection at the paper's working point, and a mass-selective-instability scan at its 5,555 u/s giving unit resolution from m/z 195 to 1522 as the paper claims; the ramp is a phase staircase, the axial sections are not modelled, and passage through the ejection slot depends on a slot profile the paper does not give. Amendment 37; `docs/literature-targets.md` §2. |

---

## Amendments to the specification

### 57 - The grounded boundary is a conductor, and a conductor flush with it is the same coin toss

**ACC-3, GRD-2, and Amendment 56.** Amendment 56 left one case stated and unchecked: a
conductor whose face coincides with a Dirichlet face of the domain. The grounded boundary is a
third conductor at zero volts - a face node is pinned to zero unless an electrode claimed it
first, and an electrode claims it by containing it - so a node on the face that lies on the
electrode's surface holds its potential or zero on the last bit of the arithmetic. **Measured
before building anything**: on a 100 V plate flush with a grounded edge, a trillionth of a cell
either way flips the edge node between 0 and 100 V, leaves **every free node's solution
identical to the bit** (no free node reaches a flipped one through an uncut arm), and moves the
interpolated potential half a cell off the contact by **21.9 V**, because the interpolant reads
the node. A conductor lying *outside* the domain against the face is in the solve through those
nodes alone: 9.61 V at the center of the domain against 0. A Neumann face is not a coin between
two values - the node flips between fixed and free and solves to the plate's potential either
way - and takes no part.

So the check Amendment 56 built now treats each Dirichlet face as a conductor at zero, under the
same code, and counts its findings apart (`BoundaryNodes`) with their own fix. **Nodes only**:
a face node is fixed either way and the boundary is never a cut target, so no arm samples it.
The face is the mesh's node plane, not the declared bound; "holds something other than zero" is
the overlap checks' reading in every state; an edge carrying an edge profile is not grounded;
and a face lying in a mirror plane - the axis of an axisymmetric solve included - is not a
surface, since by symmetry the conductor continues across it.

**What it found: 17 of 44 shipped solved elements**, every one a cross-section - ring stacks
run out to the grounded outer wall, a Paul trap's ring truncated at it, the einzel lens's center
tube, a Kingdon wire's ends, and two mirrors whose end cap is a plate with no thickness lying in
the grounded edge. Moving only the face on the wall by 1e-7 of the domain's extent each way, on
the same mesh: **fifteen are harmless** - every potential more than three cells from the wall and
every figure run (eleven fly ions) identical to the bit, the coin confined to the contact, where
it moves the potential by about a fifth of the applied. **For the two mirrors it decides whether
the mirror has an end cap**: the documents write the cap and the edge as one expression, so the
shipped arithmetic lands on the side with a cap, but 1e-7 of the extent outside the edge the cap
is not in the solve, the potential ten cells in moves by 45 percent of the applied, and
transmission goes from 1 to 0. Nothing in the templates is changed here; `SharedFaceCorpusTests`
pins the seventeen, and the fixes are item 11 of *What to do next*.

**Two traps in measuring it, both general.** Moving the domain face itself moved the mesh: the
ion funnel's radius is exactly 64 cells, and growing it by anything made it 128, which the first
survey reported as several volts of interior difference that was a different mesh. And
translating whole conductors instead exposed a **second coin with nothing to do with the wall**:
a single conductor's face lying on a node plane is continuous in its normal position, but the arms
lying *in* that plane, beside the conductor, are cut at its side face or graze past to a node a
cell away according to the rounding - on the funnel's last ring, a 4.5e-4 change in flight time
for a 6.4e-6-cell move. Cut cells make the answer continuous in where one conductor's surface sits
only while its faces are off the node planes. Not checked; item 11.

### 56 - A mesh samples a geometry at nodes and at arms, and a shared face must be clear of both

**ACC-3, GRD-2, and Amendment 55.** Amendment 55 found that a node lying on a face two
disagreeing conductors share is a coin toss rather than a discretization error, and proposed
an engine check for it. Built as described - nodes within a rounding of both surfaces - the
check **reported the case that motivated it clean**. The Astral's foil stripes are thinner
than a cell and hold no node; the three shared faces lie on node *planes*, and what the
rounding decided was which slice's potential the stencil arms in each plane were cut against.
So the condition r06's convergence tier cannot see is stated in terms of both places a mesh
samples a geometry: **no node, and no point where a stencil arm from a free node first meets
metal, may lie on a face shared by conductors that disagree in any state.**

`GeometryBuilder` and `GeometryBuilder3D` check it before every solve and put
`mesh.node-on-shared-face` (Qualified) on the `SolveReport`, so it reaches `run`, `preview`,
every figure and `solve` (which carried no warnings before). Tolerance 1e-9 of the finest
spacing, the overlap checks' tangency tolerance; "disagree" is theirs too, the potential and
every tap over every state, through the same functions. The arm test asks whether the point
met is within tolerance of the *other* surface rather than whether both are entered at one
fraction, because the second is discontinuous in exactly the case it exists for.

**What it found.** Every shipped template and corpus example is clean - 44 solved elements -
and `SharedFaceCorpusTests` keeps them so; `astral-3d` with its mesh shift removed reports
**180 arms and 0 nodes**, and is the control that must trip. Moving that mesh by 1e-7 of its
extent either side of the node gives 786.4372 and 784.3191 us: a **2.118 us step, 0.27
percent**, five orders above each run's stated uncertainty, on an ordinary slope of 5.4 us per
cell. It also corrects Amendment 55's evidence: of the 454 nodes and 1,586 links that differ
between the full-size and compact masks, only **180 links are the foil**; every node and the
other 1,406 links are grounded boards' ends meeting the domain's upper face, a flip between
two states near zero volts that the flight does not see.

**Two limits, stated.** The tolerance flags coincidence, not proximity: a face 2.6e-5 of a
cell off a node draws no warning and sits on the edge of the same step, which a geometric
sweep or sensitivity field would cross silently - so the fix the warning names is a tenth of
a cell, not the tolerance. And a conductor flush with a **Dirichlet** domain face is the same
coin toss against the grounded boundary at zero volts, and is not checked. **Now checked** -
Amendment 57, which also found seventeen shipped elements that were not clean after all.

### 55 - Where a mesh sits is part of its error, and a node on a shared face is not error at all

**ACC-3, §19's convergence tier.** r06 makes grid convergence a first-class test: refine the
mesh and watch the answer settle. That measures how fine a mesh is. It does not measure where
the mesh sits, and on a geometry with features smaller than a cell the two are different
errors. Both were found by generating a compact analyzer from the published Astral
(`compact-astral-3d`), where electrostatic similarity makes the right answer exact: every
length times 0.2, every flight time times 0.2, and nothing else moved.

**It did not hold, and the reason is not discretization error.** The Astral's foil is sixteen
slices at sixteen potentials sharing a face every 28.125 mm, and at the shipped mesh three of
those faces lay exactly on rows of nodes. A node exactly on the face between two conductors
at different potentials belongs to one, the other, or neither according to the last bit of
the face's arithmetic; a free node with a vanishing arm to each side takes whatever mixture
of the two the rounding set. Scaling by 0.2 - not representable, so it rounds differently from
1.0 - moved 454 nodes and 1,586 cut links to the other side and the flight time by
**0.27 percent**; 0.1999999 moved it by a different 0.03, while 0.5, 0.25 and 0.2000001
reproduced it to the bit. That is a coin toss inside a converged-looking result, and **no
refinement study would have found it**: every mesh in a ladder can sit on the same faces.
Moving the foil mesh a tenth of a cell along the drift restores similarity to 1.6e-8, the
integrator's tolerance.

**And removing it exposed an error of the kind r06 does anticipate, four times larger.** The
stripe is thinner than a cell and holds no node, so it is represented only by where its
surfaces cut links. Moved in fifths of a cell, well clear of every shared face, the flight
time spans **0.83 percent at 4 mm and 0.22 at 2 mm**: second-order, as a refinement ladder
would say, but the ladder alone reports one point per rung and so reports a spread of this
size as if it were resolved. The "0.4 percent agreement" with the published flight time that
item 6 carried was one draw from it; the mesh-converged figure is about 1 percent below, on a
two-mesh extrapolation (the next refinement is 68 million nodes and is refused).

**What this changes.** A convergence claim on a geometry with sub-cell features should vary
where the mesh sits as well as how fine it is, and an agreement quoted finer than that spread
is not an agreement. A shared face between conductors that disagree must not lie on a node;
`astral-3d` now declares `meshShiftZ` for it, and `CompactAstralTests` checks the margin.
**The engine check is built, and the survey done** - Amendment 56, which also corrects what
this entry says the coin was tossed on: arms, not nodes.

### 54 - A raster still is the viewport's picture, orthographic, and the window draws from the same composition

**RND-4, RND-1, AGT-2, UI-1.** r06 lists `einzel render still` as "shaded 3D raster still to
PNG" and RND-4 says "shaded 3D perspective is raster". It is built, and it departs from RND-4
in one word and adds one requirement r06 did not have.

**Orthographic, not perspective.** The viewport was made orthographic by a recorded decision -
an ion-optics drawing is read for where things are along the axis, which is precisely what
perspective distorts - and a still that is not the viewport's picture has no reason to exist.
RND-4's point was that shaded 3D is raster rather than vector; the projection was incidental,
and the one this project already argued for is kept.

**The still and the window draw from one composition, and that is the requirement r06 lacks.**
Its purpose is the half of AGT-2 the command layer could not reach: an agent could run every
analysis the window runs and could not see what the window shows. A still earns that only if
it IS the window's picture. So every decision that makes the picture - the camera, the color
ramps, how an electrode is colored, which layers exist and in what order, which are
translucent and whether they hide what is drawn after them, and the lighting constants the
shader uses - moved out of the shell's GL control into `ViewportPicture` and `Framing` in
`Einzel.Commands`. The window uploads that composition; the still hands it to a rasterizer.
Two consumers of one set of decisions, where before there was one consumer holding them all.

**Where each half lives is UI-1's, and the boundary test decided it.** The shell may reach the
engine through `Einzel.Commands` only and may not *use* `Einzel.Render` types - checked on the
compiled assembly, not only the project file. So the decisions live in the command layer, and
`Einzel.Render` got a rasterizer that decides nothing: triangles, lines, colors and a matrix
in, pixels out, with a depth buffer, supersampling and the window's own blend rules. The PNG
writer is hand-written over the base library's zlib, so there is no new dependency (LIC-1).
The two sets of record types - a picture layer and a raster layer - are a copy of shape with
no decision in either, and exist separately only because the two assemblies may not see each
other.

**What building it found, before it drew anything useful:**

- **A printed board has no interior**, so the level-set extraction every other shape goes
  through found no surface, and the planar mirror pair - the memo's own analyzer - was drawn
  as its end cap alone. Boards are drawn from their declaration now, as sheets on the domain
  edge in pieces colored by the profile along them, broken at every knot.
- **A reflected solve was drawn as its declared half.** The field builder composes
  `reflectAboutX` with the mirror image; the viewport did not, so a mirror pair had one mirror.
  Both halves now, in both dimensions, with the image wound to match and the extent taking it in.
- **A sheet had no normals.** A zero-width cap's signed distance is `|x - X|`, whose central
  difference on the sheet is exactly zero, and `Surfaces.Orient` said the result was a
  flat-shaded facet. It was not, in either window. It gets its facets' normal now.
- **The camera framed a sphere.** Half the bounding diagonal is turn-invariant and put a
  767 mm by 30 mm analyzer across the middle third of the picture. The window offers named
  views, not free rotation, so it now fits the box the current view sees - as the WPF viewport
  always did - and the field lines are inside the frame too.

**What it does not do.** It cannot test the window's own GPU path - shader, uploads, depth
state - which needs a GL context; the still shares every decision with the window and none of
its pixels. And RND-11's `QUALIFIED` line naming the code is not drawn, because a raster needs
glyphs and nothing here rasterizes text: a violation hatches the bottom of the picture, and the
codes travel in the file's text chunks and on stderr.

### 53 - The shell runs where the engine runs, and Amendment 26's bet was called

**UI-1, Amendment 25, Amendment 26.** r06 names `Einzel.Wpf` and this file recorded the
reasoning: Windows-only was *the decision rather than an accident of WPF*, Avalonia was
considered and not chosen "because the shell is not planned for use outside Windows", and
that would "get revisited if the need appears". The need appeared - a user asked for the
platform to run outside Windows - so it was revisited, and the answer was the one the
original note predicted: **a replacement of a presentation layer rather than a rewrite.**

`Einzel.Shell` is that layer. It targets plain `net10.0` rather than `net10.0-windows`,
builds its window on Avalonia and its viewport on an OpenGL control, and references
**`Einzel.Commands` and nothing else** - which is what made the swap cheap. Invariant 1
kept every physics assembly free of UI types, and Amendment 25 made every shell action a
CLI invocation, so there was no capability living in the old window that had to be
excavated before a new one could have it.

**What it cost is the measure of whether those two rules were worth keeping.** Four of
§16's views carried across - the 3-D viewport, the live-run watch, the model tree and the
field as equipotentials - at 2,167 lines, of which the physics is zero. No assembly below
the shell changed. The one file ported verbatim from `Einzel.Wpf` was the color ramp, and
only its namespace differed.

**The WPF shell is untouched and still builds.** Its assembly was renamed
`einzel-shell-wpf`, because two projects producing one assembly identity is a latent
collision and it had already made the boundary test ambiguous about which shell it had
found. Which of the two is the survivor is not settled here: what is settled is that the
cross-platform one exists, its claim to run anywhere is *checked* rather than asserted, and
nothing was lost to get it.

**Three things are genuinely different rather than merely ported.**

**The invariant is now tested from a test project that runs on Linux.** `Einzel.Wpf.Tests`
is Windows-only, so the shell's own tests only ever ran on one platform;
`Einzel.Shell.Tests` targets plain `net10.0` and its eleven tests run on both runners. A
cross-platform claim checked only on Windows is a cross-platform claim nobody is checking.

**ANGLE hands back an OpenGL ES context on Windows, and a `#version 330 core` shader does
not fail loudly there - Avalonia silently disables the control.** So the shader body is
written once and a dialect header is chosen from the context the platform actually
supplied. The failure mode is the one this project keeps recording: not an error, but a
capability that quietly is not there.

**A high-severity advisory arrived with a transitive package, and the build refused it.**
Avalonia 11.2.3 pulls `Tmds.DBus.Protocol` 0.20.0 for the Linux session bus, which carries
GHSA-xrw6-gwf8-vvr9; 0.21.2 is affected too. Warnings-as-errors with NuGet auditing turned
it into a build failure rather than a note, which is the guard working, and central
transitive pinning names the fixed 0.95.1 once for every project. LIC-1 is clear -
Avalonia and Silk.NET are both MIT, verified in their nuspecs rather than assumed.

**What is not carried across**: the journal, results by accuracy class, the regime
inspector, the sequence editor, the project view and the extension manager. Each is
presentation over a command that already works, which is the same observation
`docs/shell.md` makes about the WPF shell's own remaining rows - and is the evidence for
AGT-2 that matters, since it means the second window is a drawing exercise rather than a
capability exercise.

**A review found seven defects before it merged, and two drew the wrong picture.** Every
driven electrode was painted the same color - the port kept the intent (color by the peak
the drive reaches, not the DC) and lost it in the arithmetic, taking `Math.Abs` of a
**signed** drive amplitude and recovering the sign from a DC that is zero for a purely
driven electrode. All four rods of `quadrupole-rf` came back at +500 V. That is the seventh
appearance of that defect and the first to reach a viewport, and it survived because the
decision sat inside a GL control where checking it needs a platform. **A decision that needs
a platform to observe is a decision nothing will check** - it is `Shading` now, and its test
fails with the old expression restored.

And `Framing` spanned conductors and trajectories, so a diffusive model with no electrodes -
which has neither, and which this assembly's own test model is - framed at a millimetre at
the origin and drew its packet entirely off screen. The density is measured now, and
`Framing.Union` grows the frame as the packet drifts without letting it breathe. The other
five and the reasoning are in `docs/shell.md`.

**What is open**: `Watch run` applies to diffusive models only, and is refused with a
reason for a trajectory model because the whole bundle arrives faster than the first frame
of a watch. That is correct, and it means "watch the analysis happen" is true for a density
and not for a scan, a sweep or an optimization - which are the study drivers. Making those
watchable is a command-layer question before it is a window one. The GL path itself is
untested - shader, uploads, depth state need a context, and the ANGLE dialect defect is
exactly what a headless GL context in CI would catch.

### 52 - An error raised where the location is unknown is located by whoever caught it

**AGT-3.** `EinzelError` carries a JSON Pointer because an error here is a *recovery
instruction*, not a notification. A dimension mismatch is raised inside `Quantity`, which
has two operands and no document, so it names the root as a placeholder - and the frames
catching it, which were resolving a named parameter or a numbered electrode's face, added
it **verbatim**. The path was known at the catch site and discarded, the same shape as
`FieldAssembly.Build` dropping its `SolveReport`.

**The gap had been recorded and read as small.** The changelog carried "a
derived-parameter units error reports its path as `/` rather than naming the parameter - a
small AGT-3 gap, not yet fixed" for as long as it had only been seen once. Measured on a
real template it is not small: a foil thickness written without a unit gives **64 identical
`UNITS_INCOMPATIBLE` errors, every one located at `/`**, naming neither which electrode nor
which face and indistinguishable from what one mistake would print. Located, each reads
`/fields/2/solve3d/electrodes/0/repeat/7/maxY`.

`EinzelError.At(path)` fills in the placeholder, applied at the four sites that add a
caught error. **Only the placeholder is replaced** - locating an error must never make it
less located, since an expression evaluator is handed the path it works on and raises its
own failures with it. One of the four had already been written correctly and the branch ten
lines below it had not, which is how the recorded gap came to exist.

Found by driving a newly added refusal through the CLI rather than by a failing test.
Teeth measured both ways: making `At` the identity fails three tests, and removing its
placeholder guard fails a different one.

### 51 - A volume geometry's conductors were never checked for occupying the same space

**LIB-1, GRD-3.** `ElectrodeOverlap` has refused two conductors in one place at two
potentials since a hexapole was built with a quadrupole's rod ratio, put its rods through
one another, and solved contentedly in eight cycles at a convergence factor of 0.0285. It
takes a `CompiledElectrode` and is called from the `solve2d` path alone. **Every volume
geometry was unchecked** - `box`, `sphere`, `cylinder`, `prism`, `revolve` - from the day
the volume solver landed, and `docs/extending.md` recorded the gap, named both candidate
designs, and said "neither is built".

`ElectrodeOverlap3D` is the volume half, called from `CompileSolved3D` at the same point
the plane path checks: after expansion, because a repeated electrode only overlaps itself
once its copies exist.

**It switches on nothing.** Each primitive is asked only for its bounding box and its
signed distance, both of which the model format already requires for the solver and the
ion absorber, so a sixth primitive is checked the day it compiles. That is architecture
invariant 2 in a new place: the fifteen exact pair tests five shapes would otherwise need
include a tilted box against a revolved hyperbola, which has no closed form worth writing.

**It searches for a witness rather than proving disjointness, and that is the design
rather than a shortcut.** A point strictly inside both conductors *proves* the geometry is
ill-posed, so a refusal is never wrong. The opposite claim is far more expensive: two
conductors sharing a face have `max(dA, dB)` equal to zero over a whole surface, and a
first prototype that tried to prove `min >= 0` burned a 200,000-box budget on each of sixty
such pairs in the Astral and came back **inconclusive on the commonest legitimate
configuration there is**, while the real overlap it was meant to find is settled by the
very first probe. Since the plane check's own doctrine is that a guard must miss
rather than falsely refuse, the expensive half is the half not worth buying: a budget
running out means "no violation found". Branch and bound on `max(dA, dB)` over the
intersection of the two boxes, pruned on the 1-Lipschitz bound, expanded
deepest-bound-first, with both electrode centers probed first as a seed: for a convex
primitive that settles containment in two evaluations, and for a concave prism or revolve
outline - where `Centre` is the outline's bounding-box center and need not be inside - it
costs one probe.

**A bounding-box screen alone would refuse the C-trap**, whose five rods are nested arcs
about one axis: `rodInnerUpper`'s box lies wholly inside `rodOuter`'s while the nearest
metal is nowhere near it. The screen still earns its place in front of the search - on the
Astral it settles 3,204 of 3,328 disagreeing pairs at no cost - because it settles them
the safe way, by proving disjointness. Validation costs 4 to 43 ms on the four shipped
volume templates.

**The tangency tolerance is load-bearing**, at `1e-9` of the pair's extent. Faces meant to
coincide are written as two different expressions over the parameter surface and agree to a
few ulps rather than exactly; an exact test on a computed quantity is what made a symmetric
electrode solve to an asymmetric field, and here it would refuse every segmented electrode
chain in the library.

**It refused a shipped template on its first run.** Every drift stripe in `astral-3d` was
extruded outward by its own declared 2 mm thickness, through the inner face of the grounded
board it is printed on and **1.285 mm into the metal behind it**. The fix is one expression:
the stripe now runs from `foilGap` out to `halfGap`, the board's own face, so the two are
tangent, and `foilThickness` became derived rather than a second knob.

**No number moved, measured rather than argued.** The two declarations give identical masks
- every node, every cut link - at cell sizes of 4, 2, 1, 0.5 and 0.25 mm. Two mechanisms
combine: a cut link records the *nearest* surface along its arm, so the stripe's front face
wins over anything behind it, and a node inside both goes to the board because the boards
are written last. **So the interpenetration was latent rather than active** - a refinement
study on this template would not have silently changed the geometry, which is not what one
would guess and is exactly what a single-mesh comparison could not have said, since at the
shipped 4 mm cell the 0.715 mm stripe holds no node at all. The mutation that gives the
measurement teeth is an 8 mm extrusion, far enough to leave the back of the board; it fails
all five rungs.

**What it does not do**, stated rather than discovered: it cannot prove two conductors
disjoint, so a sliver of shared metal thinner than the search reaches is missed. Measured
two ways. Two boxes sharing a face overlap over their whole area, so the search need only
find a **slab**: 1 um of shared metal on a 10 mm box. Two *curved* surfaces just
interpenetrating share a **lens** and are harder - built as two tori, where the circles'
overlap `2r - d` is a closed form, the deepest lens found on a 3 mm tube is **5.8 um** at
the shipped budget. **The limit is a budget rather than a wall**: 30,000 probes reach
4.8 um and 300,000 buy nothing further, so a halving costs over tenfold. 3,000 is kept
because 5.8 um into a 3 mm conductor is 0.2 percent, far below any modeling mistake, and
a tenfold budget would cost the linear ion trap 430 ms of validation instead of 43. **A refusal rests on the sign of a signed distance and on
nothing else** - the Lipschitz bound decides only which boxes are worth opening, so a
primitive whose distance is a conservative under-estimate rather than exact can cost the
search a witness and can never cause a false refusal, since the witness is a point both
primitives report as interior.

**And it asks over every state the instrument has, not over the one it is declared in.**
A stage may change what an electrode holds - that is what a stage is for - and may not
change where it is, which `SameGeometry3D` already enforces. So *"do these two agree"* has
as many answers as there are states, and asking it of the base state alone is the proxy
this project keeps meeting: two conductors sharing metal at one potential while held and at
two while pushing would be a field of a geometry nobody described for exactly the duration
of the push, on a document that validated cleanly. The geometry is fixed across states, so
the expensive half - the witness search - is still done once per pair, and the refusal names
the stage. A ramping phase contributes both its ends, since the disagreement may live only
at the end it is walking toward.

**And the plane check was given the same treatment in the same change**, because it had the
same defect and fixing one path and not the other is the pattern this project keeps
recording. `ElectrodeOverlap.Check` asked about the declared state alone from the day it was
written - correct until a solve could carry a sequence - and now gathers the same states.
**None of the twenty-two shipped templates is affected either way**, which is what makes
this a guard written before the case exists rather than a fix after one; the mutation back
to reading the base state alone fails two tests on each path.


### 50 - Two shipped templates were drawn as approximations, and the approximations had reached the findings

**LIB-1.** `c-trap` modeled each bent rod as a chain of thirteen overlapping spheres, and
`paul-trap` modeled a quadrupole trap's hyperboloids as three flat annuli. Both were
deliberate when written — a `cylinder` in this format is axis-aligned and a bent rod is not;
the cross-section vocabulary had no curve — and both were documented as approximations. They
survived because **an approximation adopted because the format could not express the real
thing does not announce itself when the format gains the ability.**

**What was added below the library is one primitive.** `revolve` is a closed outline turned
about an axis through an arc: `prism` is to a line what `revolve` is to an arc, and the
vertex coordinates are a distance from the axis and a position along it — the half-plane an
axisymmetric solve already works in, so the linear ion trap's hyperbolic rod profile
transfers to a bent quadrupole unchanged. Schema **0.14**. Inside the sweep the signed
distance reduces *exactly* to the profile's own two-dimensional distance; every revolved
profile edge is a quadric, so a link's crossings are roots of a quadratic and nothing is
bisected. Checked against the closed-form torus at 14.446 / 3.614 / 0.904 um for 32 / 64 /
128 profile vertices — the inscribed-polygon bound to the digit, falling exactly 4.00x — and
against a `prism` at a kilometer of radius to under 100 nm. The Paul trap needed nothing new
at all: `polygon` had landed for the linear ion trap, which wants a hyperbolic rod for the
same reason.

**What it bought, measured:**

| | approximation | real geometry |
| --- | --- | --- |
| Paul trap, effective r0 against 4.0000 mm declared | 3.8195 | **3.9983** |
| Paul trap, boundary against a tabulated q = 0.90804 | 9 percent low | **0.07 percent** |
| Paul trap, curvature ratio against −2 at 0.4 mm | −1.9867 | **−2.0006** |
| C-trap, electrode declarations / basis channels | 49 / 1 | **5 / 1** |
| C-trap, ejected waist | 1.73 and 1.92 bend radii | **1.011 and 1.011** |
| C-trap, convergence at a 20 mm bend | 20.8x | **45.9x** |
| C-trap, arrival spread into the analyzer | 60.02 ns | **38.90 ns** |

**The Paul trap's 9.4 percent was not an unknown.** It was measured three independent ways,
explained correctly, and carried as a stated correction on every number the template
published. That is the right thing to do with a known approximation, and it is also exactly
what let it sit there: **a correction that is carried faithfully stops being a problem to
fix.**

**Two published findings were artifacts, and both had plausible mechanisms attached.** The
C-trap's waist at 1.73 and 1.92 bend radii was explained as an aperture lens at the slot; the
thin-lens fit it implied mispredicted the longer bend by 17 percent, and that failure was
recorded at the time rather than buried. On swept rods the waist is at the center of
curvature, where velocities aimed along radii meet, and a **fourfold change of slot opening
moves it 1.048x** — so the slot is a hole. And the Paul trap's octupole resonance band at
605–614 V, identified from the measured secular frequencies as an order-four condition met to
0.055 percent with order four predicted in advance from the trap's symmetry, is **gone**:
forty amplitudes below the edge, none loses its ion, and the condition now misses 2 by 0.22.
That identification was right, and an octupole is what a flat annulus buys.

**One defect in the primitive is worth the spec's attention because it is a class.** Deciding
whether a profile edge is an annular disc or a cone by asking `dh == 0.0` is an exact test on
a computed quantity. An outline is written as expressions, so a face meant to be flat is flat
only to rounding — the C-trap's inner rod has an edge sloping by 8.7e-19 m over three
millimeters of radius, which against zero is a cone of slope 2e15 whose quadratic root is
lost. The same rod's other face **was** exactly flat, so one side of a mirror-symmetric
electrode got its cut cells and the other did not, **the conductor mask was symmetric either
way**, and the solved field came out eleven percent asymmetric across a plane the geometry is
symmetric about. Fixed with a relative tolerance; cut-fraction asymmetry 1.0 to 1.9e-13. The
general statement is that a symmetric Dirichlet mask is not a symmetric discretization, so
nothing that checks a geometry by asking which nodes are inside can see this class of defect.

**And three tests were carrying the old geometry in their source, each failing differently.**
A hardcoded constant (`EffectiveMm = 3.8195`, over-predicting every secular line by ten per
cent), a quoted sentence printed as output beside an assertion on a range around it, and a
**vacuous truth** — `TheBeadsOverlapAlongEachRod` grouped electrodes by name stem and compared
consecutive members; with the rods swept each group has one member, the loop never runs, and
the worst spacing stays at the zero it was initialized to. It passed. `docs/lessons.md`.

### 49 - Rebuilding an operator does not require reallocating it

**CMP-1:** a ramped diffusive run allocated its six coefficient arrays and face-operator
storage at every rebuild. At 513 x 65 nodes, a warmed Release probe measured
**7,473,016 bytes per rebuild**. Reusing one workspace per run and species reduced
this to **3,432 bytes**, including probe overhead, without changing the numerical
operation order. Over 142,000 rebuilds the cumulative allocation is about 1.06 TB
versus 0.49 GB; this is not peak memory. Measured minimum rebuild times were 19.81
and 19.07 ms, so no substantial computational speedup is claimed.

`CoefficientReuseTests` compares fresh and reused operators exactly, on both plane
and cylindrical grids and across a boundary-kind change. The storage is cleared
before assembly so a skipped face cannot retain an old coefficient or loss flag.
The single-species and mixture loops retain separate workspaces for each species.
No interpolation, flux formula or time-stepping rule changes.

### 48 - A model's content hash does not include the files it reads

**PRJ-3 / GRD-7:** doubling an imported pressure doubled the measured transit from
320.236 to 640.472 us, while `verify` still reported the old result current. A study's
own settings were similarly absent from its manifest's content identity.

Manifests now record `inputHashes`: portable project-relative paths and streamed
SHA-256 hashes of gas velocity/pressure imports and the study file. Inputs are hashed
before computation and checked again before writing the manifest; callers must keep
inputs fixed during a calculation. Study model text is retained from evaluation rather
than re-read after the study. Changed, missing or unreadable inputs invalidate a result,
as does a moved model resolving its imports somewhere unrecorded. Older imported-data
or study manifests cannot certify inputs they never recorded and require a rerun.
`InputProvenanceTests` exercises edits, deletion, relocation, legacy records and study
settings through the CLI. This is explicit tracking of those declared files, not a
general dependency graph.

### 47 - A phase must run the declared physics, and collection must use the declared plane

**SEQ-1:** trajectory legs of a mixed sequence called the single-ion integrator without
a collision sampler or a collective self-field. Gas and space-charge settings therefore
had no effect. Legs now use per-member collision samplers, or `PacketIntegrator` for
`direct` or `pic`, with the current packet's positions and surviving population. Streams
are seeded per member and phase so successive legs do not restart the same collision draw. Collision
quality flags propagate. A numerical integration failure is refused rather than handed
to the next phase as a completed packet. `TimeShiftedField` preserves conductor queries.

`SequencedPhysicsRegressionTests` distinguishes a 1 mbar leg from its near-vacuum
control by over 10 mm, and both self-field methods from an unpushed packet by collection
at the detector. Those tests fail on the original engine. An exhausted packet stays
empty through later phases: 200 arrivals, zero conversions, no reseeding and no undefined
centroid printed (`AnExhaustedPacketIsNotConvertedOrReseeded`, `TheHumanPrinterHandlesAnExhaustedPacket`).

**ACC-5:** diffusion previously used detector orientation but ignored detector position.
Moving a detector from 40 to 20 mm left the 212.93345 us transit unchanged. Collection
still occurs at a grid face; internal or oblique detectors are now explicitly refused,
as are radial detector planes on a cylindrical grid. Existing templates, examples and
test fixtures now name the face they were actually using, without moving the mesh or
changing their numerical expectations. `AnUnrepresentableDiffusiveDetectorIsRefused` guards
this limitation; the EX-2 corpus still passes.

### 46 - A cycle average needs the spectrum, and a cache needs its defining inputs

**REG-2:** the ponderomotive denominator belongs to each frequency, not to the
shortest period of the complete field. An audit found that adding a zero-amplitude
2 MHz generator to a 1 MHz quadrupole reduced its computed well from 0.19094 V to
0.04736 V and introduced a false DC contribution. More samples cannot correct the
wrong denominator.

The current effective-field path therefore requires a known, single sinusoidal
frequency (a single Fourier harmonic qualifies). Inactive generators do not choose
its period. Mixed frequencies, rectangular waves, multiple harmonics and unknown
spectra are refused with `REGIME_INVALID`; time-domain trajectories remain supported.
A frequency-resolved effective potential is still needed to lift this restriction.
`RfValidityRegressionTests` checks the inactive-generator control and the refusal.

The same audit placed a changing RF field between every spatial cache probe: doubling
its amplitude left the cached well unchanged rather than quadrupling it. Reuse now
requires equal oscillation definitions, operating-point amplitudes and species/sampling
settings. Unknown fields or spatially varying damping rebuild conservatively.
`LocalisedAmplitudeChangeInvalidatesTheCache` fails on the previous implementation.
Quiver is an amplitude, so it now uses absolute charge; `QuiverIsAnAmplitudeForEitherChargeSign`
checks both polarities without changing the sign of the effective potential.


Places where building it showed the original to be wrong, incomplete, or right for
a reason it did not give. Each is a change to what the specification *should say*,
not merely a note about what is unbuilt.

[Spec findings](docs/spec-findings.md) carries the long form of most of these with
the measurements attached; what follows is the register of them.

### 45 - A density arriving is not a count, and "any" is not the test for whether it did

**The specification counts ions.** `ACC-5` asks for transmission as a measured quantity with
every loss itemised by the surface that took it, and every arrival figure in section 12 is a
statistic over a set of arrivals. `TRN-2` makes a density field the diffusive mode's output. The
two were never reconciled, and the gap is not presentational: a density does not arrive in ones,
so what crosses a collecting face in a step is a real number and the guard on whether anything
arrived cannot be a count being non-empty.

**And that number is never zero.** Scharfetter-Gummel is built so its zero-flux state is exactly
the Boltzmann factor `exp(-q phi/kT)` - which is what makes every equilibrium result in this
engine the scheme's own answer rather than an approximation converging - and the corollary
nobody had drawn is that the flux across a collecting face *behind a barrier* **is** that
Boltzmann factor. A packet held by 60 V against a thermal 0.026 V therefore emits `exp(-2300)`
of itself across the detector from its first step. A guard reading "did anything arrive" answers
yes for a packet that has not moved.

**The consequence, on the run this project most wanted.** The TIMS front-end sequence completed
and reported `mean arrival 11366.06 us, spread 4024.20 us` over **7.74e-245 ions**, while
99,893.6 of 99,971 were still in the tunnel. A weighted mean and a second moment, both plausible
numbers in the right units, over a population that is not ions - and nothing anywhere saying
they describe nothing. This project has been caught by the same tail once already, when an
elution onset read as the first non-empty arrival bin came out during the hold rather than
during the ramp.

**What the specification should say.** Where a transport mode's population is continuous, a
figure computed over arrivals requires a threshold on the arrived population, stated as a
fraction of what was launched, and below it the figure is **absent with a named reason** rather
than computed. A fraction rather than a count, because a model may launch few ions deliberately
and "less than one ion arrived" is a real answer for a low transmission while 1e-245 of one is
not an answer at all. Absent rather than zero is the rule the rest of this surface already
follows for an undefined measurement, and it is sharper here than usual: the alternative is not
a zero a reader might question but a plausible time in microseconds.

**Status: built.** `RunCommand.Eluted` is the decision, named and public so it can be exercised
without a run: arrivals must reach a millionth of the launched population.
`sequence.nothing-eluted` is a `ValidityViolation` naming which numbers are missing and why,
because "no mean arrival" and "this model does not elute" are the same fact and only one of them
is actionable, and it says in the same breath that the sequence completed - so a reader does not
read a missing number as a failed run. Eight cases pin the predicate, including both the shipped
model produced: 7.74e-245 of 99,971 is a tail, 81,049 of 85,170 is an elution.

### 44 - A run may take hours and nothing requires it to say so

**The specification is careful about cost before the work and silent about the work itself.**
`GRD-8` gates an operation above a cost threshold, so the platform will tell you a run is going
to take four hours; `PERF-8` sets the *only* timing requirement in the document at the opposite
end of the scale, cold start to first output under 500 ms; and `CLI-1..6` cover `--json`, the
stream split, `--dry-run`, exit codes and ordering. **Nothing asks a long-running operation to
report while it runs**, and nothing asks it to leave anything on disk before it finishes. §16
assumes a progress surface for the shell and the shell is not where studies are run.

**The consequence, observed three times rather than predicted.** The TIMS front-end sequence
failed to complete at 4.75 CPU-hours, then at 40 minutes, then at 7.6 wall-hours against a 4.17
hour estimate, the last of them because a Windows update rebooted the machine. **It produced no
output on any attempt** - not a step count, not a partial density, not the per-phase widths it
had already computed - so the one question that would settle whether the estimate is low or the
run does not terminate was never asked. Nor is this exotic: a diffusive window in a driven
structure is set by a Courant limit against a ponderomotive gradient, which is hundreds of
thousands of steps whatever each one costs, and the estimate for that case is itself four orders
low because the drift limit needs a field it has not solved.

**What the specification should say.** An operation whose cost estimate exceeds some small
multiple of `PERF-8`'s budget must, by default, (a) report where it has got to at intervals, on
the diagnostic stream, and (b) leave a durable record of that progress that survives the process
- including any work already complete in units the operation itself defines. Both by default
rather than on request: **a flag somebody has to remember is a flag that is not set on the run
that gets killed**, which is the whole failure being fixed. And the reporting must be provably
free of effect on the answer, since the cheap way to report is to hand an observer the solver's
own live state.

**What it should not say.** Not that progress belongs to the shell (§16), which is where the
argument would naturally have gone: the operations that take hours are studies, and studies are
run from the CLI. Not a new file format either - the durable record is the same records the
result document already carries, so a checkpoint and a result describe a phase identically and
one rendering serves both.

**Status: built.** `einzel run --progress <seconds>`, thirty seconds by default, writes
`results/<name>.progress.json` and one line per interval on stderr. The file is **removed when
the run writes its answer**, so its presence means the run did not finish - which needs no
timestamp comparison to read - and `einzel report` reads it, says how far the run got, and draws
the phases that completed through the same rendering a finished run's go through. The interval
is wall clock, so which steps report is not deterministic; nothing reported reaches the solve,
and a silent run and one reporting every step are asserted equal to the last digit, with the
mutation that perturbs one cell from the observer failing that test and nothing else.

Two things measured while building it, both recorded in `docs/lessons.md`. **A projection from
total elapsed time is a projection of the solve** - dividing wall clock by the fraction
simulated charged a one-off solve to every remaining microsecond and read 82 minutes against an
actual 23, and 17,576 minutes on the first report of all; the rate is now measured between
reports, which is the same correction `einzel estimate` made when it started excluding process
start. And **the solve is now the longest silent stretch**, minutes on a large geometry, so it
is announced before it starts rather than after.

### 43 - A run's evidence is machine-readable and nothing renders it for a person

**r06 is thorough about evidence travelling and silent about anybody reading it.** `GRD-1` puts
an envelope on every quantity, `GRD-2` carries warnings through every surface, `PRJ-3` makes a
manifest determine its run, and `CLI-1` puts `--json` on every verb. All of that is for a
consumer that parses. **What no requirement asks for is an account a person can read** - what
was run, what came out, and which caveats rode along - and §17's `RND-*` does not fill the gap,
because it is about figures of the *instrument* rather than of the *work*.

**The consequence, observed rather than predicted.** Following a stretch of engine work means
reading terminal scrollback or a git log. A night's runs leave `results/*.json` and manifests
that are complete, exact and unreadable; a person who wants to know what happened has to be
told, which makes the account depend on somebody writing one. That is the same failure the
generated platform layer of `AGENTS.md` exists to prevent, one level up.

**What the specification should say.** A **report over a project's results and manifests is an
engine capability**, reachable from the CLI so that `AGT-2` holds - an agent gets the same
report - and producing a self-contained page: per run, the model and its hash, the engine
version, the numbers with their envelopes, the warnings by severity, and any figures already
rendered beside them.

**And what it should not say.** Not a session recorder, and not new state. `PRJ-4`'s argument is
that the durable record of a design is the document and its history, with `.einzel/` regenerable
and discardable; a report is a *view* over the results and manifests that already exist, so it
carries no state of its own and cannot drift from what actually ran. A recorder would be a
second account of the same events, and the two would part company.

**Status: built.** `einzel report` writes one self-contained HTML page over a project's
manifests and results, with `--json` carrying the same account so `AGT-2` holds. It reuses
`einzel verify` for drift rather than recomputing it, holds no state, and is guarded by the two
tests that make "view" a property rather than a claim: nothing is added to `results/`, and two
reports over the same runs are the same page but for the instant each was rendered at - a
recorder passes the first and fails the second.

**And building it found two defects, because it is the first thing here that ever read a result
document back.** `verify` walks the manifests and never opens a result; `test` re-flies the
model. So a producer with no consumer had been unchecked however many tests it had.

- **The sequenced run path wrote a manifest and no result.** Three of the four run paths stored
  one; that path stored provenance and no answer, and it is the path every TIMS study takes -
  so the runs whose answers were missing were the ones most worth reading. `PRJ-3`'s claim that
  a manifest determines its run stood either way; what was missing is the stored answer the
  determination is *for*. It was also the one path storing **absolute** artifact paths, so its
  manifest named files by where they sat on the machine that wrote them.
- **A result document did not read back into the record that wrote it.** The emittance fields
  are `required double?`, `WhenWritingNull` omits a null on the way out, and C#'s `required`
  demands the property on the way in - so a document was unreadable exactly when a value was
  *absent*, which is a trap or a run where nothing arrived. Every ensemble run of that kind had
  stored a result this build could not load, and nothing said so because writing succeeded.
  "Regenerate and compare" was impossible for all of them. **My first fix was the wrong one of
  two**: writing every required property including its nulls round-trips and *changes the
  published document*, against this surface's own recorded policy that an undefined measurement
  is absent - caught by a test written months earlier that pins that encoding. Fixed on the
  reading side instead, so the document is byte-for-byte what it always was.

Both are fixed and both are guarded, the second by a test that straddles the switch: a packet
that arrives writes every field and round-trips fine, so the discriminating case is a run whose
ensemble measured nothing.

### 42 - Agents must extend the platform, and only one of the two ways to extend it is specified

**r06 §5 argues that agents must extend the platform and not only drive it**, and `EXT-1` through
`EXT-8` answer with a Python extension surface: a manifest, a schema, two runners, a registered
figure of merit an optimiser can drive. That is real and it is built, and it answers **one** of
the two ways this platform gets extended.

**The other is the one that has actually recurred.** `LIB-1` has fired about eleven times - a
device needing a change below `Einzel.Library` - and every instance was a **grammar function**
(`log`, `cosPi`, `asinPi`, `floor`, `mod`), a **geometry primitive** (`polygon`, `prism`, a
tilted box), or an **attribute on an existing element** (`axis`, `fringe`, a parametric
`drivePhase`, `repeat`). **A Python extension can add none of those.** It computes a number from
a finished model; it cannot give the model format a word it did not have.

So the specification describes the objective-function case in detail and the vocabulary case not
at all - and the vocabulary case is the one a new device keeps needing. There is no page saying
where a shape goes, which switches must learn about it, or what a correct one owes.

**What the specification should say.** The platform has **two extension surfaces**, and the
second is the C# seams: the grammar's function table, the electrode geometry with its three
switches, the attributes on existing elements, and the interfaces (`ITransportMode`,
`IElectrostaticField`, `RfWaveform`, the figure-of-merit catalogue, `ISelfField`). It should be
**documented as a surface** rather than left to be inferred from the source, for the same reason
`AGT-7` generates the schema: a capability an agent cannot find is one it does not have.
`docs/extending.md` is that page, and it is grounded in the eleven instances rather than in a
listing of public members.

**What it should not claim.** Not a packaged SDK. Extending needs a toolchain and a compile, so
the extending agent is one with the source tree - and a NuGet-shaped surface waits on
distribution (`UPD-*`, `DST-*`), which is deferred and stated as deferred. **An exhaustive API
reference is also the wrong shape**: an agent with the source can read a signature, and what it
cannot read off the source is which of three places a new shape must be taught about, or that
a new attribute owes a default that leaves every existing document bit-identical.

### 41 - A model could describe one ion, and an instrument that separates ions holds several

**r06 §9** gives a model an `ion`: one mass-to-charge, one charge number, and under
`transport` one mobility. Every device in §1's table that exists to *separate* ions is
therefore modellable only one ion at a time — a mobility analyser, a mass filter, a funnel
with a mixed beam through it. That is fine while the ions are independent, and they stop
being independent at exactly the populations those devices run at.

**The coupling is one quantity and nothing else.** Every coefficient a species needs is its
own — its mobility sets its drift, its charge and the gas temperature set its diffusion
through the Einstein relation, its charge sets its Scharfetter–Gummel thermal voltage, and in
a driven structure its mass and momentum-transfer rate set the cycle-averaged well it feels.
If the field were its own as well, N species would be N independent runs and could be done
one after another with no new machinery. What ties them together is the potential their
**total** charge raises, which no sequence of separate runs computes.

So the amendment is narrow: a model may declare `species` in place of `ion`, each entry
carrying its own mobility, and a diffusive run steps them together over one shared step
through one shared self-potential. Measured: a one-species mixture is **bit-identical** to
the single-species path; a second population with the mean field switched off leaves the
first **bit-identical**, which is what says the field is the only coupling; and with it on the
first is displaced 91.6 µm away from its neighbour.

**Two things r06 does not anticipate fall out of it.** The mobility has to move onto the
species — mass, charge and mobility are one ion's three properties, and leaving the mobility
under `transport` would make a document say which ions are present in one place and how each
of them moves in another. And a driven mixture needs **one pseudopotential per species**,
because that well is built from charge, mass and damping: measured at 30.55 V for m/z 200
against 3.055 V for m/z 2000 at the same point in the same RF. A shared one is refused,
because it would give every population the well built for whichever ion it came from and
would look exactly like a correct answer.

Schema 0.13. Trajectory mixtures are refused by name rather than run as the first population:
the packet integrator flies one species at a time and there is no mode in this build that
steps several trajectory species. See `docs/model-format.md` and `docs/pressure.md`.

### 40 - A parameter could not say where its number came from

**r06 §9** gives a parameter a value, a unit, bounds and a description, and LIB-1 makes that
surface the thing a study varies. What none of it can express is **where the number came
from** — so in a model reconstructed from the literature, "this is the published acceleration
voltage" and "this is a guess nobody has justified" are the same kind of statement,
distinguishable only by reading English inside a description field.

**The Astral reconstruction is where it bit.** Three of its numbers are solved for, thirteen
are read off a published drawing, and the rest are published outright. Which results would
move if a better source turned up depends entirely on which is which, and that question was
asked repeatedly over weeks and answered each time by re-reading prose — which is exactly how
the geometry came to be described as "guessed depths" in one document and "read off figure 1"
in another, both current, both wrong about part of it.

**It is also the one thing a study cannot infer.** A sweep can perturb a parameter and an
optimiser can fit one; neither can say whether the nominal was measured or invented. Every
other property of a parameter is either used by the machinery or checkable against it.

**Recommend §9 give a parameter a `provenance` and a `source`.** Built: `chosen` (the
default), `published`, `drawn`, `fitted`, `guess`, with the source **required** for the three
that make a claim and **refused** for the two that do not — the same argument §9 makes for
units, since a claim of authority with nothing behind it cannot be recomputed by the reader
and cannot be told apart from one that is backed. `drawn` is separate from `published`
because a number read off a figure carries a reading error a table does not, and in a
reconstruction the two behave differently.

A run reports `parameters.guessed` and `parameters.fitted` at severity `Provenance` —
the first because the result is about a geometry somebody assumed, the second because a
model agrees with whatever it was fitted against *by construction* and that agreement is not
evidence. Published and drawn are silent: a run resting on cited values is the ordinary case.
Schema 0.10.

### 39 - PERF-5 is not GPU-bound on the hardware anyone has, and the fair comparison is 1.4x

**r06 §PERF-5** sets a quadrupole stability scan of 500 geometries by 1000 ions at under two
hours and says it is *"GPU-bound; why ILGPU is in the stack"*. Both halves were measured
before any GPU code was written, and both are wrong on this hardware.

**The scan is reachable on the CPU.** `einzel estimate` on exactly that study — 500 points
over the RF amplitude, 1000 ions each — gives **77 minutes against the two-hour budget**, from
a pilot measured on the machine rather than a constant.

**And the GPU's advantage in double precision is 1.4x, not the 5x it first appears.** ILGPU
1.5.3 on a GTX 1650 against an i9-9900K, chained fused multiply-adds, best of several runs:

| | GFLOP/s FP64 |
| --- | --- |
| CPU, one core | 1.2 |
| CPU, all cores, scalar | 17.0 |
| **CPU, all cores, vectorised** | **65.8** |
| **GPU** | **90.8** |
| GPU, FP32, for comparison | 3,369 |

The card runs FP64 at **1:37 of FP32**, measured, which is what consumer Turing silicon is.
And the honest CPU baseline is the *vectorised* one: a single ion's Runge-Kutta stages are a
dependent chain, but ions are independent of each other, so the CPU can vectorise across them
exactly as the GPU parallelises across threads. **Comparing a vectorised GPU against a scalar
CPU flatters the GPU by the vector width** — 5.34x becomes 1.38x when the comparison is made
fairly, and the difference is a factor of four that is the AVX2 lane count and nothing else.

**So the GPU is a hardware-purchase decision, not a software one.** On a card whose FP64 is
1:2 rather than 1:37 the same kernel would be worth roughly thirty times the CPU; on the
hardware this project has, it is worth 1.4 times and costs a whole backend. **Recommend
PERF-5 drop the claim that it is GPU-bound**, and that the next compute work be the CPU path
it names in passing: vectorising trajectory integration across ions, which is the measured
4x, reuses the existing integrator and interpolant, and is what makes the GPU comparison fair
in the first place.

Two things this does not say. ILGPU itself is sound and its licence is clear — University of
Illinois/NCSA, no GPL anywhere in its closure, LIC-1 satisfied — so nothing here argues
against it later. And the field *solve* is a separate question from the trajectory scan: it
is memory-bandwidth bound, which is why study-level parallelism tops out near 5x on eight
cores while a pure-arithmetic control keeps scaling, and bandwidth is the one thing a GPU
does bring.

`docs/gpu-handoff.md` carries the probe that makes this measurement on any machine in about
ten minutes, what to look for in its output, and which of the two candidate kernels to port
once it is known. It is written to be picked up on different hardware, since that is the
decision this amendment turns on.

### 1 · SYM-1 is missing translational invariance

**r06 §9** lists cylindrical symmetry, a mirror plane, and discrete periodicity.
The first real geometry needed none of them: a printed-circuit ion mirror is stripe
electrodes running along the drift direction, so the potential is genuinely
independent of that direction and the problem reduces exactly to two dimensions.

Not a minor omission — it removes a whole dimension from the solve, which is the
difference between a 513 × 513 grid and a 513³ one. **Recommend adding it to
SYM-1.**

### 2 · §11's turning-point step cap does not do what it says

The mechanism is implemented and it does not help. In a smooth field with a turning
point, the flight time reaches machine precision in **6 steps with the cap off** and
is marginally *worse* with it on at 105. §11's rationale is that position-error
controllers under-refine at a velocity minimum; `ErrorNorm` weights velocity error
with its own absolute floor, so it is not that controller.

**Recommend §11 state the goal — the turning point must be resolved — rather than
the mechanism.** The cap is kept, defaulting on, so the code honours the spec as
written; the evidence says it should default to 0.

### 3 · §11 is missing two step constraints that turned out to be necessary

Neither is in r06, and without either the integrator produces confidently wrong
answers.

- **A step may not outrun the field's resolution.** In a field-free region the
  local acceleration is near zero, the step heuristic proposes an enormous step, and
  the embedded estimator *correctly* certifies it — for a straight line. Observed:
  39 metres in one step, through both mirrors of a pair.
- **A step may not straddle a declared field discontinuity.** Dormand–Prince stage 4
  carries −56/15, so stage samples fall outside the step interval. Handling
  boundaries as events took a reflectron from 5.5e-10 to 1.7e-16.

### 4 · §10 understates what multigrid needs, and the fix is not the textbook one

r06 chooses finite-difference multigrid on the grounds that it is straightforward
to implement correctly. That holds for boundary-value geometries and **not** for
interior electrodes, which every device except a planar mirror has: four discs in a
box reached 1e134 V.

The textbooks point at Galerkin coarsening. What actually fixed it was **cut cells**
— a sub-cell surface has a position at any spacing, so the coarse mask can be
rebuilt from the geometry rather than projected down. 7–8 cycles at 0.019–0.023,
flat under refinement, against 43–47 at 0.55.

**Recommend §10 say that a Cartesian multigrid solver needs a boundary
representation that survives coarsening.**

### 5 · FLD-1 rests on an assumption r06 does not state

§23 recommended spiking the linearity assumption. It was run, **it failed**, and it
failed for a reason §10 does not anticipate. The *physics* is linear where §10 says.
The **discretisation was not**: below one cell a perturbation was invisible — the
perturbed solve returned bit-identical and the derivative field was identically
zero, so a study would report the parameter as having **no influence**. Above one
cell the residual was percent-level. There was no step size in between.

Cut cells fixed it, and the residual is now an ordinary Taylor remainder, quadratic
to three figures. **The limit is (δ/L)², so 1 ppm holds to δ/L ≈ 1e-3** and the
memo's 100–300 µm channels linearise to ≈1e-5. FLD-2 will correctly refuse them at
1 ppm; that is a legible trade rather than an artefact.

**Recommend §10 state the discretisation requirement explicitly:** sensitivity
fields need a boundary representation that varies continuously with the parameter,
and a node-by-node one does not.

### 6 · §19's cross-code tier is unavailable, and §22's risk does not apply

No SIMION licence — its cost is part of why this project exists. **Recommend §19 and
§22 be rewritten around that.** The schedule implication is favourable and the
validation implication is not: what carries the load instead is the analytic tier as
primary reference, literature regression promoted to the main external check, and
agreement between an analytic and a solved path substituting for agreement between
two codes.

### 7 · §9's source model assumed a beam, and §12 already required otherwise

§9 required a non-zero accelerating potential "or the ion never moves". That is
right for every device §9 works through and wrong for the whole class in §1's table
that traps and then releases — and **§12 already asked for turn-around time from
exactly such a device.** Two sections contradicted each other and neither was wrong
alone. Fixed by narrowing rather than removing: zero is legal when a declared field
could accelerate the ion.

### 8 · The parameter surface reached scalars but not vector components

§9 is unambiguous that "every placement is a parametric expression". Scalars
honoured it; `VectorValue` carried three literal numbers. It went unnoticed because
both early devices are symmetric about something convenient. **Now fixed** —
`VectorValue.Expression` takes one expression per component.

Worth keeping as a pattern rather than a bug: **the format was general in the places
the first two devices exercised and specific in the places they did not**, which is
precisely the argument §21 Phase 5 makes for validating generality with an
unrelated instrument.

### 9 · §10 does not say what a cell size means when the box does not divide by it

`Grid2D.OverBox` kept cells square and let the domain grow: a 60 × 20 mm box at a
1 mm cell **was solved as 60 × 30 mm**, silently. Fixed by giving each axis its own
spacing, so the extent is exact, neither direction is coarser than requested, and
the cost lands on cell shape (at worst 2:1) rather than on extent.

**Recommend §10 say which of the two readings it means.** They differ by fifty per
cent on an ordinary geometry.

### 10 · GRD-2 needs a rule about the shortest spelling

Twice, evidence about a computation's own quality was discarded at a seam because
discarding was the shorter code path: `FieldAssembly.Build` dropped its
`SolveReport` at the one place every run, study and test passes through, and
`FiguresOfMerit.Evaluator` dropped a draw's warnings along with its interval.

The rule that generalises, and that belongs in §4: **when a computation produces
evidence about its own quality, discarding that evidence must not be the shortest
spelling.** `BuildReported` returns field and warnings; the bare `Build` now
*throws* rather than concealing, because a plain field has no envelope to taint and
there is no third option between refusing and hiding.

A third instance surfaced later and is the same shape: `transport.gas.driftVelocity`
was honoured by the event-driven mode and silently dropped by the diffusive one.

### 11 · §23's agent-acceptance question, answered

§19 asks for scripted prose tasks; §23 leaves open what they measure and what gates
a release. Settled in [Agent acceptance](docs/agent-acceptance.md). Two decisions
were not obvious from the specification: **score actions, not self-reports**, and
**every task ships plausible wrong answers** that CI asserts must fail.

Recommended gates: 80% capability, 90% warnings, any task at 0% blocks, and **any
drop against the previous release blocks regardless of level** — the regression gate
matters more than the absolute one.

### 12 · LIB-1's test earned its keep, and fired three times in three different ways

Ten device templates now ship, and the rule has fired three times — each time
narrow, each time real, and each time meaning something different, which is the
part worth recording.

**A missing expression.** The travelling-wave guide: `drivePhase` was a plain
`double` while every other placement was an expression, so a phase could not depend
on the repeat index, and a phase that cannot depend on the index cannot ramp.

**A missing function.** `multipole-guide`: a `2n`-pole is `2n` rods at `π/n`
intervals and the grammar had no trigonometry, so that geometry could not be written
at all — not awkwardly, *not at all*. One function below the library bought one
template covering every even order instead of three near-identical files.
Amendment 17.

**A wrong check.** The Paul trap: `ModelValidator.CanDoWork` decided whether a
source may start at rest by asking whether any electrode held non-zero **DC**
potential. A trap holds zero DC and all of its potential as drive, so the archetypal
start-at-rest device was refused as a model in which nothing could move an ion.

The third is the one that needs care. LIB-1 says a change below the library usually
means the abstraction is wrong — but this change said "there is a bug here", and the
abstraction was fine. **Telling those two apart is part of using the rule**, and the
signal that separates them is whether the change *adds* something the format could
not say or *corrects* something it already claimed to support.

### 13 · A conservative operator was written twice and got it right once

The cylindrical Poisson operator is written in conservative form — flux through a
ring's outer face minus its inner face, over the ring's own volume — and r06's own
reasoning for it is recorded. **The cylindrical density solver was not**, and
computed a flux per unit area as though the two cells sharing a radial face had the
same volume. They do not: on the axis a cell is a disc rather than a ring and the
weight is **4**, the same factor of four the Laplacian carries there.

The error was largest on the axis, which is exactly where a funnel concentrates its
ions: the shipped funnel's ion ledger closed to **95.99%**, and closes to
**100.0001%** with the face weights carried.

Two things about how it was found are worth more than the fix. It was invisible
until the ledger was *made* to close — before interior electrodes absorbed
continuously and the seed's own overlap was accounted, launched, collected,
remaining and the named losses never had to add up, so a four per cent leak had
nowhere to show. And every conservation test in the suite was **Cartesian**, where
the weight is identically one; the tests passed for a reason that did not
generalise, which is the same failure mode as the uniform-field conservation test
that hid the cell-centred drift sample.

**Recommend §10 or §11 state it as a rule:** an operator written in conservative
form for one solver is not conservative in another that shares its grid, and a
conservation test on a Cartesian grid tests nothing about a cylindrical one.

### 14 · An unrecognised property was ignored, not refused

**Found by writing the corpus, on its first day.** A source cloud declaring
`transverseWidth` instead of `transverseSpread` parsed cleanly, validated, solved,
ran, and produced an emittance of **7.1e-8 µm where the closed form says 1.798** —
a plausible number from a model that reads as though it says something else.

This is the same rule r06 already argues for at length, applied to the key instead
of the value. §9 makes `{"energy": 4000}` a validation error on purpose, because
"unit ambiguity is the commonest source of silent wrongness and an agent building
from prose is the actor most likely to introduce it". A misspelled *field name*
that is silently dropped is the same failure with a shorter path to it, and §22
names its consequence as the defining risk of the whole thesis.

**Now refused**, with the offending property named by JSON Pointer and a
suggestion pointing at `einzel schema`. Four shipped test fixtures turned out to
be affected: they declared 1 mm clouds and had been running with point sources.

**Recommend §14 state it**: an unrecognised property is an error. The model format
is generated from the document types, so there is no version of "forward
compatibility" this trades away — a property the schema does not contain is a
mistake, not a future feature.

### 15 · ACC-5's transmission could not express zero

Also found by the corpus. `transmission` was read off an arrival-time peak, and a
peak needs two arrivals to have a width — so a quadrupole above its low-mass
cut-off, or an ion lost on a funnel ring, raised `INTERNAL_ERROR: a peak needs at
least two arrivals` and the run reported *itself* as a defect in the engine.

That is exactly backwards for a requirement whose entire subject is transmission
as a measured, itemised quantity. **An instrument that loses everything is the case
a reader most wants reported, and it was the one case the figure could not report.**

Worse one level up: `einzel run` caught the exception and returned *no ensemble at
all*, so the itemised losses — ACC-5's actual deliverable — disappeared precisely
when the transmission was zero.

Fixed by separating the two: a transmission is a count and needs no peak, while a
width still needs two points and is now **absent rather than zero** when there are
not two. The distinction that matters is kept: nothing arrived gives 0.0, a model
that could not be flown gives null.

**Recommend ACC-5 say so explicitly.** "Never 92 percent" is about itemisation;
this is about the endpoints, and a figure of merit that cannot represent total loss
is not measuring transmission.

### 16 · A guard one level above the thing that guards itself

Not a change to the specification so much as a rule the specification implies and
does not state. Scharfetter-Gummel's Bernoulli function handles a large argument
exactly, taking the limits explicitly to avoid an overflow inside `exp`. The flux
clamped that argument to ±40 *before* calling it — which protected nothing and
capped the effective drift at `40 D / h` above a cell Peclet of 40.

Measured on a drift tube whose expected transit is a division: **6.7% long
clamped, 0.86% long unclamped**, and the 0.86% is the packet's own spread because
it is now the same with and without a gas flow.

**Every test in the suite ran below the cap**, at a cell Peclet of 16, and reported
1.000000. They were correct and could not see it. What saw it was a corpus example
whose expectation is arithmetic the engine had no part in.

The rule worth writing into §19: **where a scheme has a dimensionless number in it,
the tests should straddle the values that number switches behaviour at, and should
print it.** A test below a threshold is not weak, it is a test of a different
regime, and nothing about it says which side it is on.

### 17 · §9's expression grammar had no trigonometry, and a multipole needs it

**LIB-1's test, run deliberately.** A multipole above four rods is `2n` rods at
`π/n` intervals, and the expression grammar could not write that — not awkwardly,
not verbosely, but **not at all**. So the choice was three near-identical template
files with coordinates written out longhand, or one function below the library.

One function. `cosPi` and `sinPi`, dimensionless-only for the same reason `sqrt`
is, in **half turns** rather than radians — the convention the drive decomposition
already chose, because `Math.Cos(Math.PI / 2)` is 6.1e-17 and a rod placed at a
quarter turn would carry a spurious dipole made of rounding.

What it bought: `multipole-guide` is **one template** with `poleCount` as a
parameter, and 4 / 6 / 8 / 12 rods each reduce to **one basis solve** (8 cycles,
factor 0.024–0.029). Twelve rods cost what four do.

**And the same argument arrived a third time, for the inverse.** The C-trap's
electrodes are chains of beads on an arc, and its ejection slot is the gap between
two of them — but the gap between two bead *centres* is not the opening between two
*surfaces*. A sphere of radius `a` centred at radius `R` reaches `asin(a/R)` past
its own centre, which for the shipped numbers is **14.7 degrees on each side**, so
a declared 27-degree slot opened **minus two** and the ejected ion struck the
bounding bead.

So the grammar gained `asinPi`, and the pattern is worth naming: placing something
by angle when what is known is a **length ratio** needs an inverse trigonometric
function, and it needs to return **half turns** so that the result composes with
`cosPi` and `sinPi` without a `pi` appearing in a document. There is no `pi` in the
grammar and there should not be — half turns exist precisely so that a quarter turn
is exact.

This is now the fourth device to find a gap one level down (`log` for the Kingdon
trap, trigonometry for the multipole guide, a parametric `drivePhase` for the
travelling wave, `asinPi` here). LIB-1 says to believe that signal, and each time it
has been a **function**, never an abstraction.

**Recommend §9 note that the grammar's function set is part of what "every
placement is a parametric expression" means.** A placement that cannot be written
is a device that cannot be a template, and the list of functions is therefore a
statement about which devices are expressible — not a convenience.

### 18 · Two conductors could occupy the same space at different potentials

Found by getting Amendment 17 wrong first. Denison's rod ratio of 1.1468 is the
classical value for a **quadrupole**; applied to six rods it puts them through one
another, and the engine **solved it, converged in eight cycles, and returned a
field**. The acceptance measurement taken from it was really a measurement of rods
closing in on the axis.

A Dirichlet mask is written electrode by electrode, so where two overlap the last
one wins. Where both hold the same thing that is harmless and often deliberate — a
shape assembled from overlapping primitives is how a fillet gets built. Where they
disagree the region is simultaneously at +300 V and −300 V of drive, and the field
returned is of a geometry nobody described.

**Recommend §9 or §10 state it**: overlapping conductors that disagree about their
excitation are ill-posed and must be refused. Now done, naming both electrodes and
what each holds, with three deliberate limits — tangency allowed, agreement
allowed, and edge profiles skipped rather than guessed at.

### 19 · A stability boundary is not a property of the design alone

ACC-6 asks for a boundary resolved to one part in five hundred of the scan, and
`einzel boundary` reaches that in eleven evaluations. §12 and §19 both treat the
result as a property of the instrument. **On the shipped Paul trap it is not, until
the observation window is long enough**, and at sixty RF cycles the "boundary" is a
ragged strip:

```
   V   672 674 676 678 680 682 684 686 688 690 692
held     1   1   0   0   1   0   1   0   1   0   0
```

At two hundred cycles the same scan is a clean step between 672 and 674 V. Nothing
about the design changed. The growth rate goes to zero at the stability edge, so
whether a marginally unstable ion reaches an electrode inside the hold is a property
of **the hold**. Two bisections over brackets differing only in their lower end gave
680.7 V and 694.4 V for the same geometry, which is how it was noticed.

**Recommend §12 state that a Class B boundary is quoted with the observation window
that produced it**, and that a convergence check in that window is part of the
measurement rather than an optional extra — the same standing that grid convergence
already has under ACC-3.

**And with the launch amplitude, which is the sharper half.** The same trap's
hold-converged edge is q_z = 0.904 at a 0.05 mm launch, 0.902 at 0.1 mm, 0.885 at
0.3 mm and 0.836 at 0.6 mm. The Mathieu equation is linear, so an ideal trap's boundary
*cannot* depend on how far off center the ion started — a trajectory scaled by a
constant is another trajectory. A real one's does, and the dependence is not small.
(Measured on the flat-annulus geometry it was larger still — 0.85 / 0.82 / 0.64 —
and on the *wrong side* of the linear boundary; see Amendment 50.)

**The reason is structural, and the fix is to stop measuring it that way.** A
measurement that registers a loss only when the ion **reaches** an electrode requires
it to cross the whole anharmonic region, so it is never a small-amplitude measurement
whatever it was launched at. The *linear* boundary is a statement about a frequency,
and §12's own secular-frequency spectrum measures it without the ion going anywhere:
calibrating β(V) against Mathieu over a range where the ion stays small fits to a
worst residual of **7.8e-5**, gives an effective radius of **3.9985 mm** against
**3.9983** from the field's curvature with no ion involved, and puts β = 1 at
q_nominal = 0.90737 against a tabulated 0.90804. The ejection edges approach it **from
below** as the launch shrinks — 0.9966 of it at 0.05 mm — rather than equalling it.

**Recommend §12 distinguish the two**, because they are different quantities and only
one of them is the design parameter: a *linear* stability boundary, which is where β
reaches one and is a property of the field; and an *ejection* threshold, which is where
a particular ion launched a particular way leaves within a particular hold, and is what
an instrument actually does.

The flat-annulus geometry also carried a **narrow band of loss at q_z = 0.739–0.750**,
sixty volts inside the main edge, which survived a mesh doubling and a hold doubling and
vanished at a 0.1 mm launch and at 60 cycles — a nonlinear resonance, and §12's Class B
vocabulary has no way to report one. It was found by the confirmation walk of
Amendment 20, from a search whose bisection had converged cleanly, and named as an
**octupole** from the measured secular frequencies. **It is gone now that the electrodes
are hyperboloids**, which is the identification's own prediction: an octupole is what a
flat annulus buys. Forty amplitudes below the edge lose nothing, and the frequency
condition misses 2 by 0.22 where it was met to 0.0011. The recommendation stands — §12
still has no way to report a nonlinear resonance, and a trap with real electrodes can
have one the truncation of its hyperboloids produces.

### 20 · Bisection cannot check its own premise, and now does

A corollary of Amendment 19, and separable from it. Bisection assumes the predicate
flips once across the bracket. **Every step it takes is consistent with that
assumption by construction**, so a clean cut-off and a frayed edge produce identical
search histories and the result looks equally confident either way. §12's "boundary
resolution" therefore says nothing about whether the located value is *the* edge.

What separates them is a walk outward from the converged bracket at geometrically
growing offsets, asking whether the predicate flips back. It costs about `log2` of
the range over the bracket width — roughly doubling an eleven-evaluation search,
against a grid that would cost five hundred and one.

`boundary.multiple-crossings` is a **validity violation**, because a value quoted as
the edge of a region when it is one of several is wrong rather than imprecise.
`boundary.single-crossing-checked` carries the probe count when nothing was found —
REG-2's rule that a check made and passed must be visible, or a reader cannot tell it
from a check never made. Two limits are stated rather than papered over: the walk
stays inside the declared range, and its first step is the bracket width, so a flip
narrower than that is stepped over.

### 21 · A resonance can be found and not named, and §12 has no vocabulary for one

The Paul trap of Amendment 19 loses its ion in a narrow band at q_z = 0.739–0.750,
sixty volts inside the stable region. §12's Class B figures can establish that the
band is there — a scan over amplitude, with controls at twice the mesh, twice the
hold, a quarter of the hold and a third of the launch offset — and can say nothing
about **what** it is, because a nonlinear resonance is defined by a condition on
*frequencies*: `n_z β_z + n_r β_r = 2` for a multipole of order `n_z + n_r`.

§12 does list "secular frequency spectrum" as a Class B figure, and it is now built.
Lomb–Scargle rather than a DFT, because a trajectory is sampled at accepted
integration steps and is therefore not uniform, and resampling it first would be
inventing values the integrator never computed. Checked against Mathieu's
characteristic exponent to **0.007–0.144 per cent** across q = 0.1 to 0.85, with both
micromotion sidebands where theory puts them.

With it, the band is the **octupole**: β_z = 0.6769 and β_r = 0.3225 give
`2β_z + 2β_r = 1.9989`, an order-four condition met to 0.055 per cent and a hundred
times worse either side. Order four is the leading multipole this geometry's symmetry
permits, so it was predicted before it was fitted.

**Recommend §12 name the resonance condition as a reportable quantity**, not merely
the spectrum it is computed from. The spectrum is a diagnostic a human reads; the
condition is a *number* — which order, met how closely — and it is what a tolerance
study or an optimiser would have to be pointed at to avoid one. Nothing in the
current register asks for it, and the trap needed it on its first real use.

A second thing that fell out. **The ideal-Mathieu prediction was wrong and had to
be**: β at the *nominal* q is 0.6156, which satisfies no low-order condition at all.
The measured 0.6769 differs because the effective radius is 3.82 mm rather than the
declared 4.00. That effective radius is now confirmed two independent ways — from
the field's curvature at the centre with no ion involved, and from the secular line
of a flown ion against the closed form — agreeing to **0.02 per cent** at low q, and
diverging at high q exactly as the anharmonicity says it must.

### 22 · Two scales in a Class B measurement that must be derived, not chosen

§12 asks for isolation efficiency against notch width and says nothing about how the
excitation is set up. Two of its scales are not free parameters, and getting either
wrong produces a plausible table rather than a failure.

**The comb spacing must equal `1/T`.** A resonance excited for a time `T` has a width
of about `1/T`, so a comb spaced more widely has *holes* — an ion between two lines
is driven by neither and survives an excitation meant to eject it. A first version
used 5 kHz against a 333 Hz width, and the notch width then toggled every ion at
once, because selectivity had nothing to do with it.

**The amplitude follows from the aperture and the duration.** Resonant growth is
linear, `x(t) = (qE/m)t/2ω`, so reaching the aperture `a` in a time `T` needs
`E = 2amω/qT`. A first version used four orders too much and ejected every ion at
every notch width. An amplitude picked to make a demonstration work is a
demonstration of the amplitude.

**Recommend §12 state both**, since the figure is not well posed without them: an
isolation efficiency is a function of notch width *at a stated excitation amplitude
and duration*, and the comb must resolve the excitation's own linewidth.

A third thing the measurement showed, worth recording because it is not obvious: the
trade only has two arms at sufficient amplitude. At the amplitude that just ejects a
resonant ion the narrow end is free and efficiency is monotone in width; at three
times that the narrow end loses the target and an **interior optimum** appears. A
study run at one amplitude would report the wrong shape and be internally consistent.

### 23 · A time-varying field reached through a time-free interface answers anyway

Found while building the above. `SuperposedField` implements only
`IElectrostaticField`, and a driven member answers that interface with its value at
`t = 0` — so summing a driven element with any other silently produced **a snapshot
of the RF at the top of its cycle**, presented as the instrument. No exception, no
NaN, nothing in the result to distinguish it.

That is the third time this exact shape has appeared: the diffusive mode stepping a
density through a t = 0 snapshot, `einzel solve` reporting the DC pattern for a
driven geometry, and now this. **The class is: a time-varying quantity reached
through a time-free interface does not fail, it answers at an arbitrary instant.**

Fixed structurally rather than by a check — `FieldAssembly` now chooses
`DrivenSuperposedField` when any member is driven, so the composition is decided by
what it contains rather than by what the caller asks for.

**Recommend §10 or §11 state the rule**: any composition of fields must preserve the
strongest interface its members implement, and a static interface over a driven
member is a defect rather than a lossy convenience.

### 25 · The shell is a deliverable, and r06 does not say why the toolchain suits it

**r06 names WPF, Helix Toolkit and a DirectX 11 viewport, and never says why C# was
chosen.** The reason is not incidental: **the Windows GUI capability was part of the
language decision.** An unrecorded rationale is one a later decision cannot respect,
and the question will be asked.

**Windows-only is the decision, not an accident of WPF.** Avalonia was considered and
not chosen, because the shell is not planned for use outside Windows. If that need
appears it gets revisited then — a decision deferred rather than foreclosed, and worth
recording as deferred so that nobody later reads "WPF" as a constraint somebody failed
to notice.

**What makes deferring it cheap is architectural rather than optimistic**, which is the
part that has to stay true for the position to hold:

- **Invariant 1** — no UI type below the shell. Every assembly above `Einzel.Wpf`
  builds and runs on Linux, and CI runs there from the first commit. `Einzel.Render`
  produces a publication figure headlessly with no display, no window manager and no
  font server (RND-1).
- **AGT-2 as strengthened below** — every shell action is expressible as a CLI
  invocation. A capability cannot accumulate in the window that has nowhere else to
  live.

Together those make a later cross-platform shell a **replacement of a presentation
layer**, not a rewrite. **Windows-only applies to the shell and to nothing else** — and
that is the misreading to guard against, because "the GUI is Windows-only" and "the
project is Windows-only" are one word apart and the second would quietly undo the
Linux CI that keeps the first one cheap.

**And the shell is wanted, not merely permitted.** This document's own §16 section
previously closed by quoting §22's scope-creep risk and concluding that a shell should
be built "when a *human* workflow is blocked without it, not when the feature list
looks incomplete." That is a sequencing rule dressed as a scoping rule, and it reads
as though the GUI were a hazard. **Interactive geometry, fields drawn over that
geometry, and animation are intended outcomes.** §22's risk is a thing to manage —
the guard is UI-1's prohibition, not deferral.

**The thesis is the pair, and neither half is the product.** An agent must be able to
drive the entire design process; a human must be able to see and manipulate the same
design interactively. The reason those are one requirement rather than two is AGT-2.

**AGT-2 is right and right for a reason r06 does not give.** r06 says every capability
reachable from the window is reachable from the CLI *through the same command objects*,
and heads the diagram **SURFACES · PEERS, NOT A STACK**. That is the correct
architecture — a shell that shells out cannot drive an interactive viewport at frame
rate, and a hundred milliseconds of process start per slider drag is not a shell. But
it leaves the guarantee resting on discipline, and an invariant checked against one
surface is one that has already been broken by the time anyone notices.

**Recommend AGT-2 additionally require that every shell action be *expressible* as a
CLI invocation, and be journalled as one.** The shell keeps driving command objects
in-process; what changes is that the journal is a list of commands somebody could run.
Three things fall out of it that discipline alone does not give:

- **A capability that cannot be written as a command cannot be added to the window**,
  which is AGT-2 enforced by construction rather than by review.
- **A human's session hands over to an agent**, and an agent's to a human, because the
  journal is the same vocabulary both use. That is the whole point of the pairing and
  it does not work if the window's actions are anonymous.
- **PRJ-3's manifest and the journal converge.** A run is already regenerable from its
  manifest; this makes an interactive *session* regenerable the same way.

The cost is real and worth stating: two representations of every action, and a
temptation to let the in-process path acquire an argument the command form has no
spelling for. That is the specific thing to review when the shell is written, and it is
the same failure mode as the validation one below.

### 24 · "One drive per solve" was a design decision, and two devices refuted it

`CompiledDrive` carried this note from the beginning:

> One drive per solve, not one per electrode. A real instrument has a generator and
> electrodes tapped off it at various amplitudes and phases, and modelling it the
> other way round would let a document declare **two frequencies on one structure —
> which is a different instrument and almost always a mistake.**

It is not a mistake. It is what a trap is. A real travelling-wave guide superposes a
fast confining RF on a slow travelling wave; a trap performing a stored-waveform
isolation runs a low-frequency notched comb across its endcaps while the ring carries
the main drive. The rule cost the shipped travelling-wave guide its radial
confinement, and made the notch-width measurement of Amendment 22 run on an analytic
quadrupole rather than on a solved geometry.

**It cost nothing in the solver to remove**, which is the part worth recording. Basis
superposition is indifferent to what the weights are functions of, so two generators
reaching the same electrodes in the same proportions are one solved pattern carrying
two weights on two clocks — exactly as a DC supply and an RF supply already were.
Measured: 24 rings each tapping two generators reduce to **3 basis solves**, two for
the wave's phase ramp and one for the alternating confinement.

What it does change is step control, and §11 should say so: **a field with several
timescales caps its step by the fastest.** The guide's wave repeats at 0.5 MHz and its
confinement at 3 MHz, and the assembled field reports 333 ns.

**Recommend §9 state that an electrode's excitation is a list of taps**, and that the
number of generators a geometry carries is a property of the instrument rather than a
modelling convenience to be minimised.

**And a negative result worth as much as the capability.** Giving the guide its
confinement did not widen its acceptance at any amplitude tried — 5 of 12 entry radii
arrive with none, 2 at 100 V, 4 at 200 V, 3 at 400 V, 1 at 800 V. The window is narrow
at both ends: above about 200 V on this ring pitch the confining drive's own Mathieu q
passes the stability limit and the ion is *ejected*, and below it the well is shallow
against a 60 V wave. The template ships with the confinement at zero, because shipping
a default that makes a device worse would be worse than shipping none. What the tests
assert is that the generator **reaches** the ion — the acceptance differs with it on —
which is the claim the capability supports.

### 38 - The obvious kernel to optimise was the wrong one, and CMP-1's reference must be selectable

**r06 §6** puts the SIMD and GPU dispatch in `Einzel.Compute`, and **CMP-1** asks that the
scalar reference never be deleted or allowed to rot. Two things about that turned out to
need saying.

**A retained reference is not a checked one.** Keeping the scalar implementation in the file
satisfies the letter of CMP-1 and nothing else: if the fast path is the only one anything
calls, the reference rots silently and the day it is needed nobody knows whether it still
works. It has to be *selectable*, and the two have to be compared as a test. Here that
comparison is over nine sizes spanning below one vector width to a thousand members, with
members inactive, with a pre-loaded accumulator, and against Newton's third law; a
deliberate mutation of the vector kernel fails ten of sixteen.

**And the obvious kernel was not the expensive one.** The pair sum is O(N^2) against the
field evaluation's O(N), so it is the obvious target. On a solved *volume* at the
macroparticle counts actually used it is 27% of an integrator stage, against 73% for the
tricubic gather; vectorising it is worth 1.04x there and spreading the field loop across
cores is worth 1.64x. On a *cross-section*, where the field is cheap, the ordering reverses.
The lesson for §6's compute layer is that **the dispatch layer is not the interesting part**
- the interesting part is knowing which loop dominates for a given geometry, and that is a
measurement per model rather than a property of the code. `Einzel.Compute` is still not
built, and one kernel does not justify it; PERF-5's GPU stability scan is what would.

### 37 - The cross-section vocabulary had no general outline, and the first real trap needed one

§9 and §10 describe electrodes as primitives with closed-form signed distance so the
cut-cell discretisation can place a surface between nodes, and the 2-D primitives were a
rectangle, a disc and an edge profile. Every shipped device fitted them - round rods, flat
plates, tubes, rings - until the radial-ejection linear ion trap of Schwartz, Senko and
Syka (2002): hyperbolic rods, one with a 0.25 mm slot cut through it, the pair moved out
0.75 mm. Not awkward to write; **not expressible at all**. That is LIB-1's signal, and it
fired for the sixth time (after `log`, trigonometry, `asinPi`, a parametric drive phase and
a tilted box), and for the first time it asked for a shape rather than a function.

**What was added.** A `polygon`: any closed outline, convex or not, at one potential, with a
closed-form signed distance (nearest edge, signed by the even-odd rule) and a closed-form
first crossing of a grid link, so its faces are cut cells exactly as a disc's are. A square
written as four vertices solves to the rectangle's field to 1e-13 of the applied potential;
a rod written as two halves meeting on a line solves to the whole rod to the bit, which is
how a slotted rod is written so that a slot of zero height is a rod. Refused rather than
solved: fewer than three vertices, a repeated vertex, zero area, a self-crossing outline.
Schema 0.9.

**And what the first version got wrong, which is the more general finding.** A hyperbolic
face needs about twenty-five vertices, each two expressions over the parameter surface, and
eight half-rods: the first template was 116 KB of generated JSON that validated, solved and
flew correctly and that nobody could read. §9's "every placement is a parametric
expression" was satisfied in the letter and defeated in spirit - the thing a reader needs
to see, that the face is one hyperbola from the slot edge to the half-width, was buried in
two hundred copies of itself. A vertex entry may now carry a `count` and an `index` and
stand for a **run** of vertices, the same mechanism `repeat` uses for electrodes applied
inside one outline. 24 KB, three entries per half-rod, the hyperbola written once. **When a
generator script is needed to write a document, the format is missing the abstraction the
script supplies**, and the script's size is a measurement of the gap.

**What the device then taught about calibration.** Flown at a nominal Mathieu q of 0.92
from the ideal formula, the ion stayed confined; the same document with round rods lost it
in 3 µs. The paper's 0.75 mm stretch of the x pair weakens the quadrupole term to 0.822 of
the ideal - measured from the solved field's multipoles, and again from the on-axis
gradient - so the ion at "0.92" was at 0.757. The paper's own q scale is the effective one
(its "q of 0.83" is quoted at 368 kHz, the ideal secular frequency to a tenth of a per
cent), as every trap's is, since q is inferred from frequency rather than computed from
metal. §12's Class B figures are stated in q, and this is the first device where the q
per volt had to be measured before any of them could be compared with a paper. It costs
one multipole projection and no ion. Details in `docs/device-templates.md` and
`docs/lessons.md`.

**And the volume vocabulary had the same gap.** Box, sphere and cylinder build the devices
§1 lists and could not extrude a slotted hyperbolic rod into the paper's three axial
sections. A `prism` - the polygon given a length along an axis, with the same vertex runs -
closes it: a square prism is a box to 3e-18 m in distance and to the bit in a solve, and a
re-entrant outline finds a link's entry through its notch. The 2-D and 3-D vocabularies now
share one general outline, which is what LIB-1's "a new device is a new file" needs when the
device is neither a plate nor a rod nor a bead.

### 36 - A geometric perturbation can sit below the discretisation floor, and then it must be constructed rather than solved

Amendment 5 records that FLD-1's linearity spike failed because a sub-cell perturbation
was *invisible* to a rasterised boundary, and that cut cells fixed it. **The Astral's
mirror convergence is the same subject one level deeper, and cut cells do not fix it.**

Two mirrors converging by a 200 micron spacer tilt by `alpha` = 2.9e-4. The entire physical
effect is the field anisotropy `Ez/Ex = tan(alpha)`, so the quantity to be resolved is 2.9e-4
of the main field. A second-order solve on a 2.5 mm cell across a 40 mm gap carries roughly
`(h/L)^2` = 0.4% of field error - **fourteen times the signal**. Measured against the closed
form, the solved tilted geometry returned **3.54, 0.011 and -0.57** of the true drift
deceleration depending only on how wide the vacuum gaps between mirror strips were, and 1.52
at half the cell size. Nothing was converging.

**Two distinct failures, and only the first is Amendment 5's.** Abutting electrodes have a
boundary kind cut cells cannot represent at all: a metal-to-metal edge has Dirichlet nodes on
both sides, so there is no vacuum node to store a sub-cell crossing on, and the edge is
rasterised at node resolution. Putting a vacuum gap between the strips makes every face
metal-to-vacuum and restores the cut cell - and the answer is *still* wrong, because of the
floor above. The tilt ladder that verified proportionality "down to a thousandth of a cell"
was run on parallel plates, whose tilted faces are metal-to-vacuum, and was correct about
them.

**The fix is not a finer mesh, and no affordable mesh would do.** Reaching 2.9e-4 of field
accuracy needs `h/L` near 1e-2 in three directions at once. What works is to stop
differencing for the effect and construct it: solve the two-dimensional cross-section, which
is exact in its plane, and **rotate the solved field**. Rotations commute with the Laplacian,
so the rotated field is the exact solution for the rotated geometry, and the anisotropy comes
out of the coordinate transform. Measured at **5.4e-20** in the field and 1.7e-18 through a
model document, against factors of several before. A converging pair is then two
cross-sections rotated oppositely, each carrying one mirror at potential with the other
grounded, which is the ordinary basis decomposition and therefore exact rather than
approximate.

**A shear is the obvious spelling and is wrong twice over.** Laplace is not shear-invariant,
so a sheared field solves nothing; and a shear of a mirror pair translates both mirrors the
same way, which is a rigid translation of the instrument rather than a convergence.

**Recommend section 10 state the corollary to Amendment 5's requirement.** Sensitivity
fields need a boundary representation that varies continuously with the parameter - and
separately, **the perturbation's own signal must exceed the discretisation error of the solve
that will report it.** Where the perturbation is a rigid motion, applying it as a coordinate
transform on the solved field satisfies both conditions exactly and costs one wrapper class.
The general rule, in `docs/lessons.md`: compare the size of a small effect against the
solve's error as a *ratio* before reaching for a finer mesh, and treat a quantity that
changes sign under an irrelevant modelling choice as a measurement of that choice.

A28-style corollary for the record: this also makes each Astral mirror a **two-dimensional**
solve, so the device that motivated the volume solver no longer needs it for its mirrors, and
a solve plus a 15 microsecond flight went from about 26 seconds to 1.4.

### 35 - A volume solve could not declare a face a mirror, and the solver always could

**r06 §9's boundary conditions** are stated for a solve without distinguishing the plane and
volume paths, and the plane path has carried `rightEdge` from the beginning. The volume path
carried nothing: `DirichletMask3D` has all six faces as settable conditions and
`OperatorStencil3D` honours them, and **no document could ask for one**. The capability
existed and was unreachable - the same shape as `ITransportMode` named only in a csproj
description, and `drivePhase` a plain double until a travelling wave needed a ramp.

**A grounded domain boundary is a third electrode**, which this project has already
documented once. A stripe electrode running the length of an analyser's drift makes the field
independent of the drift direction, so grounding those faces imposes an axial field the real
instrument does not have. Measured on two rails spanning their domain in z: **-62,577 V/m of
axial field with the faces grounded, -0.0000 with them mirrored**, and with mirrors the
transverse field is identical to the digit at 5 mm and 20 mm from the face.

**Found by an ion going the wrong way**, not by a failing test: an Astral skeleton at a 3.5
per cent injection angle should drift at +1375 m/s and measured **-480**.

**Recommend r06 say the boundary vocabulary is the same in both dimensions**, since the
argument for it is the same and the omission was an accident of which path was written first.

### 34 - Every 3-D primitive is axis-aligned, and one real device is defined by not being

**r06 §9's shape vocabulary for a volume solve** is a box, a sphere and a cylinder along an
axis. The type's own remarks say why that is enough - "between them they build the devices
the specification's table asks for" - and add the escape clause, "a device that needs a
fourth is a fair reason to add one".

**The device that needs something is the asymmetric-track analyser, and it does not need a
fourth shape.** Its two ion mirrors converge by a couple of hundred microns over a third of
a metre, and that convergence is the **mechanism** rather than a tolerance: it is what makes
the drift decelerate and reverse, which is the whole behaviour the geometry exists for.
Every primitive being axis-aligned means a plate deliberately not parallel to another is not
expressible at any resolution - so such a model would validate, solve, converge, and produce
a drift that never reverses.

**What it needed was an attribute, not an abstraction**, which is the fifth time here: `log`
for the Kingdon trap, trigonometry for the multipole guide, `asinPi` for the C-trap, a
parametric `drivePhase` for the travelling wave, and now a tilt on a box. LIB-1 says to
believe the signal when a device forces a change below `Einzel.Library`; the signal each
time has been narrow and the abstraction has held.

**A tilted box is a box**, so signed distance and first entry rotate the query into the box's
own frame and run unchanged - a rotation is rigid, so a distance measured there is the
distance in the world, and affine, so a segment fraction is preserved exactly. The only
query that genuinely changes is the bounding box.

**Half turns, so a right angle is 0.5 and is exact.** The convention `cosPi` and a drive
phase already use, for the reason this project has now met three times: `Math.Cos(Math.PI/2)`
is 6.1e-17, so a nominally upright plate would be tilted by a rounding and a symmetric
geometry would carry an asymmetry made of floating point.

**Measured: the response is proportional to a thousandth of a cell.** On a 0.5 mm mesh, step
ratios of 2.0000 and 2.5000 for steps of 2 and 2.5 over a two-hundred-fold range, against a
parallel control reporting 7.1e-15 V. That is the cut cell doing what it was built for -
FLD-1's argument, met in a new place.

**And it found a degeneracy worth recording**: a conductor face lying *exactly* on the node
lattice makes the response affine rather than proportional, with an offset worth about
seventeen microns of convergence. A quarter-cell offset removes it entirely. The mechanism
is recorded as a hypothesis; the measurement and the cure are established. **Recommend the
model format's guidance say so** - do not place a conductor face exactly on a cell boundary
when the quantity of interest is a small geometric perturbation.

### 33 - GRD-8's estimate is about a machine and an operation, and was about neither

**r06 §GRD-8**: *"Any operation exceeding a configurable cost threshold requires a prior
estimate."* The requirement is right and the implementation answered a different question
twice over.

**It costed a model, and the operation is a study.** `einzel estimate` took a model file
and reported what one run of it costs. But nobody plans a multi-day job around one run -
they plan around a scan, a sweep, an optimisation or a boundary search, which is where the
hundreds of evaluations are. The gate was therefore short by the evaluation count, and
short *silently*, which is the worst direction for a number whose entire purpose is to
decide whether to start.

**The multiplier needs no pilot**, which is what makes this a defect rather than a
limitation: a study file **states its own extent** - a scan its points, a sweep its draws,
an optimiser and a bisection their evaluation ceilings. Costing it is arithmetic over
numbers already in the document.

**And the arithmetic is not the obvious one.** An evaluation solves the field once and
flies every ensemble member through it, so a study costs
`evaluations x (solve + members x flight)`. Multiplying the model's own total by the
evaluation count charges a whole solve per member - measured at **4.8x over** on the
shipped mirror pair at nine members.

**Second: an absolute time is a statement about a machine, which is Amendment 27 again.**
The rate was one hardcoded constant - 13 s per million nodes, measured on the 2-D
templates on one developer's box - and it was applied to volume solves too, where the
stencil is 27-point rather than five and the coarse levels are Galerkin. On the shipped
C-trap that put a 5.9 s solve at **1.81 s**. It is now measured by solving a coarsened
copy of *the model's own geometry*, which also captures the difference between a
boundary-value problem and one with interior electrodes.

**So GRD-8's estimate is not free, and r06's framing implies it is.** The requirement gates
on a number "available without doing the work", and a number that is *useful* on unseen
hardware cannot be: it costs one coarsened solve and one short flight. **Recommend GRD-8 be
restated** as requiring an estimate whose cost is bounded and stated, rather than one that
does no work - with the opt-out that keeps PERF-8's cold-start budget reachable
(`--no-calibrate`, which says in the basis line that nothing was measured here).

**A study that varies the geometry varies its own cost**, so the flight is sampled at the
ends of the study's own declared range and averaged - measured at **2.2x** across a mirror
separation scan, which an estimate taken at the declared values alone missed by 0.57x. The
sampling is gated on the study being long enough to absorb it: three samples cost about one
and a half evaluations, which is 7 per cent of twenty and under one per cent of the hundreds
a real optimisation declares.

**Sampling the fraction is what makes it wrong**, and getting that wrong first is worth
recording. A pilot flies part of a flight and scales up - and the only length available to
scale against is the declared *maximum* flight time, which is a ceiling rather than an
expectation. The nominal ion arrived inside the fraction and the extremes did not, so they
were scaled by the whole ceiling and the estimate came out **3.4x over**. A study samples
few points against many evaluations, so a sample can afford the whole flight at the real
cell size, and then nothing is extrapolated.

**Third: the mesh a solve gets is not the one the document asked for, and nothing said so.**
Each axis rounds its interval count up to a power of two, so cost is a **step function** of
the cell size and the node count is the product of three such roundings. On a
635 x 48 x 350 mm analyser a requested 1 mm gives 0.62 x 0.75 x 0.68 mm and **34.2 M nodes
where the request implies 10.7 M** - finer than asked on every axis, never coarser, and
**asking for 1.5 mm instead costs 7.9x less**. That is not waste and nothing about it is
wrong; it is a fact about the cost of a plan, and GRD-8's estimate is where it belongs.

**Fourth, and found by reviewing the above: an evaluation is not always an ensemble.** The
arithmetic assumes `members` independent ions through one solved field, which is the ordinary
case and not the only one. A **diffusive** run steps a density and a **space-charge** run
advances the whole packet in lockstep - in both, what the model estimate already covers *is*
one evaluation, flights included. Multiplying by the ion count double-counts, and for a
diffusive model it charges for trajectories that mode does not produce (TRN-2, RND-8) while
flying pilot ions through a model that has none.

**What remains excluded is stated on every estimate**: process start and just-in-time
compilation, being fixed costs that matter for a thirty-second scan and not for a multi-day
one. Measured on a mirror-separation scan - the adversarial case, since it sweeps across a focusing condition and evaluation cost varies 2.2x along it: **23.1 s estimated against 30.3 s of wall clock (0.76x), of which about 5 s is process start the estimate excludes by design** - so 0.89x of the computation itself. Run-to-run spread is 1 per cent.

### 32 - An exact analytic field cannot be one element of a beamline

Section 9 lists the field kinds a document may declare, and section 6's whole architecture
rests on superposition being exact for electrostatics - which it is. What neither says is
that an **analytic** element has no extent: it fills all space, because a formula does.

That is harmless while analytic fields are idealisations of a whole instrument - a uniform
field, a retarding half-space. It stops being harmless the moment one is an exact statement
of a real device that sits *next to* another device. The quadro-logarithmic field of an
orbital trap grows as `z^2`, so declaring it in the same document as the C-trap that
injects it puts an enormous field across the C-trap. The two instruments cannot be composed
even though the sequencer can express the handover and superposition is exact.

**Two solved elements compose correctly**, because each is bounded by its own domain and
decays outside it. So the gap is specific: an exact analytic field cannot be one element of
a multi-element beamline, and the exactness is precisely why anyone would want it there.

**And the obvious escape does not work, which is what makes this an amendment rather than a
task.** The natural answer is "declare the analyser as solved geometry instead, and let its
domain bound it". An orbital trap's electrodes are surfaces of revolution, and an
axisymmetric solve is exactly the tool - but the electrodes **are equipotentials of the very
field they produce**, so their profile satisfies

    -r^2 / 2 + Rm^2 ln(r / Rm) = A - z^2

which is transcendental in r: an `r^2` and a `ln r` in the same equation, invertible only
through Lambert W. The expression grammar has `sqrt` and `log` and no way to invert that,
and the 2-D shape vocabulary is rectangle, disc and edge profile - none of which is a curve
a document can name. So the analyser cannot be declared as geometry either.

The two facts together are the finding: **an exact orbital analyser can be modelled alone
and cannot be modelled beside anything.** Its field is unbounded and its geometry is
inexpressible, and those are the only two ways the format has of placing a device in a
document.

**Recommend section 9 give an analytic element an optional region** - a box outside which
it contributes nothing. That introduces a field discontinuity at the boundary, which is not
a difficulty: the integrator already lands exactly on declared discontinuities, and section
11 makes that a first-class event. What it needs is a decision about whether the region is
declared or inferred, and what happens where two regions overlap.

**Built, with a stated limitation.** An analytic element may declare a `region` — a box
outside which it contributes nothing — and two instruments now share a document. Measured:
an ordinary 1 kV/m accelerating section 75 mm from an orbital analyser feels **−1,499,000
V/m** of that analyser unbounded and **exactly its own 1,000** bounded; on the axis the
unbounded case is worse than swamping, since the model cannot be asked a question there at
all. Inside the region nothing changes, to the bit.

**The potential steps at the boundary, and that costs less than it looks like it should.**
A box is not an equipotential, so the potential does not match across it — and the first
account of this concluded that an ion crossing therefore gains or loses that energy. **It
does not.** An ion is moved by the *field*, which is exactly the declared one on each side,
so a bounded uniform field is an accelerating gap followed by a field-free drift: measured
at **13.658582 µs against a closed form of 13.658582**, with the unbounded control at
10.180506. What the step actually costs is that the energy-drift diagnostic jumps at the
boundary, and that the piecewise field is not conservative across it — an ion crossing more
than once, in by one face and out by another, can gain energy no electrode supplied. The
step is reported on every bounded element at severity `Qualified`, in volts and as a
fraction of the beam potential.

**The failure points straight at the next refinement.** A real device's field is bounded by
a conductor, and a conductor is an equipotential of the very field it produces. Bounding an
analytic element by one of its own level sets rather than by a box would make the potential
continuous by construction — offset so it is zero outside, with the field discontinuous
exactly as `halfSpaceUniform` already is, and the geometry exactly a real electrode. That
is what should replace the box, and it was not built here.

**And the unbounded case is now reported rather than silent, which is the other half.** A
`halfSpaceUniform` is the one analytic field whose extent a document already appears to
declare: it names a cap potential and a turning depth. It does not — the cap is what the
model says the plate holds *at* that depth, and the ramp continues past it. So an ion
carrying more energy along the normal than the cap turns round **behind** the plate,
self-consistently, in a region the document does not claim, with the flight time reported to
full precision either way. Found by an agent in the acceptance run that lowered a cap while
exploring a reflectron's focus and got an ion turning 5.6 mm inside the metal, with every
number arithmetically correct. `field.beyond-declared-depth` says where it actually turned
and against what cap, at severity `Qualified` for the same reason the region step is: the
model is exactly what its author wrote, and what is wrong is the reading of it. It is
measured at **three sigma of a declared energy spread** rather than at the nominal ion,
because while only the tail is past the plate the effect is a selective loss of the fastest
ions rather than a wrong flight time — the harder thing to notice, and the likelier to be
read as physics.

### 31 - A thermal cloud's divergence is not the divergence an ion-optics beam has

The model format carries `energyFractionSpread` with an argument written where it is
taken: it "varies the energy without varying the direction, which a temperature cannot
express". The mirror of that - varying the **direction** without varying the energy - was
deliberately left out, on the grounds that "a thermal cloud already has one, and offering
both lets a document say two things about the same physics".

**That reasoning is right for a source and wrong for a beam.** An ion born warm and then
accelerated does get its divergence from its temperature, in a fixed ratio. A beam defined
downstream by an **aperture** does not, and an einzel lens exists precisely to re-image
such a beam. Every ion-optics description in the wild specifies one that way: *"50 eV with
a 20 degree angular spread"* is a sentence the format could not represent.

**And a temperature cannot stand in for it**, which is what makes this a gap rather than a
verbosity. Matched to give the same divergence at 50 eV, a temperature spreads the energy
by **43%** - turning a 50 +/- 0 eV beam into a 50 +/- 15 eV one, which is a different
study's independent variable. Divergence and energy spread are separable in the instrument
and were not separable in the format.

**Recommend the format carry both, with the interaction stated rather than prevented.**
Schema 0.7 adds `divergence`: a cone half-angle, drawn **uniformly in solid angle**, not a
Gaussian - an aperture truncates rather than weights, and the rays pile near the rim where
the aberration lives. Measured against the closed form, mean cos(theta) comes out
0.969832 against 0.969846 for uniform solid angle and 0.979816 for uniform polar angle: 700
times closer to the right one. A tilt costs no energy exactly (4.5e-13 m/s on 4000), since
rotating a vector does not lengthen it.

**The third design decision this week that was right for its case and wrong for a new
one**, after `SessionJournal` refusing invalid edits and `CompiledDrive` allowing one drive
per solve. The pattern is worth naming: a constraint argued from the cases in front of you
is sound and is not a law. When a new requirement meets one, re-read the argument rather
than the rule - it is usually still true, and usually no longer sufficient.

### 30 - A manifest said what a result was made of and not what it was about

`PRJ-3` requires a run manifest to fully determine its run, and lists what that takes:
model hash, seeds, engine version, solver-behaviour version, transport mode, compute path,
extension identities, machine. **It does not ask for the model's path, and by that list it
does not need to** - the hash is what makes a result regenerable, and it survives a rename
where a path does not.

**Determining a run and identifying which model a result is about are different
questions.** With only the hash recorded, `einzel verify` had to answer the second by
searching for a file whose content still hashed to the recorded value. Two models may
legitimately hold the same content, and then:

- The result attaches to whichever file is found first, which is arbitrary.
- **Editing the model that was actually run makes its drift disappear.** The result
  silently re-attaches to the untouched twin, reports itself current, and the edited model
  reads as never run.

That second one is a stale result reporting as fresh, which is the failure direction the
whole verify mechanism exists to prevent. It is not exotic to reach: a project scaffolded
by `einzel init` and then given a corpus example of the same device is enough, which is
exactly how it was found.

**Recommend PRJ-3 distinguish the two.** A manifest carries what determines the run *and*
what identifies its subject; the hash cannot serve as both, because content is not
identity. `RunManifest.ModelPath` is now recorded, verify prefers it, and the hash search
remains the fallback for older manifests and for a model that has moved - where what is
reported is what can be observed (*the recorded path is gone, the same content is at X*)
rather than a rename, since content alone cannot tell a rename from a twin that was there
all along. Writing "renamed" there was the same mistake once more, in the fix; my own test
caught it.

The general form is worth more than the fix: **when one field is made to answer two
questions, the answers diverge exactly where the two questions differ** - here, wherever
two files hold the same bytes.

### 29 - Enumerating a requirement's own population is a different act from believing it

**GRD-2 names seven layers** - engine, command layer, CLI output, MCP response, exported
file, rendered figure, video. That is a finite, closed list, which makes the requirement
checkable in a way most are not: there is no judgement about coverage to be made, only
seven questions to ask.

**Asking them found two defects on the first pass**, and the register had been claiming
**Met** for both.

**The exported `.vtu` carried no warnings.** `VtuWriter.WriteTrajectory` and
`WriteDensityField` both take an optional `provenance` list; the density path had always
appended the run's warnings to it and the trajectory path had never done so. Same writer,
same parameter, one call site using it. This is the **seventh** time evidence about a
computation has been dropped at a seam here, and the sixth was the same file. A `.vtu` is
the artifact that travels furthest - opened in ParaView, months later, by someone who
never saw the envelope it came from.

**The rendered figure was worse, and it was not a dropped warning.** Asking whether a
warning reached the figure surfaced that the figure was not computing the same thing a
run computes: `SectionRenderer` and `AnimationRenderer` both integrated through
`TrajectoryIntegrator.Integrate`'s optional `collisions` parameter without supplying one,
so **a figure of a model declaring a gas drew the vacuum flight**. On the `thermalisation`
example the run has the ion reach **154.79 mm** and the drawn one reaches **2778.28 mm** -
eighteen times further, silently, on the artifact RND-11 exists to keep honest. The two
figures were byte-identical with and without the gas.

**The general shape, now seen three times with this one quantity**: an optional parameter
whose default is *a different physics* rather than an absence. Forgetting it produces a
plausible answer instead of a failure. The gas had already been found reaching the
figure-of-merit path and not the run path, and the regime inspector's own first draft.

**What the enumeration is worth is not the two fixes.** Both were in code that had been
reviewed, tested and documented as working. What found them was asking a question whose
answer had to be yes or no *per named layer*, rather than asking whether warnings
propagate - which is a question the whole system answers "mostly, yes" to, correctly, and
which conceals exactly the cases that matter.

### 28 - GRD-1's own exception was load-bearing, and nobody had counted what it cost

**r06's GRD-1 is absolute**: every quantitative result carries value, units, uncertainty,
ensemble size or convergence measure, and active warnings, and *the API offers no way to
obtain the scalar alone*. The absolutism is argued - a convenience accessor returning the
value would get added by someone and then used everywhere.

**The exception is also argued, and correct.** A sweep or an optimiser needs an ordering,
and an envelope has none, so `FiguresOfMerit.Evaluator` hands a driver a bare double. That
is written down where it is taken.

**What nobody had written down is the consequence.** Most figures were *only* reachable
through the exception: of the fourteen this build computes, **one** - the flight time, from
a convergence study - carried an envelope anywhere. There was no way to ask for a
turn-around time with an uncertainty on it, through the CLI, through MCP or at all. The
exception had quietly become the rule for everything except the one figure that predated
it.

**Why it went unnoticed is the instructive part.** Every individual number this project
publishes *does* carry an envelope, because they are computed by tests and studies that
build one by hand - the docs are full of "1.0127 +/- 0.0373" and "0.49% on 4000 ions". The
gap was in the *API*, and it took building the view whose whole purpose is displaying
envelopes to make it visible. A requirement can be honoured everywhere it is exercised and
still be unmet where nothing has asked.

**Recommend GRD-1 say that a figure of merit must be obtainable in enveloped form**, with
the ranking accessor an explicitly derived view of it rather than the only path. That is
the shape that makes the exception safe: the envelope exists and ranking discards it,
rather than the bare number existing and an envelope being added where somebody remembered.

**Closing it needed a mechanism rather than thirteen formulas.** Most of these figures are
statistics of an ion cloud and only the fractions have a closed-form error; a full width at
half maximum has none, and an arrival-time peak is measurably skew, so a Gaussian formula
would understate its own error. Resampling covers a width, a mean and a ratio alike and
assumes nothing about the distribution. **Two limitations came with it and are recorded in
`docs/lessons.md`**: the bootstrap is inconsistent for extreme-order statistics, which
matters because "the widest entry radius that still arrives" is one; and it must not be
applied to the deterministic energy sweep, which is a designed scan rather than a draw and
has no sampling uncertainty to report.

### 27 - PERF-7's 50 ms is the cost of starting CPython, not a budget the platform can spend

**r06 §PERF-7 puts a sandboxed extension round trip under 50 ms**, and makes that number
the granularity floor for EXT-4. The requirement is sound in intent and is not measurable
as written, because on an ordinary machine **the 50 ms is process start**.

Measured: launching the interpreter with `-I -c pass` and doing nothing costs
45.0, 49.6, 53.9, 58.2, 40, 51 and 63 ms across seven runs. The budget straddles that spread. So a test asserting
the round trip against 50 ms is asserting that CPython started quickly this time - and it
behaves accordingly: the old assertion **passed and failed on the same commit in two CI
runs minutes apart**, and on a shared build agent a bare launch takes *seconds*.

**What the platform actually controls is small and now measured.** A round trip costs
**1.08x to 1.52x** a bare interpreter launch across seven runs, and on one of them came in
*below* it - the marshalling, the schema check and the JSON on both sides are under the
noise floor of process start.

**Recommend PERF-7 be restated as a bound on the platform's own share** - the round trip
must not cost materially more than starting the interpreter - with the absolute figure
reported rather than asserted. That is scale-free, holds on a developer's machine and a
build agent alike, and measures the thing this project can change.

**Two things follow, and the first is the more important.** EXT-4 is *strengthened*: a
subprocess cannot be invoked per integration step because the boundary costs ~50 ms and
nothing here can reduce it, which is a structural argument rather than a measured
coincidence. And the open decision "whether the in-process runner is worth shipping"
gains its first real evidence: **the in-process runner is the only thing that could meet
PERF-7 as written**, since it removes the term that dominates. That is not an argument to
build it - nothing has yet needed a faster round trip - but the decision should no longer
be recorded as "the evidence says sandboxed-only is sufficient" without saying what
sandboxed-only costs.

**And a note on how this was found**, because it is the kind of thing a green suite hides:
the test had been passing for weeks with the comment *"a hard assertion here would be a
test of the build agent"* written directly above an assertion on the build agent. The
comment was right and the code did not follow it.

### 26 · Helix Toolkit is still the right choice, and its DirectX backend is archived

**r06 §16 names "Helix Toolkit on its DirectX 11 path" and gives the right reason** —
plain WPF Media3D cannot render 10⁴ trajectories interactively. §20 asks for third-party
status to be re-checked before being committed to rather than assumed, and doing that
found two things r06 could not have known:

- **The 2.x line contemporaneous with r06 is .NET Framework-only.** 2.27.3 restores on
  net10 only through the NU1701 compatibility shim. **3.1.2** has real `net8.0-windows`
  targets and restores clean with no fallback, so the version matters and the name alone
  is not enough.
- **Every Helix DirectX package depends on SharpDX, archived since December 2020.** The
  named path rests on an unmaintained project, and r06 records it as a plain choice.

**It is taken anyway, and the reason is §17's own boundary.** That section is emphatic
that the interactive viewport is *screen tuning, not an artifact*: the publication figure
comes from `Einzel.Render`, which is vector, headless and owes nothing to this dependency.
So the archived library is confined to a window, and its failure mode is that the window
stops working — not that a figure cannot be produced. Nothing that leaves Einzel passes
through it. LIC-1 is verified rather than assumed: MIT from the embedded licence file, and
the transitive closure is MIT throughout.

**Recommend §16 record the version and the SharpDX position**, so that a later reader
finds a decision taken with open eyes rather than a name that has quietly stopped meaning
what it did. The exit, if it is ever needed, is bounded by the same boundary: replacing a
viewport backend touches the shell and nothing else.

**A second, smaller finding in the same place: WPF cannot run in globalization-invariant
mode.** `Directory.Build.props` sets `InvariantGlobalization` for the whole solution, for
CLI-5's deterministic output; WPF's font cache constructs `new CultureInfo("en")` while
measuring the first line of text and the window dies before it is shown. The shell
reverses the setting, and what it was protecting is unaffected — this codebase achieves
locale-independence by passing `CultureInfo.InvariantCulture` explicitly at every
formatting and parsing site, and the build flag was the belt to those braces. The parse is
the one that matters: a value typed into the model tree is read invariantly whatever the
host locale, because the file being edited is invariant.

---

## The shell, and the rest of §16

**The register is tag-driven, and §16 carries one tag between eleven required
views.** That is a property of the original document rather than of the shell's
importance, and reading the register alone would leave the impression that the
GUI is a footnote. It is not: §16 is a section, §17 is written largely around what
the shell must *not* own, and AGT-2 — the invariant that nothing exists only in
the window — is the load-bearing claim that the shell is a peer rather than the
product.

**It is a named deliverable, and the toolchain was chosen partly for it.** The
Windows GUI capability was part of the reason for C#; r06 names WPF and Helix Toolkit
on DirectX 11 and does not record that rationale, which Amendment 25 adds. What is
wanted is **interactive geometry, the solved field drawn over it, and animation** —
outcomes, not contingencies.

**The thesis is the pair.** An agent drives the entire design process through the CLI
and MCP; a human sees and manipulates the same design in a window. Those are one
requirement rather than two because of AGT-2, and Amendment 25 strengthens it: every
shell action should be *expressible* as a CLI invocation and journalled as one, so a
human's session hands over to an agent and back in the same vocabulary.

**Seven of the eleven views exist**, and the window opens on a model: `einzel-shell
models/reflectron.json` gives a parameter tree, a trajectory bundle coloured by energy over
the drawn instrument and field, the journal, results grouped by §12's accuracy class, the
regime along the path, and the declared timeline. `ShellSession` holds one model, the shared journal, and every
action recorded as the `einzel` command that would reproduce it (Amendment 25).

**Five times now a view could not be built until a command existed**, which is Amendment
25 working rather than an obstacle it created — and every time the command layer gained the
capability rather than the window keeping it. The model tree needed `OutlineCommand`; the
viewport needed `ViewportCommand`; the results view needed `AccuracyClass` on the figure
registry, because §12's taxonomy was recorded nowhere; the regime inspector needed
`RegimeDiagnostics.MeasureAt`, because the numbers had only ever been computed at the worst
point in the gas; and the sequence editor needed `SequenceCommand`. An agent is better off
for each, which is the strongest evidence yet that AGT-2 is real rather than aspirational.

**And one of them found a hole in a load-bearing requirement.** Building the view whose
whole purpose is showing GRD-1 envelopes revealed that only one of fourteen figures had
one — see Amendment 28. A requirement can be honoured everywhere it is exercised and still
be unmet where nothing has asked.

**And the shell compiles on Linux**, which was an open bet and is now measured rather than
assumed: `EnableWindowsTargeting` is enough, XAML markup compilation included. It does not
run there and is not meant to. Only `Einzel.Wpf.Tests` is Windows-only, and on another host
it builds as an ordinary `net10.0` assembly with no sources, so a solution-wide
`dotnet test` walks past it.

**There are two shells now, and the second one runs where the engine runs.** `Einzel.Shell`
is the same §16 views on Avalonia and an OpenGL viewport, targeting plain `net10.0` and
referencing `Einzel.Commands` and nothing else - so a user who asked for the platform
outside Windows gets a window there, and the WPF shell is untouched. **Four of the eleven
views are across**: the 3-D viewport, the model tree, the field as equipotentials, and a
live run watched while it steps. The other seven are presentation over commands that
already work, which is the same thing this section says about the WPF shell's own remaining
rows.

**This is Amendment 26's bet being called, and it paid.** The Windows-only decision was
recorded as a decision with a condition on it - "revisited if the need appears" - resting
on invariant 1 and Amendment 25 to make a later cross-platform shell a swap of a
presentation layer. It was: 2,167 lines, no assembly below the shell changed, one file
ported verbatim. See Amendment 53. What is new rather than ported is that
`Einzel.Shell.Tests` targets plain `net10.0`, so for the first time a shell's own tests run
on both runners - a cross-platform claim checked only on Windows is one nobody is
checking.

**Both invariants are enforced by tests from the first commit**, which is the point of
building the scaffolding before any view. `NothingBelowTheShellReferencesIt` scans every
platform assembly beside the Linux-running test project - an invariant only ever checked
on a developer's Windows box is one already broken - and `TheShellReachesThePlatform
ThroughTheCommandLayer` checks that the shell declares a reference to `Einzel.Commands`
and to nothing else in the engine.

**That second test had to check two different things, and the difference cost a
mutation.** `GetReferencedAssemblies` reports what the *compiler emitted* - what the code
uses - so adding a `ProjectReference` to the whole transport engine left no trace in the
metadata and the test passed. UI-1 is about what the shell may reach *for*, not what it
has reached for so far, so the project file is now checked too. Only the declared-
reference check catches that mutation.

Every view §16 requires:

| View | State | What it needs beyond a window |
| --- | --- | --- |
| 3D viewport — geometry, potentials by colour, equipotentials, trajectory bundles | **Built** | Geometry, the field, and the bundle, on Helix Toolkit 3.1.2 / DirectX 11. **Every conductor is the zero level set of its own signed distance** (invariant 2), so one routine draws them all — and what differs between symmetries is what the solve claims about the third dimension: a cross-section extrudes (uncapped, because the electrode really does extend past what is drawn, with the depth named as a drawing convention per GRD-12), an axisymmetric half-plane revolves, a volume is extracted by surface nets. Checked against closed forms: a sphere's volume 0.99038 / 0.99760 / 0.99940 under refinement, every edge shared by exactly two triangles, normals exact to 1.000000, a revolved tube against Pappus to 0.99990. Equipotentials on the section plane rather than as surfaces, because a nest of closed surfaces hides the trajectories. Two colour scales, both anchored once across everything drawn — viridis for energy, and a **diverging ramp symmetric about zero** for potential, because earth is what every other potential is measured against and a ramp stretched over the observed range puts the neutral colour at 250 V for a lens holding 0 and 500. RND-8 withholds the paths and not the instrument. Helix's status is Amendment 26 |
| Density clouds instead of trajectories for diffusive regions (TRN-2) | **Built** | The viewport draws the density as **nested shells at decades below its peak** - reusing the marching-squares contours the section already draws and the same extrude-or-revolve rule the conductors follow, so a cross-section repeats along z and an axisymmetric half-plane is a solid of revolution. RND-8 on its own is entirely negative: it forbids lines through a diffusive region and, alone, leaves an empty box for the whole pressure range the mode exists to cover - and an empty box is indistinguishable from a model that lost everything. **Drawn at an instant, and the end of the run is the wrong one**: the shipped drift tube collects 9,999.76 of 10,000 ions and leaves 1.8e-302 behind, so anchoring to the end draws nothing for exactly the models that work. The instant is chosen as the middle of those that still hold a packet, is reported rather than implied (GRD-12), and the caller may name its own - which is also the seam animation scrubbing needs. Measured on the corpus drift tube: three shells at 4.555e7 / 4.555e6 / 4.555e5 per m3, nesting outward at 208 / 260 / 292 vertices, the densest spanning **9.7 mm of a 42 mm tracked region centred at 35.2 mm** - a packet three-quarters of the way down the tube rather than a uniform gas |
| Figure composer | **Seam built** | `RenderSpec` is already text in `figures/` that the CLI executes. A composer edits one of these and nothing else — which is UI-1's own test, and the reason it can be built last |
| Animation timeline, per-phase playback rates, scrubbing, frame export | **Partial** | Per-phase playback rates and frame export are built: `einzel render animation` on a declared mapping, with the rate stamped on every frame and a `frames.json` schedule beside them. Scrubbing is a shell interaction and needs the window |
| Model tree with parameter editing, live validation, units on every field | **Built** | `einzel outline` returns the declared surface - value, unit, bounds, description, what it resolves to in SI, and whether it is editable - because UI-1 forbids the shell from parsing the document to build a tree. A verb rather than a shell method (AGT-2), so an agent gets the same service. Every edit goes through the shared journal, so a change in the window is undoable by an agent on the same session. **Delivering it reversed a guard**: `SessionJournal` refused any edit that did not validate, which makes live validation impossible - a person typing 500 into a parameter bounded at 50 must see the tree with the complaint on it, and refusing every invalid document forbids any edit *sequence* that passes through one. Narrowed to refusing what does not *parse*, which is taint-never-block applied to input. `docs/lessons.md` |
| Sequence editor | **Partial** | `SequenceCommand` reports the declared timeline: phases in order, the transport mode of each, and what every electrode holds - marked against the phase before, because a sequenced instrument repeats most of its state and a table repeating every setting buries the rows that change. Bars proportional to duration, since a 2 us hold beside a 100 us flight is the shape of a pulsed extraction. Two things a reader would otherwise assume wrongly are stated: the last phase **holds** after the sequence ends, and a phase changing the mode is SEQ-1's conversion boundary. **It shows rather than edits** - a sequence is a block in the document, so editing one goes through the same journal every other change does, and what is missing is the input surface rather than the path underneath it |
| Results by accuracy class, uncertainty and warnings never behind a disclosure control | **Built** | §12's taxonomy was recorded nowhere in the code and is now on the figure registry - six Class T, four Class S, seven Class B, and two deliberately in none, since `flightTime` is the raw arrival quantity the Class T figures are computed *from* and `energyDrift` says in its own description that it is a diagnostic. Every part of the envelope is a line rather than a tooltip, which is the requirement. **Building it found the GRD-1 hole below**, and closing that took the figures carrying an envelope from 1 of 14 to 5 |
| Regime inspector | **Built** | REG-2's numbers *along the path*, which is what §16's word "along" asks for and what a run does not give - a run reports the worst point anywhere in the gas, right for a warning and useless for deciding what to change. Violations are located as stretches in millimetres rather than counted. On a hundredfold density ramp the two ends differ by Kn **4.17 against 0.042** - free-molecular at one end, a continuum at the other, in the same instrument |
| Project view with model-drift and engine-drift state | **Built** | `einzel project` - the models, studies, figures, tests and extensions, with each model in one of four states. The drift itself is `einzel verify`'s, which already separates what invalidates a result from what merely annotates it; **what verify cannot answer is what has never been run**, since it walks the manifests and a model with no result is reported by neither its success nor its failure. That is the state most models in a working project are in. Building it found a defect in verify - see Amendment 30. |
| Extension manager | Not built | **Its engine half is done.** The manifest now carries `licence` beside trust level, versions and compatible range, and `einzel ext list` surfaces it - so what the view needs from below it exists, and building it is presentation. LIC-2's remaining half is the pane itself. An SPDX identifier by convention rather than by validation: a checker that recognised some spellings and not others would report an unrecognised licence as no licence, which is the failure the field exists to prevent |
| Journal with agent and human attribution | **Built** | `SessionJournal` in `Einzel.Commands`, rendered by the window beside the model tree, with the same entries an MCP client writes. A person sees what an agent did to their model, by name, and can undo it - which is MCP-1 and GRD-9 arriving where they were always aimed. Beneath it the same actions as `einzel` command lines (Amendment 25) |
| Update notice with UPD-3's deferral options | Not built | Needs the whole of §18 |

**The pattern in that table is the interesting part.** Almost every row is
"presentation over something that already works" — which is what AGT-2 is supposed
to produce, and is weak evidence that it has. The rows that needed genuinely new
capability were the 3D viewport's raster path, which is now built, and the animation
timeline's scrubbing, which is not.

**AGT-2 is now tested against three surfaces rather than claimed.** Every MCP tool returns
`CommandJson.Write` of the same outcome record the CLI serialises for `--json`, compared
byte for byte; and every shell action is recorded as the `einzel` invocation that would
reproduce it, asserted by test for the viewport and the tree alike. An invariant checked
against one surface is one that has already been broken by the time anyone notices — that
is no longer the position here.

**What the window found that the other two surfaces could not.** Every new command was
written because a view needed it, and each improved the CLI: `einzel outline` gives an
agent a model's knobs without parsing the document, `ViewportCommand` enforces RND-8 by
asking `ITransportMode.ProducesTrajectories` rather than the pressure, and `einzel project`
reports the state of a whole folder including the models nobody has run — which `verify`
walks the manifests and so cannot see. That is AGT-2 running in the direction it was not
designed for — the window pulling capability *into* the command layer rather than
accumulating it privately.

**Three defects were found the same way**, each by a view asking a question no test had:
the density cloud found the viewport anchoring to the end of a run, which is empty for
every model that works; the project view found `verify` identifying a model by content, so
that editing the model that was run made its drift *disappear* onto an identical twin; and
the GRD-2 enumeration found a rendered figure that was not flying the declared gas at all.
None was a failure of the code under test — each was a question nobody had thought to ask
until something had to display the answer.

**And a third thing the window found, in the core rather than in itself.** Extracting a
conductor's surface needed the electrode's own bounding box, and there was no way to ask for
one without switching on the shape — which is exactly what invariant 2 forbids, and which
would need a new case in every caller when a fourth shape arrives. `CompiledElectrode3D.Bounds`
now sits beside `Centre` and `CharacteristicSize`, in the one file that already owns those
cases. The defect that forced it is the instructive part: sampled over the whole solve domain
at 48 cells, **a 1 mm plate is thinner than a cell and produced no surface at all**, with
nothing said.

**UI-1's prohibition is the half worth protecting.** The shell owns layout, input,
the interactive viewport and the update check, and owns no physics, no validation
rules, no file format knowledge and no render output. Two of those are already
guarded structurally: `Einzel.Render` sits below any shell and is exercised
headlessly in CI (RND-1), and the figure composer edits a spec the CLI executes.
The one to watch when a shell is written is **validation** — a live model tree
wants to check a field as it is typed, and re-implementing "is this a length?" in
the window is the obvious way to do it and the wrong one.

**Scope, which §22 names as a standing risk.** "Scope creep into building a
visualization application. The pull is constant and each step is individually
reasonable." The risk is real and the guard against it is **UI-1's prohibition**, not
deferral: the shell owns layout, input, the interactive viewport and the update check,
and owns no physics, no validation rules, no file-format knowledge and no render
output. §17's boundary is what keeps the pull bounded — what leaves Einzel is a vector
figure, a VTU file and ParaView, and the viewport is for *working*, not for
publishing.

**What not having a shell has cost so far is one thing, and it is not nothing:
AGT-2 is untested.** Every other invariant here is checked by something. That one is
checked by nothing, because there is no second surface — and the cheapest second
surface is MCP, not the window.

## The requirement register

Statuses are assigned against the documentation and the code, not asserted from
intent. **Met** means there is evidence, and the evidence is named. **Unverified**
means the thing plausibly works and nothing measures it — it is not a synonym for
met.

The requirement column is abridged from the r06 HTML, which remains authoritative
for the full wording; a few statements run into neighbouring text where the tag sits
in a table.

### Accuracy (§8)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `ACC-1` | Numerical flight-time error, full analyzer ≤ 1 ppm 1/25 of the R = 20k peak-width budget | **Met** | 1e-10 relative on the analytic reflectron; 1.3e-13 between the analytic and solved paths. |
| `ACC-2` | Same, high-resolution validation mode ≤ 0.25 ppm To reproduce published R = 80k results | Not built | No separate high-resolution validation mode exists. Analytic cases already clear 0.25 ppm; nothing selects a tighter tier. |
| `ACC-3` | Field interpolation contribution ≤ 0.5 × | **Met** | Tricubic enforced; a forbidden interpolant is refused on a trajectory path. Bilinear measured at 9.4e-6 against bicubic 6.4e-8. |
| `ACC-4` | Energy drift, static field ≤ 1 ppm Cheap conserved-quantity diagnostic | **Met** | 1e-9 to 1e-15 in static fields. Reports NaN in a driven field, where energy drift is not a diagnostic. |
| `ACC-5` | Class S transmission interval ≤ 1% abs, 95% Drives minimum ensemble size per point | **Met** | Losses itemised by the surface name the author wrote; checked against erf for a slit at 0.95 sigma on 20,000 ions. A transmission of **zero** is now expressible - see Amendment 15, where it was not. **And where the population is continuous, "arrived" needed a floor** rather than being non-empty: Scharfetter-Gummel's flux across a collecting face behind a barrier *is* the Boltzmann factor of that barrier, so a held packet emits a stream of values hundreds of orders below one ion from its first step, and the TIMS front end reported a mean arrival and a spread in microseconds over 7.74e-245 of them. Arrivals must now reach a millionth of the launched population, or the arrival figures are absent with `sequence.nothing-eluted` saying which and why - a fraction rather than a count, because "less than one ion arrived" is a real answer for a low transmission while 1e-245 of one is not an answer at all. See Amendment 45. Diffusive collection is restricted explicitly to an aligned grid face; an internal detector is refused rather than silently moved (`AnUnrepresentableDiffusiveDetectorIsRefused`; Amendment 47). |
| `ACC-6` | Class B boundary resolution ≤ 1/500 of scan Enough to resolve a mass filter peak shape | **Met** | `einzel boundary` bisects onto the crossing and reports it as an envelope whose interval **is** the bracket. Measured: a step at a known value bracketed to 1 part in 512 in 11 evaluations, against 501 for a grid; the quadrupole low-mass cut-off at **q = 0.90508 +/- 0.00039** against a tabulated 0.90804. The search now also **walks outward from its converged bracket** looking for the predicate flipping back, which is the one thing bisection structurally cannot see - every step of its own path is consistent with a single crossing by construction. `boundary.multiple-crossings` is a validity violation; the confirmation is reported whether or not anything was found. |
| `ACC-7` | Rendered geometric tolerance ≤ 0.1% of extent Default decimation bound for vector output; recorded per | **Met** | Ramer-Douglas-Peucker measured tight against its bound: 4,000 points to 577 at a worst deviation of 0.010000 mm against 0.01. |

### Agent instructions (§3)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `AGD-1` | einzel init writes an AGENTS.md containing both layers, the first generated and clearly delimited. Generate, never hand-write, the platform layer ... | **Met** | `einzel init` writes AGENTS.md with a generated, delimited, version-stamped platform layer; `einzel agents-md` regenerates it. |

### Agent-native design (§5)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `AGT-1` | The model is text Declarative, schema-validated, diffable JSON. A model file plus referenced artifacts fully determines a run. | **Met** | Schema-versioned JSON, currently 0.14. `einzel schema` generates the JSON Schema by reflection over the document records. An unrecognized property is **refused**, not ignored - see Amendment 14. |
| `AGT-2` | Nothing exists only in the shell Every capability reachable from the window is reachable from the CLI and from MCP, through the same command objects. This now explicitly includes rendering (§17). | Partial | **Both other surfaces now exist, and the invariant is asserted rather than argued.** Every MCP tool returns `CommandJson.Write` of the same outcome record the CLI serialises for `--json`, compared **byte for byte** by a test; every shell action is journalled as the `einzel` invocation that would reproduce it (Amendment 25), and twice a view could not be built until a command existed - `einzel outline` and `ViewportCommand` - which is that amendment running in the direction it was not designed for. **Partial for one precise reason**: the requirement names rendering explicitly and the MCP tool surface does not expose it. That surface is deliberately not a second CLI (`model_read \| model_edit \| model_undo \| session_journal \| model_validate \| model_preview`), so the question is whether rendering belongs in it or whether the requirement means reachable *through a command object* rather than *through a tool*. Until that is settled this cannot be called met. The earlier evidence here read "neither MCP nor the shell exists", which stopped being true when both were built. |
| `AGT-3` | Errors are recovery instructions Machine-readable code, offending path, violated constraint, observed value, suggested correction. | **Met** | Code, JSON Pointer path, constraint, observed value, suggestion, severity. Validation collects every error rather than throwing on the first. **A mesh too large to solve was the exception**: an argument exception deep in the solver, which the CLI can only print as `INTERNAL_ERROR` - "a defect in einzel, not in your model" - on exit 6, after `validate` had passed the model. It is now `GRID_TOO_LARGE` from validation, located at `/fields/N/solve3d/cellSize` (or `/transport/spaceChargeGrid/nodes`), with the observed node count, the limit, and a suggestion naming the finest cell size that fits - printed with as many digits as it takes to stay on the same mesh step and **checked after the round trip through its own text and unit**, because three fixed figures crossed a second power-of-two boundary on the Astral (1.47 mm for 1.46484, half the nodes) and an exact decimal can land a rounding below one (2.75 mm over 22 mm is 8.000000000000002 intervals). `GridTooLargeSurfaceTests` pastes the suggestion back into the document and checks the mesh it names; `VolumeMeshTests` springs both traps. **And a command that validates on its way to something else reports every error, not the first**: twelve call sites - `estimate`, `export`, `preview`, `compare`, `render` and others - threw `Errors[0]`, so a refused mesh could hide behind an earlier mistake for a whole round trip; `EinzelException` now carries the list and the CLI prints it (`EstimateReportsEveryErrorNotOnlyTheFirst`). A derived cell size or bound that is NaN or infinite is refused as `VALUE_OUT_OF_BOUNDS` at its field, where NaN had crashed `validate` itself and infinity had solved on two intervals an axis. |
| `AGT-4` | Results carry their own uncertainty See §4. No quantitative result is ever returned as a bare number. | **Met** | GRD-1 enforced by reflection over the public surface of `Measured`, verified by injecting a violation and watching it fail. |
| `AGT-5` | Feedback loops are cheap A preview tier returns in seconds and is permanently labelled. | **Met** | `einzel preview` is 9 ms against a full run on the shipped reflectron, tainted on the number itself, and writes nothing. |
| `AGT-6` | Feedback is visual where that helps Rendered images an agent can inspect. Geometry errors are obvious in a picture and invisible in JSON. | Partial | `einzel render section` produces SVG and PDF headlessly. There is no raster path, so an agent cannot get a picture of a 3D geometry. |
| `AGT-7` | The platform is self-describing at runtime Schemas carry descriptions and units on every field; templates, extension types, and examples are enumerable. | **Met** | Schema descriptions come from the XML doc comments the build already requires; missing XML degrades to structure and says so. |
| `AGT-8` | The environment is stable within a session Nothing about the installed platform changes while an agent is working. See §18. The error object { "code" : ... | **Met** | The CLI makes no network call in any path. Only the shell would check for updates, and there is no shell. |

### Command line (§15)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `CLI-1` | Every command emits structured output. A --json flag on every verb produces machine-readable output including the full | **Met** | `--json` on every verb. |
| `CLI-2` | Results on stdout, progress and diagnostics on stderr. | **Met** | Results on stdout, diagnostics on stderr, driven through `Program.Main` in the end-to-end tests. |
| `CLI-3` | Exit codes are meaningful and documented. Distinct codes for validation failure, regime violation, cost-gate refusal, convergence failure, engine-pin ... | **Met** | Distinct exit codes per failure class, including a regime violation getting its own code rather than a convergence one. A volume mesh past the node limit exits 1 as the validation failure it is, where it exited 6 as an internal error. |
| `CLI-4` | Every mutating command supports --dry-run . | **Met** | `--dry-run` on every mutating command, asserted to write nothing. |
| `CLI-5` | Output ordering is deterministic. | **Met** | Deterministic output ordering. |
| `CLI-6` | Startup is fast and offline. No command touches the network unless the user explicitly asks it to. Verb Purpose einzel init Create a project; --vcs git ... | **Met** | Cold start 73-147 ms against PERF-8 500 ms, with no network call in that path. |

### Compute (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `CMP-1` | The scalar reference implementation is never deleted or allowed to rot. Collisions and space charge Model Regime Used for Mobility-based, no discrete ... | **Partly met** | The pair sum has two paths, and the scalar one is **selectable** rather than merely retained (`CoulombInteraction.Kernel`), which is what "never allowed to rot" has to mean: a reference nothing can run is a reference nobody can check. They are compared over every size that exercises a different part of the loop, with members inactive, with a pre-loaded accumulator, and against Newton's third law, agreeing to 3e-15 of the acceleration scale - and a deliberate mutation fails 10 of 16. The vectorised path runs at 434 Mpair/s against 140 scalar at 240 macroparticles. The packet inner loop now allocates **nothing** (504 bytes/step before). `Einzel.Compute` still does not exist: one kernel does not need a dispatch layer, and the GPU path PERF-5 needs is what would justify one. See Amendment 38. Diffusive coefficient/face buffers are now reused too: `CoefficientReuseTests` checks exact agreement with fresh assembly, with measured allocation reduced from 7.47 MB to 3.4 kB per 513 x 65 rebuild (Amendment 49). |

### Collisions (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `COL-1` | Scattered ions remaining within acceptance are tracked to the detector with arrival times recorded, not discarded as losses. | **Met** | Scattered ions inside acceptance are tracked to the detector with their arrival times, not discarded. |

### Distribution (§18)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `DST-1` | Windows: a per-user installer requiring no administrator rights, so it works on a locked-down instrument PC, published as an asset on the GitHub release. | Not built, and **deferred indefinitely** | The portable zip needs no installer, and r06's rationale for this one - a locked-down instrument PC - is speculative rather than observed. The reason the assets are self-contained is the one that survives: somebody who wants to model an ion optic should not first have to install a .NET SDK. Together with the twelve UPD rows this is thirteen of the unbuilt requirements, all resting on the same unobserved need. |
| `DST-2` | Portable zip alongside, for machines where installing is not permitted. | **Met** | `einzel-cli-win-x64.zip`, 37 MB, and `einzel-shell-win-x64.zip`, 80 MB, both self-contained: they carry their own runtime, so nothing is installed and nothing has to be. The shell is a separate asset because it brings the WPF desktop runtime and is two and a half times the download; somebody running an agent loop on a build server should not pay for a window they will never open. Verified end to end by a dispatch run of the release workflow. |
| `DST-3` | Linux: a tarball, engine and CLI only. No installer and no silent updater. | **Met** | `einzel-cli-linux-x64.tar.gz`, 35 MB, the command line and the MCP server, self-contained. No installer and no updater exist for it to have, and `PORTABLE.txt` inside the archive says so along with UPD-2 and UPD-9's guarantees that it never contacts the network. |
| `DST-4` | SHA256SUMS.txt published with every release. | **Met** | One file, computed in a single job over the assets as they will actually be published, rather than per-job and concatenated - sums generated beside each build would be sums of files nobody can prove are these files. |
| `DST-5` | Releases are built by CI on a version tag. Nothing distributed is ever built locally. | **Met** | `.github/workflows/release.yml`, on `v*`. The tag is the human decision; everything after it happens on a clean checkout. The suite runs on both platforms as the gate (the CI workflow does not trigger on tags), and then **the published binary is run before it is shipped** - `--version`, `doctor`, `init`, `run`, `test`, `schema` - which checks the thing being distributed rather than the thing being built. The release is created as a **draft**: a tag says build this, and publishing binaries is a separate act by somebody who has looked. A malformed tag is refused rather than stamped onto an asset (three guards: shape, prerelease suffix, component count). |
| `DST-6` | The vendored Python runtime ships in the installer and the tarball. | Not built | EXT-6's interpreter is *discovered*, not vendored, so there is nothing to ship. `einzel doctor` says which interpreter it found and that it is not vendored, rather than passing it off. |
| `DST-7` | Optional external tools are detected, never bundled : the video encoder ( | **Met by construction** | Nothing is bundled, and nothing could be: LIC-1 forbids a GPL dependency in the default build, so ffmpeg and Gmsh are invoked out of process where they are used at all. `render animation` writes numbered vector frames and a schedule rather than a video, and assembling them is an out-of-process step with a tool the user supplies. |

### Examples corpus (§5)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `EX-1` | Ship at least thirty validated reference models spanning every device class, each with a prose description, expected results, and assertion tolerances. | **Met** | **39 against the thirty asked for**, spanning free flight, accelerating gaps, reflectrons, an orthogonal accelerator, a thermal source, an einzel lens, a DC and an RF quadrupole, a hexapole guide, a funnel, a travelling-wave guide captured and ballistic, an extraction trap, a 3-D Paul trap held and ejected, a linear ion trap held and ejected, a sequenced extraction, a driven and an undriven RF quadrupole in the diffusive mode, three drift tubes including an imported pressure gradient, a slit, a thermalisation, and a three-dimensional parallel-plate gap. **Every expectation is arithmetic, a published value, or an exact invariant** - never a number this engine produced and then had enshrined - and each carries a relative tolerance. EX-2's gate runs all of them on every change. The count had been recorded as "31 of the thirty" and was stale. |
| `EX-2` | The corpus runs in CI; a failing example blocks release. | **Met** | `ExampleCorpusTests` materialises every example into a real project and drives `einzel test` through `Program.Main`. **31 of 31 in 48 s**, so it is affordable on every change rather than at release. It also asserts that every example ships a test and describes itself, and it materialises an example's data files beside its model - which is what lets an imported gas field be covered by the gate at all. |
| `EX-3` | Examples are enumerable and fetchable from both surfaces. | **Met** | `einzel examples` enumerates and prints, `einzel new --from-example` writes the model **and its test** with the model reference rewritten to wherever the file landed, and `catalog_list` / `catalog_read` do both over MCP. **The second surface was the gap and it had been closed-able since MCP-1 was built** - the row read "there is no second one" long after there was, which a register audit found. The tools are thin wrappers over the same `CatalogCommand` the CLI drives, which is AGT-2's mechanism rather than a parallel implementation. It matters more than the count suggests: an agent has no Einzel forum posts or example files in its training data, so shipping models it can pull into context is the whole counter to that, and an agent on this protocol could not reach any of them. |

### Extensions (§12)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `EXT-1` | An extension declares type, schemas, trust level, resource needs, and a compatible engine version range . The runtime is an implementation detail of the ... | **Met** | The manifest declares type, schemas, trust level, resource needs and a compatible engine range. `trust` defaults to sandboxed rather than being opted into. |
| `EXT-2` | In-process (CSnakes) for first-party and explicitly trusted extensions. Lowest latency, no isolation. | Not built | The in-process CSnakes runner is not built. Section 23 leaves open whether it is worth shipping at all; sandboxed-only has so far been sufficient. |
| `EXT-3` | Sandboxed subprocess for anything agent-authored or third-party, and the default. Job objects and a restricted token on Windows, namespaces and seccomp on ... | Partial | The subprocess boundary is real: wall-clock timeout with process-tree kill, output ceiling, zero inherited environment, `python -I`, scratch working directory. **Network, filesystem and memory confinement are not enforced** - `extension.isolation-incomplete` is a non-suppressible violation on every sandboxed result. |
| `EXT-4` | Never invoked per integration step. One call per run. | **Met** | Structural rather than advisory: a subprocess cannot be invoked per step at any useful rate. **Strengthened rather than weakened by Amendment 27** - the round trip is process start almost in its entirety (the platform's own share is 1.08x to 1.52x a bare launch, once *below* it), which is exactly why per-step invocation is impossible rather than merely discouraged. |
| `EXT-5` | Large arrays cross by shared memory with an Arrow or raw-buffer layout, never by JSON. | Not built | Large arrays still cross as JSON. No shared memory, no Arrow layout. |
| `EXT-6` | A vendored interpreter ships with the application. | Not built | An interpreter is **discovered**, not vendored. `einzel doctor` says so rather than passing it off. |
| `EXT-7` | Outputs are attributed per | Partial | A deliberate JSON Schema subset - type, required, properties, items, enum, numeric bounds - because a full implementation would put remote `$ref` resolution inside a sandbox whose point is having no network. Unrecognised keywords are ignored rather than refused. |
| `EXT-8` | Before an update is applied, the updater reports which installed extensions fall outside the new engine's compatible range. The cleanest extension of all ... | Not built | Needs the updater, which does not exist. |

### Field subsystem (§10)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `FLD-1` | For each perturbation channel p , cache ∂Φ/∂p by finite difference over a full re-solve. | **Met, with a stated floor** | Cached shape derivatives, validated to 6.5e-6 of the closed form at a 0.11-cell step. **Only after cut cells**: the first spike failed, see Amendment 5. **And cut cells are necessary rather than sufficient.** A perturbation whose own signal is below the solve's field error cannot be differenced for at all - the Astral's 200 micron mirror convergence is an anisotropy of 2.9e-4 against roughly 0.4% of second-order field error, and the solved answer ranged over 3.54, 0.011 and -0.57 of the closed form on gap width alone. Where the perturbation is a **rigid motion** it can be applied as a coordinate transform on the solved field instead, which is exact: 5.4e-20. Two boundary kinds also matter and only one has cut cells - a metal-to-metal edge has Dirichlet nodes both sides and no vacuum node to carry a sub-cell crossing. See Amendment 36. |
| `FLD-2` | Every sweep runs a stratified validation subset; if the maximum residual exceeds | **Met** | The residual is an ordinary Taylor remainder, quadratic in the perturbation to three figures. The limit is (delta/L)^2, so 1 ppm holds to delta/L about 1e-3. |
| `FLD-3` | Field caches are keyed by content hash over geometry, mesh, symmetry declaration, boundary conditions, and the solver-behaviour version — the last term is ... | **Met** | Caches keyed by content hash including the solver-behaviour version. |

### Gas (§9)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `GAS-1` | A gas region carries species, temperature, a pressure field , a bulk velocity field , and a collision model. The velocity field is easy to omit and hard ... | **Met** | Species, temperature, collision model, a uniform bulk velocity, and now an imported velocity **field** - VTK ImageData, sampled trilinearly, conserved at the face, with the overhang past its extent reported. Both transport modes see it: the event-driven models carry the ion's position into the neutral draw, checked against `u + mu E` at **120.000 m/s of carry against a declared 120** with -0.000 across it, and a flow field agrees with an equivalent uniform drift to **1e-9** on the same seed. `gas.flow-extrapolated` reports a collision drawn outside the imported extent. **And the pressure is a field too**, so a differentially pumped instrument is now expressible: `pressureField` with a required unit, read as a density through n = p/kT, with mobility scaled as 1/n (which nothing here did before) and both collision models thinned against a majorant taken at the densest gas anywhere. A field at twice the declared pressure gives a **bit-identical trajectory** to declaring twice the pressure, under both collision models, and agrees to 1e-6 through the diffusive path. The regime numbers are computed where the gas is thickest, because a description that fails anywhere in the instrument has failed. One assumption left, stated: a single **temperature**, which the document already carried. |

### Guardrails (§4)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `GRD-1` | No bare numbers Every quantitative result carries value, units, uncertainty or confidence interval, ensemble size or convergence measure, and active warnings. The API offers no ... | **Partial** | Structurally enforced where it is enforced: `Measured` has no public way to read a bare value, and a test that tried had to go through `Deconstruct`. **But most figures of merit had no enveloped path at all** - `FiguresOfMerit.Evaluator` returns a bare double because ranking needs an ordering, a deliberate exception argued where it is taken, and the consequence nobody had written down is that twelve of the fourteen figures existed *only* in the excepted form. `FiguresOfMerit.Measure` is the counterpart: **1 of 15 figures carried an envelope, now 5**, by resampling the ion cloud. Validated against two closed forms the code has no part in - the mean's sigma/sqrt(N) at 0.987/1.022/0.996 and the median's sqrt(pi/2)sigma/sqrt(N) at 0.975/1.041/0.940. The remaining ten are named on every result by `results.no-envelope` rather than printed bare. See Amendment 28. |
| `GRD-2` | Warnings propagate Validity warnings travel with the result through every layer — engine, command layer, CLI output, MCP response, exported file, rendered ... | **Met** | **Now enumerated rather than asserted.** The requirement names its own population — seven layers — so `WarningPropagationTests` is one test per layer, with the MCP one next door in `Einzel.Mcp.Tests`. The exported file is checked by **set equality with the result**, not containment, because a file carrying one warning and dropping the rest passes any weaker check. **This row previously read "exported VTU/VTI files and figures" and was wrong on both**: the trajectory `.vtu` carried no warnings at all, and the figure carried neither the warnings nor the gas. See Amendment 29. |
| `GRD-3` | Warnings above threshold are not suppressible Validity violations cannot be silenced by any caller, including in batch mode. | **Met** | Validity violations carry a non-suppressible severity and no caller can silence them. |
| `GRD-4` | Validity is checked, not assumed Regime applicability, mesh convergence, ensemble convergence, adiabaticity, and the §10 linearization residual are ... | **Met** | Regime applicability, mesh convergence, ensemble convergence and the linearisation residual are all computed rather than assumed. |
| `GRD-5` | Preview results are labelled and cannot be promoted Tagged permanently; cannot be quoted, exported, fed to an optimizer, or rendered without visible ... | **Met** | The taint rides on the number, and a preview writes nothing - a tainted result in `results/` would be reported as current by `verify`. |
| `GRD-6` | Extension results are attributed Carries the extension identity and version; cannot present itself as first-party. | **Met** | Extension results carry the extension identity and interpreter; the manifest records `null` where no interpreter took part. |
| `GRD-7` | Results are immutable and traceable Every result references a manifest. Every rendered artifact references a result. | **Met** | Every result references a manifest. Studies wrote none at all until recently; sweeps, optimisations and scans all write one now. Imported gas and study inputs are fingerprinted and verified; missing historical fingerprints are not silently certified (`InputProvenanceTests`, Amendment 48). |
| `GRD-8` | Spending is deliberate Any operation exceeding a configurable cost threshold requires a prior estimate. | **Met** | `einzel estimate` takes **a study as well as a model**, which is the operation anyone actually plans against - short by the evaluation count before, and silently. A diffusive run's step is computable exactly and predicted 901 against 901 actual. A trajectory run's is path-dependent, so it is **measured by a short pilot flight** rather than omitted: the whole flight where it finishes inside the window, otherwise scaled and declared a floor. The solve rate is measured on **this machine, on this geometry** - a hardcoded constant put the C-trap's 5.9 s solve at 1.81 s. End to end: **6.25 s estimated against 7.06 s actual** on a volume model, where the same model was 1.81 s before. A study's flight is sampled across its own declared range, since evaluation cost varies **2.2x** along a scan that crosses a focus; on that scan the estimate is **0.76x of wall clock and 0.89x of the computation**, the difference being process start, which is excluded and said to be. Pilots repeat while repeating is cheap and report the cheapest, which took the rate's run-to-run spread from a factor of two to **2 per cent**. **The mesh is reported too**: each axis rounds its interval count up to a power of two, so cost is a step function of the cell size - a 635 x 48 x 350 mm analyzer at a requested 1 mm gets 0.62 x 0.75 x 0.68 mm and 34.2 M nodes, and 1.5 mm costs **7.9x less**. The suggested size is evaluated with the grid's own arithmetic and asserted to deliver what it promises, because a rule of thumb offered 1.24 mm, which lands on the boundary and gives the identical mesh. **What one evaluation IS depends on the transport**: the ordinary case solves once and flies `members` ions, but a diffusive run steps a density and a space-charge run advances the packet in lockstep, so for those the model's own cost already IS one evaluation and multiplying it by the ion count charges twice - for a diffusive model, for trajectories that mode does not produce. Process start is excluded and said to be. **`members` is what one evaluation of the named figure flies, not the study's ion count** - the registry carries a `FlightBasis` per figure and the estimate asks it, so `flightTime` costs its three-rung convergence ladder rather than a 21-ion ensemble. Billing every figure the ion count put a 2000-draw reflectron sweep at **29 s against a measured 1.08 s**, and an agent in the acceptance run shrank its study to get past it; an unrecognized figure falls back to the ensemble count, the conservative direction. **The threshold is now configurable** (`--threshold <seconds>`), which the requirement asked for and which was a constant, and the refusal says in its first clause that the study is not blocked - only `estimate` exits 3. **And an operation past the gate now reports while it runs**: the requirement covers deciding whether to start and said nothing about the hours after, so a run the gate priced at four hours then went silent for four hours. `einzel run --progress` says where it is every thirty seconds by default and leaves a checkpoint that survives the process; see Amendment 44. **And it no longer prices a run that cannot happen**: the Astral at a 1 mm cell is 68.3 M nodes against a 64 M limit, and `estimate` reported 35 minutes and exited 3 on the cost gate for a solve `run` would refuse. The limit is one constant (`VolumeMesh.MaximumNodes`) read by the validator and by the solver's allocation guard, and `estimate` validates first, so it meets the same `GRID_TOO_LARGE` refusal rather than a second copy of the limit. See Amendment 33. **The memory figure is measured now too, and it was a fifth of the truth.** It was 48 bytes a node for every solve, from a comment listing three fields; a volume solve holds **240** on the Galerkin hierarchy, 141.7 on the rediscretized one and 143.25 on a plane, stated by the solver beside its arrays (`SolveMemoryModel`) with each coarse level charged at the size its grid really is, and measured against real builds - 241.3 and 240.9 on the Astral at 1.12 M and 8.68 M nodes against 241.8 and 240.9 stated, 141.9 and 141.8 on the segmented quadrupole, 143.3 to 143.9 on four plane templates - so the Astral at 1 mm, billed 3.1 GiB, needs 15.3. It is the **live set**, and two checks say that is what has to fit: under a hard heap limit the Astral at 4 mm completed at 279 MiB and failed at 275 against a live 259, while unconstrained its heap reached 1.6x that. Measuring it found **a driven geometry holding every finished channel's mask**, and once that was removed a first-tier JIT temporary still holding its cut links: two channels were 353.7 bytes a node and are **248.7** against 248.9 predicted; the driven plane build had the same defect, a first mask kept for a later check, and costs a potential a channel now too. A diffusive run holds 263 bytes a density node where 16 was billed, **once per species**. `SolveMemoryTests` pins the figures, band 0.90 to 1.02, and fails on one extra field; its live-set sampler had to be made dense and repeated before it meant anything on a four-core runner, where it had passed a plane statement 7 percent high by missing the peak of the smaller of two builds. The 64 M-node limit is recorded as the wrong kind of rule for memory, which is a property of the machine rather than the document; `docs/cli.md`. |
| `GRD-9` | Human work is never silently lost Where an agent and a human share a live model, mutations are attributed in a shared linear journal. | **Met** | `SessionJournal`, served by `Einzel.Mcp`. The attribution and the shared linear stack are MCP-1's row. What this row adds is the *never silently lost* half, and building MCP-1 did **not** deliver it: the journal knew only about mutations made through it, so a person editing the model in their own editor had their change overwritten by the agent's next whole-document edit with nothing anywhere to say so. **The sharper consequence was to undo** - an unrecorded change breaks the chain, so walking back landed on a document predating the person's edit and discarded it as a *side effect of reversing something else*. `Reconcile` now records an outside change as an entry attributed to `outside` (not to the person: another tool, another session and a git checkout look identical from here), refuses an edit written against the document as it was, and makes the refusal recoverable by having `model_read` take the change up. Checked by mutation - a no-op `Reconcile` fails three of nine journal tests. `docs/live-session.md` |
| `GRD-10` | Drift is detectable, in both directions A stored result can be checked against both the current model and the currently installed engine. | **Met** | `einzel verify` separates drift from notes: an edited model or a changed solver-behaviour version invalidates; a different engine build with identical numerics does not. |
| `GRD-11` | Known-defective versions taint their output A result produced by a version below the published floor (§18) carries a non-suppressible defect warning. The ... | Partial | The taint mechanism exists and rides in the warning list. There is no published defect floor to compare against, because there are no releases. |
| `GRD-12` | A rendering never looks more precise than its data Decimation tolerance, time compression, and preview status are recorded in every rendered artifact ... | **Met** | Decimation tolerance recorded in the file, the `--json` result and stamped on the page. Density contour levels likewise. |

### Device library (§14)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `LIB-1` | Device templates are data in the same schema as any other model , plus a declared parameter surface. If supporting a new device requires a change below ... | **Met** | Twenty-three device templates, all data in the model schema. **The signal has fired about twelve times and every one was vocabulary, not architecture** - nothing has ever required a change to the solver, the integrator or the transport core. Three kinds, enumerated with their instances in `docs/extending.md`: a *function* in the expression grammar (`log` for the Kingdon trap, `cosPi`/`sinPi` for any multipole above four rods, `asinPi` for the C-trap's slot, `floor`/`mod` for a repeated ring), an *attribute* on an existing element (`drivePhase` as an expression for the travelling wave, `repeat`, a tilt on a box, Neumann faces on `solve3d`, `axis` on the analytic RF element, `fringe` on a region, a parametric launch direction), and a *primitive* (`polygon` for the linear ion trap's hyperbolic rod, `prism` for its three axial sections, `revolve` for the C-trap's bent one). Twenty-three templates have cost fourteen schema versions, all purely additive, and roughly half needed nothing below the library at all. **Two entries are worth distinguishing from the rest.** The Paul trap's was not a missing capability but a *wrong validator*: `CanDoWork` asked whether any electrode held non-zero DC, so a trap holding all of its potential as drive was refused as a model in which nothing could move an ion - the rule says to believe the signal, and telling "there is a bug here" from "the abstraction is wrong" is part of using it. And `revolve` arrived late for a device that had shipped for months: `c-trap` modeled its bent rods as chains of overlapping spheres because no primitive could express one, which is the format's limit correctly worked around and then **not revisited when the limit moved**. See Amendment 50 for what that cost. |

### Licensing (§20)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `LIC-1` | No GPL dependency in the default build, ever. Where GPL functionality is genuinely useful it is invoked out-of-process as a tool the user supplies, and ... | **Met** | No GPL dependency. The PDF writer is hand-written partly for this reason; `Directory.Packages.props` carries a licence note on every entry. |
| `LIC-2` | Extensions carry their own licences; the extension manager surfaces them. | **Met** | An extension **carries** one: `licence` on the manifest, scaffolded by `ext register` so a new extension answers from the first minute, and surfaced by `einzel ext list` in both forms. **An undeclared licence prints `NOT DECLARED` rather than being omitted** - the case where care is most needed must not be the one whose line is shortest - and is null in `--json` rather than a placeholder, so a caller cannot mistake "did not say" for a licence it recognises. The **manager view** now exists too: an Extensions pane listing name, version, kind, trust, licence and what refused it, with an undeclared licence marked in red rather than left blank, and **what the sandbox does not enforce printed on the pane itself** rather than behind a control - the command layer prints those on every listing, and a window that hid them would be the shell weakening a safety statement the engine refuses to weaken. The listing is `ExtensionCommand.List` through the session, so the window and `einzel ext list` cannot disagree about what is installed (UI-1), and the read is journalled as `einzel ext list` (Amendment 25). |

### Live session (§16)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `MCP-1` | Mutations are attributed and the undo stack is shared and linear. | **Met** | `SessionJournal` in `Einzel.Commands`, served by `Einzel.Mcp` over stdio. Attribution is taken from the client's `initialize` handshake rather than from a tool argument, so an agent has no spelling with which to sign an edit as anybody else - asserted in two halves, the name that comes back and the absence of any parameter that could have offered another. The stack is shared: an agent over the wire reverses an edit made in process, and the entry names both parties. Linear because an undo is itself an entry, so walking back twice appends twice rather than popping. A change made to the file outside the session is recorded and an edit against the moved document refused, which is GRD-9's row. Both claims checked by mutation: private per-author stacks fail three of six journal tests, a popping undo fails a different two, and moving attribution into a tool parameter fails three of five protocol tests. Streamable HTTP hosted by the shell - the primary transport - waits on the shell; the tools are above the transport, so it is a wrapper rather than a rewrite. `docs/live-session.md` |

### Performance (§8)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `PERF-1` | Nominal field solve, all basis solutions < 30 min Cached. Symmetry reduction makes this reachable for a 200-ring funnel | **Met** | A 96-ring travelling-wave guide reduces to two basis solves; a 48-ring funnel to two. Well inside 30 minutes. |
| `PERF-2` | Single ion, cached fields < 100 ms Interactive tuning must feel live | **Met, measured** | **5.6 ms in process against the 100 ms budget**, cheapest of seven on an i9-9900K. Measured in process because that is what the budget is about - the shell drives the same command objects there, and "interactive tuning must feel live" is a statement about that path. Through the executable the same run costs 266-286 ms, of which roughly 230 is a self-contained build starting its own runtime, which is PERF-8's separate budget. `PerformanceBudgetTests` reports the number and asserts at ten times it, because a wall-clock bound is a statement about a machine (Amendment 27) and the useful assertion is the one that catches an order-of-magnitude regression without measuring the runner. |
| `PERF-3` | Preview tier, any model < 10 s | **Met** | 9 ms on the shipped reflectron against a 10 s budget. |
| `PERF-4` | 10 4 -ion ensemble, Class S < 5 min CPU, embarrassingly parallel | **Met, measured** | **1.9 to 2.7 s against the 300 s budget** for 10,000 ions with a thermal, spatial and energy spread through the shipped reflectron - two orders inside. Asserted at the budget itself rather than at ten times it, there being no need for headroom at that margin. |
| `PERF-5` | Quadrupole stability scan, 500 x 10^3 < 2 h GPU-bound; why ILGPU is in the stack | **Met, on the CPU** | **2,809 s - 46.8 minutes against the two-hour budget** - for 500 amplitudes by 1,000 ions, 500,000 trajectories through one solved field, on an i9-9900K at 16-way parallelism and sharing the machine with a test suite for the first third of it. `einzel estimate` predicted 77 minutes, so the gate is conservative by 1.6x here. **This run is the expensive end of the requirement**: the amplitude range (100-600 V, q = 0.12 to 0.73) lies entirely inside the stable region, so every ion flew the full length; a scan that crosses the cut-off loses half its ions early and costs less. So the timing is a cost measurement rather than a physics one, and the margin is a floor. **The requirement's own claim that this is GPU-bound is withdrawn** - see Amendment 39, where the fair CPU baseline puts this machine's GPU at 1.4x in double precision. |
| `PERF-6` | Tolerance sweep, 10 3 geometries × 10 3 ions < 8 h Only reachable via §10 sensitivity fields | Partial | The superposition side is measured - 500 linearised draws at 25 ms against 142 ms for one solve. The full 10^3 x 10^3 campaign has not been run. |
| `PERF-7` | Extension round trip, sandboxed < 50 ms Sets the granularity floor for | **Unverified** | **Not separable from process start, which is not this platform's to control.** Launching the interpreter and doing nothing costs 45.0, 49.6, 53.9, 58.2, 40, 51 and 63 ms across seven runs on one machine; the budget straddles that spread, so asserting it measures CPython's start cost rather than anything here. On a shared build agent a bare launch takes **seconds**, and the old assertion passed and failed on the same commit in two runs minutes apart. What is measured and asserted instead is the platform's own share: a round trip costs **1.08x to 1.52x** a bare launch, and on one run came in *below* it - the marshalling is under the noise floor of process start. The absolute number is reported on every run. See Amendment 27. |
| `PERF-8` | CLI cold start to first output < 500 ms No network call permitted in that path | **Met** | 73-147 ms cold start against 500 ms. |
| `PERF-9` | Vector figure, 10 3 decimated trajectories < 5 s Agents iterate on figures; it must not be a batch job | Unverified, and not yet measurable | **The vector renderer draws one trajectory, not a bundle**, however many ions the source declares - so a figure carrying 10^3 decimated trajectories cannot be produced and the 5 s budget has nothing to time. Same reason as PERF-10, which was already recorded this way. The viewport does draw bundles, so the capability exists one surface over. A test pins the current behaviour, so whoever adds bundle rendering is told to come and measure both budgets rather than discovering the gap later. |
| `PERF-10` | Vector figure file size, same < 5 MB Must open in a text editor and an illustration program | Partial | The quadrupole PDF is 13 KB. No test asserts the 5 MB ceiling for 10^3 trajectories, because nothing draws 10^3 trajectories yet. |

### Project (§3)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `PRJ-1` | Models, studies, extensions, render specs, and results are text. | **Met** | Models, studies, extensions, render specs and results are all text. |
| `PRJ-2` | Large artifacts are referenced by content hash, never embedded. | **Met** | Large artifacts live in `.einzel/` and are referenced, never embedded. |
| `PRJ-3` | A run manifest fully determines its run. Model hash, seeds, engine version, transport mode, solver settings, extension identities. Results are therefore ... | **Met** | Model hash, seeds, engine version, solver-behaviour version, transport mode, compute path, extension identities, interpreter and machine. **And which model, as distinct from which content**: `modelPath` is recorded alongside the hash. PRJ-3's list does not ask for it and by that list does not need to - the hash determines the run - but identity is a second question, and answering it with the hash meant editing a model could make its drift *disappear* onto an identical twin. Recorded with forward slashes because a manifest travels; absent on older manifests, where the hash search remains the fallback. See Amendment 30. Gas imports and study files now carry their own paths and byte hashes; `InputProvenanceTests` checks edits, deletion, relocation and legacy records (Amendment 48). |
| `PRJ-4` | A plain folder is the default and fully supported. Every feature, guardrail, and agent workflow works in a directory with no repository. Requiring one ... | **Met** | A plain folder is the default. `--vcs git` writes an ignore file and changes no behaviour anywhere else. |
| `PRJ-5` | Version control, where wanted, is scaffolded. einzel init --vcs git writes an ignore file excluding .einzel/ , an AGENTS.md , and a starter layout. | **Met** | `einzel init --vcs git`. |
| `PRJ-6` | Nothing depends on a hosting provider. Agent instructions in the project An agent needs two distinct things: how to use Einzel (platform knowledge, ... | **Met** | Nothing depends on a hosting provider; the whole loop is files and commands. |
| `PRJ-7` | A project may declare assertions — expected resolving power, transmission floors, envelope constraints — runnable as einzel test . An agent that edits a ... | **Met** | A test is a file in `tests/` naming a model and a figure of merit with a relative tolerance. `init` ships one whose expectation is a closed form. |

### Regime (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `REG-1` | Trajectory integration and statistical diffusion are peer implementations of ITransportMode . | **Met** | Both modes are `ITransportMode` implementations and both are available. `ProducesTrajectories` is on the interface so a renderer asks rather than infers. |
| `REG-2` | The engine computes the governing dimensionless numbers along every path and raises a non-suppressible warning when the selected mode is outside validity. | **Met** | Knudsen, mean free path, collisions per flight and per RF cycle computed on every run and **reported whether or not anything crosses a threshold**. A regime violation gets its own exit code. The effective-field path now refuses spectra it cannot average correctly, ignores inactive generators when choosing the period, and reports the same quiver for either charge sign (`RfValidityRegressionTests`, `QuiverIsAnAmplitudeForEitherChargeSign`; Amendment 46). |
| `REG-3` | In the overlap band both modes run on the same model and the comparison is a supported operation with its own report. Accuracy classes Class T, timing. ... | **Met** | Trajectory 13.2555 +/- 1.3584 m/s against diffusion 13.8418 - 0.43 standard errors, between machineries sharing only a cross section. `einzel compare` is the supported operation. |

### Rendering (§17)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `RND-1` | Rendering is an engine capability, not a shell feature. Einzel.Render sits below the shell; the figure composer and einzel render are peer consumers of ... | **Met** | `Einzel.Render` sits below any shell and draws headlessly on the Linux CI runner with no display, window manager or font server. **And the conductor surfaces now leave the program**: the surface-nets extraction was headless, tested on Linux, and consumed only by the Windows viewport, so the artifact that lets an external renderer draw a three-dimensional geometry needed the shell - invariant 1 pointing the wrong way. `einzel export --mesh` writes them as OBJ, one named object per electrode. Building it found the sub-cell failure returning through the *electrode's own aspect ratio*: a 4 x 635 mm stripe meshes to nothing at 48 cells across its longest span, and resolving by the thinnest instead gives 1.16 M triangles and a 77 MB file. Per-axis now; bit-identical on an isotropic shape. |
| `RND-2` | A render spec is text , lives in figures/ , and is versioned with the model. The figure in a paper is regenerable from the repository rather than being a ... | **Met** | A render spec is text in `figures/`, versioned with the model. |
| `RND-3` | 2D sections and orthographic projections emit SVG and PDF , through a geometric projection pipeline that produces paths rather than pixels. This is a ... | **Met** | SVG and PDF from a path pipeline. Both writers are hand-authored; a test walks every PDF cross-reference offset. |
| `RND-4` | Shaded 3D perspective is raster. Hidden-surface vector output is a deep rabbit hole with poor payoff. Schematic 3D with hidden-line removal may be added ... | **Met, orthographic** | `einzel render still` draws a shaded 3D raster to PNG, headlessly, and it is **the viewport's picture**: the scene, colors, layers, translucency, lighting and camera come from the same `ViewportPicture` and `Framing` the window draws from, and only the rasterization differs - a managed rasterizer with a depth buffer and supersampling, no GPU, no dependency. Orthographic rather than perspective, because the viewport is, for the reason recorded against it. Provenance and every warning ride in the PNG's text chunks; a validity violation hatches the bottom of the picture. Checked on the Linux runner: both mirrors of the planar pair land at the pixels the camera predicts. **Every still carries a scale** - a ruler in whichever corner the drawn instrument covers least (over it only when all four are covered, and left out when the picture is too small to hold it) with one arm along each model axis, all the same round length, projected with the picture's own matrix so each arm is foreshortened exactly as the instrument is; in a straight-on view it is an L-shaped scale bar, and the arm pointing at the viewer is left out. Its length and corner are in the `--json` result and in the file's text chunks, and the Avalonia viewport draws the same ruler from the same layout, stroke for stroke: the arms, label and letters come out of the command layer as one list of polylines in the still's line font, which both draw. Checked in a live window on five models and every named view, where it matches a still of the window's own size in length, corner, arms and every glyph to a pixel - which it did not while the window set its text in a font of its own, at two thirds the still's size. Section 23 still leaves open whether hidden-line vector output is worth building. See Amendment 54. |
| `RND-5` | Trajectories are decimated with a stated geometric tolerance ( | **Met** | Stated and measured, and the point-to-segment distance is clamped - a reflectron is why. |
| `RND-6` | Text stays text. Labels, dimensions, and axis annotations are selectable and editable in the output, so a figure can be relabelled for a different venue ... | **Met** | Labels are text runs in both SVG and PDF, asserted in both. |
| `RND-7` | ), scrubbing, and frame export. Model tree with parameter editing, live validation, units on every field, template instantiation. Sequence editor : the ... | **Met** | Both halves, and neither is optional in the interface either. A mapping is declared as phases in a render spec, each naming the simulated time it runs to and the rate it plays at - and an animation can only be asked for through a spec, with no `--rate` flag, so **there is no command line that produces one without a declared mapping**. The rate is stamped on every frame in two readings (`500 ns of flight per second of playback - 2,000,000x slower than real time`), written by the renderer rather than offered as a styling option. On the shipped reflectron the turn-around is a fifth of the flight and 69% of the film. |
| `RND-8` | Diffusive regions animate as evolving density fields , never as particles ( | **Met** | Enforced rather than stated: the renderer asks the mode and draws no trajectories when it says no. **And now draws the density instead**, which the prohibition previously left empty. |
| `RND-9` | Native output is an image sequence. Encoding to a video container is an optional out-of-process step using a tool the user supplies. | **Met** | `render animation` writes numbered vector frames and a `frames.json` schedule, and nothing here encodes video. **That is the requirement rather than a shortfall** - and it is LIC-1's argument as much as an architectural one, since ffmpeg is exactly what would otherwise be reached for. **This row was previously registered wrong in both columns**: its requirement text was a fragment of section 15's CLI table that cites RND-9 in passing, and its evidence was DST-7's ("no video, so no external encoder to detect"), so it read as Not built while the thing it asks for had shipped. |
| `RND-10` | Videos carry provenance visibly, as a corner stamp with engine version and model hash, in addition to container metadata. | **Met, for the artifact that exists** | There is no video container by RND-9's own design, so the artifact this governs is the frame sequence. Every frame is a `Scene`, and a `Scene` carries provenance - engine version, model hash, decimation tolerance and any active warnings - written **both** as a comment in the file and as a visible stamp on the page, which is GRD-12's rule and the reason this requirement exists: metadata nobody opens is not provenance. An animation frame additionally stamps its own flight time and the playback rate in force, which is RND-7. What is not exercised is container metadata, there being no container. |
| `RND-11` | Preview-tier results are visually distinguishable in any rendered output ( | **Met** | A hatched rule the width of the page and a QUALIFIED line naming the code. **A raster still carries the band and not the line**: nothing here rasterizes text, so the codes travel in the PNG's text chunks and on stderr, and the picture is marked by a hatched strip along its bottom. Stated rather than claimed met for the still - see Amendment 54. |
| `RND-12` | Fields, trajectories, and density clouds export to VTK/VTU. The consequence is deliberate and worth stating as a scope decision rather than leaving ... | **Met** | Fields export as `.vti`, trajectories as `.vtu`, and densities as `.vti` - the last only since the density became an output at all. |
| `RND-13` | Gmsh MSH is supported for import and interchange, through an Einzel-authored reader and writer. GPL attaches to Gmsh's implementation, not to the format; ... | Not built | No MSH reader or writer. Phase 5. |
| `RND-14` | MSH is interchange, never the native representation , for the reason given in §9: a mesh is already discretized and cannot carry the parametric intent ... | **Met** | Vacuously, and by design: the native representation is parametric JSON and there is no mesh path to be tempted by. |
| `RND-15` | Where meshing itself is eventually needed — with the boundary element solver, not before — Gmsh is detected, not bundled. Shelling out to a GPL binary the ... | Not built | No BEM solver, so no meshing, so nothing to detect. |

### Space charge (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `SC-1` | For Class T runs the space-charge approximation parameters are validated against the direct method on a reference population. | **Met** | Both methods are `ISelfField` peers and are validated against each other **at matched smoothing**, which is what the requirement needs and what an unmatched comparison cannot give: the sum taken to its own point limit (softening/100, worth 3.5%) against a grid cell of 0.92 mean macroparticle spacings agrees to **0.08%**. Both approximation parameters are declared (`spaceChargeGrid`) and both are reported. The direct sum itself is validated by third law to 1e-14 and the uniform-sphere closed form to 5%. |

### Sequencer (§9)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `SEQ-1` | A phase boundary may change transport mode; the conversion is explicit, reported, and named as a source of uncertainty. | **Met** | Trajectory legs now honour gas and direct/PIC space charge, exhausted packets remain empty, and numerical failures cannot be converted into completed phases (`SequencedPhysicsRegressionTests`; Amendment 47). **The conversion exists**, in both directions, with the uncertainty named: `PacketConversion`. Trajectories to a density is a bilinear deposit conserving the population by construction (exact to 1e-12), losing the velocity distribution entirely - which is what the diffusive description *is*, since drift-diffusion holds because the velocities have relaxed. A density to trajectories samples position from the density and **invents** the velocity, drawn Maxwellian at the gas temperature plus the local drift; `transport.velocity-assumed` is a non-suppressible violation, because a caller reading a flight time computed from invented velocities and not knowing they were invented has been misled by the platform. Checked: equipartition **1.0021 at 300 K and at 1200 K** (two, because one is consistent with a constant that happens to match), drift against muE **exact**, a Gaussian cloud's centroid and spread recovered to sampling error, a 4000 m/s beam coming back at 0.2 m/s. The discriminating check is cylindrical - a cell is a ring, so a uniform density gives mean radius **2R/3 = 13.3333 mm, measured 13.5177**, against the R/2 = 10.0 a density-weighted draw gives; run the wrong way it gives 10.0245 and **only that one test of the ten fails**. **The timeline is now the instrument's**: schema 0.6 adds a model-level `sequence`, phases are resolved once and handed to every element - a solved geometry re-weighting channels it has already solved, an analytic one compiled per phase and switched by `SequencedField`, an element no phase moves left static. `stages` on a solve stays the older spelling for the single-element case, with both-declared refused. A code review caught the first version reaching only the solved branch, leaving analytic elements frozen at baseline while the solved ones moved. That closes the defect below and is the prerequisite for the mode. **A phase now names a transport mode**, absent meaning the model's - the same rule its parameter overrides follow, so a model with no sequence and one whose every phase runs in the declared mode are the same run. `CompiledModel.Phases` carries the schedule and the mode per phase, and `ChangesTransportMode` says whether any boundary actually converts. **And a run crosses the boundary.** `SequencedRun` walks the phases, each an ordinary run of its own mode over its own duration, converting where the mode changes. On the test instrument - launch, thermalise, extract - the packet advances 1.37 mm in a microsecond while flying and **does not move at all over twenty times longer as a density**, because the diffusive drift is muE and E is zero there. That is the conversion made visible rather than a defect: drift-diffusion holds precisely because the velocity distribution has relaxed, so the momentum genuinely is discarded. Position, the one thing both descriptions carry, survives to the fourth decimal. A trajectory leg starting part-way along the timeline is flown against a new `TimeShiftedField`, because the integrator always starts at t = 0 - wrapped rather than given a start time, which is the precedent `AxisymmetricField` and `PonderomotiveField` set. **The first phase may be the trap**, which is the ordering the requirement was written about, seeded through `DiffusionRun.Seed` rather than a second implementation. Reusing that path corrected two numbers a duplicate had got wrong - a grid built with `new Grid2D` rather than `OverBox`, which rounds to a power of two, so one model got two different grids depending on the path; and a mobility helper ignoring `Derived`, so a cross-section-derived mobility came back as the stored value. A third gap closed with them: the diffusive leg passed no absorbers, so electrodes did not absorb during a diffusive phase. **And `einzel run` reaches it**: the fork tests `ChangesTransportMode` before the model's own mode, since a model may declare `diffusion` and still have a sequence that leaves it. The terminal shows a per-phase table with a dash where a diffusive phase has no trajectories (different from having none left) and `packet centre` rather than `final x`; `--json` carries a `sequence` block; the manifest records `diffusion -> trajectory`, because one mode would claim to determine a run it does not describe. **Two defects already fixed once were met again**: a successful sequenced run exited `ConvergenceFailure` (the exit logic knew two outcome strings), and the printer showed `flight time NaN` (the absent-not-NaN fix was gated on `Diffusion is null`). Both were fixes written as a list of known modes rather than as the question being asked - `docs/lessons.md`. **And a driven geometry inside a diffusive phase gets the cycle average**, through the same `Effective` wrapper the wholly diffusive path uses - shared rather than written twice. That was the **fifth** occurrence of a time-varying quantity reached through a time-free interface answering at an arbitrary instant, and the first one I introduced. Measured on a four-rod quadrupole at 2 mbar with the packet released 1.5 mm off axis: **0.2341 mm after 60 us at 400 V, against 1.5000 mm with the drive off**. The geometry had to be four rods - a first version used two plates, which give a nearly uniform field where the ponderomotive force goes as grad E squared, so the packet moved 0.1% either way and the test passed on a threshold of 'less than where it began'. Nothing outstanding for SEQ-1. A stage is declared on a *solve element* while the transport mode is a property of the *run*, so a per-element stage cannot carry one - two elements would name different modes for the same instant, and there is no superposition of transport modes the way there is of fields. Worse, the existing arrangement is already wrong: `CompileStages` re-resolves the **whole model parameter surface** and re-expands **only its own element**, so two electrodes written as the same expression over the same parameter held **900 V and 300 V** during a stage, on a model that validated cleanly. Refused first - a sequenced model may have one field element - then fixed: `Timeline` resolves the phases once for the model and hands the same surfaces to every element, so a phase moves everything written over its parameters. Verified by mutation in both arms: restoring the per-element behaviour fails its own test and nothing else, separately for the plane and the volume. The 3D arm had no test at all before this - it is a separate copy of the same shape rather than shared code, which is exactly the arrangement where one arm gets fixed and the other is left behind. `run` across modes exists, and the diffusive loop now **samples the field every step**, so a diffusive phase may `ramp` a parameter - which is a trapped-ion-mobility elution scan, and is measured as one in `docs/device-templates.md` (`tims-analyzer`). Two defects found on the way, both producing clean output: a placeholder one-hertz clock on a driveless staged geometry that the pseudopotential wrapper took at its word, and a single-mode diffusive sequence routed to the path that steps a snapshot. `docs/pressure.md`, `docs/lessons.md` **Nothing is outstanding for this requirement.** It was carried as Partial while the timeline was per-element; the model-level `sequence` of schema 0.6 closed that, a phase names a transport mode, and `einzel run` crosses the boundary and records `diffusion -> trajectory` in the manifest. |

### Symmetry (§9)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `SYM-1` | A geometry subtree may declare cylindrical symmetry, a mirror plane, or discrete periodicity. The solver reduces accordingly and the interpolant ... | Partial | Cylindrical, mirror planes and discrete periodicity all implemented and measured. **Translational invariance is missing from the requirement**, and is the one the first real geometry needed - see Amendments. |

### Trajectory output (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `TRJ-1` | Trajectory output for rendering is a separately sampled stream with its own cadence, independent of integration steps. Integration steps cluster where the ... | Partial | The render stream can be coarser than the integration steps but never finer. Full independence needs dense output, which is not implemented. |

### Transport (§11)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `TRN-1` | Mobility is an explicit input with stated field dependence. | **Met** | Mobility is a declared input; a derived one is marked `mobility.derived`, and `IsWithinFit` refuses to leave the caller to work out whether the field dependence still holds. **And it is now per ion population** (schema 0.13, Amendment 41): a model may declare `species` in place of `ion`, each carrying its own mobility, because mass, charge and mobility are one ion's three properties and a list of populations in one place with a list of mobilities in another has nothing tying the two together. A species that declares none derives its own by Mason-Schamp for **that species' mass** rather than sharing a number, which would separate nothing while looking like a converged answer; `transport.mobility` beside `species` is refused rather than treated as a default. |
| `TRN-2` | Diffusive transport emits a time-resolved density field rather than trajectories, because that is what it computes. This is what §17 renders for a funnel. ... | **Met** | A density field, now with somewhere to go: exported as `.vti`, drawn as contours, and assertable through the `transitTime` figure of merit - which did not exist, so the mode's principal scalar could not be pinned by a project test or ranked by a study. **And from a sequenced run too**, which wrote a manifest, an arrivals file and a result and no density - so a packet that crossed into the diffusive description could be summarised into a centroid and a width and looked at in no other form, the exact state the wholly diffusive path was in before this requirement was answered for it. The control is the half that makes it a statement: a sequence ending in the trajectory description has *no* density, which is a different fact from an empty one, so the same flag must write nothing there. The file carries the run's caveats and their severities (`GRD-2`), and building it found that it could not: the note saying a sequenced run has no flight time was constructed below the export, so the volume - the artifact most likely to be opened by somebody who never saw the envelope - came out with an empty caveat block on a run that had earned one. One list, two readers, and the mutation back to the hand-gathered version fails the test. |

### Test (§19)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `TST-1` | Every performance path is tested against the scalar reference. | **Met, for the one path that exists** | The pair sum has a vectorised path and a scalar one, and the scalar one is **selectable** (`CoulombInteraction.Kernel`) rather than merely retained - which is what "never allowed to rot" has to mean, since a reference nothing can run is one nobody can check. They are compared over nine sizes spanning below one vector width to a thousand members, with members inactive, with a pre-loaded accumulator, and against Newton's third law: agreement to 3e-15 of the acceleration scale, and they cannot agree better because the additions happen in a different order. A deliberate mutation of the vector kernel fails 10 of 16. The parallel field loop is the other performance path and is asserted **bit-identical** to the serial one, which is a stronger statement than a tolerance and is available because nothing is summed across members. |
| `TST-2` | Every golden-file tolerance carries a comment explaining its magnitude. | Partial | Tolerances in the suite are justified in comments as a matter of practice, and every literature comparison states its source. Not enforced. |

### Shell (§16)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `UI-1` | The shell owns layout, input, the interactive viewport, and the update check. It owns no physics, no validation rules, no file format knowledge, and no ... | **Partial** | **The shell exists**, and the prohibition is now checked rather than honoured by construction: `ShellBoundaryTests` runs from the Linux-running test project over the assemblies actually present, with a `MustBePresent` guard so a scan that found nothing cannot pass. **Two different checks were needed**, and the difference cost a mutation - `GetReferencedAssemblies` reports what the compiler *emitted*, so declaring a ProjectReference to the transport engine left no trace and the test passed; UI-1 is about what the shell may reach **for**, so the project file is checked too. Every view is built on a command object, and twice a view could not be built until a command existed (`einzel outline`, `ViewportCommand`) - which is Amendment 25 running in the direction it was not designed for. **Partial rather than Met** because the shell does not own the update check: there is nothing to check. **And the prohibition has now been paid for rather than merely kept**: a second shell, `Einzel.Shell`, puts the same views on Avalonia for Linux and macOS, targets plain `net10.0`, and reaches the engine through `Einzel.Commands` alone - 2,167 lines with no change to any assembly below it, which is what UI-1 plus Amendment 25 were being banked for. Its tests target plain `net10.0` too, so a shell's own tests run on both runners for the first time. See Amendment 53. |

### Update (§18)

| Tag | Requirement (abridged from r06) | Status | Where it stands |
| --- | --- | --- | --- |
| `UPD-1` | The shell is the only component that checks for updates, and only at launch. No periodic timer, no background polling. | Not built | The shell exists and does not check for updates, because `Einzel.Update` does not. The half that is a prohibition holds vacuously - nothing anywhere polls - and the half that is a capability needs the whole of section 18. |
| `UPD-2` | The CLI never contacts the network. einzel doctor reports from a cache the shell writes; --check performs an explicit live check because the user asked. ... | **Met** | Satisfied in the strong direction: the CLI makes no network call at all, and there is no cache for `doctor` to read because nothing writes one. |
| `UPD-3` | . UI-1 The shell owns layout, input, the interactive viewport, and the update check. It owns no physics, no validation rules, no file format knowledge, ... | Not built | Not built. |
| `UPD-4` | The notice is non-modal. | Not built | Not built. |
| `UPD-5` | A project may pin an engine version. A pinned project run under a different version reports the mismatch and exits with a distinct code rather than ... | Not built | Not built. |
| `UPD-6` | An update never applies while a run, sweep, or solve is active , and never mid-session. Staged on download, applied on next launch. | Not built | Not built. |
| `UPD-7` | Applying an update regenerates the platform layer of AGENTS.md in projects opened afterward. | Not built | Not built. |
| `UPD-8` | Before applying, the updater reports what will change : extensions falling outside the new engine's compatible range, whether field caches will be ... | Not built | Not built. |
| `UPD-9` | Linux and portable installs never self-update. einzel self-update is always explicit. The version floor, without coercion A published policy file declares ... | Not built | Not built. |
| `UPD-10` | A version below the floor keeps working normally . | Not built | Not built. |
| `UPD-11` | Every result it produces carries a non-suppressible defect warning naming the defect ( | Not built | Not built. |
| `UPD-12` | einzel verify reports which stored results were produced by a version now known defective. This is the recall mechanism , and it works retrospectively on ... | Not built | Not built. |

---

## Completed in an earlier round of the list

These four were items 10 to 13 of the previous revision of *What to do next* and were
left standing when that list was replaced, with no heading of their own and no items 1
to 9 - so they read as the live list and their item 13 still called itself "the top of
the list". They are kept, and headed, because the reasoning in a completed item is worth
more than the fact of it; the live list is *What to do next*, below.

10. ~~**`render animation` (RND-7)**~~ — **done, and the requirement enforces itself
   through the interface.** An animation is asked for through a render spec and there is
   no `--rate` flag, so there is no command line that produces one without a declared
   mapping. The rate is stamped on every frame in two readings, written by the renderer
   rather than offered as a styling option.

   On the shipped reflectron, three phases give 1.000 / 4.400 / 0.995 s of playback: the
   turn-around is **a fifth of the flight and 69% of the film**, which is precisely what
   one rate cannot show. Frame times are computed from playback time rather than
   accumulated, the final frame is forced onto the arrival, and a frame on a boundary
   announces the incoming rate.

   **A design bug of mine, and the test that missed it.** Handing each frame only the
   part of the flight drawn so far made every frame choose its page from its own prefix —
   a camera following the ion, invisible in any single frame. The test written for it
   **passed with the bug restored**, because it used a *solved* template whose extent
   comes from its declared domain; moved onto an analytic model it fails at once.

   **And it exposed one that had been shipping.** The scaffolded reflectron drew its
   turning point at x = 105,080 mm on a 160 mm page, and had since sections were built.
   An analytic model's extent came from the source and the detector alone, and a
   reflectron catches the ion where it launched — the same point. No render test covered
   it because every one of them uses a device template, and every template declares a
   solve domain.

   **And the field moves, which it did not at first — the fourth sighting of one
   defect.** A driven field implements the time-free `IElectrostaticField` as well and
   answers it at t = 0 without failing, so every frame drew the same instant. After
   `einzel solve` reporting a driven geometry's DC pattern, the diffusive mode stepping a
   density through a snapshot of the RF, and `SuperposedField` becoming a snapshot when a
   driven member was summed in. The instant is now declarable and every frame supplies
   its own, with `render.field-at-instant` on a static section either way.

   Checked exactly over one period of a 1 MHz quadrupole: **20, 0, 20, 0, 20**
   equipotential paths at 0, T/4, T/2, 3T/4, T. Nothing to contour at the zero crossings;
   the same drawing at T as at 0, to the last bit; the rod pairs swapped at T/2. The
   contour levels had to be **fixed once across the animation**, because a driven field's
   range changes through the cycle and per-frame levels would spread over rounding noise
   at a zero crossing — the page defect again, in the other axis.

   Left: **geometry that moves**. A stage may change what an electrode holds and not
   where it is, so the conductors are identical on every frame by construction; a
   mechanism with a moving part is not expressible in the model format at all.

11. ~~**A density at a chosen instant**~~ — **done.** A diffusive run reported the
   density it *ended* with, so a model whose ions all arrive drew an empty box, and the
   only way to see the packet was to shorten `maximumFlightTime` — which gets one by
   throwing away everything after the moment being looked at. `DriftDiffusion.Run` now
   takes a list of instants and `einzel render section --at-us` draws one: **0 contours
   at the end, 10 at 50 µs centred at 49.5 mm, 11 at 150 µs at 102.9 mm** on the corpus
   drift tube.

   Both times are reported, because they differ: a diffusive step lands where its
   stability limit puts it, and cutting it to land on a requested instant would change
   the step sequence and so the answer. And **recording is bit-identical to not
   recording** — step count, collected ions and every node — which is asserted, since
   snapshots that perturbed the run would be snapshots of a different run.

   **And it unlocked the diffusive animation**, which was refused outright and rightly
   so while a run reported only its final density. The command layer runs the transport
   once with the frames' own instants as its snapshot list and hands the renderer the
   results, as the section path already does. On the corpus drift tube over 200 µs the
   packet drifts 22 → 100 mm, spreads 24 → 59 mm, and narrows again at the end as its
   leading edge is collected — three things a trajectory cannot show.

   **The contour levels had to be anchored once, and that matters more than the page
   did.** Density contours sit at decades below the peak, and a diffusing packet's peak
   falls as it spreads: anchored per frame the levels fall with it, the contours stay the
   same size, and a film of a packet spreading shows a packet doing nothing. Not a
   flicker — a lie. Anchored across the animation, later frames show fewer contours,
   because the density really is lower.

12. ~~**Dimensioned callouts**~~ — **done.** The memo's own figures are line drawings
   *with* dimensions, and a section without them says what the instrument looks like and
   not how big any of it is.

   **The number is measured, never written down.** A `dimensions` entry declares the two
   points it spans; the length is computed when the figure is drawn. `label` names the
   span and does not carry the value, because a typed number is a second statement of
   something the model already says and the two part company at the first parameter
   change — which is precisely what a dimensioned drawing exists to prevent.

   **And the points may be expressions over the model's parameters.** §9's rule for a
   model, "every placement is a parametric expression, never a baked number", is not
   weaker for a drawing of it. Changing `turningDepth` from 50 to 80 mm and re-rendering
   the same spec gives `penetration 50 mm` then `penetration 80 mm` with no edit in
   between; the test asserts exactly that — one spec, two models, two measurements.

13. ~~**A sequenced example, and the defect that blocks it.**~~ - **done.** `sequenced-extraction` ships, at 1.0e-7 of its closed form, and the defect that blocked it was `FlightTimeStudy` refining an absolute velocity floor to 1e-11 m/s, unsatisfiable for an ion starting from rest. The claim below that this is "the top of the list" is therefore stale, and was the clearest symptom of this block having been orphaned. The corpus does not
   exercise the sequencer, which is the one Phase 4 capability it misses. Writing the
   example found two defects, both fixed — `CanDoWork` reading the base potentials and
   not the stages (the fourth sighting of one pattern, the third in that function), and a
   stage `set` to an expression being read as its absent literal zero.

   **Fixed.** `FlightTimeStudy` refines by scaling the relative tolerance *and both absolute
   floors*. At its deepest rung `AbsoluteVelocityTolerance` reaches **1e-11 m/s** — ten
   picometres per second, against thermal speeds of hundreds of metres — and for an ion
   starting from rest the normalised velocity error is unsatisfiable at any step size.
   Isolated by tightening each of the three alone: only the velocity floor reproduces it.
   That floor is load-bearing; it is what stops `ErrorNorm` being a position-error
   controller. `einzel preview`, which does one run, gives **2.9106 µs against a closed
   form of 2 + 0.910572113**.

   Holding the floor leaves the reflectron **bit-identical** and makes its interval
   **17× narrower** (1.48e-10 µs against 2.58e-09) — a measured residual instead of a
   saturated floor. It also broke
   `AnIntervalThatCollapsesToZeroIsReportedAsAFloorRatherThanAsExact`, and the reason is
   the part worth keeping: **that model's bit-exact rung agreement depended on the ladder
   over-tightening the very floor at issue.** The test had been asserting a coincidence.
   Since nothing reachable through the study's API reproduces the collapse, the rule was
   given a name — `FlightTimeStudy.ConvergenceResidual` — and is now tested directly on
   runs that agree to the bit, which states the rule instead of hoping a model will
   demonstrate it.

   **`sequenced-extraction` ships**, the corpus's first: hold at rest, then extract.
   Predicted 2 µs + 0.910572113 µs, measured **2.9105718 — 1.0e-7 out**, which is the
   finite plates and the grounded boundary rather than the sequencer. Corpus 30 → 31, and
   Phase 4's sequencer is exercised by the release gate for the first time.

   **This is the top of the list**: it is a defect rather than a gap, it blocks a Phase 4
   deliverable from being demonstrated, and the machinery it blocks (traps, pulsed
   extraction) is what the memo's §6 item 5 is a choice between.

## What to do next

**Open work first, ranked, then what is done and what it taught.** The two have opposite update
rules - a plan is reordered whenever priorities move, history is append-only - and carrying them
in one numbering meant every reprioritisation churned a dozen entries that will never move
again. Nothing references these by number any more, so the churn is now harmless as well as
absent.

Everything struck through was on this list and is finished; it is kept because **why** each
turned out to be cheap or expensive is worth more than the fact of it.

**What the order means.** First what is in flight, because a study half-run is the most
expensive thing to leave. Then the two surfaces that make everything after them legible - a
report a person can read, and a documented way to extend the platform - since both are cheap and
both compound. Then the device work, which is where the physics is. Distribution last, with its
trigger stated rather than a date, because it is worth nothing until somebody other than this
project's author needs to run it and more than any physics the moment one does.

### Open, in priority order

1. ~~**Run the front-end study.**~~ - **run, and it turned into a study of the resolving-power law rather than of the front end.** The front end costs **nothing** once the two models are compared on matched cell sizes (-1.9 percent; 18 percent on unmatched ones, which is a 1.68x difference in cell size and nothing else). What the study found instead is that **gas speed is not an independent knob** - the tunnel holds up to `K E_peak` = 93.7 m/s at 60 V and a scan past it walks out of the device while still reporting a clean rising resolving power - and that **the field and the ramp time do not enter through their ratio**, because the release lag goes as `1/(V T)` and a scan rate is `V/T`. The published `R ~ v_g` is recovered as an asymptotic statement, exponent 0.971, approached from above as `tau/T` falls. `docs/literature-targets.md` section 6 and `docs/lessons.md`.

   The original framing, kept because the cost work stands: the study that had never finished now costs about **four
   hours** rather than fifty-five, and the cost gate that would have talked anyone out of
   starting it is fixed.

   **The gate is done.** It reported **8 s against an actual 197,000** because it omitted the
   drift limit for any model whose field had to be solved - sound in general, and wrong here by
   four orders, since a diffusive run cannot be cheaper than its own solve and the drift limit
   is not a refinement but the whole cost. It solves now (always a 2-D solve, since the
   diffusive mode is two-dimensional) and samples the **cycle-averaged** field the run actually
   drifts through rather than the time-free arm's arbitrary instant, at every phase boundary
   rather than only at zero. **8 s becomes 3.6 h against a measured 4.1**, and the step it finds
   - 58.5 ns - matches the 56 ns counted independently from the probe's assembly count.

   **Its own question is answered, and not by this study.** Whether the arrival width is the
   analyzer's own or delivery spread the ramp reads as mobility was settled by a matched pair
   of runs costing 39 and 122 minutes, differing in one parameter and both stopped at the end
   of the trap - because the width the ramp reads is the width the packet has when the ramp
   starts. The analyzer's own floor turns out to be a closed form with the mobility cancelled
   out of it, `sigma_z^2 = (kT/q)/|dE/dx|`, confirmed to **0.34 %**; its relaxation time
   `1/(2K|E'|)` = 231 us is confirmed to **1 %** over four octaves of hold; and the delivered
   packet is 2.4x wider because it **has not finished relaxing** in the 300 us the shipped
   sequence gives it, which is a statement about the trap duration and therefore about a knob.
   `docs/literature-targets.md` section 6 carries the register entry. **Before paying for the
   whole sequence, ask which phase the number lives in** - that was about eight times cheaper
   and removed a second claim (that the ramp is modelled right) from the answer.

   **The sequence has now run end to end: 905 seconds for the whole 18.3 ms of fill, trap and
   ramp at 256 x 32.** Four attempts had produced no output at all; what the fifth cost was one
   line of a cache. The ponderomotive well is the cycle mean square of the *oscillating* field,
   so a DC ramp cannot change it - and `PonderomotiveWellCache` decided every fourth step that
   it had, rebuilt a six-second well, and would have done so around 17,000 times across the
   ramp. Twenty-eight hours of establishing that nothing had moved.

   What moves is round-off, and the tolerance sat below it: the well is a mean square taken
   after removing the mean, and the mean here is a DC field the ramp walks from 60 V to zero, so
   the noise floor of that subtraction is proportional to a quantity changing by everything
   while the well changes by nothing. About 2.5e-13 of the deepest well per step, which crosses
   1e-12 in four steps and then does so forever. The bar is 1e-9 - on a 30 V well, 3e-8 V
   against a thermal `kT/q` of 0.026 V - and the probe visits one lattice node per call rather
   than all sixteen, so the other half of a ramped step is spread across sixteen of them.
   Bit-identical: 16 rebuilds became 1 and sigma_z is the same to all six printed digits, pinned
   from both sides by a test that requires 1e-11 to be held and 1e-5 to be caught.

   **And the finding is that it elutes, with a resolving power.** Re-measured on one build,
   every configuration of the document elutes: the delivered packet collects **99,666.7 of
   99,954** ions at a mean of 15,480.750 us and sigma 156.344, giving a peak elution potential
   of **21.1843 V** and **R = 7.674** against the analyser alone at 8 - so the funnel and gate
   cost about four per cent. Re-measured after PR #37, which moved this template's density
   grid onto the detector plane it had always collected three millimetres short of; the
   parked packet's equilibrium width is unchanged at 0.7118 mm across that move, which is the
   control, since the closed form for it contains only the gas temperature and the axial
   gradient.

   **That answers this item's standing question.** A delivered packet and one seeded at the
   balance point enter the ramp **1.88x** apart in width (sigma_z 1.3404 against 0.7118 mm,
   the latter being the closed-form equilibrium) and elute at **identical profiles** - the
   same peak bin, the same half-maximum width, the same R to every printed digit - with their
   weighted means agreeing to eight significant figures. So the arrival width is the **analyser's own**, not delivery spread
   the ramp reads as mobility: the packet re-equilibrates to `sigma_z^2 = (kT/q)/|dE/dx|`
   before release, which the closed form requires, since it contains nothing about the
   packet's history. Two ramps beginning 9.7 ms apart on the instrument's clock land on the
   same potential to five figures, and a *stepped* release comes out 12 per cent wider
   (sigma 178.0 and 179.9 against 159.16) - the release program reaching the width where the
   history does not.

   **An earlier run of the same document collected 7.74e-245 ions and is recorded as
   unexplained.** It does not reproduce on the current build. Excluded as causes: the ramp
   spelling, the delivery, the late start, a stale operator (67,828-68,216 assemblies either
   way), and - by direct control - the ponderomotive well cache, which was the leading
   hypothesis because that commit is the only one between the two engines touching a physics
   file. Reverting it gives 10,794 well rebuilds against 1 and the same answer to **13
   significant figures**, which incidentally measures its bit-identical claim on the model
   that motivated it rather than on the analyser alone. The frozen run's own provenance was
   overwritten by the re-runs that investigated it, so the model hash cannot be compared;
   `docs/device-templates.md` and `docs/lessons.md` carry both.

   The three changes that made it affordable are worth stating separately, because two are
   arithmetic and one is machinery.

   **The cycle mean needed no samples.** `DirectPotentialAt` evaluated the composite potential
   at sixteen instants per density node every step. A solved field's potential is linear in its
   channel weights, so its window mean is `sum_k mean(w_k) phi_k(x)` - one evaluation per
   channel and no time sampling - with `RfWaveform.Mean` giving each weight's cycle mean in
   closed form. **7.7x**, guarded on every drive completing a whole number of cycles in the
   window, falling back to the old sampling loop where it cannot. That corrects a recorded
   claim that the direct term "needs its sixteen samples whatever the ramp moves": true of the
   composite, false of the parts.

   **And the coefficient sweep threads; the Gauss-Seidel sweep does not.** 5x against
   **1.09x**, on the same nodes, three runs each - so the second was rejected rather than
   kept. The ceiling is a property of a loop and not of a subsystem: bicubic superpositions are
   compute-bound and cache-resident, a Gauss-Seidel cell is a few flops against several array
   touches plus an integer division per face. Bit-identical either way, since nothing is summed
   across nodes.

   **What is left is the step count, and it is the whole remainder.** The ramped phase steps at
   about 56 ns where the diffusion limit alone allows 1.26e-4 s - a factor of **2,236**, the
   drift limit binding - so 18.3 ms is roughly 142,000 steps whatever the per-step cost is. The
   implicit scheme gains against the *diffusion* limit and buys nothing here. Treating drift
   implicitly, or operator-splitting it, is the deeper fix and is its own piece of numerics.

   **A stale paragraph stood here and is worth recording as such.** It said the immediate item
   was the cost gate, quoting 8 s against an actual 197,000 - a number superseded higher up this
   same item, where the gate now reads 3.6 h against a measured 4.1. A superseded number in a
   planning document steers every session that loads it, which is the argument that moved the
   Astral state out of `CLAUDE.md`, met again inside the document that argument is written in.

2. **The extension surface, now that it is written down (Amendment 42).** `docs/extending.md`
   names the three kinds of change a device has ever needed below `Einzel.Library` - a grammar
   function, a geometry primitive, an attribute on an existing element - with the eleven
   instances as evidence, where each goes, and the traps each has already sprung.

   **What is open is whether it works**, which is a measurement rather than more writing: the
   acceptance suite scores what an agent *does*, and no task in it asks for an extension of this
   kind. A task that says "add a shape" and scores whether the three switches were all taught
   would say whether the page is usable, and would be the first evidence that §5's claim about
   agents extending the platform is true rather than argued.

   Deliberately not queued: an exhaustive API reference, and a package. Extending needs a
   toolchain and a compile, so the extending agent has the source; a package-shaped surface
   waits on distribution.

3. **The TIMS front end, and the fringe it needed.** `tims-front-end` puts Hernandez's
    50 mm entrance funnel (26 to 8 mm, sixteen plates on a 3.1 mm pitch, plate-alternating
    RF, a DC drop) and an entrance gate in front of the analyser, with fill / trap / ramp as
    phases. The funnel delivers **99.993 %** of a 2 mm-wide packet against 65.28 % with its
    RF off (the rest on the last three plates and the gate, every loss named), and it parks
    at **21.09 mm at r = 0.29 mm** — the analyser's own balance point and its own confined
    radius, which is what makes the delivery figure mean something.

    **It also found a defect in the field model.** The first version stopped 42 % of the
    packet at the tunnel entrance, worse with a steeper funnel gradient, because a bounded
    analytic element's edge is a step and a step in a pseudopotential is a wall: an ion
    arriving at radius r meets the whole well at once. Schema **0.12** adds `fringe` to a
    region — a linear rise from nothing at the face to full strength a declared distance
    inside, with the field the gradient of the fringed potential and no fringe bit-identical
    to what a bounded element was. `docs/lessons.md`.

    The gate at 30 V lets nothing in and loses everything held against it on the last plate,
    which is why the instrument also diverts the beam during the trap.

    **Left open, and it points at an optimisation.** The whole sequence end to end did not
    finish: 4.75 CPU-hours at 512 × 64 over 18 ms, then 40 minutes at 256 × 32 over 12 ms.
    A ramped diffusive phase re-assembles its operator every step, and here each assembly
    samples the *solved* funnel RF at sixteen instants per node to take its cycle mean and
    mean square. **But the ramp moves only DC** — the RF amplitudes are constant through the
    scan — so the mean-square field, the expensive half, does not change step to step.
    **That cache is now built, and the measurement both corrected the arithmetic and
    refused to pay here.** "About a sixteenth" was half right: the direct term *is* the cycle
    mean of the potential, so what the cache removes is every field evaluation and no potential
    evaluation - **2.72x** on total evaluations and 3.7x of wall clock on an analytic drive. On
    a synthetic DC ramp it works exactly as intended: 9,609,600 field evaluations become
    490,208, **0 of 2,145 density nodes differ**, the collected count is equal to the last
    digit, and the well rebuilds once in sixteen steps.

    **On the shipped analyser it rebuilds 30 of 30 and saves nothing.** The well genuinely
    moves **1.8e-5** between successive instants of the ramped field - deterministically, over
    exactly one drive period - so the sixteen-probe guard correctly throws the cache away every
    step. The obvious explanation is refuted by its own control: the ramp advancing inside the
    averaging window predicts that a slower ramp gives less, and a hundredfold slower ramp gave
    **3.6e-5, larger**. It scales as 1/amplitude instead - 7.0e-5 / 1.8e-5 / 1.2e-6 at 25 / 100
    / 400 V - the signature of an additive contamination cross-multiplied with the drive.

    **That jitter was fixed by holding the operating point** - `AtOperatingPoint(t)` freezes
    non-oscillatory time dependence and leaves the drive oscillating, which took the well's
    movement across a cycle from 3.15e-6 to **6.53e-14** - and the tolerance was deliberately
    left at 1e-12, on the grounds that a tolerance chosen larger than an unexplained variation
    is caching over that variation.

    **The residual is now explained, and that is what licensed loosening it.** It is the
    round-off floor of the mean subtraction, proportional to a DC field the ramp walks from
    60 V to zero: about 2.5e-13 of the deepest well per step, crossing 1e-12 in four steps and
    then forever. At 1e-9 the sequence completes in **905 seconds** and the answer is identical
    to all six printed digits. See item 1 for what it then measured, which is that the front end
    does not elute, and for the ramp-against-staircase discrepancy that is now the open
    question. Not carried: a deflector plate, a continuous fill, the funnel's own gas, an exit
    funnel - and the last of those is the standing candidate for the discrepancy.

4. **RF confinement for the TIMS tunnel, then a mobility resolving power.** The
    elution ramp runs (`tims-analyzer` with a `sequence` whose diffusive phase ramps
    `exitPotential` 60 → 0 V over 8 ms) and the first thing it measured is the reason
    this item is next rather than a refinement: **45 of 97,770 reference ions reached the
    detector**, and the most mobile ion none, because without RF the density reaches the
    4 mm bore in the millisecond or two before the ramp releases it. The order (least
    mobile first) and the release lag are measured — both eluting ions let go about 10 V
    below the quasi-static `v_g / (K E_peak)`, because the settling time `L²/2KV` is
    0.4-0.8 ms against an 8 ms ramp, and that same time constant is confirmed to a
    twentieth of a millimetre by three packets released from one point and read after a
    millisecond. The widths are not yet the instrument's.

    The real tunnel confines with a **quadrupolar RF alternating between the four
    segments of each ring** (850 kHz, 200 Vpp in Ridgeway's example), the same on every
    ring. A quadrupole is not axisymmetric, so it cannot be electrodes in the r-z solve —
    but its **pseudopotential is** (the field magnitude of a quadrupole depends on radius
    alone), so the route is: solve the segmented-ring **cross-section** in 2-D for the
    quadrupole strength and the multipoles the gaps add, hand that to the analytic
    ideal-quadrupole RF element bounded to the tunnel and superposed on the solved DC
    gradient, and let the collisional pseudopotential (already measured on the funnel at
    2 mbar) carry it into the density solve. At 2.6 mbar the damping rate is ~0.68 of the
    drive frequency, so the collisionless well is ~45 % too deep; the collisional one is
    ~2 eV at the wall against 0.026 eV thermal, a Boltzmann width near 0.3 mm on a 4 mm
    bore. The analytic element needs an **axis** (it lies in the x-y plane), which is the
    attribute-sized change below the library every device has needed once. Then `K/dK`
    off the arrivals profile as a figure of merit — the register names its absence — and
    Hernandez's R of 100-250 and the `β^(-1/4) K^(-3/4)` law become reachable, at the
    register's operating point (75 → 130 m/s of gas) rather than the 50 m/s used so far.

    **The confinement is built** (schema 0.11 adds `axis` to the analytic RF quadrupole).
    The four-segment cross-section solves to **1.2696 of a hyperbolic quadrupole** with
    0.5 mm gaps against the square wave's 4/π = 1.2732 with none, monotone in the gap; the
    template carries that fraction as a `fitted` parameter with a test tying it to the
    solve. With the RF on the bore takes **0.0000 %** of the density in 600 µs against
    26.0 % without, the packet centre moves 0.4 µm, and the rms radius is **0.3406 mm
    against 0.3423 for Boltzmann in the collisional well** — and 0.2805 for the
    collisionless one, so the 0.685 suppression is measured rather than reported. One
    check had to be corrected on the way: `rf.quiver-exceeds-mesh` compared the quiver to
    the density grid's cell and fired on an exact analytic confinement; it now asks the
    mesh the oscillating members are known on (`docs/lessons.md`).

    **The confined scan runs, and every ion arrives**: 100,000 of 100,000 for each of three
    mobilities, in TIMS order, at medians within 25 µs of what the unconfined survivors
    gave — so the wall selected ions and did not move the peak. First resolving power
    **R = 11 / 8 / 5** for K × 0.75 / 1.0 / 1.5 against the register's law's 24 / 19 / 14
    at this operating point, with the `K^(-3/4)` trend present; elution voltage linear in
    1/K to 2 per cent with a −8.5 V intercept that is the release lag in volts; the lag
    itself 0.75-0.9 of the plateau-transit formula.

    **The scan-rate law's exponent is recovered asymptotically**, and the gas-speed
    scaling with it. Six ramps from 4 to 128 ms give R = 3.3 → 25.7 with the ratio per
    halving of β falling 2.52 → 1.15 onto the law's 2^(1/4) = 1.19; fast ramps fall short
    because the release lags the sliding balance and the exit potential at the peak has
    fallen below the release value. At Ridgeway's gas profile — imported velocity and
    pressure fields authored from the register's numbers — R doubles (21.6 at 8 ms, 37.2
    at 32 ms), as `R ∝ v_g`. **Those are 256-interval numbers and the mesh bias is not
    uniform along the sweep**: doubling the mesh moves R by 16.9 per cent at 8 ms and
    0.8 per cent at 128 ms, so refining flattens the sweep and the exponent over that
    range reads 0.306 / 0.252 / 0.215 at 256 / 512 / extrapolated against the law's
    0.250. A position, a transit or a transmission from these runs stands; a width or a
    resolving power carries the caveat. The gap to Hernandez's 100-250 is now arithmetic: his ramps
    (×1.3-1.8), his flow (×1.3), and an accumulation plateau this tunnel lacks, leaving
    ~1.5 unresolved in the width. `mobilityResolvingPower` is a Class B figure of merit.
    The remainder is the front end: the entrance funnel and the gate the operating
    sequence opens and closes (see the TIMS front-end entry).

5. **The shell (§16).** **Seven of the eleven views exist** — the table in
    [the shell section](#the-shell-and-the-rest-of-16) is the current one; this entry
    said three for a while after it stopped being true. The window opens on a model, and
    what remains divides into three kinds rather than one:

    - **Presentation over something that already works** — the sequence editor shows a
      timeline and does not edit it, and the animation timeline has per-phase rates and
      frame export but no scrubbing. Both have the path underneath them; what is missing
      is the input surface. The figure composer is the same shape and can be built last,
      since `RenderSpec` is already text the CLI executes.
    - ~~**A view with a requirement behind it.**~~ **Built, and LIC-2 is met.** The
      extension manager was the only remaining view that retired a tagged requirement
      rather than presenting an existing capability. It lists what somebody deciding
      whether to run third-party code needs before they run it: licence (marked in red
      and reading `NOT DECLARED` where none was given, since that is the one field a
      reader cannot recompute), trust, and **what the sandbox does not enforce, on the
      pane rather than behind a control** — the command layer prints those gaps on every
      listing, and a window that hid them would be the shell weakening a safety statement
      the engine refuses to weaken. Notably it needed **nothing new below the shell**,
      which is the first of §16's views to need nothing: `ExtensionCommand.List` already
      returned every field the pane shows, because `einzel ext list` had already been made
      to answer the same question.
    - **A view that needs a whole assembly.** The update notice needs §18 and
      `Einzel.Update`, which does not exist.

    **Twice a view could not be built until a command existed, and both times the
    command layer gained the capability.** The model tree needed `einzel outline`,
    because a window that parsed the document to build a tree would grow its own idea of
    what a model is; the viewport needed `ViewportCommand`, because one that integrated
    its own trajectories would be a second transport implementation. That is Amendment
    25 running in the direction it was not designed for — the window pulling capability
    *into* the command layer rather than accumulating it privately — and it is the
    strongest evidence so far that AGT-2 is real rather than aspirational.

    **The viewport's own finding is about the colour scale.** §16 asks for bundles
    coloured by energy, and a scale taken per path gives every ion the same colours
    whatever its energy — two ions a kilovolt apart look identical and the picture says
    they were the same. The range is therefore reported by the command over the whole
    bundle. It is the same failure the animation's contour levels had in the other axis,
    where anchoring per frame made a film of a spreading packet show a packet doing
    nothing. The discriminating test is not that the range is wider than the widest
    single path — that margin is 1.5e-5 on a packet launched from rest — but that **no
    single path owns both ends of the scale**, which any per-path anchoring fails
    whatever the magnitudes are.

    **RND-8 is on the face of the window**, asked of `ITransportMode.ProducesTrajectories`
    rather than of the pressure: a diffusive model draws no paths and says what it has
    instead, because an empty viewport and one whose ions were all lost look identical
    and only one of them is a statement about the physics.

    **Two open bets are now settled by measurement rather than argument.** The whole
    solution **builds on Linux, XAML markup compilation included** — `EnableWindowsTargeting`
    is enough — and `Einzel.Wpf.Tests`, the one Windows-only test project, is walked past
    by a solution-wide `dotnet test` there. 848 tests on Windows, 843 on Linux, both green.

    **And two things the third surface cost.** WPF cannot run in globalization-invariant
    mode, which the whole solution sets for CLI-5; the shell reverses it, and what that
    setting protected is unaffected because every formatting and parsing site passes
    `CultureInfo.InvariantCulture` explicitly (Amendment 26). And Helix Toolkit's DirectX
    backend is SharpDX, **archived since December 2020** — taken knowingly, because §17
    confines this path to screen tuning and nothing that leaves Einzel passes through it.

    **The viewport now draws the instrument and the field**, not only the ions. Every
    conductor is the zero level set of its own signed distance, so one routine draws them
    all and a shape added to the format needs no change (invariant 2); what differs between
    symmetries is what the solve claims about the third dimension, which is why a
    cross-section extrudes, an axisymmetric half-plane revolves, and a volume is extracted.
    The mesh maths is in `Einzel.Render` and its tests run on Linux, checked against a
    sphere's area and volume, Pappus, and watertightness rather than against how it looks.

    **The geometry found a defect in the core and a gap in the corpus.** A 1 mm plate is
    thinner than a cell of a 48-cell grid over the whole solve domain, so the
    three-dimensional example produced **no conductors at all**, silently - fixed by asking
    the electrode for its bounds, which needed `CompiledElectrode3D.Bounds` beside `Centre`
    and `CharacteristicSize` because switching on the shape is what invariant 2 forbids. And
    **no diffusive example declares a geometry**, so the claim that RND-8 withholds the
    paths and not the instrument is exercised through the field rather than through
    conductors - a pointed gap, since the device that mode exists for is a funnel.

    **Three more views since**, each of which needed the command layer to gain something
    first: results by §12's accuracy class (which found Amendment 28's GRD-1 hole), the
    regime inspector (REG-2's numbers *along* the path rather than at the worst point
    anywhere, so "outside validity" becomes "between 12 and 31 millimetres"), and the
    sequence editor (the declared timeline, marked with what each phase moves).

    **One gap closed since**: the conductor surfaces can be exported (`einzel export --mesh`),
    so a three-dimensional geometry can be rendered without Windows — and doing it found that
    the viewport itself was drawing none of the Astral's sixteen stripes, because an
    electrode's own bounding box can be as badly proportioned as the solve domain was.

    **Next, in order of what unblocks the most:** the density cloud, which needs only a
    surface since the density is already computed and contoured; the figure composer, whose
    seam is already text the CLI executes; then the animation timeline's scrubbing. The
    update notice needs `Einzel.Update`, which does not exist.

6. **The Astral inverse problem: the mirror is reproduced, and one published number is
    not.** This item has now been rewritten four times, and the rewriting is the point rather
    than an embarrassment - every earlier version attributed the gap between this model and
    the published instrument to something that turned out not to be it. The chronology, with
    what each wrong attribution cost and what caught it, is `docs/astral-log.md`. The model's
    current position is `docs/device-templates.md` and the published record it is compared
    against is `docs/literature-targets.md` §4; **this entry records only what bears on the
    specification**, so the four do not drift into each other again.

    **Where it stands.** The electrode positions are published only as drawn blocks in a
    figure; reading them off it rather than guessing depths closes the mirror comparison, and
    with the published stripe shape in the model the drift register closes too.

    | | model | published |
    | --- | --- | --- |
    | resolving power, mirror alone, over ±2.5 per cent | 120,000 to 220,000 | ~180,000 |
    | drift reversal | 336.06 mm | 310-360, mean 335 |
    | flight time | 784.90 µs on the shipped foil mesh, about 775 mesh-converged | 783.2 by arithmetic at 24 reflections |
    | on-axis potential, 16 points | 0.159 kV rms | read off the figure |

    Three numbers are solved rather than published - the board gap, and `U3` and `U4`, which
    move 6 and 2 per cent from a table that already has one sign printed wrong - and they are
    solved against the design paper's own stated condition, not against anything this model
    produced. **The injection angle is the one published number that does not reconcile**:
    1.78 degrees published against 2.29 fitted, and the reversal distance goes as its fourth
    power, so that is a real disagreement rather than a rounding one.

    **What this establishes for the specification**, which is why the item is here at all:

    - **A published instrument can be reconstructed from public information alone**, which is
      §21 Phase 5's test of generality met on the hardest available case. Nothing in it came
      from conversation with the vendor.
    - **The geometry had to be measured out of a figure**, and the platform had no way to
      say so. That is now Amendment 40 and it is built: a parameter declares a `provenance`
      and a `source`, and the shipped template carries them — **3 published, 13 drawn,
      18 fitted, 29 chosen**, so which results would move if a better source turned up is a
      question the document answers rather than one its prose does.
    - **A grounded domain edge is a third electrode**, met again here: the edge behind a flat
      electrode moved `c3` by 0.17 on its own.
    - **The flight-time agreement is a percent, not four tenths of one** (Amendment 55). Three
      foil faces sat on nodes, which made the shipped figure a coin toss at 0.27 percent;
      moved off them, where the foil mesh sits still spans 0.83 percent at the shipped 4 mm
      cell and 0.22 at 2 mm, and the mesh-converged flight time is about 775 µs against 783.2.
      Still inside the register test's 2 percent, and still clear of K = 25.
    - **Two measurement floors bound any further work.** Flight-time differencing floors the
      drift coefficients at ±0.02 whatever the mesh, because it is a 330 ns signal on a 3 µs
      error that only 60 per cent cancels; a quadrature screen over per-slice basis wells
      avoids that and costs no flights, but is floored by adiabaticity. The two floors are
      independent, which is why the methods agree on ranking and disagree on values.

7. **The linear ion trap, from a cross-section to an instrument.** **The template modelled
    the 2002 paper's prototype rather than the released instrument**: one ejection slit where
    the LTQ has one in each x rod with a detector behind each, which the paper itself predicts
    while measuring the prototype at 44 per cent scan-out against 88 for the pair. Corrected
    in the cross-section, the volume template and the Stellar - whose four slots are for
    symmetric RF, with ejection still through the x pair, as the dual-pressure paper states.
    **The odd-order field fault this register attributed to the slot is the prototype's**: a
    dipole of 1.26e-3 and a hexapole of 1.84e-4 at one slit, 2.8e-15 and 8.6e-16 at two, since
    a slit in each x rod is symmetric about x. The quadrupole term moves 0.8219 to 0.8214, so
    no working point shifts. Scan-out doubles to 45.3 per cent and stays at half the paper's
    88; the profile is the remaining factor and the rod's thickness is refuted. The split
    between the two detectors is launch coherence rather than geometry - the field is symmetric
    to 1.5e-6 inside the trapping region - so the sum is reportable and the split is not. **And
    the format carries one detector plane where both instruments have two**, a LIB-1 signal
    left named rather than built. The 2002 LTQ
    cross-section reproduces the paper's resonance ejection and its unit resolution at
    5,555 u/s (Amendment 37, `docs/literature-targets.md` §2), and it exposed four things
    that stand between that and the dual-pressure device the Stellar front end actually is.
    In the order they are worth doing: ~~**a `ramp` inside a phase**~~ - **done**: a phase
    declares where a parameter ends and gets there linearly, exact where the potentials are
    linear in it and checked at the midpoint, refused for an analytic element or a diffusive
    phase, on a cross-section and on a volume solve alike; a ramped RF flies an ion to 2 µm
    of a forty-step staircase against 240 µm from the held control; ~~**the dual-pressure comparison retuned**~~ -
    **done**: with the 2002 excitation held, the Velos analyser pressure alone broadens
    m/z 524 from 0.62 to 1.44 u, and half the excitation brings it back to 0.62, so the
    2009 paper's gain is a retuning and a gentler excitation is the part of it that
    matters; ~~**the axial structure**~~ - **done**, with a `prism` primitive (the 2-D
    polygon given a length) so the same hyperbolic slotted half-rods make the paper's three
    12 / 37 / 12 mm sections in `linear-ion-trap-3d`: the end sections 3 V above the centre
    make a 2.9 V well holding a 300 K ion within 8.7 mm, where the excitation is uniform to
    below 0.001 % - the paper's figure 2 as numbers - and it scans: twelve ions at
    16,700 u/s eject at effective q 0.8703 against the cross-section's 0.8685, the 0.2 %
    being its quadrupole term (0.8207 of ideal at the 0.5 mm cell against 0.8223, converging
    with the mesh); ~~**the slot's exit optics**~~ - **measured, and the
    extraction field is not the missing piece**: a plate behind the rods takes the shipped
    relieved slot from 38.0 to 49.7 per cent between earth and a kilovolt and the straight
    channel from 6.6 to 9.5, against a 31-point gap between the two profiles at earth, so the
    profile dominates the field at every voltage tried and the efficiency stays a sensitivity
    to the geometry the paper does not give. **A slot is a waveguide below cutoff for an
    electrostatic field**: at the mouth, where the aperture lens acts, a kilovolt is worth six
    tenths of a millivolt, and the decay length is `h/pi` - checked on the 2 mm relief cavity
    at **0.6392 mm against 0.6366**, with the 0.25 mm channel 18 per cent high on two cells, in
    the direction under-resolution predicts. What the field reaches is the **relief cavity and
    the gap**: the relief's walls take 51 ions at earth and 32 at a kilovolt while the slot's
    own take 23 either way, so **a relief is what lets an extraction field inside the rod at
    all**, its reach set by the relief's height rather than its length or the voltage. The
    control is a positive plate, which costs half the transmission and puts 12 ions on the
    rod's back face against 1. 300 ions a point, because the four-profile table this replaces
    was twenty and its rows differ by less than their counting error; and ~~**space charge in the
    scan**~~ - **resolved as far as a vacuum run can take it**: the direct sum's softening,
    set from the packet's RMS radius, exceeded a line cloud's transverse size
    thirty-four-fold and switched the force off; it is now reported
    (`spacecharge.softening`) and set from the packet's three standard deviations, the
    radius rule's number to the bit for a ball and an order of magnitude smaller for a line.
    With the force on, 400,000 ions in a half-millimetre cloud shift each ion's ejection by
    a tenth of a unit with no common direction and the peak by under 0.1 u, because the
    cloud size is an input to a run with no gas and the shift goes as the density - a
    cooled cloud of that population would be about 60 µm across and seventy times denser. **Now asked of the volume trap and
    answered: no.** Thirteen runs - a cooled slice held by the axial well, ramped through
    the edge, against matched cross-section runs - put every pushed width inside the no-push
    realisation spread of its own configuration, at up to 96,000 ions and with the softening
    violation removed. The tightest form is the cross-section's: at a converged mesh, 400x
    the population moves the width by under 0.03 u on a 0.43 u peak. Three findings came
    with it, in `docs/device-templates.md` and `docs/lessons.md`: a **bootstrap over one
    realisation is not an error bar** here, and reported a difference as significant between
    two runs differing only in seed; the perturbation is **chaotic rather than mean-field**,
    saturating at 1.6-1.7 u of per-ion shift whatever the population, so differencing two
    runs cannot measure it; and the volume trap's peak width is set by its **1 mm mesh**
    rather than by its third dimension, since the cross-section at 1 mm gives the same
    width from a geometry with no axial motion. What the axial well demonstrably does is
    hold the cloud against **diffusion**: none lost in a 500 us hold with it, 88 of 240 lost
    without it and no space charge at all.
    **Gas and space charge now run together** - the packet integrator lands its shared step
    on every collision in the packet - and a 1 mm slice of the cloud cooled 1.5 ms in the
    paper's helium and scanned at up to 9,600 ions per millimetre, three times the
    instrument's densest ordinary load, shifts by 0.011 u and does not broaden. The reason
    is the generalised Kohn theorem: a dipole excitation drives the centre of mass, which in
    a near-harmonic field does not feel the mutual force (checked to 1.8e-14 m in an ideal
    RF quadrupole against members scattered by 18 mm). What is left of the capacity question
    is the ejection across the slot and the cloud's axial extent, which the volume trap holds
    and the cross-section cannot. **The Stellar's own trap** is in hand
    (Remes 2024) and shipped as `stellar-ion-trap`; its scan at the paper's four rates gives
    a floor of 0.15-0.33 u against the paper's 0.35-1.0 Th, the broadenings the instrument
    has (a millimetre cloud, amplitude noise, real machining) being absent from the template.

8. **Two templates were rebuilt from their real electrodes, and what is left in the C-trap is
   a literature question rather than a format one.** `revolve` landed, `c-trap` went from 49
   beaded spheres to five swept hyperbolic rods with a slit along the whole arc, and
   `paul-trap` went from three flat annuli to hyperboloids. Amendment 50 carries what that
   corrected: the Paul trap's 9.4 percent effective-radius shortfall is now 0.04 percent and
   its boundary sits 0.07 percent from the tabulated Mathieu one; the C-trap's ejected waist
   is at the center of curvature rather than at 1.73 and 1.92 bend radii, and two published
   findings turned out to be artifacts of geometries the format could not express when the
   templates were written.

   **What is open is provenance, not capability.** Makarov 2006 gives the C-trap's face shape
   ("rods with hyperbolic surfaces"), its gas, its RF range, the quench, the extraction
   voltages and the slot's placement in the pull-out electrode. It gives **no cross-section
   dimension and no radius of curvature**, and neither does US 6,872,938 B2 — whose preferred
   curved trap is a different device again, a stack of curved plates. So `inscribedRadius`,
   `rodHalfWidth`, `rodDepth`, `slotHalfWidth` and `bendRadius` are all guesses, marked as
   such parameter by parameter, and the register in `docs/literature-targets.md` section 7
   says which is which. A single further source with a dimensioned drawing would turn most of
   the template from guessed to sourced, and until one appears the focusing numbers are a
   property of this model rather than of the instrument.

   **And the template still does not model the 1100 V float, the quench, the gas or the two
   end lenses**, each of which the paper describes and each of which is stated as a gap on the
   template itself. The float is the one that needs something the format has: a downstream
   reference the document cannot declare.

   **The gap the rebuild exposed is now closed, and it caught something.** The overlap check
   was cross-section only — `ElectrodeOverlap.Check` takes a `CompiledElectrode` and is called
   from the `solve2d` path alone — so every volume primitive was unchecked. `ElectrodeOverlap3D`
   is the volume half (Amendment 51): a witness search on `max(dA, dB)` that switches on no
   shape at all, asking each primitive only for its bounding box and its signed distance. It
   refused `astral-3d` on its first run, where every drift stripe was extruded 1.285 mm into
   the grounded board it is printed on. The C-trap, the geometry this item predicted was most
   exposed, is **clear**: the deepest the search reaches on its closest disagreeing pair is
   1.033 mm *outside* both, so those surfaces are at least 2.07 mm apart — and the corner
   where `rodOuter` and `rodTop` bound each
   other, which reads off the vertex expressions as a deliberate contact, is on neither
   surface.

   **The general follow-on is an audit, and it has been done once.** Every template's electrode
   outlines were drawn and read against what the device actually has. Nothing else is a shape
   the format has outgrown — the round rods of a quadrupole are what a quadrupole has, and the
   two mirror templates declare edge *profiles*, which are boundary conditions rather than
   shapes and have no outline to draw. Worth repeating whenever a primitive lands, because the
   failure mode is not that an approximation is undocumented but that it is documented,
   corrected for faithfully, and therefore stops reading as a problem.

9. **The render verbs and the viewport now follow a model's timeline, and say where they have got to.** Fixed:
   `render section` and `render animation` decided what to draw from the model's *declared*
   transport mode and then called the wholly diffusive solver, so a model with a timeline
   rendered as though it had none - the elution ramp never ran, the packet stayed parked in
   every frame, exit 0. Both now ask `Modes` and route through `SequencedRun`, which takes
   snapshot instants on the instrument's own clock.

   **That created the gap `--progress` exists to close, one verb over, and it is closed.**
   Rendering a sequenced model costs what running it costs because it is running it - the 8 ms
   elution film was twenty minutes of silence. Both render verbs take `--progress`, thirty
   seconds by default as `run` does, and report per phase on stderr so `--json` still gets a
   clean stdout. **They write no checkpoint, deliberately**: that document states that the
   answer appears beside it when the run finishes and that finding the file means it did not,
   and `einzel report` reads exactly those files - so one left by a render would have a finished
   figure reported as an interrupted run. The projection and the phase accounting are the
   checkpoint writer's own rather than a second set.

   **The viewport was the third surface with the same defect**, and it is corrected too.
   `ViewportCommand.Cloud` called the wholly diffusive solver directly and took its window from
   `maximumFlightTime`, which for a timed instrument is a ceiling on a leg rather than how long
   the instrument runs - so every requested instant landed outside the run and the window drew
   geometry beside an empty box. It walks the sequence now, over the sequence's own window, and
   stops after the **first phase** unless an instant is named: a redraw is something somebody is
   waiting on, and the first phase is where a timed instrument prepares its packet. A truncated
   walk says so on the figure (GRD-12).

   **`SequencedRun` gained `phaseLimit` rather than an instant to stop at**, and the reason is
   the useful part: a phase's ramp interpolates over the phase's own declared duration, so
   stopping one early by shortening it makes every parameter it ramps arrive at its end value
   early. That is a wrong answer rather than a short one, and it would validate, solve and run.

   **And the shell stopped blocking on it.** `ViewportViewModel.RefreshAsync` runs the transport
   off the UI thread and fills the bound collections on it; `ShellSession.ViewportAsync`
   reconciles the journal on the caller's thread and hands the worker a path, so an edit in
   flight cannot race a reconcile. `Draw` no longer refreshes - the two were one method, so a
   layer checkbox re-flew the ions, and once a refresh could be minutes a control that costs
   nothing and one that costs everything were the same call.

   **Measured, because the number is the argument.** A viewport redraw of the shipped
   `tims-analyzer` is **685 s in Debug** - about 210 s in Release - and draws 3 shells at a peak
   of 1.29e14 ions per cubic metre at 500 us of its 1000 us hold. The packet parked at its
   balance point is now something the window shows; it takes minutes to show it, and the window
   stays responsive while it does.

   **And the viewport is now filled by a run that is still going, which is the other answer
   to the same problem.** A redraw truncates to the first phase because somebody is waiting on
   it; `ViewportCommand.Watch` walks the whole timeline and hands out a drawable bundle every
   so often, so the choice is no longer between a frozen window and a checkpoint file
   describing a packet in numbers. What a picture shows that a centroid and a width cannot is
   the shape: a packet still narrowing, one pressed against a wall, one that never left where
   it was seeded.

   **A frame is a whole `ViewportOutcome`, not a density**, because UI-1 puts the contouring
   on the command layer's side of the line - so a frame of a run in flight and the same
   instant drawn afterwards are the same record built by the same function. The geometry is
   extracted **once** and every frame shares it (asserted by reference, not by comparison),
   which is what makes watching affordable: the sequencer already refuses a stage that moves
   an electrode, so the meshes are identical at every instant of a run by construction.

   **Three things the wiring got wrong, all found before it shipped.**
   - **The transport reports its own leg's clock, and it is right to.** A leg does not know it
     is one of eight; the checkpoint writer prints "phase 2/3, 12.0 of 40.0 us", which is what
     a reader wants when the phase is named on the same line. A *picture* has no such line, so
     a frame carrying a leg's clock sends the instant back to zero at every phase boundary and
     stamps the packet with a time it passed through long before. The relay adds the phase
     start, taken from what the phase **did** rather than what it declared - a phase can end
     early, and accumulating declared durations would run the clock ahead of the packet for
     the rest of the sequence.
   - **The end of a run is empty whenever the ions arrived**, so applying the final bundle
     unconditionally spends minutes drawing a packet and then replaces it with an empty box -
     the exact picture the density work exists to stop a diffusive model producing. The watch
     keeps the last frame that held one and says so; the finished viewport picks the middle of
     the usable instants for the same reason, and a watch has no list to pick from.
   - **`RefreshAsync` returns `HasBundle`, which is whether there are trajectories.** A watch
     only ever runs on a model that has none by construction, so returning it would answer
     false on every successful watch there is.

   **Frames are coalesced rather than queued**, and applied on the thread that started the
   watch. A viewport wants the newest packet, not every packet: a queue shows one from four
   frames ago with three waiting behind it, falling further behind the longer it is watched.
   The queued flag is cleared *before* the frame is read, so the race can post twice and can
   never drop the last one.

   **Two narrower things stay open.** An instant landing in a *trajectory* phase yields no
   density, so a mixed sequence cannot be filmed end to end; the animation refuses with a count
   of the frames it could not fill rather than inventing them, which is right, and the figure
   that would show such an instrument does not exist. And the cost gate has not been taught that
   a render or a redraw can now be a multi-hour operation, so GRD-8 does not reach either -
   which matters less than it did now that the viewport can be watched rather than waited on,
   and still matters for a redraw, where nothing warns before the wait begins.

10. **What a volume solve holds, now that it is measured, and how to hold less of it.** The
   estimate's memory figure was 48 bytes a node and a volume solve holds 240 on the Galerkin
   hierarchy (GRD-8's row; `docs/cli.md`), so the Astral at a 1 mm cell needs 15.3 GiB and not
   3.1. Ranked here, above distribution and below the device work, because each item is small
   and the first decides whether a fine Astral fits a 16 GB machine at all.

   - **Cut links are stored densely: 96 of the 240 bytes a node, for 3 to 4 per cent of arms
     cut.** A store over cut nodes alone would take the Galerkin peak to about 150 and the
     rediscretized one to about 50 - the Astral at 1 mm to roughly 10 GiB. The stencil reads an
     arm per node per sweep, so the representation has to stay cheap to index; that is the
     design question, not the saving.
   - **The 64 M-node limit wants restating, and the branch it waited on has landed** (#48, as
     `VolumeMesh.MaximumNodes`). At the true cost it is 14.3 GiB, ten times what its message
     says, and memory is a property of the machine rather than the document, so validation
     should refuse only what the code cannot index (about 358 M nodes, `node * 6` in an `int`).
     The estimate now carries the machine comparison. Recorded in `docs/cli.md`; not yet changed.
   - **Mask assembly is a sixth to a fifth of a build on curved and polygonal templates, and
     its garbage is gone; what is left is the cut-link pass.** The 61 kB, 4.2 kB and 289 bytes
     a node first recorded here were measured before the bounding-box cull; on `main` they were
     5,470, 260 and 105, against the 105 the mask keeps. Timed against the real build in
     Release: the mask is **19-22%** of the C-trap's 16 s build, 14-22% of the linear ion
     trap's 13-15 s, and 2-5% on the Astral and the segmented quadrupole. The prism and
     revolve entry tests now allocate nothing, with bit-identical masks on all four templates
     at every level, and the C-trap's build fell to 14 s (mask **12-16%**) while the linear
     ion trap's did not move: its garbage was never its cost. 84-97% of what remains is cut
     links rather than rasterization, so the next lever is how many exact entry tests that
     pass asks for (`docs/numerics.md`, "What building the mask costs").

11. **Clear the shipped models of the grounded-face coin, and look for the single-conductor
   one (Amendment 57).** Placed above distribution and below the device work because it moves no
   number anyone has published - fifteen of the seventeen trips are measured harmless - but it
   puts a qualified mark on every figure of those models, and a warning that is always there is
   the one people learn to skim.

   - **The two mirrors first**, because there the coin is the physics: the end cap of
     `planar-mirror-pair` and `astral-mirror` is a rectangle with no thickness lying in the
     grounded edge, in the solve only because two expressions agree to the bit. A cap meant to be
     the edge is an edge profile, and the change is bit-identical - check it is.
   - **Then the ring stacks, the lens and the Paul trap**: carry the conductor past the wall so
     every node on it is inside by any arithmetic. Bit-identical where no side face crosses the
     wall on a line of nodes (the lens, the TIMS stacks); the funnels and the traveling-wave guide
     have three or four ring corners that do, and clearing those moves geometry by a fraction of a
     cell and their numbers with it - a change for its own review.
   - **The single-conductor coin**: a face on a node plane makes the arms lying in that plane cut
     or graze on the last bit. Measure how common it is (the funnel's first and last rings have
     it; round-number geometry on round-number meshes will often), then decide whether it wants a
     warning, a fix in the cut links - treating an arm in a face's plane as meeting it - or both.


12. **Distribution, and the trigger is a person rather than a date.** Fourteen of the
   twenty-one not-built requirements are `UPD-*` and `DST-*` - one assembly that does not exist -
   and SPEC's own summary is blunt about the consequence: **nobody can install this**.

   **Deferred on purpose and last on purpose.** Packaging an engine whose numbers were still
   being established would have spent Phase 2 on the one deliverable nobody could use. It is
   worth nothing while Einzel has one user and one agent, and worth more than any physics the
   moment there is a second of either, because the second user's first experience is `install`
   and there is not one.

   So the trigger is **somebody other than its author needing to run this**, which in practice
   means deciding to publish. Until then the entry exists to stop a reader concluding from the
   phase table that the project is behind.


### Done, and what each taught

9. ~~**The run report nobody can read (Amendment 43).**~~ - **built, and writing it
   found two defects that had been true of every run this project has stored.** `einzel report`
   is a view over a project's manifests and results: one self-contained HTML page per project,
   with `--json` carrying the same account so `AGT-2` holds. Drift comes from `einzel verify`
   rather than a second implementation of a distinction that took thought to get right.

   **"A view" is now a property rather than a claim.** Two tests hold it: nothing is added to
   `results/`, and two reports over the same runs are the same page but for the instant each
   was rendered at. A recorder passes the first and fails the second.

   **It is the first thing here that ever read a result document back** - `verify` walks
   manifests and never opens one, `test` re-flies the model - so a producer with no consumer
   had been unchecked however many tests it had. What that found:

   - **The sequenced run path wrote a manifest and no result.** Three of the four paths stored
     one; that path stored provenance and no answer, and it is the path every TIMS study takes.
     The recurring "capability wired into N-1 of N paths" shape, found by writing the consumer
     rather than by a failing test.
   - **A result document did not read back into the record that wrote it.** `required double?`
     plus `WhenWritingNull` means the document is unreadable exactly when a value is *absent*,
     which is a trap or a run where nothing arrived - so `PRJ-3`'s "regenerate and compare" was
     impossible for every such run, and nothing said so because writing succeeded. Fixed at the
     type level: `CommandJson` now writes every required property, null included.

   **And I made the same defect the command exists to expose.** `results/` holds two kinds of
answer - a run writes `X.result.json` beside its manifest, a **study** writes `X.json` - and
the first version knew one, so every sweep, scan, optimisation and boundary search in a
project read as a run that had stored nothing. Enumerating the run paths found the original
gap; enumerating the *writers* into `results/` is what would have found this one. Fixed by a
rule rather than a list of kinds, and the three empty states are separate fields now, since
"nothing stored", "a study's answer this page does not draw" and "a document this build
cannot load" call for three different things from a reader.

**And two presentation defects of my own, both in the "plausible wrong value" class.** A
   fraction was scaled to a percentage and its interval was not - one quantity in two units
   side by side, reporting a transmission a hundred times small, and reading as exactly what a
   trap reports. And the hatched band that marks a validity violation was keyed on
   `IsSuppressible`, which is false for everything above advisory, so the mark that exists for
   the one class `GRD-3` says must never be skimmed landed on almost every warning there is. A
   mark on everything marks nothing.

10. ~~**Settle the well jitter, because it is what stops the cache paying on the one
   device it was built for.**~~ - **explained, and it was the ramp after all.** The
   cycle average asks what an ion feels from a field that *repeats*; a ramp does not,
   and it was still advancing inside the averaging window, so its drift entered the mean
   square of the "oscillating" field where nothing can tell it from quiver.

   **The covariance carries the phase the window opens at**, which is what made it a
   jitter rather than only a bias: `2cov = (A r T/N)[-cos(phi0) + cot(pi/N) sin(phi0)]`,
   because `sum s cos(2 pi s/N) = -N/2` while `sum s sin(2 pi s/N) = -(N/2) cot(pi/N)`,
   five times larger at N = 16. A diffusive step is set by a stability limit and never
   lands on the drive, so every assembly opens somewhere else in the cycle. Closed form
   for the swing, `4rT/(N A sin(pi/N))`, matched to every digit across a hundredfold in
   rate and a sixteenfold in amplitude - and it predicts the shipped analyser's measured
   1.8e-5 as **1.94e-5**.

   **Fixed by holding the operating point**, which is what a pseudopotential is defined
   at: `ITimeVaryingField.AtOperatingPoint(t)` holds non-oscillatory time dependence at
   `t` and leaves the drive oscillating, defaulting to `this` so an unsequenced model is
   untouched by construction. On the shipped analyser the well's movement across a cycle
   goes from **3.15e-6 to 6.53e-14**, with the unfrozen path measured in the same run as
   the control. 1,346 tests pass.

   Three things worth keeping. **Two obvious alternatives are rejected by arithmetic
   rather than by building them** - sampling the cycle more finely does not converge it
   away, since `cot(pi/N) -> N/pi` and the swing tends to `4rT/(pi A)` independent of N;
   and detrending the window instead of de-meaning it removes `6/(N^2-1)` of the well,
   2.4 per cent at N = 16. **The guarding test passed and never exercised it**: its
   harness builds a fresh field per instant, so it was holding the operating point by
   hand and asserting the intended design rather than the implementation. And **the two
   recorded controls each measured a quantity the hypothesis did not predict a change
   in** - one a step-to-step difference, the other my own sampling at whole periods,
   which pins the phase and is stroboscopic.

   **Still open, and it is small**: the recorded slow-ramp control gave 3.6e-5 at a
   hundredth of the rate, which the closed form cannot produce since the swing is linear
   in rate and bounded. Either that run was not at a hundredth of the rate or a second
   term exists. And the payoff - whether the front-end sequence now finishes - is
   measured at the field level and not yet at the run level.

11. ~~**Say how much of a mixture's coupling is worth having, on a device that separates
   ions.**~~ - **answered, and it agrees with the published estimate by an independent
   route.** Two populations held in the solved tandem tunnel with their own charge in the
   field: launching 10^5, 10^7 and 10^8 holds 8.07e4, 7.51e6 and **1.39e7**. Ten times more
   than 10^7 holds only 1.8 times more, so **the tunnel stops accepting between 8 x 10^6 and
   1.4 x 10^7 ions** - the top of Silveira's stated 10^6 to 10^7, reached by solving the
   geometry rather than by bounding a free-space line charge, and therefore about as
   independent as two routes to a number get.

   **The uncharged column is the control**: with no charge the tunnel holds the same 81 per
   cent of whatever it is given at every population, so what saturates is the charge and not
   the geometry. Where the other 19 per cent goes is not established and is recorded as not
   established.

   Two things it must not be read as. **Broadening starts far below the capacity** - 1.4x at
   10^5 - which does not contradict the published estimate, because the two are about
   different quantities: theirs is the packet's field against the analysing field, of order a
   per cent, while what sets a *held* packet's width is its own potential against kT/q, which
   is 0.105 V and 4.1x thermal at the lowest population tried. And the ratio measured here is
   **spatial, at equilibrium, with no elution ramp**, so it sits one to two orders below a
   published resolving power by construction and must never be set beside their 100 to 250.

   The two caveats this entry asked for first were both honoured and both earned their place.
   The seed's declared spread sets the density, so the study varies population at a stated
   volume rather than varying "how many ions" alone. And a mean-field run is expensive as
   predicted - 220 to 330 s against 30 for an uncharged one - which is why the geometry is
   solved **once** for the study; re-solving 55 rings and two funnels per configuration ran
   over an hour without reaching a first line of output.

   Numbers and their controls in `docs/literature-targets.md`, under Silveira's space-charge
   estimate.


12. ~~**Wire particle-in-cell to the packet integrator (SC-1)**~~ — **done, and it
   found something.** Both methods are now `ISelfField` peers, so they can be handed
   the same configuration and differenced. The grid is the packet's own and lives in
   the packet's frame, which makes uniform translation **exact** (1e-11 across
   250 mm) and free; the refresh criterion is therefore written on *shape*, since
   shape is the only thing that ages. Against the reference: **0.5 per cent** on a
   flown packet's widening, about a per cent through the body of a static one.

   **The finding is that a linear gather costs 27× the integrator steps.** The
   previous commit argued that ACC-3's ban on trilinear interpolation does not reach
   a self-consistent field whose accuracy the deposit already bounds. That is right
   about accuracy and wrong about cost: a trilinear force kinks at every cell face and
   an embedded Runge–Kutta estimator reads a kink as error. Measured at 274/383/656
   steps on 16/32/64 nodes against the direct sum's 25 — the count tracking the node
   count is what identifies the mechanism. A quadratic B-spline keeps the
   deposit/gather symmetry, is continuously differentiable, and takes it to
   45/65/95.

   **Where it starts paying: about 850 macroparticles.** Below that the reference is
   simply faster and reaching for the approximation buys nothing; at 2,000 the grid is
   3.2× ahead. Worth stating as a crossing rather than as asymptotics.

   **And it is now declarable** — `"spaceCharge": "pic"` with an optional
   `spaceChargeGrid` block carrying `nodes`, `padding` and `refreshTolerance`, refused
   against any other method rather than ignored. Closing that gap forced two
   measurements the earlier claim of agreement did not survive:

   **The reference has an approximation in it too, and the old comparison measured the
   difference between two of them.** The direct sum softens at the mean macroparticle
   spacing and the grid smooths at the cell, so "agreeing to a few per cent" at each
   method's defaults was a coincidence of comparable smoothing lengths. Taking the sum
   to its own point limit is worth **3.5%**, and against *that* the grid at a cell of
   0.92 spacings agrees to **0.08%**.

   **Accuracy has an optimum rather than a floor**, which is the opposite of every
   other resolution knob here: −15.1%, −4.2%, +0.08%, +4.4% at 3.68, 1.84, 0.92 and
   0.46 cells per mean macroparticle spacing. Refining past the match makes it worse,
   and refining is what a reader does when they want a better answer. Confirmed as a
   *sampling* artefact rather than a resolution one by holding the cell fixed and
   raising the macroparticle count — 4.42% to 1.55% to 0.93% as macroparticles per cell
   go 0.012 to 0.049 to 0.195. `spacecharge.grid-resolution` now reports the ratio on
   every run whether or not it crosses a threshold, and names the node count that would
   match.

   The estimate had to learn the same lesson: 200 macroparticles take **0.99 s at 16
   nodes and 124 s at 128**, so a cost model blind to a knob a document can now set was
   gating on a number missing its dominant term. It is two terms now — linear in the
   cloud, cubic in the node count — pinned by the measured crossing and a measured
   43/57 split, and it tracks the measured 54× ratio to within 10%.

   Still undone: the refresh criterion converges (12.68% → 6.16% → 1.01% → −0.54% as
   the tolerance tightens 0.30 → 0.02) but nothing chooses it automatically, and the
   solve is a full multigrid V-cycle from scratch at every refresh rather than a few
   cycles from the previous answer.

13. ~~**Make a driven diffusive run affordable**~~ — **done, with a trade that has to
   be stated both ways.** `"densityStep": { "scheme": "implicit", "gain": 64 }` is
   backward Euler on the same Scharfetter-Gummel coefficients, solved by red-black
   Gauss-Seidel. **21.1× the speed for 0.057% error** on the shipped funnel at 2 mbar
   over a 50 µs window, so its 843,000 steps over 900 µs become about 13,000 and a run
   that took hours takes minutes. **The error does not accumulate over a longer flight,
   it falls** — the same gain gives 0.108% over 5 µs and 0.057% over 50 — because
   backward Euler's error is concentrated in the initial transient, while the speedup
   grows because the explicit cost is linear in the window and the sweeps per step are
   not.

   **The load-bearing property is not the stability, it is that positivity survives a
   partial solve.** Every term in the update is non-negative, so the iterate is a valid
   density however far from converged it is — a scheme that went negative on the way
   would be unusable however stable, because a negative density has stopped meaning
   anything.

   **And it is not a general speed-up, which is the half that would be easy to leave
   out.** The Gauss-Seidel iteration's difficulty is set by the *diffusive* part of the
   operator, so a step long by Courant's standard but still short by diffusion's costs
   about three sweeps — while a plain drift tube, already near its diffusion limit,
   climbs from 11 sweeps a step at gain 1 to 88.7 at gain 16 and comes out **slower**
   than stepping explicitly. Both are measured and both are documented.

   **What says it is correct rather than merely stable is the Boltzmann equilibrium**,
   which Scharfetter-Gummel is built to hold exactly and which backward Euler must
   therefore hold at any step. It holds to **8.9e-16 in log density over three decades
   at a gain of 1000, in two steps and two sweeps** — one sweep per step, because the
   previous density *is* the answer. Verified by breaking the solve the way a real
   mistake would: the non-negativity tests still passed, and the equilibrium moved by
   factors of 6 to 18.

   **The flux is now assembled once**, which the explicit path wanted anyway: it was
   recomputing two exponentials per face per step. **Bit-identical**, asserted over four
   configurations spanning Cartesian and cylindrical meshes, still and moving gas,
   interior absorbers and every edge kind — density, collected count and every named
   loss, to the last bit.

   Still undone: nothing chooses the gain. Both limits are computable before the run,
   but what gain is acceptable is an *accuracy* question and nothing here measures the
   accuracy of a step it has not taken. Richardson extrapolation over a doubled step
   would, at three solves a step instead of one.

   ~~The ponderomotive well's gradient at
   the ring edges sets the explicit step: on the shipped funnel at 2 mbar the step
   is 1.067 ns against a diffusion limit of 5.2 µs, a factor of 4,900, so 900 µs
   would be about 843,000 steps. Attributed by control — 15.5 ns at 0 V RF, 8.93 ns
   at 25 V, 1.067 ns at 100 V — so it is the RF and roughly as E₀². An implicit or
   operator-split step is the fix.~~ This is the last thing standing between the funnel
   benchmark and a number.

14. ~~**A region on an analytic field element, so an exact analyser can join a
   beamline.**~~ — **built, and one measurement corrected my account of what it
   costs.** Amendment 32. An analytic element may declare a box outside which it
   contributes nothing: an ordinary 1 kV/m section 75 mm from an orbital analyser
   feels **−1,499,000 V/m** of it unbounded and **exactly its own 1,000** bounded,
   and on the axis the unbounded case is worse than swamping, since the model cannot
   be asked a question there at all.

   **The potential steps at the boundary and I first called that an energy the ion
   gains.** It is not: an ion is moved by the *field*, which is exactly the declared
   one on each side, so a bounded uniform field is an accelerating gap followed by a
   drift — **13.658582 µs against a closed form of 13.658582**, with the unbounded
   control at 10.180506. What it really costs is the energy-drift diagnostic and
   non-conservation for an ion crossing *more than once*. `bounded-accelerating-gap`
   puts it in the release gate, and the gate's teeth were checked by mutation.

   **What remains is the better boundary.** A real device's field is bounded by a
   conductor, and a conductor is an equipotential of the very field it produces —
   so bounding an analytic element by one of its own level sets, offset to zero
   outside, would make the potential continuous *by construction*. That is what
   should replace the box, and it is not what was built.

   An analytic field has no extent, because a formula does not. That is harmless for
   an idealisation of a whole instrument — a uniform field, a retarding half-space —
   and stops being harmless the moment one is an exact statement of a real device
   sitting *next to* another. The quadro-logarithmic potential grows as `z^2`, so an
   orbital trap declared beside the C-trap that injects it puts an enormous field
   across the C-trap.

   **The cheap escape does not exist**, which is what makes this a task rather than a
   note. Declaring the analyser as solved geometry so its own domain bounds it fails:
   its electrodes are equipotentials of the field they produce, so their profile
   satisfies `-r^2/2 + Rm^2 ln(r/Rm) = A - z^2` — transcendental in `r`, invertible
   only through Lambert W — and the 2-D shape vocabulary is rectangle, disc and edge
   profile, none of which is a curve a document can name.

   What it needs is a box outside which an analytic element contributes nothing. The
   field discontinuity that introduces is not a difficulty: §11 already makes a
   declared discontinuity a first-class event and the integrator lands exactly on one.
   What needs deciding is whether the region is declared or inferred, and what happens
   where two overlap.

   **Until then both pairings are two models with a measured handover**, which is done
   and is worth having on its own — see the two entries in `docs/device-templates.md`.
   The handover is a *number*, not a hope: for the C-trap, a 38.90 ns arrival spread
   against a 3.1983 µs axial period, coherence 0.9996. For the ion processor, a
   4.220 ns turn-around against a 55.9366 µs analyser period, crossing the mirror's own
   aberration limit at 48 oscillations.

15. ~~**Finish the examples corpus (EX-1).**~~ — **met.** 37 against the thirty §5 asks
   for, and the gate (EX-2) is built and green at about 51 s. What the first seventeen
   cost was mostly *deciding what can honestly be asserted*, and that work is done. The
   three named as remaining are all shipped: `mr-tof-oscillations`, `thermalisation` and
   `parallel-plate-gap-3d`.

   Breadth beyond thirty is now an ordinary way to add a check rather than an outstanding
   deliverable — `bounded-accelerating-gap` was added the same night the region was built,
   which is the loop working as intended.

   ~~The last is deliberately deferred~~ — **`parallel-plate-gap-3d` now ships**, which
   is the deferral closed by the Galerkin coarsening entry: two square plates in a cubic
   box, reducing to neither a cross-section nor an axis, reproducing
   `sqrt(2 d m / (q E))` to **a part in a million** in under two seconds. The whole
   gate is 27 examples in 42 s.

   **Two mistakes cost three orders of magnitude each, and both are in the example's own
   description because both are things a model author makes.** The gap in the closed
   form is between the *facing surfaces*, so placing a 1 mm plate's centre on the gap
   boundary makes the real gap 9 mm — the field came out **11.111% high**, which is
   exactly 1000/0.009. And the applied voltage has to be split as ±V/2 rather than V and
   zero, because **the grounded domain boundary is a third electrode**: holding one
   plate at zero makes the boundary an extension of it, and the problem is asymmetric
   about the mid-plane although the geometry is not. That is worth 0.31% of the field at
   the ends of the flight and 0.11% of the answer; splitting it gives 0.0005% and
   1.2e-6. **Both were mesh-converged** — identical at 1 mm and 0.5 mm cells — so
   neither was a discretisation artefact a finer grid would have removed, which is what
   the first reading assumed.

   ~~Remaining: an MR-TOF and a thermalisation.~~ **`thermalisation` now ships** —
   0.039339 eV against (3/2)kT = 0.038778 on 240 ions, 1.45% high against a 5.3%
   standard error. It needed a new figure, `meanKineticEnergy`, because equipartition is
   the sharpest check the collision models have and was measured only in a unit test.

   **Building it found two defects, both larger than the example.** A declared gas took
   no part in *any* figure of merit — the single-ion path never built a collision
   sampler — so `run` and `test` disagreed by 95 µs on every model with a gas. And
   `gas-flow-carry`'s tolerance was written as if absolute where the format compares a
   relative error, so it admitted any positive answer: **an example in the release gate
   that could not fail.** Both recorded in `docs/lessons.md`.

   ~~Remaining: an MR-TOF.~~ **`mr-tof-oscillations` now ships** — energy drift
   **7.05e-11 over fifty crossings of a declared field discontinuity**, a hundredfold
   inside ACC-4, with teeth: the drift accumulates with reflections, 1.55e-11 at one
   crossing pair against 7.05e-11 at fifty, so it is not sitting at a floor. It also
   asserts the flight time to **1.6e-13** against a closed form, deliberately — that
   number is the drift distance over the drift speed and contains nothing about the
   mirrors, which is a trap this document already records, so the example *documents*
   the decoupling rather than pretending to measure focusing. A real analyzer fixes the
   oscillation count, which the model format cannot declare.

   **Corpus 29 to 30, and the corpus can carry a data file now.** The embedded-resource
   glob was `*.json` only, so neither imported gas field — velocity or pressure — could
   appear in an example at all, and so neither could be covered by the EX-2 gate that
   runs on every change. `ExampleModels.Assets`/`WriteAssets` write an example's data
   files beside its model, under their whole file name so two examples cannot collide
   over a `pressure.vti`.

   `drift-tube-pressure-gradient` is the first: a 38 mm tube whose gas thickens from
   1 mbar at the packet to 2 mbar at the detector. **The expectation is an integral this
   engine has no part in** — the drift speed is `mu_ref n_ref E / n(x)`, so the transit
   is the integral of `n(x) dx` over `mu_ref n_ref E`, which is the uniform answer
   scaled by the *mean* density along the path; for a linear ramp that is the average of
   the ends, 1.5. Predicted 316.667 us, measured **320.236, 1.13% out** — the packet's
   own spread, matching the 0.86% the uniform drift-tube example already reports.
   Discriminating far past its 5% tolerance: ignoring the gradient gives 211 us, a third
   away.

   What it deliberately cannot see is the *arrangement* — a drift transit depends only
   on the integral along the path, so any reflection of the profile gives the same
   answer. That is a property of the physics rather than a weakness to design away, and
   it is why the arrangement is pinned separately by the reversed-ramp unit test.

   **It needed one change below the corpus, and that change is the more useful half.**
   `einzel test` could not test a model with an imported field at all: the seam between
   a study and the transport is a `Func<CompiledModel, double?>` with nowhere to put a
   path, so a figure of merit met `BackgroundGas.FromModel` and was refused. A compiled
   model now carries `SourceDirectory` — where its document was read from — set by every
   loader, so any consumer can resolve a referenced file. **Null stays the safe value**:
   a model compiled from a string has no directory and its consumer is refused rather
   than run in a gas the document does not describe, so a loader that forgets degrades
   to the refusal.

   **And the four study drivers take it too**, so a sweep, scan, optimisation or
   boundary search over a model with an imported field runs rather than refusing —
   §13's whole subject is a design being optimised, and a device with a gas jet through
   it is exactly the kind that wants optimising. The warning survives that seam: the
   ledger reports `gas.pressure-imported` with its per-evaluation count, which is what
   distinguishes a corner of the box from every draw.

   **`sequenced-uniform` closes the sequenced-extraction gap named below**, and it is
   the sharpest sequenced check the corpus has: an ion held at rest in nothing, then
   pushed by a uniform field a phase switches on. Predicted `hold + sqrt(2 d m / (q E))`
   = 5.219358580 us, measured **5.2193585800816775 - 1.6e-11 relative**, five orders
   inside its tolerance, because an analytic field has no geometry error to absorb where
   the plate version carries 1.0e-7 of fringe. It is also the corpus's only model written
   with the **model-level `sequence`** and the only one whose timeline moves an
   **analytic** element. Its teeth were measured rather than predicted: with the
   analytic-phase fix reverted it is refused at validation - "the accelerating potential
   may only be zero when a field can accelerate the ion" - so it fails before the ion is
   launched, and that one refusal guards both that defect and the fifth occurrence of the
   can-anything-accelerate check reading only one configuration.

   **All the named remaining examples are done.** The count is 32 of thirty by number,
   which is the wrong way to read it: the list said "four are breadth" and then named
   three, so what thirty means was already the open question rather than which example
   is missing. **Recommend restating EX-1's target as the coverage it wants rather than a
   number** — what is genuinely uncovered is a multipole above four rods in the
   *diffusive* mode, a sequenced extraction, and a 3-D geometry with a drive.

   The three added most recently set a pattern worth keeping. **`travelling-wave-capture`
   and `travelling-wave-ballistic` are a pair, and neither is worth much alone**: a
   transit matching the wave in one case and the injection speed in the other would be
   a coincidence twice over; a transit matching the wave *whatever* the injection speed
   is capture. And **`gas-flow-carry` is discriminating far past its ten per cent
   tolerance**, because a run that ignored the declared flow would not arrive at all —
   it would damp to rest and cover 15.8 mm in twenty milliseconds.

   Worth finishing, and worth noticing what the first tranche already returned: **two
   defects that no test written from inside the project would have caught**, because
   both were about a model that validates and answers a different question.

16. ~~**Galerkin coarsening, or operator-dependent interpolation**~~ — **built, and it
   restores the property multigrid is supposed to have.** `A_coarse = R A_fine P`: the
   coarse levels are built from the fine operator rather than from the geometry, so they
   cannot lose it. The finest level is untouched — it keeps its cut cells and its
   geometry-driven smoother, because that is where the accuracy comes from.

   On two 1 mm slabs at a 0.25 mm cell: **1 level and a 274,625-node bottom becomes 6
   levels and 27**, 45 cycles becomes 13, and 160 seconds becomes 13. **The cycle count
   stops depending on the mesh** — 14 at 65³ against 13 at 129³, where before it was 6
   against 45.

   **And it is the same answer**, which is what separates it from the fast wrong one.
   Deeper *rediscretised* coarsening was thirty times faster and gave 486 V of 100
   applied; the two hierarchies agree to 1.1e-7 to 4.0e-7 relative, the tolerance both
   were driven to.

   **Neither hierarchy dominates, so the solver picks.** Galerkin is 11.9× on the slabs,
   4.6× on four rods and **0.64× on a sphere** — a loss, because there the cheap
   hierarchy already reached a 4,913-node bottom and the 27-point stencil and the
   assembly are pure overhead. What separates the cases is the size of the bottom the
   cheap hierarchy can reach, which needs no solve to evaluate, so that is what the
   choice is made on. `SolveReport.Galerkin` says which ran.

   Still to do: the two-dimensional solver has the same seam and does not need it (it
   already reaches 9 to 99 nodes), and the 3-D corpus example is now affordable and not
   yet written.

   **The measurement that motivated it, kept because it is what made the case.** `Representable` stops coarsening once a coarse cell would exceed the smallest
   electrode dimension, and that is a *physical* size — so refinement adds levels at the
   top and never removes the bottom. The 3-D V-cycle descends **0 to 2 levels on every
   device geometry**, against 4 to 6 with no interior electrode, and the bottom level's
   node count does not fall as the mesh refines: two 1 mm slabs bottom out at **274,625
   nodes at 65³ and still 274,625 at 129³**. The shipped segmented quadrupole bottoms
   out at 9,537 nodes; the shipped 2-D templates bottom out at **9 to 99**. It is a
   three-dimensional problem, not a dimensional necessity — the two solvers use
   different coarsening rules.

   **The guard is load-bearing, and that was established by removing it.** Letting the
   0.25 mm slabs descend further takes the solve from 45 cycles and 145 seconds to 5
   cycles and 4 seconds — and to **486 V of 100 applied**. It reports converged at a
   healthy factor; only the maximum principle catches it. At four levels down a 1 mm
   slab is smaller than a cell and is pinned to a single node, so the coarse problem
   constrains the error at two isolated points where the fine problem constrains it over
   two planes. `R A P` inherits that structure through the operator instead, which is
   why Galerkin removes the guard rather than tuning it.

   `SolveReport` now carries `Levels`, `Sweeps` and `CoarsestNodes` so none of this is
   invisible again: a cycle at zero levels is four hundred sweeps over the finest grid
   and a cycle at five levels is a handful per level, and the convergence factors in
   `docs/numerics.md` were being compared across geometries as though a cycle were a
   unit of work.

17. ~~**Two narrower gaps, both stated where they bite.**~~ — **both closed.** The gas
   **density** was a single number for the whole model, so a differentially pumped
   instrument was not expressible: an imported field gave the neutrals a velocity
   everywhere and the same number of them everywhere. `pressureField` closes it — see
   the gas-pressure-field entry, which also carries the physics that was
   missing underneath it (mobility goes as 1/n) and the two tests that had no teeth
   until a mutation was run against them.

   ~~And the `solved3d` document form still spells one `drive`~~ — **closed.** A
   `solve3d` now takes `drives` and its electrodes take `taps`, so a volume geometry can
   express what a cross-section already could. **Shared rather than reimplemented**: both
   electrode documents implement one `ITappedElectrode` interface and the tap validation
   is one function, so the refusals for declaring both forms arrived in three dimensions
   by *being* the same code. That choice was made deliberately on the evidence of the
   same night's other finding — a computation copied across a seam is how a declared gas
   came to take part in a run and not in a figure of merit.

   Verified on a volume geometry: two generators reaching the same electrodes in the
   same proportions collapse to **one** basis solve carrying two weights on two clocks,
   and two distinct spatial patterns give **two**.

18. ~~**Class B analysis**~~ — **done.** `einzel boundary` bisects to ACC-6, the
   transmission-against-resolution curve closes onto the tabulated apex (Phase 3
   acceptance criterion 3), the **secular frequency spectrum** matches the Mathieu
   characteristic exponent to 0.007–0.144 per cent with both sidebands in place, and
   **isolation efficiency against notch width** is measured on an
   `RfWaveform.Harmonic` comb that independently recovers the published digital
   cut-off at q = 0.712.

19. ~~**A drive per supply rather than per solve**~~ — **done for 2-D.** A `solve`
   declares `drives` and each electrode `taps` them by name. The travelling-wave
   guide now carries both of its generators: 24 rings on a wave at 0.5 MHz and a
   confinement at 3 MHz reduce to **3 basis solves**, and the field reports the
   confinement's 333 ns as its shortest period rather than the wave's. **The
   confinement does not yet widen the acceptance** and the template ships with it at
   zero — the usable amplitude window is narrow at both ends and finding a working
   point is a design study; see Amendment 24.

20. ~~**A gas velocity field (GAS-1)**~~ — **both modes see one now.** VTK ImageData,
   sampled trilinearly, conserved at the face, agreeing with a declared uniform
   vector to two ulps; and the event-driven models no longer refuse it — the ion's
   position is carried into the neutral draw, so a collision samples the gas where
   the ion is. Checked against `u + μE`: the difference between a moving gas and a
   still one is **120.000 m/s against a declared 120**, with **−0.000** across it,
   and a flow field agrees with an equivalent `driftVelocity` to **1e-9** on the same
   seed.

21. ~~**A gas pressure field (GAS-1's last gap)**~~ — **done.** The density was the
   last quantity about a gas here that was a single number for a whole model, so an
   imported flow gave the neutrals a velocity everywhere and *the same number of them
   everywhere*. `pressureField` on the gas block, VTK ImageData like the velocity
   field, with **the unit required on the file** — §9's own rule, because a file read
   as pascals when it holds mbar is a gas a hundred times too thin and looks entirely
   plausible.

   The physics that had been missing: **mobility goes as the reciprocal of density**,
   and nothing here did that. μN is the constant, which is why the literature
   tabulates *reduced* mobility; the declared `pressure` becomes the reference the
   declared or derived mobility belongs to, and the field grades away from it. There
   are two separate density dependences and they are not the same one — this factor
   is how *much* gas, the existing E/N expansion is how hard the ion is pushed
   *between* collisions.

   **A graded density turns Langevin into a null-collision method**, which is the
   same mechanism hard spheres already used for a speed-dependent rate, reached a
   second way: schedule at the highest density anywhere, accept with probability
   n(x)/n_max. Both bounds are now majorants over the whole field, because an event
   is scheduled before it is known where the ion will be when it lands. The thinning
   is short-circuited where the density is uniform, and that is load-bearing rather
   than an optimisation: it would otherwise accept with probability exactly one and
   *still consume a random draw*, moving every seeded result this engine has
   published.

   | | |
   | --- | --- |
   | A field at 2× the declared pressure vs *declaring* 2× the pressure, event-driven | **bit-identical trajectory**, both collision models |
   | The same, through the diffusive path and the CLI | 3515.229021382981**5** vs **1** µs |
   | Mobility at half and twice the reference density | 2.000000 / 0.500000 |
   | The scaled form at the declared density | **bit-identical** to the unscaled one |
   | Langevin thinning at three points of a 4× ramp | 0.25 / 0.625 / 1.00 to 0.01 |
   | A field in mbar vs the same field in Pa | 1e-9 |
   | 151 existing transport tests | unchanged |

   **Two tests that had no teeth, found by running the mutation.** The equivalence
   test used Langevin only, and a mutation making the local density read return the
   declared scalar *did not fail it* — the Langevin branch short-circuits its thinning
   where the density is uniform, so a flat imported field never reads a position at
   all. Correct behaviour, and no test of the read. And the graded-gas test asserted
   only that a ramp collides more than the thin gas alone, which with the density read
   at the wrong place still passes because the count lands *close to* the thin gas.
   What discriminates is **reversing the ramp** — same densities, same box, opposite
   arrangement, so anything blind to position gives the two an identical count.
   11,458 against 19,700.

   **And the same seam broke a fourth time, in the file whose comment says it is the
   third.** `SampledOutsideDensity` was added to `CollisionSampler` beside
   `SampledOutsideFlow`, and on the first draft was dropped in exactly the place the
   surrounding comment warns about — declared, set, read by nothing, everything
   compiling and every test passing while a run extrapolating its gas past the imported
   box said so nowhere. Reading the comment is not the same as being protected by it.
   Now `gas.pressure-extrapolated`, with a CLI test that drives it end to end, because
   the wiring is what keeps breaking rather than the computation. The rule needs a
   second half: **adding a quantity to a type that already reports several is not the
   same as reporting it**, and the existing reporting code is exactly where the eye
   slides past.

   **The cost gate had to be re-derived, and the first version was 50% out.** GRD-8's
   claim for this mode is that `estimate` and `run` call the same step function and
   agree exactly. A graded gas moves the mobility and so both stability limits — and
   the first version took the thinnest gas *anywhere in the imported field* where the
   run takes its limit from per-node arrays *over the tracked grid*. A CFD field is
   usually solved on a larger box than the ions are tracked through: here it ran to
   0.5 mbar while the grid reached 0.75, and the estimate said **2,252 steps against
   an actual 1,502**. Now 1,126/1,126 uniform and 1,502/1,502 graded. Found by
   comparing the two numbers, not by reading the code.

   The same asymmetry runs through the diagnostics and had to be got right in both
   directions: **E/N is worst where the gas is thinnest**, the **Knudsen number and
   collision counts are worst where it is thickest**, and reading the declared
   pressure for either reports a regime the instrument is in nowhere.

   **A refusal moved to where it cannot be forgotten.** Resolving a declared field
   needs the model document's directory, which a study or a figure of merit does not
   have; the rule to refuse rather than run in a gas the document does not describe
   was right, but lived as a guard at each of four call sites *naming `velocityField`*
   — and three were already silent about a pressure field.
   `BackgroundGas.FromModel` now refuses an unresolved field itself, with
   `WithoutImportedFields` as the deliberate exception whose name says what it gives
   up. It immediately caught a real one: `einzel run` on a diffusive model reached
   `FromModel` through `GasFlowImport.Resolve` itself. Two call sites that *did* have
   the path — `einzel compare` and the diffusive cost estimate — now resolve rather
   than refuse, which they should always have done.

   **Still assumed: one temperature.** What is imported is a pressure field read as a
   density field at the model's single declared temperature. That assumption was
   already made by there being one `temperature` in the document, but it is now the
   only thing about the gas that cannot vary from place to place.

22. ~~**The live session (MCP-1)**~~ - **done, and the work was not the protocol.**
    `journal`, `undo` and `attribution` existed only in the `Einzel.Commands`
    assembly *description string* - the same "named in a csproj and nowhere else"
    state `ITransportMode` was in before its seam was built. So "build MCP" was
    really "build the journal, then put a protocol on it", and §15 says as much: the
    server's distinct value is shared live state, and "everything else it could do,
    the CLI does at least as well and with less machinery". A journal only one party
    can write to is a file, and a file needs no server.

    **Attribution comes from the `initialize` handshake, not from a tool parameter.**
    An `author` argument would make the attribution something the *mutating party
    fills in*, which is a signature rather than an attribution - an agent could sign
    a change as the person it is working with, by mistake or because a model decided
    that read better. The client declares itself once, before any tool exists to
    call. The test is in two halves and the second is what makes the first a property
    rather than a default: the name that comes back is `agent:surveyor/3.1`, **and**
    `model_edit`'s schema has exactly `description` and `content`, so there was no
    argument through which another could have been offered. A tool that took an
    author and ignored it would pass the first half alone.

    **Shared and linear are two claims.** Shared means one stack rather than one per
    party, which is the point rather than a hazard: two private stacks over one
    document would let each party reverse changes the other had already built on, and
    the document would reach a state neither of them authored. Linear falls out of the
    walk back being over ordinary edits only. And **undo appends rather than pops**,
    because a popping stack loses the fact that somebody undid something, and who -
    which is exactly what MCP-1 asks to be recorded.

    All three checked by mutation rather than by assertion count: private per-author
    stacks fail three of six journal tests, a popping undo fails a *different* two,
    and moving attribution into a tool parameter fails three of five protocol tests.

    **The tool surface is deliberately not a second CLI**, and the server says so in
    its own instructions - the failure to guard against is an agent looking for `run`
    and `sweep`, not finding them, and concluding the platform cannot do those things.
    Every result is `CommandJson.Write` of the same outcome record the CLI serialises
    for `--json`, asserted **byte for byte**, which makes AGT-2 literal instead of
    claimed and carries GRD-2 for free: a warning reaches an MCP client by being on
    the record rather than by anyone remembering to copy it across. That is the seam
    this project has already dropped evidence at three times.

    **What remains needs the shell.** §15 makes streamable HTTP hosted in process the
    primary transport and stdio "a convenience" - the right ordering for a finished
    platform and the wrong one to build in, since the convenience runs today. The
    tools are built above the transport, so adding HTTP is a wrapper. A full `run` is
    also held back deliberately: it belongs where there is a progress surface and a
    viewport to put the answer in, and `einzel run` is one process launch away
    meanwhile. The shell now exists and has a viewport, so that holds back nothing but
    itself.

    **The first non-test dependency the project has taken**, and §20's table asks for
    the licence to be verified rather than assumed: `ModelContextProtocol.Core` 2.2.0
    declares Apache-2.0 as an SPDX expression in its own nuspec, and its whole
    transitive closure is ten `Microsoft.Extensions.*` packages, all MIT. LIC-1 clear.

## Open decisions

§23's list, with what has been settled since.

| Decision | State |
| --- | --- |
| Availability on NuGet, GitHub, and as a mark | Open |
| Spike the FLD-1 linearity assumption before Phase 2 | **Closed.** Run, failed, fixed by cut cells. The two-week estimate was right and the spike returned a negative result first, which is what a spike is for |
| Whether hidden-line 3D vector output is worth building | Open, and cheaper to leave open than it looks: there is no raster path either |
| Whether the provenance stamp on figures is default-on | **Closed in practice** — default-on, with the taint on the page |
| Whether to write the VTU writer or take a dependency | **Closed.** Written. Under a week, and it now writes fields, trajectories, 3D volumes and densities |
| What triggers revisiting code signing | Open |
| What the defect-floor policy file contains | Open, and untestable until there are releases |
| What the agent acceptance suite measures and what gates a release | **Closed.** See Amendment 11 |
| Whether the in-process extension runner is worth shipping at all | Open, and the evidence so far says sandboxed-only is sufficient: nothing has hit the 49 ms granularity floor |
| Whether the funnel benchmark uses a published geometry or one of ours | **Closed: published.** The PNNL 100-electrode funnel of Kim et al. 2000 and Page et al. 2006 - dimensions fully in print, two measured curves (transmission against RF amplitude; low-m/z cutoff against frequency and DC gradient), a closed form for the second, and a SIMION comparison on the same family (Lynn et al. 2000) for the cross-code check §19 wanted. Shipped as `pnnl-ion-funnel.json`; the register is `docs/literature-targets.md` §5. First results pending. |
| Governance if this becomes a collaboration | Open |

---
