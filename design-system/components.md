# AudioTune Pro — Component Guidelines

Reference notes per component, exported from the AudioTune Pro design system artifact. Pair with tokens.json for exact values and README.md for the brand book.

## TitleBar

The custom window chrome that replaces the native Windows title bar. Left side: a 14px monoline app glyph in `accent-teal` plus the app name set in 12px/600 `text-primary`; right side: minimize, maximize and close, each a 46px-wide hit target. Give the whole bar a 1px `divider` bottom border on `app-bg-top` so it reads as a strip of chrome, not part of the content below.

- Icon and title never wrap or truncate with an ellipsis at the window's minimum width -- shorten the subtitle before the wordmark. (The desktop app's minimum is 1040px wide, so the +12.0 readouts of the 15-band layout and the rotated readouts of the 31-band layout fit; 360px only applies to the design-system web previews.)
- Caption buttons behave like the native ones: not tab stops (`Focusable=False`); keyboard equivalents are Win+Up/Down and Alt+F4. Every other control shows the focus ring.
- Minimize and maximize hover to `surface-panel` with `text-primary`; close is the one destructive control in the bar and hovers to a filled `signal-danger` with `ink-on-accent` glyph -- no other title-bar button ever takes a signal color.
- Do not add extra icons, a search field or a menu bar here; the title bar is chrome only.

## Panel

The bordered surface every group of controls sits on. `surface-panel` fill, 1px `border-panel`, `radius-lg` corners and `elevation-panel` shadow -- the combination is what makes a panel read as a separate piece of gear rather than a tinted region of the same background.

- Use the plain Panel for a single content region (the main EQ canvas); use the "grouped" variant -- a panel containing its own bordered `surface-raised` field and a primary action -- for a compact unit like the Preset selector, where a dropdown and a Save As button belong visually together.
- Panel padding is `space-4` (16px) minimum; never let content touch the border.
- Do not stack two panel borders directly against each other with no gap -- leave at least `space-4` between adjacent panels so each one's elevation shadow can read.

## Button

