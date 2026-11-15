# Warning and error codes

> Hand-compiled index of the codes `einzel` attaches to errors and warnings, compiled against commit `6ed90fd` from the source in `src/` rather than from the narrative docs. There is no generated registry for warning codes, so this page can drift: check an entry's **Source** line against the code before relying on a detail. Thresholds quoted here are the ones in the source at that commit.

Two families exist. **Upper-case** codes (`UNITS_INCOMPATIBLE`) are error codes, mostly from `Einzel.Core.Errors.ErrorCodes`, plus a few written as literals; two (`TILT_NOT_AVAILABLE`, `NOTHING_TO_EXPORT`) are errors, and nine (`PEAK_*`, `ENSEMBLE_*`, `DRAWS_*`, `TRAJECTORY_INCOMPLETE`, `CONVERGENCE_ORDER_BELOW_NOMINAL`, `ENERGY_DRIFT_EXCEEDS_BUDGET`) are warnings. **Dotted lower-case** codes (`mesh.node-on-shared-face`) are warnings written as string literals where the warning is built. Both families go through the same surface: a code, a message and a severity.

> **The list of codes is held to the source by a test.** `DocumentationWarningCodeTests` (in `tests/Einzel.Cli.Tests`) fails when a code in `src/` has no entry here, when an entry names a code no longer in `src/`, and when the index and the entries disagree. What an entry says about a code is not checked.

## Exit codes

Defined in `src/Einzel.Core/Errors/ExitCode.cs`; the mapping from an error code to an exit code is in `src/Einzel.Cli/Program.cs` (around line 50).

| Exit | Name | Meaning | Error code that maps to it |
| --- | --- | --- | --- |
| 0 | `Success` | The operation completed. Warnings may still be attached. | none |
| 1 | `ValidationFailure` | Model or study failed validation: schema, units, bounds, solvability. Also the default for any error code without a special mapping, and for a missing file or directory. | every other code, including `GRID_TOO_LARGE`, `COST_GATE_REFUSED`, `ENGINE_PIN_MISMATCH` |
| 2 | `RegimeViolation` | The transport mode is outside its validity (REG-2). A completed `run` also exits 2 when its flight-time warnings include a `regime.*` ValidityViolation. | `REGIME_INVALID` |
| 3 | `CostGateRefused` | `einzel estimate` found the cost above the threshold (GRD-8). The study itself is not blocked; `--threshold <seconds>` moves the gate. | none (returned by the estimate path directly) |
| 4 | `ConvergenceFailure` | A solve, ensemble or optimisation failed to converge, or a `run` did not complete (the integrator gave up). | `CONVERGENCE_FAILED` |
| 5 | `EnginePinMismatch` | Declared in the enum for UPD-5. **No code path returns it** at this commit. | none |
| 6 | `InternalError` | A defect in the platform. Also returned for an unexpected `IOException` or `UnauthorizedAccessException`. | `INTERNAL_ERROR` |

A run that finishes what it was asked to do exits 0 even if its ions were lost or held: a held trap, a thermalisation and a deliberate loss are successes. Only an integrator that gave up exits 4.

## How to read a warning

Every result carries a list of warnings (`ValidityWarning`: code, message, severity). Every failure carries an `EinzelError` (code, JSON-Pointer path, constraint, observed value, suggestion, severity `Info`, `Warning` or `Error`). The code is stable and meant for branching; the message is for reading.

Warning severity ladder (`WarningSeverity`, `src/Einzel.Core/Results/ValidityWarning.cs`):

| Severity | Meaning | Suppressible |
| --- | --- | --- |
| `Advisory` | Advice a caller may not want repeated. | **Yes**, the only one |
| `Qualified` | Usable, but qualified: an unconverged ensemble, a coarse mesh, an approximation near its limit. | No |
| `ValidityViolation` | Computed outside the validity of the model used (GRD-3, REG-2). | No, in any mode |
| `Provenance` | Records how a number was produced or measured (preview tier, third-party extension, engine below the floor, a definition). Travels with the artifact. | No |

Suppressibility is computed from severity, not set by a caller. A non-suppressible warning does not change the exit code by itself (taint, never block); it stays attached to the number it qualifies.

Where warnings appear:

- **Terminal:** `[Severity] code: message`. Advisories go to stdout and everything else to stderr, so redirecting stdout still shows what qualified a number.
- **`--json`:** each warning with `code`, `message`, `severity` and `suppressible`, inside the envelope of the number it qualifies; results on stdout, diagnostics on stderr.
- **Results and manifests:** stored under `results/` with the run; `einzel verify` reads manifests and `einzel report` reads results back.
- **Files and figures:** `.vti` exports carry warnings with severity in the file header; vector figures stamp them on the page and a `ValidityViolation` hatches a band across it; PNG stills carry them in text chunks.
- **`einzel report`:** lists warnings by severity per run; only `ValidityViolation` gets the hatched mark.

Prose elsewhere sometimes abbreviates a code (for example `mobility.outside` for `mobility.outside-fit`); the entries below use the full spelling from the source.

## Index

