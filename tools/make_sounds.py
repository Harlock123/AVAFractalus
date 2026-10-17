#!/usr/bin/env python3
"""Synthesises AVAFractalus's sound effects and tunes into ../src/AVAFractalus/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices, and the
tunes are new compositions: a slow, uneasy theme for the planet's surface and a fanfare for the title. The
ship's computer speaks through the formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAFractalus", "Sounds")
RNG = random.Random(1985)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def computer(words):
    voice = say(words, rate=RATE, pitch=118, speed=0.95, size=1.0)
    hum = say(words, rate=RATE, pitch=177, speed=0.95, size=1.0, seed=11)
    return mix(voice, [s * 0.3 for s in hum])


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The fighter.
    write("Fire.wav", tone(0.18, 1800, 300, "square", attack=0.002, release=0.8, gain=0.5), 0.16)
    write("Hurt.wav", mix(tone(0.25, 900, 120, "square", attack=0.001, release=0.9, gain=0.6), tone(0.2, 3000, 600, "noise", release=1.1, gain=0.6)), 0.34)
    write("Crash.wav", mix(tone(0.8, 2000, 60, "noise", attack=0.001, release=0.7), tone(0.6, 120, 30, "sine", release=0.7)), 0.46)
    write("Explode.wav", cat(mix(tone(1.6, 2400, 40, "noise", attack=0.002, release=0.4), tone(1.4, 110, 20, "sine", release=0.5)), silence(0.2)), 0.55)
    write("Landing.wav", tone(1.2, 420, 160, "saw", attack=0.05, vibrato=0.04, vib_rate=20, release=0.4, gain=0.4), 0.22)
    write("Landed.wav", mix(tone(0.25, 120, 60, "sine", attack=0.002, release=1.0), tone(0.15, 900, 200, "noise", release=1.2, gain=0.4)), 0.3)
    write("LiftOff.wav", tone(1.0, 160, 520, "saw", attack=0.08, vibrato=0.04, vib_rate=20, release=0.5, gain=0.4), 0.24)
    write("Deny.wav", cat(tone(0.1, 200, kind="square", release=0.2), silence(0.04), tone(0.14, 160, kind="square", release=0.4)), 0.2)
    # The pilots, and the Jaggi.
    write("Running.wav", cat(*[cat(tone(0.04, 300, 200, "noise", attack=0.001, release=1.4, gain=0.5), silence(0.14)) for _ in range(4)]), 0.14)
    write("Knock.wav", cat(*[cat(mix(tone(0.05, 220, 120, "sine", attack=0.001, release=1.2), tone(0.03, 1200, 400, "noise", release=1.5, gain=0.4)), silence(0.12)) for _ in range(3)]), 0.4)
    write("Airlock.wav", tone(0.7, 3000, 800, "noise", attack=0.05, release=0.6, gain=0.6), 0.22)
    write("Aboard.wav", cat(tone(0.6, 2800, 900, "noise", attack=0.05, release=0.6, gain=0.5), *[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6"]]), 0.3)
    write("Jaggi.wav", mix(tone(0.9, 300, 900, "saw", attack=0.01, vibrato=0.3, vib_rate=28, release=0.5), tone(0.9, 2400, 600, "noise", attack=0.01, release=0.5, gain=0.5)), 0.5)
    write("JaggiIn.wav", cat(tone(0.6, 1200, 200, "saw", vibrato=0.3, vib_rate=30, release=0.4, gain=0.7), mix(tone(0.5, 2000, 100, "noise", release=0.7), tone(0.4, 120, 40, "sine", release=0.7))), 0.5)
    write("Bang.wav", mix(tone(0.12, 160, 70, "square", attack=0.001, release=1.0, gain=0.7), tone(0.08, 1800, 300, "noise", release=1.3, gain=0.6)), 0.36)
    # The Jaggi's guns and saucers.
    write("GunFire.wav", tone(0.3, 600, 1500, "square", attack=0.002, release=0.8, gain=0.4), 0.16)
    write("SaucerFire.wav", tone(0.25, 1400, 700, "saw", attack=0.002, vibrato=0.1, vib_rate=40, release=0.8, gain=0.4), 0.16)
    write("GunDown.wav", mix(tone(0.6, 1800, 100, "noise", attack=0.001, release=0.8), tone(0.5, 200, 50, "sine", release=0.8)), 0.42)
    write("SaucerDown.wav", cat(tone(0.3, 1600, 300, "saw", vibrato=0.2, vib_rate=20, release=0.5, gain=0.6), mix(tone(0.5, 2000, 80, "noise", release=0.8), tone(0.4, 160, 40, "sine", release=0.8))), 0.42)
    write("Saucer.wav", tone(1.2, 500, 700, "sine", attack=0.2, vibrato=0.08, vib_rate=7, release=0.5, gain=0.5), 0.2)
    # The mother ship, and the computer.
    write("Dock.wav", cat(tone(0.8, 200, 800, "saw", attack=0.05, release=0.3, gain=0.4), *[tone(0.1, hz(n), kind="pulse", release=0.3) for n in ["G4", "C5", "E5", "G5", "C6"]]), 0.34)
    write("AllAboard.wav", cat(*[tone(0.08, hz(n), kind="pulse", release=0.3) for n in ["E5", "G5", "C6", "E6"]]), 0.3)
    write("SayAllAboard.wav", computer("AO1 L , P AA IY1 L AH T S , IH1 N !"), 0.5)
    write("LowEnergy.wav", cat(computer("EH1 N ER Z IY , L OW1 !"), silence(0.1), *[tone(0.12, 1000, kind="square", release=0.2, gain=0.5) for _ in range(2)]), 0.4)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.3)

    # The surface (an original composition): a slow, uneasy line over a low drone, a whole number of bars.
    e = 0.5
    lead = [("E4", 1), ("-", e), ("G4", e), ("F#4", 1.5), ("-", e), ("B3", 1), ("C4", 1), ("D4", 1), ("E4", 1),
            ("G4", 1), ("-", e), ("A4", e), ("B4", 1.5), ("-", e), ("A4", 1), ("G4", 1), ("F#4", 2)]
    drone = [("E2", 4), ("C2", 4), ("E2", 4), ("B1", 4)]
    write("Music.wav", tune(84, lead * 2, drone * 2, kind="tri"), 0.26)
    fanfare = [("E5", 0.5), ("B4", 0.5), ("E5", 0.5), ("G5", 1.5), ("F#5", 0.5), ("E5", 0.5), ("D5", 0.5), ("E5", 1), ("B4", 1),
               ("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 2), ("D6", 0.5), ("B5", 1), ("E6", 3)]
    fbass = [("E3", 2), ("B2", 2), ("E3", 2), ("B2", 2), ("E3", 2), ("G2", 1), ("B2", 2), ("E3", 3)]
    write("TitleTune.wav", cat(tune(112, fanfare, fbass, kind="square"), silence(0.8)), 0.34)
    write("Cleared.wav", tune(150, [("B4", 0.5), ("E5", 0.5), ("G5", 0.5), ("B5", 0.5), ("E6", 2)], [("E3", 1), ("B2", 1), ("E3", 2)], kind="pulse"), 0.36)
    write("Victory.wav", tune(150, [("E5", 0.5), ("G#5", 0.5), ("B5", 0.5), ("E6", 1.5), ("D#6", 0.5), ("E6", 0.5), ("F#6", 0.5), ("G#6", 3)],
                              [("E3", 1), ("B2", 1), ("C#3", 1), ("A2", 1), ("B2", 1), ("E3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(66, [("B4", 1), ("G4", 1), ("F#4", 1), ("D#4", 1), ("E4", 4)], [("E3", 2), ("C3", 2), ("B2", 2), ("E2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
