# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Read SPEC.md first, and update it last

**`SPEC.md` is the living specification. Read it at the start of every session**,
before proposing or planning anything. It carries all 118 tagged requirements from
`einzel-software-spec-r06.html` with a status and the evidence behind each, the
delivery phases planned against actual, the amendments where building it showed
the original wrong, and a ranked list of what to do next. It answers "where is this
project" in one place, which nothing else here does — `docs/changelog.md` (imported
below) is a changelog and the other `docs/` pages are per-subsystem.

**Update it in the same change that alters what it says.** Specifically:

- A requirement's status changed → update its register row *and* its evidence. The
  evidence is the point: **Met** means a measurement is named, not that something
  was attempted. Use **Unverified** when a thing plausibly works and nothing
  measures it; it is not a synonym for met.
- The requirement itself turned out to be wrong, incomplete, or right for a reason
  r06 does not give → add an entry to **Amendments**, with the evidence that forced
  it. Do not silently diverge: r06 stays unchanged as the record of intent, and
  every disagreement is written down as a disagreement.
- A §23 open decision got settled → move it and say how.
- The "What to do next" list changed → reorder it, and say why rather than only
  what.

A status page that has drifted is worse than none, because it is trusted. This is
the same argument that makes the platform layer of `AGENTS.md` generated rather
than hand-written, applied to the one document here that is not generated.

## Versions are `YY.feature.patch`, and bumped at release time

Two-digit year, a feature number, a patch number: `26.1.0` is the first feature release of
2026, `26.1.1` a fix on it, `26.2.0` the next feature release. **The version is bumped when
a release is cut, not during development**, so the working tree carries the version last
released and the commit hash distinguishes builds within it.

The single source is `<VersionPrefix>` in `Directory.Build.props`; `EngineBuild.Version`
reads it back through the assembly's informational version, so the CLI, the run manifests
and `einzel doctor` cannot disagree with the build. The git tag is `v26.1.0` and the release
workflow **refuses a tag that is not `vYY.feature.patch`**, because a malformed one produces
assets nobody can order and the place that is noticed is a download.

`release-notes/README.md` carries the scheme, where the version lives, and the steps for
cutting a release. Append to `RELEASE_NOTES_next.md` as things land; it is renamed at
release time so a planned patch can become a feature release without being renamed twice.

**A year is not a semantic major, deliberately.** The thing this project must not silently
break is the model format, which carries its own `schemaVersion` and its own tested
compatibility rule. Putting a second compatibility promise on the package version would
state the same claim in two places, and the two would eventually disagree.
`solverBehaviourVersion` is separate again (PRJ-3): it changes when the numbers would
change, which is a different event from a release.

## What this repository is

The goal is to **build the software described in the specification**: a general, open-source, agent-native ion-optics platform — an open replacement for SIMION. Spec §1's device table spans einzel lenses, quadrupole mass filters, ion funnels, stacked-ring and travelling-wave guides, multipole guides, linear and 3D traps, orthogonal accelerators, reflectrons and MR-TOFs.

**The companion memo's MR-TOF is the first customer, not the design target.** It is a proof of concept that exercises the machinery end to end; the spec's own test of generality is §21 Phase 5 — "a second, unrelated instrument modelled by someone who did not write the code." Nothing device-specific may leak below `Einzel.Library` (architecture invariant 2). When adding capability, ask what it would take for a funnel or a quadrupole, not only for this analyzer.

**Where the work is** — the stage-by-stage record of what has been built, measured and
fixed — is kept in `docs/changelog.md` and imported here, so every session loads it at
startup as though it were written in this file:

@docs/changelog.md

**Add new entries to `docs/changelog.md`, not here.** GitHub Copilot's pull-request review
reads this file as its instructions, and while the record lived here it failed on every pull
request with "Prompt too big": 420 KB against a limit of about 110,000 tokens. The import
keeps the record in every Claude session and out of Copilot's prompt.

**`SPEC.md` is the living specification** — see the note at the top of this file for what it holds and when to update it.

