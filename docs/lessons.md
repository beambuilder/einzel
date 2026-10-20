# Lessons

Bugs that presented as physics and turned out to be arithmetic, and mistakes in
how things were measured. Each is recorded because it cost real time to find and
because none of them announced itself — every one produced a plausible number.

Ordered roughly by how much they would cost someone who hit them again.

## Narrowing a guard means asking what every caller will then be told

`SessionJournal` refused any edit that did not validate. Section 16's live
validation required narrowing that to refusing only what does not *parse*, which was
right - and I checked the window, saw it showed validity through its own refresh, and
stopped.

The MCP path had no equivalent. `model_edit` went from **refusing** an invalid edit
loudly to **accepting** it and returning sequence, author, description and journal
with nothing saying the model no longer validated. An agent tuning a parameter past
its bound now got the same response shape as a good edit.

Strictly worse than before the change, and produced by a change that was correct in
its own terms. The guard had been doing two jobs - preventing an invalid state, and
*telling the caller* about one - and only the first was the thing being removed.

**The rule: when a guard is narrowed, enumerate its callers and ask what each will be
told afterwards.** A guard that refuses is also, incidentally, a guard that informs;
remove the refusal and the information goes with it unless something replaces it.

This is the sixth time evidence about a computation's own quality has been dropped at
a seam here - after `FieldAssembly.Build` discarding its `SolveReport`, the sweep
evaluator discarding warnings, `CollisionSampler`'s `BoundExceeded` and
`SampledOutsideFlow`, `SampledOutsideDensity` declared and never read, and
`DriveAmplitude` becoming a summary. The others were all *omissions*. This one was a
**removal**, which is why the existing habit - "make the shortest spelling the safe
one" - did not catch it: the shortest spelling was already safe, and I made it
shorter.

**A green suite is what let it through.** Nothing failed, because nothing had ever
asserted what an MCP client is told about an edit's validity - there was no such
thing to assert until the guard moved. The test that exists now would have failed the
moment the guard changed.

## A validity check stricter than anything the spec asked for

`SessionJournal` refused any edit that did not validate. The argument was written down
at the time and reads well: in a shared session an unrunnable model is not one
party's problem, because the other party's next action is against whatever is on
disk.

Section 16 contradicted it. **Live validation needs an invalid state to be
reachable.** A person typing 500 into a parameter bounded at 50 has to see the tree
standing with the complaint against it - being *prevented from typing* makes the
editor most useless at the moment it is most needed. And refusing every invalid
document forbids any edit **sequence** that passes through one: widening a bound and
then setting a value beyond the old bound works in one order and is refused in the
other, for no reason a person could infer from anything.

The platform's own rule settles it, and it was already written down four times over:
**taint, never block.** A preview result, a decimated figure, a defective engine
version, a coarse boundary search - all keep working and carry a non-suppressible
mark. The platform never stops you working; it refuses to let a result look cleaner
than it is.

`Check` now refuses only a document that does not **parse** - there is nothing there
to be wrong, the next party cannot read it, and no edit through the journal could
have produced it. Validity is reported through `SessionJournal.Validate()` instead.

**The general shape**: this was a guard invented while building one feature, justified
by a real argument, that turned out to be stricter than any requirement. It cost
nothing until a later requirement needed the state it forbade - and then it was found
by a *test failing for the right reason*, which is the cheap way to find one.
Guards written from first principles rather than from a requirement are worth
re-reading when a new requirement arrives, because the argument that justified them
is usually still true and still outweighed.

## A refusal about the model, thrown as an argument exception, is reported as a defect in the engine

The shipped Astral with its volume solve at a 1 mm cell is 1025 x 65 x 1025 = 68.3 M nodes
against a 64 M limit. `validate` said OK, `estimate` priced it at 35 minutes, and `run`
spent more than five minutes and several gigabytes before printing `INTERNAL_ERROR:
ArgumentOutOfRangeException ... coarsen it or shrink the domain` above "This is a defect in
einzel, not in your model", on exit code 6. The message was right. Everything around it was
wrong, and there were four separate mistakes in one guard.

**The exception type decides the whole presentation.** `ArgumentOutOfRangeException` is the
natural spelling in a constructor, and at the process boundary an unrecognised exception can
only be a defect - that is what the catch-all is for. So a limit a *document* can exceed is a
refusal about the document: an `EinzelException` with a code a caller branches on, and it
belongs where the document is read. The second time this exact shape has happened here, after
Amendment 15's "a peak needs at least two arrivals", which also made a run report *itself* as
broken.

**The guard ran after the cost it guarded against.** It sat in `ScalarField3D`, and a solve
builds its conductor mask first - `DirichletMask3D` and `CutLinks3D`, 105 bytes a node against
a field's eight. Measured by moving the guard back: refusing a single-sphere geometry at the
Astral's mesh became a **3.5 minute** test run, build included; with the guard on the mask it is
refused having allocated under a megabyte, which is what the test asserts. A resource guard
belongs at the *first* allocation, not at the one whose type sounds like the resource.

**Three verbs had three answers to one question.** `validate` never counted nodes, `estimate`
counted them and priced them, and only the solver refused - and the mesh is arithmetic on the
document, so all three could have known. The same vacuous clean bill of health as `solve`
answering `converged: true` over nothing, and a source inside an electrode validating: **a
refusal that is computable from the document and is not made by validation is a refusal made
too late**. One constant in `Einzel.Core`, read by the validator and the solver's backstop, and
`estimate` validates first, so it cannot drift.

**And the suggestion needed its own care, in two directions.** The finest fitting cell size
sits exactly on a power-of-two boundary. Three significant figures rounded 1.46484 mm up to
1.47 mm, which also crossed the *other* axis's boundary at 1.46875 mm, so the suggestion named a mesh
with half the nodes while calling it the finest that fits - the opposite of the estimate's own
earlier trap, where twice the spacing landed *on* the boundary and changed nothing. And an exact
decimal is not safe either: over a box from -1.5 to 20.5 mm, 2.75 mm is the eight-interval
boundary, and converted through `mm` it gives 8.000000000000002 intervals - sixteen, the refused
mesh. That case was found by searching boxes for one, not by thinking about it. **A number
printed for a person to type back into a step function has to be checked after the round trip
through its own text and its own unit.**

A fifth thing came out underneath: `Grid3D` doubled an `int` interval count, which for a
picometer cell wrapped to zero and never returned. Harmless while only a solve called it; a
validator that counts nodes calls it on every document. **A function safe for its one caller
stops being safe when it is moved to where everything calls it.**

**And the test written for it broke a test in another class.** It passed `--threshold 1e12`
to `estimate`, and the threshold is process-wide: the CLI tests drive `Program.Main` in one
process, so `CostGateTests`' refusal test, running later, inherited a cost gate of ten to the
twelve seconds and exited 0. That class resets the threshold in its own `Dispose` and says why,
which is a convention every caller has to remember - and the next caller, me, did not. My first
fix restored it however the invocation ended, which a review rightly called a patch on a global:
it fixed the one caller and left the setting for the next. It is a parameter now. **A
per-invocation choice belongs in the invocation's arguments; a process-wide setting that one
call moves is a setting every other call has to defend against.**

### And a review of the fix found the fix's own crash

Moving the mesh arithmetic into validation is what made it correct, and also what exposed it
to documents it had never met. A derived cell size or bound can be NaN - a division by a
parameter set to zero, the square root of a negative ratio - and every comparison with NaN is
false, so `cell <= 0.0` and `max <= min` let it straight through. Before, the solver's own
argument check caught it at `run`; now the validator's mesh count threw from inside
`validate`, so the command whose job is to report what is wrong with a model printed
`INTERNAL_ERROR` instead. Infinity was quieter: it passed every check and solved on two
intervals an axis. **A check moved earlier meets every input the later stage never saw, and
the inputs a later guard used to filter now arrive unfiltered. And `x <= 0` is not a
positivity check: `!(x > 0)` is, and a finite value is a separate question.**

The same review found three smaller things with one shape - each was the fix disagreeing with
something near it. The space-charge warning still advised grids of 1024 across that the new
validation refuses. `estimate`, like eleven other call sites, reported only the first
validation error, so a refused mesh could hide behind an earlier mistake. And `estimate`'s
mesh note kept its own copy of the rounding rule, with a different minimum, a few hundred lines
from the shared one it now uses. **After adding a limit, ask what else names a quantity that
limit applies to**; advice, reports and restatements do not update themselves.

## A confinement test on a geometry that cannot confine

Checking that a driven geometry in a diffusive phase gets the *cycle-averaged* field
rather than a snapshot of the RF needs a geometry where the two differ. The first
version used two parallel plates driven in phase, released a packet 1.5 mm off axis,
and asserted the packet ended closer to the axis than it started.

    drive on   1.1939 mm
    drive off  1.1954 mm

It passed. The threshold was "less than where it began", and 1.1939 is less than
1.2, so it passed on a **0.1 per cent** difference that has nothing to do with the
claim.

The physics says why, and says it before the run: **two plates give a nearly uniform
field between them, and the ponderomotive force goes as the gradient of E squared.**
No gradient, no well. The test could not have discriminated at any amplitude,
because the geometry has nothing for the effect to act on.

Four rods, pairs in antiphase - a real quadrupole - gives:

    drive on   0.2341 mm
    drive off  1.5000 mm

**Two rules.** Before writing a test that an effect is present, check the geometry
can produce it - the closed form for the effect usually says so in one line. And a
threshold set just past the starting value is not a measurement: the drive-off
control is what turns "it moved a bit" into "it moved an order more than nothing
does", and it should have been there from the start rather than added after the
first version passed.

### And the control ran the driven case twice

The drive-off run was built by replacing `"value": 300` in the model - after the
model had been changed to 400 V. The replacement matched nothing, so both runs were
the driven one and their centroids were identical. **Caught only because the test
asserted the two differed**, which is the same `Assert.NotEqual` guard this project
has now needed four times for string surgery in a test. The amplitude is a
placeholder now, so there is no literal to fall out of step with.

## A fix written as a list of known modes learns each new mode the hard way

Wiring a third kind of run to the CLI walked straight into two defects this project
had already found and fixed once each.

**The exit code.** A successful sequenced run exited 4, `ConvergenceFailure`. The
mapping reads:

    run.Outcome is "StopConditionMet" or "DensityEvolved" ? Success : ConvergenceFailure

`DensityEvolved` is in that list because a working *diffusive* run once reported
itself as a failure - the logic knew only `StopConditionMet`. The fix was to add a
string, so the next mode added a third.

**The printer.** `flight time NaN +/- NaN`, `energy drift NaN`, `steps 0`. Those
lines were made *absent* for a diffusive run - a density has no flight time, and a
reader cannot tell a missing measurement from a failed one when both print the same
way - and that fix was gated on `run.Diffusion is null`. A sequenced run has that
null too.

**Both are the same mistake**: the fix was written as a list of the modes known at
the time rather than as the question actually being asked. "Did this run finish what
it was asked to do" and "is there a flight time to print" are properties of the
result; "which mode was it" is a proxy that stops being equivalent the moment a
third mode exists.

The list is now three long. If a fourth kind of run is added, the thing to change is
the question rather than the list - and the comment in `Program.cs` now says so,
because the next person to add a mode will find the list before they find this page.

### And a measurement mistake of my own, in the test for it

The test asserted the conversion warnings appeared in `Stdout`. They appear on
`Stderr`, because CLI-2 puts results on stdout and diagnostics on stderr. It passed
my manual check beforehand only because I had run the command with `2>&1` -
**merging the streams to look at the output destroys the very distinction the
contract is about.**

## Four helpers already existed, and writing them again got two of them wrong

`SequencedRun` needed a density grid, a seed, absorbing cells, domain edges and a
mobility. `DiffusionRun` had all five. I wrote four of them again, and two came out
different:

- the grid, built with `new Grid2D(...)` where `GridFor` uses `Grid2D.OverBox`,
  which **rounds each axis up to a power of two** - so one model got two different
  grids depending on which path ran it;
- the mobility, which ignored `Derived` - so a mobility the document derived from a
  cross section came back as the *stored* zero-field value rather than the one
  re-derived against the gas actually resolved for the run.

A third was not wrong so much as absent: the diffusive leg passed **no absorbers**,
so electrodes did not absorb during a diffusive phase. That is precisely the defect
that once made every diffusive transmission an upper bound with nothing saying so,
reintroduced locally a year later by someone writing the call from memory.

None of the three would have failed a test of the new code. Each produces a
plausible answer that simply differs from what `einzel run` gives for the same
model - which is the same shape as `run` and `test` disagreeing by 1.3e-10 in energy
drift, and has the same fix: **collapse to one implementation.**

**What makes this worth writing down is how it was found**, which was not by a
failing test. It was by asking what the CLI would have to report and noticing the
answer would have to come from somewhere - and the somewhere already existed. A
duplicate is invisible from inside the code that contains it.

## A stage moved a global parameter and only its own element followed

The sequencer's documented rationale is that a stage sets a *parameter* rather than
electrode settings, because "potentials are already expressions over parameters, so
setting one moves everything that depends on it at once - including the derived
parameters". That is the argument for the whole design, and across elements it is
false.

`CompileStages` re-resolves the **whole model parameter surface** with the stage's
overrides, and then re-expands **only that solve's** electrodes against it. So a
model with two elements, an electrode in each written as the same expression
`"potential": "volts"`, and stages on the first:

    A base potential: 300      B base potential: 300
    A during push:    900      B during push:    300

Two electrodes with identical expressions holding different voltages, on a model
that **validated cleanly**, with no diagnostic anywhere.

Found while scoping SEQ-1, not by a failing test - the question was where a
transport mode could live, and the answer turned out to be that the timeline is
already in the wrong place. **A mode is a property of the run, not of one electrode
assembly**, so a per-element stage cannot carry one: two elements would name
different modes for the same instant, and there is no superposition of transport
modes the way there is of fields.

**Refused first, then fixed.** The refusal - a sequenced model may have one field
element - was the honest state while the two coherent readings were open. The
right one is the documented one: the timeline is the instrument's, and every
element recompiles against each phase.

`Timeline` now resolves the phases **once for the model**, before any element is
compiled, and hands the same parameter surfaces to all of them. That also fixes a
second thing the per-element version got wrong: a malformed stage used to be
reported once per field element, turning one typo into a wall of identical
complaints.

### And the first fix reached only half the elements

A code review of that change found the same defect still live one layer along.
`CompileField`'s analytic branches - `fieldFree`, `uniform`, `halfSpaceUniform` -
compile from the base parameter surface and never looked at the timeline at all,
because a `CompiledField` for those kinds has nowhere to put a phase. So a model
whose sequence set a parameter used by a `halfSpaceUniform` cap potential had the
solved elements follow and the analytic one frozen at baseline, validating
cleanly. A model whose *only* elements are analytic compiled a full timeline that
nothing consumed - the sequence was a silent no-op.

**The comment that hid it is the part worth keeping.** `Restage` was a closure
"because only the solve branch needs it, and threading the declared parameters
through every field kind to reach it would put the sequencer in the signature of
things that have nothing to do with it". That argument reads as sound and is
exactly backwards: the analytic kinds have everything to do with the sequencer,
because a phase gives them different numbers. A rationale written for one version
of the code kept the next version from noticing what it had missed.

Fixed with `SequencedField`, a generic switch, rather than a per-phase branch
inside each analytic field. A special case layered on shared infrastructure is
the shape a fix takes when it is not deep enough, and there would be one more of
them every time a field kind is added.

Two refusals remain, and both are about a document saying two things at once. Two
elements each declaring stages is two timelines over one instrument. And declaring
both the model's `sequence` and an element's `stages` is refused rather than
merged - the same argument that refuses a geometry declaring both `drive` and
`drives`.

**Latent is not the same as harmless.** No shipped model had two field elements,
so nothing existing was wrong - but a two-element sequenced model is a perfectly
ordinary thing to write, and the wrong answer it gave was a plausible one.

## A harness that lies in the direction of "the thing under test is broken"

The MCP server, driven by hand - a file of JSON-RPC piped into it - produced
**nothing at all** on stdout. Not a malformed reply, not an error: silence. That
reads unambiguously as a server that does not work, and the next half hour went
into the server.

With a logger attached the SDK said both requests were handled and both responses
were sent. What happens is that a file on stdin hits EOF immediately, the transport
tears down, and the outbound writes are dropped on the way out. A real client holds
stdin open for the life of the session and never sees it. The harness was the
artefact.

**When a harness and the thing under test disagree, establish which one is the
artefact before changing either.** The trap here is that the harness was simpler,
and a simpler thing is easy to assume is the trustworthy one - but simpler meant it
was missing the one property (a stdin that stays open) the thing under test depends
on. What settled it in a minute was asking the SDK to narrate, rather than reasoning
about which side was wrong.

A second, smaller one from the same afternoon: `Environment.ProcessPath` under
`dotnet test` is the **test host**, which is itself an apphost, so passing it a dll
to run fails with "Failed to run as a self-contained app". Launch the apphost the
build already produces.

## The measurement that measured the wrong thing

The first comparison of bicubic against bilinear interpolation used a **solved**
field as the reference. On a coarse grid the solve carries its own O(h²)
discretisation error, and that error is larger than the interpolation error it is
supposed to be a backdrop for. The result: bicubic appeared **60× worse** than
bilinear, which is the opposite of the truth.

Sampling the *exact* potential onto the grid nodes isolates the interpolant, and
the ordering inverts to a 29–146× advantage the other way.

The general form: when measuring component A, the reference must not contain a
larger error from component B. Ask what else is in the number before believing
it.

## Fixing the wrong thing about the geometry

The first multi-reflection model stopped the ion at a detector a set distance
along the drift. That makes the arrival time equal *distance ÷ drift velocity*,
which depends on energy alone and not at all on the mirrors — so no energy
focusing is possible by construction, whatever the optics do.

The focusing fit named it unmistakably: c₁ = −0.500, c₂ = 0.3756, c₃ = −0.3133
is the Taylor series of 1/√(1+δ), which is free flight.

Real multi-reflection analyzers fix the **oscillation count** — converging mirrors
bring every ion back after the same number of passes — and the flight time is
that count times the oscillation period, which is where the four-penetration-depth
condition does its work.

The lesson is that a physically-plausible-looking model can be structurally
incapable of showing the effect being studied. Fitting coefficients and reading
them against a known series caught it immediately; a resolving-power number alone
would only have looked disappointing.

## A near-zero field defeats an adaptive controller

Launch an ion in the field-free middle of a mirror pair. Local acceleration is
almost nothing, so the step-size heuristic proposes an enormous step; the
embedded error estimator agrees it was accurate — **correctly**, for a straight
line — and the ion flies 39 metres through both mirrors without ever sampling
them.

The step was not inaccurate. It was uninformed. A gridded field carries no
information below its node spacing, so `IElectrostaticField.ResolutionLength` now
bounds the step and the controller cannot outrun the data.

An error estimator only measures the error of the model it was given. It cannot
tell you the model was never consulted.

## Declaring a discontinuity that is not there

`SolvedField2D` originally marked its whole domain boundary as a field jump.
Where a solve ends in a decayed field there is no jump — and two such phantom
surfaces a few microns apart, which is exactly what two abutting solve domains
produce, defeat the superposition's sign-product tracking: a step crossing both is
treated as crossing neither.

Cost: **2.6e-4 of an ion's energy**, four orders above the ACC-4 budget. It
presented as an intermittent transmission loss that was *non-monotonic in energy*
— −5% fine, −3% lost, +3% fine, +5% lost — which is the signature of step
placement rather than optics.

Declaring structure that is not there is not conservative. It is wrong in a
direction that also disables the machinery meant to handle the real thing.

## Clamping a stencil at a grid boundary

A 4×4 interpolation stencil reaches one node beyond the grid. Repeating the edge
node — the obvious choice — makes the interpolant **non-linear in the boundary
cell even when the underlying field is exactly linear**.

An ion enters and leaves a mirror through that cell twice per reflection. A
clamped stencil put **7.5 ppm** into the flight time of a mirror whose exact
solution is a pure ramp: over the entire ACC-1 budget, caused by nothing but the
corner case. Linear extrapolation of the ghost node took it to 1.9e-10, a factor
of 38,000.

Boundary cases in an interpolant are not edge cases when the geometry puts the
interesting physics at the boundary.

## Coarsening past the point where the geometry exists

Multigrid assumes coarsening preserves the problem. An interior electrode — a
rod, an aperture — occupies a fixed physical size, so each coarsening halves how
many nodes represent it, and past a few levels it is not represented at all. The
coarse grid then solves a *different* problem, and prolonging its correction back
drives the iteration apart. Four discs in a box reached **1e134 V**.

The tell, before divergence, is a convergence factor that **worsens with
refinement** instead of holding steady. A healthy V-cycle is grid-independent;
one that degrades is telling you the coarse levels are not solving your problem.

Three fixes were tried. Only the third works, and the failures are instructive:

- **Agglomeration** (fix a coarse node if anything in its 3×3 block is fixed) is
  stable, since growing the Dirichlet set only damps, but it grows the electrode
  a cell per level and roughly triples the convergence factor.
- **A flat depth floor** stops small grids coarsening at all, costing a
  33-node case its multigrid entirely.
- **A total fixed-node retention ratio** fails for a subtle reason worth knowing:
  a disc loses three quarters of its nodes per level, which is *exactly* the rate
  healthy coarsening produces, so the ratio stays flat right up until the rod
  vanishes. Counting only **interior** fixed nodes separates the case that must be
  limited from the case that must not.

## A search that fails must say so

The separation bisection in the mirror-pair study assumed four penetration depths
bracketed its root. True for one linear stage; false for two. It converged
silently on its own bracket ceiling and returned geometries that were never at
first-order focus.

Then the ranking made it worse: candidates were ordered by their second-order
coefficient, and an *untuned* mirror can have a small c₂ while its uncancelled c₁
destroys the resolution. So the ranking actively **preferred the failures**.

It presented as "two-stage barely helps, R = 260". After expanding the bracket
until it genuinely contains a sign change, and refusing to rank any candidate
whose first-order term is not actually cancelled, the same profile gives
R = 320,548. A three-order-of-magnitude error that looked exactly like a physics
result.

## A residual of exactly zero is not success

The FLD-1 spike reported a potential residual of **0.000E+000** for a sub-cell
geometry perturbation, and the obvious reading — perfect linearity — was exactly
backwards. The plate had moved less than one mesh cell, so the rasterised
geometry occupied identical nodes, the perturbed solve came back bit-identical,
and the derivative was identically zero. A tolerance study on that would have
concluded the parameter did not matter.

Exact zeros in a numerical result deserve suspicion rather than satisfaction.
They usually mean a quantity was never computed rather than computed to be zero,
and the two are indistinguishable in the number itself.

The underlying cause is fixed — the boundary now moves sub-cell, and the same
measurement returns a proper quadratic Taylor remainder — but the guard stays,
because what it detects is "the model never saw the perturbation", and that can
be true for reasons other than the one that prompted it.

The neighbouring case makes the same point from the other side: at 0.1% and 0.3%
perturbation an earlier fixture returned residuals of 0.2297 and 0.2297 —
identical to four figures. A residual that does not move when its input moves is
not measuring its input.

## A boundary condition that moves when you coarsen

A Dirichlet domain edge was implemented as a ghost node one cell outside the
grid, held at zero, with the edge node itself solved. On a single grid that is
perfectly self-consistent, and it had been right for months.

It is wrong the moment there is more than one grid. The ghost sits one cell out
at the fine level, two at the next, four at the next — so every level of a
V-cycle solves a slightly *larger domain* than the one above it, and the
correction it computes is for a different problem. A cap plate in a grounded box
diverged to **1e50 V**.

Two things about how it stayed hidden are worth more than the bug itself. First,
another limitation was masking it: the interior-electrode coarsening floor
stopped these geometries before they reached a second level, so the solver
silently fell back on plain Gauss–Seidel and reported a convergence factor of
0.83. Poor, but not obviously a bug — and "poor convergence" is exactly the sort
of number one shrugs at. Removing one limitation exposed the other, which is the
usual order of events.

Second, the diagnosis came from a two-by-two table rather than from reading the
code. Cut cells were the new thing and the obvious suspect; running the same
geometry with cuts on and off, and with Dirichlet and Neumann edges, showed the
divergence in the cut-free rows and in none of the Neumann rows. That is four
solves and it eliminated the entire feature that had just been written.

## Measuring the analysis in one mechanics and the trajectory in another

Normalised emittance is conventionally written as the geometric emittance times
βγ. Implemented that way, it drifted by **8.1e-7** across an accelerating stage —
a quantity whose entire purpose is to not change.

Two separate causes, both invisible until an exact test asked for exact
invariance.

**The paraxial term.** A divergence angle is transverse velocity over *axial*
speed, while βγ is built from the *total* speed. The two differ by a factor of
1 + y′²/2, which is around 8e-7 for a milliradian packet — and it does not cancel,
because damping shrinks the divergence and shrinks the term with it. Measuring the
area in (y, p_y) directly instead of scaling an angular area by βγ removes it
exactly: 8.1e-7 → 2.1e-8.

**The remaining 2.1e-8 was γ − 1 at the exit speed**, and it was there because the
analysis was relativistic while the transport is not. Relativistically an axial
force conserves γmv_y exactly, so v_y falls slightly as γ grows. Newtonian
transport holds v_y exactly constant, so γv_y grows. Carrying a γ in the analysis
measured the packet in mechanics the trajectory was not integrated in, and the
mismatch surfaced as an emittance that grew out of nowhere.

Dropping γ took it to **3.0e-16**, machine precision. What that gives up is bounded
and remote: γ − 1 reaches 1 ppm at around 460 keV for m/z 500, against the few keV
an ion-optical instrument runs at. If a relativistic transport mode is ever added,
the term has to come back — **in both places at once**, which is the actual lesson.
An analysis and the integrator it consumes must agree about the mechanics, and a
conserved quantity is the only thing that will tell you when they do not.

## A packet with no area has no orientation

A cloud with spatial spread and no temperature is perfectly parallel. Its emittance
is exactly zero — a real answer, not a degenerate one — but the Twiss α that
describes the *tilt* of its phase-space ellipse is undefined, because there is no
ellipse. It came out `NaN`, JSON cannot represent `NaN`, and the serialiser took
the entire result document down.

This is the **second** time that exact failure has happened here; the first was a
convergence residual. Both appeared only under `--json`, both were caught by a test
written for something else, and in both cases the field was one that is normally
finite and only fails in a case nobody pictured.

The fix that generalises is not another `Finite()` guard: it is that an undefined
measurement should be **absent, not zero**. Zero is a real emittance and a real α,
so a reader cannot tell a measured zero from an absent measurement if both print as
zero. The field is nullable and omitted when there was nothing to measure.

## Projecting onto half a basis

The rectilinear trap's field quality was measured by expanding the potential on a
circle in multipoles and comparing the largest unwanted term against the round-rod
quadrupole's. The helper was copied from the quadrupole study, where it projects
onto **cos(nθ) only**.

That is exact there and wrong here. Four identical round rods are four-fold
symmetric, so every sine term vanishes identically and a cosine projection loses
nothing. This trap has a slot in one plate and not the other: it is mirror
symmetric in x and not in y, and an asymmetry about the x axis lands **entirely in
the sine terms**. The cosine projection reported the odd orders as 1e-9 — which
read as "the slot costs nothing" — and the published figure named the 12-pole as
the worst aberration at 7.12e-3.

With both phases, the dipole is 5.43e-2, seven times the 12-pole and the largest
term by far. The headline changed from 296x worse than round rods to 2,258x, and
the attribution changed with it: the 12-pole is what flat plates cost, the dipole
is what the slot costs.

What makes this worth recording is that the near-zero was **evidence**, not
reassurance. Odd multipoles at 1e-9 in a geometry that is visibly asymmetric is
not a small effect, it is an absent measurement, and the right response to a
suspiciously exact zero is to ask what could make it exact rather than to bank it.
The same instinct is already in this file under a residual of exactly zero not
being success.

Generalises to: **a symmetry that makes half a basis redundant is a property of
the device, not of the method.** Reusing the reduced form on a device without that
symmetry measures a projection of the answer and reports it as the answer.

## A mirror plane the interpolant did not know about

The bicubic stencil reaches one node outside the grid and fills it by linear
extrapolation. That is already a considered choice - clamping was wrong and cost
7.5 ppm, and is recorded above. What was missed is that extrapolation is right for
a *Dirichlet* edge, where the data simply ends, and wrong for a **Neumann** edge,
which is a mirror plane: the field continues across it as its own reflection, so
the ghost is the node one step back inside rather than the ramp continued outward.

It surfaced on the axis of the first axisymmetric solve, where the radial field
must be exactly zero because there is no radial direction to point in. It read
**14 V/m**. An ion launched exactly on axis would have drifted off it - slowly,
plausibly, and in whichever direction the interpolant happened to lean. Reflecting
took it to exactly zero.

Two things worth keeping. The bug had been live in a shipped template all along,
because the mirror pair declares a Neumann edge too; it never failed a test because
no test asked what the field was *on* that plane. And the fix is one line per edge
that the original comment had already argued its way to the doorstep of - it says a
Neumann edge is a mirror, in the solver, three files away from the interpolant that
did not act on it.

Generalises to: **a boundary condition is a statement about the field, not only
about the solve.** Anything that samples the field afterwards has to honour it too,
and the place that is easiest to forget is the one where the answer is a clean zero
that nobody thought to check.

## Fixing an instance four times before fixing the class

JSON has no not-a-number and no infinity. Every result surface here is JSON, and a
single non-finite double does not degrade a document - it takes the whole thing
down, at the serialiser, after the run has already succeeded and the numbers are
in hand.

It happened four times, on four unrelated fields:

1. a convergence residual, when there was no order to resolve;
2. a Twiss orientation, for a packet with no phase-space area to be tilted;
3. a space-charge fraction, for a packet with no beam energy to be a fraction of;
4. the energy drift of a driven field, which reports not-a-number *deliberately*,
   because a field that does work on purpose has no conservation to diagnose.

Each was found the same way - a run that worked and a `--json` that did not - and
each was fixed where it was found. Three guards, then a fourth field nobody had
guarded.

The fix for the class is a converter: **a non-finite double is written as null.**
That is the policy the rest of the surface had already arrived at by hand - an
undefined measurement is absent, not zero, because zero is a real answer and a
reader cannot tell the two apart if both print as zero - so making it structural
costs nothing and closes the whole family. Reading is the mirror, so a stored
result still round-trips, which `verify` needs.

The lesson is about *when* to generalise. Once is an incident; twice is a
coincidence; by the third the shape of the class was already visible in the
comments being written, and the fourth was avoidable. A guard that has to be
remembered per field is a guard that will be forgotten.

## The evidence was computed and then thrown away

The segmented quadrupole lost its ion at q = 0.611 instead of 0.908 for a whole
revision, because a solve stopped short of its tolerance. The solver knew. It
returned a `SolveReport` carrying `Converged`, the cycle count and the final
residual — and `FieldAssembly.Build` wrote

```csharp
elements.Add(GeometryBuilder3D.BuildField(geometry).Field);
```

dropping `.Report` on the floor at the one seam every run, study and test passes
through. Downstream, an unconverged field is **indistinguishable from a converged
one**: it is a grid of plausible numbers with no marker on it.

Fixing the multigrid fixed the symptom. It did nothing about the reason nobody
noticed for a revision, which is the part that generalises. **GRD-2 exists for this
exact failure** — warnings propagate through engine, command layer, CLI and
exported files — and a seam that converts a reported result into a bare object is
where that requirement quietly stops holding.

Two ways out, and the choice depends on whether there is anywhere to put a taint:

- Where a **result** is produced, taint it. `BuildReported` hands back the field
  and its warnings, and `run` and `preview` carry them onto every number computed
  through the field.
- Where only a **field** is produced, there is nowhere to attach anything, so
  `Build` throws. "Taint, never block" is about results; a bare object with no
  envelope has no third option between refusing and concealing, and concealing is
  what it was doing.

The generalisable rule: **when a computation produces evidence about its own
quality, the type that returns it may not have a shape that makes discarding the
evidence the shortest spelling.** `.Field` was one character shorter than handling
the report, and that was the whole mechanism.

**It has now happened four times, and the fourth is the one worth reading.** After
`FieldAssembly.Build` dropping its `SolveReport`, the sweep evaluator dropping its
warnings, and `CollisionSampler.BoundExceeded` / `SampledOutsideFlow` being computed
and consumed by nobody — a pressure field was added, `SampledOutsideDensity` was added
beside its sibling on the sampler, and on the first draft it was **dropped in exactly
the same place as the two above it, in a file whose comment already said this was the
third time.**

Reading the comment is not the same as being protected by it. The property was
declared, set, and never read; the code compiled, every test passed, and a run that
extrapolated its gas density past the imported box said nothing about it. What fixed it
was not vigilance but a question asked deliberately after the fact — *grep for every
new public member and check something reads it* — and then a test that drives the
warning end to end through the CLI, because the wiring is what keeps breaking rather
than the computation.

So the rule needs a second half. The first is that discarding evidence must not be the
shortest spelling. The second: **adding a quantity to a type that already reports
several is not the same as reporting it**, and the existing reporting code is exactly
where the eye slides past.

A companion, found in the same review: `SolveOutcome.Converged` was
`Elements.All(e => e.Converged)`, which is `true` for an empty list. `einzel solve`
read only the two-dimensional element, skipped every three-dimensional one, and
answered `converged: true`, exit code 0. Vacuous truth is worse than a failure
because it terminates the investigation.

## Two arithmetic slips, for completeness

**Velocity fraction is not energy fraction.** v ∝ √E, so a fractional energy
offset δ is a fractional velocity offset of about δ/2 — and a factor of two in the
linear term is a factor of four in the quadratic. A hand estimate of the
single-stage resolving power came out 8× low because of it.

**A symmetric energy spread is not a symmetric velocity spread**, for the same
reason. Ions at ±5% energy do *not* arrive together at a first-order focus; the
residual is the second-order term evaluated at two different magnitudes. Asserting
they coincide would have been asserting the wrong physics. The closed form
ε²/(2(1+ε)) predicts a spread of 3.132e-5 and the integrator produced 3.132e-5.

## A bare double at the one seam every study crosses

`FiguresOfMerit.Evaluator` hands a sweep driver a `Func<CompiledModel, double?>`,
because ranking needs an ordering and there is no ordering on a GRD-1 envelope.
That much is right, and the discard was documented as deliberate.

What was discarded was not only the interval. The evaluator destructured the
whole `Measured` — value, uncertainty, evidence, **warnings** — and returned the
value. So every warning the flight behind a draw earned stopped there: a field
that missed its tolerance, a mode outside its validity, an integration that hit
its ceiling instead of the detector. A thousand draws could each have been
computed in a field that never converged, and the study would report a
distribution, a ranking, and nothing else.

The shape is familiar from `FieldAssembly.Build` discarding its `SolveReport`:
**a computation produced evidence about its own quality, and discarding it was
the shortest spelling.** `var (value, _, _, _) = measured` is one character
shorter than carrying the fourth field, and it reads as tidy.

Three things about the fix are worth keeping.

**The sink is per-evaluation, not per-emission.** An ensemble figure builds the
field once per ion, so a field warning arrives twenty-one times for one draw.
Counting emissions would report "on 21 of 3 evaluations", which is nonsense in a
way that discredits the whole line. The unit being counted is the draw.

**The count is the point.** "on 3 of 1000 draws" and "on 1000 of 1000 draws" are
the difference between a corner of the tolerance box and a study to throw away,
and a warning with no count cannot distinguish them.

