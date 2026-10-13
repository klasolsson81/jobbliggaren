"""Score for "Väntläge", played on recorded instruments (VSCO-2 CE, CC0) plus a synthesized drum kit.

0-2.8   lonely upright piano and a string pad in A minor under the wall of VÄNTAR
2.8-6.2 hope: harp glissando at lift-off, strings rise, a glockenspiel sparkle on "Full av hopp"
6.2-8.3 ironic melodrama Am-F-Dm-E (brass, low strings, timpani, cymbal) cut dead on the thunder
8.3-11.55 silence for the airport chime and the crickets
11.55-14 a G pedal builds as the tower rises; 14.0 C major tutti on the logo
16-32.72 a light 120 BPM groove in C (pizzicato bass, marimba hook, piano, kit) that grows per chapter
32.72-35.5 dead stop for the overloaded take-off; 35.5 chorus on the take-off; 39.5 final hit; outro

Usage: python3 compose3.py <outdir>
"""
import os, sys, random, subprocess
import numpy as np
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from vsco import Instrument, Track, load, SR, ROOT

OUT = sys.argv[1]
os.makedirs(OUT, exist_ok=True)
DUR = 44.0
rng = random.Random(7)
NAMES = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}


def n(s):
    name, acc, octv = s[0], s[1:-1], int(s[-1])
    return NAMES[name] + 12 * (octv + 1) + acc.count('#') - acc.count('b')


def ch(*xs): return [n(x) for x in xs]


# ---------------------------------------------------------------- instruments (VSCO names sit an octave low)
I = {}
_pmap = dict(l.strip().split('=') for l in open(os.path.join(ROOT, 'Keys/Upright Piano/MappingChart.txt')) if '=' in l and l[0].isdigit())


def _piano_map(f):
    if not f.startswith('Player_dyn'): return None, None
    return int(_pmap[f[-7:-4]]), int(f[10])


I['piano'] = Instrument('Keys/Upright Piano', release=0.4, gain=0.9, mapping=_piano_map)
I['vln'] = Instrument('Strings/Violin Section/susVib', release=0.5, transpose=12)
I['vla'] = Instrument('Strings/Viola Section/susvib', release=0.5, transpose=12)
I['vc'] = Instrument('Strings/Cello Section/susvib', release=0.5, transpose=12)
I['vcspic'] = Instrument('Strings/Cello Section/spic', release=0.12, transpose=12)
I['cb'] = Instrument('Strings/Solo Contrabass/SusVib', release=0.5, transpose=12)
I['pizz'] = Instrument('Strings/Solo Contrabass/Pizz', release=0.2, transpose=12)
I['hn'] = Instrument('Brass/F Horn/sus', release=0.4, transpose=12)
I['tbn'] = Instrument('Brass/Tenor Trombone/sus', release=0.35, transpose=12)
I['tpt'] = Instrument('Brass/Trumpet/sus', release=0.35, transpose=12)
I['tptst'] = Instrument('Brass/Trumpet/stac', release=0.1, transpose=12)
I['tuba'] = Instrument('Brass/Tuba/sus', release=0.4, transpose=12)
I['mar'] = Instrument('Percussion/Marimba', rx=r'_([A-G])(#|b)?(-?\d)_', release=0.3, transpose=12)
I['glock'] = Instrument('Percussion/Glock', rx=r'_([A-G])(#|b)?(-?\d)\.', release=0.8, transpose=12)
I['harp'] = Instrument('Strings/Harp', rx=r'_([A-G])(#|b)?(-?\d)_', release=0.8)

