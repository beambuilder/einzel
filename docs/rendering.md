# Rendering

`Einzel.Render` turns a model into a line drawing. It sits below the shell, has no
UI types, and needs no display: RND-1 makes rendering an engine capability rather
than a shell feature, so `einzel render` and a future figure composer are peer
consumers of one pipeline.

The Linux CI job is the check. `Einzel.Render.Tests` draws real device templates as
SVG and PDF on a runner with no display, no window manager and no font server.

## Why vector, and why it matters here

This is a real gap in the incumbent. SIMION's output is essentially screenshots, so
a publication figure gets redrawn by hand in an illustration program - and the
redrawn figure then drifts from the model it depicts, silently, because nothing
connects them any more.

So the pipeline produces **paths, not pixels** (RND-3), and **text stays text**
(RND-6): every label is a real text run in both formats, so a figure can be
relabelled for a different venue without regenerating it. Nothing converts a glyph
to an outline.

A **render spec is text** (RND-2) - a small JSON file in `figures/`, versioned
beside the model, naming the model it draws. The figure in a paper is regenerable
from the repository rather than being a file someone once exported.

```json
{
  "renderSpecVersion": "0.1",
  "kind": "section",
  "model": "../models/quadrupole.json",
  "widthMm": 90,
  "equipotentials": 12,
  "caption": "As it will appear in the paper"
}
```

`einzel render section figures/quad.json` draws it; `einzel render section
models/quad.json` draws a model directly with the defaults. Either file may be
handed to the verb, and which one it is comes from **what it declares** -
`renderSpecVersion` versus `schemaVersion` - not from which folder it sits in. A
spec detected by its folder is a spec broken by being moved.

## Conductors are drawn from their own signed distance

Nothing in this assembly knows what a mirror or a quadrupole is (architecture
invariant 2). **An electrode outline is the zero level set of its signed distance**,
which the model format already requires every electrode to supply because the
solver and the ion absorber both need it.

So one marching-squares routine draws every conductor there is - a rod, a plate, a
ring, a sphere, a box - and it is the *same* routine that draws equipotentials,
because an equipotential is a level set too. A shape added to the model format
needs no change here at all.

## The same argument in three dimensions, for the viewport

`Surfaces` is the level set as a triangle mesh, which the shell's 3-D viewport draws
(§16). It sits here rather than in the shell for RND-1's reason - rendering is an
engine capability - and it earns that placement by being testable headlessly on
Linux, which is where its tests run.

**What differs between symmetries is not the shape but what the solve claims about
the third dimension**, and treating all three alike would be wrong rather than
merely slow:

| The solve says | The conductor is | Because |
| --- | --- | --- |
| a cross-section | an **uncapped prism** | the geometry repeats along z, so the electrode extends past anything drawn - capping it would draw an end the model does not have. Where it stops is a drawing convention and the caller says so (GRD-12) |
| an axisymmetric half-plane | a **solid of revolution** | SYM-1: the half-plane is a geometry that repeats all the way round, so a rectangle there is a tube in space |
| a volume | a **surface extraction** | nothing is claimed, so nothing may be assumed |

**Surface nets rather than marching cubes.** One vertex per cell at the mean of that
cell's edge crossings, one quad per sign-changing lattice edge: watertight by
construction, no 256-case table, and a vertex that sits where the surface is rather
than on a cell edge. It rounds a true crease by about a cell, and §17 is explicit
that the viewport is screen tuning rather than an artifact - the publication figure
is the vector section above, drawn at full sharpness.

**Orientation comes from the field, not from the winding.** Every producer emits
triangles in whatever order falls out; one pass then sets each normal to the
gradient of the same signed distance that defined the surface and flips any triangle
that disagrees. That is exact, and it is *necessary* here rather than tidy: the
segments marching squares emits are deliberately undirected (see below), so a
revolved or extruded profile has no winding to inherit.

| Check | Result |
| --- | --- |
| A sphere's volume at 24 / 48 / 96 cells | **0.99038 / 0.99760 / 0.99940**, improving under refinement |
| Its area, likewise | 0.99484 / 0.99870 / 0.99968 |
| Edges shared by exactly two triangles | **all 11,970** |
| Worst normal against the true outward radius | **1.000000** |
| A revolved tube against Pappus at 16 / 64 / 256 facets | 0.97450 / 0.99839 / **0.99990**, inscribed so approaching from below |
| An extruded prism's open ends | exactly 8 boundary edges |

The volume is the sharpest of these because it tests three things at once - that the
surface is closed, that every triangle is wound outward, and that the vertices sit
where the surface is. A hole, a flipped patch or a systematic offset all move it.

**Two things it got wrong first.** A **1 mm plate vanished**: sampled over the whole
solve domain at 48 cells a cell is 1.25 mm and the plate falls between lattice
planes, so the three-dimensional example produced no conductors at all, silently.
The extraction runs over the electrode's own bounding box now, asked of the
electrode rather than switched on its shape. And a **closed profile carries a
duplicate point** - marching squares repeats the first point at the end - so a prism
built straightforwardly from one has a seam down its side, invisible on screen and
not invisible to anything asking whether the surface is closed.

