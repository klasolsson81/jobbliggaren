"""Synthesize SFX, mix them with the rendered score, and write a 48 kHz stereo master.

Usage: python3 mix.py <events.json> <stemdir> <out.wav>
Levels are set by measurement (pyloudnorm LUFS for beds, peak dBFS for transients),
because every cue is placed from the same event list the picture uses.
"""
import json, sys
import numpy as np
import pyloudnorm as pyln
from scipy.io import wavfile
from scipy.signal import butter, sosfilt, fftconvolve, resample_poly

SR = 48000
rng = np.random.default_rng(11)
ev = json.load(open(sys.argv[1]))
STEMS = sys.argv[2]
OUT = sys.argv[3]
C = ev['clock']
BPM, BEAT, BAR = C['BPM'], C['BEAT'], C['BAR']
M0 = 0.0
DUR = ev['duration']
N = int(DUR * SR) + SR // 2
meter = pyln.Meter(SR)

# ---------------------------------------------------------------- utilities
def db(x): return 10 ** (x / 20)
def T(n): return np.arange(n) / SR
def noise(n): return rng.standard_normal(n)
def sos(kind, f, order=2): return butter(order, f, kind, fs=SR, output='sos')
def bp(x, lo, hi, order=2): return sosfilt(sos('bandpass', [lo, hi], order), x)
def hp(x, f, order=2): return sosfilt(sos('highpass', f, order), x)
def lp(x, f, order=2): return sosfilt(sos('lowpass', f, order), x)
def st(mono, pan=0.0):
    a = (pan + 1) * np.pi / 4
    return np.stack([mono * np.cos(a), mono * np.sin(a)], axis=1)
def peak_norm(x, dbfs):
    p = np.max(np.abs(x)) or 1.0
    return x * db(dbfs) / p
def lufs_norm(x, target):
    l = meter.integrated_loudness(x if x.ndim == 2 else st(x))
    if not np.isfinite(l): return x
    return x * db(target - l)
def load(path):
    sr, d = wavfile.read(path)
    d = d.astype(np.float32) / 32768.0
    if d.ndim == 1: d = np.stack([d, d], 1)
    assert sr == SR, sr
    return d
def svf_bp(x, fc, q=0.7):
    """time-varying state-variable band-pass; fc is an array (Hz)"""
    y = np.zeros_like(x); low = band = 0.0
    f = 2 * np.sin(np.pi * np.clip(fc, 20, SR / 6) / SR)
    for i in range(len(x)):
        high = x[i] - low - q * band
        band += f[i] * high
        low += f[i] * band
        y[i] = band
    return y
def ir(seconds=1.8, predelay=0.012, damp=6000):
    n = int(seconds * SR)
    t = T(n)
    tail = noise(n * 2).reshape(2, n).T * np.exp(-t / (seconds / 6.9))[:, None]
    tail[:, 0] = lp(tail[:, 0], damp); tail[:, 1] = lp(tail[:, 1], damp)
    out = np.zeros((n + int(predelay * SR), 2))
    out[int(predelay * SR):] = tail
    for d, g in [(0.007, 0.5), (0.013, 0.35), (0.021, 0.28), (0.029, 0.2)]:   # early reflections
        out[int(d * SR), 0] += g; out[int((d + 0.003) * SR), 1] += g
    return out / np.sqrt(np.sum(out ** 2))
ROOM = ir(1.6, 0.010, 7000)
def reverb(x, wet=0.2, room=ROOM):
    y = np.stack([fftconvolve(x[:, c], room[:, c])[:len(x)] for c in range(2)], 1)
    return x + wet * y