**Taint, never block — but only where there is something to taint.** `Setup` used
to call `FieldAssembly.Build`, which *throws* on an unconverged solve, so a sweep
died rather than reporting. It now uses `BuildReported` when it has a sink and
`Build` when it does not, which is the honest reading of the rule: carry the
warning if you can, refuse if you cannot, and never drop it. A bare field has
nowhere to carry it, so for that caller the throw is still right.

The control test is the part that took a second attempt. Asserting that a clean
model's study warns about *nothing* failed — correctly — because a clean study
now reports the same convergence provenance a run does. That is the seam working,
not a regression. What the control has to assert is the absence of the specific
claim made about the strained model, which is a weaker statement and a true one.

## A formula that was right about the wrong mechanism

The space-charge screen converted a packet's self-potential into a flight-time
error as `½ × phi / V`: the self-potential is an energy spread, time goes as the
inverse square root of energy, so halve it. Every step of that is correct. The
answer was wrong by **527 times**, in the direction that under-reports.

The reasoning describes ions leaving a trap from different depths of the
self-potential well, which really does give them different energies and really
does give that timing error. It is not what dominates once they are flying. In
flight the packet **expands**: the self-field keeps pushing for the whole drift,
and the relative speed it imparts is set by turning the self-potential into
*relative* kinetic energy, sqrt(2 q phi / m). For 40,000 ions in a half-millimetre
ball at 4 kV that is 149 m/s. The old reading — perturb a 4 keV beam energy by
0.058 eV and see what it does to the speed — gives 0.28 m/s, because it is asking
what happens to an ion already travelling at 39 km/s rather than what happens
between two ions travelling together.

Both are questions about the same self-potential. Only one of them is the question.

**Nothing internal could have caught it.** The formula was dimensionally right,
monotone in every parameter, checked against a hand calculation, and covered by
three tests — all of which asserted the wrong relation with conviction. The unit
tests were the same mistake written twice. What caught it was building the direct
pairwise sum SC-1 asks for and comparing, and the first comparison failed by two
and a half orders of magnitude, which is a large enough gap that it could not be
argued away as a modelling choice.

That is what SC-1 is for, and it is worth stating in general: **a screening
estimate cannot be validated against reasoning, only against a computation that
does not share its assumptions.** Ours shared its assumptions with its own tests
for as long as those were the only thing checking it.

Two smaller things fell out of the fix.

**The inversion is a maximum where the forward direction is a minimum.** The
estimate takes `min(linear, escape)`, and `min(a, b)` is within budget as soon as
*either* is — so the population that satisfies it is the *larger* of the two
inversions. Writing the minimum in both places looked symmetric and reported a
population limit of three thousandths of an ion, which at least failed loudly.

**And the packet's total momentum is not the invariant it looks like.** The
obvious check on a pairwise sum is that internal forces do not move the centre of
mass. In a real flight that is false for two reasons that are not errors: the
applied field is an external force, and a detector removes momentum along with
the ion carrying it. Asserting on it was asserting that mirrors do not reflect.
The invariant that *is* exactly true is the balance of the mutual accelerations
themselves, checked at every stage of every step — which also covers the case the
naive version was reaching for, an indexing error over absorbed members.

## The same operator, written twice, conservative once

The cylindrical Poisson operator is written in conservative form — flux through a
ring's outer face minus its inner face, over the ring's own volume — and the
reasoning behind it is written down. Months later the drift-diffusion solver was
built on the same grid class, with the same `Cylindrical` flag, and its face flux
was computed per unit area and applied to both neighbours as though their volumes
were equal. In an axisymmetric solve they are not: a cell is a ring, so the ion
count crossing a face is created on one side and destroyed on the other.

The weight a face needs is `A_face · h / V`, identically 1 in the plane and
`1 ± h/2r` in a cylindrical one. **On the axis it is 4** — the inner face has no
area, so the cell is a disc rather than a ring. That is the *same* factor of four
the Laplacian carries on the axis, and it was already documented, one file away, as
the thing a plane operator gets wrong there.

**Three things about how it hid.**

*The tests were all Cartesian.* Every conservation check in the suite ran on a
plane grid, where the weight is exactly one and a scheme with no weights at all is
correct. They passed for a reason that did not generalise — the identical failure
mode as the uniform-field conservation test that hid a cell-centred drift sample,
recorded above.

*The ledger did not have to close.* An electrode emptied only the initial density,
and the ions it deleted were removed after the launched population had been
counted. So launched, collected, remaining and the named losses were never required
to add up, and a four per cent leak on the shipped funnel had nowhere to appear.
Making the itemisation complete is what made the defect visible; the fix to the
bookkeeping found the fix to the physics.

*It was worst exactly where it mattered.* The weight departs from one as `h/2r`, so
the error is negligible at the wall and total on the axis — which is where a funnel
puts its ions, and a funnel is the device this transport mode exists for.

**The check that discriminates is not the conservation figure.** A wrong weight can
still conserve to a few per cent over a short run, and the population sum was
99.9995% on the first off-axis fixture that exercised it. What cannot be nearly
right is the weight itself: exactly 4 on the axis, exactly `1 ± h/2r` off it. Assert
the quantity with an exact value, not the symptom with a tolerance.

**The rule.** A conservative discretisation is a property of an *operator on a
geometry*, not of a grid class. Sharing `Grid2D` and a `Cylindrical` flag with a
solver that got it right transfers none of it, and the second author of an operator
on the same mesh is the least likely person to re-derive the face areas.

## A guard that guarded nothing, under a test that could not reach it

Scharfetter-Gummel's exponent is `P = v h / D`, the ratio of drift to diffusion
across one cell, and it feeds a Bernoulli function `B(x) = x / (exp(x) - 1)`. That
function already handles a large argument **exactly** - it is zero above +40 and
`-x` below -40, which are the true limits and not approximations to them, and it
takes them explicitly to avoid an overflow inside `exp`.

The flux clamped `P` to ±40 *before* calling it. Reading the two together, the
clamp looks like it is protecting the exponential. It is not: the exponential
protects itself, one function down. What the clamp actually did was cap the
effective drift at `40 D / h`, so above a cell Peclet of 40 the density moved too
slowly by exactly the ratio the cap imposed.

**Every existing test ran below the cap.** The advection checks are at a cell
Peclet of 16 and report 1.000000. They are correct. They could not see this.

**What saw it was an expectation that was a division.** A corpus example - a drift
tube with a declared mobility and a declared gas flow, so `L / (mu E + v_gas)` is
arithmetic with nothing of the engine's in it - came out 6.7% long. Unclamped it
comes out 0.86% long, which is the packet's own spread, and the convincing part is
that the *same* 0.86% now appears with and without the flow. A residual independent
of the drift speed is a packet effect; a residual that grows with the drift is a
scheme effect, and only having both cases separates them.

**Three things generalise.**

A guard placed one level above the thing that already guards itself is not
redundant, it is a second policy - and the outer one wins silently. Look for the
inner guard before adding an outer one.

A test whose parameter sits below the threshold of the bug is not a weak test, it
is a test of a different regime. The suite's advection checks were not sloppy;
nothing about them says which side of 40 they are on. Where a scheme has a
dimensionless number in it, the tests should straddle the values that number
switches behaviour at, and should print it.

And an expectation that is *arithmetic the engine had no part in* catches a class
of thing that self-consistency cannot. That is the whole argument for EX-1's
corpus, and it paid for itself on the second batch.

## Reading the DC of an electrode that holds none, three times

A basis-superposed field is linear in the applied potentials, so an electrode's
excitation is *two* numbers — a DC potential and a drive amplitude — and code that
asks only about the first is asking about the half that is often zero. That has now
been the bug three separate times, in three unrelated places:

- **`einzel solve`, 3-D.** It iterated `Solve` elements and `continue`d past every
  `Solve3D`, then answered `Elements.All(e => e.Converged)` — vacuously true over an
  empty list. `converged: true`, exit 0, for a field it never touched.
- **`einzel solve`, 2-D.** Fixed in three dimensions and still wrong in two: it
  built one mask from the electrodes' DC potentials. For the shipped `quadrupole-rf`,
  whose every electrode holds zero DC, that was a solve of an earthed box — **peak
  potential 0 V, zero cycles, converged, exit 0.**
- **`ModelValidator.CanDoWork`.** A source at rest is legal exactly when some field
  could accelerate it, and the test was `Electrodes.Any(e => e.Potential != 0)`. So
  the **Paul trap**, the archetypal device whose ions sit still until the RF moves
  them, was refused as a model in which nothing could move an ion. The 3-D arm of
  the same switch fell through to `_ => true` and inspected nothing at all — the
  same bug wearing the opposite mask, one over-refusing and one under-refusing.

**Why it keeps happening.** `e.Potential` is the obvious spelling, it is correct for
every DC device, and every failure is silent and confident: an earthed box solves
fine, and a refusal names a plausible-sounding constraint. Nothing about the
symptom points at the drive.

The generalisable rule is the one already recorded under *the evidence was computed
and then thrown away*: **when a quantity has two parts, the shortest spelling must
not be the one that silently drops a part.** `IsDriven` exists on both electrode
records precisely so the complete question has a short name. It is worth grepping
for `.Potential` the next time something driven behaves as though it were earthed.

## The refinement ladder tightened a floor into meaninglessness

A pulsed-extraction model - two plates at zero for a 2 us hold, then plus and minus
500 V - gave `StepSizeUnderflow` at exactly the switch after 63 accepted steps. A fixed
count, invariant under tolerance, cell size, flight time and the ion's speed, which is
the signature of a step being *rejected* at every size rather than a controller
converging.

`FlightTimeStudy` refined by scaling the relative tolerance **and both absolute floors**
by the same factor. At its deepest rung `AbsoluteVelocityTolerance` reached **1e-11 m/s** -
ten picometres per second, against thermal speeds of hundreds of metres. For an ion
starting from rest the normalised velocity error is then unsatisfiable at any step size.

Isolated by tightening each of the three alone: the relative tolerance and the position
floor both cross the switch; the velocity floor alone reproduces it. And that floor is
load-bearing - it is what stops `ErrorNorm` being a position-error controller, which
section 11's own findings turn on.

**A floor states what is negligible, and what is negligible does not change because a
more accurate answer was asked for.** The ladder now refines the relative tolerance and
the position floor, and holds the velocity floor.

### What the fix cost, and what that revealed

The reflectron's flight time is **bit-identical** either way. Its interval narrows
seventeenfold - 1.48e-10 us against 2.58e-09 - because it becomes a measured residual
instead of a saturated floor.

But it broke `AnIntervalThatCollapsesToZeroIsReportedAsAFloorRatherThanAsExact`, and the
reason is the part worth keeping: **that model's bit-exact agreement between its two
finest rungs depended on the ladder over-tightening the very floor at issue.** The test
had been asserting a coincidence - and asserting it carefully, with the premise checked
rather than assumed, which is the only reason the breakage was legible rather than
mysterious.

Nothing reachable through the study's own API reproduces the collapse: a refinement ratio
of 1.001 still diverges at 1.7e-12, and loosened floors make the rungs differ *more*
rather than less. **A rule that can only be exercised by a coincidence is a rule with no
test**, so the rule was given a name - `FlightTimeStudy.ConvergenceResidual` - and is now
tested directly on hand-built runs that agree to the bit. That is a better test than the
one it replaces: it states the rule instead of hoping a model will demonstrate it.

### Four eliminations, and a control that was not one

Recorded because each cost time. **Not the ion's speed** - at 1e-6, 1e-3 and 1 V it failed
identically. **Not the turning-point cap**, off for a time-varying field. **Not the switch
or the stopping surface** - `SwitchCrossingTests` crosses the same shape in 123 steps.
**Not the facing pair**, which crosses in 112. **Not `FieldAssembly`** - a single
integration through it passes at every tolerance from 1e-8 to 1e-14.

And the one to remember: a run with both stages energised completed, and that looked like
a control isolating "a change at the switch". It was not - with the field on from the
start the ion reached the detector at 0.879 us and **never got to the switch**. *A control
has to reach the thing being controlled for*, and checking that the successful run
exercised the mechanism would have taken one glance at its flight time.

## Reading the DC of an electrode that holds none, a fourth time

`ModelValidator.CanDoWork` decides whether a source may start at rest by asking whether
anything in the model can accelerate an ion. It asked whether any electrode held a
non-zero potential or a drive.

**A pulsed-extraction trap holds neither until its second stage.** So the archetypal
start-at-rest device - the one §12's turn-around time is defined for, and the one CLAUDE.md
already cites as the reason a source may start at rest at all - was refused on the grounds
that nothing could move its ion.

This is the fourth appearance of the same pattern, and the third in this one function:

1. `einzel solve` reported the DC pattern for every driven 2-D geometry.
2. `CanDoWork` asked only about DC, so the Paul trap was refused.
3. `CanDoWork`'s three-dimensional arm inspected nothing at all and passed by default.
4. `CanDoWork` reads the base potentials and not the **stages**.

The repository's own advice was already written down - *"grep for `.Potential` the next
time something driven behaves as though it were earthed"* - and it is not enough, because
the fourth case is not about the drive at all. The wider statement: **a check that asks
what an instrument is doing must ask over every configuration the instrument has**, and a
sequenced one has as many configurations as it has stages.

The control matters as much as the fix. Widening a check until it accepts the case in
front of you is easy and useless; what says the widening was correct is that a sequence
which never energises anything is *still* refused.

## A stage set to an expression was read as zero

`CompileStages` built its override dictionary with `Quantity.From(value.Value, value.Unit)`
and never looked at `value.Expression`. `QuantityValue.Value` defaults to zero, so a stage
declaring `{"expression": "extractionVolts", "unit": "V"}` applied **nothing**.

The model validated. The field solved. The run reported an ion that never moved. There
was no diagnostic anywhere, because from the engine's point of view the author had asked
for zero volts - and zero volts is a perfectly ordinary thing for a stage to apply, since
that is exactly what the *first* stage of an extraction does.

Now refused, rather than supported, because what an expression should mean here is a
design question: the parameter surface it would evaluate against is the one the stage is
in the middle of changing. Refusing is the answer that does not require settling it.

Same family as an unrecognised property being ignored rather than refused, and the same
consequence: **a document that means something other than what it says, with nothing
anywhere to say so.**

## An endpoint difference is not a spread

A published isochronicity figure was compared against the model's own quadrature and
reported as reproduced: 9.9e-7 against a paper's 2.1e-6. The first half was wrong, and it
reached three documents before a second implementation of the same quadrature, written to a
different specification, disagreed and forced the check.

Two errors compounded, and the first is the general one. **The spread was measured as the
difference between the two endpoints of the energy range**, and the shape being measured has
an interior minimum — which is the design property, and the whole reason anyone cares about
it. A curve that dips in the middle has nearly equal ends, so the endpoint difference is
small by *cancellation* rather than by flatness, and it gets smaller the more sharply the
curve dips. The statistic was anti-correlated with the thing it was standing in for.

Measured properly, as maximum minus minimum over the published range, the same shape gives
9.2e-5 rather than 9.9e-7 — and 1.1e-2 over the full published plateau, which was the second
error: the range sampled was about a tenth of it.

The rule: **when the quantity is "how much does this vary", the statistic is max minus min
over the whole declared range, and an endpoint difference is only equal to it for a monotone
function.** If the design property is a stationary point, the endpoints are the two places
guaranteed to agree.

## A fit goes stale when the thing it was fitted to moves, and arithmetic cannot see it

A stripe shape had been fitted to a mirror geometry and shipped in the template. The mirror
then moved three times over the following work. The fitted stripe stayed, and **the model
stopped reproducing the published drift register for nineteen sections without anything
saying so** — the reversal came out at 409.7 mm against a published 335.

The part worth keeping is the guard that was in place. A geometry check had been added in the
same commit that moved the mirror, and it verified the angles *by arithmetic*. It passed,
correctly, because the arithmetic was right: what had gone stale was a fit whose validity is
a property of a geometry that is no longer there, and no amount of checking the new geometry's
internal consistency can detect that. **A flight would have caught it immediately**, because
the fit's whole purpose is to produce a number a flight measures.

Two rules. A parameter obtained by fitting carries an invisible dependency on everything it
was fitted against, so **moving any of those invalidates it silently** — and the fix is to
record what it was fitted to, or to refit as part of the same change. And **a derived quantity
should be guarded by the measurement it was derived from**, not by a consistency check on its
inputs; the second is cheaper, always available, and blind to exactly this.

## A document whose name promises current state will be read as current state

`docs/astral-log.md`, when it was called `astral-handoff.md`, grew to 6,000 lines and 80 numbered sections, each recording what
was found next and several reversing what an earlier one concluded. Its own "Where this
stands" summary cited sections 16 to 23 and nothing beyond, so the part a reader trusts first
was current to under a third of the page.

The harm was measurable rather than theoretical. Two claims in CLAUDE.md — which is loaded
into *every* session — had been written from that summary and were superseded; one of them
steered an hour of work in this repository before anyone checked. And the correction written
for it was itself taken from the same stale summary and had to be corrected again within the
hour.

What made it dangerous was not the length and not the wrong turns, which are worth keeping.
It was that the name and the opening section both promised a snapshot. Split into a
current-state page, a published register and a chronological log — and renamed so the log
says it is one — the same 6,000 lines are harmless.

The rule this project already applies elsewhere: a status page that has drifted is worse than
none, because it is trusted. **That applies to any document with a summary at the top, and
the summary is the part most likely to be stale**, because it is written once and the
sections below it are appended to.

## A placeholder whose comment said nothing read it, and two things did

A geometry with DC stages and no RF drive was given a nominal clock at one hertz, and the
comment beside it said: *"there is then one nominal clock whose frequency nothing uses, and
one hertz keeps the step cap out of the way."* Two readers took the frequency at its word.
`ShortestPeriodSeconds` reported one second instead of infinity, and the regime report
described the geometry as a 1 Hz drive. Neither mattered until a third reader arrived.

The diffusive path wraps a driven field in a pseudopotential — the cycle-averaged well a
slow ion feels — and the wrapper had to be gated on *having a drive*, because a DC ramp is
time-varying with nothing to average over. The obvious gate is a finite shortest period.
The placeholder made the period finite. So a TIMS elution ramp — a field that measurably
held its density to a micrometre for a millisecond — was averaged over a one-second cycle,
most of which is zero field, and the whole density drifted out at gas speed during a
300 µs hold. **The only symptom was a physically plausible spectrum arriving too early.**

Two rules. **A placeholder is a value, and every reader will treat it as one**; the honest
value for "no drive" is the one the arithmetic gives — infinity — and every reader of it
already handled infinity as "no cap" and "no drive". And **a comment asserting what does
not read a value is a claim about the whole codebase at the moment it was written**, which
the next reader falsifies without ever seeing the comment.

## A single-mode sequence took the path that steps a snapshot

The run command routes a model to the sequencer when its phases change transport mode, and
otherwise to the plain path for its declared mode. A diffusive model with a sequence that
*stays* diffusive therefore went to the plain diffusive path, which reads the field through
the time-free interface and steps a snapshot of it — so an elution ramp declared on it ran
with the ramp silently ignored: exit 0, a density, no warning. The sixth occurrence here of
a time-varying quantity reached through a time-free interface answering at an arbitrary
instant rather than failing.

The routing test was "does the mode change", which is a *proxy* for "does this run need
the sequencer", and the two came apart exactly when a sequence changed the field without
changing the mode. The trajectory path never had the problem because its integrator asks
the field for the instant it is at.

And the sequenced leg, once reached, discarded the return value of the very wrapper that
had done the damage — `_ = Effective(...)` — so the run that averaged a DC ramp over a
second said nothing about having averaged anything. Evidence about a computation's own
quality dropped at a seam: the seventh time, in a file whose comments name the sixth.

## The first non-empty bin is not an onset

An elution spectrum's first arrival came out at 22 µs, during a hold that measurably held.
The bin held about 10⁻¹⁰⁰ of an ion: Scharfetter–Gummel moves an exponentially small
amount of density across every face at every step, so the collecting boundary sees a
non-zero flux from the first step onward. **The onset of a spectrum is where a measurable
fraction has arrived** — a quantile — and a test written on the first non-empty bin is a
test of the scheme's tail.

## A test that drove the system onto a floor was a test of the platform's libm

`CONVERGENCE_ORDER_BELOW_NOMINAL` now gives different advice for a gridded field and
an analytic one, and the obvious way to test that is to drive a refinement ladder onto
a floor and read the message. That worked: three tests, both mutations caught, 372 ms.
**It passed on Windows and failed on Linux.**

Two things conspired. The floor was made by giving the field structure below every step
the controller takes — a square wave written as `Math.Sign(Math.Sin(x / period))`, and
**libm's sine is not bit-identical between the Windows CRT and glibc**. And the whole
construction is a deliberate amplifier of last-bit differences, so a difference in the
final bit of one field sample becomes a different step sequence becomes a different
fitted order.

Making the wave exact — `Math.Floor` and an integer parity, both exact under IEEE-754 —
fixes the sine and does not fix the test, because the step controller itself calls
`Math.Pow(error, -0.2)`, which carries no cross-platform guarantee either.

**The deeper problem is what was being asserted.** Reaching a floor means the flight
time has stopped being set by the tolerance, so the fitted order is a fit to noise —
and the test was asserting which side of a threshold that noise landed on. Measuring
it makes that plain: sweeping the perturbation amplitude over four decades gave orders
of −0.84, −0.49, −0.20, −0.10 and +0.08 against a threshold of 0.5, and one of the five
did not fire at all. Sweeping a resolution cap instead gave 0.435 at one value and no
firing at the next one down. **Neither was structural. Both were luck**, and the
Windows pass was the same luck.

What is exactly determined is *which advice a field earns*, which is the branch that
was wrong. So the advice became a named function, tested directly for both kinds of
field and for the three ways a resolution can fail to be one. The end-to-end test then
has one job — that the warning uses the function rather than a second copy of the
sentence — and it asks only that **at least one of seventeen** configurations reaches a
floor, asserting the message on every one that does. Three do here; for all seventeen
to stop would take far more than the last bit of a `Math.Pow`.

The rule: **if a test needs a chaotic system to land on a particular side of a
threshold, it is measuring the arithmetic of the machine it runs on.** Find the part of
the behaviour that is exactly determined, test that directly, and let the end-to-end
test assert something that many configurations satisfy rather than one.

## Comparing a vectorised device against a scalar one flatters it by the vector width

The specification says a quadrupole stability scan is "GPU-bound; why ILGPU is in the
stack", so the obvious first measurement is FP64 throughput on both devices. Chained
fused multiply-adds, best of several runs, on a GTX 1650 against an i9-9900K:

| | GFLOP/s FP64 |
| --- | --- |
| CPU, one core | 1.2 |
| CPU, all cores | 17.0 |
| GPU | 90.8 |

**Five times the CPU. And the comparison is wrong**, because the CPU kernel is a single
dependent chain per lane: each multiply-add needs the previous one's result, so it uses
neither SIMD nor the second FMA port and is latency-bound rather than throughput-bound.
The GPU kernel is the same dependent chain, but there are thousands of them running at
once, which is precisely the thing the CPU version was not allowed to do.

The physics decides whether that is fair. One ion's Runge-Kutta stages **are** a
dependent chain — but ions are independent of each other, so the CPU can vectorise
across them exactly as the GPU parallelises across threads. Measured that way the CPU
gives **65.8 GFLOP/s** and the GPU's advantage is **1.38x**, not 5.34x. The missing
factor is 3.87, and the AVX2 lane count for doubles is 4.

The rule: **when comparing two devices, both sides must be allowed the parallelism the
problem actually has.** A scalar baseline against a parallel candidate measures the
baseline's restraint, and the ratio it produces is roughly the candidate's width — which
is a number about the benchmark, not about the hardware.

A second thing the same measurement settled, and it is worth stating because it is easy
to take from a spec sheet instead: this card runs FP64 at **1:37 of FP32**, measured. So
a 1.4x advantage in double precision sits underneath a 37x advantage in single, and the
decision about whether to build a GPU backend is a decision about which card is in the
machine rather than about the software.

## A round-off guard tested with the numbers that motivated it

A half-space field warns when the ion turns beyond the depth the model declares. The
gradient is cap over depth, so multiplying it back by the depth returns the cap only to
within an ulp or two, and a model placed **deliberately** at the boundary would trip on
which way that rounding fell. The scaffolded reflectron is exactly such a model — its
cap equals its acceleration potential, and its own description says the ion turns exactly
at the declared depth. So the guard was written: the overshoot must clear a millionth of
the depth before it is reported.

The test for it used that model's numbers, 4 kV on 50 mm, and asserted silence. It
passed. **It also passed with the guard removed**, because 4000 divided by 0.05 and
multiplied back by 0.05 is exactly 4000. The obvious values were the benign ones.

That is not luck, it is selection. The values a test reaches for are the ones from the
model in front of you, and the model in front of you is the one that motivated the
guard — which means it is the case you already know about, and the case you already know
about is as likely to be benign as not. Half the guard's job is the instances you have
not looked at.

Searching the space settled it in a few seconds: over caps from 0.1 to 20 kV and depths
from 5 to 200 mm at the granularity a person actually types, thousands of pairs round
low, and 1 kV on 7 mm is one. With those the test fails when the guard is removed. The
search has to use **the engine's own arithmetic** to be worth anything — a first pass
divided by a thousand where the unit registry multiplies by 1e-3, and every pair it
found was benign under the code being tested.

The rule: when a guard exists to absorb round-off, the test's constants decide whether
it exercises the guard at all, and no amount of reading the test tells you which. Run
the mutation; if it survives, go and find an instance that discriminates rather than
believing the one you have.

## The mutation passed four tests and failed two, and the four were the interesting ones

A gas whose density varies from place to place needed the collision rate read at the
ion rather than off the declared pressure. Six tests were written for it, all passing.
Then the check that matters: **restore the bug and see which fail.**

Making `BackgroundGas.NumberDensityAt` ignore the field and return the declared scalar
failed only *two* of the six — and neither was the headline test, the one asserting
that a field at twice the declared pressure gives a bit-identical trajectory to
declaring twice the pressure.

The reason is worth keeping. That test used the Langevin model, whose rate does not
contain the relative speed — so in a **uniform** gas every scheduled event is real and
there is no thinning step at all. The branch is short-circuited on `IsGraded`, which is
correct and deliberate (a thinning that always accepts would still consume a random
draw and move every seeded result in the engine). A *flat* imported field is uniform.
So the test never reached the mutated line. It was a real test of the scheduled rate
and the null-collision bound, and no test whatever of the local read.

The second weak one was subtler. A ramp from n to 4n across the box was asserted to
collide *more than* the uniform thin gas — which sounds discriminating and is not. With
the density read at the wrong place the effective rate collapses back to roughly the
thin one, the count lands **close to** that end of the bracket, and a bare "more than"
survives on noise.

Both were fixed by making the test contain the thing it claims to test:

- The equivalence test runs under **both** collision models, and hard spheres read the
  local density unconditionally.
- The ramp test **reverses itself**. The same densities over the same box arranged the
  other way round is a configuration that *any* position-blind reading gives an
  identical count for. 11,458 against 19,700.

**A test passes a mutation when the path it exercises does not contain the mutated
line.** That is not a weak test in general — it may be an excellent test of something
else — but it is not evidence about the mutation, and counting it as corroboration is
how a suite comes to have a hole exactly where its author thought it was strongest. So
run the mutation, read *which* tests failed, and treat every test that did not as
untested rather than as confirmation.

The corollary is about brackets. An assertion of the form "between A and B" is only as
good as the distance from the true value to whichever end the bug moves it to. When a
bug's effect is to collapse a quantity onto one end of its own bracket, the bracket
cannot see it — and a *symmetry* the bug destroys (here, reversal) can.

## A time-varying quantity read through a time-free interface, four times

`ITimeVaryingField` extends `IElectrostaticField`. That is the right relationship - a
driven field *is* a field - and it has one consequence that has now cost four separate
defects: **a caller holding the base interface gets an answer, at t = 0, without
anything failing.**

1. `einzel solve` built its mask from the electrodes' DC potentials. For the shipped
   RF quadrupole, whose electrodes hold zero DC and all their potential as drive, that
   was a solve of a grounded box reported as `converged: true`, exit 0.
2. The diffusive mode accepted a driven geometry and stepped a density through the RF at
   the top of its cycle - a static field that exists for no length of time - and reported
   a transit distribution with no warning anywhere.
3. `SuperposedField` implements only `IElectrostaticField`, so **a driven element summed
   with anything else silently became a snapshot**. Fixed structurally: `FieldAssembly`
   picks a driven superposition when any member is driven, so the composition is chosen
   by what it contains rather than by what the caller asks for.
4. The renderer drew equipotentials through `PotentialAt(point)`, so every frame of an
   animation showed the same instant. The picture was plausible - at t = 0 a sinusoid is
   at its peak, so it was the field at full amplitude - which is why nobody looked.

None of these threw. Each produced a field that is *a* field the instrument has, at
*an* instant, and nothing on the output said which.

The structural fix is the one made in (3): choose the implementation by what the thing
contains, not by what the caller asks for. Where that is not available, the fix is to
make the instant an argument rather than a default - which is what (4) did, and it
brought a warning with it, because a section of a driven structure is a frame of a film
whether or not it is drawn as one.

**The thing to grep for is a call to the base interface on a value that might be
driven.** It will not be a compile error and it will not be a wrong-looking number.

## The test used a solved model, and the bug was in the analytic branch

Three times in one night a test passed with its bug restored, each for the same reason
in different clothes. The third is the cleanest.

An animation frame was choosing its page from the part of the flight drawn so far, so
the scale changed frame to frame and the ion sat pinned to the edge of a box that grew
to meet it. The test written to pin the fix asserted the obvious thing - every frame has
the same page - and it passed with the fix reverted.

The model it used was the einzel lens. A **solved** geometry declares its domain, and
the extent comes from that domain; the flight cannot change the page at all. So the test
was a perfectly good test of something, and no test whatever of the thing it was named
for. Rewritten against an analytic reflectron - no declared domain, extent taken from
the flight - it fails immediately.

The generalisation is worth more than the instance. **Where a function has branches
chosen by the shape of its input, a test fixture chooses a branch.** A fixture picked for
being convenient and realistic will pick the *common* branch, and the bug will be in the
other one - because the common branch is the one that gets exercised by everything else
and has already had its bugs shaken out.

That is exactly why the reflectron's own extent had been wrong since sections were built:
**every render test uses a device template, and every device template declares a solve
domain.** The analytic branch had no coverage at all, and the first thing a new user
renders goes through it.

## A guard written four times, silent about the fifth thing

Resolving a declared gas field needs the model document's own directory, which a study
or a figure of merit reaching the transport does not have. The rule was right and had
been reasoned about carefully: refuse, rather than run in a gas the document does not
describe, because a run that quietly uses a still uniform gas succeeds and answers about
a different instrument.

It was implemented as a guard at each of **four call sites**, and each named
`velocityField` — because that was the only importable quantity when they were written.
Adding a pressure field meant every one of them was now silent about half of what it
was guarding.

The fix was to move the check to the function that *cannot* read a file:
`BackgroundGas.FromModel` refuses an unresolved field itself, and
`WithoutImportedFields` is the deliberate exception, named for what it gives up rather
than for what it does. This is the same rule as `FieldAssembly.Build` throwing rather
than discarding its `SolveReport`, and as `Setup` using `BuildReported` where it has a
sink: **make the shortest spelling the safe one.**

It found a real defect within minutes of being written — and, pleasingly, in the
*importer*. `GasFlowImport.Resolve` began by calling `FromModel`, so the guard fired on
the one caller that was about to do the right thing, and `einzel run` on a diffusive
model with a declared field was refused. A guard that catches its own author on the
first run is a guard placed where the mistake actually lives.

The general form: **when a rule is enforced once per caller, adding a case to the rule
means auditing every caller, and the audit is invisible if you forget.** Enforce it
where the callers converge.

## A ratio of two ceilings is one

A test compared the transit through a drift tube at one pressure against the same tube
with an imported field at half of it. The mobility scaling says the ratio should be
0.5; it read 0.908, which is close enough to one to look like the scaling not working
at all.

The scaling was fine. The field was 50 V/m over 38 mm — under two volts of push — and
at 1 mbar almost nothing reached the detector inside the declared flight time:
**0.05 ions of 10,000 collected**, with a "mean transit" of 3869 µs against a ceiling of
4000. The thinner run collected 1982 and genuinely finished. What was being compared was
a real transit against a truncated one.

This is the **incomplete arrival** trap that `einzel compare` already documents and
warns about — a mean transit over the subset that arrived is not a transit time, and
the two subsets are not the same ions — met again from a direction where nothing was
watching for it. The accessor now asserts the packet arrived before reading a transit
off it, which is where the check belongs: at the point the number is taken, not at each
place it is used.

**A summary statistic computed over a truncated population is not a smaller version of
the right answer. It is a measurement of the truncation.**

## Three thresholds picked before measuring, all comparing the wrong pair

The C-trap's central claim is that the RF is what carries an ion round the bend, checked
against the same model with the drive off. Getting the *assertion* right took three
attempts, and all three failed the same way: I chose what to compare before looking at
what the numbers were.

**First: worst excursion against worst excursion**, asserting an order of magnitude. The
measurement was 586 um guided against 3004 um unguided - a ratio of five, which reads as
"not very different". But those are not the same kind of quantity. The guided ion's 586 um
is the amplitude of an oscillation it returns from; the unguided one's 3004 um is simply
how far it had got when it hit a rod. Comparing them is comparing an amplitude with a
displacement.

**Second: final distance against worst**, on the reasoning that a confined ion comes back.
It does - but an oscillating quantity sampled at one arbitrary instant is anywhere in its
range, and the guided ion happened to be caught at 61% of its amplitude when it crossed the
detector. A single sample never characterises an oscillation.

**Third: the unguided ion's closest late approach against its own worst.** It strikes at
25.9 us, so the second half of its path *begins* when it is already 782 um out. Its own
worst is not a scale it ever returns from - it is where it stopped.

What finally works is the closest approach over the later part of the flight, of one ion
**against the other**: the guided ion comes back to **2.5 um** and the unguided one never
gets nearer than **782 um**, a factor of 313. That is bounded-versus-unbounded, which is
what confinement actually means, and it needs no arbitrary length.

**The rule: look at the numbers before choosing the comparison.** A threshold picked in
advance encodes an assumption about what kind of quantity you are measuring, and here that
assumption was wrong three times over in the same direction - treating something that
oscillates as though one number described it.

## The property an instrument is built on cannot validate the field it rests in

The quadro-logarithmic field's radial component went in negated. `-dU/dr` is
`k(r/2 - Rm^2/2r)`; what I wrote was `k(Rm^2/2r - r/2)`, so it pushed ions outward where
the doc comment two lines above said "pulls inward inside it".

**Every frequency test passed with it.** The axial motion obeys `m z'' = -q k z` with no
`r` anywhere in it, so it is exactly decoupled from the radial coordinate - which is the
whole design of an orbital analyser, and the reason its frequency measures mass. A radial
field of entirely the wrong sign does not touch it. Five cases spanning radius, tangential
speed and axial amplitude all agreed with `sqrt(q k / m)` to parts in a hundred million,
while the ions they were computed from were being flung outward instead of held.

What caught it was the two checks that couple the components back together: `E = -grad U`
by numerical differencing, and energy conservation along a trajectory.

**The general form is worth more than the bug.** A designed invariance is a designed
blindness. When an instrument is built so that one quantity does not depend on the others,
measuring that quantity cannot tell you the others are right - and it is exactly the
quantity a test writer reaches for first, because it is the one with the clean closed form.
Pair it with something that spans the parts: a gradient check, an energy check, a
conservation law that involves every component.

