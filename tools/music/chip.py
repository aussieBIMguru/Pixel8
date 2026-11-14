"""Python mirror of Pixel8.Core.Audio (MmlParser + ChipSynth), for validating tracks and rendering WAV previews.
Keep the two in step: same grammar, same envelopes, same mix."""
import math, re, sys, wave, struct
import numpy as np

RATE = 32000
LOWPASS_HZ = 6000
DUTY = [0.125, 0.25, 0.5, 0.75]
NOTE = {'c': 0, 'd': 2, 'e': 4, 'f': 5, 'g': 7, 'a': 9, 'b': 11}
DRUMS = set('kshx')


class Ev:
    def __init__(self, start, dur, pitch, vol, duty, gate):
        self.start, self.dur, self.pitch, self.vol, self.duty, self.gate = start, dur, pitch, vol, duty, gate


def strip_comments(text):
    return [l.split('#', 1)[0].rstrip() for l in text.splitlines()]


def parse(text):
    header, chans = {}, {'A': '', 'B': '', 'T': '', 'N': ''}
    for line in strip_comments(text):
        if not line.strip():
            continue
        m = re.match(r'^\s*([ABTN])\s*:(.*)$', line)
        if m:
            chans[m.group(1)] += ' ' + m.group(2)
            continue
        m = re.match(r'^\s*(\w+)\s*:(.*)$', line)
        if m:
            header[m.group(1).lower()] = m.group(2).strip()
            continue
        raise ValueError('bad line: ' + line)
    tempo = float(header.get('tempo', 120))
    loop = header.get('loop', 'yes').lower() in ('yes', 'true', '1')
    out, bars = {}, {}
    for ch, src in chans.items():
        out[ch], bars[ch] = parse_channel(src, ch)
    return header.get('title', ''), tempo, loop, out, bars


def expand_repeats(src):
    # [ ... ]n, nestable
    while '[' in src:
        m = re.search(r'\[([^\[\]]*)\]\s*(\d*)', src)
        if not m:
            raise ValueError('unbalanced [ ]')
        n = int(m.group(2) or 2)
        src = src[:m.start()] + ' '.join([m.group(1)] * n) + src[m.end():]
    if ']' in src:
        raise ValueError('unbalanced ]')
    return src


def parse_channel(src, ch):
    src = expand_repeats(src)
    i, n = 0, len(src)
    octave, length, vol, duty, gate = 4, 4, 15, 2, 7
    t = 0.0
    evs, bar_marks = [], []

    def num():
        nonlocal i
        j = i
        while i < n and src[i].isdigit():
            i += 1
        return int(src[j:i]) if i > j else None

    def dur():
        nonlocal i
        l = num()
        beats = 4.0 / (l if l else length)
        add = beats
        while i < n and src[i] == '.':
            add /= 2
            beats += add
            i += 1
        return beats

    while i < n:
        c = src[i]
        i += 1
        if c.isspace():
            continue
        if c == '|':
            bar_marks.append(t)
            continue
        if ch == 'N' and c in DRUMS:
            d = dur()
            evs.append(Ev(t, d, c, vol, duty, gate))
            t += d
            continue
        if ch != 'N' and c in NOTE:
            p = NOTE[c]
            while i < n and src[i] in '+-':
                p += 1 if src[i] == '+' else -1
                i += 1
            d = dur()
            evs.append(Ev(t, d, 12 * (octave + 1) + p, vol, duty, gate))
            t += d
            continue
        if c == 'r':
            t += dur()
            continue
        if c == '^':
            d = dur()
            if evs:
                evs[-1].dur += d
            t += d
            continue
        if c == 'o': octave = num(); continue
        if c == '>': octave += 1; continue
        if c == '<': octave -= 1; continue
        if c == 'l': length = num(); continue
        if c == 'v': vol = max(0, min(15, num())); continue
        if c == '@': duty = max(0, min(3, num())); continue
        if c == 'q': gate = max(1, min(8, num())); continue
        raise ValueError(f'channel {ch}: unexpected "{c}" at {i}')
    return (evs, t), bar_marks


