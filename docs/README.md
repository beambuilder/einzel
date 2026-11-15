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
| [Living specification](../SPEC.md) | Every r06 requirement with its status and evidence, what changed and why, and what to do next |
| [Tutorial: a first lens](tutorial-first-lens.md) | A first model, start to finish |
| [Coming from SIMION](coming-from-simion.md) | What maps to what, for someone who knows SIMION |
| [Warning codes](warning-codes.md) | What each warning and error code means and what to do about it |
| [Architecture](architecture.md) | Assemblies, layering, the four invariants, and why each exists |
| [Model format](model-format.md) | Schema 0.14 in full: parameters and their provenance, expressions, fields, electrodes (rectangle, disc, polygon, prism, revolve, edge profile), source clouds, ion species, drives, sequences, bounded analytic elements |
| [Device templates](device-templates.md) | Writing a new device as data, and the shipped templates, each with what solving it bought |
| [Numerics](numerics.md) | Integrator, field solver, interpolation, and the accuracy budget |
| [Sweeps and optimisation](optimisation.md) | Tolerance studies, sensitivity fields, Nelder-Mead and CMA-ES |
| [Rendering](rendering.md) | Vector sections in SVG and PDF, conductor surfaces for the viewport, decimation bounds, and how a figure carries its own caveats |
| [Pressure](pressure.md) | Collision models, regime validity, and what gas does to a funnel |
| [Extensions](extensions.md) | The Python extension surface, what the sandbox contains, and what it does not |
| [Agent acceptance](agent-acceptance.md) | The prose-task suite, what it measures, and what gates a release |
| [Extending](extending.md) | The three kinds of change a device has needed below `Einzel.Library`, where each goes, and the traps each has sprung |
| [Lessons](lessons.md) | Bugs that presented as physics and were arithmetic |
| [CLI](cli.md) | Command reference, exit codes, and the agent loop |
| [Live session](live-session.md) | The shared attributed journal, and the MCP server over it |
| [The shell](shell.md) | The Avalonia shell (`src/Einzel.Shell`, cross-platform) and the earlier WPF one being retired: the views that exist, and the commands they needed |
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

## Where things stand

[`SPEC.md`](../SPEC.md) is the status page: every requirement with its status and the
measurement behind it, the delivery phases against what happened, the amendments, and a
ranked list of what to do next. It is kept current in the same change that alters what it
says. [`changelog.md`](changelog.md) is the chronological record of how each piece got there
and is not a status page.

Nothing here is released software: version 26.1.0 is in the tree and its GitHub release is a
draft, so the way to get Einzel today is to build it from source (see the
[README](../README.md)). Effort estimates, performance targets, and the numerical error
budget in the specification are engineering judgement rather than measured values, except
where these pages quote a measurement.

## Where to start

| You are | Read |
| --- | --- |
| **A user** trying it | [README](../README.md) for the build and a five-minute run, then [Tutorial: a first lens](tutorial-first-lens.md), then [CLI](cli.md). Coming from SIMION: [Coming from SIMION](coming-from-simion.md). When a warning appears: [Warning codes](warning-codes.md) |
| **A model author** | [Model format](model-format.md), [Device templates](device-templates.md), [Pressure](pressure.md) for gas, [Sweeps and optimisation](optimisation.md), [Rendering](rendering.md), [Validation](validation.md) for what is and is not checked |
| **An agent** (or someone building one) | [CLI](cli.md) and the generated `AGENTS.md` in a project, [Live session](live-session.md) for the MCP server, [Extensions](extensions.md), [Agent acceptance](agent-acceptance.md), [Warning codes](warning-codes.md) |
| **A contributor** | [`SPEC.md`](../SPEC.md), [Architecture](architecture.md), [Extending](extending.md) before adding a capability, [Numerics](numerics.md), [Lessons](lessons.md), [The shell](shell.md) |