A related trap in the same file, and it is why the first run reported twelve failures
rather than two: `Assert.Equal(expected, measured, 3)` on a frequency of order 1e6 asks for
three *decimal places*, which is 3e-10 relative. The physics was right to 5e-8 and the
assertion was wrong by two orders of magnitude. **A decimal-place assertion is an absolute
one**, and on a large number it silently becomes far stricter than anything the code could
deliver - and on a small number, far looser.

## A verb that works, is documented, and cannot be found

`einzel outline` had no line in `einzel --help`. It worked. It was documented in
`docs/cli.md`. It is the verb the shell's model tree is built on and the one an agent would
use to read a model's parameters without parsing the document. And running `einzel --help`
did not mention it existed. So did `render animation`.

For a surface whose entire argument is that an agent drives it, **a capability that cannot
be discovered from the tool is close to one that does not exist.** This is the same
reasoning that makes the platform layer of `AGENTS.md` generated rather than hand-written:
an instruction set that has drifted is worse than none, because it is trusted.

Nothing checked it, so `HelpCoversEveryVerbTests` now does - **against the dispatcher's own
switch rather than a list kept in the test**, because a hardcoded list drifts for exactly
the reason the help did. Both directions are asserted: a verb that dispatches and is not
listed, and a line listing a verb that no longer dispatches. The second fails differently -
it sends a reader to a command that answers "unknown".

Writing it took two corrections, both mine and both instructive about what the check is
*for*. The first regex missed `"optimise" or "optimize" =>`, so an alias chain read as a
help line with no verb behind it; aliases are now recognised and deliberately not required
to appear, since listing both spellings would suggest they differ. The second matched the
*nested* `agents` sub-switch and demanded top-level lines for `tasks`, `setup` and `score`,
which are correctly listed as `agents tasks` and so on - one line per thing a person types.
**Both failures were the test misreading the structure, and neither was a defect** - which
is worth saying, because a new check that fires immediately is as likely to be wrong as the
code is.

## One field answering two questions diverges where the questions differ

A run manifest recorded the model's **content hash** and not its path. By PRJ-3's own list
that is complete: the hash is what makes a result regenerable, and it survives a rename
where a path does not.

So `einzel verify` had to identify the model by searching for a file that still hashed to
the recorded value. That conflates *what this result was made of* with *what it is about*,
and the two come apart wherever two files hold the same bytes:

- The result attaches to whichever file is found first, which is arbitrary.
- **Editing the model that was actually run makes its drift disappear** - the result
  silently re-attaches to the untouched twin, reports itself current, and the edited model
  reads as never run.

The second is a stale result reporting as fresh, which is the one direction verify exists
to prevent. Reaching it takes no contrivance: `einzel init` scaffolds a reflectron, and
adding the corpus's own reflectron gives a project with two identical models. That is how
it was found - not by suspecting it, but by building a view that listed both files side by
side, where the state jumping from one row to the other was visible at a glance.

The fix records the path as well, prefers it, and keeps the hash search as the fallback
for older manifests and for the case it was written for. The general rule: **when one field
is made to answer two questions, it answers the second one wrongly exactly where the two
questions differ.** Ask what a value identifies as well as what it determines.

**And the same mistake once more, in my own fix.** I wrote the fallback's message as *"the
model has been renamed"*. It cannot know that. The recorded path being gone while identical
bytes sit elsewhere is equally consistent with a rename and with a twin that was there all
along - which is the very coincidence the whole defect turned on. My own test caught it:
deleting one of two identical models produced a "rename" rather than the orphan the test
expected, and the right response was to correct the message rather than the test. It now
says what is observed - the recorded path is gone, the same content is at X, so the result
still stands - and adds that this is a rename *if nothing else held that content*.

**Do not report a history you did not observe.** A message describing a state is checkable;
one describing an event is a guess about how the state arose, and it reads with exactly the
same authority.

## The end of a run is where the answer is, and where the picture is not

A diffusive run reports the density it *ended* with. The section renderer learned that
this is the wrong thing to draw and gained an instant to draw at; the note written at the
time says it plainly - "a model whose ions have all arrived left an empty box - correctly,
and uselessly, because the picture worth having is the packet in flight."

Adding the density cloud to the viewport, I anchored it to the end of the run. Its own
test failed immediately, and the numbers say why: the shipped drift tube launches 10,000
ions, **collects 9,999.76, and leaves 1.8e-302 behind**. Drawing that is drawing nothing,
for exactly the models that work.

The general form is worth more than the fix. **A result and a picture of a result want
different instants.** The end of a run is the only moment that answers "what happened" -
transmission, transit time, where the ions went - and it is the one moment guaranteed to
be empty of the thing a picture is of. Any surface that draws a time-evolving quantity
needs an instant chosen for the drawing, and needs to say which instant it chose.

The viewport now takes snapshots across the flight and draws the middle of those still
holding a packet, reporting it as `render.density-at-instant`. That the caller can name
its own instant is the same seam animation scrubbing will need.

## Every normal was exactly zero, and that located a transposition

The density shells passed every structural check written for them: three coordinates per
vertex, one normal per vertex, triangle indices in range, levels a decade apart. What
failed was that **760 of 760 normals had length zero**.

`Surfaces.Orient` takes the normal from the gradient of a scalar and leaves it at zero
where the gradient vanishes. A zero everywhere means the scalar was flat everywhere the
surface is - and the vertex it named was at y = 36.4 mm on a grid spanning +/-6 mm.

The cause was that I filled the sample array as `values[row, column]` where
`Contours.Sample` builds `values[column, row]`. Transposed, the contour is traced
somewhere the density is not: still a well-formed mesh, still watertight, still correctly
indexed, and sitting in a region where the density is uniformly zero.

Two things generalise.

- **A geometric invariant catches what a structural check cannot.** Counts, ranges and
  parities all held. What could not hold was a normal being a unit vector, because that
  depends on the surface sitting where the field actually varies.
- **A zero-length normal is a locator, not just a defect.** It says "the field is flat
  here", and printing *where* turned a wrong answer into a coordinate that was obviously
  outside the domain. The first version of the test only reported the worst magnitude,
  which was `1.000` and said nothing at all.

A near-miss on the same fix is worth recording: I first assumed the cause was the
differencing step, since `OrientStepMetres` is 1e-6 and is chosen for a signed distance -
which changes by the step itself - while a density changes by whatever it changes by.
That reasoning is correct and the step is now the density's own half-cell, but it was not
the bug. **A plausible explanation that fixes nothing is the expensive kind**, and only
re-measuring separated them.

## Enumerating a requirement's population, rather than believing it

`GRD-2` says validity warnings travel with the result through every layer, and then
**names them**: engine, command layer, CLI output, MCP response, exported file, rendered
figure, video. Seven. The register had said **Met** for a long time, and the evidence
column said "exported VTU/VTI files and figures".

Asking the seven questions one at a time found that two of them were no.

**The exported `.vtu` carried nothing.** `VtuWriter.WriteTrajectory` and
`WriteDensityField` both take an optional `provenance` list. The density call site
appended the run's warnings to it; the trajectory call site did not. Same writer, same
parameter, one line apart in intent and two thousand in the file.

**The rendered figure was not flying the gas**, which is the more interesting half because
it is not what was being looked for. The question was whether a *warning* reached the
figure. The answer was that the figure had never been computing the same thing a run
computes: both renderers called `TrajectoryIntegrator.Integrate` without supplying the
optional `collisions` argument, so a model at a millibar was drawn in vacuum. On the
`thermalisation` example that is a drawn flight of **2778 mm against the 155 mm the run
reports** — and the two figures, gas declared and gas removed, were byte-identical.

Three things generalise.

- **A closed population makes a requirement checkable.** "Do warnings propagate?" is
  answered "mostly, yes" — correctly — and that answer conceals precisely the layers
  where they do not. "Does a warning reach the exported file?" has no such refuge.
- **Set equality, not containment, for a carried set.** The file test asserts the `.vtu`
  carries *exactly* the warnings the result does. Asserting the anchor appears would pass
  on a file that carried one and dropped four.
- **An optional parameter whose default is a different physics is a trap, not a default.**
  This is the third time the gas has reached one path and not another — after the
  figure-of-merit path that made `einzel test` disagree with `einzel run`, and the regime
  inspector's own first draft. An omitted `collisions` argument does not fail; it produces
  a well-formed drawing of a different instrument. Where an argument selects *what physics
  runs*, absence should mean refusal, as `BackgroundGas.FromModel` now does for an
  unresolved imported field.

**The audit was worth more than either fix.** Both defects were in code that had been
reviewed, tested, and written up as working.

## A ratio's size is its distance from one, not its value

Comparing two effects that are both expressed as ratios, I wrote

    Assert.True(phaseSpread < driveShift / 4.0, ...)

meaning "the phase matters much less than the drive does". It cannot pass. A ratio that
says *no variation at all* is **1**, not 0, so a phase spread of 1.10 is not four times
smaller than a drive shift of 3.14 — it is an effect of **0.10** against one of **2.14**,
which is twenty-one times smaller. The comparison has to be on the excess over one.

The measurement was right and the assertion was wrong, which is the dangerous way round:
the number printed beside it, `1.10x`, is exactly what "the phase hardly matters" looks
like, so the failure read as a physics result rather than as arithmetic.

**Where else this bites:** any quantity whose null value is 1 rather than 0 — a ratio, a
speed-up, a scale factor, a convergence factor, an enhancement. `a < b / 4` is a claim
about magnitudes, and it only means what it looks like when zero is the null.

## A device driven outside the regime an effect lives in reports the effect's absence

The scan that established the C-trap's focusing first ran with the RF at 500 V and the
ejection at 60 V. The packet did not converge, and the obvious reading was that the
curvature does not focus.

It was not. The ions were rattling in a well eight times deeper than the push and leaving
at whatever phase they happened to escape at — **11 to 23 degrees off radial**, four of
five striking the slot edges. A real C-trap switches its RF off to eject, and with the
drive off the same geometry focuses **20.8x**.

This has appeared here before, in a confinement test run on two flat plates: the
ponderomotive force goes as the gradient of E squared, a nearly uniform field has no well,
the packet moved 0.1% either way, and the test passed on a threshold of "less than where it
began". **The absence of an effect and an operating point that cannot show it produce the
same output.** Before concluding a mechanism is absent, check that the operating point
admits it.

## A discontinuous potential is not a discontinuous trajectory

Bounding an analytic field to a box makes its potential jump at the boundary, because a box
is not an equipotential of anything interesting. I wrote that up as "an ion crossing gains
or loses that much energy per crossing", graded it against ACC-1's budget, and made
`FieldAssembly.Build` refuse above it.

**It is wrong, and one measurement settled it.** An ion is moved by the **field**, and the
field is exactly the declared one on each side. So a uniform field bounded to a box is an
accelerating gap followed by a field-free drift — an ordinary instrument:

| | |
| --- | --- |
| `sqrt(2 m L / (q E)) + (D - L) / v` | **13.658582 us** |
| measured, bounded | **13.658582 us** |
| same model with no region | 10.180506 us |

What the step actually costs is narrower and had to be worked out separately: the
**energy-drift diagnostic** is computed from the potential and so jumps at the boundary, and
the piecewise field is **not conservative across it** — an ion that crosses more than once,
in by one face and out by another, can gain energy no electrode supplied. A single straight
crossing has no such path.

**The mistake was reasoning about the potential when the integrator uses the field.** The
two are the same object only where the field is the gradient of the potential, and a region
is precisely a place where the code makes them disagree. The general form: *before deriving
a consequence from a quantity, check that the code path in question actually reads it.*

It also mattered how severe I made it. A `ValidityViolation` on a model that is provably
correct is a false alarm on a channel GRD-3 makes unsuppressible, and it would have taught
readers to ignore the one warning class that must never be ignored.

## The same defect, twice more, found by running every example and reading the output

Fixing the energy-drift one above took ten minutes. Looking for its siblings took twenty
and found two, and neither had a failing test because neither could have had one.

**A trajectory that never started reported zero drift.** The early return for "the source is
inside a conductor" sets `MaximumRelativeEnergyDrift = 0.0` — for an ion that did not fly at
all, so the diagnostic is not merely unmeasured, it is meaningless. Twelve lines above the
one that was wrong for the at-rest case.