def freq(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def env_pulse(n_on, n_total):
    # attack 3 ms, decay to 0.6 over 120 ms, release 20 ms after the gate
    a = int(0.003 * RATE); d = int(0.12 * RATE); r = int(0.02 * RATE)
    e = np.zeros(n_total)
    k = np.arange(n_total)
    on = k < n_on
    e[on] = np.minimum(1, k[on] / max(1, a)) * (0.6 + 0.4 * np.clip(1 - (k[on] - a) / d, 0, 1))
    last = e[n_on - 1] if n_on > 0 else 0
    rel = (k >= n_on) & (k < n_on + r)
    e[rel] = last * (1 - (k[rel] - n_on) / r)
    return e


class Lfsr:
    def __init__(self, seed=1):
        self.s = seed or 1
    def next(self):
        # 15-bit LFSR as on the NES noise channel (long mode)
        bit = (self.s ^ (self.s >> 1)) & 1
        self.s = (self.s >> 1) | (bit << 14)
        return 1.0 if self.s & 1 else -1.0


def render(text):
    title, tempo, loop, chans, _ = parse(text)
    spb = 60.0 / tempo
    total_beats = max(v[1] for v in chans.values())
    N = int(round(total_beats * spb * RATE))
    mix = np.zeros(N)
    gains = {'A': 0.22, 'B': 0.22, 'T': 0.30, 'N': 0.22}
    lfsr = Lfsr(0x4A1)
    for ch, (evs, _) in chans.items():
        buf = np.zeros(N)
        for e in evs:
            s0 = int(round(e.start * spb * RATE))
            s1 = min(N, int(round((e.start + e.dur) * spb * RATE)))
            if s1 <= s0 or e.vol == 0:
                continue
            n_total = s1 - s0
            amp = e.vol / 15.0
            if ch == 'N':
                buf[s0:s1] += drum(e.pitch, n_total, lfsr) * amp
                continue
            n_on = max(1, int(n_total * e.gate / 8))
            f = freq(e.pitch)
            k = np.arange(n_total)
            ph = (k * f / RATE) % 1.0
            if ch == 'T':
                # 4-bit stepped triangle like the NES (32 steps per cycle)
                step = np.floor(ph * 32)
                tri = np.where(step < 16, step, 31 - step)
                w = tri / 7.5 - 1
                ev = np.zeros(n_total); ev[:n_on] = 1
                rel = int(0.005 * RATE)
                ev[n_on:n_on + rel] = np.linspace(1, 0, len(ev[n_on:n_on + rel]))
                buf[s0:s1] += w * ev * amp
            else:
                duty = DUTY[e.duty]
                w = np.where(ph < duty, 1.0, -1.0)
                w += polyblep(ph, f / RATE) - polyblep((ph - duty) % 1.0, f / RATE)
                buf[s0:s1] += w * env_pulse(n_on, n_total) * amp
        mix += buf * gains[ch]
    # DC block (1-pole high-pass ~ 40 Hz) and soft clip
    from scipy.signal import lfilter
    R = 1 - (2 * math.pi * 40 / RATE)
    out = lfilter([1, -1], [1, -R], mix)
    # gentle one-pole low-pass (~6 kHz), as in ChipSynth
    lp = 1 - math.exp(-2 * math.pi * LOWPASS_HZ / RATE)
    out = lfilter([lp], [1, -(1 - lp)], out)
    out = np.tanh(out * 1.1)
    return title, loop, out, chans


def polyblep(t, dt):
    r = np.zeros_like(t)
    a = t < dt
    x = t[a] / dt
    r[a] = x + x - x * x - 1
    b = t > 1 - dt
    x = (t[b] - 1) / dt
    r[b] = x * x + x + x + 1
    return r


def drum(kind, n_total, lfsr):
    k = np.arange(n_total)
    t = k / RATE
    def noise(rate, dur):
        m = min(n_total, int(dur * RATE))
        hold = max(1, int(RATE / rate))
        v = np.zeros(n_total)
        cur = 0
        for i in range(m):
            if i % hold == 0:
                cur = lfsr.next()
            v[i] = cur
        return v
    if kind == 'k':
        f = 150 * np.exp(-t / 0.05) + 45
        ph = np.cumsum(f / RATE) % 1.0
        tri = 4 * np.abs(ph - 0.5) - 1
        return tri * np.exp(-t / 0.09) * (t < 0.2) + noise(8000, 0.01) * 0.3 * np.exp(-t / 0.004)
    if kind == 's':
        f = 220 * np.exp(-t / 0.03) + 160
        ph = np.cumsum(f / RATE) % 1.0
        tri = 4 * np.abs(ph - 0.5) - 1
        return noise(6000, 0.2) * 0.8 * np.exp(-t / 0.06) + tri * 0.4 * np.exp(-t / 0.04)
    if kind == 'h':
        return noise(16000, 0.06) * 0.5 * np.exp(-t / 0.015)
    if kind == 'x':
        return noise(12000, 0.6) * 0.6 * np.exp(-t / 0.18)
    return np.zeros(n_total)


def check(path):
    text = open(path).read()
    title, tempo, loop, chans, bars = parse(text)
    lengths = {ch: v[1] for ch, v in chans.items()}
    problems = []
    used = [l for ch, l in lengths.items() if l > 0]
    if loop and len(set(used)) > 1:
        problems.append(f'channel lengths differ: {lengths}')
    for ch, marks in bars.items():
        for m in marks:
            if abs(m / 4 - round(m / 4)) > 1e-9:
                problems.append(f'{ch}: bar line at beat {m} is not on a bar (4/4)')
    if loop and used and used[0] % 4:
        problems.append(f'loop length {used[0]} beats is not whole bars')
    return title, tempo, loop, lengths, problems


if __name__ == '__main__':
    import os
    ok = True
    for p in sys.argv[1:]:
        title, tempo, loop, lengths, problems = check(p)
        secs = max(lengths.values()) * 60 / tempo
        print(f'{os.path.basename(p):14} {title:16} {tempo:5.0f} bpm  {max(lengths.values()) / 4:5.1f} bars  {secs:5.1f}s  loop={loop}  ' +
              ('OK' if not problems else 'PROBLEMS'))
        for pr in problems:
            print('    ' + pr); ok = False
    sys.exit(0 if ok else 1)
