# Magnetic fields

Einzel has one magnetic field kind: a **uniform magnetic flux density**, filling all
space or bounded to a hard-edge box, applied by **single-ion trajectory integration**
as the `q v x B` half of the Lorentz force. That is the first step towards magnetic
sectors, Wien filters, Penning and ICR cells and E x B devices; it is not yet a
magnetic potential solver. This page says what exists, how it is checked, what is
refused and why, and what is not built.

The format is in [Model format](model-format.md#a-magnetic-field-schema-015); the
element looks like this:

```json
{ "type": "uniformMagnetic",
  "fluxDensity": { "value": [0, 0, 0.5], "unit": "T" },
  "region": { "minX": { "value": 0, "unit": "mm" }, "maxX": { "value": 500, "unit": "mm" },
              "minY": { "value": -500, "unit": "mm" }, "maxY": { "value": 500, "unit": "mm" },
              "minZ": { "value": -50, "unit": "mm" }, "maxZ": { "value": 50, "unit": "mm" } } }
```

## What exists

- **A dimension and units.** `Dimension.MagneticFluxDensity` (kg s^-2 A^-1, so that
  a velocity times a tesla is a volt per metre) and the units `T`, `mT`, `uT`/`µT`,
  `G` (exactly 1e-4 T) and `kG`. A flux density written in V/m is a
  `UNITS_INCOMPATIBLE` error at `/fields/N/fluxDensity`.
- **`IMagneticField`** (`Einzel.Fields.Magnetic`), separate from
  `IElectrostaticField` for the reason `ITimeVaryingField` is: code that knows about B
  asks for it by type, and code that does not cannot be handed one through a default
  member it will quietly ignore. Implementations: `UniformMagneticField`,
  `BoundedMagneticField` (hard edge), `SuperposedMagneticField` (B adds).
- **`MagnetizedField`**: the assembly's electric field and its magnetic field carried
  as one object. `FieldAssembly` builds one **only when the model declares a magnetic
  element**, so every model without one is handed exactly the field object it always
  was. A driven or sequenced electric part gets `DrivenMagnetizedField`, which
  forwards every `ITimeVaryingField` member (including the defaulted ones), so a
  switched electric field beside a magnet keeps its clock.
- **The force**, in `DormandPrince54.Derivative`: after the electric field is
  sampled, `if (field is IMagneticField m) a += v x m.B(x, side)` (B on the side of each magnet face the step started on - trap 4), then the one
  multiplication by q/m. Every stage of every step, every landing bisection and every
  conductor or detector landing goes through that function, so they all see B.

### The integrator, and the three traps

1. **The analytic field-free drift.** The drift assumes zero acceleration means a
   straight line, which is false in B. `MagnetizedField.FieldFreeRunLength` returns
   the smaller of the electric run and `IMagneticField.MagnetFreeRunLength`: zero
   anywhere B acts, and outside a bounded magnet the exact distance along the ray to
   the box (slab method). So a sector's straight approach and exit are still taken
   as exact drifts (15.000000 mm of the 180 degree example), and nothing inside a
   magnet ever is.
2. **Step control, decided per step from B where the ion is.** The error estimator
   sees the force because the force is in the derivative. On top of that a step may
   span at most **a twentieth of a cyclotron turn**, `2 pi m / (|q| |B(x)|) / 20` with
   `|B(x)|` the flux density at the start of the step - not a global bound: a bound
   summed over two disjoint magnets capped every step at the period of a field that
   exists nowhere, and a small magnet capped every step of a long beamline around it
   (`ASmallMagnetDoesNotCapTheStepsOfTheBeamlineAroundIt`: 4 steps over the metre
   with no magnet, 4 with a 1 T magnet beside the path, 7 through a 2 mm one; with the
   global cap restored that test and `TwoDisjointMagnetsCapEachAtItsOwnPeriod` fail).
   Twenty for the RF reason: it is a guard against an error estimate fooled by a step
   comparable to the period, not what sets accuracy. At 18 degrees a turn the local
   fifth-order error of a circle is about 1e-6 of the radius per step, so at this
   engine's tolerances (1e-8 and tighter) the controller binds and the cap does not:
   the 100-orbit check takes 297 steps a turn at 1e-11. It binds at loose tolerance,
   where it holds 20.4 steps a turn at a 1e-2 tolerance.
   **The turning-point cap is off where B at the ion is non-zero**: the magnetic force
   is perpendicular to v, so the cap's "decelerating" test fires on the sign of a
   round-off, and on an E x B cycloid the ion is at rest once a turn - the RF argument
   exactly. With the cap left on, the E x B tests fail. It is decided per step, so a
   magnet with zero flux density, or one the ion never enters, leaves a reflectron's
   flight **bit-identical with default settings**, step counts included (it used to
   switch the cap off for the whole flight, and the zero-B check passed only because
   it switched the cap off on both sides). A bounded magnet with zero flux density
   reports no face and bounds no drift, so it is as absent as no magnet at all.
3. **Energy.** B does no work, so the total-energy diagnostic stays meaningful in a
   static magnet: kinetic plus electric potential energy is conserved, and it is what
   the 100-orbit test asserts.
4. **A bounded magnet's face is a discontinuity in B, and every stage of a step takes
   B from one side of it.** The face is reported through
   `SignedDistanceToMagneticDiscontinuity` and composed with the electric surfaces by
   `DiscontinuityDistance.Combine` - the nearest's magnitude and **the product of the
   signs**, the rule `SuperposedField` has always used. (The first version took the
   sign of whichever surface was nearer, so crossing a mirror plane inside a magnet
   could leave the composed sign unchanged and skip the landing, and moving between a
   face the ion is inside and one it is outside flipped it with nothing crossed. A
   force-free magnet straddling a reflectron's mirror plane gave 1.7e-10 that way; with
   the product it gives the closed form to 1.7e-16.)

   Every stage of a step evaluates B with the box membership of **the start of the
   step**, taken a hair ahead along the velocity (`IMagneticField.MagneticFieldAt(x,
   side)`), so a step that straddles a face integrates one smooth field; the integrator
   sees the sign change, bisects, lands just past the face and continues with the far
   side's field. Without this an ion entering or leaving a hard edge had stages with
   B = 0 and stages with full B, and for an electron at a tesla no step above
   `MinimumStep` made that error acceptable: the flight ended `StepSizeUnderflow` at the
   magnet's edge (after 152 steps, 17 rejected, leaving the box after its semicircle;
   at 0.1 T, 159 steps, 23 rejected). Now it completes in 144 steps (7 rejected) and
   lands within 2e-12 of the closed-form time; an m/z 100 ion through the same edge
   takes 151 steps (2 rejected) where it took 175 (46 rejected), and the 180 degree
   sector 225 steps (0 rejected, 2,359 evaluations) where it took 244 (38 rejected,
   2,764). "A hair ahead" because the ion is ON the face exactly when it has just
   landed there, and a box contains its boundary: leaving a 2 mm magnet the next step
   carried its B out into the field beyond and kicked the ion 44 per cent too hard.

   **The landing is now load-bearing**, which it was not: with the face withheld from
   the discontinuity, the sector's exit is 1.5e-4 off in y with an exit angle of
   5.9e-3 rad, the mass ratio 1.09996 rather than 1.1, and every electron flight wrong.

   A step that starts outside every magnet integrates straight through one, so it
   is also kept from reaching past the **middle** of the next magnet along its
   velocity (`IMagneticField.MagnetHalfwayLength`, with the present acceleration
   allowed for): it may cross the entrance, and lands there, but not the exit as well,
   which the sign product would read as no crossing. In a uniform field the
   controller grows a step to most of a metre and a 2 mm magnet in the way was skipped
   entirely (transverse kick 0 against `-omega L` = -1929.7 m/s); it is now the closed
   form to 1e-6.

   **The limit that remains is `SuperposedField`'s**: a step crossing two surfaces at
   once - a mirror plane and a magnet face within a step of it - returns the sign to
   where it started. A magnet face 0.2 mm in front of a reflectron's plane or 0.7 mm
   behind it costs 5.0e-10 or 1.1e-10 of the flight time; the nearer-surface rule did
   no better (1.4e-10 and 2.0e-10 at 1.5 and 3 mm). The fix there is to track element
   regions rather than multiply signs, and it is not built.

