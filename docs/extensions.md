# Extensions

Agents must extend the platform, not only drive it. An extension path requiring
C#, a compile and a restart is not usable in a loop, so the language is Python and
the sandbox is paid for architecturally — embedded CPython cannot be sandboxed
in-process.

## The authoring loop

```
einzel ext register shortest        # scaffolds a working extension
einzel ext test shortest --input payload.json
einzel ext list                     # what is installed, and what this build cannot contain
```

`register` writes an extension that **runs immediately**, for the same reason
`einzel init` writes a model that runs: an agent with a template that works can
change one thing and see what happens, where an agent with a stub has to fix
somebody else's code before it can start.

An extension is a folder under `extensions/` with a manifest and a Python file.
Nothing has to be told about it — copying a folder in is a complete install,
because a project is a directory.

## One manifest, two runners

```json
{
  "name": "shortest",
  "version": "0.1.0",
  "kind": "objective",
  "trust": "sandboxed",
  "entry": "extension.py",
  "function": "run",
  "figures": ["resolvingPower", "flightTime"],
  "engineMinimum": "26.1.0",
  "outputSchema": {
    "type": "object",
    "required": ["value"],
    "properties": { "value": { "type": "number" } }
  }
}
```

EXT-1: type, schemas, trust, resource needs, engine range. **The runtime is an
implementation detail of the manifest**, which is the point of having one — the
same declaration can be run two ways without the extension knowing which.

`trust` defaults to **sandboxed** rather than being something a manifest opts into,
because a trust level that has to be asked for is one that gets granted by
accident. The in-process runner (EXT-2, CSnakes) is not built; the subprocess
runner is, and it is the default.

**Five extension points**, all coarse-grained: `geometry`, `analysis`, `objective`,
`sequence`, `interchange`. What is *closed* is per-step physics, the field solver
inner loop, and the integrator — and the process boundary makes that structural
rather than advisory. EXT-4 gives an extension one call per run; a subprocess
cannot be invoked per integration step at any useful rate, so per-step scripting is
not discouraged here, it is impossible.

## What the sandbox does, and what it does not

This is the part to read before running anything agent-authored.

| Measure | Enforced |
| --- | --- |
| Wall-clock timeout, process tree killed | **yes** |
| Output size ceiling | **yes** |
| No inherited environment (`SYSTEMROOT` alone on Windows) | **yes** |
| Interpreter isolation (`python -I`) | **yes** |
| Scratch working directory, not the project | **yes** |
| No stdin beyond the payload | **yes** |
| **No network** | **no** |
| **Filesystem confinement** | **no** |
| **Memory and CPU ceilings** | **no** |

EXT-3 asks for job objects and a restricted token on Windows, and namespaces and
seccomp on Linux. Neither is built. What is built is everything that can be done
portably from managed code.

**The gap is stated rather than implied by the word "sandbox".** A containment
measure that is claimed and not applied is worse than one that is absent and known
to be: the first makes someone run untrusted code they would otherwise have read
first. So `extension.isolation-incomplete` is attached to every sandboxed result,
is a **validity violation** (GRD-3 forbids suppressing it), and `einzel ext list`
prints the unenforced list on stderr every time.

The environment scrub is worth its own line: a child that starts with the parent's
environment starts with its credentials, its proxy settings, and its `PYTHONPATH`.
The child sees **one** environment variable on Windows, `SYSTEMROOT`, and **none**
anywhere else, and a test asserts the list rather than a count.

`SYSTEMROOT` is kept because CPython cannot start without it there. It seeds its hash
randomisation from the operating system's random source before it runs anything, and up
to 3.10 it reaches that source through CryptoAPI (`CryptAcquireContext`), which does not
work in a process with no `SYSTEMROOT`: with a bare `Clear()` every extension died before
its first line with "Fatal Python error: _Py_HashRandomization_Init: failed to get random
numbers to initialize Python", and `einzel ext test` failed on every project. Measured
with the sandbox's own `-I -B` flags: 3.9.13 dies with an empty environment and starts
with `SYSTEMROOT` alone; `WINDIR` alone, `TEMP` and `TMP` alone, and `PATHEXT` alone each
leave it dead, so none of them is kept; 3.11.5 starts with nothing at all, because 3.11
moved to `BCryptGenRandom`. The variable is the path of the Windows directory, not a
credential, a proxy setting or an import path. Linux and macOS keep nothing.

## Attribution, per GRD-6

An extension result carries the extension's name and version and **cannot present
itself as first-party**. A figure of merit computed by somebody's Python stays
distinguishable from one the engine computed however far downstream it travels.

## Output is checked against the declared schema

EXT-7. Without it an extension returning the wrong shape produces a null or a zero
somewhere downstream and the traceback points at the *engine* — which is exactly
the debugging session that makes people stop writing extensions.

`SchemaCheck` implements a deliberate subset of JSON Schema: `type`, `required`,
`properties`, `items`, `enum`, and numeric bounds. That covers what an extension
contract actually says, and the alternative — a dependency implementing the whole
specification including remote `$ref` resolution — would put a network fetch inside
a sandbox whose entire purpose is not having one. Unrecognised keywords are
**ignored rather than refused**, so a richer schema written for a human reader still
validates on the parts this understands.

## The traceback reaches the caller