**A peak with no width reported a resolving power of zero, and the warning beside it said
unbounded.** Resolving power is `t / 2dt`, so a zero width makes it infinite — the best
conceivable value — and the run printed `resolving 0`, the worst. The infinity was even
computed:

    var quantity = Quantity.Number(double.PositiveInfinity);
    return new Measured(
        Quantity.Number(0.0),        // <- and this is what was returned
        ...

assigned to a local and never used. The compiler does not warn about that when the value
comes from a method call, so `TreatWarningsAsErrors` could not catch it either. It reached a
shipped example, `slit-transmission`, which prints it on every run.

**What found both was running all 37 examples and reading the diagnostics**, not inspection.
The sweep is three lines of shell and it is now worth repeating after any change to what a
run reports.

## A quantity that was never measured, printed as an excellent value

Every run in this project that launches an ion **at rest** reported

    energy drift  0.00E+000 relative (ACC-4 budget 1e-6)

which reads as four orders inside budget. It means **not measured**.

The drift is relative, so it needs a scale, and the scale is the ion's total energy at
launch. An ion released from rest at a point where the potential is zero has a total energy
of exactly zero, so the tracker returns without doing anything — and `maximumEnergyDrift`
was left at the `0.0` it was initialised to. The accelerating gap, the sequenced extraction,
the Paul trap and the rectilinear trap were all affected, and all of them are examples
somebody would read.

**The rule this project already has is "absent, not zero", and it had been applied three
times before without generalising**: a Twiss orientation is undefined for a perfectly
parallel cloud and is now absent rather than zero; a peak width needs two arrivals and is
absent rather than zero when there is one; a driven field does work deliberately, so its
energy drift is already NaN rather than a number that looks like a diagnostic. This is the
same case and it was initialised to zero one line above the branch that returns.

**What makes it worse than an ordinary missing value is that zero is the best possible
answer for this quantity.** A reader who sees a blank asks; a reader who sees the ideal
result stops looking. When a diagnostic's "not computed" value coincides with its "perfect"
value, the two must be distinguished at the point where the computation is skipped, not
left to the reader.

## Exit codes and validity violations are orthogonal, and enumerating said so

Having fixed the exit code to ask whether the engine finished, the obvious next step looked
like making it also fail when a run carries a non-suppressible **validity violation** — that
being, on the face of it, exactly "the engine did not compute what the document describes".
An unconverged field is the motivating case: `FieldAssembly.Build` refuses to hand over a
bare field for it, while `einzel run` exits 0.

**Enumerating first turned that from a task into a settled no.** Eleven of the thirty-seven
shipped examples carry a validity violation:

| | |
| --- | --- |
| `mobility.outside` | 5 — a low-field mobility outside its fitted range |
| `spacecharge.ignored` | 4 — the packet's own charge implies an error over budget |
| `spacecharge.point` | 2 — a cloud with no extent, so no estimate is possible |
| `rf.quiver`, `diffusion.implicit` | 1 each |

Every one of them behaves exactly as designed, and each asserts a closed form in its own
test file *knowing* the caveat applies. So the change would have failed 30% of the reference
corpus — worse than the rule it was meant to complete, which failed 16%.

**The reason is the doctrine, which the idea contradicted without my noticing.** Taint, never
block: the platform never stops you working, it refuses to let a result look cleaner than it
is. A validity violation is the mechanism for "this number carries a caveat you cannot
suppress". An exit code is the mechanism for "the command failed". They are deliberately
orthogonal, and coupling them would turn every unsuppressible caveat into a broken build.

What that leaves genuinely open is much narrower than the note it replaces: an *unconverged
field* specifically, where `Build` already refuses and `run` does not. That is one warning
code, not a class.

**The pattern, twice in one sitting.** Both halves of this were decided by counting the
population rather than by reasoning about the motivating case, and both times the count
changed the answer — once from "add a schema field" to "ask a different question", once from
"do it" to "don't". A rule about results is testable against the corpus of results before it
is written.

## The proxy was the outcome; the question was who failed

Written up as an open question the night before, and now settled — the answer was not the
one the note predicted, which is why it is worth reading with the original.

The note said the fix could not be a third widening of the success list, because "ended at
the time limit with everything still inside" describes both a trap holding its ions and a
beamline whose ion ran out of flight time, and nothing in the document tells them apart. It
concluded the distinction was **intent**, and therefore a schema question.

**Enumerating the corpus made that conclusion unnecessary.** Six of the thirty-seven shipped
examples exited with a failure code while behaving exactly as designed, and they split into
two kinds rather than one:

| | |
| --- | --- |
| ends at a declared hold | `paul-trap-held`, `thermalisation`, `orbital-trap-frequency` |
| deliberate loss, the control half of a pair | `paul-trap-ejected`, `quadrupole-rf-unstable`, `ion-funnel-no-rf` |

The second kind ends `StruckElectrode`, which no amount of trap-versus-beamline intent
would have fixed. So the shared property is not what the model is for. It is that **the
engine finished in every one of these cases** — the transmission, the itemised losses and
`confined` say what became of the ion, and each of those six examples asserts exactly that
in its own test file.

What is left as a failure is an integrator that gave up: a step-size underflow, an exhausted
step budget. Those leave a trajectory that stops part way for a numerical reason, with no
bound on how wrong it is.

**The generalisation, corrected.** The earlier note's rule — *when a proxy keeps needing to
be widened, ask the question, but check the question is answerable from what you have* — was
right about the first half and wrong about the second. The question was answerable; I had
picked the wrong question. "Was this model meant to hold ions" is unanswerable and
unnecessary; "did the engine compute what it was asked" is neither.

**The way to tell those apart was to enumerate the population.** Three examples suggested
intent; six ruled it out. Reasoning about one case chose a schema change that would not have
fixed half the instances.

`TrajectoryOutcome.Completed()` is now a switch with every case named and a throw for the
rest, rather than a list of the successful ones — so a sixth outcome fails to compile until
somebody decides which kind it is, instead of being silently classified as a failure the way
diffusive and sequenced runs both were.

## A list of known outcomes, three times, where the question was never asked

`einzel run` exits 4 — ConvergenceFailure — on `paul-trap-held`, an example that behaves
exactly as designed. The exit logic reads

    run.Outcome is "StopConditionMet" or "DensityEvolved"

and `MaximumFlightTimeReached` is not in it. **This is the third sighting.** The list had
to learn `DensityEvolved` after a working diffusive run reported itself as a failure, and
the sequenced path needed the same fix again. Each time it was widened; each time the next
mode broke it.

The reason is that the list is a **proxy** for the question, and the question is *did this
run finish what it was asked to do*. "Which outcome was it" is equivalent to that only for
the modes that existed when the line was written.

**But the fix here is not a third widening, and working out why is the useful part.** The
tempting rule is "ended at the time limit with everything still inside and nothing struck →
success", which describes a trap holding its ions. It also describes **a beamline whose ion
ran out of flight time half way down the column** — same outcome, same loss channel, same
counts. The two are indistinguishable from the result alone.

So the distinction is not in the physics, it is in **what the model is for**, and nothing in
the document says. That makes it a schema question rather than a bug: a trap would have to
declare that reaching the time limit is its intended end. Recorded rather than guessed at.

The generalisation: **when a proxy for a question keeps needing to be widened, the fix is
usually to ask the question — but check first that the question is answerable from what you
have.** Here it is not, and noticing that is what stops a fourth widening that would make a
lost beam look like a held one.

## A guard that checks a string is present cannot see a no-op

Writing a test that needed a trap model with a bigger ion cloud, I edited the document by
string replacement and guarded it the way this project already learned to:

    Assert.Contains("\"cloud\"", document);

The trap **already declared a cloud**, of one ion. So the replacement inserted a second
`"cloud"` key, `System.Text.Json` took the last of the two — the original — the run flew one
ion, and the guard passed on a string that had been there all along.

The existing rule came from three shipped tests that edited a scaffolded model by string
replacement against a layout the corpus had reformatted: the edit matched nothing, and each
test reported the feature it was checking as broken. The fix then was to assert the
replacement happened. **What that fix has to assert is that the document CHANGED**, not that
it now contains something — because the second is true whenever the thing was already there,
which is exactly the case where a replacement quietly does nothing.

    Assert.NotEqual(original, document);

## A test that cannot fail in the regime it is written for

The same test then compared `einzel run`'s confinement against the `confined` figure of
merit over a trap holding **all** of its ions. Both said 100%, and both would have said 100%
with the defect restored — a trap that holds everything reports the same number by any
route, and so does one that holds nothing.

The regime where the two can differ is **partial** confinement. With the cloud made hot
enough that the trap keeps 5% of it, the run says 5.0% and the unfixed figure says 100%.
The test now asserts that the reported fraction is strictly between, so it cannot pass on a
trap where the comparison is vacuous.

**Agreement between two computations is evidence only in a regime where they could
disagree.** This project has the same finding from the other direction — an advection test
whose cell Péclet sat below the threshold of the bug, and a confinement test on two flat
plates that could not make a ponderomotive well.

## A loop invariant nobody profiled, in the hottest loop the project has

The deterministic energy sweep behind `resolvingPower` and `transmission` called
`Setup` — and so `FieldAssembly` — **inside** its member loop:

```csharp
for (var k = 0; k < members; k++)
{
    var offset = spread * fraction;
    var (launch, species, field, settings, detector, collisions) = Setup(model, offset, report);
    ...
}
```

An energy offset changes how fast the ion is launched. It changes **nothing whatever**
about the field it flies through — `Setup`'s only use of the offset is one `Math.Sqrt` in
the launch speed. So that was the same solve, computed from the same `CompiledModel`,
thrown away and recomputed, once per energy member.

**It cost 249 ms per ion, flat, against a whole solve of about 270.** The flatness is the
tell: a per-ion cost that does not fall as ions are added is a per-ion *fixed* cost, and
the only fixed cost of that size in the path is the solve. Twenty-one ions took 5.23 s to
do one solve's worth of work and twenty-one flights.

Hoisting it gave **bit-identical values** — 90176.37243580694, 136915.00595510416,
123107.75087866254, 27.315480249008885, to the last digit — and took 21 ions to 1.53 s.

**Why it survived.** Nothing was wrong. Every number was right, every test passed, and the
figure of merit is *supposed* to build a field. The comment in `WarningLedger` even
records the behaviour as understood — "an ensemble figure builds the field once per ion,
so one draw emits the same code twenty-one times" — as a fact about warning counting
rather than as a cost. **A cost is invisible to a suite that only checks answers**, and
this one had been paid by every study, every optimisation and every literature regression
in the project.

**Where it matters is not where it was measured.** The saving *is* the solve, so it grows
with it: on a 2-D mirror it is 3.4x, and on a volume geometry whose solve is seconds and
whose flight is milliseconds it approaches the member count. The 240-evaluation optimiser
run over the Astral mirror was paying 2,160 solves for 240 distinct fields.

**The test is self-calibrating, which is what makes it a test rather than a threshold.** A
wall-clock bound is a statement about a machine (SPEC.md Amendment 27). What is asserted
instead is a ratio between two things measured on the same machine in the same run: *the
marginal cost of one more energy member, against one whole solve of the same model.*
Solving per member costs a whole solve per member by construction, so any bound under one
catches it. Measured at **0.18 solves hoisted and 1.16 with the solve put back**, and the
bound sits at 0.5 — between the two states rather than hard against either.

**And the generalisation.** `FlyCloud`, ten lines further down the same file, had always
shared one field across every ion in a cloud. Two loops over an ensemble, in one class,
disagreeing about whether the field belongs inside the loop — and the correct one was
right there to be copied. **When two neighbouring routines do the same thing at different
cost, the cheaper one is usually not an optimisation but the intended shape.**

## A ladder, because two points cannot tell a slope from an offset

Adding a tilt to a box needed one claim demonstrated: that a tilt far below one cell
reaches the solved field, because the surface is a cut cell. The obvious test is two
tilts an octave apart — if the response doubles, it is proportional and therefore
resolved rather than rasterised.

It came out **1.85**, against 2 expected. That is the kind of number one is tempted to
explain: a few per cent off, plausibly a second-order term in the geometry, widen the
band and move on.

Running a ladder instead of two points said what it actually was:

| convergence | asymmetry | ratio to previous |
| --- | --- | --- |
| 1 µm | 3.2624e-2 V | — |
| 2 µm | 3.4299e-2 V | 1.0513 |
| 5 µm | 3.9637e-2 V | 1.1556 |
| 25 µm | 7.5430e-2 V | 1.4217 |
| 100 µm | 2.0961e-1 V | 1.7444 |
| 400 µm | 7.5075e-1 V | 1.9324 |

**The ratio rises toward 2 as the tilt grows, which is the signature of an additive
offset, not of a second-order term.** A second-order term would make the ratio depart
from 2 more as the perturbation grew, not less. And the slope is dead constant at
0.0018 V/µm across the whole ladder — so the response was `0.031 + 0.0018 δ`, perfectly
linear with an intercept that had no business being there.

The intercept survived down to a tilt of one micron — a thousandth of a cell — where the
proportional part is twenty times smaller than it. So something discontinuous was
happening the instant the tilt became non-zero.

**It was the geometry, not the tilt.** The plate faces sat exactly on grid nodes. Moving
every face a quarter cell off the lattice removed the intercept completely, and the same
ladder became exactly proportional — step ratios of 2.0000 and 2.5000 for steps of 2 and
2.5, over a four-hundred-fold range.

The mechanism is a hypothesis and is recorded as one: a node lying exactly on a Dirichlet
surface is classified inside, so an arbitrarily small tilt moves the surface off that node
on one side and not the other — a whole node's change in classification rather than a small
change in a cut length. What is established is the measurement and the cure.

**Three things generalise.**

**Two points cannot distinguish `aδ` from `aδ + c`.** Any two-point ratio test of
proportionality is really a test of "is the offset small compared with the signal at the
sizes I happened to pick", and the answer changes with the sizes. A ladder costs one loop.

**A ratio that drifts toward its expected value as the perturbation grows is an offset;
one that drifts away is a genuine higher-order term.** The direction of the drift names
the cause, and it is free to look at.

**The control proved less than it appeared to.** The parallel-plate case reported 7e-15 V
of asymmetry, which reads as "the mesh is clean". It is not evidence of that: the untilted
problem is *exactly* symmetric, so it would report zero however badly discretised or
converged it was. A control that cannot fail for the reason you are worried about is not a
control for that reason.

## Assert the arithmetic that consumes a measurement, not the measurement

Two tests passed on a developer's machine and failed on CI, in opposite directions, for
one reason. Both were mine, and the engine was right in both.

**On a fast runner the pilot was too fast to time.** `estimate` measures the machine by
solving a coarsened copy of the model's own geometry, and refuses to quote a rate from a
pilot under 15 ms — below that the timer measures the clock rather than the solve. The
Windows runner did it in **12 ms**, so the basis correctly said *"too little to time"* and
fell back to the documented rate. The test asserted the measured branch unconditionally.

**On a shared runner two calibrations disagreed.** A study test compared the totals of a
25-point and a 50-point scan and asserted the ratio was 2, reasoning that "both are
calibrated on the same machine in the same run, so everything machine-specific cancels".
That holds on an idle box. On CI it came out **1.125**, because two pilot measurements
minutes apart can differ by more than the quantity under test. The test was measuring the
runner's variance.

Both are SPEC.md Amendment 27 — *an absolute time is a statement about a machine* — which
this project learned from the extension timing tests and wrote down, and which I then
walked into twice more while building a feature whose entire subject is timing.

**The fixes are not looser bounds.** A wider range would have made both tests pass and
tested nothing:

- The calibration test asserts the **disjunction**: the basis must say either that it
  measured this machine or why it could not, and *exactly one* of those. That is the
  reporting contract (GRD-12), and it holds on any hardware.
- The study test asserts the **arithmetic invariant**: a study's total *is* its evaluation
  count times its per-evaluation cost, to nine figures. That is what the code guarantees,
  and it is machine-independent by construction.

**The rule.** Where a value is derived from a measurement of the machine, do not assert the
value — assert the arithmetic that consumes it, or the contract that reports it. The
measurement is the input, and an input you do not control is not a thing to have
expectations about.

**A fourth failure, of a different kind, and it was hiding behind a bad grep.** A theory
pinned resolving power to values this build had produced, to full round-trip precision, and
failed on Linux at **122623.78 against 123107.75** — four parts in a thousand. Resolving
power is `t / 2dt` where `dt` is a difference of nearly-equal flight times, so **catastrophic
cancellation turns a last-bit difference into a per-mille one**, and last-bit differences
across platforms are ordinary: `exp` and `pow` are not bit-specified and the JIT makes its
own vectorisation and FMA choices.

The guard moved to **flight time**, which is an accumulation rather than a difference and
therefore does not amplify. Writing that down forced a correction to my own reasoning: I had
justified the assertion as catching a per-member solve, and **a per-member solve gives the
identical answer** — that is precisely what "bit-identical" meant. No value assertion can
detect it; the cost test is what guards the hoist. The value test guards what the refactor
actually touched.

**And how it stayed hidden for two CI runs.** I read the failures with
`grep -oE "Einzel\.[A-Za-z.]+Tests\.[A-Za-z]+\.[A-Za-z]+ \[FAIL\]"`, which cannot match a
parameterised `[Theory]` — its name carries the arguments. Two runs' worth of failures were
in the log and my filter could not see them, so each round I fixed what I could see and
pushed into the same wall. **A filter over failure output is itself a test of nothing if it
can silently match less than everything**; read the count, or match the failure marker rather
than the name.

**And a corollary about scale.** It is not only *how long* something takes that is a
statement about a machine; so is the assumption that a given amount of work takes long
enough to time at all. The 12 ms pilot was not a slow machine or a fast one — it was work
too small for the clock, which is a threshold effect and appears without warning when the
hardware changes.

**A third failure sharpened it further, and it is the version worth remembering.** A test
asserted that a volume solve is costed above a plane solve — sound, since a 27-point stencil
costs more than a five-point one. On CI it reported `plane 13.0, volume 11.2` and failed.
13.0 is *exactly* the documented fallback constant: the plane pilot had come in under the
timing floor and fallen back, while the volume pilot measured. **The test compared a constant
against a measurement**, and each number was perfectly reasonable on its own.

So the rule has two halves. Do not assert a value that came from measuring the machine — and
**do not compare two values that may not have come from the same place.** A fallback is
invisible at the point of comparison: both are doubles, both are labelled "s per million
nodes", and nothing in the arithmetic says one of them was never measured. The fix asserts
the ordering of the *documented* constants, which is deterministic and is what the code
guarantees, and additionally the measured pair **only when the run reports both as measured**
— printing which case applied, so a run that skipped the second assertion says so rather than
passing quietly.

## A speedup measured across two builds is a measurement of the builds

Parallelising the study drivers, the first number was **12.8x** — a 31-point scan going
from 30.6 s to 2.39 s. It was wrong, and the way it was wrong is worth keeping.

The 30.6 s baseline had been measured earlier, from a **Debug** binary. The 2.39 s came from
**Release**. Release is 3.27x faster on its own, so most of the apparent speedup was the
build. The same-binary comparison is 4.57 s to 1.50 s: **3.05x**.

**Same binary, same input, one variable moving.** Obvious stated plainly, and easy to miss
when the baseline is a number you already have written down and the new measurement is one
you just took. A remembered baseline carries no record of how it was produced.

### The control that localised the real limit

3.05x on what looked like sixteen cores is poor, and the tempting conclusion — "the parallel
machinery does not scale" — is checkable. Run the identical driver over a figure of merit
that does arithmetic instead of solving a field:

| DOP | solve-bound scan | CPU-bound control |
| --- | --- | --- |
| 1 | 1.00x | 1.00x |
| 2 | 1.57x | 2.06x |
| 4 | 2.53x | 3.93x |
| 8 | **5.25x** | 3.92x |
| 16 | 4.75x — *worse than 8* | **6.74x** |

The machinery is fine: the control reaches 6.74x and *gains* from hyperthreading. The solve
peaks at the **eight physical cores** and then loses ground, which is what a stencil sweep
does once the memory bus is saturated — the extra threads add no bandwidth and cost cache.

**Two things follow.** The machine has 8 physical cores and 16 logical, so the ceiling was
never 16; and for this workload `Environment.ProcessorCount` is a *worse* default than the
physical count, measurably, by about 10 per cent. More usefully, it says where the remaining
headroom is not: threading *inside* one solve competes for the same saturated bus, so a
multi-threaded smoother should be expected to disappoint, while a GPU — which brings its own
memory bandwidth — is the thing that would actually lift the ceiling.

**The general shape: when a parallel speedup disappoints, run the same harness over a
workload with the opposite bottleneck.** If the control scales and the real work does not,
the limit is in the work, and no amount of tuning the parallelism will move it.

## A decision the plane path made, that the volume path never did

Adding `reflectAboutX` to a volume solve looked like a one-line change: the plane path solves
half a geometry and superposes it with a mirrored copy, and `ReflectedField` mirrors a
coordinate of *any* field rather than knowing how it was meshed. So it should compose.

It came back **roughly double** — a mirrored half reading 153 V where the full solve read 99.
Doubling is the signature of a superposition adding two contributions where it should be
taking a union, and the union only works if each half contributes **nothing outside its own
domain**.

`SolvedField2D` does exactly that: it carries an outside potential and returns zero field
beyond its grid. `SolvedField3D` had no such notion at all — no bounds check anywhere. It
called the tricubic unconditionally, and a tricubic asked for a point it was never fitted
over does not decline; it continues the cubic. Measured on a 20 mm box holding one plate at
**100 V**:

| outside by | potential | field |
| --- | --- | --- |
| 0.5 mm | −1.6 V | 3.3 kV/m |
| 14 mm | −283 V | 53 kV/m |
| 80 mm | −43,113 V | 1.6 MV/m |
| 180 mm | **−486,643 V** | **8.1 MV/m** |

Four orders past the applied potential, violating the maximum principle — the cheapest exact
check there is that a solve has not gone wrong, and one this project already runs *inside*
the domain.

**It had already been observed and misread.** An Astral skeleton whose ion escaped its 635 mm
analyser was found 4,643 mm away, and that was written down as the ion "coasting" once
outside the field. It was not coasting. It was being accelerated by a field nobody declared.

**The shape to watch for: a decision taken on one path and never ported to its sibling.**
This is the same finding as the 3-D solver supporting Neumann faces that no document could
ask for, and as `ITransportMode` named only in a csproj — a capability or a policy that
exists on one side of a dimension boundary and silently does not on the other. The plane and
volume paths here are deliberately separate, because refactoring a numerical core that
carries every validated number is how those numbers get quietly lost — and the price of that
separation is that every decision has to be made twice, or it is made once and forgotten.

**The check that would have caught it years earlier costs nothing**: assert the maximum
principle *outside* the domain as well as inside. No potential anywhere — in the solved box
or beyond it — may exceed the largest applied value.

## The pattern

Every one of these produced a *plausible* number. None threw. The things that
actually caught them were:

- **Closed forms.** An exact answer to compare against turns a plausible number
  into a wrong one.
- **Coefficients rather than summary figures.** c₁ = −0.500, c₂ = 0.3756 named a
  bug that "R is disappointing" would not have.
- **Convergence behaviour, not single values.** An error that fails to fall with
  refinement, or a cycle count that grows with it, is diagnostic on its own.
- **Reverting the fix to check the test.** An assertion that passes with the bug
  restored is not testing the bug. The anisotropic-coarsening test first asserted
  the maximum principle, which held either way; the convergence factor was what
  actually moved — 0.213 against 0.303 — and only trying it both ways showed which.
- **Exact invariants.** The maximum principle — no potential may exceed the
  applied value — is a tolerance-free check that a solve has not diverged.
  Liouville's theorem is the same kind of check on the integrator, and being
  independent of energy it catches things energy conservation cannot. Both found
  bugs that presented as small, plausible drifts.
- **Enumerating a requirement's own named population.** Where a requirement lists
  what it applies to, the list is the test plan. Two GRD-2 layers were failing while
  the register said Met, and neither would have been found by asking whether warnings
  propagate in general.
- **Removing a refusal without removing its reason.** `CollisionSampler` refused a
  gas flow because it had no position to evaluate one at. Threading the position in
  made the refusal obsolete — but the trajectory run path built its gas with
  `FromModel`, which never resolves a declared velocity field, while only the
  diffusive path called `Resolve`. Lifting the refusal alone would have reintroduced
  the exact failure it existed to prevent, silently. **A guard is removed correctly
  only when the thing it guarded against is checked for directly.**
- **A transient inside the measurement window.** A stepped gas flow read a difference
  of 361 m/s against a declared 200, which looks like a physics discrepancy. The step
  sat 3 mm from the launch and the ion crossed it in six microseconds, so the "before"
  average was over three samples of an ion still accelerating from rest. Moving the
  step past the settling distance gave 204.5. **An average is over whatever the window
  contains, including the part that is not yet the thing being measured.**
- **A convenience accessor that quietly became a summary.** Adding a second
  generator turned `CompiledElectrode.DriveAmplitude` from *the* amplitude into *the
  first tap's* amplitude, and left it as a property with the same name. Every reader
  kept compiling. One of them was `ElectrodeOverlap.Agrees`, the check that refuses
  two conductors occupying the same space at different excitations — so two
  electrodes agreeing about the main RF and differing about a supplementary one were
  judged identical, and the mask kept whichever was written last. **The one check
  that exists to prevent a field of a geometry nobody described became a route to
  one.** Found by asking, after the change, which readers of the old scalar were
  asking a question the scalar no longer answers.
- **Measuring a linear property with a measurement that cannot be linear.** A
  stability boundary is a statement about a *frequency* - where the characteristic
  exponent reaches one - but the obvious way to find it is to ask whether the ion was
  lost. That requires it to travel to an electrode through the whole anharmonic
  region, so the answer depends on where it started and no amount of care removes the
  dependence: the shipped Paul trap's hold-converged edge is q_z = 0.85 at a 0.1 mm
  launch and 0.82 at 0.3 mm. **The fix was not a better loss measurement but a
  different quantity.** Reading beta off the spectrum of an ion that stays small
  locates the linear boundary to a worst residual of 1.2e-3 and needs no journey at
  all. When a measurement is amplitude-dependent and the thing being measured is not,
  suspect the measurement rather than adding controls to it.
- **A stability test cannot see a wrong operator, and a positivity test cannot
  either.** Backward Euler on the Scharfetter-Gummel coefficients is unconditionally
  stable and stays non-negative at every Gauss-Seidel sweep, and both properties
  survive a genuinely wrong assembly - gathering a neighbour with *this* cell's outward
  coefficient instead of its inward one passed every stability and non-negativity test
  in the suite. What caught it was the **Boltzmann equilibrium**, which the scheme is
  built to hold exactly and which moved by factors of 6 to 18. The rule: when a scheme
  has an exact fixed point, the test that matters is that it sits there - the
  well-behavedness tests are satisfied by too many wrong schemes to discriminate.

- **A convergence study needs the packet still in the domain.** A first version of the
  implicit order test compared densities after a window long enough for the packet to
  have been collected. What remained was a residue, and the relative L2 difference
  between two nearly empty fields came out at 39 to 71 per cent and scaled with nothing.
  It reads as a broken scheme. The same measurement over a window the packet has not
  left is clean. **A norm over a field that is mostly gone is a norm over what is left.**

- **The reference in a convergence study carries its own error, so the linear quantity
  is not what it looks like.** Comparing an implicit run at gain g against an explicit
  reference, the two are (g - 1) base steps apart, not g - so what must be constant is
  error/(g - 1), and dividing by g makes a correct first-order scheme look like it is
  converging at the wrong rate. Only visible because the gains spanned a factor of
  eight; at two gains either reading fits.

- **A figure of merit and the run that reports it must be the same computation, and
  saying so once does not keep it true.** `einzel run` flew its ions through a declared
  gas and `einzel test` flew the same model in vacuum, because the figure-of-merit path
  built the launch, the field and the detector but never a collision sampler. They
  disagreed by 95 us on the corpus example whose entire subject is a gas carrying an ion,
  and nothing compared them. **This is the second time**: `run` and `test` computing a
  flight time two ways was found and fixed once already, by collapsing them to one
  implementation - and the gas then arrived on only one side of the seam. A shared entry
  point is not the same as a shared computation, and every argument added to one of them
  is a chance for the two to drift apart again.

- **An example whose expected value coincides with the broken answer can never catch the
  break.** `gas-flow-carry` launches its ion at exactly the gas velocity so the transit
  is `L/u` by arithmetic - and in vacuum an ion launched at that speed covers the same
  distance in the same time. The vacuum answer was not close to the expectation, it *was*
  the expectation, and closer to it than the physical answer. **When choosing the
  conditions that make an example's arithmetic clean, check that they do not also make
  the failure mode invisible.**

- **A tolerance in the wrong units is an assertion that cannot fail.** Expectations in
  the test format compare a *relative* error, and one example's tolerance read `500.0` -
  written as plus or minus 500 us on 5000. As a fraction that admits any positive answer,
  so the example was in the release gate asserting nothing, and its own description said
  "discriminating far past its ten per cent tolerance" - the same misreading, written
  down. An audit of all 29 expectations found one other at 50%. **A number whose units
  are ambiguous should be read against what it would mean if taken the other way**, and
  here one reading was a tenth and the other five hundred.
, and a conservation test passes with positivity
  broken.** The quadratic B-spline deposit clamps its three-node stencil onto the grid
  at a boundary; leaving the *offset* unclamped with it makes the middle weight
  `0.75 - u^2` negative — at the very edge the weights are **1.125, −0.25, 0.125**. They
  sum to one, so charge is exact and every existing test was satisfied; a positive
  macroparticle was depositing a negative density, and the gather shares those weights so
  the self-field was built from it. **The argument that let it through was written down
  in the commit message**: the weights "sum to exactly one for any offset, which is what
  lets the index be clamped at a face without losing charge". True, and it settles a
  different question than the one that mattered. When a property is invoked to license a
  shortcut, check that it is the property the shortcut needs.

- **A reference method has approximations in it too, and comparing against it at
  default settings compares two of them.** Particle-in-cell was reported as agreeing
  with the direct pairwise sum "to a few per cent". Both numbers were right and the
  comparison was not meaningful: the sum softens at the mean macroparticle spacing and
  the grid smooths at the cell, so what was being measured was the difference between
  two smoothing lengths that happened to be comparable. Taking the sum to its own point
  limit (softening / 100, worth **3.5%**) and setting the cell to the mean spacing gives
  **0.08%** - a much stronger claim, and one that says what makes them agree. **Before
  quoting an agreement, ask what each side approximates and whether the two can be set
  to the same thing.**

- **Refinement is not always an improvement, and the case where it is not is the one
  someone will walk into.** Halving the particle-in-cell cell size past the mean
  macroparticle spacing makes the answer *worse* - -15.1%, -4.2%, +0.08%, +4.4% across a
  16x range, with an optimum in the middle. Raising a resolution number is what a reader
  does when they want a better answer, and here it silently buys a worse one. Two things
  follow: the optimum has to be *reported* rather than left to be discovered, and a
  "converges under refinement" test would have passed on the wrong side of it. Checked
  by control rather than asserted - holding the cell fixed and raising the macroparticle
  count took the error from 4.42% to 1.55% to 0.93%, which is what makes it a sampling
  artefact rather than a resolution one.

- **An argument that was right about accuracy and wrong about cost.** ACC-3 forbids
  trilinear interpolation on a trajectory path. Particle-in-cell's gather looked
  exempt - it is a self-consistent field whose accuracy the deposit already bounds,
  and the gather must share the deposit's weights or the self-force does not cancel -
  so the first version used it. The reasoning holds; what it missed is that a
  trilinear force is kinked at every cell face and an embedded Runge-Kutta estimator
  reads a kink as error. **The packet took 27 times the integrator steps**, which no
  accuracy argument would have predicted. A quadratic B-spline is smooth, keeps the
  deposit/gather symmetry, and takes it back to 2x. When exempting something from a
  smoothness rule, ask what else reads the derivative.
- **Factorial experiments over code reading.** Two binary switches and four runs
  localised a divergence to a feature nobody suspected, faster than reading the
  diff would have.
- **Running the same measurement twice, slightly differently.** Two boundary
  searches over brackets differing only in their lower end returned 680.7 V and
  694.4 V for the same trap. Either alone reads as a measurement; the pair says the
  predicate is frayed, which is what sent the observation window from 60 cycles to
  200 and put a confirmation walk into `BoundarySearch`.

- **A test that passes for a reason that will not scale to the general case.** The
  viewport's colour scale must be anchored across the whole bundle, not per path — a
  per-path scale gives two ions a kilovolt apart the same colours. The obvious assertion
  is "the reported range is wider than the widest single path", and it is *correct*: on a
  packet launched from rest the margin is 0.3 eV in 20000, or 1.5e-5. That is a real
  discrimination and a fragile one, because it depends on the ions having different
  energies at all. What discriminates whatever the magnitudes are is that **no single
  path owns both ends of the scale** — any per-path anchoring reports some one path's own
  extremes and fails it. When an assertion works by comparing two magnitudes, ask whether
  there is a structural statement of the same thing; a structural one cannot be made thin
  by a change of model.

- **A default that is right for a screen and wrong for the subject.** Helix Toolkit
  defaults to a perspective camera, which is right for a game and wrong for an
  instrument: an ion-optics drawing is read for where things are along the axis, and
  that is the one thing perspective distorts. And `ZoomExtents` fired before layout, so a
  1.3 m flight ran off the edge of the viewport — the control had been given a model but
  not yet measured, so the fit was to whatever size it had before. Both are the same
  shape: **a framework default is a decision somebody else made about a different
  problem**, and the ones that produce a plausible-looking picture are the expensive kind.

- **A build setting that is a backstop reads exactly like one that is load-bearing.**
  `InvariantGlobalization` is set solution-wide for CLI-5's deterministic output, and WPF
  cannot run under it at all — the font cache constructs `new CultureInfo("en")` while
  measuring the first line of text. The question that mattered was not "can we turn it
  off" but **what is it actually protecting**: locale-independence here comes from
  passing `CultureInfo.InvariantCulture` explicitly at every formatting and parsing site,
  and the flag was the belt to those braces. Turning it off for one assembly is therefore
  safe and checkable; turning it off in a codebase that relied on it would move every
  number a French-locale user saw. Before removing a global setting, find the thing it
  duplicates — if there is nothing, it is not a backstop.

- **The failure mode where the data is right and the picture is empty.** The viewport's
  electrodes were extracted correctly, uploaded correctly and drawn correctly, and were
  invisible — there was no light in the scene, so every Phong surface rendered at its
  ambient term alone. The instinct on seeing nothing is to go and check the data, and the
  data was fine. Where a pipeline ends in a picture, the last stage has inputs that are not
  the data: **a missing light, a camera pointing elsewhere, a transparent material and an
  empty buffer all look identical**, and only one of them is about the thing being
  computed.

- **A resolution chosen from the container, not from the thing in it.** Extracting a
  conductor's surface over the whole solve domain at 48 cells makes a cell 1.25 mm across,
  and a 1 mm plate falls between lattice planes: the three-dimensional example produced
  **no conductors at all**, with nothing said. The generalisation is that a sampling grid
  sized to the domain silently loses every feature smaller than a cell, and *the smallest
  feature is usually the interesting one* — an aperture, a slot, a gap. Size the grid to
  what is being resolved, and where that means asking an object how big it is, add the
  accessor rather than switching on its type at the call site: `CompiledElectrode3D.Bounds`
  went next to `Centre` and `CharacteristicSize` for exactly that reason.

- **A defect that only a screenshot can see, found by driving the buttons rather than
  reading them.** The first named view after startup came out as the top view whichever
  button was pressed, because the camera's look, position and up were written one property
  at a time and each raises its own change notification — the control saw a momentarily
  inconsistent basis and re-derived one of them. No assertion over the view-model would have
  caught it and no amount of reading the handler suggested it. What caught it was invoking
  each button through UI Automation and reading the axis indicator out of the resulting
  image. **Where the output is a picture, the test harness has to look at the picture** —
  and the first attempt at that was itself misleading, because clicking one button and
  believing the result is not a measurement until a second button has been clicked to
  compare against.

- **A sequential colour ramp drawn as thin lines is illegible on every background, and no
  choice of background fixes it.** Viridis spans dark to light by construction, so it passes
  through whatever luminance the ground has: measured across grounds from `#101010` to
  `#D0D0D0`, the worst contrast anywhere on the ramp never rises above **1.25**. Truncating
  the dark end barely helps — skipping the darkest 60% still only reaches 2.83. What works is
  **lifting the ramp off the ground and then moving the ground further away from it**, which
  is the opposite of the instinct the symptom produces ("it's too dark, lighten the
  background"). The general form: when a scale and its ground overlap in the one dimension
  that separates them, only one of them can be moved out of the way, and it is the scale.
  Worth measuring rather than eyeballing — the optimum here (a 0.44 lift against a `#081019`
  ground) is not a value anyone would have picked, and it is bounded by a *different*
  property: lifting further makes the ramp non-monotone in lightness, which is the thing the
  ramp was chosen for.

- **The invariant axis of a cross-section is the one the beam travels along, so that is how
  far to draw it.** A translational solve says the geometry repeats along the third axis and
  never says how far; drawing to the transverse span made a quadrupole's rods 32 mm of a
  200 mm instrument, sitting in the corner of the picture beside a trajectory six times
  longer. The reach of the ions is the part of an infinite structure anyone is looking at.
  Generalises past drawing: **where a model declines to bound something, the bound worth
  choosing is usually the one the rest of the model already implies.**

- **Correcting a framework's choice after the fact is a race; telling it the choice is
  not.** The viewport control installs a camera of its own when it has none, and the
  opening view was set afterwards from the window's `Loaded` handler — so which view a
  model opened in depended on how long its field took to solve. Three increasingly
  elaborate fixes (a later dispatch priority, an atomically-assigned camera, a deferred
  fit) each made it work on the model in front of me and fail on the next. The actual fix
  was one line: set `DefaultCamera`, which is the property the control reaches for, so
  there is nothing to race. **When a fix has to be re-tuned per case, the thing being fixed
  is usually ordering, and ordering is not fixed by trying harder at the same point.**

- **A guard that cannot be reached, found by a test that could not construct its input.**
  The viewport clamped an axisymmetric trace to the axis, because revolving a profile at a
  negative radius would draw the same surface twice. Writing the test for it turned out to
  be impossible: `ModelValidator` refuses such a document outright, with the path, the
  reason and the correction. The clamp had never done anything and never could. **A second,
  weaker copy of a rule that already holds is worse than none** — it reads as though a case
  exists, and the next person has to work out which of the two is load-bearing. The test
  now asserts the rule where it lives, which is also where it holds for every other
  consumer. The general move: when a guard is easy to write and its test is hard, ask
  whether the state it guards against is reachable at all.

- **A comment that was right, directly above code that did not follow it.** A performance
  test read *"a hard assertion here would be a test of the build agent"* and then asserted
  2,000 ms — a guess about the build agent. It had been green for weeks and then passed and
  failed **on the same commit in two CI runs minutes apart**. Chasing it found something
  larger: PERF-7's whole 50 ms budget is the cost of starting CPython (45–63 ms measured
  here), so the requirement is not separable from a term the platform does not control.
  Two lessons. **When a comment states a hazard, check that the code below it avoids that
  hazard** — the comment is evidence somebody saw the problem, not evidence they solved it.
  And **a flaky test is worth chasing to its root rather than loosening**: the loose version
  had hidden a real finding about a requirement.

- **A gate that only fires in the cases guaranteed to fail.** Fixing the above, I gated the
  absolute assertion on "the interpreter starts in under the budget" — which means it runs
  precisely when process start has consumed the whole budget and left nothing for the work.
  It failed on its second run. **A conditional assertion needs its condition checked against
  the failing case, not just the passing one**: the question is not "when is this safe to
  assert" but "what does the population of runs that reach the assertion look like".

- **A command's return type is part of the architectural boundary, and a caller acquires a
  dependency by reading a property.** `ResultsCommand` handed back
  `Einzel.Io.MeasuredJson` — the type the CLI already serialises, so it looked like reuse
  rather than a decision. The shell then referenced `Einzel.Io` without a single `using`
  directive being written for it, because reading `measured.Uncertainty.Lower` is enough.
  UI-1's invariant test caught it, which is the point of having the invariant checked by a
  machine: nobody reviewing the view model would have seen a boundary being crossed, since
  the code that crosses it looks like ordinary property access. **Where an assembly
  boundary matters, the types on the public surface are the boundary** — not the using
  directives, and not the project references somebody remembered to omit.

- **A control experiment before a fix, when the symptom appeared alongside a change.** The
  shell window started rendering blank white immediately after I added three new panels, so
  the panels were the obvious suspect. Reverting the XAML and rebuilding produced the same
  blank window from code that had rendered correctly an hour earlier — the graphics state
  had degraded over about fifteen launches and hard kills in one session. Without the
  control I would have spent the time "fixing" working code. The visual tree was
  independently readable through UI Automation throughout, which is what let the work
  continue: **when the display is unreliable, assert on the content rather than the
  picture** — and for "are the right numbers in the right cells" that is the better check
  anyway.

- **A designed scan is not a sample, and putting a sampling interval on one is a category
  error.** Closing the GRD-1 envelope gap meant resampling the ion cloud to get an
  uncertainty for statistics that have no closed-form error. It worked, and it also
  produced intervals for models that declare *no* cloud — where the acceptance is swept
  deterministically, evenly spaced and seed-free, the same every run. Resampling that
  reports the scan's own spacing as though it were a sampling error. The codebase had
  already been bitten by the same confusion from the other direction: `DefaultEnergySpread`
  carries a paragraph written because somebody compared a deterministic sweep with a
  cloud's random draw and read the difference as noise in an objective. **Before attaching
  an uncertainty, ask what would have to vary for the number to vary** — if the answer is
  "nothing, it is the same every run", there is no uncertainty of that kind to report.

- **The bootstrap is inconsistent for extreme-order statistics, and I demonstrated it by
  accident.** Wanting a statistic with no closed-form error, I picked the range — and its
  estimated error went 0.181, 0.313, 0.225 across a sixteenfold increase in sample size,
  not falling and not converging. That is a known property: a resampled draw can only
  contain values already present, so the resampled maximum comes from a handful of the
  largest observations however many replicates are taken. It matters here rather than
  academically, because *the widest entry radius that still arrives* is an extreme-order
  statistic and this project has already had to replace one such measurement with a count
  over a fixed grid after it gave 0.65 mm on one radius grid and 0.20 mm on another for the
  same geometry. The test now asserts the **failure**, because a test that tolerated either
  outcome would document nothing and one demanding success would demand something untrue.

- **"Absent" and "zero" are different answers, and so are their reasons.** A model with no
  source temperature has a turn-around time of exactly nought — an analytic statement about
  the model, needing no ensemble and carrying no sampling interval. My test asserted it
  should be absent, on the reasoning that ensemble figures need a cloud. Both halves were
  right and the conclusion was wrong: it is not that the figure could not be computed, it is
  that it *is* nought. **When a figure is missing, the question is not "was there enough
  data" but "is there a value" — and sometimes the value is zero and the evidence is a
  derivation rather than a measurement.**

- **A heuristic about size, standing in for a statement about behaviour.** Checking that a
  sequence's phase-to-phase diff discriminated, I asserted a phase must change fewer than
  all its electrodes — reasoning that marking everything would mean the diff had stopped
  discriminating. The corpus's extraction moves **both** its plates, to +500 V and −500 V,
  because push-pull extraction is what it is. The assertion was a claim about instrument
  size wearing the costume of a claim about the diff. Replaced by recomputing the expected
  diff in the test and comparing it exactly, which cannot be satisfied by a wrong
  implementation and does not care how many electrodes there are.

## A rotation about the wrong axis is silent

The Astral skeleton tilted its mirror stacks with `tiltAxis: "x"` to make the two mirrors
converge along the drift, which is the mechanism that reverses an asymmetric-track
analyser's drift and the entire point of the model. **A rotation about x mixes y and z**,
so what it actually converged was the two *boards*, across the gap the ion flies down. The
mirror surfaces have their normals along **x**, and a rotation about x leaves them exactly
where they were.

Everything worked. The model validated, solved in the same number of cycles, flew an ion,
and **the drift decelerated and reversed** — because converging boards do decelerate a
drift, through the transverse confinement stiffening as the gap narrows. It is a real
mechanism, roughly three times weaker, and it produced a reversal convergence 8× the
published spacer that was then attributed to unknown electrode depths.

**What caught it was an analytic model disagreeing by a constant factor.** A tilted mirror
gives each reflection a z-impulse of θ times its x-impulse, and the x-impulse is fixed at
`2·m·v_x`, so `Δv_z = −2·v_x·θ` per reflection whatever the mirror depth. Inverting the
measured deceleration through that relation gives an *effective oscillation period*, which
came out at 151 µs where the ion's real period is ~32 µs. A 4.7× shortfall that no
parameter error explains is not a parameter error.

The generalisation: **a geometric transformation can be well-formed, validated, and
applied to the wrong degree of freedom, and the result is a working model of a different
instrument.** Nothing in a units check, a schema check or a convergence check can see it,
because none of them knows what the rotation was *for*. What sees it is a closed form for
the effect the transformation is supposed to produce — and the tell is a discrepancy that
is a constant factor rather than a trend, because a wrong mechanism has its own scaling.

The control that settles it costs one run: **reverse the tilt sign.** One sign shortened
the transit and one lengthened it and reversed the drift, which is what a tilt must do. A
mechanism that decelerated whichever way the mirrors leaned would not be a tilt.

## A binary predicate over a diverging observable throws the physics away

Finding where an ion stops arriving was posed as `einzel boundary` over "does a flight
time exist". That predicate is false three different ways — the drift reversed, the ion
struck an electrode, or the flight-time ceiling was reached — and a bisection cannot tell
them apart. Two of four points in a depth scan turned out to be measuring *arrives →
strikes metal*, and the trend drawn through them was reported as physics.

**The underlying observable was not binary at all.** The drift decelerates, so the transit
lengthens smoothly and diverges: 185.13, 198.87, 238.18 µs before it stops arriving.
Fitting `Z = v_z0·t − ½·k·c·t²` to **two** runs recovers the launch drift speed and the
deceleration constant, agreeing with the eleven-evaluation bisection to 14 per cent and
with an independently measured drift speed to 0.9 per cent.

Two rules. **Report the outcome, not its shadow** — a run that says `StruckElectrode` and
names the surface distinguishes all three cases for free, and the figure of merit that
returns null for each does not. And **a bisection result is only comparable across runs
that share a bracket**: the same geometry searched over `[0.02, 2.0]` and `[0.01, 8.0]`
gave 1.99 mm and 0.31 mm, which reads as a non-monotone physical trend and is two
different questions.

## A warning emitted and not read costs the same as one never emitted

`BoundarySearch` throws when both ends of a bracket agree, and walks outward from the
converged bracket to raise `boundary.multiple-crossings` when the predicate flips back.
Both were built for exactly the failure above. Neither helped, because the analysis script
read `boundary.value` and never looked at `warnings`.

This project has fixed *the shortest spelling discards the evidence* four times inside the
engine — `FieldAssembly.Build` dropping its `SolveReport`, the sweep evaluator dropping
its warnings, `CollisionSampler`'s two unread flags, `SampledOutsideDensity`. The fifth
was outside it, in throwaway analysis code, and cost a wrong conclusion that reached a
tracked document. **Engine-side discipline about carrying evidence does not survive the
boundary into the scripts that consume it**, and scripts are where results are actually
read.

## A flight-time ceiling impersonates physics

Looking for the convergence at which an analyser's drift reverses, `c = 0.20 mm` came back
`MaximumFlightTimeReached` at z = 312.1 mm, short of the 325 mm detector — which reads
unambiguously as an ion that turned round. It had not. Raised from a 450 µs ceiling to
2000 µs, the same model **arrives**, at 478.47 µs: the ion was moving forward the whole
time and was 13 mm short when the clock stopped.

The failure is that `MaximumFlightTimeReached` is one outcome for two situations — the ion
is confined, or the run was too short — and near a threshold the transit **diverges**, so
the slowest arriving cases are exactly the ones the ceiling clips. A threshold search over
that predicate finds the ceiling and reports it as the physics, and it will be a smooth,
plausible function of the parameter being scanned.

**The check is to look at where the ion ended, not that it ran out of time**, and to raise
the ceiling until the answer stops moving. Same family as the incomplete-arrival trap
already documented for `einzel compare`, where a mean transit over the subset that arrived
is not a transit time; and the same family as the Paul trap's ejection edge, where "did the
ion reach an electrode within N cycles" measured the hold rather than the design.

## A cancellation is a claim about the mechanism, not about the algebra

Two measured quantities divided one another and the messy term dropped out: out-and-back
time `2·v_z0/(k·c)` over oscillation period `2·v_x/(L·k)` cancels `k` *and* the period, and
leaves `N = α·L/c`. It was written up as a design law — the oscillation count depends on
the injection angle, the drift length and the spacer and on nothing else.

**It is a factor of 1.84 out.** N is 45.94 measured against 24.95 predicted. The algebra is
right and one of the two expressions is not: `k·T = 2·v_x/L` is the *specular* result, and
it holds only if the mirror delivers the full `2·m·v_x·θ` of z-impulse per reflection. This
one delivers **0.578** of it.

The failure is that **the cancellation was doing double duty**. It was a piece of algebra,
and it was also — silently — the assumption that the idealisation feeding it was exact.
Nothing in the derivation flagged which of its inputs was measured and which was modelled,
because after cancelling, neither appears.

What repairs it is to *name* the residue rather than let it hide in the coefficient:
`η = k·T·L/(2·v_x)`, and `N = α·L/(η·c)`. The form is exact and was worth trusting — checked
by the `1/c` scaling over a threefold range and, more sharply, by the **`α/c` invariance**:
scale both and predict no change, which held to 0.5 per cent while the turning point moved
as `α²/c` and so flew a genuinely different trajectory each time. Only the coefficient was
wrong, and it turned out to be the useful part: `η` is a property of the mirror, it is
measurable, and it is the figure of merit an earlier scan had been looking for and missing.

**The check that would have caught it immediately is to evaluate both sides.** The relation
was validated against a *time* — 1275 µs predicted against 1356.96 measured, 6 per cent —
which is a real check and does not touch the coefficient, because the same fitted `k` sits
on both sides. Counting the oscillations needs one extra thing, a trajectory, and it is the
only measurement that puts the modelled quantity and the observed one side by side.

## A fitted parameter with a closed form is a free validity check on the fit

Fitting a deceleration from two flight times returns two numbers, and only one of them was
wanted. The other — the launch drift speed — is fixed by the ion's energy and launch angle
at `sqrt(2qV/m)·sinα`, and the fit is not told it. So every fit carries its own audit: if
`v_z0` does not come back at the closed-form value, the fit is not describing the thing it
was set up to describe, whatever the parameter of interest looks like.

It earned its keep twice within an hour. Padding the solve domain moved the fitted `v_z0`
from 858.3 to 877.8 m/s against a closed-form **877.69** — so the unpadded model was
absorbing its boundary's distortion into a quantity that cannot depend on the geometry at
all, and **the fit converging onto the closed form is a second, independent reason to pad**.
And a mirror-depth point returned `v_z0` = **14,453 m/s** with an efficiency of 10.57, from
combining a decelerating trajectory with one that turned out to be *accelerated* five-fold
along the drift. Two trajectories of different kinds still fit a rate; they produce a number,
not an error.

**The number looked fine.** 10.57 sits plausibly beside 0.578 if nobody is checking, and it
is exactly the sort of point a power law gets drawn through — which is how the retracted
depth trend happened in the first place. What made it visible was not that it looked wrong
but that a quantity with an independent value disagreed with that value.

The generalisation: **when a fit returns more parameters than you need, check the ones you
did not want against whatever fixes them.** They cost nothing to compare and they are the
only part of the fit that can be wrong in a way the residual cannot see.

## One scalar cannot set the resolution of a shape with three extents

Sub-cell geometry has now been lost twice by the same mechanism wearing different ratios.
First by sampling an electrode over the whole solve domain: a 1 mm plate in a 60 mm box at
48 cells falls between lattice planes and produces **no surface at all, silently**. That was
fixed by extracting over the electrode's own bounding box — which is correct, and assumed
the box is roughly cubic.

**An analyser's stripe is 4 mm thick and 635 mm long, so its own box is as badly proportioned
as the domain was.** 48 cells across the longest span gives 15 mm cells, the metal again lies
between two sample planes, and the surface is again empty. The padding made it worse: 8 per
cent of the *largest* extent applied to *every* axis put 50 mm of padding around a 4 mm board,
so the metal occupied a fortieth of the box before sampling began.

**The obvious repair causes the mirror-image failure.** Take the step from the *thinnest*
span and the slab resolves — and its length is tessellated at the same spacing, giving 72,000
triangles for what is a box and a 77 MB file for sixteen of them. Both failures are silent and
neither raises anything; one produces nothing and the other produces far too much, and only
the second is even noticeable.

The repair is per-axis: `clamp(cells × span / longest, 4, cells)`, with padding a fraction of
each axis's own extent. On an isotropic shape every axis lands on the requested count and
nothing changes, which is what keeps the existing sphere checks meaningful.

**The rule: a resolution set by one scalar over a shape with three independent extents will
mis-serve some shape, and which one depends on which extent the scalar came from.** There is
no correct single choice — the longest loses thin things, the thinnest bankrupts long ones,
and the mean does both a little. If a routine takes one `cells` and applies it to a box,
ask what it does to the worst aspect ratio it will ever see, because the answer is never
"nothing" and is usually silent.

The test that discriminates is a *pair*: a slab must mesh **with its thickness** — a surface
can be extracted and be a sheet, which has triangles and looks plausible — and it must not
cost its aspect ratio in triangles. Each mutation fails exactly one of them, and an isotropic
control catches the case where a fix changes what was already right.

## A taper inside a long channel does nothing, and the geometry looks right the whole time

Modelling the Astral's ion foil, the first parameterisation was a pair of plates above and
below the ion path with the **gap to the axis tapering along the drift** — narrow where the
returning force is wanted, opening away from it. The geometry was built and verified: the
taper ran the right way, its slope fitted to 2.2 per cent of the declared value over 26
sampled levels, and the electrodes cleared both the mirrors and the boards.

**The on-axis potential varied by 0.0003 V across the whole drift, at a −20 V bias.** Not
small — zero, against a requirement of 2.9 to 3.7 V derived from the published numbers.

The plates spanned the entire field-free region: 339 mm long with a 12 to 31 mm gap. **Deep
inside a channel bounded above and below by one equipotential, the potential is that
equipotential**, and the taper does not enter — the gap only matters within a few gap-widths
of an end, and a channel that long effectively has no ends. So the structure floated the
whole drift region uniformly to the bias, and a uniform offset has no gradient and exerts no
force at all.

**Nothing about the geometry was wrong.** Every check run against it passed — extent, taper
direction, slope, clearances, mesh — because they were all checks that the solid was the
intended solid. What was wrong was the assumption that the intended solid would produce a
field that varied. Those are different claims and only the second one mattered.

Two things generalise.

**A shape check is not a field check.** Confirming an electrode is where it was meant to be
says nothing about whether it does what it was meant to do, and for anything relying on
fringing or partial coverage the two can diverge completely. Measure the field the geometry
was built to produce before building anything on top of it — a figure of merit, a fit, a
study. Here that ordering cost one solve and saved building an optimisation around a shape
with no authority.

**Aspect ratio decides whether a conductor is felt as a shape or as a level.** A conductor
long compared to its gap is a *level*: everything inside sits at its potential. It becomes a
*shape* only when its extent is comparable to its distance from the point of interest. That
is the same fact the multipole rods and the funnel rings rely on, met from the side where it
destroys the effect rather than the side where it creates one.

## Fixing a multiplication in one path is not fixing it

`einzel estimate` costed a **study** at one evaluation until that was corrected — the number
was short by the evaluation count, silently, in the command whose whole job is saying what a
run costs before it is started. The fix landed, was tested, and was written up.

It left the **model** path charging for a single trajectory whatever cloud the source
declares. The shipped rectilinear trap declares two thousand ions, so its estimate was short
by a factor of two thousand, in exactly the same way and for exactly as long.

**The reason is worth naming: the fix was made while thinking about studies.** Every line
read in that session was a study line, the tests written were study tests, and the model path
computes the same quantity a few hundred lines away without the multiplication. Nothing about
working on the first would surface the second — the two look identical from inside whichever
one is open.

So: **when a quantity is multiplied by a count in one path, ask the same question of every
other path that computes it.** The grep is `TrajectorySeconds` or whatever the quantity is
called, and it takes a minute. Both of these were found by *using* the command rather than by
a test, which is the more expensive way round.

The fix also has to carry the same reasoning the first one worked out — a diffusive run steps
a density and produces no trajectories, and a space-charge run advances its packet in lockstep
so its members are not independent flights. In both, the model's own cost already is the whole
run, and multiplying double-counts. That logic existed, correct, one method away, and had to
be repeated rather than shared, which is its own small warning.

## A tilt that moved only metal-to-metal edges was invisible to the cut cells

**Symptom.** An asymmetric-track analyser's mirrors were declared converging by 0.3 mm and
the drift decelerated and reversed, so the mechanism "worked" and produced a deceleration
curve, a reversal threshold and a scaling law right to 12 per cent. The efficiency of a
single reflection against the specular `2·α·v` was 0.447 at a 4 mm cell, 0.303 at 2 mm for
the same tilt, and **−2.82** at 2 mm for a ten-times larger tilt. An analytic tilted mirror
gave 1.0025.

**Cause.** The strips abut on a flat board. Rotating them about the axis in the board plane
slides only the edges *between* strips, and both nodes across such an edge are Dirichlet.
Cut cells store, per node, how far a conductor surface is — meaningful only for a node in
vacuum. A metal-to-metal edge therefore has no sub-cell representation and is rasterised at
node resolution: invisible below one cell of displacement, a staircase above it. The one
metal-to-vacuum face that did tilt (the mirror mouth) supplied the ~8% that leaked through,
and it happened to carry the decelerating sign — so the artefact impersonated the mechanism.

**And the sign was backwards.** Once the tilt was made visible, the kick pointed the other
way: the declared geometry diverged along the drift. Two independent errors whose product
looked like the right answer, for weeks.

**Why it survived.** The tilt was verified "proportional to a thousandth of a cell" — on
parallel plates, whose tilted faces are metal-to-vacuum. That test was correct and did not
generalise. The mechanism's *efficiency* came out as an unexplained constant, 0.578, and was
given a name and a physical interpretation instead of being measured against the field.

**Discriminating test.** Two strips on a board, tilted identically: abutting gives 0.09 of
the tilt in the field, one vacuum cell between them gives 1.10, untilted gives 0.000.

**Rules.** A discretisation check on one boundary type says nothing about the other; any
geometric perturbation — tilt, offset, taper — must be tested on the boundary type it will
actually move. When a mechanism's efficiency is an unexplained constant between 0 and 1,
measure the field directly before naming the constant after the physics. And a sign should
be checked against an analytic control *before* the first result that depends on it is
written down, because a wrong sign combined with a wrong magnitude can look exactly right.

## A signal below the discretisation floor cannot be resolved, only constructed

**Symptom.** An asymmetric-track analyser's mirrors converge by 200 microns over 350 mm.
Declaring that tilt on the electrodes and solving the geometry in three dimensions gave a
drift deceleration of 3.54, 0.011 and -0.57 times the closed-form answer depending only on
how wide the vacuum gaps between mirror strips were, and 1.52 at half the cell size. No
mesh refinement was converging it.

**Cause.** The entire signal is the field anisotropy `Ez/Ex = tan(alpha)`, which is 2.9e-4
here. A second-order solve on a 2.5 mm cell across a 40 mm gap carries about 0.4% of field
error - fourteen times the signal. The answer was not badly meshed; it was below the floor.
No affordable mesh would have fixed it, because reaching 2.9e-4 of field accuracy needs
h/L near 1e-2 in every direction at once.

**Fix.** Stop differencing for it and construct it. Solve the two-dimensional cross-section,
which is exact in the plane, and *rotate the solved field*: rotations commute with the
Laplacian, so the rotated field is the exact solution for the rotated geometry, and
`Ez = tan(alpha) Ex` comes out of the coordinate transform rather than out of the grid. The
anisotropy went from wrong by factors of several to exact at 5.4e-20.

**A shear is not a substitute**, and it is the obvious thing to reach for. Laplace is not
shear-invariant, so the sheared field is not a solution of anything; and a shear of a mirror
pair translates both mirrors the same way, which is a rigid translation of the instrument
rather than a convergence.

**Rules.** Before meshing for a small effect, compare its size against the discretisation
error of the solve that will report it - the ratio, not either number alone. Where a
geometric perturbation is a rigid motion, apply it as a coordinate transform on the solved
field rather than as a change to the geometry being solved. And a quantity that swings in
sign under an irrelevant modelling choice is not a bad measurement of the right thing, it is
a measurement of the modelling choice.

## The drawn flight was not the flown flight

`einzel run --vtu` on a collisional model wrote a trajectory that crossed a 2.5 mbar funnel in
ten microseconds on the axis, beside a result that said 589 µs and a strike on the exit plate.
The reportable flight time comes from a convergence study that flies the declared gas; the
trajectory file came from a second integration in the same method, written to sample at the
model's cadence, that was never handed the collision sampler. Both were "the run". Nothing
checked that the two agreed, because the drawing was never compared with a number - it is the
one artifact people look at with the numbers out of sight, which is exactly why it must not be
the one that lies.

The general form has now been met three times in three seams: the figure-of-merit path
(`einzel test` flew in vacuum), the regime inspector (the path flown in vacuum and the gas
numbers reported along it), and the trajectory file. **A shared entry point is not a shared
computation.** Every integration that claims to be the run has to be handed everything the
run was, and a test that reads the artifact back against the reported number is the only
thing that catches the next one - `TrajectoryFileGasTests` asserts the file's last instant is
the reported flight time on a model where vacuum and gas differ fifteen-fold, and fails with
the sampler removed.

## Asking the pressure a question that belongs to the mode, in the renderer

`render section` of a *diffusive* model stamped the figure `QUALIFIED` with
`regime.trajectory-above-validity` - "trajectory integration is not the description of this
physics" - for a mode it was not using. The renderer computed the trajectory mode's regime
warnings from the gas alone. RND-8 had already been fixed by asking `ProducesTrajectories`
of the mode rather than inferring from the pressure; the regime stamp two functions away was
still inferring. Seventh appearance of the pattern this file records under "a time-varying
quantity reached through a time-free interface" and its cousins: a property of the *run*
(its mode, its drive) asked of a proxy (the pressure, the DC) that stops being equivalent the
moment a second mode exists. The fix is one condition; the lesson is to grep for every place
a gas is present and check what each one asks.

## A confined ion at a nominal q of 0.92 was the geometry telling the truth

The linear ion trap template, on its first flight, held an ion at a nominal Mathieu q
of 0.92 for four hundred RF cycles with the excitation off - past the tabulated
stability edge of 0.908, where it should have struck a rod within a few microseconds.
The same document with its hyperbolic polygons swapped for round rods lost the ion in
3.3 µs. The run-path field was checked over a cycle and was exactly a cosine; the
snapshot field's quadrupole term was 0.9994 of the ideal. Everything pointed at the
polygon shape being wrong in the run and right in the snapshot, which is not a thing
that can happen, since they are one object.

The snapshot had been measured on the **unstretched** geometry. The paper moves the x
rod pair out 0.75 mm to compensate the slot, and the ideal formula
q = 4eV/(m r0² Ω²) puts every vertex at r0. With the x pair at 4.75 mm the quadrupole
term is 0.822 of the ideal, so the ion at "0.92" was at 0.757 and confined for a good
reason. Nothing in the engine was wrong; the number being compared against was
computed from a geometry the document did not describe.

Three things generalise. **A stretched trap's nominal q is not its q**, and every
commercial trap is stretched - the paper's own q scale is the effective one, inferred
from a measured secular frequency (its 368 kHz at "q = 0.83" is the ideal beta to a
tenth of a per cent), which is how every trap user calibrates and is why the
discrepancy never shows up in the literature. **Measure the coefficient from the solved
field before calibrating anything against a formula**: the multipole projection at half
the inscribed radius gives it in a second, with no ion involved. And **swap the shape,
not the physics, to isolate a shape**: the round-rod control settled in one run what the
field-over-a-cycle check and the multipole check together could not, because both of
those were asked of the wrong geometry.

## A polygon that costs one hundred and sixteen kilobytes is the wrong spelling

The first linear-ion-trap template wrote each hyperbolic face as twenty-five vertices,
each vertex two expressions over the parameter surface, eight half-rods: 116 KB of
generated JSON that validated, solved and flew correctly and that no person or agent
could read, edit or diff. The document was parametric in the letter - every vertex
moved with `inscribedRadius` - and not in spirit, because the thing a reader needs to
see, "the face is this hyperbola from the slot edge to the half-width", was buried in
two hundred copies of itself.

The fix was a *run*: one vertex entry with a `count` and an `index`, evaluated that many
times - `repeat`'s mechanism applied inside one electrode. Twenty-four kilobytes, three
entries per half-rod, and the hyperbola written once. The rule: when a generator script
is needed to write a document, the format is missing the abstraction the script
supplies, and the script is a measurement of how big the gap is.

## A DC offset written x-up, y-down is zero on the axis and makes no well

The three-section trap's end offset was first written the way the quadrupolar DC is: the
x pair raised, the y pair lowered. That is a change of Mathieu a, exactly zero on the axis
by symmetry, and the axis potential came out flat along the whole rod - no well at all -
while every electrode reported the potential asked of it. The end sections' offset is a
common one, all four rods of a section together; that is what raises the axis. The test
that caught it asked for the well to rise by the end of the centre section and got a
number smaller than the launch point's, which was the geometry answering a different
question correctly. The rule: when a potential is applied to a symmetric set of electrodes,
say which symmetry it has - common, quadrupolar, dipolar - because each is a different
field and the document cannot tell them apart from the numbers.

## A softened force is a switched-off force when the packet is thinner than the softening

The direct-sum space charge softens the mutual force below the mean macroparticle
spacing, and the spacing is set by the packet's RMS radius over the cube root of the
count. A linear trap's cloud is a line: ten millimetres long and fifty microns across. Its
RMS radius is dominated by the length, so forty macroparticles are spaced 1.7 mm and the
force across a 0.05 mm packet is softened to nothing. A scan with four thousand ions came
back identical to one with none, to the last digit, and nothing said so, because the
softening was reported nowhere. It is reported now, against the packet's thinnest declared
extent, on every direct-sum run. Two rules: a method with an internal length scale must
compare it with the smallest scale in the problem, not the typical one; and identical
results across a parameter that should matter are not corroboration, they are a method
that is not seeing the parameter.

**And the rule itself was the wrong rule for anything but a ball.** "The mean spacing" was
computed as the radius over the cube root of the count, which is the spacing of points
filling a sphere - and a line's RMS radius is its length, so the spacing came out as if the
forty points filled a ball ten millimetres across. Points filling a box are spaced at the
cube root of its *volume* over the count, and the volume goes as the product of the three
extents. Written from the packet's three standard deviations, an isotropic packet gets the
old number to the bit and the line cloud drops from 3.8 mm of softening to 0.19 mm: still
3.8 times its transverse size at forty macroparticles, because forty points along ten
millimetres are a quarter of a millimetre apart whatever rule is used, and the count that
brings the softening inside the packet falls from 1.6 million to about 2,200. The lesson
is about the word "mean": a mean spacing is a property of a distribution's shape, and a
formula that reduces the shape to one radius has assumed the shape.

## A null result needs a theorem or it is a shrug

The cooled cloud scanned with its own charge on gave the same peak at every population, to
a hundredth of a unit, three times past the instrument's load. The first two null results
on this question had each been a defect - a softening that switched the force off, then a
cloud spread across the trap instead of along it - so a third read as one more. It was
not: in a field linear in position the mutual forces cancel in the centre-of-mass equation
by the third law, and a dipole excitation drives the centre of mass, so the resonance a
harmonic trap ejects at cannot depend on how many ions are in it. Written as a test, the
theorem holds to 1.8e-14 m against members scattered by 18 mm. Two rules. A null result is
only a result when the mechanism that should have produced an effect is named and shown
not to apply, and "the force was on" (the per-ion instants moved) is not that. And a
theorem the model reproduces is worth more than an effect it shows, because it says which
part of the field the real effect has to live in - here the anharmonic part and the
ejection - and so where the next measurement goes.

## A view-model test cannot see a layout, and the layout was wrong

The extension manager's four tests passed on the first run: the licences were right, the
undeclared one said so, the containment gaps were present, and the read was journalled. The
pane was still wrong. It sits in a docked panel about 270 px wide, and the seven-column
grid I had written truncated its headers to "Exte / Li / Wh / Re" and showed the licences as
"Ap" and "NC" - so the one field the pane exists to surface was the one it cut off. Rewritten
as a stacked template it reads properly, and the containment lines then needed wrapping too,
having been clipped mid-word at "needs a restricted token and a job ob".

Neither defect is reachable from a view model: both are about width. What found them was
driving the window through UI Automation and looking at the screenshot, which this project
had already needed once, for a camera that silently opened in the wrong view. **A pane whose
purpose is to make one field legible has not been tested until somebody has looked at it.**

A smaller one alongside: the first driver script searched the whole window for a button
named "Read" and clicked whichever came first, which on one run was the sequence editor's.
Scope an automation search to the subtree you mean.

## A bootstrap over one realisation is not an error bar for a chaotic system

The cooled-cloud capacity study reported a 19 per cent broadening as significant: the
control's interquartile width and the pushed one's had bootstrap bands that did not overlap.
Then the control was re-run with a different cloud seed and *nothing else changed*, and its
band did not overlap the first control's either. Two runs identical in every physical
respect were being called different.

A bootstrap resamples the draw you have. It measures the sampling error of the statistic and
says nothing about the variability of the draw itself, which in a trap where each ion takes
340 collisions over 1.7 ms is the larger of the two. The right error bar is **independent
realisations**: three no-push seeds gave 1.320, 1.531 and 1.597, and every pushed run fell
inside that. The rule generalises to any measurement on a chaotic trajectory ensemble - if
the statistic is a width and the system amplifies perturbations, replicate the seed before
believing a band.

A corollary about the per-ion view, which looked like the sharper measurement and was not:
switching the push on moved each ion's ejection by 1.6 u rms, and quadrupling the population
moved that to 1.74. A mean-field perturbation scales linearly; this saturates. What is being
measured there is decorrelation, and its magnitude says nothing about the force that started
it.

## Two configurations differing in more than the variable you are attributing

The volume trap's peak came out three times wider than the cross-section's, which reads
immediately as the axial degree of freedom. The volume solve runs at a 1 mm cell because a
0.5 mm one takes minutes; the cross-section's default is 0.125 mm. Running the cross-section
at 1 mm gives the volume's width exactly, from a geometry with no axial motion at all - so
the attribution was to the wrong variable, and the true statement is that both are
mesh-limited at 1 mm.

Same shape as the pair-sum benchmark that appeared to show a 1.14x speedup rising to 3.74x:
in both cases two configurations differed in something besides the thing being measured, and
in both cases the cheap control - three minutes of cross-section, one time-budgeted rerun -
settled it. **Before attributing a difference to the interesting variable, list what else
differs between the two runs.**

## A lambda in a branch that is never taken still allocates, on every call

The packet integrator's stage loop was made allocation-free by pooling twenty-two arrays a
step, and 504 bytes a step remained. The number did not move with the member count, which
is what gave it away: a per-member leak scales, and this did not. Seven stages times 72
bytes is a display class holding seven captured variables - the closure of a lambda in the
`if (parallel)` branch, which on the serial path is never entered. **The compiler hoists
captured variables when the method is entered, not when the branch holding them is taken**,
so a lambda anywhere in a hot method costs its closure on every call through it. Moving the
lambda into its own method took the loop to zero bytes a step. The general rule: when a hot
method must contain a lambda for one path, put the lambda somewhere else.

## A benchmark run a handful of times measures the warm-up

The first comparison of the vectorised pair sum against the scalar one reported 1.14x at 60
macroparticles rising to 3.74x at 2000, and the shape of that curve invited an explanation
about cache behaviour and loop overhead. It was warm-up: the rep counts were 555 down to 3,
so the small cases amortised the jitting and the large ones did not. Run against a time
budget and taking the cheapest of seven batches, the same measurement is 2.55x at 60 and
3.33x at 2000, and the scalar rate is flat at 140 Mpair/s across the whole range - which is
the sanity check the first version lacked. This project had already written the rule down
for `AllocationDoesNotGrowWithStepCount`: a floor sampled once is not a floor. It applies to
any performance number, and **the tell is a quantity that should be flat and is not**.

## Measure which half is slow before optimising the half you assumed

The pair sum in a pushed packet is O(N^2) and the field evaluation is O(N), so the sum is
obviously the thing to speed up - and on a solved *volume* at the macroparticle counts
actually used, it is 27% of a stage while the tricubic field gather is 73%. Vectorising the
sum, done first because it was the obvious target, is worth 1.04x there. Spreading the field
loop across cores is worth 1.64x. On a *cross-section*, where the field is cheap, the
ordering reverses exactly. Both were worth building, but the recommendation given before the
measurement named the wrong one as the thing that would unblock the 3-D study.

## A cloud is long along the launch direction, and the trap's axis is somewhere else

A source declares a longitudinal and a transverse spread, and longitudinal means along the
launch direction. A linear trap's ion starts at rest, so its direction looks like a
formality, and the space-charge study left it at x - toward the rods. The "line along the
axis" was a line across the trap, most of its ions started inside the metal, and the
scan's ejection ledger was full of strikes at the first step. Nothing refused it: a source
inside a conductor is refused, and a cloud whose *tail* is inside one is not. For a
packet at rest the direction is not a formality, it is the one thing that says which way
the packet is long, and it has to be the trap's axis.

## A domain edge is a boundary condition, not a conductor

With slots in all four rods and a detector behind one, ions ejected through the other
three left the trap, crossed the grounded domain edge, and coasted for the rest of the run
in field-free space, reported metres away as still in flight. The edge holds the potential
at zero; it stops no ion. Both trap templates now carry grounded housing walls on the sides
without a detector: electrically nothing, since the edge was grounded anyway, but a named
surface a loss can be charged to. A model whose ions can leave the solved box must say
what they meet there, or the ledger reports a physical impossibility as an outcome.

## A bounded field's edge is a wall, and the instrument has no wall there

A region bounds an analytic element with a step: full strength inside, nothing outside. For a
DC element that costs a potential step, which the run reports. For a confining RF it is worse,
and it took a funnel to show it: a packet delivered to the tunnel entrance at radii up to a
few millimetres met the whole pseudopotential well at once at the region's face — `Ψ(r)` for
whatever r it happened to arrive at — and 42 per cent of it stopped in the last two
millimetres before the RF began. Pushing harder made it worse, 54 per cent at 50 V of funnel
drop and 71 at 100 V, which is what identified it: a barrier, not a push too weak.

The real ring stack's field decays over about a bore radius, and across that fringe the well's
radial gradient squeezes an ion toward the axis while its axial gradient is still small. The
hard edge has the axial gradient infinite and the radial one absent until it is too late.

**A boundary the model has and the instrument does not is a force the instrument does not
have.** Where a modelling boundary stands in for a real electrode's fringe, give it the
fringe's shape — here a declared ramp over a bore radius — and say in the run that it is
declared rather than solved.

## A validity check written on a proxy fires on the proxy

The pseudopotential's mesh check asked whether the ion's quiver was larger than the cell
the effective potential was resolved on, and took the density grid's cell for that. The
argument behind the check is about the *field*: an average over an excursion describes
something only if the oscillating field is roughly linear across it, and a solved RF
sampled on a mesh coarser than the excursion is being averaged over interpolation. The
density grid was a stand-in for the field's mesh, and on every model so far the two were
about the same size.

The TIMS tunnel's confinement is an analytic quadrupole — exactly linear, so the cycle
average is exact whatever the quiver — solved beside a DC gradient on a 0.25 mm cell and
stepped on a density grid with a 0.125 mm radial cell. At the bore the quiver is 0.29 mm.
The check fired as a non-suppressible violation on a field for which the thing it guards
against cannot happen, and would have put that violation on every run of the template.

**When a check is written against a proxy, name the quantity the proxy stands for and ask
that instead.** Here the quantity is the resolution of the *oscillating* members of the
field — infinite for an analytic drive, the solve cell for a solved one, and not the DC
gradient's cell however fine or coarse — so it is now a member of the time-varying field
interface with the conservative default. The funnel, whose RF is solved on the cell the old
check happened to use, warns exactly as before.

## A fast scan must run through the stability edge

At 200 kDa/s the ramp passes from the excitation's resonance to the stability edge in
forty microseconds, and an excitation that has not ejected an ion in that time hands it to
the edge. A ramp that stopped short of the edge, at q 0.895, held a third of the ions to
the end of the run and reported them held; extending it to 0.94 ejected them and put them
in the tail of the peak, which is where the instrument has them too. What the scan window
covers is part of the measurement, and a window chosen for a slow scan is not a window for
a fast one.

## Positivity is not the failure mode I expected, and my test had no teeth

The explicit Scharfetter-Gummel step is bounded by a stability limit, and the textbook
consequence of exceeding it is a **negative density** — the thing exponential fitting exists
to prevent in the first place. So when the mixture solver gained a shared step, taken as the
shortest any species needs, the obvious test was that no species goes negative when a fast
one is stepped at a sluggish one's rate.

It passed with the wrong-step mutation restored. Told to take the first member's step —
seven times too long for the quick member — the lowest density anywhere stayed at exactly
zero, and the run looked fine.

What actually happened is that the quick species finished with **1.0376% more ions than were
launched**. The scheme did not go negative; it created matter. The test now asserts the
conservation ledger, which is the sharper claim anyway: a population that grew has no
defensible reading, where a small negative density can always be argued as round-off.
Positivity is still checked, because it costs one pass over the grid, but it is not what
carries the test.

The general form: **the failure mode a scheme is famous for is not necessarily the one it
exhibits at the parameters you are at.** Run the mutation before believing the test, and if
the assertion survives, find the quantity that does move.

## A convenience the schema forces on you is worth measuring before working around it

A study of the tandem tunnel varied two things across eight runs — how many ions each
population held, and whether their charge was modelled — and re-solved fifty-five rings and
two funnels for every one of them, because the obvious spelling builds the field from the
model inside the loop. It ran for over an hour without reaching its first line of output.

Neither varied quantity touches an electrode. `ExecuteMixture` takes the field as an
argument precisely so a caller can hoist it, and hoisting it is the whole difference between
a study that finishes and one that does not. The same shape as the energy-sweep loop that
built a field per ion: **a per-iteration cost that does not fall as the iteration count
rises is a fixed cost in the wrong place**, and it is invisible to a suite that only checks
answers.

## The printer kept asking which mode this was, when the question was whether there is a number

`einzel run`'s terminal output decided whether to print a flight time with
`run.Diffusion is null`. A sequenced run then walked into `flight time NaN +/- NaN`, so the
line became `run.Diffusion is null && run.Sequence is null`. A mixture would have been the
third addition to that list, and the comment above the line already said so in as many
words.

A list of the modes known when a line was written is not the question the line is asking.
The question is **did this run produce a single arrival time for an ion**, which is a
property of the result: false for a density, which has no ions; false for a sequence, which
ends on its own clock rather than on an arrival; false for a mixture, for both reasons at
once. It is now a **required** member of `RunOutcome`, set at each construction site with
its reason, so a fifth kind of run fails to compile until somebody decides which it is —
the same device as the outcome switch that throws on an unrecognised case.

This is at least the fourth quantity here to be fixed by widening a list and then have to be
fixed again by replacing the list with the question. The others were the success set of
outcome names, twice, and the diffusive requirement gate.

## A capability is wired into every path or refused by the ones it is not

Mixtures landed and were wired into `einzel run` for a plain diffusive model and into
`render section`. A review of the branch found three other paths that took a mixture, did not
fail, and answered about an ion the document never declared.

The mechanism is the same in all three. A mixture declares its mobilities per species and is
refused a `transport.mobility`, so the compiled mobility falls back to one **derived from the
gas cross section**; and `MassSi`/`ChargeSi` are documented as the first population's, "enough
for the diagnostics that need AN ion". Together those two make the single-species path *work*
on a mixture rather than throw — it runs a fifth, invented ion.

- **A sequenced mixture.** The run fork tests for a sequence before it tests for a mixture, so
  a mixture with an elution ramp — the configuration the feature was built for — took the
  sequenced path, which steps one density.
- **Every diffusive figure of merit.** `einzel test`, `sweep`, `scan`, `optimise` and
  `boundary` computed `transitTime` and the rest from that invented ion. A project test
  pinning the number would pass while measuring the wrong thing, which is worse than a failure
  by exactly the margin that makes it believable.
- **`render section --at-us`.** Accepted and discarded, because the mixture stepper records no
  intermediate densities.

All three are now refused or reported by name. Refusing is the right first move rather than
wiring: stepping several populations through a timeline needs the sequenced path to take the
mixture stepper, and a refusal that says what is missing is honest where a plausible
single-population answer is not.

**The rule that generalises: when a new capability makes an existing type polymorphic, the
paths that consumed the old singular form do not fail — they read the fallback.** After adding
one, grep for every consumer of the fields the new form leaves at a default, and make each one
either handle it or refuse it. `IsMixture` was honoured in three places out of six.

## A test whose fixture is wrong reports the refusal it was not testing

The test for the figure-of-merit refusal wrote its own project test file, guessing
`"model": "models/x.json"` and `"figure": "transitTime"`. The scaffold uses
`"../models/reflectron.json"` — relative to the *tests* directory — and `"figureOfMerit"`.

So the run was refused for a **missing model**, which is a refusal, from the right command,
with a non-zero exit. Only the message gave it away. A test asserting merely "this is refused"
would have passed while exercising nothing, and the guess that produced it was mine twice over
— I had made the same relative-path assumption about a render spec an hour earlier and
corrected it the same way.

**Assert on the refusal's own text, not on the fact of one.** Every guard in this project
carries a constraint sentence for exactly this reason.

## A baseline must differ from the measurement in exactly one thing

Two timing tests in the extension sandbox failed on Windows CI on consecutive runs of the
same branch, and passing in between — so the code was not the variable. Both assert a
*floor*: how fast the sandbox can stop a runaway, and how little the boundary costs above
starting an interpreter.

**One was a statistic problem and the fix is the project's own precedent.** The timeout test
measured a single kill. A floor sampled once is not a floor, and a slow sample says the agent
was busy — so it now takes the fastest of seven and prints them all, exactly as
`AllocationDoesNotGrowWithStepCount` does. The bound did not move. **Taking a minimum is not
widening a bound**: if every sample is late the minimum is late and the test still fails,
where widening admits the failure permanently.

**Correction, from a later CI failure: the change went on the wrong term.** The paragraph
above says the timeout test "now takes the fastest of seven". What took the fastest of seven
was `Bare` - the interpreter-start *baseline* - while the kill it is subtracted from stayed a
single measurement. The assertion is about `elapsed - bare`, and on the Windows agent that
failed it next, bare was **61 ms against an elapsed of 13,674**: the sampling had been applied
to the term contributing 0.4 per cent of the answer and not to the one contributing the rest.

Both terms sit in the same expression two lines apart, and the write-up describes the
reasoning correctly and the code inaccurately - so re-reading either the code or the lesson
alone would not have caught it. **When a statistical fix is applied to a difference, name
which term was sampled**, because "it now takes the fastest of seven" reads as a statement
about the measurement and was a statement about its baseline.

The kill is sampled now, three times rather than seven because each sample has to wait out
the declared timeout. On an unloaded machine the three come out at 1244 / 1236 / 1231 ms
against a 1200 ms declared timeout, so the enforcement genuinely is a tight bound and what
the old single sample measured on CI was the agent.

**The other was a baseline problem and is not fixed.** The round-trip test already took the
cheapest of seven interleaved pairs and still failed on CI, so contention was not the cause.
The diagnosis is that its ratio is not scale-free: it compares "start Python" against "start
Python, import two modules, load a module from disk, create a scratch directory, and marshal",
so an agent with slow I/O inflates one side only.

A baseline importing what the host imports was built to close that gap, and it reported a
**negative platform share** — a round trip cheaper than the work it contains, which is
impossible. Matching the empty environment the runner uses, then the stream redirection, then
the isolation flags, each moved the number and none removed the sign. It was reverted.

Three things worth keeping from that.

**An impossible intermediate value is a stop signal, not a curiosity.** The negative share
appeared in the first measurement and it took three more rounds of matching before it was
treated as disqualifying. A ratio below one, where one side strictly contains the other's
work, cannot be a slow machine or a noisy sample.

**Reverting was the right outcome and it took longer than it should have** — the change would
have made the bound *looser* while leaving it just as meaningless, which is worse than the
flakiness. Effort already spent is not a reason to ship.

**And the write-up was wrong before the code was.** The note explaining the revert claimed the
negative share was pre-existing on the bare baseline too. It is not: the bare comparison
reports +5.8 ms, 1.18×, positive and small. The claim was written from the shape of the
argument rather than from a measurement that was one command away.

## A sign change recovers a geometric quantity that a magnitude only fits

Stranded on `astral-foil-energised` and rescued before that branch was deleted. The
configuration it was measured on - a single foil bias, one number for the whole drift - has
since been superseded by sixteen independently-biased stripes, so **the numbers below describe
a model this project no longer ships**. The method is what survives.

The question was whether a foil modelled as a centred potential well was really acting as one,
or whether its parameters had simply been tuned until the ion turned round in the published
place. With two free parameters, matching a published distance proves very little.

**The discriminating measurement is where the effect changes sign.** Energising the well pulls
the turning point inward when the ion turns beyond the well's centre, and pushes it outward
when the ion turns near the centre - because an ion turning just past mid-drift was accelerated
over almost its whole outbound path. So the crossover, where energising the foil makes no
difference at all, must sit at **twice the well centre**, and that is a geometric quantity
nothing in the dynamics was told.

The well was centred at 175 mm by construction, predicting a crossover at 350; the measured
crossover interpolated to 334.8 mm, 4.3 per cent out, with nothing fitted to it.

**Matching the published distance could be a coincidence of two free parameters. Recovering the
well centre could not** - it is a number the tuning had no access to. Where a model is fitted
to a magnitude, look for a quantity of the model that shows up as a *zero* or a *sign change*
rather than as a size: a fit can be dragged onto any magnitude, and it cannot manufacture the
location of a crossing.

Not re-measured against the sixteen-stripe foil, and it should be before it is relied on: the
argument assumes a single centred well, and a graded stripe law need not have one.

## Fixing a stale list means checking the item you promote, not only the one you strike

The ranked *What to do next* list had its item 1 pointing at a study that four later commits
had already answered, so it was struck with its result and the next open entry was promoted to
the top - by reading that entry's prose rather than the code it describes.

**The promoted entry was stale too, and by an hour.** It asked for a per-node cache of a
ramped diffusive phase's cycle-averaged well, written at 06:31; the cache landed at 07:29 the
same morning, in a commit that *also* corrected the entry's arithmetic ("about a sixteenth"
is really 2.72x, because the direct term is itself a cycle mean of the potential) and reported
the thing that actually matters - that on the shipped analyser the cache rebuilds 30 of 30 and
saves nothing, because the well moves 1.8e-5 between successive instants for a reason nobody
has explained. So the new item 1 asked for work that was done, and buried the open question it
should have named.