TIMP_HIT = os.path.join(ROOT, 'Percussion/Timpani/Timpani1_Hit_v3_rr1_Sum.wav')
TIMP_HIT_F = os.path.join(ROOT, 'Percussion/Timpani/Timpani1_Hit_v1_rr1_Sum.wav')
TIMP_ROLL = os.path.join(ROOT, 'Percussion/Timpani/Rolls/Timpani1_Roll_v5_rr1_Sum.wav')
TIMP_ROOT = 41.5  # measured
CRASH = os.path.join(ROOT, 'VSCO 1 Percussion/varMetal/Cymbals/clash/crash_hit_ff_loose.wav')
SUSP_ROLL = os.path.join(ROOT, 'VSCO 1 Percussion/varMetal/Cymbals/susp/susp_hit_woodmall_roll_cresc.wav')
BDRUM = os.path.join(ROOT, 'VSCO 1 Percussion/drums/bass/bdrum_fff_1.wav')

S = {k: Track(DUR) for k in ['piano', 'strings', 'bass', 'brass', 'timp', 'perc', 'mar', 'glock', 'harp', 'spic', 'stab']}


def play(stem, inst, t, notes, vel, dur, jitter=0.006):
    if isinstance(notes, int): notes = [notes]
    for m in notes:
        S[stem].note(I[inst], t, m, max(0.02, min(1.0, vel + rng.uniform(-0.04, 0.04))), dur, jitter, rng)


def pitched(path, root, midi, dur, gain, fade=0.3):
    x = load(path); ratio = 2 ** ((midi - root) / 12)
    n_out = int(min(len(x) / ratio, (dur + fade) * SR))
    src = np.arange(n_out) * ratio
    y = np.stack([np.interp(src, np.arange(len(x)), x[:, c]) for c in (0, 1)], 1)
    k = int(dur * SR)
    if k < n_out: y[k:] *= np.exp(-np.linspace(0, 6, n_out - k))[:, None]
    return (y * gain).astype(np.float32)


def swell(stem, t0, t1, g0, g1):
    """gain ramp on a stem region (crescendo on held notes)"""
    b = S[stem].buf; i0, i1 = int(t0 * SR), int(t1 * SR)
    b[i0:i1] *= np.linspace(g0, g1, i1 - i0)[:, None]


# ---------------------------------------------------------------- drum kit: synthesized voices (GM note numbers) + the VSCO crash
from scipy.signal import butter, sosfilt
_nr = np.random.default_rng(23)
def _T(n): return np.arange(n) / SR
def _noise(n): return _nr.standard_normal(n)
def _bp(x, lo, hi, o=2): return sosfilt(butter(o, [lo, hi], 'bandpass', fs=SR, output='sos'), x)
def _hp(x, f, o=2): return sosfilt(butter(o, f, 'highpass', fs=SR, output='sos'), x)
def _lp(x, f, o=2): return sosfilt(butter(o, f, 'lowpass', fs=SR, output='sos'), x)
def d_kick():
    n = int(0.45 * SR); t = _T(n); f = 46 + 95 * np.exp(-t / 0.032)
    return _lp(np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.26) + _bp(_noise(n), 1200, 6000) * np.exp(-t / 0.0025) * 0.22, 6000)
def d_stick():
    n = int(0.12 * SR); t = _T(n)
    return (0.6 * np.sin(2 * np.pi * 1750 * t) + 0.3 * np.sin(2 * np.pi * 2420 * t)) * np.exp(-t / 0.012) + _bp(_noise(n), 1500, 6000) * np.exp(-t / 0.008)
def d_snare():
    n = int(0.3 * SR); t = _T(n)
    return 0.5 * np.sin(2 * np.pi * 190 * t) * np.exp(-t / 0.05) + _bp(_noise(n), 1500, 9000) * np.exp(-t / 0.10)
def d_clap():
    n = int(0.3 * SR); t = _T(n); x = np.zeros(n)
    for off in (0, 0.009, 0.019):
        i = int(off * SR); m = n - i; x[i:] += _bp(_noise(m), 900, 4800) * np.exp(-_T(m) / 0.006)
    return x + 0.5 * _bp(_noise(n), 900, 4800) * np.exp(-t / 0.09) * (t > 0.02)
