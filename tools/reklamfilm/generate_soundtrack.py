"""Soundtrack generator for the Jobbliggaren LinkedIn promo film (59.5 s).

Synthesizes a fully rights-free score + sound design from scratch (numpy only):
a calm piano/pad theme in D major at 90 BPM, plus sparse UI-style effects
timed to the film's key moments (filter clicks, the green "37" confirmation,
the "Sparad som skickad" stamp, notification plings, checklist ticks).

Usage:
    python tools/reklamfilm/generate_soundtrack.py out.wav

Mux into the film:
    ffmpeg -i Reklamfilm_LinkedIn.mp4 -i out.wav \
        -c:v copy -c:a aac -b:a 192k -shortest Reklamfilm_LinkedIn_med_ljud.mp4

Event times were read off the delivered mp4 (frame inspection at 4 fps) and
hold for that exact cut only — re-time if the film is re-exported.
"""

from __future__ import annotations

import sys
import wave

import numpy as np

SR = 48_000
DUR = 59.5
N = int(SR * DUR)
BPM = 90.0
BEAT = 60.0 / BPM          # 0.6667 s
BAR = 4 * BEAT             # 2.6667 s

rng = np.random.default_rng(20260718)

# --- note helpers -----------------------------------------------------------

A4 = 440.0
NOTES = {"C": -9, "C#": -8, "D": -7, "D#": -6, "E": -5, "F": -4,
         "F#": -3, "G": -2, "G#": -1, "A": 0, "A#": 1, "B": 2}


def hz(name: str) -> float:
    """'D4' -> frequency. Octave numbering follows scientific pitch."""
    pitch, octave = name[:-1], int(name[-1])
    semis = NOTES[pitch] + (octave - 4) * 12
    return A4 * 2.0 ** (semis / 12.0)


def _place(buf: np.ndarray, t0: float, mono: np.ndarray, pan: float, gain: float) -> None:
    """Add a mono snippet into the stereo buffer at t0 with constant-power pan."""
    i0 = int(t0 * SR)
    if i0 >= N or i0 < 0:
        return
    seg = mono[: N - i0]
    theta = (pan + 1.0) * np.pi / 4.0  # pan in [-1, 1]
    buf[i0:i0 + len(seg), 0] += seg * np.cos(theta) * gain
    buf[i0:i0 + len(seg), 1] += seg * np.sin(theta) * gain


# --- instruments ------------------------------------------------------------

