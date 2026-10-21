# Architecture

## Assemblies

Engine outward. Each references only what is above it in this list.

| Assembly | Holds |
| --- | --- |
| `Einzel.Core` | Units, dimensions, geometry, the result envelope, the error taxonomy, the model format and its validation |
| `Einzel.Fields` | DC solvers, interpolants, analytic and solved fields, basis superposition, and the `ISolveStore` seam a field cache implements |
| `Einzel.Transport` | Integrators, the trajectory recorder, ion species |
| `Einzel.Analysis` | Figures of merit by accuracy class |
| `Einzel.Library` | Device templates as data, and the loader that enumerates them |
| `Einzel.Sweeps` | Tolerance Monte Carlo, sensitivity fields, Nelder-Mead and CMA-ES |
| `Einzel.Io` | Model JSON, VTU export, the wire form of a result |
| `Einzel.Project` | Project layout, run manifests, content hashing |
| `Einzel.Extensions` | Extension manifest, schema check, and the sandboxed subprocess runner |
| `Einzel.Render` | Projection, contouring, decimation, and the SVG and PDF writers |
| `Einzel.Commands` | Command objects: every operation as one serialisable thing |
| `Einzel.Cli` | The primary surface |

| `Einzel.Mcp` | The live-session server, over one shared attributed journal |

Not yet built: `Einzel.Compute`, `Einzel.Update`. `Einzel.Wpf` holds three of §16's
eleven views — the model tree, the journal and the 3-D viewport — and the tests that
keep invariant 1 and UI-1 true while the rest are written - the model tree, the journal and
the 3-D viewport. See [The shell](shell.md).

The CLI, the MCP server, and the shell are **peers, not a stack**. All three drive
the same command objects. That is what makes "nothing exists only in the window"
enforceable rather than aspirational: a capability added to one surface is added to
the command layer, and the others get it for free.

**All three now exist, and the peer relationship is asserted rather than
described.** Every MCP tool returns `CommandJson.Write` of the same outcome record
the CLI serialises for `--json`, and a test compares the two **byte for byte**.
One serialiser, one command object, so the surfaces cannot drift — and a warning
added to a result reaches an MCP client by being on the record rather than by
anyone remembering to copy it across.

**The shell is the third, and it is the one that tests the direction the invariant is
really about.** Every action it takes is journalled as the `einzel` invocation that
would reproduce it (Amendment 25), so a capability with no command spelling cannot be
added to the window. Twice a view has been blocked until a command existed — the model
tree on `einzel outline`, the viewport on `ViewportCommand` — and both times the command
layer gained the capability. An agent is better off because a window was built, which is
what "peers, not a stack" is supposed to produce.

`einzel-mcp` is a separate executable rather than a verb on `einzel`, which is the
peer relationship taken seriously in the other direction: spec figure 6 says the
project-folder loop has "no protocol, no session, no network", and a `serve` verb
inside `einzel` would put a server in the binary whose distinguishing property is
that it is not one. See [the live session](live-session.md).

## The four invariants

Violating one is a design bug, not a shortcut.

### 1. No UI type below the shell

Nothing may reference `Einzel.Wpf`, and every assembly above it must build and run
on Linux. CI builds and tests on `ubuntu-latest` *and* `windows-latest` from the
first commit, because an invariant only ever checked on a developer's Windows box
is one that has already been broken by the time anyone notices.

**The shell itself builds on Linux too, which was an open bet and is now measured**:
`EnableWindowsTargeting` is enough, XAML markup compilation included. It does not run
there and is not meant to — what the Linux job checks is that nothing below the shell
has acquired a dependency on it. `Einzel.Wpf.Tests` is the one Windows-only test
project, and on another host it builds as an ordinary `net10.0` assembly with no
sources, so a solution-wide `dotnet test` walks past it: 848 tests on Windows, 843 on
Linux.

Its hardest test has now arrived and it passes. `Einzel.Render` draws real device
templates as SVG and PDF on the Linux runner, where there is no display, no window
manager and no font server. There is no drawing surface anywhere in the pipeline
and no font measurement: a scene is a list of paths and text runs, and the two
writers turn that into files. See [Rendering](rendering.md).

### 2. No device class below `Einzel.Library`

The engine knows about fields, integrators, and figures of merit. It does not know
what a reflectron is.

This shows up in naming. The primitive an ion mirror is built from is called
`HalfSpaceUniformField` — field-free on one side of a plane, uniformly retarding
on the other — not `IonMirror`. The closed-form reflectron used as a test
reference lives in the *test* project. `ArrivalTimePeak` computes resolving power
without knowing what produced the arrivals.