[`animation.past-arrival`](#animationpast-arrival), [`animation.stops-short`](#animationstops-short), [`boundary.below-acc6`](#boundarybelow-acc6), [`boundary.budget-exhausted`](#boundarybudget-exhausted), [`boundary.multiple-crossings`](#boundarymultiple-crossings), [`boundary.single-crossing-checked`](#boundarysingle-crossing-checked), [`collisions.rate-underestimated`](#collisionsrate-underestimated), [`collisions.sequence-leg`](#collisionssequence-leg), [`collisions.single-ion-interval`](#collisionssingle-ion-interval), [`convergence.at-resolution`](#convergenceat-resolution), [`CONVERGENCE_FAILED`](#convergence_failed), [`CONVERGENCE_ORDER_BELOW_NOMINAL`](#convergence_order_below_nominal), [`COST_GATE_REFUSED`](#cost_gate_refused), [`diffusion.implicit-not-paying`](#diffusionimplicit-not-paying), [`diffusion.implicit-step`](#diffusionimplicit-step), [`diffusion.implicit-unconverged`](#diffusionimplicit-unconverged), [`diffusion.packet-resolution`](#diffusionpacket-resolution), [`diffusion.packet-under-resolved`](#diffusionpacket-under-resolved), [`DRAWS_FAILED`](#draws_failed), [`DRAWS_FEW`](#draws_few), [`ENERGY_DRIFT_EXCEEDS_BUDGET`](#energy_drift_exceeds_budget), [`ENGINE_PIN_MISMATCH`](#engine_pin_mismatch), [`ensemble.degenerate`](#ensembledegenerate), [`ensemble.resampling-failed`](#ensembleresampling-failed), [`ensemble.small-sample`](#ensemblesmall-sample), [`ensemble.too-small-to-resample`](#ensembletoo-small-to-resample), [`ENSEMBLE_INCOMPLETE`](#ensemble_incomplete), [`ENSEMBLE_SMALL`](#ensemble_small), [`extension.attributed`](#extensionattributed), [`extension.isolation-incomplete`](#extensionisolation-incomplete), [`field.beyond-declared-depth`](#fieldbeyond-declared-depth), [`field.not-converged`](#fieldnot-converged), [`field.region-fringe`](#fieldregion-fringe), [`field.region-potential-step`](#fieldregion-potential-step), [`focusing.fit-residual`](#focusingfit-residual), [`gas.flow-extrapolated`](#gasflow-extrapolated), [`gas.flow-imported`](#gasflow-imported), [`gas.flow`](#gasflow), [`gas.pressure-extrapolated`](#gaspressure-extrapolated), [`gas.pressure-imported`](#gaspressure-imported), [`gas.stationary-above-flow-threshold`](#gasstationary-above-flow-threshold), [`GRID_TOO_LARGE`](#grid_too_large), [`INTERNAL_ERROR`](#internal_error), [`mesh.node-on-shared-face`](#meshnode-on-shared-face), [`mixture.populations`](#mixturepopulations), [`mixture.seed-overlaps-metal`](#mixtureseed-overlaps-metal), [`mobility.derived`](#mobilityderived), [`mobility.outside-fit`](#mobilityoutside-fit), [`mobility.peak-outside-ramp`](#mobilitypeak-outside-ramp), [`mobility.peak-unresolved`](#mobilitypeak-unresolved), [`mobility.resolving-power-definition`](#mobilityresolving-power-definition), [`NOTHING_TO_EXPORT`](#nothing_to_export), [`optimiser.budget-exhausted`](#optimiserbudget-exhausted), [`optimiser.failed-evaluations`](#optimiserfailed-evaluations), [`optimiser.optimum-at-bound`](#optimiseroptimum-at-bound), [`parameters.fitted`](#parametersfitted), [`parameters.guessed`](#parametersguessed), [`PEAK_MODEL_ASSUMED`](#peak_model_assumed), [`PEAK_UNRESOLVED`](#peak_unresolved), [`probe.field-at-instant`](#probefield-at-instant), [`probe.inside-conductor`](#probeinside-conductor), [`probe.outside-domain`](#probeoutside-domain), [`probe.refused`](#proberefused), [`project.nothing-run`](#projectnothing-run), [`project.orphaned-results`](#projectorphaned-results), [`regime.collisions-outrun-rf`](#regimecollisions-outrun-rf), [`regime.comparison-incomplete`](#regimecomparison-incomplete), [`regime.comparison-mismatched-mechanism`](#regimecomparison-mismatched-mechanism), [`regime.comparison-outside-band`](#regimecomparison-outside-band), [`regime.comparison-unmatched-boundaries`](#regimecomparison-unmatched-boundaries), [`regime.diffusion-below-validity`](#regimediffusion-below-validity), [`regime.knudsen-continuum`](#regimeknudsen-continuum), [`regime.model-above-validity`](#regimemodel-above-validity), [`regime.model-below-validity`](#regimemodel-below-validity), [`regime.no-gas`](#regimeno-gas), [`regime.no-path`](#regimeno-path), [`regime.no-trajectory`](#regimeno-trajectory), [`regime.overlap-band`](#regimeoverlap-band), [`regime.trajectory-above-validity`](#regimetrajectory-above-validity), [`REGIME_INVALID`](#regime_invalid), [`render.density-at-instant`](#renderdensity-at-instant), [`render.density-empty`](#renderdensity-empty), [`render.extruded-depth`](#renderextruded-depth), [`render.field-at-instant`](#renderfield-at-instant), [`render.first-species`](#renderfirst-species), [`render.mixed-sequence`](#rendermixed-sequence), [`render.no-density`](#renderno-density), [`render.no-path`](#renderno-path), [`render.no-trajectories`](#renderno-trajectories), [`render.phase`](#renderphase), [`render.plane-misses-conductors`](#renderplane-misses-conductors), [`render.sequence-truncated`](#rendersequence-truncated), [`report.manifest-without-result`](#reportmanifest-without-result), [`report.nothing-run`](#reportnothing-run), [`report.result-unreadable`](#reportresult-unreadable), [`report.run-interrupted`](#reportrun-interrupted), [`report.study-not-rendered`](#reportstudy-not-rendered), [`result.preview-tier`](#resultpreview-tier), [`results.no-cloud`](#resultsno-cloud), [`results.no-envelope`](#resultsno-envelope), [`rf.effective-potential`](#rfeffective-potential), [`rf.quiver-exceeds-mesh`](#rfquiver-exceeds-mesh), [`scan.no-figure-anywhere`](#scanno-figure-anywhere), [`scan.outside-declared-bounds`](#scanoutside-declared-bounds), [`SCHEMA_INVALID`](#schema_invalid), [`secular.no-drive`](#secularno-drive), [`sequence.last-phase-holds`](#sequencelast-phase-holds), [`sequence.mode-changes`](#sequencemode-changes), [`sequence.none`](#sequencenone), [`sequence.nothing-eluted`](#sequencenothing-eluted), [`spacecharge.approaching`](#spacechargeapproaching), [`spacecharge.grid-resolution`](#spacechargegrid-resolution), [`spacecharge.ignored`](#spacechargeignored), [`spacecharge.macroparticle-collisions`](#spacechargemacroparticle-collisions), [`spacecharge.mean-field`](#spacechargemean-field), [`spacecharge.modelled`](#spacechargemodelled), [`spacecharge.point-packet`](#spacechargepoint-packet), [`spacecharge.self-field-unconverged`](#spacechargeself-field-unconverged), [`spacecharge.sequenced-packet`](#spacechargesequenced-packet), [`spacecharge.softening`](#spacechargesoftening), [`spectrum.peak-at-band-edge`](#spectrumpeak-at-band-edge), [`spectrum.short-record`](#spectrumshort-record), [`study.dry-run`](#studydry-run), [`TILT_NOT_AVAILABLE`](#tilt_not_available), [`TRAJECTORY_INCOMPLETE`](#trajectory_incomplete), [`transport.azimuth-invented`](#transportazimuth-invented), [`transport.deposited-outside-grid`](#transportdeposited-outside-grid), [`transport.mode-changed-in-sequence`](#transportmode-changed-in-sequence), [`transport.mode-changed`](#transportmode-changed), [`transport.no-flight-time`](#transportno-flight-time), [`transport.sampled-density`](#transportsampled-density), [`transport.sequenced-no-flight-time`](#transportsequenced-no-flight-time), [`transport.velocity-assumed`](#transportvelocity-assumed), [`UNITS_INCOMPATIBLE`](#units_incompatible), [`UNITS_REQUIRED`](#units_required), [`UNITS_UNKNOWN`](#units_unknown), [`VALUE_OUT_OF_BOUNDS`](#value_out_of_bounds)

## Model validation and errors

### GRID_TOO_LARGE

A volume mesh (or the particle-in-cell grid) has more nodes than a solve may hold.

- **Severity:** Error; exits 1
- **Raised by:** `validate` and every verb that reads the model
- **When:** Each axis rounds its interval count up to a power of two from the requested cell size and the product exceeds `VolumeMesh.MaximumNodes`. Also raised for `transport.spaceChargeGrid.nodes`.
- **What to do:** Use the cell size the suggestion names (it is checked after a round trip through its own text), or shrink the domain. The 2-D solver has no such limit.
- **Source:** `Einzel.Core/Numerics/VolumeMesh.cs:288; Einzel.Core/Model/ModelValidator.cs:4452`

### NOTHING_TO_EXPORT

`einzel export --mesh` found no conductor surface to mesh.

- **Severity:** Error
- **Raised by:** `export`
- **When:** The model's solved elements declare no electrodes.
- **What to do:** Only solved elements have geometry; an analytic field is a formula. Add a `solve`, `solve3d` or axisymmetric element.
- **Source:** `Einzel.Commands/ExportCommand.cs:101`

### SCHEMA_INVALID

The document does not satisfy the model schema, or states something inconsistent.

- **Severity:** Error
- **Raised by:** Every verb that loads a model, study or render spec
- **When:** Unparseable or non-object JSON; unrecognised property; wrong type; inconsistent declarations (both `drive` and `drives`, a repeated electrode name in one solve); an unknown transport mode name.
- **What to do:** Check spelling against `einzel schema` (generated from the document types). By far the commonest code (over 120 sites).
- **Source:** `Einzel.Io/ModelJson.cs:74,90; Einzel.Core/Model/ModelValidator.cs:235`

### TILT_NOT_AVAILABLE

An axisymmetric solve declares a non-zero `tiltHalfTurns`.

- **Severity:** Error
- **Raised by:** `validate`
- **When:** `tiltHalfTurns` is non-zero on a solve with cylindrical symmetry.
- **What to do:** Use a translational cross-section, or tilt the electrodes.
- **Source:** `Einzel.Core/Model/ModelValidator.cs:2905`

### UNITS_INCOMPATIBLE

A value has the wrong physical dimension for its field, or dimensions were combined illegally.

- **Severity:** Error
- **Raised by:** `validate`; parameter and expression evaluation
- **When:** The unit's dimension differs from the field's, or an expression mixes dimensions.
- **What to do:** Supply a unit of the dimension the message states. A mismatch raised inside arithmetic is located by the catching frame, so the path names the parameter or electrode face.
- **Source:** `Einzel.Core/Units/Quantity.cs:88; Einzel.Core/Model/QuantityValue.cs`

### UNITS_REQUIRED

A quantity has no unit.

- **Severity:** Error
- **Raised by:** `validate`, and every verb that loads a model
- **When:** A quantity is given without a unit symbol (empty or whitespace). Also raised for an animation rate with no unit.
- **What to do:** Add a unit such as `mm`, `keV` or `mbar`; the suggestion names examples.
- **Source:** `Einzel.Core/Units/UnitRegistry.cs:206; Einzel.Render/AnimationDocument.cs:108`

### UNITS_UNKNOWN

A unit symbol is not in the unit registry.

- **Severity:** Error
- **Raised by:** `validate` and any verb that loads a model
- **When:** The symbol is not registered.
- **What to do:** The suggestion lists close spellings; check against `einzel schema`.
- **Source:** `Einzel.Core/Units/UnitRegistry.cs:222; Einzel.Core/Model/ModelValidator.cs:5018`

### VALUE_OUT_OF_BOUNDS

A value falls outside its declared or physical bounds.

- **Severity:** Error
- **Raised by:** `validate`; scans, studies and outline edits also use it
- **When:** A value is outside the bounds declared on a parameter, or outside a range the validator requires (for example `spaceChargeGrid.padding` not above 1).
- **What to do:** Move the value inside the bound in the constraint, or widen the declared bound.
- **Source:** `Einzel.Core/Model/ModelValidator.cs:257 (about 90 sites across validator, scan, study and outline code)`

## Field solve and mesh

### CONVERGENCE_FAILED

A solve, ensemble, study or optimisation did not converge.

- **Severity:** Error; exits 4
- **Raised by:** `export`, field assembly, studies, optimiser, sequenced runs
- **When:** An unconverged solve is exported or built bare; the unperturbed or starting design of a study produces no figure of merit.
- **What to do:** Run `einzel solve` for the residual and convergence factor per element and channel; for a study, check the objective at nominal parameters first.
- **Source:** `Einzel.Fields/FieldAssembly.cs:248; Einzel.Commands/ExportCommand.cs:376; Einzel.Sweeps/ToleranceStudy.cs:169; Einzel.Sweeps/Optimisation.cs:304; Einzel.Commands/SequencedRun.cs:795`

### field.beyond-declared-depth

A half-space's ion turns round beyond its declared depth.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** A beam ion (at three sigma of a declared energy spread, else the nominal ion) turns behind the plate because the cap potential is below the beam energy.
- **What to do:** Raise the cap or lengthen the declared depth.
- **Source:** `Einzel.Commands/RunCommand.cs:1050`

### field.not-converged

A field element's solve stopped short of its tolerance.

- **Severity:** ValidityViolation
- **Raised by:** `run`, `preview`, figures (via `FieldAssembly.BuildReported`)
- **When:** Any solve in the element reports not converged. The bare `FieldAssembly.Build` throws `CONVERGENCE_FAILED` instead.
- **What to do:** Run `einzel solve` for residual and convergence factor per channel; refine or fix the geometry.
- **Source:** `Einzel.Fields/FieldAssembly.cs:606`

### field.region-fringe

A bounded analytic element has a fringe.

- **Severity:** Provenance
- **Raised by:** `run` and other field-building verbs
- **When:** The region declares `fringe`; the potential is continuous and the field is the gradient of the ramp.
- **What to do:** Informational: a fringe is a declared shape, not a solved one.
- **Source:** `Einzel.Fields/FieldAssembly.cs:289`

### field.region-potential-step

A bounded analytic element's potential steps at the region face.

- **Severity:** Qualified
- **Raised by:** `run` and other field-building verbs
- **When:** A region without a fringe whose potential reaches a non-zero value on its boundary. A single straight crossing is unaffected, but the field is not conservative across the face.
- **What to do:** Place the boundary where the field has decayed, or add a fringe. Treat the energy-drift diagnostic with care.
- **Source:** `Einzel.Fields/FieldAssembly.cs:513`

### mesh.node-on-shared-face

A mesh node or stencil arm lies on a face shared by conductors that disagree, or on a grounded domain face a conductor touches.

- **Severity:** Qualified
- **Raised by:** `run`, `preview`, `solve`, `export`, figures
- **When:** Nodes, arms or boundary nodes within 1e-9 of the finest spacing of such a face; the result depends on the last bit of the face arithmetic (a coin toss, not discretisation error).
- **What to do:** Move the solve domain by about a tenth of a cell, or leave a gap; at the grounded face, carry the conductor past it or use an edge profile.
- **Source:** `Einzel.Fields/Solved/SharedFaceNodes.cs:79`

## Trajectory integration and convergence

### convergence.at-resolution

The two finest refinements agree to within one unit in the last place.

- **Severity:** Provenance
- **Raised by:** `run`, studies
- **When:** `ConvergenceResidual` reports at resolution; the interval is a double-precision floor.
- **What to do:** Informational; the value is at least as good as the interval says.
- **Source:** `Einzel.Transport/Integration/FlightTimeStudy.cs:205`

### CONVERGENCE_ORDER_BELOW_NOMINAL

The tolerance ladder's observed order is under half the nominal order.

- **Severity:** Qualified
- **Raised by:** `run`, `test`, studies
- **When:** Not at resolution, finite, and `observedOrder < 0.5 * NominalOrder`.
- **What to do:** The message carries advice from the field: a gridded field says refine the mesh; an analytic field has no grid, so the floor is arithmetic or a discontinuity the ladder straddles.
- **Source:** `Einzel.Transport/Integration/FlightTimeStudy.cs:230`

### ENERGY_DRIFT_EXCEEDS_BUDGET

Relative energy drift exceeds the ACC-4 budget (1e-6).

- **Severity:** ValidityViolation
- **Raised by:** `run`, `test`, studies
- **When:** `energyDrift > EnergyDriftBudget`. The value is absent (not zero) for driven fields and for at-rest launches with no conserved scale.
- **What to do:** Tighten tolerance or refine the mesh; a 4 mm Astral cell sits just above budget.
- **Source:** `Einzel.Transport/Integration/FlightTimeStudy.cs:242`

### TRAJECTORY_INCOMPLETE

An integration ended other than at the stopping surface.

- **Severity:** Qualified if the run completed (struck an electrode, or still held at the time limit); ValidityViolation if the integrator gave up (step underflow, step budget)
- **Raised by:** `run`, `test`, studies
- **When:** `run.Outcome != StopConditionMet`; severity from `TrajectoryOutcome.Completed()`.
- **What to do:** Qualified: read transmission, itemised losses and `confined`; that is the result. ValidityViolation: refine tolerances or investigate the step collapse; the numbers stop part way.
- **Source:** `Einzel.Transport/Integration/FlightTimeStudy.cs:182`

## Analysis and figures of merit

### ensemble.degenerate

An ensemble of N ions is really one ion.

- **Severity:** Provenance
- **Raised by:** figures of merit in studies
- **When:** The source starts at rest or the energy spread is zero, so every member is identical.
- **What to do:** Declare a cloud with a temperature or spatial spread; an energy spread cannot move an ion with no energy.
- **Source:** `Einzel.Commands/FiguresOfMerit.cs:1919`

### ensemble.resampling-failed

Resampling produced fewer than two usable replicates.

- **Severity:** ValidityViolation
- **Raised by:** `results`
- **When:** `replicated.Count < 2`.
- **What to do:** Increase the sample; check the figure is computable on resampled clouds.
- **Source:** `Einzel.Analysis/Bootstrap.cs:154`

### ensemble.small-sample

The bootstrap interval comes from a very small sample.

- **Severity:** Qualified
- **Raised by:** `results`
- **When:** Sample count is at least two but small; read the interval as an order of magnitude.
- **What to do:** Launch more ions.
- **Source:** `Einzel.Analysis/Bootstrap.cs:179`

### ensemble.too-small-to-resample

A bootstrap interval was requested on fewer than two observations.

- **Severity:** ValidityViolation
- **Raised by:** `results`, figures with an envelope
- **When:** `sample.Count < 2`. The zero interval is absence of information, not certainty.
- **What to do:** Launch more ions (declare a cloud).
- **Source:** `Einzel.Analysis/Bootstrap.cs:120`

### ENSEMBLE_INCOMPLETE

Some launched ions did not reach the detector.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** `Arrived < Launched`; the resolving power describes survivors only.
- **What to do:** Read the itemised losses; do not compare across runs with different survivors.
- **Source:** `Einzel.Analysis/ArrivalTimePeak.cs:190`

### ENSEMBLE_SMALL

Fewer than 100 ions arrived.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** `Arrived < 100`.
- **What to do:** Raise `ions`; the interval reflects the small sample but the shape may be unresolved.
- **Source:** `Einzel.Analysis/ArrivalTimePeak.cs:199`

### focusing.fit-residual

The energy-scan fit for a focusing coefficient is poor.

- **Severity:** Qualified
- **Raised by:** focusing-coefficient figures
- **When:** The RMS residual exceeds 0.1 of the coefficient's spread.
- **What to do:** Do not trust the binding order; narrow the energy range.
- **Source:** `Einzel.Commands/FiguresOfMerit.cs:783`

### mobility.peak-outside-ramp

The arrival peak lies outside the declared ramp.

- **Severity:** Qualified
- **Raised by:** `mobilityResolvingPower`
- **When:** Peak before the ramp start or after its end; no resolving power is reported.
- **What to do:** Lengthen the ramp or the simulated window.
- **Source:** `Einzel.Commands/FiguresOfMerit.cs:1545`

### mobility.peak-unresolved

All arrivals fall within one solver step.

- **Severity:** Qualified
- **Raised by:** `mobilityResolvingPower`
- **When:** FWHM is not positive.
- **What to do:** Refine the density step (smaller implicit gain).
- **Source:** `Einzel.Commands/FiguresOfMerit.cs:1564`

### mobility.resolving-power-definition

States how mobility resolving power was computed.

- **Severity:** Provenance
- **Raised by:** `mobilityResolvingPower`
- **When:** Attached whenever the figure is computed against a ramp.
- **What to do:** Read the definition before comparing with a published R.
- **Source:** `Einzel.Commands/FiguresOfMerit.cs:1530`

### PEAK_MODEL_ASSUMED

Resolving power assumes a Gaussian peak.

- **Severity:** Advisory (suppressible)
- **Raised by:** `run`
- **When:** Always attached to the Gaussian-equivalent resolving power; the message gives the observed skewness.
- **What to do:** If skewness is large, quote the model-free central-half width instead.
- **Source:** `Einzel.Analysis/ArrivalTimePeak.cs:236`

### PEAK_UNRESOLVED

Every arrival lies within floating-point resolution, so the peak width is zero.

- **Severity:** Qualified
- **Raised by:** `run` (Class T figures)
- **When:** The arrival-time peak has no resolvable spread; resolving power is reported absent.
- **What to do:** Expected for a field-free slit. If something should spread arrivals, check the cloud and energy spread; `ensemble.degenerate` flags the one-ion case.
- **Source:** `Einzel.Analysis/ArrivalTimePeak.cs:303`

### results.no-cloud

The model declares no ion cloud.

- **Severity:** Provenance
- **Raised by:** `results`
- **When:** No cloud; acceptance is swept deterministically with no sampling interval.
- **What to do:** Declare a cloud for ensemble figures.
- **Source:** `Einzel.Commands/ResultsCommand.cs:207`

### results.no-envelope

Some figures are not reported by `results`.

- **Severity:** Provenance
- **Raised by:** `results`
- **When:** The build computes them only as bare numbers for ranking.
- **What to do:** Use `einzel sweep` or `einzel optimise`.
- **Source:** `Einzel.Commands/ResultsCommand.cs:196`

### spectrum.peak-at-band-edge

The strongest secular line is within one resolution width of the searched band's edge.

- **Severity:** ValidityViolation
- **Raised by:** `secularFrequencyX|Y|Z`
- **When:** Line within `ResolutionHz` of either end of the band.
- **What to do:** Widen the band and see whether the peak moves.
- **Source:** `Einzel.Analysis/SecularSpectrum.cs:212`

### spectrum.short-record

The record is too short to locate the line to 5%.

- **Severity:** Qualified
- **Raised by:** `secularFrequencyX|Y|Z`
- **When:** `ResolutionHz > 0.05 * line`; fewer than about twenty cycles observed.
- **What to do:** Lengthen the flight or hold.
- **Source:** `Einzel.Analysis/SecularSpectrum.cs:224`

## Regime, gas and collisions

### collisions.rate-underestimated

A sampled relative speed exceeded the null-collision bound.

- **Severity:** ValidityViolation
- **Raised by:** `run` in a gas
- **When:** `boundExceeded`: the collision rate was too low for at least one event, biasing every dependent result.
- **What to do:** Treat results as biased; an ion far faster than thermal outruns the bound.
- **Source:** `Einzel.Transport/Integration/FlightTimeStudy.cs:369`

### collisions.single-ion-interval

A single-ion flight time in a gas is one collisional history.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** A gas is present and one ion is flown; the interval is integrator convergence, not arrival spread.
- **What to do:** Declare an ion cloud for a distribution.
- **Source:** `Einzel.Commands/RunCommand.cs:2350`

### gas.flow-extrapolated

A collision was drawn outside the imported velocity field.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** `sampledOutsideFlow`; the edge value is continued.
- **What to do:** Widen the imported field past the tracked region.
- **Source:** `Einzel.Transport/Integration/FlightTimeStudy.cs:392`

### gas.pressure-extrapolated

A collision was drawn outside the imported pressure field.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** `sampledOutsideDensity`.
- **What to do:** Widen the imported field past the tracked region.
- **Source:** `Einzel.Transport/Integration/FlightTimeStudy.cs:380`

### regime.collisions-outrun-rf

More than one collision per RF cycle.

- **Severity:** ValidityViolation (a completed `run` exits 2)
- **Raised by:** `run`
- **When:** `CollisionsPerRfCycle > 1`; the pseudopotential picture does not apply to trajectories.
- **What to do:** Use the diffusive mode with the effective potential, or lower the pressure.
- **Source:** `Einzel.Transport/Collisions/RegimeDiagnostics.cs:252`

### regime.comparison-incomplete

Less than half the ions or density arrived inside the flight-time ceiling.

- **Severity:** ValidityViolation
- **Raised by:** `compare`
- **When:** Either transmission is below 0.5; a mean transit over a subset is not a transit time.
- **What to do:** Raise the flight-time ceiling or the field.
- **Source:** `Einzel.Commands/ModeComparison.cs:207`

### regime.comparison-mismatched-mechanism

Event-driven Langevin is compared with a mobility derived from a rigid-sphere cross section.

- **Severity:** ValidityViolation
- **Raised by:** `compare`
- **When:** Derived mobility with `gas.model == langevin`.
- **What to do:** Declare a mobility explicitly, or use the hard-sphere model on both sides.
- **Source:** `Einzel.Commands/ModeComparison.cs:159`

### regime.comparison-outside-band

The comparison pressure is outside the overlap band.

- **Severity:** ValidityViolation
- **Raised by:** `compare`
- **When:** Pressure outside `[OverlapMbar, DiffusiveMbar]` (1e-3 to 1e-2 mbar).
- **What to do:** Move into the band, or read the disagreement as saying which mode is wrong.
- **Source:** `Einzel.Commands/ModeComparison.cs:220`

### regime.comparison-unmatched-boundaries

The diffusive run has walls and the trajectory run does not.

- **Severity:** ValidityViolation
- **Raised by:** `compare`
- **When:** No declared geometry and more than 5% of the density absorbed at the grid edge.
- **What to do:** Declare electrodes or a detector so both modes see the same instrument.
- **Source:** `Einzel.Commands/ModeComparison.cs:195`

### regime.diffusion-below-validity

The diffusive mode is run below its validity pressure.

- **Severity:** ValidityViolation
- **Raised by:** diffusive `run`
- **When:** Pressure below `OverlapMbar`; ions may cross without colliding.
- **What to do:** Use trajectory integration.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1185`

### regime.knudsen-continuum

Knudsen number below 1: the gas is a continuum on the geometry's scale.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** `Knudsen < 1` and finite. The message says the run treats the gas as stationary and uniform.
- **What to do:** Import a gas velocity/pressure field, or use the diffusive mode.
- **Source:** `Einzel.Transport/Collisions/RegimeDiagnostics.cs:240`

### regime.model-above-validity

Langevin capture is declared below the crossover.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** `gas.model == Langevin` and pressure below `LangevinMbar`; will over-damp and miss the arrival-time pedestal.
- **What to do:** Declare the hard-sphere model.
- **Source:** `Einzel.Transport/Collisions/RegimeDiagnostics.cs:273`

### regime.model-below-validity

Hard-sphere scattering is declared above the Langevin crossover.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** `gas.model == HardSphere` and pressure above `LangevinMbar`; will under-damp.
- **What to do:** Declare the Langevin model.
- **Source:** `Einzel.Transport/Collisions/RegimeDiagnostics.cs:262`

### regime.no-gas

The model declares no gas.

- **Severity:** Provenance
- **Raised by:** `regime`
- **When:** `!gas.IsPresent`; there are no regime numbers to report.
- **What to do:** Informational: vacuum is always trajectory territory.
- **Source:** `Einzel.Commands/RegimeCommand.cs:142`

### regime.no-path

No trajectory was produced to report along.

- **Severity:** Provenance
- **Raised by:** `regime`
- **When:** The path is empty.
- **What to do:** Informational (RND-8: a diffusive model has no path).
- **Source:** `Einzel.Commands/RegimeCommand.cs:177`

### regime.no-trajectory

The transport mode computes a density, so there is no path to report along.

- **Severity:** Provenance
- **Raised by:** `regime`
- **When:** The mode does not produce trajectories.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/RegimeCommand.cs:163`

### regime.overlap-band

The pressure is inside the band where both transport descriptions apply.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** Pressure above `OverlapMbar` and not above `DiffusiveMbar`.
- **What to do:** Run `einzel compare` (REG-3) to report the disagreement. (The message text still says the comparison cannot be made.)
- **Source:** `Einzel.Transport/Collisions/RegimeDiagnostics.cs:229`

### regime.trajectory-above-validity

Trajectory integration is run above 1e-2 mbar.

- **Severity:** ValidityViolation (a completed `run` exits 2)
- **Raised by:** `run`
- **When:** Pressure above `DiffusiveMbar`.
- **What to do:** Use `"mode": "diffusion"` at this pressure. (The message text still says the diffusive mode is not built; it is.)
- **Source:** `Einzel.Transport/Collisions/RegimeDiagnostics.cs:217`

### REGIME_INVALID

The requested transport mode, or operation, is outside its validity for the model.

- **Severity:** Error; exits 2
- **Raised by:** `compare`, viewport watch, `render animation`, diffusive RF, transport mode lookup
- **When:** (a) a comparison is asked of a model with no gas; (b) a viewport watch is asked of a trajectory model; (c) an animation is asked of a diffusive model; (d) a diffusive RF well is asked of a non-monochromatic drive; (e) a transport mode is named that is declared but not built.
- **What to do:** The constraint names which applies and the verb to use instead. The CLI maps this code to exit 2 wherever it surfaces.
- **Source:** `Einzel.Commands/ModeComparison.cs:119; Einzel.Commands/ViewportCommand.cs:443; Einzel.Render/AnimationRenderer.cs:327; Einzel.Transport/Diffusion/PonderomotiveField.cs:125; Einzel.Transport/ITransportMode.cs:151`

## Diffusive mode

### diffusion.implicit-not-paying

The implicit step cost more than it saved.

- **Severity:** ValidityViolation
- **Raised by:** diffusive `run`
- **When:** `perStep >= result.StepGain`.
- **What to do:** Use the explicit scheme.
- **Source:** `Einzel.Commands/DiffusionRun.cs:433`

### diffusion.implicit-step

The density was stepped implicitly.

- **Severity:** Provenance
- **Raised by:** diffusive `run` with `densityStep.scheme: implicit`
- **When:** The implicit scheme was selected; reports gain, steps and sweeps per step.
- **What to do:** Informational; the error is first order in the step.
- **Source:** `Einzel.Commands/DiffusionRun.cs:416`

### diffusion.implicit-unconverged

An implicit step's inner solve stopped while still moving the density.

- **Severity:** ValidityViolation
- **Raised by:** diffusive `run`
- **When:** `WorstSweepChange > 1e-8`.
- **What to do:** Lower the gain; the ion ledger closes only to that level.
- **Source:** `Einzel.Commands/DiffusionRun.cs:445`

### diffusion.packet-resolution

Reports how many cells wide the packet is.

- **Severity:** Provenance
- **Raised by:** diffusive `run`
- **When:** Always, whether or not it is a problem.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1298`

### diffusion.packet-under-resolved

Fewer than 4 cells across the packet.

- **Severity:** ValidityViolation
- **Raised by:** diffusive `run`
- **When:** `cells < 4`; the width is the mesh's rather than the model's.
- **What to do:** Raise `densityGrid.intervalsX`.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1315`

### gas.flow

Reports whether the gas or the field carries the ions.

- **Severity:** Provenance
- **Raised by:** diffusive `run`
- **When:** The gas is flowing; gives the ratio of bulk gas speed to maximum field drift.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1096`

### gas.flow-imported

The neutral velocity is read from an imported field.

- **Severity:** Qualified
- **Raised by:** diffusive `run`
- **When:** `gas.Flow` is a sampled field; reports the fraction of the tracked region that was extrapolated.
- **What to do:** Check the overhang fraction; widen the imported box if non-zero.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1035`

### gas.pressure-imported

The gas density is read from an imported pressure field.

- **Severity:** Qualified
- **Raised by:** diffusive `run`, studies
- **When:** A pressure field is declared; the declared pressure is the mobility reference and mobility goes as 1/n.
- **What to do:** Check the field's unit and extent.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1064`

### gas.stationary-above-flow-threshold

A stationary gas is used above 1e-2 mbar.

- **Severity:** Qualified
- **Raised by:** diffusive `run`
- **When:** Pressure above `DiffusiveMbar` and no flow declared.
- **What to do:** Declare `driftVelocity` or import a velocity field.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1113`

### mixture.populations

Several populations were stepped together.

- **Severity:** Provenance
- **Raised by:** multi-species `run`
- **When:** `species` declared; states the shared step and the coupling.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/DiffusionRun.cs:362`

### mixture.seed-overlaps-metal

Every species lost part of its seed to a conductor before the run began.

- **Severity:** ValidityViolation
- **Raised by:** multi-species diffusive `run`
- **When:** `seedLoss.Count > 0`.
- **What to do:** Move the source out of the metal.
- **Source:** `Einzel.Commands/DiffusionRun.cs:354`

### mobility.derived

The mobility was derived from a cross section (Mason-Schamp), not declared.

- **Severity:** Qualified
- **Raised by:** diffusive `run`
- **When:** `declared.Derived`; it is field-independent and may overstate drift where the ion is heated.
- **What to do:** Declare a mobility with its field dependence if known.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1171`

### mobility.outside-fit

The field reaches a reduced field past the range the mobility was fitted to.

- **Severity:** ValidityViolation
- **Raised by:** diffusive `run`, `compare`
- **When:** `!mobility.IsWithinFit(worst, thinnest)`.
- **What to do:** Use a mobility fitted over the range, or reduce the field; at 166 Td a low-field value overstated drift 1.4 times.
- **Source:** `Einzel.Commands/DiffusionRun.cs:1222`

### sequence.nothing-eluted

Only a Boltzmann tail reached the detector.

- **Severity:** ValidityViolation
- **Raised by:** sequenced `run`
- **When:** Arrivals exist but are not a transmitted packet.
- **What to do:** Check the release program; there is no arrival time to report.
- **Source:** `Einzel.Commands/RunCommand.cs:1415`

### transport.no-flight-time

A density run has no flight time.

- **Severity:** Provenance
- **Raised by:** diffusive and mixture `run`
- **When:** The run computed densities, not trajectories. Raised at two sites (diffusion, mixture).
- **What to do:** Read the transit-time distribution under `diffusion` or `mixture`.
- **Source:** `Einzel.Commands/RunCommand.cs:1732,1931`

## Space charge

### spacecharge.approaching

The screen is within a tenth of the 1 ppm budget.

- **Severity:** Qualified
- **Raised by:** `run`
- **When:** `TimingFraction > 0.1 * AccuracyBudget`.
- **What to do:** Note the population limit the message names.
- **Source:** `Einzel.Commands/RunCommand.cs:1213`

### spacecharge.grid-resolution

PIC cell against mean macroparticle spacing.

- **Severity:** Provenance within 0.7 to 2.0, else ValidityViolation
- **Raised by:** `run` with `spaceCharge: pic`
- **When:** Reported every run; accuracy has an optimum near 1 and does not improve with refinement.
- **What to do:** Set `spaceChargeGrid.nodes` to the count the message names.
- **Source:** `Einzel.Commands/RunCommand.cs:870`

### spacecharge.ignored

Space charge is not modelled and the screen exceeds 1 ppm.

- **Severity:** ValidityViolation
- **Raised by:** `run` without space-charge modelling
- **When:** `TimingFraction > AccuracyBudget`.
- **What to do:** Set `spaceCharge` to `direct` or `pic`, or reduce `population`.
- **Source:** `Einzel.Commands/RunCommand.cs:1196`

### spacecharge.macroparticle-collisions

Macroparticles scatter as single ions in a gas.

- **Severity:** Provenance
- **Raised by:** `run` with space charge and a gas
- **When:** `model.Gas.IsPresent`; fluctuations are those of the macroparticle count.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/RunCommand.cs:1073`

### spacecharge.mean-field

Diffusive mean-field space charge was solved.

- **Severity:** Provenance
- **Raised by:** diffusive `run`
- **When:** `spaceCharge: meanField`.
- **What to do:** Informational (reports peak self-potential against kT/q).
- **Source:** `Einzel.Commands/DiffusionRun.cs:856`

### spacecharge.modelled

Packet self-interaction is modelled.

- **Severity:** Provenance
- **Raised by:** `run` with `direct` or `pic`
- **When:** Always for modelled space charge; the screening estimate is reported as a cross-check.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/RunCommand.cs:1159`

### spacecharge.point-packet

A multi-ion packet is declared at a single point.

- **Severity:** ValidityViolation
- **Raised by:** `run`
- **When:** `charge.IsPointLike`; the self-field is unbounded and no estimate is possible.
- **What to do:** Give the cloud a spatial spread or set population 1.
- **Source:** `Einzel.Commands/RunCommand.cs:1178`

### spacecharge.self-field-unconverged

The density's own Poisson solve did not converge.

- **Severity:** ValidityViolation
- **Raised by:** diffusive `run`
- **When:** `selfField.Report.Converged == false`.
- **What to do:** Refine the grid or relax the geometry.
- **Source:** `Einzel.Commands/DiffusionRun.cs:846`

### spacecharge.sequenced-packet

Sequenced packet space-charge caveats.

- **Severity:** Qualified
- **Raised by:** sequenced `run`
- **When:** A trajectory phase advances a packet together; the self-field is rebuilt per phase. Emitted once.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/SequencedRun.cs:757`

### spacecharge.softening

Reports the direct-sum softening against the packet's thinnest extent.

- **Severity:** ValidityViolation if the softening exceeds the thin dimension, else Provenance
- **Raised by:** `run` with `spaceCharge: direct`
- **When:** Always; a ratio above 1 means the force across the packet is suppressed.
- **What to do:** Use at least the macroparticle count the message names, or `spaceCharge: pic`.
- **Source:** `Einzel.Commands/RunCommand.cs:1110`

## RF and pseudopotential

### rf.effective-potential

A drive is modelled as a collisional pseudopotential.

- **Severity:** Provenance
- **Raised by:** diffusive `run`
- **When:** A driven field in the diffusive mode; reports the suppression against the collisionless well.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/DiffusionRun.cs:965`

### rf.quiver-exceeds-mesh

The RF quiver exceeds the cell the oscillating field is resolved on.

- **Severity:** ValidityViolation
- **Raised by:** diffusive `run`
- **When:** `worst > cell`; averaging has nothing left to average.
- **What to do:** Refine the oscillating field's mesh or lower the drive amplitude.
- **Source:** `Einzel.Commands/DiffusionRun.cs:977`

### secular.no-drive

A secular frequency was requested of a model with no time-varying field.

- **Severity:** ValidityViolation
- **Raised by:** `secularFrequency*` figures
- **When:** The field is not time-varying, or its shortest period is not finite.
- **What to do:** Declare a drive; a static field has no effective well.
- **Source:** `Einzel.Commands/FiguresOfMerit.cs:1809`

## Sequences and mode conversion

### collisions.sequence-leg

Collision count for a trajectory phase of a sequenced run.

- **Severity:** Provenance
- **Raised by:** sequenced `run`
- **When:** A trajectory phase with collision samplers.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/SequencedRun.cs:784`

### sequence.last-phase-holds

The last phase holds after the sequence ends.

- **Severity:** Provenance
- **Raised by:** `sequence`
- **When:** Always for a sequenced model.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/SequenceCommand.cs:160`

### sequence.mode-changes

The sequence crosses between transport descriptions.

- **Severity:** Provenance
- **Raised by:** `sequence`
- **When:** Any phase changes the transport mode.
- **What to do:** See the SEQ-1 conversion warnings.
- **Source:** `Einzel.Commands/SequenceCommand.cs:169`

### sequence.none

The model declares no sequence.

- **Severity:** Provenance
- **Raised by:** `sequence`
- **When:** `Phases.Count == 0`.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/SequenceCommand.cs:113`

### transport.azimuth-invented

Azimuth is drawn uniformly for an axisymmetric density.

- **Severity:** Advisory
- **Raised by:** sequenced `run`
- **When:** `density.Cylindrical`.
- **What to do:** A packet that was not axisymmetric does not come back.
- **Source:** `Einzel.Transport/PacketConversion.cs:263`

### transport.deposited-outside-grid

Part of a packet was outside the density grid at conversion.

- **Severity:** ValidityViolation
- **Raised by:** sequenced `run`
- **When:** `outside > 0`; counted, not clamped.
- **What to do:** Enlarge the grid.
- **Source:** `Einzel.Transport/PacketConversion.cs:133`

### transport.mode-changed

A packet crossed between trajectory and density descriptions.

- **Severity:** ValidityViolation
- **Raised by:** sequenced `run`
- **When:** Raised at two sites: trajectories to a density (velocities lost) and a density to trajectories (sampling error added).
- **What to do:** Read the conversion notes; neither direction is lossless.
- **Source:** `Einzel.Transport/PacketConversion.cs:109,252`

### transport.mode-changed-in-sequence

Summary that a run crossed a mode boundary.

- **Severity:** ValidityViolation
- **Raised by:** sequenced `run`
- **When:** `conversions > 0`.
- **What to do:** Read the conversion warnings above it.
- **Source:** `Einzel.Commands/SequencedRun.cs:545`

### transport.sampled-density

A converted density is a deposit of finitely many samples.

- **Severity:** Advisory
- **Raised by:** sequenced `run`
- **When:** Trajectory-to-density conversion.
- **What to do:** Refining the grid does not reduce the sampling error.
- **Source:** `Einzel.Transport/PacketConversion.cs:123`

### transport.sequenced-no-flight-time

A sequenced run has no single flight time.

- **Severity:** Provenance
- **Raised by:** sequenced `run`
- **When:** Always.
- **What to do:** Read the per-phase table under `sequence`.
- **Source:** `Einzel.Commands/RunCommand.cs:1456`

### transport.velocity-assumed

Velocities were invented for a density-to-trajectory conversion.

- **Severity:** ValidityViolation
- **Raised by:** sequenced `run`
- **When:** Always on that conversion; Maxwellian at the gas temperature plus the local drift.
- **What to do:** Valid only while the ions are in the gas that thermalised them.
- **Source:** `Einzel.Transport/PacketConversion.cs:243`

## Studies: sweep, scan, boundary, optimiser

### boundary.below-acc6

The boundary is resolved more coarsely than ACC-6's 1 in 500.

- **Severity:** Qualified
- **Raised by:** `boundary`
- **When:** `resolved > AccuracyTarget`.
- **What to do:** Tighten the resolution setting or accept the coarser boundary.
- **Source:** `Einzel.Sweeps/BoundarySearch.cs:251`

### boundary.budget-exhausted

The search stopped short of the requested resolution.

- **Severity:** ValidityViolation
- **Raised by:** `boundary`
- **When:** Resolution not met within the evaluation budget; usually a non-monotone predicate.
- **What to do:** Scan the range to see the shape.
- **Source:** `Einzel.Sweeps/BoundarySearch.cs:238`

### boundary.multiple-crossings

The figure crosses the threshold again outside the converged bracket.

- **Severity:** ValidityViolation
- **Raised by:** `boundary`
- **When:** The confirmation walk found a second crossing.
- **What to do:** Run `einzel scan` over the parameter and bracket one edge at a time.
- **Source:** `Einzel.Sweeps/BoundarySearch.cs:214`

### boundary.single-crossing-checked

The single-crossing premise was probed.

- **Severity:** Provenance
- **Raised by:** `boundary`
- **When:** Probes outside the bracket all agreed (reported with the probe count).
- **What to do:** Informational; consistent with, not proof of, one crossing.
- **Source:** `Einzel.Sweeps/BoundarySearch.cs:227`

### DRAWS_FAILED

Some tolerance-study draws produced no figure of merit.

- **Severity:** ValidityViolation
- **Raised by:** `sweep`
- **When:** `values.Length < attempted`; the distribution describes survivors and is biased toward geometries that work.
- **What to do:** Find which draws fail (usually a perturbation outside buildable geometry) and narrow tolerances or bounds.
- **Source:** `Einzel.Sweeps/ToleranceStudy.cs:346`

### DRAWS_FEW

Fewer than 100 draws.

- **Severity:** Qualified
- **Raised by:** `sweep`
- **When:** `values.Length < 100`.
- **What to do:** Raise the draw count.
- **Source:** `Einzel.Sweeps/ToleranceStudy.cs:355`

### optimiser.budget-exhausted

The optimiser stopped without meeting both convergence tests.

- **Severity:** Qualified
- **Raised by:** `optimise`
- **When:** `!converged`; the message says which test was met.
- **What to do:** Raise the budget or loosen the open tolerance.
- **Source:** `Einzel.Sweeps/Optimisation.cs:446`

### optimiser.failed-evaluations

Some evaluations produced no figure of merit.

- **Severity:** Qualified above 25% failures, else Advisory
- **Raised by:** `optimise`
- **When:** `Failures > 0`; the search treated them as very poor designs.
- **What to do:** Check which designs fail and tighten the box.
- **Source:** `Einzel.Sweeps/Optimisation.cs:462`

### optimiser.optimum-at-bound

The optimum sits on a bound.

- **Severity:** Qualified
- **Raised by:** `optimise`
- **When:** One or more variables ended at the box edge.
- **What to do:** Widen the bound to find where the objective turns.
- **Source:** `Einzel.Sweeps/Optimisation.cs:482`

### parameters.fitted

Some declared parameters were fitted.

- **Severity:** Provenance
- **Raised by:** `run`
- **When:** Parameters with provenance `fitted`; agreement with what they were fitted against is not evidence.
- **What to do:** Do not cite agreement with the fit target as validation.
- **Source:** `Einzel.Commands/RunCommand.cs:945`

### parameters.guessed

Some declared parameters are guesses.

- **Severity:** Provenance
- **Raised by:** `run`
- **When:** Parameters with provenance `guess`; the result is about an assumed geometry.
- **What to do:** Replace with published values when found.
- **Source:** `Einzel.Commands/RunCommand.cs:942`

### scan.no-figure-anywhere

No scan point produced a figure of merit.

- **Severity:** ValidityViolation
- **Raised by:** `scan`
- **When:** All rows empty.
- **What to do:** Check the model at one point with `einzel run`.
- **Source:** `Einzel.Sweeps/ParameterScan.cs:428`

### scan.outside-declared-bounds

A scan end lies outside the parameter's declared bounds.

- **Severity:** Qualified
- **Raised by:** `scan`
- **When:** `!parameter.IsWithinBounds(end)`; those rows will have no figure.
- **What to do:** Keep the range inside the declared bounds, or widen them.
- **Source:** `Einzel.Sweeps/ParameterScan.cs:355`

### study.dry-run

Nothing was computed; this is what the study would do.

- **Severity:** Qualified
- **Raised by:** study verbs with `--dry-run`
- **When:** `--dry-run`.
- **What to do:** Re-run without `--dry-run`.
- **Source:** `Einzel.Commands/StudyCommand.cs:883`

## Rendering and animation

### animation.past-arrival

The declared mapping outlives the flight.

- **Severity:** Provenance
- **Raised by:** `render animation`
- **When:** The declared span exceeds the flight by more than 0.1%.
- **What to do:** Shorten the mapping.
- **Source:** `Einzel.Render/AnimationRenderer.cs:156`

### animation.stops-short

The declared mapping ends mid-flight.

- **Severity:** Provenance
- **Raised by:** `render animation`
- **When:** The declared span is below 99.9% of the flight.
- **What to do:** Extend the mapping if unintended.
- **Source:** `Einzel.Render/AnimationRenderer.cs:168`

### render.density-at-instant

States the instant a density is drawn at.

- **Severity:** Provenance
- **Raised by:** viewport
- **When:** Always for a density picture.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/ViewportCommand.cs:927`

### render.density-empty

The density had nothing left to draw.

- **Severity:** Provenance
- **Raised by:** `render section`
- **When:** No density levels at the end of the run.
- **What to do:** Draw an earlier instant with `--at-us`.
- **Source:** `Einzel.Render/SectionRenderer.cs:435`

### render.extruded-depth

A cross-section's electrodes are drawn to an invented depth.

- **Severity:** Provenance
- **Raised by:** viewport, `render still`
- **When:** A translational solve with electrodes.
- **What to do:** Informational: the depth is a drawing convention (GRD-12).
- **Source:** `Einzel.Commands/ViewportCommand.cs:1191`

### render.field-at-instant

A driven field is drawn at one instant.

- **Severity:** Provenance
- **Raised by:** `render section`, animation
- **When:** The field is time-varying.
- **What to do:** Informational; set `atSeconds` in the spec.
- **Source:** `Einzel.Render/SectionRenderer.cs:324`

### render.first-species

A frame draws only the first of several populations.

- **Severity:** Provenance
- **Raised by:** viewport watch
- **When:** More than one species.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/ViewportCommand.cs:585`

### render.mixed-sequence

Only the diffusive phases of a mixed sequence are drawn.

- **Severity:** Provenance
- **Raised by:** viewport
- **When:** The model has trajectory mode and phases.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/ViewportCommand.cs:274`

### render.no-density

There is no density to draw.

- **Severity:** Provenance
- **Raised by:** viewport
- **When:** Raised at three sites: the sequence ends in trajectories; the transport refused; the run's density is below the drawing floor.
- **What to do:** Name an instant inside a diffusive phase, or check the refusal.
- **Source:** `Einzel.Commands/ViewportCommand.cs:792,837,913`

### render.no-path

No ion produced a drawable path.

- **Severity:** Provenance
- **Raised by:** viewport
- **When:** `paths.Count == 0`.
- **What to do:** Check where the ions fail.
- **Source:** `Einzel.Commands/ViewportCommand.cs:326`

### render.no-trajectories

Trajectories are not drawn for a diffusive model (RND-8).

- **Severity:** Provenance
- **Raised by:** `render section`, viewport
- **When:** The mode computes a density. Raised at three sites.
- **What to do:** Read the density contours or shells instead.
- **Source:** `Einzel.Commands/ViewportCommand.cs:256,459; Einzel.Render/SectionRenderer.cs:456`

### render.phase

The frame's phase.

- **Severity:** Provenance
- **Raised by:** viewport watch
- **When:** The phase count is known.
- **What to do:** Informational.
- **Source:** `Einzel.Commands/ViewportCommand.cs:575`

### render.plane-misses-conductors

The section plane intersects none of the conductors.

- **Severity:** Provenance
- **Raised by:** `render section`
- **When:** No declared conductor intersects the plane.
- **What to do:** Use a spec `plane` block to move onto the metal.
- **Source:** `Einzel.Render/SectionRenderer.cs:368`

### render.sequence-truncated

The viewport drew only part of a sequence.

- **Severity:** Provenance
- **Raised by:** viewport
- **When:** Fewer phases walked than declared.
- **What to do:** Name a later instant or use `render section --at-us`.
- **Source:** `Einzel.Commands/ViewportCommand.cs:811`

## Extensions

### COST_GATE_REFUSED

An operation exceeded a cost or time limit.

- **Severity:** Error (exits 1 as an error; see Inconsistencies in the report)
- **Raised by:** Extension runner
- **When:** Only raised when an extension call exceeds `resources.timeoutMs`. The `estimate` cost gate (exit 3) does not use this code.
- **What to do:** Raise `resources.timeoutMs` if the extension genuinely needs it; an extension gets one call per run (EXT-4).
- **Source:** `Einzel.Extensions/SubprocessRunner.cs:254`

### ENGINE_PIN_MISMATCH

An extension's declared engine range excludes the running engine.

- **Severity:** Error (exits 1)
- **Raised by:** Extension objectives (`ext:` figures in studies)
- **When:** `ExtensionCatalogue.Incompatibility` reports the installed extension incompatible with this engine version.
- **What to do:** Widen the extension's engine range after checking it still works, or pin the engine it was written against.
- **Source:** `Einzel.Commands/ExtensionObjective.cs:125`

### extension.attributed

The value was computed by an extension, not the engine.

- **Severity:** Provenance
- **Raised by:** extension results
- **When:** Always.
- **What to do:** Informational.
- **Source:** `Einzel.Extensions/SubprocessRunner.cs:311`

### extension.isolation-incomplete

The sandbox does not confine network, filesystem or memory.

- **Severity:** ValidityViolation
- **Raised by:** every sandboxed extension result
- **When:** Always for a sandboxed extension.
- **What to do:** Treat the extension as code you have read.
- **Source:** `Einzel.Extensions/Sandbox.cs:83`

## Project, verify and report

### project.nothing-run

No model has a stored result.

- **Severity:** Provenance
- **Raised by:** `project`
- **When:** Models exist but none was ever run.
- **What to do:** Run `einzel run`.
- **Source:** `Einzel.Commands/ProjectCommand.cs:179`

### project.orphaned-results

Stored results name a model that is no longer present.

- **Severity:** Qualified
- **Raised by:** `project`
- **When:** Orphans exist; discarding `results/` would lose them.
- **What to do:** Keep or restore the model.
- **Source:** `Einzel.Commands/ProjectCommand.cs:189`

### report.manifest-without-result

Runs stored a manifest but no result document.

- **Severity:** Qualified
- **Raised by:** `report`
- **When:** `withoutResult > 0`.
- **What to do:** Re-run the model.
- **Source:** `Einzel.Commands/ReportCommand.cs:332`

### report.nothing-run

No stored runs to report.

- **Severity:** Provenance
- **Raised by:** `report`
- **When:** No runs.
- **What to do:** Run `einzel run`.
- **Source:** `Einzel.Commands/ReportCommand.cs:317`

### report.result-unreadable

A stored result does not read back.

- **Severity:** ValidityViolation
- **Raised by:** `report`
- **When:** `unreadable > 0`; a defect in the reading surface.
- **What to do:** Report it.
- **Source:** `Einzel.Commands/ReportCommand.cs:365`

### report.run-interrupted

A run left a checkpoint and no result.

- **Severity:** Qualified
- **Raised by:** `report`
- **When:** A `.progress.json` without a result.
- **What to do:** Give the run longer, a coarser mesh, or a machine nobody will reboot.
- **Source:** `Einzel.Commands/ReportCommand.cs:348`

### report.study-not-rendered

Stored study answers are not drawn by the report.

- **Severity:** Advisory
- **Raised by:** `report`
- **When:** `notDrawn > 0`.
- **What to do:** Read them via the study verbs' `--json`.
- **Source:** `Einzel.Commands/ReportCommand.cs:380`

### result.preview-tier

A preview-tier result.

- **Severity:** Provenance
- **Raised by:** `preview`
- **When:** Always; no convergence study behind it.
- **What to do:** Run `einzel run` for a quotable result.
- **Source:** `Einzel.Commands/PreviewCommand.cs:146`

## Field probe

### probe.field-at-instant

A driven or sequenced field was probed with no instant given.

- **Severity:** Provenance
- **Raised by:** `probe`, `export --composite`
- **When:** The field varies with time and no `--at-us` was given, so every value is the field at t = 0 us.
- **What to do:** Give `--at-us` to choose the instant.
- **Source:** `Einzel.Commands/ProbeCommand.cs:442` (used by `Einzel.Commands/ExportCommand.cs:199`)

### probe.inside-conductor

Some probed points lie inside a conductor.

- **Severity:** Qualified
- **Raised by:** `probe`
- **When:** At least one sample has status `inside-conductor`; the message gives the count. The potential there is the electrode's and the field is the interpolant's, which is not a physical field inside metal.
- **What to do:** Read those rows as the electrode's potential only; move the point or line into the vacuum if a field is wanted.
- **Source:** `Einzel.Commands/ProbeCommand.cs:704`

### probe.outside-domain

Some probed points lie outside every element's domain.

- **Severity:** Qualified
- **Raised by:** `probe`, `export --composite`
- **When:** At least one sample has status `outside-domain`: outside every solve (a reflected solve's mirror image and a tilted cross-section's rotated extent included) and every bounded analytic element's region. The value reported there is what the field returns outside what was solved or declared - not a solved value, not a measurement of vacuum, and not necessarily zero. It wins over `inside-conductor`, since a declared electrode can run on past its solve.
- **What to do:** Probe inside a solve domain or an analytic element's region, or enlarge the domain.
- **Source:** `Einzel.Commands/ProbeCommand.cs:714`

### probe.refused

The field refused some probed points.

- **Severity:** Qualified
- **Raised by:** `probe`
- **When:** At least one sample has status `refused` (the field declined to answer, for example on a singular axis); the row's note says why.
- **What to do:** Read the row's note; move the point off the singular place.
- **Source:** `Einzel.Commands/ProbeCommand.cs:724`


## Internal

### INTERNAL_ERROR

A defect in the platform.

- **Severity:** Error; exits 6
- **Raised by:** Any verb; the process boundary
- **When:** An unexpected exception reaches the top (type and stack are printed); or an extension cannot be started, exits non-zero, or no Python 3 is found.
- **What to do:** Report it with the command and trace. For extension cases run `einzel doctor` to see which interpreter was found.
- **Source:** `Einzel.Cli/Program.cs:86; Einzel.Extensions/SubprocessRunner.cs:233,274; Einzel.Commands/ExtensionObjective.cs:136; Einzel.Commands/FiguresOfMerit.cs:521; Einzel.Commands/ExtensionCommand.cs:157`
