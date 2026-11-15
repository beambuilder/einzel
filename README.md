# Einzel

An open-source, agent-native ion-optics platform: an open replacement for SIMION.

Einzel models the ion optics of mass spectrometers — einzel lenses, quadrupole mass
filters, ion funnels, travelling-wave guides, multipole guides, linear and 3-D ion traps,
orthogonal accelerators, reflectrons and multi-reflection time-of-flight analysers —
across nine decades of pressure, from a single ion in vacuum to a density field in a
millibar of gas. It is a C# engine with a scriptable command line, a Python extension
surface, a live-session server for AI agents, and a windowed viewport.

**Two ideas shape everything else.** It is *open*, so a student, a collaborator, or a
reviewer can run the same model without a license or a seat. And it is designed *from the
engine outward for an agent to drive*: the command line is the primary surface, every
result carries its units, its uncertainty and its warnings, and every error is a recovery
instruction rather than a complaint.

## Status

**Version 26.1.0 is the version in the tree, and there is no public release yet**, so today the way to
get Einzel is to build it from source, below. When a release is published on
[GitHub Releases](https://github.com/maccoss/einzel/releases) it will offer:

- **Windows: an installer**, `MacCossLab.Einzel-stable-Setup.exe`. Per-user, no administrator rights,
  .NET included. It is **not code-signed yet, so SmartScreen warns on first run: click More info, then
  Run anyway.** It installs into `%LocalAppData%\MacCossLab.Einzel\current`, adds Start menu and desktop
  shortcuts to the window, and puts `einzel` on your user PATH for terminals opened afterwards. The window
  checks for a newer release once when it starts and offers it; nothing is downloaded without a click, and
  the command line never contacts the network. Uninstall from *Settings > Apps*.
- **Every platform: one portable archive** (Windows, Linux and macOS, x64 and arm64), holding `einzel`,
  `einzel-mcp` and `einzel-shell`. The archives are **framework-dependent**: install the .NET 10 runtime
  first (check with `dotnet --list-runtimes` for `Microsoft.NETCore.App 10.x`). On Linux this is the way
  to install; there is no installer and no updater there.

[`docs/install.md`](docs/install.md) has the details. Versions are `YY.feature.patch` - two-digit year, a
feature number, a patch number - and `release-notes/` explains the scheme and carries the notes for each
release.
The engine, the command line, the extension surface, the live-session server and the
cross-platform shell are built and tested. See [`SPEC.md`](SPEC.md) for every requirement's
status with the measurement behind it, and for what is being worked on next.

## Build and run

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). The exact version is
pinned in [`global.json`](global.json); newer patch releases are accepted.

```bash
git clone https://github.com/maccoss/einzel.git
cd einzel
dotnet build --configuration Release
dotnet test
```

The command line lands in `src/Einzel.Cli/bin/Release/net10.0/einzel` (`einzel.exe` on
Windows). Put that directory on your `PATH`, or run it by full path.

Everything builds and runs on Linux and Windows, and continuous integration builds and
tests both on every change. The shell (`src/Einzel.Shell`, `einzel-shell`) is Avalonia on
plain `net10.0`, so it is not tied to Windows; the test CI runs on Windows and Linux only, so macOS is untested.
A first walk through the tools is in [`docs/tutorial-first-lens.md`](docs/tutorial-first-lens.md).

## Five minutes

```bash
einzel init myproject
einzel run myproject/models/reflectron.json
```

`init` writes a project — a directory of small text files — containing a parameterised
single-stage reflectron and a test for it. `run` flies an ion through it:

```
flight time   10.180506 +/- 6.05E-11 us
              convergence in integrator tolerance, observed order 1.3 of 1
energy drift  1.08E-011 relative (ACC-4 budget 1e-6)
steps         141, 0.2000 m advanced analytically
final x       -100.000000 mm
engine        26.1.0+<commit>, model sha256:2e3ceb9...
```

That 10.180506 µs has a closed form, and the shipped test asserts against the closed form
rather than against a number this engine once produced:

```bash
einzel test myproject
```

From here, `einzel outline` lists a model's declared parameters with their units and
bounds, `einzel new --from-template quadrupole` starts a different device, and
`einzel run --vtu` writes a trajectory ParaView can open.

