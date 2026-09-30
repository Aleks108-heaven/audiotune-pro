# AudioTune Pro — Windows 11 System-Wide Equalizer

A system-wide equalizer for Windows 11: a 10-band graphic EQ, bass/treble
shelving, 3D surround (speaker widening and headphone crossfeed/HRTF),
clipping-protection limiter, a system volume slider, and presets tuned for the ASUS
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

### HRTF impulse response (optional)

`tools/build_hrtf.py` builds a 4-channel "true stereo" impulse response
(virtual speakers at ±30°) from the free
[MIT KEMAR](http://sound.media.mit.edu/resources/KEMAR.html) compact HRTF set:

```powershell
# download and unzip compact.zip from the MIT page, then:
python tools/build_hrtf.py <path-to-unzipped-compact> "$env:APPDATA\AudioTunePro\hrtf\mit-kemar-30deg.wav"
```

Then pick that file with **HRTF file…** in Headphones mode. The generated
`.wav` is deliberately **not** committed or bundled in the installer: the MIT
download ships without a licence file, and its terms may be limited to
educational/research use, so check them before redistributing it. The script
needs `numpy`. Files with 2 channels are convolved per ear; 4 channels are a
true-stereo IR (L→L, L→R, R→L, R→R).

### Installer

`CI-artefact/AudioTunePro-Setup.msi` is a real installer, built with the
[WiX Toolset](https://wixtoolset.org/) v5 (free — v6+ requires accepting a
paid maintenance-fee EULA, so this project intentionally pins v5). Running it
installs AudioTune Pro to `C:\Program Files\AudioTune Pro` (per-machine; one
UAC prompt to install or update) and adds:

- an **All-Users Desktop shortcut**
- an **All-Users Start Menu** entry (with its own "Uninstall AudioTune Pro" shortcut)
- an entry in Windows Settings → Apps, for the standard uninstall flow

Program Files is writable only by administrators, so other programs running as
your user can't replace the app with a fake. Settings stay per user in
`%AppData%\AudioTunePro`, and "Start with Windows" is still a per-user setting.

**Upgrading from 1.1.0 or earlier (per-user install in `%LocalAppData%`):**
Windows treats the per-machine package as a separate product, so uninstall the
old version first (Settings → Apps). The installer detects it and tells you.
Your settings and presets are kept.

Both shortcuts and the app itself use the equalizer-bars icon in
`src/AudioTunePro.App/Assets/icon.ico`.

To rebuild it after code changes (regenerate `CI-artefact/` first, see
above, then):

```powershell
dotnet tool install --global wix --version 5.0.2   # one-time; skip if already installed
wix extension add -g WixToolset.UI.wixext/5.0.2    # one-time
cd installer
wix build AudioTunePro.wxs -ext WixToolset.UI.wixext -arch x64 -o ../CI-artefact/AudioTunePro-Setup.msi
```

### Reproducible, verifiable builds

- **Locked packages:** every NuGet dependency (including transitive ones) is pinned to the exact
  version and content hash in each project's `packages.lock.json` (enabled in
  `Directory.Build.props`). CI restores in locked mode and fails if anything differs. To update a
  package on purpose: change the version, run `dotnet restore AudioTunePro.slnx --force-evaluate`,
  and commit the changed lock files.
- **Signed installer:** `.github/workflows/release.yml` (runs on a `v*` tag or manually) builds the
  MSI, signs the app binaries and the MSI with `installer/sign.ps1` when the repository secrets
  `SIGNING_CERT_PFX_BASE64` and `SIGNING_CERT_PASSWORD` exist, and uploads the installer with a
  `SHA256SUMS.txt`. Without the secrets it still builds, unsigned. You can also sign locally:
  `./installer/sign.ps1 -PfxPath cert.pfx -PfxPassword (Read-Host -AsSecureString)`, `wix build`,
  then `./installer/sign.ps1 -Target Msi ...`. A code-signing certificate (from a public CA) is
  required; `*.pfx` files are git-ignored.

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
- **3D surround**, chosen automatically for your output device (or forced
  manually). All of it runs inside Equalizer APO, chosen to be as light on the
  CPU as it allows:
  - **Speakers**: stereo widening as a plain 2x2 gain matrix (no filters, no
    delay, near-zero CPU). The master preamp is trimmed by up to ~5 dB at full
    strength so out-of-phase material can't clip, so widening sounds slightly
    quieter. The trim only applies while Auto-gain protection is on.
  - **Headphones**: crossfeed (each ear also hears the opposite channel,
    low-passed at 700 Hz and delayed 0.27 ms), which keeps mono level
    unchanged. Cost: two biquads and a short delay.
  - **Headphones + HRTF file** (optional): pick a short binaural `.wav` and
    Equalizer APO convolves with it for true 3D. This is the only mode with a
    noticeable CPU cost; the Amount slider does not affect it.
  - **Auto** reads the default output's form factor (speakers / headphones /
    headset) via Windows Core Audio and switches when you plug in a jack or
    change device. It is event-driven, with no polling. Off / Speakers /
    Headphones override it.
- **System volume slider** for the current playback device, kept in sync
  with the Windows volume.
- **Clipping-protection limiter** ("Auto-gain protection"): since Equalizer
  APO has no built-in lookahead/brickwall limiter, AudioTune Pro estimates
  the worst-case constructive peak your curve could produce (accounting for
  adjacent-band overlap) and automatically trims the master preamp so the
  result stays under your chosen ceiling. This is a static, pre-computed
  safeguard, not real-time dynamics processing — see `AutoGainLimiter.cs`
  for the exact math.
