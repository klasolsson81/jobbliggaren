"""Compose the score as MIDI stems and render them with FluidSynth (FluidR3 GM).

Two pieces:
  * hold.wav  - cheesy bossa "on hold" music for the cold open (gets the phone filter + tape stop in mix.py)
  * stem_*.wav - the main cue, 112 BPM, D major, aligned so that bar 1 lands on the logo check (M0).

Usage: python3 compose.py <events.json> <outdir>
"""
import json, os, random, subprocess, sys
import mido

SF2 = '/usr/share/sounds/sf2/FluidR3_GM.sf2'
SR = 48000
random.seed(7)

ev = json.load(open(sys.argv[1]))
OUT = sys.argv[2]
os.makedirs(OUT, exist_ok=True)
C = ev['clock']
BPM, BEAT, BAR = C['BPM'], C['BEAT'], C['BAR']

# note helpers ---------------------------------------------------------------
NAMES = {'C': 0, 'D': 2, 'E': 4, 'F': 5, 'G': 7, 'A': 9, 'B': 11}
def n(s):
    """'F#4' -> midi number"""
    name, acc, octv = s[0], s[1:-1], int(s[-1])
    v = NAMES[name] + 12 * (octv + 1)
    v += acc.count('#') - acc.count('b')
    return v
def ch(*names): return [n(x) for x in names]


class Stem:
    def __init__(self, name, program, channel=0, bpm=BPM, swing=0.0):
        self.name, self.program, self.channel, self.bpm, self.swing = name, program, channel, bpm, swing
        self.notes = []  # (beat, dur_beats, note, vel)

    def add(self, beat, dur, notes, vel, jitter=0.012, vj=7):
        if isinstance(notes, int): notes = [notes]
        for k, m in enumerate(notes):
            b = beat
            # light swing on off-beat 8ths
            if self.swing and abs((b * 2) % 2 - 1) < 1e-6:
                b += self.swing
            t_j = random.uniform(-jitter, jitter) / (60 / self.bpm)
            self.notes.append((b + t_j + k * 0.0, dur, m, max(1, min(127, int(vel + random.uniform(-vj, vj))))))

    def write(self, path):
        mid = mido.MidiFile(ticks_per_beat=480)
        tr = mido.MidiTrack(); mid.tracks.append(tr)
        tr.append(mido.MetaMessage('set_tempo', tempo=mido.bpm2tempo(self.bpm), time=0))
        msgs = []
        if self.channel != 9:
            msgs.append((0, mido.Message('program_change', program=self.program, channel=self.channel, time=0)))
        msgs.append((0, mido.Message('control_change', control=91, value=40, channel=self.channel, time=0)))  # reverb send
        msgs.append((0, mido.Message('control_change', control=93, value=0, channel=self.channel, time=0)))   # chorus
        for (b, d, m, v) in self.notes:
            on = max(0, int(round(b * 480))); off = max(on + 1, int(round((b + d) * 480)))
            msgs.append((on, mido.Message('note_on', note=m, velocity=v, channel=self.channel, time=0)))
            msgs.append((off, mido.Message('note_off', note=m, velocity=0, channel=self.channel, time=0)))
        msgs.sort(key=lambda x: (x[0], 0 if x[1].type == 'note_off' else 1))
        last = 0
        for t, msg in msgs:
            msg.time = t - last; last = t
            tr.append(msg)
        # tail so the reverb rings out
        tr.append(mido.MetaMessage('end_of_track', time=480 * 8))
        mid.save(path)


def render(stem, wav):
    midp = wav.replace('.wav', '.mid')
    stem.write(midp)
    subprocess.run(['fluidsynth', '-ni', '-q', '-g', '0.7', '-r', str(SR),
                    '-o', 'synth.reverb.active=0', '-o', 'synth.chorus.active=0',
                    '-F', wav, SF2, midp], check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)


# =============================================================================
# 1. HOLD MUSIC (bossa, Bb, 126 BPM) - "Ditt samtal är viktigt för oss"
# =============================================================================
HB = 126
ep = Stem('hold_ep', 4, 0, HB)        # Rhodes
vib = Stem('hold_vib', 11, 1, HB)     # vibraphone melody
gtr = Stem('hold_gtr', 24, 2, HB)     # nylon guitar
hbass = Stem('hold_bass', 32, 3, HB)  # acoustic bass
hdr = Stem('hold_dr', 0, 9, HB)
prog = [('Bb2', ch('D4', 'F4', 'A4', 'C5')), ('G2', ch('D4', 'F4', 'Bb4', 'C5')),
        ('C3', ch('Eb4', 'G4', 'Bb4', 'D5')), ('F2', ch('Eb4', 'A4', 'C5', 'D5'))] * 2
