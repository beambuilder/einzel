# Einzel documentation

Einzel is an open-source, agent-native ion-optics platform — an open replacement
for SIMION. It covers DC and RF ion optics across nine decades of pressure, and
is designed so that an AI agent, not only a human, can author models, run
studies, read results, and extend the platform.

The authoritative design document is `einzel-software-spec-r06.html` at the
repository root. These pages document what has been **built**, why it was built
that way, and what was learned building it. Where the two disagree, the spec
states the intent and these pages state the reality; both are noted.

## Contents

| Page | What it covers |
| --- | --- |
| [The fork](fork.md) | Why `beambuilder/einzel` exists, its branch model (`main` mirrors upstream, `esibd` integrates), the PR body, the upstream sync routine, and what never enters the repository |
| [Living specification](../SPEC.md) | Every r06 requirement with its status and evidence, what changed and why, and what to do next |
| [Architecture](architecture.md) | Assemblies, layering, the four invariants, and why each exists |
| [Model format](model-format.md) | Schema 0.9 in full: parameters, expressions, fields, electrodes (rectangle, disc, polygon, edge profile), source clouds, drives, sequences |
| [Ion lists](ion-lists.md) | The `.ions.json` document: explicit rows with per-ion species, position, direction, energy, birth time or RF phase and tag; the `cloud` set that draws what `run` draws; seeded generators (uniform, Gaussian, sequence, linspace, disc, cone); the expanded `.ions.csv` that reproduces a list to the bit; flown by `einzel fly` |
| [Device templates](device-templates.md) | Writing a new device as data, and the nineteen shipped templates, each with what solving it bought |
| [Numerics](numerics.md) | Integrator, field solver, interpolation, and the accuracy budget |
| [Sweeps and optimisation](optimisation.md) | Tolerance studies, sensitivity fields, Nelder-Mead and CMA-ES |
| [Rendering](rendering.md) | Vector sections in SVG and PDF, conductor surfaces for the viewport, decimation bounds, and how a figure carries its own caveats |
| [Pressure](pressure.md) | Collision models, regime validity, and what gas does to a funnel |
| [Extensions](extensions.md) | The Python extension surface, what the sandbox contains, and what it does not |
| [Agent acceptance](agent-acceptance.md) | The prose-task suite, what it measures, and what gates a release |
| [Lessons](lessons.md) | Bugs that presented as physics and were arithmetic |
| [CLI](cli.md) | Command reference, exit codes, and the agent loop |
| [Using Einzel as a library](library.md) | Solve once and fly many ions in process: the assemblies a host references, an example the test suite runs, a drive per ion with `DrivenSolvedField.Create`, and the time-free trap |
| [Live session](live-session.md) | The shared attributed journal, and the MCP server over it |
| [The shell](shell.md) | WPF on Windows: the seven views that exist, and the commands they needed |
| [Literature targets](literature-targets.md) | Published instruments to reproduce, and what each needs |
| [Validation](validation.md) | The test tiers, what each proves, and what is not covered |
| [Spec findings](spec-findings.md) | Places where building it revealed something about the specification |
| [GPU handoff](gpu-handoff.md) | Whether a GPU is worth it here, the probe that answers it on any machine in ten minutes, and which kernel to port |
| [Astral log](astral-log.md) | **A chronological log, not a status page.** Eighty sections of the Astral reconstruction, several reversing earlier ones. Read it backwards, and take the model's current position from [Device templates](device-templates.md) instead |
| [Changelog](changelog.md) | **A chronological record, not a status page.** Stage by stage, what was built and measured and the mistakes on the way. `CLAUDE.md` imports it, so every Claude Code session starts with it. Take the current state from the [living specification](../SPEC.md) and the pages above |

## The two ideas everything follows from

**Open and free.** No licence, no seat count, no barrier to a student or a
collaborator running the same model. This is not only a licensing position: an
agent working against a closed binary infers behaviour from documentation and
guesses the rest, while an agent working against Einzel can read the
implementation.

**Agent-native.** Designed so the loop of *read files, edit files, run commands,
read output* closes in seconds, with no protocol and no session. A project is a
directory. Every capability reachable from a future GUI is reachable from the
command line and from MCP, through the same command objects.

These reinforce each other, and they share a danger. Agent-friendliness makes
wrong answers cheaper to produce: an agent generating fifty plausible transmission
numbers in an afternoon, three of them computed outside the validity of the model
used, is worse than a slow tool that forces a human to look at each one. That is
why the guardrails are engine behaviour rather than advice, and why
[Numerics](numerics.md) and [Validation](validation.md) are as detailed as they
are.

## Current state

Stages 0 through 5 of the delivery plan are complete: units and the result
envelope, the trajectory integrator, the model format and CLI, the field solver,
Class T analysis with device templates, and `Einzel.Sweeps` — tolerance Monte
Carlo, sensitivity ranking, and both optimisers.

Since then: source **ion clouds** and the Class S figures they make possible, a
**space-charge screen** that estimates what is not modelled and warns
non-suppressibly, a **time-domain RF** path that recovers the Mathieu and Meissner
stability boundaries against published values, and **emittance** — which completes
the Class T figures §12 asks for and doubles as a Liouville check on the
integrator.

Then, in order:

- **`Einzel.Render`** — vector sections in SVG and PDF, drawn headlessly in CI with
  no display attached, with conductors traced from their own signed distance so the
  renderer carries no device knowledge.
- **Pressure** — two event-driven collision models checked against the Langevin rate
  coefficient, equipartition and Mason-Schamp mobility, the `ITransportMode` seam
  REG-1 asks for, and REG-2 regime validity computed on every run rather than
  assumed.
- **Extensions** — a manifest, a sandboxed subprocess runner at a 49 ms round trip,
  output validated against the declared schema, and a Python objective the optimiser
  can drive.
- **Statistical diffusion** — the second transport mode REG-1 makes a peer of
  trajectory integration, validated against an exactly stationary Boltzmann
  equilibrium, and agreeing with the event-driven mode to 0.43 standard errors in
  the overlap band REG-3 names. Since extended with a cycle-averaged effective
  potential, so the 1e-2 to 10 mbar band where funnels and travelling-wave guides
  actually run has a description at all.
- **Space charge** — the direct pairwise sum SC-1 names as the reference an
  approximate method is validated against, which first corrected the screening
  estimate it replaced by a factor of 527.
- **The density as an output** — exported as `.vti` and drawn as decade contours, so
  RND-8's prohibition on trajectories through a diffusive region has something on
  the other side of it. Interior electrodes now absorb for the whole run rather than
  only emptying the seed, and a declared gas velocity reaches the diffusive solver
  instead of being dropped at it.
- **`einzel scan`** — one parameter across a range, one row per point. The third
  study driver, and the operation every curve in these pages had previously been a
  hand-written loop in a test file.

Not yet built: the in-process extension runner, the compute dispatch layer, the MCP
server, the update mechanism, and the shell. Of the render verbs, `section` exists;
`still` and `animation` do not. The examples corpus EX-1 asks thirty models for has
one.

Nothing here is released software. Effort estimates, performance targets, and the
numerical error budget in the specification are engineering judgement rather than
measured values, except where these pages quote a measurement.