def d_hat(open_=False):
    n = int((0.5 if open_ else 0.08) * SR); t = _T(n)
    x = _hp(_noise(n), 7000, 4) + 0.3 * _hp(np.sign(np.sin(2 * np.pi * 5300 * t)) * np.sign(np.sin(2 * np.pi * 7900 * t)), 6000, 2)
    return x * np.exp(-t / (0.16 if open_ else 0.02))
def d_tom(f0):
    n = int(0.35 * SR); t = _T(n); f = f0 * (1 + 0.6 * np.exp(-t / 0.02))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.18) + 0.2 * _lp(_noise(n), 900) * np.exp(-t / 0.02)
def d_crash():
    x = load(CRASH).mean(axis=1); return x[:int(2.5 * SR)] / (np.abs(x).max() + 1e-9)
KITV = {36: (d_kick, 1.0, 0.0), 37: (d_stick, 0.38, -0.1), 38: (d_snare, 0.6, 0.0), 39: (d_clap, 0.45, 0.05),
        42: (lambda: d_hat(False), 0.22, 0.3), 46: (lambda: d_hat(True), 0.2, 0.3), 49: (d_crash, 0.42, -0.2),
        43: (lambda: d_tom(98), 0.55, -0.25), 45: (lambda: d_tom(118), 0.55, -0.1), 47: (lambda: d_tom(142), 0.5, 0.05),
        48: (lambda: d_tom(160), 0.5, 0.15), 50: (lambda: d_tom(186), 0.5, 0.25)}
kit_ev = []


def kit(t, note, vel, dur=0.2):
    t += rng.uniform(-0.003, 0.003)
    kit_ev.append((t, note, int(max(1, min(127, vel + rng.uniform(-5, 5))))))


def render_kit(path):
    out = np.zeros((int((DUR + 1) * SR), 2), np.float32); cache = {}
    for t, note, v in kit_ev:
        fn, g, pan = KITV[note]
        key = (note, rng.randrange(4))           # four round-robin variants per voice
        if key not in cache: cache[key] = fn()
        x = cache[key] * g * (v / 127) ** 1.6
        i = int(round(t * SR)); j = min(len(out), i + len(x))
        if i < 0 or i >= len(out): continue
        out[i:j, 0] += x[:j - i] * (1 - max(pan, 0)); out[i:j, 1] += x[:j - i] * (1 + min(pan, 0))
    write(path, out)


# ================================================================ 0-2.8 lonely
play('piano', 'piano', 0.0, ch('A2', 'E3'), 0.32, 2.7)
for t, m, d in [(0.05, 'E5', 0.45), (0.55, 'D5', 0.45), (1.05, 'C5', 0.45), (1.55, 'B4', 0.45), (2.05, 'A4', 0.9)]:
    play('piano', 'piano', t, n(m), 0.42, d)
play('strings', 'vc', 0.2, n('A2'), 0.25, 2.7); play('strings', 'vla', 0.5, n('E3'), 0.22, 2.4); play('strings', 'vln', 0.8, ch('C4', 'E4'), 0.2, 2.1)
swell('strings', 0.0, 2.8, 0.5, 0.85)

# ================================================================ 2.8-6.2 hope
for k, m in enumerate(ch('A3', 'C4', 'E4', 'G4', 'A4', 'C5', 'E5', 'G5', 'A5', 'C6')):
    play('harp', 'harp', 2.72 + k * 0.028, m, 0.55 + k * 0.02, 1.4, jitter=0.002)
HOPE = [(2.8, ch('F2'), ch('A3', 'C4', 'F4'), 'C5', 0.75), (3.6, ch('G2'), ch('B3', 'D4', 'G4'), 'D5', 0.8),
        (4.4, ch('A2'), ch('C4', 'E4', 'A4'), 'E5', 0.8), (5.2, ch('F2'), ch('A3', 'C4', 'F4'), 'F5', 0.6), (5.8, ch('E2'), ch('G#3', 'B3', 'E4'), 'E5', 0.4)]
