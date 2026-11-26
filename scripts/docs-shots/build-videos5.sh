#!/usr/bin/env bash
# Turns the recordings from videos5.mjs into docs/media/ files for the README and social posts:
#   <cloud>-full.mp4    every screen, about 5 minutes, 1280x720 H.264, no audio, streams from the first byte
#   <cloud>-short.mp4   the best part of that cloud, about a minute
#   <cloud>-short.png   a poster frame from the short clip
#   <cloud>-short.gif   the short clip as a silent loop that plays inline in the README
# Usage: scripts/docs-shots/build-videos5.sh <folder holding <cloud>-full.webm and <cloud>-short.webm>
set -euo pipefail
RAW="${1:?folder with the recorded .webm files}"
OUT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/docs/media"
mkdir -p "$OUT"
for cloud in azure aws gcp; do
  for mode in full short; do
    in="$RAW/$cloud-$mode.webm"
    [ -f "$in" ] || { echo "missing $in" >&2; exit 1; }
    # A slow cloud read is recorded and played faster (the caption says "sped up"): <cloud>-<mode>.speed.json holds [start,end] seconds.
    filter=$(python3 - "$RAW/$cloud-$mode.speed.json" "$in" <<'PY'
import json, subprocess, sys
try: segs = sorted(json.load(open(sys.argv[1])))
except FileNotFoundError: segs = []
dur = float(subprocess.check_output(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", sys.argv[2]]))
parts, t, n = [], 0.0, 0
def add(a, b, speed):
    global n
    if b - a < 0.05: return
    parts.append(f"[0:v]trim={a:.2f}:{b:.2f},setpts=(PTS-STARTPTS)/{speed},scale=1280:720:flags=lanczos,format=yuv420p[v{n}]"); n += 1
for a, b in segs:
    add(t, a, 1); add(a, b, 8); t = b
add(t, dur, 1)
print(";".join(parts) + ";" + "".join(f"[v{i}]" for i in range(n)) + f"concat=n={n}:v=1:a=0[out]")
PY
)
    ffmpeg -v error -y -i "$in" -filter_complex "$filter" -map "[out]" -c:v libx264 -preset slow -crf 28 -movflags +faststart -an "$OUT/$cloud-$mode.mp4"
    printf '%-6s %-5s %6.1fs  %s\n' "$cloud" "$mode" "$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$OUT/$cloud-$mode.mp4")" "$(du -h "$OUT/$cloud-$mode.mp4" | cut -f1)"
  done
  ffmpeg -v error -y -ss 4 -i "$OUT/$cloud-short.mp4" -frames:v 1 "$OUT/$cloud-short.png"
  ffmpeg -v error -y -i "$OUT/$cloud-short.mp4" -vf "setpts=PTS/4,fps=6,scale=560:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=64[p];[b][p]paletteuse=dither=bayer:bayer_scale=4" -loop 0 "$OUT/$cloud-short.gif"
  echo "   gif $(du -h "$OUT/$cloud-short.gif" | cut -f1)"
  python3 - "$RAW/$cloud-full.chapters.json" <<'PY'
import json, sys
try:
    for c in json.load(open(sys.argv[1])):
        print(f"   {c['at']//60}:{c['at']%60:02d}  {c['scene']}")
except FileNotFoundError:
    print("   (no chapters file)")
PY
done
