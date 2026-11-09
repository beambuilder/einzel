#!/usr/bin/env python3
"""Measure peak bytes a node across a ladder of sizes, one fresh process per point.

    python sweep.py plane|driven|volume|channels|diffuse|species|meanfield  [--live] [--exe PATH] [--out DIR]
    python sweep.py table [FILTER]

Writes <out>/results.jsonl, one line per run, and `table` prints it as bytes a node. Every
size is a separate process because the peak working set only ever rises: a second run in the
same process would read the first one's peak. Sequential, because two at once contend for the
same memory. --live measures the live set (what `einzel estimate` reports) instead of the
working set; the two need separate runs, because sampling the first disturbs the second. See
README.md for what each column means and which runs the constants came from.
"""
import copy
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
MEBI = 1048576.0


def option(name, default):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


LIVE = "--live" in sys.argv
EXE = option("--exe", os.path.join(HERE, "bin", "Release", "net10.0", "memory-harness" + (".exe" if os.name == "nt" else "")))
OUT = option("--out", os.path.join(HERE, "out"))


def load(*parts):
    with open(os.path.join(ROOT, *parts), encoding="utf-8") as handle:
        return json.load(handle)


def template(name):
    return load("src", "Einzel.Library", "Templates", name + ".json")


def example(name):
    return load("src", "Einzel.Commands", "Examples", name + ".json")


def set_cell(model, millimeters, field=0):
    element = model["fields"][field]
    element["solve" if "solve" in element else "solve3d"]["cellSize"] = {"value": millimeters, "unit": "mm"}


def set_parameter(model, name, value):
    model["parameters"][name] = {"value": value, "unit": model["parameters"][name].get("unit", "1")}


def quadrupole_span(model):
    """The quadrupole template's solve width, in mm, from the expressions its domain is written in."""
    p = {name: entry.get("value") for name, entry in model["parameters"].items()}
    rod = p["inscribedRadius"] * p["rodRatio"]
    return 2.0 * (p["inscribedRadius"] + rod + rod * p["housingClearance"])


def mixture(model, count):
    """Replace a model's single ion by `count` species of rising mass, in place."""
    ion = model.pop("ion")
    mobility = model["transport"].pop("mobility")
    population = model["source"]["cloud"].pop("population", 100000)

    model["species"] = [
        {
            "name": f"s{i}",
            "massToCharge": {
                "value": ion["massToCharge"]["value"] * (1 + i),
                "unit": ion["massToCharge"]["unit"],
            },
            "chargeNumber": 1,
            "population": population,
            # The first declares its mobility; the rest derive theirs from the gas.
            **({"mobility": mobility} if i == 0 else {}),
        }
        for i in range(count)
    ]

    return model


def run(label, model, mode, *extra):
    os.makedirs(OUT, exist_ok=True)
    label = label + ("-live" if LIVE else "")
    path = os.path.join(OUT, label + ".json")

    with open(path, "w", encoding="utf-8") as handle:
        json.dump(model, handle, ensure_ascii=False)

    command = [EXE, mode, path] + (["live"] if LIVE else []) + list(extra)
    done = subprocess.run(command, capture_output=True, text=True, encoding="utf-8")
    lines = done.stdout.strip().splitlines()

    if done.returncode != 0 or not lines:
        print(label, "FAILED", done.stderr.strip()[-300:], flush=True)
        return

    row = json.loads(lines[-1])
    row["label"] = label

    with open(os.path.join(OUT, "results.jsonl"), "a", encoding="utf-8") as handle:
        handle.write(json.dumps(row) + "\n")

    print(label, flush=True)


def table(match=""):
    print(
        f"{'label':40} {'nodes':>11} {'sec':>6} {'peak WS':>8} {'commit':>7} "
        f"{'heap':>7} {'live':>7} {'alloc':>7} {'kept':>6} {'est':>6}   (bytes a node)"
    )

    with open(os.path.join(OUT, "results.jsonl"), encoding="utf-8") as handle:
        for row in map(json.loads, handle):
            if match not in row["label"]:
                continue

            nodes = row["gridNodes"] or sum(e["NodeCount"] for e in row["elements"])

            def per(mebibytes):
                return "-" if mebibytes is None else f"{mebibytes * MEBI / nodes:.1f}"

            # A live run's working set is held down by its own sampler, so it is not shown.
            live = row.get("quantity") == "live"

            print(
                f"{row['label']:40} {nodes:11d} {row['seconds']:6.1f} "
                f"{per(None if live else row['deltaPeakWorkingSetMiB']):>8} "
                f"{per(None if live else row['deltaPeakCommitMiB']):>7} "
                f"{per(row.get('polledHeapPeakMiB')):>7} {per(row.get('liveSetPeakMiB')):>7} "
                f"{per(row['allocatedMiB']):>7} {per(row['retainedMiB']):>6} {per(row['estimateMiB']):>6}"
            )


