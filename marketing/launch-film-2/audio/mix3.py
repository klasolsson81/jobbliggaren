"""Mix for "Väntläge": score stems + synthesized SFX on the picture's own cue list, mastered to -14 LUFS.

Usage: python3 mix3.py <events.json> <stemdir> <out.wav>  (v2: recorded-instrument stems from compose3.py)
Shares its DSP helpers and the basic SFX library with the first film (mixlib.py).
"""
import os
exec(open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'mixlib.py')).read())

HALL = ir(2.6, 0.022, 6500)

# ------------------------------------------------------------------ new SFX for this film
def flap_row(ncell, dur):
    n_ = int((dur + 0.3) * SR); x = np.zeros((n_, 2))
    for c in range(ncell):
        pan = -0.7 + 1.4 * c / max(1, ncell - 1)
        flips = 3 + (c * 7) % 4
        t0 = c * (dur * 0.6 / max(1, ncell))
        for k in range(flips + 1):
            tt = max(0.0, t0 + k * 0.065 + rng.uniform(-0.004, 0.004))
            i = int(tt * SR); m = int(0.012 * SR)
            if i + m >= n_: continue
            tm = T(m)
            body = 0.5 * np.sin(2 * np.pi * rng.uniform(800, 1100) * tm) * np.exp(-tm / 0.004)
            clk = bp(noise(m + 32), 1500, 6500)[32:] * np.exp(-tm / 0.0018)
            g = (1.0 if k == flips else 0.55) * rng.uniform(0.6, 1.0)
            x[i:i + m] += st((clk + body) * g, pan)
    return x
def terminal_amb(dur):
    n_ = int(dur * SR); t = T(n_)
    murmur = bp(noise(n_), 250, 1400) * (0.6 + 0.4 * lp(noise(n_), 3) / 0.05)
    room = lp(noise(n_), 900) * 0.6
    x = (0.5 * murmur / (np.max(np.abs(murmur)) + 1e-9) + 0.3 * room / (np.max(np.abs(room)) + 1e-9))
    x *= np.clip(t / 0.05, 0, 1) * np.clip((dur - t) / 0.3, 0, 1)
    return reverb(np.stack([x, np.roll(x, 2111)], 1), 0.3, HALL)
def wind(dur):
    n_ = int(dur * SR); t = T(n_)
    fc = 500 + 400 * np.sin(2 * np.pi * 0.35 * t) + 250 * np.sin(2 * np.pi * 0.9 * t + 1)
    x = lp(svf_bp(noise(n_), fc, 1.4), 4000)
    x *= np.clip(t / 0.4, 0, 1) * np.clip((dur - t) / 0.5, 0, 1)
    return np.stack([x, np.roll(x, 777)], 1)
def boom():
    n_ = int(2.4 * SR); t = T(n_)
    sub = np.sin(2 * np.pi * np.cumsum(30 + 60 * np.exp(-t / 0.15)) / SR) * np.exp(-t / 0.9)
    hit = lp(noise(n_), 2500) * np.exp(-t / 0.12) * 0.5
    return reverb(st(sub + hit), 0.35, HALL)
def thunder(soft=False):
    n_ = int(3.2 * SR); t = T(n_)
    crack = hp(noise(n_), 1500) * np.exp(-t / 0.05) * (0.3 if soft else 0.8)
    rumble = lp(noise(n_), 220, 4) * np.exp(-t / 0.55) * (1 + 0.6 * np.sin(2 * np.pi * 3.1 * t) * np.exp(-t / 0.5))
    x = rumble / (np.max(np.abs(rumble)) + 1e-9) + crack
    return reverb(np.stack([x, np.roll(x, 1600)], 1), 0.3, HALL)
def pa_chime():
    notes = [(0.0, 1046.5), (0.38, 830.6), (0.76, 659.3)]           # C6 - G#5 - E5: a descending terminal chime
    n_ = int(2.4 * SR); x = np.zeros(n_)
    for t0, f in notes:
        i = int(t0 * SR); y = bell(f, 0.75, ((1, 1.0, 1.0), (2.0, 0.25, 0.5), (3.0, 0.12, 0.3), (4.2, 0.06, 0.2)))
        x[i:i + len(y)] += y[:n_ - i] * 0.8
    return reverb(st(x, 0.0), 0.35, HALL)