for i, (root, voic) in enumerate(prog):
    b0 = i * 4
    # bossa comp: 1, 2&, 3&(tie), 4&
    for off, d in [(0, 1.2), (1.5, 0.45), (2.5, 0.9), (3.5, 0.45)]:
        ep.add(b0 + off, d, voic, 52)
        gtr.add(b0 + off, d * 0.7, [voic[0] - 12, voic[1], voic[2]], 46)
    r = n(root); fifth = r + 7
    for off, m, d in [(0, r, 1.4), (1.5, fifth, 0.45), (2, fifth, 1.4), (3.5, r, 0.45)]:
        hbass.add(b0 + off, d, m, 80)
    for k in range(8):
        hdr.add(b0 + k * 0.5, 0.2, 70 if k % 2 == 0 else 69, 38 if k % 2 else 52)  # cabasa/maracas
    hdr.add(b0, 0.2, 37, 40); hdr.add(b0 + 1.5, 0.2, 37, 34); hdr.add(b0 + 3, 0.2, 37, 38)
mel = [(0, 'D5', 1.5), (1.5, 'C5', 0.5), (2, 'Bb4', 1), (3, 'A4', 1),
       (4, 'Bb4', 1.5), (5.5, 'D5', 0.5), (6, 'F5', 2),
       (8, 'Eb5', 1.5), (9.5, 'D5', 0.5), (10, 'C5', 1), (11, 'Bb4', 1),
       (12, 'A4', 1.5), (13.5, 'C5', 0.5), (14, 'F5', 2),
       (16, 'D5', 1.5), (17.5, 'C5', 0.5), (18, 'Bb4', 1), (19, 'A4', 1)]
for b, s, d in mel:
    vib.add(b, d * 0.95, n(s), 74, jitter=0.006)
for st in (ep, vib, gtr, hbass, hdr):
    render(st, f'{OUT}/{st.name}.wav')

# =============================================================================
# 2. MAIN CUE (120 BPM, D major). Positions are in beats from M0 (the logo tick).
#    logo 0-4 | ch1 4-14 | ch2 14-26 | ch3 26-40 | ch4 40-52 | trash 52-60 | end 60-68 | final chord 68
#    MIDI beat 0 = M0 - 4 beats (pickup bar). Drums are exported as hits and synthesized in mix.py.
# =============================================================================
def A(beat): return beat + 4

piano = Stem('piano', 0, 0)
mar = Stem('marimba', 12, 1)
glock = Stem('glock', 9, 2)
bass = Stem('bass', 32, 3)
vibes = Stem('vibes', 11, 4)
pad = Stem('pad', 89, 5)
HITS = []   # (beat_from_M0, note, velocity)
def hit(beat, note, vel, jitter=0.004):
    HITS.append((beat + random.uniform(-jitter, jitter) / BEAT, note, max(1, min(127, int(vel + random.uniform(-5, 5))))))

CH = {
    'D':     ('D2', ch('D3', 'A3', 'C#4', 'E4', 'F#4'), ch('D5', 'F#5', 'A5', 'C#6', 'E6')),
    'Dadd9': ('D2', ch('D3', 'A3', 'E4', 'F#4', 'A4'), ch('D5', 'E5', 'F#5', 'A5', 'D6')),
    'Bm7':   ('B1', ch('B2', 'F#3', 'A3', 'D4', 'F#4'), ch('B4', 'D5', 'F#5', 'A5', 'D6')),
    'F#m7':  ('F#2', ch('F#3', 'C#4', 'E4', 'A4'), ch('F#4', 'A4', 'C#5', 'E5', 'A5')),
    'Gmaj7': ('G1', ch('G2', 'D3', 'F#3', 'B3', 'D4'), ch('G4', 'B4', 'D5', 'F#5', 'B5')),
    'G':     ('G1', ch('G2', 'D3', 'G3', 'B3', 'D4'), ch('G4', 'B4', 'D5', 'G5', 'B5')),
    'A':     ('A1', ch('A2', 'E3', 'A3', 'C#4', 'E4'), ch('A4', 'C#5', 'E5', 'A5', 'C#6')),
    'Asus':  ('A1', ch('A2', 'E3', 'A3', 'D4', 'E4'), ch('A4', 'D5', 'E5', 'A5', 'D6')),
    'Em7':   ('E2', ch('E3', 'B3', 'D4', 'G4'), ch('E4', 'G4', 'B4', 'D5', 'G5')),
    'Dmaj9': ('D2', ch('D3', 'A3', 'C#4', 'E4', 'F#4', 'A4'), ch('D5', 'F#5', 'A5', 'C#6', 'E6')),
}
PADV = {'D': ch('D4', 'F#4', 'A4'), 'Dadd9': ch('D4', 'E4', 'A4'), 'Bm7': ch('D4', 'F#4', 'A4'), 'F#m7': ch('C#4', 'E4', 'A4'),
        'Gmaj7': ch('D4', 'F#4', 'B4'), 'G': ch('D4', 'G4', 'B4'), 'A': ch('C#4', 'E4', 'A4'), 'Asus': ch('D4', 'E4', 'A4'),
        'Em7': ch('D4', 'G4', 'B4'), 'Dmaj9': ch('C#4', 'E4', 'A4')}

