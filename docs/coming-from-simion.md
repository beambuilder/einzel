# Coming from SIMION

This page maps SIMION's concepts onto Einzel's for someone who already designs ion
optics in SIMION. Each row of the table has a short section below it saying what
differs. It is blunt in both directions: what Einzel does not have, and what it
does that SIMION does not.

Two things to know first.

- **There has been no cross-validation against SIMION.** The project has no SIMION
  licence. Einzel's numbers are checked against closed-form solutions, published
  instrument results, refinement studies and conservation laws instead. See
  [Validation](validation.md).
- **Einzel is driven from files and a command line.** A project is a folder of JSON
  documents. The `einzel` command validates, solves, flies, scans and draws them.
  There is a desktop window, but it is young, and nothing exists only in it. If you
  have not used Einzel yet, start with the [tutorial](tutorial-first-lens.md).

## The map

| SIMION | Einzel | What differs |
| --- | --- | --- |
| Potential array (`.PA`), Refine | A `solved2d` or `solved3d` field element; `einzel solve` | Multigrid with cut-cell boundaries; solved on every run from the declared geometry |
| `.PA#` / `.PA0` and Fast Adjust | Basis superposition, inside a run | Used for RF, sequences and ramps; no stored array reused across runs |
| `.GEM` files, Modify | Electrodes declared as primitives with parametric expressions | No CSG, no drawing, no STL/CAD import |
| Workbench (`.IOB`), array instances | A list of field elements, superposed in one coordinate frame | No placing, rotating or scaling a solved array |
| Symmetry and mirroring | `cylindrical` or planar 2-D, full 3-D, Neumann edges, `reflectAboutX` | Similar coverage |
| `.FLY2` / `.ION` particle definitions | The `ion` and `source` blocks, with an optional `cloud` | One source and one ion species per trajectory run |
| Fly'm | `einzel run` | Answers carry an interval, warnings and a manifest |
| Data recording | `results/*.json`, `--json`, `.vtu`, `.vti`, `.csv` | Fixed outputs rather than user-chosen events |
| User programs (Lua), time-varying voltages | Declared drives, sequences and ramps; Python extensions for whole-run objectives | No per-step user code, by design |
| HS1, SDS, drag | `hardSphere` and `langevin` collisions; a drift-diffusion density mode | No simple drag term; a separate transport mode above about 1e-2 mbar |
| Charge repulsion (beam, Coulomb, factor), Poisson space charge | `direct` pairwise sum or `pic` for a packet; `meanField` for a density | No continuous-beam mode; no image charges in the electrodes |
| Magnetic arrays | None | No magnetic fields of any kind |
| Dielectrics | None | Conductors in vacuum or gas only |
| PE view, contours | `einzel render section` (SVG/PDF), `render still` (PNG), the shell's viewport | Vector figures; no potential-energy surface view |
| Trajectory quality (T.Qual) | Adaptive Dormand-Prince 5(4), tolerance ladder, energy drift | Tolerance-controlled, with the interval reported |
| Simplex optimiser, batch mode | Study files: `scan`, `sweep`, `boundary`, `optimise` (Nelder-Mead, CMA-ES) | Every verb is batch; studies are declarative files |
| STL / CAD import | None | Geometry is declared primitives only |

## Potential arrays and Refine

In SIMION you build an array of grid points, mark which are electrodes, and Refine
relaxes the potential over the rest.