def pa_soft():
    n_ = int(1.2 * SR); x = np.zeros(n_)
    for t0, f in [(0.0, 659.3), (0.22, 523.3)]:
        i = int(t0 * SR); y = bell(f, 0.5, ((1, 1.0, 1.0), (2.0, 0.2, 0.4), (3.0, 0.08, 0.25)))
        x[i:i + len(y)] += y[:n_ - i] * 0.8
    return reverb(st(x, 0.0), 0.3, HALL)
def sonar(f=1500, dur=1.2):
    n_ = int(dur * SR); t = T(n_)
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / 0.18) * np.clip(t / 0.003, 0, 1)
    y = x.copy(); d = int(0.21 * SR); y[d:] += 0.35 * x[:-d]; y[2 * d:] += 0.15 * x[:-2 * d]
    return st(y, 0.1)
def radar_sweep():
    w_ = whoosh(0.8, 300, 3000, -0.8, 0.8); s_ = sonar(1250, 0.8); L_ = min(len(w_), len(s_))
    return w_[:L_] * 0.8 + s_[:L_] * 0.6
def tower_rise(dur):
    n_ = int(dur * SR); t = T(n_); u = t / dur
    rumble = lp(noise(n_), 160, 4) * 1.2
    whir = np.sin(2 * np.pi * np.cumsum(70 + 160 * u) / SR) * 0.35
    grind = bp(noise(n_), 300, 900) * (0.3 + 0.3 * np.sin(2 * np.pi * 11 * t)) * 0.4
    x = (rumble / (np.max(np.abs(rumble)) + 1e-9) + whir + grind) * np.clip(t / 0.15, 0, 1) * np.clip((dur - t) / 0.2, 0, 1)
    return reverb(st(x), 0.2, HALL)
def impact(big=True):
    n_ = int(2.2 * SR); t = T(n_)
    sub = np.sin(2 * np.pi * np.cumsum(42 + 70 * np.exp(-t / 0.06)) / SR) * np.exp(-t / (0.7 if big else 0.4))
    snap = bp(noise(n_), 800, 6000) * np.exp(-t / 0.03) * 0.6
    return reverb(st(sub + snap), 0.25, HALL)
def sweep_tone(f0, f1, dur, wobble=0.0):
    n_ = int(dur * SR); t = T(n_); u = t / dur
    f = f0 * (f1 / f0) ** u * (1 + wobble * np.sin(2 * np.pi * 9 * t))
    return st(np.sin(2 * np.pi * np.cumsum(f) / SR) * np.sin(np.pi * u) ** 0.5, 0.15)
def paper_flutter(dur):
    n_ = int(dur * SR); x = np.zeros((n_, 2))
    for k in range(14):
        t0 = rng.uniform(0, dur - 0.5); L = rng.uniform(0.25, 0.6); m = int(L * SR); i = int(t0 * SR)
        tm = T(m)
        y = bp(noise(m), 1800, 6500) * (0.5 + 0.5 * np.sin(2 * np.pi * rng.uniform(22, 38) * tm)) * np.sin(np.pi * tm / L)
        x[i:i + m] += st(y * rng.uniform(0.4, 1.0), rng.uniform(-0.8, 0.8))
    return x
def beacon():
    a = sweep_tone(600, 1200, 0.18); b_ = sweep_tone(900, 1800, 0.22)
    out = np.zeros((int(0.6 * SR), 2)); out[:len(a)] += a; out[int(0.12 * SR):int(0.12 * SR) + len(b_)] += b_
    return out
def clunk():
    n_ = int(0.35 * SR); t = T(n_)
    x = np.sin(2 * np.pi * 70 * t) * np.exp(-t / 0.05) + 0.5 * bp(noise(n_), 1500, 5000) * np.exp(-t / 0.004)
    return st(x, rng.uniform(-0.4, 0.4))
def scrape(dur):
    n_ = int(dur * SR); t = T(n_); u = t / dur
    grain = bp(noise(n_), 900, 4000) * (0.6 + 0.4 * (rng.random(n_) > 0.7))
    whine = np.sin(2 * np.pi * np.cumsum(180 + 520 * u ** 1.5) / SR) * 0.25 * u
    return st(lp(grain * 0.5, 5500) + whine * np.clip(t / 0.1, 0, 1), 0.0)
def strain():
    n_ = int(0.6 * SR); t = T(n_); u = t / 0.6
    x = np.sin(2 * np.pi * np.cumsum(700 + 500 * u + 40 * np.sin(2 * np.pi * 14 * t)) / SR) * 0.4 * np.sin(np.pi * u)
    creak = bp(noise(n_), 300, 1200) * (rng.random(n_) > 0.92) * 0.6
    return st(x + creak, 0.0)
