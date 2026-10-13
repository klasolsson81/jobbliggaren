#!/usr/bin/env bash
# review_pack.sh <film.mp4> <outdir>: contact sheets every 0.5 s + full-res key frames + audio pictures
set -euo pipefail
F="$1"; O="$2"; rm -rf "$O"; mkdir -p "$O/frames" "$O/key"
ffmpeg -loglevel error -i "$F" -vf "fps=2,scale=324:405" "$O/frames/f%03d.png"
i=0; for f in "$O"/frames/f*.png; do t=$(python3 -c "print(f'{($i)*0.5:05.1f}s')"); convert "$f" -gravity south -background '#222' -fill white -pointsize 16 -splice 0x22 -annotate +0+3 "$t" "$f"; i=$((i+1)); done
ls "$O"/frames/f*.png | split -l 25 - "$O/list_"
n=1; for l in "$O"/list_*; do montage $(cat "$l") -tile 5x -geometry +4+4 -background '#111' "$O/sheet_$n.png"; n=$((n+1)); rm "$l"; done
for t in ${KEYS}; do
  ffmpeg -loglevel error -ss $t -i "$F" -frames:v 1 "$O/key/t$(printf '%04.1f' $t).png"; done
ffmpeg -loglevel error -i "$F" -lavfi "showspectrumpic=s=1600x420:legend=1:scale=log" "$O/audio_spectrogram.png"
ffmpeg -loglevel error -i "$F" -lavfi "showwavespic=s=1600x200:colors=0x15603F" "$O/audio_waveform.png"
rm -rf "$O/frames"