## Decimation is a guarantee, not a hint

RND-5 requires a stated geometric tolerance and ACC-7 sets the default at **0.1% of
the drawing's extent**. GRD-12 requires it recorded in the artifact, so it appears
three times: in the file's own metadata, in the `--json` result, and stamped on the
page.

Ramer-Douglas-Peucker, because the guarantee is what makes the number quotable: no
discarded point lies further than the tolerance from the retained line. The cheaper
radial and nth-point schemes reduce point counts without bounding anything.

| tolerance | 4,000 points reduce to | worst deviation |
| --- | --- | --- |
| 0.01 mm | 577 | 0.010000 mm |
| 0.05 mm | 243 | 0.049892 mm |
| 0.20 mm | 104 | 0.199790 mm |
| 1.00 mm | 43 | 0.956524 mm |

Tight against the bound rather than slack under it, which is what says the bound is
the thing being respected rather than a number that happens to appear nearby.

**The point-to-segment distance is clamped, and a reflectron is why.** An ion that
turns round comes back along nearly the same line, so its turning point sits almost
on the chord between the two ends of the flight. Measured to the *infinite line*
through them it looks redundant and gets decimated away, leaving a figure of an ion
that flew straight through a mirror. Pinned by a test.

**Sample finely, then decimate.** The trajectory is flown twice: once at the model's
own cadence to learn how long the flight is, then at a cadence chosen from that.
Drawing whatever the model happens to sample for its VTU export gave the einzel lens
a three-segment curve through a focusing element, which is a drawing of the sampling
interval rather than of the optics.

That a ray through the lens still decimates to three points is the *right* answer:
straight in, kink at the lens, straight out is what a thin lens does to a ray.

## A tainted result is visibly tainted

GRD-2 carries warnings to every surface and RND-11 requires a qualified result to be
**visually distinguishable** in rendered output. A figure is the artifact most likely
to be shown to an audience with none of the uncertainty apparatus attached - RND-10
makes the same argument for video - so metadata alone is not enough.

A figure drawn from a field that missed its solve tolerance gets a hatched rule the
width of the page and a `QUALIFIED` line naming the warning code. Hard to crop out by
accident, and it survives being pasted into a slide.

## Both formats come from one scene

`Scene` is paths and text runs in page millimetres. SVG and PDF are two writers over
it, so a figure cannot come out different in one format than in the other - which is
the failure mode of a pipeline where each format re-derives the drawing.

**The PDF writer is hand-written, and LIC-1 is the reason** as much as weight. The
capable PDF libraries in .NET are variously GPL, AGPL, or dual-licensed in a way that
has to be re-checked per release, and a figure writer is not where this project wants
a licence question. What a section figure needs - paths, strokes, fills, and text in
a base font - is a few hundred lines of a format stable since 1993. Text is set in
Helvetica, one of the fourteen fonts every reader must have, so nothing is embedded.

Its cross-reference table is checked by a test that walks every offset and asserts it
lands on the object it claims. That is the part a reader refuses on, and it is the
part most likely to be wrong in a writer built by hand.

## An axisymmetric solve is drawn as a whole section

A cylindrical solve is a half-plane in (axial, radial), and a section through the
axis of one shows **both halves** - a ring is two conductors on the page, not one.
That is SYM-1, declared on the solve, so it is symmetry knowledge rather than device
knowledge; the renderer still has no idea what the rings are for.

The field needs no special handling: a cylindrical solve is already wrapped as an
axisymmetric field, which samples at the radius of the point it is asked about and so
answers for a negative one already. Only the electrode signed distances are mirrored.

## What marching squares got wrong first

Cases 1 and 14 both emit the segment `(Left, Bottom)` - they are complementary cells,
and a consistently oriented contour would traverse them in opposite directions. So
**segment orientation is not consistent**, and a joining pass that matches only
head-to-tail breaks the contour at every such crossing.

A rectangular conductor came out as four separate runs instead of one. That looks
identical on screen until the path is filled or dashed, and it is not free: joining
undirected took the einzel lens from 10 conductor runs to 6 (three tubes, mirrored),
the quadrupole's equipotentials from 338 paths to 28, and its PDF from **112 KB to
13 KB** for the same drawing.

Endpoints are bucketed by rounded coordinate so joining is linear rather than
quadratic in the segment count; a contour over a fine sample grid is tens of
thousands of segments and the quadratic form is minutes.

## A density is drawn where a trajectory cannot be

RND-8 and TRN-2 forbid drawing lines through a diffusive region: above about
10^-2 mbar the model computes a density field and no trajectories exist, so lines
would depict something the model never produced. The renderer asks the transport
mode `ProducesTrajectories` rather than inferring it from the pressure, and draws
none when the answer is no.