def heavy_thud():
    n_ = int(0.9 * SR); t = T(n_)
    x = np.sin(2 * np.pi * (60 + 50 * np.exp(-t / 0.02)) * t) * np.exp(-t / 0.18) + 0.5 * lp(noise(n_), 600) * np.exp(-t / 0.05)
    bonk = np.sin(2 * np.pi * 330 * t) * np.exp(-t / 0.08) * 0.35
    return reverb(st(x + bonk), 0.15)
def shrink():
    n_ = int(0.5 * SR); t = T(n_); u = t / 0.5
    x = lp(svf_bp(noise(n_), 3000 * (300 / 3000) ** u, 2.0), 7000) * np.sin(np.pi * u)
    pop = np.sin(2 * np.pi * np.cumsum(500 + 900 * np.exp(-np.clip(t - 0.42, 0, None) / 0.02)) / SR) * np.exp(-np.clip(t - 0.42, 0, None) / 0.04) * (t > 0.42)
    return st(x * 0.8 + pop * 0.6, 0.0)
def jet(dur):
    n_ = int(dur * SR); t = T(n_); u = t / dur
    x = lp(svf_bp(noise(n_), 300 + 2800 * u ** 1.3, 1.0), 9000) * np.clip(t / 0.2, 0, 1) * np.clip((dur - t) / 0.6, 0, 1)
    return np.stack([x, np.roll(x, 400)], 1)


def crickets2(dur):
    """a field of crickets: syllable trains on a ~4.5 kHz carrier, two close, three distant, over a night bed"""
    n_ = int(dur * SR); out = np.zeros((n_, 2))
    for f, per, off, g, pan, far in [(4520, 0.41, 0.03, 1.0, -0.35, False), (4870, 0.53, 0.21, 0.8, 0.5, False),
                                     (4310, 0.37, 0.11, 0.35, 0.8, True), (5050, 0.47, 0.3, 0.3, -0.75, True), (4680, 0.6, 0.05, 0.25, 0.1, True)]:
        x = np.zeros(n_); tt = off
        while tt < dur:
            syl = 3 + (rng.random() < 0.4)
            rate = rng.uniform(28, 34)
            for k in range(syl):
                L = rng.uniform(0.014, 0.019); m = int(L * SR); s0 = int((tt + k / rate) * SR)
                if s0 + m >= n_: break
                pt = T(m); u = pt / L
                fm = f * (1 + 0.018 * (1 - u))
                ph = 2 * np.pi * np.cumsum(fm) / SR
                env = np.clip(pt / 0.003, 0, 1) * np.exp(-u * 2.2)
                rasp = 1 + 0.35 * lp(noise(m), 500) / 0.05
                x[s0:s0 + m] += g * rng.uniform(0.7, 1.0) * env * rasp * (np.sin(ph) + 0.12 * np.sin(2 * ph))
            tt += per * rng.uniform(0.9, 1.1)
        if far: x = lp(x, 5200)
        d = int(abs(pan) * 0.0006 * SR)
        l, r = (x, np.roll(x, d)) if pan < 0 else (np.roll(x, d), x)
        out += np.stack([l * (1 - 0.4 * max(pan, 0)), r * (1 + 0.4 * min(pan, 0))], 1)
    bed = lp(noise(n_), 320, 2) * 0.05 + bp(noise(n_), 2500, 7000) * 0.004
    out += np.stack([bed, np.roll(bed, 977)], 1)
    out *= np.clip(T(n_) / 0.15, 0, 1)[:, None]
    out[-int(0.02 * SR):] *= np.linspace(1, 0, int(0.02 * SR))[:, None]
    return reverb(out, 0.18, ROOM)
def lamp_pop():
    n_ = int(0.5 * SR); t = T(n_)
    click = bp(noise(n_), 2500, 9000) * np.exp(-t / 0.004)
    tink = sum(np.sin(2 * np.pi * f * t) * np.exp(-t / d) for f, d in [(3200, 0.05), (5100, 0.03), (7400, 0.02)]) * 0.25
    fizz = bp(noise(n_), 1500, 6000) * (rng.random(n_) > 0.85) * np.exp(-t / 0.12) * 0.5
    return st(click + tink + fizz, 0.5)