The two design documents remain the source of truth for *intent*. Tracked alongside them: `SPEC.md`, `README.md`, `LICENSE` (Apache 2.0).

- `einzel-software-spec-r06.html` — the software specification, rev 0.6. **The source of truth for every architectural decision below.** Tracked in git. Read the relevant `§` section before proposing or changing design.
- `compact-mrtof-stellar-memo.html` — companion working memo, rev 0.7. The instrument the platform must model first; the spec's acceptance criteria reference it by section (e.g. "memo §6 item 5", "the memo's mirror pair tracked end to end"). Phase 1 is not done until that mirror pair runs at ACC-1. **Gitignored and not published** — it carries the patent and freedom-to-operate analysis and this remote is public, so it exists only in the local working tree. Do not add it to git, and do not quote its patent or competitive analysis into tracked files.

- `papers/` — **full texts of the reference papers, gitignored and not published.** Kept so
  that a session never has to ask for them again. It currently holds the crowd-control paper
  (J. Mass Spectrom. 2024;59(4):e5006), which is behind Wiley and cannot be re-fetched; the
  instrument paper is open access and `papers/README.md` records how to get it (the browser
  tool, not curl — PMC blocks scripted fetches). **Read `papers/README.md` before asking for
  a paper.** The tracked record of what the papers say is the published register in
  `docs/literature-targets.md` §4, which paraphrases and cites rather than reproducing.

Both are hand-authored, self-contained HTML documents: inline `<style>` blocks over an IBM Plex / CSS-variable palette, figures as inline `<svg>`. Edit the HTML directly; there is no generator and no markdown source. Revisions are new files with a bumped suffix (`-r06` → `-r07`), not in-place overwrites, and the change line at the top of the document records what the revision added.

**Detailed documentation lives in `docs/`** — architecture and the four invariants, the model format in full, device templates, numerics with every measured figure, the lessons from bugs that presented as physics, the CLI contract, validation coverage *and its gaps*, and findings against the specification. **`docs/extending.md` is the one to read before adding a capability**: it names the three kinds of change a device has ever needed below `Einzel.Library`, with the eleven instances as evidence, where each goes, and the traps each has already sprung. Read the relevant page before changing something in that area; it records why things are the way they are, and several of the decisions cost real time to reach.

## Commands

```powershell
dotnet build                                  # warnings are errors; XML docs required on public API
dotnet test                                   # all tests
dotnet test --filter FullyQualifiedName~MeasuredApiSurfaceTests   # one class
dotnet test --filter "FullyQualifiedName~QuantityTests.RoundTripsThroughANamedUnit"  # one test
start <file>.html                             # preview a design document

# The CLI, once built (src/Einzel.Cli/bin/Debug/net10.0/einzel.exe)
einzel init <dir> [--vcs git]                 # create a project
einzel validate models/reflectron.json        # units, bounds, regime validity
einzel run models/reflectron.json --vtu       # run; --vtu writes a ParaView trajectory
einzel run models/reflectron.json --json      # machine-readable, for the agent loop
einzel fly models/quad.json --ions ions/scan.ions.json  # an ion list, one trajectory per row (docs/ion-lists.md)
```

**Two numerical rules that cost real accuracy when broken**, both found by tests that failed for the right reason:

- **A four-by-four interpolation stencil must extrapolate at grid boundaries, never clamp.** Clamping repeats the edge node, which makes the interpolant non-linear in the boundary cell even when the field is exactly linear. An ion enters and leaves a mirror through that cell twice per reflection; a clamped stencil put **7.5 ppm** into a flight time whose exact solution is a pure ramp — over the whole ACC-1 budget. Linear extrapolation of the ghost node took it to 1.9e-10.
- **Measure the interpolant against a *sampled* exact field, never a solved one.** A solved field carries its own O(h²) discretization error, and on a coarse grid that error is larger than the interpolation error it is supposed to be a backdrop for. Comparing against a solved field measures the solver and reports it as the interpolant's — it initially made bicubic look 60× *worse* than bilinear.

