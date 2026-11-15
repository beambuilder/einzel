# The shell

Avalonia, plain `net10.0` with an OpenGL viewport, and §16's eleven views. **Nine exist**:
the model tree, the journal, the 3-D viewport, results by accuracy class, the regime
inspector, the sequence editor, the project view, the extension manager, and the update
notice, which arrived with `Einzel.Update`. It runs where the engine runs - the test CI
builds and runs it on Windows and Linux; macOS is supported by the toolkit and not covered
by those tests, so treat it as untested. Parameters are edited through the session journal
and can be undone, the viewport orbits, pans and zooms with a ruler in the corner, and
Journal, Results, Regime, Sequence, Project and Extensions are tabs beside it.

**There was a second shell, on WPF, and it is gone.** The WPF one came first and was
Windows-only; `Einzel.Shell` was written beside it on Avalonia, carried every view across,
and the WPF project was deleted once nothing was left that only it had. Much of this page
was written about that shell, because the decisions it records - the level-set extraction,
the two colour ramps, RND-8 on the face of the viewport, the camera - are the ones the
current shell inherited or deliberately reversed. Where a section is about the WPF window
it says so. [How the shell came to be on Avalonia](#how-the-shell-came-to-be-on-avalonia)
is the port itself.

**UI-1 is the whole design.** The shell owns layout, input, the interactive viewport and
the update check. It owns no physics, no validation rules, no file format knowledge and
no render output. **Amendment 25** adds the constructive half: every shell action must be
*expressible* as a CLI invocation and journalled as one — so a capability with no command
spelling cannot be added to the window, and a person's session hands over to an agent in
the same vocabulary.

That is not decoration. It has now twice meant a window feature could not be built until
a command existed:

| The view | The command it needed | Why the window could not just do it |
| --- | --- | --- |
| Model tree | `OutlineCommand` | A window that parsed the model to build a tree would grow its own idea of what a model is, and the two would come to disagree |
| 3D viewport | `ViewportCommand` | A viewport that integrated its own trajectories would be a second transport implementation |
| Results by class | `ResultsCommand`, and `AccuracyClass` on the registry | Which of §12's families a figure belongs to is the engine's taxonomy; a window sorting them itself would be a second copy of §12 |
| Regime inspector | `RegimeDiagnostics.MeasureAt` and `RegimeCommand` | Where a regime boundary lies is spec figure 4's, and the numbers had only ever been computed at the worst point in the gas |
| Sequence editor | `SequenceCommand` | Which phases exist and what each holds is compiled from the document; a window reading `stages` itself would be parsing the format |
| Extension manager | `ExtensionCommand.List` | Which extensions are installed, what each declares and what the sandbox does not enforce are the runner's own answers; a window walking `extensions/` itself would be a second reader of the manifest format |

The same argument, arriving five times — and each time the command layer gained the
capability rather than the window keeping it, so an agent is better off for a window having
been built. The one to watch is the in-process path
acquiring an argument the command form has no spelling for; that is the moment the
amendment is being broken, and it will look like a convenience at the time.

## Invariant 1, and the bet it was keeping open

Architecture invariant 1 says nothing below the shell may reference it and every assembly
above it builds and runs on Linux. Two tests enforce it, both running on Linux:
`NothingBelowTheShellReferencesIt` and `TheShellReachesThePlatformThroughTheCommandLayer`.

**The second had to check two different things, and the difference cost a mutation.**
`GetReferencedAssemblies` reports what the *compiler emitted* — what the code actually
uses — so adding a `ProjectReference` to the whole transport engine left no trace in the
metadata and the test passed. UI-1 is about what the shell may reach *for*, not what it
has reached for so far, so the project file is parsed too.

**That invariant is what made the WPF shell cheap to replace**, and the replacement is
what shows it was being kept rather than merely asserted: no capability was trapped in the
old window, because every one of them was already a command. The shell is now the only
project in the solution that references a UI toolkit, and it targets plain `net10.0`, so
there is no longer a `-windows` target anywhere here.

`Einzel.Shell.Tests` targets plain `net10.0` and runs on both CI runners. The WPF shell's
tests targeted `net10.0-windows`, and a solution-wide `dotnet test` walked past them on
Linux - so what they checked was checked on one platform, which for a cross-platform claim
is nobody checking it. There is now no skipped test project in the solution.

## Globalization: the one build setting the shell reverses

`Directory.Build.props` sets `InvariantGlobalization` for everything. The shell reverses
it, because **text layout and number entry in a window are locale-facing**: the window has
to lay out text in the user's own script and show them a number the way their desktop
writes one.

The WPF shell reversed the same setting for a blunter reason, and it is worth keeping
because it is the kind that is found at run time rather than at build time: **WPF cannot
run under it at all** - the font cache constructs `new CultureInfo("en")` while measuring
the first line of text, which throws, and the window dies before it is shown, a
`TypeInitializationException` out of `MS.Internal.FontCache.MajorLanguages`.

What that setting was protecting is unaffected. Its stated purpose is that number and
date formatting must not vary with the host locale, because that would leak into CLI
output (CLI-5) and into golden-file comparisons — and the way this codebase achieves that
is by passing `CultureInfo.InvariantCulture` explicitly at every formatting and parsing
site. The build flag was the belt to those braces, and the braces are what hold.

**The parse is the one that matters.** A person typing a value into the model tree is
parsed invariantly whatever their locale, because the file they are editing is invariant.
`1,5` is refused rather than quietly read as fifteen.

## The model tree

`einzel outline` returns the declared parameter surface: value or expression, unit,
bounds, description, what each resolves to in SI, and whether it is editable. All of it
was already declared, which is the pleasing part — LIB-1 gave parameters units and bounds
so a *study* could perturb them, and those turn out to be exactly what a person editing
one needs to see.

**Every edit goes through the shared journal, not to the file**, so a change made in the
window is undoable by an agent connected to the same session and vice versa (MCP-1,
GRD-9). Writing the file directly would be the shortest spelling and would silently make
the session one-sided.

**Delivering it reversed a guard.** `SessionJournal` refused any edit that did not
validate. Live validation makes that untenable: a person typing 500 into a parameter
bounded at 50 must see the tree standing with the complaint against it, and refusing every
invalid document forbids any edit *sequence* that passes through one — widening a bound
and then setting a value beyond the old bound works in one order and is refused in the
other. Narrowed to refusing what does not *parse*, which is taint-never-block applied to
input. What that cost is in `lessons.md`: narrowing a guard without asking what each of
its callers will be told afterwards is how evidence gets dropped at a seam, and the MCP
server was the caller that stopped being told.

## The 3D viewport

§16: geometry, electrode potentials by colour, equipotential surfaces or slices,
trajectory bundles coloured by energy, m/z or fate, and density clouds rather than
trajectories for diffusive regions. **All of that is built.**

The window draws the instrument, the field on its section plane, and the ions, each on a
colour scale anchored once across everything shown. Layers can be turned off individually,
and the electrodes are semi-transparent by default — the whole reason to look at metal and
an ion together is to see where the ion goes relative to the metal, and an opaque rod in
front of the axis makes that the one thing the picture cannot show.

### Helix Toolkit, and what taking it knowingly cost — the WPF shell

**History: the current viewport is ours and has no third-party 3-D dependency at all.**
This is kept because the bet was taken deliberately, written down, and then called in.

r06 names Helix Toolkit on its DirectX 11 path, with the reason that plain WPF Media3D
cannot render 10⁴ trajectories interactively. Two things r06 could not know, both found
by checking rather than assuming (§20 asks for exactly that):

- **The 2.x line contemporaneous with r06 is .NET Framework-only** and restores on net10
  only through the NU1701 compatibility shim. **3.1.2** has real `net8.0-windows` targets
  and restores clean with no fallback.
- **Every Helix DirectX package depends on SharpDX, archived since December 2020.** That
  is a dependency on an unmaintained project, taken with open eyes.

It is taken because **§17 makes this path screen tuning, not an artifact**. The
publication figure comes from `Einzel.Render` — vector, headless, no third-party
dependency — so the archived library is confined to a window, and its failure mode is that
the window stops working, not that a figure cannot be produced. LIC-1 is verified rather
than assumed: MIT from the embedded licence file, and the transitive closure is MIT
throughout.

**The failure mode it was being insured against never arrived, and the dependency left by
another route.** Avalonia is a 2-D toolkit, so when the shell moved the viewport had to be
written rather than configured - and what that bought, beyond running on Linux, is that the
unmaintained library went out of the dependency graph entirely. It is recorded in SPEC.md
as Amendment 26 and closed by Amendment 53.

### A conductor is the zero level set of its own signed distance

The same argument `Contours` makes for a vector section, one dimension up: an electrode
outline is the zero set of its signed distance, so one routine draws every conductor there
is and a shape added to the model format needs no change here (architecture invariant 2).
Drawing from the declared primitives instead — a box as a box, a cylinder as a cylinder —
would be shorter today and would need a new case for the fourth shape.

**What differs between symmetries is not the shape but what the solve claims about the
third dimension**, and getting that wrong would be wrong rather than merely ugly:

| The solve says | So the conductor is | Because |
| --- | --- | --- |
| a cross-section (translational) | an **extruded prism**, uncapped, as far as the ions go | the geometry repeats along z, so the electrode extends past anything drawn — capping it would draw an end the model does not have. The invariant axis is the one the beam travels along, so a quadrupole's rods run the length of the flight; using the transverse span instead made them 32 mm of a 200 mm instrument and put them in the corner of the picture |
| an axisymmetric half-plane | a **solid of revolution** | the half-plane is not a picture of the geometry, it is a geometry that repeats all the way round: a rectangle there is a tube in space. A domain reaching below the axis needs no guard here — `ModelValidator` refuses one, and a second weaker copy of that rule would read as though the case existed |
| a volume | a **surface extraction** | nothing is claimed, so nothing can be assumed |

**Where the prism stops is a drawing convention and the figure says so** — `render.extruded-depth`
names the depth and calls it what it is (GRD-12). A convention that is not stated is
indistinguishable from a dimension of the instrument.

**Surface nets rather than marching cubes** for the volume case: one vertex per cell at the
mean of that cell's edge crossings, one quad per sign-changing lattice edge. Watertight by
construction, no 256-case table, and a vertex that sits where the surface is rather than on
a cell edge. What it gives up is sharpness at a true crease — a box corner is rounded by
about a cell — and §17 is explicit that this path is screen tuning rather than an artifact,
so a rounded corner costs nothing that leaves Einzel.

Checked against closed forms the code had no part in:

| | |
| --- | --- |
| A sphere's volume at 24 / 48 / 96 cells | **0.99038 / 0.99760 / 0.99940**, improving under refinement |
| Its area, likewise | 0.99484 / 0.99870 / 0.99968 |
| Edges shared by exactly two triangles | **all 11,970** |
| Worst normal against the true outward radius | **1.000000** |
| A revolved tube against Pappus, 16 / 64 / 256 facets | 0.97450 / 0.99839 / **0.99990**, inscribed so approaching from below |

**Orientation comes from the field, not from the winding.** Every producer emits triangles
in whatever order falls out, and one pass then sets each normal to the gradient of the same
signed distance that defined the surface and flips any triangle that disagrees. That is
exact and needs no reasoning about which way a marching-squares run happens to run — which
is worth having, because the segments `Contours` emits are deliberately undirected. A sign
error there gives a shape lit from the inside, which looks like a hole.

### Two defects the geometry found

**A 1 mm plate vanished.** Extracting over the whole solve domain at 48 cells makes a cell
1.25 mm across, and a plate thinner than that falls between lattice planes: the
three-dimensional example produced **no conductors at all**, silently. The extraction now
runs over the electrode's own bounding box, which is both correct and much cheaper — and
the box is asked of the electrode rather than switched on its shape, because that switch is
exactly what invariant 2 forbids. `CompiledElectrode3D.Bounds` sits beside `Centre` and
`CharacteristicSize`, in the one file that already owns the shape cases.

**A closed outline was revolved and extruded open, and this paragraph is why.** It used to
say that a closed run from `Contours` repeats its first point at the end, so a prism built
from one needs the duplicate welded out. It never repeated it: the joiner drops the repeat
and sets `Run.Closed`, while `Surfaces.Revolve` and `Extrude` decided closure by looking for
the repeat. So every closed outline the viewport drew lost its closing segment — a slit one
sample wide down a prism, a band one sample wide missing from a ring — from the day it drew
conductors. The closed-form checks in the table above passed because their profiles were
built by hand, with the repeat. A traced run now goes through overloads taking the
`Contours.Run`, which read its own flag: a traced tube had 96 open edges and has none, and
encloses the Pappus volume of its own traced polygon exactly. The density shells went through
the same path and are fixed with it. `docs/lessons.md`, "A fixture built from a comment
tests the comment".

### A face on the domain's edge was not drawn

**The einzel lens was drawn as three thin sleeves with flanges.** Each of its tubes is a
rectangle from the bore at 5 mm to the domain's outer radius at 12 mm, and the entrance and
exit also reach the domain's two ends. A conductor is traced over its solve's domain, and
marching squares counts a sample equal to the level as inside — so a face lying on the last
row of samples, with nothing sampled past it, had no crossing to find. The outline stayed open
there, revolved into the bore and an annulus at each gap, and a 7 mm wall read as a thin tube
with a flange at either end. The mesh's largest radius was 12 mm either way, because the
annuli reach it; what was missing was the wall between.

**Traced as the solve holds it.** `HeldConductor` (in `Einzel.Render`, so the vector section
uses the same rule) clips each electrode to the domain at every edge that is not a mirror —
the larger of its signed distance and the distance outside the domain, which is an
intersection and has the edge as a face — inset a millionth of a sample step so the row on
the edge counts as outside. Three properties, each tested:

- **A mirror ends nothing.** By symmetry a conductor continues across a Neumann edge, so a face
  there would be a face the instrument does not have. Which edges are mirrors is asked of
  `GeometryBuilder.IsMirror`, the function the mask is built from, so the axis of an
  axisymmetric solve is one whatever the document says.
- **A conductor clear of every clipped edge is traced exactly as before**, to the bit: near its
  surface the electrode is nearer than any edge. 0 of 361,501 samples differ.
- **A sheet is traced as declared.** The planar mirror pair's end cap is a rectangle of no
  thickness lying in the grounded edge; clipped and inset it holds nothing and would vanish.
  Clipping only ever closes an interior, so a conductor with no sample inside it falls back to
  its declared distance, and the cap is drawn exactly as it was.

A conductor carried *past* a grounded edge — which is what `mesh.node-on-shared-face` advises —
is drawn ending at the edge, where the solve ends it, rather than at a declared extent nothing
computed. **Padding the sample grid past the domain was the obvious fix and the wrong one**: it
would draw that declared extent, and it would wrap the mirror pair's cap, which is zero on the
edge and positive on both sides of it, in a second copy of itself.

On the shipped lens each tube now has its outer wall along its whole length and the end
annuli at x = 0 and 166 mm, and encloses **0.99755 / 0.99847 / 0.99882** of the inscribed
Pappus volume — under one by the cell marching squares cuts from each corner, wherever the
corner is. A cross-section's prism is still uncapped. Volume solves were never affected: each
conductor is extracted over its own padded bounding box, and no shipped volume template
declares a conductor past its domain.

### The field is drawn as equipotentials on a slice

§16 offers "equipotential surfaces or slices" and the slice is the half a reader can see
*through*. A nest of closed surfaces hides everything inside the outermost one, including
the trajectories the viewport exists to show.

**Asked at an instant explicitly.** A driven field implements the time-free interface too
and answers at t = 0 through it without failing — which is how a section, a solve report, a
summed field and a diffusive run have each ended up describing an arbitrary moment of an RF
cycle. Zero is still the instant drawn; what differs is that it is chosen.

### One line geometry for the whole bundle

§16's reason for requiring a DirectX path is 10⁴ trajectories drawn interactively, and 10⁴
scene nodes is what makes that impossible. Every path goes into one vertex buffer with its
own per-vertex colours, and the scene holds a single `LineGeometryModel3D`.

### Two colour scales, and the potential one is centred on earth

§16 asks for bundles coloured by energy and electrodes coloured by potential. Those are
different kinds of quantity and get different ramps.

**Energy is sequential and gets viridis** — a floor at zero and no special interior value.

**But a sequential ramp drawn as thin lines is illegible on any single ground, and that is
worth knowing.** Viridis spans dark to light by construction, so it passes through
*whatever* the background is: measured across grounds from `#101010` to `#D0D0D0`, the worst
contrast anywhere on the ramp never rises above **1.25**. Lightening the viewport does not
fix it, and neither does truncating — skipping the darkest 60% still only reaches 2.83,
because the whole lower half of the ramp is dark. The ions it hides are the slow ones at a
turning point, which are the interesting ones.

**What works is lifting the ramp off the ground and then putting the ground as far below it
as it will go.** Blending toward white by an amount that falls to zero at the bright end
keeps the hue progression, the ordering and the top colour exactly. **0.44 is where the two
requirements meet**: lifting further breaks monotone lightness — the lifted floor rises
above the mid-ramp and the scale dips in the middle — which is the property the ramp was
chosen for. Jointly optimised with the ground, the worst contrast anywhere becomes **4.70 on
the energy scale and 6.77 on the potential one, against 1.01 and 2.49 before**, and a test
asserts both stay above 3.

The instinct was to *lighten* the background, and the measurement says the opposite.

**Potential is signed and gets a diverging ramp, symmetric about zero.** Stretching a ramp
across the observed range puts the neutral colour at the arithmetic middle, which for a lens
holding 0 V and 500 V is 250 V — an earthed tube would then be painted the same blue as a
genuinely negative one. Cool for negative, warm for positive, **and bright at both ends**:
the print-standard cool-warm ramp runs from a dark navy to a dark crimson and both sink into
a dark viewport, so the ends of the scale — the electrodes doing the most — become the
hardest things to see. The figure on paper is drawn by `Einzel.Render` and does not use this. Earth is the value a reader looks for first: it is where an ion feels
no force and what every other potential is measured against, so the neutral colour has to
sit there. The legend shows the scale's own ends rather than the observed range, or it would
not describe the picture.

**An electrode is coloured by the peak its drive reaches, not the DC it sits at.** A
quadrupole's rods hold zero volts of DC and all of their potential as drive, so colouring by
the DC alone paints a mass filter as an earthed box — the sixth appearance of one mistake,
guarded by a test this time.

### The colour scale is anchored across the bundle, and that is why it lives in the command

§16 asks for bundles coloured by energy. **A scale taken per path gives every ion the same
colours whatever its energy** — so two ions a kilovolt apart look identical and the picture
says they were the same. `ViewportOutcome` reports the range over everything drawn, once.

That is the same failure the animation's contour levels had in the other axis: anchored per
frame, a film of a packet spreading showed a packet doing nothing.

The discriminating test is not "the range is wider than the widest single path" — that
margin is 1.5e-5 on a packet launched from rest, and thin. What discriminates whatever the
magnitudes are is that **no single path owns both ends of the scale**: any per-path
anchoring reports some one path's own extremes, and fails.

A **degenerate range gives a colour, not a division**. A packet whose ions all carry the
same energy is a monoenergetic beam in a field-free drift — the simplest model anyone
writes — and a scale that divided by a zero width would paint the whole bundle NaN. Half,
rather than either end, because there is no top or bottom of a scale with no width.

**Viridis, and the reason is not taste.** The ramp is part of how a quantity is read. A
rainbow — the default almost everywhere, and what a naive blue-to-red gives — has
non-monotone lightness, so it invents boundaries where the data is smooth and hides them
where it is not, and it collapses under the commonest colour vision deficiencies. The test
asserts monotone luminance by the Rec. 709 weights over 65 samples, which is what stops
the ramp being "improved" into a rainbow later.

### RND-8 is on the face of the viewport

Above about 1e-2 mbar the model computes a density and no trajectories exist. Lines through
a funnel would depict something the model never computed — which is worse than drawing
nothing, because a picture is the artifact most likely to be shown with none of the
uncertainty apparatus attached.

**Asked of the transport mode, not of the pressure.** A viewport that decided from the
pressure would be re-deriving a decision `ITransportMode.ProducesTrajectories` already
owns, and the two would part company at the first model that declared them inconsistently.

And the reason is *stated where the picture would be*: an empty viewport and one whose ions
were all lost look identical, and only one of them is a statement about the physics.

**The warnings say it first when they say it at all.** `render.no-trajectories` is the
engine's own words; printing a second paraphrase above it reads as two separate problems.
The summary is the fallback for a bundle missing with nothing on the record — which
`ViewportCommand` does not currently produce, and that is exactly why the window must not
depend on its not doing so.

The field's own warnings ride out with the picture (GRD-2), because a bundle drawn through
a field that never converged looks exactly like one drawn through a field that did. This is
the seam this project has dropped evidence at six times.

### Adjusting the view

Rotate, pan and zoom have always worked and nothing said so. There is now a toolbar —
**Side, Top, Front, Iso, Fit** — plus the view cube, and layer toggles for the electrodes,
the field, the paths and the transparency.

**Named views matter more than the gestures for an instrument.** Ion optics is read as an
axial section and two transverse ones, and getting to one by dragging is approximate where a
button is exact. Front on the einzel lens gives the annulus down the bore with the section
plane edge-on; Side gives the three tubes with the equipotentials bunched in the gaps.

**It opens oblique, and that is chosen rather than fallen into.** The straight-on section is
how ion optics is usually drawn and is one click away — but it is also exactly the view in
which a ring and a rectangle look the same, so the first thing a person sees should be the
one that says the geometry is three-dimensional.

### Four things that were wrong first, all about the camera

**Perspective**, which is Helix's default. An ion-optics drawing is read for where things
are along the axis, and that is the one thing perspective distorts. Orthographic now.

**`ZoomExtents` fired before layout**, so the reflectron's 1.3 m flight ran off the right
edge: the control had been given a model but not yet measured, so the fit was to whatever
size it had before.

**The opening view was silently discarded.** Set in the constructor, it framed a scene with
nothing in it, and framing an empty scene leaves the camera wherever that left it — so every
model opened in the side view whatever was asked for. It is now set once, after the first
draw that has something in it.

**And never again after that**, because a redraw is what follows a parameter edit — yanking
the camera back each time would take the view away from the person watching the thing they
just changed.

**And then it was still overridden**, because the control installs its own camera when *it*
loads — so a view set from the window's `Loaded` handler was applied and then replaced, and
which view you got depended on how long the model took to solve: the lens opened oblique,
the trap from above. Fixed by *telling* the control rather than correcting it afterwards —
`Viewport3DX.DefaultCamera` is what it reaches for when it has none, so there is nothing to
race.

**And the fit ran before there was anything to fit to, twice over.** `ZoomExtents` is
computed from scene bounds the control establishes while rendering, so calling it in the
same breath as assigning a camera fits to the bounds it had before: on a quadrupole that
framed the 32 mm cross-section and ran the 420 mm of rod off both edges. Deferred a frame,
and with animation off so the camera the next line of code sees is the one on screen.

**The first named view after startup came out as the top view whichever button was
pressed.** `SetView` wrote look, position and up into the live camera one property at a
time, and each raises its own change notification, so the control saw a camera whose look and
up were momentarily inconsistent and re-derived one of them. A whole camera, assigned at
once, fixes it. Found by clicking the buttons through UI Automation and reading the axis
indicator in the screenshot, which is the only way this class of defect shows up at all.

**The scene had no light**, so every Phong surface rendered at its ambient term alone — the
electrodes were extracted correctly, drawn correctly, and could not be seen. Worth stating
because it is the failure mode where the data is right and the picture is empty, and the
instinct is to go looking at the data.

## The density cloud

RND-8 forbids trajectories through a diffusive region. On its own that is entirely
negative: the viewport drew the instrument and the field and nothing else, so the mode's
principal result could be summarised into a transmission and a transit time and looked at
in no other form. **An empty box and a model that lost everything look the same.**

The density is drawn as **nested shells at decades below its peak** — the same rule the
section's contours follow, because a density spans orders of magnitude and evenly spaced
levels draw the top decade several times over and the extent not at all. Each shell is a
marching-squares contour of the density on the section plane, then **extruded or revolved
by what the solve claims about the third dimension**, which is the rule the conductors
already follow: a cross-section repeats along z, an axisymmetric half-plane is a solid of
revolution. The flag comes from the density the solver produced rather than being
re-derived from the model, so the drawing cannot disagree with the thing it draws.

Shells are always translucent whatever the transparency toggle says, and that is not an
oversight: three nested shells drawn opaque are one shell, showing the packet's tail and
nothing of its core — the exact inversion of where the ions are.

**The instant matters more than any of that, and the end of the run is the wrong one.**
A diffusive run reports the density it *ended* with. The shipped drift tube launches
10,000 ions, collects 9,999.76 and leaves 1.8e-302 behind, so a viewport anchored to the
end draws nothing for exactly the models that work. The run is asked for snapshots across
its flight and the drawing is taken from the middle of those still holding a packet;
`render.density-at-instant` says which, because three nested shells are the same three
shells whenever they were taken (GRD-12). A caller may name its own instant, which is the
seam animation scrubbing needs.

Measured on the corpus drift tube:

| | |
| --- | --- |
| Shells | 4.555e7 / 4.555e6 / 4.555e5 ions per m³ |
| Vertices, nesting outward | 208 / 260 / 292 |
| Tracked region in x | 42.0 mm |
| Densest shell | **9.7 mm, centred at 35.2 mm** |
| Normals off unit length, worst of 760 | 2.2e-16 |

The densest shell spanning under a quarter of the tracked region, three-quarters of the
way down it, is the assertion that separates a packet from a uniform gas — and it is the
one a structurally valid but wrongly placed contour fails.

**Two defects the tests caught before this shipped**, both in `docs/lessons.md`: anchoring
to the end of the run, which the section renderer had already learned once; and filling
the sample array as `[row, column]` where `Contours.Sample` builds `[column, row]`, which
traced every contour somewhere the density is not. The transposed version passed every
structural check — counts, parities, index ranges — and was caught by **every one of 760
normals having length exactly zero**, because a contour traced where the field is flat has
no gradient to take a normal from.

### A sequenced model is walked along its own timeline, one phase at a time

The cloud came from the wholly diffusive solver, which reads the field once and never looks at
a sequence — so a model with a timeline was drawn as though it had none, and its window came
from `maximumFlightTime`, which for a timed instrument is a ceiling on a leg rather than how
long the instrument runs. Every requested instant landed outside the run, and the viewport drew
the geometry beside an empty box. **Three surfaces had the same defect** — `einzel run`'s fork,
both render verbs, and this.

**It stops after the first phase unless an instant is named.** A redraw is something somebody is
waiting on and a TIMS elution is twenty minutes of stepping, so opening on a whole timeline would
hang the window on exactly the models this exists for. It is also the right default rather than
merely the cheap one: a timed instrument prepares its packet in its first phase, and a packet
parked against the gas at its balance point is the state worth opening on. A truncated walk says
so on the figure (GRD-12), naming the phase it stopped after and what to run for more.

**`SequencedRun` takes a phase limit rather than an instant**, and the reason is worth keeping: a
phase's ramp interpolates over the phase's own declared duration, so stopping one early by
shortening it makes every parameter it ramps arrive at its end value early. A wrong answer, not a
short one, and it would validate, solve and run.

### The redraw stopped holding the window, and a layer toggle stopped re-running the physics

Once a refresh could be minutes, two things that had been fine became untenable.

**`Draw` was refreshing as well as drawing.** Ticking a layer checkbox re-flew the ions — a
control that costs nothing and a control that costs everything were the same method. They are two
now: `Draw` turns meshes and polylines into vertices, `Refresh` runs the transport and then draws.
Only the three sites that need new physics — opening, loading, and a parameter edit — call the
second.

**And the transport moved off the UI thread.** `ViewportViewModel.RefreshAsync` awaits the command
in the background and fills the bound collections on the caller's thread, which is what an
`ObservableCollection` bound to a viewport requires. **The journal is reconciled on the UI thread,
not the worker's**: `ShellSession.ViewportAsync` reconciles and records first, then hands the
worker a path rather than the journal, so an edit arriving while a transport is in flight cannot
race a reconcile — GRD-9's subject, since an unrecorded outside change is what breaks the undo
chain. The synchronous `Refresh` remains and both funnel into one apply step, so the two cannot
come to disagree about what was drawn; a test asserts they do not.

**Measured on the shipped `tims-analyzer`:** a redraw is **685 s in Debug** — about 210 s in
Release at the 3.27x this project measured — and produces **3 shells at a peak of 1.29e14 ions
per cubic metre, drawn at 500 us of its 1000 us hold**. So the packet parked against the gas at
its balance point is now something the window shows, and it takes minutes to show it.

**What is still open, and this measurement is why it matters:** nothing gates the wait. GRD-8
costs a run before it starts and does not reach a redraw, so the first thing a reader learns
about a three-minute refresh is that it has begun. The window stays responsive throughout, which
is the difference between a wait and a hang — but a wait nobody was warned about is still the
wrong way round.

### Watch run — the other answer, and the one that was actually wanted

A redraw and a watch answer different questions, and only one of them was available. A redraw
truncates to the first phase *because* somebody is staring at the window; **Watch run** walks
the whole timeline and draws the packet every couple of seconds as it steps. On a driven
diffusive model that is minutes to hours, and until this existed the only two things to do with
them were stare at a window that had stopped answering or read a checkpoint file describing the
packet in numbers.

**What a picture shows that a centroid and a width cannot is the shape.** A packet still
narrowing toward its equilibrium, one pressed against a wall and losing population to it, one
that never left where it was seeded — all three report a centroid and a width, and only one of
them is the instrument working.

**A frame is a whole `ViewportOutcome`, not a density.** UI-1 puts the contouring on the command
layer's side of the line, so a window handed a `DensityField` would have to do the one thing
`ViewportCommand` exists to do. What crosses the seam is the same record a finished run
produces, built by the same function — so a frame of a run in flight and the same instant drawn
afterwards cannot come out looking different.

**The geometry is extracted once and every frame shares it**, which is what makes watching
affordable at all: surface extraction over every conductor is the dominant cost of a bundle, and
the shipped C-trap is 795,564 triangles. It is sound because the sequencer already refuses a
stage that moves an electrode — a stage may change what one holds, not where it is — so the
meshes are identical at every instant of a run by construction. Asserted by *reference* rather
than by comparison, because two lists that happen to be equal would also pass while the work was
being done again.

#### Three things the wiring got wrong

**The transport reports its own leg's clock, and it is right to.** A leg does not know it is one
of eight; the checkpoint writer prints `phase 2/3, 12.0 of 40.0 us`, which is exactly what a
reader wants when the phase is named on the same line. A *picture* has no such line — a frame
carrying a leg's clock sends the instant back to zero at every phase boundary, and the GRD-12
stamp on it names a time the packet passed through long before. The relay adds the phase start,
and takes it from what the phase **did** rather than what it declared: a phase can end early,
and accumulating declared durations would then run the clock ahead of the packet for the whole
rest of the sequence.

**The end of a run is empty whenever the ions arrived.** Applying the final bundle
unconditionally spends minutes drawing a packet and then, at the last instant, replaces it with
an empty box — the exact picture the density work exists to stop a diffusive model producing. A
watch keeps the last frame that held a packet and says on the status line that is what is drawn.
The finished viewport picks the middle of the usable instants for the same reason; a watch has
no list to pick from, so it keeps.

**`RefreshAsync` returns `HasBundle`, which is whether there are trajectories.** A watch only
ever runs on a model that has none by construction, so a watch returning it would answer false
on every successful watch there is. It returns `HasDensity`.

#### Coalesced, and applied on the thread that started it

A viewport wants the newest packet, not every packet. The run produces frames faster than a
window draws them whenever the drawing thread is busy, and a queue then shows a packet from four
frames ago with three waiting behind it — the window falling further behind the longer it is
watched. The newest frame replaces any that has not been drawn. **The queued flag is cleared
before the frame is read**, so the race can post twice and can never drop the last one; the
other order loses the last frame of a run.

Frames are marshaled onto the synchronization context captured when the watch started, because
a bound `ObservableCollection` may only be mutated on the thread that owns it. That is tested on
a real dispatcher thread rather than under xUnit's absent context, for the reason the refresh
test is: with nothing to post to, frames are applied inline and the assertion holds for a reason
that says nothing about the marshaling.

**Stopping keeps what it drew.** A density step is not interruptible part way, so what stopping
does is decline to take the next one — the run ends where it stands and returns what it has.
The packet drawn is where the run had got to, which is a real state of the instrument and the
reason somebody pressed stop; the status says it is partial rather than the drawing being taken
away. A watch is canceled and disposed when the window closes, because one outliving its window
would keep stepping a transport nobody can see, and on a driven diffusive model that is hours of
a core.

**A trajectory model is refused rather than watched**, and not because it could not be: a flight
finishes faster than a window could draw it part way through, so a watch would be a slower route
to the answer the ordinary viewport gives at once. The refusal names what to do instead (AGT-3).

**Amendment 25 holds**: the action is journalled as `einzel run <model> --progress 5`, which is
the same transport writing a checkpoint instead of a picture. The run is the same run; what
differs is who is told about it.

## Results by accuracy class

§12 sorts figures into three families and the sort is not decoration: a Class T figure
describes one packet's arrival, a Class S figure a population, a Class B figure where a
boundary in operating space lies. **That taxonomy was recorded nowhere in the code** —
`FigureOfMeritInfo` carried a name, a unit, a description and which way is better. It is on
the registry now, because a window deciding for itself which figures are Class S would be
growing its own copy of §12.

Two figures are deliberately in no class. `flightTime` is the raw arrival quantity the
Class T figures are computed *from*, and `energyDrift` says in its own description that it
is a diagnostic rather than a design target. §12 lists neither, and assigning them a class
to make the grouping tidy would weaken what it says about the twelve that do belong.

**The layout is the requirement.** §16 says uncertainty and warnings sit alongside the
value and never behind a disclosure control — the rule in that section most easily broken
by somebody who has not read §4, because a value is small and an envelope is bulky and
hiding the bulk is the natural thing to do. Every part is a line.

**And building it found a real hole: 1 of 14 figures carries a GRD-1 envelope.** GRD-1 says
the API offers no way to obtain a scalar alone, and `FiguresOfMerit.Evaluator` is a
deliberate, argued exception for ranking — but the consequence nobody had written down is
that most figures exist *only* in the excepted form. There is no way to ask this build for a
turn-around time with an uncertainty on it. The view reports them absent with the reason
rather than printing bare numbers in the one view whose whole purpose is showing the
envelope; `results.no-envelope` names all thirteen.

## The regime inspector

§16 asks for "governing dimensionless numbers **along a selected path**, violations
highlighted", and the *along* is the substance. Every run already computes these numbers —
at the **worst** point anywhere in the gas, which is the right answer for a warning and
useless for deciding what to change. `RegimeDiagnostics.MeasureAt` reports one point
instead, so "outside validity" becomes "outside validity between 12 and 31 millimetres".

The two share a private core and are asserted **bit-identical** wherever the gas is
uniform, because a uniform gas makes them the same question. Where it is not, they must
differ — on a hundredfold density ramp:

| | thin end | thick end | worst-case |
| --- | --- | --- | --- |
| Knudsen number | **4.17** | **0.042** | 0.042 |
| Reduced field | 40000 Td | 400 Td | — |

One end free-molecular, the other a continuum, in the same instrument. A single verdict
describes neither.

**Thresholds are not restated in the view.** Each sample goes to the same
`RegimeDiagnostics.ForTrajectoryMode` a run uses, so a boundary that moves moves once — a
second copy of "above 1e-2 mbar trajectory integration is the wrong description" would be a
second thing to keep in step with spec figure 4.

**Three defects in the first version, two of them repeats.** The path was flown in a
*vacuum* — no collision sampler — and the gas numbers reported along it, which is the same
silent substitution `RunCommand`'s own comment warns against. A *diffusive* model was flown
too, until RND-8 was asked of the mode rather than the pressure. And the excursion test used
a model that declares no gas, so it returned early and asserted nothing.

## The sequence editor

§16 asks for "the state machine as a timeline, with excitations and transport mode per
phase". The timeline is the model's own — lifted from the elements to the instrument, so
every element follows one schedule — and each phase carries the mode it is described in,
because two elements naming different modes for one instant is not something a
superposition can resolve the way it resolves two fields.

**What changes between phases is the information.** A sequenced instrument repeats most of
its state from one phase to the next, so a table repeating every setting for every phase
buries the rows that matter. Each electrode is marked against the phase before it:

```
2 phases over 102 us
  hold      0 to 2 us (2)        holds
  extract   2 to 102 us (100)    lower -> 500 V, upper -> -500 V
```

The bars are proportional to duration, because a hold of a microsecond beside a flight of a
hundred is the shape of a pulsed extraction and equal rows hide it.

**Two things a reader would otherwise assume wrongly are said.** The last phase *holds*
after the sequence ends rather than switching off — an ion still in flight continues in that
field rather than in none, which would be a physics change disguised as a bookkeeping one.
And a phase that changes the transport mode is marked, because that is SEQ-1's boundary:
the packet is converted there, losing its velocities in one direction and having them
invented in the other.

**Both the DC and the drive**, because reading one is a mistake this project has now made
six times — a quadrupole's rods hold zero volts of DC and all of their potential as drive,
so an editor showing the DC alone would present a mass filter as an earthed box that never
changes.

**It shows rather than edits.** A sequence is a block in the model document, so editing one
is editing the document and goes through the same journal every other change does; what is
missing is the input surface, not the path underneath it.

## The project view

Section 16 asks for model-drift and engine-drift state. `einzel verify` computes both and
already separates them - an edited model or a changed solver-behaviour version invalidates
a stored result, while a different engine build with identical numerics does not - so the
view is presentation over it, plus one field verify structurally cannot supply.

**A model nobody has run.** Verify walks the manifests, so a model with no result is
reported by neither its success nor its failure; it is simply absent. That is the state
most models in a working project are in and the first thing a person opening a folder
wants to see, so `einzel project` reports four states rather than two: **invalid** (a thing
to fix), **stale** (a thing to re-run), **not run** (where every model starts), and
**current**. Keeping "not run" visually distinct from "stale" matters - a fresh project has
run nothing, and painting those the same says a new project is broken.

Validation is run rather than read from a stored result, because a model can be edited into
an invalid state after its last successful run and a view reporting it current on the
strength of a stale manifest would say the opposite of the truth.

**Building it found a defect in `verify`.** A manifest recorded the model's content hash
and not its path, so verify identified the model by searching for a file that still hashed
to the recorded value. Two models may legitimately hold the same content - `einzel init`
scaffolds a reflectron, and the corpus has one - and then editing the model that was
actually run made its drift **disappear**: the result re-attached to the untouched twin and
reported itself current. `RunManifest.ModelPath` is now recorded and preferred, with the
hash search kept as the fallback for older manifests and for a model that has moved. That
fallback reports what it observes - the recorded path is gone, the same content is at X -
rather than claiming a rename, since content alone cannot tell a rename from a twin that
was there all along. SPEC.md Amendment 30.

The root is found from the open model **by the command layer**, not by the window. UI-1
puts project layout outside the shell along with the rest of the file format, and a window
that knew where `models/` sits would grow its own idea of what a project is.

## What is not built

- **A diffusive example with a geometry in it.** No corpus example declares one, so
  "electrodes appear on the RND-8 path" is exercised through the field rather than through
  conductors. That is a gap in the corpus rather than in the code, and a pointed one: the
  device this mode exists for is a funnel, which is nothing but electrodes.
- **Equipotential surfaces** as opposed to slices. §16 offers either; the slice was chosen
  because a nest of closed surfaces hides the trajectories.
- **Colouring a bundle by m/z or by fate.** §16 offers all three and energy is built; the
  data for the other two is already on each path.
- **Scrubbing**, which is the animation timeline's shell half. Per-phase playback rates and
  frame export are built.
- **Two more views**: the figure composer, and the animation timeline (per-phase rates and
  frame export are built; scrubbing is not). The update notice is no longer among them -
  `Einzel.Update` exists and the window carries its bar.

The pattern is worth noticing: almost every remaining row is presentation over something
that already works, which is what AGT-2 is supposed to produce and is weak evidence that it
has.

## The ground moved to white, and the ramps had to move with it

The viewport drew on `#081019`, chosen by measurement: a sequential ramp spans dark to light
by construction, so it passes through *every* background luminance and no ground rescues an
unmodified one — across grounds from `#101010` to `#D0D0D0` the worst contrast anywhere on
viridis never exceeds 1.25. What works is pushing the ramps away from the ground and then
putting the ground as far from them as it goes.

**That argument is symmetric, and the dark half of it was the wrong half to take.**
`Einzel.Render` draws the publication figure on white, and Amendment 25 makes every shell
action expressible as a CLI invocation — so a viewport that looks nothing like the figure it
previews is an inconsistency rather than a preference.

| | dark ground | white ground |
| --- | --- | --- |
| energy ramp, worst contrast | 4.70 | **4.79** |
| potential ramp, worst contrast | 6.77 | **3.03** |

Viridis is now **darkened toward black at its bright end by 0.50**, the exact mirror of the
0.44 lift it carried before, and 0.50 is again where the two requirements meet: 0.60 reaches
6.74 and stops being monotone in lightness, which is the property viridis was chosen for. The
diverging ramp moved from a near-white neutral to a **mid-grey** one — the neutral is earth,
the value a reader looks for first, and a near-white centre is right on black and invisible
on white.

**The ground and the ramps are one decision, so they live in one file.** `ColourRamp.Ground`
holds the background and the XAML takes it from there, because a white ground with the old
bright ramps measures **1.09** — worse than either arrangement. The contrast test reads that
same constant rather than restating it, so the two cannot be edited apart; making exactly
that mistake fails the test at 1.26.

## A flight needs a visible beginning, end, and outcome

The viewport drew conductors, a field and trajectories, and nothing saying where a flight
started or whether it finished. That was tolerable while every model was a beamline read
left to right, and stopped being so with an analyser whose ions launch part-way along a
drift, reverse, and are caught **behind their own launch point** — nothing in the picture
said which end was which.

Three things, all from `ViewportCommand` because UI-1 leaves the window computing nothing:

- **The source**, an octahedron with a spike along the launch direction. Direction is the
  half of a source a point cannot show, and on an instrument that reverses it is the only
  thing distinguishing the end the ions leave from the end they return to.
- **The detector**, a translucent quad on its plane, in **blue** — red is reserved for an ion
  that struck something.
- **Where each ion stopped**, coloured by what stopped it: green arrived, red struck, amber
  anything else. The fate was already computed and only ever shown as a text line reading
  "1 arrived", which is true and does not answer the question a viewer is asking, which is
  whether *that* path reached *that* plane.

**Three outcomes get three colours, which is not tidiness.** Amber is almost always the
flight-time ceiling, and folding it into either green or red is how "the run stopped early"
comes to look like "the ion was confined" — a conflation this project has already had to fix
in the exit codes and in `einzel compare`.

**Both markers are drawing conventions, not dimensions** (GRD-12). A source is a point and a
detector is an unbounded plane; the sizes are chosen to be visible — a fortieth of the
instrument's extent, computed where the extent is already known — and a reader must not take
the quad's edges for the detector's extent.

## The extension manager, and the half of LIC-2 a command could not satisfy

**LIC-2 has two halves and only one of them is the engine's.** An extension carries a
licence - `licence` on the manifest, scaffolded by `ext register` so a new extension answers
from the first minute - and `einzel ext list` prints it. The requirement's other half is
that *the extension manager surfaces them*, and until there was a pane there was no manager.

The Extensions tab lists name, version, kind, trust, licence, what it does, and why this
engine refuses it where it does. Three decisions in it are worth keeping.

**An undeclared licence says `NOT DECLARED`, in red, rather than showing an empty cell.**
That is the CLI's decision carried into the window, and the reason is the same: the
extension somebody most needs to ask about must not be the one whose row is quietest. It is
also the one field on the row a reader cannot recompute for themselves - they can see the
version and the directory, and they cannot infer a licence that was never stated.

**What the sandbox does not enforce is on the pane, not behind a control.** `einzel ext list`
prints those gaps on stderr on every single listing, because a containment measure claimed
and not applied is worse than one absent and known to be. A window that put them behind a
button would be the shell quietly weakening a statement the command layer refuses to weaken,
which is exactly the direction UI-1 exists to prevent. The test asserts the three by
substance - network, filesystem, memory - so adding a fourth gap does not fail it and
dropping one of these does.

**And it was laid out wrongly at first, which no test could have said.** The pane began as
a seven-column grid; in a 270 px panel that truncated the headers to "Exte / Li / Wh / Re"
and the licences to "Ap" and "NC", so the field the pane exists for was the field it cut
off. The four view-model tests passed throughout. Driving the window and reading the
screenshot is what found it, as it was for the camera that opened in the wrong view; it is
now a stacked template, and the containment lines wrap rather than clipping mid-word.

**No extensions and nowhere to put them read differently.** A project with an empty
`extensions/` folder says so and names `ext register`; a model sitting outside any project
says there is no extensions folder to read. Both show no rows, and only one of them means
something is missing. An earlier version of that test put the loose model at the project
root and reported the two states as identical - which they were, because the layout is found
by walking up.

## How the shell came to be on Avalonia

The first shell was WPF, and WPF is Windows. A user asked for the platform to run
elsewhere, so `Einzel.Shell` was written as the same §16 views on Avalonia with an OpenGL
viewport: plain `net10.0`, no `-windows`, and a reference to **`Einzel.Commands` and
nothing else**.

**The swap cost nothing below the shell, and that is the whole point of having kept the
two rules.** Invariant 1 kept every physics assembly free of UI types, and Amendment 25
made every shell action a CLI invocation - so there was no capability trapped in the old
window that had to be excavated before a new one could have it. 2,167 lines, zero physics,
and one file (the color ramp) ported with only its namespace changed.

**The two ran side by side for four changes, and then the WPF one was deleted.** While
both existed its assembly was renamed to `einzel-shell-wpf`, because two projects emitting
one assembly identity is a latent collision and it had already made `ShellBoundaryTests`
ambiguous about which shell it had found - that test became a `[Theory]` over both, so
UI-1 was checked against each rather than against whichever the scan happened to pick up.
**Which shell survives was settled on 2026-11-09: this one**, and the table below is the
record of what had to be carried across before the other could go.

| §16 view | WPF | Avalonia |
| --- | --- | --- |
| 3-D viewport | yes | **yes** |
| Model tree, shown | yes | **yes** |
| Model tree, edited, validated as typed, undone | yes (no undo control) | **yes** |
| Field over the geometry | yes | **yes** |
| A run watched while it steps | yes | **yes** |
| A scale in the viewport | not yet | **yes** |
| Journal, with the actions as commands | yes | **yes** |
| Results by accuracy class | yes, on its UI thread | **yes, in the background** |
| Regime inspector | yes, on its UI thread | **yes, in the background** |
| Sequence editor | yes | **yes** |
| Project view | yes | **yes** |
| Extension manager | yes | **yes** |
| Rotate, pan and zoom with the mouse | yes (Helix) | **yes** |
| Stop a watched run, keeping what it drew | yes | **yes** |
| A legend saying what the colours mean | yes | **yes** |

**Nothing was left that only the WPF shell had**, so it was deleted: 3,575 lines, 2,819 of
them the window and 756 its tests, with one archived 3-D dependency going out of the graph
with them and three of its tests ported onto the live types they were really about. The
last three rows are not §16 views, and each was missing from this table until something
went looking for it. Helix gave the WPF viewport orbit, pan and zoom for nothing, and the
Avalonia viewport had the four named views and no gestures at all; **the Stop button and the
colour legend were found by a review of the deletion itself**, after this page had already
said nothing was left that only the WPF shell had. That claim was made by walking §16's
views and the deleted project's *tests*, and both of these were in the toolbar and under the
viewport - **a comparison drawn up from a requirements list does not cover what a window grew
that no requirement asked for.**

Every row in the second column was presentation over a command that already worked - the same
observation this page makes about the WPF shell's own remaining rows, and the reason a second
window was a drawing exercise rather than a capability one. Two rows needed something below
the window all the same, and neither was a new capability: the results and regime panels
needed background versions of their session calls, and the gestures needed the camera to hold
a zoom and a pan.

### Deleting it, and the three tests that were not about WPF at all

Before deleting a project, the question is not whether its code is still wanted but whether
anything it asserted is asserted nowhere else. Two of `Einzel.Wpf.Tests`' classes were
`ViewportViewModelTests` and `WatchedViewportTests`, and most of what they checked had
moved: the view models went to `Einzel.Shell/Presentation/` with their tests, and the
watch is covered by `WatchedRunTests` on both runners.

**Three were not covered, and one of them was the only test of a type that is still live.**
They were written against `Einzel.Wpf.ColourRamp`, a copy, and what they were really about
is `Einzel.Commands.ColorRamp` - which the port had left with **no tests of its own**:

- `BothRampsAreLegibleAgainstTheViewportGround` - WCAG contrast above 3.0 for both ramps
  against the viewport's background, which is what stops the ground being changed without
  the ramps being rechecked.
- `TheRampRisesInLightnessThroughout` - Rec. 709 luminance monotone over 65 samples, which
  is what stops viridis being "improved" into a rainbow later.
- `ABundleOfOneEnergyIsDrawnInAColour` - a degenerate range gives the middle of the ramp
  rather than a division by zero, with a spread range as the control. A monoenergetic beam
  in a field-free drift is the simplest model anyone writes.

They are `ColorRampTests` in `Einzel.Cli.Tests` now, pointed at the live type, and two
mutations of it fail them. **A test that was only ever run against a copy was testing the
copy**, and deleting the copy is when that becomes visible.

### Two things the deletion lost, and the comparison that missed them

A review of the deletion found two capabilities the WPF window had and this one did not,
after the table above had already said nothing was left that only the WPF shell had. Both
are restored, and **how they were missed is the part worth keeping**: the comparison was
drawn up by walking §16's eleven views and the deleted project's tests. Neither of these is
a §16 view, and neither had a test - they were a button in the toolbar and a strip under
the viewport. **A comparison drawn up from a requirements list does not cover what a window
grew that no requirement asked for.**

**Stop, on a watched run.** A watch is the one action here measured in minutes - a TIMS
elution is twenty - so it is the one that needs a way out, and without it the choices were
to wait or to close the application. It is cooperative, because a density step is not
interruptible part way: what it does is decline to take the next one, so the run ends where
it stands. **What it drew is kept**, because the packet on screen is where the run had got
to and is the reason somebody pressed it; the status says the run is partial rather than the
drawing being taken away to say so.

**The token has to reach the listener, not only `Task.Run`.** `Task.Run`'s token decides
whether the work ever starts and is never consulted again once it has, so a watch cancelled
a second in would run to completion with a Stop button that did nothing visible for twenty
minutes. The run offers to be interrupted at exactly one place - it asks the listener whether
it wants a frame at every step - so `ShellSession.WatchAsync` wraps whatever listener it is
given rather than asking each caller to remember, which is this project's recurring rule that
the shortest spelling should be the safe one. `StoppingAWatchKeepsWhatItDrewAndTookFewerSteps`
asserts the **step count** against the same run unstopped, because a token reaching only
`Task.Run` passes any assertion about the exception alone.

**The legend.** A trajectory bundle is coloured by energy and a field by potential, and a
colour standing for a number is the one place a reader cannot recompute it - GRD-1's argument
applied to a drawing. The rows come from `ViewportPicture.Scales`, in the command layer,
because **the ends are not the observed range**: the potential ramp is symmetric about earth,
so a lens holding 0 and 500 V is drawn on a ramp running -500 to +500, and a legend that
worked that out for itself would be a second copy of a rule the picture already applies - the
WPF window kept exactly that rule in a comment in its code-behind. The swatch is sampled at
32 stops rather than interpolated between the ends, because viridis is not linear between its
ends and that is the whole reason it is used.

**One row per span actually drawn**, which is a wrinkle the legend exposed rather than
created: the equipotentials anchor on the solved field's extremes and the metal on the peak
each electrode reaches, and contour levels are chosen inside the domain so they generally do
not reach the electrodes. Where the two agree to a rounding there is one row saying
`potential`; where they differ there are two, `field` and `electrodes`, because one number
for both would describe a ramp neither layer uses. **Whether the two layers should share a
span is a separate question about the picture** - "anchored once across everything shown" is
the rule this viewport states elsewhere - and changing it would move every still, so it is
recorded here rather than done under a legend fix.

### The five panels, and the two that run the model

Results, regime, sequence, project and extensions are tabs beside the journal, each over the
view model it had in WPF. **Results and regime run the model, so they run in the background.**
The WPF shell ran both on its UI thread behind a wait cursor, which on a volume model is
minutes of a window that does not answer. `ShellSession.ResultsAsync` and `RegimeAsync`
reconcile the journal and record the command on the caller's thread, as `ViewportAsync` does,
and run only the command in the background; the view models' `RefreshAsync` fill their lists
on the caller's thread when it returns, so nothing is changed under the window from another.
**The tree is closed while one runs**, as it is during a watch: what runs is the document on
disk, and an edit part way through would give figures of one revision beside a tree of
another. An edit made after one has finished marks what it shows as of the model before the
edit, rather than leaving the figures of the previous model looking like those of the one in
the tree beside them. A full run stores a result, so the project view is read again, and its
state for this model goes from "not run" to "current" without anybody pressing Read.

**A review found that the run itself could mix two revisions, below the window.** The results
command read the model three times - the run, the figures, whether it declares a cloud - and a
run read it three times more: to validate and hash it, to compile it, and at the end for the
manifest's schema version. So an edit landing mid-run, from this window or from an agent on
the same file, could give a manifest whose hash was of one document beside a run of another
(PRJ-3), and an edit that left the model invalid failed a run that had finished. Each now
reads once, and the results command checks the run's manifest hash against the text it read
and refuses a mismatch. Tested by editing the model from inside the run's own "solving"
announcement, which lands at a known moment after its read; a delay would be a race the test
could lose on a fast machine and then pass measuring nothing.

**The cheap three are read when their tab is first opened**, not when the window opens: each
read is journalled as a command, and a journal that opens with five reads nobody asked for
buries the ones they did. An edit forgets the timeline and the project, which it can change,
and re-reads whichever is on screen.

The tables became stacked rows where a column of a 400-pixel panel would truncate, as the WPF
extension pane once did to its licences. The regime table keeps its columns and scrolls
sideways, with its header scrolling with it so a column is never read under the wrong name.
The results panel shows the evidence behind each figure on its own line, which the WPF one
did not show at all; GRD-1 counts it as part of the envelope.

### Orbit, pan and zoom

Drag to turn, right-drag or Shift-drag to move, the wheel to zoom about the cursor, and a
double-click or **Fit** to refit at the current angle. The named views fit exactly and leave
nothing of the mouse behind, so pressing one is the same picture every time.

**The bookkeeping is in `ViewportCamera`, where it is tested; the window only maps pixels.**
Zoom and pan are a scale and a shift in clip space applied after the fitted frame, so an
untouched camera draws exactly what a still of the same view draws, and the ruler over the
viewport reads the zoomed matrix and stays true. **An orbit keeps the scale and the centre
and turns about them.** Refitting at every angle, as a named view does, makes the picture
swell and shrink under the cursor - a 100 mm rod turned end-on is 2 mm across and would fill
the screen - and the test for it uses exactly that rod, against a refit as the control. An
edit's new camera keeps the zoom and pan of the one it replaces, so somebody zoomed in on a
slit to see what an edit does to it stays on the slit.

**Two things the live window settled that a test could not.** Dragging right first turned the
instrument the wrong way: measured on the near corner of a plate, a 25-pixel drag right moved
it 53 pixels left. The point under the cursor now follows it both ways (the same drag moves the
corner 56 pixels right, and Iso puts it back to the pixel). And the orbit's first version
widened the depth to the scene's reach from the centre, so nothing would be clipped at a new
angle; breaking that changed no test, and the reason is that it was redundant - the frame's
depth is already twice its largest half-extent, and a box fits in a sphere of the square root
of three times that. It was deleted rather than kept untested.

### The session moved, and the window stopped going around it

The first Avalonia window called `ViewportCommand` and `OutlineCommand` itself. That drew the
right picture and left **nothing behind**: no journal, so nothing a person did could be undone
or seen by an agent on the same model, and no record of the commands, so the window was the
one surface whose actions could not be read back as something to type (Amendment 25). Its
model tree was a list of labels.

**The session and six view models now live in `src/Einzel.Shell/Presentation/`** -
`ShellSession`, and the tree, results, regime, sequence, project and extension view models -
moved out of `Einzel.Wpf`, where they held no WPF type at all: their only references were
`Einzel.Commands` and `System.ComponentModel`. While both windows existed the WPF project
compiled them from their new place rather than keeping a copy, so the two ran one source
and could not drift apart meanwhile - an Avalonia type in one of those files would have
failed the WPF build, which is what held the line until that project went. Only
`ViewportViewModel` stayed behind, because it fed Helix where the Avalonia viewport draws
from `ViewportPicture` instead, and it was deleted with the rest. Two of the WPF test
classes went with the code they test, and now run on Linux too.

**The tree edits now, through the journal, under the person's name**, which is the
operating-system user - declared once when the window opens, never per action, for the reason
the MCP server takes its author from the handshake. A free parameter is a box; a derived one
says `derived` and shows its expression and what that came to on the line beneath, because
that pair is what a reader needs in order to know which knob to turn. A value commits on Enter
or on leaving the box, and the rules a person meets at the keyboard are in
`ModelTreeViewModel.Commit` rather than in the window, so they are tested where the window
cannot be:

- **A box left as it was records nothing.** Committing on lost focus means tabbing through the
  tree passes through every row; without this each one would write a journal entry.
- **Numbers are parsed invariantly.** `4,5` is refused rather than read as forty-five, and the
  refusal says to write the magnitude alone with a point.
- **A derived parameter is refused by name**, naming the expression it is derived from.
- **Out of bounds applies, and the tree stands with the complaint against it**: taint, never
  block, applied to input. The line under the tree turns red and names the bound; the
  viewport keeps the last picture it could draw and the status line says that is what it is
  showing, since a model that does not compile has no field to draw and blanking the viewport
  would hide the instrument a person is in the middle of changing.
- **A refusal is a third colour**, amber, because it is about what was just typed rather than
  about the document, and a person should not have to read the sentence to tell which.

**One defect the window would have produced, found by looking for its cause.** An edit
re-reads the tree, which replaces every row, and a box committed with Enter then loses focus on
its way out of the list - carrying the value just applied, against a row that still shows the
old one. The window ignores a row the tree no longer holds; checked live, one edit is one
journal entry.

**An edit redraws, and the viewport now takes a new scene.** `SceneView.Scene` could only be
set before the control was realized - "a viewport that changed scene mid-flight would have to
tear down its buffers on the render thread, which is the next piece of work". That is
`SceneView.Replace`: the scene is handed over under the same lock a watched frame uses, and on
the render thread every buffer is released and the new layers uploaded. A watched frame still
waiting is dropped, since it belongs to the instrument before the edit. **The named view is
kept and the frame is refitted to the new scene**: this camera has no free orbit, so where
somebody is standing is the named view alone, and an edit that doubles a drift length would
otherwise put half the instrument off screen.

**One redraw at a time, and a watch only over a current picture.** A redraw is a field solve
and a transport, and the first version started one per edit, numbering them so a stale one
stood down only after it had paid for itself - three quick edits ran three solves side by side
for two pictures nobody saw. `Coalescing` runs the redraw one at a time and folds every request
made meanwhile into a single run after it, which reads the document as it stands by then; a
superseded run's failure is not reported and the last run's is. Checked live on a quadrupole at
96 cells per radius: three edits, two redraws, and the window answering in under a millisecond
while each solved. **Watch run is enabled only while the viewport shows the document as it
stands** - not during an edit's redraw, and not after one that could not be drawn - because a
watch runs the file on disk and its frames would otherwise land on the geometry from before the
edit. The tree and the undo button are closed while a watch runs, so no redraw can be asked for
that would replace its frames. Both rules came from a review of this change.

**Each box is named for the parameter it changes**, with its unit and bounds as help text, so a
screen reader tabbing through the tree hears "inscribedRadius, in mm, 1 to 50 mm" rather than a
list of numbers: the label beside a box is not associated with it.

**Undo has a button**, which the WPF shell never had: its view model could undo and nothing in
the window called it. The Journal tab lists what has happened, by whom, and every action as
the command that would repeat it, selectable so it can be handed to an agent as it stands.

**The shell process is now invariant in its numbers.** Turning globalization back on, which a
window needs, also turned the desktop's locale on, and about 265 interpolated strings below
the shell format numbers without naming a culture because in the CLI none is needed. On a
German desktop each would print a decimal comma beside a box that refuses one. `Program.UseInvariantNumbers`
makes the current culture invariant on every thread at startup and leaves the culture data in
place, which is what the CLI's build setting was doing for it. Two sites were fixed directly as
well, because they were found first: `OutlineCommand.BoundsText`, which printed `0,1 to 20 kV`
under the tree, and the scan's out-of-bounds warning. The analyzer that exists for this, CA1305,
is silent wherever globalization-invariant mode is on and does not look inside interpolated
strings at all, which is why none of the 265 was ever reported - in this repository it found
two, both of the `ToString("G6")` kind.

### A ruler in the corner of the viewport

`ScaleOverlay` draws the scale indicator over the GL control as ordinary Avalonia lines: one
arm along each model axis, all the same round length, foreshortened exactly as the instrument
is. **It decides nothing** - the length, which arms show, where they sit and the glyphs of the
label and letters come from `ScaleIndicator.For` and `ScaleIndicator.Strokes`, laid out from the
very matrix the render pass hands the GPU and the composed picture it is drawing
(`SceneView.Picture`), which are the calls `einzel render still` makes. So a still and the window
draw the same ruler, stroke for stroke, in the same corner - the one the instrument covers least
- and leave it out alike when the picture is too small to hold it (`docs/rendering.md` has why
it is a ruler in the scene rather than a flat bar, and how the corner is chosen).

Over the GL control rather than inside it, because an annotation must not be hidden by the
instrument; it takes no pointer input. It is
redrawn when the framing changes - a named view, or a watched packet growing the frame, which
happens on the render thread and is marshalled to the UI thread - and when the window is
resized, since the ruler is sized from the picture's smaller side.

**Text as strokes, because two fonts for one layout disagree.** Opened in a live window on
five models - the einzel lens, the planar mirror pair, the quadrupole, the C-trap and the
three-dimensional parallel-plate gap - and stepped through every named view, the ruler measured
true (20 mm across 114 px where the lens's own tubes put 5.7 px to the millimeter), sat clear of
the instrument in every view, including the plate seen face-on that the corner choice exists
for, followed a resize, and returning to Iso gave a byte-identical frame on all five. Against
`einzel render still` at the window's own 1020 by 761 pixels it gave the same length and corner
and arms on the same pixels. **Its text was not the still's.** The layout's text height is the
height of a digit, which is what the still's stroke font draws at, and the window set its text
in Avalonia's own font with that number as the font size, which is an em - about a half again
taller than a digit - so every label came out two thirds the size: digits 14 px tall where the
layout asked for 20. Sizing the font up from its measured digit height fixed the height and,
a review of that fix found, left the window's label about 3 percent wider than the box the
layout reserves, which it measures from the still's glyphs. So the window no longer sets text
at all: `ScaleIndicator.Strokes` hands out the arms, the label and the letters as one list of
polylines, each with its width, and the window and the still both draw that list and nothing
else. Measured live, every glyph's ink box now matches the still's to a pixel, and stills
through the shared list are pixel-identical to stills drawn the old way on all twenty
model-and-view pairs tried.

### Avalonia is 2-D, so the viewport is ours

Helix Toolkit did the scene graph, the camera and the lighting for the WPF viewport.
Avalonia has no equivalent: `OpenGlControlBase` hands over a context and a size, and
everything above that - shader, camera, mesh upload, depth state - is written here.
`Silk.NET.OpenGL` wraps the function pointers Avalonia's `GlInterface` supplies; it is a
binding rather than an engine, and nothing below the shell sees it.

That is more code, and it is what removed the SharpDX problem this page records above:
the WPF 3-D path rode on a library archived since December 2020, this one rides on OpenGL,
and when that project was deleted the unmaintained dependency left the graph with it.

### ANGLE hands back an ES context, and the failure is silent

On Windows, Avalonia's default GL backend is ANGLE, which supplies an **OpenGL ES 3.0**
context. A shader declaring `#version 330 core` does not compile there - and Avalonia does
not surface that as an error, it **disables the control**. A viewport that draws nothing
looks exactly like a viewport whose scene was empty.

So the shader body is written once and the version header is chosen from the context the
platform actually supplied:

```csharp
private static string Header(GlVersion version) =>
    version.Type == GlProfileType.OpenGLES
        ? "#version 300 es\nprecision highp float;\n\n"
        : "#version 330 core\n\n";
```

`precision highp float;` is required in ES and is a syntax error in desktop GL, which is
why the two headers differ by more than a number. One body rather than two shaders, for
the reason the renderer draws conductors and equipotentials with one marching-squares
routine: two copies of a thing that must agree eventually stop agreeing.

### A high-severity advisory arrived with a transitive package

Avalonia 11.2.3 pulls `Tmds.DBus.Protocol` 0.20.0 for the Linux session bus, and that
version carries **GHSA-xrw6-gwf8-vvr9**. 0.21.2 is affected too. The repository builds
with warnings as errors and NuGet auditing on, so it **failed the build** rather than
passing as a note - which is the guard doing its job on the first package added in months.
Central transitive pinning names the fixed 0.95.1 once and every project gets it.

LIC-1 is clear and verified rather than assumed: Avalonia declares MIT in its nuspec,
Silk.NET is MIT under the .NET Foundation, and `Tmds.DBus.Protocol` is MIT as the package
that brought it in is.

### Watch run, and what it does not cover

The viewport fills from a run that is still going, through `ShellSession.WatchAsync` - the
same `ViewportCommand.Watch` and `IViewportProgress` the WPF shell used, reached through the
session so the watch is recorded as `einzel run <model> --progress 5`. Frames are
**coalesced rather than queued** - a viewport wants the newest packet, not every packet,
and a queue falls further behind the longer it is watched - and the queued flag is cleared
*before* the frame is read, so the race can post twice and can never drop the last one.
Only the density is rebuilt per frame; the geometry is extracted once and shared, which on
the shipped C-trap is 795,564 triangles not re-uploaded sixty times.

`Watcher` decides when a frame is wanted from the **wall clock**, not the step count, so a
model whose steps are microseconds and one whose steps are milliseconds both draw at the
same rate. Its first version refused the first frame: `_last` was `TimeSpan.Zero` and the
stopwatch also reads about zero there, so the window opened on an empty box and stayed
empty for an interval - which reads as a run that has not started. `TimeSpan.MinValue`
then overflowed the subtraction. The question being asked is "has a frame been wanted
yet", so `_last` is nullable and it is asked directly. Caught by its own test.

**A trajectory model is refused with a reason rather than watched**, by the command layer:
the whole bundle arrives faster than the first frame of a watch would. That is right, and
it means "watch the analysis happen" is currently true for a density and **not for a scan,
a sweep, a boundary search or an optimization** - which are the study drivers, and the
thing somebody actually leaves running for hours. Making those watchable is a
command-layer question before it is a window one, and it is the next thing worth doing
here.

### Tested without a window

`Einzel.Shell.Tests` targets plain `net10.0` and runs on both runners - the first time a
shell's own tests have. None of its tests opens a window. It runs with globalization on, as
the shell does, because a test of what a person types has to run where the desktop's locale
can reach it:

- **`ModelTreeViewModelTests`** pins what typing does: an edit reaches the file through the
  journal under the person's name and moves the derived parameter; a box left as it was
  records nothing; under `de-DE` a comma is refused and a point is read; a derived parameter is
  refused by name; out of bounds applies and undo puts it back; refreshing the history leaves
  the rows alone, so a redraw finishing cannot take the keyboard out of a box. Each rule was
  broken on purpose and fails its own test.
- **`PanelTests`** runs results, preview and regime through the background path and checks
  each reports what the foreground one did and is recorded as the same command; a refusal
  comes back as a status line; and "not run" colours as neither a problem nor a success.
- **`CoalescingTests`** pins the redraw's scheduling: requests during a run fold into one more
  run, never two at once; an idle request runs; only the last run's failure is reported; and
  the window is told both when it starts and when it is done. Each rule broken on purpose
  fails, and a folded request is asserted to come back finished rather than awaited, so a
  regression fails instead of waiting forever on a gate nobody opens - which is what the first
  version of the test did.
- **`ShellCultureTests`** checks that numbers are invariant on this thread and on new ones once
  the shell starts, beside controls that print a comma first - so it cannot pass because the
  machine happens to use a point.
- **`ExtensionsViewModelTests`** and **`ProjectViewModelTests`** moved here from the WPF tests
  with the code they test.
- **`FramingTests`** pins the camera as arithmetic. It moved to `Einzel.Cli.Tests` when the
  framing moved into the command layer, and still runs on both runners. The projection is a
  `float[16]`, so what it does to a point is checkable directly: the center of the scene
  lands at the origin of clip space, a point at the framed radius lands inside the frustum,
  the trajectories are included in the measured extent (the reflectron lesson - a page
  chosen from the conductors alone puts the turning point off it), and an empty scene gives
  a framing rather than a division by zero.
- **`WatcherTests`** pins the cadence, including the first frame and the last-with-a-packet
  rule.
- **`WatchedRunTests`** drives a real diffusive run end to end and asserts the packet's
  leading edge **moves** across the frames. That frames arrive says the callback is wired;
  that the packet moves says they are different frames. A second test watches through the
  session and asserts the watch is recorded as the command that would repeat it.

What is not tested without a window is the GL path itself - the shader, the uploads, the
depth state. That needs a context, and the ANGLE defect above is exactly the class of
thing it would catch. A headless GL context in CI is the honest answer and is not built.

### Seven defects a review found, and two of them drew the wrong picture

The Avalonia shell went up for review before it merged. Two findings are worth keeping for
what they say about porting rather than about this window.

**Every driven electrode was painted the same color.** The port kept the intent — an
electrode is colored by the peak its drive reaches, not the DC it sits at, which is the
mistake this project has made six times — and lost it in the arithmetic:

```csharp
// Wrong. Math.Sign(1.0) is +1, so a -500 V tap became +500 V.
var peak = conductor.PotentialVolts
    + (Math.Sign(conductor.PotentialVolts is 0.0 ? 1.0 : conductor.PotentialVolts)
       * Math.Abs(conductor.DriveAmplitudeVolts));
```

`DriveAmplitudeVolts` is **signed**, and the sign is what makes a quadrupole's two pairs
exact negatives — one basis channel, and the single most informative thing in the picture.
Taking its absolute value and recovering the sign from the DC works for a DC-biased
electrode and fails completely for a purely driven one, where the DC is zero and the
ternary substitutes `+1`. Every rod of `quadrupole-rf` came back at `+500 V` and the same
saturated red. The WPF shell it was ported from computed `PotentialVolts +
DriveAmplitudeVolts`, which is correct, so the regression was introduced in carrying it
across.

**So this is the seventh appearance of that defect and the first to survive into a
viewport** — and it survived because the decision lived inside an `OpenGlControlBase`, where
checking it needs a GL context. It is `Shading` now, which needs none, and
`ShadingTests.AntiphaseDrivenRodsAreDrawnAtOppositeEnds` fails with the old expression
restored. **A decision that needs a platform to observe is a decision nothing will check.**

**And a diffusive model with no electrodes was framed at a millimetre.** `Framing.Measure`
spanned conductors and trajectories. RND-8 means a diffusive model has no trajectories by
construction, and it need not declare electrodes either — the drift tube in this assembly's
own test has a uniform analytic field and no metal at all. Nothing was measured, the
framing fell back to `radius 1.0` at the origin, and a packet spanning 42 mm was drawn
entirely outside the frustum: **a blank viewport for exactly the model class `Watch run`
exists to serve**, and the test suite contained the model that would do it.

The density is measured now, and a second half was needed with it: a packet that *drifts*
leaves the box it was framed in. Re-measuring per frame is the obvious answer and is worse
— the box would breathe with the packet, which reads as a lurching camera rather than a
moving packet. `Framing.Union` grows the frame and never shrinks it, as the bounding sphere
of two spheres so that repeated application cannot drift it outward.

The other five, briefly:

| | |
| --- | --- |
| The finished-watch caption printed `final.Density.Count` while the viewport showed `LastWithPacket` — "0 density contours" over a picture of five, in the case that mechanism exists for | counted off the frame actually shown |
| The watch path replaced the status line and surfaced none of the run's warnings (GRD-2, and the eighth drop-at-a-seam here) | one `Worst` shared with the open path |
| Translucent conductors and density shells blended with depth writes still on, so "see through metal" revealed only whatever was drawn first | `DepthMask(false)` for both passes, depth still *tested* so the paths drawn beforehand read through |
| The window solved and flew synchronously in its constructor — no window at all for nine seconds on the C-trap, which is what `Watch`'s own comment forbids twelve lines above | measured on a background thread, drawn on the UI one |
| `AFlightIsRefusedRatherThanWatched` edited its model by string replacement without asserting the edit happened, against a rule `docs/lessons.md` records twice | `Assert.NotEqual(Model, flown)`, and `Assert.Throws<EinzelException>` rather than `ThrowsAny` |

Plus two allocations: the diverging ramp's five anchors were a method-local literal rebuilt
on every call while the viridis table forty lines above was already `static readonly`, and
the equipotential upload re-wrapped every point into its own `double[3]` to reach an
overload that flattened it again.

### The viewport no longer decides what its picture looks like

Every decision that made the cross-platform viewport's picture - the camera, the color
ramps, how an electrode is colored, which layers exist and in what order, which are
translucent and whether they write depth, and the lighting - lived inside `SceneView`. They
now live in `ViewportPicture` and `Framing` in `Einzel.Commands`, and `SceneView` uploads the
composition it is handed and draws its layers in order; even its fragment shader is built
from `ViewportPicture.Light` and `ViewportPicture.Ambient` rather than typed numbers.

The reason is `einzel render still`, which draws the same composition to a PNG with a
software rasterizer: a still is worth having only if it is the window's picture, and two
copies of a decision drift. UI-1 decided where the decisions could go - the boundary test
forbids the shell from using `Einzel.Render` types at all - so they sit in the one engine
assembly the shell may reach. The detail is in `docs/rendering.md` and SPEC.md Amendment 54.

Two things changed for a person using the window, both found by looking at stills. **Printed
boards and reflected halves are drawn**: the planar mirror pair was one end cap and nothing
else. And **the frame is fitted to what each named view sees** rather than to a sphere round
the instrument, so a long analyzer runs edge to edge instead of across the middle third.

**Fitting per view broke turning between views, and a review caught it before it shipped.**
`SceneView` unioned the frame fitted for the new view with the previous view's frame, to keep
whatever a watched packet had drifted into. But a frame is now a box in its own view's
coordinates, and the union carried the old box's rotated corners across - so the frame never
shrank, and grew on every click of a named view. A cube filled about half the side view after
one switch from iso, and less after each round trip. The sphere it replaced was the same from
every angle, which is why the union had been harmless before. The bookkeeping now lives in
`ViewportCamera` in `Einzel.Commands`, where it can be tested without a GL context: what a
watch adds is kept as a box in model coordinates, which does not depend on the view, and each
turn fits the scene afresh and takes that box in at the new angles. Turning away and back
gives the frame you started with, to 1e-9 over five round trips.