**A stale entry is evidence the list is stale, not that one entry is.** The completed item and
the entry below it went stale by the same mechanism at the same time: someone finished a piece
of work and updated the code, the changelog and the register, and not the ranked list. Striking
the top one and promoting its neighbour untouched promotes a note of the same vintage - and the
promotion carries more authority than the entry ever had, because it now reads as *the next
thing to do*.

The check is cheap and it is the same one that catches a stranded branch: take a distinctive
phrase from the entry and run `git log -S` on it. That finds the commit that wrote the entry
*and* anything that superseded it, in one command. Here it returned the two commits an hour
apart, which is the whole story.

The same applies to the copies. This claim lived in three places - the changelog, the ranked
list, and the device write-up - and correcting one would have left two documents disagreeing
with the code and with each other. When a claim is worth repeating in three documents it is
worth grepping for in all three before it is repeated a fourth time.

## A control that measures a different quantity neither confirms nor refutes the hypothesis

A ramped diffusive phase's cycle-averaged well moves 1.8e-5 between successive steps on the
shipped TIMS analyser, so the cache that exists to keep it rebuilds every step and saves
nothing. The obvious candidate was the ramp advancing inside the averaging window, so that a
slow drift is averaged as though it were part of the quiver. It was written down as **refuted
by its own control**: a hundredfold slower ramp did not reduce the jitter.

**The mechanism is real.** `SequencedRun.Instant` returns a shifted *view* of a still-varying
field rather than a frozen snapshot, so the ramp does keep moving inside the window, and the
term it injects has a closed form that reproduces every measured digit across a hundredfold
in rate and a sixteenfold in amplitude.

**What it produces is a bias, and the control measured a jitter.** A linear ramp's slope is
constant, so the injected term is identical at every instant: the well is offset by the same
amount every step, and differencing successive steps cancels it exactly - at the 1e-15
floating-point floor for every rate, including the one where the bias is 2.4e-4. So the ramp
contributes no step-to-step change at all, and slowing it could not have moved the number the
control was watching, whatever the mechanism.

The hypothesis was therefore never tested. It was neither confirmed nor refuted, and the
refutation went into three documents.

**The check that would have caught it is one sentence**: before running a control, say which
quantity the hypothesis predicts a change in, and confirm the control measures that quantity.
Here the hypothesis predicts a change in *the well*, and the control measured a change in
*the difference between successive wells* - a derivative, which annihilates exactly the
constant the hypothesis predicts. A control on a derivative is blind to every hypothesis
whose prediction is constant, and that is a large class.

Two smaller things from the same investigation.

**A first-order closed form failed where it should have been checked hardest.** The bias is
`-2rT/(NA)` to first order plus the drift's own variance, and the variance term is 1 per cent
of the answer at the smallest amplitude tried. Asserting the first-order form gave ratios of
1.0000, 1.0000, 0.9899 - and the one that missed is at the extreme of the sweep, exactly where
an eye reads a discrepancy as the model beginning to fail rather than as a term left out.
Keeping the second-order term makes it exact everywhere.

**And the guard that exists says the right thing about the wrong case.**
`PonderomotiveField`'s constructor refuses a field whose shortest period is not finite,
arguing that "a field that varies in time because a parameter ramps is not an oscillation and
is not to be averaged". That argument is correct and it fires only when the field is a ramp
and *nothing else*. A field that is a ramp **and** a drive passes the guard, and every elution
scan is one. A guard whose stated rationale is broader than its condition is worth re-reading
when a new configuration arrives.

## A control built to be exact can remove the variation it was built to find

Hunting a 1.8e-5 step-to-step wobble in a ramped diffusive run's cycle-averaged well, I wrote
a synthetic field carrying the suspected mechanism and sampled it at instants **exactly one
drive period apart**. That made the no-ramp control bit-exact, which felt like the right thing
to want: any residue is then unambiguously the ramp's.

**It also pinned the RF phase at which the averaging window opens, and the phase was the whole
effect.** The covariance between a linear drift and a sampled sinusoid is
`(A r T/N)[-cos(phi0) + cot(pi/N) sin(phi0)]`, because the two sampling identities differ -
`sum s cos(2 pi s/N) = -N/2` but `sum s sin(2 pi s/N) = -(N/2) cot(pi/N)`, five times larger at
N = 16. Sampling at whole periods holds `phi0` at zero, which keeps only the small
phase-independent term and reports the phase-dependent one, five times bigger, as absent. The
conclusion drawn was "the ramp produces a bias and no jitter", and it was wrong for the same
reason the earlier recorded control was wrong: the measurement could not see the thing.

Re-sampled at offsets that are not whole periods - which is what a real run does, since a
diffusive step is set by a stability limit and never lands on the drive - the swing matches its
closed form to every digit, and predicts the real device's 1.8e-5 as 1.94e-5.

**The general form: an exactness you engineer into a control is a constraint you have imposed
on the system.** Ask what it holds fixed, and whether the hypothesis predicts variation in
exactly that. Here "sample a periodic system at its own period" is the natural way to make a
comparison clean, and it is stroboscopic - it is guaranteed to hide anything whose signature is
phase.

Two smaller ones from the same afternoon.

**Three samples of a sinusoid under-read its range by 13 to 25 per cent.** The first version of
the corrected test used offsets a third of a period apart, which gives only three distinct
phases, and reported 0.85 of the closed form. That reads as the model being imperfect rather
than the sampling being coarse - and 0.85 is a plausible-looking number, which is what makes it
expensive. Sixty-four offsets across a period give 1.0000.

**Two obvious fixes were arithmetic away from being wrong.** Averaging more finely does not
help: `cot(pi/N) -> N/pi`, so the swing tends to `4rT/(pi A)` and stops depending on N. And
detrending the window rather than de-meaning it removes `6/(N^2-1)` of the well - 2.4 per cent
at N = 16, five thousand times the jitter it would remove - because the least-squares slope of
a sinusoid sampled over one period is not zero. Both took one line of algebra to reject and
both would have taken a day to discover by implementing.

## An interface default decides for every implementer, once, invisibly

Adding `AtOperatingPoint` to `ITimeVaryingField` needed five of its seven implementers to do
something and two to do nothing, so it was given a default of `return this` - correct for every
field whose time dependence *is* the oscillation, and correct for the two that needed nothing.

**It was also silently wrong for one of the five, and a review of my own change is what found
it.** `SequencedField` implements the interface, needed to hold its state selection, and did
not override. It compiled, every test passed, and the result was that a staged *analytic*
element inside a driven superposition kept averaging across phase boundaries while the solved
half of the same model was held - the two element kinds disagreeing about which instant they
were describing, which is the exact defect the change existed to remove.

**The default is what hid it.** A member with no default fails to compile until every
implementer decides; a member with a safe-looking default decides for all of them at the moment
it is written, and the implementer that needed to disagree gets no compiler error, no test
failure and no reason to be looked at. Both properties that made the default the right call -
that it is correct for most implementers and cheap for the rest - are what made the omission
undetectable.

The rule: **when adding a member with a default, enumerate the implementers and say, for each,
why the default is right.** Seven names is a two-minute list, and it is the only step that
distinguishes "the default suits this one" from "this one was never considered".

### Two more bugs one line away, and the assumption that carried them