AGT-3 makes an error a recovery instruction, and the only thing that says what went
wrong inside somebody's Python is their own traceback. It is carried into the
`EinzelError` suggestion, truncated rather than dropped.

## An extension as a figure of merit

Section 13 has an optimiser composing objectives from section 12, which may be
Python extensions. A study names one with an `ext:` prefix:

```json
{ "figureOfMerit": "ext:shortest", "sense": "minimise", ... }
```

A prefix rather than a new field, because a figure of merit is already selected by
name and `ext:` is a namespace rather than a second mechanism — which is what keeps
the optimiser from having to know the difference.

The extension is handed the model's declared parameters in SI and whichever
built-in figures its manifest asks for. **Declared rather than inferred**, because
each ensemble figure flies a cloud and computing all of them for every draw of a
thousand-draw study would spend most of the study on numbers nobody asked for. A
figure that could not be computed is present and `null` rather than absent, so an
extension can tell "this design loses its beam" from "you did not ask for that".

An extension objective is reported dimensionless and under its own name. That is
honest rather than lazy: the extension returns a bare number, and inventing a unit
for it here would be the platform asserting something only the author knows.

## Measured

| | |
| --- | --- |
| Bare interpreter launch, cheapest of five | **45-63 ms** across seven runs |
| Sandboxed round trip, cheapest of five | **49-61 ms** |
| The platform's own share | **1.08x to 1.52x** a bare launch, once *below* it |
| Environment variables the child sees | 1 on Windows (`SYSTEMROOT`), 0 elsewhere |
| Runaway extension killed at a 1200 ms declared timeout | 1276 ms |

**The round trip is process start almost entirely, and that is measured rather than
asserted.** PERF-7's whole 50 ms budget straddles the cost of launching the interpreter
and doing nothing, so the budget is not separable from CPython's own start cost - a test
asserting it is asserting that Python started quickly this time, and the old one passed
and failed on the same commit in two CI runs minutes apart. What is asserted now is the
platform's own share, which is scale-free and holds on a build agent taking seconds per
launch just as it does here.

That *strengthens* EXT-4 rather than weakening it: the boundary costs ~50 ms and nothing
here can reduce it, so anything needing to happen more often than that cannot be an
extension - a structural argument rather than a measured coincidence. It also gives the
open question about the in-process runner its first real evidence, since removing the
process is the only thing that would meet PERF-7 as written. SPEC.md Amendment 27.

**A kill has to be waited on.** `Process.Kill` only *asks*: it returns before the
operating system has finished, so a timeout that does not then wait has not bounded
anything - the extension is still running and still holding whatever it had open.
On Windows that showed up immediately as a working directory that could not be
deleted, which is how it was found, on CI rather than locally.

## Not built

- **The in-process runner (EXT-2).** CSnakes is alive and maintained — 1.2.1, some
  450,000 downloads at the time of writing — and its licence has not been checked
  against LIC-1 because nothing depends on it yet. The subprocess runner is the
  default and the security-relevant one, so it came first.
- **A vendored interpreter (EXT-6).** One is *discovered* instead, and `einzel
  doctor` says so rather than passing it off as the vendored path. Discovery means
  an extension behaves differently on a machine with a different Python.

  **This is an installer decision, deferred to the installer.** The likely shape is
  `uv python install --install-dir` into a directory einzel owns under the user's
  data folder: uv is Apache-2.0 or MIT, the standalone CPython builds it fetches are
  permissive, and pointing at a directory einzel owns rather than at uv's shared
  store keeps another tool's `uv python uninstall` from breaking this one. Two
  constraints it has to respect whenever it happens: provisioning downloads, so it
  can never be implicit in a run (UPD-2 forbids the CLI touching the network, AGT-8
  makes the environment stable within a session), and resolution has to stay a
  filesystem lookup so PERF-8's 500 ms cold start survives it.

  **The manifest now records which interpreter ran an extension**, because PRJ-3
  says a manifest fully determines its run and an extension result depends on which
  Python computed it. It is `null` when no extension ran, rather than filled in with
  whatever was on the path: an interpreter that took no part in a run is not
  provenance, and recording it would imply it mattered. That is also why discovery is
  lazy - finding an interpreter costs a process start, and a run that never needed
  one should not pay for it. Once EXT-6's vendored interpreter exists this becomes a
  version rather than a path, which is the better thing to record: a path on one
  machine is not the same interpreter as the same path on another.
- **Shared-memory transport (EXT-5).** Large arrays should cross by shared memory
  with an Arrow or raw-buffer layout, never by JSON. The small-payload path is
  built, which is what an objective or an analysis extension needs. The manifest
  does not mention the transport, so adding the buffer path later changes no
  extension that does not want it.
- **OS-level isolation.** See the table above.
- **The updater's compatibility report (EXT-8).** The comparison is built and
  `einzel ext list` uses it; the updater that would run it before applying an
  update is not.
- **Geometry, sequence and interchange extension points.** Declared in the manifest
  and refused where a number is expected. Only `objective` and `analysis` are wired
  to anything.

## The cleanest extension of all

The spec's own aside, worth repeating: for model authoring specifically, the best
extension is not an extension. A Python script in `extensions/` that emits a model
document has zero coupling to the engine, needs no sandbox because it never runs
inside the platform, and is trivially testable.