Non-relativistic, as the rest of transport: there is no gamma anywhere, and adding a
relativistic mode would have to add it to the electric force and to the emittance
analysis at the same time (see `docs/lessons.md`).

## Closed-form checks

All in `tests/Einzel.Transport.Tests/MagneticFieldTests.cs` unless named otherwise;
m/z 100, 0.5 T, expectations written from the textbook solution of
`m dv/dt = q (E + v x B)`.

| Check | Expected | Measured |
| --- | --- | --- |
| Quarter turn at 1 kV: radius m v / (\|q\| B) | 91.057211322 mm | 91.057211322 mm (asserted to 1e-9) |
| Quarter turn at 4 kV | 182.114422644 mm | 182.114422644 mm |
| T/4 = pi m / (2 \|q\| B), both energies | 3256.031345 ns | 3256.031345 ns at 1 kV and at 4 kV |
| Sense: cation along +x in +z B | turns to -y | -y; +y with B reversed; +y for an anion; mirror image exact to 1e-15 m |
| Velocity along B | unchanged | to 1e-9 |
| 100 orbits, rtol 1e-11 | energy and speed constant, orbit closes | energy drift 2.0e-9, speed 9.9e-10, closes to 9.9e-10 of a radius |
| E x B drift from rest, m/z 100 z=+1 and m/z 300 z=-1, 7 periods | E/B = 4000 m/s along x | 4000.000000 m/s both; 1.8e-9 m/s along E; at rest to 8e-8 m/s |
| Wien filter, v0 = E/B | straight | \|y\| < 1e-12 m over 50 mm |
| Wien filter, v0 = 1.2 E/B | y = -0.836600955 mm, t = 0.417134952 us | same to every printed digit (y to 1e-7, t to 1e-9) |
| Wien filter, v0 = 0.8 E/B | y = +1.856428299 mm | same |
| 180 degree hard-edge sector | exit at -2r = -182.114422644 mm, t = 15 mm / v + pi m / (\|q\| B) = 6.853527232 us | relative 3.2e-13 in y (2.8e-13 before the side-of-face change), exit angle 1.7e-15 rad (was 1.4e-12), 1e-10 in t; 15.000000 mm taken as exact drift |
| Mass dispersion, m/z 121 against 100 | sqrt(1.21) = 1.1 | 1.100000000000 |
| B = 0 beside a half-space mirror | bit-identical to no magnet | identical flight time, state, step count and drift distance (turning-point cap off on both sides) |
| B = 0, unbounded or in a box, and a 0.5 T magnet the ion never enters, default settings | bit-identical to no magnet | identical outcome, time, state, accepted and rejected steps, drift distance and energy drift (`MagneticEdgeTests`) |
| Electron, 1 kV, into a 1 T hard edge from a drift | semicircle, 2r = 0.2133 mm, t = 0.9426998062 ns | completes, y to every printed digit, t to 1.8e-12 |
| 2 mm, 0.5 T magnet in a uniform-field beamline | kick -omega L = -1929.706640 m/s | -1929.706640 m/s |