On its own that rule is entirely negative, and it was: the mode's principal output
had no drawing at all, so the honest figure of a funnel at a millibar was an empty
box with a warning on it. Density contours are what goes there instead.

**Decades, not even fractions.** A density spans orders of magnitude - the core of
a packet and its tail differ by six - so evenly spaced levels put every line inside
the core and draw the extent, which is the part a reader most wants, not at all.
Six levels at 10^-1 through 10^-6 of the peak, fainter with each decade, because
the eye reads line weight as concentration whatever the caption says.

**The levels are recorded in the figure's provenance.** A density plotted without
them is a shape rather than a measurement, which is GRD-12's argument applied to a
contour set.

**Lines rather than filled bands.** Marching squares gives runs, and a filled band
needs them nested into rings and holes - a different algorithm whose failures are
silent, since a hole drawn as a solid reads as a *denser* region rather than as a
bug.

**An empty density is named.** A run whose ions have all reached a boundary leaves
a residue many orders below one ion in the whole domain, and contouring that draws
the shape of the round-off. Below the floor nothing is drawn and
`render.density-empty` says so, along with the change that would produce a picture:
drawing nothing and saying nothing is indistinguishable from a figure where the
density was never computed at all.

The density is passed in rather than computed here. Running the transport is the
command layer's job - it owns turning a model document into a density problem - and
a renderer that could do it would be a renderer that decides how long a run lasts.
Where the transport refuses outright, the figure is still drawn from the geometry
and the field, and the warning says which of the two it got.

## An animation declares how it compresses time, and says so on every frame

RND-7 is unusually emphatic, and the whole design follows from it: an animation
"declares an explicit non-linear time mapping — playback rate per sequence phase — and
the current rate is displayed on screen throughout playback. **Neither part is
optional.** This is the animation equivalent of GRD-1: the artifact may compress, but it
may not hide that it compressed."

The reason is §22's own: six orders of magnitude of timescale cannot be shown honestly
at one rate, and *a viewer cannot detect the compression*. An ion spends nanoseconds
turning round in a mirror and hundreds of microseconds drifting. An animation that skips
the first to keep the second watchable has removed the part the instrument was designed
around, and nothing on screen would say so.

### The requirement enforces itself through the interface

`einzel render animation` takes **a render spec, never a bare model**, and there is no
`--rate` flag. A model document has nowhere to declare a time mapping, so the only way
to ask for an animation is to have written one down. A convenience flag would have made
the hidden-compression case the easy one.

`--fps` *is* offered, because a frame rate is a property of the playback device rather
than of the physics and changing it changes no claim the animation makes.

```json
"animation": {
  "framesPerSecond": 10,
  "phases": [
    { "until": {"value": 4.0,    "unit": "us"}, "rate": {"value": 4.0, "unit": "us/s"}, "label": "inbound" },
    { "until": {"value": 6.2,    "unit": "us"}, "rate": {"value": 0.5, "unit": "us/s"}, "label": "turn-around" },
    { "until": {"value": 10.1805,"unit": "us"}, "rate": {"value": 4.0, "unit": "us/s"}, "label": "outbound" }
  ]
}
```

On the shipped reflectron that is **1.000 s, 4.400 s and 0.995 s of playback**: the
turn-around is a fifth of the flight and **69% of the film**. Sixty-five frames, and the
frame at playback 1.000 s shows exactly 4.0000 µs.

### Units on the rate, for the reason units are on everything else

The rate is *simulated time per second of playback*, written `us/s`, `ns/s`, `ms/s`.
Dimensionless — it is a time over a time — and what makes it a *rate* is that the
denominator is a second of playback rather than a second of flight, which no dimension
can carry. So the field is called `rate` and the unit spells the playback second out.

The stamp gives **two readings of the one number**, because they answer different
questions:

> `t = 5.000 µs · turn-around · 500 ns of flight per second of playback — 2,000,000x slower than real time`

The time-per-second is what converts anything on screen back into flight time and
carries a unit, which is what GRD-1 asks of every quantity here. The slow-down factor is
the intuition, and alone it says nothing about how long the flight is. The unit is picked
from the magnitude rather than fixed, because one animation may span nanoseconds of
turn-around and milliseconds of trapping.

### Frame times are computed, never accumulated

Each frame's instant comes from its own playback time by one lookup and one multiply. A
phase whose playback duration is not a whole number of frames would otherwise push a
fractional frame of error into every phase after it, and a six-phase animation would
drift visibly against its own declared mapping.

Two details that are decisions rather than details. **The final frame is forced onto the
end**, because for a flight the last instant is precisely the one a reader wants — the
arrival, the ejection, the packet at the detector — and a frame grid that is not a whole
number of frames long otherwise stops short of it. And **a frame landing exactly on a
boundary announces the incoming rate**: it shows the boundary instant and is followed by
a frame's worth of playback at the new speed, so naming the rate that has just stopped
applying would be naming the wrong one.

### Fly once, draw many

