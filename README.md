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

### Publishing a runnable build

`CI-artefact/` holds a framework-dependent Release build you can run directly
(`CI-artefact\AudioTunePro.exe`) without a `dotnet run`/IDE step — handy for
manual testing. It's git-ignored (regenerate it rather than committing it):

```powershell
dotnet publish src/AudioTunePro.App/AudioTunePro.App.csproj -c Release -r win-x64 --self-contained false -o CI-artefact
```

It requires the .NET 10 Desktop Runtime on the machine that runs it (already
present if you can build this repo). Add `--self-contained true` if you need
to hand the folder to a machine without .NET installed (produces a much
larger output).

### Installer

`CI-artefact/AudioTunePro-Setup.msi` is a real installer, built with the
[WiX Toolset](https://wixtoolset.org/) v5 (free — v6+ requires accepting a
paid maintenance-fee EULA, so this project intentionally pins v5). Running it
installs AudioTune Pro to `%LocalAppData%\Programs\AudioTune Pro` (per-user,
**no admin/UAC prompt**) and adds:

- a **Desktop shortcut**
- a **Start Menu** entry (with its own "Uninstall AudioTune Pro" shortcut)
- an entry in Windows Settings → Apps, for the standard uninstall flow

both shortcuts and the app itself use the equalizer-bars icon in
`src/AudioTunePro.App/Assets/icon.ico`.

To rebuild it after code changes (regenerate `CI-artefact/` first, see
above, then):

```powershell
dotnet tool install --global wix --version 5.0.2   # one-time; skip if already installed
wix extension add -g WixToolset.UI.wixext/5.0.2    # one-time
cd installer
wix build AudioTunePro.wxs -ext WixToolset.UI.wixext -arch x64 -o ../CI-artefact/AudioTunePro-Setup.msi
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

## Performance

AudioTune Pro itself does no audio DSP — Equalizer APO does that inside
Windows' own audio engine process (`audiodg.exe`), entirely separate from
this app. What's measured below is only AudioTune Pro's own UI process.

Measured on the built `CI-artefact\AudioTunePro.exe` (Release,
framework-dependent), idle with the level meter running (its default state):

| Metric | Result |
|---|---|
| CPU (sustained idle) | ~0.2s of CPU time per 15s wall-clock → ~1.3% of one core |
| Working set | ~197 MB, flat over a 35s window (no growth) |
| Private bytes | ~120 MB |
| Background threads | 12 |

The CPU figure was measured as `Get-Process` CPU-time delta over a fixed
wall-clock window (`(cpuAfter - cpuBefore) / windowSeconds`); the memory
figures are `WorkingSet64`/`PrivateMemorySize64` sampled at rest. The
~120–200 MB footprint is typical baseline for a WPF + WinForms(tray) .NET
desktop app — mostly the .NET Desktop Runtime/WPF/WinForms framework being
loaded, not something that grows with usage. Turning off **"Show level
meter"** stops the one background thread doing recurring work (WASAPI
loopback capture) if you want the smallest possible footprint.

## Project layout

```
src/
  AudioTunePro.Core/        Platform-agnostic: EQ model, Equalizer APO config
                             generator, auto-gain limiter math, built-in presets.
  AudioTunePro.Core.Tests/  xUnit tests for the Core logic.
  AudioTunePro.App/         WPF UI, Equalizer APO detection/install glue,
                             tray icon, WASAPI level meter, settings storage.
                             Assets/icon.ico is the app/shortcut icon.
installer/
  AudioTunePro.wxs          WiX v5 source for the MSI installer (see above).
  License.rtf               Shown on the installer's license page.
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