The renderer is the same argument in a new place. It draws a conductor as the
**zero level set of the electrode's own signed distance**, which the model format
already requires for the solver and the ion absorber - so a rod, a plate, a ring
and a sphere are one routine, and a shape added to the format needs no change in
`Einzel.Render` at all.

LIB-1 states the test: if supporting a new device requires a change below
`Einzel.Library`, either it is genuinely novel physics or the abstraction is
wrong, and almost always the second. A mirror pair and a quadrupole currently
share no code at all — they name the same three electrode primitives in different
arrangements. See [Device templates](device-templates.md).

### 3. No extension code inside the engine loop

Extensions are coarse-grained: whole input in, whole output out, one call per run,
never per integration step. The subprocess boundary makes per-step scripting
physically impossible rather than merely discouraged - a subprocess cannot be
invoked per step at any useful rate, and the measured round trip is 49 ms, which is
where PERF-7 puts the granularity floor.

Now built, with one honest caveat: the *boundary* is real, and the *containment*
inside it is partial. Wall-clock timeouts, a scrubbed environment, an isolated
interpreter and a scratch working directory are enforced; network, filesystem and
memory confinement need OS primitives this build does not use. That gap is stated
on every result and printed by `einzel ext list`, because a containment measure
claimed and not applied is worse than one absent and known to be. See
[Extensions](extensions.md).

### 4. No GPL dependency in the default build, ever

Where GPL functionality is genuinely useful it is invoked out of process as a tool
the user supplies, and its absence degrades a feature rather than blocking the
platform. Note the distinction that makes this workable: parsing a *format*
carries no obligation; linking a *library* does. `Directory.Packages.props`
carries a licence note on every entry.

## The result envelope

Every quantitative result carries its value, units, uncertainty or confidence
interval, the ensemble size or convergence measure behind it, and any active
warnings. The API offers no way to obtain the value alone.

`Measured` therefore exposes no property or method returning a bare magnitude. The
only route to the value is `Deconstruct`, which hands back the uncertainty, the
evidence, and the warnings in the same call. `MeasuredApiSurfaceTests` enforces
this by reflection, so the rule governs members nobody has written yet — the
concern being that a convenience accessor gets added by someone eventually and
then used everywhere.

To be exact about what is enforced: a caller can still write
`var (v, _, _, _) = result`. Discarding is possible; it is just visible and
greppable rather than the path of least resistance.

The rule survives to the wire. `MeasuredJson` is built *only* by deconstructing a
`Measured`, so no serialiser here can emit a number alone even if someone tried.

Preview status, extension attribution, and defect taint ride in the warning list
at a non-suppressible severity rather than as separate fields, because they
behave identically to validity warnings — one propagation path to get right
instead of four.

## Errors as recovery instructions

An error names a machine-readable code, the offending path as a JSON Pointer, the
constraint violated, the value observed, and a suggested correction.

```json
{
  "code": "UNITS_INCOMPATIBLE",
  "path": "/source/accelerationPotential",
  "constraint": "this field requires a quantity of dimension m^2 kg s^-3 A^-1",
  "observed": { "value": 4, "unit": "mm" },
  "suggestion": "'mm' has dimension m; supply a unit of dimension m^2 kg s^-3 A^-1"
}
```

Validation collects *every* error rather than throwing on the first, because the
recovery an agent wants is the whole list — fixing one unit per round trip is the
behaviour this avoids.

Codes are a compatibility surface that callers branch on. They are added, never
reworded or repurposed.

## A project is a directory

```
models/  studies/  figures/  results/  tests/  extensions/   small, text, tracked
AGENTS.md                                       generated + hand-written guidance
.einzel/                                        caches and trajectories; regenerable
```

Everything defining a modelling effort is small, text, and diffable; everything
large is derived and discardable. `.einzel/` can be deleted without losing
anything, because the run manifest fully determines the run.

Nothing touches version control. A plain folder is the default and fully
supported; `--vcs git` writes an ignore file and changes no behaviour anywhere
else. Requiring a repository would reinstate exactly the barrier the project
exists to remove.

The platform layer of `AGENTS.md` is **generated and version-stamped, never
hand-written**. Instructions shipped with one version that describe another are
worse than none, because an agent trusts them and cannot detect the drift.

## Build settings that are load-bearing

Two things in `Directory.Build.props` are not stylistic.

`TreatWarningsAsErrors` — a numerical engine held to 1 ppm cannot afford a culture
of ignored diagnostics. It caught a sign-extension bug in `Dimension` on the very
first build.

`GenerateDocumentationFile` with CS1591 unsuppressed, so undocumented public API
fails the build. Schema descriptions and CLI help are meant to generate from the
same metadata, which only works if the metadata exists.

`global.json` pins the SDK, because several .NET versions are typically installed
and the repository must not silently build on one that is out of support.