A section figure solves the field and flies the ion inside the renderer. For three
hundred frames that is a multigrid solve per frame, which is unaffordable — and worse
than unaffordable, because two frames that flew separately are two frames that can
disagree about the flight. `FramePlan` is what a caller drawing many figures of one model
computes once: the field, its warnings, the whole trajectory, and the banner.

### The camera does not follow the ion

The first version handed each frame **the part of the flight drawn so far**. An analytic
model takes its extent from the flight, so every frame chose its page from its own
prefix: the scale changed frame to frame and the ion sat pinned to the edge of a box that
grew to meet it. It reads as a camera tracking the ion rather than as an instrument being
flown through, and *nothing about any single frame reveals it*.

So the plan carries the whole flight and `UpToSeconds` truncates it for drawing. The test
that pins this asserts one page across all frames — and **its first version passed with
the bug restored**, because it used the einzel lens, a solved geometry whose extent comes
from its declared domain and never touches the flight. Moved onto an analytic reflectron
it fails immediately. Third time tonight that a test exercised a path not containing the
line under test.

### The field moves, and the cycle closes

An animation of a driven structure drew the same instant on every frame. A driven field
implements the time-free `IElectrostaticField` as well as `ITimeVaryingField`, and
**answers the time-free one at t = 0 without failing** — so the renderer got a field, a
plausible one, and the same one every time.

That is the fourth appearance of one defect: `einzel solve` reporting the DC pattern of
a driven geometry, the diffusive mode stepping a density through a snapshot of the RF, a
`SuperposedField` becoming a snapshot when a driven member was summed into it, and now
this. **A time-varying quantity reached through a time-free interface does not fail; it
answers at an arbitrary instant.**

The instant is now declarable — `atSeconds` on a render spec, defaulting to the launch —
and an animation supplies each frame's own. A section of a driven model carries
`render.field-at-instant` either way, because a figure of a driven structure is a frame
of a film whether or not it is drawn as one.

The check is exact. A 1 MHz sinusoid has a 1 µs period; over one period, at the quarter
points:

| t | 0 | T/4 | T/2 | 3T/4 | T |
| --- | --- | --- | --- | --- | --- |
| equipotential paths | 20 | **0** | 20 | **0** | 20 |

The drive passes through zero at the quarter points, so there is no field to contour. At
`T` it is the **same drawing as at 0, to the last bit** — a sinusoid is exactly periodic
and nothing between the field and the page is not. At `T/2` the two rod pairs have
swapped sign, so it is neither.

### The contour levels are fixed once, or they flicker

Levels are spread over the field's range, and a driven field's range changes through the
cycle. Taken per frame they would be spread over whatever range that instant happened to
have — and **at a zero crossing that is rounding noise**, so the frame would fill with
contours of nothing. The same defect as a page chosen per frame, in the other axis.

`SectionRenderer.PotentialRange` samples a couple of dozen instants across the animation
on a coarse grid — coarse is enough, because the extremes of a Laplace solution are on
its boundaries, and this is a range rather than a contour. Fixed once, the contours move
because the field moves.

### A diffusive model is animated as a moving density

RND-8 forbids drawing lines through a diffusive region, so an animation of one was
refused outright — and rightly, while a run reported only the density it *ended* with:
the frames would all have been the same box, and a film that shows motion never computed
is worse than no film because it looks like one.

With the density recordable at chosen instants the frames have something that moves. The
command layer runs the transport once with the frames' own instants as its snapshot list
and hands the renderer the results, exactly as the section path already does — running
the transport is not the renderer's job.

On the corpus drift tube, 33 frames over 200 µs:

| frame | contours | packet centre | packet width |
| --- | --- | --- | --- |
| 0 | 12 | 21.98 mm | 23.97 mm |
| 8 | 10 | 43.82 mm | 42.29 mm |
| 24 | 10 | 90.29 mm | 59.48 mm |
| 32 | 10 | 100.32 mm | 39.51 mm |

It drifts, it spreads, and at the end it narrows again because the leading edge has been
collected. All three are things a trajectory cannot show.

**The contour levels are anchored once, and that matters more here than the page did.**
Density contours are drawn at decades below the peak, and a diffusing packet's peak falls
as it spreads. Anchored per frame the levels would fall with it, the contours would stay
the same size, and **a film of a packet spreading would show a packet doing nothing** —
not flicker, but a lie. Anchored to the peak over the whole animation, later frames show
*fewer* contours, because the density really is lower. The levels are identical on every
frame and the test asserts that directly.

### The head is marked, and interpolated onto the instant

A polyline that grows says only where the ion has *been*. The head is a small closed
polygon — the scene has paths and text and nothing else, which is what lets one scene go
to SVG and to a hand-written PDF without either backend knowing about shapes.

It sits at the instant rather than at the last recorded sample. An adaptive integrator
keeps points fastest where the physics is hardest, so a marker snapping to samples would
stutter in exactly the places an animation exists to show. Interpolated linearly, which
is what the drawn polyline already is, so the head sits exactly on the line it
terminates.