Every clickable action in AudioTune Pro, in three weights. Use exactly one primary button per panel -- it is the only button filled with `accent-teal` and `ink-on-accent` text (Save As; the Banner's Get Equalizer APO is the primary of its own panel). Secondary buttons (Get Equalizer APO, Reset) fill with `surface-raised`, a `border-panel` border and `text-primary` label; they carry every action that is not the panel's one primary commitment.

- Delete Preset is a secondary button at rest -- it does not sit in a permanently red "danger" state -- and only takes `signal-danger` on hover or keyboard focus, and is disabled for built-in presets, since a destructive action should announce itself at the moment of intent, not sit alarming at rest.
- Disabled buttons drop straight to `text-disabled` on `surface-raised`; never simulate disabled with opacity.
- `radius-md` corners, `button-label` type, `space-5` horizontal padding. Buttons never wrap their label to a second line -- shorten the label instead.

## Toggle

A binary switch for feature rows in the sidebar (Equalizer enabled, Start with Windows, Show level meter, Auto-gain protection). On is a fully filled `toggle-track-on` (`accent-teal`) track with the knob at the right; off is a `toggle-track-off` track with the knob at the left -- the two states must never share a track color, since gray-on-gray gives no on/off contrast at a glance.

- Label color follows state: `text-primary` when the row is on or otherwise active, `text-secondary` when off but available, `text-disabled` when the row itself is unavailable (e.g. this toggle depends on a feature that isn't installed).
- Track is `radius-full`; knob is a plain filled circle, 2px inset from the track edge, no shadow.
- Always pair the toggle with its label to its left, switch at the right edge of the row -- never a bare switch with no adjacent text.
- The off track carries a 1px `track-guide` outline so the switch reaches 3:1 against the panel; the on track is fully `toggle-track-on`.

## Slider

The horizontal Tone control -- Bass, Treble, Preamp -- pairing a label, a track and a live monospace readout. Track fill and handle are `accent-teal` while the row is active; both drop to `accent-muted` together (never one and not the other) when the row is disabled, with the value text falling to `text-disabled`.

- Track is a thin 4px `track-fill` rail; the filled portion and the handle are always the same color, so the eye reads one continuous mark rather than two.
- Handle is `radius-sm` (a small rounded square), not a circle -- this distinguishes a Tone slider from a Toggle knob at a glance.
- The value column is fixed-width and right-aligned in `value-lg`, so a column of dB values lines up regardless of sign or digit count.
- Give the handle a click/touch target at least 24px tall even though its visible size is 16px.

## Fader

One band of the graphic EQ (10, 15 or 31 bands, see Density below): a thin vertical track, a filled handle with a soft glow beneath it, the gain value above in monospace and the center frequency below. Color is never fixed -- it is a live function of the band's gain relative to the limiter ceiling: `signal-safe` while there is comfortable headroom, `signal-warn` once the band is within the limiter's caution margin of the ceiling, `signal-danger` once it is within the final margin or clipping. The dB value's text color always matches the handle's color, so the number and the mark agree.

- Track is a hairline 2px `track-fill` rule running the full height of the EQ panel, so the bands read as one coherent grid, not as separate widgets.
- The handle is a short filled rounded bar (`radius-sm`), not a circle -- a circle reads as a knob, not a fader cap. A soft blurred glow in the same color sits behind it to suggest energy, at low opacity so it never competes with neighboring bands.
- The rail and a 0 dB tick are `track-guide` (visible, not `track-fill`), with a +12 / 0 / -12 dB scale at the left of the row. Double-click a fader to reset it to 0 dB. While the equalizer is bypassed the faders drop to `accent-muted` with `text-disabled` values and no glow.
- State = the band's boost measured against the limiter's boost budget (`SignalStateCalculator.BoostLimitDb`): 0 dB is always safe; warn within 3 dB and danger within 1 dB of the budget. With Auto-gain on the budget is 12 dB plus the (negative) ceiling; with it off it is a fixed 6 dB.
- Never rely on color alone to signal risk: the dB value is always visible above the handle, and the Limiter section's `PeakIndicator` gives the same information as text for a band at risk.
- Frequency labels are fixed and never move with the handle; they sit in a single row along the bottom of the EQ panel (31, 62, 125, 250, 500, 1k, 2k, 4k, 8k, 16k at 10 bands; 25, 40, 63 ... 16k at 15; 20, 25, 31.5 ... 20k at 31). A label prints the frequency without its unit, with no more precision than it needs (31.5, 160, 1.25k, 12.5k), always with a "." decimal.

**Density.** The EQ panel is about 600px wide at the default window size, so the readout size follows the band count; the value row above and the frequency row below are fixed-height at every density, so the dB scale at the left lines up with the fader travel.

| Bands | Column | Cap (width x height) | dB value | Frequency label |
| --- | --- | --- | --- | --- |
| 10 (octave) | ~59px | 28 x 14 | `value-sm`, horizontal | `freq-label`, horizontal |
| 15 (2/3 octave) | ~39px | 22 x 14 | `value-xs`, horizontal | `freq-label`, horizontal |
| 31 (1/3 octave) | ~19px | 14 x 14 | `value-xs`, rotated -90 degrees (reads upward) | `freq-label`, rotated -90 degrees |

- At 31 bands every band still shows its own number: the readouts stand on end rather than being hidden or limited to the hovered fader, so "the dB value is always visible above the handle" holds at every density. A tooltip names the band and its gain ("2.5 kHz: +3.5 dB") for anyone who wants a larger read.
- The glow behind the cap is at 50% opacity at 10 and 15 bands and drops to 20% at 31, where neighboring caps are a few pixels apart and a stronger glow would blur them into one ribbon.
- The 0 dB tick reaches 4px past the cap on both sides at every density. The focus ring is the same 2px `focus-ring` outline, drawn around the slider's own (narrower) bounds.
- The accessible name states the frequency in words ("250 hertz band gain", "2.5 kilohertz band gain"); the gain is the slider's value, so a screen reader announces it as it changes.

## ResponseCurve

One neutral line drawn behind the faders that shows the combined response of everything on the EQ panel: the graphic curve plus Bass, Treble, Clarity and Low-cut. It is what makes the extra bands and the clarity controls readable at a glance -- with only the faders in play it runs through the center of every cap, and wherever it leaves them you are seeing what the shelves, Clarity or Low-cut add. A legend at the right of the hint row ("Total response", a short sample of the line) names it.

- Same +12 / 0 / -12 dB scale as the faders, and the same rows as a band column, so the dB scale at the left serves both. Bands sit in equal columns, so the line is sampled in equal steps across the row; it passes through every cap's center and continues half a column past the first and last band, holding the end values flat there (the way Equalizer APO treats a GraphicEQ line).
- `text-primary`, 1.5px, round joins. Neutral on purpose: the faders and the meter own the safe / warn / danger colors, teal is reserved for the one primary state, and nothing here is a gain-bearing control. No fill under the line and no point markers. While the equalizer is bypassed the line is flat and `text-disabled`, and so is the legend sample.
- Drawn beneath the sliders, so caps and focus rings stay on top. It takes no input (hit testing off), has no focus and no automation peer: every value it draws is available from the faders and sliders themselves.
- Values beyond +12 / -12 dB run along the top or bottom edge of the rail instead of leaving the panel (a Low-cut at 80 Hz takes the lowest bands below -12 dB).
- It states what Equalizer APO is told, not a guess: it is computed from the same filter list that is written to the config file, for 48 kHz audio. It leaves out Preamp, the limiter's trim and 3D surround, which change level or stereo image rather than tone; the legend's tooltip says so.

## BandSelector

The BANDS choice at the right of the EQ panel's header row: a `section-label` "BANDS" followed by three RadioButtons (10, 15, 31), the same RadioButton used for 3D Surround -- one choice, and its teal dot is the only teal in the control. The counts are numbers, so they are set in the `mono` family. The panel's `section-label` title follows the choice ("15-BAND GRAPHIC EQ").

- Switching keeps the curve (by interpolation over log-frequency); an unedited preset is rebuilt from its own definition, so flipping back and forth never drifts. Going to fewer bands smooths fine detail: the tooltip says so.
- The choice is a user setting, kept with the rest of the app's settings, not part of a preset: presets keep the resolution they were saved with and are shown on whichever layout is selected.

## Clarity

Two HSlider rows side by side under the faders, beneath a `section-label` "CLARITY": **Amount** (0-100%, `value-lg` readout) and **Low-cut** (30-150 Hz, with the leftmost stop reading "Off"). They live with the faders rather than in the sidebar because they shape the same curve, and because the sidebar's Limiter section must stay on screen at the default window size.

- Both follow "Equalizer enabled": they drop to `accent-muted` / `text-disabled` together with the faders when it is off.
- Amount scales three fixed filters (-2.5 dB at 300 Hz, +2.0 dB at 3.2 kHz, +1.5 dB high shelf at 10 kHz at 100%); the limiter reserves headroom for the boost, so the PeakIndicator and the Auto-gain trim readout account for it. Low-cut is a high-pass: it adds no gain and needs no headroom.
- Reset turns both off; a preset stores both.

## LevelMeter

The horizontal system-mix level meter beneath the EQ panel. It measures the mix Windows sends to Equalizer APO, i.e. *before* the equalizer, so it never shows clipping the EQ adds -- the label says so ("PRE-EQ") and the Limiter section covers that risk. The fill is a single gradient across the whole track -- `signal-safe` through most of its length, `signal-warn` near the top, `signal-danger` at the very end -- so the same three-color gain language used on the faders reads continuously across the full output range, rather than as a flat single-color bar.

- Track is `track-fill` at 6px tall, `radius-full`-style rounded ends. The meter spans the full width of the EQ panel (10px tall) and reveals one fixed gradient from the left: teal only to -14 dBFS, amber from -10 to -4, red from 0 up. The scale is -18..+6 dBFS: the last 6 dB (0 to +6) is over-range headroom that only lights when the mix goes over full scale. Instant attack, ~1.1 s fall, a mono numeric readout (e.g. -3.0 dB) at the right of the label, and ticks every 6 dB (-18, -12, -6, 0, +6) below. The whole meter is hidden when "Show level meter" is off.
- The meter's current level is the fill's width, not a moving playhead; it should feel like a physical VU strip, not a progress bar.
- Pair with the `section-label` "SYSTEM MIX (PRE-EQ)" directly above it, left-aligned to the meter.

## Banner

A system-level notice pinned above the main content -- used for an unmet dependency (Equalizer APO not installed), never for routine confirmation. `warn-bg` fill with a `warn-border` edge keeps it visually distinct from the panels beneath it without borrowing the danger color, since a missing dependency is a caution, not a failure.

- Always state the fact first, the fix second, in one sentence each: "X not found. Install it once, then Y."
- The leading glyph is `signal-warn`; pair it with an action button on the right rather than a bare link, so the fix is one click away.
- Banner sits full-width at the top of the content area, `radius-lg` corners, `space-4` internal padding. If the product later needs a dismiss control, put it as a plain `text-secondary` "x" at the far right, never replacing the action button.

## PeakIndicator

A pill badge that states clipping risk in words, so the Limiter section never relies on the estimated-peak number alone. Each badge pairs a small filled dot with a label in the matching signal color, on a 12%-opacity tint of that same color -- Safe (`signal-safe`), Approaching ceiling (`signal-warn`), Near clipping (`signal-danger`).

- Show exactly one badge at a time in the Limiter section, driven by the same gain math that colors the faders and the level meter -- the three signal states must always agree with each other across the whole window.
- The "Estimated peak" numeric line stays in `value-lg`/mono beneath the badge; the badge is the at-a-glance read, the number is the precise one.
- Never use the dot alone without the text label -- the word is what makes this indicator colorblind-safe.

