#!/usr/bin/env bash
# render_par.sh <out.mp4> : renders four segments in parallel (the storm at 6-12 s is the slow part) and concatenates them
set -euo pipefail
cd "$(dirname "$0")"
OUT="$1"; mkdir -p out/seg
export NODE_PATH=${NODE_PATH:-$(npm root -g)}
node render.cjs video out/seg/1.mp4 30 0 7.5 > out/seg/1.log 2>&1 &
node render.cjs video out/seg/2.mp4 30 7.5 12 > out/seg/2.log 2>&1 &
node render.cjs video out/seg/3.mp4 30 12 28 > out/seg/3.log 2>&1 &
node render.cjs video out/seg/4.mp4 30 28 44 > out/seg/4.log 2>&1 &
wait
printf "file 'seg/1.mp4'\nfile 'seg/2.mp4'\nfile 'seg/3.mp4'\nfile 'seg/4.mp4'\n" > out/seg.txt
ffmpeg -y -loglevel error -f concat -safe 0 -i out/seg.txt -c copy "$OUT"
for i in 1 2 3 4; do tail -n 1 out/seg/$i.log; done