Tracing that omission through `SequencedField` found two others in the same class, both older
than this work and both of the shape this file already records repeatedly.

**It discarded the time when delegating to its state.** `ElectricFieldAt(pos, t)` selected the
state by `t` and then called the state's *time-free* accessor, so a driven state's oscillation
was pinned at whatever that answers. That is the eighth appearance here of a time-varying
quantity reached through a time-free interface answering at an arbitrary instant, and unlike
the previous seven it bit in **both** transport modes: an integrator flying through a sequenced
driven analytic element saw its RF standing still.

**And its shortest period was unconditionally infinite.** Two consequences, both silent - the
diffusive path takes an early return on a non-finite period, so such a field was never
cycle-averaged; and step control had no drive period to cap against.

**The assumption was written down, which is why it is worth keeping.** The property's own
remark said "a sequence of static states has nothing oscillating in it" - true of every
sequence anyone had built, and not a property of the class, because `FieldAssembly.Sequenced`
wraps whatever the per-phase build returns. A remark that states an assumption about a class's
inputs is a note to check it against what actually constructs the class; here the constructor
call site was one file away and had been able to supply a driven state all along. The sibling
property `OscillatingResolutionLength` asked the states rather than assuming, so the class
already contained the correct pattern beside the incorrect one.

### And a floor is measured, not asserted

The test for the fix asserted the residual movement was below `1e-13` against a measured
6.5e-14 - one and a half times of headroom on a floating-point floor that depends on the
machine, the JIT and the solver's iteration count. It now measures the floor in the same test,
on the same geometry with a phase that holds instead of ramping, and asserts the same order
against that. The two come out at **6.53e-14 and 6.57e-14**: the held result *is* the
arithmetic floor, which is a stronger statement than any absolute bound, and it cannot fail on
another runner for a reason that has nothing to do with the fix.

## Two loops in one file, and one threads five times better than the other

A driven diffusive step has two expensive loops over the density grid: the coefficient sweep,
which evaluates the field at every node, and the red-black Gauss-Seidel sweep that advances the
density. Both are over the same 33,345 nodes, both write only their own slots, and both are
bit-identical however the rows are divided. **One goes 5x on sixteen cores and the other goes
1.09x**, measured three runs each with no run-to-run spread at all.

| loop | serial | threaded |
| --- | --- | --- |
| coefficient sweep (the ramped probe) | 26 s | **15 s** |
| red-black sweep (the held probe) | 12 s | 11 s |

**And the difference is readable in the source rather than mysterious.** The coefficient sweep
does seven cycle-mean potential evaluations per node, each a superposition of bicubic
interpolations over the solved channels - arithmetic-heavy, and its working set is a couple of
megabytes, so it is cache-resident. The Gauss-Seidel sweep does a handful of floating-point
operations per cell against several array touches plus an **integer division and modulo per
face per cell**, from decoding a neighbour index. That is indexing- and memory-bound, and
threading it competes for a bus that is already the limit.

CLAUDE.md records the ceiling from the other side: parallelising studies peaked at 5.25x on the
solve and lost ground past eight physical cores, while the arithmetic control reached 6.74x and
gained from hyperthreading, and the note ends "expect a threaded smoother to disappoint for the
same reason". That was right about the smoother and **wrong about the loop next to it**, which
is the whole lesson: the memory-bandwidth ceiling is a property of a loop, not of a subsystem,
so a loop is measured rather than classified by what it sits beside.

**The 1.09x was rejected, and that is the other half.** It cost a lock, a thread-local
reduction, a second code path and thirty-eight lines lifted out of their loop, for eight per
cent. A change whose measured return does not justify its complexity should not be kept just
because it works - and the measurement is worth more than the change would have been.

### Three smaller things from the same afternoon

**A reduction can be threaded when it is a maximum and not when it is a sum.** The sweep
accumulates `Math.Max` over cells, which is associative and commutative, so a per-partition
maximum combined at the end is bit-identical to the serial one. A *sum* is not: the rounding
would depend on how the work was divided, which is why `PacketIntegrator` threads its
applied-field loop and deliberately leaves its symmetric pair sum serial.

**A lambda in a branch allocates on the path that does not take it.** The parallel loop lives
in its own method, because a lambda's captures are hoisted when the method is *entered* rather
than when its branch is taken - the 504-bytes-a-step finding this file already records. Writing
`if (parallel) Parallel.For(...) else for (...)` in one method would have put that allocation
in the serial path of a per-step loop, which is exactly where CMP-1 forbids it.

**And the body was moved out verbatim.** The row methods take sixteen parameters, named as the
enclosing locals were, so not one line of the arithmetic was retyped. A long signature is the
cheaper price in a numerical core - the same trade the 3-D solver made by being written beside
the 2-D one rather than by generalising it.

### The comparison the project's own conventions would not allow

The check I most wanted was the same grid through both paths in one process, compared to the
bit. Reaching it needs the threshold and the sweep to be visible to a test, and **this
repository has no `InternalsVisibleTo` anywhere** - `internal` here means "for the sibling
class in the same assembly", not "for tests". Widening that for one test would have traded a
convention for an assertion.

What replaced it is stronger. Setting the threshold to **1** takes every diffusive test in the
suite through the parallel path, so the Boltzmann equilibrium still has to hold to 8.9e-16, the
ion ledger to 100.0000%, and the corpus drift tube to `L/(mu E)` - closed forms this engine had
no part in, over 1,355 tests. A bespoke equality assertion of mine would have been weaker than
that and would have looked more thorough.

**And it found something, which a bespoke assertion would not have.** One test failed:
`TheCacheRemovesEveryFieldEvaluationAndNoPotentialEvaluation` reported **8,337,733 field
evaluations against a true 9,609,600**. Not the physics - every closed-form test passed - but
the test's own `Counting` wrapper, whose `Fields++` is a read, an add and a write, so concurrent
increments lose counts. **A test's own instrumentation is part of what has to be thread-safe
once the code it counts is**, and an `Interlocked.Increment` fixes it.

The part worth keeping is how nearly it hid. At the shipping threshold that test's grid is 561
nodes, below the cut, so it takes the serial path and passes - the defect only exists on a
configuration nobody ships. It would have waited there until somebody lowered the threshold or
grew the grid, and then presented as the well cache having regressed.

## A shortcut that trades accuracy for speed is only as good as the ratio it assumes

`einzel estimate` omitted the drift limit for any model whose field had to be solved, and said
so in the code:

> solving the field to estimate the cost of the run defeats the purpose of estimating

**That is sound in general and wrong by four orders for one mode.** For a trajectory model the
solve really is most of the cost, so the argument holds. For a diffusive one the run solves the
same field and then steps through it a hundred thousand times, so the solve is 1.81 s of
197,000 - and omitting it left the gate reporting **8 s for a fifty-five-hour run**, on the one
verb whose entire job is deciding whether to commit hours.

**The assumption was never written down, which is why it outlived the case it was true for.**
The comment gives the conclusion - solving defeats the purpose - and not the ratio the
conclusion rests on, which is that a solve is comparable to a run. Where a shortcut trades
accuracy for speed, **state the ratio it assumes**, because a second mode will arrive for which
the ratio is different and the sentence will still read as true.

The corrected form is structural rather than a threshold anybody has to defend: **a diffusive
run cannot be cheaper than its own solve.** So solving to estimate costs at most the run's
unavoidable floor, whatever the model, and the gate now reports what it spent. It is bounded
too - the diffusive mode is two-dimensional, so this is never a volume solve.

**And the same fix found the ninth instance of the recurring defect, in the cost gate itself.**
The sampling loop read `field.ElectricFieldAt(point)` - the time-free arm - so for a driven
geometry it took the RF at an arbitrary instant rather than the cycle average the run drifts
through. `GRD-8`'s claim is that estimate and run *call the same function*; that was true of the
step rule and not of the field it was handed.

**The check that says the two now measure one thing** is not the agreement itself but its
independence: the estimate finds a 58.5 ns step, and counting the probe's assemblies - one per
step in a ramped phase - gives 56 ns from a completely separate route.

## Nothing had ever read a result document back, and both halves of the round trip were broken

`einzel report` reads `results/*.result.json`. It is the first thing here that ever has:
`verify` walks the manifests and checks hashes without opening a result, and `test` re-flies
the model rather than reading a stored answer. So the first reader found two defects on its
first run against a real project, and both had been true for every run this project has
stored.

**The sequenced run path wrote a manifest and no result.** Three of the four paths wrote one;
this one wrote provenance and no answer. It is the path every TIMS study takes, so the runs
whose answers were missing were exactly the ones most worth reading. This is the recurring
"a capability wired into N-1 of N paths" shape, and what is new is *how* it was found: not by
a failing test, but by writing the first consumer. **A producer with no consumer is unchecked
however many tests it has**, because every test written from inside the project asserts what
was computed rather than what was stored.

**And a result document did not read back into the record that wrote it.** The emittance
fields are `required double?` - the surface's way of saying the construction site must decide
and the answer may be nothing. `WhenWritingNull` omits a null on the way out and C#'s
`required` demands the property on the way in, so one keyword carried two meanings and they
disagreed. The document was unreadable exactly when a value was *absent* - a trap, or a run
where nothing arrived - so PRJ-3's "regenerate and compare" was impossible for every ensemble
run of that kind, and nothing said so because **writing succeeded**.

The general statement: **`required` on a serialised record is a claim about the document as
well as about the constructor**, and a default that omits nulls turns the two into a
contradiction.

### And my first fix was the wrong one of the two, which a test written months earlier caught

Writing every required property including its nulls round-trips just as well. It also
**changed the published document** for every ensemble run - and this surface's own policy,
recorded in four places, is that an undefined measurement is *absent*, with a test spelling
out why: "a consumer distinguishes 'no orientation' from 'zero' by the key not being there."
Any consumer that had believed the surface would have started reading a null where it expected
absence.

Both fixes satisfy the rule as I had written it down, and one of them breaks something else,
so the rule as written was not precise enough. **The precise version is about which
requirement is which**: absence of `required int Launched` is a malformed document, and
absence of `required double? EmittanceMmMrad` is this surface's own encoding of *no value*, so
demanding it on read was demanding that the encoding not be used. Relaxing the requirement on
the reading side fixes the round trip and changes no byte of output.

**What generalises: when two fixes both satisfy the rule you wrote down, the rule is
underdetermined, and one of them is probably changing something the rule was not about.** The
tell here was that one fix touched the output and the other did not - and the thing being
fixed was an input.

The other half is worth keeping too. The test that caught it was not mine and not new: it
existed because a NaN Twiss angle had taken the serialiser down long before, and it asserted
the *encoding* rather than only the absence of a crash. **A test that pins a wire format is
what makes a compatibility break visible as a failure instead of as a support question.**

**My first version of the test could not have caught it.** A packet that arrives writes every
field and round-trips fine; the case that discriminates is a run whose ensemble measured
nothing. That is the straddling rule this page already carries in three other places - a test
whose parameter sits on one side of the value the behaviour switches at is a test of a
different regime - met here on an *absence* rather than on a dimensionless number.

### The corrected claim, and my first version of it was wrong

I wrote, in the code and in the commit I was drafting, that `einzel verify` "had nothing to
check a sequenced run against". **It did not need one.** Verify enumerates manifests and
compares the model hash and the solver-behaviour version; it never opens a result. So it had
been reporting sequenced runs as current all along - correctly, about drift, over an answer
that was not there. The defect is real and its consequence was one step further away than I
first wrote it: not that verification was broken, but that there was nothing stored for a
*reader* to read.

**Worth keeping because the wrong version is more satisfying.** "The verifier was blind" is a
sharper story than "a document nobody read was absent", and it was the version I reached for
before checking what verify actually opens.

## A mark on everything marks nothing

The report gives an unsuppressible warning a hatched band - the device `Einzel.Render` puts
across a tainted figure, so the vocabulary is already this project's. Keyed on
`IsSuppressible`, which is false for **everything above advisory**, so it landed on a
housekeeping note about a convergence floor and on almost every warning there is.

GRD-3's argument is that a validity violation must never be skimmed. The failure mode it
warns about is a false alarm on that class teaching readers to ignore it; **the same
teaching happens when the mark is true of nearly everything**, which is the other direction
and is not written down anywhere in the requirement. Four severities exist and they mean
different things, so the page has four levels and the hatch is for `ValidityViolation` alone.

The headline count moved for the same reason. "Runs carrying an unsuppressible warning" is
almost every run - eleven of this project's own thirty-nine corpus examples carry one while
behaving exactly as designed, which was measured when the exit code was being decided and
already written down. Counting them in a masthead would have been the same mistake in the
same file twice.

## A fraction and its interval live in the same units

The page printed `transmission 0.00 %, interval 0 to 1`. Both are correct; only one had been
scaled. Two numbers about one quantity in two different units, side by side - which is the
ambiguity section 9 refuses a model document for (`{"energy": 4000}` is a validation error on
purpose), met on the output side where nothing refuses it.

The value was a hundred times smaller than the run measured, and it read as plausible: a
transmission of 0.00 % is exactly what a trap reports. **A unit error is worst where the wrong
answer is a legitimate value of the same quantity.**

The second half is rounding. `F2` turns 99.9976 % into `100.00 %`, which claims every ion
arrived when 0.24 of ten thousand did not - and for a diffusive run the density's tail is
precisely where the loss lives. The format now widens only where rounding would land on
nothing or on everything without being there, so an ordinary figure stays short and the two
values that carry a claim of completeness are never claimed falsely.

## I made the defect I had just written a command to expose

`einzel report` exists because the sequenced run path stored a manifest and no result - one
of four paths missing a capability the other three had. The first version of the report
looked in `results/` for `X.result.json` and nothing else.

**A study writes `X.json`, not `X.result.json`.** So every sweep, scan, optimisation and
boundary search in a project was reported as a run that had stored nothing, with the warning
about it attached - and the projects with the most work in them would have said it loudest.
The same "wired into N-1 of N" shape, in the command written to expose that shape, inside an
hour.

**What would have caught it is enumerating the writers, and I enumerated the readers.** I
checked which run paths wrote a result and found the gap; I did not ask which *commands*
write into `results/` at all. `grep` for the directory would have given two answers in one
line. **When fixing an "N-1 of N" defect, the N is the set of writers, and the count you
arrive at by reading the code you are already in is a count of the ones you were looking
at.**

The fix generalises rather than listing kinds: **the answer sits beside the manifest under
the manifest's own stem** - `.result.json` for a run, `.json` for a study - so a fifth kind
of study would be found by the same rule.

**And the three empty states had to be separated.** A run with no stored answer, a study's
answer this page does not draw, and a document this build cannot load all show an empty
table, and each calls for something different: run it, read it another way, report a defect.
The first version filed the first and third together under one count and told the reader to
do the wrong thing.

## Two assertions covering two failure modes, and I had which was which backwards

A packet held against a moving gas settles to `sigma^2 = (kT/q)/|dE/dx|` - the Einstein
relation cancels the mobility, so the width is a property of the analyser and not of the ion
in it. The test asserts two things: where the packet parks, and how wide it is. I wrote down
that the *width* was the discriminating one, on the argument that a broken drift-to-diffusion
ratio would move it with the mobility.

**Running the mutations said otherwise, exactly:**

| mutation | parking point | width |
| --- | --- | --- |
| Einstein relation (`D`) x 1.44 | x 1/1.44 = 0.694 | **unchanged, 1.0000 mm** |
| thermal voltage (`kT` in the flux) x 1.44 | x 1.44 | x sqrt(1.44) = 1.2000 mm |

Scharfetter-Gummel's zero-flux state is `exp(-q phi / kT)`. The equilibrium involves `kT` and
**not** `D`, so the width is invariant to the diffusion coefficient *by construction* and `D`
sets only how fast the packet gets there. What the balance point tests is the identity
`q dphi/kT == v h / D` between the field term of the exponent and the gas term - **which is
the Einstein relation.** So the parking point is the Einstein check and the width is the
Boltzmann check, and neither alone covers the other.

**The rule: a test with two assertions is two tests, and which failure each catches is a
measurement rather than a reading.** Both of mine were necessary and I could not have said
which was which from the algebra I had in front of me. The habit that pays is running one
mutation per suspected mechanism and writing the table down, rather than arguing from the
formula about what a wrong implementation would do.

### And a third assertion that catches neither

The companion test asserts the width does not move with the mesh - 0.00 um across a fourfold
refinement, which is a strong and tolerance-free property, because the discrete equilibrium
*is* the continuous one. **It passes under both mutations**, reporting 1.2000 mm at every mesh:
internally consistent and absolutely wrong. Mesh-independence is a claim about resolution and
says nothing whatever about the value. It earns its place beside the closed-form comparison
and would be worthless in place of it.

## Seven hours of a study that has never once finished is worth nothing, and the reason is the absence of a checkpoint

The TIMS front-end sequence has now failed to complete three times: 4.75 CPU-hours at
512 x 64, 40 minutes at 256 x 32, and 7.6 wall-hours at 512 x 64 against an estimate of 4.17
before a Windows update rebooted the machine. **It has produced no output on any attempt**, so
the one thing that would settle whether the estimate is low or the run does not terminate has
never been observed.

Two rules, and the second is the one that cost this.

**A run measured in hours needs to emit something before it ends.** The engine writes its
result at the end, so an interrupted run leaves nothing at all - not a partial density, not a
step count, not the per-phase widths it had already computed. A study whose output is
all-or-nothing at hour seven is a study that cannot be run on a machine somebody else
administers.

**And the question did not need the long run.** The open question - is the arrival width the
analyser's own or the delivery's - was answered by two runs of 39 and 122 minutes that differ
in *one parameter*, both stopped at the end of the trap, because the width the ramp reads is
the width the packet has when the ramp starts. Measuring the decisive quantity directly was
about eight times cheaper than measuring it at the end of the pipeline that produces it, and
it removed a second claim (that the ramp is modelled right) from the answer. **Before paying
for the whole sequence, ask which phase the number actually lives in.**

**Both are now built, and the first one cost less than the argument for it.** `einzel run`
reports on stderr every thirty seconds by default and rewrites a checkpoint beside its
manifest, holding where it is, the phases that finished whole, and each population's center
and width. The interval is a default rather than a flag because **a flag somebody has to
remember is a flag that is not set on the run that gets killed**, and the file is removed when
the run writes its answer, so finding one means the run did not finish - which needs no
timestamp comparison to read. `docs/cli.md` carries the details.

Three things it taught while being built.

**A projection from total elapsed time is a projection of the solve.** The first version
divided wall clock by the fraction of the phase simulated, which charges the one-off solve to
every remaining microsecond: 82 minutes against an actual 23 on the shipped analyzer, and
17,576 minutes on the very first report, where one step had been taken. Measuring the rate
*between* reports fixed it and held at 23 minutes across three consecutive reports. Same rule
`einzel estimate` reached when it started excluding process start: **a rate measured over a
window containing a fixed cost is not a rate.**

**An observer handed a live buffer has to be proved harmless.** Reporting is cheap only
because the solver hands over its own density rather than a copy, and *which* steps report is
set by the wall clock - so a consumer that wrote into what it was shown would produce numbers
that still looked like measurements and were not even reproducible. Two runs of one seeded
model, one silent and one reporting every step, are asserted equal to the last digit; the
mutation that adds `1e-9` to one cell in the observer fails that test and nothing else.

**And the run it was built for is the last thing that will be invisible.** The two relaxation
runs launched before it existed were started from an older binary snapshot and emit nothing.
The longer of the two was restarted from the new one at a cost of 32 minutes, which is the
trade in its plainest form: half an hour of machine time to convert a 2.7-hour all-or-nothing
run into one that banks each phase as it finishes.

## A snapshot of a bin directory is the last build, and after a mutation test the last build is the mutation

Long studies here run from a copy of the CLI in the scratchpad rather than from
`src/Einzel.Cli/bin`, because editing source while a run holds those binaries blocks the
build - which cost several hours over one night. The copy is made with `cp -r
src/Einzel.Cli/bin/Debug/net10.0/*`.

**Three mutations were run to check the new checkpoint's tests had teeth, the source was
restored and verified byte-identical, and the snapshot was taken without rebuilding.** So the
snapshot carried the third mutation - `Wants => false`, which disables progress reporting
entirely - and the four-hour run started from it reported nothing at all. That reads exactly
like the feature not working, and half an hour went into looking for the bug in code that was
correct.

What makes it worth writing down is how *plausible* the failure was. The mutated binary still
announced each finished phase, because that path does not go through `Wants`; it still computed
the right physics, because the observer is provably inert; and `--progress notanumber` was still
refused with the right message, because the argument parsing is in a different assembly. Three
checks that the feature was present all passed.

**Two rules.** A binary snapshot is only as current as the last `dotnet build`, so **build
immediately before copying, and stamp what was copied** - this project already learned the
same thing when a snapshot came out labelled with the commit *before* the one whose code it
contained. And **restoring the source after a mutation test is half the job**: the bin
directory is still mutated until something rebuilds it, so a mutation test should end with a
build, not with a `cp` of the backup.

## A producer written without its reader, for the third time in three days

`einzel report` exists because a night's runs left `results/*.json` that only a parser could
read. Writing it found that the sequenced run path stored no result at all, and that a result
document did not read back into the record that wrote it - both defects of the same shape,
a thing produced with nothing downstream consuming it.

Then the same shape twice more, in code hours old each time.

**The per-phase packet width reached the result document and not the report.** It was added
because a TIMS study's open question is "how wide is the packet when the ramp starts", and the
report's sequenced arm yielded phases, conversions, arrived, mean arrival and arrival spread -
every property of the whole run, and not the one quantity the study was about.

**And the checkpoint above was very nearly written with nothing reading it.** A run that did
not finish leaves a `.progress.json` and no result, and the report would have said only that
"its answer is nowhere" - true, and it discards the phases that did finish, which for a
relaxation study is most of the measurement. It now reads the checkpoint, says how far the run
got, and draws the completed phases **through the same rendering a finished run's go through**,
because a checkpoint and a result describe a phase in the same record.

The rule that generalizes is not "write the reader too" - it is about where to look. Both
report defects were found by *enumerating the writers into `results/`* rather than the readers
of it; the run paths were enumerated first and that is what missed the studies. **After adding
a producer, ask what reads it, and answer by enumerating consumers rather than by recalling
one.**

## A cache's own invalidation check cost more than the work it was avoiding

The ponderomotive well cache exists because computing the well is expensive: on the shipped
TIMS front end it is 8,448 nodes times seven offsets times sixteen cycle samples, about six
seconds. It holds the well across steps and rebuilds only when the well has moved, which it
establishes by probing sixteen nodes and comparing against what it holds.

**On the front end's elution ramp it cost 1.38 seconds per step and the well never moved.**
Split three ways: the density step 0.005 s, the face assembly 0.0035 s, and everything else
in `Refresh`. Sixteen rebuilds over a 69-step ramp at six seconds each, plus a sixteen-node
probe at 60 ms on every step in between. Over the full 8 ms ramp that is around 17,000
rebuilds - **28 hours** - to compute a quantity a DC ramp cannot change, because the well is
the cycle mean square of the *oscillating* field and a DC ramp moves no amplitude, frequency
or phase.

**What moved was the round-off, and the tolerance was below it.** The well is a mean square
taken after removing the mean; the mean is a DC field the ramp walks from 60 V to zero, so
the noise floor of that subtraction is proportional to a quantity that changes by everything
while the well changes by nothing. Measured at about 2.5e-13 of the deepest well per step,
which accumulates past a 1e-12 tolerance in four steps and then does so forever.

**A tolerance below the arithmetic's own floor is not a tight tolerance - it is a tolerance
that has stopped testing anything but the last few bits.** It reads as caution and behaves as
a cache that never caches. Two changes, and the error each admits is stated: the bar is now
1e-9 of the deepest well, which on a 30 V well is 3e-8 V against a thermal `kT/q` of 0.026 V;
and the probe visits one lattice node per call rather than all sixteen, so a well that really
moves is noticed within sixteen steps rather than within one - a fraction of a microsecond of
instrument time, over which a sequence cannot change an amplitude, because it changes them at
phase boundaries and every boundary starts a new cache.

Both are pinned by tests that fail if the bar moves either way: a change of 1e-11 must be
held and one of 1e-5 must be caught, and the probe cost is pinned as an evaluation count so
that neither a wider lattice nor a return to probing all of it can quietly restore the cost.

**And the diagnosis took four wrong turns, all of them mine, all recorded here already.**

**I optimized the half I assumed.** The first fix was to sample the coefficient arrays at a
ramped phase's two ends and interpolate, since they are affine in the ramp fraction. It works
- 149 affine samples, zero re-samples - and it bought nothing, because sampling was 0.008 s
of a 1.38 s step. `docs/lessons.md` already says *measure which half is slow before optimizing
the half you assumed*, from the space-charge work.

**Then I measured on a loaded machine.** The first per-step figures came off a run sharing the
box with a companion run and repeated solution builds, which is where an 86x turned into a
1.5x once the machine was idle. This file already carries that rule about `einzel estimate`.

**Then I built a probe that could not see the cost.** Scaling every phase to a thousandth
scales the step count and not the state the fill leaves behind, so the probe's ramp ran on a
density that had never been delivered - 0.01 s per step against the real 1.38. A probe is only
a probe of what it holds constant.

**And the affine fix was then reverted**, because it changes the stability step in its last
bits and so the step sequence: `ARampingDcRunIsBitIdenticalWithTheCacheAndWithout` failed on
a step count of 26 against 20. It is a real 2.4x on a ramped phase and it is available; what
it costs is that every seeded diffusive result moves slightly, which this project has
consistently refused to pay for speed. Recorded as measured and declined rather than as
unexplored.

## A shared predicate stops a count and a diagnostic disagreeing only until the next state arrives

`ReportOutcome.StoredNothing` exists because a count said "0 runs stored no result" in the
same document as a warning that one had. The two were spelled out separately, a third state
arrived, and only one of them learned about it - so they were collapsed into one named
predicate with a comment saying why.

**A fourth state then arrived and the predicate did not learn.** A run interrupted before it
could write anything has no result, so it satisfied `StoredNothing` and was reported as a run
that "stored a manifest and no result document ... Re-running the model stores one" - advice
for a different problem, and following it would restart a run that was working. Found by
reading a report of a project with a run *still going in it*, not by a test.

So the shared predicate was the right fix and it is not a complete one: it makes the count and
the warning agree with **each other**, and neither of them with reality when the set of states
grows. What would have caught it is the property the report already implies one level up -
that the counts partition the runs - which no test stated. It does now: an interrupted run is
asserted absent from `withoutResult` and present in `interrupted`, and the mutation that
removes the new clause fails it.

**The rule: when a type gains a state, grep for every predicate that enumerates the others.**
A predicate written as a conjunction of "not any of the ones I knew about" is a default case
wearing a specific name, and it silently absorbs whatever arrives next.

## A documented predicate can be exactly right and still be the wrong question to route on

`CompiledModel.ChangesTransportMode` asks whether two **adjacent** phases name different modes,
and needs at least two phases to ask it. That is what its name says, what its comment says, and
what SEQ-1's conversion boundary is: a place where a packet has to be handed from one
description to the other. Nothing about it is wrong.

`einzel run` forked on it. So a sequence of one diffusive phase on a model declaring
`trajectory`, or a sequence every phase of which declares `diffusion` on such a model, is not a
"change" - and the run went down the **trajectory** path with the timeline ignored outright. The
model asked for a density and got a single-ion flight, `TRAJECTORY_INCOMPLETE` and
`collisions.single-ion-interval` on the result, **exit 0**, and nothing anywhere saying a
sequence had been skipped.

**The two questions are different and only one of them is the fork's.** "Does this run cross a
boundary that needs a conversion" is about adjacent pairs. "Can this run be flown in one mode"
is about the **set** of modes it uses, and a diffusive phase anywhere forces the sequenced path
because the trajectory path cannot step a density at all - there is no trade-off, only a
description the other path does not implement.

**The validator had already asked the right one, and said why in a note.** Its own `Modes`
gathers "every transport mode this run uses, the model's and every phase's", precisely so that
"the diffusive mode needs a gas" attaches to the *run* rather than to one declaration in it -
which is itself a defect fixed once before, when a trajectory model with a diffusive phase
skipped every diffusive requirement and validated cleanly. So the answer was in the codebase, in
a member written for the same reason, one assembly away.

**The rule: a predicate whose name describes a property is not thereby the right predicate for
a decision that sounds like it.** Before routing on one, write down the question the fork is
actually asking and check it word for word against what the predicate computes. `Phases.Count >
1` and "compares adjacent pairs" are both in `ChangesTransportMode`'s implementation and neither
is in "can this be flown in one mode".

## The evidence went missing inside the code written to carry it

`--vtu` on a sequenced run wrote no density at all, which is the "capability wired into N-1 of N
paths" shape this project has recorded several times. Fixing it meant writing the export - and
GRD-2 says the caveats travel with the file, because a `.vti` is the artifact most likely to be
opened by somebody who never saw the result envelope it came from. So the export gathers the
run's warnings into the file's provenance header. That is the whole point of the block.

**It gathered two of the three lists that hold them.** `fieldWarnings` - the solve's own
caveats, the ones that say the numbers may not describe the document - were not in it. And the
note saying a sequenced run has no single flight time is constructed *inline*, in the result's
own envelope, about a hundred lines **below** the export, so the export could not have seen it
however carefully it had been written.

The consequence: on the shipped test model, which earns exactly one warning, the volume came out
with an **empty caveat block on a run that had earned one**. Everything compiled, the file was
written, the artifact was listed, and the assertion I had first written for it was about a
warning code the model does not produce - so my own test's failure was the only thing that
surfaced it, and my first two attempts at that test were both guesses at which code was missing
rather than a look at the file.

**Two things fixed it and only one is the list.** The warnings are now built into one local
above both readers, so a caveat added later reaches the file and the document by being added
once. And the severity is on each line rather than only the code, because a reader deciding
whether to trust a volume needs to know which of these is a note about how the run was framed
and which is the engine saying the numbers are suspect.

**The rule, which is the fourth statement of one already here:** when a computation produces
evidence about its own quality, discarding it must not be the shortest spelling - and *building*
the carrier is not exemption from that. The place a caveat gets dropped is the place that
enumerates caveats by hand. Ask which lists exist, not which ones come to mind, and put the
answer somewhere both readers take it from.

## Three runs, one heading, two comparisons

The TIMS front end completes and elutes nothing. I bounded that with three runs and wrote the
result up as a property of how the sweep is spelled: written as a `ramp` the packet freezes,
written as a single `set` it travels and 81,049 of 85,170 arrive, written as sixteen `set`
stages it elutes. A clean story with a named mechanism to go looking for.

**The runs did not share a source.** The eluting single-`set` run seeded its packet AT the
balance point; the frozen ramp delivered its packet from 40 mm up the entrance funnel. So the
comparison moved the packet as well as the spelling, and every conclusion drawn from it was
about an interaction I had not separated. Re-run parked, **a `ramp` elutes 85,170 ions** - the
thing the write-up said a ramp does not do.

**What made it look like one comparison was that all three ran from "the same document".** They
did: one document, one parameter overridden per variant. `sourceX` was -40 mm in two of them and
21.09 mm in the third, which is one line of a diff and the whole of the experiment. A variant
set built by overriding a shared document reads as controlled *because* it is mostly shared, and
the differing line is exactly the one nobody re-reads.

What recovered it was tabulating every model in the study directory by the parameters that
distinguish them - source, phase structure, flight-time limit - rather than by the name I had
given each. The names encoded my intent; the table encoded what would run.

**And the probe that cleared the ramp had the same shape of flaw.** It showed the potential at
the parking point falling 11.100 V to 0.720 V through a ramp phase, which is the ramp working -
on a model scaled a thousandfold in time, so its ramp began at 10.3 *microseconds* where the
failing run's begins at 10.3 milliseconds. The absolute instant is the one quantity the two runs
differ in and the probe moved it. That is this project's own rule, already written down, met a
second time: a probe only probes what it holds constant.

The route that did cover the real instant cost nothing, because it was already being recorded:
the per-phase **assembly count**, added a day earlier to catch a held phase re-assembling for a
ramp that was not there. The ramp phase re-assembles 68,216 times in the failing run and 67,828
in the eluting one - so both re-sample a moving field every step, and "the field is frozen" is
refuted without building anything. **A counter already in the output beats a probe you have to
write**, and it beats it twice over because it was taken during the run in question rather than
during a stand-in for it.

**The rules.** Before drawing a conclusion from a set of variants, list them by the parameters
that differ rather than by the names you gave them, and say out loud which single quantity each
pair isolates - a set that shares a document is not thereby controlled. And when a cheap probe
and an expensive run disagree, check what the probe scaled before believing it.

## A study run out of `bin/Release` runs whatever was last built there

Studies here are run from the Release tree, because Release is 3.27x faster and a diffusive
sequence is measured in minutes either way. Development builds and tests Debug. Nothing links
the two, so `bin/Release` holds whatever was last built into it - and after two days of work on
the diffusive path, that was **two days old**.

Both controls in a two-by-two went out on it. One of them was fine by luck: its phases are all
holds, so the operator assembles once and the well rebuilds once, and the well-cache defect
fixed in between could not bite. The other has a ramp, so on the stale binary it was rebuilding
a six-second well every fourth step - the 28-hour behaviour the fix removed - and it sat there
producing nothing while I read code.

**The tell was a missing key, not a wrong number.** The phase records came back without
`spreadMm`, which had been added to them the day before. A number that is absent is a much
better signal than a number that is merely different, and it is only available because this
surface writes an absent measurement as absent rather than as zero: had the width defaulted to
0.0, the output would have been complete, plausible, and from the wrong engine.

The worse outcome was the one narrowly avoided: the two cells of the comparison would have been
measured on **different binaries**, days apart in behaviour, and the difference attributed to
the physics being varied. That is the same error as timing a run against an estimate while the
test suite is running, one level up - the confound is not in the machine's load but in which
program was executed.

**The rule: build the tree you are about to run, immediately before running it, and record what
you built.** A manifest already carries the engine version for exactly this reason (`PRJ-3`),
which means the check costs one line and I did not make it. And when a run produces less than
you expect - a missing field, a shorter table - suspect the binary before the model.

## Re-running a stem destroys the provenance of the run you are trying to explain

`results/` is keyed by the model's stem: `delivered.json` writes `delivered.manifest.json` and
`delivered.result.json`, and the next run of that document overwrites both. PRJ-4 licenses
that deliberately - the durable record is the model and its history, and results are
regenerable rather than precious.

**An anomaly is exactly the case where that licence does not hold.** A run of the TIMS front
end collected 7.74e-245 ions and did not move. Explaining it took five further runs of the
same document, and each one overwrote the manifest of the one before - so by the time the
question had narrowed to "was the document itself the same at the time?", the model hash that
would have answered it had been written over four times. `PRJ-3` had recorded exactly the
right thing and the file no longer held it.

The checkpoint would have carried it too, and that was removed on schedule: a finished run
deletes its own `.progress.json`, which is the invariant that makes finding one mean the run
did not finish. Both mechanisms worked as designed and both destroyed the same evidence.

**The rule: when a result is anomalous and you intend to investigate it, copy its manifest and
result aside before re-running the stem.** It costs two file copies. The alternative is what
happened here - a finding that is now recorded as unexplained partly because the evidence
that would have settled it was overwritten by the investigation.

## The leading hypothesis had one commit's worth of support and was wrong

