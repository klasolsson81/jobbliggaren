"""A small multi-sample player over VSCO-2 Community Edition (CC0) recordings.

Instrument(folder, pattern) indexes every WAV whose name carries a note (A#3, C4 ...) and an optional
dynamic layer (v1..v5, dyn1..) and round robin. play() picks the nearest recorded root, resamples it to
pitch, applies a release, and mixes it into a stereo buffer.
"""
import os, re, subprocess
import numpy as np

SR = 48000
ROOT = os.environ.get('VSCO', os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'vsco-2-ce'))
NOTE = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
_cache = {}


def load(path):
    if path not in _cache:
        raw = subprocess.run(['ffmpeg', '-v', 'error', '-i', path, '-f', 'f32le', '-ac', '2', '-ar', str(SR), '-'],
                             check=True, stdout=subprocess.PIPE).stdout
        _cache[path] = np.frombuffer(raw, np.float32).reshape(-1, 2).copy()
    return _cache[path]


def midi_of(name, acc, octave):
    return NOTE[name] + (1 if acc == '#' else -1 if acc == 'b' else 0) + 12 * (int(octave) + 1)


def f0(x):
    """Rough fundamental of a sample's sustained part (autocorrelation), for verifying note names."""
    m = x[len(x) // 8: len(x) // 8 + 16384].mean(axis=1)
    m = m - m.mean()
    ac = np.fft.irfft(np.abs(np.fft.rfft(m, 65536)) ** 2)[:4000]
    lo = SR // 2000
    k = lo + int(np.argmax(ac[lo:]))
    return SR / k


_trim = {}


def trimmed(path):
    """start a sample just before it reaches 5% of its peak, so slow bowed swells land on the beat"""
    if path not in _trim:
        x = load(path); y = np.abs(x).max(1)
        i = max(0, int(np.argmax(y > y.max() * 0.05)) - int(0.012 * SR))
        z = x[i:].copy(); f = int(0.004 * SR); z[:f] *= np.linspace(0, 1, f)[:, None]
        _trim[path] = z
    return _trim[path]


class Instrument:
    def __init__(self, folder, rx=r'_([A-G])(#|b)?(-?\d)_', layer=r'(?:_v|dyn|_)(\d)(?:_|$)', release=0.25, gain=1.0, transpose=0, mapping=None, trim=True):
        self.release, self.gain, self.transpose, self.trim = release, gain, transpose, trim
        self.samples = []  # (midi, layer, path)
        d = os.path.join(ROOT, folder)
        for f in sorted(os.listdir(d)):
            if not f.lower().endswith('.wav'): continue
            if mapping:
                midi, lay = mapping(f)
                if midi is None: continue
            else:
                m = re.search(rx, f)
                if not m: continue
                midi = midi_of(*m.groups())
                lm = re.search(layer, f[m.end() - 1:])
                lay = int(lm.group(1)) if lm else 1
            self.samples.append((midi + transpose, lay, os.path.join(d, f)))
        assert self.samples, folder
        self.layers = sorted({s[1] for s in self.samples})
        self._rr = 0

    def pick(self, midi, vel):
        lay = self.layers[min(len(self.layers) - 1, int(vel * len(self.layers)))]
        cand = [s for s in self.samples if s[1] == lay] or self.samples
        best = min(abs(s[0] - midi) for s in cand)
        near = [s for s in cand if abs(s[0] - midi) == best]
        self._rr += 1
        return near[self._rr % len(near)]

    def render(self, midi, vel, dur):
        root, _, path = self.pick(midi, vel)
        x = load(path)
        if self.trim:
            x = trimmed(path)
        ratio = 2 ** ((midi - root) / 12)
        n_out = int(min(len(x) / ratio, (dur + self.release) * SR + 1))
        if n_out <= 8: return np.zeros((8, 2), np.float32)
        src = np.arange(n_out) * ratio
        y = np.stack([np.interp(src, np.arange(len(x)), x[:, c]) for c in (0, 1)], 1)
        rel0 = int(dur * SR)
        if rel0 < n_out:
            r = np.ones(n_out); k = n_out - rel0
            r[rel0:] = np.exp(-np.linspace(0, 6, k))
            y *= r[:, None]
        g = self.gain * (0.35 + 0.65 * vel)
        return (y * g).astype(np.float32)


class Track:
    def __init__(self, seconds):
        self.buf = np.zeros((int(seconds * SR) + SR, 2), np.float32)

    def add(self, t, y):
        i = int(round(t * SR))
        if i < 0: y = y[-i:]; i = 0
        j = min(len(self.buf), i + len(y))
        self.buf[i:j] += y[:j - i]

    def note(self, inst, t, midi, vel, dur, jitter=0.006, rng=None):
        if rng is not None: t += rng.uniform(-jitter, jitter)
        self.add(t, inst.render(midi, vel, dur))
