# AudioTune Pro — design system

This project has a design system checked into `design-system/` (`README.md` = the brand book,
`tokens.json` = the token values, `components.md` = per-control guidelines). Read `design-system/README.md`
first before writing or reviewing any UI code, then pull exact values from `tokens.json`.

The full system — with live, rendered previews of every control (TitleBar, Panel, Button, Toggle,
Slider, Fader, LevelMeter, Banner, PeakIndicator) and a browsable token/cover page — lives at:
https://claude.ai/artifact/1uVTBDt2MGfzU4u1jDGzjL

Ground rules when implementing UI:
- `accent-teal` is the one primary/active color per view (primary button, active preset, enabled
  toggle, focus ring) — never use it as decoration.
- The 10-band EQ faders and the output-level meter derive their fill color live from gain, using
  `signal-safe` / `signal-warn` / `signal-danger` — never a fixed color per band.
- Numeric values (dB, Hz) are always set in the mono family (`type.families.mono` in tokens.json),
  never the sans UI family.
- Disabled controls drop straight to `text-disabled` / `accent-muted`; never simulate disabled with
  opacity alone.
- Every interactive control needs a visible `focus-ring` outline for keyboard use.

`tokens.json` follows the shape documented at the top of the file: `color.tokens` (a flat list with
`name`/`value`/`usage`), `type.groups[].styles`, `spacing.tokens`, `radius.tokens`, `shadow.tokens`.
Since this is a WPF/WinUI app, not CSS, treat `tokens.json` as the source of truth for values and
translate colors/spacing/radii into `ResourceDictionary` entries (a `design-system/AudioTunePro.Tokens.xaml`
resource dictionary can be generated from this file on request — ask Claude for it).