`global.json` pins SDK 10.0.400 (`rollForward: latestFeature`), which matters because 8, 9, and 10 are all installed on this machine and the repo must not silently build on an out-of-support runtime. Toolchain per the spec: **C# / .NET 10 (LTS)**, vendored CPython for extensions, ILGPU for GPU paths, WPF (Windows-only) and Avalonia (cross-platform) for the two shells, everything else cross-platform.

Two build settings are load-bearing rather than stylistic, and both live in `Directory.Build.props`: `TreatWarningsAsErrors` (a 1 ppm engine cannot afford a culture of ignored diagnostics — it caught a sign-extension bug in `Dimension` on the first build) and `GenerateDocumentationFile` with CS1591 unsuppressed, so undocumented public API fails the build. That second one is AGT-7: schema descriptions and CLI help are meant to generate from the same metadata.

The CLI being built is the primary surface and the thing to keep working first (spec §15):

```
einzel init | new --from-example | validate | preview | estimate
einzel solve | run | sweep | test | verify
einzel render section|still|animation | export vtu
einzel ext test|register | schema | templates | examples
einzel agents-md | doctor | self-update
```

CLI contract: `--json` on every verb, results on stdout and diagnostics on stderr, documented distinct exit codes per failure class, `--dry-run` on every mutating command, deterministic output ordering, and cold start to first output under 500 ms with no network call in that path (CLI-1..6, PERF-8).

## Architecture (spec §6)

Assemblies, engine outward:

```
Einzel.Core        model, units, geometry, symmetry, parameters, validation
Einzel.Fields      DC/RF solvers, basis + sensitivity fields, interpolants
Einzel.Transport   integrators, statistical diffusion, collisions, space charge
Einzel.Analysis    figures of merit by accuracy class, spectra, aberrations
Einzel.Sweeps      tolerance Monte Carlo, optimization drivers
Einzel.Library     device templates as DATA, plus a parameterization API
Einzel.Extensions  manifest, schema validation, in-process + sandboxed runners
Einzel.Render      projection, vector emit, frame sequences, VTU export
Einzel.Project     project layout, manifests, drift detection, AGENTS.md generation
Einzel.Commands    command objects: validate/apply/diff/undo/journal/attribution
Einzel.Compute     scalar / SIMD / ILGPU kernel dispatch
Einzel.Io          model format, field import, mesh interchange, export
Einzel.Cli         the primary surface
Einzel.Mcp         live-session server
Einzel.Update      release check, download, staging, version policy
Einzel.Wpf         shell, viewport, panels (WPF, Windows-only)
Einzel.Shell       the same views on Avalonia; plain net10.0, runs anywhere
```

CLI, MCP server, and WPF shell are **peers, not a stack** — all three drive the same serializable command objects. **All three now exist**, and the relationship is asserted rather than described: every MCP tool returns `CommandJson.Write` of the same outcome record the CLI serialises for `--json`, compared byte for byte by a test, and every shell action is journalled as the `einzel` invocation that would reproduce it. A shell that shelled out could not drive an interactive viewport at frame rate; a hundred milliseconds of process start per slider drag is not a shell.