for t, lo, mid_, top, d in HOPE:
    play('piano', 'piano', t, lo + mid_, 0.45, d)
    play('strings', 'vln', t + 0.02, n(top), 0.5, d + 0.05)
    play('strings', 'vla', t, mid_[1:], 0.36, d + 0.05)
    play('strings', 'vc', t, [lo[0] + 12], 0.4, d + 0.05)
    play('bass', 'pizz', t, lo[0], 0.6, 0.4)
for t, m in [(3.75, 'E6'), (3.87, 'G6'), (4.0, 'C7')]:
    play('glock', 'glock', t, n(m), 0.55, 0.9, jitter=0.001)
S['timp'].add(5.55, pitched(TIMP_ROLL, TIMP_ROOT, n('A1'), 0.65, 0.35))

# ================================================================ 6.2-8.3 melodrama, cut dead at 8.3
DRAMA = [(6.2, 'A1', ch('A2', 'E3', 'A3'), ch('E4', 'C4', 'A3'), ch('A4', 'C5', 'E5'), 'E5', 0.5),
         (6.7, 'F1', ch('F2', 'C3', 'F3'), ch('F4', 'C4', 'A3'), ch('A4', 'C5', 'F5'), 'F5', 0.5),
         (7.2, 'D2', ch('D3', 'A3', 'D4'), ch('F4', 'D4', 'A3'), ch('A4', 'D5', 'F5'), 'F5', 0.5),
         (7.7, 'E1', ch('E2', 'B2', 'E3'), ch('E4', 'B3', 'G#3'), ch('G#4', 'B4', 'E5'), 'G#5', 0.62)]
for t, root, low, horn, vln, tpt, d in DRAMA:
    play('strings', 'cb', t, n(root) + 12, 0.85, d); play('strings', 'vc', t, low, 0.85, d)
    play('strings', 'vln', t, vln, 0.85, d); play('strings', 'vla', t, horn, 0.75, d)
    play('brass', 'hn', t, horn, 0.85, d); play('brass', 'tbn', t, low[:2], 0.8, d)
    play('brass', 'tuba', t, n(root) + 12, 0.85, d); play('brass', 'tpt', t, n(tpt), 0.9, d)
    S['timp'].add(t, pitched(TIMP_HIT_F, TIMP_ROOT, n(root) + 12, 0.45, 0.9))
S['timp'].add(6.2, pitched(TIMP_ROLL, TIMP_ROOT, n('E2'), 2.1, 0.5))
S['perc'].add(6.2, pitched(BDRUM, 0, 0, 1.2, 0.9)); S['perc'].add(7.7, pitched(BDRUM, 0, 0, 0.6, 0.8))
S['perc'].add(7.1, pitched(SUSP_ROLL, 0, 0, 1.2, 0.55))
for k in S: S[k].buf[int(8.3 * SR):int(11.5 * SR)] = 0  # the cut

# ================================================================ 11.55-14 the turn: G pedal
play('strings', 'cb', 11.55, n('G2'), 0.7, 2.45); play('strings', 'vc', 11.6, ch('G2', 'D3'), 0.7, 2.4)
play('strings', 'vla', 12.0, ch('B3', 'D4'), 0.6, 2.0); play('strings', 'vln', 12.4, ch('G4', 'B4', 'D5'), 0.6, 1.6)
play('brass', 'hn', 12.6, ch('G3', 'B3', 'D4'), 0.6, 1.4); play('brass', 'tbn', 13.1, ch('G2', 'D3'), 0.6, 0.9)
swell('strings', 11.55, 14.0, 0.25, 1.0); swell('brass', 12.6, 14.0, 0.35, 1.0)
S['timp'].add(12.2, pitched(TIMP_ROLL, TIMP_ROOT, n('G1') + 12, 1.8, 0.55)); swell('timp', 12.2, 14.0, 0.3, 1.0)
for k, m in enumerate(ch('G3', 'B3', 'D4', 'G4', 'B4', 'D5', 'G5', 'B5', 'D6', 'G6')):
    play('harp', 'harp', 12.1 + k * 0.17, m, 0.5, 0.8, jitter=0.002)