def comp(x, thresh_db=-18, ratio=3.0, att=0.005, rel=0.12, makeup=0):
    lvl = np.max(np.abs(x), axis=1) + 1e-9
    env = np.zeros_like(lvl)
    a_a, a_r = np.exp(-1 / (att * SR)), np.exp(-1 / (rel * SR))
    e = 0.0
    for i in range(len(lvl)):
        c = a_a if lvl[i] > e else a_r
        e = c * e + (1 - c) * lvl[i]
        env[i] = e
    edb = 20 * np.log10(env)
    over = np.maximum(edb - thresh_db, 0)
    g = db(-over * (1 - 1 / ratio) + makeup)
    return x * g[:, None]

master = np.zeros((N, 2))
def place(sig, t, gain=1.0):
    i = int(round(t * SR))
    if i < 0: sig = sig[-i:]; i = 0
    j = min(N, i + len(sig))
    master[i:j] += sig[:j - i] * gain

# ---------------------------------------------------------------- SFX library
def key(soft=False, heavy=False):
    n = int(0.05 * SR); t = T(n)
    click = bp(noise(n), 1800, 7500) * np.exp(-t / 0.0035)
    f = rng.uniform(150, 230) * (0.8 if heavy else 1)
    thock = np.sin(2 * np.pi * f * t) * np.exp(-t / (0.016 if heavy else 0.010))
    x = 0.55 * click + (0.45 if heavy else 0.3) * thock
    x *= rng.uniform(0.7, 1.0) * (0.55 if soft else 1.0) * (1.25 if heavy else 1)
    return st(x, rng.uniform(-0.15, 0.15))
def mouse(press=True, release=True, level=1.0):
    n = int(0.11 * SR); t = T(n); x = np.zeros(n)
    def tr(at, g):
        m = n - int(at * SR); tt = T(m)
        x[int(at * SR):] += g * (bp(noise(m), 2200, 9000) * np.exp(-tt / 0.0012) + 0.35 * np.sin(2 * np.pi * 2900 * tt) * np.exp(-tt / 0.005))
    if press: tr(0.0, 1.0)
    if release: tr(0.06, 0.55)
    return st(x * level, 0.1)
def tick_row():
    n = int(0.02 * SR); t = T(n)
    x = bp(noise(n), 3000, 9000) * np.exp(-t / 0.0009) + 0.25 * np.sin(2 * np.pi * 3400 * t) * np.exp(-t / 0.004)
    return st(x * 0.5, 0.2)
def whoosh(dur=0.55, f0=300, f1=2600, pan0=-0.4, pan1=0.4):
    n = int(dur * SR); t = T(n); u = t / dur
    fc = f0 * (f1 / f0) ** u
    x = lp(svf_bp(noise(n), fc, 0.9), 9000)
    envl = np.sin(np.pi * np.clip(u / 0.75, 0, 1) * 0.5) ** 2 * np.clip((1 - u) / 0.25, 0, 1)
    x *= envl
    p = pan0 + (pan1 - pan0) * u
    a = (p + 1) * np.pi / 4
    return np.stack([x * np.cos(a), x * np.sin(a)], 1)
def riser(dur):
    n = int(dur * SR); t = T(n); u = t / dur
    a = svf_bp(noise(n), 400 * (7000 / 400) ** u, 0.8) * u ** 2.6
    cym = lp(hp(noise(n), 4500, 4), 11000) * u ** 4.0
    sweep = np.sin(2 * np.pi * np.cumsum(180 * (5 ** u)) / SR) * u ** 3 * 0.25
    x = lp(a, 10000) + 0.35 * cym + sweep
    x[-int(0.004 * SR):] *= np.linspace(1, 0, int(0.004 * SR))
    return np.stack([x, np.roll(x, 37)], 1)
def logo_tick():
    n = int(0.6 * SR); t = T(n)
    tk = bp(noise(n), 2000, 9000) * np.exp(-t / 0.0018) + 0.5 * np.sin(2 * np.pi * 1900 * t) * np.exp(-t / 0.02)
    sub = np.sin(2 * np.pi * (58 + 30 * np.exp(-t / 0.03)) * t) * np.exp(-t / 0.22)
    return st(0.7 * tk + 0.9 * sub)