**The shell is a named deliverable, and the Windows GUI capability was part of why C# was chosen** — a rationale r06 never records, which is SPEC.md Amendment 25. **That decision was conditional and the condition fired.** Windows-only was the decision rather than an accident of WPF — Avalonia was considered and not chosen because the shell was not planned for use outside Windows, *and that gets revisited if the need appears*. It appeared (a user asked for it), it was revisited, and the bet paid: **`Einzel.Shell` is the same §16 views on Avalonia with an OpenGL viewport, targeting plain `net10.0` and referencing `Einzel.Commands` and nothing else.** 2,167 lines, no assembly below the shell changed, one file ported verbatim — a replacement of a presentation layer rather than a rewrite, exactly as invariant 1 (no UI type below the shell) and Amendment 25's CLI-expressibility were being banked for. SPEC.md Amendment 53. **Four of eleven views are across** (viewport, model tree, field, a run watched while it steps); the other seven are presentation over commands that already work. `Einzel.Shell.Tests` targets plain `net10.0`, so for the first time a shell's own tests run on both CI runners — a cross-platform claim checked only on Windows is one nobody is checking. The WPF shell is untouched and still builds; its assembly is now `einzel-shell-wpf`, because two projects emitting one assembly identity is a latent collision. Which of the two survives is not settled. **The misreading to guard against is still the same one** — "the WPF shell is Windows-only" and "the project is Windows-only" are a few words apart, and the second would undo the Linux CI that made the Avalonia shell cheap to build. What is wanted is interactive geometry, the solved field drawn over it, and animation. §22's scope-creep risk is managed by UI-1's prohibition (the shell owns layout, input, the viewport and the update check, and owns no physics, no validation, no format knowledge and no render output), not by deferring the window.

**The thesis is the pair, and neither half is the product**: an agent drives the entire design process through CLI and MCP, and a human sees and manipulates the same design in a window. Amendment 25 strengthens AGT-2 to make that work — **every shell action should be expressible as a CLI invocation and journalled as one**. The shell still drives command objects in-process; what changes is that its journal is a list of commands somebody could run. A capability with no command spelling then cannot be added to the window, and a human's session hands over to an agent in the same vocabulary. Now that the shell exists, the thing to review is the in-process path acquiring an argument the command form has no spelling for - which is how the amendment gets broken, and it will look like a convenience at the time.

Four invariants. Violating one is a design bug, not a shortcut:

1. **No UI type below the shell.** Nothing may reference `Einzel.Wpf`; every assembly above it builds and runs on Linux. `Einzel.Render` must produce a publication figure headlessly in CI with no display attached.
2. **No device class below `Einzel.Library`.** Quadrupole, funnel, LIT, reflectron exist as data templates in the same schema as any other model. If supporting a new device requires a change lower down, it is almost always the abstraction that is wrong (LIB-1).
3. **No extension code inside the engine loop.** Extensions are coarse-grained — whole input in, whole output out, one call per run (EXT-4). The subprocess boundary makes per-step scripting physically impossible rather than merely discouraged.
4. **No GPL dependency in the default build, ever** (LIC-1). GPL functionality (ffmpeg, Gmsh) is invoked out-of-process as a tool the user supplies; its absence degrades a feature, never blocks the platform. Note RND-13: parsing the `.msh` *format* carries no obligation — linking the *library* would.

## Rules that shape almost every implementation decision

The spec tags requirements (`AGT-`, `GRD-`, `PRJ-`, `EXT-`, `REG-`, `ACC-`, `FLD-`, `RND-`, `UPD-`, `CLI-`, `LIC-`, `TST-`). **Cite the tag** when justifying code against the spec. The load-bearing ones:

- **GRD-1, no bare numbers.** Every quantitative result carries value, units, uncertainty/CI, ensemble size or convergence measure, and active warnings. *The API offers no way to obtain the scalar alone.* The absolutism is deliberate: a convenience accessor returning the value would get added by someone and then used everywhere.
- **GRD-2/3, warnings propagate and are not suppressible** above threshold — through engine, command layer, CLI, MCP, exported files, figures, and video.
- **AGT-2, nothing exists only in the shell.** Every window capability is reachable from CLI and MCP through the same command object. The figure composer edits a text render spec that the CLI executes identically.
- **SI internally, units explicit at every boundary.** `{"energy": 4000}` is a validation error, on purpose — unit ambiguity is the commonest source of silent wrongness and an agent building from prose is the likeliest to introduce it.
- **AGT-3, errors are recovery instructions**: machine-readable code, offending path, violated constraint, observed value, suggested correction, severity.
- **PRJ-3, a run manifest fully determines its run** (model hash, seeds, engine version, solver-behaviour version, transport mode, compute path, extension identities, machine). Results are regenerable rather than precious — which is what makes `.einzel/` safe to discard and version control optional (PRJ-4).
- **Taint, never block.** A defective engine version, a preview-tier result, a decimated figure: all keep working and carry a non-suppressible mark (GRD-5, GRD-11, GRD-12, UPD-10/11). The platform never stops you working; it refuses to let a result look cleaner than it is.
- **AGT-8, the environment is stable within a session.** The CLI never touches the network (UPD-2); only the shell checks for updates, only at launch (UPD-1). An agent issuing 300 commands sees one version throughout.
- **REG-2, regime validity is computed, not assumed.** Trajectory integration and statistical diffusion are peer `ITransportMode` implementations. Selecting one outside its validity raises a non-suppressible warning, and in the overlap band running both and reporting the disagreement is a supported operation.
- **RND-8 / TRN-2, never draw trajectories for diffusive transport.** Above ~10⁻² mbar the model computes a density field and no trajectories exist; lines through a funnel depict something the model never computed.
- **Interpolation, not the integrator, dominates timing error.** Trilinear interpolation is forbidden anywhere on a trajectory path; tricubic with continuous first derivatives is the floor, and grid convergence is a first-class test from the first commit (ACC-3).
- **The inner loop allocates nothing.** Particles are structure-of-arrays in pooled `double[]` buffers so GC cannot interrupt a run, and the scalar reference implementation is never deleted or allowed to rot (CMP-1, TST-1).

## A project is a directory (spec §3)

The unit of work is a folder, not a session or a protocol — read files, edit files, run commands, read output. No network, no handshake:

```
models/ extensions/ studies/ figures/ results/ tests/   small, text, tracked
AGENTS.md    generated platform layer + hand-written project guidance
.einzel/     field caches, trajectories, frames — large, binary, regenerable, ignored
```

The platform layer of `AGENTS.md` is **generated (`einzel agents-md`) and version-stamped, never hand-written** — instructions shipped with v1.2 that describe v1.0 behaviour are worse than none, because an agent trusts them and cannot detect the drift.

## Delivery phases (spec §21)

1. **Spine, project, CLI** — model/units/symmetry, DC multigrid solver, basis superposition, tricubic interpolation, adaptive integrator, JSON schema, error taxonomy, result objects with uncertainty, manifests, full CLI, VTU export. Accepts when the memo's mirror pair is tracked end to end at ACC-1 and an agent builds a DC model from prose with nothing but a project directory and the CLI.
2. **Extensions, sweeps, shell, figures** — both extension runners, examples corpus, sensitivity fields, tolerance Monte Carlo, optimization, ILGPU, WPF shell, `Einzel.Render` vector sections, installer and update mechanism.
3. **RF and pressure** — time-domain RF, statistical diffusion, collision models, gas velocity import, sequencer, space charge, Class B analysis.
4. **Traps, animation, MCP** — waveform excitation, trap sequences, animation with non-linear time mapping, live-session server.
5. **Generalize and release** — BEM solver, MSH interchange, CAD import, public repository.

Sequencing principles: seams first (transport mode, symmetry, accuracy class, device library, extension host stubbed in Phase 1 with one implementation behind each); the schema and CLI are Phase 1 deliverables so the agent thesis is de-risked early; VTU export lands in Phase 1 so ParaView supplies the whole visualization story a year before the shell exists.

## Galerkin coarsening: built, and chosen against the cheaper hierarchy

`A_coarse = R A_fine P` — coarse levels built from the fine operator rather than from
the geometry, so they cannot lose it. **The finest level is untouched**: cut cells and
the geometry-driven smoother stay exactly as they were, because that is where the
accuracy comes from.

Two 1 mm slabs at a 0.25 mm cell: **1 level and a 274,625-node bottom becomes 6 levels
and 27**, 45 cycles becomes 13, 160 s becomes 13 s. **The cycle count stops depending on
the mesh** — 14 at 65³ against 13 at 129³, where it was 6 against 45. And it is the
**same answer**, 1.1e-7 to 4.0e-7 relative, which is what separates it from the fast
wrong one (deeper *rediscretised* coarsening was 30× faster and gave 486 V of 100).