for k in range(16): kit(13.0 + k * 0.0625, 38, 26 + k * 5, 0.05)
for k, m in enumerate([50, 48, 47, 45]): kit(13.75 + k * 0.0625, m, 90 + k * 6, 0.1)

# ================================================================ 14.0 C major tutti
T = 14.0
play('brass', 'tpt', T, ch('C5', 'E5', 'G5'), 0.95, 1.1); play('brass', 'hn', T, ch('E4', 'G4', 'C5'), 0.9, 1.6)
play('brass', 'tbn', T, ch('C3', 'G3', 'E4'), 0.9, 1.3); play('brass', 'tuba', T, n('C2'), 0.9, 1.2)
play('strings', 'cb', T, n('C2'), 0.9, 1.9); play('strings', 'vc', T, ch('C3', 'G3'), 0.9, 1.9)
play('strings', 'vla', T, ch('E4', 'G4'), 0.85, 1.9); play('strings', 'vln', T, ch('C5', 'E5', 'G5', 'C6'), 0.9, 1.9)
play('piano', 'piano', T, ch('C2', 'C3', 'G3', 'C4', 'E4', 'G4', 'C5'), 0.8, 1.6)
S['timp'].add(T, pitched(TIMP_HIT_F, TIMP_ROOT, n('C2') + 12, 0.8, 1.0))
S['perc'].add(T, pitched(CRASH, 0, 0, 2.2, 0.75)); S['perc'].add(T, pitched(BDRUM, 0, 0, 1.0, 1.0))
swell('strings', 14.4, 16.0, 1.0, 0.35); swell('brass', 14.3, 15.6, 1.0, 0.2)
for t, m in [(15.0, 'G4'), (15.25, 'A4'), (15.5, 'B4'), (15.75, 'D5')]:
    play('piano', 'piano', t, n(m), 0.5, 0.22)
play('mar', 'mar', 15.5, n('B4'), 0.55, 0.2); play('mar', 'mar', 15.75, n('D5'), 0.6, 0.2)

# ================================================================ 16-32.72 groove
PROG = [(ch('E3', 'G3', 'C4', 'E4'), n('C2')), (ch('D3', 'G3', 'B3', 'D4'), n('B1')),
        (ch('E3', 'G3', 'C4', 'E4'), n('A1')), (ch('E3', 'A3', 'C4', 'F4'), n('F1'))]
HOOK = [(0.0, 'E5', 0.5), (0.5, 'G5', 0.5), (1.0, 'C6', 0.75), (1.75, 'B5', 0.25), (2.0, 'A5', 0.5), (2.5, 'G5', 0.5), (3.0, 'E5', 0.5), (3.5, 'D5', 0.5)]
HOOK2 = [(0.0, 'C5', 0.5), (0.5, 'E5', 0.5), (1.0, 'G5', 0.75), (1.75, 'A5', 0.25), (2.0, 'G5', 0.5), (2.5, 'E5', 0.5), (3.0, 'D5', 1.0)]


def level(t):
    return 1 if t < 21.0 else 2 if t < 26.0 else 3


