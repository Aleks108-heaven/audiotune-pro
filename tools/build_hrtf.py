"""Builds a 4-channel "true stereo" HRTF impulse response for AudioTune Pro's
Headphones mode from the MIT KEMAR compact HRTF set (128 taps, 44.1 kHz).

Two virtual speakers at -30/+30 degrees azimuth, 0 degrees elevation. Channel
order matches Equalizer APO's true-stereo convention: L->L, L->R, R->L, R->R.

Usage: python tools/build_hrtf.py <path to extracted KEMAR 'compact' folder> <out.wav>
Data: http://sound.media.mit.edu/resources/KEMAR.html  (check its terms before redistributing)
"""
import struct, sys, wave
import numpy as np

def load(folder, azimuth):
    with wave.open(f"{folder}/elev0/H0e{azimuth:03d}a.wav") as w:
        raw = np.frombuffer(w.readframes(w.getnframes()), dtype="<i2").astype(np.float64) / 32768.0
    return raw[0::2], raw[1::2]  # left ear, right ear

folder, out = sys.argv[1], sys.argv[2]
# The compact set only covers 0-180 degrees (right side); KEMAR is symmetric, so the
# left speaker is the right speaker with the ears swapped.
sr_l, sr_r = load(folder, 30)   # right speaker (+30): to left ear, to right ear
sl_l, sl_r = sr_r, sr_l         # left speaker  (-30): to left ear, to right ear
ir = np.stack([sl_l, sl_r, sr_l, sr_r], axis=1)  # L->L, L->R, R->L, R->R

# Normalise so the loudest frequency, for the worst-case input pair, does not exceed 0 dB.
n = 1024
h = np.fft.rfft(ir, n=n, axis=0)
worst = max(np.abs(h[:, 0] + h[:, 2]).max(), np.abs(h[:, 1] + h[:, 3]).max(),
            np.abs(h[:, 0]).max(), np.abs(h[:, 3]).max())
ir = ir / worst * 0.9

with wave.open(out, "wb") as w:
    w.setnchannels(4); w.setsampwidth(2); w.setframerate(44100)
    w.writeframes((np.clip(ir, -1, 1) * 32767).astype("<i2").tobytes())
print(f"wrote {out}: {ir.shape[0]} taps, gain scale {0.9 / worst:.3f}")