### A manifest beside the frames

`frames.json` records the model and its hash, the frame rate, the playback duration, and
for every frame its file, its playback time, its instant, its rate and its phase. Every
frame carries its rate on the page, which is what RND-7 requires; the manifest carries
the whole schedule, so the compression can be audited rather than trusted one frame at a
time. It is also what a player needs: frames are equally spaced in *playback* time and
not in flight time.

### What it refuses

- **A spec with no phases**, a phase that does not advance, a non-positive rate, a
  non-positive frame rate.
- **A mapping a diffusive run cannot reach.** Repeating the last density for the frames
  past the end would show a packet sitting still, which is what a finished run looks like
  and is not what it is.
- **A spec with `trajectory: false`.** The geometry and the field are identical on every
  frame, so with the ion left out the sequence is one drawing repeated - the same
  argument as the diffusive refusal, one step further in.

And two things it warns about rather than refusing, because both are legitimate to ask
for and neither looks like a choice: `animation.past-arrival`, where the mapping outlives
the flight and the last frames show an ion that is not stationary but finished; and
`animation.stops-short`, where it ends in mid-flight, which reads as a loss.

### No video, and that is LIC-1

Nothing here rasterises, and a GPL dependency is forbidden in the default build — ffmpeg
is exactly what would be reached for. What comes out is a numbered sequence of vector
frames; assembling them is an out-of-process step with a tool the user supplies, so its
absence degrades a feature rather than blocking the platform.

## An analytic flight sets its own page

A model with no declared solve domain takes its extent from the instrument's own points,
and those used to be the source and the detector alone. **In a reflectron they are the
same point** — the ion is caught where it launched — so the page was a box a tenth of a
millimetre across while the ion travelled 1.3 m into the mirror and back.

The scaffolded reflectron, which is the first thing anybody renders, drew its turning
point at **x = 105,080 mm on a 160 mm page**. It had been doing so since sections were
built, and no test caught it because every render test uses a device template, and every
device template declares a solve domain.

The flight is now included in the extent, and the pad is taken from the extent actually
gathered rather than from the separation of source and detector — which is zero in any
instrument that catches the ion where it launched. It costs nothing: the trajectory had
to be flown to be drawn, and it is now flown *before* the page is chosen rather than
after.

## A density can be drawn while it is still moving

A diffusive run reports the density it **ended** with, which for a model whose ions have
all arrived is an empty box - correctly, and uselessly, because the picture worth having
is the packet in flight. The only way to get one was to shorten `maximumFlightTime`,
which gets a packet by throwing away everything after the moment being looked at.

`einzel render section --at-us <t>` records the density at that instant and lets the run
finish. On the corpus drift tube:

| | density contours | packet centre on the page |
| --- | --- | --- |
| at the end | **0** | - |
| `--at-us 50` | 10 | 49.5 mm |
| `--at-us 150` | 11 | 102.9 mm |

It drifts and it spreads, both visible. The unit is in the flag's name, as `--width-mm`
already does it: a bare `--at` would be ambiguous between microseconds and seconds by a
factor of a million, and that is the rule that makes a bare number a validation error in
a model document.

**The instant recorded is not quite the instant asked for, and the figure says both.** A
diffusive step is set by a stability limit, and cutting it to land exactly would change
the step sequence and therefore the answer - a high price for an offset of one step. So
the snapshot is taken at the first step at or after what was asked for, and the
provenance block carries `density at t = 50.1302 us, asked for 50 us`.

**Recording does not perturb what it records**, which is asserted rather than assumed:
the same run with and without snapshots is bit-identical in step count, collected ions
and every node of the final density. The snapshots are clones taken between steps, so
there is nothing for them to change.

An instant past the end of the run is **absent** rather than filled in with the final
state. A density that was never computed is not the density at that instant, and
silently substituting the last one would make a figure of a finished run look like a
figure of a running one.

## A dimension is measured, never written down

The memo's own figures are line drawings *with dimensions*, and a section without them
says what the instrument looks like and not how big any of it is.

What a dimension declares is **the two points it spans**. The length is the distance
between them, computed when the figure is drawn:

```json
"dimensions": [
  { "from": {"value": [-100, 0, 0], "unit": "mm"},
    "to":   {"value": [0, 0, 0], "unit": "mm"},
    "label": "drift", "offsetMm": 10 },
  { "from": {"expression": ["0", "0", "0"]},
    "to":   {"expression": ["turningDepth", "0", "0"]},
    "label": "penetration", "offsetMm": -8 }
]
```

`label` names the span - "drift", "bore". **It does not carry the value.** A typed number
would be a second statement of something the model already says, and the two would part
company at the first parameter change, which is exactly what a dimensioned drawing exists
to prevent.

**And the points may be expressions**, over the same parameters the model is written in.
Section 9's rule for a model - "every placement is a parametric expression, never a baked
number" - is not weaker for a drawing of it. Changing `turningDepth` from 50 mm to 80 mm
and re-rendering the *same figure spec* gives `penetration 50 mm` and then
`penetration 80 mm`, with no edit in between. That is the assertion the test makes: one
spec, two models, two measurements.

