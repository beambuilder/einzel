# Changelog

What has been built, measured and fixed, stage by stage, with the numbers and the mistakes
that produced them. `CLAUDE.md` imports this file, so every Claude Code session loads it at
startup exactly as though it were written there. **Add new entries here, not to
`CLAUDE.md`.**

**Where the work is:** Stages 0 through 4 are complete.

- **Stage 0** — solution, CI, and the three foundational types in `Einzel.Core` that cannot be retrofitted: `Quantity`/`Dimension` (units), `Measured` (the GRD-1 envelope), `EinzelError` (AGT-3).
- **Stage 1** — `Einzel.Transport`: Dormand–Prince 5(4) with per-step error control, Neumaier-compensated time accumulation, analytic field-free drift, exact landing on stop surfaces and declared field discontinuities, and analytic fields (field-free, uniform, retarding half-space). **ACC-1 is demonstrated at ~1e-10 relative against the closed-form single-stage reflectron, four orders inside the 1 ppm budget**, and first-order energy focusing (total field-free path = 4 × penetration depth) is reproduced.
- **Stage 2** — the vertical slice, end to end: model schema v0.1 (`Einzel.Core/Model`), JSON and VTU (`Einzel.Io`), project layout and run manifests (`Einzel.Project`), command objects (`Einzel.Commands`), and the `einzel` CLI. `einzel init` → edit text → `einzel run --vtu` reproduces the analytic 10.1805 µs, writes a manifest, and emits a ParaView trajectory. Cold start ~80 ms against PERF-8's 500 ms.
- **Stage 3** — `Einzel.Fields`: geometric multigrid on a 2D Cartesian grid (red-black Gauss-Seidel, full-weighting restriction, bilinear prolongation, V-cycles), bicubic C¹ interpolation, basis superposition, and `SolvedField2D`. Observed convergence order **1.996–2.000** against the nominal 2 for a five-point stencil; multigrid cycle count is **grid-independent** (8→7→7→7 from 32 to 256 intervals). The solved mirror reproduces the analytic reflectron to **1.3e-13** — the same 10.180505718 µs by two independent routes.
- **Stage 4** — `Einzel.Analysis` (Class T figures of merit: arrival-time peak, resolving power both model-free and Gaussian, transmission, TOF focusing-order coefficients) and `Einzel.Library` (`MirrorProfile`, `PlanarMirror`, `MirrorPair`). The memo's mirror pair is modelled from a solved printed-circuit geometry, and **memo §6 item 1 is answered**.

  | | separation | c1 | c2 | R at ±3% |
  | --- | --- | --- | --- | --- |
  | Single-stage | 290.4 mm | 4.6e-8 | 0.130 | 8,347 |
  | Two-stage, 35% first stage | 767.0 mm | 4.8e-7 | −0.0028 | 316,681 |

  Three things matter more than the numbers. **The four-penetration-depth rule is wrong by 10 mm** — first-order focus is at 290.4 mm, not 300.0 — because the fringe field shifts it; that gap is what solving the geometry buys over assuming it. **R here is energy-aberration only**: no spatial or angular spread, no turn-around time, no detector response, no space charge. So the two-stage result says energy spread stops being the limiting aberration, *not* that the instrument reaches 320k. And **second-order focus costs envelope**: 767 mm cap-to-cap with 1378 mm of drift against the memo's ~420 × 280 × 160 mm shoebox. Buying the aberration back at this mirror depth makes the analyzer too big, which is exactly the trade an optimiser should be pointed at.

- **Stage 5** — `Einzel.Sweeps`: tolerance Monte Carlo, one-at-a-time attribution, sensitivity ranking, FLD-1 sensitivity fields, and both optimisers §13 asks for.

**The FLD-1 spike §23 asked for was run, failed, and now passes.** It failed for a reason §10 does not anticipate: the *physics* is linear where §10 says, but a rasterised boundary made the *discretisation* a staircase function of electrode position — invisible below one cell (derivative identically zero, a study would report the parameter as having **no influence**) and percent-level above one cell, with no step size in between.

**Fixed by a cut-cell (Shortley–Weller) discretisation.** `CutLinks` stores, per node and per direction, how far a conductor surface is as a fraction of a cell and what potential it holds; `CompiledElectrode.FirstEntry` finds the crossing in closed form. The stencil reduces exactly to the five-point formula where nothing is cut. What it bought:

- A planar boundary swept across a whole cell solves to **3.1e-10** of applied at every offset, against up to 1.6e-2 rasterised.
- Curved boundaries converge at **second order** (coaxial log potential, order 2.00/1.95), which makes §19's coaxial closed-form check possible for the first time.
- The shape derivative matches its closed form to **6.5e-6** at a 0.11-cell step, and the FLD-1 residual is now an ordinary Taylor remainder — quadratic in the perturbation to three figures. The limit is (δ/L)², so 1 ppm holds to δ/L ≈ 10⁻³ and the memo's 100–300 µm channels linearise to ≈1e-5. FLD-2 will correctly refuse them at 1 ppm; that is now a legible trade rather than an artefact.
- **Interior-electrode multigrid works.** The coarse mask is rebuilt from geometry rather than projected down, so an electrode too small to hold a coarse node still cuts the links around it: 7–8 cycles at a factor of 0.019–0.023, flat under refinement, against 43–47 at 0.55 before. `Einzel.Sweeps` tests went from 2 m 15 s to 4 s.

Two defects surfaced underneath it, both now fixed and recorded in `docs/lessons.md`. A **Dirichlet domain edge** was a ghost node one cell outside the grid, so the boundary moved outward at every coarsening and a cap plate in a grounded box diverged to 1e50 V once deep coarsening was allowed; `GeometryBuilder` now grounds the edge node itself. And **`Grid2D.OverBox` solved a domain up to 50% taller than declared** — a 60×20 mm box at a 1 mm cell became 60×30 mm, silently — because the y interval count rounded up to a power of two while the x spacing was kept.

**Grids now carry independent spacings per axis** (`SpacingX`/`SpacingY`). Each axis rounds its own interval count up to a power of two from the same requested cell size, so the domain is meshed exactly, neither direction is ever coarser than asked, and the worst cell aspect ratio is 2:1. The stencil scales its y half by (hx/hy)² — exactly 1.0 on a square grid, so isotropic solves are bit-identical. Verified at **observed order 2.00, 2.00, 2.00** on a deliberately 2:1 grid, which is the test that matters: a wrong aspect factor is a wrong Laplacian and it converges contentedly to the wrong answer.

**The optimiser is built** — Nelder–Mead and CMA-ES over the declared parameter surface, behind one `Optimiser.Run`. Everything happens in a normalised box, so a length, a voltage, and a dimensionless ratio all take steps of comparable size; out-of-box candidates are repaired and penalised rather than refused; a failed design is a large *finite* number rather than an infinity, because a simplex reflecting onto infinity learns nothing about which way to go. GRD-1 applies: the optimum is one `Measured` per variable whose interval is the spread of the final simplex or population — a convergence measure saying how *sharply* the optimum is defined, not a confidence interval. New `Evidence.Search(Evaluations, Converged, SpreadSi)` carries it. Three non-suppressible warnings, of which `optimiser.optimum-at-bound` is the one that matters most: an optimum stopped by the box looks identical to a real one.

**The first literature regression that passes.** `Optimiser` recovers the classical round-rod quadrupole ratio — **r/r₀ = 1.14148 ± 3.05e-6** in 45 evaluations against Denison's published 1.1468 — by minimising the 12-pole fraction A₆/A₂, cancelling it 880-fold from nominal. Stable to 0.0016 across sampling radii, so it is a property of the field rather than of the measurement. **It is only measurable because the rod surfaces are cut cells**: a rasterised circle is a staircase and a staircase radiates into exactly the multipoles being measured. The remaining 0.46% is **discretisation**, established by refinement rather than assumed — 1.14148 / 1.14426 / 1.14487 at 16 / 32 / 64 cells per r₀, second order, extrapolating to ≈1.1451, with the grounded housing worth the other ≈0.002. The first guess (that the housing was the whole story) was wrong. Details in `docs/optimisation.md` and `docs/literature-targets.md`.

**Still to point the optimiser at:** the mirror-pair second-order focus. c₂ changes sign across the scan, so the root is bracketed, but each evaluation is ~23 s and a search would run minutes — worth doing as a study rather than a test.

- **A code review found the 3D CLI verbs were blind, and GRD-2 had a hole at the field seam.** Three things worth keeping:

  **`einzel solve` answered `converged: true`, exit 0, for models it never touched.** It read only `Solve` (2D) and `continue`d past every `Solve3D` element, and `Elements.All(e => e.Converged)` is vacuously true over an empty list. So the verb whose entire job is to report a residual gave a clean bill of health to a field it had skipped — the shape of answer that stops an investigation, which makes it worse than a failure. `solve` now reports **one entry per basis channel** (a driven structure is one solve per spatial pattern, not one per element), names `dimensions` and `channel`, and refuses a model with nothing to solve. `export` writes a **3D `.vti` volume**, one file per channel, because a single file called "the field" would be picking an RF phase without saying which.

  **`FieldAssembly.Build` discarded the `SolveReport` at the one seam every run, study and test passes through** — `elements.Add(BuildField(geometry).Field)`, one character shorter than handling it. A field that stopped short of its tolerance is indistinguishable from one that met it, which is precisely how the q = 0.611 result survived a revision. `BuildReported` now returns the field **and** its warnings; `run` and `preview` carry them onto every number; the bare `Build` **throws** rather than concealing, because a plain field has no envelope to taint and no third option between refusing and hiding. The rule that generalises: **when a computation produces evidence about its own quality, discarding that evidence must not be the shortest spelling.**

  **The coarsening guard asked the wrong spacing.** `Representable` tested `MinimumSpacing` — the *finest* of three axes — against the smallest electrode. Since each axis rounds its own interval count up to a power of two, a 2:1 aspect is ordinary, and the shipped segmented quadrupole descended to a level whose **z cell was 4.875 mm against a 4.587 mm rod radius**: the exact condition the guard exists to refuse, passed because another axis was still fine. Now `MaximumSpacing`. The test written for it first asserted the maximum principle and **passed with the bug restored** — no teeth. What actually discriminates is the convergence factor, 0.213 against 0.303, and only trying it both ways revealed that.

  Also fixed: coarse masks are now **memoised by grid** (the twelve-rod quadrupole was rebuilding an identical mask over a million `Contains` calls per cycle); the coarse-level flag became `GeometryBuilder3D.Coarsener(...)`, a whole function rather than a `bool coarse = false` default whose obvious spelling was the harmful one; a coarse pin can no longer overwrite a grounded face or collide with another electrode's; and `Electrode3D`'s shape switches were **disagreeing about their default arms** — size fell through to a box, centre to a sphere — so a fourth shape would have been sized as one thing and centred as another with no diagnostic. All three named cases now, and a throw for the rest.

- **Statistical diffusion — the second transport mode, and REG-3 measured.** A drift-diffusion solve on a grid, with **mobility as a declared input** (TRN-1) and a **density field as the output** (TRN-2). RND-8's rule that a diffusive region must never be drawn as trajectories now has something to ask: `ITransportMode.ProducesTrajectories`.

  **Scharfetter–Gummel fluxes, for the same reason cut cells mattered in the field solver.** Centred differencing here is not merely less accurate — it oscillates and produces negative densities as soon as drift outruns diffusion, which in a funnel is everywhere, and a negative density is a quantity that has stopped meaning anything.

  | | |
  | --- | --- |
  | Free diffusion vs √(2Dt), three times | **1.0000** each |
  | Drift vs μE | **1.00000** |
  | Boltzmann equilibrium, seeded and evolved | **1.00000** over three decades |
  | Ion conservation, every loss named | 100.0000% |
  | Lowest density at cell Péclet 483 | non-negative |

  **The Boltzmann check is sharp because the scheme is *built* to be exact there**: zero flux gives n₂/n₁ = B(−P)/B(P) = exp(P), and P is precisely qΔφ/kT. The discrete equilibrium *is* the continuous one, so anything worse than a per cent is a bug — and it found one. A first version sampled the drift at the **cell centre** and used it for both faces; where the field varies, two cells sharing a face then disagree about how much crossed it and the scheme stops conserving. A seeded equilibrium drained from the middle at 4.7× per millisecond. **The conservation test passed throughout, because its field was uniform** — a test that passed for a reason that did not generalise.

  **REG-3, measured.** For the comparison to mean anything both modes must describe the *same gas*, so the event-driven side scatters off a declared cross section and the diffusive side takes its mobility from that same cross section via Mason–Schamp. At 1e-2 mbar and 6.2 Td: trajectory **13.2555 ± 1.3584 m/s**, diffusion **13.8418 m/s** — **0.43 standard errors**, between machineries that share nothing but a cross section.

  **And where they legitimately disagree.** The field has to be chosen against E/N, not picked: at 1e-2 mbar the mobility is 9.2 m²/Vs, so 40 V/m is **166 townsend**, deep into field heating. There the trajectory mode gives 265.2 ± 4.1 m/s against a low-field μE of 369.4 — **overstated 1.393×**. The event-driven mode gets it right without being told; the diffusive mode is only as good as the mobility handed to it, which is exactly what TRN-1's "stated field dependence" is for, and `Mobility.IsWithinFit` returns false rather than leaving the caller to work it out.

  **`"mode": "diffusion"` now runs from a model document, and `einzel compare` is REG-3's supported operation.** A source becomes a Gaussian initial density normalised to the declared population, a detector a collecting boundary, an electrode a region the seed is emptied from. Checked against a closed form: μE at 1 mbar is 184.7 m/s, so 38 mm takes **206 µs**, and the run reports **206.6 µs**. A diffusive result has **no flight time** and says so (`transport.no-flight-time`) rather than filling one in — what a density has instead is a transit *distribution*, a transmission, and a spread. Schema **0.4** carries `mobility` and `densityGrid`.

  **Building `compare` surfaced three ways the comparison can be meaningless, none obvious beforehand, all now warned about.** They are the useful output of the increment:
  - **Mismatched mechanism.** Langevin capture on the event-driven side against a Mason–Schamp mobility derived from a *cross section* is rigid-sphere versus polarization — different scattering, so the disagreement is between the two **inputs**.
  - **Incomplete arrival.** A mean transit over the subset that arrived is not a transit time, and the two subsets are not the same ions. At 6.2 Td the drift was so weak that 0% of the density and 70% of the ions arrived, and the modes "disagreed" by 145% — almost all of it the flight-time ceiling.
  - **Unmatched boundaries.** The density grid has edges and a bare trajectory model does not: with no declared geometry an ion flies to the detector however far it wanders, while the density is absorbed at the edge of its box — **89%** of it, in the case that exposed this. The two modes were being asked about *different instruments*, one with walls and one without.

  With those understood, a 1e-2 mbar comparison at 82.8 Td — trajectory 252.1 ± 6.8 µs against diffusion 174.1 µs, 1.45× apart — is not the solvers disagreeing. It is the low-field mobility being outside its fitted range, which `mobility.outside-fit` says on the same report.

  **RND-8 is now enforced rather than stated**: the renderer asks the mode whether it produces trajectories and draws none when it does not, with `render.no-trajectories` on the figure. And a successful diffusive run no longer exits `ConvergenceFailure` — the exit logic only knew `StopConditionMet`.

  **A diffusive run's cost is now known before it starts, exactly.** GRD-8 gates on a number available without doing the work, and for this mode that number is not modelled: the step is set by two stability limits computable from the mesh, the mobility and the field. Predicted **901 steps against 901 actual** (drift-limited, 1 mbar) and **6,266 against 6,266** (diffusion-limited, 1e-2 mbar), because `estimate` and `run` call the *same function* — an estimate computed by a second implementation of the step rule is an estimate of that implementation. Wall time is conservative by ~20% from a measured 2.4 Mcell/s. The drift limit needs the field, so it is included where every element is analytic (free to sample) and omitted with a stated caveat where anything must be solved. **A thinner gas costs more**: a hundredth of the pressure on a six times coarser grid is still twice the work, which the basis line says in words because a number that surprises without explaining itself gets worked around rather than understood.

  **Not built:** an electrode that keeps absorbing rather than only emptying the seed. Details in `docs/pressure.md`.

- **`Einzel.Extensions` — a Python extension surface, and invariant 3 goes from constraint to mechanism.** §5's argument is that agents must extend the platform, not only drive it, and an extension path requiring C#, a compile and a restart is not usable in a loop. `einzel ext register | test | list` is that loop; `register` scaffolds an extension that **runs immediately**, for the same reason `init` writes a model that runs.

  **One manifest, two runners — and only the sandboxed one is built, which is the right one to build first.** `trust` defaults to `sandboxed` rather than being opted into, because a trust level that has to be asked for gets granted by accident. EXT-4 is structural rather than advisory: a subprocess cannot be invoked per integration step at any useful rate, and **the round trip is process start almost in its entirety**. Measured: a bare `python -I -c pass` costs 45-63 ms on this machine, and PERF-7's whole 50 ms budget straddles that spread - so the budget is not separable from CPython's own start cost, and asserting it measures the machine. What the platform controls is **1.08x to 1.52x a bare launch, once below it**. That *strengthens* EXT-4 rather than weakening it: the boundary costs ~50 ms and nothing here can reduce it. SPEC.md Amendment 27, found when the old assertion passed and failed on the same commit in two CI runs minutes apart.

  **The sandbox states what it does not do, and that is the load-bearing decision.** Enforced: wall-clock timeout with process-tree kill, output ceiling, **zero inherited environment variables** (asserted — a child inheriting the parent's environment inherits its credentials and proxy settings), `python -I` isolation, a scratch working directory. **Not enforced: network, filesystem, memory** — those need job objects and a restricted token on Windows, namespaces and seccomp on Linux. `extension.isolation-incomplete` is a **non-suppressible validity violation** on every sandboxed result, and `ext list` prints the gap on stderr every time. A containment measure claimed and not applied is worse than one absent and known to be: the first makes someone run untrusted code they would otherwise have read.

  **EXT-7's schema check is a deliberate subset** — type, required, properties, items, enum, numeric bounds — because a full JSON Schema dependency would put remote `$ref` resolution inside a sandbox whose entire point is having no network. Unrecognised keywords are ignored rather than refused. **The extension's own traceback reaches the caller** (AGT-3), because "the extension failed" is the message that stops people writing a second one.

  **§13's sentence is now true**: a study naming `ext:name` gets a Python objective the optimiser drives without knowing the difference. A prefix rather than a new field, since a figure of merit is already selected by name. The extension is handed the model's parameters in SI plus whichever built-in figures its manifest **declares** — declared rather than inferred, because each ensemble figure flies a cloud and computing all of them for every draw of a thousand-draw study would spend the study on numbers nobody asked for. A figure that could not be computed is `null` rather than absent, so "lost the beam" is distinguishable from "did not ask".

  **The manifest records which interpreter ran an extension** (PRJ-3), and `null` when none did — an interpreter that took no part in a run is not provenance, and recording it would imply it mattered. Studies also write a manifest now: they wrote results and **no manifest at all**, which is GRD-7 missing rather than thin, and a sweep is exactly the operation whose thousand draws are worth being able to regenerate.

  **Not built:** the in-process CSnakes runner (checked as promised — 1.2.1, ~450k downloads, alive; its licence is unchecked against LIC-1 because nothing depends on it yet), a **vendored interpreter** (EXT-6 — one is *discovered*, and `doctor` says so rather than passing it off), shared-memory transport for large arrays (EXT-5), OS-level isolation, and the geometry/sequence/interchange extension points. Details in `docs/extensions.md`.

- **Pressure: ions fly through gas, and REG-2 is enforced rather than documented.** Two event-driven collision models — **hard sphere** below ~1e-5 mbar (residual-gas scattering, the arrival-time pedestal) and **Langevin** from there to ~1e-2 mbar (trap and guide damping, thermalization). Schema **0.4** adds a `gas` block to `transport`; absent means vacuum, and a vacuum flight is **bit-identical** with and without a sampler attached, asserted rather than assumed.

  **Three literature-grade checks, none of which this code produced.** The Langevin rate coefficient against its closed form (5.998e-10 cm³/s for m/z 500 in N₂, where every published value clusters); the scheduled collision rate against n·σ·⟨g⟩ at **0.9995** over 12,076 collisions; **thermalization to (3/2)kT at 0.9524** after 288 collisions — the sharpest, because equipartition is exact and is not something the code knows, and it tests the kinematics, the Maxwellian draw and the isotropy at once; and **Mason–Schamp low-field mobility at 1.0127 ± 0.0373**, a 0.34σ discrepancy, where the ions get their drift by colliding and the closed form moves nothing.

  **That last test taught a lesson about its own precision.** A first version used 40 ions and reported 0.935, which reads as a 6.5% discrepancy. It was **one and a half standard errors**: a drifting ion also diffuses, and over that flight the diffusion length is comparable to the drift, so a single ion carries ~45% of spread. It now computes its own standard error and asserts against that rather than a band chosen to fit.

  **The Langevin rate does not contain the speed** — the capture cross-section goes as 1/v and the rate is the product — so it is a plain exponential draw, while hard spheres need the **null-collision method** with a bound five thermal speeds above the true rate. A bound that is ever exceeded biases the rate low, so it is *reported* rather than hidden.

  **Two integrator changes a gas forced.** A collision is an instant, so it lands like a sequencer switch — the time is known, the step is cut to it, no root-find. And **the analytic field-free drift had to be bounded by it rather than disabled**: a drift jumps over every scheduled collision, and that is precisely the path a long flight in a thin gas takes. Bounding is *correct*, not convenient — between two collisions the motion really is a straight line, so the analytic advance is the exact solution over a shorter interval. Separately, **the turning-point step cap is fatal in a gas for the same reason it is fatal in a driven field**: an ion thermalising sits at a velocity minimum permanently, and an ion drifting in 1 mbar underflowed after eight steps and 32 ns of a 300 µs flight, reporting `StepSizeUnderflow` — a numerical failure standing in for ordinary physics.

  **REG-1's seam now exists** (`ITransportMode`), with `DiffusiveTransport` declared, `IsAvailable = false`, and refused by name with what it would need. It was only ever named in a csproj description before. `ProducesTrajectories` is on the interface so a renderer *asks* rather than inferring from pressure, which is what TRN-2/RND-8 need to be checkable. **REG-2** computes Knudsen, mean free path, collisions per flight and collisions per RF cycle on every run and reports them **whether or not anything crosses a threshold** — a reader who sees Kn = 40 knows the run was checked; one who sees nothing cannot tell that from its not having been checked. Six non-suppressible warnings, and a **regime violation gets its own exit code** (2, not 4): an ion that never arrives at 1 mbar has not failed to converge, it has been described by the wrong physics.

  **The funnel, finally with gas.** An ion entering 6 mm off axis: vacuum and 1e-3 mbar are **bit-identical (0 collisions)**; at 1e-1 mbar, 1455 collisions take radial speed from **864.6 to 255.7 m/s** and exit radius from 1.59 to 0.42 mm. That is a funnel doing what a funnel is for, and it did not exist before. Two awkward things it also shows: the damping only appears **at or above the validity boundary of the mode computing it**, and at 1e-1 mbar the ion is damped axially too and never leaves — a real funnel is pushed through by a gas *flow*, and a stationary gas has no such push. At the 2 mbar a funnel actually runs at: mean free path **21.4 µm**, Kn **0.0143**, **29 collisions per RF cycle** — an ion that never completes an oscillation, so the pseudopotential the device is designed around does not exist for it.

  **Not built:** statistical diffusion (the density-field mode above 1e-2 mbar, which is why every funnel number is still a lower bound), a neutral velocity *field*, inelastic channels, and pressure gradients. Details in `docs/pressure.md`.

- **`Einzel.Render` — vector sections, and invariant 1's hardest test now passes.** RND-1 makes rendering an engine capability rather than a shell feature, so `einzel render section` and a future figure composer are peer consumers of one pipeline. `Einzel.Render.Tests` draws real templates as SVG and PDF on the Linux runner, where there is **no display, no window manager and no font server** — there is no drawing surface anywhere in the pipeline and no font measurement, because a scene is a list of paths and text runs.

  **A conductor is drawn as the zero level set of its own signed distance**, which the model format already requires for the solver and the ion absorber. One marching-squares routine draws every conductor there is, and the *same* routine draws equipotentials, because an equipotential is a level set too. A shape added to the format needs no change in this assembly — invariant 2 in a new place.

  **Decimation is a guarantee, not a hint** (RND-5, ACC-7 at 0.1% of extent, recorded per GRD-12 in the file, the `--json` result, and stamped on the page). Ramer–Douglas–Peucker, measured tight against its bound: 4,000 points → 577 at a worst deviation of 0.010000 mm against 0.01. The point-to-segment distance is **clamped**, and a reflectron is why — an ion that turns round has its turning point almost on the chord between the ends of the flight, so measuring to the *infinite line* decimates the reflection away and draws an ion flying straight through a mirror.

  **Sample finely, then decimate.** The trajectory is flown twice — once at the model's cadence to learn the flight time, then at a cadence chosen from that — because drawing whatever the model samples for VTU gave the einzel lens a three-segment curve through a focusing element. That a ray *still* decimates to three points is the right answer: straight in, kink, straight out is what a thin lens does.

  **A tainted figure is visibly tainted** (RND-11/GRD-5): a hatched rule the width of the page and a `QUALIFIED` line naming the code, because a figure is the artifact most likely to be shown with none of the uncertainty apparatus attached. **Text stays text** in both formats (RND-6), so a figure can be relabelled for another venue without regenerating it.

  **The PDF writer is hand-written, and LIC-1 is the reason** as much as weight: the capable .NET PDF libraries are variously GPL, AGPL or dual-licensed in a way that needs re-checking per release, and a figure writer is not where this project wants a licence question. Base-14 Helvetica, nothing embedded; a test walks every cross-reference offset and asserts it lands on the object it names.

  **What marching squares got wrong first:** cases 1 and 14 both emit `(Left, Bottom)`, so segment orientation is *not* consistent and a head-to-tail join breaks the contour at every complementary crossing. A rectangular conductor came out as four runs instead of one — identical on screen until the path is filled or dashed, and not free: joining undirected took the lens from 10 conductor runs to 6, the quadrupole's equipotentials from 338 paths to 28, and its PDF from **112 KB to 13 KB** for the same drawing.

  **Not built at the time:** `render still` (raster — nothing rasterized yet; since built, see the viewport-still entry below) and `render animation` (needs RND-7's explicit non-linear time mapping, displayed throughout playback). Both are named by the CLI and refused with a reason rather than falling through as unknown verbs, because "not built yet" and "you spelled it wrong" are different problems. Dimensioned callouts now exist - see below. Details in `docs/rendering.md`.

- **The CLI is the primary surface and now has most of §15.** `init | new | validate | estimate | preview | solve | run | compare | sweep | scan | boundary | optimise | test | verify | project | outline | export | render | ext | schema | templates | examples | agents-md | doctor`, plus the CLI-1..6 contract: `--json` on every verb, results on stdout and diagnostics on stderr, `--dry-run` on every mutating command, distinct exit codes per failure class, deterministic ordering. Cold start 73–147 ms against PERF-8's 500 ms. **Not built: `self-update`**, which needs `Einzel.Update`. `render section`, `render animation` and `ext list|test|register` now exist; `render still` and the in-process extension runner do not.

  Four things here are load-bearing rather than plumbing. **`einzel schema` is generated by reflection** over the document records with descriptions from the XML doc comments the build already requires (AGT-7), so the format an agent reads cannot drift from the code; missing XML degrades to structure-without-descriptions and *says so*. **A study is a file** (`schema --study`) naming a figure of merit out of a registry — `flightTime`, `energyDrift`, `resolvingPower`, `transmission` — which is the seam §12's Python objectives will register into. **`verify` separates drift from notes**: an edited model or a changed solver-behaviour version invalidates; a different engine build with identical numerics, or another machine, does not. **`preview` taints the number itself** (GRD-5) and writes nothing, because a tainted result in `results/` would be reported as current by `verify`.

  `einzel init` now scaffolds a schema-0.2 parameterised reflectron *and a test for it*, so `init` → `test` works from the first minute and the expected value is a closed form rather than something this engine produced once.

- **Ion clouds, and with them Class S.** A source may declare a cloud — ion count, seed, temperature, transverse and longitudinal width, energy spread — and every figure of merit computed over it becomes a property of the *instrument* rather than of one ion. That removes three of the four caveats every resolving power here carried. **Turn-around time** is the sharpest check: FWHM = 2√(2ln2)√(mkT)/qE, matched to **0.49%** on 4000 ions, 0.54/0.87/2.04 ns at m/z 195/500/2722. Schema **0.3**; every spread defaults to zero so nothing existing changes. No angular-divergence knob on purpose — a thermal cloud already has one, and offering both lets a document say two things about the same physics.

  Two things the measurement taught rather than confirmed. **`run` reports two peak widths, both named**, because the model-free central half and the Gaussian-equivalent FWHM disagree whenever the peak has a tail (skew +3.27 on the shipped reflectron), and printing one beside a resolving power computed from the other invites the wrong reconciliation. And **a first-order energy focus suppresses thermal spread quadratically** — sixteen times the temperature gives sixteen times the width, not four, because the first-order term is cancelled by construction. A test written expecting √T was wrong and the measurement corrected it.

  **An open question raised by being able to compute it:** the Ion Processor paper reports Δt* roughly constant at 0.8–1.2 ns across m/z 195–2722, while thermal turn-around goes as √m and would spread 3.7×. Recorded in `docs/literature-targets.md` as a question to settle against the source, not a discrepancy to claim.

- **The RF path exists and the stability diagram is recovered**, which §19 calls "the best single test that the RF path is correct". Time threads through every Dormand–Prince stage (each at its own c coefficient); `ITimeVaryingField` is separate from `IElectrostaticField` because a static field conserves energy and can be sampled in any order and a driven one does neither. **Three literature numbers, none from this code:** sinusoidal cut-off **q = 0.90684** vs a tabulated 0.90804; square-wave **q = 0.71113** vs a published 0.712 (Schrader/Anderson/Russell, JASMS 2024); and a digital working point **a = 0.2630** vs their 0.2640 at 61.15/38.85 duty. The a–q diagram's shape comes out too, closing toward the apex at a = 0.237, q = 0.706.

  RF cost far less than expected because **basis superposition already was the mechanism** — the field is linear in the applied potentials, so making the weights functions of time *is* RF with nothing re-solved. Two things it broke, both instructive. **The turning-point step cap (§11) is fatal in a driven field**: an oscillating ion is at a velocity minimum twice per cycle, so the cap fires continuously — nineteen steps and a quarter of a cycle before underflow. It is disabled for driven fields, which strengthens the existing finding that the cap "does not help and slightly hurts". And **energy drift stops being a diagnostic** (a driven field does work deliberately), so it reports NaN rather than a number that looks like a diagnostic and means nothing; refinement replaces it. A static field is bit-unchanged, asserted not assumed.

  **Not yet:** no arbitrary-waveform library — a drive is a sinusoid or a rectangular wave, and §9 lists an arbitrary waveform as one of the excitations an electrode may carry.

- **Emittance, which completes the Class T figures §12 asks for.** The phase-space area a packet occupies — position against divergence — and the quantity that says whether it will fit through the next aperture, since optics trade size against divergence and cannot reduce the product. Reported in both transverse planes about the packet's own mean velocity (so a mirror or a deflector needs no special handling), with the Twiss orientation that says which side of the waist the detector is on. Checked against σ_x·√(kT/m)/v to **0.77%** on 6,000 ions.

  **It is also an integrator check, and a sharper one than expected.** Liouville's theorem says a conservative force cannot change phase-space area, and this is a conserved quantity *independent of energy* — energy conservation is blind to a map that shears phase space. A field-free drift preserves it to **1.5e-14**, an ideal thin lens to **8.1e-15**, and the same lens with a cubic term grows it by 1.58×, which is the control that makes the other two mean something.

  **Two findings came out of insisting the normalised form be exactly invariant**, both in `docs/lessons.md`. Written the conventional way — geometric emittance times βγ — it drifted **8.1e-7** across an accelerating stage, because a divergence is measured against *axial* speed while βγ is built from *total* speed and the paraxial term between them shrinks as the beam damps. Measuring the area against transverse momentum directly fixes that. The residual **2.1e-8** was then exactly γ−1 at the exit speed: the analysis was relativistic while transport is Newtonian, so the two disagreed about whether v_y or γv_y is conserved. Dropping γ took it to **3.0e-16**. What that gives up is bounded and remote — γ−1 reaches 1 ppm near 460 keV for m/z 500 — but if a relativistic transport mode is ever added the term must come back **in both places at once**.

  A third, smaller one: a cloud with spatial spread and no temperature is perfectly parallel, so its emittance is exactly zero and its Twiss α is *undefined*. That reached the serialiser as NaN and took the whole `--json` document down — the second time that exact failure has happened here. The fix that generalises is that an undefined measurement is **absent, not zero**, because zero is a real answer and a reader cannot tell the two apart if both print as zero.

- **Space charge: the screen, and the distinction it forced.** `ions` (how many trajectories to compute — numerical) and `population` (how many ions are physically in the packet — what pushes on itself) are now separate fields; `population` defaults to `ions`, the conservative reading, so a dense packet is never silently sparse. A run reports the flight-time error the packet's own charge implies whether or not it crosses a threshold, warns **non-suppressibly** past ACC-1's 1 ppm, and names the population the packet could hold within budget.

  **The screen said a 0.5 mm packet at 4 kV holds ~5,600 ions within 1 ppm. It holds about ten**, and finding that is what building the reference method bought.

  The old conversion was `timingFraction = ½ × selfPotential / beamEnergy` — a fractional energy spread, halved because time goes as the inverse square root of energy. That is a real mechanism (ions extracted from different depths of the self-potential well leave with different energies) and it is **not the one that dominates in flight**. What dominates is that the packet *expands*: the self-field pushes for the whole drift, and the relative speed it imparts comes from converting the self-potential into **relative** kinetic energy, √(2qφ/m), not from perturbing a beam energy thousands of times larger. For 40,000 ions in a 0.5 mm ball at 4 kV those are 149 m/s against a beam speed of 39,291 m/s — 3.8e-3, where the old formula said 7.2e-6. **A factor of 527, in the unsafe direction, in a number documented as an upper bound.**

  The estimate now takes the flight time and reports `min(a₀T, √(2qφ/m))/v`, where `a₀ = 2qφ/(mR)` is the surface acceleration: linear while the packet has not had time to expand, saturating at the escape value after. Both branches are needed — the escape value alone would report two ions half a millimetre apart as catastrophic, and eventually is 200 times the flight. `PopulationLimit` inverts it, and the inversion takes the **maximum** of the two branches rather than the minimum, because `min(a,b)` is within budget as soon as *either* is; taking the minimum looked symmetric with the forward direction and reported a limit of three thousandths of an ion.

  Two caveats stated rather than buried. The uniform-sphere model is **harsh for a real bunch**, which is thin in one direction and much less dense than a ball of the same extent — the correction is to the *conversion*, not to the geometry model, and the geometry model was always an order-of-magnitude screen. And a first-order energy focus partly undoes it, measurably: see below.

- **Space charge is now modelled, not only screened** — `Einzel.Transport.Interaction`, the direct pairwise sum SC-1 names as the reference an approximate method is validated against. Built first because an approximation cannot be validated against something that does not exist, and useful in its own right: a pulsed-extraction packet is thousands of ions, not the 10²⁰ that particle-in-cell exists for.

  `CoulombInteraction` sums every pair with macroparticle weighting — a packet of 10,000 modelled by 500 trajectories gives each 20 ions' worth of charge *and* mass, so charge-to-mass is unchanged and motion in the applied field is bit-identical; the weight touches only the mutual force. Plummer softening at the mean macroparticle spacing, **reported rather than hidden**, because the force between two macroparticles closer than that is deliberately not the Coulomb force.

  `PacketIntegrator` inverts the loop: the ordinary path flies ion 1 to its detector before ion 2 is launched, so there is no instant at which both exist and it is *structurally* incapable of space charge. The step is **shared and has to be** — a mutual force between ions at different times is not a force between anything — and Dormand–Prince is evaluated a stage at a time across the packet, because the mutual part of stage 3's field depends on where the others are at stage 3. Written beside the single-ion path rather than by generalising it, the same choice the 3-D solver made and for the same reason.

  What it gives up, stated: it **cannot land exactly on a declared field discontinuity**, because a shared step cannot land on a surface each macroparticle reaches at its own instant. It caps the step short of the first arrival instead — the honest weaker guarantee, and why this is the reference method for space charge rather than a replacement for the path that carries ACC-1.

  | Check | Result |
  | --- | --- |
  | Newton's third law, every stage of every step | balanced to 1e-14 of the acceleration scale |
  | Direct sum vs the uniform-sphere closed form | within 5% at 4,000 points (sampling noise, 1/√N) |
  | Two ions from rest vs energy conservation | 1e-6 |
  | No interaction vs the analytic reflectron | 1e-6 (linear landing, no analytic drift) |
  | Free-flight widening vs the corrected screen | within a factor of 3, screen bounding |

  **The momentum invariant had to be rewritten before it meant anything.** Total packet momentum is conserved only in free flight with nothing absorbed: an applied field is an external force and a detector removes momentum with the ion carrying it. Asserting on it was asserting that mirrors do not reflect. What is checked instead is the *interaction's* own balance at every stage — exactly true whatever the applied field is doing, and where an indexing error over absorbed members would show.

  **And a result that looked like a sign error and is not: a reflectron at first-order focus makes space charge better.** The mutual push correlates position with energy — the ion at the front is accelerated along its travel, the one at the back retarded — and a leading, faster ion penetrates the mirror deeper and spends longer in it. That is the compensation a reflectron exists to provide, applied to a spread the packet's own charge created. Measured at 24.9 ns free against 11.3 ns pushed. A plausible story is not a finding, so it is **tested by detuning**: move the drift length off the focusing condition and the compensation has to weaken, which it does. An integrator with a sign wrong would narrow the packet at every drift length.

  **Wired to the model format as schema 0.5**: `"transport": { "spaceCharge": "direct" }`. A string rather than a flag, because particle-in-cell will be a third value. **No new field was needed for the weighting** — `ions` (trajectories computed) and `population` (ions physically present) already meant exactly macroparticles and real ions, and two fields that must agree would have been one too many.

  Two ways to ask for it and not get it are **refused rather than run**, because each would produce a result that looks like the one asked for: fewer than two trajectories (nobody to push on) and a cloud with no spatial spread (an unbounded self-field, not a large one). A declared gas was a third refusal - the packet advanced in lockstep and had no collision hook - and is now a capability: **one sampler per macroparticle, the shared step cut to land on the earliest collision anywhere in the packet**, so cooling and the mutual push act on one packet in one run. Checked against equipartition (0.930 of (3/2)kT on 200 members, against 0.952 on the single-ion path) and against a vacuum control (a pushed ball expands 0.16 → 28.7 mm in vacuum and 8.1 mm in 10 Pa of nitrogen). The caveat is reported on every such run: a macroparticle scatters as one ion and carries many, so the fluctuations are those of the macroparticle count. And **`einzel estimate` states the cost in words as well as in a number** (GRD-8) — 150 trajectories through the shipped trap take 87 s and 2,000 would take about four hours, so the linear intuition is exactly wrong and saying "quadratic" is worth more than the figure.

  Two things the wiring caught. **Turn-around time inherited the setting and came back as 0.000 ns**: it works by flying a thermal-only cloud with every other spread switched off, which has no spatial extent by construction — so its self-field is unbounded, the packet blew up, too few ions arrived, and the catch returned zero. It reads exactly like a measurement. The sub-model now forces `none`, which is also the physically right answer: turn-around is the temperature's contribution alone. And **a 20 mm test drift showed a 0.2% change**, which would have passed on a build where the interaction was never wired up — a switch that runs different code and produces the same number is not a feature. The test drifts half a metre.

  **Still to build:** particle-in-cell as the *approximate* method — deposit to a grid, one multigrid solve with a non-zero source, gather back. The existing multigrid is the right machine, and now there is something to validate it against.

- **The agent acceptance suite exists** (`docs/agent-acceptance.md`), closing the second of §23's two Phase-1 open questions. Six prose tasks across two tracks — four on capability, two on whether a warning is *acted on*. `einzel agents tasks | setup | score`. Two design decisions worth keeping: it **scores actions, not self-reports** (a quotable number means a manifest exists, because a preview leaves none), and **every task ships plausible wrong answers** that CI asserts must fail, because a check that only passes the right answer is testing that a file exists. Recommended gates: 80% capability, 90% warnings, any task at 0% blocks, and any drop against the previous release blocks regardless of level. The regime-invalid traps §19 asks for need Phase 3.

  **It has now been run: six fresh agents, one attempt each, 6 of 6 on every individual check.** One attempt is not a rate, so the gates are not yet meetable and this is not a pass rate — read it as "the suite runs end to end and the platform can do these six things". **The score was not the point.** Roughly twenty defects came out of six transcripts, several of a kind no test written from inside the project would catch, because they are about what is *discoverable* rather than what is correct. The four that mattered:

  - **`einzel test` passed with zero tests**, and `einzel solve` reported converged over a model with nothing to solve. Both vacuous truths over an empty collection; both now exit 1 and say what is missing.
  - **A source inside an electrode validated and solved cleanly**, failing only at `run` — so an agent asked for a model that validates and solves would have shipped one whose ion dies at step zero with two clean bills of health saying otherwise. `ModelValidator` now checks the signed distance, which is arithmetic on numbers already in the document.
  - **`run` and `test` computed the same flight time two ways** and disagreed by 1.3e-10, five orders in energy drift, so the most obvious workflow there is — quote what `run` prints, pin it with `einzel test` — failed for no stated reason. Collapsed to one implementation.
  - **A result printed as `10.180506 ± 0 µs`.** The agent refused to publish it and measured its own tolerance ladder instead. A residual of zero is not "no uncertainty", it is an uncertainty smaller than a comparison of two doubles can see; it now falls back to the whole ladder with one ulp as the floor, carrying a non-suppressible `convergence.at-resolution` note.

  Also from those transcripts, now fixed: **`optimiser.budget-exhausted` said "without meeting its tolerance"** when convergence is *two* tests and the parameter one is almost always already met — so tightening `parameterTolerance` appeared to do nothing at any value. It now reports the observed spread, which test it met, and which is still open.

  **Three more from the same transcripts, all now fixed, and all the same shape: the platform was arithmetically right and told the reader something untrue of their model.**

  **The cost gate charged seven flights for every three flown.** `estimate` predicted 29 s for a 2000-draw sweep that runs in **1.08 s** and exited 3 with no override, because it billed every figure the study's declared ion count — right for an ensemble, wrong for `flightTime`, which is one ion down a three-rung convergence ladder. The agent **shrank its study** to get past a number twenty-six times too high, which is the gate degrading the science it exists to protect. The registry now carries a `FlightBasis` per figure and the estimate asks it: 3 trajectories per evaluation rather than 21, 4 s rather than 29. An unknown figure falls back to the ensemble count, the conservative direction, since the gate exists to stop a surprise rather than to permit one. `--threshold` moves it (GRD-8 asked for configurable and it was a constant), and **the refusal now says the study is not blocked** — only `estimate` exits 3 — because "above the threshold" on an exit code named cost-gate refusal reads as a prohibition and was obeyed as one.

  **`CONVERGENCE_ORDER_BELOW_NOMINAL` prescribed a finer grid to a model with no grid**, on 578 of 1505 evaluations of an analytic field, unsuppressibly. The field knows which it is, so the advice asks it: a gridded field still gets "interpolation error, refine"; an analytic one gets "there is no grid — the floor is the arithmetic, or a discontinuity the ladder is straddling". **An unsuppressible warning that cannot be acted on teaches people to skim the one class that must never be skimmed.** **And the test for it was a test of the platform's libm**: driving a ladder onto a floor and reading the message passed on Windows and failed on Linux, because the square wave that made the floor used `Math.Sin` (not bit-identical between the Windows CRT and glibc) and the construction amplifies last-bit differences — as does `Math.Pow(error, -0.2)` in the step controller, so making the wave exact does not fix it. The real error was asserting which side of a threshold a *fit to noise* lands on: sweeping the perturbation gave orders of −0.84 to +0.08 against a threshold of 0.5, with one configuration not firing at all. The advice is now a named function tested directly, and the end-to-end test asks that **at least one of seventeen** configurations reaches a floor rather than a particular one.

  **A half-space has no far side, and nothing said so.** Its cap is what the plate holds at the declared depth and the ramp continues past it, so a cap below the beam energy turns the ion round *behind* the plate — 5.6 mm behind at 3600 V against a 4000 V beam — self-consistently, to full precision. `field.beyond-declared-depth` (Qualified) now says where it actually turned. Measured at **three sigma of a declared energy spread** rather than at the nominal ion, because while only the tail is past the plate the effect is a **selective loss of the fastest ions** rather than a wrong flight time — harder to notice, likelier to be read as physics.

  **And the round-off guard on it was tested with the numbers that motivated it.** The overshoot must clear a millionth of the depth, because the gradient is a division and a model placed deliberately at the boundary would trip on which way it rounded — which the scaffolded reflectron is. The test used that model's 4 kV on 50 mm and **passed with the guard removed**, since those round-trip exactly. Searching the space found thousands of ordinary pairs that do not; 1 kV on 7 mm discriminates. The rule is in `docs/lessons.md`: **the values a test reaches for are the ones from the model that motivated the guard, and that is the case you already know about.**

**The Ion Processor is now a comparison rather than a target.** Turn-around through the solved rectilinear cross-section is **1.224× the naive V/2r₀ closed form at every mass and, to 1%, at both extraction voltages** — so what the slot and the fringe take out of the extraction field is a single geometric factor (the field at the packet is 0.82 of V/2r₀), reusable rather than re-measured per operating point. At 4 kV push, the top of the paper's stated 1–4 kV range, m/z 195 gives 0.652 ns and m/z 500 gives 1.044 ns, straddling the reported Δt* = 0.8–1.2 ns. **The magnitude reproduces; the spread cannot.** Holding every mass from 195 to 2722 inside a 1.5× band needs a quantity varying by 1.5×, and a thermal turn-around varies by 3.74× whatever the field — so that row is normalised, or it is not a turn-around.

And **extraction efficiency is now an actual comparison**: the paper's ~84% at m/z 1522 falls between a 2.0 and 2.5 mm slot on a 2 mm inscribed radius. **The trade is badly asymmetric in a direction that is easy to miss.** Turn-around barely notices the slot — 1.821 ns at 1.0 mm against 1.878 ns at 3.0 mm, three per cent over a threefold widening — so watch only turn-around and a wide slot looks free. The dipole grows **53-fold** over the same range while the 12-pole stays flat at 6.4e-3 to 7.9e-3, which also confirms across a sixfold range what was previously attributed at two points: the 12-pole is what flat plates cost, the dipole is what the slot costs. A dipole displaces the trapping centre, which for a device whose job is to present a packet against a slot is exactly the aberration that matters.

**A standing literature-regression target**, recorded in `docs/literature-targets.md`: the Stewart/Grinfeld Ion Processor (JASMS 2023, PMC10767742). **Do not confuse this with the memo's Stellar HP/LP LIT pair** — that is existing hardware, a radial-ejection linear ion trap; this is the new rectilinear conjoined collision cell plus transversal pulsed-extraction trap. Memo §6 item 5 is precisely the choice between them, so both are targets. Its Δt* = 0.8–1.2 ns turn-around time across m/z 195–2722 is **a DC problem and reproducible far sooner than the rest**. Both analysis-side prerequisites are now done — turn-around time and emittance as figures of merit, and ensemble launching from a thermal distribution — so what is left is **geometry**: the rectilinear cross-section as a solved template, which is `rectangle` primitives and nothing new. Extraction efficiency, ion capacity, and the pressure gradient are Phase 3. As important as the MR-TOF work, not less.

**LIB-1 is now satisfied.** Schema 0.2 adds a declared parameter surface (named values with units, bounds, descriptions, and derived expressions checked dimensionally) and a `solved2d` field type carrying electrode geometry as data. Device templates are embedded JSON in `Einzel.Library/Templates/`, not classes:

- `planar-mirror-pair.json` — 11 parameters, an edge-profile board pair, a cap, and a declared reflection.
- `quadrupole.json` — four discs. **Shares no code with the mirror at all**, which is the point: adding a device is a new file, not a new class. Verified against the analytic form — Φ(x) = −Φ(y) exactly, and Ex/x constant to **0.17%** across the central 45% of r₀, i.e. a linear restoring force. Ratio to the ideal hyperbolic field is 0.926, the expected round-rod approximation.
- `rectilinear-trap.json` — four flat plates round a square aperture at r₀ = 2 mm, the front one split by a 1 mm extraction slot, with corner gaps. The Ion Processor cross-section, carrying both configurations: side plates against front and back and it is a trap, back plate high and it extracts.
- `kingdon-trap.json` — a wire on the axis of a cylinder: the electrostatic orbital trap, and the Orbitrap's ancestor. **The first device combining an axisymmetric solve with genuinely three-dimensional motion** — the geometry is two coaxial cylinders solved in a half-plane, and the ion circles. Its closed forms are exact and strange: in φ = A ln(r) the force goes as 1/r, so the circular-orbit condition loses its radius entirely and **every orbit has the same speed** — 1.5, 4.0 and 7.5 mm all orbit at 2047.02 m/s, from `sqrt(qV/(m log(b/a)))`, which contains no radius. Angular momentum is conserved to **2.9e-12** over 16 orbits and the axial excursion is **exactly zero**, because an axisymmetric field has no azimuthal component to make a torque from — a tolerance-free invariant of the same class as the maximum principle. The departure from the logarithm is largest **near the wire** (0.28 V of 100 at r = 2 mm, 0.0002 V at r = 8 mm), which is where a 0.1 mm wire on a 0.25 mm cell is under-resolved: an error largest where the mesh is worst is discretisation, one that is uniform is a wrong operator. It needed **`log` in the expression grammar**, which did not exist — the same shape as `multipole-guide` finding no trigonometry. Does not confine axially; a real one adds end electrodes.

**The third device needed no change below `Einzel.Library` — and three in the model format**, each an assumption about beams that a trap does not meet. A source may now **start at rest** (§9 required a non-zero accelerating potential "or the ion never moves"; a pulsed extraction trap's packet sits still until the instrument switches a field on — and §12 already asked for turn-around time from exactly such a device, so two sections of the spec contradicted each other and neither was wrong alone). A **vector placement may be parametric** (§9 says every placement is an expression; scalars always were, vectors were not, and both earlier templates hid it by being symmetric about something convenient). And a **dimensionless zero satisfies any dimension**, because the grammar has no unit literals and there was otherwise no way to write "on axis" — narrow on purpose, a dimensionless *one* is still refused.

  What the solve bought, all in `docs/device-templates.md`:

  | | |
  | --- | --- |
  | Largest unwanted multipole, this trap vs round rods | order 1 at 5.43e-2 vs order 6 at 2.41e-5 — **2,258× worse** |
  | Turn-around from the naive V/2r₀ | 3.448 ns, **18.8% low** |
  | Turn-around from the solved field | 4.215 ns, **0.7% low** |
  | Measured through the geometry | 4.243 ns |

  **The dominant aberration is a dipole, and it is the slot's, not the plates'.** Narrowing the slot 10× collapses it 96-fold (5.43e-2 → 5.69e-4) while the 12-pole barely moves (7.12e-3 → 6.06e-3), so the 12-pole is what flat plates cost (~250× round rods) and the dipole is what the slot costs, seven times larger again. A dipole displaces the trap centre rather than distorting the well, which for an extraction trap is the aberration that matters most. An earlier draft reported only the 12-pole and called it 296×; that projection used cosines alone, which is exact for four-fold-symmetric round rods and blind to a slot that breaks symmetry about x — the asymmetry lives entirely in the sine terms.

  Same lesson as the mirror's four-penetration-depth rule being 10 mm out: the formula is right, the number fed into it is not. And **turn-around is only 1.8% of the arrival spread** — decomposing a 0.2 mm packet gives 4.28 ns thermal, 231.9 ns depth, 12.3 ns width against a measured total of 241.4 ns. Quadrature closes to 3.8% rather than exactly, and the gap is the aperture coupling the two spatial spreads: which ions survive depends on depth and width together, so the population arriving with all three on is not the one either pair-wise run measured. There is also no useful space focus: the spread grows monotonically at 20.7 ns/mm from 2 to 11 mm, because a field varying 2× across the packet destroys a focusing condition derived for a uniform one.

  **The gap this template exposed is now closed: electrodes stop ions.** See below. `CompiledElectrode.FirstEntry` already finds the entry point in closed form and the integrator already lands exactly on declared events, so it is wiring rather than new numerics.

- **Electrodes stop ions**, which is the gap the trap exposed and the one that made every transmission figure 100% by construction. A field that has conductors declares them as a **signed distance** (negative inside) through `IConductorBounded`, so an impact is the zero of a scalar along the step — the same kind of event as a stopping surface, found by the same bracketed root-find, landing *on* the surface rather than a step short of or inside it. No second event mechanism.

  Three things make it sound. The **chord is safe because the step is already capped** by the grid's own cell spacing, so a trajectory cannot arc into an electrode and back out between samples — a cap added for an unrelated reason that turns out to license this one. **Order matters**: an electrode is checked ahead of the detector (an ion that hits metal did not arrive) and behind a declared discontinuity (an electrode cannot be on the far side of a surface not yet crossed). And a **source inside a conductor is refused rather than flown**, since otherwise it reads as an instrument that loses everything.

  **ACC-5 is now satisfiable**: losses are itemised by the surface name the model author wrote, or by mechanism where there is no surface, and every launched ion appears exactly once. `frontPlateRight` is a thing to move; "transmission is 51 percent" is not. Checked against erf for a slit — 0.95σ at 20,000 ions with every electrode grounded so the field is exactly zero and the answer is pure geometry — and the impact point lands within 1e-8 m of the surface.

  **It corrected two published numbers immediately.** The trap's wide-plane emittance was **15.63 mm·mrad and is 1.56** — the ions inflating it were exactly those flying through the front plate. The width row of the arrival-spread decomposition was **87.2 ns and is 12.3**. Both were artefacts of ions traversing metal, and both had reached the docs.

  **Not modelled:** mesh and grid electrodes, which real instruments use and which are transparent to most of the beam. There is no way to declare one, and a mesh cannot be modelled as its wires either, because the wires run along the invariant axis of a 2D solve.

- **Cylindrical symmetry (SYM-1), and with it the device this platform is named after.** `"symmetry": "cylindrical"` on a solve makes x the axis of rotation and y the radius. It is not presentation — the radial part of the Laplacian becomes (1/r) d/dr (r dφ/dr), written in **conservative form** (flux through a ring's outer face minus its inner face, over the ring's own volume) so cut cells carry over unchanged and the axis is stable. **On the axis the inner face has zero area**, so the ring is a disc and the limit is 4(φ₁−φ₀)/h² — *twice* what a mirrored plane stencil gives, which is the factor a solve gets wrong if it treats the axis as an ordinary symmetry plane.

  | | |
  | --- | --- |
  | Coaxial pair against φ = A ln r + B | 1.3e-3 V of 100 V applied |
  | The same against a *linear* profile | 19.3 V — the plane operator's answer, and far away |
  | Convergence order, 32→256 cells | 1.84 / 2.00 / 1.95 |
  | Tube penetration vs the first Bessel zero | **2.40503 against 2.404826** |
  | What the plane operator would give | π/2 = 1.5708 |

  The coaxial check sounds vacuous — φ = A ln r + B holds in both geometries — and that is what makes it sharp: the *plane* solver gets a linear profile there, so agreeing with the logarithm is exactly what the radial weighting is responsible for. The Bessel check is the cylindrical-only one, and it converges to j₀₁ **from below** because the cap's mode coefficients go as 1/(jₙJ₁(jₙ)) and J₁ alternates sign at its zeros, so the second mode enters negative and slows the apparent decay until it dies.

  **A latent bug in the interpolant surfaced here and had been live in a shipped template all along.** The bicubic ghost node is filled by linear extrapolation, which is right at a Dirichlet edge and wrong at a **Neumann** edge — a mirror plane, where the ghost is the reflection. On the axis that left a radial field of **14 V/m** at a place with no radial direction; an ion launched exactly on axis would have drifted off it. Reflecting takes it to exactly zero. The mirror-pair template declares a Neumann edge too, and no test had ever asked what the field was *on* that plane. In `docs/lessons.md`.

  `AxisymmetricField` does SYM-1's other half — the half-plane solve presented as the field in space, sampled at (x, √(y²+z²)) with the radial field pointed back along the ion's azimuth. Azimuthal field exactly zero, transverse field on the axis exactly zero, and a rectangle in the half-plane is a **ring** in space. It gives up `FieldFreeRunLength` (returns 0): a straight line in space is a curve in (axial, radial), so a mapped direction is only instantaneously right and the guarantee is about the *whole* run.

- **A sweep discarded every warning its draws earned, at the one seam every study crosses.** `FiguresOfMerit.Evaluator` hands a driver a `Func<CompiledModel, double?>`, because ranking needs an ordering and a GRD-1 envelope has none. The discard was documented as deliberate — and it dropped the *warnings* along with the interval. A thousand draws could each have been computed in a field that never converged, and the study would report a distribution, a ranking, and nothing else. Same shape as `FieldAssembly.Build` discarding its `SolveReport`: **the evidence about a computation's own quality was the shortest thing to drop.**

  `WarningLedger` is the sink, distinct by code and counted, because "on 3 of 1000 draws" and "on 1000 of 1000" are the difference between a corner of the box and a study to throw away. Three things it forced. Counting is **per evaluation, not per emission** — an ensemble figure builds the field once per ion, so one draw emits the same code twenty-one times. `Setup` now uses `BuildReported` when it has a sink and `Build` (which throws) when it does not, which is the honest reading of taint-never-block: carry it if you can, refuse if you cannot, never drop it. And the control test had to be weakened to be true — a clean study now reports the same convergence provenance a run does, so what has to be absent is the *specific* claim made about the strained model, not all warnings.

- **`einzel-lens.json`** — three coaxial tubes, the fourth template and the namesake. 5 mm bore, 500 V centre, 1 keV beam: a ray 1 mm off axis crosses at 129.1 mm, so f = 81 mm ≈ 16 bore radii. **It converges for either sign of the centre voltage** (+500 V → 129.1 mm, −500 V → 273.3 mm), which is the classic non-obvious property and the one a merely plausible field fails. Focal length shortens with voltage (287/144/81/49 mm at 300/400/500/600 V) and outer rays focus 4.7 mm shorter than inner ones, which is spherical aberration in the right direction.

  **Unipotential, measured rather than assumed.** Total energy is conserved to **6.4e-10** across a path crossing a strong field twice. But the *kinetic* energy returns only to 2.5e-6 — because the launch point sits a quarter down the entrance tube where the centre electrode's field has not finished decaying, and the potential there is 2.457 mV against a 1000 V beam, matching the discrepancy to four figures. So a lens is unipotential only to the extent its tubes are long, the residual falls as exp(−2.405 L/r), and how long is a design question the solve answers.

- **RF on solved geometry, and the stability diagram on real rods.** A solve may declare a `drive` (frequency, waveform, duty cycle) and each electrode taps it with a signed `driveAmplitude` and a `drivePhase`. Driving costs almost nothing beyond solving once, because **basis superposition already was the mechanism** — the field is linear in the applied potentials, so making the weights functions of time *is* the RF, and the Poisson equation is never stepped in time.

  **Channels, not electrodes.** Electrodes whose potentials are the same function of time, or exact negatives, share a basis. A quadrupole's two pairs are exact negatives, so **four rods reduce to one basis solve** whose weight swings 500 → 0 → −500 V; a q scan or a mass scan re-solves nothing at all. This is SYM-1's aside made real ("two RF basis fields plus a DC gradient, not 200 basis solutions"). Grouping is exact, not tolerant — a tolerance would silently merge channels meant to differ and the field would look fine.

  **The first result where the solver and the time-domain integrator had to be right together.** §19 calls the a–q diagram the best single test of the RF path, but it had only ever been run against an analytic field that is quadrupolar by construction — which tests the integrator, not the solver.

  | | Low-mass cut-off |
  | --- | --- |
  | Solved round rods | **q = 0.90525** |
  | This engine, ideal hyperbolic field | q = 0.90684 |
  | Tabulated Mathieu | q = 0.90804 |

  0.31% below the tabulated ideal and in the right direction. That it is the *geometry* rather than a formula is checkable: changing the rod ratio to 1.30 moves the cut-off to 0.89978. And "unstable" is now physical — the ion ends on `rodYPlus`, the pair that goes unstable first on the a = 0 line, rather than leaving an aperture the test had to invent.

  **Grouping is by spatial pattern, not time dependence**, which is what makes it minimal. Each electrode's potential is first split into the supplies feeding it — one constant, one per distinct phase — so a resistor chain down a funnel is a *single* supply however many distinct voltages it holds, because what makes a supply one supply is that its electrodes move **together**. Then supplies whose potentials are exactly proportional share a solve and carry a weight each.

- **A non-finite double is now written as `null`, as a property of the surface.** JSON has no NaN or infinity, and one such value does not degrade a document — it takes the whole thing down at the serialiser, after the run succeeded. That happened **four times** on four unrelated fields (a convergence residual, a Twiss orientation, a space-charge fraction, and a driven field's deliberately-NaN energy drift), each fixed where it was found. `FiniteDoubleConverter` closes the family: absent, not zero, which is the policy the rest of the surface had already reached by hand. Reading is the mirror so stored results still round-trip, which `verify` needs. The lesson — about when to stop fixing instances — is in `docs/lessons.md`.

- **Three dimensions.** `Grid3D`, `ScalarField3D`, `DirichletMask3D` with `CutLinks3D` on six arms, `PoissonSolver3D` (red-black Gauss–Seidel, full-weighting restriction, trilinear prolongation, Shortley–Weller in cell units), `TricubicInterpolant`, `SolvedField3D`, and box/sphere/cylinder primitives with closed-form signed distance and first-entry. The first solver here with **no symmetry behind it** — a cross-section assumes the geometry repeats along the third axis, an axisymmetric solve assumes it repeats all the way round, and this assumes nothing.

  **Written beside the 2-D path, not by generalising it.** That path carries every validated number in this engine, and refactoring a numerical core known to be right in order to add a case next to it is how those numbers get quietly lost. The duplication is the cheaper price.

  | | |
  | --- | --- |
  | Harmonic quadratic, reproduced | **4.3e-13** relative |
  | Non-polynomial harmonic, observed order | 1.92 / 1.99 |
  | Cycle count at 16 / 32 / 64 intervals | 12 / 13 / 13, factor 0.08 |
  | Neumann face vs the full solve it mirrors | 1.4e-16 V |
  | Curved conductor vs the 1/r law | 2.8 V of 100 applied |
  | Maximum principle at the nodes | exact |
  | Tricubic on a linear field | 3.6e-15 V, gradient 6.2e-12 V/m |

  The quadratic is the sharpest test available: the seven-point Laplacian is **exact** for a quadratic (truncation starts at the fourth derivative), so a harmonic quadratic on the faces is an identity rather than an approximation converging — nothing about the operator, the faces or the transfers can be wrong and still pass.

  **Coarse levels are node-aligned, and that is what makes them work.** Sub-cell surfaces on a coarse level are actively harmful: an electrode a fraction of a coarse cell across gives arms a thousandth of a cell long with enormous coefficients, and the correction converges *somewhere else* — a charged sphere hit **137 V of 100 applied**. The fix separates what each level is for: **cut cells on the finest level, where accuracy comes from; node-aligned geometry below, where only acceleration does**, with an electrode too small to hold a coarse node pinning its nearest one so it stays present. That took the sphere from **13 s to 783 ms (9 cycles, factor 0.126) with an identical answer**. Agglomeration was tried and rejected in 2-D — but against a geometry rebuild that *worked*; here it does not, and stable-but-cruder beats correct-but-uncoarsenable. Galerkin coarsening is still the better answer and still not done.

  Three things this cost, all worth keeping. The residual norm had to become **RMS rather than max**: with sub-cell surfaces one tiny-arm node dominates the maximum, so the norm rises while the solution improves, and judging convergence on it stopped a good solve after two cycles. `OverBox` rounds intervals **up to a power of two**, so asking for 24 and asking for 32 gives the same mesh — a refinement study that did not know that reported an observed order of exactly zero. And the maximum principle is a statement about **nodes**: a cubic through the step at a conductor overshoots ~2% by construction, which is the interpolant behaving normally.

  **Now wired to the model format** as `solved3d`, with box / sphere / cylinder electrodes, repeats, drives and stages — everything `solved2d` carries, because none of it was ever about the dimension count.

- **`segmented-quadrupole.json`** — three axial sections at their own working points, and **the first device a cross-section cannot express at any resolution**: what makes it segmented is that the field varies *along* the axis, which is the direction a translational solve is invariant in.

  **Twelve rod segments reduce to one basis solve** — the pairs within a section are exact negatives, and the sections are tapped off one generator in a fixed ratio at the same phase, so the whole structure is a single spatial pattern. Switch the analysing DC on and it becomes **two**, and that is physics rather than accounting: the coupling is a *capacitor*, which passes RF and blocks DC, so the prefilter sees the drive and not the offset and the proportions no longer match. A resistive tap collapses it back to one. That capacitive coupling is also what a prefilter is *for* — ions meet a confining field before the analysing one instead of crossing the DC fringe on the way in. (My first draft tied `preDc` to the RF coupling ratio; the channel count coming out at 1 instead of 2 is what exposed it.)

  The sections measurably sit at different working points — transverse field at r0/2 of **224.3 vs 258.7 kV/m, a ratio of 0.867 against a declared 0.850**, the 2% being each section's own ends bleeding into its middle. An ion tracked through the whole structure arrives 0.26 mm off axis after 54 µs and 3,982 steps. A full run — solve plus tracking — is about **11 seconds**.

  **The cut-off brackets the ideal Mathieu boundary**: through at q = 0.855, lost at q = 0.910, against a tabulated **0.90804** — on round rods, cut into three sections, with gaps and end fringes. And it is lost in the **main** section (`mainYMinus`, z = 38.7 mm of 23–47 mm), not the prefilter, which is the segmentation working: the entrance sits at 85% of the main amplitude so its q is 0.85 of it and stays stable while the analysing section ejects.

  **That is not where the number started, and the wrong explanation is the part worth keeping.** Before the coarse levels were node-aligned the ion was lost at q = 0.611, and I wrote down "field quality at a coarse mesh" as the reason. It was not: refining the mesh moves the mid-section transverse field by **0.014%** and a transmitted flight time by 9e-5. It was an under-converged solve, and fixing the multigrid moved the boundary from 0.611 to the right answer. A wrong number with a plausible explanation attached is the expensive kind — the explanation is what stops you looking.

  **What is converged and what is not, now measured separately.** The mid-section field is settled — 0.014% across a real transverse refinement — and that is what the cut-off rests on, since the ion is lost at z = 38.7 mm in the middle of a 24 mm section. The field **inside a 1 mm segment gap is not**: 2.4% then 1.4%, still moving at the finest mesh that finishes. So the template shows that sections at different working points can be declared, decomposed and solved, and does **not** yet show what the joins do to an ion — which is the one claim a segmented quadrupole would most like to make. Two further traps in the same study: `OverBox` rounds each axis to a power of two independently, so asking for 5 and asking for 8 give the *same* transverse mesh; and the shipped "5 cells across r0" is really **8.5**, because the study had been labelled by the request rather than by the grid it produced.

  Two small things the wiring exposed. `Solve3D` camel-cases to `solve3D`, not `solve3d`, so the property is spelled `Solve3d` to keep C#, JSON and the generated schema on one spelling. And a derived-parameter units error reported its path as `/` rather than naming the parameter — recorded here as a small AGT-3 gap, and **fixed once the consequence was measured**; see the entry at the end of this list.

- **The sequencer — a geometry operated through timed states.** A solve may declare `stages`, each a duration plus a set of parameter values. **A stage sets parameters, not electrode settings**, which is the whole design: potentials are already expressions over parameters, so setting one moves everything depending on it *including derived parameters*. Listing settings instead would let a stage change an amplitude while leaving the quantity it was derived from behind. It also costs no new vocabulary — the same override mechanism a sweep uses to *perturb* a design is what a sequence uses to *operate* one.

  **Landing on a switch needs no root-find**, unlike a boundary in space, because the time is known: the integrator asks `NextSwitchAfter` and refuses to step past it. The check is that a sequenced run equals the same flight computed as **two separate runs stitched together** — the same physics written two ways, needing no closed form. Disagreement is **1.0e-8 / 4.6e-8 / 1.3e-9** relative at tolerances 1e-8 / 1e-10 / 1e-12: parts per billion throughout, which is round-off between two different step sequences rather than the parts per thousand a straddled switch would leave. Not monotone, because which steps each route takes is luck rather than a trend.

  Two rules that are enforced rather than documented. **A stage may change what an electrode holds, not where it is** — moving metal would change the mask, so each stage would need its own solve and grid, and the field would still be computed and still be wrong. Refused, naming the electrode and the stage. And **the last stage holds after the sequence ends**: an instrument left alone stays where it was put, and a field switching off would make every ion still in flight suddenly coast — a physics change disguised as a bookkeeping one.

  A sequence needs no drive: a pulsed extraction is DC that switches, and a solve with stages and no `drive` is exactly that. Stages sharing a spatial pattern share their solve, so a trap that holds at one voltage and pushes at another costs one basis field.

- **Repeated geometry (SYM-1's discrete periodicity), and the funnel it makes possible.** An electrode may declare `repeat: { count, index }`; the index binds as an ordinary parameter so every expression on the electrode sees it, and copies are named by position (`ring-17`) so an error or a loss itemisation says which. Two functions were added for it — `floor` and `mod`, both dimensionless-only for the reason `sqrt` is; `mod` is Euclidean so `mod(-1,2)` is 1 and a backwards index still alternates.

  This is what keeps a 200-ring stack a *parametric* document rather than a generated one: "move every ring 50 µm and re-solve" is still sayable, which is the whole basis of §13's tolerance work.

- **`ion-funnel.json`** — a tapering stack of RF rings with a DC chain, written as one ring repeated. **The solve count does not grow with the ring count**: 8 / 24 / 48 rings all reduce to **2** basis solves. That is SYM-1's own argument measured, and it comes out at 2 rather than 3 because the two RF phases are exact negatives — one spatial pattern, one weight.

  An ion entering 6 mm off axis threads the whole stack and exits the 1.5 mm aperture, so it was compressed at least 4×. **The RF is demonstrably what confines**: switch the drive off and the ion ends on `ring-14`. Acceptance falls off properly with entry radius (1/3/6 mm through, 9/11 mm lost on named rings).

  **Gas now exists and it bites hardest here.** Collisions damp the radial motion so ions settle onto the axis rather than ringing — measured at 864.6 → 255.7 m/s of radial speed — but only at pressures at or above trajectory integration's own validity boundary, so the acceptance above is still a lower bound until statistical diffusion is built. See `docs/pressure.md`. And a sign that has to be right: the DC chain starts at zero to match the grounded entrance, because putting the high potential there instead makes the boundary push the ion straight back out — which is what happened first, and the run said so, 3 metres upstream.

- **`travelling-wave-guide.json`** — a ring stack whose drive phase ramps along it, and the first device that **needed a change below `Einzel.Library`**. LIB-1 says to believe that signal, and it was right and narrow: `drivePhase` was a plain `double` while every other placement was an expression, so a phase could not depend on the repeat index — and a phase that cannot depend on the index cannot ramp. Its own doc comment had said "a travelling wave is a ramp from zero to one along its length" since it was written. That was the one device it could not express.

  **A sinusoid collapses every phase into two solves, and that is what makes the device affordable.** A cos(2π(ft + φ)) is exactly A cos(2πφ)·cos(2πft) − A sin(2πφ)·sin(2πft) — a fixed pair of time functions with constant coefficients — so however many distinct phases a structure carries it reaches **two** supplies. Measured against the same decomposition without the quadrature step: **96 rings give 21 supplies without it and 2 with**. The naive count is not the ring count, and that is its own argument: phases of 1/6 and 7/6 are the same angle and share a supply only if they agree to the bit, which after the wrap they do not, so what you would actually pay is unpredictable. `CosPi`/`SinPi` rather than `Cos`/`Sin` of a scaled argument, because `Math.Sin(Math.PI)` is 1.2e-16 and an antiphase electrode would otherwise pick up a quadrature component made of round-off — a third channel carrying a field of nothing. Rectangular waves are excluded and keep one supply per phase, because a square wave shifted a quarter cycle is not a combination of two fixed ones.

  **The wave travels at the declared speed to 0.09%** — 3002.6 m/s against ringsPerWave × ringPitch × driveFrequency = 3000.0 — measured from the phase of the fundamental spatial harmonic. Following the tallest point was the first attempt and does not work: the window holds two wavelengths, the tallest point jumps to the next crest as the first leaves, and a straight line through the jump read 4,200 m/s. Reversing the declared direction reverses the wave (−2987.3 m/s), which is the control that separates a wave from a stack oscillating in place.

  **And it carries ions.** The signature of capture is that transit stops depending on injection: over injection speeds from 0.6 to 1.4 of the wave speed, ballistic transit spans 15.00 to 6.43 µs while measured spans **8.754 to 8.875 µs — a spread 57× smaller**, all of it at the wave's own 9.0 µs. Amplitude is what decides it, and the window is narrower than it looks: 20 V only pulls an ion part way, 60 V carries it, 120 V drives it into the rings. **A first version of this test concluded the opposite** — it compared the two transits to each other instead of to ballistic, found them 0.75 µs apart, and called that "no capture". Two numbers being close proves nothing when their ballistic values were close too.

  What it does not do is confine radially: acceptance is about **0.1 mm on a 2 mm bore**, because a travelling wave has deep wells along the axis and almost no radial restoring force. A real travelling-wave guide superimposes a fast alternating RF for confinement on the slow travelling wave, which this format cannot express — an electrode carries one drive tap, not two.

- **RF in the diffusive mode: the 0.01–10 mbar band is modellable, and the textbook pseudopotential is wrong there.** Between about 1e-2 and 10 mbar — which is where ion funnels, travelling-wave guides and collision cells actually run — neither transport mode could describe a driven structure. Trajectory integration is outside its validity; the diffusive mode had no way to see a drive. Two templates pointed at the same hole.

  **`PonderomotiveField` wraps a driven field as the cycle-averaged one a slow ion feels**, so the drift-diffusion solve needs no change at all — it asks for a potential at a point and gets the effective one, the same trick `AxisymmetricField` uses.

  **The collisional form is not the one usually quoted, and that is the whole point.** A damped quiver is smaller, so the round trip through the field gradient leaves less net force: derived in the class from m(v̇ + νv) = qE₀cos(Ωt), the well is q²E₀²/(4m(Ω²+ν²)) rather than Dehmelt's q²E₀²/(4mΩ²). **Measured on the shipped funnel at 2 mbar and 1 MHz: suppression 0.693, so the collisionless formula everyone quotes overstates the confining well by 44%.**

  **The damping rate is the momentum-transfer rate, taken from the mobility** — ν = q/(mμ) — not from the collision count. A heavy ion in a light gas gives up only about the mass ratio of its momentum per collision, so for m/z 500 in nitrogen the collision count would over-damp by roughly twenty times; and taking it from the mobility keeps it consistent with the drift the same solve computes, which a second independent estimate would not be.

  Closed forms, not plausibility: the collisionless well reproduces Dehmelt exactly, its curvature gives the secular frequency **qΩ/√8** written in the Mathieu parameter rather than in volts, ν = Ω suppresses it by exactly one half, and the quiver amplitude falls by the same √2. **REG-2 applies**: the suppression is reported whether or not it crosses a threshold, and `rf.quiver-exceeds-mesh` is a non-suppressible violation — which the funnel trips at 100 V, where the ion is swept 0.849 mm across a 0.312 mm cell and the averaging has nothing left to average.

  Two things this cost. A first test wrote the quadrupole's field amplitude as V·r/r₀² and every closed form came out **exactly four times too small**, because that field's potential is V(x²−y²)/r₀² and its gradient carries a factor of two — the tests now ask the field for its own amplitude rather than restating its convention. And **`ResolutionLength` is positive infinity for an analytic field**, meaning "no resolution limit" rather than "an enormous one"; reading it as a differencing step gave ∞−∞ and a field of NaN while every potential stayed correct.

- **The diffusive mode accepted a driven geometry and stepped a density through a snapshot of the RF.** Found by pointing it at the travelling-wave guide, which is the obvious thing to try: a real one runs in a gas, and the diffusive mode is what this engine has for a gas. A driven field's time-free members sample t = 0, so what the solve used was the RF at the top of its cycle — a static field that exists for no length of time — and it reported a transit distribution with **no warning anywhere**. That was the one place in this engine a transport mode was selected outside its validity and nothing said so. Now refused as `REGIME_INVALID`, naming what would have to exist: the RF entering the diffusive drift as an **effective potential**, which this build does not compute.

  Two smaller things fell out of it. A diffusive run **crashed the human printer** — `INTERNAL_ERROR`, an empty `FinalPositionMm` indexed at `[0]` — and printed `flight time NaN +/- NaN` above it. A density has no flight time, no energy drift and no final position; those lines are now absent rather than not-a-number, which is the rule the rest of the surface follows. And an `EinzelException` **always exited 1**, so a regime violation raised as a refusal exited differently from the same finding raised as a warning on a completed run. CLI-3 wants a code per failure *class*, and the class is in the error rather than in how it reached the top.

- **`einzel solve` reported the DC pattern and nothing else for every driven 2D geometry.** The 3D path had been fixed by an earlier code review to report one entry per basis channel; the 2D path still built one mask from the electrodes' DC potentials. For the shipped **`quadrupole-rf`**, whose every electrode holds zero volts of DC and all of its potential as drive, that was a solve of a grounded box: **peak potential 0 V, zero cycles, `converged: true`, exit 0** — a clean bill of health for a mass filter's field that was never touched. The funnel reported its DC chain and not the RF that does the confining. `GeometryBuilder.SolveChannels` now mirrors the 3D API and both dimensions report the same way.

- **The density became an output, electrodes started absorbing, and the gas started moving.** Three gaps in the diffusive mode, all of the same shape: a thing the mode computed or was told that nothing downstream could see.

  **A density had no drawing and no file.** RND-8 forbids trajectories through a diffusive region, which was right and, on its own, entirely negative — the mode's principal result could be summarised into a transmission and a transit time and looked at in no other form, so the honest figure of a funnel at a millibar was an empty box. `run --vtu` now writes a `.vti` density on the tracked grid, with the warnings in the file's own header (GRD-2), and `render section` draws it as contours at **decades** below the peak. Decades rather than even fractions because a density spans orders of magnitude — a packet's tail is a millionth of its core, not a small fraction of it — and even spacing draws the top decade several times and the extent not at all. The levels go in the figure's provenance, since a density plotted without them is a shape rather than a measurement. `--vtu` on a diffusive model previously wrote nothing and said nothing, which is the worst of the three options.

  **An electrode emptied the seed and then let everything through.** That stops a source placed inside metal from starting there — the case that reads as an instrument losing everything — and does nothing at all about density arriving later, so a funnel's rings shaped the field and then passed the density straight through. **Every diffusive transmission figure was an upper bound with nothing saying so.** The mask is now handed to the solver and those cells are held at zero at every step. A conductor is **an open boundary with a name**: with the far side at zero the Scharfetter–Gummel flux reduces to `B(−P)·n_here`, non-negative for any potential drop, so an electrode can only take and never give — which falls out of the scheme rather than needing a clamp, and is checked with the field driving ions *out* of the metal. A wall across the channel takes 100.00% collected to 0.00%, all of it named on the wall; the control is the same run without it, because "almost nothing arrived" is equally consistent with a solver that lost the density. And the seed's own overlap now **joins the same ledger** — it used to be deleted after the launched population was counted, so launched, collected, remaining and the named losses did not add up.

  **`transport.gas.driftVelocity` was honoured by one transport mode and silently dropped by the other.** It has been in the format since the collision models landed and the event-driven side has always used it; the diffusive solver never looked. Same shape as `FieldAssembly.Build` discarding its `SolveReport`. Advection by a moving neutral **is not the gradient of anything**, so it cannot enter as a potential difference — it enters the SG exponent directly as `P_gas = v·n̂ h / D`, which is the same exponent the field term already is, because by the Einstein relation `q(φ_here − φ_there)/kT` *is* `v h / D`. The two add, and the scheme stays exact for a linearly varying total drift.

  | | |
  | --- | --- |
  | Centroid carried by gas alone, 40 and 120 m/s | **1.000000** each |
  | Centroid against μE + v_gas, gas at ±60 m/s | **1.000000** each |
  | A still gas against no declared velocity | bit-identical, every node |
  | Ions conserved, moving gas across a varying field | 100.0000% |

  Six figures, not a band: SG is exact for a linearly varying drift and a uniform one trivially is, so the first moment is the scheme's own answer rather than an approximation converging. **Sampled at the face and averaged over its two nodes** — the neighbour computes the same average with the opposite sign, so the two cells agree about how much crossed. Sampling at the cell centre would repeat exactly the bug that drained a seeded Boltzmann equilibrium at 4.7× per millisecond. The reversed gas is the control, because a sign error is invisible when gas and field push together.

  **Both directions are reported, per REG-2.** A declared flow gets the ratio saying which is carrying the ions (`gas.flow`: 6.5, "the gas is carrying these ions, not the field"). A model with *no* flow above 1e-2 mbar — where spec figure 4 makes a velocity field a requirement rather than a benefit — gets `gas.stationary-above-flow-threshold`, because a stationary gas is a modelling choice and does not look like one in the output. And **`CollisionSampler` refuses a flow field rather than ignoring one**: it schedules and draws without a position, so it cannot evaluate a velocity that varies with one, and the alternative is an ion flying through a declared jet as though the gas were still.

  **Not built:** a neutral velocity *field*. `IGasFlow` is the seam and `UniformGasFlow` is the only implementation, so a funnel's transmission is still computed in a gas that is either standing still or moving all in one piece — and the jet off an inlet capillary is neither. **Since built** — `SampledGasFlow` imports one, and the pressure is a field too; see the GAS-1 entries below.

- **A cylindrical density was not conserving ions, and closing the ledger is what found it.** The cylindrical *Poisson* operator is written in conservative form — flux through a ring's outer face minus its inner face, over the ring's own volume — and that reasoning is recorded in `docs/numerics.md`. The *density* solver, on the same grid class with the same `Cylindrical` flag, computed a flux per unit area and applied it to both neighbours as though their volumes were equal. In an axisymmetric solve they are not.

  The weight is `A_face·h/V`: identically **1** in the plane, so an isotropic solve multiplies by one and is unchanged to the last bit, and `1 ± h/2r` in a cylindrical one. **On the axis it is 4**, because the inner face has no area and the cell is a disc rather than a ring — the *same* factor of four the Laplacian carries there, already written down one file away. The stability limit follows: a weighted face scales the outward coefficient, so the explicit step on the axis is four times shorter than the unweighted rate says, and `estimate` takes the weight from the same function `run` does.

  **The shipped funnel's ion ledger closed to 95.99%. It closes to 100.0001%.** The error goes as `h/2r`, so it is negligible at the wall and total on the axis — which is where a funnel puts its ions, and a funnel is the device this mode exists for.

  Three things about how it hid, all in `docs/lessons.md`. **Every conservation test in the suite was Cartesian**, where the weight is exactly one and a scheme with no weights is correct — passing for a reason that did not generalise, the same failure mode as the uniform-field test that hid the cell-centred drift sample. **The ledger did not have to close**: until interior electrodes absorbed continuously and the seed's own overlap was accounted, launched/collected/remaining/losses were never required to add up, so a four per cent leak had nowhere to appear — the bookkeeping fix found the physics fix. And **the conservation figure is not the discriminating check**: a wrong weight still conserves to 99.9995% on a short off-axis run. What cannot be nearly right is the weight, asserted exactly at 4 and `1 ± h/2r`.

  **Also measured, and not yet fixed: a driven diffusive run is expensive for a reason the estimate warned about.** The ponderomotive well's gradient at the ring edges sets the Courant limit — on the shipped funnel at 2 mbar the step is **1.067 ns against a diffusion limit of 5.2 µs, a factor of 4,900**, so 900 µs is ~843,000 steps. Attributed by control rather than asserted: 15.5 ns at 0 V of RF, 8.93 ns at 25 V, 1.067 ns at 100 V, so it is the drive and roughly as E₀². An implicit or operator-split step is the fix.

- **`einzel scan`: the third study mode, and the operation this engine kept rewriting by hand.** A study could be a tolerance sweep or an optimisation and nothing else. But a sweep collapses a range into a distribution and an optimiser reports only where it stopped, while **all of §12's Class B is a question about a curve** — stability and cut-off boundaries, mass filter peak shape against a scan line, low-mass cut-off for funnels and guides.

  So every curve here so far was a loop in a C# test file: the q scans, the extraction-slot scan at 0.5–3.0 mm, the 20.7 ns/mm drift scan. **None wrote a manifest, none could be re-run from the project, and none was reachable by an agent.** `ParameterScan` is the third driver beside `ToleranceStudy` and `Optimiser`, taking the same function from a validated model to a number; `scan` is a block in a study file, picked up by `einzel schema --study` through reflection with no schema edit.

  Four decisions worth keeping. **Both ends are included and returned exactly** — half of (0.1, 0.2) is 0.15000000000000002, so an end reached by interpolation lands an ulp outside a bound, and a scan written the obvious way (from a parameter's declared minimum to its maximum) has its last row refused by validation with nothing on the page to say why. My own test found that. **A failed point is a row, not the end of the scan**, and the reason matters more here than in a sweep: on a stability scan "the ion was lost on `rodYPlus`" *is* the answer, so a driver that stopped at the first failure would stop exactly where the interesting thing is. **A range past the declared bounds is warned about once, up front**, because half a table of blanks reads as the solver failing rather than the model refusing. And **the steepest interval is reported and deliberately not called a boundary**: what comes back is where on the grid actually computed the figure moves fastest and how wide that interval is *as a fraction of the scan* — ACC-6's own currency, one part in five hundred — so a reader can tell a resolved transition from a scan too coarse to have found one. An interval where the figure **vanishes** outranks one where it merely moves far, flagged as `figureVanishes` rather than an infinite change, because JSON has no infinity and a null would be indistinguishable from a value never computed.

  **What this is not, yet:** Class B proper. ACC-6 wants the boundary bisected onto, not bracketed by a grid, and peak shape against a scan line needs figures of merit that do not exist.

- **The examples corpus (EX-1), and the two defects writing it found.** One reference model of the thirty §5 asks for had existed since the beginning. Seventeen do now, and the release gate EX-2 wants is built: every example is materialised into a real project and driven through `einzel test` via `Program.Main`, **17 of 17 in 29 s**, so it runs on every change rather than at release.

  **Data, not code, discovered by a resource glob** — the same mechanism the device templates use, so adding one is a pair of files and nothing else. `name.json` is the model and `name.test.json` is what it must produce; `einzel new --from-example` writes both and rewrites the model reference to wherever the file landed, so the loop from `new` to a green tick has no step the user has to know about.

  **Every expectation is arithmetic, a published value, or an exact invariant** — never a number this engine produced and then had enshrined. `free-flight` and `reflectron-off-focus` come out at **exactly zero** error; the gap, the turnaround, the mass scaling and the orthogonal accelerator between 3.5e-16 and 5.0e-12; turn-around time and thermal emittance to 0.93% and 0.72% inside their own sampling errors. The one worth noticing is the **RF quadrupole pair**: q = 0.70 transmits 1.0 and q = 0.95 transmits 0.0, bracketing the tabulated Mathieu cut-off of 0.90804 from both sides, and neither number comes from here.

  **Two defects came out of it, both of the kind no test written from inside the project would catch**, because both were about a model that validates and answers a different question.

  **An unrecognised property was ignored rather than refused.** A cloud declaring `transverseWidth` instead of `transverseSpread` parsed cleanly, validated, solved, ran, and gave an emittance of **7.1e-8 µm where the closed form says 1.798**. This is the rule §9 already argues at length — `{"energy": 4000}` is a validation error on purpose, because "unit ambiguity is the commonest source of silent wrongness and an agent building from prose is the actor most likely to introduce it" — applied to the *key* instead of the value, and §22 names its consequence as the defining risk of the whole thesis. Now refused, naming the property by JSON Pointer and pointing at `einzel schema`. **Four shipped test fixtures turned out to be affected**: they declared 1 mm clouds and had been running with point sources.

  **ACC-5's transmission could not express zero.** It was read off an arrival-time peak, and a peak needs two arrivals to have a width — so a quadrupole above its cut-off, or an ion lost on a funnel ring, raised `INTERNAL_ERROR: a peak needs at least two arrivals` and the run reported *itself* as a defect in the engine. Exactly backwards for a requirement whose subject is transmission as a measured quantity: **an instrument that loses everything is the case a reader most wants reported, and it was the one case the figure could not report.** Worse one level up — `einzel run` caught the exception and returned no ensemble at all, so the itemised losses disappeared precisely when the transmission was zero. Fixed by separating the two: a transmission is a count and needs no peak; a width still needs two points and is now **absent rather than zero** when there are not two.

  **A third, smaller one, in the tests themselves.** Three tests edited the scaffolded model by string replacement against a JSON layout the corpus reformatted, so the edit matched nothing, the model was unchanged, and each reported the feature it was checking as broken. They now go through an `Edit` helper that **asserts the replacement happened** — a test that edits a file and does not check the edit is a test that can silently stop testing anything.

  Also added: **`transitTime`**, the mean transit of a diffusive run, because without it the diffusive mode's principal scalar could not be asserted by a project test or ranked by a study — half of REG-1's peer pair was outside the machinery that keeps the other half honest.

  **`parallel-plate-gap-3d` is the corpus's first genuinely three-dimensional example**, and it exists because Galerkin coarsening made it affordable — it was deferred at 124 s against a gate that runs everything else in 42. Two square plates in a cubic box, reducing to neither a cross-section nor an axis, reproducing `sqrt(2 d m / (q E))` to **1.2e-6** in under two seconds. Since the expectation is the same arithmetic the analytic accelerating-gap example uses, what is checked is the **solver** rather than the integrator.

**Two mistakes cost three orders of magnitude each, and both are the model author's rather than the engine's.** The gap in the closed form is between the **facing surfaces**, so putting a 1 mm plate's centre on the gap boundary makes the real gap 9 mm — the field came out **11.111% high**, which is exactly 1000/0.009 and is how it was caught. And **the grounded domain boundary is a third electrode**: holding one plate at 0 V makes the boundary an extension of it, so the problem is asymmetric about the mid-plane although the geometry is not. Worth 0.31% of the field at the ends of the flight and 0.11% of the answer; applying ±V/2 instead gives 0.0005% and 1.2e-6. **Both were mesh-converged** — identical at 1 mm and 0.5 mm cells — so neither was the discretisation artefact the first reading assumed, and the engine's own `CONVERGENCE_ORDER_BELOW_NOMINAL` warning pointing at "a finer grid" was pointing at the wrong fix.

**Still missing:** twelve more, and the gap is breadth rather than machinery — no multipole above four rods, no 3-D trap, no MR-TOF, and nothing in the diffusive mode.

- **Class B: bisection onto a boundary, and Phase 3 acceptance criterion 3.** ACC-6 asks for a boundary resolved to **one part in five hundred of the scan**. A grid reaches that by having 501 points in it; `BoundarySearch` halves the bracket instead, which is `log2(500)` steps plus the two that establish it — **11 evaluations against 501, measured**.

  **The result is an interval and the midpoint is a convention.** A bisection does not produce a value with an error bar around it, it produces a bracket known to contain the crossing, so the boundary comes back as a GRD-1 envelope whose `uncertainty` *is* that bracket, with `Evidence.Search` carrying the evaluations and the width. Three refusals that matter: a figure that **stops existing is outside, always** — a cut-off is precisely where the ion stops arriving, so treating its absence as a failed evaluation would refuse to look for the thing being looked for; a bracket whose **ends agree is refused rather than guessed at**, naming both and pointing at `einzel scan`; and a search coarser than ACC-6 is **qualified rather than refused**, because a coarse boundary is still a boundary and the reader needs to know which one they have.

  | | |
  | --- | --- |
  | Tabulated Mathieu cut-off, a = 0 | q = 0.90804 |
  | This engine, ideal analytic field | q = 0.90684 |
  | **Solved round rods, bisected** | **q = 0.90508 ± 0.00039**, 11 evaluations, 5.5 s |

  **And transmission against resolution, which is Phase 3's acceptance criterion 3.** Hold U/V fixed, scan V, and the width of the stability band *is* the width in mass — q goes as V/m, so a band of relative width dV/V passes one of relative width dm/m.  Both edges bisected:

  | U/V | q low | q high | q centre | R = V/dV |
  | --- | --- | --- | --- | --- |
  | 0.100 | 0.40521 | 0.77519 | 0.59020 | 1.6 |
  | 0.130 | 0.53383 | 0.74246 | 0.63815 | 3.1 |
  | 0.150 | 0.62226 | 0.72179 | 0.67203 | 6.8 |
  | 0.160 | 0.66781 | 0.71203 | 0.68992 | 15.6 |

  **The band closes onto the tabulated apex.** The first stability region's apex is at a = 0.23699, q = 0.70600, so the scan line runs out at U/V = a/2q = 0.16785 — and the centre walks monotonically to **0.68992, 2.28% below**, while R rises tenfold. Both halves are asserted, because either alone is much weaker: a band narrowing onto the *wrong* q is the wrong geometry, and one sitting at the right q that never narrows is not filtering.

  **The first version was wrong for a reason worth keeping.** Its stability criterion was "did the ion strike a rod within twenty RF cycles". Near the low-q edge the instability is weak and takes far longer than that to grow past the inscribed radius, so it **called the whole low-q region stable** and the bracket had no edge in it — the search correctly refused, saying both ends were inside. The criterion has to be reaching the *detector*, and the window has to be the transit time: **a stability test whose window is shorter than the instability's growth time measures the window.**

  **What Class B still lacks needs an arbitrary waveform, not more analysis.** The secular frequency spectrum and isolation efficiency against notch width are §12 items that §9's arbitrary-waveform excitation would unblock; this build has sinusoid and rectangular only.

- **GAS-1: the gas can vary from place to place.** A single declared vector is a stream, not a jet. Spec figure 4 requires a velocity **field** above 1e-2 mbar and §21 lists "gas velocity import" among Phase 3's deliverables, and GAS-1 says why in its own words: the neutral jet off an inlet capillary "drags ions and frequently dominates the axial DC gradient", and it is not uniform across a ring stack.

  **VTK ImageData, which is what this engine already writes** — no format to decide and no dependency to take, because reading a *format* carries no licence obligation while linking a library would (RND-13). The path resolves against the **model document's own directory**, not the working directory, so a model means the same thing wherever the command is run from. **ASCII only, stated rather than discovered**: binary, appended and compressed payloads are most real VTK files and none is read, so such a file is refused by name with the ParaView setting that fixes it — the same kind of deliberate subset as EXT-7's JSON Schema one.

  | Check | Result |
  | --- | --- |
  | An imported *uniform* field against a *declared* uniform one | agree to **2 ulps** |
  | Trilinear against a linear field | exact, 1e-9 over the box |
  | An accelerating flow against uniform at each end | strictly between, both ways |
  | A file this engine wrote, read back | every node exactly |

  The first is what makes the import trustworthy: two entirely separate paths to the same gas — a vector in the document, and a file read, interpolated and sampled per node — give the same answer. **Two ulps rather than bit-identical, and the reason is worth knowing:** interpolating a constant returns that constant only to rounding, because 30(1−f) + 30f is 29.999999999999996 for plenty of f. Inherent to sampling, not fixable in a reader.

  **A caller that cannot resolve the path is refused, not run in a still gas.** Resolving needs the model file's directory, and a study or a figure of merit meets the transport without one — which is precisely the shape of the bug where `driftVelocity` was honoured by the event-driven mode and silently dropped by the diffusive one. **The overhang is reported rather than absorbed**: outside the imported extent the edge value is continued, right for a stream and wrong for the end of a jet, and the samples do not say which, so `gas.flow-imported` states what fraction of the tracked region was extrapolated. One bug found writing it, in my own overhang arithmetic: the helper conflated a **flat axis** (a 2-D import makes no claim about z, so it covers all of it) with **no overlap** (covers none), so a box far outside the field reported itself as fully covered.

  **Einzel consumes a velocity field and does not compute one** — the same boundary §17 draws around visualisation. A compressible flow through a differentially pumped stack is a CFD problem, and a half-hearted one inside an ion-optics engine would be worse than none because it would look like an answer.

  **Both gaps named here are now closed** — the event-driven mode carries the ion's position into the draw, and the pressure is a field too. See below.

- **A clamp that guarded nothing and capped the drift, found by an expectation that was a division.** Scharfetter–Gummel's exponent `P = v h / D` feeds a Bernoulli function that already handles a large argument **exactly** — zero above +40 and `−x` below −40 are the true limits, taken explicitly to avoid an overflow inside `exp`. The flux clamped `P` to ±40 *before* calling it. Read together the clamp looks like it protects the exponential; it does not, the exponential protects itself one function down. What it did was **cap the effective drift at `40 D / h`** whatever the field and the gas actually were.

  | | |
  | --- | --- |
  | Cell Péclet on the corpus drift tube, field alone | 25.4 |
  | The same with a 120 m/s gas flow | 42.3 |
  | Closed form `L / (μE + v_gas)` | 126.7 µs |
  | Measured, clamped | 135.1 µs, **6.7% long** |
  | Measured, unclamped | 127.8 µs, **0.86% long** |

  The 0.86% is the packet's own spread, and what makes that convincing is that it is now **the same 0.86% with and without the flow**: a residual independent of the drift speed is a packet effect, one that grows with the drift is a scheme effect, and only having both cases separates them.

  **Every advection test in the suite runs at cell Péclet 16, below the cap, and reports 1.000000.** They were correct and could not see this. What saw it was a corpus example whose expected number is a *division* — a drift tube with a **declared** mobility and a declared gas flow, so there was nothing for the engine to agree with itself about. The suite now has a case at cell Péclet 105 and 209, exact to a part in a million. The stability step needed no change: the Courant limit was always taken against the true drift, so removing the cap makes the flux agree with the step rather than sitting conservatively under it.

  **Three rules generalise**, all in `docs/lessons.md`. A guard placed one level above the thing that already guards itself is not redundant, it is a second policy, and the outer one wins silently. **A test whose parameter sits below the threshold of a bug is not a weak test, it is a test of a different regime** — where a scheme has a dimensionless number in it, the tests should straddle the values that number switches behaviour at, and should print it. And an expectation that is arithmetic the engine had no part in catches a class of thing self-consistency cannot, which is the whole argument for EX-1's corpus.

  **Corpus 17 → 20**, and the diffusive mode has examples for the first time because `transitTime` made its principal scalar assertable: `drift-tube-diffusion` against `L/(μE)` at 0.86%, `drift-tube-gas-flow` against `L/(μE + v_gas)` at the same 0.86%, and `slit-transmission` against **erf(a/σ√2) = 0.68269 at 0.17%** — the slit's jaws both grounded so the field is exactly zero and the transmission is pure geometry.

  **And a flaky test made diagnosable.** `AllocationDoesNotGrowWithStepCount` failed inside the full parallel suite and passed alone, which is the worst way for a test to be wrong because it reads as a regression in the thing under test. It now takes the **cheapest of five runs** — the runtime charges one-off costs like a tier-1 recompilation to whichever window they fire in, and the property is a floor, so the minimum is the right statistic — and prints the numbers: 240 bytes over 41 steps, 240 bytes over 2030.

- **`multipole-guide` — every even order in one file, and the overlapping rods that found a gap.** LIB-1's test run deliberately: what does a multipole above four rods cost below `Einzel.Library`? **One function, and it was general.** A 2n-pole is 2n rods at π/n intervals, and the expression grammar had **no trigonometry** — so that geometry could not be written at all. Not awkwardly, not verbosely: not at all. The choice was three near-identical template files with coordinates longhand, or `cosPi`/`sinPi` in the grammar.

  **Half turns rather than radians**, the convention the drive decomposition already chose and for the same reason: `Math.Cos(Math.PI/2)` is 6.1e-17, so a rod at a quarter turn lands a hair off axis and the multipole carries a spurious dipole made of rounding. `cosPi(0.5)` is exactly zero.

  | poles | electrodes | basis solves | cycles | convergence |
  | --- | --- | --- | --- | --- |
  | 4 | 4 | **1** | 8 | 0.0262 |
  | 6 | 6 | **1** | 8 | 0.0285 |
  | 8 | 8 | **1** | 8 | 0.0236 |
  | 12 | 12 | **1** | 8 | 0.0257 |

  **Twelve rods cost what four do**, because adjacent rods are exact negatives however many there are. Exact negation is what does it — which is why the amplitude is `rfAmplitude * (1 - 2 mod(pole, 2))` rather than a cosine of the pole index: the second would be right to a rounding and would split into two channels.

  **The rods have to fit, and now they cannot not.** `rodRatio ≤ sin(π/N)/(1 − sin(π/N))` — 2.414 at four rods, 1.000 at six, 0.620 at eight — so the knob is `rodFill`, a *fraction* of that maximum, and an overlapping geometry is not expressible rather than merely refused. `rodFill = 0.475` reproduces **Denison's 1.1468** at four poles through the derived chain, which is a sharp check on `sinPi` as well as on the geometry.

  **A gap found by getting that wrong first.** Applying Denison's *quadrupole* ratio to six rods puts them through one another, and **the engine solved it, converged in eight cycles, and returned a field** — the acceptance measurement taken from it was really a measurement of rods closing in on the axis. A Dirichlet mask is written electrode by electrode, so where two overlap the last one wins; where both hold the same thing that is harmless and often deliberate (a fillet is built that way), and where they **disagree** the region is simultaneously at +300 V and −300 V and the field is of a geometry nobody described. `ElectrodeOverlap` now refuses that, naming both electrodes and what each holds, with three deliberate limits — tangency allowed, agreement allowed, edge profiles skipped rather than guessed at.

  **What is deliberately not claimed:** whether a higher order accepts a larger offset. The template launches on a 45° diagonal, which for a quadrupole is the *widest* gap between rods — an ion enters at r = 4.95 mm and still arrives, outside the 4 mm inscribed radius — and for a hexapole is not, so the comparison measures the angular gap at least as much as the order. Measured anyway for the record: at 200 V the hexapole accepts 0.68 r₀ and the octupole 0.58, and at 300 V that **reverses** to 0.46 and 0.48. A non-monotone ordering that flips with amplitude is a sign the scanned variable is not the one that matters; settling it needs a scan over launch *angle* and an acceptance defined as a solid angle rather than one ray.

- **`paul-trap` — the 3-D quadrupole trap, a figure of merit that is not an arrival, and a boundary that was not one.** **THIS ENTRY'S NUMBERS WERE MEASURED ON FLAT ANNULI. The electrodes are hyperboloids now — see the rebuild entry at the end of this list and `docs/device-templates.md` for the current figures.** A driven ring between two earthed endcaps on the axis of rotation. **Axisymmetric, so it is a half-plane solve rather than a volume** — SYM-1 is what makes a three-dimensional trap cost what a cross-section costs — and because the endcaps are earthed, three electrodes reduce to **one basis solve** (10 cycles, factor 0.0587).

  **A trap cannot be measured by anything that counts arrivals.** A trapped ion never arrives anywhere, so a transmission reads zero for a trap that works and zero again for one that lost everything. `confined` is the complement — still inside at the end of the hold, having struck nothing and reached no detector — and the model puts its detector **outside** the trap so the three outcomes stay distinct: struck, escaped, held.

  | | |
  | --- | --- |
  | Ejection boundary, 0.3 mm launch, 200 cycles, 128 × 64 | **672–674 V**, q_z = 0.8218–0.8236 |
  | The same at 256 × 128, and at 800 cycles | **unchanged** — mesh- and hold-converged |
  | Tabulated Mathieu, a = 0 line | q_z = 0.90804 |
  | Where the ion is lost | an **endcap**, at exactly ±z0 |
  | Effective r0 from the solved field | **3.8195 mm** against 4.0000 declared |
  | Boundary a scale factor alone would predict | **677.5 V**, q_z = 0.828 |

  **Most of the 9.4% shortfall is one number, and the rest is not.** Flat annuli at the nominal radius lie *inside* the hyperbola sharing their vertex everywhere except at that vertex — at z = 2.23 mm the ring hyperbola would be at r = 5.09 mm and this ring is at 4.00 — so the field at the centre is stronger than r0 implies, the effective radius is smaller, q per volt is larger, and ejection comes at a **lower** amplitude. That accounts for the sign and for 0.828 of the 0.908. The same samples give the anharmonicity free: dEz/dz ÷ dEr/dr is exactly −2 wherever the quadratic dominates, and drifts −1.9867 → −1.9461 as the sampling radius doubles. **A hyperbolic trap would hold −2 everywhere by construction**, so a departure *growing with radius* is the higher multipole flat electrodes buy — and that growth is what the test asserts, because a departure that did not grow would be discretisation or a bug.

  **And the ejection edge is amplitude-dependent, which an ideal boundary cannot be** — q_z = 0.85 at a 0.1 mm launch, 0.82 at 0.3 mm, 0.64 at 0.6 mm, all hold-converged (800 and 2000 cycles agree at both offsets). The Mathieu equation is linear, so a trajectory scaled by a constant is another trajectory. My first write-up said the scale factor "closes to under 1%" on the strength of the 0.3 mm offset alone, which is the mistake of quoting a number whose controls have not been run.

  **The reason is structural, and the fix is to stop measuring it that way.** A boundary found by asking *did the ion reach an electrode* requires the ion to cross the whole anharmonic region, so it is never a small-amplitude measurement whatever it was launched at: a small launch survives past the linear edge because the anharmonic frequency shift halts the growth before z0, and a larger one is lost below it.

  **β needs no journey, and it settles it.** Calibrating β(V) against Mathieu over a range where the ion stays small — β is amplitude-independent to 2e-3 there, and measurably shifted at high q, which is the control — fits one number to a worst residual of **1.2e-3** and gives an effective radius of **3.8137 mm** against **3.8195** from the field's curvature with no ion involved. Two routes sharing nothing but the solved field, **0.15% apart**, and in the direction the curvature's own δ-dependence predicts. That puts β = 1 at **675.5 V, q_nominal 0.8254**, which the two ejection edges **bracket**: 665–670 V at 0.3 mm, 695–700 V at 0.1 mm.

  **So the 9.4% shortfall is one geometric factor, measured three independent ways.** A caveat about the tool, though: the endpoint is anchored to the *tabulated* 0.90804 rather than to the continued fraction used elsewhere here, because that expansion has a near-singularity exactly at β = 1 — its n = 1 denominator (β−2)² goes to one — and puts the crossing at 0.9117, four parts in a thousand off. It is accurate at β from 0.3 to 0.8 and not at the endpoint.

  **Two quantities, not one, and only one is the design parameter.** A *linear* stability boundary is where β reaches one and is a property of the field; an *ejection* threshold is where a particular ion launched a particular way leaves within a particular hold, and is what an instrument does. Conflating them is what made the first account confusing.

  **A resonance band inside the stable region, found by the confirmation walk on its first real use.** 605–614 V (q_z = 0.739–0.750), sixty volts below the main edge. Identical at 256 × 128 and at 400 cycles; **gone** at 60 cycles (so the growth is slow and secular, not exponential) and **gone** at a 0.1 mm launch (so it is driven by the higher multipoles). That combination is a nonlinear resonance and nothing else. **Which** resonance is not established — β_z there is 0.615, landing on no n_zβ_z + n_rβ_r = 2 up to order six — and it is recorded as measured rather than explained. The bisection that found the main edge reported nothing unusual, and structurally could not have.

  **The finding worth keeping: a stability boundary is not a property of the design alone.** At 60 RF cycles the ejection edge is a ragged strip — held at 674, lost at 676 and 678, held at 680, lost at 682, held at 684, solid loss from 690. At 200 cycles the same scan is a clean step between 672 and 674. Nothing about the design changed: the growth rate goes to zero at the edge, so whether a marginally unstable ion reaches an electrode inside the hold is a property of *the hold*. Two bisections over brackets differing only at their lower end gave **680.7 V and 694.4 V for the same geometry**, which is how it was noticed at all.

  **So `einzel boundary` now checks its own premise.** Every step of a bisection is consistent with a single crossing *by construction*, so a clean edge and a frayed one produce identical histories and the result looks equally confident either way. It now walks outward from the converged bracket at geometrically growing offsets asking whether the predicate flips back — about log2 of the range over the bracket width, roughly doubling an 11-evaluation search against a grid's 501. `boundary.multiple-crossings` is a **validity violation**; the confirmation is reported with its probe count whether or not anything was found. Two limits stated rather than papered over: the walk stays inside the declared range, and its first step is the bracket width, so a flip narrower than that is stepped over.

  **What it cost below the library was a bug, not a capability — and it is the third time the same mistake has been made.** `ModelValidator.CanDoWork` decided whether a source may start at rest by asking whether any electrode held non-zero **DC**. A Paul trap holds zero DC and all of its potential as drive, so the archetypal start-at-rest device was refused as one in which nothing could move an ion; the 3-D arm of the same switch inspected nothing at all and passed by default, the same bug wearing the opposite mask. `einzel solve` had already been wrong this way twice — reporting the DC pattern for every driven 2-D geometry, and answering `converged: true` over an empty list of 3-D elements. **Reading only the DC of a driven electrode is a recurring mistake here**; grep for `.Potential` the next time something driven behaves as though it were earthed. Recorded in `docs/lessons.md`.

  Also fixed while here: **`InferProjectRoot` fell back to the working directory**, so a study kept outside any project wrote `results/` into whatever tree the caller happened to be standing in and said only "wrote results\x.json". It now falls back to the file's own directory.

  Corpus 21 → 23 (`paul-trap-held` at q_z = 0.30, `paul-trap-ejected` at q_z = 1.20 — bracketing the published boundary from both sides, deliberately wide, because an example that pinned the edge would be pinning this geometry's edge and calling it Mathieu's). Details in `docs/device-templates.md` and `docs/optimisation.md`.

- **The secular frequency spectrum, and the resonance it named.** §12 lists it as a Class B figure; the reason to build it now was that the trap above had a loss band nothing could identify. **A nonlinear resonance is *defined* by a frequency condition** — `n_z β_z + n_r β_r = 2` for a multipole of order n_z + n_r — so a scan over amplitude can establish that a band exists and can never say what it is.

  **Lomb–Scargle rather than a DFT, and that is the load-bearing choice.** A trajectory is sampled at accepted integration steps, which cluster where the physics is hard — `TrajectoryRecorder` working as designed — so the series is **not uniform**. A DFT would need it resampled first, which is inventing values the integrator never computed and then measuring them. Lomb–Scargle is the closed-form least-squares fit of a sinusoid at each trial frequency and needs no such step.

  Checked against the Mathieu characteristic exponent — a continued fraction in a and q, evaluated **in the test** rather than shipped, because comparing the engine's β to the engine's spectrum would be testing self-consistency:

  | q | β | expected | measured |
  | --- | --- | --- | --- |
  | 0.10 | 0.070850 | 35.425 kHz | 35.374 kHz (−0.144%) |
  | 0.30 | 0.216059 | 108.030 kHz | 108.037 kHz (+0.007%) |
  | 0.50 | 0.373744 | 186.872 kHz | 186.937 kHz (+0.035%) |
  | 0.70 | 0.563066 | 281.533 kHz | 281.500 kHz (−0.012%) |
  | 0.85 | 0.772950 | 386.475 kHz | 386.507 kHz (+0.008%) |

  **The sidebands are the sharper check**: finding the lowest line right says the slow motion has the right frequency, but finding the drive split into a *pair* straddling it — 813.19 against 813.13 kHz, 1186.94 against 1186.87 — says the motion has the form Mathieu's solution gives. The reported uncertainty is 1/T, because a record of finite length cannot locate a line more finely however fine the trial spacing; `spectrum.short-record` and `spectrum.peak-at-band-edge` say when that bites.

  **The band is the octupole.** β_z = 0.6769, β_r = 0.3225, so **2β_z + 2β_r = 1.9989** — order four, met to 0.055% at the band centre and a hundred times worse either side. Nine candidate conditions are searched and one is always nearest, so what makes this an identification rather than a fit is that **order four was predicted in advance**: the trap is symmetric about its centre plane and about the axis, so every odd multipole vanishes and four is the first available. It is independently corroborated by the field, where the curvature ratio departs from −2 by an amount that grows with radius.

  **And the effective radius is now confirmed twice over, from routes sharing nothing but the solved field** — the curvature at the centre with no ion involved (3.8195 mm), and the secular line of a flown ion against Mathieu at q × (r0/r0_eff)². They agree to **0.02% at q = 0.27**, 0.04% at 0.40, 0.28% at 0.54 — and diverge to 3.1% at 0.80, which is the other half of the same statement rather than a failure: the trap is an ideal quadrupole of radius 3.82 mm *to the extent the ion stays small*. That is why the stability boundary cannot be predicted from the effective radius alone — a boundary measurement requires the ion to travel all the way to an electrode, through the anharmonic region.

  **The ideal-Mathieu prediction was wrong and had to be**: β at the *nominal* q of 0.745 is 0.6156, which satisfies no low-order condition. Working in the nominal geometry rather than the solved one is what made the resonance unidentifiable in the first place.

  Exposed as `secularFrequencyX|Y|Z`, so a study can scan or optimise against it. Refused with `secular.no-drive` for a static field — a secular frequency is the slow oscillation in an RF field's *effective* well, and a static field has no such well. The band searched is 2–90% of the drive, taken from the field's own shortest period and stopping below the drive deliberately, because micromotion at the drive is the largest line in most spectra and reporting it back would be reporting the input as a discovery. Details in `docs/validation.md`.

- **The arbitrary waveform, and the last Class B figure.** §9 lists an arbitrary waveform among the excitations an electrode may carry; §12's isolation efficiency against notch width cannot be measured without one. It is a **Fourier series**, which is not a restriction — every periodic waveform is one — and is the natural spelling for the thing being designed: a notch is a list of harmonics with a gap in it, where a sampled table is a waveform someone has to inverse-transform first. It is also smooth by construction, where a table is piecewise something and a jump in the drive is a jump in the acceleration. **One basis solve however many harmonics**, since a sum of harmonics is still one scalar function of time.

  **Three reductions it must satisfy, and it does.** A single order-one term *is* the sinusoid (6.1e-16 over 721 phases). A half turn of phase is *exactly* antiphase where the argument is representable (exactly 0 at phases k/16; 1.0e-14 at arbitrary phases across orders 1–17 — the honest limit, since `2(nt + ½)` is itself rounded and the error grows with order). And the Fourier series of a square wave converges on it with textbook Gibbs — interior 0.182 → 0.051 → 0.013 at 5/20/80 terms while the **edge overshoot holds at 0.182/0.179/0.177**, which is asserted too, because a series showing no overshoot would not be a Fourier series.

  **The literature check**: the 80-term series recovers the published digital-mass-filter cut-off by the same bracket as the direct rectangular wave — last through 0.710, first lost 0.715, containing Schrader/Anderson/Russell's 0.712. That is what says the path *drives an ion* rather than evaluating to the right numbers. **A mistake worth keeping**: the first version wrote the series with zero phase, but a square wave's series is a **sine** series — so it was a square wave shifted a quarter cycle, which converged perfectly well and moved the cut-off to ~0.703. A reduction that converges to the *wrong thing* is worse than one that does not converge.

  **Isolation efficiency against notch width, measured.** A mass axis is a frequency axis (q goes as 1/m), so a notch in frequency is a window in mass: with the target at m/z 500 and a notch over comb orders 71–75, masses 490/500/510 survive and 420/460/545/600 are ejected. **The trade needs two amplitudes to show both arms** — at the amplitude that just ejects a resonant ion the narrow end is free and efficiency is monotone (1.00 → 0.50 → 0 → 0); at three times that the narrow end loses the target and an **interior optimum appears at half-width 6, efficiency 0.75**. A study run at one amplitude reports the wrong shape and is internally consistent.

  **Two scales that had to be derived, both first got wrong by orders of magnitude.** The comb spacing must equal **1/T** — a resonance excited for time T has width 1/T, so a wider comb has *holes* and an ion between lines survives; at 5 kHz against a 333 Hz width the notch width toggled every ion at once. And the amplitude follows from `E = 2amω/qT`, 76 V/m here; 300 V/m ejected everything at every width. **An amplitude picked to make a demonstration work is a demonstration of the amplitude.**

  **And a defect found on the way, the third of its exact shape.** `SuperposedField` implements only `IElectrostaticField`, and a driven member answers that interface at t = 0 — so **a driven element summed with anything else silently became a snapshot of the RF at the top of its cycle**, with no exception and nothing in the result to say so. The diffusive mode was found doing this, `einzel solve` was found doing it, and now this. The class is: **a time-varying quantity reached through a time-free interface does not fail, it answers at an arbitrary instant.** Fixed structurally — `FieldAssembly` picks `DrivenSuperposedField` when any member is driven, so the composition is chosen by what it contains rather than by what the caller asks for. Step control follows: the shortest period is the minimum over members, and `OscillatingUniformField` reports its **highest harmonic's** period rather than its fundamental's, since a comb reaching order 120 carries information 120× faster than its repeat rate.

  **What it does not reach: the model format cannot declare a supplementary excitation.** A `solve` carries one `drive` with one frequency, so the notch measurement runs on the analytic quadrupole. Same limitation the travelling-wave guide has. The fix is **a drive per supply rather than per solve** — the decomposition already groups electrodes into supplies by spatial pattern, and a supply is exactly the thing that has a frequency, so it costs one level of schema nesting and nothing in the solver. Details in `docs/validation.md` and `docs/model-format.md`.

- **Several generators on one geometry — and the design note that had to be retracted to get there.** `CompiledDrive` said, from the beginning: *"one drive per solve ... modelling it the other way round would let a document declare two frequencies on one structure — which is a different instrument and almost always a mistake."* **It is not a mistake, it is what a trap is.** A real travelling-wave guide superposes a fast confining RF on a slow travelling wave; a stored-waveform isolation runs a notched comb across the endcaps while the ring carries the main drive. The rule cost the shipped guide its radial confinement and forced the notch measurement onto an analytic quadrupole.

  A `solve` now declares `drives` and each electrode `taps` them **by name**. Both spellings coexist — `drive` and `driveAmplitude` stay the short form for the common single-generator case — and **declaring both is refused rather than merged**, because a document saying a geometry has one drive and also three is not a document with a default to fall back on.

  **It cost nothing in the solver, which is the part worth keeping.** Basis superposition is indifferent to what the weights are functions of, so two generators reaching the same electrodes in the same proportions are **one solved pattern carrying two weights on two clocks** — exactly as a DC supply and an RF supply already were. Measured on the guide: **24 rings each tapping both generators → 3 basis solves**, two for the wave's phase ramp (a sinusoid collapses to a fixed quadrature pair however many rings) and one for the alternating confinement. The quadrature collapse is now decided **per generator**, since an instrument may run a sinusoidal confinement and a switched excitation at once.

  **It does change step control**: `ShortestPeriodSeconds` is the minimum over generators, so the guide reports **333.33 ns** — its 3 MHz confinement's period, not its 0.5 MHz wave's.

  **And the negative result is worth as much as the capability.** Giving the guide its confinement did **not** widen the acceptance at any amplitude tried — 5 of 12 entry radii arrive with none, then 2 / 4 / 3 / 1 at 100 / 200 / 400 / 800 V, and 1 at half the frequency. The window is narrow at both ends: above ~200 V on this ring pitch the confining drive's own Mathieu q passes the stability limit and the ion is **ejected**, while below it the well is shallow against a 60 V wave and the alternating field decays as exp(−2πr/pitch) so little of it reaches the axis. **The template ships with the confinement at zero volts** — a default that makes a device worse is worse than no default. What the tests assert is that the generator *reaches* the ion (acceptance differs with it on), which is the claim the capability supports; whether a working point exists is a two-dimensional study in wave and confinement amplitude.

  **A statistic that had to be replaced on the way**: "widest entry radius that still arrives" gave 0.65 mm on one radius grid and 0.20 mm on another for the same geometry. A maximum over a ragged set is a maximum over noise; counting arrivals over a fixed grid is the same measurement made stable.

  **Now done for 3-D too**: a `solve3d` takes `drives` and its electrodes take `taps`, so a volume geometry expresses what a cross-section already could. **Shared rather than reimplemented** — both electrode documents implement one `ITappedElectrode` interface and the tap validation is one function, so the refusals for declaring both forms arrived in three dimensions by *being* the same code. Verified on a volume geometry: two generators reaching the same electrodes in the same proportions collapse to **one** basis solve carrying two weights on two clocks, and two distinct spatial patterns give **two**. Details in `docs/model-format.md` and `docs/device-templates.md`.

- **A gas that moves, in the event-driven mode — GAS-1's last transport gap.** The diffusive mode could see an imported velocity field; the trajectory models **refused** one, and refusing was right at the time: a collision was drawn from a time and a velocity with no place to evaluate the flow at, so the alternative was a run that used the uniform drift and said nothing. The change is one argument — the ion's **position** goes into the draw, so a collision samples the Maxwellian about the bulk velocity *where the ion is*.

  **Checked against `u + μE`**, which is a closed form the engine has no part in, and taken as a *difference* so it cancels the collision model, the cross section and the temperature:

  | | along the flow | across it |
  | --- | --- | --- |
  | still gas | −5.405 m/s | 1005.209 m/s |
  | moving at 120 m/s | 114.595 | 1005.209 |
  | **difference** | **120.000** | **−0.000** |

  And the control: a `UniformGasFlow` and a declared `driftVelocity` are the same gas said two ways, so on the same seed they must give the same *trajectory* rather than the same average — **1e-9**. A stepped flow (still below a plane, 200 m/s above) gives **204.5 m/s** of carry across the step.

  **The trap that removing a refusal set, and it nearly shipped.** The trajectory path built its gas with `BackgroundGas.FromModel`, which does **not** resolve a declared `velocityField` — only the diffusive path called `GasFlowImport.Resolve`. Lifting the refusal without also resolving there would have reintroduced *exactly* the failure the refusal existed to prevent: a model declaring a jet, flown as though the gas stood still, silently. **A guard is removed correctly only when the thing it guarded against is checked for directly.**

  **And two things the sampler knew that nobody read.** `BoundExceeded` and the new `SampledOutsideFlow` were computed and consumed by nothing — the third time evidence about a computation's own quality has been dropped at a seam here, after `FieldAssembly.Build` discarding its `SolveReport` and the sweep evaluator discarding its warnings. Both now reach the result: `collisions.rate-underestimated` (a biased collision rate looks exactly like a correct one) and `gas.flow-extrapolated` (outside the imported box the flow is the edge value continued, which is right for a stream and wrong for the end of a jet).

  **A measurement mistake worth keeping**: the stepped-flow test first put the step 3 mm from the launch, which the ion crosses in 6 µs — so the "before" average was three samples of an ion still accelerating from rest, and the carry read 361 against a declared 200. That looks like a physics discrepancy and is a launch transient. **An average is over whatever the window contains, including the part that is not yet the thing being measured.**

  Details in `docs/pressure.md`.

- **The pressure is a field too, and mobility goes as 1/n — which nothing here did.** The density was the last quantity about a gas held as a single number for a whole model, so an imported flow gave the neutrals a velocity everywhere and *the same number of them everywhere*. That is not a differentially pumped instrument, which is what every device above 1e-2 mbar actually is: a funnel behind an inlet capillary spans decades of pressure between entrance and exit.

  **The unit is required on the file, and that is §9's rule rather than a new one.** A CFD velocity is metres per second essentially always; a pressure is not, and **a file read as pascals when it holds mbar is a gas a hundred times too thin** — entirely plausible, and it never announces itself. `{"energy": 4000}` is a validation error for exactly this reason, and nothing in that argument weakens when the number becomes a hundred thousand numbers.

  **The physics that was missing:** an ion drifts further between collisions in a thinner gas, so **μN is the constant** — which is why the literature tabulates *reduced* mobility. The declared `pressure` becomes the reference the mobility belongs to and the field grades away from it. Two separate density dependences, and they are not the same one: this factor is how *much* gas, the existing E/N expansion is how hard the ion is pushed *between* collisions. Scaling only the second leaves the drift flat across a gradient while reporting a changing field dependence, which reads as the mobility having been handled.

  **A graded density turns Langevin into a null-collision method** — the same mechanism hard spheres already ran for a speed-dependent rate, reached a second way: schedule at the highest density anywhere, accept with probability n(x)/n_max. Both bounds are majorants over the whole field, because an event is scheduled before it is known where the ion will be when it lands. The thinning is short-circuited where the density is uniform, and that is **load-bearing rather than an optimisation**: it would otherwise accept with probability exactly one and *still consume a random draw*, moving every seeded result this engine has published.

  | | |
  | --- | --- |
  | A field at 2× declared pressure vs *declaring* 2× the pressure, event-driven | **bit-identical trajectory**, both collision models |
  | The same, diffusive, through the CLI | 3515.229021382981**5** vs **1** µs |
  | Mobility at half and twice the reference density | 2.000000 / 0.500000 |
  | The scaled form at the declared density | **bit-identical** to the unscaled one |
  | Langevin thinning at three points of a 4× ramp | 0.25 / 0.625 / 1.00 to 0.01 |
  | 151 existing transport tests | unchanged |

  **Two tests that had no teeth, found by running the mutation rather than by reading them.** The equivalence test used Langevin only, and making the local density read return the declared scalar *did not fail it* — the Langevin branch short-circuits its thinning where the density is uniform, so a flat imported field never reads a position at all. Correct behaviour, and no test of the read. The graded-gas test asserted only that a ramp collides more than the thin gas alone, which still passes with the density read at the wrong place because the count lands *close to* the thin gas. What discriminates is **reversing the ramp**: same densities, same box, opposite arrangement, so anything blind to position gives the two an identical count. 11,458 against 19,700. The rule that generalises: **a test passes a mutation when the path it exercises does not contain the mutated line** — so read which tests failed, and treat the rest as untested rather than as corroboration.

  **And the same seam broke a fourth time, in the file whose comment says it is the third.** `SampledOutsideDensity` was added to `CollisionSampler` beside `SampledOutsideFlow` and, on the first draft, dropped in exactly the place the surrounding comment warns about — declared, set, read by nothing, everything compiling and every test green while a run extrapolating its gas past the imported box said so nowhere. Now `gas.pressure-extrapolated`, with a CLI test driving it end to end because the wiring is what keeps breaking rather than the computation. **Adding a quantity to a type that already reports several is not the same as reporting it** — the existing reporting code is where the eye slides past.

  **The cost gate had to be re-derived, and the first version was 50% out.** GRD-8's claim for this mode is that `estimate` and `run` call the same step function and agree exactly. A graded gas moves the mobility and so both stability limits — and the first version took the thinnest gas *anywhere in the imported field* where the run takes its limit from per-node arrays *over the tracked grid*. A CFD field is usually solved on a larger box than the ions are tracked through: here it ran to 0.5 mbar while the grid reached 0.75, and the estimate said **2,252 steps against an actual 1,502**. Now 1,126/1,126 uniform and 1,502/1,502 graded. Found by comparing the two numbers, not by reading the code — nothing about the wrong version looks wrong. The same asymmetry runs through the diagnostics and had to be right in both directions: **E/N is worst where the gas is thinnest**, the **Knudsen number and collision counts are worst where it is thickest**.

  **The pseudopotential is graded too.** The momentum-transfer rate goes as the density, so a funnel whose gas thins toward its exit has a well that deepens toward its exit — and the gradient of that is a real force, which differencing the effective potential picks up. A version holding the damping at one declared value would report the well as flat where it is not.

  **A refusal moved to where it cannot be forgotten.** Resolving a declared field needs the model document's own directory, which a study or a figure of merit does not have. Refusing was right, but it lived as a guard at each of **four call sites, naming `velocityField`** — and three were already silent about a pressure field. `BackgroundGas.FromModel` now refuses an unresolved field itself, with `WithoutImportedFields` as the deliberate exception whose name says what it gives up. It caught a real one immediately: `GasFlowImport.Resolve` reached `FromModel` itself, so `einzel run` on a diffusive model was refused by the guard meant for callers without a path. Two sites that *did* have the path — `einzel compare` and the diffusive cost estimate — now resolve rather than refuse, which they should always have done. Same rule as `FieldAssembly.Build` throwing rather than discarding its `SolveReport`: **make the shortest spelling the safe one.**

  **Also caught by its own test:** an earlier draft of the CLI test ran at 50 V/m over 38 mm, collected **0.05 ions of 10,000**, and compared two flight-time ceilings — the incomplete-arrival trap this project already documents for `einzel compare`, met again from the other side. The transit accessor now asserts the packet arrived before reading a transit off it.

  **The corpus can carry a data file now, and does.** The embedded-resource glob was `*.json` only, so neither imported gas field could appear in an example — and so neither was covered by the EX-2 gate that runs on every change. `drift-tube-pressure-gradient` is a 38 mm tube whose gas thickens from 1 mbar at the packet to 2 mbar at the detector, and **its expected number is an integral**: the transit is the uniform answer scaled by the *mean* density along the path, which for a linear ramp is the average of the ends. Predicted 316.667 µs, measured **320.236, 1.13% out** — the packet's own spread. Ignoring the gradient gives 211 µs, a third away. What it deliberately cannot see is the *arrangement*, since a drift transit depends only on the integral along the path and any reflection gives the same answer; that is pinned separately by the reversed-ramp unit test. Corpus 29 → 30.

  **It needed one change below the corpus, and that is the more useful half.** `einzel test` could not test a model with an imported field at all — the seam between a study and the transport is a `Func<CompiledModel, double?>` with nowhere to put a path, so a figure of merit met `BackgroundGas.FromModel` and was refused. A compiled model now carries **`SourceDirectory`**, set by every loader, so any consumer can resolve a referenced file. **Null stays the safe value**: a model compiled from a string has no directory and its consumer is refused rather than run in a gas the document does not describe, so a loader that forgets degrades to the refusal rather than to a silent wrong answer. **And the four study drivers take it too**, so a sweep, scan, optimisation or boundary search over such a model runs rather than refusing — §13's whole subject is a design being optimised, and a device with a gas jet through it is exactly the kind that wants it. The warning survives that seam: the ledger reports `gas.pressure-imported` with its per-evaluation count.

  **Still assumed: one temperature.** What is imported is a pressure field read as a density through n = p/kT at the model's single declared temperature — an assumption the document already made, but now the only thing about a gas that cannot vary from place to place. A **non-positive sample is refused rather than clamped**, because mobility goes as 1/n: a zero is an infinite drift and a stability limit of zero, so the run does not answer wrongly, it never finishes. Details in `docs/pressure.md`.

- **Corpus 23 → 26, exercising what the night built.** Three examples, each with an expectation that is arithmetic and nothing else.

  **`gas-flow-carry`** — no field at all, a gas streaming at 200 m/s down a metre of tube. Collisions drive the ion toward the frame the gas is at rest in, so the steady drift *is* the gas velocity and the transit is L/u = **5000 µs by arithmetic**; measured 4904.5. The ion is launched at exactly 200 m/s (0.103642697 V for m/z 500) so there is no equilibration lag either. **The ten per cent tolerance understates how discriminating it is**: with the flow ignored the same ion damps to rest and covers **15.8 mm in twenty milliseconds** instead of arriving.

  **`travelling-wave-capture` and `travelling-wave-ballistic` are a pair, and neither is worth much alone.** Injected at *half* the wave speed: with the wave on the transit is the distance over the **wave's** speed — 27 mm / 3000 m/s = 9.0 µs, measured 8.697 — and with it off, 27 mm / 1500 m/s = **18.000000 µs exactly**, because a guide with no amplitude is field-free and the analytic drift is exact. A transit matching the wave in one case and the injection speed in the other would be a coincidence twice over; a transit matching the wave *whatever* the injection speed is capture. That is the distinction an earlier version of this measurement got wrong by comparing two captured transits to each other.

  Remaining for EX-1: an MR-TOF, a thermalisation, and a three-dimensional geometry.

- **A review of the night's own work, and the defect it found.** Adding a second generator turned `CompiledElectrode.DriveAmplitude` from *the* amplitude into *the first tap's* amplitude, and left it as a property with the same name. Everything kept compiling. One reader was `ElectrodeOverlap.Agrees` — the check that refuses two conductors occupying the same space at different excitations — so **two electrodes agreeing about the main RF and differing about a supplementary one were judged identical**, and the Dirichlet mask kept whichever was written last. The one check that exists to prevent a field of a geometry nobody described had become a route to one.

  Now compared over **every** tap, with order significant (the conservative reading: two electrodes whose taps are the same set in a different order really do hold the same thing, so refusing them costs a spurious complaint rather than a silent wrong field). **Three tests, and all three were run with the fix reverted** — two fail with the bug restored, which is what makes them tests of the bug rather than tests that a file exists.

  Also swept: `CanDoWork` now asks `IsDriven` rather than the first tap's amplitude, and `einzel schema` was checked to carry `drives`, `taps` and `TapTermDocument` — AGT-7 says the format an agent reads cannot drift from the code, and a reflection-generated schema is only as good as the records it reflects over.

  The general lesson, in `docs/lessons.md`: **a convenience accessor that quietly becomes a summary keeps every caller compiling and changes what some of them mean.** After widening a scalar into a list, ask which readers of the scalar were asking a question it no longer answers.

- **The corpus has no 3-D example, and finding out why was worth more than the example.** One was written — a parallel-plate gap checked against `sqrt(2d²m/(qV))`, the same closed form the analytic accelerating-gap example uses. It was not shipped:

  | | cycles | factor |
  | --- | --- | --- |
  | **parallel plates, 2 slabs in a grounded box** | **49** | **0.652** |
  | the shipped segmented quadrupole, 12 rods | 12–13 | 0.08 |
  | a charged sphere, node-aligned coarse levels | 9 | 0.126 |

  **124 seconds** for the plates, against 11 for a whole segmented-quadrupole *run*, and against a gate that does the other twenty-six examples in forty-two. A factor of 0.65 means the V-cycle is barely doing anything.

  **The simplest 3-D geometry anybody would write is the worst case for the documented interior-electrode limitation**, and that is the wrong way round. A rod is thin, so coarsening loses it fast and the pinning fix restores its presence; **a slab is a large solid Dirichlet region**, so a coarse level that half-represents it is solving a different problem over most of the domain rather than a corner of it.

  It was also **3.2% off**, which is the geometry rather than the solver — a finite plate in a grounded box 2 mm behind it is not an infinite capacitor, and asserting V/d to 1% would be asserting that it is. Fixing that needs a *larger* domain, so more solve, not less.

  So the volume solver, its tricubic interpolant and its cut cells are exercised by `Einzel.Fields.Tests` and the segmented-quadrupole study, and **not by the release gate** — a stated gap rather than an oversight. Galerkin coarsening closes it, and would make the plates converge like everything else. In `docs/numerics.md`.

- **Poisson, not only Laplace - the field half of SC-1's approximate method.** Every solve here has been Laplace: a potential with no charge in it, fixed on conductors. Particle-in-cell needs grad2 phi = -rho/eps0. **The cycle already carried a right-hand side and had only ever been handed zeros** - the smoother subtracts it, the residual is defined against it, and the coarse levels get the restricted *residual*, which is what they need whatever the fine source is. One argument, no numerics.

  Checked by **manufactured solution**, the sharpest thing available: pick a potential, differentiate it analytically for the source that produces it, compare. Nothing on the exact side is discretised. With phi = sin(pi x) sin(pi y), Laplacian -2 pi^2 phi:

  | intervals | worst error | order | cycles | factor |
  | --- | --- | --- | --- | --- |
  | 32 | 8.0358e-4 | | 11 | 0.0632 |
  | 64 | 2.0082e-4 | **2.000** | 11 | 0.0659 |
  | 128 | 5.0201e-5 | **2.000** | 11 | 0.0702 |

  **The order is the load-bearing check**: a source entering the smoother and the residual inconsistently would still converge - to the wrong answer - and would show it as an order that is not two rather than as a failure. And a null source gives **exactly** the old Laplace answer in the same cycle count; not nearly, or every number published from a solved field has moved.

  Left for SC-1: cloud-in-cell deposit, the same weights on the gather (or momentum is not conserved), and the comparison against the direct pairwise sum - which exists, and is why it was built first.

- **Cloud-in-cell: the particle half of SC-1's approximate method.** The direct sum costs O(N^2); particle-in-cell costs one solve plus O(N), which is what makes 10^4 macroparticles affordable when 10^3 already takes hours. Charge is conserved **by construction** - the eight weights sum to exactly one whatever the position, so 8.010883e-14 C in is 8.010883e-14 C on the grid, and normalising afterwards would pass the same test while hiding a weighting error rather than preventing one. Charge that leaves the grid is **counted**, not clamped or dropped: a packet off its own grid gives a field quietly too weak, which looks exactly like a packet more dilute than it is.

  **The same weights on the way out, and it is not a convenience.** A particle writes charge to a node with a weight and reads the field back with the same weight, so its own contribution cancels. Measured as a fraction of the field a neighbour one cell away feels, against a nearest-node gather sharing no weights:

  | offset in the cell | matched | mismatched |
  | --- | --- | --- |
  | 0.00 | 8.05e-5 | 0.521 |
  | 0.50 | 1.15e-4 | 0.495 |
  | 0.89 | 1.68e-4 | 0.485 |

  **Three and a half orders of magnitude**, and the mismatched column is what makes the matched one a property of the *symmetry* rather than of the grid being fine. Half the neighbour field felt by a particle from itself is a packet that expands for a reason nobody put in — and a tricubic gather, which is *more accurate* for a smooth field, would do exactly that. **ACC-3 is not violated**: it forbids trilinear interpolation on a *trajectory path*, and this is the interpolation of a self-consistent field whose accuracy the deposit already bounds. The applied field an ion flies through is still tricubic.

  It is **not exactly zero** and the test says so: the cancellation is exact on a uniform periodic grid with centred differences, and an earthed box breaks it slightly through its images. So the assertion is a ratio to the scale that would matter, not a claim of zero.

  A uniform ball of 20,000 macroparticles reproduces Qr/(4πε₀R³) and Q/(4πε₀r²) to 1–8% in 11 cycles at factor 0.110 — the residual being the earthed cube rather than the method, since the closed form is for a sphere alone in space.

  **Left for SC-1:** the integration — which grid a drifting packet deposits onto, when to re-solve, and the comparison against the direct sum on the same configuration. Every piece exists; nothing wires them to `PacketIntegrator` yet. In `docs/numerics.md`.

- **Particle-in-cell, wired to the packet integrator — and an argument of mine that was right about accuracy and wrong about cost.** Both methods are now `ISelfField` peers (positions in, accelerations accumulated out), which is what lets them be handed the same configuration and differenced. SC-1's "validated against" means nothing without that.

  **Three design questions, answered in the code.** The grid is *the packet's own, in the packet's frame* — a packet crossing a metre cannot have a grid over the instrument at any useful resolution — so every deposit and gather is relative to the centroid and **uniform translation is exact** (1e-11 across 250 mm) and free. Because translation is exact, the only thing that ages is *shape*, so the refresh criterion is a fractional change in RMS radius rather than a step count. And the boundary is an earthed box, which a packet in flight is not in; centring it is what keeps that cheap, since a centred distribution induces almost no field at its own centre.

  **The finding.** The cloud-in-cell commit argued that ACC-3's ban on trilinear interpolation does not reach a self-consistent field whose accuracy the deposit already bounds, and that the deposit/gather symmetry buys more than the extra order would. **Right about accuracy, wrong about cost** — a trilinear force kinks at every cell face and an embedded Runge–Kutta estimator reads a kink as error:

  | nodes | steps, linear | steps, quadratic |
  | --- | --- | --- |
  | 16 | 274 | **45** |
  | 32 | 383 | **65** |
  | 64 | 656 | **95** |

  against the direct sum's 25. **The step count tracking the node count is what identifies the mechanism** — more nodes, more faces per unit path; a fixed overhead would not scale. A quadratic B-spline (27 nodes, not 8) is continuously differentiable, is used for deposit *and* gather so the self-force still cancels, and its weights still sum to exactly one for *any* offset — which is what lets the index be clamped at a face without losing charge.

  **Where it starts paying: about 850 macroparticles** — 0.16× at 250, 1.21× at 1000, 3.21× at 2000. Worth stating as a crossing rather than as asymptotics: below it the reference method is simply faster and reaching for the approximation buys nothing.

  **Against the reference**: 0.5% on a flown packet's widening over 2 µs (0.384 → 1.907 mm direct, 1.916 mm grid), about a per cent through the body of a static ball. The outermost radial bin is the worst and has to be — it straddles the surface, where a smoothed deposit and a point-softened sum disagree about a discontinuity by construction.

  **A trade that showed itself**: keeping the box across refreshes needs headroom above the requested padding, and at 1.6× that cut rebuilds from 32 to 4 and cost the surface bin 0.94 → 0.83, because a bigger box at fixed nodes resolves the packet with fewer cells. 1.15× keeps the accuracy at 11 rebuilds.

  **A defect a code review found in exactly this, and the argument of mine that let it through.** The quadratic deposit clamps its three-node stencil onto the grid at a boundary, and leaving the *offset* unclamped with it makes the middle weight `0.75 − u²` **negative** — at the edge the weights are 1.125, −0.25, 0.125. They sum to one, so charge stayed exact and **every test passed at 673 green**; what was wrong is that a positive macroparticle deposited a negative density, and the gather shares those weights. The licensing argument is written down two paragraphs up — "the weights still sum to exactly one for *any* offset, which is what lets the index be clamped at a face without losing charge" — which is true and settles a different question. **Conservation is not positivity.** Clamping the offset too degrades the quadratic shape into the linear one exactly where the third node would leave the grid, which is the right thing for it to do. `NoDepositWeightIsEverNegative` sweeps the whole axis, because the middle is where this cannot happen.

  **Now declarable, and closing that gap broke the agreement claim above.** `"spaceCharge": "pic"` takes an optional `spaceChargeGrid` block (`nodes`, `padding`, `refreshTolerance`), refused against any other method rather than ignored. Two measurements came out of making the knobs sayable, and both matter more than the wiring.

  **A reference method has approximations in it too.** The direct sum softens at the mean macroparticle spacing; the grid smooths at the cell. So "they agree to a few per cent" was comparing two different smoothing lengths that happened to be comparable — agreement there is a coincidence of magnitudes and disagreement would not have been evidence of a defect. The sum has a limit it can be taken to (softening/100, worth **3.5%**) and the grid has a scale it can be set to, so the comparison can be made properly: at a cell of **0.92 mean spacings the two agree to 0.08%**.

  **And accuracy has an optimum rather than a floor** — the opposite of every other resolution knob in this engine:

  | cells per mean spacing | vs the sum's point limit |
  | --- | --- |
  | 3.68 | **−15.1%** |
  | 1.84 | −4.2% |
  | 0.92 | **+0.08%** |
  | 0.46 | **+4.4%** |

  Refining past the match makes it *worse*, and refining is exactly what someone does when they want a better answer. **Confirmed as a sampling artefact rather than a resolution one** by holding the cell fixed at 128 nodes and raising the macroparticle count: 4.42% → 1.55% → 0.93% as macroparticles per cell go 0.012 → 0.049 → 0.195. Below about one macroparticle per cell the deposit stops representing a density and starts representing lumps. `spacecharge.grid-resolution` reports the ratio on **every** run whether or not it crosses a threshold (REG-2's rule on a new quantity), as a validity violation outside 0.7–2.0, and names the node count that would match — computable with no run at all, since the cell and the spacing both scale with the packet radius and it cancels to `2·padding·∛N/nodes`.

  **The estimate was blind to a term that now varies 500-fold.** 200 macroparticles take **0.99 s at 16 nodes and 124 s at 128**; the cost model had one linear-in-trajectories term because nodes were not declarable when it was written. Two terms now — linear in the cloud for the gather, cubic in the node count for the solve — pinned by the measured crossing and a measured 43/57 split, tracking the measured 54× ratio to within 10%.

  **The refresh criterion is a controlled approximation, measured**: +12.68 / +6.16 / +1.01 / −0.54 % as the tolerance tightens 0.30 → 0.15 → 0.05 → 0.02. The sign at the coarse end was *predicted* — a field held across a refresh is the field of a denser packet, so it always pushes too hard — and the crossing to negative at 0.02 is staleness falling below the smoothing difference above, not the prediction failing.

  **A trap that caught me again**: `Grid3D.OverBox` rounds each axis up to a power of two, so 24 and 32 are the same mesh. A first node-count table ran 16/24/32/48/64 and produced two pairs of identical numbers, which reads as insensitivity to resolution over a fourfold range. Already written down for the 3-D solver. Details in `docs/numerics.md`.

- **A driven diffusive run is affordable, and the trade has to be stated both ways.** `"densityStep": { "scheme": "implicit", "gain": 64 }` — backward Euler on the same Scharfetter-Gummel coefficients, solved by red-black Gauss-Seidel. On the shipped funnel at 2 mbar, where the ponderomotive well's gradient at a ring edge sets a 195 ps Courant limit against a 747 ns diffusion limit:

  | gain | steps | sweeps/step | speedup | error |
  | --- | --- | --- | --- | --- |
  | 4 | 6,404 | 3.0 | 1.4x | 0.008% |
  | 16 | 1,601 | 3.0 | 4.7x | 0.028% |
  | 64 | 401 | 3.0 | **10.8x** | **0.108%** |
  | 256 | 101 | 4.0 | 17.7x | 0.427% |
  | 1024 | 26 | 4.9 | 21.4x | 1.673% |

  So 843,000 steps over 900 µs become about 13,000, and a run that took hours takes minutes. **The error is exactly linear in the step**, which is textbook first-order backward Euler and is itself a check that the path is right rather than merely stable.

  **And it does not accumulate over a longer flight — it falls, while the speedup grows.** The same comparison over 50 µs rather than 5 gives **21.1× at gain 64 for 0.057%** (against 10.8% for 0.108%), and **120× at gain 1024 for 0.894%** (against 21.4× for 1.673%). The error is concentrated in the initial transient where the density changes fastest; the explicit cost is linear in the window while the implicit sweeps-per-step stays at three. So the short-window figures are the pessimistic ones.

  **The explicit step was set by a region where nothing is happening** — the well is steepest at an electrode edge, which is exactly where the density is almost zero.

  **The load-bearing property is not the stability, it is that positivity survives a partial solve.** The update is `n' = (n + dt Σ b n'_neighbour) / (1 + dt Σ a)` and every term in it is non-negative, so each sweep is a non-negative combination of non-negative numbers and the iterate is a valid density however far from converged. A scheme that went negative on the way would be unusable however stable it was.

  **And it is not a general speed-up**, which is the half easiest to leave out. The Gauss-Seidel iteration's difficulty is set by the *diffusive* part of the operator, so a step long by Courant's standard and still short by diffusion's costs three sweeps — while a plain drift tube already near its diffusion limit climbs from 11 sweeps a step at gain 1 to **88.7 at gain 16** and comes out slower than stepping explicitly. `diffusion.implicit-not-paying` says so on the run.

  **What says it is correct rather than merely stable is the Boltzmann equilibrium.** Scharfetter-Gummel is built so its zero-flux state is exactly `B(−P)/B(P) = exp(P)`; that is a property of the *space* discretisation, so backward Euler must hold it at any step. It holds to **8.9e-16 in log density over three decades at a gain of 1000, in two steps and two sweeps** — one sweep per step, because the previous density *is* the answer and Gauss-Seidel recognises it immediately. **Verified by breaking the solve** the way a real mistake would (gathering a neighbour with this cell's outward coefficient): every stability and non-negativity test still passed, and the equilibrium moved by factors of 6 to 18. **A stability test cannot see a wrong operator and neither can a positivity test.**

  **The flux is assembled once now**, which the explicit path wanted anyway — it was recomputing two exponentials per face per step, about a million times over on a driven funnel. **Bit-identical**, asserted rather than assumed over four configurations spanning Cartesian and cylindrical meshes, still and moving gas, interior absorbers and every edge kind: density, collected count and every named loss to the last bit. That needed keeping the *factored* form — `scale`, `B(−P)` and `B(P)` stored separately rather than the two products — because `(w·s·b)·n` and `w·(s·(b·n))` differ in the last bit. The ledger reads the same expression for the same reason; a first version used `Out × density` and came back 1–3 ulps out.

  **Two measurement mistakes worth keeping.** A convergence study whose window let the packet be collected compares two nearly empty fields — the relative difference came out at 39–71% and scaled with nothing, which reads as a broken scheme. And the reference in such a study **carries its own error**: at gain g the two runs are (g−1) base steps apart, not g, so what must be constant is error/(g−1), and dividing by g makes a correct first-order scheme look wrong.

  **Not done:** nothing chooses the gain. Both limits are computable before the run, but what gain is acceptable is an accuracy question and nothing here measures the accuracy of a step it has not taken. Richardson extrapolation over a doubled step would, at three solves a step instead of one. Details in `docs/pressure.md`.

- **The live session, and MCP-1 met — but the work was not the protocol.** `journal`, `undo` and `attribution` existed only in the `Einzel.Commands` assembly **description string**, the same "named in a csproj and nowhere else" state `ITransportMode` was in before its seam was built. §15 says why that is the whole job: the server's "distinct value is shared live state ... everything else it could do, the CLI does at least as well and with less machinery." A journal only one party can write to is a file, and a file needs no server.

  **Attribution comes from the `initialize` handshake, not from a tool parameter.** An `author` argument would make the attribution something the *mutating party fills in*, which is a signature rather than an attribution — an agent could sign a change as the person it is working with, by mistake or because a model decided that read better. The client declares itself once, before any tool exists to call. **The test is in two halves and the second is what makes the first a property rather than a default**: the name that comes back is `agent:surveyor/3.1`, *and* `model_edit`'s schema has exactly `description` and `content`, so there is no argument through which another could have been offered. A tool that took an author and ignored it would pass the first half alone.

  **Shared and linear are two claims.** Shared means one stack rather than one per party, which is the point rather than a hazard: two private stacks over one document would let each party reverse changes the other had already built on, and the document would reach a state neither of them authored. Linear falls out of the walk back being over ordinary edits only. And **undo appends rather than pops**, because a popping stack loses the fact that somebody undid something, and who — which is exactly what MCP-1 asks to be recorded. Walking back twice appends twice.

  **The whole document before and after, not a patch**, so undo needs no inverse operation per command — a command that knew how to reverse itself would be a second implementation of what it does, and the two would part company at the first command somebody forgot to teach. **In memory**, because PRJ-4's argument says the durable record of a design is the document and its git history, not a sidecar.

  All three claims checked by **mutation** rather than by assertion count: private per-author stacks fail three of six journal tests, a popping undo fails a *different* two, and moving attribution into a tool parameter fails three of five protocol tests.

  **The tool surface is deliberately not a second CLI**, and the server says so in its own instructions — the failure to guard against is an agent looking for `run` and `sweep`, not finding them, and concluding the platform cannot do those things. `model_read | model_edit | model_undo | session_journal | model_validate | model_preview`. Preview is here because AGT-5 built it for exactly this; a full run is not, because it belongs where there is a progress surface and a viewport, and `einzel run` is one process launch away meanwhile.

  **Every result is `CommandJson.Write` of the same outcome record the CLI serialises for `--json`, asserted byte for byte.** That makes AGT-2 literal instead of claimed, and carries GRD-2 for free: a warning reaches an MCP client by being on the record rather than by anyone remembering to copy it across — which is the seam this project has already dropped evidence at three times. A refusal comes back as a tool *result* carrying AGT-3's code, path, constraint and suggestion, not as a transport fault, because a refusal is an answer about the model rather than a failure of the call.

  **`einzel-mcp` is its own executable, not a verb on `einzel`.** Figure 3 puts the three surfaces side by side as peers and figure 6 is emphatic that the project-folder loop has "no protocol, no session, no network"; a `serve` verb inside `einzel` would put a server in the binary whose distinguishing property is that it is not one. Nothing but protocol reaches stdout, which turns CLI-2's convention into a hard requirement.

  **The first non-test dependency the project has taken**, and §20's table asks for the licence to be verified rather than assumed: `ModelContextProtocol.Core` 2.2.0 declares Apache-2.0 as an SPDX expression in its own nuspec, and its whole transitive closure is ten `Microsoft.Extensions.*` packages, all MIT. `.Core` rather than the full package, which leaves behind a hosting integration this does not use.

  **A harness that lied in the worst direction.** Driving the server by hand — a file of JSON-RPC piped in — produced *nothing at all* on stdout, which reads unambiguously as a server that does not work. With a logger attached the SDK showed both requests handled and both responses sent: a file on stdin hits EOF immediately, the transport tears down, and the outbound writes are dropped. A real client holds stdin open and never sees it. **When a harness and the thing under test disagree, establish which one is the artefact before changing either** — the trap being that the simpler thing looks like the trustworthy one, when simpler here meant missing the one property the server depends on.

  **And GRD-9 was not delivered by building MCP-1, which is the same requirement stated twice.** The journal knew only about mutations made *through it*, so a person editing the model in their own editor had their change overwritten by the agent's next whole-document edit with nothing saying so — the requirement's own words, failed, in code that was an hour old. **The sharper consequence is what an unrecorded change does to undo**: it breaks the chain, so walking back lands on a document predating the person's edit and discards it *as a side effect of reversing something else*. `Reconcile` records an outside change as an entry attributed to `outside` — not to the person, because another tool, another session and a git checkout look identical from inside — refuses an edit written against the document as it was, and makes the refusal recoverable by having `model_read` take the change up. A no-op `Reconcile` fails three of nine journal tests.

  **Not built:** streamable HTTP hosted in process by the shell, which §15 makes the primary transport and which needs the shell. The tools sit above the transport, so that is a wrapper rather than a rewrite. Details in `docs/live-session.md`.

- **A packet can cross between the two transport descriptions (SEQ-1's conversion).** §9 says an instrument is a timed state machine of "ordered phases with durations, excitation overrides, **transport mode**, and transition conditions", and SEQ-1 adds that a phase boundary may change it. That is ordinary instrument behaviour rather than an exotic case — ions are collected and thermalised in a gas-filled trap, where the description is a density, then extracted into vacuum and flown, where it is trajectories. The two modes have been peers since REG-1's seam was built and could not hand anything to each other.

  **The third clause of SEQ-1 is the substance: "named as a source of uncertainty".** These are not two encodings of one state. **Trajectories to a density loses the velocities entirely** — a density field is a scalar per cell and there is nowhere for a distribution to live, which is not an implementation limit but what the diffusive description *is*: drift-diffusion holds precisely because the velocities have relaxed to the local equilibrium. **A density to trajectories has to invent them**, drawn Maxwellian at the gas temperature plus the local drift — the assumption the diffusive description already made, exactly right while the ions are in the gas that thermalised them and wrong the moment anything happens faster than the momentum-transfer time. `transport.velocity-assumed` is a **non-suppressible violation** for that reason: a caller who reads a flight time computed from invented velocities and does not know they were invented has been misled by the platform.

  | | |
  | --- | --- |
  | Deposited population against declared | **exact** (1e-12) |
  | Equipartition of drawn velocities, 300 K and 1200 K | **1.0021** each |
  | Drift added, against μE | 18.472423 against **18.472423** m/s |
  | A Gaussian cloud's centroid, 20,000 ions | 10.0197 mm against 10.0000 |
  | A 4000 m/s beam after a round trip | 0.2 m/s |

  Two temperatures because one alone is consistent with a thermal draw *and* with a constant that happens to match; the drift is a difference between two runs at one seed, so the thermal part cancels.

  **The discriminating check is cylindrical, and it is the one a plausible implementation fails.** A cell is a ring whose volume grows with radius, so a uniform density holds far more ions at the wall than on the axis — drawing cells by their density *value* over-samples the axis and produces a packet that looks entirely reasonable. The closed form separates them: p(r) ∝ r gives mean radius **2R/3 = 13.3333 mm, measured 13.5177**, against **R/2 = 10.0** for the wrong weighting. Run the wrong way it gives 10.0245, and **only that one test of the ten fails**.

  Population is conserved **by construction** — the four bilinear weights sum to one whatever the position — rather than by a normalising pass, which would pass the same test while hiding a weighting error rather than preventing one. An ion outside the grid is **counted, not clamped**, because clamping piles the escaped population onto the boundary and makes a leaky instrument look confining. And the azimuth of a cylindrical sample is drawn uniformly, which is information the conversion *creates*: a round trip is not the identity even in distribution for a packet that was never axisymmetric.

  **Not built: a stage cannot yet name a transport mode — and scoping that found the reason, which is a defect.** A stage is declared on a *solve element* while the transport mode is a property of the *run*, so a per-element stage cannot carry one: two elements would name different modes for the same instant, and there is no superposition of transport modes the way there is of fields. Worse, the existing arrangement is already wrong. `CompileStages` re-resolves the **whole model parameter surface** with the stage's overrides and then re-expands **only its own element** — so two electrodes in different elements, written as the *same expression* over the same parameter, came out at **900 V and 300 V** during a stage, on a model that validated cleanly with no diagnostic anywhere. The stage design's own rationale is the claim that fails: setting a parameter "moves everything that depends on it at once". It moves everything in one element.

  **Refused first, then fixed.** The refusal — a sequenced model may have one field element — was the honest state while the two readings were open; the right one is the documented one. **The timeline is the instrument's**, so `Timeline` resolves the phases *once for the model*, before any element is compiled, and hands the same parameter surfaces to all of them. **Every element follows it, and how depends on what it is** — a solved geometry re-weights channels it has already solved; an analytic one is compiled once per phase and switched by `SequencedField`. An element no phase moves stays static, which is a distinction rather than an optimisation: wrapping it would hand the integrator switch instants to land on for a field identical on both sides of them.

  **A code review found the first version of this fix reached only half the elements**, which is the more useful half of the story. The analytic branches compile from the base surface and a `CompiledField` for those kinds had nowhere to put a phase — so a sequence setting a parameter used by a `halfSpaceUniform` cap potential had the solved elements follow and the analytic one frozen, validating cleanly; a model whose *only* elements are analytic compiled a timeline nothing consumed, making the sequence a silent no-op. **The comment that hid it is the part to keep**: `Restage` was a closure "because only the solve branch needs it, and threading the declared parameters through every field kind would put the sequencer in the signature of things that have nothing to do with it" — an argument that reads as sound and is exactly backwards. A rationale written for one version of the code kept the next from noticing what it had missed. Schema **0.6** adds a model-level `sequence`, which is the spelling that says what it means; `stages` on a solve stays the older one for the single-element case. Verified by mutation: restoring the per-element behaviour fails that test and nothing else.

  It also fixed something the per-element version got wrong quietly — a malformed stage was reported **once per field element**, turning one typo into a wall of identical complaints. Two refusals remain, both about a document saying two things at once: two elements each declaring stages is two timelines over one instrument, and declaring both `sequence` and `stages` is refused rather than merged, the same argument that refuses a geometry declaring both `drive` and `drives`. **A phase now names a transport mode, and a run crosses the boundary (SEQ-1).** Schema 0.6 puts `mode` on a phase, absent meaning the model's — the same rule its parameter overrides follow, so a model with no sequence and one whose every phase runs in the declared mode are the same run. `SequencedRun` walks the phases, each an ordinary run of its own mode over its own duration, converting where the mode changes. On the test instrument — launch, thermalise, extract — the packet advances **1.37 mm in a microsecond while flying and does not move at all over twenty times longer as a density**, because the diffusive drift is μE and E is zero there. That is the conversion made visible rather than a defect: drift-diffusion holds precisely because the velocity distribution has relaxed, so the momentum genuinely is discarded. Position, the one thing both descriptions carry, survives to the fourth decimal.

  A trajectory leg starting part-way along the timeline is flown against a new **`TimeShiftedField`**, because the integrator always starts at t = 0 and a leg beginning at 21 µs has to be handed an instrument shifted by 21 µs rather than a start time it has nowhere to put. Wrapped rather than adding one to `IntegrationSettings` — the precedent `AxisymmetricField` and `PonderomotiveField` set, and for the reason this file already gives: that core carries every validated number here, and refactoring it to add a case beside it is how those get quietly lost.

  **A sixth occurrence of the check that keeps learning new configurations.** The diffusive requirements — a gas, a mobility, a density grid — were gated on the model's own `transport.mode`, so a trajectory model with a diffusive phase skipped all of them, validated cleanly, and would have failed at run time asking for the gas it never declared. Fixed by asking the right question rather than adding a case: `Modes` returns every mode the run uses, and the requirements attach to that.

  **The first phase may be the trap** — the ordering the requirement was written about, since ions are collected and thermalised in a gas and only then extracted. Seeded through `DiffusionRun.Seed`, the same function a wholly diffusive `einzel run` uses, rather than a second implementation.

  **Reusing that path corrected two numbers a duplicate had got wrong.** A first version built its grid with `new Grid2D(...)` where `GridFor` uses `Grid2D.OverBox`, which rounds intervals up to a power of two — so one model got **two different grids** depending on which path ran it. And its mobility helper ignored `Derived`, so a mobility the document derived from a cross section came back as the stored value rather than the re-derived one. A third gap closed with them: the diffusive leg passed **no absorbers**, so electrodes did not absorb during a diffusive phase — the defect that once made every diffusive transmission an upper bound with nothing saying so, reintroduced locally. The rule: **four helpers already existed, and writing them again got two of them wrong.**

  **And `einzel run` reaches it.** The fork tests `ChangesTransportMode` before the model's own mode, since a model may declare `diffusion` and still have a sequence that leaves it. The terminal gets a per-phase table — with a **dash** where a diffusive phase has no trajectories, which is a different statement from having none left, and `packet centre` rather than `final x`, because there is no single ion. `--json` carries a `sequence` block, and the manifest records **`diffusion -> trajectory`**, because one mode would make a manifest claim to determine a run it does not describe.

  **Two defects this project had already fixed once were met again, and they are the same mistake.** A successful sequenced run exited `ConvergenceFailure`, because the exit logic reads `Outcome is "StopConditionMet" or "DensityEvolved"` — a list that had to learn `DensityEvolved` after a working diffusive run reported itself as a failure. And the printer showed `flight time NaN +/- NaN`, because the fix that made those lines *absent* for a density was gated on `run.Diffusion is null`. **Both were written as a list of the modes known at the time rather than as the question being asked**: "did this run finish what it was asked to do" and "is there a flight time to print" are properties of the result, while "which mode was it" is a proxy that stops being equivalent the moment a third mode exists. Also mine: the test asserted the warnings on stdout and passed my manual check only because I had run with `2>&1` — merging the streams destroys the distinction CLI-2 is about. All in `docs/lessons.md`.

  **And a driven geometry inside a diffusive phase gets the cycle average.** A driven structure has no static field to step a density through, and the time-free interface answers with the RF at the phase's first instant — a field that exists for no length of time. The sequenced path now uses the same `Effective` wrapper the wholly diffusive path does, shared rather than written twice. **The fifth occurrence of that defect, and the first I introduced**: before it, `einzel solve` reporting the DC of a driven geometry, the diffusive mode stepping through a snapshot, `SuperposedField` becoming one when a driven member was summed in, and the renderer drawing one instant on every frame.

  Measured on a four-rod quadrupole at 2 mbar, packet released 1.5 mm off axis: **0.2341 mm after 60 µs at 400 V, against 1.5000 mm with the drive off.** **The geometry had to be four rods, and the first version of the test taught that** — two plates give a nearly *uniform* field, the ponderomotive force goes as ∇E², so there was no well, the packet moved 0.1% either way, and the test passed on a threshold of "less than where it began". **A confinement test on a geometry that cannot confine measures nothing.** Nothing outstanding for SEQ-1.

  **Corpus 31 → 32, and the new one is the sharpest sequenced check there is.** `sequenced-uniform` holds an ion at rest in nothing and then pushes it with a uniform field a phase switches on: predicted `hold + sqrt(2 d m / (q E))` = 5.219358580 µs, measured **5.2193585800816775, 1.6e-11 relative** — five orders inside tolerance, because an analytic field has no geometry error to absorb where the plate version carries 1.0e-7 of fringe. The corpus's only model in the **model-level `sequence`** spelling and the only one whose timeline moves an **analytic** element. **Its teeth were measured rather than predicted**: with the analytic-phase fix reverted it is refused at validation before the ion is launched, and that one refusal guards both defects at once.

  Also added, and overdue by five schema bumps: a test that **every version `SupportedVersions` claims to read actually reads**. §14 has asked for one since 0.2 and none existed, so "every earlier document still reads" has been an assertion in a list rather than a measurement. It ships with a control — an unknown version is refused — because a reader that accepted anything would pass the first half without reading a single version right. It also caught a slip in itself on the first run: the guard asserting the edit had happened fired on the identity case, where replacing 0.3 with 0.3 changes nothing. Details in `docs/model-format.md`, `docs/pressure.md` and `docs/lessons.md`.

- **The shell opens on a model, with three of §16's eleven views.** `einzel-shell models/reflectron.json` gives a parameter tree with live validation and units on every field, the shared `SessionJournal` with agent and human attribution, and a **3-D viewport drawing the instrument, the field and the ions** on Helix Toolkit / DirectX 11. Every action is recorded as the `einzel` command that would reproduce it (Amendment 25).

  **Every conductor is the zero level set of its own signed distance**, which is `Contours`' argument one dimension up: one routine draws every conductor there is, and a shape added to the model format needs no change here (invariant 2). Drawing from the declared primitives would be shorter today and would need a new case for the fourth shape. **What differs between symmetries is not the shape but what the solve claims about the third dimension** — a cross-section says the geometry repeats along z so the conductor is an **uncapped prism** (capping it would draw an end the model does not have, and the drawn depth is named as a drawing convention per GRD-12); an axisymmetric half-plane says it repeats all the way round, so a rectangle there is a **tube in space**; a volume claims nothing and needs a genuine extraction.

  **Surface nets rather than marching cubes**, checked against closed forms the code had no part in: a sphere's volume **0.99038 / 0.99760 / 0.99940** at 24/48/96 cells (improving under refinement, which is the half that says it is discretisation), every one of 11,970 edges shared by exactly two triangles, the worst normal against the true outward radius **1.000000**, and a revolved tube against Pappus at **0.99990**. **Orientation comes from the field, not the winding**: each normal is the gradient of the same signed distance that defined the surface, and triangles are flipped to agree — which is needed because `Contours` emits deliberately undirected segments, and a sign error there gives a shape lit from inside that looks like a hole.

  **Two colour scales, and the potential one is centred on earth.** Energy is sequential and gets viridis; a potential is signed and gets a diverging ramp **symmetric about zero**, because stretching a ramp across the observed range puts the neutral colour at the arithmetic middle — 250 V for a lens holding 0 and 500 — and an earthed tube would be painted the same blue as a genuinely negative one. Earth is the value a reader looks for first. **An electrode is coloured by the peak its drive reaches**, not the DC it sits at: the sixth appearance of that mistake, guarded by a test this time.

  **RND-8 withholds the paths and not the instrument.** A funnel drawn with no rings and no field is a picture of nothing at all, and the requirement was never about that.

  **The geometry found a defect in the core.** Sampled over the whole solve domain at 48 cells a **1 mm plate is thinner than a cell**, so the three-dimensional example produced no conductors at all, silently. Fixed by extracting over the electrode's own bounding box — which needed `CompiledElectrode3D.Bounds` beside `Centre` and `CharacteristicSize`, because a caller switching on the shape is exactly what invariant 2 forbids and would need a new case in every caller when a fourth shape arrives.

  **The view is adjustable and now says so**: Side / Top / Front / Iso / Fit, the view cube, and layer toggles for electrodes, field, paths and transparency. Rotate, pan and zoom always worked and nothing said so. **Named views matter more than the gestures for an instrument** — ion optics is read as an axial section and two transverse ones, and getting to one by dragging is approximate where a button is exact.

  **Twice a view could not be built until a command existed, and both times the command layer gained the capability rather than the window keeping it.** The model tree needed `einzel outline` — a window that parsed the document to build a tree would grow its own idea of what a model is, and the two would come to disagree. The viewport needed `ViewportCommand` — one that integrated its own trajectories would be a second transport implementation. That is Amendment 25 running in the direction it was not designed for, and it is the strongest evidence yet that AGT-2 is real: **an agent is better off because a window was built.**

  **The colour scale is the viewport's own finding.** §16 asks for bundles coloured by energy, and a scale taken per path gives every ion the same colours whatever its energy — two ions a kilovolt apart look identical and the picture says they were the same. `ViewportOutcome` reports the range over the whole bundle, once. Same failure as the animation's contour levels in the other axis, where anchoring per frame made a film of a spreading packet show a packet doing nothing. **The discriminating test is not "wider than the widest single path"** — that margin is 1.5e-5 on a packet launched from rest — but that **no single path owns both ends of the scale**, which any per-path anchoring fails whatever the magnitudes are. A degenerate range gives a half, not a division: a monoenergetic beam in a field-free drift is the simplest model anyone writes, and dividing by a zero width paints the bundle NaN.

  **Viridis, and the reason is not taste.** A rainbow ramp has non-monotone lightness, so it invents boundaries where the data is smooth and collapses under the commonest colour vision deficiencies. The test asserts monotone Rec. 709 luminance over 65 samples, which is what stops it being "improved" into a rainbow later.

  **RND-8 is on the face of the window**, asked of `ITransportMode.ProducesTrajectories` rather than of the pressure — a viewport deciding from the pressure would re-derive a decision the mode owns. A diffusive model draws nothing and *says what it has instead*, because an empty viewport and one whose ions were all lost look identical and only one is a statement about the physics. The warnings say it first when they say it at all; a second paraphrase above the engine's own words reads as two separate problems.

  **Two open bets settled by measurement.** The whole solution **builds on Linux, XAML markup compilation included** — `EnableWindowsTargeting` is enough — and `Einzel.Wpf.Tests`, the one Windows-only test project, is walked past by a solution-wide `dotnet test` there. **848 tests on Windows, 843 on Linux, both green.**

  **Two things the window cost.** **WPF cannot run in globalization-invariant mode** — its font cache constructs `new CultureInfo("en")` while measuring the first line of text — so the shell reverses the solution-wide setting. What that setting protected is unaffected: locale-independence here comes from passing `CultureInfo.InvariantCulture` explicitly at every formatting and parsing site, and the flag was the belt to those braces. And **Helix Toolkit's DirectX backend is SharpDX, archived since December 2020** — taken knowingly, because §17 confines this path to screen tuning and the publication figure comes from `Einzel.Render`, which is vector, headless and owes it nothing. 3.1.2 rather than the 2.x line contemporaneous with r06, which is .NET Framework-only and restores on net10 only through the NU1701 shim. MIT throughout the transitive closure. Recorded as SPEC.md Amendment 26.

  **Five things that were wrong first, four of them the camera and one the light.** Helix defaults to a **perspective** camera, and an ion-optics drawing is read for where things are along the axis — the one thing perspective distorts. `ZoomExtents` **fired before layout**, so a reflectron's 1.3 m flight ran off the edge: the control had a model but had not been measured. The **opening view was silently discarded**, because it was set in the constructor and framing a scene with nothing in it leaves the camera wherever that left it — every model opened in the side view whatever was asked for; it is now set once, after the first draw that has something in it, **and never again**, because a redraw follows a parameter edit and yanking the camera back would take the view away from the person watching the thing they just changed. And **the first named view after startup came out as the top view whichever button was pressed**, because `SetView` wrote look, position and up into the live camera one property at a time and each raises its own change notification, so the control saw a momentarily inconsistent basis and re-derived one of them; a whole camera assigned at once fixes it. Found by clicking the buttons through UI Automation and reading the axis indicator in the screenshot, which is the only way that class of defect shows up at all.

  **And the scene had no light**, so every Phong surface rendered at its ambient term alone — the electrodes were extracted correctly, drawn correctly, and could not be seen. Worth keeping because it is the failure mode where the data is right and the picture is empty, and the instinct is to go looking at the data.

  **Three more views, and each needed the command layer to gain something first.**

  **Results by §12's accuracy class**, whose taxonomy was recorded nowhere in the code - `FigureOfMeritInfo` carried a name, a unit, a description and which way is better, and not which of Class T, S or B a figure belongs to. Six T, four S, three B, and **two deliberately in none**: `flightTime` is the raw arrival quantity the Class T figures are computed *from*, and `energyDrift` says in its own description that it is a diagnostic. §12 lists neither, and assigning them a class to tidy the grouping would weaken what it says about the twelve that do belong.

  **And that view found a hole in GRD-1 — 1 of 14 figures carried an envelope.** GRD-1 says the API offers no way to obtain a scalar alone; `FiguresOfMerit.Evaluator` is a deliberate, argued exception for ranking; and the consequence nobody had written down is that most figures existed *only* in the excepted form. There was no way to ask this build for a turn-around time with an uncertainty on it. **`Bootstrap` closes it to 5 of 14** by resampling the ion cloud - one mechanism for a width, a mean and a ratio alike, assuming nothing about the distribution, which matters because an arrival-time peak is measurably skew. Validated against **two** closed forms the code had no part in: the mean's sigma/sqrt(N) at 0.987/1.022/0.996 and the median's sqrt(pi/2)sigma/sqrt(N) at 0.975/1.041/0.940. SPEC.md Amendment 28.

  **Two limitations came with it, both recorded.** The bootstrap is **inconsistent for extreme-order statistics** - the error on a range went 0.181/0.313/0.225 across a sixteenfold sample increase - which matters because "the widest entry radius that still arrives" is one, and this project already replaced such a measurement after it gave 0.65 mm on one radius grid and 0.20 mm on another. And it **must not be applied to the deterministic energy sweep**: without a declared cloud the acceptance is scanned evenly and seed-free, a designed scan rather than a draw, and resampling it would report the scan's own spacing as an uncertainty - the confusion `DefaultEnergySpread`'s remarks were written to prevent.

  **The regime inspector**, and the word that matters is *along*. REG-2's numbers are already computed on every run - at the **worst** point anywhere in the gas, which is right for a warning and useless for deciding what to change. `MeasureAt` reports one point, so "outside validity" becomes "outside validity between 12 and 31 millimetres". The two share a private core and are **bit-identical wherever the gas is uniform**, because a uniform gas makes them the same question; on a hundredfold density ramp they must differ, and do - **Kn 4.17 at the thin end against 0.042 at the thick**, free-molecular at one end and a continuum at the other in the same instrument, with E/N from 40000 down to 400 Td.

  **Three defects in my own first version of it, two of them repeats.** The path was flown in a **vacuum** - no collision sampler - and the gas numbers reported along it, the same silent substitution `RunCommand`'s own comment warns against; caught because `gas-flow-carry` came back with 2 samples instead of 128, since with no field the analytic drift crosses the whole metre in one step. A **diffusive** model was flown too, until RND-8 was asked of the mode rather than the pressure. And the excursion test used the funnel, which declares no gas, so it returned early and asserted nothing.

  **The sequence editor**: the declared timeline, with what each phase *moves* rather than what it holds - a sequenced instrument repeats most of its state, so a table repeating every setting buries the rows that change. Bars proportional to duration, because a 2 us hold beside a 100 us flight is the shape of a pulsed extraction. Two things a reader would otherwise assume wrongly are stated: the last phase **holds** after the sequence ends, and a phase changing the transport mode is SEQ-1's conversion boundary.

  **`Einzel.Analysis` had no test project** - the only source assembly without one, its tests living wherever the device that exercised them happened to be. It has one now.

  Details in `docs/shell.md`.

  **Invariant 1 is checked from the Linux-running test project**, which is the whole point — an invariant only ever checked on a developer's Windows box is one that has already been broken by the time anyone notices. The scan is over the assemblies actually present beside the tests rather than a hardcoded list, with a `MustBePresent` guard so a scan that found nothing cannot pass: that vacuous truth has caught this project four times already.

  **UI-1 needed two different checks, and the difference cost a mutation.** `GetReferencedAssemblies` reports what the **compiler emitted** — what the code *uses* — so declaring a `ProjectReference` to the whole transport engine left no trace in the metadata and the test passed. UI-1 is about what the shell may reach **for**, not what it has reached for so far, so the project file is checked too. Only the declared-reference check catches that mutation, and the emitted one still earns its place: a transitive reference through a third assembly appears in no csproj.

  **A trap in verifying it**: nothing references the shell, so `dotnet test` never rebuilds it and a local mutation check runs against a stale binary. The first mutation "passed" for exactly that reason and was nearly believed. On CI the full build precedes the tests, so it is sound there.

  **`EnableWindowsTargeting` is an unsettled bet.** It lets the shell restore and compile on Linux so CI can keep building the whole solution on both runners; whether WPF *XAML* compilation works there is unverifiable on a Windows machine. If it fails on first push the answer is a solution filter excluding the shell on Linux — invariant 1's real content is unaffected either way, since the boundary test does not need the shell to exist.

- **The model tree and the journal view, and the guard they reversed.** §16's first two views, and the first thing they needed was a command that did not exist. **UI-1 forbids the shell from having file format knowledge**, so the window cannot parse a model to build a tree — a window that did would grow its own idea of what a model is, and the two would come to disagree. `einzel outline` answers instead: value, unit, bounds, description, what each parameter resolves to in SI, and whether it is editable. All of it was already declared, which is the pleasing part — **LIB-1 gave parameters units and bounds so a *study* could perturb them**, and those turn out to be exactly what a person editing one needs to see. A verb rather than a shell method, because AGT-2 forbids anything existing only in the shell and an agent wanting a model's knobs without parsing the document needs the same service.

  **Then §16 contradicted a guard I had committed.** `SessionJournal` refused any edit that did not validate — my own choice, argued as "an unrunnable model is not one party's problem when another party is working from the same file". **Live validation makes that untenable**: a person typing 500 into a parameter bounded at 50 must see the tree standing with the complaint against it, not be prevented from typing. And refusing every invalid document forbids any edit *sequence* that passes through one — widening a bound and then setting a value beyond the old bound works in one order and is refused in the other, for no reason a person could infer.

  The platform's own rule settles it, already written down four times over: **taint, never block**. `Check` now refuses only what does not *parse* — there is nothing there to be wrong, and no edit through the journal could have produced it — and validity is reported through `SessionJournal.Validate()` rather than enforced. **The shape that generalises**: a guard invented while building one feature, justified by a real argument, that is stricter than any requirement. It costs nothing until a later requirement needs the state it forbade. Guards written from first principles rather than from a requirement are worth re-reading when a new requirement arrives — the argument is usually still true and still outweighed.

  Also caught: an outline test of mine passed for the wrong reason. It set `inscribedRadius` to 5.0, the template's existing value, so "the document changed" held only because the JSON round-trip reformats. Now 7.0, with an added assertion that everything **derived** from it moved too — which is the actual reason a parameter surface exists rather than a list of electrode settings.

- **`idealQuadrupoleRf` — the only analytic driven field, and the format gap that blocked a corpus example.** Every driven field the format offered was *solved*, so a closed form written from the ideal quadrupole had nothing exact to be checked against: a solved rod geometry departs from the ideal by a few per cent, and asserting the ideal formula against it would state a modelling difference as arithmetic. The class had existed in `Einzel.Fields` since the Mathieu work and had simply never been reachable from a document. A zero *frequency* is refused (a static field wearing a drive's clothes, which would run quietly and give a quadrupole with no RF); a zero *amplitude* is allowed and is how a document says the generator is off.

  It unblocked the driven diffusive pair. `rf-quadrupole-confined` and `rf-quadrupole-unconfined` differ in one number and each matches a **different** closed form: 200 V gives 0.13745984 mm against `sqrt(kT/(m omega_sec^2))` = **0.13746 (1.2e-6)**, and 0 V gives 1.301045 mm against `sqrt(seed^2 + 2Dt)` = 1.303075 (1.6e-3). Neither is worth much alone — a width matching one formula says nothing about whether the drive does anything.

  **The well is collisional, which is what makes it sharp.** From m(v̇ + νv) = qE₀cos(Ωt) the depth is q E₀²/(4m(Ω²+ν²)) rather than Dehmelt's q E₀²/(4mΩ²), with ν = q/(mμ) from the declared mobility. At 1 mbar and 1 MHz that is ν/Ω = 0.34 and a suppression of 0.896, so **the collisionless formula is ten per cent away** — the check is on the collisional treatment, not merely on a pseudopotential existing. And `E0(r) = 2 V r / r0²`, not `V r / r0²`: the potential is V(x²−y²)/r0² and its gradient carries the factor of two, whose omission makes the answer exactly four times too small.

  Two mistakes worth keeping. The grid must hold an **eleven-fold range of width** — sized for the confined packet alone it clipped the unconfined one and reported it 9% narrow. And the fix over-corrected: 512 intervals where 256 was already enough, **four minutes against seventeen seconds** for the same answers to the same precision. Refining past what is needed is the reflex and here it bought nothing.

- **`c-trap` — a curved axis, and the first geometry invariant under nothing.** **THIS ENTRY'S NUMBERS WERE MEASURED ON BEADED ROUND RODS. The rods are swept hyperbolic profiles now — see the rebuild entry at the end of this list and `docs/device-templates.md` for the current figures.** Four rods bent around a quarter circle. A translational solve assumes the geometry repeats along an axis and an axisymmetric one assumes it repeats all the way round; a curved axis does neither, so this is the first template needing a genuine volume solve. **The rods are chains of overlapping spheres**, because a `cylinder` here is axis-aligned and a bent rod is not — and that needed **no new primitive**, since `repeat` binds an index and `cosPi`/`sinPi` place a bead anywhere. **49 electrode declarations reduce to 1 basis channel** (19 cycles, factor 0.3316, 9.1 s on 65×65×33): the in-plane and out-of-plane pairs are exact negatives, and bending changes nothing about that.

  **The drive is what carries the ion round**, against the same model with the amplitude at zero: it arrives having come back to **2.5 µm** of the axis late in flight, against an unguided ion that strikes a rod at 25.9 µs and never gets nearer than **782 µm**. Bounded against unbounded — three earlier versions of that assertion compared the wrong pair of quantities and are in `docs/lessons.md`.

  **The slot's gap is not its opening.** Ejection needs a hole in the inner electrode, declared as two segments with an angular gap — and the bounding bead at each end reaches into it by `rodRadius / innerArcRadius`, 14.5° a side, so a 27° gap is *closed*. Found by ejecting into it: before the slot existed the ion struck metal after exactly the inscribed radius of travel. **And it has to be a cooled ion**: at 439 m/s the packet drifts 12° along the arc while being pushed out and clips the far edge; at 62 m/s it leaves cleanly and reaches a plane 14 mm inside the arc in 4.29 µs.

  **A launch direction had to become parametric** (schema 0.7). `VectorValue` has carried `Expression` since the parameter surface existed, citing §9's "every placement is a parametric expression, never a baked number"; `DirectionValue` never got it. A curved axis is the device that needs it — launching tangentially means a direction that depends on where round the arc you start, so with a literal, changing `arcHalfTurns` moves the geometry and leaves the launch pointing where it used to. It bit twice in one template, since the detector's *normal* has the same dependence. Same shape as `drivePhase` being a plain `double` until a travelling wave needed a ramp.

  **The curvature focuses the ejected packet — what the device is *for*, claimed in the template's prose from the day it was written and measured only now.** Every ion is pushed out along its own radius, so their velocities all point inward and the packet converges as it flies; a straight trap pushes every ion the same way and whatever length of trap they occupied they still occupy at the analyser. **The straight-trap comparison needs no run**: a parallel ejection is a rigid translation, and a translation preserves every distance, so it is exactly 1.0×.

  | bend radius | launch extent | waist | convergence | focus at |
  | --- | --- | --- | --- | --- |
  | 15 mm | 1.331 mm | 0.0547 mm | **24.3×** | 25.94 mm = **1.73 R** |
  | 20 mm | 1.774 mm | 0.0852 mm | **20.8×** | 38.38 mm = **1.92 R** |

  **The focus is not at the arc centre**, which is the part a design has to know: velocities aimed along radii meet there, one bend radius away, and the packet instead crosses the centre *still converging*. The slot is a lens as well as a hole — accelerated up to it, field-free after it, which is an aperture lens by construction. **A strength for it is deliberately not claimed**: a thin-lens fit to the shorter bend implies f_slot = −35.5 mm and predicts **46.0 mm** for the longer against a measured **38.4**, so the two are not one fixed lens and one variable one — the slot is declared as an *angle*, so its own opening scales with the bend too. The failed prediction is recorded because carrying it as a formula would be carrying a 17% error dressed as a model.

  **Leaving the drive on refocuses the ejection, and does it through the cycle average.** 20.8× at 38.37 mm quiet, against 6.1–8.8× at 11.07–12.21 mm driven — three times sooner and two and a half times worse, so an analyser placed at the quiet focus would be in the wrong place. **The phase sweep refuted the guess that prompted it**: an ejection into a field reversing at 3 MHz looks like it should depend on where in the cycle the push arrived, and over a whole cycle the focus moves **1.10×** against the drive's own **3.14×**. So what acts is the pseudopotential, not the instantaneous field — the ion crosses ~17 RF periods reaching the waist, and the tenth that remains is the one partial cycle at the start.

  **Three defects found getting there, none by a failing test.** The RF at 500 V against a 60 V push meant ions left at 11–23° off radial and four of five struck the slot — *a device driven outside the regime an effect lives in reports the effect's absence*. `ejectVolts` put the inner rod at −V, so an ion fell V through the slot and climbed the same V back out into the grounded interior, arriving with nothing left; it now pushes against **earth**, which leaves that space field-free, and field-free is where a converging packet converges. And **`slotHalfTurns` meant the gap between bead *centres*** rather than the opening between *surfaces* — 14.7° eaten a side, so 27° declared opened −2°. It now means the opening, which needed **`asinPi`** in the grammar: placing something by angle from a length ratio needs an inverse sine, in half turns so it composes with `cosPi`/`sinPi` and no `pi` appears in a document. Fourth device to find a gap one level down, and the fourth time it was a *function* rather than an abstraction.

  **C-trap to orbital analyser, as far as it can go: the handover is one number.** The two cannot share a document, so what is measured instead is what one delivers against what the other needs — and **the currency is time, not space**, because in a quadro-logarithmic field the axial frequency depends on nothing but m/q (pinned directly in `QuadroLogarithmicFieldTests`). The analyser is indifferent to orbit radius, axial amplitude and energy; two ions at one frequency still cancel if they start at different *phases*, and phase is set by when an ion arrived. So the injection specification is the packet's arrival spread over the axial period — both numbers taken from the shipped templates rather than written down here.

  | | arrival spread | of a period | coherence |
  | --- | --- | --- | --- |
  | analyser axial period | 3.1983 µs | — | — |
  | ejected, drive off | **60.02 ns** | 1.88% | **0.9990** |
  | ejected, drive on | 170.93 ns | 5.34% | 0.9893 |

  **What it deliberately does not show is that the curvature delivers that coherence.** Every ion sits the same distance from the rods bent or straight, so they fall through the same potential either way and a straight trap would arrive just as together — the 60 ns is the *slot's* fringe, not the bend. The curvature buys the other thing: a packet 20.8× narrower in space, which is about passing an entrance aperture. And in this field a spatially broad packet is **not** a dephased one, since the frequency is exactly amplitude-independent; a real analyser's imperfections give compactness a second job that this model does not have and should not be read as having.

  **What cannot be done yet: the two instruments in one document.** Analytic fields fill all space, and the quadro-logarithmic potential grows as z², so an orbital trap declared beside its C-trap puts an enormous field across it. Two *solved* elements compose correctly, each bounded by its own domain — so the gap is specifically that an exact analytic field cannot be one element of a beamline, and the exactness is why anyone would want it there. SPEC.md Amendment 32.

- **Ion processor to a multi-reflection analyser: the second handover, and the crossing where the binding limit changes hands.** Same shape as the C-trap's — a trap accumulates and cools, then pulses into an analyser — with the shipped `rectilinear-trap` and the shipped mirror pair, both solved. **Not** a model of a particular instrument: an asymmetric-track analyser gets its reflection count from a slow drift along the mirror axis, and nothing here models that drift.

  **Turn-around is the part of the arrival spread no analyser can undo.** Mirrors refocus energy spread; ions that left at different *instants* stay apart for the whole flight. So R = t/(2Δt) grows linearly with flight time while the mirror's own aberration limit does not grow at all.

  | | | | |
  | --- | --- | --- | --- |
  | trap turn-around | **4.220 ± 0.067 ns** (2,000 ions) | analyser period | 55.9366 µs/oscillation |
  | analyser energy limit, ±3% | R = 321,018 | cap to cap | 767.0 mm |

  8 oscillations (12.3 m) gives R = 53,015; 16 (24.5 m) gives 106,029. **The two limits cross at 48 oscillations** — 2.7 ms, 74 m — so every practical instrument sits below it and **the turn-around is the thing to improve**, not the mirror.

  **One number there is arithmetic wearing a measurement's clothes.** The aberration limit is 321,018 at 3, 6 *and* 12 oscillations, identical to the digit, because `MirrorPair.Fly` computes one period and multiplies rather than stitching legs. So the flatness confirms the arithmetic, not the physics; the physical claim underneath is that every oscillation is identical, which is **exactly what an asymmetric track gives up**. Unmeasured here, and labelled as such. And **the trap's own 241 ns arrival spread is the wrong number** — that is extraction-depth spread, which is an energy spread and is what mirrors are *for*; using it would understate the reachable R by two orders of magnitude.

- **An analytic element can be bounded, so two instruments share a document (SPEC.md Amendment 32).** An analytic field has no extent, *because a formula does not* — so a quadro-logarithmic potential, which grows as z², puts an enormous field across the trap that injects it. Superposition was never the problem and the sequencer could always express the handover. **And the obvious escape does not exist**: declaring the analyser as *solved* geometry fails because its electrodes are equipotentials of the field they produce, so the profile is transcendental in r (Lambert W) and the 2-D shape vocabulary has no curve a document can name.

  `region` on an analytic element is a box outside which it contributes nothing. Measured on a two-element document — an orbital analyser and an ordinary 1 kV/m accelerating section 75 mm downstream:

  | in the second device | field along x |
  | --- | --- |
  | analyser unbounded | **−1,499,000 V/m** |
  | analyser bounded | **1,000.0 V/m**, exactly its own |

  **On the axis it is worse than swamping**: unbounded, the second device has a line through it at which the model cannot be asked a question at all, because that line is the analyser's singular axis and a quadro-logarithmic field refuses a point there rather than returning a large one. Inside the region nothing changes, to the bit — the control that makes the rest mean anything. A **solved** element may not declare a region (already bounded by its own domain, and a document that says a thing twice can say it two ways); all six bounds are required, since "the axes I left out" is not how anyone reads a partly-filled box; a region with no extent is refused. `einzel schema` carries the whole thing **by reflection with no schema edit**, which is AGT-7 working rather than being claimed. `DrivenBoundedField` exists so the choice is made by what the field *is* — a plain wrapper round a driven field would have been the **sixth** appearance of "a time-varying quantity reached through a time-free interface answers at an arbitrary instant".

  **The potential steps at the boundary, and measuring it corrected my own account of it.** A box is not an equipotential, so the potential does not match across it — from which I first concluded that a crossing ion gains or loses that energy. **It does not.** An ion is moved by the *field*, which is exactly the declared one on each side, so a bounded uniform field is an accelerating gap followed by a field-free drift:

  | | |
  | --- | --- |
  | `sqrt(2mL/qE) + (D−L)/v` | **13.658582 µs** |
  | measured, bounded | **13.658582 µs** |
  | same model, no region | 10.180506 µs, accelerating the whole way |

  What the step *does* cost is narrower and now says so: the **energy-drift diagnostic** jumps at the boundary, and the piecewise field is **not conservative across it** — an ion crossing more than once, in by one face and out by another, can gain energy no electrode supplied. A single straight crossing has no such path. Reported at severity `Qualified` on every bounded element, in volts and as a fraction of the beam potential. The step is still large for the fields one most wants to bound (a uniform potential never decays, a quadro-logarithmic one *grows*), so "place the boundary where the field has decayed" has nowhere to point — tolerable for a beam passing through, and not what a real device does.

  **The failure points straight at the fix.** A real device's field is bounded by a **conductor**, and a conductor is an equipotential of the very field it produces — so bounding an analytic element by one of its own level sets, offset to zero outside, would make the potential continuous *by construction*, with the field discontinuous exactly as `halfSpaceUniform` already is and the geometry exactly a real electrode. That is the next refinement and it was not built.

  Three things the tests found, none of them the feature. **`FieldAssembly.Build` threw on any warning**, which collided with REG-2's "report whether or not it crosses a threshold" and would have made `Build` unusable for every composed beamline; its contract now refuses a `ValidityViolation` only — an unconverged solve means the numbers may not be the ones the document describes and a bare field has no envelope to carry that on, while a region's step is a consequence of geometry the author asked for. **The boundary sampler landed on the field's own singular axis**, so refused points are skipped *and counted*, with the count in the message, because a survey with holes in it should say how many. And **the region check sat behind the element compile**, so a solved element that was also invalid for another reason never reported that it may not declare a region — hiding one mistake behind another makes a document take two rounds to fix and gives no reason for the second.

- **Every at-rest launch reported `energy drift 0.00E+000 relative (ACC-4 budget 1e-6)`, and it meant "not measured".** Found by checking the new region feature through `einzel run`, not by a failing test — and no test failed when it was fixed, which is how it survived. Drift is *relative*, so it needs a scale, and the scale is the ion's total energy at launch; an ion released from rest where the potential is zero has exactly zero, so the tracker returns without doing anything and the value stayed at the `0.0` it was initialised to. **The accelerating gap, the sequenced extraction, the Paul trap and the rectilinear trap were all affected**, and all are examples someone reads.

  Now NaN, which the JSON surface already writes as absent and the printer now says in words — "no conserved scale, because the field does work or the ion started at rest at zero potential". The driven-field branch had used NaN for the same reason since RF landed, one `if` below the line that was wrong. **The sharp control is that being at rest is not what removes the scale**: an ion at rest 50 mm into a uniform field sits at −500 V, has 500 eV to be relative to, and reports 9.2e-16.

  What makes this worse than an ordinary missing value: **zero is the best possible answer for this quantity**, so "not computed" and "perfect" printed identically. A reader who sees a blank asks; one who sees the ideal result stops looking. Where a diagnostic's not-computed value coincides with its ideal value, the two must be separated at the point the computation is skipped.

- **`einzel run` now reports what is still inside, so a working trap stops reading as a total failure.** A trapped ion never arrives anywhere, so `paul-trap-held` — behaving exactly as designed — printed `0 of 1 ions arrived, transmission 0.0 %` and nothing else, which is what a trap that lost every ion prints too. `confined` is the complement, **counted from the flight the run already did** rather than by calling the figure of merit of the same name, which re-flies the whole ensemble: two implementations of one quantity is what made `run` and `test` disagree twice here. Held reports 100.0%, ejected 0.0%, and an ordinary beamline gets **no line at all**, since a line of zeros on every model is noise.

  **And a run's exit code now asks whether the ENGINE finished, not whether the ion arrived.** A trap that works exited 4, because `MaximumFlightTimeReached` was not in the success set — the third occurrence of one pattern, after diffusive and sequenced runs, each fixed by widening a list of the outcome names known when it was written.

  **Enumerating the corpus is what settled it, and it refuted my own first answer.** I had recorded that the fix could not be a third widening because "ended at the time limit, everything still inside" describes a held trap *and* a beamline that ran out of flight time, so the distinction was intent and therefore a schema question. **Six of the thirty-seven shipped examples exited 4 while behaving exactly as designed**, and they split two ways, not one: three end at a declared hold (`paul-trap-held`, `thermalisation`, `orbital-trap-frequency`) and three are deliberate losses that are the control halves of pairs (`paul-trap-ejected`, `quadrupole-rf-unstable`, `ion-funnel-no-rf`). The second kind ends `StruckElectrode`, which no amount of trap-versus-beamline intent would have fixed.

  So the shared property is not what the model is for: it is that **the engine finished in all six**, and the transmission, the itemised losses and `confined` say what became of the ion. What remains a failure is an integrator that gave up — a step-size underflow or an exhausted step budget — which leaves a trajectory stopping part way with no bound on how wrong it is. `TrajectoryOutcome.Completed()` is a switch with every case named and **a throw for the rest**, so a sixth outcome fails to compile until somebody decides which kind it is. Tested exhaustively over the enum, with a count guard, because a change that makes runs stop failing is exactly how an exit code becomes vacuous.

  **And two warnings stopped claiming a validity violation on models behaving as designed.** `TRAJECTORY_INCOMPLETE` now grades by the same classification and says the run is complete and this is its result; `PEAK_UNRESOLVED` is `Qualified`, since a field-free slit's peak is degenerate *by construction* and the case where that means something went wrong has its own warning, `ensemble.degenerate`. Both still reported, both still non-suppressible. A false alarm on the class GRD-3 makes unsuppressible teaches readers to ignore the one class that must never be ignored.

  **And the obvious next step was measured and rejected.** Making the exit code also fail on a non-suppressible validity violation looks like the completion of this — an unconverged field is exactly "the engine did not compute what the document describes", and `Build` already refuses one. **Eleven of the thirty-seven examples carry a violation** (`mobility.outside` ×5, `spacecharge.ignored` ×4, `spacecharge.point` ×2, `rf.quiver`, `diffusion.implicit`), every one behaving as designed and asserting a closed form knowing the caveat applies. That would have failed 30% of the corpus against the 16% this fixed. **It also contradicts the doctrine**: taint, never block — a violation is how a caveat travels unsuppressibly, an exit code is how a command fails, and they are orthogonal on purpose. What stays open is one warning code, not a class: an unconverged field, where `Build` refuses and `run` does not.

- **An orbital analyser's capture ramp is already expressible, which was the last thing assumed missing.** A real orbital trap does not switch on around an ion already in the right orbit — it takes a packet injected at a large radius and **ramps its voltage**, squeezing the orbit inward. Working out what a single document flying an ion from a C-trap into an analyser still needs, that looked like the gap. It is not: `curvature` is an expression over the parameter surface and a phase sets parameters, so the ramp costs nothing new.

  Measured against the control that matters — the same document with the ramp flattened, differing in two numbers: **4.711 mm apart after 6 µs**, 1001 steps against 644. A stiffer well is a faster orbit, so the step count moves in the direction that says which way the ramp went. Without the flat control a ramped run proves only that a model with a sequence in it runs.

  **What remains untested is whether capture can be TUNED** — matching injection radius, energy and timing to the ramp is a study rather than a feature, and so is how many phases approximate a smooth ramp closely enough.

- **Extensions declare a licence, which is LIC-2's engine half.** An extension is third-party code run against this engine and LIC-1 is absolute about what may enter the default build, so somebody deciding whether to install one needs to know what it is offered under *before* they install it. The manifest carried trust level, versions and a compatible range, and nothing about the licence.

  `licence` on the manifest, scaffolded by `ext register` (the same argument as `init` writing a model that runs — a field that has to be added later is one that gets left out), and surfaced by `einzel ext list` in both forms. **The load-bearing half is the undeclared case**: it prints `NOT DECLARED` rather than being omitted, because the one extension worth asking about must not be the one whose line is shortest, and it is `null` in `--json` rather than a placeholder so a caller cannot mistake "did not say" for a licence it recognises. Absent, not zero — and it matters more here than usual, because the reader cannot recompute the answer for themselves.

  **An SPDX identifier by convention rather than by validation.** A checker that recognised some spellings and not others would report an unrecognised licence as no licence, which is exactly the failure the field exists to prevent.

  What is left of LIC-2 is the extension manager *pane* — presentation over something that now works.
- **`einzel estimate` costs a study, and the loop invariant that made every study dearer than it needed to be.** GRD-8 gates an operation above a cost threshold, and the operation anyone plans a multi-day job around is a **study** — a scan, a sweep, an optimisation, a boundary search. `estimate` took a model, so the number it gave was short by the evaluation count, silently, which is the worst direction for a number whose whole job is deciding whether to start.

  **The multiplier costs nothing, which is what makes this a defect rather than a limitation** — every driver states its own extent, so it is arithmetic over numbers already in the file. A ceiling is reported *as* a ceiling: a scan computes every point it declares, a search stops when it converges. **And the arithmetic is not the obvious one**: an evaluation solves once and flies every member through that one field, so a study is `evaluations x (solve + members x flight)`. Multiplying the model's own total by the evaluation count charges a whole solve per member — **4.8x over** at nine members.

  **The energy sweep was doing exactly that, and had been from the beginning.** `Setup` — and so `FieldAssembly` — was called *inside* the member loop, and an energy offset changes the launch speed and nothing whatever about the field. **249 ms per ion, flat, against a whole solve of 270.** The flatness is the tell: a per-ion cost that does not fall as ions are added is a per-ion fixed cost. Hoisting it gave **bit-identical values to the last digit** (90176.37243580694, 136915.00595510416, 123107.75087866254, 27.315480249008885) and took 21 ions from 5.23 s to 1.53 s.

  **Nothing was wrong, which is why it survived.** Every number was right and a figure of merit is *supposed* to build a field. The behaviour was even written down — "an ensemble figure builds the field once per ion, so one draw emits the same code twenty-one times" — as a fact about warning counting rather than as a cost. **A cost is invisible to a suite that only checks answers.** And `FlyCloud`, ten lines down the same file, had always shared one field across a whole cloud: two loops over an ensemble in one class disagreeing about whether the field belongs inside the loop, with the correct one there to be copied.

  **The saving is the solve, so it grows with it** — 3.4x on a 2-D mirror, approaching the member count on a volume geometry whose solve is seconds and whose flight is milliseconds. The 240-evaluation Astral search was paying **2,160 solves for 240 distinct fields**.

  **The guard is self-calibrating, which is what makes it a test rather than a threshold.** A wall-clock bound is a statement about a machine (Amendment 27); what is asserted is a ratio between two things measured on the same machine in the same run — the marginal cost of one more member, against one whole solve of the same model. Solving per member costs a whole solve per member by construction. Measured at **0.18 solves hoisted and 1.16 with the solve put back**, bound at 0.5, between the two rather than hard against either.

  **The rate is now measured on this machine, on this geometry.** It was one hardcoded constant — 13 s per million nodes, from the 2-D templates on one developer's box — applied to volume solves too, where the stencil is 27-point and the coarse levels are Galerkin: on the C-trap that put a 5.9 s solve at **1.81 s**. The trajectory term is measured too, by a short pilot flight, and says which of two things happened — the whole flight where it finishes inside the window, otherwise scaled with the bias **named rather than corrected for**, because which limit binds (resolution or tolerance) depends on the model. End to end: **6.25 s estimated against 7.06 s actual**, where the same model was 1.81 s before.

  **Pilots repeat while repeating is cheap.** A single timing of a short pilot is mostly jitting: the plane rate swung between **14.9 and 29.0** s per million nodes run to run, a factor of two straight into the estimate. Cheapest of up to five, stopping once the pilot has cost about a second — self-limiting in the right direction, since the short pilot is the noisy one and is cheap to repeat. Spread went to **2 per cent**.

  **A study that varies geometry varies its own cost, so the flight is sampled across the study's own declared range.** A mirror separation scan runs **2.2x** dearer at one end than the other because it crosses a focus, and costing at the declared values alone missed it by **0.57x**. Gated on the study declaring twenty evaluations, from the arithmetic rather than by taste: three samples cost about one and a half evaluations, 7 per cent of twenty and under one per cent of the hundreds an optimisation declares.

  **Sampling a *fraction* of a flight is what makes it wrong, and I did that first.** A part-flight has to be scaled up, and the only length available to scale against is the declared **maximum** flight time — a ceiling, not an expectation. The nominal ion arrived inside the fraction and the extremes did not, so they were scaled by the whole ceiling: **3.4x over**. A sample now flies the whole flight at the real cell size, and nothing is extrapolated.

  **And the mesh a solve gets is not the one the document asked for.** Each axis rounds its interval count up to a power of two, so **cost is a step function of the cell size** and the node count is the product of three such roundings. On a 635 x 48 x 350 mm analyser — the Astral's own aspect ratio — a requested 1 mm gives **0.62 x 0.75 x 0.68 mm and 34.2 M nodes where the request implies 10.7 M**, and asking for **1.5 mm costs 7.9x less**. Nothing about that is wrong: the mesh is finer than asked and never coarser. What was missing is that somebody planning a multi-day run could not see it. **The suggested size is evaluated with the grid's own arithmetic**, because the rule of thumb — twice the finest spacing — lands *exactly* on the power-of-two boundary and rounds the wrong side: 1.24 mm gave the identical mesh, a no-op that looked authoritative. The test asks for the suggested size and checks the node count falls by the promised factor: promised 7.9x, delivered 7.9x.

  **Measured honestly on that adversarial scan: 23.1 s estimated against 30.3 s of wall clock (0.76x)**, of which about 5 s is process start the estimate excludes by design — so **0.89x of the computation itself**, at a 1 per cent run-to-run spread. Process start and JIT stay excluded and stated: a fixed cost, negligible for a multi-day study and not for a thirty-second one. SPEC.md Amendment 33.

  **A measurement mistake worth keeping:** an end-to-end check timed a scan at 50.6 s against 31.0 s minutes earlier, which reads as the estimate being badly wrong. The full test suite was running in parallel. **A wall-clock comparison on a loaded machine measures the load**, and the estimate is the one number in this project most likely to be validated that way.

- **The estimate's memory figure was a fifth of the truth, and measuring it found a driven solve holding every channel's geometry.** `einzel estimate` billed 48 bytes a node for every solve, from a comment listing three fields and some restriction buffers. Sampling the live set through real builds - a forced full collection from a second thread every few milliseconds - gives **240 bytes a node on the Galerkin hierarchy, 141.7 on the rediscretized one, 143.25 on a plane** (first stated as 153; see the review entry below), and the fields are 24 of the 240: the conductor mask and six cut-link arms of two doubles each are 105 before a field exists, and the Galerkin build stores the fine operator (64) and forms its first coarse level with scratch over the fine grid. **The Astral at a 1 mm cell was billed 3.1 GiB and needs 15.3.**

  | | predicted | measured, bytes a node |
  | --- | --- | --- |
  | Astral, 1.12 M and 8.68 M nodes (Galerkin) | 240.0 | 241.3, 240.9 |
  | segmented quadrupole, 1.09 M and 4.28 M (rediscretized) | 141.7 | 141.9, 141.8 |
  | linear ion trap, two channels | 248.9 | **353.7** before, **248.7** after |
  | diffusive run, per density node | - | 263, where 16 was billed |

  **The live set is what has to fit**, checked two ways: under a hard heap limit the Astral at 4 mm completed at 279 MiB and failed at 275 against a live 259, while unconstrained its heap reached 1.6x that, because the collector frees large arrays lazily. The statement lives beside the arrays (`SolveMemoryModel`), the hierarchy is asked of the solver (`PoissonSolver3D.UsesGalerkin`) rather than decided twice, and `SolveMemoryTests` pins it as a slope between two meshes - band 0.90 to 1.02, which one extra field fails.

  **Two channels held 353.7 where 248 was right, for two reasons in turn.** The channel loop kept each finished channel's mask in the record it returned, to report two integers. Removing that took it to 344.7, and the remaining 96 was the previous channel's cut links held by **a temporary with no name**: `mask.Cuts?.CutCount` puts `mask.Cuts` in a stack slot, and a method run once executes as first-tier JIT code, which keeps every such slot reachable until it is reused. **When a loop body allocates something large that must not outlive the iteration, give the body a frame of its own.** Both in `docs/lessons.md`.

  **The 64 M-node limit is the wrong kind of rule for memory**: at 240 bytes a node it is 14.3 GiB, ten times what `ScalarField3D`'s message says, and memory is a property of the machine while validation is a property of the document. What the code cannot index is about 358 M nodes (`node * 6` in an `int`). Recorded in `docs/cli.md` and not changed here, because the limit lived on the then-unmerged `fix/grid-too-large-is-a-refusal` branch; that has since landed as `VolumeMesh.MaximumNodes` (#48) and the restating is still to do.


- **A box may be tilted, and the Astral's convergence is the reason.** Every three-dimensional primitive is axis-aligned, which builds the devices §1's table asks for and cannot express one thing: a plate deliberately **not** parallel to another. An asymmetric-track analyser's two mirrors converge by a couple of hundred microns over a third of a metre, and that convergence is the **mechanism** rather than a tolerance — it is what makes the drift decelerate and reverse. Without it the model validates, solves, converges, and produces a drift that never reverses.

  **An attribute, not an abstraction — the fifth time.** After `log` for the Kingdon trap, trigonometry for the multipole guide, `asinPi` for the C-trap and a parametric `drivePhase` for the travelling wave. LIB-1 says to believe the signal when a device forces a change below `Einzel.Library`; each time it has been narrow and the abstraction has held. **A tilted box is a box**, so signed distance and first entry rotate the query into the box's own frame and run unchanged — a rotation is rigid, so a distance measured there is the distance in the world, and affine, so a segment fraction is preserved exactly. The only query that genuinely changes is the bounding box, which the shell extracts conductors over.

  **Half turns, so 1.0 is 180° and a right angle is 0.5** — and a test caught me getting my own convention wrong, writing 0.25 and expecting a right angle. The same confusion had reached the validator's error message, where it would have taught it to whoever tripped the bound.

  | convergence | of a cell, each end | asymmetry | step |
  | --- | --- | --- | --- |
  | 1 µm | 0.0010 | 1.6577e-3 V | — |
  | 2 µm | 0.0020 | 3.3154e-3 V | **2.0000** |
  | 5 µm | 0.0050 | 8.2886e-3 V | **2.5000** |
  | 200 µm | 0.2000 | 3.3254e-1 V | 2.0045 |

  **Proportional to a thousandth of a cell**, against a parallel control reporting **7.1e-15 V** — the cut cell doing what FLD-1 built it for, met in a new place.

  **A ladder rather than two points, and that is what found something.** Two tilts an octave apart gave **1.85** against 2 expected — a number one is tempted to explain as a second-order term. The ladder said otherwise: the ratio *rose* toward 2 as the tilt grew, which is the signature of an additive offset rather than a higher-order term, and the slope was dead constant at 0.0018 V/µm. The response was `0.031 + 0.0018 δ`, and the intercept survived down to a thousandth of a cell.

  **It was the geometry, not the tilt: the plate faces sat exactly on grid nodes.** Moving every face a quarter cell off the lattice removed the intercept completely. The mechanism is recorded as a hypothesis — a node lying exactly on a Dirichlet surface is classified inside, so an arbitrarily small tilt moves the surface off that node on one side and not the other, a whole node's change in classification rather than a small change in a cut length. **The modelling rule that generalises: do not place a conductor face exactly on a cell boundary when the quantity of interest is a small geometric perturbation.**

  **And the control proved less than it appeared to.** The parallel pair reports 7e-15 V, which reads as "the mesh is clean" — but the untilted problem is *exactly* symmetric, so it would report zero however badly discretised or converged it was. A control that cannot fail for the reason you are worried about is not a control for that reason. SPEC.md Amendment 34.


- **A volume solve may declare a face a mirror, and an ion drifting backwards is what found it.** `DirichletMask3D` has carried all six faces as settable conditions since the 3-D solver was written, and `OperatorStencil3D` honours them — **and no document could ask for one**. The plane path has had `rightEdge` throughout. Same shape as `ITransportMode` named only in a csproj, and `drivePhase` a plain double until a travelling wave needed a ramp.

  **A grounded domain boundary is a third electrode**, which this project already documented for the parallel-plate example. A stripe electrode running the length of an analyser's drift makes the field independent of the drift direction, so grounding those faces imposes an axial field the real instrument does not have. Measured on two rails spanning their domain in z: **−62,577 V/m of axial field with the faces grounded, −0.0000 with them mirrored.** The control matters — a mirrored solve reporting zero proves nothing unless the grounded one is large.

  **Found by an ion going the wrong way.** An Astral skeleton at a 3.5% injection angle should drift at +1375 m/s; it measured **−480**. With the faces mirrored it drifts at **1374 m/s against a predicted 1374**, and z-invariance is exact — the off-axis transverse field is **−31,804.2225 V/m at both 5 mm and 20 mm from the face**, to the digit.

  **The invariance check was vacuous first**, sampled on the axis where two symmetric rails give zero transverse field by symmetry: 0 compared with 0, which passes against a solve with no z-invariance at all. Off-axis it discriminates.

- **The 3-D Astral skeleton flies, and the second bug was the mirrors being inside-out.** Depth must be measured from each mirror's **mouth** inward, so U4 (+6012 V, the reflector) sits furthest from the beam and U1 (−7360 V, accelerating) nearest it. Measuring from x = 0 put the reflector at the mouth, so the ion met +6012 V on arrival instead of being accelerated in; with the grounded faces as well it escaped to **x = 4643 mm in a 635 mm analyser** and coasted, which is where 20,000,000 steps went.

  | | |
  | --- | --- |
  | flight time | **120.058 µs** against a predicted 165 mm / 1374 m/s = 120.1 µs |
  | drift rate | 1374 m/s against `v·sinθ` = **1374 m/s** |
  | steps | 16,012, against 20,000,000 |
  | transverse | y = −0.00 mm throughout |
  | energy drift | 2.16e-6, just over ACC-4's 1e-6 at a 4 mm cell |

  **And the cost picture is the opposite of what the broken run suggested.** That run spent 94% of its time flying, because the escaped ion coasted for 20,000,000 steps. Working, at a 4 mm cell in Release: **solve 5.298 s (94.3%), flight 0.321 s** — and the gap widens with refinement, since node count goes as 1/cell³ while step count goes as 1/cell. The solve is healthy rather than pathological (3 levels, limited by the thin 17-node axis; 13 cycles; factor 0.20; ~52 M node-updates/s on one core). **A number measured on a broken model describes the bug, not the instrument** — and it had already reached a planning document and inverted a priority there.

  **What it did not yet show** was 24 oscillations: at 3.5% the ion crossed the drift in **3.77**, because getting 24 needs the drift to **reverse** and the mirror convergence was not modelled at that point. ~~So the oscillation count is the first real test of the tilt.~~ **It was, and it passed** — see the reversal bullet below, and `docs/device-templates.md` for where the model now stands.

  **And a trap of my own, in the reader rather than the engine.** A probe read `flightTimeSeconds` from `--json` and printed **0.000 µs** for a 120 µs flight: there is no such key, because `flightTime` is a GRD-1 envelope, and `dict.get(k) or 0` turned the miss into a plausible zero. GRD-1 prevents the engine emitting a bare number; nothing stops a consumer reintroducing one.

- **Two measurements about running this engine, both of which change planning.** A **Release build is 3.27x faster than Debug** (2.16 s against 7.06 on the shipped C-trap), and every timing published from a development session is a Debug timing unless it says otherwise. The estimate needs no telling: because its rate is a pilot measured at runtime, it followed the speed-up on its own — 6.25 s in Debug to 0.98 s in Release for the same model. What does not scale is process start, which the estimate excludes by design, so its wall-clock ratio looks worse on a short Release run (0.60x) while its share of the *computation* is the same 0.85-0.9x as in Debug.


- **Studies use the machine, and the speedup is a fifth of what I predicted.** Evaluation-level parallelism in the two drivers whose evaluations are independent — `ParameterScan` and `ToleranceStudy` — with `maxParallelism` declared in the study file.

  | DOP | solve-bound scan | CPU-bound control |
  | --- | --- | --- |
  | 1 | 1.00x | 1.00x |
  | 4 | 2.53x | 3.93x |
  | 8 | **5.25x** | 3.92x |
  | 16 | 4.75x — *worse* | **6.74x** |

  **The machinery is fine; the solve is memory-bandwidth bound.** The control — same driver, arithmetic instead of a solve — reaches 6.74x and *gains* from hyperthreading, while the solve peaks at the eight physical cores and loses ground beyond them. So plan on about 5x, and expect a threaded smoother to disappoint for the same reason: it competes for the same saturated bus. A GPU brings its own bandwidth, which is the one thing that would lift the ceiling.

  **A number I had to retract**: the first measurement was **12.8x**, from comparing a Debug sequential baseline against a Release parallel run. Release is 3.27x faster on its own, so almost all of it was the build. Same binary, same input, one variable moving.

  **Three pieces of shared state were wrong, and all three produce plausible numbers rather than crashes.** The `WarningLedger` deduped per *study* rather than per evaluation, so concurrently one evaluation's warning silently suppresses another's identical code — a study wrong throughout reporting itself wrong on half its draws (mutation: 1997 of 2000 where the truth is 1000). The draw sequence *is* the study, so draws are made in seed order before anything is evaluated. And the exemplar message kept whoever finished first — `CONVERGENCE_ORDER_BELOW_NOMINAL` quotes its own draw's observed order — so two runs of one seeded sweep gave identical counts, identical figures, and a different example, failing `ASweepIsReproducibleFromItsSeed` on a string nobody had thought of as a result.

- **A volume solve invented a field outside its own box, and the mirror found it.** Adding `reflectAboutX` to `solve3d` looked like a one-line change and came back **double**: superposition adding two contributions where it should take a union. The union works only if each half contributes nothing outside its domain — which `SolvedField2D` has always done and `SolvedField3D` never did. It called the tricubic unconditionally, and a tricubic asked for a point it was never fitted over continues the cubic. On a 20 mm box holding one plate at **100 V**: **−486,643 V at 8.1 MV/m** 180 mm outside, four orders past the applied potential.

  **It had already been observed and misread**: the Astral skeleton's escaped ion, found 4,643 mm from a 635 mm analyser, was written down as "coasting". It was being accelerated by a field nobody declared.

  Bounded, the mirrored half reproduces the full solve to **0.00000 V of 100 applied**, and on the Astral gives the same flight — **120.0580 µs both ways** — for **1.90x** less solve, at the same 13 cycles and the same 0.1997 convergence factor.

- **The Astral's drift decelerates and reverses.** The convergence is the mechanism, and it is now measured with a clean control. Per 40 mm segment, parallel boards give **1374.34 m/s in every segment, to the last digit** — a translationally invariant analyser has no axial force — while 800 µm of convergence decelerates monotonically to 1174 m/s, decrements growing 11, 30, 38, 48, 73.

  Pushed further it turns around: at 1° injection and 800 µm the ion goes out to z = 321 mm, stops, and comes back, and the flat −843 m/s at the end is it coasting *outside* the box, which is only true because of the fix above.

  **The reversal threshold is a fourth-power law in the injection angle**: 11.447 mm at 2°, 0.4638 mm at 1°, under 0.05 mm at 0.5° — 24.7x per halving, bisected by `einzel boundary`. Halving the angle doubles the reflections *and* quarters the axial energy, which predicts a cube; the measurement says more, and two points cannot say what else.

  ~~**Not reconciled with the published instrument**~~ — **it reconciles.** That line was written when the gap was 200 µm at 2° with 24 oscillations against 11.4 mm at 2° with 4, and the remainder was in the guesses: `d1..d4` free, the board gap assumed. Reading the electrode positions off the published figure instead of guessing depths puts the reversal at **336.06 mm with 24 outbound**, against a published **310 to 360 mm, mean 335, and 24 to 26**, and the flight time at **784.90 µs** on the shipped foil mesh, about 775 mesh-converged, against 783.2 by arithmetic. (It read 336.15 mm and 786.44 µs until the compact analyzer showed the second was a rounding coin toss - see the `compact-astral-3d` entry at the end of this list.) The tilt on its own reverses at 404 mm and the published stripe shape brings it to 336, which is the design paper's own account of the mechanism rather than a discrepancy.

  **And the convergence it fitted was published all along**, in a paper already sitting unread in `papers/`: its table states the angle, which is 196 µm over the 250 mm mirror body — the spacer — and 503 µm across the 641 mm effective separation, against the 0.56 mm the model fitted knowing none of it. What does not yet agree is the injection angle: published 1.78°, fitted 2.29°, and the reversal distance goes as its fourth power.

  **Writing this paragraph twice is itself the finding.** Its first version said "334.76 mm after 25 reflections, by the mirror tilt alone, with no ion foil in the model" — taken from the handoff's own summary, which was current to section 23 of 78 while section 72 had already measured the tilt alone at 404 mm. A superseded number in **this** file steers every session that loads it, which is why the Astral state now lives in `docs/device-templates.md`, the published register in `docs/literature-targets.md`, and the chronology in `docs/astral-log.md` under a name that says it is one.

  **Known wrong and stated on the template**: the drift faces are Neumann, which says the structure repeats along z — true while the boards are parallel, false the moment they converge.

- **The linear ion trap, and the polygon it forced.** The radial-ejection trap of Schwartz,
  Senko and Syka (JASMS 2002) - the ancestor of the Velos dual-pressure design and the Stellar
  front end - has hyperbolic rods with 0.25 mm ejection slits and the x pair moved out
  0.75 mm. **The released instrument ejects radially both ways, a slit in each x rod with a
  detector behind each**; the paper's device is the prototype, cuts one, measures 44 per cent
  scan-out on it and predicts 88 for the pair. Not awkward to write in rectangle and disc: **not expressible at all**. LIB-1's
  signal fired for the sixth time, and for the first time asked for a shape: `polygon`, any
  closed outline at one potential, with a closed-form signed distance and first crossing so
  its faces are cut cells like a disc's. A square as four vertices solves to the rectangle's
  field to 1e-13; a rod as two halves meeting on a line solves to the whole rod to the bit,
  which is how a slotted rod is written so that a slot of zero height is a rod. Schema 0.9.

  **The first template was 116 KB and correct, and that was the wrong spelling.** Twenty-five
  vertices a face, two expressions a vertex, eight half-rods: parametric in the letter and
  unreadable in fact. A vertex entry may now carry `count` and `index` and stand for a **run**
  - `repeat`'s mechanism inside one outline - and the hyperbola is written once: 24 KB. When a
  generator script is needed to write a document, the format is missing what the script
  supplies.

  **Flown at a nominal q of 0.92 the ion stayed confined, and the engine was right.** The same
  document with round rods loses it in 3 µs. The 0.75 mm stretch weakens the quadrupole term
  to **0.822 of the ideal formula's** (multipoles: A2 20.55 against 24.98 V per 100 V applied),
  so "0.92" was 0.757. The paper's own q scale is the effective one - it quotes 368 kHz at
  q = 0.83, the ideal beta to a tenth of a per cent - as every trap's is, since q is inferred
  from frequency and not from metal. **A single slit's fault is a dipole** (1.26e-3 of A2)
  and a hexapole (1.84e-4) the symmetric stretch cannot touch - **and the second slit cancels
  both** (2.8e-15, 8.6e-16), because a slit in each x rod is symmetric about x. So that fault
  belongs to the prototype rather than to the instrument. What the stretch answers is the
  **octupole** (1.62e-3), the 3-D "stretch" term, as the paper says, and the quadrupole term
  moves 0.8219 to 0.8214 between one slit and two - so the working point is unaffected.

  **Resonance ejection, then the scan.** With the paper's dipole excitation (3 V + 20 mV per
  m/z at 421.3 kHz) the ion leaves from effective q 0.870 up, along x onto the x rods; with it
  off, only past the edge between 0.890 and 0.900. A twelve-ion cloud ramped at the paper's
  5,555 u/s gives **FWHM 0.75 / 0.62 / 0.64 / 0.54 u at m/z 195 / 524 / 1422 / 1522** -
  "unit resolution up to m/z 2000", which is the paper's claim, with nothing tuned. Ions leave
  at effective q 0.862 to 0.869 rather than 0.88, captured from below as a positive octupole
  allows; a mass calibration absorbs that as every instrument's does. `einzel run` now writes
  a **per-ion event ledger** (outcome, surface, time, end position), because a spectrum is a
  histogram of ejection instants and cannot be recovered from a peak width.

  **What it does not reproduce: passage through the slot.** With a 0.25 mm channel straight
  through the rod, 1 of 45 ions ejected toward it reached the detector; the rest struck the
  channel walls one to four millimetres in, at |y| = 0.125 exactly. The slot mouth is a
  diverging aperture lens for an ion leaving a 5e5 V/m field into a field-free channel, and
  the channel tolerates 0.02 rad against a beam of 0.05 to 0.15. The paper does not give the
  slot's profile; the template carries a channel depth and a relief as named guesses (0.5 mm,
  then eightfold), which passes 9 of 20 against 0. **A sensitivity, not a prediction.**

  **And the dual-pressure question, asked of the pressure alone, goes the other way.** The
  Velos analyser cell runs near 5.3e-4 mbar and its paper credits the lower pressure with
  higher resolution at a given rate. Changing only the helium at the 2002 settings broadens
  m/z 524 from **0.62 to 1.44 u** and 1522 from 0.54 to 0.90; doubling the rate at 3 mTorr
  costs less (0.62 to 0.90). The gas damps each ion's own phase before the excitation grows
  it, so at 5.3e-4 mbar, where the momentum-transfer time is ~20 ms, every ion carries its
  own phase into a hundred-microsecond ejection. The Velos compared two retuned instruments;
  this compares one number, and whether a retuned working point at the lower pressure wins
  in this model is a two-parameter study not yet run. Recorded as open, not as a refutation.

  **Retuned, the low-pressure result reconciles.** At 5.3e-4 mbar, half the paper's
  excitation (6.7 V) gives **0.62 u**, the 3 mTorr width exactly; the paper's 13.5 V gives
  1.44 and twice it 1.46. A strong excitation at low pressure captures ions early from a
  spread of phases; a gentle one lets each approach resonance. The Velos's gain was a
  retuning, and this is the part of it that matters for the width.

  **A phase may now ramp.** `ramp` on a sequence phase names where a parameter ends, and it
  gets there linearly from wherever it stood; the solved geometry's channel weights are
  interpolated in time, which is exact where potentials and amplitudes are linear in the
  parameter - checked at the phase's midpoint and refused otherwise, naming the electrode and
  the two numbers (70.7 V against 50 V for a square root). Refused for an analytic element
  (it would freeze while the solved ones ramp), ~~a diffusive phase~~ (since allowed - the density solver re-samples a changing field every step, which is what a TIMS elution scan needed), and a volume solve. A ramped
  RF flies an ion to **2 µm** of where a forty-step staircase puts it, against 240 µm from the
  held control; a ramp from zero amplitude works because the end electrodes count as solved
  states. The scan is one phase now, where it was thousands; at 200 kDa/s a 4 µs step would
  have been most of a peak.

  **The Stellar's own trap, from its paper** (Remes et al. 2024, now in `papers/`): the Velos
  Pro structure, a four-fold 0.76 mm stretch and slots in all four rods at 0.5 mTorr, as
  `stellar-ion-trap` from the same generator. Four-fold symmetry is a change of scale, not an
  aberration: the slot dipole and the stretch octupole vanish to 1e-15, the quadrupole term is
  **0.6966 of ideal** against 0.7062 for r0 = 4.76 mm, the edge is at effective q 0.900-0.905.
  Its scan at the paper's 33 / 67 / 125 / 200 kDa/s at m/z 622 gives **0.19 / 0.14 / 0.15 /
  0.15 u** (48 ions, kernel-density FWHM) against the paper's ~0.35 / 0.5 / 0.7 / 1.0 Th: a
  floor, since an ideal trap with a cold cloud ejects within a few RF cycles and none of the
  instrument's broadenings (a millimetre cloud, amplitude noise, real machining) is in the
  template. Two things a fast scan needs: the ramp must run through the stability edge, and
  ions leaving through a slot with no detector behind it must strike something - both
  templates now carry grounded housing walls, because the domain edge is a boundary condition
  and an ion that crosses it coasts out of the box.

  **The three sections, in a volume - and the prism it needed.** A `prism` is the 2-D polygon
  given a length along an axis, with the same vertex runs, an exact signed distance and first
  crossing; a square prism is a box to 3e-18 m and to the bit in a solve. `linear-ion-trap-3d`
  extrudes the same half-rods into the paper's 12 / 37 / 12 mm sections with a 2 mm-aperture
  lens at each end. The end sections 3 V above the centre make a **2.9 V well** that holds a
  300 K ion within **8.7 mm** of the centre (17.1 mm with 20 V lenses alone), where the
  excitation is uniform to below 0.001 % and carries 0.17 % of axial component - the paper's
  figure 2 as numbers. **The end offset is common, not quadrupolar**: x up and y down is a
  change of Mathieu a, zero on the axis, and makes no well; the first draft had that.

  **Space charge in the scan found a limit of the method, and the rule behind it was wrong.**
  Forty macroparticles for 4,000 ions along 10 mm of axis and 0.05 mm across it were
  softened at **1.7 mm, thirty-four times the transverse size**, so the force across the
  packet was switched off and the scan came back identical to one with no space charge, to
  the last digit, with nothing said. `spacecharge.softening` now reports the softening
  against the packet's thinnest extent on every direct-sum run. And the softening is now
  set from the packet's **three standard deviations** rather than its RMS radius - the
  spacing of points filling a box goes as the cube root of its volume, and a line's volume
  is not the cube of its length: the radius rule's number to the bit for a ball, an order of
  magnitude smaller for the line, inside the transverse size at about 2,200 macroparticles
  rather than 1.6 million. **With the force on the peak does not move**: 400 macroparticles
  for up to 400,000 ions in a half-millimetre line, scanned at 16,700 u/s in vacuum, shift
  each ion's ejection by a tenth of a unit with no common direction and the median by under
  0.1 u. The cloud size is an input to a run with no gas, and the shift goes as the
  density; a cooled cloud of that population would sit inside about 60 µm, seventy times
  denser. So the 2002 paper's capacity claim needed gas and space charge in one run, which
  was the packet integrator's missing collision hook. Also fixed: the direct-sum path called
  for an arrival peak unguarded, so a space-charge run in which every ion left through the
  rods ended in `INTERNAL_ERROR` - Amendment 15's defect, reintroduced on the other loop.

  **The volume trap scans.** A ramp on a `solve3d` weighs the channels at the phase's two
  ends as a cross-section does, its end state joining the states gathered for the channel
  decomposition so a ramp from zero has its pattern solved. Twelve ions at 16,700 u/s eject
  at effective q **0.8703 against the cross-section's 0.8685** in the same run, and the
  0.2 % is the field: the centre section's quadrupole term is **0.8207 of ideal at the
  0.5 mm cell** (0.8109 at 1 mm, converging upward) against the cross-section's 0.8223, and
  the same at the section's middle and a quarter of the way to its end.

  Two corpus examples at the paper's working point (held with the excitation off, ejected
  **Gas and space charge in one run, and a null result with a theorem behind it.** The
  packet integrator takes one collision sampler per macroparticle and lands its shared step
  on the earliest collision anywhere in the packet. A 1 mm slice of the trap's cloud - 240
  macroparticles, 50 µm across - cooled 1.5 ms in 4 mTorr of helium (560 collisions each)
  and scanned at 16,700 u/s with the packet pushing on itself shifts by **-0.001 / +0.001 /
  +0.011 u at 480 / 2,400 / 9,600 ions per millimetre** against the unpushed control, and
  does not broaden; an LTQ's cloud runs at 300-3,000 per millimetre. **The reason is the
  generalised Kohn theorem**: a dipole excitation drives the centre of mass, and in a field
  linear in position the centre of mass does not feel the mutual force - pair forces cancel
  by the third law. Checked directly: a packet in an ideal RF quadrupole pushed hard enough
  to scatter its members by 18 mm moves its centre of mass by **1.8e-14 m**. So a
  space-charge shift of a resonance-ejection peak lives in the anharmonic part of the field
  and in the ejecting ion's view of the cloud it leaves, and in this trap both are under a
  hundredth of a unit. What a cross-section cannot hold is the cloud's axial extent - a
  slice's ends spread along the axis under their own charge where the real trap's end well
  stops them - so the capacity question's remainder is the volume trap's, at days per run.

  Two corpus examples at the paper's working point (held with the excitation off, ejected
  with it on: same RF, same ion, same gas), 39 in all. Details in `docs/device-templates.md`,
  `docs/literature-targets.md` section 2, `docs/validation.md`, `docs/numerics.md`, SPEC.md
  Amendment 37 and item 14.

- **`tims-analyzer` — an ion held still against a moving gas, and the scan that lets it go.**
  The separating tunnel of the Bruker timsTOF, from the published dimensions (Hernandez 2014,
  Ridgeway 2018): 27 segmented ring boards on a 1.725 mm pitch through an 8 mm bore, the ring
  potentials going as the square of position so the axial field is linear in it, and
  nitrogen flowing entrance to exit at 50 m/s. An ion parks where the field's push back
  equals the gas drag, so each mobility has its own place along the tunnel — the elution
  relation `E_e = v_g / K` written as a distance. The register is `docs/literature-targets.md`
  section 6; the write-up is `docs/device-templates.md`.

  **The two questions are asked separately.** Whether the ring stack makes the field its
  potentials imply is geometry: the solved balance point is **21.1036 mm against a closed-form
  21.1169**, 0.06 per cent. Whether the density settles where the *solved* field balances the
  gas is physics, asked against that solved point so a discretisation error cannot hide in
  it: **21.1041 mm, one micrometre**. Three mobilities park in inverse proportion to 0.17 per
  cent. The field peaks at **41.2 mm of 46.6**, because the exit element flattens the gradient
  as it is approached, so 88 per cent of the metal is analyser and the widest mobility it
  holds is set by that peak. Two things the geometry taught: the solve's own grounded edge
  reversed the gradient over the last 8 mm until entrance and exit elements at the end
  potentials stood in for the funnels — and their **length** is load-bearing, a tube shorter
  than its bore letting the grounded plane reach down the axis; and the operating point is
  deliberately Hernandez's low-field one (18.6 Td) rather than the commercial 45-150 Td,
  because at the commercial point a low-field mobility is not valid.

  **The elution ramp runs, and it needed the density solver to see time.** A `sequence`
  phase may `ramp` a parameter in a **diffusive** phase now: `DriftDiffusion.Run` takes a
  field *function* and re-samples the coefficients, the stability step and the face operator
  every step (bit-identical to the fixed path when the function returns the same field; a
  reversing field brings the packet back; a *rising* field keeps the step stable, which a
  step chosen at the start would not). Hold 300 µs at 60 V, walk the exit potential to zero
  over 8 ms: the least mobile ion elutes first — TIMS order, opposite to a drift tube — and
  both eluting ions let go about **10 V below the quasi-static `v_g / (K E_peak)`**, because
  the settling time `L²/2KV` is 0.4-0.8 ms against an 8 ms ramp and the packet lags the
  sliding balance. That time constant is confirmed independently: three packets released
  from one point and read after a millisecond sit where `7 mm · exp(−1 ms/τ)` puts them, to
  a twentieth of a millimetre. `run --json` reports `meanArrivalUs`/`arrivalSpreadUs` and
  writes the whole spectrum as `<name>.arrivals.csv`.

  **But 45 of 97,770 ions arrived, and the most mobile ion none.** This stage carries no RF,
  so radial diffusion puts ~3 mm on the density in the five milliseconds before release and
  the bore takes it — 93,000 on rings 11-13, where the packet parks. The machinery, the order
  and the lag are measured; the transmission and the widths are not the instrument's, and
  **RF confinement is the next stage** (SPEC.md item 15): a quadrupolar RF alternating
  between the four segments of each ring, which is not axisymmetric but whose pseudopotential
  is, so it enters the r-z density solve as the collisional well the funnel already measured.

  **Two defects, both producing clean output.** A staged geometry with no drive was given a **placeholder 1 Hz clock** with a
  comment saying nothing read it; the pseudopotential wrapper read it, saw a finite period,
  and cycle-averaged the whole ramp over a one-second cycle — a trap that measurably holds
  let its density drift out at gas speed during the hold, and the leg discarded the wrapper's
  own warnings. And a model declaring `diffusion` whose sequence never left it was routed to
  the plain diffusive path, which reads the field once through the time-free interface — so
  with the refusal lifted the ramp ran **silently ignored**, exit 0 - the sixth sighting of a
  time-varying quantity reached through a time-free interface answering at an arbitrary
  instant; and the leg, once reached, discarded the wrapper's own warnings, the seventh time
  evidence about a computation's quality was dropped at a seam. A third, smaller one: an
  onset read as the first non-empty bin came out at 22 µs, during the hold, because
  Scharfetter-Gummel moves 1e-100 of an ion across the collecting face from the first step;
  an onset is a quantile. All three in `docs/lessons.md`.

- **The TIMS tunnel confines, and four flat segments are 1.27 of a quadrupole.** The real
  tunnel holds ions off the bore with a quadrupolar RF alternating between the four segments
  of each ring (850 kHz, 200 Vpp in Ridgeway's example), the same on every ring. A quadrupole
  is not axisymmetric and cannot be electrodes in the half-plane solve; **its pseudopotential
  is** — the field magnitude depends on radius alone — so the template carries it as the
  analytic `idealQuadrupoleRf` lying *across* the tunnel axis, superposed on the solved DC
  gradient, and the collisional pseudopotential the funnel already measured carries it into
  the density solve. That needed one attribute below the library, `axis` on the analytic RF
  element (schema 0.11): the sixth time a device has asked for a change there and the sixth
  time it was attribute-sized. Default z, so every earlier document computes what it did to
  the bit; a permutation rather than a rotation, so a point on the axis is at zero field to
  the bit.

  **How much of a quadrupole the segments are is solved, not assumed** — the RF is the same
  on every ring, so a cross-section is the right solve for it. Four sectors round the 8 mm
  bore at ±V give a quadrupole term of **1.2577 / 1.2696 / 1.2727** of the hyperbolic ideal
  at 1.0 / 0.5 / 0.25 mm gaps, closing on the square wave's **4/π = 1.2732** with none — a
  square wave's fundamental is larger than the square wave, so flat segments are *more* of a
  quadrupole than hyperbolae at the same voltage. The 12-pole is `(r/r0)^4/3` of it, as the
  series says, and the field magnitude varies round the circle by 0.012 % at the 0.3 mm the
  cloud occupies, which is what licenses the axisymmetric treatment. The template carries
  the 0.5 mm-gap fraction as a `fitted` parameter with a test tying it to the solve.

  **With it on, measured over a 600 µs hold**: the bore takes **0.0000 %** of the density
  against 26.0 % without; the packet centre moves 0.4 µm, since the RF has no axial
  component; and the rms radius is **0.3406 mm against 0.3423** for Boltzmann in the
  collisional well — and **0.2805** for the collisionless one. The well is exactly harmonic
  and Scharfetter-Gummel's equilibrium is exactly Boltzmann, so the width is a closed form
  with the engine's suppression factor (0.685 at 2.6 mbar, the damping rate two thirds of
  the drive) in it, and the textbook formula is 21 % wrong — measurably, which is the control.

  **A validity check fired on its proxy.** `rf.quiver-exceeds-mesh` compared the largest
  quiver to the *density* grid's cell, and at the bore the RF sweeps an ion 0.29 mm against
  a 0.125 mm radial cell — a non-suppressible violation on a field that is exact whatever the
  quiver. The check is about the representation of the *oscillating* field, so it now asks
  `OscillatingResolutionLength`: infinite for an analytic drive, the solve cell for a solved
  one, and never the DC gradient's. The funnel warns as before. Two conventions are stated
  as choices: "200 Vpp" read as ±100 V per segment with neighbours in antiphase, and a 0.5 mm
  segment gap the papers do not give.

  **Confined, the scan delivers every ion, and the wall was never moving the peak.** The
  same hold-and-ramp with the RF on: **100,000 of 100,000** arrive for each of three
  mobilities, in TIMS order, at medians within 25 µs of the unconfined survivors' — the bore
  had been selecting ions, not shifting them. First resolving power **R = 11 / 8 / 5** for
  K × 0.75 / 1.0 / 1.5 at 50 m/s of gas and 7.5 V/ms, against the register's law's
  24 / 19 / 14 with a guessed plateau length, the `K^(-3/4)` trend present; Hernandez's
  100-250 is at 1.5-2.6× the gas speed and R goes as v_g. Elution voltage is linear in 1/K
  to 2 per cent with a −8.5 V intercept that is the release lag turned into volts, and the
  lag is 0.75-0.9 of the plateau-transit formula. **And a review comment on the elution PR
  was right, and these runs show it in numbers**: the hold phase assembled its operator
  130-253 times for a field that never changed, because the phase-change probe sampled the
  boundary (already the next phase for a staged field) and the instantaneous RF (which
  differs from itself at any two instants). Fixed - inside the phase, on the cycle-averaged
  field, to a tolerance - and now counted per phase in the JSON, so a hold reports one.

  **The scan-rate law is recovered where it should hold and broken where it should not.**
  Six ramps from 4 to 128 ms on the reference ion at 50 m/s: **R = 3.3 / 8.3 / 13.5 / 18.2 /
  22.3 / 25.7**, every ion collected. The law says halving β raises R by 2^(1/4) = 1.19; the
  measured ratio falls 2.52 → 1.63 → 1.35 → 1.23 → 1.15 and closes on it from above. The
  fast-ramp shortfall is the release lag — the exit potential at the peak is 13.1 V at 4 ms
  against the quasi-static 32 V, 30.6 V at 128 ms — so `V/(β Δt)` collapses as `V(t_peak)`
  while the width in time barely moves. **At Ridgeway's gas profile R doubles** (21.6 at
  8 ms, 37.2 at 32 ms - **both 256-interval numbers; the 8 ms one is 24 per cent below its
  mesh-converged value and the 32 ms one has no ladder under it at all**), the profile
  authored as imported velocity and pressure fields from
  the register's own numbers, the parking point moving to 24.92 mm against 25.2 predicted
  by hand from the local gas speed and the `1/n` mobility. The gap to Hernandez's 100-250
  is arithmetic now - his 100-300 ms ramps, his 140 m/s optimum, an accumulation plateau -
  with about 1.5 left in the width's own composition. `mobilityResolvingPower` (V/ΔV off
  the arrivals against the ramped parameter) is a Class B figure of merit, and every
  diffusive figure takes the sequenced path for a sequenced model. **And the mesh bias is
  not uniform along the sweep, which is what makes the sweep's own slope untrustworthy.**
  Doubling the mesh moves R by **16.9 per cent at 8 ms and 0.8 per cent at 128 ms** on this
  profile: a coarse mesh adds roughly the same numerical width to both, and the long-ramp
  packet is ten times wider, so the inflation is 69 per cent of the variance at one end and
  2.7 at the other. The exponent over 8 to 128 ms therefore reads **0.306 at 256 intervals,
  0.252 at 512 and 0.215 extrapolated**, against the law's 0.250 - so the agreement at 512
  is two unequal errors cancelling. A 1024 rung at 128 ms was costed and not run: 14.29
  hours at 512, about fourfold that at 1024, for a further 0.31 per cent. **The
  simulated window is separate from the declared ramp and may be stopped past the peak**,
  which halves the 8 ms rung's step count and reproduces its spectrum to every printed
  digit. Details in `docs/device-templates.md`.

- **`tims-front-end` — the funnel and the gate, and the fringe a bounded field needed.**
  Hernandez's 50 mm entrance funnel (26 to 8 mm, sixteen plates on a 3.1 mm pitch, the RF
  alternating plate to plate, a 25 V DC drop) and an entrance gate in front of the analyser,
  with his fill / trap / ramp as phases. A 2 mm-wide packet released 40 mm up the funnel:
  **99.993 per cent survives with the funnel RF on against 65.28 with it off** — the rest on
  the last three plates and the gate, every loss named — and it parks at **21.09 mm at a mean
  radius of 0.29 mm**, which are the analyser template's own balance point and its own
  confined radius. That coincidence is what makes the delivery figure mean something.

  **The first version stopped 42 per cent of it two millimetres short of the tunnel, and a
  steeper funnel gradient made that worse** (54 per cent at 50 V of drop, 71 at 100 V) — the
  signature of a barrier rather than of a push too weak. Not the instrument: the tunnel's RF
  is an analytic element bounded to the tunnel, and a region's edge was a step, so an ion
  arriving at radius r met the whole pseudopotential well at once with nothing to squeeze it
  inward first. A real ring's field decays over about a bore radius, and across that fringe
  the radial gradient acts before the axial one bites. Schema **0.12** adds `fringe` to a
  region: a linear rise from nothing at the face to full strength that far inside, the
  potential continuous, the field the gradient of the fringed potential (checked by central
  differences), and **no fringe bit-identical** to what a bounded element was. The
  region-step warning gives way to a note that a fringe is a declared shape, not a solved
  one. **A boundary the model has and the instrument does not is a force the instrument does
  not have** — `docs/lessons.md`.

  **The gate gates, and what it holds back it loses**: at 30 V nothing enters the tunnel and
  the whole packet, pressed against the gate by the gas and the funnel's gradient, ends on
  the last plate — which is why the instrument also diverts the beam with a deflector plate
  during the trap. A gate alone is a beam dump.

  **Left open, and it points at an optimisation.** The whole sequence end to end did not
  finish — 4.75 CPU-hours at 512 x 64 over 18 ms, then 40 minutes at 256 x 32 over 12 ms. A
  ramped diffusive phase re-assembles its operator every step, and here every assembly samples
  the **solved** funnel RF at sixteen instants per node for its cycle mean and mean square,
  which is sixteen bicubic interpolations per node per step; the analyser's own ramps are cheap
  because their RF is analytic. **But the ramp moves only DC** - `exitPotential` moves and the
  RF amplitudes do not - so the mean-square field, the expensive half, is the same at every
  step. **That cache is now built, and the measurement both corrected the arithmetic and
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

  **And the jitter was the ramp too.** The covariance carries the RF phase the averaging
  window opens at - `2cov = (A r T/N)[-cos(phi0) + cot(pi/N) sin(phi0)]`, because
  `sum s cos(2 pi s/N) = -N/2` while `sum s sin(2 pi s/N) = -(N/2) cot(pi/N)`, five times
  larger at N = 16 - and a diffusive step is set by a stability limit, so every assembly opens
  somewhere else in the cycle. The closed form for the swing predicts the measured 1.8e-5 as
  **1.94e-5**.

  **Fixed by holding the operating point, which is what a pseudopotential is defined at.**
  `AtOperatingPoint(t)` holds non-oscillatory time dependence and leaves the drive
  oscillating, defaulting to `this` so an unsequenced model is untouched by construction. On
  the shipped analyser the well's movement across a cycle falls from **3.15e-6 to 6.53e-14**,
  with the unfrozen path in the same run as the control. Both halves of the operating point
  are held - stage and ramp fraction - so a window straddling a phase boundary no longer
  averages two operating points either, which changes a *held* sequenced run as well as a
  ramped one. 1,346 tests pass.

  Three things worth keeping. **Two obvious alternatives are rejected by arithmetic** rather
  than by building them: sampling the cycle more finely does not converge it away, since
  `cot(pi/N) -> N/pi` and the swing tends to `4rT/(pi A)` independent of N, and detrending the
  window instead of de-meaning it removes `6/(N^2-1)` of the well - 2.4 per cent at N = 16.
  **The guarding test passed and never exercised it**, because its harness builds a fresh
  field per instant and so was holding the operating point by hand: it asserted the intended
  design rather than the implementation, which is why it said "rebuilds once" while the
  shipped analyser said thirty of thirty. And **both recorded controls measured a quantity the
  hypothesis predicted no change in** - one a step-to-step difference, the other my own
  sampling at whole drive periods, which pins the phase and is stroboscopic.

  **And the cost is now decomposed, so the study is affordable.** Measured on a probe that is
  the shipped model with every phase scaled to a thousandth - the step is set by the field and
  the mesh, not by the window, so step count scales with duration and the solve is a one-off:
  **199 s as found, 26 s with the cycle mean in closed form, 15 s with the coefficient sweep
  across cores**, which is 55 hours down to about **4**. Cross-checked at 1 %: 1,842 s measured
  against 1,990 predicted, and ten times the assemblies. The answer moved 1.2e-15 across the
  whole of it.

  **The cycle mean needed no samples.** `DirectPotentialAt` evaluated the composite potential at
  sixteen instants per density node every step; a solved field's potential is linear in its
  channel weights, so the window mean is `sum_k mean(w_k) phi_k(x)` - one evaluation per channel
  and no time sampling, with `RfWaveform.Mean` in closed form. **7.7x**, guarded on every drive
  completing a whole number of cycles in the window and falling back to sampling where it
  cannot. It corrects a claim recorded above: the direct term needing "its sixteen samples
  whatever the ramp moves" is true of the composite and false of the parts.

  **The coefficient sweep threads 5x and the Gauss-Seidel sweep 1.09x**, on the same nodes,
  three runs each - so the second was rejected rather than kept. **The memory-bandwidth ceiling
  is a property of a loop, not of a subsystem**: bicubic superpositions are compute-bound and
  cache-resident, a Gauss-Seidel cell is a few flops against several array touches plus an
  integer division per face. Bit-identical either way, since nothing is summed across nodes, and
  verified by forcing the threshold to 1 so every diffusive test takes the parallel path.

  **What is left is the step count.** 56 ns a step where the diffusion limit allows 1.26e-4 s -
  a factor of 2,236, the drift limit binding - so the window is ~142,000 steps whatever the
  per-step cost. The implicit scheme gains against the diffusion limit and buys nothing here.
  **And `einzel estimate` says 8 s against 197,000**, four orders low, with its basis line
  stating why in prose: the drift limit needs a field the estimate has not solved. A runtime
  pilot of a few microseconds would return the real step; the estimate already measures its
  solve and flight rates that way. Not built.

  The study's own question stays open:
  whether the arrival width is the analyser's own or whether the delivery leaves an axial
  spread the ramp then reads as mobility. Not carried: a deflector plate, a continuous fill,
  the funnel's own higher-pressure gas, an exit funnel. Details in
  `docs/device-templates.md`.

- **Several ion populations at once, and the one thing that couples them.** A model may
  declare `species` in place of `ion` (schema **0.13**), each entry carrying its own
  mass-to-charge, charge number, mobility and population, and a diffusive run steps them all
  through one field. **Every coefficient a species needs is its own** — mobility sets its
  drift, charge and gas temperature set its diffusion through the Einstein relation, charge
  sets its Scharfetter–Gummel thermal voltage, and in a driven structure mass and
  momentum-transfer rate set the well it feels. If the field were its own too, N species would
  be N independent runs. They are coupled by **exactly one quantity, the potential their total
  charge raises**, which no sequence of separate runs computes.

  | | |
  | --- | --- |
  | one species through the mixture path vs `DriftDiffusion.Run` | **0 of 8385 nodes differ**, same steps, population equal to 17 digits |
  | two mobilities parking vs `v_gas / (K·slope)` | 3.19 vs 3.19 mm, 6.38 vs 6.38; **position ratio 2.000 against a mobility ratio of 2.000** |
  | a second population, mean field **off** | **0 of 4257 nodes** of the first differ |
  | the same, **on** | first displaced **−91.6 µm**, away from its neighbour |
  | equal and opposite polarities | **exactly 0 V and 0 C** against 0.652 V alone |
  | the shared step | set by the quicker species, **10.0×** shorter than the sluggish one needed |

  **Three of those are controls and they carry the weight.** Bit-equality against the
  single-species path is what keeps the two from drifting; the numerics are literally the same
  functions and only the loop is new. A second population being *unfelt* with the mean field
  off says the field is the **only** coupling — a run that merely differs proves something
  changed. Exact cancellation says the source is a **signed** sum: an implementation that
  solved each species and added the potentials would land *near* zero with solver round-off
  rather than *on* it.

  **`"spaceCharge": "meanField"` is the diffusive mode's method**, wired to both diffusive
  paths through one shared helper — `q·n` on the tracked grid, one Poisson solve, added per
  node so drift, the SG exponent and the stability limit all come from one field. Conductors
  screen it exactly. Reported per phase in a sequence, **absent rather than zero** where none
  was asked for. A method that cannot act is refused rather than ignored, both ways round.

  **The step is shared and has to be** — a mutual field between densities at different times is
  not a field between anything, the same argument `PacketIntegrator` makes in the discrete
  case. **What goes wrong without it is not what the textbook says**: an overlong explicit
  Scharfetter–Gummel step here does not go negative, it **creates ions** — 1.0376% more than
  were launched, with the lowest density anywhere still exactly zero. My positivity test passed
  with the bug restored; the conservation test kills it.

  **A driven mixture needs one well per species.** The pseudopotential is built from charge,
  mass and momentum-transfer rate, so one RF structure is worth **30.55 V to m/z 200 and
  3.055 V to m/z 2000** at the same point — a factor of 10.0, exactly 1/m, and the mechanism by
  which a driven guide is mass-selective at all. A shared one is refused by name.

  **Every way of saying it twice is refused**: `ion` beside `species`, `transport.mobility`
  beside `species`, a cloud population beside species populations, a list of one, and a
  trajectory-only run. `einzel schema` carries the lot **by reflection with no schema edit**.

  **And the well cache stopped being gated on the ramped path.** It was built only when the
  field was a function of time — true when written, false the moment the density's own charge
  started forcing re-samples. A self-field re-sample leaves the *applied* field untouched, so
  the well is unchanged by construction, and rebuilding it at sixteen samples a node over the
  whole grid per species per refresh was the dominant cost of a driven mean-field run.
  Verified to change no answer against 259 transport and 27 corpus tests.

  **Also fixed, and it is the fourth time**: the terminal decided whether to print a flight
  time by listing the modes that have none (`Diffusion is null`, then `&& Sequence is null`).
  It asks `run.HasFlightTime` now, a **required** member set at each of four construction sites
  with its reason, so a fifth kind of run fails to compile until somebody decides which it is.
  SPEC.md Amendment 41; details in `docs/model-format.md`, `docs/pressure.md`, `docs/lessons.md`.

- **The tandem TIMS tunnel holds about 10^7 ions, which is the published figure by another
  route.** Two populations of m/z 622 differing only in mobility, held together in the storage
  region for 2.5 ms with and without their own charge:

  | launched | held, charge off | held, charge on | width | gap |
  | --- | --- | --- | --- | --- |
  | 10^5 | 8.07e4 | 8.07e4 | 0.65 → 0.91 mm | 6.30 → 6.36 mm |
  | 10^7 | 8.07e6 | 7.51e6 | 0.65 → 2.31 mm | 6.30 → 8.43 mm |
  | 10^8 | 8.07e7 | **1.39e7** | 0.65 → 2.56 mm | 6.30 → 9.04 mm |

  **Launching ten times more than 10^7 holds only 1.8 times more** — the tunnel stops
  accepting between **8e6 and 1.4e7**, the top of Silveira's stated 10^6 to 10^7, which they
  reached by bounding a free-space line charge that neglects the electrodes entirely. The
  uncharged column is the control: with no charge the tunnel holds the same 81 per cent at
  every population, so what saturates is the charge and not the geometry. Where that 19 per
  cent goes is **not established** and is recorded as not established.

  **The degradation has a shape a designer could misread**: the charge pushes the populations
  *further apart* (6.3 → 9.0 mm) and the separation still gets worse, because the peaks widen
  faster than their centres move. And broadening starts far below the capacity — 1.4× at 10^5
  ions — which does not contradict the published estimate because the two are about different
  quantities: theirs is the packet's field against the analysing field, of order a per cent,
  while what sets a *held* packet's width is its own potential against kT/q. Measured: **0.105
  V = 4.1x thermal** at 10^5, 1.783 V = 69x at 10^7, 2.519 V = 97x at 10^8. Already four times
  the energy that would otherwise set the width, at the lowest population tried.

  **Two things it is not, both recorded with the measurement.** Its ratio is spatial — gap
  over width in millimetres, at equilibrium, with no elution ramp — so it is one to two orders
  below a published resolving power by construction and must never be set beside one. And its
  ion counts are not comparable with theirs without converting to charge per unit length:
  their reference spreads 10^6 over 23 mm, these packets sit in under a millimetre, so the
  10^5 row is **0.29x** their line density rather than a hundredth of it.

  **Cost**: 14 minutes for six configurations; an uncharged one is 30 s against a mean-field
  one at 220-330. The geometry is solved **once** for the study — re-solving 55 rings and two
  funnels per configuration ran over an hour without reaching its first line of output.

- **The published TIMS space-charge estimate reproduces, and my prediction about it was
  wrong.** Silveira's Eq. 4 for a million charges on a 23 mm line gives 61.68 V/m at 2 mm,
  matching their quoted "roughly 0.6 V/cm"; the solver gives **62.28 V/m in the real 4 mm
  bore, 1.010 of theirs**. I predicted the bore would screen that and make their free-space
  estimate conservative by a computable factor. **It does not screen it at all** — moving the
  wall from 4 to 32 mm moves the field at 2 mm by **1.4%**, monotonically, while moving the
  potential at that very same node by **3.07× against 3.12× predicted** from the closed form.

  **Gauss's law is why, and it means their neglect of the electrodes is not an approximation
  for this quantity.** Induced charge on an axisymmetric bore sits at larger radius than the
  point being read, and a cylindrical shell contributes exactly nothing inside itself. Which
  boundary condition applies is not a free choice: made no-flux instead of earthed — a mirror
  rather than a wall — the same bore takes **31%** out of the field, because a mirror images
  the line charge and an image is not a shell. My first wall study also reported a
  *non-monotone* approach that read as physics and was my own grid: at a fixed interval count
  the cells coarsen as the domain grows.

- **`einzel report`, and the two defects reading a result document found.** An account of a
  project's runs that a person can read: one self-contained HTML page - per run, the model and
  its hash, the engine and solver-behaviour versions, the numbers with their units and
  intervals, the warnings by severity, what it wrote - with `--json` carrying the same account
  so AGT-2 holds. **Every input existed and nothing rendered them**, so following a stretch of
  engine work meant reading terminal scrollback or a git log.

  **A view, not a recorder, and that is now a property rather than a claim.** PRJ-4 puts the
  durable record in the model document and its history, with `results/` regenerable; the report
  reads what is already there and holds no state. Two tests hold it: nothing is added to
  `results/`, and two reports over the same runs are the same page but for the instant each was
  rendered at - **a recorder passes the first and fails the second**. Drift comes from `einzel
  verify` rather than a second implementation of a distinction that took thought to get right.

  **It is the first thing here that ever read a result document back** - `verify` walks
  manifests and never opens one, `test` re-flies the model - so a producer with no consumer had
  been unchecked however many tests it had. Both defects it found had been true of every run
  this project has stored:

  - **The sequenced run path wrote a manifest and no result.** Three of the four run paths
    stored one; that path stored provenance and no answer, and it is the path every TIMS study
    takes, so the runs whose answers were missing were the ones most worth reading. Found on a
    real project holding four manifests and no results. The recurring "capability wired into
    N-1 of N paths" shape - found by *writing the consumer* rather than by a failing test.
    It was also the one path storing **absolute** artifact paths, so its manifest named files
    by where they sat on the machine that wrote them.
  - **A result document did not read back into the record that wrote it.** The emittance fields
    are `required double?` - this surface's way of saying the construction site must decide and
    the answer may be nothing - and `WhenWritingNull` omits a null on the way out while C#'s
    `required` demands it on the way in. So a document was unreadable exactly when a value was
    *absent*, which is a trap or a run where nothing arrived: PRJ-3's "regenerate and compare"
    was impossible for every such run, and nothing said so because **writing succeeded**.

    **And my first fix was the wrong one of the two, caught by a test written months earlier.**
    Writing every required property including its nulls round-trips just as well - and changed
    the published document for every ensemble run, against this surface's own recorded policy
    that an undefined measurement is *absent*, with a test spelling out that "a consumer
    distinguishes 'no orientation' from 'zero' by the key not being there". Both fixes satisfy
    the rule as I had written it, and one breaks something else, so **the rule as written was
    underdetermined**. The precise version is which requirement is which: absence of
    `required int Launched` is a malformed document, absence of `required double?
    EmittanceMmMrad` is the encoding of *no value* - so it is fixed on the **reading** side and
    the document does not change at all. Type level rather than per property, because the next
    `required` nullable would be declared without an attribute and go unnoticed until something
    read it back.

  **The test for the second had to straddle the switch.** A packet that arrives writes every
  field and round-trips fine, so the four-path test cannot see it at all; the discriminating
  case is a run whose ensemble measured *nothing*. Same rule this project has recorded three
  times for dimensionless numbers, met here on an absence.

  **And I made the same defect I had just written the command to expose.** `results/` holds
  *two* kinds of answer - a run writes `X.result.json` beside its manifest and a **study**
  writes `X.json` - and the first version looked for the first only, so every sweep, scan,
  optimisation and boundary search in a project was reported as a run that had stored
  nothing, loudest on the projects with the most work in them. What would have caught it is
  enumerating the *writers* into `results/`; I enumerated the run paths I was already
  reading. Fixed by a rule rather than a list of kinds - **the answer sits beside the
  manifest under the manifest's own stem** - and the three empty states are now separate
  fields, because "nothing was stored", "a study's answer is there and this page does not
  draw it" and "a document is there and this build cannot load it" each call for something
  different from the reader.

  **And two presentation defects of my own, both "plausible wrong value".** A fraction was
  scaled to a percentage and its interval was not - one quantity in two units side by side,
  which is the ambiguity §9 refuses a model document for - reporting a transmission a hundred
  times small and reading as exactly what a trap reports. And the hatched band that marks a
  tainted result was keyed on `IsSuppressible`, false for everything above advisory, so the
  mark that exists for the one class GRD-3 says must never be skimmed landed on a note about a
  convergence floor and on almost every warning there is. **A mark on everything marks
  nothing** - the same teaching GRD-3 warns about, from the other direction. Four severities,
  four levels, the hatch for `ValidityViolation` alone. Also: `F2` turns a 99.9976 %
  transmission into `100.00 %`, which claims every ion arrived when 0.24 of ten thousand did
  not, so the format widens only where rounding would land on nothing or everything without
  being there. Details in `docs/cli.md`, `docs/lessons.md`, SPEC.md Amendment 43.

- **The TIMS analyser's resolution floor is a closed form, and the mobility cancels out of
  it.** The front-end study's open question was whether the arrival width is the analyser's
  own or whether the delivery leaves an axial spread the ramp reads as mobility. Answered -
  and the closed form came first, on paper, before any run.

  Near the balance point the net axial drift is linear in displacement, so the stationary
  state of drift against diffusion is a Gaussian with `sigma_z^2 = D/(K|dE/dx|)`, and the
  **Einstein relation cancels the mobility**: `sigma_z^2 = (kT/q)/|dE/dx|`. So the floor
  depends on the gas temperature and the axial field gradient and on **nothing else** - not
  the ion, not the gas speed, not the pressure. Those set *where* a mobility parks, which is
  what separates species; they do not set how wide either one is. The knobs that narrow a
  packet are a steeper gradient and a colder gas, which this project had not identified.

  | | \|dE/dx\| at the parking point | sigma_z |
  | --- | --- | --- |
  | nominal `2V/L^2` | 55,319 V/m^2 | 0.6836 mm |
  | **solved field, differenced off the exported potential** | **50,667**, 0.916 of nominal | **0.7143 mm** |
  | **measured, parked** | - | **0.7119 mm** |

  **0.34 % once the gradient comes from the solved field**, and the 8 % shortfall is the exit
  element flattening the gradient - already recorded as the field peaking at 41.2 mm of 46.6.
  The mirror's four-penetration-depth rule again: the formula is right and the number fed into
  it is not. Two free checks: the solved axial field at the parking point is **-1170.03 V/m**
  against `v_gas/K` = 1168.2 (**0.16 %**, the elution relation confirmed with no ion
  involved), and the width is **mesh-independent** - 0.7119 mm at both a 0.47 mm and a 0.23 mm
  cell, identical to four decimals, because Scharfetter-Gummel's zero-flux state *is* the
  Boltzmann factor. The **radial** width does move with the mesh (0.2043 to 0.1643 mm), which
  is the control that makes the axial claim mean something.

  **And the answer to the question is the trap duration rather than the delivery.** A matched
  pair differing in one parameter - where the packet is released - both stopped at the end of
  the trap, because the width the ramp reads is the width the packet has when the ramp starts:

  | at the end of the trap | axial sigma | radial |
  | --- | --- | --- |
  | delivered, 40 mm up the funnel | **1.7057 mm** | 0.6541 mm |
  | parked at the balance point | **0.7118 mm** | 0.1643 mm |

  2.4x wider - but the parked packet is **settled** (0.7119 then 0.7118, so it has forgotten
  its 2 mm launch) and the delivered one is **still narrowing** (1.9318 then 1.7057). So
  delivery does not imprint a width; the delivered packet arrives wide and has not finished
  relaxing in the 300 us the shipped sequence gives it. Prediction on record for the run that
  was killed before it landed: the variance relaxes with `1/(2K|E'|)` = 231 us and the
  delivered packet implies 1019 us, **4.4x slower**, which is either a tail dominating a
  second moment (sharp with a shoulder, not broad) or the linearisation failing over a
  +-2 mm packet.

  **`MobilityBalanceWidthTests` pins the closed form**, exact to every printed digit on an
  analytic linear field, mesh-independent to 0.00 um across a fourfold refinement, with the
  mobility cancelling measured as a width ratio of **1.00000**. **And running the mutations
  refuted the test's own comment**: I wrote that a broken drift-to-diffusion ratio would move
  the width with the mobility. It does not - `D` x 1.44 moves the *parking point* by 1/1.44
  and leaves the width alone, while `kT` in the flux x 1.44 moves the width by sqrt(1.44).
  The equilibrium is `exp(-q phi/kT)`, which contains `kT` and not `D`, so **the parking
  point is the Einstein check and the width is the Boltzmann check** and I had them
  backwards. The mesh-independence test passes under both mutations, reporting a consistent
  and absolutely wrong 1.2000 mm at every mesh.

  **The per-phase packet width now reaches the report**, which is what made any of this
  askable - `DensityField.Spread()` has existed since the diffusive mode was built and the
  sequenced phase record carried only the centroid, the recurring "a thing the mode computed
  that nothing downstream could see" shape. One field on **both** sides of a conversion, since
  SEQ-1's own subject is that position is the one thing both descriptions carry.

  **And the full sequence has now failed to finish three times** - 4.75 CPU-hours, then 40
  minutes, then 7.6 wall-hours against a 4.17 h estimate before a Windows update rebooted the
  machine - **with no output on any attempt**, so whether the estimate is low or the run does
  not terminate has never been observed. Two rules in `docs/lessons.md`: a run measured in
  hours must emit something before it ends, and **ask which phase the number lives in before
  paying for the whole pipeline** - the matched pair cost 39 and 122 minutes and removed a
  second claim (that the ramp is modelled right) from the answer. Details in
  `docs/device-templates.md`, `docs/literature-targets.md` section 6, `docs/lessons.md`.

- **A run measured in hours now says where it is, and the width's relaxation time is measured
  to one per cent.** Two things, and the first is what made the second cheap.

  **A sequenced run reaches the report as a timeline rather than as five scalars.** The
  per-phase packet width had been added to the result document the day before - because a
  TIMS study's open question is *how wide is the packet when the ramp starts* - and
  `einzel report`'s sequenced arm yielded phases, conversions, ions arrived, mean arrival and
  arrival spread: every property of the whole run, and not the one quantity the study was
  about. Computed by the solver, carried through the document, absent from the surface a
  person reads. **The same shape the report command exists to expose, in code a day old.**

  A table rather than more rows, because a flat name and value has nowhere to put the instant
  a number belongs to. That makes it a measuring instrument with no new capability at all:
  **split a hold into phases of identical settings and the table is a relaxation curve**, one
  row per boundary. A diffusive phase's trajectory count is **absent** rather than zero, since
  a density is not a count of anything and a zero beside a population of ten thousand reads as
  an instrument that lost everything - RND-8's argument met on a number instead of a drawing.
  One rendering, shared with a checkpoint's, so a killed run's phase cannot be described
  differently from a finished one's.

  **Measured that way: `tau = 1/(2K|E'|)` = 231 us, against 230.2 / 231.6 / 233.1 / 234.5 us**
  over intervals of 100, 200, 400 and 800 us. A packet released 2 mm wide comes down through
  1.6662, 1.4059, 1.0613, 0.7861, 0.7143, 0.7117 to 0.7117 mm and stays - so the equilibrium
  the closed form predicts is reached from four times that width, and the excess over it decays
  as a plain exponential at the predicted rate across four octaves. Every hold assembled its
  operator **once**, which is the held-phase fix doing what it was for.

  **And it refutes one of the two explanations on record for the delivered packet's 4.4x
  slower relaxation.** The candidates were a tail dominating a second moment or the
  linearization failing over a packet several millimeters wide. **It is not the width**: the
  parked packet's first interval is measured at sigma = 1.67 mm - plus or minus five
  millimeters at three sigma, more than twice the span the objection was about - and relaxes at
  230 us there, the predicted rate, from the widest point on the curve. What is left is the
  delivered packet's own shape, and the reading for a spectrum is *sharp with a shoulder*
  rather than broad.

  **The second thing is the engineering gap the night exposed. A run measured in hours emitted
  nothing before it ended.** The TIMS front-end sequence failed to complete three times -
  4.75 CPU-hours, then 40 minutes, then 7.6 wall-hours against a 4.17 hour estimate before a
  Windows update rebooted the machine - and **nothing was observed on any attempt**, so whether
  the estimate was low or the run does not terminate was never asked. GRD-8 gates on cost
  *before* the work, so the platform will say a run is going to take four hours and then go
  silent for four hours. SPEC.md Amendment 44.

  `einzel run --progress <seconds>`, **thirty seconds by default**, writes
  `results/<name>.progress.json` and one line per interval on stderr (CLI-2 - progress is a
  diagnostic, so `--json` still gets a clean stdout). The default is the decision rather than
  the plumbing: **a flag somebody has to remember is a flag that is not set on the run that
  gets killed.** The file is **removed when the run writes its answer**, so finding one means
  the run did not finish - which needs no timestamp comparison to read - and it is written
  through a temporary file and moved into place, so a process killed mid-write leaves the
  previous checkpoint rather than a broken one. A failed write is announced once and
  swallowed: a full disk must not end an eight-hour run at hour seven.

  **A finished phase is written whether or not the interval has come round**, because that is
  the state a killed run should be found in - a study that splits a hold into phases has most
  of its answer in the phases that completed, and before this they went with the process.

  **Watching does not change the answer, and that is the claim that could have failed
  quietly.** The observer is handed the solver's own live density buffer rather than a copy,
  which is what makes reporting cheap enough to do at all, and *which* steps report is set by
  the wall clock - so if the answer depended on being watched it would not even be
  reproducible. Two runs of one seeded model, one silent and one reporting every step, are
  asserted equal to the last digit; the mutation that adds `1e-9` to one cell from inside the
  observer fails that test and nothing else. The hot-path cost is one call per step asking
  whether a report is wanted; the centroid and the width are full grid passes and are computed
  only when it is.

  **A projection from total elapsed time is a projection of the solve.** The first version
  divided wall clock by the fraction of the phase simulated, which charges the one-off solve to
  every remaining microsecond: **82 minutes against an actual 23** on the shipped analyzer, and
  17,576 minutes on the very first report, where one step had been taken. Measured between
  reports instead, it held at 23 minutes across three consecutive reports. The same correction
  `einzel estimate` made when it started excluding process start: **a rate measured over a
  window containing a fixed cost is not a rate.** And with that fixed, **the solve became the
  longest silent stretch** - minutes on a sixteen-plate funnel and a twenty-seven-ring analyzer
  - so it is announced before it starts rather than after.

  **The checkpoint was very nearly written with nothing reading it**, which would have been
  the third instance in three days of the defect this report command exists to expose. A run
  that did not finish leaves a `.progress.json` and no result, and the report would have said
  only that "its answer is nowhere" - true, and it discards the phases that did finish. It now
  reads the checkpoint, says how far the run got, and gives an interrupted run **its own state
  on the page**, because it calls for something different from every other: not "nothing to
  do", not "re-run it", not "run it again to store an answer", but give it longer, or a coarser
  mesh, or a machine nobody is going to reboot. The rule, in `docs/lessons.md`: both report
  defects were found by enumerating the *writers* into `results/` rather than its readers, so
  **after adding a producer, ask what reads it, and answer by enumerating consumers rather than
  by recalling one.**

- **The front-end sequence runs end to end, and what stopped it was a cache checking its own
  premise.** Four attempts had produced no output at all - 4.75 CPU-hours, then 40 minutes, then
  7.6 wall-hours ended by a reboot, then 928 s at the 619th step of an 8 ms ramp projecting to
  nine hours. The whole 18.3 ms of fill, trap and ramp now takes **905 seconds** at 256 x 32.

  **The cost was one line.** The ponderomotive well is the cycle mean square of the
  *oscillating* field, so a DC ramp cannot change it - and `PonderomotiveWellCache` decided
  every fourth step that it had, rebuilt a six-second well, and would have done so about
  **17,000 times** across the ramp. Twenty-eight hours of establishing that nothing had moved.

  **What moves is round-off, and the tolerance sat below it.** The well is a mean square taken
  after removing the mean, and the mean here is a DC field the ramp walks from 60 V to zero, so
  the noise floor of that subtraction is proportional to a quantity that changes by everything
  while the well changes by nothing: about **2.5e-13 of the deepest well per step**, which
  crosses 1e-12 in four steps and then does so forever. The bar is 1e-9 now - on a 30 V well
  that admits 3e-8 V against a thermal `kT/q` of 0.026 V - and the probe visits **one lattice
  node per call** rather than all sixteen, so the other half of a ramped step is spread across
  sixteen of them. `docs/device-templates.md` had recorded a deliberate decision *not* to loosen
  the bar while the variation was unexplained, which was right; what changed is that it is
  explained. Bit-identical: 16 rebuilds became 1 and sigma_z is the same to all six printed
  digits, pinned from both sides by a test requiring 1e-11 to be held and 1e-5 to be caught.

  **And the finding is that it elutes, with a resolving power - my two earlier accounts of
  this were both wrong.** Re-measured on one build, every configuration elutes. The delivered
  packet collects **99,666.7 of 99,954** ions, mean 15,480.750 us, sigma 156.344, peak
  elution potential **21.1843 V**, **R = 7.674** - against the analyser alone at 8, so the
  funnel and gate cost about four per cent and the delivery costs nothing measurable. Those
  are the figures after PR #37, which moved the template's density grid onto the detector
  plane it had been collecting three millimetres short of; before it they were 20.9878 V and
  7.481. **The parked packet's equilibrium width is 0.7118 mm on both sides of that move**,
  which is the control the closed form predicts, since it carries only the gas temperature
  and the axial gradient.

  **Which answers the template's standing question.** A delivered packet and one seeded at the
  balance point enter the ramp **1.88x** apart in width (sigma_z 1.3404 against 0.7118 mm) and
  elute at **identical profiles** - same peak bin, same half-maximum width, same R to every
  printed digit - their weighted means agreeing to eight significant figures. The arrival width is
  the **analyser's own**: the packet re-equilibrates to `sigma_z^2 = (kT/q)/|dE/dx|` before
  release, and that formula contains nothing about where the packet came from. Two ramps
  beginning 9.7 ms apart land on the same potential to five figures; a *stepped* release is
  **12 per cent wider** (sigma 178.0 / 179.9 against 159.16), so the release program reaches
  the width where the history does not.

  **The frozen result is unexplained and unreproducible, and the leading hypothesis was
  refuted by its own control.** An earlier run collected 7.74e-245 ions. Excluded: the ramp
  spelling, the delivery, the late start, a stale operator - and the **ponderomotive well
  cache**, which was the obvious suspect since that commit is the only one between the two
  engines touching a physics file. Reverting `PonderomotiveWellCache.cs` on the current build
  gives **10,794 well rebuilds against 1 and the same answer to 13 significant figures**. So
  the cache is excluded and its bit-identical claim is now measured on the front end rather
  than on the analyser alone. **And its provenance is gone by my own hand**: `results/` is
  keyed by stem, so each re-run overwrote the manifest carrying the model hash that would have
  said whether the document itself had changed.

  **A sequence whose phases all name a mode the model does not was flown in the model's, and the
  timeline ignored outright.** `ChangesTransportMode` asks whether two *adjacent* phases differ
  and needs at least two phases to ask it, so a single diffusive phase on a model declaring
  `trajectory` - or a sequence every phase of which says `diffusion` on such a model - is not a
  change by that reading, and the fork sent it down the trajectory path: the model asked for a
  density and got a single-ion flight, **exit 0**, with nothing saying the sequence had been
  skipped. The eighth sighting of one shape here and the second on this fork - a question
  answered by a proxy that stops being equivalent when the set of cases grows. The validator
  already asked the right question, and its own note says why; `CompiledModel.Modes` gathers
  every mode the run uses and `NeedsSequencedTransport` routes on it.

  **`--vtu` on a sequenced run wrote no density**, so a packet that crossed into the diffusive
  description could be summarised into a centroid and a width and looked at in no other form -
  the state the wholly diffusive path was in before RND-8's argument was answered for it, and
  the same "wired into N-1 of N paths" shape as this path lacking a result document and storing
  absolute artifact paths. The control is what makes it a statement: a sequence ending as
  trajectories has *no* density, which is a different fact from an empty one, so the same flag
  must write nothing there.

  **And my own export dropped the evidence at the seam, which is what the file exists to
  carry.** It gathered the run's warnings by hand from two of the three lists that hold them, so
  the field's caveats travelled nowhere - and the note saying a sequenced run has no flight time
  is constructed inside the result's own envelope a hundred lines *below* the export, so the
  volume came out with an **empty caveat block on a run that had earned one**. GRD-2's whole
  subject, on the artifact it is most about: a `.vti` is the thing most likely to be opened by
  somebody who never saw the envelope it came from. One list built above both readers now, with
  the severity on each line, and the mutation back to the hand-gathered version fails that test
  and nothing else.

- **The viewport is filled by a run that is still going, which is the capability a long run
  was missing.** `IDensityProgress` has carried the solver's live density buffers since the
  checkpoint writer was built, and the only consumer turned them into a centroid, a width and a
  line of prose. So a person watching a TIMS elution could read that the packet had reached
  21.1 mm and could not see it - on the one class of model where the whole difficulty is
  understanding what the instrument does to a packet over twenty minutes.

  **A redraw and a watch answer different questions.** A redraw truncates to the first phase
  *because* somebody is staring at the window; `ViewportCommand.Watch` walks the whole timeline
  and hands out a drawable bundle every couple of seconds. What a picture shows that a centroid
  and a width cannot is the shape: a packet still narrowing toward its equilibrium, one pressed
  against a wall and losing population to it, one that never left where it was seeded - all
  three report a centroid and a width, and only one is the instrument working.

  **A frame is a whole `ViewportOutcome`, not a density**, because UI-1 puts the contouring on
  the command layer's side of the line - so a frame of a run in flight and the same instant
  drawn afterwards are the same record built by the same function. **The geometry is extracted
  once and every frame shares it** (asserted by reference, not by comparison, because two equal
  lists would also pass while the work was being done again); it is sound because the sequencer
  already refuses a stage that moves an electrode, so the meshes are identical at every instant
  by construction. The shipped C-trap is 795,564 triangles, which is what that saves.

  **Three things the wiring got wrong, all found before it shipped, and each is a general
  shape.**
  - **The transport reports its own leg's clock, and it is right to.** A leg does not know it is
    one of eight; the checkpoint writer prints "phase 2/3, 12.0 of 40.0 us", which is what a
    reader wants when the phase is named on the same line. A *picture* has no such line, so a
    frame carrying it sends the instant back to zero at every phase boundary and stamps the
    packet with a time it passed through long before. **A quantity is right or wrong for what is
    around it**, and a second consumer of an existing seam may not supply the context the first
    one did. The offset is taken from what the phase **did** (`PhaseOutcome.EndsAtSeconds`)
    rather than what it declared, because a phase can end early - and the declared duration is
    the one in scope earlier, which is why it is the one that gets used.
  - **The end of a run is empty whenever the ions arrived.** Applying the final bundle
    unconditionally spends minutes drawing a packet and then replaces it with an empty box - the
    exact picture the density work exists to stop a diffusive model producing. Every frame was
    correct and the last one was correct too. The watch keeps the last frame that held a packet
    and says so; the finished viewport picks the middle of the usable instants for the same
    reason, and a watch has no list to pick from.
  - **`RefreshAsync` returns `HasBundle`, which is whether there are trajectories.** A watch only
    ever runs on a model that has none by construction, so it answered false on every successful
    watch there is - the proxy shape again, copied along with a signature from the neighbouring
    method.

  **Frames are coalesced rather than queued, and applied on the thread that started the watch.**
  A viewport wants the newest packet, not every packet: a queue shows one from four frames ago
  with three behind it, falling further behind the longer it is watched. The queued flag is
  cleared *before* the frame is read, so the race can post twice and can never drop the last
  one. A trajectory model is refused with what to do instead - a flight finishes faster than a
  window could draw it part way through. Amendment 25 holds: the action is journalled as
  `einzel run <model> --progress 5`, the same transport writing a checkpoint instead of a
  picture.

- **Two templates were drawn as approximations, and the approximations had reached the findings.** `c-trap` modeled each bent rod as a chain of thirteen overlapping spheres and `paul-trap` modeled a quadrupole trap's hyperboloids as three flat annuli. Both were deliberate — a `cylinder` here is axis-aligned and a bent rod is not; the cross-section vocabulary had no curve — and both were documented as approximations. They survived because **an approximation adopted because the format could not express the real thing does not announce itself when the format gains the ability.** SPEC.md Amendment 50.

  **One primitive was added below the library**: `revolve`, a closed outline turned about an axis through an arc. `prism` : line :: `revolve` : arc, and its vertices are (distance from the axis, position along it) — the half-plane an axisymmetric solve already works in, so the linear ion trap's hyperbolic rod profile transfers to a bent quadrupole unchanged. Schema **0.14**. Inside the sweep the signed distance reduces *exactly* to the profile's own; every revolved profile edge is a quadric, so a link's crossings are quadratic roots and nothing is bisected. Checked against the closed-form torus at **14.446 / 3.614 / 0.904 µm** for 32 / 64 / 128 profile vertices — the inscribed-polygon bound to the digit, falling exactly 4.00× — and against a `prism` at a kilometer of radius to under 100 nm. The Paul trap needed nothing new: `polygon` had landed for the linear ion trap.

  | | approximation | real geometry |
  | --- | --- | --- |
  | Paul trap, effective r0 against 4.0000 mm declared | 3.8195 | **3.9983** |
  | Paul trap, boundary against a tabulated q = 0.90804 | 9% low | **0.07%** |
  | Paul trap, curvature ratio against −2 at 0.4 mm | −1.9867 | **−2.0006** |
  | Paul trap, secular line against Mathieu, worst | 1.2e-3 | **7.8e-5** |
  | C-trap, electrode declarations / basis channels | 49 / 1 | **5 / 1** |
  | C-trap, ejected waist | 1.73 and 1.92 bend radii | **1.011 and 1.011** |
  | C-trap, convergence at a 20 mm bend | 20.8× | **45.9×** |
  | C-trap, arrival spread into the analyzer | 60.02 ns | **38.90 ns** |

  **The Paul trap's 9.4% was not an unknown.** It was measured three independent ways, explained correctly, and carried as a stated correction on every number the template published — which is the right thing to do with a known approximation and is exactly what let it sit there. **A correction that is carried faithfully stops being a problem to fix.**

  **Two published findings were artifacts, and both had plausible mechanisms attached.** The C-trap's waist at 1.73 and 1.92 bend radii was explained as an **aperture lens at the slot**; the thin-lens fit it implied mispredicted the longer bend by 17%, and that failure was recorded at the time rather than buried. On swept rods the waist is at the center of curvature, where velocities aimed along radii meet, and **a fourfold change of slot opening moves it 1.048×** — so the slot is a hole. And the Paul trap's **octupole resonance band** at 605–614 V, identified from the measured secular frequencies as an order-four condition met to 0.055% with order four predicted in advance from the trap's symmetry, is **gone**: forty amplitudes below the edge lose nothing, and the condition now misses 2 by 0.22 where it was met to 0.0011. That identification was right, and an octupole is what a flat annulus buys — so removing the cause is the identification's own prediction.

  **And the C-trap's drive-phase conclusion is withdrawn with the geometry that produced it.** On beaded rods the drive moved the focal distance 3.14× while a whole cycle of phase moved it 1.10×, and the reading was that the pseudopotential steers the packet. On swept rods the two are the same size — 1.26× against 1.25× — so the instantaneous field does as much as its average and **no single number describes a driven ejection**. What leaving the drive on costs is the focus itself: 45.9× quiet against 2.6–4.1× at four phases.

  **One defect in the primitive is a class.** Deciding whether a profile edge is an annular disc or a cone by asking `dh == 0.0` is an exact test on a computed quantity. An outline is written as expressions, so a face meant to be flat is flat only to rounding — the C-trap's inner rod has an edge sloping by 8.7e-19 m over three millimeters of radius, which against zero is a cone of slope 2e15 whose quadratic root is lost. The same rod's other face **was** exactly flat, so one side of a mirror-symmetric electrode got its cut cells and the other did not, **the conductor mask was symmetric either way**, and the field came out **11% asymmetric** across a plane the geometry is symmetric about. Fixed with a relative tolerance; cut-fraction asymmetry 1.0 → 1.9e-13. **A symmetric Dirichlet mask is not a symmetric discretization**, so nothing that checks a geometry by asking which nodes are inside can see this.

  **Three tests were carrying the old geometry in their source, each failing differently**: a hardcoded constant (`EffectiveMm = 3.8195`, over-predicting every secular line by 10%), a quoted sentence printed as output beside an assertion on a range around it, and a **vacuous truth** — `TheBeadsOverlapAlongEachRod` grouped electrodes by name stem and compared consecutive members; with the rods swept each group has one member, the loop never runs, and the worst spacing stays at the zero it was initialized to. It passed. And a **control** that stopped discriminating rather than failing: β's amplitude-independence was checked against a deliberate imperfection, so when the imperfection went the control asserted nothing, and had to move from a 0.20 mm launch out to 1.6 mm where the truncation of the hyperboloids bites. **A control fails when the thing it measures is fixed.** `docs/lessons.md`.

  **What is open in the C-trap is provenance, not capability.** Makarov 2006 gives the face shape ("rods with hyperbolic surfaces"), the gas, the RF range, the quench and the extraction voltages, and puts the slot in the **pull-out** electrode; it gives no cross-section dimension and no radius of curvature, and neither does US 6,872,938 B2, whose preferred curved trap is a different device again — a stack of curved plates. `inscribedRadius`, `rodHalfWidth`, `rodDepth`, `slotHalfWidth` and `bendRadius` are all guesses, marked as such parameter by parameter. `docs/literature-targets.md` §7.

- **Two conductors in one place at two potentials, refused in a volume — and it refused a shipped template on its first run.** `ElectrodeOverlap` has caught that in a cross-section since a hexapole was built with a quadrupole's rod ratio, put its rods through one another, and **solved contentedly in eight cycles**. It takes a `CompiledElectrode` and is called from the `solve2d` path alone, so `box`, `sphere`, `cylinder`, `prism` and `revolve` were all unchecked from the day the volume solver landed. `docs/extending.md` recorded the gap, named both candidate designs, and said "neither is built" — **a known gap, written down, is still a gap**, and it survived *because* it was recorded honestly: the note reads as a considered limitation rather than a defect waiting.

  **`ElectrodeOverlap3D` switches on nothing.** Each primitive is asked only for its bounding box and its signed distance, both of which the format already requires for the solver and the ion absorber, so a sixth primitive is checked the day it compiles. The fifteen exact pair tests five shapes would otherwise need include a tilted box against a revolved hyperbola, which has no closed form worth writing.

  **It searches for a witness rather than proving disjointness, and reframing it that way is the whole finding.** The obvious check is `min max(dA, dB) < 0` by branch and bound — both distances are 1-Lipschitz, so the bound is easy and the first prototype was twenty lines. It is the wrong question: two conductors **sharing a face**, which is how every segmented electrode chain in this library is written, have `max(dA, dB)` equal to zero over a whole *surface*, so proving the minimum non-negative means subdividing that surface to the tolerance. The prototype burned a 200,000-box budget on each of sixty such pairs in the Astral and came back **inconclusive on the commonest legitimate configuration there is**. But the plane check's own doctrine is that a guard must *miss rather than falsely refuse* — so the absence never has to be proved. **A point strictly inside both conductors is a proof; nothing proves the absence.** Budget-exhausted then means "no violation found", tangency passes by construction, and the Astral's real overlap is found by the very first probe, the buried stripe's own center. **Before building a decision procedure, ask which of the two answers you are allowed to be wrong about.**

  | | electrodes | disagreeing pairs | validated in |
  | --- | --- | --- | --- |
  | `c-trap` | 5 | 6 | 14 ms |
  | `segmented-quadrupole` | 12 | 52 | 7 ms |
  | `linear-ion-trap-3d` | 32 | 400 | 43 ms |
  | `astral-3d` | 86 | 3,328 | 4 ms |

  **A bounding-box screen alone would refuse the C-trap**, whose five rods are nested arcs about one axis: `rodInnerUpper`'s box lies wholly *inside* `rodOuter`'s while the metal is nowhere near it — and the corner where `rodOuter` and `rodTop` bound each other, which I first read off the vertex expressions as a deliberate tangency, is on neither surface: both signed distances there are **+1.033 mm**. The deepest the search ever gets on that pair is 1.033 mm *outside* both, so the surfaces are at least **2.07 mm** apart. The screen still earns its place in front of the search, settling 3,204 of the Astral's 3,328 pairs at no cost, because it settles them the safe way. **The tangency tolerance is load-bearing** at 1e-9 of the pair's extent, for the reason the flat-face defect taught: faces meant to coincide are written as two different expressions and agree to a few ulps, not exactly.

  **What it caught:** every drift stripe in `astral-3d` was extruded outward by its own declared 2 mm thickness, through the inner face of the grounded board it is printed on and **1.285 mm into the metal behind it**. The stripe now runs from `foilGap` out to `halfGap`, the board's own face, so the two are tangent, and `foilThickness` is derived rather than a second knob.

  **No number moved, and that is measured rather than argued.** The two declarations give **identical Dirichlet masks — every node, every cut link — at cell sizes of 4, 2, 1, 0.5 and 0.25 mm**. Two mechanisms combine, neither about the overlap: a cut link records the **nearest** surface along its arm, so the stripe's front face wins over everything behind it, and a node inside both goes to the board because the boards are written last. **So the interpenetration was latent rather than active** — the natural worry is that refining eventually resolves the buried metal and the geometry changes under a convergence study, and it does not. Only the ladder says so: at the shipped 4 mm cell the 0.715 mm stripe holds **no node at all**, sitting between nodes at 19.20 and 23.04 mm, so a single-mesh comparison shows only that a sub-cell feature is sub-cell. Teeth checked by mutation — an 8 mm extrusion, far enough to leave the back of the board, fails all five rungs.

  **What it does not do**, stated rather than discovered: it cannot prove two conductors disjoint, so a sliver thinner than the search reaches is missed — and how thin is measured two ways. Two boxes sharing a face overlap over their whole area, so the search only has to find a **slab**: 1 um of shared metal on a 10 mm box. Two *curved* surfaces just interpenetrating share a **lens** and are harder — built as two tori, where the circles' overlap is a closed form, the deepest lens found on a 3 mm tube is **5.8 um**. **The limit is a budget rather than a wall**: 30,000 probes reach 4.8 um and 300,000 buy nothing more, so a halving costs over tenfold, as subdividing in three dimensions should. Identical at 64 and 256 profile vertices, which is what says it measures the search rather than the polygon. `docs/extending.md`, `docs/device-templates.md`, `docs/lessons.md`, SPEC.md Amendment 51.

  **And it asks over every state the instrument has, which the proxy shape forced.** A stage changes what an electrode *holds* and may not change where it is, which `SameGeometry3D` already enforces — so *"do these two agree"* has as many answers as the instrument has states, and asking it of the declared state alone is the proxy that stops being equivalent the moment a document declares a sequence. Two conductors sharing metal at one potential while held and at two while pushing would give a field of a geometry nobody described **for exactly the duration of the push**, on a document that validated cleanly. The geometry is fixed across states, so the witness search is still run once per pair, the refusal names the stage, and a ramping phase contributes both its ends since the disagreement may live only at the end it is walking toward. No shipped volume template declares stages, so this is a guard written before the case exists rather than a fix — which is the only time it is cheap.

- **An error that cannot say where it is, and the 64 copies that made the case.** `EinzelError` carries a JSON Pointer because AGT-3 makes an error a recovery instruction rather than a notification. A dimension mismatch is raised inside `Quantity`, which has two operands and no document, so it names the root as a placeholder — and the frames that catch it, which were resolving a *named parameter* or a *numbered electrode's face*, added it **verbatim**. The path was known at the catch site and discarded.

  **Recorded as "a small AGT-3 gap, not yet fixed" for as long as it was only seen once.** What changed is the consequence being measured: a foil thickness written without a unit on the Astral gives **64 identical `UNITS_INCOMPATIBLE` errors, every one located at `/`** — naming neither which electrode nor which face, and indistinguishable from what a single mistake would print. Located, each one reads `/fields/2/solve3d/electrodes/0/repeat/7/maxY`, down to the repeat index.

  **The fix is `EinzelError.At(path)` at four call sites**, and it only fills in the placeholder — locating an error must never make it *less* located, since an expression evaluator is handed the path it is working on and raises its own failures with it. One of the four had already been written the right way and **the branch ten lines below it had not**: `ParameterSurface` located a *literal* parameter's failure and dropped a *derived* one's, which is exactly the gap the changelog had recorded without connecting it to the general shape.

  Found by driving my own new refusal through the CLI rather than by a failing test — the project's own rule that after adding a producer you ask what reads it. The first attempt at a deliberately broken model used a bare `2.0` where the grammar has no unit literals, so it produced the wrong error, and the wrong error was the finding. **Teeth measured both ways**: making `At` the identity fails three tests, and removing its placeholder guard fails a different one.

- **The slot's exit optics, and the electrode that was not in the solve.** SPEC item 7's one
  open sub-item was the ejection slot: a real LTQ's detector sits behind a strong extraction
  field and the cross-section ends 1.5 mm behind the rod in a grounded wall, so the efficiency
  was recorded as *a sensitivity, not a prediction*. **Closing that gap does not make it a
  prediction.** A plate behind the rods, 300 ions a point, takes the shipped relieved slot from
  **38.0 to 49.7 per cent** between earth and a kilovolt and the straight channel from **6.6 to
  9.5**, against a **31-point gap between the two profiles at earth**: the profile dominates
  the field at every voltage tried.

  **A slot is a waveguide below cutoff for an electrostatic field**, and the plate's own basis
  measures it: at the slot mouth, which is where the aperture lens acts, **a kilovolt is worth
  six tenths of a millivolt** - one part in a million, for both profiles. The decay length is
  `h/pi`, the lowest mode of a channel of full height h, checked on the 2 mm relief cavity at
  **0.6392 mm against 0.6366, a ratio of 1.0040** over 32 samples. The 0.25 mm channel falls by
  a dead-constant 0.2704 per cell over eight cells - 0.0941 mm against 0.0796, **18 per cent
  high on two cells, in the direction under-resolution predicts** - and either number is fatal:
  eighty e-foldings over 7.25 mm of rod.

  So what an extraction field reaches is the **relief cavity and the gap**, never the mouth,
  which the loss columns say independently: the relief's walls take **51 ions at earth and 32
  at a kilovolt** while the slot's own walls do not move (23 against 23). **A relief is not
  only mechanical clearance - it is what lets an extraction field inside the rod at all**, and
  its reach is set by the relief's height rather than by its length or the voltage. The control
  is a positive plate, which must make things worse and does: +200 V costs the relieved slot
  nearly half its transmission and puts **12 ions on the rod's back face against 1 at earth**.

  **An engine defect found by needing the plate, and it is the recurring shape.** An
  `edgeProfile` electrode keeps its volts in its `profile` and every shipped one leaves the
  scalar `potential` null. The channel decomposition builds a supply from that scalar and the
  taps, so an edge profile joined none, was weighted at zero, and the rasterizer multiplied it
  through: **in any driven solve the board was not there at all**, silently, on a converged
  solve reporting its other channels. Caught against a closed form - a 1 eV ion crossed 4 mm in
  **6.419 us against 6.44 us of field-free flight**, so the -500 V plate was exactly absent.
  It never showed because **both templates that use edge profiles declare no drive**, and the
  undriven arm applies the profile as written. The same null scalar had also made `CanDoWork`
  refuse such a model as one in which nothing could move an ion - the **sixth** configuration
  that check has learned, and its own remarks already carried the rule that predicts it: a new
  way to hold a potential is a new configuration. Fixed with a unit coefficient, and the
  narrower wrong case the fix creates - a stage that *changes* a profile, which would be two
  patterns wearing one coefficient - is now refused by name rather than left. Both fixes
  mutation-checked; the two controls pass either way, which is what makes them controls.

  **And the table this replaces was twenty ions**, of which about thirteen go toward the slot,
  so its rows differ by less than their counting error. The ordering survives at 300 and the
  magnitudes move: the shipped profile reads 9 of 20 there and 38.0 per cent here. Details in
  `docs/device-templates.md` and `docs/lessons.md`.

- **The linear ion trap modelled a prototype, and the paper says so on its own page.** The
  template cut one ejection slit, in the +x rod, because that is the device Schwartz 2002
  describes. **The released LTQ ejects radially both ways** - a slit in each x rod with a
  detector behind each - and the paper predicts exactly that while measuring the prototype:
  *"a scan out efficiency of 44% ... It is assumed that half of the ions are neutralized on
  the rod opposite the ejection slot. The use of a second slot on the opposite rod in
  combination with a second detector should produce a scan out efficiency of 88%."* The
  Stellar paper confirms it from the other side, describing the LTQ's two-fold stretch as
  answering "the field distortions of the ejection **slots**", plural. Corrected in
  `linear-ion-trap`, `linear-ion-trap-3d` - the one that carries the axial DC trapping - and
  the Stellar; `slotXMinus` at zero gives the prototype back.

  **The Velos family cuts four and only two pass ions**, which is a distinction I nearly got
  wrong in the other direction. Second's dual-pressure paper is explicit - *"a fully symmetric
  geometry with ejection slots in all four rods. This design provides fully symmetrical RF
  fields"* - so the Stellar keeps all four, and ejection stays the dipole across x. **The model
  reproduces the paper's stated reason independently**: cutting the x pair only leaves the
  stretch four-fold and the slots two-fold, which breaks the antisymmetry between the rod pairs
  and admits an octupole of **1.25e-4 against 1.4e-16 with all four**.

  **The odd-order fault was the prototype's.** At r0/2: one slit gives a dipole of **1.26e-3**
  and a hexapole of **1.84e-4** of the quadrupole; two give **2.8e-15 and 8.6e-16**, because a
  slit in each x rod is symmetric about x. The octupole is the stretch's and survives at
  1.62e-3. **The quadrupole term moves 0.8219 to 0.8214**, so the ejection working point and
  every corpus example pinned to it are unaffected. Three tests carried the old geometry in
  their assertions and now encode the new claim; all seven in the class pass.

  **Scan-out roughly doubles, as the paper says it must, and still lands at half its figure**:
  **21.7 per cent one slit, 45.3 two**, against 88. The remaining factor of two is the slot
  profile - the relief ratio is the knob (8.6 / 17.3 / 22.7 / 35.1 / 52.2 per cent at 1 / 2 /
  4 / 8 / 16 times), the channel depth barely matters below 1 mm, and **the rod's thickness is
  refuted**: 45.3 / 43.0 / 36.7 / 47.7 per cent through 3.5 / 4.5 / 6.0 / 8.0 mm of metal, non-
  monotone, thickest highest.

  **And the 124-to-12 split between the two detectors is the launch, not the trap.** The solved
  field is symmetric about x to **1.5e-6 of applied inside the trapping region** - the 15 per
  cent the whole domain showed was the housing, which had a wall on one side and the bare
  domain edge on the other, now a named detector on both. What is asymmetric is the ensemble:
  every ion starts at one point at one instant, so their secular oscillations are in phase and
  they eject together. Spreading the cloud walks the split 17.6/82.4 to **46.1/53.9** at 1 mm.
  The honest reading is that **a physically sized cloud is still coherent** - 300 K in this well
  is 0.1 mm, still 19/81 - and what decoheres a real trap is milliseconds of cooling before the
  ramp. So the sum over both detectors is reportable and the split between them is not.

  **A format gap, named rather than built**: the model format carries **one** detector plane and
  both instruments have two, so the -x slit gets a named absorber and a scan-out is the arrivals
  plus the ions itemised against `detectorLeft`. A LIB-1 signal with a second real device behind
  it. Details in `docs/device-templates.md` and `docs/literature-targets.md` §2.

- **`compact-astral-3d` — the compact analyzer as an instrument, and the coin toss it found in
  the published one.** The compact analyzer this project is designing had only
  `planar-mirror-pair` to stand for it: one plane of a mirror pair, no drift, no tilt, no
  reversal. It is now `astral-3d` whole at a chosen scale, one fifth as shipped, **generated**
  by `generators/compact-astral-3d.py` so every published length keeps its value and
  provenance as `<name>FullSize` beside one `scale` that multiplies it. The meshes are lengths
  too, so the compact solve is the full-size discrete problem node for node and costs the same;
  a cell held fixed would have been a five times coarser model that looked like a worse
  instrument. `planar-mirror-pair` and `astral-mirror` are relabeled as the cross-section
  studies they are.

  **Similarity is exact, so it was tested exactly - and it failed.** Scales of 0.5, 0.25 and
  0.2000001 reproduced the full-size flight to the bit; 0.2 moved it **0.27 percent** and
  0.1999999 a different 0.03. The foil's sixteen slices hold sixteen potentials and share a
  face every 28.125 mm, and three of those faces sat exactly on node planes of the 4 mm foil
  mesh, so which slice the 180 stencil arms lying in those planes were cut against was the
  last bit of arithmetic that 0.2 rounds differently. (The masks also differ in 454 nodes and
  1,406 more links, all on the domain's upper face where grounded boards end - a harmless flip
  measured separately; see the next entry.) **No refinement ladder finds this**: every rung
  can sit on the same faces. `meshShiftZ` moves the foil mesh 0.3 mm, a tenth of a cell off every shared face,
  and similarity then holds to **1.6e-8**, the integrator's tolerance.

  **Removing the coin toss exposed an ordinary error four times larger.** The stripe is thinner
  than a cell and holds no node, so it exists only where its surfaces cut links, and moving the
  mesh in fifths of a cell moves the flight time over **0.83 percent at 4 mm and 0.22 at 2 mm**
  - second order, and the means fall 6.4 µs with it, extrapolating to **about 775 µs**, a percent
  below the arithmetic's 783.2. The "0.4 percent" agreement recorded for months was one draw from
  that spread. Still inside the register test's 2 percent and clear of K = 25, but an agreement
  quoted finer than where the mesh sits is not an agreement (SPEC.md Amendment 55).

  `CompactAstralTests` checks it at the mesh, where it is exact and affordable rather than at the
  flight, which takes a minute: every resolved quantity scales as its dimension says (length one,
  potential none, field minus one - current carries minus one, because charge is fixed), a scale
  of one is `astral-3d` to the bit, the three meshes match node for node, and no shared face sits
  within a twentieth of a cell of a node. **What does not scale is the design question**:
  turn-around time and detector response are absolute, so their resolving-power limit falls by
  five; a machining error is five times larger against the geometry; and the packet's own field
  grows as 1/s² against the applied field's 1/s, so the same space charge comes at a fifth of the
  population. Two engine follow-ups were proposed: a warning when a solve's mesh samples a face two
  disagreeing conductors share (**built** - next entry), and a bounding-box cull in the 3-D mask
  builder, which tested every link against all 86 electrodes and spent about a minute per mask in a
  Debug test run (**built** in PR #49: 10.3 s to 0.15 s per mask in Release, bit-identical to the
  unculled build). Details in `docs/device-templates.md`.

- **`mesh.node-on-shared-face` - and the check as specified reported its own motivating case
  clean.** Built first exactly as proposed above - nodes within a rounding of two disagreeing
  conductors' surfaces - it swept every template and example clean, and then, run against
  `astral-3d` with its mesh shift removed, **reported that clean too**. The foil stripe is 0.715 mm
  on a 2.9-3.8 mm mesh and holds no node: the coin was tossed on the **stencil arms** lying in the
  plane of each shared face. So the rule is stated in terms of both places a mesh samples a
  geometry - no node, and no point where an arm from a free node first meets metal, on a face shared
  by conductors that disagree in any state - and the Astral with its shift removed reports **180
  arms, 0 nodes**. SPEC.md Amendment 56; `docs/numerics.md`, "A face two conductors share".

  `GeometryBuilder`/`GeometryBuilder3D.NodesOnSharedFaces` run before every solve and put it on the
  `SolveReport` (a new `Warnings` list), so `FieldAssembly.BuildReported` carries it to `run`,
  `preview` and every figure; **`einzel solve` carried no warnings at all** and now reports it once
  per element. Tolerance 1e-9 of the finest spacing; "disagree" is `ElectrodeOverlap.StatesOf` /
  `Agrees`, made public so the two questions asked of a pair cannot drift apart. **The arm test asks
  a distance, not a pair of entry fractions**: nudge the face one ulp and the arm grazes one stripe
  instead of entering it while its cut has already flipped - an entry fraction is discontinuous in
  exactly the case the test is for.

  **Survey: 44 solved elements across every template and example, all clean**, kept so by
  `SharedFaceCorpusTests` with the shift-removed Astral as the control. **What it was worth**, by
  moving the foil mesh across the node: 786.4372 us at -1e-7 of the extent, 784.3191 at +1e-7 - a
  **2.118 us step (0.27%)** on a slope of 5.4 us per cell, five orders above each run's own stated
  uncertainty. And it corrected the record: of the 454 nodes and 1,586 links differing between the
  full and compact masks, **only 180 links are the foil**; the rest are grounded boards' ends on the
  domain's upper face, where the last node plane rounds either side of the declared maximum -
  checked to be a flip the flight does not see.

  **Two limits, stated.** The tolerance flags coincidence, not proximity: a face 2.6e-5 of a cell
  off a node is on a determinate side of the same 0.27% step with no warning, which a geometric
  sweep would cross silently - the fix the warning names is a tenth of a cell. And a conductor flush
  with a **Dirichlet** domain face is the same coin toss against the grounded boundary, unchecked
  (**now checked** - see "The grounded boundary is a conductor" below).
  Mutation-checked five ways: detector disabled (11 of 14 unit tests fail; the three that pass are
  the should-not-warn controls), tolerance zero (the four near-miss tests fail), **arms disabled (the
  three arm tests and the Astral control fail - the nodes-only check, measured)**, base state only
  (the stage test fails), and the warning dropped at `FieldAssembly.Note` or in `SolveCommand` (the
  end-to-end tests fail).

- **`einzel render still`: the viewport's picture as a PNG, and what drawing it found.** An agent could run every analysis the window runs and could not see what the window shows. A still earns that only if it IS the window's picture, so every decision that makes the picture - the camera, the color ramps, how an electrode is colored, the layers and their order, translucency and depth-writing, the lighting - moved out of the shell's GL control into `ViewportPicture` and `Framing` in `Einzel.Commands`. The window uploads that composition and builds its shader from the same lighting constants; the still hands it to a managed rasterizer in `Einzel.Render` that decides nothing. UI-1's boundary test forbids the shell from even *using* `Einzel.Render` types, which is what put the decisions in the command layer. PNG hand-written over the base library's zlib; provenance and every warning in its text chunks; a validity violation hatches the bottom of the picture. **Orthographic, not RND-4's "perspective"** - SPEC.md Amendment 54.

  **Four defects in the viewport, all found by looking.** A printed board has no interior, so the planar mirror pair - the memo's own analyzer - was drawn as its end cap alone; boards are now sheets on the domain edge, colored by the profile, broken at every knot. A `reflectAboutX` solve was drawn as its declared half, so a mirror pair had one mirror; both halves now, the image wound to match. A zero-width cap had every normal zero - its central difference on the sheet is exactly zero - which neither window shades as the "flat-shaded facet" `Surfaces.Orient` promised; it gets its facets' normal now. And the camera framed a bounding sphere, which put a 767 mm by 30 mm analyzer across the middle third of the picture; it fits the box each named view sees, field lines included, as the WPF viewport always did. **Fitting per view then broke turning between views**, found in review: the window unioned the new view's box with the previous view's, which are boxes in different view coordinates, so the frame grew on every click of a named view - a cube filled half the side view after one switch from iso. That bookkeeping is now `ViewportCamera` in `Einzel.Commands`, testable without a GL context: a watch's growth is kept as a model-space box and re-fitted at each view, so turning away and back returns the opening frame. All mutation-checked: each fix reverted fails its own test and nothing else.

  **Two more from the review, both about asking for a still the verb says it accepts.** The largest size, 8192 px a side, needed 8.6 GB of supersampled canvas at once and ran out of memory as an internal error; the rasterizer now draws in horizontal bands through one reused 134 MB buffer, byte-identical to a single pass because sample positions stay in the whole picture's coordinates, and that still takes 11 s at a 381 MB peak. And `--width-px 1600px` went through `int.Parse` and exited 6, "a defect in einzel"; it is now a validation failure naming the flag and what was typed.

  **And a shipped literature template had stopped running.** When diffusive collection was restricted to an aligned grid face, `pnnl-ion-funnel`'s detector at 101.5 mm sat inside a grid ending at a literal 102 mm, so every run was refused - while `docs/literature-targets.md` went on quoting its threshold. None of its three tests runs it. The grid now ends at `detectorX` by expression; re-measured at the five published amplitudes the threshold moved by at most 0.013 (0.165 / 0.583 / 0.911 / 0.973 / 0.990), with `mobility.outside-fit` and `rf.quiver-exceeds-mesh` riding on every amplitude and now recorded beside the result.

- **A mesh too large to solve is a refusal about the model, and validation makes it.** The Astral
  at a 1 mm cell is 1025 x 65 x 1025 = 68.3 M nodes against a 64 M limit: `validate` said OK,
  `estimate` priced it at 35 minutes, and `run` spent more than five minutes building a conductor
  mask before the field constructor threw an argument exception, printed as `INTERNAL_ERROR` -
  "a defect in einzel, not in your model" - on exit 6. Now `GRID_TOO_LARGE` at
  `/fields/2/solve3d/cellSize`, exit 1, in about two seconds, from every verb that reads the
  model. **One limit and one rounding rule** (`src/Einzel.Core/Numerics/VolumeMesh.cs`), read by the
  validator, by `Grid3D.OverBox`, and by a backstop that now sits on the **mask**, the first
  per-node allocation, rather than on the field, which came after several gigabytes. The
  suggestion names the finest cell size that fits and is **checked after the round trip through
  its own text and unit**: three fixed figures printed 1.47 mm for 1.46484, crossing a second
  axis's boundary and naming half the mesh, and an exact decimal can sit a rounding below a
  boundary (2.75 mm over 22 mm is 8.000000000000002 intervals). The particle-in-cell grid meets
  the same limit at `/transport/spaceChargeGrid/nodes`. **The 2-D path has the same hole and no
  limit at all** - an absurd cell size validates and then exits 6 on an `int` overflow - and is
  left for its own change. And the test written for this broke another class's: `--threshold`
  moved a process-wide static and never put it back, so a later estimate in the same process
  inherited a gate of 1e12 s; it is now a parameter of the estimate.

  **A review of the fix found the fix's own crash.** A derived cell size or bound can be NaN
  (a division by a parameter set to zero, the square root of a negative ratio), and every
  comparison with NaN is false, so `cell <= 0` let it through and the new mesh count threw from
  inside `validate` - `INTERNAL_ERROR` from the command whose job is to say what is wrong.
  Infinity passed everything and solved on two intervals an axis. Both are now refused at the
  field. Also from that review: the space-charge advice named grids the new limit refuses (now
  capped at 256), twelve call sites validated and threw the first error alone (now all of
  them, so a refused mesh cannot hide behind an earlier mistake), `Grid3D.NodeCount` wrapped
  negative past 2^63 and slipped the guard (now saturates), and the estimate's mesh note used
  its own copy of the rounding rule. `docs/lessons.md`, `docs/cli.md`.

- **Mask assembly, timed: a fifth of a build on curved geometry, and the garbage was only some
  of that.** Measuring a volume solve's memory recorded a mask allocating **61 kB a node** on
  the C-trap's revolved rods, 4.2 kB on the linear ion trap's prisms and 289 bytes on the
  Astral, against the 105 it keeps, and builds of 95 s and 200 s that made the mask look
  dominant. **All of that was the branch it was measured on**: it predates the bounding-box
  cull, and the 200 s was under a live-set sampler forcing full collections every few
  milliseconds. On `main` the masks allocate 5,470, 260 and 105 bytes a node, and the builds
  take 16 s and 13 to 15 s.

  | | mask | build | mask's share | B/node | gen0 |
  | --- | --- | --- | --- | --- | --- |
  | `c-trap` 0.5 mm, before | 2.7-3.5 s | 16.1-16.3 s | 19-22% | 5,470 | 694 |
  | after | **1.6-2.3 s** | **14.1-14.3 s** | **12-16%** | **105.1** | 1-4 |
  | `linear-ion-trap-3d` 0.5 mm, before | 1.0-1.5 s, two channels | 13.2-14.6 s | 14-22% | 259.5 | 41-43 |
  | after | 1.0-1.5 s | 12.6-15.2 s | 16-18% | **105.2** | 1-3 |
  | `astral-3d` 4 mm | 0.15-0.30 s | 7.3-8.1 s | 2-4% | 105.3 | 1-2 |
  | `segmented-quadrupole` 0.5 mm | 0.07-0.23 s | 4.6-5.0 s | 2-5% | 105.2 | 1-2 |

  The garbage was two entry tests: the prism and revolve `FirstEntry` each built a
  `List<double>` per call, and the revolve took each profile edge's roots from an iterator and
  its cap angles from a new array. They now gather crossings into 1 KiB on the stack, or an
  array borrowed from the shared pool for an outline too long for it, and take roots through
  out parameters - **bit-identical masks** on all four templates, every fixed flag, value, cut
  fraction and cut potential at every level, each electrode at its own potential so a swapped
  winner would show. **It bought time on the revolve and none on the prism**: thirty objects
  and many interface calls a call against one list over a 29-edge polygon, so the prism's
  polygon was always its cost and its gen0 count fell from 42 to 2 without the clock noticing.
  What remains is **84-97% cut links**, not rasterization, so the next lever is how many exact
  entry tests that pass asks for. `MaskAllocationTests` holds every primitive's entry and
  distance tests to zero bytes and a mask to what it keeps; restoring the old entry tests fails
  both (12,588 bytes a node, 192 MB a sweep), and so does making only the borrowed-array path
  allocate. Merging `main` into the memory branch to measure this also carried a conflict
  worth knowing: the shared-face check read each channel's mask, which the memory work had
  stopped keeping, so it is now asked of the first channel inside its own frame.
  `docs/numerics.md`, "What building the mask costs".

- **The memory tests passed on a workstation and failed on CI, and the sampler was the reason
  — which hid a plane statement 7 percent high.** `SolveMemoryTests` sampled the live set by
  forcing a full collection every 2 ms, which on Windows is a timer tick of about 15, and
  differenced the peaks of two builds, so a missed peak read either way. On the four-core CI
  runners in Release, where a build is eight times faster than in Debug, three tests failed:
  0.776 and 1.143 of the statement, and a second channel costing 90.5 bytes a node against a
  limit of 30. Sampling as densely as the collections allow - waiting only as long as the last
  collection took - and measuring each build three times made every reading stable, and then
  the plane failed *consistently*: **153.3 stated, 132.7 measured**. The plane's peak is the
  instant the first coarse level copies its cut links, and the statement had added every deeper
  level and that level's residual, which do not exist yet at that instant. Derived from what is
  alive then, **143.25**; measured on four shipped plane templates, 143.3 to 143.9. The
  workstation had passed the wrong number because the slope's error happened to cancel it.
  Keeping the *largest* of the three readings then failed once on the Linux runner, 7 bytes a
  node over in the Galerkin build - about one grid-sized array, seen once more in WSL and never
  on demand, consistent with catching an instant when a new array exists beside the one it
  replaces. The test keeps the middle reading: that discards a lone outlier either way, and an
  array a build really keeps is in all three.

  **Three more from the review of the same pull request, all real.** The driven plane build kept
  its first channel's mask for the shared-face check after the loop - 73 bytes a node through
  every later solve; each channel now solves in a frame of its own, and a test that restores the
  old loop reads exactly 73.0. The first coarse level was charged as an eighth of the fine grid,
  and a grid keeps one node more than half on each axis: the Astral's real first level makes it
  241.8 against 241.3 measured where an eighth said 240.0, and 32769 by 5 by 5 is 251.9. And a
  diffusive mixture was charged its working set once: each species holds its own - measured,
  the second and third added 247.6 and 248.3 to one species' 248.5 - so a three-species run was
  billed a third of it. The estimate's comparison with this machine also lived inside the
  solved-field explanation, so a diffusive run through analytic fields was never compared at all.

- **The grounded boundary is a conductor, and seventeen shipped models were not clean after all.**
  A Dirichlet face pins its nodes to zero unless an electrode claimed them first, and an electrode
  claims a node by containing it - so a node on the face lying on an electrode's surface holds the
  electrode's potential or zero on the last bit. **Measured before building**: a 100 V plate flush
  with a grounded edge, nudged a trillionth of a cell each way, flips the edge node 0 ↔ 100 V and
  leaves **every free node identical to the bit** - no free node reaches a flipped one through an
  uncut arm - while the interpolated potential half a cell off the contact moves **21.9 V**, because
  the bicubic stencil reads the node. Against a conductor lying *outside* the domain the coin
  decides whether it is in the solve at all (9.61 V at the center against 0). A Neumann face is not
  a coin between two values - left free, the node solves to 100.0000 V - and takes no part. SPEC.md
  Amendment 57; `docs/numerics.md`, "A grounded face of the domain is a third conductor".

  `NodesOnSharedFaces` counts these as `BoundaryNodes` under the same code, with their own sentence
  and fix. **Nodes only** (a face node is fixed either way and the boundary is never a cut target);
  the face is the mesh's node plane, not the declared bound; "holds something" is `Agrees` against
  the electrode stripped of its excitation, in every state; an edge carrying an edge profile is not
  grounded; and a face lying in a mirror plane - the axis of an axisymmetric solve included - is
  **not a surface**, since the conductor continues across it; without that, a rod carried past the
  end wall, as the warning advises, still tripped at the corner.

  **Survey: 17 of 44 solved elements trip, all cross-sections** - ring stacks run out to the outer
  wall, the Paul trap's ring, the einzel lens's center tube, the Kingdon wire's ends, and two
  mirrors. Moving **only the face on the wall** by 1e-7 of the extent each way, on the same mesh:
  **fifteen are harmless**, every potential more than three cells from the wall and every figure
  identical to the bit. **The two mirrors are not**: `planar-mirror-pair`'s and `astral-mirror`'s end
  cap is a rectangle with no thickness lying in the grounded edge, written as the same expression as
  the edge, so it exists only because the two agree exactly - 1e-7 of the extent outside, the cap
  is gone and transmission goes 1 → 0. An edge-profile cap is **verified bit-identical** (masks, cut
  links, potentials, flight) and clean, and waits on its own change. `SharedFaceCorpusTests` pins the
  seventeen and still requires zero electrode-pair trips; the einzel-lens figure test now expects
  exactly this warning.

  **Two traps in measuring it, both in `docs/lessons.md`.** Moving the domain itself doubled the
  funnel's rows (its radius is exactly 64 cells) and reported interior volts that were a different
  mesh. And translating whole conductors instead found **a second coin, unrelated to the wall**: a
  single conductor's face on a node plane - the funnel's first and last rings' inner faces sit on
  rows 48 and 6 - makes the arms lying in that plane cut at the side face or graze past to a node a
  cell away on the last bit, worth **4.5e-4** of the funnel's flight time for a 6.4e-6-cell move.
  Cut cells are continuous in one conductor's position only while its faces are off the node planes.
  Recorded as open (SPEC.md item 11), not fixed.

  Mutation-checked seven ways. Boundary pass disabled fails the 12 positive tests and none of the
  controls; profiled edges grounded fails the edge-profile test; earthed conductors counted fails
  the earthed-plate and stage tests; stages ignored fails the stage test; the mirror lift removed
  fails the three mirror tests; tolerance zero fails the six that nudge a face or lean on the lift,
  which is ten tolerances. **Neumann faces treated as grounded first failed nothing** - the mirror
  rule hides a face flush with a mirror, so both Neumann tests were passing for the wrong reason -
  and fails both once each also has a face *crossing* the mirror on a line of nodes, which only the
  Neumann exclusion keeps quiet.

Adding a travelling-wave guide or a multipole should need only one more file — axisymmetry, repeats and RF all exist now. If it needs a change below `Einzel.Library`, LIB-1 says the abstraction is wrong — believe it.

Two findings from Stage 1 that bear on the spec:

1. **The turning-point step cap (§11) does not help and slightly hurts.** In a smooth field the flight time is at machine precision with 6 steps and marginally worse with 105. §11's rationale is that "position-error controllers under-refine" at the velocity minimum — but `ErrorNorm` weights velocity error with its own absolute floor, so it is not a position-error controller and does not under-refine. The cap is implemented and on by default (`TurningPointStepFactor = 0.01`) to honour the spec as written; the evidence says it should default to 0.
2. **A field discontinuity is a real error source and must be landed on exactly.** Dormand–Prince stage 4 carries the coefficient −56/15, so intermediate stage samples fall outside the step interval and can land on the wrong side of a field jump even when both endpoints are inside. Handling the boundary as an event took the reflectron from 5.5e-10 to 1.7e-16. A residual around 1e-10 remains and behaves as noise rather than as a controlled error — it is an artifact of idealised *discontinuous* analytic fields and should not appear in solved, interpolated fields, but it is what sets the achievable tolerance on finite-difference tests.

- **A declared gas took no part in any figure of merit, and an example in the release gate could not fail.** Two defects found while adding a thermalisation example, both larger than the example.

  **`einzel run` and `einzel test` disagreed on every model with a gas.** The figure-of-merit path built the launch, the field and the detector but never a collision sampler, so it flew in vacuum however much gas the document declared: **4904.4862 µs against 5000** on `gas-flow-carry`. Verified by deleting the gas block and getting an identical answer to the last digit. **This is the second time run and test have drifted** — the first was found and fixed by collapsing them to one implementation, and the gas then arrived on only one side of the seam. A shared entry point is not a shared computation.

  **That example could never have caught it.** It launches its ion at exactly the gas velocity so the transit is `L/u` by arithmetic — and in vacuum an ion launched at that speed covers the same metre in the same 5000 µs. The vacuum answer *was* the expectation, and closer to it than the physical answer.

  **And its tolerance was vacuous.** Expectations compare a *relative* error; that one read `500.0`, written as ±500 µs on 5000, which as a fraction admits any positive answer. Its description said "discriminating far past its ten per cent tolerance" — the same misreading, written down. An audit of all 29 corpus expectations found one other at 50%; both are now 10%.

  New: **`meanKineticEnergy`**, because equipartition is the sharpest check the collision models have — an ion left in a gas must reach (3/2)kT whatever it started with — and it was measured only in a unit test, outside the machinery that keeps every other figure honest. Over the ions still in flight rather than the arrivals, since a thermalised packet has no preferred direction and selecting on arrival selects the fast ones. **`CloudFlight.Remaining`** carries them, so a packet that never arrives still has measurable state. The `thermalisation` corpus example gives **0.039339 eV against 0.038778** on 240 ions, 1.45% high against a 5.3% standard error.

- **`render animation`, and RND-7 enforced by the interface rather than checked.** The
  requirement is emphatic: an animation "declares an explicit non-linear time mapping —
  playback rate per sequence phase — and the current rate is displayed on screen
  throughout playback. **Neither part is optional.** This is the animation equivalent of
  GRD-1: the artifact may compress, but it may not hide that it compressed."

  **So an animation is asked for through a render spec and there is no `--rate` flag.** A
  model document has nowhere to declare a mapping, so there is no command line that
  produces an animation without one. `--fps` *is* offered, because a frame rate is a
  property of the playback device and changes no claim the animation makes.

  On the shipped reflectron, three phases — 4 µs/s inbound, **0.5 µs/s through the
  turn-around**, 4 µs/s out — give 1.000 s, 4.400 s and 0.995 s of playback. The
  turn-around is **a fifth of the flight and 69% of the film**, which is exactly what one
  rate cannot show. Sixty-five frames; the frame at playback 1.000 s shows 4.0000 µs.

  **The rate has a unit, and two readings.** `us/s`, `ns/s`, `ms/s` — dimensionless,
  since it is a time over a time, and what makes it a *rate* is that the denominator is a
  second of playback rather than of flight, which no dimension can carry. The stamp reads
  `t = 5.000 µs · turn-around · 500 ns of flight per second of playback — 2,000,000x
  slower than real time`: the first converts anything on screen back into flight time and
  carries a unit (GRD-1); the second is the intuition and alone says nothing about how
  long the flight is.

  **Frame times are computed, never accumulated** — one lookup and one multiply from each
  frame's own playback time, so a phase that is not a whole number of frames long cannot
  push error into the phases after it. The final frame is forced onto the end, because
  for a flight the last instant is the one a reader wants. And a frame landing exactly on
  a boundary announces the **incoming** rate, since it is followed by a frame's worth of
  playback at the new speed.

  **A design bug I introduced and the test that did not catch it.** The first version
  handed each frame the part of the flight drawn so far. An analytic model takes its
  extent from the flight, so every frame chose its page from its own prefix — the scale
  changed frame to frame and the ion sat pinned to the edge of a box that grew to meet
  it, which reads as a camera following the ion. Nothing about a single frame reveals it.
  Fixed by handing over the whole flight with an instant to truncate at. **The test
  written for it passed with the bug restored**, because it used the einzel lens — a
  *solved* geometry whose extent comes from its declared domain and never touches the
  flight. Moved onto an analytic reflectron it fails at once. Third time in one night
  that a test exercised a path not containing the line under test.

  Refused: a spec with `trajectory: false` on a trajectory model (the geometry and the
  field are identical on every frame, so the sequence would be one drawing repeated), a
  spec with no phases, a phase that does not advance, a non-positive rate, and a
  mapping a diffusive run cannot reach. Warned about rather than refused: `animation.past-arrival` and
  `animation.stops-short`, because both are legitimate to ask for and neither looks like
  a choice.

  **No video, and that is LIC-1** — ffmpeg is exactly what would be reached for. What
  comes out is numbered vector frames plus a `frames.json` schedule; assembling them is
  an out-of-process step with a tool the user supplies.

  **The field moves too, and finding that it did not was the fourth sighting of one
  defect.** A driven field implements the time-free `IElectrostaticField` as well as
  `ITimeVaryingField` and **answers the time-free one at t = 0 without failing**, so the
  renderer drew the same instant on every frame — after `einzel solve` reporting the DC
  pattern of a driven geometry, the diffusive mode stepping a density through a snapshot
  of the RF, and `SuperposedField` becoming a snapshot when a driven member was summed
  into it. The instant is now declarable (`atSeconds` on a render spec) and every frame
  supplies its own; a section of a driven model carries `render.field-at-instant` either
  way, because a figure of a driven structure is a frame of a film whether or not it is
  drawn as one.

  Checked exactly, over one period of a 1 MHz quadrupole: **20, 0, 20, 0, 20**
  equipotential paths at 0, T/4, T/2, 3T/4, T. The drive is through zero at the quarter
  points so there is nothing to contour; at T it is the *same drawing as at 0, to the last
  bit*; at T/2 the rod pairs have swapped sign. **The contour levels had to be fixed once
  across the animation** — a driven field's range changes through the cycle, and levels
  taken per frame would be spread over rounding noise at a zero crossing and fill the
  frame with contours of nothing. Same defect as a page chosen per frame, in the other
  axis.


  **And a diffusive model is animated as a moving density.** It was refused outright, and
  rightly while a run reported only the density it *ended* with — the frames would all
  have been the same box. Now the command layer runs the transport once with the frames'
  own instants as its snapshot list and hands the renderer the results, as the section
  path already does. On the corpus drift tube over 200 µs the packet **drifts (22 → 100
  mm), spreads (24 → 59 mm), and narrows again at the end** as its leading edge is
  collected — three things a trajectory cannot show.

  **The contour levels are anchored once, and that matters more than the page did.**
  Density contours sit at decades below the peak, and a diffusing packet's peak falls as
  it spreads: anchored per frame the levels would fall with it, the contours would stay
  the same size, and *a film of a packet spreading would show a packet doing nothing*.
  Not flicker — a lie. Anchored across the animation, later frames show **fewer**
  contours, because the density really is lower.

  **Not built: geometry that moves.** A stage may change what an electrode holds and not
  where it is, which the sequencer already enforces, so the conductors are identical on
  every frame by construction. A mechanism with a moving part is not expressible at all.

- **The scaffolded reflectron drew its turning point 105 metres off a 160 mm page**, and
  had done since sections were built. A model with no declared solve domain takes its
  extent from the instrument's own points, and those were the source and the detector
  alone — which in a reflectron are **the same point**, because the ion is caught where it
  launched. The pad was then a tenth of a millimetre around a flight of 1.3 m.

  Found by animating it. **No test caught it because every render test uses a device
  template, and every device template declares a solve domain** — so the analytic branch
  of the extent had no coverage at all. The flight is now included, and the pad comes
  from the extent actually gathered rather than from a separation that is zero. It costs
  nothing: the trajectory had to be flown to be drawn, and it is now flown before the
  page is chosen rather than after.

- **A density can be drawn while it is still moving.** A diffusive run reports the
  density it *ended* with, so a model whose ions have all arrived left an empty box —
  correctly, and uselessly, because the picture worth having is the packet in flight.
  The only way to get one was to shorten `maximumFlightTime`, which gets a packet by
  throwing away everything after the moment being looked at.

  `DriftDiffusion.Run` takes a list of instants and returns the density at each;
  `einzel render section --at-us <t>` draws one. On the corpus drift tube: **0 contours
  at the end, 10 at 50 µs with the packet centred at 49.5 mm, 11 at 150 µs at 102.9 mm**
  — it drifts and it spreads, both visible.

  **The instant recorded is not quite the one asked for, and the figure says both.** A
  diffusive step is set by a stability limit and cutting it to land exactly would change
  the step sequence and so the answer — a high price for an offset of one step. The
  provenance carries `density at t = 50.1302 us, asked for 50 us`.

  **Recording does not perturb what it records**, asserted rather than assumed: the same
  run with and without snapshots is bit-identical in step count, collected ions and every
  node of the final density. An instant past the end is **absent** rather than filled in
  with the final state, because a density never computed is not the density at that
  instant and substituting the last one would make a figure of a finished run look like
  one of a running run.

  The unit is in the flag's name, as `--width-mm` already does it: a bare `--at` would be
  ambiguous between microseconds and seconds by a factor of a million.

- **A dimension is measured, never written down.** The memo's own figures are line
  drawings *with* dimensions, and a section without them says what the instrument looks
  like and not how big any of it is. What a `dimensions` entry declares is **the two
  points it spans**; the length is the distance between them, computed when the figure is
  drawn. `label` names the span — "drift", "bore" — and does not carry the value, because
  a typed number is a second statement of something the model already says and the two
  part company at the first parameter change.

  **The points may be expressions over the model's own parameters**, so a dimension
  describes the geometry rather than where the geometry used to be. Changing
  `turningDepth` from 50 to 80 mm and re-rendering the *same figure spec* gives
  `penetration 50 mm` then `penetration 80 mm`, with no edit in between — which is what
  the test asserts: one spec, two models, two measurements. §9's rule for a model, "every
  placement is a parametric expression, never a baked number", is not weaker for a
  drawing of it.

  Ordinary drafting convention: extension lines clear of the feature and past the
  dimension line, an offset dimension line, arrowheads, and the measurement above it on
  the side away from what it measures. The offset is signed so two dimensions over one
  feature can go opposite ways, and the unit is chosen from the magnitude because one
  figure may carry a 300 mm drift and a 50 µm channel.

- **Two defects and one unresolved one, from trying to write a pulsed-extraction
  example.** The corpus has no sequenced example — the one Phase 4 capability it does not
  exercise — and it still does not, because the run does not complete. But the trip found
  two real defects that are fixed.

  **`CanDoWork` read the base potentials and not the stages**, so a pulsed-extraction trap
  — which holds everything at zero until it switches — was refused as an instrument in
  which nothing could move an ion. That is the **fourth** appearance of one pattern and
  the *third* in this one function: `einzel solve` reporting the DC of a driven geometry;
  `CanDoWork` asking only about DC and refusing the Paul trap; its 3-D arm inspecting
  nothing at all; and now the stages. The advice already written down — "grep for
  `.Potential` the next time something driven behaves as though it were earthed" — does
  not catch the fourth, because it is not about the drive. The wider statement: **a check
  that asks what an instrument is doing must ask over every configuration it has**, and a
  sequenced one has as many as it has stages. The control is asserted too: a sequence
  that never energises anything is still refused.

  **A stage set to an expression was read as zero.** `CompileStages` built its overrides
  with `Quantity.From(value.Value, value.Unit)` and never looked at `value.Expression`,
  and `Value` defaults to zero — so a stage meant to apply a kilovolt applied nothing.
  The model validated, the field solved, and the run reported an ion that never moved,
  with no diagnostic anywhere, because zero volts is exactly what the *first* stage of an
  extraction legitimately applies. Now refused rather than supported: what an expression
  should mean there is a design question, since the surface it would evaluate against is
  the one the stage is changing.

  **Fixed, and the fix is one line.** `FlightTimeStudy`
  refines by scaling the relative tolerance *and both absolute floors*, and at its deepest
  rung `AbsoluteVelocityTolerance` reaches **1e-11 m/s** — ten picometres per second,
  against thermal speeds of hundreds of metres. For an ion starting from rest the
  normalised velocity error is then unsatisfiable at any step size, so the step halves 63
  times. Isolated by tightening each of the three alone: only the velocity floor
  reproduces it, and that floor is what stops `ErrorNorm` being a position-error
  controller. `einzel preview`, one run, gives **2.9106 µs against a closed form of
  2 + 0.910572113** — the model was always right.

  Holding the floor leaves the reflectron **bit-identical** and makes its interval **17×
  narrower** — a measured residual instead of a saturated floor. It also broke the test
  documenting `convergence.at-resolution`, and the reason is the part worth keeping:
  **that model's bit-exact rung agreement depended on the ladder over-tightening the very
  floor at issue** — the test had been asserting a coincidence. Nothing reachable through
  the study's API reproduces the collapse, so the rule was given a name,
  `FlightTimeStudy.ConvergenceResidual`, and is tested directly on runs that agree to the
  bit. **A rule that can only be exercised by a coincidence is a rule with no test.**

  **`sequenced-extraction` ships** — the corpus's first sequenced model, and Phase 4's
  sequencer in the release gate for the first time. Predicted 2 µs + 0.910572113 µs,
  measured **2.9105718, 1.0e-7 out**: the finite plates and the grounded boundary, not
  the sequencer. Corpus 30 → 31. Full diagnosis in `docs/lessons.md`.

- **A pushed packet got faster, and the measurement redirected the work.** The pair sum is
  O(N²) and the field evaluation O(N), so the sum is the obvious target. Measured first:
  on a solved **volume** at 240 macroparticles the sum is **27% of a stage** and the
  tricubic gather is 73%; on a **cross-section** it is 71%. So vectorising the sum is worth
  1.04x on the volume and 1.93x on the cross-section, and spreading the field loop across
  cores is worth 1.64x on the volume and nothing on the cross-section. Both built; the
  choice between them is **timed at the first stage** rather than guessed from the member
  count, and the automatic choice matches or beats the better forced one in both cases
  (226 → 113 ms and 525 → 319 ms end to end).

  **The vectorised pair sum** compacts the active members first (no branch in the inner
  loop), keeps structure-of-arrays in pooled buffers, and puts j across the lanes so the
  symmetric half falls out — the reaction on each j is a lane, the action on i is one
  horizontal sum. **434 Mpair/s against 140 scalar** at 240 members, 3.33x at a thousand,
  against a 4-wide vector. The scalar rate is flat at 140 across a thirtyfold range, which
  is what says the measurement is of the arithmetic rather than the cache.

  **CMP-1's reference is now selectable, not merely retained.** A scalar implementation
  nothing can run is one nobody can check; `CoulombInteraction.Kernel` is how a test reaches
  it, and the two agree to 3e-15 of the acceleration scale over nine sizes, with members
  inactive, with a pre-loaded accumulator, and on Newton's third law. A deliberate mutation
  fails 10 of 16.

  **The parallel field loop is bit-identical however many cores run it**, because nothing is
  summed across members — asserted on 96 members over 48 steps on 16 cores, every final
  position, velocity and flight time equal to the last bit. The mutual force is deliberately
  *not* parallelised: splitting a symmetric sum across threads would make the accumulation
  order depend on the thread count.

  **And the inner loop now allocates nothing**, from 504 bytes a step. Twenty-two arrays a
  step were pooled — including the Runge–Kutta stage weight lists, which are *constant*
  rather than merely poolable. That left exactly 504 bytes still there, **invariant with the
  member count**, which is what identified it: seven stages × 72 bytes is the closure of a
  lambda in the `if (parallel)` branch the serial path never takes. **A lambda's captures
  are hoisted when the method is entered, not when its branch is taken.** In
  `docs/lessons.md`, with two others: a benchmark run a handful of times measures the
  warm-up, and measure which half is slow before optimising the half you assumed.

- **The trap's capacity question, asked of the volume and answered no.** Thirteen runs: a
  cooled 3 mm slice held by the axial well, ramped at 16,700 u/s through the stability edge
  in the paper's helium, against matched cross-section runs. **Every pushed width lies inside
  the no-push realisation spread of its own configuration** — cross-section 0.403/0.434 with
  no push against 0.434 at 400×; volume 1.320/1.531/1.597 against 1.421, 1.539 and 1.575 —
  and the median moves 0.005 u in the cross-section. The 240-macroparticle volume runs carry
  a `spacecharge.softening` violation, so they were re-run at the 660 the warning names: 1.401 u,
  inside the spread and *lower* than before. A null that survives removing its own caveat is
  worth more than one that has not been tested against it.

  **Three findings came with it, all methodological.** A **bootstrap over one realisation is
  not an error bar** in a chaotic ensemble — it called two no-push runs differing only in
  cloud seed significantly different, which is how the first "19% broadening" was retracted;
  independent seeds are the error bar. The perturbation is **chaotic rather than mean-field**:
  per-ion ejection moves 1.63 u rms at 24,000 ions and 1.74 at 96,000, a ratio of 1.07 for
  4×, correlation 0.53 — decorrelation, saturating, so differencing two runs cannot measure
  the force. And **the peak width here is the mesh, not the third dimension**: the volume runs
  at 1 mm because 0.5 mm takes minutes, and the cross-section at 1 mm gives 1.391/1.562 —
  the volume's range, from a geometry with no axial motion at all.

  **What the axial well demonstrably does is hold the cloud against diffusion.** None lost in
  a 500 µs hold with it; **88 of 240 lost without it and no space charge at all**, 121 at
  96,000 ions. Mean free path 6.9 mm, so diffusion spreads ~17 mm in that time. That is the
  thing a cross-section cannot show, and it is why the axial sections exist.