- **Live output level meter** via WASAPI loopback capture (visual only —
  negligible CPU cost). Full-width bar on a -18..+6 dBFS scale (ticks every
  6 dB, live dB readout): teal only at the bottom, amber through the middle,
  red from 0 dBFS up, with the last 6 dB reserved for over-range. It falls off
  smoothly, stops while the window is hidden or minimized, and follows the
  default output device.
- **Compact, resizable layout**: the sidebar (master switch, presets, volume,
  tone, 3D surround, limiter) fits at the default window size and scrolls in
  smaller windows; "Start with Windows" and "Show level meter" live in the footer.
- **One instance only**: launching a second copy just brings the running one
  to the front, so two copies never fight over the Equalizer APO config.
- **PeakIndicator badge**: a Safe / Approaching ceiling / Near clipping status
  next to the estimated peak reading, driven by the same gain math as the EQ
  faders. When Auto-gain protection is off, it correctly reads against true
  0 dBFS clipping rather than the (in that state, inert) configured ceiling.
- **Remembers your edits**: the live EQ state is saved as you change it and
  re-applied at launch, so the sliders always match what you hear. An
  "edited since saved" note appears until you Save As.
- **System tray**: closing the window keeps AudioTune Pro (and your EQ)
  running in the background (a one-time balloon says so); use the tray menu
  to reopen or exit. Minimizing works normally.
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

## Design system

The UI's visual design — colors, type, spacing, per-control behavior — is
driven by tokens checked into `design-system/`: `README.md` is the brand
book, `tokens.json` the values, `components.md` the per-control guidelines.
`design-system/AudioTunePro.Tokens.xaml` is a WPF `ResourceDictionary`
generated from `tokens.json` and linked directly into
`AudioTunePro.App.csproj` (not copied) as the single source of truth,
merged in `App.xaml` ahead of `Views/Styles.xaml`.

In high-contrast mode the token brushes are remapped to system colors at launch. Notable pieces built on those tokens: a custom `WindowChrome`-based title bar
(the native title bar is fully replaced), EQ faders and the Limiter's
PeakIndicator badge that derive their color live from gain relative to the
limiter ceiling (`SignalState`/`SignalStateCalculator`,
`src/AudioTunePro.App/ViewModels/`), and a global 2px keyboard focus ring
applied via `SystemParameters.FocusVisualStyleKey`.

## Security notes

- The app makes **no network connections** and listens on no ports; it only opens
  the Equalizer APO download page in your browser when you click the button.
- It runs as a normal user (never elevated) and reads/writes only its own
  settings under `%AppData%\AudioTunePro` and Equalizer APO's include file.
- **HRTF paths are validated**: because Equalizer APO's config is read by
  Windows' audio service, only a plain, fully-qualified local `.wav` path is ever
  written into it. Network/UNC paths, relative paths, alternate streams,
  wildcards and anything containing a line break are rejected (tests in
  `SecurityHardeningTests.cs`).
- Settings and preset files over 2 MB are ignored, and loaded values are clamped
  to their valid ranges.
- Dependencies are pinned by lock files and audited for known vulnerabilities in CI
  (see "Reproducible, verifiable builds"). CI actions are pinned to commit hashes.
- Known limits: Equalizer APO's own `config` folder is writable by normal users
  by design, so keep untrusted software off the machine; the installer is
  unsigned until you add a code-signing certificate.

## Project layout

```
design-system/
  README.md                Brand book: color/type/spacing rules per control.
  tokens.json               Source-of-truth token values.
  components.md              Per-control implementation notes.
  AudioTunePro.Tokens.xaml    Generated WPF ResourceDictionary, linked into the app.
src/
  AudioTunePro.Core/        Platform-agnostic: EQ + surround model, Equalizer APO config
                             generator, auto-gain limiter math, built-in presets.
  AudioTunePro.Core.Tests/  xUnit tests: EQ/limiter/config math, signal states,
                             persistence and security hardening.
  AudioTunePro.App/         WPF UI, Equalizer APO detection/install glue,
                             tray icon, WASAPI level meter, output-device detection
                             and volume, settings storage.
                             Assets/icon.ico is the app/shortcut icon.
tools/
  build_hrtf.py             Builds the optional KEMAR HRTF impulse response.
installer/
  AudioTunePro.wxs          WiX v5 source for the MSI installer (see above).
  License.rtf               Shown on the installer's license page.
  sign.ps1                  Authenticode-signs the app binaries and the MSI.
.github/workflows/
  ci.yml                    Build + tests + vulnerability audit (locked restore).
  release.yml               Builds, optionally signs, and uploads the installer.
Directory.Build.props       Turns on NuGet lock files and package auditing.
*/packages.lock.json        Exact pinned package versions (commit these).
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
- Surround config lines (notably the `AUX0`/`AUX1` crossfeed channels and the
  4-channel HRTF order) follow Equalizer APO's documented syntax but were not
  verified against every APO version; Equalizer APO's Editor flags syntax errors.
- Auto detection relies on the driver reporting the endpoint form factor;
  if a device is misclassified, choose Speakers or Headphones manually.
- The level meter reflects the *pre-EQ* system mix (WASAPI loopback on the
  default render device), not Equalizer APO's post-processing output.
