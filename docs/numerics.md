# Numerics

Everything here is measured on this codebase unless it says otherwise. Where a
number is quoted, the test that produces it is named.

## The accuracy budget

The specification sets these, and they are the reason for most of the design
decisions below.

| | Bound | Why |
| --- | --- | --- |
| ACC-1 | Flight-time error ≤ 1 ppm | 1/25 of the peak-width budget at R = 20,000 |
| ACC-2 | ≤ 0.25 ppm in validation mode | To reproduce published R = 80,000 results |
| ACC-3 | Interpolation ≤ 0.5 × ACC-1 | The usual offender |
| ACC-4 | Energy drift ≤ 1 ppm in a static field | Cheap conserved-quantity diagnostic |

The counter-intuitive one is ACC-3, and it is worth stating plainly because the
instinct on missing a timing target is to reach for a higher-order integrator.
That is usually the wrong lever. An ion crossing a gridded potential accumulates
error from the interpolant's discontinuous derivatives at every cell boundary,
and the sign of that error is set by the direction of travel — so over 10⁵
crossings it accumulates linearly rather than cancelling as a random error would.

We measured it. Flying the same trajectory through an exactly-sampled harmonic
field, varying only the interpolant:

| Grid | Cells crossed | Bicubic (C¹) | Bilinear (C⁰) | Ratio |
| --- | --- | --- | --- | --- |
| 64 | 51 | 6.43e-8 | 9.41e-6 | 146× |
| 128 | 102 | 3.01e-8 | 2.64e-6 | 88× |
| 256 | 205 | 9.27e-9 | 2.70e-7 | 29× |

Bilinear on the coarsest grid is **nineteen times over the entire ACC-1 budget**,
not merely over the interpolation share of it. `SolvedField2D` therefore refuses
a C⁰ interpolant outright; the escape hatch exists only so that the tests above
can measure what it costs.

## Trajectory integration

`TrajectoryIntegrator` is the scalar reference implementation. It is never
deleted or allowed to rot, because every future SIMD or GPU path is tested
against it.

Five mechanisms, each addressing a distinct way a flight time goes wrong.

**Dormand–Prince 5(4) with per-step error control.** Seven stages, a fifth-order
solution with an embedded fourth-order estimate, first-same-as-last so an
accepted step costs six field evaluations. Coefficients are written as exact
rational quotients so the compiler rounds each once.

**Neumaier-compensated time accumulation.** A 192 µs flight in picosecond steps
is ~10⁵ additions of a small increment onto a growing total; naive summation
loses about a bit per doubling of the term count. Neumaier rather than Kahan
because Kahan drops the correction when the incoming term exceeds the running
total, which happens on the first step and again after every analytic drift.

**Analytic advance through field-free regions.** In a multi-reflection analyzer
most of the path is drift, and integrating a straight line numerically
accumulates error for no physics. `FieldFreeRunLength` is a guarantee, not a
hint: a non-zero return asserts the field is identically zero over that whole
run.

**Exact landing on declared discontinuities.** Runge–Kutta assumes the derivative
is smooth across the step, and DP54's stage 4 carries the coefficient −56/15, so
intermediate stage samples fall *outside* the step interval and can land on the
wrong side of a field jump even when both endpoints are inside. Left unhandled
this put the reflectron error on a floor of 5.5e-10 that barely responded to
tolerance; landing on the boundary as an event took the same case to 1.7e-16.

**A resolution cap.** A gridded field carries no information below its node
spacing, so a step may not outrun `ResolutionLength`. Without it, an ion launched
in a field-free region proposes an enormous step, the error estimator correctly
agrees it was accurate *for a straight line*, and the ion sails through the entire
instrument without sampling it. The step was not inaccurate; it was uninformed.

### Measured

Against the closed-form single-stage reflectron, m/z 500 at 4 keV:

- Flight time within **1.3e-13** of the analytic value when the field is solved
  numerically, and ~1e-10 with the idealised discontinuous analytic field
- Energy drift ~1e-9 to 1e-8 against the ACC-4 budget of 1e-6
- Free flight and the smooth uniform-field turnaround at machine precision, 1e-15

`AnalyticTierTests`, `IntegratorBehaviourTests`, `SolvedReflectronTests`.

### The turning-point cap

Spec §11 requires forced step refinement at turning points, on the grounds that
"position-error controllers under-refine" at the velocity minimum. Ours is not a
position-error controller — `ErrorNorm` weights velocity error with its own
absolute floor — and measurement says the cap does not help: in a smooth field
the flight time is at machine precision with 6 steps and marginally *worse* with
105. It is implemented and on by default to honour the spec as written. See
[Spec findings](spec-findings.md).

## Field solving

Geometric multigrid on a uniform Cartesian grid: a Shortley–Weller stencil,
red-black Gauss–Seidel smoothing, full-weighting restriction, bilinear
prolongation, V-cycles, correction scheme.

Red-black rather than lexicographic ordering because each colour sweep is
internally independent, and the architecture puts a SIMD and GPU dispatch layer
under the engine — a smoother that cannot be parallelised would have to be
replaced later.

### Measured

**Second-order convergence**, against a manufactured harmonic solution
Φ = A sinh(ky) sin(kx):

| Intervals | Max error | Observed order |
| --- | --- | --- |
| 16 | 1.281 V | |
| 32 | 0.321 V | 1.996 |
| 64 | 0.0804 V | 1.997 |
| 128 | 0.0201 V | 2.000 |

The reference is deliberately not a polynomial: the five-point Laplacian is exact
up to quadratics, so a polynomial reference would report machine precision at
every refinement and reveal nothing about the order.

**Grid-independent cycle count** for boundary-only geometries — 8, 7, 7, 7 cycles
from 32 to 256 intervals, at a residual reduction near 0.03 per cycle. That
property, not the stencil, is what makes a full basis campaign tractable.

**Basis superposition** matches a direct solve to 1.4e-11 V on a 4800 V scale.
Solving once per electrode at unit potential turns voltage optimisation from a
solve-per-iteration problem into arithmetic. It breaks the moment geometry
changes, which is why tolerance work needs sensitivity fields rather than more
superposition. On real meshes with cut cells it holds to the solver's
convergence rather than to round-off: on the 0.2 mm quadrupole a DC set summed
from group channels differs from its direct solve by at most 3.9 uV of 1500 V
(2.6e-9) and the RF channel by 7.0 uV (5.1e-8), on the two-dimensional lens by
0.37 uV (1.4e-9). A `FieldBasisPlan` builds fields that way when a caller asks
(`docs/library.md`); the default decomposition does not, so `run` keeps its
bits.

## Cut cells

A conductor surface almost never lands on a grid node. Deciding node by node
whether each one is inside or outside — rasterising — is the obvious thing to do
and it costs two quite different things.

**Accuracy.** The boundary is placed at the nearest node, so it is wrong by up to
half a cell. That is a first-order error, on an otherwise second-order scheme,
sitting exactly where the field is usually most interesting.

**Differentiability.** The discrete operator becomes a staircase function of
electrode position. Move an electrode by a fifth of a cell and *nothing* changes;
move it by a cell and everything does. Shape derivatives then measure the
staircase rather than the physics, which is what made the FLD-1 spike fail.

The fix is the Shortley–Weller stencil: a second difference on unequal spacings,

    d2f/dx2 = 2/(h- + h+) [ f-/h- + f+/h+ - f0 (1/h- + 1/h+) ]

so an arm of the stencil may stop at a conductor surface partway to the next
node. With every spacing equal it reduces to the familiar five-point formula, so
there is no separate uniform code path and no arithmetic cost where there is no
cut. `CompiledElectrode.FirstEntry` gives the crossing in closed form per
primitive, and `CutLinks` stores, per node and per direction, how far the surface
is as a fraction of a cell and what potential it holds there.

Two details that are not incidental:

- **Entry, not straddling.** A crossing is found by asking where a segment first
  enters a conductor, not by testing whether its two endpoints are on opposite
  sides. An electrode thinner than a cell lies wholly *between* two nodes, so a
  straddle test reports nothing and the electrode disappears — which is every
  coarse level of a multigrid hierarchy, and is where geometry used to dissolve.
- **Cell units.** The stencil is carried with coefficients of order one and the
  mesh applied exactly once. Folding 1/h² into the neighbour sum scales every
  term by millions while leaving the difference between them — the quantity
  actually wanted — unchanged, and spends precision for nothing.

### Measured

**A planar boundary is where it says it is.** A 20 mm parallel-plate gap on a
0.625 mm mesh, with the plate face swept across a whole cell in twenty steps. The
exact potential is a straight ramp, which any consistent second-difference
stencil reproduces exactly, so the residue is a direct measure of where the
solver thinks the boundary is:

| | worst error over the sweep |
| --- | --- |
| Cut cells | **3.1e-10** of the applied potential — solver tolerance |
| Snapped to the nearest node | up to 1.6e-2 |

A factor of fifty million, and more to the point the error no longer depends on
where in the cell the boundary fell. The second differences of a probe potential
across the sweep run 5.4e-3 to 6.2e-3 V and vary monotonically; a staircase shows
up there as alternating spikes.

**A curved boundary converges at second order.** A rod of 5 mm radius in a
40 mm box whose edges carry the analytic coaxial potential A ln r + B, which is
then the exact solution over the whole annulus:

| Intervals | h | Max error | Observed order | Cycles | Factor |
| --- | --- | --- | --- | --- | --- |
| 64 | 0.625 mm | 2.30e-4 | | 7 | 0.019 |
| 128 | 0.3125 mm | 5.76e-5 | 2.00 | 8 | 0.022 |
| 256 | 0.1562 mm | 1.49e-5 | 1.95 | 8 | 0.023 |

Section 19 asks for coaxial fields against closed form. That check was not
available before, because a rasterised circle is a staircase and the comparison
would have measured the staircase rather than the solver.

### The one approximation left

A surface passing arbitrarily close to a node gives an arbitrarily small spacing
on one side of the stencil, and the coefficient grows as its reciprocal. Nothing
breaks mathematically — the operator stays an M-matrix and Gauss–Seidel still
converges — but a single node carrying a coefficient millions of times its
neighbours' dominates the residual norm, and a convergence test measured against
that norm stops describing the rest of the grid.

So `CutLinks.MinimumFraction` floors the fraction at 1e-3, and the price is a
boundary knowingly moved by at most a thousandth of a cell. In the parallel-plate
sweep above, a floor of 0.05 cost 3.0e-4 of the applied potential when a face
landed 0.04 cells from a node; at 1e-3 the same case costs at most 3.1e-5, and
every position outside that window solves to 1e-11. The window is a thousandth
of a cell wide, so it is not where a shape derivative usually finds itself.

It has not been removed altogether because doing so trades a small bounded
geometric error for an unbounded numerical one, which is the worse of the two.

### A face two conductors share must not be sampled by the mesh

Cut cells make the answer continuous in where **one** conductor's surface sits. They
cannot do that for a face **two** conductors share at different potentials, because
the boundary potential itself jumps there and the mesh samples it at points. Tangency
is legitimate - every segmented chain in the library is written that way, and the
overlap checks allow it on purpose - but when a node, or the point where a stencil arm
first meets metal, lands exactly on the shared face, which conductor it is credited to
is decided by the last bit of the face's arithmetic against the mesh's. A node goes to
one, the other (the one written last), or neither, left free between two arms of
vanishing length and solving to a mixture of both. An arm is cut against whichever
entry rounded nearer and carries that conductor's potential.

**No refinement ladder can see it.** Interval counts are powers of two over a fixed
domain, so a face on node k at one rung is on node 2k at the next, and every rung
tosses the same coin. It was found on `astral-3d` by electrostatic similarity instead
(SPEC.md Amendment 55).

`GeometryBuilder.NodesOnSharedFaces` and `GeometryBuilder3D.NodesOnSharedFaces` look for
it before every solve and put `mesh.node-on-shared-face` (Qualified) on the
`SolveReport`, which `FieldAssembly.BuildReported` carries onto every run, preview and
figure, and `einzel solve` reports once per element. The rules:

- **Two samples, two tests.** A node counts when it is within 1e-9 of the finest
  spacing of both surfaces - the overlap checks' tangency tolerance, seven orders above
  the few ulps by which a face written as an expression and a node written as origin
  plus index times spacing disagree when they coincide. An arm from a free node counts
  when the point where it first meets one conductor is within the same distance of the
  other's surface and nothing is met sooner, strictly inside the arm (an entry at either
  end is a node, and is counted as one).
- **A distance, not a pair of entry fractions.** The obvious arm test - both conductors
  entered at the same fraction - misses the case it exists for: nudge the face one ulp
  and the arm, lying in the face's plane, no longer enters one of the two at all, while
  the cut has flipped to the other. A distance to a surface is continuous in the
  geometry; whether a segment enters a box is not.
- **"Different excitations" is the overlap checks' own reading**, through
  `ElectrodeOverlap.StatesOf`/`Agrees` and their volume twins: the potential and every
  tap, in any state the instrument has, a ramp's far end included.
- **Cheap.** Only pairs that disagree and whose bounding boxes meet are visited, and
  only nodes within a cell of both boxes; a node goes on to the arm test only when it is
  within a cell of both surfaces. On a volume without a mask to hand, whether a node is
  fixed is asked of the geometry node by node, as the finest mask is built, rather than
  by building one - which on `astral-3d` is minutes.

**The arms are not a refinement; they are where the case that found this lives.** The
Astral's foil stripes are 0.715 mm thick against a 2.9 to 3.8 mm mesh and hold no node,
so a check of nodes alone - which is how this was first specified - reports the
template clean with its mesh shift removed. The detector reports **180 arms and no
node**: three foil faces on node planes, four plates, fifteen nodes across each stripe.

**What it was worth, measured by moving the foil mesh** (`meshShiftZ`) across the node,
Release build, flight time in us:

| shift | -1e-2 cell | -1e-3 | -2.6e-5 (1e-7 of the extent) | **0** | +2.6e-5 | +1e-3 | +1e-2 | shipped, 0.10 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| flight | 786.3828 | 786.4319 | 786.4372 | **786.4373, warns** | 784.3191 | 784.3246 | 784.3760 | 784.9047 |

A **2.118 us step, 0.27 percent**, on an ordinary slope of about 5.4 us per cell either
side - five orders larger than each run's own stated uncertainty of about 1e-5 us, which
cannot contain it because it is not a convergence error. The step equals the effect of
moving the mesh four tenths of a cell. At zero shift the rounding happened to land on
the lower side; the compact analyzer's 0.2 landed on the upper, which is the 786.44 against
784.32 that exposed it.

**The survey found nothing else.** Every shipped template and corpus example - 44 solved
elements - is clean, which `SharedFaceCorpusTests` now keeps true, with the zero-shift
Astral as the control that has to trip.

**What it does not cover, stated rather than discovered.**

- **The tolerance flags coincidence, not proximity.** The table is a step: a face 2.6e-5
  of a cell from a node is on a determinate side of it by every arithmetic, draws no
  warning, and still sits on the edge of a 0.27 percent discontinuity. A sweep or a
  sensitivity field that moves a shared face across a node plane crosses the step at a
  point where every evaluation looked clean. The advice in the warning - a tenth of a
  cell - is about that, not about the tolerance.
- **A conductor against a Neumann face is not checked, because it is not a coin between
  two values.** The same full-size and compact masks that differ on those 180 arms also
  differ on **454 nodes and 1,406 further cut links**, every one on the domain's upper z
  face, where the grounded boards' ends coincide with it: the last node plane is
  `origin + 256 * spacing`, which rounds either side of the declared maximum (1.4e-14 mm
  over it in the compact analyzer, exactly on it at full size). That face is Neumann and the
  boards are earthed, so both outcomes sit near zero volts, and the flight does not see it:
  all three full-size runs either side of the step hold those 454 nodes inside the boards -
  checked - so the step is the foil's arms alone, and the +1e-7 run reproduces the compact
  analyzer's 784.32 with the boundary in the opposite state, which bounds the boundary's share
  at about 1e-3 us. A **Dirichlet** face is a third conductor at zero, and that case is
  checked - the next section.
- **An edge profile** has no surface to be near (its signed distance is infinite) and
  takes no part; a profiled board meeting an interior electrode at the edge is decided by
  declaration order, which `ElectrodeOverlap` also leaves aside.

### A grounded face of the domain is a third conductor, and gets the same check

A Dirichlet face is pinned to zero volts node by node, **unless an electrode has already
claimed the node** - so that a plate can reach the edge and hold it (see "Domain edges").
An electrode claims a node by containing it: `Contains`, signed distance at most zero,
against the node's coordinate in a volume, and a `Floor` or `Ceiling` of a quotient for a
rectangle in the plane. So a node on the face that lies on an electrode's *surface* holds the
electrode's potential or zero according to the last bit, and that is common by design: every
ring stack whose rings run out to the outer wall, every tube flush with it, every plate that
holds an edge.

**Measured first, on a 100 V plate flush with a grounded edge** (`GroundedFaceNodeTests`),
its face nudged a trillionth of a cell either way:

| | face a trillionth short | face a trillionth past |
| --- | --- | --- |
| the edge node | 0 V | 100 V |
| every free node's solution | identical, to the bit | |
| potential half a cell off the contact | | **21.875 V** higher |
| potential three cells away | | identical |

**The solve does not see it, and the field does.** Every neighbour of a flipped node is
another edge node or inside the plate, so no free node reaches it through an uncut arm. But
the bicubic stencil of the cells beside the contact reads the edge node, so within about a
cell of where the plate meets the ground the interpolated potential moves by over a fifth of
what the plate holds. A volume is the same: the face nodes flip between 0 and 100 V, the free
nodes agree to the solver's tolerance (1e-9 V, since in 3-D the stopping point moves with the
fixed values), and the potential half a cell off the rim moves 21.9 V.

**Against a conductor lying outside the domain, the coin decides whether it is in the solve
at all.** The arm from the free column beside the edge meets it at exactly the far end, which
the cut links do not record, so the stencil reads the edge node: the plate's potential when
the node is on the plate, zero when the plate is a trillionth of a cell clear. At the center of
the test's domain that is **9.61 V against 0**.

**A Neumann face is not a coin between two values**: a node there flips between fixed at the
plate's potential and free beside it, and left free it solves to **100.0000 V** through a cut
of vanishing length - an ordinary cut cell. It takes no part.

`NodesOnSharedFaces` in both dimensions now counts these as `BoundaryNodes`, under the same
code, `mesh.node-on-shared-face`, with its own sentence and its own fix. The rules:

- **Nodes only.** A face node is fixed either way, and the boundary is never a cut target, so
  no arm can sample it; an arm that reaches a flipped node uncut reads the node's value, which
  is the node's coin.
- **The face is where the mesh puts it**, origin plus index times spacing, not the declared
  bound - which is how the compact analyzer's upper face landed 1.4e-14 mm outside its own
  domain. The test builds a box from 0.002 to 0.018 m whose last node is an ulp outside it.
- **"Holds something other than zero" is the overlap checks' reading**, asked of the electrode
  against itself stripped of its excitation, in every state the instrument has. An earthed
  conductor against a grounded face is harmless; a driven one at zero DC is not.
- **An edge carrying an edge profile is not grounded anywhere** - the profile claims every
  node on it - and takes no part.
- **A face lying in a mirror plane is not a surface** - a Neumann edge or face, the axis of an
  axisymmetric solve included - since by symmetry the conductor continues across it. A solid
  rod is a rectangle from the axis outward and in space the axis is inside it, so where a
  grounded edge meets a mirror the corner node is asked about a point ten tolerances inside
  the mirror: a rod whose end is flush with the edge is flagged there, and a rod carried past
  it is clean, as the warning's advice says it will be.

**The survey: 17 of 44 solved elements trip, all cross-sections** - eleven templates and six
examples. Ring stacks run out to the grounded outer wall (`ion-funnel`, `pnnl-ion-funnel`,
`tims-analyzer`, `tims-front-end`, `tims-tandem`, `travelling-wave-guide`), the Paul trap's
ring truncated at it, the einzel lens's 500 V center tube, the Kingdon wire's two ends, and two
mirrors (`planar-mirror-pair`, `astral-mirror`) whose end cap is a rectangle **with no
thickness, lying in the grounded left edge**. What each is worth was measured by moving **only
the face on the wall** by 1e-7 of the domain's extent each way - the one thing moving the
domain's face changes relative to the conductor - on the same mesh:

| | fifteen elements | the two mirrors |
| --- | --- | --- |
| potential at the contact | 20.9 to 21.9 percent of the applied (2.7 at the Kingdon wire's end) | 97.0 and 99.8 percent |
| more than three cells from any edge | **identical, to the bit** | 79 and 81 percent |
| ten cells from any edge | identical | 44 and 46 percent |
| every figure run - flight time, transmission, confinement, oscillation frequency, on the eleven that fly ions | **identical, to the bit** | transmission 1 against **0** |

So fifteen are harmless: the coin is real and confined to the wall, and no ion goes there. The
four diffusive templates were compared on the field alone, since their sequences run for minutes
to hours, and a density held at a fraction of a millimeter from the axis by RF does not reach a
wall three cells from which nothing moved.
**For the mirrors it decides whether the mirror has an end cap.** The shipped documents write the
cap and the domain's edge as one expression, so the arithmetic lands on the side with a cap,
deterministically - just inside the edge the cap is a cut surface and the flight is the shipped
one to 6e-8 - but 1e-7 of the extent outside it, the cap is not in the solve and the ion is never
reflected to the detector. Nothing is changed in the templates here. The funnels' ring corners
that cross the wall on a line of nodes cannot be cleared without moving geometry, which would
move published numbers; `SharedFaceCorpusTests` pins the seventeen, so a new one, or one gone,
fails until it is measured. The fix for the mirrors is bit-identical and waits on its own change:
a cap meant to *be* the edge is an edge profile.

**Two traps in making the measurement, both worth keeping.**

- **Moving the domain itself moved the mesh.** Interval counts round up to a power of two, and
  the ion funnel's radius is exactly 64 cells (16 mm at 0.25 mm), so growing it by 1e-7 of the
  extent made it 128: the first survey reported interior differences of several volts that were
  a different mesh, not the coin. That is why only the face on the wall moves.
- **Translating whole conductors instead found a second coin, unrelated to the wall.** The funnel's
  first and last rings have their inner faces exactly on node rows (12 mm on row 48, 1.5 mm on
  row 6). A single conductor's face on a node plane is continuous in its *normal* position -
  the nodes on it flip between fixed and free-with-a-vanishing-arm, which is the same thing -
  but the arms **lying in that plane**, from free nodes beside the conductor, are cut at its side
  face when the plane nodes are inside it and graze past to a node a cell away when they are not.
  Moving ring 23 by 6.4e-6 of a cell turned two cuts at 0.46 and 0.077 of a cell into two uncut
  arms, moved the nearest free node by 2.5 percent of its channel, and moved the ion's flight
  time **4.5e-4**. Cut cells make the answer continuous in where one conductor's surface sits
  **only while its faces are off the node planes**. Not checked; recorded as open.

## Interior electrodes

Coarsening assumes it preserves the problem, and with a rasterised boundary that
**failed for interior electrodes** — a rod, an aperture. An electrode occupies a
fixed physical size, so each coarsening halved how many nodes represented it, and
past a few levels it was not represented at all. The coarse grid then solved a
different problem and prolonging its correction back drove the iteration apart:
four discs in a box reached **1e134 V**.

Cut cells fix this, because they change what "represented" means. A surface has a
position at any spacing, whether or not a node happens to fall behind it, so the
coarse mask is *rebuilt from the geometry* rather than projected down from the
fine one — `PoissonSolver2D.Solve` takes a `coarsen` factory for exactly that.
An electrode too small to contain a coarse node still cuts the links around it.

Measured on the coaxial rod above, where the old coarsening limit allowed two
levels:

| | Cycles | Factor |
| --- | --- | --- |
| Limited coarsening (projected mask) | 43–47 | 0.52–0.55 |
| Full coarsening (rebuilt mask) | 7–8 | 0.019–0.023 |

Grid-independent, on interior-electrode geometry, which is what it was supposed
to be all along. The `Einzel.Sweeps` test suite went from 2 m 15 s to 4 s on the
same machine as a direct consequence.

The old floor of 128 interior fixed nodes still applies to a mask that has no
geometry to rebuild from — one assembled node by node, as `BasisFieldSet` does —
because such a mask really does lose a quarter of an electrode per level.
`InteriorElectrodeSolveTests` asserts the maximum principle, that no potential
anywhere may exceed the applied value, which is an exact check rather than a
tolerance and the cheapest possible detector of divergence.

Three approaches were tried and rejected, recorded so they are not re-tried:

- **Agglomerating the mask** (a coarse node is fixed if anything in its 3×3 block
  is) is stable, because growing the Dirichlet set only damps the correction, but
  it grows the electrode by a cell at every level: the convergence factor went
  from 0.075 to 0.31 at 64 intervals and 0.15 to 0.47 at 128.
- **A flat depth floor** stops small grids coarsening at all, and cost the
  32-interval manufactured-solution case its multigrid entirely (factor 0.508).
- **A total fixed-node retention ratio** does not work either: a disc loses three
  quarters of its nodes per level, exactly the rate healthy coarsening produces,
  so the ratio stays flat until the rod disappears and then it is too late. Only
  counting *interior* fixed nodes separates the two cases.

## Independent spacings per axis

A grid carries `SpacingX` and `SpacingY`, and they need not be equal. The
Shortley–Weller stencil already holds a spacing per arm, so the y half is scaled
by (hx/hy)² to bring it into the x cell units the rest of the operator works in.
On a square grid that factor is exactly one, and multiplying by one is exact, so
an isotropic solve is unchanged to the last bit.

The reason to want it is that the alternative was worse. Keeping cells square
meant deriving the y interval count from the aspect ratio, rounding it up to a
power of two, and accepting whatever box that reached — so a 60 × 20 mm domain at
a 1 mm cell needed 21.3 intervals in y, rounded to 32, and was **solved as
60 × 30 mm**. Fifty per cent taller than declared, silently, and nothing checked.

Now each axis rounds its own interval count up to a power of two from the same
requested cell size. Both spacings therefore lie in (cellSize/2, cellSize], which
means the extent is exact, neither direction is ever coarser than asked, and the
worst cell aspect ratio is two to one.

Two to one is comfortable for a point smoother. Well beyond it, error damps
poorly along the coarse direction and the fix is line smoothing or
semi-coarsening — but nothing here can produce that, by construction.

Measured on a deliberately 2:1 grid against the manufactured harmonic solution:

| Grid | hx | hy | Max error | Observed order |
| --- | --- | --- | --- | --- |
| 17×17 | 6.250 mm | 3.125 mm | 32.03 V | |
| 33×33 | 3.125 mm | 1.562 mm | 8.026 V | 2.00 |
| 65×65 | 1.562 mm | 0.781 mm | 2.010 V | 2.00 |
| 129×129 | 0.781 mm | 0.391 mm | 0.503 V | 2.00 |

That measurement is the point of the exercise rather than a formality: a wrong
aspect factor is a wrong Laplacian, the solve converges perfectly happily to the
wrong answer, and only an order measurement notices. A cut boundary running
across y on a stretched grid — the arrangement that would go unnoticed if the
factor were applied to the wrong half — solves to 1.1e-16 of applied.

## Domain edges

A Dirichlet domain edge means the potential is zero **on the edge**, and
`GeometryBuilder` grounds those nodes unless an electrode has already claimed
them. The alternative reading — a ghost node one cell outside the grid held at
zero, with the edge node itself solved — is self-consistent on any single grid,
and it is what the solver did.

It is wrong as soon as there is more than one grid. The ghost sits one cell out
at the fine level, two at the next, four at the next, so every level of a V-cycle
solves a slightly larger domain than the one above it and the correction it
computes is for a different problem. A cap plate in a grounded box diverged to
**1e50 V**.

It went unnoticed because the interior-electrode coarsening limit stopped these
geometries before they reached a second level. The solver fell back on plain
Gauss–Seidel and reported a convergence factor of 0.83 — poor, but not obviously
a bug. Grounding the edge takes the same case to 9 cycles at 0.039.

Electrodes are rasterized first and are not overwritten, so a plate that reaches
the edge holds it. **A plate whose face is flush with the edge holds the nodes on
that face or leaves them grounded on the last bit**, which moves the field beside
the contact by a fifth of the plate's potential and, for a conductor lying outside
the domain against it, decides whether it is in the solve at all - see "A grounded
face of the domain is a third conductor".

A Neumann edge is unchanged: it is a mirror plane, so the ghost node outside it
equals its reflection inside, which is exact at any spacing and coarsens
faithfully.

## Three dimensions

The first solver here with no symmetry behind it. A cross-section assumes the
geometry repeats along the third axis and an axisymmetric solve assumes it repeats
all the way round; this assumes nothing, which is what a segmented quadrupole or
an auxiliary DC wedge needs and what neither of the others can express.

Written **beside** the two-dimensional path rather than generalising it. That path
carries every validated number in this engine - the reflectron at 1.3e-13, cut
cells at 3.1e-10, the coaxial and Bessel closed forms - and refactoring a numerical
core that is known to be right, in order to add a case next to it, is how those
numbers get quietly lost. The duplication is the price and it is the cheaper one.

### What is checked

| | |
| --- | --- |
| Harmonic quadratic, reproduced | **4.3e-13** relative |
| Non-polynomial harmonic, observed order | 1.92 / 1.99 |
| Cycle count at 16 / 32 / 64 intervals | 12 / 13 / 13, factor 0.08 |
| Neumann face against the full solve it mirrors | 1.4e-16 V |
| Curved conductor against the 1/r law | 2.8 V of 100 applied |
| Maximum principle at the nodes | exact |
| Tricubic on a linear field | 3.6e-15 V, gradient 6.2e-12 V/m |

The quadratic is the unusual one and the sharpest. The seven-point Laplacian is
**exact** for a quadratic - its truncation error starts at the fourth derivative,
which a quadratic does not have - so a harmonic quadratic imposed on the faces is
not an approximation converging, it is an identity. Nothing about the operator, the
faces or the multigrid transfers can be wrong and still pass it.

### Coarse levels are node-aligned, and that is what makes them work

Multigrid coarsening does not survive interior electrodes when the coarse levels
carry sub-cell surfaces. An electrode loses seven eighths of its nodes per level
rather than three quarters, and an electrode a fraction of a coarse cell across
produces arms a thousandth of a cell long, whose coefficients are enormous - the
correction that comes back does not converge slowly, it converges somewhere else.
A charged sphere reached **137 V of 100 applied** that way.

The fix is to separate what each level is for. **Cut cells on the finest level,
where the accuracy comes from; node-aligned geometry on every level below, where
only acceleration comes from.** A coarse level that is merely crude is a perfectly
good preconditioner; a coarse level that is ill-conditioned is not. An electrode
too small to contain a coarse node has its nearest node pinned, so it stays present
at the smallest size the level can express rather than vanishing.

| | |
| --- | --- |
| Sphere solve, no coarsening | 13 s |
| Same, node-aligned coarse levels | **783 ms, 9 cycles at factor 0.126** |
| Answer | identical to the digit |

Sixteen times faster for the same field. Worth noting that agglomeration was tried
in *two* dimensions and rejected - it grows the electrode a cell per level and
roughly triples the convergence factor - but that comparison was against a
geometry rebuild that worked. Here the rebuild does not work, and stable-but-cruder
beats correct-but-uncoarsenable by a wide margin.

Galerkin coarsening, building the coarse operator from the fine one rather than
from the geometry again, is still the better answer and is still not done.

**Coarse masks are memoised by grid.** They were being rebuilt at every level of
every V-cycle from geometry that had not changed - for the twelve-rod segmented
quadrupole, over a million `Contains` calls per cycle producing a mask that was
bit-identical every time. The hierarchy is now built once per solve and reused.

**Known cost, not fixed:** `CutLinks3D` allocates twelve doubles per node
unconditionally - a fraction and a potential for each of six arms - which is 104 MB
at the shipped segmented-quadrupole mesh and around 410 MB at eleven cells across
r0. Only nodes adjacent to metal are ever cut, so an index array plus compact
per-cut storage would be about 4 bytes a node instead of 96. It is not done, because
it puts an indirection in the smoother's innermost lookup and that is the hottest
loop in the solver; it should be measured, not assumed.

**A coarse level's Dirichlet *values* do not matter, only which nodes it fixes.** A
V-cycle solves for the error, whose boundary data is zero, and the correction array
starts at zero and is never seeded from the mask. So a coarse level that merges two
differently-driven electrodes across a gap it can no longer resolve is crude, not
wrong: it clamps the error to zero somewhere it should be free, which under-corrects
that neighbourhood and slows the iteration. What is *not* allowed is a coarse cell
larger than the electrode itself - that is a different problem rather than a coarser
one, and its correction points elsewhere. This is why the guard tests a physical
size and not a node count.

**The guard tests the coarsest of the three spacings, not the finest.** Each axis
rounds its own interval count up to a power of two, so a 2:1 aspect ratio is
ordinary here rather than exceptional. Asking `MinimumSpacing` let the shipped
segmented quadrupole descend to a level whose z cell was **4.875 mm against a 4.587
mm rod radius** - the exact condition the guard exists to refuse, passed because one
of the other two axes was still fine enough. A guard that only has to be satisfied
in its best direction is not a guard.

**The interpolant overshoots about 2% just outside a conductor surface**, which is
what a cubic through a step does. Measured at 101.8 V of 100 applied, in a shell
one cell thick where an ion is about to be absorbed anyway. The maximum principle
is therefore a statement about nodes, and is checked there.

### What building the mask costs, and how much of it was garbage

Measuring a volume solve's memory found mask assembly allocating far more than the mask
keeps: **61 kB a node on the C-trap's revolved rods, 4.2 kB on the linear ion trap's
prisms and 289 bytes on the Astral**, against the 105 the mask holds (`SolveMemoryModel`).
That was transient garbage, so it could not move the peak. What it cost in time was not
known, and a build of the linear ion trap had taken about 200 s against 95 s for the C-trap,
which looked as though the mask might dominate.

**Both of those were measured on code that no longer exists.** They came from a branch that
predates the bounding-box cull, which asks each electrode only about the nodes near it; with
the cull the same masks allocate **5,470 bytes a node, 260 and 105**. And the linear ion
trap's build was timed under a live-set sampler that forced a full collection from a second
thread every few milliseconds. On today's code, with no sampler, the two builds take 13 to
15 s and 16 s.

Measured in Release with nothing else running on the machine, timing the mask apart from the
real build (`SolveChannels`: every channel's mask and solve, plus the shared-face check):

| | nodes | channels | mask | build | mask's share | allocated by the mask | gen0 in the build |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `c-trap`, 0.5 mm (revolve) | 1.08 M | 1 | 2.7-3.5 s | 16.1-16.3 s | **19-22%** | 5,470 B/node | 694-697 |
| ...entry tests rewritten | | | **1.6-2.3 s** | **14.1-14.3 s** | **12-16%** | **105.1** | 1-4 |
| `linear-ion-trap-3d`, 0.5 mm (prism) | 1.09 M | 2 | 1.0-1.5 s each | 13.2-14.6 s | 14-22% | 259.5 | 41-43 |
| ...entry tests rewritten | | | 1.0-1.5 s each | 12.6-15.2 s | 16-18% | **105.2** | 1-3 |
| `astral-3d`, 4 mm (box) | 1.12 M | 1 | 0.15-0.30 s | 7.3-8.1 s | 2-4% | 105.3 | 1-2 |
| `segmented-quadrupole`, 0.5 mm (cylinder) | 1.09 M | 1 | 0.07-0.23 s | 4.6-5.0 s | 2-5% | 105.2 | 1-2 |

A range covers every run: seven to nine of the mask per row, across the harness's modes, and
two of the build. The first mask in a process is usually the slowest, since it pays for
compiling and for the operating system handing over 110 MB of fresh pages - the segmented
quadrupole's took 221 ms and the next 73 - and a command-line build is always a first run.
The Astral and the segmented quadrupole have neither prisms nor revolves, so their rows are
the same code before and after.

**On boxes and cylinders the mask is a few percent of a build; on the curved and polygonal
templates it is a sixth to a fifth.** The garbage was in two entry tests. The prism and
revolve `FirstEntry` each gathered their crossings in a new `List<double>`, and the revolve
took each profile edge's quadratic roots from an iterator - an enumerator object per sloping
edge, of 27 edges a profile - and its two cap angles from a new array. They now gather into 1 KiB on the
stack, or into an array borrowed from the shared pool for an outline too long for that (more
than 62 revolved vertices or 126 extruded), and take roots through out parameters. The masks
are **the same bits**: every fixed flag, value, cut fraction and cut potential, at the finest
level and at every coarse level beneath it, with each electrode held at its own distinct
potential so that a swapped winner would show, hashed before and after on all four templates.

**Removing the garbage bought time on the revolve and none on the prism**, which is the part
worth knowing. A revolve entry test made about thirty objects a call, and several times as
many interface calls through the iterators; a prism entry test made one list over a 29-edge
polygon, and the polygon is the cost. So the C-trap's mask fell by a third to a half and its
build by 12 percent, while the linear ion trap's moved by less than its run-to-run spread, its
gen0 count falling from about 42 to 2 without the clock noticing.

**What is left is the cut-link pass, not rasterization.** Building the fine mask with the
coarse levels' builder - the same rasterization, no cut links - splits it:

| | mask | rasterize | cut links |
| --- | --- | --- | --- |
| `c-trap`, before | 3.1-3.3 s | 0.11 s | **96-97%** |
| `c-trap`, after | 1.6-2.0 s | 0.11 s | **93-95%** |
| `linear-ion-trap-3d`, after | 1.0-1.5 s | 0.15-0.16 s | **84-90%** |

So the next lever on a curved template is the number of exact entry tests the cut pass asks
for, not anything they allocate. `MaskAllocationTests` holds the entry tests to zero bytes and
a mask to what it keeps.

## Landing on a switch

A sequencer switches state at known times, and a Runge-Kutta step that spans one
averages two different fields into a single answer - plausible, and wrong.

Unlike a boundary in space this needs **no root-find at all**, because the time is
known in advance. The integrator asks the field when it next switches and refuses
to take a step past it, so it lands on the boundary and the next step starts in the
new state. By default (`transport.edges` absent or `cap`) the derivative the next
step starts from is not recomputed: it is the last stage of the landing step (FSAL),
sampled at `t + h`, which is the switch or within an ulp of it. `NextSwitchAfter`
returns infinity for a continuously driven field, however fast: a sinusoid has no
discontinuity. A rectangular edge is not a switch, and under the default it is
handled by the steps-per-cycle cap and by the error controller rejecting the steps
that straddle it - about 35 rejections per edge, measured below.

### Landing on every edge: `transport.edges: land`

With `"edges": "land"` the integrator also lands on every declared waveform
breakpoint: the edges of a rectangular drive, the breakpoints an expression or a
table declares. Four rules make the landing exact:

- **The instant is the field's own flip.** `ITimeVaryingField.NextEdgeAfter` returns
  the smallest double at which the field's evaluation is past the breakpoint, computed
  with the field's own arithmetic, so one ulp before it the field is on the old level
  and at it on the new one. The step is cut to the earlier of that and the next switch.
- **The landing step samples the near side.** Its two `c = 1` stages (the sixth stage
  and the derivative at the result) are sampled at one ulp before the instant; the state
  still advances by the full cut step.
- **The next step restarts on the far side.** On acceptance the time is set to the
  instant exactly, not to the compensated sum, and the derivative is evaluated there.
  One extra field evaluation per landing; the FSAL value is not reused across the jump.
- **An instant already reached is taken in place.** When the next instant is closer
  than the minimum step, or is the next double after the current time (two taps whose
  flips differ by an ulp; above about 8 ms an ulp exceeds the 1e-18 s minimum step),
  the time is set to it and the derivative re-evaluated without a step. That counts as
  neither an accepted nor a rejected step.

A model with no breakpoints, rectangular drives or stages flies identically under
either value, to the bit (`EdgeLandingTests` pins it on a sinusoidal quadrupole).

**Measured** on the ideal quadrupole with a rectangular drive (100 u, 1 MHz,
r0 = 4 mm, start (1, 0.5) mm at rest) against the exact transfer-matrix solution of
the Meissner equation, rtol 1e-11, error relative to `max(|ref|, x0)` and
`max(|ref|, omega x0)`:

| case | cap error | land error | cap rejected | land rejected | cap steps | land steps |
| --- | --- | --- | --- | --- | --- | --- |
| d 0.5, q 0.5, 1 T | 8.3e-10 | 9.2e-13 | 33 | 0 | 166 | 140 |
| d 0.5, q 0.5, 10 T | 1.6e-8 | 9.2e-12 | 704 | 0 | 1904 | 1373 |
| d 0.5, q 0.5, 100 T | 1.8e-7 | 9.2e-11 | 7372 | 0 | 19168 | 13703 |
| d 0.5, q 0.45, 100 T | 5.4e-8 | 6.3e-11 | 7421 | 15 | 19200 | 13716 |
| d 0.5, q 0.7, 20 T | 3.2e-9 | 1.1e-11 | 1491 | 30 | 4373 | 3296 |
| d 0.3, q 0.4, 5 T | 1.5e-8 | 1.2e-11 | 344 | 4 | 906 | 644 |

The rejections the cap paid at every edge are gone, the steps fall by about 28 per
cent, and the error falls by three orders of magnitude to the tolerance's own level;
it still grows about linearly with the number of periods. At rtol 1e-9 the 100-period
q = 0.5 row is 1.3e-5 under `cap` and 1.5e-8 under `land`. 10^5 consecutive edges
(50 ms) at duty 0.5 and 0.3 reach the flight time exactly without underflow, and the
same chain with a second edge one ulp after every real one takes each twin in place
(100,001 in-place landings, 2.95 million steps against 2.95 million without the twins).

The default stays `cap` until the B.4 validation decides the flip. It has two known
hazards that `land` removes and the default keeps, so that every existing document
computes what it did: a landing that falls one ulp short of a switch leaves the next
query answering the same switch an ulp away (a one-ulp step), and the landing step's
last stage is sampled at `t + h`, on either side of the switch depending on rounding.

A drive written as an expression of the phase or as a table over one period is held
to the same rule. An expression is compiled once into a function over doubles and
called at every step, fifty to seventy-five times faster than interpreting the
text per step (measured after JIT warm-up), and agrees with the interpreter to the
bit, so an expression of a square wave is the rectangular drive exactly. A table
is a binary search and one cubic per call, linear or the periodic spline. Either
may declare **breakpoints**,
the phases where it jumps or kinks, and today only the cycle mean reads them: it is
an eight-point Gauss-Legendre rule over 1024 panels with the panel edges on the
breakpoints, exact to rounding between them (a square wave's mean is zero to the
bit, a 0.3 duty cycle's within 3.3e-16 of -0.4), while a jump left undeclared lands
inside a panel and costs up to 2/1024 of the mean (7.5e-5 measured at duty 0.3). A
table's mean is its interpolant's exact integral. Under the default the integrator
does not land on breakpoints, and a jump inside a step is bounded by the same 20
steps per cycle a rectangular edge is; under `transport.edges: land` it lands on
every declared one (below). An undeclared jump is landed on by neither.

### Measured

The check is that a sequenced run equals the same flight computed as **two separate
runs stitched together** - the same physics written two ways, needing no closed
form to compare against.

| Integrator tolerance | Disagreement, relative (`cap`) | Disagreement, relative (`land`) |
| --- | --- | --- |
| 1e-8 | 1.0e-8 | 2.3e-7 |
| 1e-10 | 4.6e-8 | 2.0e-8 |
| 1e-12 | 1.3e-9 | 1.0e-9 |

Parts per billion at every tolerance, which is round-off between two different step
sequences rather than the parts per thousand a straddled switch would leave. It is
not monotone, because which steps each route happens to take is luck rather than a
trend - what matters is that the disagreement is at round-off from the start, not
that it shrinks.

Under `land` the gap at 1e-10 halves and the one at 1e-12 falls a little. The 1e-8 row
is not a measure of the switch: against the stitched flight at 1e-12, `cap`, `land`
and the stitched route at 1e-8 are all 6e-7 to 8.5e-7 out, and `cap` agrees with the
stitched route because it takes the same steps up to the switch, while `land` takes 22
steps where `cap` takes 53. At 1e-10, `land` is 1.1e-8 from the tight flight and `cap`
3.6e-8.

Stages that share a spatial pattern share their solve, so a trap that holds at one
voltage and pushes at another costs one basis field, not three.

## RF on solved geometry

Driving a real geometry costs almost nothing beyond solving it once, because
**basis superposition already was the mechanism**. The field is linear in the
applied potentials, so solving at unit potential and then making the weights
functions of time *is* the RF - nothing is re-solved as the drive swings, and the
Poisson equation is never stepped in time at all.

### Channels, not electrodes

SYM-1 makes the point in passing - "a 200-ring funnel driven in two RF phases needs
two RF basis fields plus a DC gradient, not 200 basis solutions" - and it
generalises. Electrodes whose potentials are the same function of time, or exact
negatives of one another, share a basis.

A quadrupole's two pairs are exact negatives, so **four rods reduce to one basis
solve**. The channel's weight swings 500 V to 0 to -500 V across a cycle and the
field is that weight times one solved basis. A q scan, or a mass scan, re-solves
nothing: the amplitude and the mass are weights and an ion, and the channels
come from the field cache, or from memory within one study. A DC scan of a
driven model is different: every DC electrode feeds the one constant supply, so
a new DC level is a new pattern and one solve (see `docs/cli.md`, "Studies").

The grouping is exact rather than approximate: two electrodes share a channel when
their DC and RF parts are equal or both exactly negated, at the same phase - which
is what a real instrument produces, because the electrodes are wired to the same
supply. A tolerance here would silently merge two channels that were meant to
differ, and the field would be plausible.

Grouping is by **spatial pattern**, not by time dependence, and that is what makes
it minimal. Every electrode's potential is first split into the supplies feeding
it - one constant, one per distinct drive phase - so a resistor chain down a funnel
is a *single* supply however many distinct voltages it holds, because what makes a
supply one supply is that its electrodes move **together**, not that they move to
the same place. Then supplies whose applied potentials are exactly proportional
share a solve and carry a weight each.

Measured on the funnel template, which is the device SYM-1 argues from:

| Rings | Electrodes | Basis solves |
| --- | --- | --- |
| 8 | 8 | 2 |
| 24 | 24 | 2 |
| 48 | 48 | 2 |

Two, not three - the two RF phases are exact negatives of one another, so they are
one spatial pattern with one weight. Three phases that were not negatives, as a
travelling-wave guide has, would be three.

### A study's field basis

The one constant supply is the fewest solves for one field and the most for a scan of
DC: a funnel's resistor chain is one channel, and so is a quadrupole's DC, until a study
moves one rod pair against the other and every point becomes a pattern of its own. A
study that sets `"fieldBasis": "varyingElectrodes"` decides from its own points which
electrodes move, and builds every point under a `FieldBasisPlan`: each group of electrodes
that move together (equal potentials at every sampled point, exact doubles) is a supply of
its own - one channel at one volt, weighted by its potential - and the electrodes nobody
moves keep their potentials in one fixed channel, weighted one. The drive's channels are
decomposed as before. The plan comes from the study definition, never from what a cache
holds, so the same study builds the same fields wherever it runs.

Superposition is exact to the solver's convergence, not to round-off: the sum of the
channels differs from the direct solve of the same potentials by the residual each solve
stopped at (3.9 uV of 1500 V on the quadrupole's DC, 0.37 uV on the lens). On the
quadrupole's 31-point tune line every point's transmission equals the default path's, flight
times agree to 8.6e-9 relative and arrival spreads to 1.9e-6, the largest at the stability
edge where a trajectory is most sensitive; a static volume's flight times to 8.0e-9, the lens
to 8.0e-10. A static element under the plan is a time-free weighted sum, so the integrator
steps it as it steps a static field. Because the figures are not bitwise equal, anything
derived from comparing them can move too, not only the figures: on a scan of two plates whose
flight time was flat to fifteen digits the basis moved a warning's count from 6 of 8
evaluations to 5 of 8, and on static plates it moved the interval the scan calls steepest, on
differences of 1e-15.

### Measured

The a-q diagram had been recovered before, but against an analytic field that is
exactly quadrupolar by construction. That tests the integrator and the drive; it
does not test the solver, because there is no solve in it. On four round rods with
a mesh, cut cells and a grounded housing:

| | Low-mass cut-off |
| --- | --- |
| Solved round rods | **q = 0.90525** |
| This engine, ideal hyperbolic field | q = 0.90684 |
| Tabulated Mathieu | q = 0.90804 |

**0.31% below the tabulated ideal**, and in the right direction: round rods carry a
12-pole component a hyperbola does not, and cancelling it at r/r0 = 1.1468 still
leaves the 20-pole and the housing.

That it is the *geometry* rather than a formula is checkable directly - changing the
rod ratio to 1.30 moves the cut-off to 0.89978. A field that had quietly come from
an equation could not do that.

And "unstable" now means something physical. On an ideal field an unstable ion
leaves an aperture the test had to invent; here the rods are solid, so it ends on a
named surface - `rodYPlus`, which is the pair that goes unstable first on the a = 0
line.

## Cylindrical symmetry

SYM-1 asks that a geometry may declare cylindrical symmetry, that the solver
reduce accordingly, and that the interpolant reconstruct the full field
transparently. Section 22 calls it load-bearing for funnels; it is load-bearing
for rather more than that, because **most of section 1's device table is
rotationally symmetric** - einzel lenses, ion funnels, stacked-ring guides,
apertures, drift tubes. A translational cross-section cannot express any of them:
what makes them work is that the electrode wraps all the way round, and the same
declaration in a plane is a pair of bars.

`"symmetry": "cylindrical"` on a solve makes **x the axis of rotation and y the
radius**, with the domain at y >= 0.

### The operator

In cylindrical coordinates the radial part of the Laplacian is
(1/r) d/dr (r dphi/dr), not d2phi/dr2, because a ring of given thickness has less
circumference the closer it sits to the axis. It is written in **conservative
form** - flux through the outer face of a ring minus flux through its inner face,
divided by the ring's own volume - which makes the discrete operator conserve what
the continuous one does, and is stable near the axis where discretising the 1/r
term directly is not.

Face radii are in cells, so the cut-cell machinery carries over unchanged: a node
at radius rho with arms reaching fSouth and fNorth has faces at rho - fSouth/2 and
rho + fNorth/2, and the ring's measure is the difference of their squares.

**On the axis the inner face has zero area**, so no flux crosses it and the ring is
a disc. That limit gives 4(phi_1 - phi_0)/h^2 for a uniform arm - **twice** what a
mirrored plane stencil gives. A solve that treats the axis as an ordinary symmetry
plane is wrong by that factor and converges contentedly.

### The axis is a mirror, and the interpolant has to know

Finding this cost a test failure that looked like nothing. The bicubic stencil
reaches one node outside the grid and fills it by **linear extrapolation**, which is
right at a Dirichlet edge - simply where the data ends - and wrong at a Neumann
edge, which is a mirror plane. Extrapolating across a mirror leaves a spurious
normal field on it. On the axis that is a radial field at a place with no radial
direction, and it read **14 V/m**; an ion launched exactly on axis would have
drifted off it, slowly and plausibly.

Reflecting instead - the ghost is the node one step back inside - takes it to
**exactly zero**. The fix is general: it applies to every Neumann edge, and the
shipped mirror-pair template has one.

### Measured

| | |
| --- | --- |
| Coaxial pair against phi = A ln r + B | 1.3e-3 V of 100 V applied |
| The same geometry against a linear profile | 19.3 V, so the two are far apart |
| Convergence order, 32 to 256 cells | 1.84 / 2.00 / 1.95 |
| Field penetration into a grounded tube, against the first Bessel zero | 2.40503 against 2.404826 |
| The plane operator would give, for the same geometry | pi/2 = 1.5708 |
| Radial field on the axis | exactly 0 |
| Azimuthal field anywhere | exactly 0 |

The coaxial test sounds like it proves nothing, since phi = A ln r + B holds in both
geometries - and that is exactly what makes it sharp. The **plane** solver gets a
linear profile there, so agreeing with the logarithm is precisely what the radial
weighting is responsible for.

The Bessel test is the one that is specific to cylindrical geometry. Inside a
grounded tube the field from an end cap decays along the axis as exp(-j x / R) with
j the first zero of J0 - the radial eigenfunction of the cylindrical Laplacian - so
the decay rate reads out the operator directly. It converges to j01 **from below**,
because the cap's mode coefficients go as 1/(j_n J1(j_n)) and J1 alternates in sign
at its zeros, so the second mode enters negative and slows the apparent decay until
it dies.

### Reconstruction

`AxisymmetricField` wraps the half-plane solve and presents the field in space: an
ion at (x, y, z) is sampled at (x, sqrt(y^2 + z^2)) and the radial field it finds is
pointed back along its own azimuth. Conductors come with it - a rectangle in the
half-plane is a **ring** in space, which is what makes an aperture an aperture
rather than a slot.

One thing is given up: `FieldFreeRunLength` returns zero, so no run is ever taken
analytically. A straight line in space traces a curve in (axial, radial), so a
direction mapped once is only instantaneously right, and the guarantee this returns
is that the field is identically zero over the *whole* run.

## Electrodes stop ions

An electrode used to be a boundary condition on the potential and nothing else, so
an ion flew through a plate as readily as through the hole beside it. That made
every aperture scenery and every transmission figure 100% by construction.

A field that has conductors declares them through `IConductorBounded` as a
**signed distance**, negative inside. That choice is the whole design: an impact is
then the zero of a scalar function along the step, which is the same kind of event
as a stopping surface and is found by the same bracketed root-find - so an ion
lands *on* the surface rather than a step short of it or a step inside it, and
there is no second event mechanism with its own edge cases.

Three things make it sound rather than approximate.

**The chord is safe because the step is already capped.** A gridded field limits
the step to its own cell spacing, so a trajectory cannot arc into an electrode and
back out between two samples: an electrode is many cells thick and the chord and
the arc differ by far less. The cap was added for a different reason - an ion in a
field-free region proposing an enormous step - and it turns out to be what makes
this safe too.

**Order matters.** An electrode is checked ahead of the detector, because an ion
that hits metal on the way did not arrive; and behind a declared field
discontinuity, because an electrode cannot be on the far side of a surface the ion
has not crossed yet.

**A source inside a conductor is refused rather than flown.** Otherwise it reads as
an instrument that loses everything, rather than a model with its source in the
metal.

### Measured

| | |
| --- | --- |
| Transmission through a slit, against erf(a / sigma sqrt 2) | 0.95 sigma at 20,000 ions |
| Impact point against the surface struck | below 1e-8 m |
| Ions accounted for (through, or on a named surface) | exact |

The slit test is the sharp one: with every electrode grounded the field is
identically zero, so the ions fly straight and the fraction that gets through is
the fraction of the launch distribution inside the opening - an error function, and
nothing to do with this code.

## Emittance

The phase-space area a packet occupies, reported per transverse plane about the
packet's own mean velocity. Two forms, and the difference between them is not
cosmetic.

**Geometric**, √(⟨y²⟩⟨y′²⟩ − ⟨yy′⟩²) with y′ = v_y/v_axial, in m·rad. What an
aperture cares about. Root-mean-square rather than a bounding ellipse, because a
bounding area is set by whichever ion strayed furthest and a real distribution has
tails.

**Normalised**, the same area measured against transverse momentum — ⟨y, v_y/c⟩
rather than ⟨y, y′⟩×βγ. Invariant under axial acceleration, which the conventional
βγ form is not; see [Lessons](lessons.md) for the two terms that separate them and
for why the momentum is Newtonian.

A radian is dimensionless, so an emittance has the dimension of length. **1 mm·mrad
is exactly 1 µm**, so the figure of merit is registered in `um` and the number
reads as the conventional unit without conversion.

### Measured

| | |
| --- | --- |
| Thermal packet against σ_x·√(kT/m)/v | 0.77% at 6,000 ions |
| Uncorrelated lattice against (2/3)·σ·δ | exact to 15 figures |
| Preserved through a field-free drift to a plane | 1.5e-14 |
| Preserved through an ideal thin lens | 8.1e-15 |
| Grown by a cubic lens term (spherical aberration) | 1.58x |
| Geometric emittance ratio across a 10 V → 2 kV stage | 0.031606977, closed form 0.031606977 |
| Normalised emittance across the same stage | 3.0e-16 |

The drift and lens figures are Liouville's theorem, and they are a check on the
integrator as much as on the figure of merit — a conserved quantity **independent
of energy**, so it constrains an axis that ACC-4's energy drift does not reach. The
aberration row is the control: a metric blind to nonlinearity would report 1.00x
there and the conservation rows would prove nothing.

## Interpolation

`BicubicInterpolant` is the Catmull-Rom form of bicubic Hermite: node derivatives
are central differences, which makes the result C¹ across cell boundaries.
Measured derivative jump at a cell boundary is 4.6e-4 V/m against bilinear's
3.6e2.

**Grid-boundary stencils must extrapolate, never clamp.** The 4×4 stencil reaches
one node beyond the grid, and repeating the edge node makes the interpolant
non-linear in the boundary cell even when the field is exactly linear. An ion
enters and leaves a mirror through that cell twice per reflection, and a clamped
stencil put **7.5 ppm** into the flight time of a mirror whose exact solution is a
pure ramp — over the whole ACC-1 budget, from the corner case alone. Linear
extrapolation of the ghost node took the same case to 1.9e-10.

## Determinism

Bit-reproducibility across machines is not a requirement for a computed
result; run-to-run reproducibility on one machine is. An ion list is input rather
than output, and its expansion is held to two narrower statements (Amendment 60):
the expanded `.ions.csv` gives the same starting states on any machine, because
its rows are round-trip-formatted numbers; and seeded expansion is compared with
stored golden rows, bit for bit for `uniform`, `sequence` and `linspace` on every
runner, and for the generators that call `Math.Log`, `Math.Sin` or `Math.Cos` bit
for bit on Windows and within 1e-12 relative elsewhere. Where a seeded expansion
differs between machines, the expanded file is the anchor. Golden comparisons use
documented tolerances with stated reasons. `Deterministic` and
`InvariantGlobalization` are set in `Directory.Build.props` so that number
formatting cannot vary with the host locale and leak into CLI output or golden
files.

## A parallel-plate capacitor is the worst 3-D case, and that is the wrong way round

The documented multigrid limitation — coarsening does not preserve interior
electrodes, so the convergence factor degrades — has a concrete cost that is easy to
underestimate, because the geometry that shows it worst is the simplest one anybody
would write.

## Galerkin coarsening, and the choice between two hierarchies

`A_coarse = R A_fine P`. Rediscretising on a coarse grid asks the geometry what it looks
like at that spacing, and past a point the answer is "a different shape" — a 1 mm slab
four levels down is smaller than a cell and gets pinned to a single node. The triple
product never looks at the geometry, so it cannot lose it.

**The finest level is untouched.** It keeps its cut cells and its geometry-driven
smoother, because that is where the accuracy comes from and none of it is in question.
Only the coarse levels change.

### What it bought

Two 1 mm slabs in a grounded box:

| cell | mode | levels | coarsest | cycles | factor | sweeps | wall | peak V |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 0.5 mm | rediscretised | 0 | 274,625 | 6 | 0.015 | 2,412 | 26.2 s | 100.00 |
| 0.5 mm | **Galerkin** | **5** | **27** | 14 | 0.180 | 644 | **2.1 s** | 100.00 |
| 0.25 mm | rediscretised | 1 | 274,625 | 45 | 0.596 | 18,270 | 159.5 s | 100.00 |
| 0.25 mm | **Galerkin** | **6** | **27** | **13** | 0.170 | 650 | **13.4 s** | 100.00 |

**The cycle count stops depending on the mesh** — 14 at 65³ against 13 at 129³, where
before it was 6 against 45. That is the property multigrid is supposed to have and did
not have here on any device geometry.

**And it is the same answer**, which is the assertion that separates this from the fast
wrong one. Deeper *rediscretised* coarsening was thirty times faster and gave 486 V of
100 applied; the two hierarchies here agree to **1.1e-7, 4.0e-7, 2.4e-9 and 7.4e-8**
relative on the four geometries — the tolerance both were driven to. A coarse hierarchy
changes how the fine problem is solved, not what it is.

### Neither hierarchy dominates, so the solver picks

| geometry | rediscretised | Galerkin | ratio |
| --- | --- | --- | --- |
| slabs, 129³ | 159.5 s | 13.4 s | **11.9×** |
| four rods, 65³ | 5.2 s | 1.1 s | **4.6×** |
| a 2 mm sphere, 65³ | 1.1 s | 1.7 s | **0.64×** — a loss |

The sphere is where the cheap hierarchy already reached a small bottom (4,913 nodes), and
there the twenty-seven point stencil and the `R A P` assembly are pure overhead. What
separates the cases is **the size of the bottom level the cheap hierarchy can reach**, so
that is what the choice is made on — and it needs no solve to evaluate. The threshold is
20,000 nodes, which is measured rather than derived: above it the bottom is relaxed by up
to four hundred sweeps over a grid that does not shrink when the mesh is refined.

`SolveReport.Galerkin` says which ran, so the choice is never invisible.

### Two things worth keeping about how it was built

**A 27-point stencil is closed under this coarsening**, which is why the hierarchy needs
one operator type. Restriction reaches one fine cell, the operator one more, prolongation
one more — three fine cells is one and a half coarse ones, so one.

**`halfH2` stays at the finest level's value all the way down.** The fine equation is
`-(A phi) = halfH2 * rhs`, and restricting it gives `-(R A P) e = halfH2 * (R r)`. The
coarse operator inherited the fine operator's units rather than being rediscretised in
its own, so a hierarchy that recomputed `halfH2` per level would be wrong by a factor of
four per level — and would still converge, to something else.

### The test that was wrong and failed on correct code

The first version of the operator check asserted that `R A P` reproduces the
rediscretised seven-point Laplacian. **That identity holds in one dimension and not in
three**: the transfers are tensor products, so
`R A P = sum over axes of (R_a A_a P_a) x (R_b P_b) x (R_c P_c)` and `R_b P_b` is
`[1/8, 3/4, 1/8]`, not the identity. The off-axis entries belong there.

Working out what they should be instead turned a weak test into one that pins every
coefficient against arithmetic the code had no part in — centre `27/64`, face `-3/128`,
edge `-5/256`, corner `-3/512`, all to 1e-13, and the row summing to exactly zero.

## The three-dimensional V-cycle barely descends, and the guard is right

**A cycle is not a unit of work, and cycle counts are what get compared.** The section
below quotes 49 cycles against 12-13 against 9 as though they measured the same thing.
They do not: a cycle at zero coarse levels is several hundred smoothing sweeps over the
finest grid, and a cycle at five levels is a handful per level.

`SolveReport` now carries `Levels`, `Sweeps` and `CoarsestNodes`, threaded through the
recursion in both solvers, and `einzel solve` prints them - with
`<- not multigrid: it never coarsened` where the depth is zero.

### What it says

`Representable` stops coarsening once a coarse cell would exceed the smallest electrode
dimension. **That is a physical size, so it does not move when the mesh is refined**:
levels get added at the top and the bottom stays where it was.

| geometry | 33³ | 65³ | 129³ | 257³ | coarsest cell |
| --- | --- | --- | --- | --- | --- |
| two 1 mm slabs | 0 | 0 | 1 | 2 | frozen at 0.5 mm |
| four 1.2 mm rods | 0 | 1 | 2 | — | frozen at 0.625 mm |
| a 2 mm sphere | 1 | 2 | 3 | — | frozen at 1.25 mm |
| **no interior electrode** | **4** | **5** | **6** | — | 10 mm, fully coarsened |

So the grid-independent cycle count this solver is documented as having - 8→7→7→7 from
32 to 256 - is a property of the **boundary-only** case, which is the one row with no
electrodes in it.

| geometry | cell | nodes | levels | coarsest | cycles | factor | sweeps | wall |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| slabs | 0.5 mm | 65³ | **0** | **274,625** | 6 | 0.015 | 2,412 | 36.0 s |
| slabs | 0.25 mm | 129³ | 1 | **274,625** | 45 | 0.596 | 18,270 | 176.3 s |
| rods | 0.3125 mm | 65³ | 1 | 35,937 | 31 | 0.475 | 3,650 | 6.1 s |
| sphere | 0.625 mm | 33³ | 1 | **4,913** | 13 | 0.155 | 1,734 | 0.3 s |
| sphere | 0.3125 mm | 65³ | 2 | **4,913** | 13 | 0.158 | 1,786 | 1.3 s |

**The sphere is what health looks like**: eight times the nodes, cycle count flat at 13,
sweeps 1,734 → 1,786. **The slabs are the pathology**: the bottom level is the entire
fine grid at 65³, and still 274,625 nodes at 129³. The bottom of the V does not shrink,
which is precisely the cost multigrid exists to remove.

Two consequences worth stating plainly. **At a 0.5 mm cell the slabs coarsen zero
times** - the "6 cycles at factor 0.015" is 400 relaxation sweeps per cycle on the
finest grid, which is why a 65³ Laplace solve takes 36 seconds. And **adding the first
coarse level makes it worse, not better** (slabs 6 → 45 cycles, rods 3 → 31), because
zero levels is brute-force relaxation that works and one level is a two-level method
whose coarse correction is poor.

### It is a three-dimensional problem, not a dimensional necessity

The two solvers coarsen by different rules: the 2-D one descends while the coarse mask
still holds an interior node, the 3-D one while a coarse cell is no larger than the
smallest electrode. On shipped templates:

| | levels | coarsest |
| --- | --- | --- |
| einzel lens (2-D) | 5 | **99 nodes** |
| quadrupole (2-D) | 6 | **9 nodes** |
| ion funnel (2-D) | 5 | 27 nodes |
| rectilinear trap (2-D) | 6 | 15 nodes |
| **segmented quadrupole (3-D)** | **2** | **9,537 nodes** |

### The guard is load-bearing, measured by removing it

Raising `ResolvedBy` lets the cycle descend further. On the 0.25 mm slabs:

| ResolvedBy | levels | cycles | sweeps | wall | peak V of 100 applied |
| --- | --- | --- | --- | --- | --- |
| **1.0** (shipped) | 1 | 45 | 18,270 | 144.9 s | **100.00** |
| 2.0 | 2 | 5 | 1,274 | 5.4 s | **486.75** |
| 4.0 | 3 | 4 | 344 | 3.6 s | **516.29** |
| unlimited | 6 | 5 | 130 | 4.2 s | **464.15** |

**Deeper coarsening is thirty times faster, converges cleanly, and is wrong.** It
reports converged at a healthy factor; only the maximum principle catches it, which is
the argument for keeping that check as a tolerance-free test rather than a diagnostic.

**The mechanism is sharper than "a coarse grid is cruder", and one plausible explanation
was checked and rejected.** The coarse levels solve for the *error*, so a coarse mask
carrying the electrodes' real potentials would inject a spurious 100 V per cycle - which
would explain the magnitude neatly. It is not that: coarse correction fields are created
zero and never have a mask applied, so their fixed nodes are correctly zero. What
actually happens is that **at four levels down a 1 mm slab is smaller than a cell and is
pinned to a single node**, so the coarse problem constrains the error at two isolated
points where the fine problem constrains it over two whole planes. The correction is a
solution to a different problem.

**That is exactly what Galerkin coarsening fixes**, and this is the measurement that
turns "Galerkin is the right answer" from an assertion into a conclusion: the coarse
operator would be `R A P`, inheriting the fine Dirichlet structure through the operator
rather than re-rasterising the geometry - so the coarse problem would be the same
problem coarsened, and the guard could be removed rather than tuned.

`CoarseningDepthTests` pins both halves: the slabs hold the maximum principle *because*
they refuse to coarsen, and zero levels means the coarsest grid is the finest one.

Two slabs 10 mm apart in a grounded box, 0.5 mm cells, 65 x 65 x 46 nodes:

| | cycles | factor |
| --- | --- | --- |
| **parallel plates** | **49** | **0.652** |
| the shipped segmented quadrupole, twelve rods | 12–13 | 0.08 |
| a charged sphere, node-aligned coarse levels | 9 | 0.126 |

**124 seconds** for the plates, against 11 for a whole segmented-quadrupole run. A
factor of 0.65 means the V-cycle is barely doing anything and the solve is close to
plain relaxation.

The reason is structural rather than surprising once seen. A rod is thin, so
coarsening loses it quickly and the pinning fix restores its presence; **a slab is a
large solid Dirichlet region**, and a coarse level that half-represents a slab is
solving a different problem over a large volume rather than a small one. The error the
coarse grid feeds back is wrong where most of the domain is.

**This is why the example corpus has no three-dimensional model.** One was written — a
parallel-plate gap checked against `sqrt(2 d² m / (q V))`, the same closed form the
analytic accelerating-gap example uses — and it was not shipped, for two reasons worth
separating:

- **It costs two minutes** against a gate that runs the other twenty-six examples in
  forty-two seconds. That is the multigrid limitation, not the example.
- **It was 3.2 per cent off**, which is the *geometry*: a finite plate in a grounded
  box 2 mm behind it is not an infinite capacitor, and asserting `V/d` to one per cent
  would be asserting that it is. Fixing that means a larger domain, which costs more
  solve, not less.

So the volume solver, its tricubic interpolant and its cut cells are exercised by
`Einzel.Fields.Tests` and by the segmented-quadrupole study, and **not** by the
release gate. That is a stated gap rather than an oversight, and the thing that closes
it is Galerkin coarsening — which would also make the plates converge like everything
else.

## Poisson, not only Laplace

Every solve here until now has been **Laplace** - a potential with no charge in it,
fixed on conductors. SC-1's *approximate* space-charge method needs **Poisson**:
deposit the packet's own charge onto a grid, solve grad2 phi = -rho/eps0, gather the
field back.

**The cycle already carried a right-hand side and had only ever been handed zeros.**
The smoother subtracts it, the residual is defined against it, and the coarse levels
receive the restricted *residual* - which is what they need whatever the fine level's
source is. So the source costs one argument and no numerics.

Checked by the **method of manufactured solutions**, which is the sharpest thing
available: pick a potential, differentiate it analytically to get the source that
produces it, hand the solver that source, and compare. Nothing on the exact side is
discretised and no reference implementation is involved. With
phi = sin(pi x) sin(pi y) on the unit square, whose Laplacian is -2 pi^2 phi exactly:

| intervals | worst error | order | cycles | factor |
| --- | --- | --- | --- | --- |
| 32 | 8.0358e-4 | | 11 | 0.0632 |
| 64 | 2.0082e-4 | **2.000** | 11 | 0.0659 |
| 128 | 5.0201e-5 | **2.000** | 11 | 0.0702 |

Second order to three figures, and the cycle count is grid-independent. **The order is
the load-bearing check**: a source entering the smoother and the residual
inconsistently would still converge - to the wrong answer - and would show it as an
order that is not two rather than as a failure.

And the control: passing no source gives **exactly** what the solver gave before one
existed, bit for bit and in the same number of cycles. Not nearly - exactly, or every
number this engine has published from a solved field has moved.

What is left for SC-1's approximate method is the particle side: cloud-in-cell
deposit, the same weights on the gather (or momentum is not conserved), and validation
against the direct pairwise sum, which exists and is the reason it was built first.

## Cloud-in-cell: charge onto a grid, field back off it

The particle half of SC-1's approximate method. The direct pairwise sum, which is the
reference it is validated against, costs O(N^2); particle-in-cell costs one solve plus
O(N), which is what makes 10^4 macroparticles affordable when 10^3 already takes hours.

**Charge is conserved by construction rather than by normalising.** The eight weights
sum to exactly one whatever the position, so what goes on the grid is what was handed
in - 500 particles, 8.010883e-14 C in, 8.010883e-14 C on the grid. Normalising
afterwards would pass the same test while hiding a weighting error rather than
preventing one.

**Charge that leaves the grid is counted, not clamped or dropped.** A packet that has
drifted off its own grid produces a field that is quietly too weak, which looks exactly
like a packet more dilute than it is; clamping is worse still, piling the charge onto a
face and producing a field that is wrong and confident.

### The same weights on the way out, and why it is not a convenience

A particle writes charge to a node with some weight and reads the field back from it
with the same weight, so its own contribution cancels in the sum. Gather with a
*better* interpolant - a tricubic, which is more accurate for a smooth field - and
every particle feels itself, the packet heats up out of nothing, and the field looks
entirely reasonable throughout.

Measured, as a fraction of the field a neighbour one cell away would feel (2.304e4 V/m
for this charge), against a nearest-node gather that shares no weights:

| offset in the cell | matched | mismatched |
| --- | --- | --- |
| 0.00 | 8.05e-5 | 0.521 |
| 0.13 | 8.32e-5 | 0.480 |
| 0.37 | 1.01e-4 | 0.467 |
| 0.50 | 1.15e-4 | 0.495 |
| 0.61 | 1.29e-4 | 0.470 |
| 0.89 | 1.68e-4 | 0.485 |

**Three and a half orders of magnitude**, and the mismatched column is what makes the
matched one a property of the *symmetry* rather than of the grid being fine. Half the
neighbour field, felt by a particle from itself, is not a small error - it is a packet
that expands for a reason nobody put in.

It is not exactly zero, and saying so matters: the cancellation is exact on a uniform
periodic grid with centred differences, and here the box is earthed, whose images break
the symmetry slightly. So the assertion is a ratio to the scale that would matter, not
a claim of zero.

**Trilinear, and ACC-3 is not violated.** That requirement forbids trilinear
interpolation on a *trajectory path*, and this is not one: it is the interpolation of a
self-consistent field whose accuracy is bounded by the deposit anyway, and where the
deposit/gather symmetry buys more than the extra order would. The applied field an ion
flies through is still tricubic.

### Against the closed form

A uniformly charged ball, 20,000 macroparticles, 48 cells across an 8 mm earthed cube,
against `Qr/(4πε₀R³)` inside and `Q/(4πε₀r²)` outside:

| r | measured | closed form | ratio |
| --- | --- | --- | --- |
| 0.5 mm | 7.7384e2 | 7.1998e2 | 1.075 |
| 1.0 mm | 1.2956e3 | 1.4400e3 | 0.900 |
| 1.5 mm | 6.4595e2 | 6.3998e2 | 1.009 |
| 2.0 mm | 3.6669e2 | 3.5999e2 | 1.019 |

Eleven cycles at a convergence factor of 0.110. The 1.0 mm point is the ball's own
surface, where the closed form has a kink the grid cannot resolve, and the whole
comparison carries the earthed box: the closed form is for a sphere alone in space and
this one sits in a cube whose images pull the potential down. **That is a boundary
condition rather than a solver error**, and tightening it means a bigger box rather
than a better method.

### What is still missing for SC-1

The **integration**: choosing the grid a drifting packet deposits onto, when to
re-solve, and the comparison against the direct sum on the same configuration. The
pieces are all here and nothing wires them to `PacketIntegrator` yet.

## Particle-in-cell, wired to the packet integrator

SC-1 asks for a direct pairwise sum and an approximate method validated against it.
Both are now `ISelfField` — positions in, accelerations accumulated out — which is
what lets the two be handed the same configuration and differenced. A caller that had
to know which one it held would end up knowing why, and the choice would stop being
the model's.

### Three design questions, answered in the code

**Which grid does a drifting packet deposit onto?** Its own, in its own frame. A
packet crossing a metre-long analyzer cannot have a grid over the instrument at any
resolution that resolves the packet, so the box is built around the packet and
centred on the centroid — and every deposit and gather is done relative to that
centroid. **Uniform translation is therefore exact**, measured at 1e-11 across a
250 mm displacement, and costs nothing.

**When to re-solve?** On *shape*, not on position or a step count. Translation is
already exact, so the only thing that ages between solves is the packet's shape:
the criterion is a fractional change in RMS radius, defaulting to 5%. That is a
statement about the approximation rather than a number chosen to make something
finish.

**What is the boundary?** An earthed box, which a packet in flight is not in. Centring
the box is what keeps that cheap — a centred distribution in a symmetric earthed box
induces almost no field at its own centre — and `Padding` buys the residual down and
is reported.

### The finding: a linear gather costs 27× the integrator steps

The first version used cloud-in-cell for both deposit and gather, on the argument that
ACC-3's ban on trilinear interpolation is about *trajectory paths* and this is a
self-consistent field whose accuracy the deposit already bounds. **That argument is
right about accuracy and wrong about cost**, and the step controller does not care
about the distinction.

A trilinear gather is continuous and its derivative is not: the force kinks at every
cell face, and an embedded Runge–Kutta estimator reads a kink as error. Measured on a
free-flight packet, against the direct sum's 25 steps:

| nodes across the box | steps, linear | steps, quadratic |
| --- | --- | --- |
| 16 | 274 | **45** |
| 32 | 383 | **65** |
| 64 | 656 | **95** |

The step count tracking the node count is what identifies the mechanism: more nodes
means more faces per unit path, and a fixed overhead would not scale.

The fix keeps the property that made the linear choice necessary. A **quadratic
B-spline** (triangular-shaped cloud) uses twenty-seven nodes instead of eight, is
continuously differentiable, and is used for the deposit *and* the gather — so the
self-force still cancels, which is the whole reason the two must share a shape. The
weights sum to exactly one for any offset, so charge is still conserved by
construction, and that identity holding for *any* offset is what lets the index be
clamped at a face without losing charge.

### What it costs, and where it starts paying

| macroparticles | direct sum | particle-in-cell | ratio |
| --- | --- | --- | --- |
| 250 | 0.57 s | 3.50 s | 0.16 |
| 500 | 1.95 s | 4.62 s | 0.42 |
| 1000 | 7.84 s | 6.47 s | **1.21** |
| 2000 | 35.01 s | 10.92 s | **3.21** |

**The crossing is near 850 macroparticles**, and it is worth stating plainly rather
than quoting the asymptotics: below that the reference method is simply faster, and a
run that reaches for the approximate one there is paying for nothing. What the table
shows is the direct sum's share growing as N² while the grid's does not.

Absolute times are from one machine and are not asserted; the test asserts only that
the ratio rises with N, which is the only claim a wall-clock measurement on a shared
runner can honestly make.

### Against the reference

A ball of 4,000 macroparticles, self-force binned by radius:

| r/R | direct | grid | ratio |
| --- | --- | --- | --- |
| 0.1 | 1.5897e8 | 1.5915e8 | 1.0011 |
| 0.3 | 3.6507e8 | 3.5928e8 | 0.9842 |
| 0.5 | 5.7025e8 | 5.7375e8 | 1.0061 |
| 0.7 | 7.8024e8 | 7.7132e8 | 0.9886 |
| 0.9 | 9.6628e8 | 8.8094e8 | 0.9117 |

The outermost bin is the worst and has to be: it straddles the ball's surface, where
the density steps to zero, and a smoothed deposit and a point-softened sum disagree
about a discontinuity by construction. The body of the packet agrees to about a per
cent.

**And end to end**, which is the check that says nothing accumulates over a flight
that was not already there in one evaluation: a packet released in free space expands
under nothing but its own charge, from 0.384 mm RMS to **1.907 mm by the direct sum
and 1.916 mm by the grid — 0.5 per cent apart** over 2 µs.

### A trade that showed itself

Rebuilding the box on every refresh throws away the previous potential and the
Dirichlet mask with it, so a freshly built box carries headroom above the requested
padding and is kept while the packet grows into it. At 1.6× headroom that cut rebuilds
from 32 to 4 — and cost accuracy: the outermost bin fell from 0.94 to **0.83**, because
a bigger box at a fixed node count resolves the packet with fewer cells. At 1.15× the
rebuilds are 11 and the accuracy is back. The headroom is resolution traded for
allocation, and the packet is only a few cells across either way, which is what makes
the trade visible at all.

### Declaring it, and the two knobs

`"spaceCharge": "pic"` with an optional sibling block:

```json
"transport": {
  "spaceCharge": "pic",
  "spaceChargeGrid": { "nodes": 32, "padding": 4.0, "refreshTolerance": 0.05 }
}
```

Both numbers are approximation knobs rather than conveniences, so both are declarable
and both are reported on the result. A `spaceChargeGrid` against any other method is
**refused rather than ignored**, which is the rule an unrecognised property already
follows: a document that configures a solve it is not running has been misunderstood by
its author, and silence is the expensive answer.

`einzel estimate` costs both methods in the same currency - pair-equivalents a stage -
so it can state their **ratio at this cloud** rather than the asymptotics. That
distinction matters: particle-in-cell is linear where the sum is quadratic, so quoting
the asymptotics alone recommends it everywhere, including the majority of clouds where
it loses to the method it approximates.

### The refresh criterion is a controlled approximation

The one number in this method that is a choice rather than a consequence, so it needs
evidence that tightening it goes somewhere - and that somewhere is the reference. On a
400-macroparticle packet flown 2 us:

| refreshTolerance | rms mm | vs the direct sum | solves |
| --- | --- | --- | --- |
| 0.30 | 2.1254 | +12.68% | 7 |
| 0.15 | 2.0025 | +6.16% | 12 |
| **0.05** (default) | **1.9054** | **+1.01%** | 32 |
| 0.02 | 1.8761 | -0.54% | 72 |

**The sign at the coarse end was predicted rather than explained afterwards**: a field
held across a refresh is the field of a packet *denser* than the one being pushed, so a
stale field always pushes too hard. It comes out wide at every tolerance where staleness
dominates, and monotonically less so as it tightens.

**It crosses zero at 0.02, and that is not the prediction failing.** It is staleness
falling below the *other* difference between the two methods, which is the next section
and is the more useful finding.

### The two methods must be compared at matched smoothing

Neither computes the point-charge field of the macroparticles. **The direct sum softens
at short range** - Plummer, at the mean macroparticle spacing - and **the grid smooths
at the cell**. So a comparison at whatever each happens to default to is a comparison of
two different smoothing lengths: agreement there is a coincidence of magnitudes, and
disagreement is not evidence of a defect.

The sum has a limit it can be taken to and the grid has a scale it can be set to, so the
comparison can be made properly. Taking the softening down:

| softening | rms mm |
| --- | --- |
| the mean spacing (default) | 1.87142 |
| / 10 | 1.93896 |
| / 100 | **1.94027** - the point limit |

**The reference's own softening is worth 3.5%**, which is larger than any agreement
claimed against it elsewhere in this document.

Against that limit, by cell size, on the same packet:

| nodes | cell mm | cell / spacing | rms mm | vs the limit |
| --- | --- | --- | --- | --- |
| 16 | 0.25000 | 3.68 | 1.64705 | **-15.1%** |
| 32 | 0.12500 | 1.84 | 1.85939 | -4.2% |
| 64 | 0.06250 | 0.92 | 1.94189 | **+0.08%** |
| 128 | 0.03125 | 0.46 | 2.02595 | **+4.4%** |

**At a cell of about the mean macroparticle spacing the two agree to 0.08%** - far
stronger than the few per cent an unmatched comparison gives, and it says *what makes
them agree* rather than reporting that they do.

**And accuracy here has an optimum rather than a floor.** Refining past the match makes
it worse, which is the opposite of what refinement does everywhere else in this engine
and is exactly what someone does when they want a better answer.

**Confirmed as a sampling artefact rather than a resolution one**, by holding the cell
fixed at 128 nodes and raising the macroparticle count:

| macroparticles | per cell | vs the limit |
| --- | --- | --- |
| 400 | 0.012 | +4.42% |
| 1,600 | 0.049 | +1.55% |
| 6,400 | 0.195 | +0.93% |

So the error is set by **macroparticles per cell**, not by the cell in absolute terms:
below about one macroparticle per cell the deposit stops representing a density and
starts representing lumps, and the mutual force comes out too strong. That is the
classical finite-grid heating of a particle-in-cell scheme, found here independently
rather than assumed.

`spacecharge.grid-resolution` reports the ratio **whether or not it crosses a
threshold** (REG-2's rule applied to a different quantity - a reader who sees 0.92 knows
the run was checked, and one who sees nothing cannot tell that from its not having
been), as a validity violation outside 0.7 to 2.0, and it names the node count that
would match. It needs no run to compute, because the cell and the spacing both scale
with the packet radius and it cancels: the ratio is `2 x padding x cbrt(N) / nodes`.

### A trap in measuring any of this

`Grid3D.OverBox` rounds each axis up to a power of two, so **asking for 24 and asking
for 32 gives the same mesh**. A first version of the node-count table above ran
16/24/32/48/64 and produced two pairs of identical numbers; read without knowing that,
it says the answer is insensitive to resolution over a fourfold range. It is already
written down for the 3-D solver, and it caught me again here.


## Gas and space charge in one run

The packet integrator had no collision hook, and a declared gas was refused with a pushed
packet because it would otherwise have taken no part. The hook is one argument: a list of
collision samplers, one per macroparticle, each on its own schedule with its own seed
stream. The shared step is cut to the earliest scheduled collision of any active member,
the step is taken, and the members whose event that was scatter at the positions they have
actually reached before the derivatives are rebuilt. A collision closer than the integrator
can resolve is applied without a step, so a dense gas presents as a physical rate and not
as step-size underflow - the same rule the single-ion path follows.

**Why land on it rather than apply it at the end of the step it fell in.** The mutual
force this integrator exists to compute is evaluated at the members' positions, and a
velocity applied a fraction of a step late puts the scattered member somewhere it was not
when it scattered. The single-ion path lands exactly for the same reason and the packet
path should not be weaker. The price is that the step is bounded by the packet's total
collision rate: two hundred macroparticles at a pascal collide every few nanoseconds
between them.

| | |
| --- | --- |
| 200 hot macroparticles in 1 Pa of nitrogen, 2 ms, no mutual force | mean energy **0.930** of (3/2)kT, 290 collisions each (the single-ion path gives 0.952 on 288) |
| Collisions the integrator counted against the samplers' own totals | equal |
| A dense ball released from rest, 100 µs, pushed, in vacuum | RMS radius 0.162 → **28.7 mm** |
| The same packet, the same push, in 10 Pa of nitrogen | 0.162 → **8.07 mm**, 146 collisions each |

The third and fourth rows are the point: the vacuum control is what makes "smaller" a
statement about the gas rather than about a push that never acted, and "larger than it
started" is what says the push still acted through the gas.

**A macroparticle scatters as one ion and carries many.** The kinematics are one ion
against one neutral, so the packet relaxes to the ion's velocity distribution sampled at
the macroparticle count; the drag and the diffusion coefficients are the ion's, the
fluctuations are those of the smaller sample. Reported on every such run
(`spacecharge.macroparticle-collisions`), because a cooled cloud is exactly what such a run
is for and its size is set by a balance the fluctuations enter.

## Making a pushed packet affordable, and the measurement that redirected the work

The direct pair sum is O(N^2) and looks like the obvious thing to make faster. Measuring
first said otherwise, and the measurement is the useful part. One integrator stage, 240
macroparticles, on the two linear-ion-trap templates:

| | field evaluation | pair sum | pair sum's share |
| --- | --- | --- | --- |
| cross-section (2-D solve) | 0.119 ms | 0.288 ms | **71 %** |
| volume (3-D solve) | 0.792 ms | 0.298 ms | **27 %** |
| volume, 1000 macroparticles | 3.413 ms | 3.649 ms | 52 % |

**On the volume geometry the pair sum is not the bottleneck at the sizes actually used**:
a tricubic gather over a solved volume costs about 3 microseconds and there are as many of
them as there are members. Vectorising the sum was still worth doing, and it is worth
about four per cent there.

### The pair sum, over vector lanes

Structure-of-arrays in pooled buffers, active members compacted first so the inner loop
has no branch, and j across the lanes so the symmetric half of the sum falls out: the
reaction on each j is a lane of an accumulator, and the action on i is one horizontal sum
at the end of the row.

| macroparticles | scalar | vectorised | |
| --- | --- | --- | --- |
| 60 | 140 Mpair/s | 356 Mpair/s | 2.55x |
| 240 | 140 | 434 | **3.11x** |
| 1000 | 137 | 455 | 3.33x |
| 2000 | 141 | 469 | 3.33x |

Four doubles to a vector on this machine, so 3.33x of a possible 4x. The scalar rate is
flat at 140 Mpair/s across a thirty-fold range, which is what says the measurement is of
the arithmetic and not of the cache.

The scalar implementation is kept and **selectable** (`CoulombInteraction.Kernel`), which
is what CMP-1's "never deleted or allowed to rot" needs to mean in practice: a reference
nothing can run is a reference nobody can check. The two are compared over every size that
exercises a different part of the loop, with members switched off, with a pre-loaded
accumulator, and against Newton's third law. They agree to **3e-15 of the acceleration
scale** and cannot agree better: the additions happen in a different order.

### The applied-field loop, across cores

Every member's field evaluation is independent - it reads a field that holds no mutable
state and writes only its own slot - so the loop may be spread across cores, and the answer
is **bit-identical however many run it**, because nothing is summed across members. That is
asserted, not argued: 96 members over 48 steps on 16 cores, every final position, velocity
and flight time equal to the last bit.

The mutual force is deliberately **not** parallelised. It is one call over the whole packet
that already vectorises internally, and splitting a symmetric sum across threads would make
the accumulation order depend on the thread count.

**Whether to parallelise is measured, not assumed.** What decides it is members times
cost-per-sample, and only one of those is known in advance, so the first stage is timed and
the decision made from it. Forcing it on is the wrong answer for a cheap field: on the
cross-section, parallel alone was no better than vectorising alone, because the field loop
there is a tenth of a millisecond and starting a parallel loop costs some tens of
microseconds. End to end, a 240-member packet pushed through a solved field for 2
microseconds:

| | scalar, serial | vectorised | and across cores | automatic |
| --- | --- | --- | --- | --- |
| cross-section | 226 ms | 113 ms (2.01x) | 117 ms | **113 ms (2.01x)** |
| volume | 525 ms | 506 ms (1.04x) | 331 ms | **319 ms (1.64x)** |

The automatic choice matches or beats the better of the two forced ones in both, which is
the property worth having: a calibration that picked wrong would be worse than no
calibration at all.

### The inner loop allocates nothing, and a lambda that was never called

CMP-1 asks that the inner loop allocate nothing, so a collection cannot interrupt a run.
This one allocated **504 bytes on every step** - twenty-two arrays per step, from six stage
derivative buffers, a staged state, an error vector, a position copy per stage, and the
weight list each Runge-Kutta stage was called with. All are now built once per flight; the
stage weight lists are *constant* rather than merely poolable, since which buffer each
stage reads is fixed by the tableau.

That left exactly 504 bytes a step still there, **invariant with the member count**, which
is what identified it: 7 stages times 72 bytes is a display class with seven captured
variables, allocated by the lambda in the parallel branch - a branch the serial path never
takes. The compiler hoists captures at method entry, not at the branch. Moving the lambda
into its own method took it to **zero bytes per step**, measured as the cheapest of five
runs at 22 steps against 1002.

## The quadro-logarithmic field

`U(r, z) = (k/2)(z^2 - r^2/2) + (k/2) Rm^2 ln(r/Rm)` — a harmonic axial well superposed on
a logarithmic radial one, and the field an orbital trap is built from. Named for its
mathematics rather than for the instrument, following `HalfSpaceUniformField` and for the
same reason: architecture invariant 2 keeps device names above `Einzel.Library`.

**It satisfies Laplace exactly.** The quadratic part contributes `-k` to the radial
Laplacian and `+k` to the axial one, and the logarithm is harmonic on its own, so the sum
is zero everywhere off the axis. That is not a numerical property to be measured but an
identity to be checked, and the residual below is the differencing rather than the field.

**What it exists for is an independence rather than a value.** `dU/dz = k z` carries no
`r`, so the axial frequency is `sqrt(q k / m)` whatever the radius, the angular momentum or
the axial amplitude. Measuring mass by frequency rests entirely on that.

| | |
| --- | --- |
| Axial frequency, r from 6 to 14 mm and z0 from 1 to 4 mm | constant to **4e-8** |
| Frequency vs `sqrt(q k / m)`, m/z 200 to 2000 | **6.5e-9 to 1.9e-8** |
| Azimuthal field component | **1.15e-16** of the field — machine epsilon |
| Angular momentum along a flown orbit | 1.2e-10 |
| Energy along the same | 2.3e-10 |
| Laplacian, differenced | 5.7e-8 to 2.7e-7 of `k` |

**The azimuthal check is separated from the trajectory one on purpose.** A surface of
revolution can exert no torque about its own axis, so that component is zero as an identity
and is asserted exactly. What a flown orbit measures is the *integrator's* fidelity to it,
which lands at 1e-10 — and would keep looking fine if the field had acquired a small
azimuthal term, since that is what a small drift looks like.

**The axis is refused rather than clamped.** A logarithm has no value at zero, and the
region is where the central electrode is. Returning a large number would let an ion be
launched inside metal and flown there.

**It needed a new dimension**, which is the sort of gap only a new kind of device finds:
`V/m^2`, the curvature of a potential rather than its slope. Distinct from `V/m` by one
power of length, which is exactly the distinction a dimension system exists to keep — a
curvature quoted as a field is wrong by a length, and at millimetre scales that is a factor
of a thousand. `V/mm^2` is a millionfold, not a thousandfold, which is the kind of slip
worth making unwriteable.

**And a sign error it very nearly kept**, recorded in `docs/lessons.md`: the radial
component went in negated and *every frequency test passed anyway*, because the axial
motion is exactly decoupled from the radial coordinate. A designed invariance is a designed
blindness — the quantity an instrument is built to make independent of everything else is
the one least able to tell you the rest is right. What caught it was `E = -grad U` and
energy conservation, the two checks that span the components.