The frozen run was on engine `ab19163`; every eluting run was on `c135bc1` or later. Between
those two commits exactly one file under `src/` changes any number:
`PonderomotiveWellCache.cs`. The other changes were an elution *reporting* floor, tests and
documentation. So the inference was short and looked airtight - the cache's tolerance moved
from 1e-12 to 1e-9 and its probe went from sixteen lattice nodes to one, and that commit's
bit-identical claim had been measured on the **analyser**, never on the front end, whose
composite carries a solved funnel RF. A plausible mechanism, a clean bisection, an admitted
gap in the evidence for the alternative.

**The control killed it in one run.** Reverting that one file on the current build and running
the same document gives **10,794 well rebuilds against 1** and the same answer to **13
significant figures** - 99,833.4920418748 ions against 99,833.49204187385, mean
15,505.159357357079 against 15,505.159357357172. The cache is excluded, and the side effect is
worth as much as the result: its bit-identical claim is now measured on the model that
motivated it.

Two things worth keeping. **A bisection over commits is not a bisection over causes** - "one
file changed" bounds where a cause could live and says nothing about whether it does, and the
temptation is strongest when the diff is smallest. And **the control was cheap and I nearly
skipped it**, because I had already written the hypothesis into four documents; reverting one
file and re-running cost one build and one run, against a wrong entry in the living
specification. Write the hypothesis down as a hypothesis, then run the thing that can refute
it before it is written down as a finding.

## Amendment numbers are a shared namespace, and two branches both took 42

PR #37 branched from a commit where SPEC.md's highest amendment was 41, and numbered its four
additions 42, 43, 44, 45. That was correct when written. #36 then merged first and took all
four numbers for entirely different content - 42 for the extension surface, 43 for the run
report, 44 for progress and checkpoints, 45 for the elution floor. Git merged the file without
a conflict in the amendment section, because the two branches added text in different places,
so **the merged specification had four pairs of duplicate headings and six citations pointing
at the wrong one of each pair.**

Nothing in the build catches this. SPEC.md is prose, and the living specification is exactly
the document that must not quietly disagree with itself.

**The repair is not a regex.** `Amendment 43` appears in main's own register rows and in two
plan items, meaning main's 43; it appears in #37's rows meaning #37's. A blanket substitution
would have renumbered both and turned the cross-references into nonsense while leaving every
heading looking plausible. What worked was renumbering by exact string - four headings and six
citations, each asserted to occur exactly once - and then checking that `### <n>` has no
duplicates.

**The rule: a document with a hand-maintained number sequence is a merge hazard that produces
no conflict.** When two branches are open on one repository, the second to merge has to
renumber, and the check is a uniqueness assertion on the headings rather than a reading of the
diff. Worth doing before the build, because the build will not tell you.

## The guard I flagged as a compatibility break caught four of my own fixtures

#37 added a refusal: a diffusive detector must coincide with a grid face. I reviewed it as a
compatibility break - documents that validated and ran would now fail, with no schema bump -
and it is one. Then the merged suite failed seven tests, and four of them were my own fixtures
declaring a detector twenty millimetres past the end of their density grid.

They had always run. The collecting face was chosen from the detector's **normal**, and its
**position was ignored** - so the documents named a plane the collector did not use, and the
runs were correct about a geometry nobody had described. One of them was the fixture behind a
published front-end resolving power.

So the review finding and the fix are both right, and the order in which I learned them is the
part to keep: **I classified the refusal as a cost before discovering that I was one of the
people it was catching.** A guard that rejects existing documents is a compatibility break
*and* evidence about those documents, and the second reading is the one that says whether the
break is worth taking. Reading the diff gave me the first; running the suite gave me the
second.

## A version bump that invalidates everything and changes nothing is worth measuring

`SolverBehaviourVersion` went 1 to 2, correctly: #37 changed the RF spectrum validation, the
cache's keying and the period selection, any of which could move a number. PRJ-3 makes that
bump invalidate every stored result in every project, so `verify` now reports drift
everywhere.

Which leaves a question nobody had to ask: **did the published numbers actually move?** For the
scan-rate series they did not. The 8 ms anchor re-measured over the same tracked region gives
R = 21.572 against the recorded 21.57, with sigma 94.757 against 94.76 - identical to the
precision it was quoted at.

**Measured as a pair, because a single re-run would have confounded two changes.** The same PR
requires the detector to sit on a grid face, and every model in that study had it three
millimetres off - so moving the detector onto the existing face isolates the engine, and
aligning the grid to the detector isolates the geometry (+1.5 per cent). Without the pair the
honest report would have been "the series moved by 1.5 per cent and I cannot say why".

**The rule: when a behaviour version is bumped, re-measure one anchor per published series
before assuming either way.** The bump is a statement that numbers *may* change, and the
cheapest thing that turns it into a statement about whether they *did* is one short run per
series - which also tells you whether the register needs restating at all.

## The clock said it was wrong before anything else did

An elution animation of the shipped TIMS analyser came back in **25 seconds**. The run it was
filming takes **thirteen minutes**. Nothing failed, the frames were well-formed, the provenance
block said `density recorded at 126 instants over 7674 steps`, and the contour levels spanned
seven decades - every sign of a working figure except the one that mattered.

**It was drawing a real density of an instrument nobody had described.** `render section` and
`render animation` both decided what to draw by asking the model's *declared* transport mode and
then calling `DiffusionRun.Execute` - the wholly diffusive path, which reads the field once and
never looks at a sequence. So the exit potential never ramped, the packet sat parked where it
started for all 126 frames, and the command exited 0.

That is the **ninth** sighting of one shape here and the second on this exact question: a
capability wired into N-1 of N paths, reached through a proxy (`TransportMode`) that stopped
being equivalent to the question (`which modes does this run use`) the moment a sequence could
name one. `einzel run`'s own fork was corrected the same way, and the correction did not travel.

**What found it was a ratio, not a test.** Frame count, byte count and path count were all
plausible; 7,674 steps for an 11.3 ms window is 1.47 us a step where the shipped model's
stability limit is ~56 ns. The rule: **when a computation finishes far faster than the work it
claims to have done, price it before believing it** - a figure whose cost is wrong is usually a
figure of something else.

## Two ways a requested instant lands in the wrong phase, and only one is a clamp

Threading snapshot instants through a sequenced run put both of these in, and the tests found
them rather than the reading did.

**An instant at the very end of a run is dropped.** Phase ends are accumulated sums of
durations, so the declared end of an 80 us run is 80 us only up to rounding, and the solver
records at the first step *at or after* what was asked for. An animation forces its final frame
onto exactly the declared end - so the one frame most likely to be looked at was the one
reliably missing. The fix is to give the last phase no upper bound at all and let the *run*
decide whether it reached an instant, because arithmetic on declared durations does not know.

**An instant on a phase boundary dates the packet a whole phase forward.** Shifted onto the next
phase's clock it lands a few zeptoseconds *after* zero - positive, so `Math.Max(0.0, ...)` does
nothing, which is what I wrote first and it changed nothing. The solver serves an instant at or
before its launch from the density it was handed and anything past that only at the first step
at or after; on a leg of one or two steps that one bit meant a frame asked for at the end of the
hold was drawn *after* the push. Snapping a near-zero offset to zero is the fix, and the two
mistakes look identical until you print the offset.

**The generalising rule: a clamp guards a sign, and floating-point boundary error has no sign
you can rely on.** Where two clocks meet, the quantity to test is the distance from the
boundary against a tolerance in that clock's own units - not whether it came out negative.

## A figure that was cheap because it was wrong becomes a run that is silent

Fixing the routing did what it should: the animation now runs the model's own sequence and takes
**as long as the run does**, because it *is* the run. That is correct and it creates the exact
gap `einzel run --progress` was built to close a week earlier - twenty minutes with nothing on
stderr, and a 128 ms ramp would be three and three-quarter hours of the same.

The render verbs do not pass an `IRunProgress`, and until this change they never ran anything
long enough to need one. **Worth stating as the shape rather than the instance: when a path
starts doing real work it did not do before, it inherits every requirement that attaches to real
work** - progress, checkpointing, the cost gate - and none of them arrive by being in the
assembly next door.

## Stopping a phase early is a wrong answer, not a short one

A viewport redraw runs the transport, which was cheap until the viewport learned to follow a
model's sequence - a mobility analyzer's timeline is twenty minutes of stepping, and a window
that stops answering for twenty minutes is one somebody force quits. So the walk had to be able
to stop.

**The obvious spelling is `untilSeconds`, and it is the one that produces a wrong number.** A
phase's `ramp` interpolates each parameter from its start to its end value **over the phase's own
declared duration**. Truncating the phase - `phase with { DurationSeconds = shorter }` - therefore
makes every parameter it ramps arrive at its end value early: an elution ramp that should be a
tenth of the way down would be at zero. That model validates, solves, runs and reports a packet
in the wrong place, with nothing anywhere saying the ramp had been compressed.

**A phase boundary needs no truncation**, so `phaseLimit` counts phases instead and nothing about
any phase changes when the walk stops after one. It is also the better default rather than merely
the safe one: a timed instrument prepares its packet in its first phase, and a packet parked
against the gas at its balance point is the state worth opening a window on.

**The rule: before adding a way to stop something early, ask what inside it is parameterised on
its full length.** A ramp, an average over a window, a normalization, a fraction-of-the-way-through
- each turns a shortened run into a differently-wrong one rather than a partial one.

## A capability that arrives at a surface brings that surface's requirements with it

Correcting the render verbs to follow a model's sequence did what it should, and made a render
cost what a run costs - because it *is* the run. The 8 ms elution film became twenty minutes of
silence, and a 128 ms ramp would be three and three-quarter hours of it. The render verbs passed
no `IRunProgress` and, until that change, had never run anything long enough to need one.

The same thing happened one surface over. The viewport's density had always come from a wholly
diffusive run of a short model; pointed at a sequenced one it would have stepped a whole timeline
on the UI thread.

**Stated as the shape: when a path starts doing real work it did not do before, it inherits every
requirement that attaches to real work** - progress, a cost gate, not blocking the thing that
called it - and none of them arrive by being implemented in the assembly next door. The question
to ask after widening what something can reach is not "does it still work" but "what is it now,
and what does that kind of thing owe".

**Two smaller ones came out of the same seam.** A checkpoint must not be written by something that
stores no result: the document says in its own words that the answer will appear beside it and
that finding the file means the run did not finish, and `einzel report` reads exactly those files -
so a render leaving one would have a finished figure reported as an interrupted run. And the
viewport's `Draw` had been refreshing as well as drawing, so ticking a layer checkbox re-flew the
ions; once a refresh could be minutes, **a control that costs nothing and a control that costs
everything were the same method.**

## An exponent is a property of whatever is limiting, not of the law

Sixteen cheap runs measured the four levers in the TIMS resolving-power law, one at a time
against a shared anchor. Every one of the first three answers was wrong, and each was wrong in
the same way.

**The gas speed came back as an exponent of 1.98 against the law's 1.00**, at a worst residual
of 11.9 per cent - which is a fit saying it has been handed the wrong functional form. An
affine fit gave `R = 0.3144 v_g - 6.960` at under 6 per cent, and its sharpest prediction held:
extrapolating to a zero at 22 m/s said the analyser should stop eluting there, and at 25 m/s
the elution voltage measured **0.417 V of a 60 V ramp**. A quantity `a x - b` has a log-log
slope of `a x/(a x - b)`, above one and drifting with x - so **an exponent fitted to an affine
quantity is a number about where the sweep sat.**

**The charge came back as 0.084 against 0.500**, because the width it was supposed to move was
not free to move: sigma fell 8.6 per cent for a threefold charge where `sigma_z^2 = (kT/q)/
|dE/dx|` demands 42. Fitting five runs that differ in temperature and charge onto one line in
`T/q` - the closed form's own claim, confirmed to 4 per cent - isolated a floor of 110.8 us
that neither lever reached, and made the thermal levers look capped at 1.205x between them.

**And then the floor turned out to be mostly the mesh.** Refining 256 to 512 to 1024 intervals
moved the arrival width 133.52 to 114.55 to 107.31 us at an observed order of 1.39. The
elution *voltage* moved 0.14 per cent across the same refinement - so the numerical error was
entirely in the width, which is precisely the quantity every one of those exponents was
fitted against.

**What each mistake shares.** The gas-speed exponent was really the release lag's. The charge
exponent was really the mesh's. The ramp-rate exponent came back as 0.389 against 0.250 because
the short-ramp end of the sweep was mesh-floored and the long-ramp end, with a packet ten times
wider, was not - **a bias that shrinks along a sweep makes the sweep look steeper than it is.**
In each case a real dependence existed and something else was limiting, so what the fit
returned was a property of the limit.

**The rule.** Before fitting an exponent, establish that the quantity you are fitting is the
one that moves - by checking that the response is not dominated by a floor, an offset or a
discretisation. Three cheap diagnostics do most of it: fit the affine form beside the power
law and compare residuals; refine the mesh and see whether the answer moves; and look at the
raw quantity the figure of merit is built from, because sigma being flat across a 2.4-fold
sweep is visible in a table and invisible in an exponent.

**And the corollary that cost the most.** Every study model in this project declares 256
intervals, so every resolving power published from one is low by about that 30 per cent. The
shipped template declares 512. Nothing compared them, and nothing warned - which is why
`diffusion.packet-resolution` now reports cells per packet width on every diffusive run, and
refuses below four.

## A clock that is right for a line of text is wrong for a picture

The density solver reports its own leg's clock: a phase that runs from 40 to 80 microseconds
of the instrument's timeline reports 0 to 40. That is not a bug and it is not an oversight — a
leg genuinely does not know it is one of eight, and the consumer that existed when the seam was
built prints a line of text with the phase named on it:

```
  phase 2/3 push        12.0 of 40.0       us     4,182 steps
```

which is exactly what a reader wants. The phase and the leg-local instant are on the same line,
so there is no ambiguity to resolve.

**Then a second consumer arrived and the same number was wrong.** A viewport frame is a
picture, and the instant is stamped on it as provenance (GRD-12) because three shells of a
packet say nothing without one. Carrying the leg's clock there sends the time back to zero at
every phase boundary — the packet visibly advances, the number visibly restarts — and the stamp
names an instant the packet passed through long before.

**Two things generalise.**

The first is that a quantity is not right or wrong on its own; it is right or wrong *for what
is around it*. The leg clock is correct in a line that names the phase and incorrect in a
picture that cannot. So when a second consumer is added to an existing seam, the question is
not whether the data is correct but whether the new consumer supplies the same context the
first one did. Here it could not, and the fix is in the *adapter* rather than in the seam.

The second is about where the offset comes from. The obvious implementation accumulates the
phases' **declared** durations, which is available at `Entering` and reads naturally. It is
wrong: a phase can end early — everything collected, a stop condition met — and from that point
the clock runs ahead of the packet for the whole rest of the sequence, by an amount nothing on
the picture would explain. The authoritative number is on `PhaseOutcome.EndsAtSeconds`, which
is what the phase *did*. **Prefer the completed fact to the declared intention whenever both
are available**, and note that the declared one is usually the one in scope earlier, which is
why it is the one that gets used.

## The end of a run is empty whenever the ions arrived

A watch that applies its final bundle unconditionally spends minutes drawing a packet and then,
at the last instant, replaces it with an empty box. Every frame was correct; the last one is
correct too — the ions really did arrive and there really is nothing left in the box. The
result is a feature that works perfectly and shows nothing.

This project had already solved it once, in the other direction: the finished viewport draws
**the middle of the instants that still hold a packet**, with the end state as the fallback
rather than the default, and the comment there records that this is the way round it was wrong
first. A live watch has no list of instants to pick from, so the equivalent is to keep the last
frame that held one — and to say on the status line that this is what is drawn, because a
drawing whose instant is not the run's end must not silently claim to be.

**The rule.** Where a run's final state is legitimately empty, a surface that shows "the
result" has to decide between the last state and the last *informative* state, and it has to
say which it chose. Reaching for the final state is the default that produces an empty screen
at the end of every successful run.

## A return value that answers the wrong question passes every test but one

`ViewportViewModel.RefreshAsync` returns `HasBundle` — whether there are trajectories to draw.
The watch was written beside it and returned the same thing, which compiled, matched the
sibling method, and read as consistent.

A watch only ever runs on a diffusive model, which has no trajectories **by construction**. So
it returned false on every successful watch there is, and the first test written against it
failed for a reason that looked like a defect in the transport rather than in the signature.

The shape is the one this project has recorded before under a different name: a *proxy* that
was equivalent to the real question in the context it was written for. "Is there a bundle" and
"is there something drawn" were the same question while the viewport drew only trajectories.
They stopped being the same question when it learned to draw a density, and the method that
inherited the proxy was the one added afterward.

**When copying a signature from a neighbouring method, ask what its return value means rather
than what it is called** — and in particular, whether the thing it measures can be true at all
for the new caller.


## An exact test on a computed quantity, and an eleven percent field asymmetry

The `revolve` primitive decides what each edge of a profile sweeps into. An edge whose radius
varies with the axial coordinate makes a cone, one parallel to the axis a cylinder, one
**perpendicular** an annular disc — and only the disc is handled separately, because for it
the quadratic that finds a link's crossing degenerates. The first version asked
`if (dh == 0.0)`.

**An outline is written as expressions, so a face meant to be flat comes out flat only to
rounding.** The C-trap's inner rod traces a hyperbola from `-rodHalfWidth` to `+rodHalfWidth`
and then closes with a corner at the same half-width. The run's last vertex arrives at
`3.0000000000000009` mm; the corner is written `3.0000000000000001`. The edge between them
slopes by **8.7e-19 m** over three millimeters of radius. Tested against zero that is not a
disc but a cone of slope 2e15, whose quadratic is so ill-conditioned that its root is lost
or displaced by a quarter of the link.

**What made it expensive is that the other face of the same rod was exactly flat**, because
`-rodHalfWidth` and the run's first vertex round the same way. So one side of a
mirror-symmetric electrode got its cut cells and the other did not. The **conductor mask was
symmetric either way** — a node is inside or outside, and the distance function was never
wrong — so nothing about the geometry looked amiss, and the solved field came out **eleven
percent asymmetric** across a plane the geometry is symmetric about.

The fix is a relative tolerance built from the coordinates in play,
`|dh| <= 1e-12 * (|h0| + |h1| + |r1 - r0|)`. Cut-fraction asymmetry went from 1.0 to 1.9e-13.

**Three things generalise.**

- **Never test a computed geometric quantity against zero.** `dh` is a difference of two
  expression results, and the question being asked — "is this face flat?" — is a question
  about the author's intent, which survives rounding. The exact test asks a different
  question that happens to agree most of the time.
- **A symmetric mask is not a symmetric discretization.** Cut links carry sub-cell surface
  positions, so two geometries with identical masks can solve to different fields. Anything
  that checks a geometry by asking which nodes are inside cannot see this class of defect.
- **The discriminating test is a mirror pair, not a value.** `AFaceFlatOnlyToRoundingIsStillCutWhereItIs`
  fires links at both faces of a deliberately mirror-symmetric rod and compares the two
  entry fractions; it needs no reference value at all, which is what makes it immune to the
  geometry being rebuilt later.

## Two templates were drawn as approximations nobody had looked at

The C-trap's rods were **chains of thirteen overlapping spheres**, scalloped by 13.6 percent
of their own radius; the Paul trap's electrodes were **three flat annuli** where a quadrupole
trap has hyperboloids. Both were deliberate at the time — a `cylinder` is axis-aligned and a
bent rod is not; the cross-section vocabulary had no curve — and both survived for the same
reason: **an approximation adopted because the format could not express the real thing does
not announce itself when the format gains the ability.**

What the two cost, measured:

| | approximation | real geometry |
| --- | --- | --- |
| Paul trap, effective r0 against 4.0000 mm declared | 3.8195 | **3.9983** |
| Paul trap, boundary against a tabulated q = 0.90804 | 9 percent low | **0.07 percent** |
| Paul trap, curvature ratio against −2 at 0.4 mm | −1.9867 | **−2.0006** |
| C-trap, ejected waist | 1.73 and 1.92 bend radii | **1.011 and 1.011** |
| C-trap, convergence at 20 mm | 20.8x | **45.9x** |

**The Paul trap's 9.4 percent was not an unknown.** It was measured three independent ways,
explained correctly, and carried as a stated correction on every number the template
published — which is the right thing to do with a known approximation and is also exactly
what let it sit there for months. A correction that is carried faithfully stops being a
problem to fix.

**Two published findings turned out to be artifacts of the approximations**, and both had
plausible mechanisms attached:

- The C-trap's waist at 1.73 and 1.92 bend radii was explained as an **aperture lens at the
  slot** — accelerated up to it, field-free after, which is a real mechanism and is what an
  aperture does. The thin-lens fit it implied mispredicted the longer bend by 17 percent, and
  that failure was recorded rather than buried. With swept rods the waist is at the center of
  curvature, where velocities aimed along radii meet, and a fourfold change of slot opening
  moves it 1.048x — so the slot is a hole. **The failed prediction was the signal, and it was
  read as a limitation of the model rather than as evidence against it.**
- The Paul trap's **octupole resonance band** at 605–614 V was identified from the measured
  secular frequencies as an order-four condition met to 0.055 percent, with order four
  predicted in advance from the trap's symmetry. That identification was right — and an
  octupole is what a flat annulus buys, so giving the trap hyperboloids removed the band.
  Forty amplitudes below the edge, none loses its ion, and the frequency condition misses 2
  by 0.22 where it was met to 0.0011. **A correct explanation of a phenomenon caused by an
  approximation is still a correct explanation, and the phenomenon still goes away.**

**Three tests were carrying the old geometry in their source**, and each failed in a
different way when the electrodes changed:

- A **hardcoded constant**: `const double EffectiveMm = 3.8195` in the secular-frequency
  test, over-predicting every line by ten percent. Now measured from the field.
- A **quoted sentence**: the boundary test printed "hold-converged ejection edges: 665-670 V
  at 0.3 mm" as literal output text beside an assertion on a range around them. Now bisected.
- A **vacuous truth**: `TheBeadsOverlapAlongEachRod` grouped electrodes by name stem and
  compared consecutive members of each group. With the rods swept, each group has one member,
  the inner loop never runs, and the worst spacing stays at the zero it was initialized to.
  **It passed.** Replaced by a test that the gap from the trap axis to metal is constant round
  the arc — which is the property the beads failed.

**The rule.** When a template's geometry is rebuilt, the tests that need rewriting are not
only the ones that fail. Grep the test sources for numbers describing the old geometry, and
for any loop whose body may now execute zero times.

## A control that no longer discriminates is a control that was measuring the defect

The Paul trap test `BetaDoesNotDependOnHowFarOffCenterTheIonStarted` asserted two things: that
beta is amplitude-independent near the center, which is the premise of the calibration, and
that it is measurably **not** at high q, which is the control saying the spectrum is sharp
enough to see an anharmonicity at all.

On hyperboloids the control stopped firing — a 0.05 mm and a 0.20 mm launch now agree to
below a part in a thousand at every amplitude, where the flat annuli shifted beta by 1.7e-2 at
600 V. The obvious reading is that the measurement has become insensitive. It has not: the
control was calibrated against a deliberate imperfection, and the imperfection is gone.

Moving it out to a **1.6 mm** launch, where the truncation of the hyperboloids does bite,
restores it — 6.6e-3 at 300 V and 9.4e-1 at 600 V. The same happened to the C-trap's
drive-phase sweep, where the phase spread was an order below the drive shift on beaded rods
and is the same size on swept ones, reversing the conclusion the old control supported.

**The rule.** A control asserts that a measurement *can* detect something. When the thing it
was calibrated against changes size, the control has to be recalibrated or it silently becomes
an assertion about nothing — and unlike a stale expected value, it does not fail when it stops
being useful. It fails when the thing it measures is *fixed*.


## A figure of merit's guards live in the figure of merit

Measuring a resolving power across a ladder of TIMS runs, I read `meanArrivalUs` and
`arrivalSpreadUs` out of each result document and did the arithmetic myself - the figure is
`V(t_peak)` over how far the ramp moves in a FWHM, four lines of Python, and it saved
re-flying every configuration through `einzel test`.

One run came back at **R = 101.7, the largest number in the study**, from a front end whose
field cannot hold a 140 m/s gas. Its mean arrival was **943 us against a ramp that starts at
10,300** - every ion gone before the scan began, during the fill - and the implied release
potential was **130.175 V on a 60 V supply**.

**The engine refuses exactly this, and has all along.** `MobilityResolvingPower` computes
`inside = peak >= rampStart && peak <= rampEnd`, returns **null** when the peak is outside,
emits `mobility.peak-outside-ramp` naming both instants and what to do about it, and
`MobilityResolvingPowerTests` covers the warning present and absent. `einzel run` does not
evaluate figures of merit, so it had no occasion to refuse; I took the raw fields and
divided.

This project already records the same shape for a different figure: *"a probe read
`flightTimeSeconds` from `--json` and printed 0.000 us... GRD-1 prevents the engine emitting
a bare number; nothing stops a consumer reintroducing one."* That was written about a reader
somebody else wrote. This one I wrote, after reading that sentence, and then read its output
as a finding about the platform.

**The rule.** A figure of merit is its refusals at least as much as its arithmetic - the
arithmetic is four lines and the refusals are two hundred. Recomputing it from the fields a
result document happens to expose gets the four lines and none of the two hundred. If the
cost of calling the real figure is a re-flight, that is the price of the guards, and the
alternative is not a cheaper measurement but an unguarded one.

## A comparison of two models on two meshes measures the meshes

The same study set the shipped TIMS front end against the analyzer alone, matched on seed,
on hold, on gas, on field and on parking point, so that the two differed in geometry alone.
The front end came out **18 percent worse**, against a register entry recording the cost as
4 percent, and I wrote that up as a correction to a published number.

The two models have different domains - 115.6 mm against 68.6 mm - so at the same declared
interval count their cells are **0.452 and 0.268 mm**. Refining each until the cells matched
(0.113 and 0.134 mm) took the difference to **-1.9 percent**, which is nothing. The whole
of it was the coarser mesh inflating the front end's arrival peak.

**What makes this worth writing down is that the same study had already established the
mechanism.** Two sections earlier it measures R rising 1.15 to 1.19x under one refinement
while the elution potential moves 0.035 percent, and concludes - reproducing the register -
that a coarse mesh distorts widths and resolving powers and leaves positions alone. Then it
compared two resolving powers across a 1.68x difference in cell size.

**The rule.** Matching interval counts is not matching meshes; matching cell sizes is. And
where a study has just finished establishing which of its figures a discretization moves, the
next comparison of that figure is the one to check first, not last.

## A second moment is not a width, and refinement is how you tell

The delivered TIMS packet has carried an oddity for some time: it enters the elution ramp at
**1.3082 mm** where the analyzer's parked packet is at 0.6819, and the closed form
`sigma_z^2 = (kT/q)/|dE/dx|` - which carries nothing about the packet's history - says both
should sit at 0.71. The register narrowed the cause to two candidates, *"either a tail
dominating a second moment (sharp with a shoulder, not broad) or the linearisation failing
over a +-2 mm packet"*, and eliminated the second by measuring a 1.67 mm parked packet
relaxing at the predicted rate.

Refining the mesh eliminates the first from the other side. Across a fourfold refinement:

| | spatial second moment | arrival peak |
| --- | --- | --- |
| analyzer | 0.6819 -> **0.6819 mm** | 128.2 -> 111.4 us |
| front end | 1.3082 -> **3.2665 mm** | 156.3 -> 109.5 us |

**Two moments of one packet moving in opposite directions.** A genuinely broad packet
narrows under refinement, because the coarse mesh was inflating it. A tail-dominated second
moment **grows**, because the finer grid resolves more of the tail - here, density strung
back up the funnel that the coarse grid could not hold - while the core that sets the arrival
width sharpens.

**The rule.** Where a distribution may be skew, a second moment and a width are different
quantities and refinement separates them: refine, and see which way each moves. And
`spreadMm` on a sequenced phase is a second moment. For a packet that was delivered rather
than seeded it is not a width, and reading it as one - which is what I did in concluding the
front end held a 1.9x wider packet - is reading the tail.

## A guard that exists in one dimension is not a guard

`ElectrodeOverlap` has refused two conductors occupying one place at two potentials since
a hexapole was built with a quadrupole's rod ratio, put its rods through one another, and
solved contentedly in eight cycles. It takes a `CompiledElectrode` and is called from the
`solve2d` path. Every volume geometry - `box`, `sphere`, `cylinder`, `prism`, `revolve` -
went unchecked from the day the volume solver landed, and the gap was *written down* in
`docs/extending.md` as a stated limitation with both candidate designs named and the note
"neither is built".

Writing the volume half refused a shipped template on its first run. `astral-3d` extruded
every drift stripe outward by its own declared 2 mm thickness, through the inner face of
the grounded board it is printed on and 1.285 mm into the metal behind it.

**A known gap, written down, is still a gap.** It survived because it was recorded honestly
rather than despite it: the note describes the missing check accurately and reads as a
decision, so every later reader - me included, repeatedly - saw a considered limitation
rather than a defect waiting. This is the same shape as the Paul trap's 9.4 % shortfall,
which was measured three independent ways, explained correctly, and carried as a stated
correction on every published number until the geometry that caused it was replaced. **A
correction that is carried faithfully stops being a problem to fix.** The counter-practice
is to give a recorded gap an owner and a trigger, not only a description.

## A check that must not falsely refuse should search for a proof, not for the truth

The obvious volume overlap check is the minimum of `f = max(dA, dB)` over the intersection
of the two bounding boxes: negative means shared metal. Both signed distances are
1-Lipschitz, so branch and bound on it is straightforward and the first prototype was
twenty lines.

It is also the wrong question. Two conductors sharing a face - which is how *every*
segmented electrode chain in this library is written - have `f` equal to zero over a whole
surface, so proving `min f >= 0` means subdividing that surface to the tolerance. The
prototype burned its 200,000-box budget on each of sixty such pairs in the Astral and came
back **inconclusive on the commonest legitimate configuration there is**, which is a guard
that cannot decide the case it will meet most often.

The doctrine already in the plane check settles it: *"a test that guessed would sometimes
refuse a legitimate geometry, which is worse than one that sometimes misses"*. So the check
never needs to prove disjointness. **A point strictly inside both conductors is a proof of
the violation; nothing proves the absence.** Reframed as a witness search the same
machinery terminates on a budget, refuses only what it has proved, and passes tangency by
construction rather than by tolerance. The Astral's buried stripe is found by the second
probe.

**The general form: before building a decision procedure, ask which of the two answers you
are allowed to be wrong about.** If one direction is a proof and the other is only an
absence of evidence, search for the proof and budget the search - the expensive half is
usually the half you were never permitted to act on anyway.

## Two conductors can interpenetrate and change nothing, and the mesh is why

The Astral fix had to be shown not to move a published number, so the two declarations were
compared as *masks* rather than as fields: every node's fixedness and value, every cut
link's fraction. Identical - at cell sizes of 4, 2, 1, 0.5 and 0.25 mm, a sixteenfold
refinement.

The reason is two rules working together, neither of which is about the overlap. A cut link
records the **nearest** surface along its arm, so the stripe's front face at 20 mm wins over
everything behind it; and a node inside both conductors goes to whichever was written last,
which is the board. Between them, what is behind the first surface is never sampled.

**So the interpenetration was latent rather than active, and that is not what one would
guess.** The natural worry about a buried conductor is that refining the mesh eventually
resolves it and the geometry changes under a convergence study - which would make every
refinement on this template suspect. It does not, and only the ladder says so; a
single-mesh comparison at the shipped 4 mm cell shows nothing at all, because the 0.715 mm
stripe holds **no node** there and exists purely as a cut link.

The measurement's own teeth are the other half: extruding the stripe by 8 mm rather than 2,
far enough to leave the back of the board, fails all five rungs. Without running that, "no
differences at five meshes" is equally consistent with a comparison that compares nothing.

## "Do these two agree" has as many answers as the instrument has states

`ElectrodeOverlap` skips a pair of overlapping conductors when they hold the same thing,
because overlapping is not the fault - disagreeing is. It read that off the electrodes as
declared, which was the right question from the day it was written, and stopped being the
right question the day a solve could carry a sequence.

A stage changes **what an electrode holds** - that is the whole design - and may not change
**where it is**, which `SameGeometry` and `SameGeometry3D` already enforce. So two conductors
sharing metal at one potential while the trap holds and at two while it pushes are a field of
a geometry nobody described **for exactly the duration of the push**, on a document that
validates, solves and runs. The check that exists to prevent that would have said nothing.

Both checks now gather every state - the base, each stage, and each ramping stage's far end -
and refuse a pair that disagrees in any of them, naming the stage. Because the geometry is
fixed across states the expensive half is unchanged: one intersection test, or one witness
search, per pair.

**Three things worth keeping.**

The far end of a ramp is the arm that matters and the easy one to miss. A ramping phase's
opening excitations are usually the previous phase's, so a disagreement introduced by the
ramp lives only at the end it is walking toward - which is the whole point of a mass scan or
a TIMS elution, where a potential ends somewhere else.

**None of the twenty-two shipped templates is affected**, and that is the argument for doing
it now rather than a reason not to. A guard written before the case exists costs an
afternoon; the same guard written after costs an afternoon plus whatever was published from
the run it should have refused.

And the shape is the one this repository keeps recording: **a question answered by a proxy
that was equivalent when the case set was smaller.** `einzel solve` reading a driven
electrode's DC, `CanDoWork` asking the base potentials of a sequenced trap, the run fork
asking whether two *adjacent* phases differ in mode, the terminal listing the modes that have
no flight time. Each was correct when written. The tell is a check that reads one state, one
mode, or one tap of something the format has since made plural.

## A branch-and-bound's running best is an upper bound, and it is not the quantity's name

Two mistakes in one number, both mine, and they compound.

The first prototype of the volume overlap check reported "closest approach 1868.34 um at
rodOuter / rodTop" for the C-trap, and that figure reached a commit message, `CLAUDE.md`,
`SPEC.md` and two test doc comments before it was checked. **It is an unconverged running
best.** Branch and bound reports the smallest value *found so far*; more probes can only
lower it, so any value read out before the queue empties is an upper bound on the answer.
Re-run at twenty times the budget it settles at **1032.73 um** - and that is the value the
very first hand probe of that pair had already printed, on the first night, from a single
point.

The second mistake is what the quantity is. `min over space of max(dA, dB)` is not the
distance between two solids: the midpoint of the shortest segment joining two surfaces has
both distances equal to **half** the gap, so this minimum is at most half the separation and
can be less. Reporting it as "the nearest metal is 1.87 mm away" gave a number that was
wrong twice over - unconverged, and then read as a quantity it is not. What is defensible
with no geometric assumption is the measurement itself: no point is inside both, and the
deepest the search reaches is 1.033 mm *outside* both, so the surfaces are at least 2.07 mm
apart.

**The tell was available and I did not take it.** The value carried more digits than a
search which had exhausted its budget could justify - 200,000 boxes had been spent on that
pair without the queue emptying, which is precisely the state in which "best so far" means
"not yet". A search that terminates by running out of budget should report its answer as a
bound, or not report it at all.

**And the experiment the correction suggested does not exist.** Having found the real
separation, the obvious next measurement is how thin an interpenetration the check can still
find *on curved surfaces* - walk one rod into the other and watch. It cannot be done on this
pair: shifting `rodTop` three millimeters along the axis closes the gap by only 0.92 mm,
because their closest approach is mostly radial. Growing the template's own dimensions
instead trips the polygon self-intersection check first, at `rodHalfWidth` 5.5 mm, before
the rods reach one another - and non-monotonically, since the profile changes shape. So the
sliver limit recorded for this check is measured on two boxes sharing a face, where the
shared volume is a slab and the search finds **1 um of shared metal on a 10 mm box**. A
curved near-tangency gives a lens-shaped sliver rather than a slab and is harder.