```bash
einzel --help          # every command
einzel templates       # the shipped device templates
einzel examples        # reference models, each checked against a closed form
einzel doctor          # what this installation can and cannot do
```

## Three surfaces, not a stack

The command line, the live-session server and the shell are peers. All three drive
the same command objects, so nothing exists in one that cannot be reached from another.

| | |
| --- | --- |
| `einzel` | the primary surface: JSON on every verb, results on stdout and diagnostics on stderr, a distinct exit code per failure class, no network call in any path |
| `einzel-mcp` | a Model Context Protocol server for an agent working alongside a person on one model, with a shared attributed journal and undo |
| `einzel-shell` | a cross-platform window (Avalonia) over the same model: a parameter tree edited through the shared journal with undo, a 3-D viewport you can orbit, pan and zoom, panels for results, regime, sequence, project and extensions, and a journal that records each action as the `einzel` command that would reproduce it. `src/Einzel.Wpf`, the earlier Windows-only window, is scheduled for deletion |

## What is in the box

**Fields.** Geometric multigrid in 2-D, axisymmetric and 3-D, with cut-cell boundaries so
a curved electrode is a curve rather than a staircase; second-order convergence measured
rather than assumed. Basis superposition, so an RF structure is solved once and driven by
making its weights functions of time — a 96-ring travelling-wave guide reduces to two
solves.

**Transport.** Adaptive Dormand–Prince integration landing exactly on stopping surfaces,
field discontinuities and collision instants; event-driven hard-sphere and Langevin
collisions; a drift-diffusion density mode for pressures where trajectories stop meaning
anything; direct-sum and particle-in-cell space charge. Which description applies is
computed and reported, not assumed.

**Devices as data.** A device is a JSON document, not a class: parameters with units and
bounds, expressions checked dimensionally, electrodes as shapes. Templates ship for the
einzel lens, quadrupole and multipole guides, ion funnels (including the PNNL one), a
travelling-wave guide, Paul, Kingdon, rectilinear and linear ion traps (2-D and 3-D, and the Stellar's), a curved
C-trap, an orbital analyser, planar and Astral-style multi-reflection analysers (full size and one fifth), and
the timsTOF-style mobility tunnel, front end and tandem (`einzel templates` lists them).

**Analysis.** Arrival-time peaks, resolving power, transmission itemised by the surface
each ion struck, emittance, turn-around time, secular frequency spectra. Tolerance Monte
Carlo, parameter scans, boundary bisection, and two optimisers.

## How it is checked

There is no SIMION license here, so cross-code comparison is not available and the load is
carried by two other tiers. **Analytic**: closed-form fields with exact trajectories — a
single-stage reflectron reproduced to 1e-10 relative, four orders inside the 1 ppm budget.
**Literature**: published instruments reproduced against published numbers, none of them
produced by this code — the Mathieu stability boundary on solved round rods, Denison's
quadrupole rod ratio, the Langevin rate coefficient, equipartition in a gas, the LTQ's unit
resolution to m/z 2000. [`docs/validation.md`](docs/validation.md) lists what each tier
proves and, as importantly, what is not covered.

Every quantitative result carries its value, its units, its uncertainty, the ensemble size
or convergence measure behind it, and any active warnings. There is no way to ask the API
for the bare number, which is deliberate.

## Documentation

| | |
| --- | --- |
| [`SPEC.md`](SPEC.md) | the living specification: every requirement, its status, the evidence, and what to do next |
| [`docs/tutorial-first-lens.md`](docs/tutorial-first-lens.md) | a first model, start to finish |
| [`docs/coming-from-simion.md`](docs/coming-from-simion.md) | for someone who knows SIMION: what maps to what |
| [`docs/warning-codes.md`](docs/warning-codes.md) | what each warning and error code means and what to do about it |
| [`docs/`](docs/README.md) | architecture, model format, device templates, numerics, pressure, rendering, extensions, the CLI, and the lessons from bugs that presented as physics |
| [`einzel-software-spec-r06.html`](einzel-software-spec-r06.html) | the original design document, kept unchanged as the record of intent |

## License

[Apache 2.0](LICENSE). No dependency in the default build is under a copyleft license, and
that is a rule rather than a coincidence: GPL tools are invoked out-of-process where they
are used at all, and their absence degrades a feature instead of blocking the platform.