def sweep(which):
    if which == "plane":
        # The quadrupole at 1024 to 4096 intervals a side - 1.05 M to 16.8 M nodes, the plane rows
        # of README.md. The span is read off the template's own parameters rather than written
        # down: a guessed 39.8 mm against the real 39.8168, with the cell shaved by 0.9999, put
        # span over cell a hair past each power of two, so every case got twice the intervals its
        # label named. The cell here is a hair LARGER than span over intervals, so the ratio lands
        # a hair under the power of two and rounds up onto it.
        base = template("quadrupole")
        span = quadrupole_span(base)

        for intervals in (1024, 2048, 4096):
            model = copy.deepcopy(base)
            set_cell(model, span / intervals * 1.0001)
            run(f"plane-quadrupole-{intervals}", model, "fields")

    elif which == "driven":
        ladders = (
            ("ion-funnel", (0.2, 0.1, 0.05, 0.025)),
            ("travelling-wave-guide", (0.1, 0.05, 0.025, 0.0125)),
        )

        for name, cells in ladders:
            for cell in cells:
                model = template(name)
                set_cell(model, cell)
                run(f"driven-{name}-{cell}", model, "fields")

    elif which == "volume":
        for name in ("c-trap", "segmented-quadrupole", "linear-ion-trap-3d"):
            for cell in (2, 1, 0.5, 0.25):
                model = template(name)
                set_cell(model, cell)
                run(f"volume-{name}-{cell}", model, "fields")

    elif which == "channels":
        # The same mesh with one channel more: the guide's confinement, the segmented
        # quadrupole's analysing DC, the linear trap's excitation.
        for cell in (0.05, 0.025, 0.0125):
            model = template("travelling-wave-guide")
            set_cell(model, cell)
            set_parameter(model, "confineAmplitude", 100.0)
            run(f"channels-twg-confine-{cell}", model, "fields")

        for cell in (0.5, 0.25):
            model = template("segmented-quadrupole")
            set_cell(model, cell)
            set_parameter(model, "mainDc", 50.0)
            run(f"channels-segquad-dc-{cell}", model, "fields")

            model = template("linear-ion-trap-3d")
            set_cell(model, cell)
            set_parameter(model, "exciteAmplitude", 10.0)
            run(f"channels-lit3d-excite-{cell}", model, "fields")

    elif which == "diffuse":
        base = example("drift-tube-diffusion")

        for intervals in (256, 512, 1024, 2048):
            model = copy.deepcopy(base)
            model["transport"]["densityGrid"].update(intervalsX=intervals, intervalsY=intervals)
            run(f"diffuse-drifttube-{intervals}", model, "diffuse")

        # The funnel is driven, so each species carries a cycle-averaged well.
        for intervals in (128, 256, 512):
            for scheme in ("explicit", "implicit"):
                model = template("pnnl-ion-funnel")
                model["transport"]["densityGrid"].update(intervalsX=intervals * 2, intervalsY=intervals)

                if scheme == "explicit":
                    model["transport"].pop("densityStep", None)

                run(f"diffuse-pnnl-{scheme}-{intervals}", model, "diffuse")

    elif which == "species":
        base = example("drift-tube-diffusion")

        for count in (2, 3):
            for intervals in (512, 1024, 2048):
                model = mixture(copy.deepcopy(base), count)
                model["transport"]["densityGrid"].update(intervalsX=intervals, intervalsY=intervals)
                run(f"species{count}-{intervals}", model, "diffuse")

    elif which == "meanfield":
        # The same drift tube with and without its own space charge, so the difference is the
        # self-field and nothing else. Over a flight long enough for the self-field to refresh
        # inside the stepping loop, where its Poisson solve stacks on the step's own arrays:
        # the first solve, before the loop, does not, and one step sees only that one.
        base = example("drift-tube-diffusion")
        base["transport"]["maximumFlightTime"] = {"value": 5, "unit": "us"}

        for intervals in (256, 512, 1024):
            for method in ("none", "meanField"):
                model = copy.deepcopy(base)
                model["transport"]["densityGrid"].update(intervalsX=intervals, intervalsY=intervals)

                if method != "none":
                    model["transport"]["spaceCharge"] = method

                run(f"meanfield-{method}-{intervals}", model, "diffuse", "declared")

        # A mixture solves ONE self-field, for its summed charge, so the term should appear once
        # however many species there are - plus the summed charge's own array.
        for intervals in (256, 512):
            for method in ("none", "meanField"):
                model = mixture(copy.deepcopy(base), 2)
                model["transport"]["densityGrid"].update(intervalsX=intervals, intervalsY=intervals)

                if method != "none":
                    model["transport"]["spaceCharge"] = method

                run(f"meanfield-{method}-species2-{intervals}", model, "diffuse", "declared")

    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    flags_with_values = {a for flag in ("--exe", "--out") if flag in sys.argv for a in [sys.argv[sys.argv.index(flag) + 1]]}
    positional = [a for a in sys.argv[1:] if not a.startswith("--") and a not in flags_with_values]

    if not positional:
        sys.exit(__doc__)

    if positional[0] == "table":
        table(positional[1] if len(positional) > 1 else "")
    else:
        sweep(positional[0])
