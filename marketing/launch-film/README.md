# Launch film "Kalkylarket" (LinkedIn, 4:5)

A 41-second launch film for jobbliggaren.se, built as code so every frame and every sound is reproducible.
It is a marketing asset, not product code: nothing here is built, tested or deployed with the app.

- `index.html` + `timeline.js` — the picture. `window.seek(t)` renders the frame at `t` seconds
  deterministically; all timings live in `timeline.js`, which also exports the sound-effect cue list.
- `render.cjs` — Playwright drives Chromium frame by frame and pipes PNGs into ffmpeg (1080×1350, 30 fps).
- `audio/compose.py` — the score as MIDI (hold music + main cue, 120 BPM, D major), rendered with
  FluidSynth and the FluidR3 GM soundfont; drum hits are exported for synthesis.
- `audio/mix.py` — synthesizes the drum kit and every sound effect, mixes against the shared cue list,
  and masters to -14 LUFS / ≤ -2 dBTP.
- `assemble.sh` — muxes picture and mix (AAC 256 kbps, faststart). `review_pack.sh` makes contact sheets.
- `STORYBOARD.md` — scene-by-scene script, copy and sound.

All company names, people and numbers shown in the mock UI are fictional demo data.

## Rebuild

Requires Node 22 with the `playwright` package, Chromium, ffmpeg, Python 3 with numpy, scipy, mido and
pyloudnorm, and `fluidsynth` with `fluid-soundfont-gm` (Debian/Ubuntu package names).

```bash
./fetch-fonts.sh                                   # Source Sans 3 + JetBrains Mono (OFL) from npm
mkdir -p out audio/stems
node render.cjs events out/events.json             # cue list from the timeline
python3 audio/compose.py out/events.json audio/stems
python3 audio/mix.py out/events.json audio/stems out/mix.wav
node render.cjs video out/silent.mp4 30
./assemble.sh out/silent.mp4 out/mix.wav out/kalkylarket.mp4
```

(`render.cjs` loads `playwright` via `require`; with a global install, prefix the node commands with
`NODE_PATH=$(npm root -g)`.) A single frame: `node render.cjs stills out/stills 2.4`.
