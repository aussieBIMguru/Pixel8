# Pixel8 music

Each `.mml` file here is one track, written as text and rendered at startup by the game's NES-style chip synth
(`Pixel8.Core.Audio`: `Mml` parses, `ChipSynth` renders). There are no audio files and no licences. All tracks
are original. Edit a file and restart the game to hear the change. To check timing or render a WAV without building, run
`python tools/music/preview.py [track ...]`.

| File | Plays |
|---|---|
| `title.mml` | title screen, party select |
| `map1.mml` … `map5.mml` | the map (and its overlays) on Depth 1 … 5 (map4 Sunken Halls, map5 The Cold Crypt, round 11) |
| `combat.mml` | normal and elite fights |
| `boss.mml` | boss fights |
| `victory.mml` · `defeat.mml` | stings: after every won fight / when the party falls (play once, the music ducks) |

## Format

```
# comment (anything after #)
title: Torchlit Halls     header: title, tempo (quarter notes per minute), loop (yes/no)
tempo: 96
loop: yes

A: @2 v9 l8               channel lines: A and B are pulse waves, T is the triangle (bass), N is noise (drums)
A: o5 e4 a4 g e d4 |      lines for the same channel join up in order
```

| Command | Meaning |
|---|---|
| `c d e f g a b` | a note; `+` sharp, `-` flat (`f+`, `b-`), then an optional length and dots |
| `1 2 4 8 16 32` after a note | whole, half, quarter, eighth… (`c4.` dotted quarter); no number = the `l` length |
| `r` | rest (takes a length too) |
| `^8` | extend the previous note (a tie) |
| `o4` · `>` · `<` | set octave (o4 c = middle C) · up one · down one |
| `l8` | default length |
| `v0`–`v15` | volume |
| `@0`–`@3` | pulse duty: 12.5% (thin), 25% (classic), 50% (square), 75% |
| `q1`–`q8` | gate: how much of each note sounds, in eighths (default 7; `q8` = legato) |
| `[ ... ]3` | repeat (default 2; can nest) |
| `\|` | bar line: checked against 4/4, otherwise ignored |
| on `N`: `k s h x` | kick, snare, closed hat, crash |

Looping tracks should have every channel the same length, in whole bars. The tests and `preview.py` both check this.
