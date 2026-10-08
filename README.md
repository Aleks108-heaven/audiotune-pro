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

Run the command **from the repo root** (`MSB1009: Project file does not exist` means the terminal was in
another folder), and publish into an empty `CI-artefact` (delete the old one first): the installer packs
everything in that folder. Release builds embed their debug symbols, so there are no loose `.pdb` files.

**Smart App Control / Application Control:** these Windows features refuse unsigned programs that have no
reputation, and the decision can change from one run to the next. An unsigned build can therefore start
fine on one launch and be blocked on another (also for `dotnet test` on a developer PC). The app now shows
a message and writes `%AppData%\AudioTunePro\logs\startup-error.log` instead of vanishing, but the real fix
is a signed release (see "Signed installer" below). Do not turn Smart App Control off to work around it: Windows
cannot switch it back on without a reset. Run the tests in CI or on a machine where it is not enforcing.

### HRTF impulse response (optional)

`tools/build_hrtf.py` builds a 4-channel "true stereo" impulse response
(virtual speakers at ±30°) from the free
[MIT KEMAR](http://sound.media.mit.edu/resources/KEMAR.html) compact HRTF set:

```powershell
# download and unzip compact.zip from the MIT page, then:
python tools/build_hrtf.py <path-to-unzipped-compact> "$env:APPDATA\AudioTunePro\hrtf\mit-kemar-30deg.wav"
```

The app no longer has a control for choosing an HRTF file (the **HRTF file…** button was removed), and it
ignores any HRTF path left in older settings; the config generator and this script remain for anyone who wants to
wire a file in by hand. The generated `.wav` is deliberately **not** committed or bundled in the installer: the MIT
download ships without a licence file, and its terms may be limited to
educational/research use, so check them before redistributing it. The script
needs `numpy`. Files with 2 channels are convolved per ear; 4 channels are a
true-stereo IR (L→L, L→R, R→L, R→R).

### Installer

`release/AudioTunePro-Setup.msi` is a real installer, built with the
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

To rebuild it after code changes (regenerate `CI-artefact/` first, from empty, see
above, then):

```powershell
dotnet tool install --global wix --version 5.0.2   # one-time; skip if already installed
wix extension add -g WixToolset.UI.wixext/5.0.2    # one-time
New-Item -ItemType Directory -Force release | Out-Null
cd installer
wix build AudioTunePro.wxs -ext WixToolset.UI.wixext -arch x64 -o ../release/AudioTunePro-Setup.msi
```

The MSI is written to `release/` (git-ignored), not `CI-artefact/`, so a rebuild can never pack the previous
MSI inside the new one. The installer version in `AudioTunePro.wxs` must match `<Version>` in `Directory.Build.props`.
Uninstalling does not remove the per-user "Start with Windows" entry (an installer running per machine cannot
safely edit other users' registry hives); it then points at a missing file and is ignored. Turn the toggle off
in the app before uninstalling, or remove it under Settings → Apps → Startup.

### Reproducible, verifiable builds

- **Locked packages:** every NuGet dependency (including transitive ones) is pinned to the exact
  version and content hash in each project's `packages.lock.json` (enabled in
  `Directory.Build.props`). CI restores in locked mode and fails if anything differs. To update a
  package on purpose: change the version, run `dotnet restore AudioTunePro.slnx --force-evaluate`,
  and commit the changed lock files.
- **Signed installer:** `.github/workflows/release.yml` (runs on a `v*` tag or manually) builds the
  MSI, signs the app binaries and the MSI with `installer/sign.ps1` when the repository secrets
  `SIGNING_CERT_PFX_BASE64` and `SIGNING_CERT_PASSWORD` exist, and uploads the installer with a
  `SHA256SUMS.txt`. The secrets are exposed only to the two signing steps (never to restore, test, publish or
  the WiX install), and signing only happens for tags and `master`. Without the secrets it still builds,
  unsigned, and the run prints a warning: an unsigned build can be blocked by Smart App Control. You can also sign locally:
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
  - **Headphones + HRTF file**: no longer selectable in the app (see "HRTF impulse response" above).
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
  for the exact math. The ceiling is headroom, not only a boost limiter: any
  ceiling below 0 dB lowers the output even with a flat curve (the default
  -0.3 dB is a small safety cushion). The Limiter section therefore shows
  **Auto-gain trim**, the level change actually being applied (limiter plus
  speaker-widening trim), which always matches the `Preamp:` line written to
  Equalizer APO.
- **System-mix level meter** ("SYSTEM MIX (PRE-EQ)") via WASAPI loopback capture (visual only —
  negligible CPU cost). It shows the mix Windows sends to Equalizer APO, so it does **not** show clipping the EQ
  itself adds. Full-width bar on a -18..+6 dBFS scale (ticks every
  6 dB, live dB readout): teal only at the bottom, amber through the middle,
  red from 0 dBFS up, with the last 6 dB reserved for over-range. It falls off
  smoothly, stops while the window is hidden or minimized, and follows the
  default output device.
- **Compact, resizable layout**: the sidebar (master switch, presets, volume,
  tone, 3D surround, limiter) fits at the default window size and scrolls in
  smaller windows; "Start with Windows" and "Show level meter" live in the footer.
  The window (including its minimum size) is clamped to the screen's work area,
  so it is never opened taller or wider than the display.
- **Keyboard-friendly faders**: each fader is a focusable slider with an
  accessible name ("31 hertz band gain"); arrow keys nudge it, Home/End jump to
  the extremes, and **Delete** (or a double-click) resets it to 0 dB.
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
  to reopen or exit. Minimizing works normally. When Windows logs off or shuts
  down, the app closes for real and first saves any edit still waiting on its
  150 ms apply/save debounce.
- **Start with Windows** toggle.

## Performance

AudioTune Pro itself does no audio DSP — Equalizer APO does that inside
Windows' own audio engine process (`audiodg.exe`), entirely separate from
this app. What's measured below is only AudioTune Pro's own UI process.

Measured on the built `CI-artefact\AudioTunePro.exe` (Release,
framework-dependent, version 1.2.1), idle with the level meter running (its default state).
Version 1.2.2 adds four background worker threads (apply, save, volume, meter) that sleep until there is work;
the figures below have not been re-measured for it:

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
- **HRTF files are checked, not just their paths:** before a path reaches the config, the file must be a real
  (not linked) RIFF/WAVE file of at most 4 MB, 65,536 samples and 16 channels, so the audio service is never pointed
  at something that could stall it. This is re-checked on every apply. It narrows the risk but cannot remove it:
  the file lives in a folder the user (and any program running as the user) can write, and could be swapped after
  the check.
- **The rest of your Equalizer APO config is read (never changed):** `Preamp:` lines in `config.txt` (and files it
  includes from the same folder) are added to the limiter's estimate, and a `VSTPlugin:` line (third-party code loaded
  into the Windows audio service) is called out in the Limiter section.
