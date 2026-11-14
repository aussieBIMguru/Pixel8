"""Checks and previews Pixel8 music without building the game.

    python tools/music/preview.py              check every track in data/music (timing), print a table
    python tools/music/preview.py combat boss  also write combat.wav / boss.wav (two loops each) into tools/music/out

Needs Python 3 with numpy and scipy. chip.py mirrors Pixel8.Core.Audio (Mml + ChipSynth); keep them in step.
"""
import os, sys, wave
import numpy as np
sys.path.insert(0, os.path.dirname(__file__))
import chip

here = os.path.dirname(os.path.abspath(__file__))
music = os.path.normpath(os.path.join(here, '..', '..', 'data', 'music'))
files = sorted(os.path.join(music, f) for f in os.listdir(music) if f.endswith('.mml'))
ok = True
for f in files:
    title, tempo, loop, lengths, problems = chip.check(f)
    beats = max(lengths.values())
    print(f'{os.path.basename(f):14} {title:18} {tempo:5.0f} bpm {beats / 4:5.1f} bars {beats * 60 / tempo:5.1f}s loop={loop} ' +
          ('OK' if not problems else 'PROBLEMS'))
    for p in problems:
        print('    ' + p)
        ok = False

out = os.path.join(here, 'out')
for name in sys.argv[1:]:
    os.makedirs(out, exist_ok=True)
    title, loop, samples, _ = chip.render(open(os.path.join(music, name + '.mml')).read())
    if loop:
        samples = np.concatenate([samples, samples])
    pcm = (np.clip(samples, -1, 1) * 32767 * 0.9).astype('<i2')
    with wave.open(os.path.join(out, name + '.wav'), 'wb') as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(chip.RATE); w.writeframes(pcm.tobytes())
    print('wrote', os.path.join(out, name + '.wav'))
sys.exit(0 if ok else 1)