def groove(t0, t1, stop=None, drum_rest=()):
    t = t0
    while t < t1 - 1e-6:
        bar = int(round((t - 16.0) / 2.0))
        voic, root = PROG[bar % 4]
        ok = lambda x: x < t1 and (stop is None or not (stop[0] <= x < stop[1]))
        rest = lambda x: any(a <= x < b for a, b in drum_rest)
        for k in range(8):
            tt = t + k * 0.25; L = level(tt)
            if not ok(tt) or rest(tt): continue
            if k in (0, 4) or (L >= 2 and k == 5): kit(tt, 36, 100 if k == 0 else 88)
            if k in (2, 6): kit(tt, 38 if L >= 2 else 37, 92 if L >= 2 else 70)
            if L >= 3 and k in (2, 6): kit(tt, 39, 64)
            kit(tt, 42, (58 if k % 2 == 0 else 40) + 4 * L, 0.1)
            if L >= 3 and k == 7: kit(tt, 46, 60, 0.25)
        for off, m, d in [(0, root, 0.45), (0.75, root, 0.2), (1.0, root + 12, 0.25), (1.5, root + 7, 0.4)]:
            if ok(t + off): play('bass', 'pizz', t + off, m, 0.8, d, jitter=0.003)
        for off in (0.5, 1.5):
            if ok(t + off): play('piano', 'piano', t + off, voic, 0.36 + 0.05 * level(t + off), 0.18, jitter=0.004)
        motif = HOOK if bar % 2 == 0 else HOOK2
        for off, m, d in motif:
            tt = t + off * 0.5
            if ok(tt):
                play('mar', 'mar', tt, n(m), 0.62, d * 0.5, jitter=0.003)
                if level(tt) >= 2: play('glock', 'glock', tt, n(m) + 12, 0.3, d * 0.5, jitter=0.002)
        if level(t) >= 2:
            for k in range(8):
                tt = t + k * 0.25
                if ok(tt): play('spic', 'vcspic', tt, root + (12 if k % 2 == 0 else 19), 0.5 + 0.1 * (k % 2 == 0), 0.2, jitter=0.003)
        if level(t) >= 3:
            for off in (0.75, 1.25):
                if ok(t + off): play('stab', 'tptst', t + off, [voic[1] + 12, voic[2] + 12], 0.55, 0.15, jitter=0.003)
            if ok(t): play('strings', 'vla', t, voic[1:3], 0.32, 1.9); play('strings', 'vc', t, [root + 12], 0.34, 1.9) if ok(t) else None
        t += 2.0


groove(16.0, 32.72, drum_rest=[(20.5, 21.0), (25.5, 26.0)])
for tc in (16.0, 21.0, 26.0, 31.0):
    kit(tc, 49, 92 if tc > 16 else 84, 1.0)
for k, m in enumerate([48, 47, 45, 43]):  # tom fills into chapters
    for base in (20.5, 25.5): kit(base + k * 0.125, m, 80 + k * 8, 0.1)
play('harp', 'harp', 28.6, ch('C5', 'E5', 'G5', 'C6', 'E6', 'G6'), 0.5, 0.8)
for k, m in enumerate(ch('C6', 'E6', 'G6', 'C7')):
    play('glock', 'glock', 28.6 + k * 0.07, m, 0.5, 0.7, jitter=0.001)

# ================================================================ 30.72-33.5 the gag: silence; 33.5 chorus
for k in range(12): kit(35.0 + k * 0.0417, 38, 50 + k * 6, 0.05)
CHORUS = [(35.5, ch('F2'), ch('A3', 'C4', 'F4'), 'C5', 1.0), (36.5, ch('G2'), ch('B3', 'D4', 'G4'), 'D5', 1.0),
          (37.5, ch('A2'), ch('C4', 'E4', 'A4'), 'E5', 1.0), (38.5, ch('F2'), ch('A3', 'C4', 'F4'), 'A5', 0.5), (39.0, ch('G2'), ch('B3', 'D4', 'G4'), 'G5', 0.5)]