def ui_tick(f=1400):
    n = int(0.08 * SR); t = T(n)
    fr = f - (f * 0.3) * np.clip(t / 0.02, 0, 1)
    x = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / 0.02)
    return st(x * 0.6, rng.uniform(-0.3, 0.3))
def strike():
    dur = 0.28; n = int(dur * SR); t = T(n)
    jitter = lp(noise(n), 40) ; jitter = 0.6 + 0.4 * jitter / (np.max(np.abs(jitter)) + 1e-9)
    x = bp(noise(n), 2200, 6500) * jitter * np.clip(t / 0.01, 0, 1) * np.clip((dur - t) / 0.04, 0, 1)
    return st(x * 0.7, 0.15)
def popfx():
    n = int(0.15 * SR); t = T(n)
    fr = 420 + 700 * np.exp(-t / 0.018)
    x = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / 0.05)
    x += 0.4 * bp(noise(n), 2000, 8000) * np.exp(-t / 0.002)
    return st(x, 0.1)
def lift():
    dur = 0.2; n = int(dur * SR); t = T(n); u = t / dur
    x = lp(svf_bp(noise(n), 700 * (2600 / 700) ** u, 1.0), 7000) * np.sin(np.pi * u) ** 2
    return st(x, rng.uniform(-0.3, 0.3))
def land():
    n = int(0.12 * SR); t = T(n)
    x = np.sin(2 * np.pi * (110 + 60 * np.exp(-t / 0.01)) * t) * np.exp(-t / 0.03)
    x += 0.3 * bp(noise(n), 1500, 6000) * np.exp(-t / 0.0015)
    return st(x, rng.uniform(-0.3, 0.3))
def crickets(dur):
    n = int(dur * SR); x = np.zeros(n)
    for f, period, off, g, pan in [(4400, 0.36, 0.02, 1.0, -0.4), (4750, 0.47, 0.17, 0.7, 0.45)]:
        tt = off
        while tt < dur:
            for k in range(3):
                s = int((tt + k * 0.024) * SR); m = int(0.013 * SR)
                if s + m >= n: break
                pt = T(m)
                x[s:s + m] += g * np.sin(2 * np.pi * f * pt) * np.sin(np.pi * pt / 0.013) ** 2
            tt += period * rng.uniform(0.94, 1.06)
    amb = lp(noise(n), 700) * 0.04
    x = x + amb
    x *= np.clip(T(n) / 0.12, 0, 1)
    x[-int(0.012 * SR):] *= np.linspace(1, 0, int(0.012 * SR))
    return np.stack([x * 0.9, np.roll(x, 211) * 0.9], 1)
def bell(f, dur=1.4, partials=((1, 1.0, 1.0), (2.76, 0.35, 0.45), (5.4, 0.18, 0.25), (8.9, 0.08, 0.15))):
    n = int(dur * SR); t = T(n); x = np.zeros(n)
    for r, a, d in partials:
        x += a * np.sin(2 * np.pi * f * r * t) * np.exp(-t / (dur * d / 2.5))
    x *= np.clip(t / 0.002, 0, 1)
    return x
def chime():
    a = bell(1318.5, 1.0); b = bell(1975.5, 1.2)
    n = len(b) + int(0.09 * SR); x = np.zeros(n)
    x[:len(a)] += a * 0.7; x[int(0.09 * SR):] += b * 0.8
    return st(x * 0.6, 0.15)
def ding():
    n = int(1.6 * SR); t = T(n)
    mod = np.sin(2 * np.pi * 1568 * 1.4 * t) * (2.2 * np.exp(-t / 0.25))
    x = np.sin(2 * np.pi * 1568 * t + mod) * np.exp(-t / 0.55)
    x += 0.35 * np.sin(2 * np.pi * 3136 * t) * np.exp(-t / 0.25)
    x *= np.clip(t / 0.0015, 0, 1)
    return st(x * 0.7, 0.25)
