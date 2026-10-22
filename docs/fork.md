# The fork

`beambuilder/einzel` is a fork of [`maccoss/einzel`](https://github.com/maccoss/einzel)
(Apache-2.0). It exists to turn Einzel into the simulator an electrospray ion-beam
deposition (ESIBD) group needs in place of SIMION: solve a geometry once and fly
many ions against the cached field, explicit ion lists with per-ion birth time,
phase, energy and voltage, arbitrary periodic waveforms including measured
traces, ion-neutral collisions and space charge, and an agent (MCP) surface over
all of it. Every step is validated against SIMION, a closed form, or the
literature before it lands. What upstream can use goes back upstream, one topic
at a time, in contact with the author; what is specific to this work stays here.

The validation itself does not live in this repository. It is a separate
project with SIMION models, reference numbers and task folders; this repository
holds product code, tests and docs only.

## Branches

| Branch | Role |
| --- | --- |
| `main` | mirror of `upstream/main`. Never committed to. Fast-forward only, pushed after every sync. `git diff main esibd` is the fork's whole difference against the author's tree. |
| `esibd` | the fork. Receives topic branches by pull request and `main` by a merge commit after every upstream sync, titled "Merge upstream `<sha>`: <what changed>". Never rebased. |
| `feat/…`, `fix/…`, `measure/…`, `docs/…`, `tools/…` | one topic each. Branch from `esibd` for fork work; from `main` for anything meant for upstream, so that the upstream PR carries no fork-only commits. Deleted after merge. |
| `base/<upstream-sha>` tags | upstream bases on which the full reference corpus was re-run and passed. |
| `esibd/<YYYY-MM-DD>` tags | `esibd` after a validated upstream merge. |

Two long-lived branches, because fork-first means `esibd` accumulates features
upstream does not have. If they sat on `main`, every upstream sync would rebase
the whole fork history and every upstream PR would drag fork-only commits along.

Repository settings that are meant to hold this in place, set by the owner on
GitHub and not checked by anything in the repository: default branch `esibd`;
branch protection on `main` (no direct pushes) and on `esibd` (pull request
required, CI green required); squash merge and rebase merge off, so the commits
land as written; auto-merge off. CI (`.github/workflows/ci.yml`) runs on pushes
and pull requests to both `main` and `esibd`, on Ubuntu and Windows.

## Commits and pull requests

Upstream's standards apply unchanged (`CLAUDE.md`, `SPEC.md`): measure first,
every new test fails under a named mutation, docs move in the same change,
compiler warnings are errors (`Directory.Build.props`), every public member
carries XML documentation, nothing device-specific below `Einzel.Library`.

A commit subject is the one-sentence finding or behaviour change in upstream's
declarative style ("The basis fields persist between runs and `verify` knows
when they are stale"), not the mechanism and not a file list. The body says why,
what changed and where, what was measured before and after, which test fails
under which mutation, and which docs moved. Attribution trailers stay on every
commit, as they do upstream.

Every topic lands on `esibd` through a pull request whose body keeps the seven
headings of `.github/pull_request_template.md`, in order:

1. **Problem**: what is wrong or missing, with the evidence.
2. **Change**: what was done, why this way, alternatives rejected in one line each.
3. **Measurement**: before/after table; "Unverified" where a number is missing.
4. **Tests**: each new test and the mutation that makes it fail; the controls.
5. **Docs**: pages, changelog entry, SPEC row or amendment touched.
6. **Not in this PR**: follow-ups, known gaps, deferred decisions.
7. **How to verify**: the exact commands a reviewer runs and what they print.

Aim for under 400 changed lines of product code per PR; split along schema,
engine, CLI otherwise. Merge with a merge commit and delete the branch.

Fork changes are recorded in the changelog at the end of this page, not in
`docs/changelog.md`. That file is upstream's, `CLAUDE.md` imports it at every
session start, and every one of the six upstream merges of 2026-10-19/20
(`2352d7f` to `de73ec8`) added to its head, so a fork entry there would
conflict at every sync.

## Upstream

Anything meant for the author goes issue first: state the measurement and the
proposed change, wait for the answer, then open the PR against the issue from a
branch off `main`. One upstream PR per topic at a time. After an upstream merge,
sync `main`, merge into `esibd`, drop the fork-side copy of the change if it
diverged.

Sync routine, weekly or before any upstream PR:

```
git fetch upstream
git checkout main && git merge --ff-only upstream/main && git push origin main
git checkout esibd && git merge main -m "Merge upstream <sha>: <what changed>"
dotnet build -c Release && dotnet test -c Release --no-build
git push origin esibd
```

If the upstream diff touches the solver-behaviour version, `SPEC.md` or anything
under `src/Einzel.Fields` or `src/Einzel.Transport`, the reference corpus is
re-run before the new base is tagged. Open topic branches are rebased on the
updated `esibd` (or `main`, for upstream work) the same day.

## Building and testing

The repository needs the .NET SDK that `global.json` names (10.0.400 or a later
feature band). The same three commands build and test it on Windows and on
Linux; CI runs them on both:

```
dotnet build -c Release
dotnet test -c Release --no-build
dotnet test -c Release --no-build --filter FullyQualifiedName~<TestClass>
```

Measured at `de73ec8` on the development machine (Windows 11, SDK 10.0.402):
the build took 16 s after restore and the suite 4 min. The suite needs a
Python 3 on `PATH` for `Einzel.Extensions.Tests` and four `Einzel.Cli.Tests`;
on Windows those ten tests fail until upstream
[PR #58](https://github.com/maccoss/einzel/pull/58) (the sandbox keeps
`SYSTEMROOT`) is merged. The CLI is `src/Einzel.Cli/bin/Release/net10.0/einzel(.exe)`,
the MCP server `src/Einzel.Mcp/bin/Release/net10.0/einzel-mcp(.exe)`.

Fork-only helpers live under `tools/` and are listed in `Einzel.slnx`, so they
build in CI and any API drift shows up as a build failure. The first is
`tools/quadbatch`, the in-process batch driver (solve once, fly many ions with
per-ion RF phase) that the validation project uses. It builds each ion's field
with the library's own `DrivenSolvedField.Create` rather than a copy of that
class, and keeps its basis solves in a project's field cache when the job names
one; [Using Einzel as a library](library.md) shows the same path in one example
that the test suite runs. It is retired when the library also takes explicit ion
lists.

## How the validation project uses this repository

The validation project builds a specific `esibd` commit (or a `base/<sha>` tag)
from a sibling clone, points its scripts at `src/Einzel.Cli/bin/Release/net10.0`
and `tools/quadbatch/bin/Release/net10.0`, and records the commit hash with
every result. Nothing from that project is committed here: no SIMION
files, no field caches (`.einzel/`), no trajectories, no raw benchmark output,
no personal paths.

## What never enters this repository

SIMION files and outputs; `.einzel/` caches; trajectories, `.vtu` and other
generated outputs; raw benchmark output (`bench/results/raw/`, once the
benchmark suite exists); exported ion lists outside small test fixtures;
personal absolute paths; machine-specific settings; credentials or API keys;
session or scratch files. Binary test fixtures only when small (under 100 KB)
and justified in the commit.

## Fork changelog

Newest first. One entry per merged pull request: the finding, the measurement,
the PR number.

- **A cloud and an ion list fly through one per-row core, and the answer does
  not depend on how many fly at once.** `IonListFlight.Fly` flies rows of an
  ion list as independent trajectories, written back by index, on up to
  `maxParallelism` threads; a row born later sees a time-shifted field, a row
  born at 0 the field itself, and row k's collisions draw from the gas seed
  plus k. `FiguresOfMerit.FlyCloud` now flies its cloud through the same core
  at one thread, so every caller keeps its flight: `run` on five cloud examples
  and the 48-ion quadrupole cloud gives the same result document as before, and
  a test holds the routed cloud equal to the old loop on nine shipped examples,
  two of them in gas. One thread or eight give the same bits on a 304-row list;
  every field kind on the evaluation path was read for mutable state and holds
  none. [Ion lists](ion-lists.md). PR #11.
- **Seeded generators expand to rows, and a seed gives the same rows.** A
  set without `cloud` builds its ions from constants and generators
  - `uniform`, `gaussian`, `sequence`, `linspace` on any scalar, `disc` and
  per-axis scalars on the position, `cone` with `fill` on the direction - with
  one `System.Random(seed)` per set and a fixed, documented draw order, so a
  set is prefix-stable in `n` (except `linspace`); a birth-phase grid is a
  `sequence` on `rfPhase`. Each generated ion is compiled as a written row, so
  a set of constants equals the same rows written out, bit for bit. Measured
  with fixed seeds at n = 10,000: means and variances within four standard
  errors, a disc uniform in area and a cone uniform in solid angle by
  chi-square below the 0.1 % point; one expanded set per generator is compared
  with stored golden rows, bit for bit for `uniform`, `sequence` and `linspace`
  on every runner and for the five that call the maths library on Windows,
  within 1e-12 relative elsewhere. `docs/model-format.md`
  now documents `source.cloud.divergence`, which it had said did not exist;
  the same remark on `CloudDocument` and `IonCloudSettings` is corrected.
  [Ion lists](ion-lists.md) gains the generators and a SIMION `.fly2` parity
  table. PR #10.
- **An ion list is a document, and its `cloud` set draws what `run` draws.**
  `<name>.ions.json` (schema `0.1`, unknown keys refused with their JSON
  pointer) lists ions as rows - id, mass-to-charge, charge, position,
  direction, kinetic energy or acceleration potential, birth time or RF phase,
  tag - and as `cloud` sets; every omitted attribute comes from the model's
  `ion` and `source`, with the validator's own arithmetic, so a row with only
  an id is the model's ion to the bit. `IonList.Expand` compiles a list against
  a model into `IonRow`s, refusing each mistake by code and pointer; a `cloud`
  set equals `IonCloud.Draw` as `einzel run` calls it, bit for bit, on all 15
  shipped examples with a cloud. The expanded rows are written as `.ions.csv`
  in SI with round-trip formatting and read back to the same bits; a CSV is
  accepted as an ion list too, against the same model checks. A birth more
  than 1000 maximum flight times late is refused at its row. Nothing flies a
  list yet, and no existing output changes: five cloud examples and the 48-ion
  quadrupole cloud give the same `run` result as before.
  [Ion lists](ion-lists.md). PR #9.
- **A host can weight its own basis fields into a driven field, and the batch
  driver does.** `DrivenSolvedField.Create` is the public, validated way to
  build the per-ion field, direct plus amplitude times w(f t + phase) over
  basis fields a host has solved; [Using Einzel as a library](library.md)
  shows solve once, fly many in an example the test suite runs.
  `tools/quadbatch` drops its 65-line copy of the class for it and can keep
  its basis solves in a project's field cache (`cacheRoot`). Rebuilt on
  `Create`, the harness reproduces its recorded output bit for bit on the 12
  check ions and on the full `twod` and `main_c0.4` jobs (25,824 ions each),
  cached or not; its four 1.09 M-node basis fields are ready in 0.21 s from
  the cache against 11.3 s solved, and a 517-ion job takes 3.0 s against
  14.4 s. No number on the `einzel run` path changes. `SPEC.md` Amendment 59.
  PR #8.
- **A second run of an unchanged geometry loads its field instead of solving
  it.** Each basis channel `run`, the studies, `test` and `solve` solve is
  kept under `.einzel/cache/solver-<version>-<compute path>/<channel hash>` as raw doubles
  and a record, checked against its hash, version, compute path, grid and
  SHA-256 on every load, and solved again on any failure. On the task-10
  quadrupole (8.54 M nodes, two channels) the second `einzel run` is
  field-ready 0.34 s after the process starts against
  48.9 s, 0.76 s against 49.3 s for the whole run,
  for 136.6 MB of cache; the cached potentials are bit-identical to
  the `be63a4b` export. `--no-cache`, `einzel cache status|clear|prune`, and
  `verify` reports a cache entry that no longer holds the field a result
  used. `SPEC.md` `FLD-3` from Partial to Met; Amendment 58. PRs #4–#7.
- **A solved element has a key that names its physics, not its text.**
  `SolveHash` hashes each solved element's compiled geometry and each basis
  channel's pattern on it (a static element's declared potentials), after
  compilation, so a comment, a rename or a reformat leave it and a tenth of a
  micron on one electrode moves it; the mirror plane and the extrusion tilt,
  applied after the solve, are left out. `einzel solve` prints both hashes
  per channel with the solver-behaviour version; run manifests record them
  per element (`solves`). Keys are computed before the solve from the
  decomposition the solve makes, and a test holds them equal to the keys of
  the channels actually solved. No numbers change. `SPEC.md` `FLD-3` set
  from Met to Partial: the key exists, the cache does not. PR #3.
- **The task-10 batch driver lives here as `tools/quadbatch`.** Solve once,
  fly many ions with a per-ion RF phase, in process, until the library's
  public surface offers the same; listed in `Einzel.slnx` so CI builds it.
  Input handling hardened on review (gas refusal, warnings in the output,
  NaN-safe JSON). Re-run on `esibd` `73956dd`, every recorded number of the
  validation project's task 10 came back bit-identical. PR #2.
- **Fork work lands on `esibd` by pull request; `main` mirrors upstream.**
  `esibd` branched from upstream `de73ec8`; CI runs on it; this page records
  the branch model, the PR body, the sync routine and what stays out. At
  `de73ec8` on the development machine (Windows 11, SDK 10.0.402):
  `dotnet build -c Release` 0 errors, 16 s after restore, one warning
  (`AVLN3001` from the Avalonia XAML compiler on `Einzel.Shell`; it is not a
  C# compiler warning, so `TreatWarningsAsErrors` does not catch it);
  `dotnet test -c Release --no-build` 1747 tests, 1737 passed, 10 failed, all
  ten the Python-sandbox `SYSTEMROOT` defect (`Einzel.Extensions.Tests` 6,
  `Einzel.Cli.Tests` 4). PR #1.