# pickup + logo (beats -1 .. 4)
for b, s_ in [(-1.0, 'A4'), (-0.5, 'C#5'), (-0.25, 'E5')]:
    mar.add(A(b), 0.3, n(s_), 66)
piano.add(A(0), 4, CH['Dmaj9'][1], 56)
pad.add(A(0), 4, PADV['Dmaj9'], 40)
bass.add(A(0), 3.5, n('D2'), 70)
glock.add(A(0), 2.0, ch('D6', 'A6'), 92, jitter=0)
glock.add(A(1), 1.5, n('F#6'), 70, jitter=0)
arp1 = ch('D5', 'F#5', 'A5', 'C#6', 'E6', 'C#6', 'A5', 'F#5')
for k in range(1, 8):
    mar.add(A(k * 0.5), 0.4, arp1[k], 52 + (8 if k % 2 == 0 else 0))
for k in range(8):
    hit(2 + k * 0.25, 82, 16 + k * 3)

CHAPTERS = [  # start beat, [(chord, beats)], level
    (4,  [('Dadd9', 4), ('Bm7', 4), ('A', 2)], 1),
    (14, [('D', 4), ('F#m7', 4), ('Gmaj7', 2), ('Asus', 2)], 2),
    (26, [('Bm7', 4), ('Gmaj7', 4), ('Em7', 4), ('A', 2)], 3),
    (40, [('Dadd9', 4), ('Bm7', 4), ('G', 2), ('A', 2)], 4),
]
hook = [(0, 'F#5'), (0.5, 'A5'), (1, 'E5'), (1.5, 'A5'), (2, 'D5'), (2.5, 'A5'), (3, 'E5'), (3.5, 'F#5')]

def chapter(start, chords, lvl, accent=True, fill=True):
    """lvl 1..4 builds the arc: 1 sparse, 2 hats + stabs, 3 claps + vibes, 4 open hat + tambourine + hook"""
    b = start
    total = sum(d for _, d in chords)
    for nm, dur in chords:
        root, voic, pool = CH[nm]
        piano.add(A(b), dur * 0.92, voic, 46 + 4 * lvl)
        if lvl >= 2:
            for off in ([1.5, 3.5] if dur == 4 else [1.5]):
                piano.add(A(b + off), 0.28, voic[1:], 36 + 4 * lvl)
        pad.add(A(b), dur, PADV[nm], 26 + 5 * lvl)
        r = n(root)
        if lvl == 1:
            pat = [(0, r, 1.8), (2, r + 7, 1.8)] if dur == 4 else [(0, r, 1.8)]
        else:
            pat = [(0, r, 1.0), (1.5, r + 12, 0.4), (2, r + 7, 0.9), (3.5, r + 12, 0.4)] if dur == 4 else [(0, r, 0.9), (1.5, r + 7, 0.4)]
        for off, m, d in pat:
            bass.add(A(b + off), d, m, 78 + 4 * lvl, jitter=0.006)
        order = [0, 2, 1, 3, 2, 4, 3, 1]
        for k in range(dur * 2):
            mar.add(A(b + k * 0.5), 0.35, pool[order[k % 8]], 44 + (10 if k % 2 == 0 else 0) + 3 * lvl)
        b += dur
    # drums: beat-indexed from the chapter start, so a 2-beat tail is a clean half bar
    for k in range(total):
        bb = start + k
        if k % 2 == 0: hit(bb, 36, 84 + 3 * lvl if k % 4 == 0 else 78 + 3 * lvl, 0.002)
        else: hit(bb, 37, 62 + 3 * lvl)
        if lvl >= 2 and k % 4 == 2: hit(bb + 0.5, 36, 54, 0.002)
        for q in range(4): hit(bb + q * 0.25, 82, 22 + 2 * lvl + (10 if q == 2 else 0))
        if lvl >= 2:
            hit(bb, 42, 48 + 3 * lvl)
            if not (lvl >= 4 and k % 4 == 3): hit(bb + 0.5, 42, 34 + 3 * lvl)
        if lvl >= 3 and k % 2 == 1: hit(bb, 39, 46 + 3 * lvl)
        if lvl >= 4:
            if k % 4 == 3: hit(bb + 0.5, 46, 54)
            hit(bb + 0.5, 54, 30)
    if accent:
        hit(start, 55, 56 + 4 * lvl, 0)
        glock.add(A(start), 1.0, n('A6'), 60, jitter=0)
    if fill:
        for j, m in enumerate([47, 45, 43]):
            hit(start + total - 0.75 + j * 0.25, m, 46 + j * 8 + 2 * lvl)