def refresh():
    dur = 0.3; n = int(dur * SR); t = T(n); u = t / dur
    x = lp(svf_bp(noise(n), 900 * (3200 / 900) ** u, 1.1), 8000) * np.sin(np.pi * u) ** 2 * 0.6
    return st(x, 0.2)
def toggle():
    x = mouse(True, False, 0.7)
    y = np.zeros((len(x) + int(0.025 * SR), 2)); y[:len(x)] += x; y[int(0.025 * SR):] += 0.6 * x
    return y
def scan(dur):
    n = int(dur * SR); t = T(n); u = t / dur
    whir = (np.sin(2 * np.pi * 240 * t) + 0.4 * np.sin(2 * np.pi * 480 * t)) * 0.12
    air = lp(svf_bp(noise(n), 800 * (3200 / 800) ** u, 1.4), 6000) * 0.35
    x = (whir + air) * np.sin(np.pi * u) ** 0.6
    return st(x, -0.1)
def fail():
    n = int(0.3 * SR); t = T(n)
    x = np.sin(2 * np.pi * (300 - 60 * np.clip(t / 0.08, 0, 1)) * t) * np.exp(-t / 0.09)
    x += 0.3 * np.sin(2 * np.pi * 810 * t) * np.exp(-t / 0.03)
    return st(x * 0.8, -0.1)
def passfx():
    n = int(0.4 * SR); t = T(n)
    x = np.sin(2 * np.pi * 1174.7 * t) * np.exp(-t / 0.10) + 0.25 * np.sin(2 * np.pi * 4698 * t) * np.exp(-t / 0.02)
    return st(x * 0.55, 0.1)
def dialog_fx():
    n = int(0.12 * SR); t = T(n)
    fr = 480 + 260 * np.clip(t / 0.05, 0, 1)
    x = np.sin(2 * np.pi * np.cumsum(fr) / SR) * np.exp(-t / 0.04) * np.clip(t / 0.004, 0, 1)
    return st(x * 0.5)
def crumple(dur=0.55):
    n = int(dur * SR); x = np.zeros(n)
    for _ in range(220):
        at = dur * rng.beta(1.3, 2.4)
        m = int(rng.uniform(0.0006, 0.005) * SR); s = int(at * SR)
        if s + m >= n: continue
        fc = rng.uniform(1500, 7500)
        burst = bp(noise(m + 64), fc * 0.7, min(fc * 1.4, 20000))[64:] * np.hanning(m)
        x[s:s + m] += burst * rng.uniform(0.2, 1.0)
    rustle = bp(noise(n), 1200, 6000) * 0.12 * np.sin(np.pi * T(n) / dur)
    x += rustle
    return np.stack([x, np.roll(x, 97)], 1)
def bin_thump():
    n = int(0.5 * SR); t = T(n)
    th = np.sin(2 * np.pi * (85 + 40 * np.exp(-t / 0.01)) * t) * np.exp(-t / 0.09) + 0.4 * lp(noise(n), 400) * np.exp(-t / 0.03)
    s = int(0.02 * SR)
    lid = np.zeros(n)
    tt = T(n - s)
    lid[s:] = sum(a * np.sin(2 * np.pi * f * tt) * np.exp(-tt / d) for f, a, d in [(1830, 0.5, 0.09), (2470, 0.35, 0.06), (3900, 0.2, 0.04)])
    return st(0.9 * th + 0.35 * lid, 0.0)
def room_tone(dur):
    n = int(dur * SR)
    x = lp(noise(n), 1800) + 0.5 * lp(noise(n), 200)
    return np.stack([x, np.roll(x, 1013)], 1)

# ---------------------------------------------------------------- drum kit (synthesized, replaces the GM kit)
def d_kick():
    n = int(0.45 * SR); t = T(n)
    f = 46 + 95 * np.exp(-t / 0.032)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.26)
    click = bp(noise(n), 1200, 6000) * np.exp(-t / 0.0025) * 0.22
    return lp(body + click, 6000)
