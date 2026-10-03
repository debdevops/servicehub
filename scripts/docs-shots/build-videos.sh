#!/usr/bin/env bash
# Turns the raw recordings from videos.mjs into what the README and a social post need, in docs/media/:
#   <cloud>.mp4          1280x720, H.264, no audio, streams from the first byte — works on GitHub and as an X upload
#   <cloud>.png          a poster frame (the Replay All preview, with its caption)
#   <cloud>-preview.gif  a short sped-up loop that plays inline in the README and links to the MP4
# It also prints each cloud's chapter list (m:ss, from the .chapters.json videos.mjs writes) for the README.
# Usage: scripts/docs-shots/build-videos.sh <folder holding azure.webm aws.webm gcp.webm and their .chapters.json>
set -euo pipefail
RAW="${1:?folder with the recorded .webm files}"
OUT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)/docs/media"
mkdir -p "$OUT"
for cloud in azure aws gcp; do
  in="$RAW/$cloud.webm"
  [ -f "$in" ] || { echo "missing $in" >&2; exit 1; }
  ffmpeg -v error -y -i "$in" -vf "scale=1280:720:flags=lanczos,format=yuv420p" -c:v libx264 -preset slow -crf 28 -movflags +faststart -an "$OUT/$cloud.mp4"
  dur=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$OUT/$cloud.mp4")
  # poster: a few seconds into the Replay All chapter (its preview), else 55% of the way through
  at=$(python3 - "$RAW/$cloud.chapters.json" "$dur" <<'PY'
import json, sys
dur = float(sys.argv[2])
try:
    ch = {c["scene"]: c["at"] for c in json.load(open(sys.argv[1]))}
    print(round(ch["replay-all"] + 9, 2))
except Exception:
    print(round(dur * 0.55, 2))
PY
)
  ffmpeg -v error -y -ss "$at" -i "$OUT/$cloud.mp4" -frames:v 1 "$OUT/$cloud.png"
  # preview: the whole story at 10x, 480 wide, 5 fps, one shared palette
  ffmpeg -v error -y -i "$OUT/$cloud.mp4" -vf "setpts=PTS/10,fps=5,scale=480:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=48[p];[b][p]paletteuse=dither=bayer:bayer_scale=4" -loop 0 "$OUT/$cloud-preview.gif"
  printf '%-6s %6.1fs  mp4 %s  gif %s\n' "$cloud" "$dur" "$(du -h "$OUT/$cloud.mp4" | cut -f1)" "$(du -h "$OUT/$cloud-preview.gif" | cut -f1)"
  python3 - "$RAW/$cloud.chapters.json" <<'PY'
import json, sys
try:
    for c in json.load(open(sys.argv[1])):
        print(f"   {c['at']//60}:{c['at']%60:02d}  {c['scene']}")
except FileNotFoundError:
    print("   (no chapters file)")
PY
done