for start, chords, lvl in CHAPTERS:
    chapter(start, chords, lvl)
    if lvl >= 3:
        line = ['F#5', 'E5', 'D5', 'E5', 'F#5', 'A5', 'B5', 'A5', 'G5', 'F#5', 'E5', 'C#5']
        for k, s_ in enumerate(line):
            vibes.add(A(start + k), 0.9, n(s_), 54 + 4 * (lvl - 3))
    if lvl == 4:
        for off, s_ in hook:
            glock.add(A(start + 4 + off), 0.3, n(s_) + 12, 38)
# chapter 3: rising marimba runs under the runaway counter (beats 26 + 3.2 .. 26 + 5.2)
run = ch('A4', 'B4', 'C#5', 'D5', 'E5', 'F#5', 'G5', 'A5', 'B5', 'C#6', 'D6', 'E6', 'F#6', 'A6')
for k, m in enumerate(run):
    mar.add(A(26 + 3.25 + k * 0.137), 0.12, m, 46 + k * 2)

# trash 52-60: tiptoe, dead stop on the click (beat 56), build into the end card
tiptoe = ['E3', 'G3', 'B3', 'G3', 'D3', 'F#3', 'A3', 'F#3']
for k, s_ in enumerate(tiptoe):
    piano.add(A(52 + k * 0.5), 0.16, n(s_), 62 if k % 2 == 0 else 50, jitter=0.004)
    mar.add(A(52 + k * 0.5), 0.16, n(s_) + 12, 50 if k % 2 == 0 else 40, jitter=0.004)
bass.add(A(52), 0.4, n('E2'), 66); bass.add(A(54), 0.4, n('D2'), 62)
for k in range(8):
    hit(52 + k * 0.5, 42, 28 if k % 2 else 36)
piano.add(A(54.75), 0.9, ch('C#4', 'G4'), 48)
vibes.add(A(54.75), 1.0, n('C#5'), 44)
for k in range(8):
    hit(58 + k * 0.25, 38, 36 + k * 7, 0.003)
for j, m in enumerate([50, 48, 47, 45]):
    hit(59 + j * 0.25, m, 66 + j * 6, 0.003)
bass.add(A(59), 0.9, n('A1'), 84)
piano.add(A(59), 0.9, CH['A'][1], 50)

# end 60-68, final chord 68
chapter(60, [('Gmaj7', 4), ('Asus', 2), ('A', 2)], 4, accent=True, fill=True)
for off, s_ in [(0, 'B5'), (0.5, 'D6'), (1, 'F#6'), (2, 'E6'), (3, 'D6')]:
    glock.add(A(60 + off), 0.5, n(s_), 68)
for off, s_ in [(0, 'A5'), (1, 'E6'), (2, 'C#6'), (3, 'E6')]:
    glock.add(A(64 + off), 0.5, n(s_), 68)
glock.add(A(66), 0.4, n('A6'), 60, jitter=0)       # the click on "Skapa konto"
piano.add(A(68), 6, CH['Dmaj9'][1], 64)
pad.add(A(68), 6, PADV['Dmaj9'] + [n('F#5')], 48)
bass.add(A(68), 5, n('D2'), 86)
mar.add(A(68), 2, ch('D5', 'A5', 'D6'), 70)
glock.add(A(68), 3, ch('D6', 'F#6', 'A6'), 82, jitter=0)
hit(68, 36, 98, 0); hit(68, 49, 66, 0)

for st in (piano, mar, glock, bass, vibes, pad):
    render(st, f'{OUT}/stem_{st.name}.wav')
json.dump({'hits': sorted(HITS), 'beat': BEAT}, open(f'{OUT}/drum_hits.json', 'w'))
print('rendered stems to', OUT, 'drum hits', len(HITS))
