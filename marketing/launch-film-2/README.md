# Launch film 2 "Väntläge" (LinkedIn, 4:5)

A 44-second film for jobbliggaren.se, built as code so every frame and every sound is reproducible.
It is a marketing asset, not product code: nothing here is built, tested or deployed with the app.

The story: an application is a paper plane. Without control it circles in a storm called "VI ÅTERKOMMER";
Jobbliggaren is the air-traffic control for your job search (matching radar, follow-up board, company watch,
CV review). `STORYBOARD.md` has the shot list, copy and sound.

- `index.html` + `main.js` — the picture: Three.js scenes (city, storm, tower, radar, split-flap boards,
  runway, clouds) plus HTML typography. `window.seek(t)` renders the frame at `t` seconds deterministically;
  all timings live in `TL` in `main.js`, which also exports the sound-effect cue list.
- `render.cjs` — serves the folder locally and drives Chromium (SwiftShader WebGL) frame by frame into
  ffmpeg (1080×1350, 30 fps). `render_par.sh` renders three segments in parallel.
- `audio/vsco.py` — a small multi-sample player over the VSCO-2 Community Edition recordings (CC0).
- `audio/compose3.py` — the score on recorded instruments (strings, brass, timpani, piano, marimba, glock,
  harp) plus a synthesized drum kit; every sound source is CC0 or generated here.
- `audio/mix3.py` (+ `mixlib.py`) — synthesizes the sound effects, mixes against the cue list and masters
  to −14 LUFS / ≤ −2.6 dBTP.
- `assemble.sh` muxes picture and mix; `review_pack.sh` makes contact sheets for review.

All companies, names and numbers on screen are fictional demo data. Product facts follow the app: four match
grades, a follow-up queue, watch notifications tagged "Bevakar", and a rule-based CV review (beta) with
43 criteria whose verdicts quote the CV.

## Rebuild

Requires Node 22 with `playwright` and Chromium, ffmpeg, and Python 3 with numpy, scipy and pyloudnorm.

```bash
./fetch-deps.sh                                    # three.js, fonts, VSCO-2 CE samples
mkdir -p out
node render.cjs events out/events.json             # cue list from the timeline
python3 audio/compose3.py audio/stems                # reads ./vsco-2-ce, or $VSCO
python3 audio/mix3.py out/events.json audio/stems out/mix.wav
./render_par.sh out/silent.mp4                     # roughly an hour on four CPU cores (SwiftShader)
./assemble.sh out/silent.mp4 out/mix.wav out/vantlage.mp4
```

(`render.cjs` loads `playwright` via `require`; with a global install, prefix the node commands with
`NODE_PATH=$(npm root -g)`.) A single frame: `node render.cjs stills out/stills 2.4`.