for t, lo, mid_, top, d in CHORUS:
    play('strings', 'vln', t, [n(top), n(top) - 12], 0.85, d + 0.04); play('strings', 'vla', t, mid_, 0.6, d + 0.04)
    play('strings', 'vc', t, [lo[0] + 12], 0.7, d + 0.04); play('strings', 'cb', t, lo[0] + 12, 0.6, d)
    play('brass', 'hn', t, mid_, 0.65, d); play('brass', 'tpt', t, n(top), 0.7, d)
    play('piano', 'piano', t, lo + mid_, 0.6, d * 0.9)
    for k in range(int(d / 0.25)):
        tt = t + k * 0.25
        kit(tt, 42, 66 if k % 2 == 0 else 48, 0.1)
        if k % 2 == 0: kit(tt, 36 if (k // 2) % 2 == 0 else 38, 104 if (k // 2) % 2 == 0 else 96)
        play('bass', 'pizz', tt, lo[0] + (0 if k % 2 == 0 else 12), 0.78, 0.2, jitter=0.003)
        play('spic', 'vcspic', tt, lo[0] + 12 + (0 if k % 2 == 0 else 7), 0.6, 0.18, jitter=0.003)
    S['timp'].add(t, pitched(TIMP_HIT, TIMP_ROOT, lo[0] + 12, 0.4, 0.7))
for tc in (35.5, 37.0, 37.9):
    kit(tc, 49, 104, 1.2)
S['perc'].add(35.5, pitched(CRASH, 0, 0, 1.6, 0.6))
for t, m in [(35.5, 'C6'), (36.5, 'D6'), (37.5, 'E6'), (38.5, 'A6'), (39.0, 'G6')]:
    play('glock', 'glock', t, n(m), 0.45, 0.6, jitter=0.001)
for k in range(6): kit(39.0 + k * 0.0833, 38 if k < 4 else 47, 70 + k * 8, 0.06)

# ================================================================ 39.5 final hit and outro
T = 39.5
play('brass', 'tpt', T, ch('C5', 'E5', 'G5'), 0.9, 1.0); play('brass', 'hn', T, ch('E4', 'G4', 'C5'), 0.85, 2.2)
play('brass', 'tbn', T, ch('C3', 'G3', 'E4'), 0.85, 1.4); play('brass', 'tuba', T, n('C2'), 0.85, 1.4)
play('strings', 'cb', T, n('C2'), 0.85, 3.6); play('strings', 'vc', T, ch('C3', 'G3'), 0.85, 3.8)
play('strings', 'vla', T, ch('E4', 'G4'), 0.8, 3.8); play('strings', 'vln', T, ch('C5', 'E5', 'G5', 'C6'), 0.85, 3.8)
play('piano', 'piano', T, ch('C2', 'C3', 'G3', 'C4', 'E4', 'G4', 'C5'), 0.75, 2.4)
S['timp'].add(T, pitched(TIMP_HIT_F, TIMP_ROOT, n('C2') + 12, 0.9, 1.0))
S['perc'].add(T, pitched(CRASH, 0, 0, 3.0, 0.7)); S['perc'].add(T, pitched(BDRUM, 0, 0, 1.2, 0.95))
kit(T, 36, 116, 0.3)
swell('strings', 40.0, 43.6, 1.0, 0.25); swell('brass', 39.9, 41.6, 1.0, 0.1)
for t, m in [(40.5, 'C4'), (40.75, 'G4'), (41.0, 'C5'), (41.25, 'E5'), (41.6, 'F4'), (41.85, 'A4'), (42.1, 'C5'), (42.35, 'G5')]:
    play('piano', 'piano', t, n(m), 0.38, 0.6)
play('piano', 'piano', 42.6, ch('C3', 'G3', 'E4', 'C5'), 0.34, 1.2)
for t, m in [(42.6, 'E6'), (42.72, 'G6'), (42.84, 'C7')]:
    play('glock', 'glock', t, n(m), 0.5, 1.2, jitter=0.001)

# ---------------------------------------------------------------- write stems
def write(path, x):
    x = x[:int((DUR + 0.5) * SR)]
    pk = float(np.max(np.abs(x))) or 1.0
    y = (x / max(pk, 1.0)).astype(np.float32)
    subprocess.run(['ffmpeg', '-y', '-v', 'error', '-f', 'f32le', '-ar', str(SR), '-ac', '2', '-i', '-', '-c:a', 'pcm_f32le', path],
                   input=y.tobytes(), check=True)
    return pk


for k, tr in S.items():
    pk = write(f'{OUT}/{k}.wav', tr.buf)
    print(f'{k:8s} peak {pk:.2f}')
render_kit(f'{OUT}/kit.wav')
print('kit rendered; stems in', OUT)