def piano(freq: float, dur: float, bright: float = 1.0) -> np.ndarray:
    """Soft felt-piano-ish pluck: decaying partials + gentle attack noise."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    partials = [(1.0, 1.0), (2.001, 0.42 * bright), (2.998, 0.18 * bright),
                (4.004, 0.07 * bright), (5.01, 0.03 * bright)]
    out = np.zeros(n)
    for mult, amp in partials:
        # higher partials die faster, like a real string
        tau = max(0.25, dur * 0.55) / mult ** 0.8
        out += amp * np.sin(2 * np.pi * freq * mult * t) * np.exp(-t / tau)
    # tiny hammer transient
    thump = rng.normal(0, 1, n) * np.exp(-t / 0.008)
    out += 0.02 * thump
    att = np.minimum(t / 0.004, 1.0)
    rel = np.minimum((dur - t) / 0.05, 1.0).clip(0, 1)
    return out * att * rel


def pad_note(freq: float, dur: float, attack: float = 1.6, release: float = 2.0) -> np.ndarray:
    """Warm slow pad voice: three detuned dark oscillators, slow swell."""
    n = int(dur * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for det in (-0.15, 0.0, 0.15):  # percent detune -> slow chorus
        f = freq * (1 + det / 100.0)
        ph = rng.uniform(0, 2 * np.pi)
        out += (np.sin(2 * np.pi * f * t + ph)
                + 0.25 * np.sin(2 * np.pi * 2 * f * t + ph)
                + 0.08 * np.sin(2 * np.pi * 3 * f * t + ph))
    env = np.minimum(t / attack, 1.0) * np.minimum((dur - t) / release, 1.0).clip(0, 1)
    # gentle 0.15 Hz shimmer so held chords breathe
    env = env * (0.92 + 0.08 * np.sin(2 * np.pi * 0.15 * t + rng.uniform(0, 6)))
    return out / 3.0 * env


def bass_note(freq: float, dur: float) -> np.ndarray:
    n = int(dur * SR)
    t = np.arange(n) / SR
    out = np.sin(2 * np.pi * freq * t) + 0.18 * np.sin(2 * np.pi * 2 * freq * t)
    env = np.minimum(t / 0.03, 1.0) * np.exp(-t / (dur * 0.9)) \
        * np.minimum((dur - t) / 0.08, 1.0).clip(0, 1)
    return out * env


# --- sound effects ----------------------------------------------------------

def sfx_click() -> np.ndarray:
    """Soft UI 'tock' — a filter locking into place."""
    n = int(0.09 * SR)
    t = np.arange(n) / SR
    body = np.sin(2 * np.pi * 740 * t) * np.exp(-t / 0.018)
    snap = rng.normal(0, 1, n) * np.exp(-t / 0.004)
    return body + 0.25 * snap


def sfx_tick() -> np.ndarray:
    """Even smaller tick for checklist checkmarks."""
    n = int(0.12 * SR)
    t = np.arange(n) / SR
    a = np.sin(2 * np.pi * 1180 * t) * np.exp(-t / 0.02)
    b = np.sin(2 * np.pi * 1770 * t) * np.exp(-t / 0.012)
    return a + 0.5 * b


def sfx_chime(f1: float, f2: float, spread: float = 0.10) -> np.ndarray:
    """Two-note warm confirmation, second note slightly later."""
    n = int(1.1 * SR)
    t = np.arange(n) / SR
    out = np.zeros(n)
    for i, f in enumerate((f1, f2)):
        tt = t - i * spread
        m = (tt > 0)
        out[m] += (np.sin(2 * np.pi * f * tt[m])
                   + 0.3 * np.sin(2 * np.pi * 2 * f * tt[m])) * np.exp(-tt[m] / 0.35)
    return out


def sfx_stamp() -> np.ndarray:
    """Dull rubber-stamp thud with a fast pitch drop."""
    n = int(0.22 * SR)
    t = np.arange(n) / SR
    f = 160 * np.exp(-t / 0.05) + 55
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.06)
    slap = rng.normal(0, 1, n) * np.exp(-t / 0.006)
    return body + 0.15 * slap


def sfx_whoosh() -> np.ndarray:
    """Airy paper swish for the CV upload (band-limited noise sweep)."""
    n = int(0.5 * SR)
    t = np.arange(n) / SR
    noise = rng.normal(0, 1, n)
    spec = np.fft.rfft(noise)
    freqs = np.fft.rfftfreq(n, 1 / SR)
    spec *= np.exp(-((freqs - 1100) / 700) ** 2)  # band-pass around 1.1 kHz
    noise = np.fft.irfft(spec, n)
    env = np.sin(np.pi * t / t[-1]) ** 2
    return noise / (np.abs(noise).max() + 1e-9) * env


def sfx_patter(dur: float, density_end: float = 26.0) -> np.ndarray:
    """Distant soft patter — ad cards piling up. Density ramps up."""
    n = int(dur * SR)
    out = np.zeros(n)
    t = 0.0
    while t < dur:
        prog = t / dur
        rate = 4.0 + prog * density_end
        t += rng.exponential(1.0 / rate)
        i0 = int(t * SR)
        if i0 >= n:
            break
        ln = int(0.05 * SR)
        tt = np.arange(min(ln, n - i0)) / SR
        f = rng.uniform(180, 420)
        hit = np.sin(2 * np.pi * f * tt) * np.exp(-tt / 0.012) * rng.uniform(0.3, 1.0)
        out[i0:i0 + len(tt)] += hit
    return out


# --- score ------------------------------------------------------------------

music = np.zeros((N, 2))
sfx = np.zeros((N, 2))

def bar_t(bar: int) -> float:
    """1-indexed bar start time."""
    return (bar - 1) * BAR

# chord chart: (bar, [pad notes], bass root or None)
CHORDS = [
    (1,  ["D3", "A3", "E4", "F#4"], None),        # D(add9) — intro
    (3,  ["B2", "F#3", "D4", "A4"], None),        # Bm7 — 40 000 counter
    (4,  ["G2", "D3", "B3", "A4"],  None),        # G(add9) — "någonstans finns ditt jobb"
    (5,  ["A2", "E3", "D4", "A4"],  None),        # Asus4 — "men var börjar man?"
    (6,  ["B2", "F#3", "D4", "A4"], None),        # Bm7 — matchning, pulse starts
    (7,  ["G2", "D3", "B3", "E4"],  None),        # G — narrowing
    (8,  ["D3", "A3", "F#4", "E5"], "D2"),        # D(add9) — "37 kvar" arrival
    (9,  ["G2", "D3", "B3", "A4"],  "G1"),        # G — ladda upp CV
    (10, ["B2", "F#3", "D4", "A4"], "B1"),        # Bm7 — läser/matchar
    (11, ["G2", "D3", "B3", "E4"],  "G1"),        # G — se var du matchar
    (12, ["A2", "E3", "C#4", "A4"], "A1"),        # A — ansök
    (13, ["D3", "A3", "F#4", "E5"], "D2"),        # D — "Liggaren minns resten"
    (14, ["B2", "F#3", "D4", "A4"], "B1"),        # Bm7 — statuslista
    (15, ["G2", "D3", "B3", "A4"],  "G1"),        # G — bevaka
    (16, ["A2", "E3", "C#4", "A4"], "A1"),        # A — notis
    (17, ["B2", "F#3", "D4", "A4"], "B1"),        # Bm7 — CV-granskning
    (18, ["G2", "D3", "B3", "E4"],  "G1"),        # G — konkreta tips
    (19, ["G2", "D3", "B3", "A4"],  None),        # G(add9) — checklistan, pull back
    (20, ["A2", "E3", "D4", "A4"],  None),        # Asus4→A — bockarna
    (21, ["D3", "A3", "F#4", "E5"], "D2"),        # D(add9) — upplösning
    (22, ["D3", "A3", "F#4", "A4"], None),        # D — outro, ring out
]

PAD_GAIN = 0.055
for idx, (bar, notes, bass) in enumerate(CHORDS):
    t0 = bar_t(bar)
    nxt = bar_t(CHORDS[idx + 1][0]) if idx + 1 < len(CHORDS) else DUR
    dur = (nxt - t0) + 1.2  # overlap into the next chord for a legato seam
    for j, name in enumerate(notes):
        pan = (-0.5 + j / (len(notes) - 1)) * 0.7
        _place(music, t0, pad_note(hz(name), dur), pan, PAD_GAIN)
    if bass:
        for half in (0.0, BAR / 2):  # root on beats 1 and 3
            _place(music, t0 + half, bass_note(hz(bass), BAR / 2 + 0.2), 0.0, 0.16)
        if nxt - t0 > BAR:  # chords held 2+ bars keep the bass walking
            for half in (BAR, BAR * 1.5):
                _place(music, t0 + half, bass_note(hz(bass), BAR / 2 + 0.2), 0.0, 0.16)

# arpeggio pulse: eighth notes over chord tones, bars 6-18 (matchning -> granskning)
ARP = {
    6:  ["D5", "F#5", "A5", "F#5"], 7: ["D5", "E5", "G5", "B5"],
    8:  ["D5", "F#5", "A5", "E5"],  9: ["D5", "G5", "B5", "A5"],
    10: ["D5", "F#5", "A5", "F#5"], 11: ["D5", "E5", "G5", "B5"],
    12: ["C#5", "E5", "A5", "E5"],  13: ["D5", "F#5", "A5", "E5"],
    14: ["D5", "F#5", "A5", "F#5"], 15: ["D5", "G5", "B5", "A5"],
    16: ["C#5", "E5", "A5", "E5"],  17: ["D5", "F#5", "A5", "F#5"],
    18: ["D5", "E5", "G5", "B5"],
}
for bar, tones in ARP.items():
    t0 = bar_t(bar)
    ramp = min(1.0, 0.55 + 0.15 * (bar - 6))          # sneak the pulse in
    for k in range(8):                                  # 8 eighths per bar
        if bar == 6 and k % 2 == 1:
            continue                                    # first bar: quarters only
        name = tones[k % len(tones)]
        vel = ramp * (0.85 if k % 4 == 0 else rng.uniform(0.5, 0.7))
        pan = 0.35 if k % 2 else -0.2
        _place(music, t0 + k * BEAT / 2, piano(hz(name), 0.9, bright=0.6), pan, 0.045 * vel)

# lead phrases: sparse felt-piano melody carrying the story
MELODY = [
    # (time, note, dur, gain) — intro motif as the logo completes
    (2.0, "A4", 1.8, 0.11), (2.67, "F#4", 1.8, 0.10), (3.33, "D5", 2.6, 0.12),
    # "någonstans där finns ditt jobb" — hopeful answer
    (8.3, "B4", 1.2, 0.10), (9.0, "D5", 1.2, 0.10), (9.67, "E5", 2.2, 0.11),
    # "men var börjar man?" — unresolved hang
    (11.2, "E5", 1.0, 0.09), (11.9, "F#5", 2.4, 0.10),
    # "37 kvar" arrival phrase
    (18.8, "F#5", 0.9, 0.11), (19.4, "A5", 0.9, 0.11),
    (20.0, "B5", 1.4, 0.12), (20.9, "A5", 2.2, 0.11),
    # small answers along the feature tour
    (24.4, "F#5", 1.0, 0.09), (25.1, "E5", 1.8, 0.09),
    (30.0, "E5", 1.0, 0.09), (30.7, "C#5", 1.8, 0.09),
    (36.2, "F#5", 1.0, 0.09), (36.9, "D5", 1.8, 0.09),
    (43.3, "A5", 1.0, 0.09), (44.0, "F#5", 1.8, 0.09),
    (46.0, "G5", 0.9, 0.09), (46.7, "B5", 2.0, 0.10),
    # closing tag over the outro logo
    (53.5, "A5", 1.2, 0.10), (54.3, "F#5", 1.2, 0.10),
    (55.1, "E5", 1.5, 0.09), (56.0, "D5", 3.2, 0.11),
]
for t0, name, dur, gain in MELODY:
    _place(music, t0, piano(hz(name), dur, bright=0.8), 0.05, gain)

# --- sound design, timed to the picture ------------------------------------

# ad cards piling up under "40 000 annonser" (5.5-8.5 s), very distant
_place(sfx, 5.5, sfx_patter(3.0), 0.1, 0.030)
# filters locking in during "Din matchning"
for t0 in (13.0, 15.0, 16.6):
    _place(sfx, t0, sfx_click(), -0.1, 0.16)
# counter lands on the green 37 -> warm confirmation (A5 -> D6)
_place(sfx, 17.85, sfx_chime(hz("A5"), hz("D6")), 0.1, 0.12)
# CV upload swish + "CV inläst" tick
_place(sfx, 20.6, sfx_whoosh(), -0.2, 0.10)
_place(sfx, 23.3, sfx_tick(), 0.0, 0.10)
# "SPARAD SOM SKICKAD" stamp
_place(sfx, 31.40, sfx_stamp(), 0.0, 0.55)
# "Liggaren säger till i tid" + "3 nya annonser" -> gentle notification plings
_place(sfx, 35.8, sfx_chime(hz("F#5"), hz("B5"), spread=0.08), 0.2, 0.080)
_place(sfx, 41.3, sfx_chime(hz("E5"), hz("A5"), spread=0.08), 0.2, 0.070)
# closing checklist: three soft ticks as the checkmarks fill
for t0 in (50.70, 51.70, 52.55):
    _place(sfx, t0, sfx_tick(), 0.0, 0.12)

# --- mix, master, write -----------------------------------------------------

mix = music + sfx

t_all = np.arange(N) / SR
fade_in = np.minimum(t_all / 1.2, 1.0)
fade_out = np.clip((59.3 - t_all) / 1.8, 0.0, 1.0)
mix *= (fade_in * fade_out)[:, None]

peak = np.abs(mix).max()
mix *= 10 ** (-1.5 / 20) / peak          # peak-normalize to -1.5 dBFS
mix = np.tanh(mix * 1.1) / np.tanh(1.1)  # gentle glue, no hard edges

out_path = sys.argv[1] if len(sys.argv) > 1 else "soundtrack.wav"
pcm = (np.clip(mix, -1, 1) * 32767).astype("<i2")
with wave.open(out_path, "wb") as w:
    w.setnchannels(2)
    w.setsampwidth(2)
    w.setframerate(SR)
    w.writeframes(pcm.tobytes())

rms = 20 * np.log10(np.sqrt((mix ** 2).mean()) + 1e-12)
print(f"wrote {out_path}: {DUR}s @ {SR} Hz, peak -1.5 dBFS, RMS {rms:.1f} dBFS")