Drawn to the ordinary drafting convention - extension lines starting clear of the feature
and running a little past the dimension line, a dimension line offset perpendicular,
arrowheads, and the measurement above the line on the side away from what it measures.
The convention is what makes a drawing readable by somebody who has never seen this
program. The offset is signed, so two dimensions over one feature can go to opposite
sides.

The unit is chosen from the magnitude, because one figure may dimension a 300 mm drift
and a 50 µm channel and neither is readable in the other's unit. A span that projects to
a point on the section plane is skipped rather than drawn as a dot, which would read as a
dimension of zero rather than as one seen end on.

## The conductors can leave the program, as a mesh

`einzel export <model> --mesh` writes the electrode surfaces as Wavefront OBJ, one named
object per electrode with its potential as a comment above it.

**The extraction already existed and could not get out.** `Surfaces.FromSignedDistance`
builds each conductor from its own signed distance and its tests run on the Linux runner
against a sphere's area and volume, Pappus, and watertightness — and the only consumer was
the Windows viewport. So the one artifact that lets an external renderer draw the geometry
was reachable only through the shell, which is invariant 1 pointing the wrong way: nothing
below the shell may depend on it, and here something useful below the shell was usable only
through it.

**OBJ rather than a VTK format, and the reason is the audience.** This engine already writes
ImageData and UnstructuredGrid, and consistency would argue for PolyData — but those exist to
be read by ParaView for analysis and this exists to be read by a renderer for a picture. OBJ
names each object, so every electrode arrives under the name the model author gave it, which
is the same name a loss itemisation or an error message uses. ParaView reads it too, though
it merges the objects. Hand-written for the reason the PDF writer is (LIC-1).

**Millimetres, stated in the header**, because OBJ carries no unit and most renderers treat
one unit as one metre — which would make a 625 mm analyser 0.6 units long. §9 refuses an
unlabelled quantity everywhere else and a mesh file is not an exception.

**A latent bug found by validating the file rather than trusting it.** OBJ keeps *separate*
index counters for positions and normals, and the writer advanced one for both. That is
indistinguishable from correct while every object carries normals — which is every object
this engine produces today — and silently wrong from the first one that does not: every
later object would point at normals belonging to something else. No parse error, no missing
geometry, just shading that is quietly of a different solid.

It is worth recording *how* it surfaced. The file was reported as not opening, and the first
instinct was to look for a malformed line. There was none — the file validated on every
structural check — and the actual causes were a wiped temp directory and a `.einzel` folder
that Blender's file browser hides. **The bug was found while proving the file innocent**,
which is the argument for validating an output format against the format rather than against
the fact that one consumer happened to accept it.

## Sub-cell geometry was lost a second time, through the aspect ratio

The shell viewport once produced **no conductors at all, silently**, because a 1 mm plate is
thinner than a cell of a 48-cell grid over the whole solve domain. That was fixed by
extracting over the electrode's own bounding box — and the same failure came back through a
ratio nobody had thought about.

**An analyser's stripe is 4 mm thick and 635 mm long.** Its own bounding box is therefore as
badly proportioned as the domain was: 48 cells across the longest span gives 15 mm cells, the
metal falls between two sample planes, and the surface is empty again. Worse, the padding that
keeps a surface strictly inside its box was 8 per cent of the *largest* extent applied to
*every* axis — 50.8 mm of padding onto a 4 mm board, inflating its thinnest direction
twenty-six-fold before the sampling even started.

**The obvious fix causes the opposite failure.** Taking the step from the *thinnest* span
resolves the slab and then tessellates its length at the same spacing: 72,000 triangles for
what is a box, and sixteen of them made a **77 MB** file. Both failures are silent, in
opposite directions, and neither raises anything.

So each axis is resolved on its own — `clamp(cells × span / longest, 4, cells)` — with the
padding a fraction of each axis's own extent. The cells are then not cubes, which costs
nothing, because surface nets works on a lattice and the spacings were already carried
separately. **On an isotropic shape every axis lands on the requested count and the mesh is
bit-identical to what it was**, which is what keeps the sphere checks meaningful. The Astral's
sixteen stripes went from nothing, to 1.16 M triangles, to **16,320 in 0.89 MB**.

The rule: **a resolution set by one scalar over a shape with three independent extents will
mis-serve some shape.** Which one depends on which extent the scalar was taken from, and
every choice is wrong for something.

## An extracted surface is eroded by about half a cell, and that is visible

A conductor is drawn as the zero level set of its signed distance, and surface nets places
one vertex per straddling cell rather than on the surface itself. So a plate comes out
**about half a cell short at each end**, and on an electrode that gets few cells the effect
is easy to see and easy to measure off by mistake.