def d_stick():
    n = int(0.12 * SR); t = T(n)
    return (0.6 * np.sin(2 * np.pi * 1750 * t) + 0.3 * np.sin(2 * np.pi * 2420 * t)) * np.exp(-t / 0.012) + bp(noise(n), 1500, 6000) * np.exp(-t / 0.008)
def d_snare():
    n = int(0.3 * SR); t = T(n)
    return 0.5 * np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.05) + bp(noise(n), 1500, 9000) * np.exp(-t / 0.10)
def d_clap():
    n = int(0.3 * SR); t = T(n); x = np.zeros(n)
    for off in (0, 0.009, 0.019):
        i = int(off * SR); m = n - i
        x[i:] += bp(noise(m), 900, 4800) * np.exp(-T(m) / 0.006)
    return x + 0.5 * bp(noise(n), 900, 4800) * np.exp(-t / 0.09) * (t > 0.02)
def d_hat(open_=False):
    n = int((0.5 if open_ else 0.08) * SR); t = T(n)
    x = hp(noise(n), 7000, 4) + 0.3 * hp(np.sign(np.sin(2 * np.pi * 5300 * t)) * np.sign(np.sin(2 * np.pi * 7900 * t)), 6000, 2)
    return x * np.exp(-t / (0.16 if open_ else 0.02))
def d_tamb():
    n = int(0.15 * SR); t = T(n)
    return bp(noise(n), 6000, 13000) * (np.exp(-t / 0.045) + 0.6 * np.exp(-np.abs(t - 0.02) / 0.01))
def d_shaker():
    n = int(0.08 * SR); t = T(n)
    return bp(noise(n), 4000, 10000) * np.clip(t / 0.008, 0, 1) * np.exp(-t / 0.03)
def d_cym(dur):
    n = int(dur * SR); t = T(n)
    return (hp(noise(n), 4000, 2) * 0.6 + bp(noise(n), 6000, 14000)) * np.exp(-t / (dur / 4.5)) * (1 + 2 * np.exp(-t / 0.01))
def d_tom(f0):
    n = int(0.35 * SR); t = T(n)
    f = f0 * (1 + 0.6 * np.exp(-t / 0.02))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.18) + 0.2 * lp(noise(n), 900) * np.exp(-t / 0.02)
KIT = {36: (d_kick, 1.0, 0.0), 37: (d_stick, 0.38, -0.1), 38: (d_snare, 0.6, 0.0), 39: (d_clap, 0.45, 0.05),
       42: (lambda: d_hat(False), 0.22, 0.3), 46: (lambda: d_hat(True), 0.2, 0.3), 54: (d_tamb, 0.16, -0.35),
       82: (d_shaker, 0.14, -0.3), 55: (lambda: d_cym(1.4), 0.22, 0.25), 49: (lambda: d_cym(3.0), 0.3, -0.2),
       43: (lambda: d_tom(98), 0.55, -0.25), 45: (lambda: d_tom(118), 0.55, -0.1), 47: (lambda: d_tom(142), 0.5, 0.05),
       48: (lambda: d_tom(160), 0.5, 0.15), 50: (lambda: d_tom(186), 0.5, 0.25)}
def drum_bus():
    dh = json.load(open(f'{STEMS}/drum_hits.json'))
    out = np.zeros((N, 2))
    cache = {}
    for beat, note, vel in dh['hits']:
        if note not in KIT: continue
        fn, g, pan = KIT[note]
        key = (note, int(rng.integers(0, 4)))          # 4 round-robin variants per voice
        if key not in cache: cache[key] = fn()
        x = cache[key] * g * (vel / 127) ** 1.6
        i = int(round((M0 + beat * BEAT) * SR))
        if i < 0 or i >= N: continue
        j = min(N, i + len(x))
        out[i:j] += st(x[:j - i], pan)
    out = reverb(out, 0.10)
    return comp(out, -18, 2.5, 0.003, 0.08)