**Neither hierarchy dominates, so the solver picks from the geometry before solving
anything**: 11.9× on the slabs, 4.6× on four rods, **0.64× on a sphere** — a loss, where
the cheap hierarchy already reached a small bottom and the 27-point stencil is overhead.
The criterion is the size of the bottom the cheap hierarchy can reach; the threshold is
20,000 nodes and is measured rather than derived. `SolveReport.Galerkin` says which ran.

Two things that were easy to get wrong and are worth knowing. **A 27-point stencil is
closed under this coarsening** (restriction one cell, operator one, prolongation one =
three fine cells = one coarse), so the hierarchy needs one operator type. And
**`halfH2` stays at the finest level's value all the way down**, because the coarse
operator inherited the fine one's units — recomputing it per level would be wrong by 4×
per level and would still converge, to something else.

**A test that failed on correct code, the right way round.** The first operator check
asserted `R A P` reproduces the rediscretised 7-point Laplacian. That holds in *one*
dimension; in three the transfers are tensor products and `R_b P_b = [1/8, 3/4, 1/8]`,
so the off-axis entries belong there. Deriving what they should be instead pinned every
coefficient against arithmetic the code had no part in — centre 27/64, face −3/128, edge
−5/256, corner −3/512, to 1e-13, row summing to exactly zero.

## A solver limitation to know about

**Measured, and worse than it reads below.** `SolveReport` now carries `Levels`,
`Sweeps` and `CoarsestNodes`, and `einzel solve` prints them. What they say: the 3-D
V-cycle descends **0-2 levels on every device geometry** (4-6 with no interior
electrode), because `Representable` stops at a *physical* cell size — so refinement adds
levels at the top and never removes the bottom. Two 1 mm slabs bottom out at **274,625
nodes at 65³ and still 274,625 at 129³**; the shipped segmented quadrupole bottoms out
at 9,537; the shipped **2-D** templates bottom out at **9-99**, because the two solvers
coarsen by different rules. At a 0.5 mm cell the slabs coarsen *zero* times, so their
"6 cycles at factor 0.015" is 400 relaxation sweeps a cycle over the finest grid — which
is why a 65³ Laplace solve takes 36 seconds. **A cycle is not a unit of work and the
factors below were being compared as though it were.**

**The guard is load-bearing, established by removing it**: letting the 0.25 mm slabs
descend further takes 45 cycles and 145 s down to 5 cycles and 4 s, and gives **486 V of
100 applied**, reported as converged. Only the maximum principle catches it. A plausible
alternative explanation — coarse masks carrying the electrodes' real potentials, so each
cycle injects 100 V — was checked and is wrong: coarse correction fields start at zero
and never have a mask applied. What actually happens is that a 1 mm slab four levels
down is smaller than a cell and gets **pinned to a single node**, so the coarse problem
constrains the error at two points where the fine one constrains it over two planes.
That is precisely what `R A P` fixes. Details in `docs/numerics.md`.


The multigrid V-cycle assumes coarsening preserves the problem. That holds for boundary-only Dirichlet geometries — Stage 3 measured 8→7→7→7 cycles from 32 to 256 intervals — but **not for interior electrodes** such as rods or apertures. An electrode occupies a fixed physical size, so each coarsening halves how many nodes represent it, and past a few levels it is not represented at all; the coarse grid then solves a different problem and its correction, prolonged back, drives the iteration apart. Four discs in a box reached **1e134 V** that way.

Measured convergence factors with interior electrodes degrade with refinement rather than holding steady: 0.028 / 0.061 / 0.141 at 32 / 64 / 128 intervals with a grounded box, and 0.43 at 64 intervals without one. **This is mitigated, not solved.** The shipped templates are sized where it demonstrably converges, `InteriorElectrodeSolveTests` asserts the maximum principle (no potential anywhere may exceed the applied value — the cheapest exact check that a solve has not diverged), and a retention check refuses the clearest dissolving coarsenings. A real fix is Galerkin coarsening or operator-dependent interpolation, and it should happen before anyone solves a large rod geometry.

