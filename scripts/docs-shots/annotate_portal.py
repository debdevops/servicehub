"""Annotate cloud-console screenshots (captured from a real, logged-in browser): redact account identifiers, draw numbered callouts,
and record the key text in keys.json — same look and registry as the in-app shots.

    python3 annotate_portal.py <cloud> <output dir>

The callouts and redactions for each cloud live in portal_specs/<cloud>.py (coordinates in the 1568x681 capture frame).
The raw captures hold account identifiers, so they are never committed: only the redacted PNGs are."""
import importlib, json, sys
from PIL import Image, ImageDraw, ImageFont
spec = importlib.import_module(f"portal_specs.{sys.argv[1]}")
SRC, SPECS = spec.SRC, spec.SPECS
OUT = sys.argv[2]
K = 2  # upscale for crisp lines
def font(sz):
    for p in ("/System/Library/Fonts/Helvetica.ttc", "/System/Library/Fonts/SFNS.ttf"):
        try: return ImageFont.truetype(p, sz)
        except Exception: pass
    return ImageFont.load_default()
keys = {}
for name, (src, redact, calls) in SPECS.items():
    im = Image.open(SRC + src + ".jpg").convert("RGB")
    im = im.resize((im.width * K, im.height * K), Image.LANCZOS)
    d = ImageDraw.Draw(im)
    for x0, y0, x1, y1 in redact:
        d.rectangle((x0*K, y0*K, x1*K, y1*K), fill=(40, 40, 40))
    f = font(15 * K)
    keys[name + ".png"] = []
    for i, (box, text) in enumerate(calls, 1):
        x0, y0, x1, y1 = [v * K for v in box]
        d.rounded_rectangle((x0-3*K, y0-3*K, x1+3*K, y1+3*K), radius=6*K, outline=(225, 29, 72), width=3*K)
        cx, cy, r = x0 - 3*K, y0 - 3*K, 13*K
        d.ellipse((cx-r, cy-r, cx+r, cy+r), fill=(225, 29, 72), outline=(255, 255, 255), width=2*K)
        w = d.textlength(str(i), font=f)
        d.text((cx - w/2, cy - 9*K), str(i), fill=(255, 255, 255), font=f)
        keys[name + ".png"].append({"n": i, "t": text})
    im.save(f"{OUT}/{name}.png")
json.dump(keys, open(f"{OUT}/keys.json", "w"), indent=1)
print("wrote", len(SPECS))