def roll_whoosh():
    a = whoosh(0.65, 300, 2400, -0.7, 0.7); b_ = whoosh(0.5, 500, 3000, 0.6, -0.5)
    out = np.zeros((int(1.0 * SR), 2)); out[:len(a)] += a; i = int(0.3 * SR); out[i:i + len(b_)] += b_ * 0.7
    return out
def strain2(dur):
    n_ = int(dur * SR); t = T(n_); u = t / dur
    whine = np.sin(2 * np.pi * np.cumsum(260 + 420 * u ** 1.3 + 30 * np.sin(2 * np.pi * 9 * t)) / SR) * 0.3
    flutter = bp(noise(n_), 1200, 5000) * (0.5 + 0.5 * np.sin(2 * np.pi * 31 * t)) * 0.35
    creak = bp(noise(n_), 250, 1100) * (rng.random(n_) > 0.93) * 0.5
    x = (whine + flutter + creak) * np.clip(t / 0.1, 0, 1) * np.clip((dur - t) / 0.15, 0, 1)
    return st(x, 0.0)

# ------------------------------------------------------------------ score
E_ = ev['events']
def load_any(path):
    sr, d = wavfile.read(path)
    d = d.astype(np.float32) if d.dtype.kind == 'f' else d.astype(np.float32) / 32768.0
    if d.ndim == 1: d = np.stack([d, d], 1)
    assert sr == SR, sr
    return d
stem_lufs = {'piano': -22.0, 'strings': -22.0, 'bass': -23.0, 'brass': -23.0, 'timp': -25.0, 'perc': -26.0, 'mar': -22.5,
             'glock': -28.0, 'harp': -26.0, 'spic': -27.0, 'stab': -27.5, 'kit': -22.5}
wet = {'piano': 0.22, 'strings': 0.32, 'bass': 0.05, 'brass': 0.28, 'timp': 0.3, 'perc': 0.3, 'mar': 0.18, 'glock': 0.32,
       'harp': 0.32, 'spic': 0.18, 'stab': 0.2, 'kit': 0.08}
music = np.zeros((N, 2))
for k, lv in stem_lufs.items():
    v = load_any(f'{STEMS}/{k}.wav')
    assert np.abs(v).max() > 1e-4, ('silent stem', k)
    v = hp(v.T, 28 if k in ('bass', 'timp', 'perc') else 40).T
    v = lufs_norm(v, lv)
    v = reverb(v, wet[k], HALL if k not in ('kit', 'bass') else ROOM)
    L = min(N, len(v)); music[:L] += v[:L]
music = comp(music, -20, 2.0, 0.01, 0.2)
music = lufs_norm(music, -19.0)
g = np.ones(N)
def seg(t0, t1, gdb): a, b = int(t0 * SR), int(t1 * SR); g[a:b] = db(gdb)
seg(0, 6.2, -2.0)
seg(16.6, 21.0, -3.0)
seg(21.0, 26.0, -1.5)
seg(35.5, 39.5, 1.0)
seg(8.3, 11.52, -80)
seg(32.72, 35.0, -80)
seg(40.1, 45.0, -1.5)
from scipy.ndimage import uniform_filter1d
g = uniform_filter1d(g, int(0.012 * SR))
def duck(t0, t1, amount, ramp=0.12):
    a, b = int(t0 * SR), int(t1 * SR); r = int(ramp * SR); d = db(amount)
    g[a:b] *= d
    g[max(0, a - r):a] *= np.linspace(1, d, a - max(0, a - r))
    g[b:b + r] *= np.linspace(d, 1, len(g[b:b + r]))
duck(22.25, 25.4, -7)
duck(29.15, 29.8, -4)
music *= g[:, None]
place(music, 0.0)

