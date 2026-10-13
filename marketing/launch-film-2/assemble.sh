#!/usr/bin/env bash
# assemble.sh <silent.mp4> <mix.wav> <out.mp4> — mux the mastered mix (already -14 LUFS / <= -1.5 dBTP)
set -euo pipefail
ffmpeg -y -loglevel error -i "$1" -i "$2" -map 0:v -map 1:a -c:v copy -c:a aac -b:a 256k -ar 48000 -movflags +faststart -shortest "$3"
ffmpeg -hide_banner -i "$3" -af ebur128=peak=true -f null - 2>&1 | grep -A1 -E "^\s+I:|True peak" | grep -E "I:|Peak:" | tail -2