- **Diagnostics:** errors are written to `%AppData%\AudioTunePro\logs\app.log` (capped at 256 KB, rolls to
  `app.log.1`; startup failures go to `startup-error.log`). Logs contain exception text and file paths, no audio or
  personal data, and never leave the PC. Unhandled errors on the UI thread are logged and the app keeps running.
- Settings and preset files over 2 MB are ignored, and loaded values are clamped
  to their valid ranges.
- Dependencies are pinned by lock files and audited for known vulnerabilities in CI
  (see "Reproducible, verifiable builds"). CI actions are pinned to commit hashes.
- Known limits: Equalizer APO's own `config` folder is writable by normal users
  by design (verified: `BUILTIN\Users` has Full Control) while the Windows audio service reads it, so any program
  running as you can change what that service loads. That is Equalizer APO's design, not something this app can fix;
  keep untrusted software off the machine. The installer is unsigned until you add a code-signing certificate.

## Quality review (QA and design)

A review of the app against a QA verification checklist (functional, security,
language/framework, dependencies, accessibility) and a design review against
`design-system/`, followed by fixes and a live test. Evidence labels follow the
checklist: **Confirmed** (reproduced), **Suspected** (from code reading only),
**Not tested**.

**Status: PARTIALLY VERIFIED (pass with findings).** No confirmed defects in the
logic; some behaviours need a real machine or real Windows sessions.

### What was verified

- Release build: 0 warnings, 0 errors. Unit tests: **92 passing** (64 before the
  review). No known-vulnerable NuGet packages, including transitive ones.
- CI: actions pinned to commit hashes, `contents: read`, locked restore,
  vulnerability audit step.
- Live run of the built app, driven through Windows UI Automation (with
  screenshots at the default and minimum window sizes):
  - Delete on a focused fader resets it to 0 dB; other keys behave normally.
  - Auto-gain trim readout: flat curve with a -6 dB ceiling reads -6.0 dB and the
    file on disk says `Preamp: -6.00 dB`; with protection off it reads 0.0; with
    Surround on Auto the speaker-widening trim is included and still matches the file.
  - Closing the window hides to the tray and the process keeps running.
  - Simulated session end (`WM_QUERYENDSESSION`/`WM_ENDSESSION` sent to the test
    process only): the process exits, and an edit made just before exit is saved
    (the original code lost it in 4 of 4 runs; the fixed code kept it in 4 of 4).

### Fixed during the review

| Area | Change |
|---|---|
| Logoff/shutdown | `App.OnSessionEnding` lets the main window close for real and flushes the pending save. The original suspicion that shutdown was *blocked* was **not reproduced**; the confirmed benefit is the flushed edit. |
| Limiter transparency | New **Auto-gain trim** readout (`EqualizerApoConfigGenerator.TotalTrimDb`), because a ceiling below 0 dB quietly lowered a flat curve while the badge read Safe. |
| Small screens | `MinWidth`/`MinHeight` are clamped to the work area too, and fader columns are tighter so "+12.0" fits at the minimum width. |
| Keyboard access | Delete resets a focused fader (previously double-click only). |
| Visual bug | Disabled sliders (Surround Amount; faders when the equalizer is bypassed) showed a bright white block; track buttons now have a transparent template. |
| Layout | The new trim caption briefly pushed the PeakIndicator badge below the sidebar fold; the Limiter readout is now a two-row grid with the badge pinned right. |
| Testability | Include-line and meter logic moved into `Core` (`ApoConfigWriter`, `LevelMeterMath`) with 28 new tests. |