**So it was measured on a geometry built for it rather than on a template.** Two tori about
one axis: profile circles of radius r with centers d apart share a lens of width `2r - d`,
which is a closed form, so the sliver is a controlled quantity where the template's was not.
On a 3 mm tube the search finds a lens **5.8 um** deep at the shipped 3,000 probes, against
1 um of slab on a 10 mm box - an order of magnitude harder, as the shapes predict. **The
limit is a budget rather than a wall**: 30,000 probes reach 4.8 um and 300,000 buy nothing
further, so a halving costs more than tenfold, which is what subdividing in three dimensions
should cost. And the threshold is identical at 64 and 256 profile vertices - the control that
separates a property of the search from a property of the polygon, which matters here because
an N-gon sits inside its circle by 3.6 um at 64 vertices, comparable with the sliver itself.

**The general form: when a real geometry cannot be driven into the regime you want to
measure, build one that can.** A synthetic pair with a closed-form answer measured in
minutes what the template refused to show at all.

## An error that cannot say where it is, and the 64 copies that made the case

`EinzelError` carries a JSON Pointer because AGT-3 makes an error a **recovery
instruction** rather than a notification. A dimension mismatch is raised inside
`Quantity`, which has two operands and no document, so it names the root as a
placeholder - and the frames that caught it, which were resolving a *named parameter* or
a *numbered electrode's face*, added it verbatim. **The path was known at the catch site
and discarded**, which is the same shape as `FieldAssembly.Build` dropping its
`SolveReport` and the sweep evaluator dropping its warnings.

It had been written down, as "a derived-parameter units error reports its path as `/`
rather than naming the parameter - a small AGT-3 gap, not yet fixed". That description is
accurate and it is why nothing happened: one parameter with a vague path is a blemish.

**What changed is that the consequence got measured.** A foil thickness written without a
unit on a real template gives **64 identical `UNITS_INCOMPATIBLE` errors, every one
located at `/`** - naming neither which electrode nor which face, and indistinguishable
from what a single mistake would print. Located, each reads
`/fields/2/solve3d/electrodes/0/repeat/7/maxY`, down to the repeat index. The gap was not
small; it had only ever been seen small.

**And the fix was already present, ten lines above the place it was missing.**
`ParameterSurface` located a *literal* parameter's failure with `with { Path = path }` and
dropped a *derived* one's - the two branches of one method disagreeing, which is how the
recorded gap came to exist without anyone seeing it as an instance of anything.

Two things worth keeping about the shape of the fix. **Only the placeholder is replaced**:
locating an error must never make it *less* located, and an expression evaluator is handed
the path it works on and raises its own failures with it, so an outer frame that knew only
the enclosing element would otherwise coarsen every one on the way past. And the teeth are
measured in both directions - making `At` the identity fails three tests, removing its
placeholder guard fails a different one.

**How it was found is the reusable part.** Not by a failing test: by driving a refusal I
had just added through the CLI, which is this project's own rule that after adding a
producer you ask what reads it. The first attempt at a deliberately broken model used a
bare `2.0` where the grammar has no unit literals, so it produced the wrong error
entirely - **and the wrong error was the finding**. A probe that fails for an unintended
reason is still showing you something; read it before fixing it.


## A convergence ladder at one end of a sweep says nothing about the other end

The TIMS resolving-power series is a sweep over ramp duration, and its mesh convergence had
been established once, on the 8 ms rung: sigma 93.48 / 79.99 / 74.90 us at 256 / 512 / 1024
intervals, an observed order of 1.41, and a converged value 30 per cent above the coarsest.
The register carried that as a blanket caveat over the whole row - *every figure here is
mesh-limited and low* - which was the honest reading of one ladder and turned out to be
wrong at the other end of the sweep.

**At 128 ms, doubling the mesh moves R by 0.8 per cent against 16.9 at 8 ms.** Twenty times
smaller, and the 256-interval rung already recorded in the register is 1.3 per cent low
rather than 30.

The mechanism is worth writing as a variance rather than as a width, because as a width it
looks like a contradiction. Taking the extrapolated widths as the physical ones, a
256-interval mesh adds 3,580 us^2 at 8 ms and 14,562 us^2 at 128 ms - **four times more in
absolute terms**, which is what one would expect, since the long-ramp packet spends longer
in the analyzer and so accumulates more numerical diffusion. What changes the conclusion is
that the physical variance grows 105-fold over the same range, so the fractional inflation
falls from 69 per cent to 2.7.

**The consequence is not a level, it is a slope.** A bias that shrinks along a sweep tilts
the sweep. The scan-rate exponent over 8 to 128 ms reads **0.306 at 256 intervals, 0.252 at
512 and 0.215 once both ends are extrapolated**, against the law's 0.250 - so the
coarse-mesh sweep is 22 per cent steep, and the 512 sweep's near-perfect agreement with the
law is **two unequal errors cancelling**, not convergence. Reporting that agreement would
have been the worst of the three outcomes: right for a reason that does not survive
refinement.

So: a convergence study establishes convergence **at the point it was run**. Where a sweep
crosses a regime in which the discretization error changes its share of the answer, the
ladder has to be repeated at both ends before any slope taken across it means anything.
Repeating it in the middle is cheaper than it looks, too, and skipping it there has a cost:
the 32 ms rung of this series has no ladder, so the per-doubling figures through the middle
of the sweep cannot be corrected at all.


## A declared duration and a simulated window are two different things

A TIMS elution declares a ramp, and the ramp's *rate* is the physics: it sets beta and so
the resolving power. How much of the sequence is computed is a separate question, and the
study models had been declaring both at full length. So the 128 ms rung stepped a tunnel
whose last ion was collected at about 40 ms out to 128 ms plus a 3 ms drain - roughly two
thirds of a fourteen-hour run computing an empty box, and the checkpoint said so plainly
while it was happening: **99,999.994 ions collected, 2.4e-165 still inside**.

The fix is to shorten the phase while leaving the rate alone - cut the 8 ms elute from 8000
to 3900 us and end its ramp at 51.25 V instead of 0, which is the same 0.0125 V/us. The
result reproduces the full-window run to **every printed digit** of mean, sigma, peak,
elution voltage, width and R, at **2.05x fewer steps**. The arrival timestamps are identical
because the stability limits are; the ion counts differ in the last digit or two at the
1e-87 level, which is the ramp interpolation reaching the same rate along a different
floating-point path.

Two smaller things came with it. **The saving has to be read in steps, not seconds** - the
truncated run's wall clock was only 1.33x better because it shared eight cores with the
long run, which is this file's own rule about wall-clock comparisons on a loaded machine,
met again. And **the evidence that the window was wasted was in the checkpoint all along**:
the collected count and the remaining population are printed every interval, and nobody had
read them as a statement about cost.


## An electrode that keeps its potential somewhere else is invisible to everything that asks

An `edgeProfile` electrode holds a piecewise-linear potential along a domain edge - a printed
board with a resistive divider down it, which is how both mirror templates are written. Its
volts live in its `profile`, and **every shipped one leaves the scalar `potential` null**,
because there is nothing for a scalar to say.

Two checks ask the scalar, and both got the wrong answer.

**The channel decomposition gave it no supply.** A supply is built from `Direct` and `Taps`,
so an edge profile joined none, appeared in no pattern, and was weighted at zero - and
`RasteriseEdgeProfile` multiplies the profile by that weight. The board was **not in the
solve at all**: no refusal, no warning, a converged solve reporting its other channels. A
model whose right edge was held at -500 V flew its ion across 4 mm in 6.419 us, against
6.44 us of field-free flight at 621.2 m/s. The plate was exactly absent, to three figures.

**And the start-at-rest guard concluded nothing could move an ion.** `Energised` asks
`Potential != 0 || IsDriven`, so a model whose only live electrode was a profiled board was
refused as an instrument in which nothing happens. That is the **sixth** configuration that
check has had to learn, after the DC, the drive, the 3-D arm, the solved stages, and an
analytic element energised only by a phase - and its own remarks had already written down the
rule that predicts it: *a new way to hold a potential is a new configuration.* The remark was
there; the case still had to arrive.

**Why it went unseen for so long is the part worth keeping.** Both templates that use edge
profiles declare **no drive**, and an undriven solve takes the other arm, where the scale is
1.0 and the profile is applied as written. So every shipped number was right, and the defect
was reachable only by declaring a drive on a geometry that also has a profiled board -
which nobody had, until an ejection slot needed an extraction plate behind it. **A shape used
by two templates that happen to share one property is a shape tested in one regime.**

The fix is a unit coefficient: a pattern is normalized by its leading coefficient and the
channel's weight is that leading value, so weight times normalized coefficient is exactly one
however many other electrodes share the supply - which is precisely "apply my profile once,
as written". A profile that is everywhere zero gets zero instead, so a grounded board does
not conjure a channel whose field is nothing.

**And the fix creates a narrower wrong case, which is now refused rather than left.** A stage
that *changes* a profile would be two spatial patterns wearing one coefficient, so the second
stage would silently run the first one's field. A uniform scaling of every point would in fact
be expressible, and is not detected - so the refusal names the whole restriction rather than
the half of it that is genuinely impossible, and says what to do instead.


## A table whose rows differ by less than their counting error

The recorded comparison of four slot profiles was measured on **twenty ions**, of which about
thirteen go toward the slot at all - so each entry carries a counting error of roughly four
ions, which is the size of the differences between the rows. Re-measured at 300 ions the
*ordering* survives and the *magnitudes* move: the shipped profile reads 9 of 20 there and
38.0 per cent here.

Nothing about the original run was wrong, and its conclusion - that a straight channel barely
works and a relief rescues it - is the same conclusion. What is not supportable from twenty
ions is any of the finer readings the table invites, such as how much of the loss is on the
relief's walls against the face beside the slot.

The rule is not "use more ions". It is that **a table of counts should carry the count**, so
a reader can see at a glance which of its differences are real - and that an ensemble figure
whose denominator is a subset (here, the ions that went the right way) should say what the
subset was, because the sample is always smaller than the run.


## A template can model the prototype a paper describes rather than the instrument that shipped

The linear ion trap template cut one ejection slit, in the +x rod, faithfully following
Schwartz 2002. The released LTQ ejects radially **both ways**, through a slit in each x rod
with a detector behind each — and the paper says so on its own page, in the paragraph that
measures the prototype:

> "This produces a scan out efficiency of 44% ... It is assumed that half of the ions are
> neutralized on the rod opposite the ejection slot. The use of a second slot on the opposite
> rod in combination with a second detector should produce a scan out efficiency of 88%"

So the correction was available in the source the template was built from, in the sentence
that gives the number the template was later judged against. **Reading a device paper for its
geometry means reading its future tense as well as its present**: a prototype paper tells you
what the instrument became, and the tell is a passage that proposes a change and computes what
it would buy.

What it cost was not the geometry, which is one parameter. It was the **finding attached to
it**: the register recorded a slot dipole of 9.6e-4 as *the slot's field fault*, with the
stretch unable to cancel it. A slit in each x rod is symmetric about x, so the dipole and the
hexapole cancel to rounding — the fault belongs to the prototype and not to the device. The
number was right; the sentence around it named the wrong object.

**And the correction can overshoot.** The Stellar's trap has slots in **all four** rods, which
the dual-pressure paper states explicitly — "a fully symmetric geometry with ejection slots in
all four rods. This design provides fully symmetrical RF fields" — with detectors behind only
the x pair. Reading "ejects through two slits" as "has two slits" would have removed two real
slots. The two readings are separated by what the y slots are *for*: they shape the field and
pass no ions. The model says which is right without the paper: slits in the x pair alone leave
the stretch four-fold and the slots two-fold, which admits an octupole of 1.25e-4 against
1.4e-16 with all four.

The general rule: **a geometry in a template is a claim about a physical device, and it should
name which device.** Every slot parameter now says whether it is the prototype's or the
released instrument's, and the prototype is one override away rather than lost.



## A memory figure written from a list of fields, and the geometry it left out

`einzel estimate` billed every solve 48 bytes a node, from a comment naming what the solver
allocates: potential, right-hand side, residual, "and the correction and restriction buffers
down the V-cycle hierarchy". Nothing measured it, and it was a fifth of what a volume solve
holds. Sampling the live set through real builds - a full collection forced from a second
thread every few milliseconds, keeping the largest - gives **240 bytes a node** on the Galerkin
hierarchy, 142 on the rediscretized one, and 143 to 150 for a plane solve.

**The fields the comment listed are 24 of the 240.** The largest terms are the geometry: a
conductor mask (9 bytes a node) and six cut-link arms of two doubles each (96), allocated in
full although about 4 per cent of the arms are cut. The Galerkin hierarchy then stores the fine
operator as eight doubles a node and forms its first coarse level from it with three scratch
arrays over the fine grid - 64, 20 and 27 more, all live at once at the peak. **A list of what
a solver allocates, written by someone thinking about the numerics, lists the numerics.** The
Astral at a 1 mm cell was billed 3.1 GiB and needed 15.3. (The cut links have since been stored
over their cut arms alone, which took the Galerkin figure to 149.5 and that Astral to 9.5 GiB;
`docs/cli.md`.)

Two checks said the live set was the right quantity. Under a hard heap limit the Astral at 4 mm
completed at 279 MiB and ran out at 275, against a sampled live peak of 259 - so the collector
gives back its garbage when it must, and the live set plus a few per cent is the floor. And
with memory to spare the same build's heap reached 390 bytes a node, because the collector
frees large arrays lazily. **What has to fit is the live set; what a process shows is higher
and depends on the machine**, so the estimate states the first and says the second is higher.

The statement now lives beside the arrays (`SolveMemoryModel`), one named term per array, and
a test measures a real build against it as a slope between two meshes, so the runtime's fixed
cost cancels. The band is 0.90 to 1.02: tight above, because an estimate that runs under is
discovered by the run failing, and one extra field is 3.3 per cent - a mutation adding one
fails both volume tests.


## A method run once keeps every temporary it ever made

A driven volume geometry is solved one basis channel at a time, and each channel should keep
only its potential. The linear ion trap, two channels, measured **353.7 bytes a node** at its
peak against 248. The first cause was ordinary: the loop kept each finished channel's mask in
the record it returned, 105 bytes a node, to report two integers. Replacing the mask with its
counts took it to **344.7** - nine bytes, which is the mask's own flags and values, and not the
96 of cut links hanging off it.

The cut links were held by **a temporary with no name**. The record was built as
`new ChannelSolve(index, mask.FixedCount, mask.Cuts?.CutCount ?? 0, ...)`, and the null check
puts `mask.Cuts` into a stack slot. A method called once runs as first-tier JIT code, and
first-tier code reports every such slot live until it is reused or the method returns - so the
previous channel's cut links stayed reachable through the whole of the next channel's solve.
Moving the loop body into a method of its own, so the slot dies with the frame, gave **248.2**
against 248 predicted.

Three things generalise. **Setting a local to null does not reach it**, because the reference
is not in the local. **A test in Debug sees it and a benchmark in a hot loop does not**: the
channel loop runs twice, never tiers up, and behaves in production exactly as it does
unoptimized - which is the regime to measure a one-shot build in. And the fix is structural
rather than clever: **when a loop body allocates something large that must not outlive the
iteration, give the body a frame of its own.** The test for it compares a two-channel build
with the same geometry undriven; putting the inline loop back makes the second channel cost
102.8 bytes a node against a limit of 30.

## A node on a face two conductors share is decided by rounding, and no refinement ladder can see it

`astral-3d`'s foil is sixteen slices at sixteen potentials, sharing a face every 28.125 mm,
and three of those faces sat exactly on rows of nodes in the 4 mm foil mesh. A node exactly
on the face between two conductors that disagree is inside one, the other, or neither
according to the last bit of that face's arithmetic - and a free node with a vanishing arm to
each side takes whatever mixture of the two potentials the rounding picked. It is not
discretization error. It is a coin toss, and at this mesh it was worth 0.27 percent of the
flight time.

**The convergence tier could never have found it.** Grid intervals are powers of two over a
fixed domain, so a face on node k at one rung is on node 2k at the next: every mesh in a
refinement ladder sits on the same faces, and each rung tosses the same coin. What found it
was an **exact symmetry the arithmetic does not share**. Electrostatic similarity says an
instrument scaled by 0.2 flies the same path in 0.2 of the time; 0.2 is not representable, so
every coordinate moves by rounding and nothing else. Binary-exact scales (0.5, 0.25)
reproduced the flight to the bit, 0.2 moved it 0.27 percent, and 0.1999999 moved it a
different 0.03. A physical invariance that perturbs only the last bit is a probe of every
knife-edge in a discretization at once.

**And a coin toss can hide a larger error behind it.** With the foil mesh moved a tenth of a
cell off every shared face, the flight time was deterministic - and moving the mesh further,
in fifths of a cell, moved it over 0.83 percent, because the stripe is thinner than a cell
and exists only where its surfaces cut links. That is ordinary second-order error (0.22 per
cent at 2 mm), but a ladder reports one point per rung and so presents a spread of that size
as a resolved number. The published agreement this template carried, "0.4 percent", was one
draw from it.

The rules. **Where conductors that disagree share a face, keep the face off the nodes** - a
margin of a tenth of a cell is plenty, and it has to be checked, because the mesh's own
rounding decides where the nodes fall. **Where a feature is smaller than a cell, where the
mesh sits is a convergence dimension** as much as how fine it is, and an agreement quoted
finer than the placement spread is not an agreement. And after removing a source of
nondeterminism, measure what it was sitting on top of before quoting the number it now gives.


## A check specified from a description of the case reported that case clean

The previous entry describes the foil's coin toss as *nodes* on shared faces, and the engine
check proposed after it was specified the same way: find nodes within a rounding of two
disagreeing conductors' surfaces. Built exactly so, it swept every shipped template and
example and found all of them clean - which was the answer hoped for, and would have been
recorded as the result.

**The sweep was only worth something if the detector fired on the case that motivated it,
so that ran first: `astral-3d` with its mesh shift removed. It reported that clean too.**
The foil stripe is 0.715 mm thick on a 2.9 to 3.8 mm mesh and holds **no node at all**; the
three faces lie on node *planes*, and what the rounding decided was which slice's potential
the stencil arms lying in each plane were cut against. 180 arms, 0 nodes. Comparing the
full-size and compact masks cell by cell settled where the rest of the recorded "454 nodes
and 1,586 cut links" were: every node and 1,406 of the links sit on the domain's upper face,
where grounded boards' ends meet it - a separate, harmless flip between two states both near
zero volts - and the 180 arms are the whole of the 0.27 percent. The description had merged
two effects and named the wrong one, and a check built from the description inherited it.

Two smaller things came with it. **A test on entry fractions is discontinuous where a test
on distance is not**: "both conductors entered at the same fraction" misses the face nudged
one ulp, because then the arm grazes one conductor instead of entering it while the cut has
already flipped - so the arm test asks whether the point met is within tolerance of the
other surface. And **a tolerance that flags coincidence does not flag the step it sits on**:
a face 2.6e-5 of a cell off a node draws no warning and is still on the edge of a 0.27
percent discontinuity, which a sweep across it would cross silently.

The rules. **Run a detector against the case that motivated it before trusting what it says
about anything else** - a clean sweep from a check that cannot see its own reason for
existing is the most convincing wrong answer there is. **Specify a check from the mechanism,
not from the account of the incident**: the account said nodes because nodes are how one
talks about a mesh, and the mechanism was "the mesh samples the geometry, and a sample landed
on a discontinuity of the boundary data" - which names nodes *and* arms. And when a
recorded number is quoted as evidence, find out what it counted.

## A cost measured on a branch describes the branch, and garbage is not time

SPEC.md recorded that building a volume mask allocated **61 kB a node** on the C-trap and 4.2
kB on the linear ion trap, against the 105 the mask keeps, beside builds of 95 s and about 200
s that made the mask look like the thing to fix. Every one of those numbers was true of where
it was measured and of nothing else. The branch predated the bounding-box cull, which asks each
electrode only about the nodes near it, and on `main` the same masks allocated 5,470 and 260
bytes a node. The 200 s build was timed under a live-set sampler forcing a full collection
from a second thread every few milliseconds; without it the build takes 13 to 15 s.

**Before acting on a recorded cost, re-measure it on the code you are about to change.** A
number carries the conditions it was taken under - a branch, a build, an instrument attached
to the process - and none of them travels with it into a status page.

The second half is the one that would have survived re-measuring. With the garbage removed the
C-trap's mask fell by a third to a half and the linear ion trap's did not move: a revolve
entry test made about thirty objects per call and several times as many interface calls
through iterators, a prism entry test one list beside a 29-edge polygon. The prism's gen0 count fell from 42 to 2
and the clock did not notice. **Allocation is evidence of cost, not a measurement of it**;
what decides whether removing it pays is what else the call does, and the only way to know is
to time it - which is why the regression test holds the allocation and the documentation
holds the time.

## A measurement that can only read low makes a difference that can read either way

`SolveMemoryTests` sampled each build's live set by forcing a full collection every 2 ms and
keeping the largest, which can only under-read the peak - and then compared builds by their
*difference*, a slope between two meshes or one channel against two. A difference of two
under-readings is not an under-reading: it reads high when the smaller build's peak is missed
and low when the larger one's is. It passed on a sixteen-core workstation and failed on both
four-core CI runners in Release, where a build is eight times faster than in Debug and 2 ms is
a timer tick of about 15 on Windows: the smallest build finished in about 10 ms.

**The flakiness was hiding a wrong number.** With the sampler made dense - waiting only as
long as the last collection took - and each build measured three times, every reading became
stable, and the plane statement then failed every time: 153.3 stated, 132.7 measured, because
it counted coarse levels that do not exist at the instant of its peak. The sparse sampler had
read 143 for the same slope, inside the band, because the error in the difference happened to
point the same way as the error in the statement. **A test that only passes when its
measurement is noisy is not a passing test**; before loosening a band that a faster machine
breaks, make the measurement deterministic and see what it says. And choose the statistic for
the errors the measurement now has, not the ones it used to have: the largest of three was
right while samples could only miss, and wrong once they were dense enough to catch an instant
the statement does not count - the middle of three is robust to a lone outlier either way.

**And the outlier it was robust to was the sampler's own.** `GC.GetTotalMemory` after a forced
collection is not the live set at that collection: it counts whatever the build allocated
between the collection and the read, and a sampler thread descheduled in that gap - routine on
a loaded four-core runner - came back to a fresh V-cycle residual, eight bytes a node of
garbage. On PR #57's Linux run two readings of three caught it and the middle could not help.
**Read what the collection reported about itself** (`GC.GetGCMemoryInfo`, size after less
fragmentation), not a counter that keeps moving after it; and when an outlier only appears
under load, load the machine on purpose before explaining it.

## A perturbation that isolates one thing must move only that thing

The grounded-boundary check (`docs/numerics.md`, "A grounded face of the domain is a third
conductor") needed a survey: seventeen shipped solved elements put a node on a grounded wall
and on a conductor's surface at once, and the question was whether any of them cared. The
plan was the one that had worked for the Astral's foil - move the domain by 1e-7 of its
extent either way, so the node lands on one side and then the other, and compare. **It
reported several volts of difference in the middle of the ion funnel, far from any wall** -
which reads as the coin mattering, and was not.

**The first move changed the mesh.** Interval counts round up to a power of two, and the
funnel's radius is exactly 64 cells, 16 mm at 0.25 mm; growing it by any amount at all made
it 128. So the comparison was between two different discretizations, and the difference was
theirs. This is the same cliff `MeshCliffTests` and Amendment 33 are about, met from the
other side: not a cost that jumps, but a perturbation meant to be infinitesimal that is not.

**The second move - translate each conductor touching a wall instead, on a fixed mesh - found
a different coin entirely.** The funnel's first and last rings have their inner faces exactly
on node rows (12 mm on row 48, 1.5 mm on row 6), and translating a ring moves those faces
too. A single conductor's face on a node plane is continuous in its normal position, but the
arms *lying in* that plane beside the conductor are cut at its side face or graze past to a
node a cell away according to the rounding: two cuts at 0.46 and 0.077 of a cell became two
uncut arms, and the flight time moved 4.5e-4 for a 6.4e-6-cell move. Real, worth recording,
and nothing to do with the wall.

**Moving only the face on the wall** - the one thing moving the domain face changes relative
to the conductor - gave the clean answer: fifteen of the seventeen identical to the bit more
than three cells from the wall and in every figure, the two mirrors not.

The rules. **A perturbation used to isolate an effect has to move exactly that effect's
cause and nothing else**, and "move the domain" and "move the conductor" each move more than
they appear to - the mesh with the first, every other face of the conductor with the second.
**Check the mesh is the same mesh before comparing two solves of it**; `(CountX, CountY)`
equal is one assertion. And **a surprising difference is a finding before it is an answer**:
the interior volts were two things, neither the one being measured, and one of them is now an
open item of its own.

## A change that streams less memory can still be slower, by moving what the JIT inlines

The cut links on #57 stopped storing every arm of every node - 96 bytes a node the smoother
streamed on every sweep and almost never used - and measured faster on the Astral and the
C-trap. An outside measurement then found **a whole solve 10 to 12 percent slower** on a
four-rod quadrupole of 1.09 M nodes, three alternated pairs in a row. Both were right.

**Timed per sweep, the two builds disagree about which is faster depending on the JIT tier.**
With full optimization from the first call (`DOTNET_TieredCompilation=0`) the branch's
smoother took 58 to 62 ms against main's 80 to 91. Under normal running, warmed up through
dynamic PGO, main's took **37 ms and the branch's 51**: the profile-guided tier doubled main's
speed and did almost nothing for the branch. The listing says why. Main's optimized `Smooth`
had everything inlined, 151 inlinees and no calls in the loop; the branch's had **six calls to
`Arm`** per free node, each passing thirteen arguments and returning two through references.
The packed store made the cut arm's read a rank over an eight-node block, which made `Arm`
bigger, and the inliner stopped taking it - for the common case, where no arm is cut and the
rank never runs. The bytes saved were real and smaller than the frames added.

Fixed by making the shape say what is hot: `Arm` is inlined by instruction and everything about
a cut arm is a call to a method that never is. **29 ms against main's 38 on the quadrupole and
24 to 25 against 36 on the linear ion trap**, the JIT listing showing `Arm` inlined and only the
cold call left, and the potentials identical to main's to the SHA-256 on both channels of both.

The rules. **A microbenchmark has to run at the tier the code runs at.** Fifteen calls through
reflection measured on-stack-replacement code and gave a third answer; eighty warm calls gave
the one a solve sees. **When two careful measurements disagree, look for the variable neither
controlled** - here the inliner, which neither alternated pair could see. And **a whole-solve
comparison on a loaded machine cannot resolve ten percent**: on this one the same work took 7.5
to 18 s across a few minutes, while the minimum over eighty sweeps was steady to a millisecond.
`DOTNET_JitDisasm` with `DOTNET_JitStdOutFile` works on a release runtime and takes a minute.

## A fix reaches the function it was made in, and the construction lives in several

Two defects found in one afternoon, and they are one defect.

**`einzel export` was still reading the DC of a driven cross-section.** `einzel solve` had
exactly this bug - one mask built from each electrode's DC potential, so the shipped
`quadrupole-rf` solved as a grounded box - and it was fixed by giving the builder a
`SolveChannels` and pointing `solve` at it. `export` did not call anything that was fixed. It
built the same mask the same way in its own twenty lines, and went on writing driven
cross-sections as volumes of zeros, which ParaView opens without complaint. Checked over every
template against the base build: **six** exported every node at zero (the RF quadrupole, the two
cross-section linear ion traps, the multipole guide, the Paul trap, the travelling-wave guide), **four**
exported their DC chain and dropped their RF (both funnels, both TIMS front ends), and the
seven static cross-sections are bit-identical before and after the move.

**And the edge-profile fix had reached the driven arm of one function and not the static arm
ten lines above it.** An edge profile's volts are in its profile and its scalar is zero; the
decomposition was taught to give it a unit coefficient, and `Patterns`' static branch went on
reading `electrode.Potential` and handing a zero to a rasterizer that multiplies the profile
by it. So `einzel solve` on the planar mirror pair reported a mirror with no boards - converged,
peak 4800 V, which is the cap and looks right. A check on the peak cannot see a missing board.
What caught it was moving `export` onto the same function and requiring every static
template to survive unchanged.

**The rule.** When a defect is fixed in one caller, the population to enumerate is not the
callers of the function that was fixed - it is every place that *performs the same
construction*, which here meant grepping for `BuildMask(` without a `potentialOf` on a
geometry that might be driven, and reading every branch of the function the fix landed in.
A fix is a statement about a spelling; the bug was a statement about a question ("what does
this electrode hold?"), and every spelling of that question had it.

**A third thing came with it: a basis written as a potential.** A driven channel is a spatial
pattern normalized so its leading electrode holds one volt. The three-dimensional export
already named such an array `potential_V` when it was the only channel, so a quadrupole driven
at 500 V opened in ParaView with its rods at 1. Out by the drive amplitude, and plausibly so -
a small, confident, wrong number, which is the kind this project keeps finding. The array is
now named as a basis and the header says what it holds and which electrodes carry it.

**And review found the dictionary itself was the defect.** The static channel's pattern was a
map from electrode name to potential, and a name is optional, defaults to `electrode`, and need
not be unique - so two unnamed plates at opposite potentials became one entry and exported at
the same voltage, while every run, which builds its mask electrode by electrode, got them
right. **A display name is not a key.** Where a list of declared things is the source, identify
them by position, and derive any report about them from the same function that acted on them.
The next entry made the name a key on purpose, by giving each unnamed electrode its own and
refusing two that share one; the static channel kept its positional identity anyway, because
the mask is built from the list and a report about the mask should be built from the same list.

## A default every instance shares cannot be a key

An electrode's name was optional and defaulted to `electrode`, and nothing required two in one
solve to differ. The channel decomposition a driven or sequenced geometry is solved through is
a map from electrode name to volts, and every contribution was written with `=`. So two unnamed
plates at +2 V and -2 V were one entry holding -2 V, and the deflector they make was switched
off: an ion the same model with named plates bends **0.595 mm** flew **1.6e-8 mm** off its
axis, on a document that validated, solved, converged and exited 0. The same in a volume
(0.178 mm against 2e-6), and in a geometry with no drive at all whose plates a sequence phase
switches on.

**Three things kept it hidden.** A static geometry builds its mask electrode by electrode, so
its field was right and the defect needed a drive or a sequence to show; the control that says
so is the same model with the drive deleted, which bends the ion 0.5952 mm with its plates
unnamed. Every one of the 62 shipped templates and examples names every electrode distinctly,
and so does every test that wrote a driven geometry, so the defect lived entirely in documents
nobody in this project writes. And those are the documents an agent writing from prose is
likeliest to write: the schema said a name was optional and described it as "used in
reporting", which is how it reads and not how the code used it. **Four readers keyed on it** -
the decomposition, the impact report ACC-5 itemizes losses by, the diffusive seed-loss ledger
and the viewport's electrode count - so it was an identity in everything but its declaration.

**So the fix makes it one, rather than moving the decomposition onto positions.** Keying by
position would have fixed the field and left an ion lost on either plate reported as striking
`electrode` - "never 92 percent" in a new form - and a position is the one identity a reader
of a loss report cannot see. An unnamed electrode is now `electrode-N` after its place in the
list, which matches the path an error gives and does not move when another electrode gains a
name; two electrodes in one solve sharing a name are refused at the second one's `name`; and
the decomposition refuses a repeated name itself, so a caller that did not come through the
validator is stopped rather than given a quieter field.

**The scope came from enumerating the corpus, and the obvious one was wrong.** A model-wide rule
refuses `astral-3d` and `compact-astral-3d`, whose two mirror solves each declare all
twenty-two boards under the same names at one mirror's potentials or the other's. That sharing
is right - it is the same metal, and an ion striking it struck one surface - and channels are
built per solve, so it is only within one that a shared name changes a field.

**And the same assignment had a second victim.** One electrode tapping one generator twice, at
one phase or at two phases of a sinusoid, resolves both taps into the same supplies, so it
wrote one entry twice and the later tap replaced the earlier: 100 V and 50 V flew exactly as
50 V alone. Contributions to a keyed accumulator add; `=` is right only where something
guarantees one contribution per key, and nothing did.

The rules. **A default every instance shares cannot be a key**: if a field is optional and
anything indexes by it, the default has to be unique or the field required. **Find every reader
that keys on a thing before deciding what the thing is** - here four did, and between them they
had already decided. And **before scoping a uniqueness rule, enumerate the documents it would
refuse**: two shipped templates said where the boundary was. The export fix (PR #56) met the
same dictionary in the static channel and wrote the first half of this - a display name is not
a key - and left the driven path for its own change, which this is.

## A timeout on a shared runner measures the runner, and a fixed wait looks like work

The live-session tests failed on the Windows CI runner with "Initialization timed out",
twice on one PR and once on another, neither of which touched the server. Two things were
going on, and only one was the failure.

**The failure was a starved process start.** The tests that passed in the same run took 8 to
17 seconds and one a full minute, against 5 on a workstation: a distribution, where a
deadlock is a wall. Oversubscribing a workstation reproduced the shape - the handshake went
from 0.3 s to 3.3 s at twice the cores and 15 s at six times - and a trivial Python
subprocess had once taken over thirty seconds in the same CI phase. The test is about the
protocol, so its start-up allowance is now three minutes rather than the client library's
sixty seconds. **Before raising a timeout, establish that the slow cases finish**: here they
visibly did, and that is what separates starvation from a hang that a longer timeout would
only hide.

**The other was a fixed wait that looked like the cost of the test.** Every test took five
seconds even idle, and almost all of it was disposal: the client library waits for the
server to exit before closing the server's input, and this server exits when its input
closes, so the wait always ran out and the server was killed. Varying the library's
shutdown timeout moved disposal exactly with it - 1.06, 2.06, 5.05 s - which is the
signature of a wait rather than of work. **A duration that does not move with the work and
does move with a timeout is a timeout.**

## A fixture built from a comment tests the comment

`Surfaces.Revolve` and `Surfaces.Extrude` decided whether a profile was closed by looking for
its first point repeated at the end, and the comment on the helper said why: *a closed run
from `Contours` repeats its first point at the end*. It never has. The joiner drops the repeat
and sets `Run.Closed` instead, so every closed outline the viewport revolved or extruded, from
the day the viewport drew conductors, was read as open and lost its closing segment: a slit one
sample wide down a prism, a band one sample wide missing from a ring. The tests of `Revolve`
and `Extrude` built their profiles by hand, with the repeat, exactly as the comment described.
They passed for as long as the code existed, and they were testing the comment.

**What found it was an expectation with a ceiling.** A test of something else - that the einzel
lens's tubes are closed against the edge of their domain - asked for the volume each mesh
encloses, against Pappus inscribed in 48 facets. The entrance tube came out at **1.0008** of it.
A polyhedron inscribed in the declared solid cannot exceed it, so the excess was a finding
rather than noise; a bare revolved rectangle came out exact, which put the extra volume in the
mesh rather than in the arithmetic, and a divergence-theorem sum over an open surface is not a
volume at all - it depends on where the origin is. Had the assertion been a band of a per cent
either side of Pappus, 1.0008 would have passed.

The rules. **A unit's test input should come from the producer that feeds the unit in use**,
not from that producer's description: a convention stated in a comment and followed by a
hand-built fixture is checked against itself, and the producer is never in the loop. **Where
two pieces of code must agree on a representation, give the boundary a spelling that carries
it** - here an overload taking the `Contours.Run`, which reads the run's own flag - so the
agreement does not depend on either side remembering the other's convention. And **write the
bound a closed form actually guarantees**: an inscribed solid has a ceiling, and a symmetric
tolerance would have absorbed the only sign that anything was wrong.
