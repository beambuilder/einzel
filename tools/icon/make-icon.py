"""Draws the Einzel application icon: an einzel lens in section, focusing a beam.

Everything drawn is real. The electrodes are the tubes of the shipped `einzel-lens` template
(coaxial, outer two earthed, centre at 850 V) cut along the axis, at their true proportions in
mm, and the rays are trajectories the engine computed through the solved field. x and y share
ONE scale (pixels per mm); nothing is stretched. Nothing is clipped: the whole outer radius
fits in the frame. The rays are mirrored about the axis (the lens is axisymmetric), and the
rays stop at the model's detector plane, 5 mm before the end of the exit tube (x = 33 of 38 mm).

The computed paths and geometry live in rays.json so this script runs without the engine.
HOW rays.json WAS PRODUCED (engine 26.1.0+f5810ae4986d13a0c37521650e4d7d53f848f010):
    einzel init <dir>; cd <dir>
    einzel new models/lens.json --from-template einzel-lens
  then, per ray, a copy of lens.json with these parameters changed from the template default
  (default in brackets): entranceLength 6 [30] mm, gap 2 [6] mm, centreLength 8 [24] mm,
  driftLength 20 [100] mm, wallThickness 6 [7] mm, centrePotential 850 [500] V, and
  launchRadius = 0, 1, 2, 3 mm (one run each); boreRadius 5 mm, beamEnergy 1000 V, m/z 500
  and cellsPerRadius 24 unchanged; "transport.sampleInterval": 5 ns added so the path is
  finely sampled. Each run:
    einzel run models/<copy>.json --vtu
  and the sampled path read from the Points of .einzel/<name>.trajectory.vtu (metres, in
  the x-r half-plane; x along the axis, y the radius). Every run exited 0 and ended at the
  detector (x = 33 mm), not on an electrode. Ray crossing points of the axis come out at
  27.4 / 25.3 / 22.0 mm for r = 1 / 2 / 3 mm (outer rays focus sooner: spherical aberration).
  The shorter tubes than the template's are chosen so the focus lands inside a square icon.
  (The engine notes mesh.node-on-shared-face for this model, as it does for the template:
  the tubes lie on the grounded outer edge of the solve domain; harmless here.)

Run from this directory: python make-icon.py [--preview PATH]. Writes einzel.ico (16 to 256 px)
and einzel-256.png into src/Einzel.Shell/Assets. Needs Pillow.
"""
import json, sys
from pathlib import Path
from PIL import Image, ImageDraw

S = 1024                      # drawn large, then reduced, so edges are smooth
BACK, OUTER, CENTER, RAY = "#101c28", "#93a7ba", "#f2a23a", "#63d6ea"
DATA = json.loads(Path(__file__).with_name("rays.json").read_text())
G = DATA["geometryMm"]
X0, X1 = 0.0, G["totalLength"]                  # mm; the tubes span the frame width
MARGIN = 0.075                                  # fraction of the frame left either side
SCALE = (1 - 2 * MARGIN) * S / (X1 - X0)        # pixels per mm, the same for x and y

def px(x_mm, y_mm):
    return (MARGIN * S + (x_mm - X0) * SCALE, S / 2 - y_mm * SCALE)

def draw(size_hint):
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, S - 1, S - 1), radius=S * 0.2, fill=BACK)

    small = size_hint <= 32   # fewer, heavier rays where there are few pixels
    r_in, r_out = G["boreRadius"], G["outerRadius"]
    for tube in G["tubes"]:
        color = CENTER if tube["name"] == "centre" else OUTER
        for sign in (-1, 1):
            xa, ya = px(tube["x0"], sign * r_in)
            xb, yb = px(tube["x1"], sign * r_out)
            d.rounded_rectangle((xa, min(ya, yb), xb, max(ya, yb)), radius=S * 0.012, fill=color)

    rays = {r["launchRadiusMm"]: r["pathMm"] for r in DATA["rays"]}
    chosen = (0, 3) if small else (0, 1, 2, 3)      # a subset of the SAME computed rays
    width = int(S * (0.03 if small else 0.011))
    for radius in chosen:
        for sign in ((1,) if radius == 0 else (-1, 1)):
            pts = [px(x, sign * y) for x, y in rays[radius]]
            d.line(pts, fill=RAY, width=width, joint="curve")
    return img

out = Path(__file__).resolve().parents[2] / "src" / "Einzel.Shell" / "Assets"
out.mkdir(parents=True, exist_ok=True)
sizes = [16, 24, 32, 48, 64, 128, 256]
frames = [draw(n).resize((n, n), Image.LANCZOS) for n in sizes]
frames[-1].save(out / "einzel-256.png")
frames[-1].save(out / "einzel.ico", format="ICO", sizes=[(n, n) for n in sizes],
                append_images=frames[:-1])
print("wrote", out / "einzel.ico")
# A contact sheet for looking at every size at once; written only when asked for.
if "--preview" in sys.argv:
    i = sys.argv.index("--preview")
    target = Path(sys.argv[i + 1]) if len(sys.argv) > i + 1 else Path("preview.png")
    sheet = Image.new("RGBA", (256 + 128 + 64 + 48 + 32 + 16 + 70, 256), "#808080")
    x = 0
    for n in (256, 128, 64, 48, 32, 16):
        sheet.alpha_composite(frames[sizes.index(n)], (x, 0)); x += n + 10
    sheet.save(target)
    # Enlarged 16/32/64 px for checking legibility.
    big = Image.new("RGBA", (16 * 16 + 32 * 8 + 64 * 4 + 40, 256), "#808080")
    x = 0
    for n, k in ((16, 16), (32, 8), (64, 4)):
        big.alpha_composite(frames[sizes.index(n)].resize((n * k, n * k), Image.NEAREST), (x, 0)); x += n * k + 10
    big.save(target.with_name(target.stem + "-small.png"))