### Audit follow-up (2026-10-06, v1.2.2)

A second, read-only audit (security checklist, Windows host checks, design review) found ten issues. This is what
changed and what is still open. "Fixed" means changed in code and covered by a build and the 146 unit tests (up from
92); nothing here was verified by running the full UI, so please exercise the window once after updating.

| ID | Finding | Status |
|----|---------|--------|
| F-01 | Unsigned binaries blocked by Smart App Control; the installed app crashed 5 times at startup (10/5) with no message | **Partly fixed.** Startup failures now show a message and write `startup-error.log`; the release run warns when unsigned. **Still open:** the binaries stay unsigned until a code-signing certificate and the two `SIGNING_CERT_*` secrets exist. That needs a purchase and cannot be done in the repo. |
| F-02 | Auto-gain off, plus `Preamp: 2.7 dB` in `config.txt` (about +14 dB at 31 Hz on the audited PC) | **Fixed in the app:** the limiter counts the rest of `config.txt`'s Preamp lines, and the Limiter section states them. Your own `config.txt` and presets were deliberately not edited; whether Auto-gain stays off is your choice. |
| F-03 | Recurring "stopped interacting with Windows" hangs (6 in 4 days) | **Mitigated, not proven fixed.** Every disk, registry and Windows-audio call (config and settings writes, volume, meter, device discovery) now runs on background workers, and shutdown flushes with a 3 s cap. The hang itself was never reproduced and no dump exists. Watch Event Viewer (Application, Event 1002) and `app.log`; to capture evidence, enable Windows Error Reporting LocalDumps for `AudioTunePro.exe`. |
| F-04 | No error handling or logging; every build reported version 1.0.0.0 | **Fixed.** Global handlers + `app.log`; version 1.2.2 in every build; symbols embedded so stack traces keep line numbers. |
| F-05 | Release workflow exposed the signing key to every step | **Fixed.** Secrets reach only the two signing steps; signing only on tags and `master`. |
| F-06 | Equalizer APO's config folder is writable by all users and read by the audio service; HRTF files unchecked | **Mitigated.** HRTF files are validated (see Security notes), `VSTPlugin:` lines are flagged. The folder permission is Equalizer APO's design and remains. |
| F-07 | Meter labelled "OUTPUT LEVEL" but reads before the EQ | **Fixed.** Now "SYSTEM MIX (PRE-EQ)", with an explanatory tooltip; design docs updated. |
| F-08 | Installer: stale-MSI embedding, loose PDBs, autostart re-pointing, uninstall leftover | **Fixed except the leftover:** MSI now builds into `release/`, no PDBs, autostart is repaired only when stale. Uninstall still leaves the per-user "Start with Windows" entry (see Installer). |
| F-09 | Design-system gaps | **Mostly fixed:** `track-guide` raised to #66718A (3.3:1 on raised surfaces); text-input and dropdown borders use it; scale labels use the same minus as the values; meter value is exposed to screen readers (not tested with a real screen reader); the "one teal" rule is reconciled in the design docs. **Accepted as designed:** `text-disabled` (2.2:1) and `accent-muted` (2.3:1) are only used for disabled controls, which WCAG exempts, and `border-panel` (1.2:1) is decoration on panels that need no border to be identified. |
| F-10 | Tests and CI | **Partly fixed:** 54 new tests cover the new Core code. **Still open:** `MainViewModel` and the WPF project have no unit tests (they would need injected services), CI still runs only the Core tests, and xUnit v2 / the three test-support packages were not upgraded (a migration to xUnit v3 should be tested on its own). |

Other open items from the first review, unchanged: a 3 kHz test tone supports the "pre-EQ" meter description, but
one inconsistency with the browser's own session meter was never explained; not tested: DPI scales other than the
one used for the live run, screen readers, the installer, whether the app can write to Equalizer APO's config
folder as a standard user on every install, whether the audio service can read an HRTF file stored in a user folder,
and how Equalizer APO treats `#` inside a path.
## Project layout

```
design-system/
  README.md                Brand book: color/type/spacing rules per control.
  tokens.json               Source-of-truth token values.
  components.md              Per-control implementation notes.
  AudioTunePro.Tokens.xaml    Generated WPF ResourceDictionary, linked into the app.
src/
  AudioTunePro.Core/        Platform-agnostic: EQ + surround model, Equalizer APO config
                             generator and include-file writer, auto-gain limiter math,
                             level-meter math, built-in presets.
  AudioTunePro.Core.Tests/  xUnit tests: EQ/limiter/config math, config.txt include
                             handling, meter ballistics, signal states, persistence
                             and security hardening.
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
  default render device), not Equalizer APO's post-processing output, so it cannot show clipping the EQ adds.
  Keep Auto-gain protection on for that; the limiter now includes `Preamp:` lines from the rest of `config.txt`.
