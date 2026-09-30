# AudioTune Pro — Component Guidelines

Reference notes per component, exported from the AudioTune Pro design system artifact. Pair with tokens.json for exact values and README.md for the brand book.

## TitleBar

The custom window chrome that replaces the native Windows title bar. Left side: a 14px monoline app glyph in `accent-teal` plus the app name set in 12px/600 `text-primary`; right side: minimize, maximize and close, each a 46px-wide hit target. Give the whole bar a 1px `divider` bottom border on `app-bg-top` so it reads as a strip of chrome, not part of the content below.

- Icon and title never wrap or truncate with an ellipsis at the window's minimum width -- shorten the subtitle before the wordmark. (The desktop app's minimum is 980px wide, so the EQ's ten +12.0 readouts fit; 360px only applies to the design-system web previews.)
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

One band of the 10-band graphic EQ: a thin vertical track, a filled handle with a soft glow beneath it, the gain value above in monospace and the center frequency below. Color is never fixed -- it is a live function of the band's gain relative to the limiter ceiling: `signal-safe` while there is comfortable headroom, `signal-warn` once the band is within the limiter's caution margin of the ceiling, `signal-danger` once it is within the final margin or clipping. The dB value's text color always matches the handle's color, so the number and the mark agree.

- Track is a hairline 2px `track-fill` rule running the full height of the EQ panel, so ten bands read as one coherent grid, not ten separate widgets.
- The handle is a short filled rounded bar (`radius-sm`), not a circle -- a circle reads as a knob, not a fader cap. A soft blurred glow in the same color sits behind it to suggest energy, at low opacity so it never competes with neighboring bands.
- The rail and a 0 dB tick are `track-guide` (visible, not `track-fill`), with a +12 / 0 / -12 dB scale at the left of the row. Double-click a fader to reset it to 0 dB. While the equalizer is bypassed the faders drop to `accent-muted` with `text-disabled` values and no glow.
- State = the band's boost measured against the limiter's boost budget (`SignalStateCalculator.BoostLimitDb`): 0 dB is always safe; warn within 3 dB and danger within 1 dB of the budget. With Auto-gain on the budget is 12 dB plus the (negative) ceiling; with it off it is a fixed 6 dB.
- Never rely on color alone to signal risk: the dB value is always visible above the handle, and the Limiter section's `PeakIndicator` gives the same information as text for a band at risk.
- Frequency labels are fixed and never move with the handle; they sit in a single row along the bottom of the EQ panel (31, 62, 125, 250, 500, 1k, 2k, 4k, 8k, 16k).

## LevelMeter

The horizontal output-level meter beneath the EQ panel. The fill is a single gradient across the whole track -- `signal-safe` through most of its length, `signal-warn` near the top, `signal-danger` at the very end -- so the same three-color gain language used on the faders reads continuously across the full output range, rather than as a flat single-color bar.

- Track is `track-fill` at 6px tall, `radius-full`-style rounded ends. The meter spans the full width of the EQ panel (10px tall) and reveals one fixed gradient from the left: teal only to -14 dBFS, amber from -10 to -4, red from 0 up. The scale is -18..+6 dBFS: the last 6 dB (0 to +6) is over-range headroom that only lights when the mix goes over full scale. Instant attack, ~1.1 s fall, a mono numeric readout (e.g. -3.0 dB) at the right of the label, and ticks every 6 dB (-18, -12, -6, 0, +6) below. The whole meter is hidden when "Show level meter" is off.
- The meter's current level is the fill's width, not a moving playhead; it should feel like a physical VU strip, not a progress bar.
- Pair with the `section-label` "OUTPUT LEVEL" directly above it, left-aligned to the meter.

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