In Einzel a solved field is one element of the model's `fields` list: `solved2d`
for a cross-section or an axisymmetric half-plane, `solved3d` for a volume. You
declare the domain, a cell size and the electrodes. `einzel solve` solves it and
reports cycles, convergence factor, residual and peak potential; `einzel run`
solves it again before flying. See [Model format](model-format.md#solved-fields).

What differs:

- **Electrode surfaces are not snapped to the grid.** Einzel uses a cut-cell
  (Shortley-Weller) discretisation: where a surface falls between two nodes, the
  stencil uses the exact fractional distance. A planar boundary swept across a cell
  solves to 3e-10 of the applied potential at every offset, and curved boundaries
  converge at second order (measured orders 2.00 and 1.95 on a coaxial pair). A
  sub-cell change in an electrode position therefore changes the field, which
  matters for tolerance studies. See [Numerics](numerics.md).
- **The solver is geometric multigrid**, with Galerkin coarse levels chosen from
  the geometry when they pay. Cycle counts are reported and do not grow with the
  mesh on the geometries measured.
- **Interpolation is bicubic or tricubic** with continuous first derivatives.
  Trilinear interpolation is not used on a trajectory path.
- **Each axis rounds its interval count up to a power of two.** The mesh is never
  coarser than requested, and cost changes in steps. `einzel estimate` reports the
  step.
- **Mesh error is your job to check.** A run's interval covers the integrator, not
  the mesh. Refine the cell size and compare, as the tutorial does.
- **Meshes have limits.** A 3-D mesh is capped at 64 million nodes, refused at
  validation with the finest cell size that would fit.

## Fast Adjust (`.PA#`, `.PA0`)

SIMION's fast-adjust arrays store one solution per electrode so voltages can be
changed without re-refining.

Einzel uses the same linearity internally, as basis superposition. Electrodes whose
potentials move together share one basis solve, and the field is a weighted sum.
That is how RF works (the weights are functions of time), how a sequence switches
voltages between phases, and how a ramp sweeps them, all without re-solving inside
a run. A four-rod quadrupole is one basis solve; a 48-ring funnel is two.

What differs: **nothing is cached between runs.** Each `einzel run`, and each
evaluation of a scan or optimisation, compiles the model and solves its field
again. There is no saved `.PA0` to fast-adjust from the command line. For 2-D and
small 3-D models a solve takes well under a second to seconds; for large 3-D models
it is the dominant cost, and `einzel estimate` tells you how much.

## Geometry: `.GEM` files and Modify

SIMION geometry files describe electrodes with solids, combined with CSG operations,
and can import STL.

Einzel electrodes are primitives declared in the model file:

- **2-D:** `rectangle`, `disc`, `polygon` (any closed outline, with vertex runs so a
  hyperbola is written once) and `edgeProfile` (a domain edge whose potential varies
  piecewise-linearly along it, for printed-circuit mirrors).
- **3-D:** `box` (optionally tilted), `sphere`, `cylinder`, `prism` (a polygon
  extruded along an axis) and `revolve` (a polygon swept round an axis).
- **`repeat`** stamps out an electrode N times with its index available to every
  expression, so a 200-ring stack is one declaration.

Every coordinate and potential can be an expression over named parameters with
units and bounds (`"boreRadius + wallThickness"`). Change one parameter and
everything that depends on it moves; a scan or optimiser can vary it.

What Einzel does not have: **no CSG** (no union, intersection or subtraction of
solids), **no STL, STEP or other CAD import**, no graphical geometry editor, and no
ideal transparent grids or meshes (a fine mesh cannot be declared as an electrode).
Two conductors that overlap at different potentials are refused at validation. See
[Model format](model-format.md) and [Extending](extending.md).

## Workbench and array instances

SIMION's workbench places, rotates and scales potential array instances in a
common volume.

Einzel's `fields` list is superposed in **one coordinate frame**. Each solved
element has its own domain and contributes nothing outside it, so two solved
regions side by side compose correctly. An analytic element (uniform field, ideal
mirror, ideal RF quadrupole, quadro-logarithmic field) can be limited to a box with
`region`, optionally with a linear `fringe`.

What differs: there is **no instance transform**. You cannot solve an element once
and place, rotate or scale copies of it. Each element is declared where it sits.

## Symmetry and mirroring

| SIMION | Einzel |
| --- | --- |
| Planar 2-D | `solved2d` with no `symmetry`: a cross-section, invariant along z |
| Cylindrical 2-D | `solved2d` with `"symmetry": "cylindrical"`: x is the axis, y the radius |
| 3-D | `solved3d` |
| Mirror planes | `neumann` on a domain edge or face; `reflectAboutX` to reflect a solved field and superpose it |

The cylindrical operator is written in conservative form with the correct factor on
the axis, and is checked against the Bessel-function penetration into a tube.

## Particle definitions: `.FLY2` and `.ION`

In Einzel the `ion` block gives `massToCharge` (in Da) and `chargeNumber`. The
`source` block gives a position, a direction and an `accelerationPotential`, which
is an energy, not a speed. An optional `cloud` adds an ion count, a seed, a
temperature, transverse and longitudinal Gaussian widths and an energy spread. A
packet may start at rest for a pulsed extraction. See
[Model format](model-format.md#the-source-cloud).

What differs:

- **One source and one detector plane per model.** A detector is an infinite plane
  with a normal; electrodes also stop ions and every loss is named by electrode.
  There is no list of separate particle groups.
- **One ion species per trajectory run.** To compare masses, make `massToCharge` an
  expression over a parameter and scan it. Several `species` at once exist only in
  the diffusive density mode.
- **No angular divergence setting.** A thermal cloud already has one; declaring both
  would say the same thing twice.

## Fly'm

`einzel run models/x.json` solves and flies. What you get that Fly'm does not give
you:

- **Every number has an interval and says where it came from.** The flight time is
  repeated at three integrator tolerances and the spread is reported. Several
  ensemble figures (an arrival-time width, for example) carry a bootstrap interval
  over the ion cloud.
- **Warnings travel with the number and cannot be switched off.** They are graded
  `Advisory`, `Qualified` and `ValidityViolation`, and appear on the terminal, in the
  JSON, in exported files and on figures. See [Warning codes](warning-codes.md).
- **A manifest records how the result was made**: the model hash, engine version,
  solver-behaviour version, seeds and machine. `einzel verify` tells you which
  stored results no longer match their model.
- **Regime checks.** With a gas declared, every run reports the Knudsen number,
  mean free path and collisions per flight, and gives a non-suppressible warning
  (and exit code 2) when trajectory integration is used outside its validity.

Exit codes distinguish validation failures, regime violations, cost-gate refusals,
convergence failures and internal errors. See [CLI](cli.md).

## Data recording

SIMION's data recording lets you choose which quantities to write at which events.

Einzel writes a fixed set of outputs (see [CLI](cli.md#files-a-run-writes)):

- `results/<name>.result.json`: every figure of merit with its interval, evidence
  and warnings, plus the final position.
- `results/<name>.manifest.json`: provenance.
- `--json` on any verb: the same as machine-readable output, including a per-ion
  event list (outcome, surface struck, time, end position) for a cloud.
- `--vtu`: the sampled trajectory as a VTK file for ParaView. `sampleInterval` sets
  its cadence.
- `einzel export`: the solved field as VTK ImageData (`.vti`).
- Diffusive runs: the density as `.vti` and the arrival profile as `.csv`.

What differs: you cannot add your own recording events or quantities. If you need
something not written, it has to come from those files.

## User programs and time-varying voltages

SIMION user programs (Lua) run code at points during a flight: changing voltages
each time step, adding forces, killing ions, recording data.

Einzel has **no per-step user code, by design.** Extensions are Python programs
that take a whole input and return a whole output, once per run, in a sandboxed
subprocess. Their main use today is as a study objective (`"figureOfMerit":
"ext:name"`), so an optimiser can drive a figure you compute yourself from the
model's parameters and built-in figures. See [Extensions](extensions.md).

Time-varying voltages are declared instead of programmed:

- **Drives:** a sinusoidal or rectangular (with duty cycle) generator on a solve,
  each electrode tapping it with a signed amplitude and a phase. Several named
  generators can drive one geometry. Phase can be an expression, which gives a
  travelling wave.
- **Sequences:** ordered phases, each with a duration, setting parameters. A phase
  can change the transport mode (for example a gas-filled trap, then a vacuum
  flight).
- **Ramps:** a phase can ramp a parameter linearly, which gives a mass scan or a
  TIMS elution ramp.

What Einzel does not have: arbitrary waveforms in the model format (only sinusoid
and rectangular), voltages that respond to where an ion is, and custom forces.

## Collisions: HS1, SDS and drag

Einzel's trajectory mode has two event-driven collision models, set in
`transport.gas`:

- `hardSphere`: elastic collisions off a declared cross-section, for residual gas
  up to about 1e-5 mbar.
- `langevin`: polarisation capture from a declared polarisability, up to about
  1e-2 mbar.

Above about 1e-2 mbar Einzel switches description rather than slowing down: the
`diffusion` transport mode evolves an ion **density** by drift-diffusion
(Scharfetter-Gummel), with mobility as a declared input (or derived from a cross
section by Mason-Schamp). RF enters as the collisional pseudopotential. No
trajectories are drawn for a diffusive run, because none were computed. A sequence
can switch between the two modes. The gas can carry an imported velocity field and
pressure field (VTK ImageData). See [Pressure](pressure.md).

What differs: there is **no simple viscous drag term** and nothing equivalent to
SDS's random-jump trajectories. Thermalisation, mobility drift and the trajectory
versus density cross-check (`einzel compare`) are measured against closed forms.

## Charge repulsion and space charge

| SIMION | Einzel |
| --- | --- |
| Coulomb repulsion between particles | `"spaceCharge": "direct"`: every pair, with macroparticle weighting |
| Factor repulsion | No equivalent. `ions` (trajectories computed) and `population` (real ions) are separate, so each macroparticle carries the right charge |
| Beam repulsion | No continuous-beam mode |
| Poisson space charge in an array | `"pic"` (particle-in-cell on the packet's own grid) for a packet; `"meanField"` for a diffusive density |

What differs: space charge is for **pulsed packets**, which fly together with a
shared time step. The packet's own field does not induce charge on the electrodes
(no image charges). A cloud run with no space charge still reports an estimate of
the error that neglecting it implies. See
[Model format](model-format.md#space-charge).

## Magnetic arrays and dielectrics

Einzel has **no magnetic fields of any kind** (no magnetic arrays, no uniform B,
no sector magnets) and **no dielectrics**. Electrodes are ideal conductors in vacuum
or gas. If your instrument needs either, Einzel cannot model it today.

## PE views and contours

Einzel's figures come from the model, not from a screenshot:

- `einzel render section` draws a plane through the instrument as **vector SVG or
  PDF**: conductors, equipotentials, the trajectory or density contours, a scale
  bar, provenance, and any warning stamped on the figure.
- `einzel render still` draws the 3-D viewport's picture as a PNG.
- `einzel render animation` writes frames on a declared playback-rate mapping.
- The desktop shell (`einzel-shell`) shows the same viewport interactively, with a
  parameter tree you can edit and undo.

What differs: there is **no potential-energy surface view**, no interactive
geometry editing, and the window is young. ParaView, through `--vtu` and
`einzel export`, is the general-purpose 3-D viewer. See [Rendering](rendering.md)
and [Shell](shell.md).

## Trajectory quality

SIMION's trajectory quality setting trades time steps for accuracy.

Einzel integrates with adaptive Dormand-Prince 5(4) under a relative tolerance
(`transport.relativeTolerance`), lands exactly on detectors, electrode surfaces,
declared field discontinuities, collisions and phase switches, and drifts
analytically through field-free regions. The flight time is computed at three
tolerances and the observed convergence order is reported. Energy drift is reported
on every run against a budget of 1e-6. Steps are also capped by the field's own
grid spacing, so an ion cannot step over a feature.

## Optimisation and batch mode

Every Einzel verb runs from the command line, so batch mode is the normal mode.
Studies are JSON files naming a model, a figure of merit and what to vary
(`einzel schema --study` prints the format):

- `einzel scan`: one parameter across a range.
- `einzel sweep`: tolerance Monte Carlo over declared parameters, with
  one-at-a-time attribution of which parameter binds first.
- `einzel boundary`: bisection onto a stability or transmission boundary.
- `einzel optimise`: Nelder-Mead or CMA-ES over declared parameters, in a
  normalised box, with warnings when the optimum sits on a bound.

Scans and sweeps run their evaluations in parallel. `einzel estimate` gives the
cost of a study before you run it. See [Optimisation](optimisation.md).

## STL and CAD import

Not available. Geometry is the declared primitives listed above. The project does
intend to read mesh formats out of process in a later phase; nothing does today.

## What Einzel has that SIMION does not

- **Cut-cell boundaries** with measured second-order convergence, so sub-cell
  geometry changes are real and tolerance studies are meaningful.
- **An interval and non-suppressible warnings on every reported number**, carried
  into files and figures.
- **Run manifests** and `einzel verify`, so you can tell whether a stored result
  still answers the current model.
- **Studies as files**: scan, tolerance Monte Carlo, boundary bisection, Nelder-Mead
  and CMA-ES.
- **A drift-diffusion density mode** for millibar pressures, cross-checked against
  the trajectory mode where both are valid.
- **A sequencer** with ramps and transport-mode changes between phases.
- **Tests** (`einzel test`) that pin a model's results, and an examples corpus
  checked against closed forms on every change.
- **An agent surface**: every command takes `--json`, and an MCP server
  (`einzel-mcp`) lets an agent share a live model with a person in the shell.
- **Apache-2.0 licence**, no GPL dependency in the default build.