On the shipped Astral, whose stage-1 plates are 20 mm long inside a box 935 mm long, the x
axis lands on the floor of 4 cells at 5.8 mm each: the plate meshes at **16.94 mm against
20 declared**, and the *gaps* between stages therefore read about 6 mm wider than they are.

**It is a drawing convention and not a dimension** (GRD-12), the same status as the depth a
cross-section's prism is drawn to. A figure that needs a dimension should carry a declared
one, which is measured from the model rather than from the picture — that is what
`dimensions` is for. Raising the per-axis floor tightens it at a cost in triangles, and the
right floor is a judgement about what these meshes are for rather than a number to derive.

## A section that cuts no metal says so

A figure of an instrument with no instrument in it, and the only sign was a missing key in
the path counts. Three states drew exactly nothing and looked identical: the plane misses the
metal, the device has no metal on this plane, and the extraction failed.

`render.plane-misses-conductors` now fires when a model declares conductors and none of them
intersect the plane, **and it reports how far away the nearest one is** — which is the
actionable half. "No conductor lies on this plane" sends a reader looking for a bug; *"the
nearest of them is 1 mm from this plane"* tells them they aimed at a 20-to-24 mm board and cut
at 25, which is exactly the mistake that prompted this.

**It is not a defect in the device.** Any stripe-on-boards analyser has all its electrodes off
the plane the ion flies in, so the default section — the one containing the trajectory — is
correctly empty of metal. The plane is moved with a render spec's `plane` block.

## `render still` - the viewport's picture, as a PNG

```bash
einzel render still models/mirror.json --view iso --width-px 1600 --height-px 1000 [--see-through] [--no-scale] [--out f.png] [--json]
```