Two things that did *not* work, so they are not re-tried: agglomerating the mask (fixed if anything in the 3×3 block is) is stable but grows the electrode a cell per level and roughly triples the convergence factor; a flat depth floor stops small grids coarsening at all and cost Stage 3's 32-interval case its multigrid entirely.

## Four numerical rules learned the hard way in Stage 4

Each of these presented as *physics* and turned out to be *numerics*. All four are general, not mirror-specific.

1. **Never declare a discontinuity that is not there.** `SolvedField2D` used to mark its whole domain boundary as a field jump. Where a solve ends in a decayed field there is no jump, and two such phantom surfaces a few microns apart — which is what two abutting solve domains produce — defeat `SuperposedField`'s sign-product tracking: a step crossing both is treated as crossing neither. That cost an ion **2.6e-4 of its energy**, four orders above the ACC-4 budget, and presented as an intermittent transmission loss. Pass `boundaryIsDiscontinuous: false` when the field has decayed at the edge.
2. **A gridded field must cap the step by its own resolution** (`IElectrostaticField.ResolutionLength`). Launch an ion in a field-free region and the local acceleration is ~0, so the step heuristic proposes an enormous step, the embedded error estimate *correctly* agrees it was accurate for a straight line, and the ion sails through both mirrors without sampling them. The step was not inaccurate; it was uninformed.
3. **For a periodic flight, measure one period and multiply — do not stitch legs.** Each leg boundary is a root-find the ion starts exactly on, and 12 of them give 12 chances to miss a crossing and silently return a flight that is 13 half-periods long. Two legs instead of twelve is both more robust and more accurate.
4. **Fixing the drift distance destroys energy focusing.** Stop an MR-TOF at a detector a set distance along the drift and the arrival time is that distance over the drift velocity — dependent only on energy, not on the mirrors. The focusing coefficients say so unmistakably: c1 = −0.500, c2 = 0.3756, c3 = −0.3133 is the Taylor series of 1/√(1+δ), i.e. free flight. Real analyzers fix the *oscillation count*.

## Validation without SIMION

**There is no SIMION licence available** (~$600/yr — its cost is part of why this project exists). Spec §19's cross-code tier is therefore unavailable, and §22's "validation against SIMION takes far longer than budgeted" risk does not apply. Do not plan work against either. What carries the load instead:

- **The analytic tier is the primary reference.** Closed-form fields with exact trajectories: free flight, parallel-plate, ideal single-stage reflectron focusing, Mathieu stability boundaries. Already the sharpest check available and now also the main one.
- **Literature regression is promoted to the main external check.** Published reflectron, MR-TOF, quadrupole, and funnel geometries reproduced against reported performance. These catch conceptual errors that self-consistency cannot.
- **Convergence and cross-mode tiers are unchanged**, and matter more as internal evidence.
- **For the field solver (Stage 3), use a free FEM code out-of-process** — Elmer, FEniCS, or deal.II solving the same Poisson problem — as the independent check on the multigrid solve. Out-of-process comparison, so LIC-1 is untouched.

## Caveats the spec places on itself

Effort estimates, performance targets, regime boundaries, and the numerical error budget are **engineering judgement, not measured values**. Third-party library status, licence terms, MCP SDK capabilities, and CSnakes' Python version support were current at writing and must be re-checked before being committed to. §23 lists decisions still open — treat them as genuinely open rather than inferring an answer from elsewhere in the document. **Two of them are now closed and recorded in `docs/spec-findings.md`:** the FLD-1 linearity spike was run (it failed, then passed once cut cells landed), and the agent acceptance suite has been designed and built — see `docs/agent-acceptance.md` for what it measures and the recommended release gates.
