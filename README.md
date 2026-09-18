# AudioTune Pro — Windows 11 System-Wide Equalizer

A system-wide equalizer for Windows 11: a 10-band graphic EQ, bass/treble
shelving, clipping-protection limiter, and presets tuned for the ASUS
Vivobook's built-in speakers and headphone output.

## How it works

Windows has no supported way to inject DSP into every app's audio without
either a signed kernel driver or a registered audio effects engine.
AudioTune Pro drives **[Equalizer APO](https://equalizerapo.com/)** — a free,
open-source (LGPL) audio effects engine that installs itself as a real
Windows audio endpoint effect, no kernel driver required — and gives it a
polished control surface:

```
┌────────────────────┐   writes config    ┌───────────────────┐   applies DSP   ┌──────────────┐
│  AudioTune Pro UI   │ ─────────────────▶ │  Equalizer APO     │ ───────────────▶│ All app audio │
│  (this app)         │  (config.txt        │  (system audio     │                  │ system-wide   │
│                     │   include file)      │   engine)          │                  │               │
└────────────────────┘                     └───────────────────┘                  └──────────────┘
```

AudioTune Pro never edits your `config.txt` filter definitions directly — it
writes its own `AudioTunePro.txt` include file and adds a single `Include:`
line to `config.txt`, so it can coexist with any other Equalizer APO setup
you already have.

## Prerequisites

1. **Windows 11** with the .NET 10 Desktop Runtime (ships with recent Windows
   updates; if missing, install from https://dotnet.microsoft.com).
2. **[Equalizer APO](https://sourceforge.net/projects/equalizerapo/)** —
   install it once and select your playback device(s) during setup. AudioTune
   Pro detects the install automatically and shows a "Get Equalizer APO"
   button in-app if it isn't found yet.

## Building & running

```powershell
dotnet build AudioTunePro.slnx
dotnet run --project src/AudioTunePro.App/AudioTunePro.App.csproj
```

Or open `AudioTunePro.slnx` in Visual Studio / Rider and run
`AudioTunePro.App`.

Running the tests:

```powershell
dotnet test src/AudioTunePro.Core.Tests/AudioTunePro.Core.Tests.csproj
```

## Features

- **10-band graphic EQ** (31 Hz – 16 kHz) rendered as a single Equalizer APO
  `GraphicEQ` spline — cheap on CPU (one interpolated filter, not ten
  separate peaking filters).
- **Bass / Treble shelving controls** for quick tonal adjustment on top of
  the graphic curve.
- **Presets**, including two tuned specifically for the ASUS Vivobook:
  - *ASUS Vivobook Speakers* — compensates for the bass roll-off below
    ~150 Hz typical of small laptop drivers, tames a harsh 2–4 kHz peak, and
    adds a touch of presence for clarity at low volume.
  - *ASUS Vivobook Headphones* — a gentler baseline for the headphone jack.
  - Flat, Bass Boost, Vocal Boost, Movie, Gaming, Podcast/Voice Call,
    Classical, Rock, Pop, Electronic — plus save/delete your own.
- **Clipping-protection limiter** ("Auto-gain protection"): since Equalizer
  APO has no built-in lookahead/brickwall limiter, AudioTune Pro estimates
  the worst-case constructive peak your curve could produce (accounting for
  adjacent-band overlap) and automatically trims the master preamp so the
  result stays under your chosen ceiling. This is a static, pre-computed
  safeguard, not real-time dynamics processing — see `AutoGainLimiter.cs`
  for the exact math.
- **Live output level meter** via WASAPI loopback capture (visual only —
  negligible CPU cost).
- **System tray**: closing the window keeps AudioTune Pro (and your EQ)
  running in the background; use the tray menu to reopen or exit.
- **Start with Windows** toggle.

## Project layout

```
src/
  AudioTunePro.Core/        Platform-agnostic: EQ model, Equalizer APO config
                             generator, auto-gain limiter math, built-in presets.
  AudioTunePro.Core.Tests/  xUnit tests for the Core logic.
  AudioTunePro.App/         WPF UI, Equalizer APO detection/install glue,
                             tray icon, WASAPI level meter, settings storage.
```

User settings and custom presets are stored as JSON under
`%AppData%\AudioTunePro`. The rendered Equalizer APO config lives at
`<Equalizer APO install dir>\config\AudioTunePro.txt`.

## Known limitations

- Requires Equalizer APO to be installed separately (see Prerequisites) —
  this is what makes true system-wide processing possible without shipping
  a signed kernel driver.
- The limiter is a static gain-trim safeguard computed from the EQ curve,
  not a real-time lookahead/brickwall limiter — extremely loud, already
  near-0dBFS source material can still clip in rare cases.
- The level meter reflects the *pre-EQ* system mix (WASAPI loopback on the
  default render device), not Equalizer APO's post-processing output.