A shaded three-dimensional raster, headless, and **the picture the interactive viewport
shows** rather than one like it. That is the whole reason it exists: an agent could run
every analysis the window runs and could not see what the window shows, so a person
looking at a viewport and an agent reasoning about the same model were looking at
different things (AGT-2's missing half).

**One composition, two consumers.** The scene is `ViewportCommand`'s; the colors, the layers
and their order, which are translucent and whether they hide what is drawn after them, and
the lighting are `ViewportPicture.Compose`; the camera is `Framing`. The window's GL control
uploads exactly that composition and builds its fragment shader from the same lighting
constants; the still hands it to `Rasterizer`. Before this, every one of those decisions
lived inside the GL control, where a still would have had to make them again - and two
copies of one decision are how `run` and `test` came to disagree twice.

**Where each half lives is UI-1's.** The shell may reach the engine through
`Einzel.Commands` alone and may not use `Einzel.Render` types - checked on the compiled
assembly - so the decisions live in the command layer and `Einzel.Render` got a rasterizer
that decides nothing: triangles, lines, colors and a matrix in; pixels out, with a depth
buffer, per-pixel lighting from interpolated normals, 2x2 supersampling, and the window's
blend and depth-write rules. About three hundred lines, managed, no GPU, no native
dependency. The PNG writer is hand-written over the base library's zlib, for the reason the
PDF writer is (LIC-1).

**Drawn in horizontal bands, because a whole supersampled canvas does not fit.** A sample is
three doubles of color and one of depth, 32 bytes, so the largest still the verb accepts -
8192 pixels a side - needed 8.6 GB at once and ran out of memory, reported as an internal
error. The rasterizer now draws one band of about four million samples at a time (134 MB)
into one reused buffer. **The bytes are identical to a single pass**: each sample's color
depends only on what is drawn over it and in what order, every band draws every layer in
order, and sample positions stay in the whole picture's coordinates so the arithmetic is the
same - only the row a band stores into moves. Asserted byte for byte down to one-row bands,
with a translucent layer over crossing triangles and lines. Measured: an 8192-pixel still of
the einzel lens takes 11 s at a peak of 381 MB.

**Orthographic**, because the viewport is - an ion-optics drawing is read for where things
are along the axis. RND-4 says "perspective"; the disagreement is SPEC.md Amendment 54.

**Fitted to what the view sees.** The camera used to frame half the bounding diagonal, which
never jumps when the view turns and put a 767 mm by 30 mm mirror pair across the middle third
of the picture. It now fits the box each named view sees, field lines included, as the WPF
viewport always did.

**Provenance travels in the file** - `iTXt` chunks carry the engine and solver-behaviour
versions, the model and its hash, the view, and every warning the viewport earned, in UTF-8
because warnings quote microseconds. **A validity violation hatches the bottom of the
picture** (RND-11). What it does not draw is RND-11's `QUALIFIED` line naming the code: a
raster needs glyphs, and the only ones here are the ruler's (below) - digits, units and axis
letters, not the alphabet a warning code is written in - so the codes are in the text chunks
and on stderr instead.

**What it cannot check** is the window's own GPU half - shader compilation, uploads, depth
state - which needs a GL context. The still shares every decision with the window and none of
its pixels; the ANGLE dialect defect in `docs/shell.md` is exactly the kind of thing only the
window can show.

### A ruler in the corner, true in every view

Every still carries a scale in a corner: **one arm along each model axis, all the same
round length, projected with the matrix the picture is drawn with.** The length is the
largest 1, 2 or 5 times a power of ten that keeps the longest arm under 0.16 of the picture's
smaller side, and it is written once under the arms, in the unit that keeps the number small
(`10 mm`, `500 µm`, `2 m`), with each arm's axis letter at its tip.

**A ruler in the scene rather than a bar on the screen, and the angled view is why.** In the
side, top and front views the camera looks straight down a model axis, so a flat bar would
be exact - and the ruler is one there: an L of two arms, with the axis pointing at the viewer
left out. In the iso view each axis is foreshortened by its own amount - x by 0.87, y by 0.91
and z by 0.63 at the shipped angles - so a flat bar would state one scale for three. Arms
along the model's axes shorten exactly as the instrument does, which a test pins against the
closed form in the two view angles rather than against the matrix the ruler reads.

**In a corner, because the camera is orthographic**: a segment along an axis has the same
length on screen wherever it sits, so the ruler can stand away from the instrument without being
any less true, and does not widen the frame. **Which corner is chosen from the picture, and it
is not always clear.** The camera fits the instrument with only a few percent to spare, so a
plate seen face-on fills all four corners - the first version, always in the lower left, landed
on the electrode there while this page said it never would, which a review caught. Each corner is
now scored by how much of the drawn instrument it would cover, triangle by triangle and segment by
segment on screen with the equipotentials left out (they cover the whole sampled region), and the
least-covered one wins, lower left on a tie. Where every corner is covered the ruler is drawn over
the instrument. The corner is in the `--json` result (`scaleCorner`) and the PNG's text.
**A ruler that does not fit is left out**: one whose arms, letters and label cannot all sit inside
the picture is not laid out, rather than drawn partly off the edge - a 32-pixel still gets none,
and its `--json` reports none.
**One length for every arm**, because per-axis lengths would invite comparing arms that are
not comparable; with one, the arms' different screen lengths *are* the foreshortening. The
layout keeps above the hatched band a tainted picture carries, and `--no-scale` leaves it out.

**Decided in the command layer (`ScaleIndicator`), like the rest of the picture**, and drawn
by the Avalonia viewport from the same calls, over its GL control and redrawn when the view or
the window's size changes. Both draw it in `StrokeFont`, a line font of digits, units and axis
letters: `ScaleIndicator.Strokes` hands out the arms, the label and the letters as one list of
polylines with their widths, and the still strokes them through the rasterizer's own line
routine while the window strokes them as Avalonia lines - no font file, so no license question
and nothing for a headless runner to lack, and no second font to come out a different size
from the first, which the window's did when it set its text in Avalonia's. The box the layout
fits into the picture holds those glyphs, descenders included: the micro sign in `500 µm` hangs
a third of a digit below the line, and the room once left under the label for digits alone let
it reach a pixel and a half into the margin. A character it cannot spell is
drawn as an empty box rather than dropped, so a gap reads as a gap rather than as a shorter,
different length; a test walks every label the ruler can write, nanometers to kilometers. The
length is in the `--json` result (`scaleMm`, `scaleAxes`) and in the PNG's text chunks,
because the lines are only as good as the pixels they survive being resized into.

### What drawing it found

- **Printed boards were invisible.** An edge profile has no interior, so the level-set
  extraction every other shape uses found nothing, and the planar mirror pair was drawn as
  its end cap alone. Boards are now sheets on the domain edge, over the whole edge because
  that is what `RasteriseEdgeProfile` fixes, in pieces broken at every profile knot and
  colored by the profile at each piece's middle.
- **A reflected solve was drawn as half an instrument.** The field composes `reflectAboutX`
  with the mirror image and the viewport did not, so a mirror pair had one mirror. Both halves
  are drawn now, the image mirrored exactly as the field is (`x -> 2p - x`) with its winding
  reversed so it is not lit from inside.
- **A sheet had no normals.** `Surfaces.Orient` left a zero normal wherever the gradient of the
  signed distance vanished and called the result a flat-shaded facet. On a zero-width cap that
  is every vertex, and neither window shades a zero normal as a facet. It gets the area-weighted
  normal of its facets now, and only where the gradient really vanished.

## Not built

- **Filled density bands, and a color scale.** Contour lines carry the levels in the
  provenance; a filled and keyed plot would carry them on the page.
- **Text in a raster beyond the ruler's.** The line font spells lengths, units and axis
  letters; RND-11's `QUALIFIED` line needs the alphabet. See above.
- **The ruler in the WPF viewport.** The Avalonia viewport draws it; the WPF one has its own
  camera, from Helix Toolkit, and would need that camera's projection carried to the same
  layout.

Also not built for animations: **geometry that moves**. A stage may change what an
electrode holds and not where it is, which is a rule the sequencer already enforces, so
the conductors are the same on every frame by construction. That is correct rather than
a limitation — but a mechanism with a moving part is not expressible at all, in the model
format or here.