The corpus adds three examples to the EX-2 gate (`src/Einzel.Commands/Examples`),
each driven through `einzel test`:

| Example | Closed form | Expected | Off by |
| --- | --- | --- | --- |
| `cyclotron` | (7/6) pi m / (\|q\| B), detector at x = -r/2 | 7.59740647 us | 1.1e-14 |
| `wien-filter` (B in gauss) | root of v_d t + ((v0 - v_d)/w) sin(w t) = L | 0.41713495 us | 1.2e-15 |
| `magnetic-sector-180` (B in mT) | 15 mm / v + pi m / (\|q\| B) | 6.85352723 us | 1.5e-15 |

### What breaks each, measured by mutation

| Mutation | Caught by |
| --- | --- |
| Cross product written `B x v` | 9 of 15 transport tests, including the sense test; `wien-filter` corpus |
| B ignored | 9 of 15 transport tests; all three corpus examples |
| Field-free drift taken inside a magnet | 9 of 15 transport tests; `cyclotron` and `magnetic-sector-180` corpus |
| Cyclotron step cap removed | `ALooseToleranceStillTakesTwentyStepsATurn` only (the cap does not bind at normal tolerances) |
| Cyclotron cap from the global `MaximumFluxDensity` rather than B at the ion | `ASmallMagnetDoesNotCapTheStepsOfTheBeamlineAroundIt`, `TwoDisjointMagnetsCapEachAtItsOwnPeriod`, `AMagnetTheIonNeverEntersFliesBitForBitWithDefaultSettings` |
| Turning-point cap off wherever a magnet is declared, not only where B acts | both bit-identity tests (`AMagnetWithZeroFluxDensity...`, `AMagnetTheIonNeverEnters...`) |
| Electric and magnetic surfaces composed by the nearer one's sign | `AMagnetAndAMirrorComposeTheirSignsAsAProduct`, the force-free magnet across the mirror plane (1.7e-10) |
| Stages take B where they are rather than on the start's side | both electron flights (`StepSizeUnderflow`) |
| The start's side taken at the ion rather than a hair ahead | the 2 mm magnet (kicked 44 per cent too hard) |
| Approach cap (`MagnetHalfwayLength`) removed | the 2 mm magnet (skipped) |
| Turning-point cap left on in a magnet | both E x B tests |
| Validator's diffusive refusal removed | `TheDiffusiveModeRefusesAMagnet`; `einzel run` is still refused, by the engine backstop in `DriftDiffusion.Run` |
| Magnet-edge landing removed | the 180 degree sector, the mass dispersion, all three edge-entry flights, the 2 mm magnet, `TwoMagnetsComposeTheirSignsAsAProduct` (see trap 4) |

