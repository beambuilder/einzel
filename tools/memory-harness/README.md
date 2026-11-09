# memory-harness

What a solve or a diffusive run holds, per node, in a fresh process - as a **live set**, which is
what `einzel estimate` states and what has to fit, and as a **working set**, which is what the
operating system supplies to an unconstrained process and what a task manager shows. The same
run measured both ways, one process each.

It exists so the estimate's memory statements can be checked from outside the test suite, and so
the gap between the two quantities is a number rather than a remark. Not part of `Einzel.slnx`,
and with its own `Directory.Build.props` so the engine's analyzers do not apply to it.

```bash
dotnet build tools/memory-harness -c Release
python tools/memory-harness/sweep.py plane            # the working set; likewise driven, volume,
python tools/memory-harness/sweep.py plane --live     #   channels, diffuse, species, meanfield
python tools/memory-harness/sweep.py table            # bytes a node, one row per run
```

## The two quantities, and why one process each

**`--live`: the peak live set.** A thread forces a full collection, reads the collector's own
record of what survived it, and waits as long again as the collection took, for as long as the
work runs - the sampler `SolveMemoryTests` uses, whose remarks give the reason for each choice.
This is what `SolveMemoryModel` and the diffusive constants state.

**Default: the peak working set**, `Process.PeakWorkingSet64` after the work minus before it, with
the managed heap polled lightly beside it. Garbage the collector has not yet reclaimed occupies
physical memory exactly as live data does, so this is what an unconstrained run actually takes.

They need separate processes because measuring the first disturbs the second: forcing a full
collection every few hundred microseconds is what makes the live peak visible, and it is also
what keeps the working set down. And the peak working set only ever rises, so a second run in one
process would read the first one's peak. `sweep.py` starts one process per point, sequentially.

**The working set is not a bound.** The collector frees large arrays lazily when memory is
plentiful and gives them back when it is not: under a hard heap limit the Astral at 4 mm completed
at 279 MiB and ran out at 275, against a live peak of 259 (`docs/cli.md`). So the live set plus a
few percent is what a run needs, and the working set is what it will take if nothing stops it.

## Measured

Release, commit `de73ec8` (main after #55 and #57), an Intel Core i9-9900K (8 cores, 16 threads)
with 63.8 GB, idle; bytes a node; `est` is what `einzel estimate` states. The rows are what
`sweep.py plane`, `volume` and `diffuse` run - the volumes at their 1 and 0.5 mm cells and the
drift tube at 512 and 1024 intervals - once without `--live` and once with it.

| case | nodes | live | working set | working set / live | est | kept |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| plane, quadrupole | 1,050,625 | 143.3 | 295.9 | 2.06 | 143.2 | 8.0 |
| plane, quadrupole | 4,198,401 | 143.3 | 320.1 | 2.23 | 143.2 | 8.0 |
| plane, quadrupole | 16,785,409 | 143.3 | 304.6 | 2.13 | 143.2 | 8.0 |
| volume, segmented quadrupole | 140,481 | 49.7 | 116.2 | 2.34 | 51.5 | 8.0 |
| volume, segmented quadrupole | 1,085,825 | 48.6 | 204.9 | 4.22 | 51.4 | 8.0 |
| volume, c-trap | 139,425 | 47.8 | 132.9 | 2.78 | 50.9 | 8.0 |
| volume, c-trap | 1,081,665 | 47.8 | 215.0 | 4.50 | 51.3 | 8.0 |
| volume, linear ion trap, two channels | 140,481 | 158.6 | 528.9 | 3.33 | 159.4 | 16.1 |
| volume, linear ion trap, two channels | 1,085,825 | 156.1 | 492.5 | 3.15 | 158.4 | 16.0 |
| diffusive step, drift tube | 263,169 | 248.1 | 278.9 | 1.12 | 263.0 | 8.1 |
| diffusive step, drift tube | 1,050,625 | 248.0 | 255.8 | 1.03 | 263.0 | 8.0 |

Mean-field space charge, live, over a 5 us flight so the self-field refreshes inside the stepping
loop (`sweep.py meanfield --live`), same machine and commit:

| case | 66,049 | 263,169 | 1,050,625 |
| --- | ---: | ---: | ---: |
| drift tube | 248.4 | 248.1 | 248.0 |
| drift tube, mean field | 309.8 | 309.4 | 309.3 |
| two species | 496.5 | 496.1 | - |
| two species, mean field | 565.8 | 565.5 | - |

## What the tables say

- **The estimate states the live set, and agrees with an independent measurement of it** to within
  six percent on every case here: over it on the volumes (the cut-arm allowance) and the
  diffusive step, and a tenth of a byte under it on the plane.
- **An unconstrained solve's working set is two to four and a half times its live set**, and on
  the rediscretized volumes the ratio grows with size (2.3 to 4.2, 2.8 to 4.5 across eightfold).
  The multigrid cycle allocates fresh buffers every cycle and the collector frees them when it
  chooses. A diffusive step is nearly all live data, 1.03 to 1.12.
- **What a finished field keeps is 8 bytes a node a channel**: the potential.
- **The self-field is 61.3 to 61.4 on top of the step, once whatever the species count** (69.3 to
  69.4 with two, the extra 8 being the summed charge). Measured over one step it reads 25, because
  the first Poisson solve precedes the loop; see `docs/lessons.md`.

**Re-run it when an allocation pattern changes** - pooling the V-cycle's buffers would close most
of the gap between the two quantities, and nothing else would notice.