# ------------------------------------------------------------------ SFX from the shared cue list
for e in E_:
    t, k = e['t'], e['type']
    if k == 'terminalAmbience': place(lufs_norm(terminal_amb(e['dur']), -40), t)
    elif k == 'flapRow': place(peak_norm(flap_row(e['n'], e['dur']), -14), t)
    elif k == 'whooshUp': place(peak_norm(whoosh(0.7, 250, 2600, -0.3, 0.4), -16), t - 0.15)
    elif k == 'windFlight': place(lufs_norm(wind(e['dur']), -31), t)
    elif k == 'thunder': place(peak_norm(thunder(), -11), t)
    elif k == 'thunderSoft': place(peak_norm(thunder(True), -16), t)
    elif k == 'chime': place(peak_norm(pa_chime(), -20), t)
    elif k == 'paSoft': place(peak_norm(pa_soft(), -26), t)
    elif k == 'thunderFar': place(peak_norm(thunder(True), -24), t)
    elif k == 'crickets': place(lufs_norm(crickets2(e['dur']), -31), t)
    elif k == 'radarSweep': place(peak_norm(radar_sweep(), -14), t)
    elif k == 'towerRise': place(peak_norm(tower_rise(e['dur']), -12), t)
    elif k == 'logoHit': place(peak_norm(impact(True), -7), t)
    elif k == 'cut': place(peak_norm(whoosh(0.35, 400, 2200, 0.2, -0.2), -24), t - 0.12)
    elif k == 'blip': place(peak_norm(sonar(1800, 0.7), -20), t)
    elif k == 'routeLine': place(peak_norm(sweep_tone(600, 1250, 0.7), -24), t)
    elif k == 'fizzle': place(peak_norm(sweep_tone(900, 180, 0.6, 0.06), -20), t)
    elif k == 'popCard': place(peak_norm(popfx(), -18), t)
    elif k == 'okTick': place(peak_norm(passfx(), -19), t)
    elif k == 'failTick': place(peak_norm(fail(), -15), t)
    elif k == 'swarm': place(lufs_norm(paper_flutter(e['dur']), -30), t)
    elif k == 'beacon': place(peak_norm(beacon(), -16), t)
    elif k == 'disperse': place(peak_norm(whoosh(0.9, 400, 2400, -0.6, 0.8), -18), t)
    elif k == 'facadeLights':
        for j in range(6): place(peak_norm(clunk(), -17 - j * 0.5), t + j * 0.09)
    elif k == 'ding': place(reverb(peak_norm(ding(), -11), 0.15), t)
    elif k == 'rollStart': place(peak_norm(scrape(e['dur']), -18), t)
    elif k == 'strain': place(peak_norm(strain2(e.get('dur', 0.6)), -17), t)
    elif k == 'pop': place(peak_norm(lamp_pop(), -17), t)
    elif k == 'rollWhoosh': place(peak_norm(roll_whoosh(), -15), t)
    elif k == 'thud': place(peak_norm(heavy_thud(), -8), t)
    elif k == 'shrink': place(peak_norm(shrink(), -14), t)
    elif k == 'takeoff': place(peak_norm(jet(e['dur']), -15), t)
    elif k == 'airySwell': place(lufs_norm(wind(2.6), -32), t)
    elif k == 'finalHit': place(peak_norm(impact(False), -10), t)

# ------------------------------------------------------------------ master
master = hp(master.T, 22).T
master = lp(master.T, 15500).T
master = comp(master, -16, 2.0, 0.003, 0.15)
master = master[:int(DUR * SR)]
fi = int(0.01 * SR); master[:fi] *= np.linspace(0, 1, fi)[:, None]
fo = int(0.8 * SR); master[-fo:] *= np.linspace(1, 0, fo)[:, None] ** 1.5
from scipy.ndimage import minimum_filter1d
def limiter(x, ceiling_db=-2.6, look=0.004, rel=0.08):
    c = db(ceiling_db)
    up = resample_poly(x, 4, 1, axis=0)
    det = np.abs(up).max(1)[:len(x) * 4].reshape(-1, 4).max(1)
    need = np.minimum(1.0, c / (det + 1e-12))
    Lk = int(look * SR)
    gg = minimum_filter1d(need, size=2 * Lk + 1); gg = uniform_filter1d(gg, size=Lk); gg = np.minimum(gg, need)
    a_r = np.exp(-1 / (rel * SR)); out = np.empty_like(gg); e1 = 1.0
    for i in range(len(gg)):
        e1 = gg[i] if gg[i] < e1 else a_r * e1 + (1 - a_r) * gg[i]
        out[i] = e1
    return x * out[:, None]
master = master / (np.max(np.abs(master)) + 1e-12) * db(-6)
for _ in range(4):
    master = limiter(master * db(-14.0 - meter.integrated_loudness(master)))
up = resample_poly(master, 4, 1, axis=0)
print('integrated LUFS:', round(meter.integrated_loudness(master), 2), ' true-peak approx dBTP:', round(20 * np.log10(np.abs(up).max()), 2))
wavfile.write(OUT, SR, (np.clip(master, -1, 1) * 32767).astype(np.int16))
print('wrote', OUT)