## What is refused, and why

Only single-ion trajectory integration applies v x B. Every other path that consumes
the field would answer as though the magnet were absent, so each refuses by name.
Two layers: the **validator** refuses the document with `REGIME_INVALID` (exit 2,
CLI-3's regime class - the document is well formed and asks a transport path for
something outside what it can describe, as a driven geometry in the diffusive mode
does) at the magnet's own `/fields/N`, indexed by the document and not by the compiled
list, so a typo in an earlier element does not move the pointer; and
**`MagneticRefusal.ThrowIfMagnetic`** is a backstop in each engine consumer for a caller
that bypasses the validator, raising the same code. `SCHEMA_INVALID` is kept for what is
malformed: a `fringe` on a magnet's region, a phase that changes B, and a `fluxDensity`
declared on an element kind that does not read it (a Wien filter written as one
`uniform` element with a `fluxDensity` used to validate and fly with no magnet; every
other kind-specific property is refused on the wrong kind the same way).

| Path | Refused where | Why it cannot honour B |
| --- | --- | --- |
| Space charge (`direct`, `pic`) | validator; `PacketIntegrator.Fly` | the packet integrator evaluates E alone |
| Diffusive transport mode, wholly or in any sequence phase | validator (asked over every mode the run uses); `DriftDiffusion.Run`, `MixtureDiffusion.Run`, `PonderomotiveField` | drift-diffusion takes a mobility times E; a magnetised (anisotropic) mobility is not built |
| A mode-changing sequence's time-shifted legs | covered by the diffusive refusal; `TimeShiftedField` refuses to wrap a magnetic field | the wrapper answers `ITimeVaryingField` only |
| `einzel compare` | `ModeComparison.Execute`, at `/fields/N` | one side is the diffusive mode |
| A sequence phase that changes B | validator, at `/fields/N/fluxDensity` | there is no time-varying B; following it would mean freezing it |
| A fringe on a magnetic region | validator, at `/fields/N/region/fringe` | a ramped B is not curl-free across the ramp |

Every other consumer of the assembled field hands it to `TrajectoryIntegrator` and so
honours B unchanged: `einzel run` (single ion and cloud), `test`, `preview`,
`estimate`'s pilot flight, every figure of merit and therefore every study (`scan`,
`sweep`, `optimise`, `boundary`), `regime` (its path is flown with collisions and B
together), the viewport and `render section`/`still`/`animation`, and MCP
`model_preview`. Two consumers read the field rather than fly through it, and report B
beside phi and E: **`einzel probe`** (`fluxDensityT` on every row, `Bx_T By_T Bz_T` in
the table and the CSV) and **`einzel export --composite`** (a `B_T` vector array, named in
the header). Without a magnet both produce exactly the output they did before. The list was made by enumerating every caller of
`FieldAssembly.Build`/`BuildReported` and every caller of
`TrajectoryIntegrator.Integrate`, `PacketIntegrator.Fly`, `DriftDiffusion.Run`,
`MixtureDiffusion.Run` and `new TimeShiftedField`/`PonderomotiveField`.

A source launched **away** from its detector validates when a non-zero magnet is
declared (a 180 degree sector does exactly that), and an ion **at rest** in a pure
magnet is refused, because B does no work.

## Not built

- **Solved or imported B.** No magnetic scalar potential solve from pole pieces, no
  B map read from a file. A map would implement `IMagneticField` as it stands.
- **Time-varying B.** The seam is a second interface deriving from `IMagneticField`
  with a `MagneticFieldAt(position, timeSeconds)` overload, as `ITimeVaryingField`
  derives from `IElectrostaticField`, tested for in `DormandPrince54.Derivative`.
- **Fringe fields.** The region edge is a sharp cut-off; a real magnet's fringe
  (and its focusing at an inclined edge) needs a field that is both divergence- and
  curl-free, which a scaled uniform B is not.
- **Permeable materials, coils, currents** - nothing makes B; it is declared.
- **Relativistic motion.**
- **Magnetised transport other than single ions**: space charge in a magnet, a
  magnetised mobility for the diffusive mode.
- **Drawing the magnet.** The viewport and figures draw trajectories through a
  magnetic model correctly but draw no outline for the magnet's box.
