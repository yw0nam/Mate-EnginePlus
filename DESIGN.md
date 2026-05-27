# DESIGN.md — Mate-Engine

Visual system for the Mate-Engine desktop companion UI. Treat this file as the source of truth before touching any `.mat`, `.prefab`, or UI script. Pair with [`PRODUCT.md`](PRODUCT.md) for register, users, and tone.

Everything below is grounded in the live materials (`Assets/MATE ENGINE - Scripts/ThemeManager/*.mat`) and screenshots captured 2026-05-27 in `Screenshots/design-audit/`.

---

## 1. Theme

**Single canonical theme: dark, cool-blue.** No light mode. No theme variants.

The scene that forces this choice: *"A VRM character sits on a user's transparent-background desktop window at 9pm, dual-monitor, dim ambient light, one ear on Discord. The user glances at a small floating panel next to the character to nudge a setting, then closes it. The panel must read instantly, not bloom, and not break the character's lighting."*

That sentence forces:
- Solid dark surface, not blur/glass — the avatar already provides translucency at the window level.
- Cool/blue undertone, not warm grey — warm grey clashes with the typical anime VRM rim-light palette (cyans, lavenders).
- Low chroma overall — the avatar carries chroma; the UI cannot compete.

User personalization is **HSV hue rotation** of all UI materials via `Tools/ThemeManager.cs` (one global `hue` + `saturation` slider). This means the *base* palette must be navigable through any hue rotation without breaking — i.e. it derives all chroma from a single hue family that lifts coherently.

---

## 2. Color tokens

All colors are stored in mat files as gamma sRGB. The OKLCH below is the canonical form for any new code; the hex is what the existing `.mat` files round to.

### Surfaces (deep navy)

| Token | OKLCH | Hex | Source mat | Use |
|---|---|---|---|---|
| `surface.panel` | `oklch(0.20 0.025 270)` | `#191D2B` | `Ui Background.mat` | The main panel body. Rounded ~24px corners. |
| `surface.input` | `oklch(0.20 0.030 268)` | `#161F30` | `Ui Buttons.mat`, `Ui Text Input.mat`, `Ui Dropdown Background.mat` | Input fields, dropdown closed-state, default button rest. |
| `surface.toggle` | `oklch(0.20 0.026 265)` | `#131E2B` | `Ui Toggle.mat` | Toggle track when off. |
| `surface.overlay` | `oklch(0.20 0.025 269) / 57%` | `#181D2A` @ 0.57α | `Ui Category.mat` | Section banding / sticky header inside scroll regions. |
| `surface.bubble` | `oklch(0.27 0.038 263)` | `#1C2939` | `AiBubble.mat` (custom shader) | Chat bubble background, one step lighter than the panel so bubbles read against it. |

### Interactive (lifted slate)

| Token | OKLCH | Hex | Source mat | Use |
|---|---|---|---|---|
| `interactive.rest` | `oklch(0.34 0.040 272)` | `#3D4660` | `Ui Buttons Off.mat`, `Ui Scroll Bar.mat` | Off-state toggle thumb, scrollbar handle, secondary button rest. |
| `interactive.active` | `oklch(0.46 0.060 275)` | `#596484` | `Ui Buttons On.mat` | On-state toggle, primary button hover/pressed, selected list item. |
| `interactive.track` | `oklch(0.45 0.045 272)` | `#5A6380` | `Ui Slider Background.mat` | Slider track (unfilled portion). |

### Foreground (pale lavender white)

| Token | OKLCH | Hex | Source mat | Use |
|---|---|---|---|---|
| `fg.primary` | `oklch(0.93 0.020 290)` | `#DFE5FF` | `Ui Text.mat`, `Ui Slider Fill.mat` | Body text, labels, slider fill, dropdown caret, primary icon stroke. |
| `fg.muted` | `oklch(0.62 0.020 270)` | `#777E8E` | `API Ui.mat` | Placeholder text, disabled labels, hint copy ("Search song title…"). |
| `fg.flash` | `oklch(0.91 0.040 273) / 2%` | `#C4D6FF` @ 0.02α | `Ui Buy.mat` | Barely-there highlight wash. Press-flash only. Never as a fill. |

### Banned

- `#000` / `#fff`. Anywhere. The pale white is `#DFE5FF`; the dark is `#191D2B`. No exceptions — see [`PRODUCT.md`](PRODUCT.md) tone.
- Any accent color outside the navy/lavender family. If you need to signal danger, lift `interactive.active` and adjust the `ThemeManager` hue base — do not paint a red button.
- The default Unity chromakey purple (`#A076FF`-ish) outside the avatar background — it must never leak into UI.

---

## 3. Typography

Three TMP fonts referenced in chat-side prefabs (GUIDs in `ChatHistoryItem.prefab`). Don't hard-code per-Text font assets — they're swapped at runtime by `Settings/TMPFontReplacer.cs` based on locale.

### Scale (semantic)

| Token | Size | Weight / Tracking | Use |
|---|---|---|---|
| `type.wordmark` | 28–32px | Caps, regular, tracking +60 | Top-of-panel app name ("MATE ENGINE"). |
| `type.panelTitle` | 18–20px | Caps, regular, tracking +80 | Per-panel header ("BLENDSHAPES", "ALARM", "GENERAL SETTINGS"). |
| `type.sectionLabel` | 11–12px | Caps, regular, tracking +120 | In-panel section banners ("AUDIO SETTINGS", "MOVEMENT"). |
| `type.body` | 14px | Sentence case, regular | Standard label / dropdown selected value / chat bubble copy. |
| `type.bodySm` | 12px | Sentence case, regular | Field hints, voice slider readout ("VOL", "1.00X"). |
| `type.button` | 12–13px | Caps, regular, tracking +100 | Pill buttons ("RESET", "FINISH", "ADD NEW ALARM"). |

**Hierarchy rule**: each step is ≥1.4× the next. Avoid 14/15/16 stairs.

**Caps are the marker of structural levels** (wordmark, panel title, section label, button). Sentence case is for content (body, hints). Never mix — sentence-case headers feel wrong in this system; all-caps body copy is shouting.

**Line length** caps at 65ch on chat bubbles (the only long-form surface).

---

## 4. Layout & spacing

### Panel form factor

- **Vertical default**: 320×640 (±40px). Sits beside a standing avatar without overlapping more than ~25% of its silhouette.
- **Wide variant**: 720×360 for the Welcome / tutorial flow only. Centered.
- **Corner radius**: 24px on the outer panel. 12px on nested controls (inputs, buttons, dropdowns). 8px on chips/list items.
- **Outer panel padding**: 24px top/bottom, 20px left/right.
- **Top wordmark band**: 56px tall with the icon toolbar (`Settings` panel only) sitting at the top edge.

### Spacing scale

`4, 8, 12, 16, 24, 32, 48, 72`. No values between. Default vertical rhythm between unrelated rows is 16; tight pairs (label + control) is 8.

### Section banding

Use `surface.overlay` (57% alpha tint) as a horizontal band behind a section title. Do **not** draw separator lines (`<hr>`-style 1px borders). The system uses tonal bands, not strokes, to subdivide a panel.

### Component density

Single column. Two-up grids are allowed for paired controls (GRAPHICS + LANGUAGE dropdowns in Settings; BACK + SKIP + FINISH in Welcome). Never more than 2 across in vertical panels. Lists are one-tall-per-row.

---

## 5. Components

### Pill button

- Background: `surface.input`, hover/press lift to `interactive.rest` then `interactive.active`.
- Text: `fg.primary`, `type.button`.
- Corner radius: half the height (true pill — visible in "RESET", "BACK", "SKIP", "FINISH", "ADD NEW ALARM", "ADD TIMER").
- Padding: 12px vertical, 20px horizontal minimum.
- Icon-only buttons are square-rounded (12px radius), not pills.

### Dropdown

- Closed state: `surface.input` background, `fg.primary` text, right-aligned chevron in `fg.primary`. Caps for the value ("ULTRA", "ENGLISH").
- Open state: list panel uses `surface.input`, 12px radius, drops below — never above — the trigger.

### Slider

- Track: `interactive.track` (unfilled), `fg.primary` (filled). 4px tall, fully rounded.
- Thumb: 14×14 circle, `fg.primary`. No drop shadow.
- Three-up sliders (PET / EFFECTS / MENU VOLUME) stack with 8px gap.

### Toggle

- Track: `surface.toggle` (off) → `interactive.active` (on). 18px tall, fully rounded.
- Thumb: 14×14 circle in `fg.primary`, slides 16px between states.
- Label sits left of the toggle, NOT above. Caps for the label.

### Chat bubble

- Background: `surface.bubble` (uses custom kage shader — locked `_OverlayUseLuma`).
- Text: `fg.primary`, `type.body`, 14px line-height 1.45.
- Corner radius: 12px on three corners + 4px on the speaker-side tail corner (bottom-left for AI, bottom-right for user, mirrored).
- Max-width: 280px (≈ 65ch at body size).

### Section header (in-panel)

- Caps, `type.sectionLabel`, `fg.primary`, sitting on top of a `surface.overlay` band (full panel width, 28px tall, 4px below the previous block).
- Margin-bottom 12px before first control.

### Top-toolbar tab row (Settings panel)

- Five icon buttons across the top inset (visible in `02-settings-menu.png`).
- 32px tall, no labels — icon-only. Active tab gets a 2px bottom indicator in `fg.primary`; inactive icons drop to `fg.muted`.

### Search field

- Pattern from Dance Player panel ("Search song title…").
- `surface.input` background, 12px radius, leading 12px padding, trailing icon at right edge.
- Placeholder in `fg.muted`, typed text in `fg.primary`.

### Media controls (Dance Player)

- Center-aligned row of 5 icons: skip-back / prev-track / play-pause / next-track / shuffle-or-loop.
- Play-pause is visually heavier (slightly larger circle in `fg.primary` stroke), not a fill — keeps with the no-accent rule.
- No labels.

---

## 6. Iconography

- Stroke-only icons. ~1.5px stroke at 24px nominal size. Rounded line caps and joins.
- Single color: `fg.primary` (active) or `fg.muted` (inactive). Never two-tone.
- The Settings top toolbar uses category icons (likely chat, customize, accessories, …). Treat them as glyphs of the same family — do not mix in filled icons.

---

## 7. Motion

- Panel open/close: fade + 8px slide-in from the avatar-facing edge. **160ms ease-out (`cubic-bezier(0.22, 1, 0.36, 1)`)** — exponential out, no overshoot.
- Inter-panel switch: outgoing fades 120ms, incoming fades 160ms, no overlap (one-at-a-time rule from PRODUCT.md).
- Toggle thumb travel: 140ms ease-out.
- Slider thumb drag: 1:1 with input, no smoothing.
- Hue/saturation theme change: applied frame-by-frame via `ThemeManager.Apply()`, no animation — the user is dragging the slider, they want immediate feedback.
- **Never animate** width / height / margin / padding — that's a Unity layout rebuild and hits both perf and pixel-snapping. Animate position, scale, and `CanvasGroup.alpha` only.

---

## 8. Accessibility & input

- **Right-click on avatar** and **`M` key** both open the main menu. Settings panel must always be reachable from the keyboard.
- Touch regions on the avatar are *expressive*, not load-bearing — every action they trigger must also be reachable from the panel UI.
- Minimum hit target: 32×32 logical px. Pill buttons hit this naturally; icon-only tab row sits exactly at 32 — don't shrink it.
- Contrast: `fg.primary` on `surface.panel` is ~10:1 (passes WCAG AAA for body). `fg.muted` on `surface.panel` is ~4.5:1 (AA only — never use for body, only hints/disabled).
- The chromakey purple background is keyed out at the window level; it is **not** a UI color and never appears in shipped builds.

---

## 9. Anti-patterns (product-specific)

In addition to the global impeccable bans (no side-stripe borders, no gradient text, no glassmorphism default, no hero-metric template, no identical card grids, no modals-as-first-thought):

- **No light theme.** Even as a setting. ThemeManager rotates hue over the dark base; lightness is fixed.
- **No coloured semantic states.** Don't paint errors red or success green. Lift `interactive.active` for "selected/on" and use copy ("Error: …") for failures.
- **No nested panels.** A panel cannot contain another panel. Sub-pages within a panel replace content, not stack.
- **No skeuomorphic shadow on UI surfaces.** The Welcome panel has a subtle outer shadow against the desktop — that's the only allowed elevation.
- **No emojis as UI affordances.** Icon glyphs only. The avatar conveys emotion.
- **No 1px hairline borders between sections.** Use tonal bands (`surface.overlay`).
- **No "MATE ENGINE" wordmark on every panel** — it appears once, in the Settings root only.
- **Never embed marketing copy** ("Now with X!", "Try Y!") in the UI. README/Steam page handles that.

---

## 10. Conventions for new UI work

1. **Add a new panel?** Use a fresh Canvas (ScreenSpaceCamera, sortingOrder=1), name it `<Feature>MenuCanvas`, ship it inactive by default. Open/close through the existing menu router; don't bypass the one-at-a-time rule.
2. **Add a new control?** Reuse the existing `.mat` materials in `ThemeManager/`. Register the material with `ThemeManager` so hue/sat shift covers it.
3. **Add a new color?** First check whether `interactive.active` or `fg.primary` already covers it. If not, add it to this file *before* the `.mat` exists, and tag it with the OKLCH and source.
4. **Add a new font?** Add it through `TMPFontReplacer`'s locale list. Don't reference a TMP_FontAsset directly in a new prefab — the locale system needs to swap it.
5. **Add a new icon?** Stroke-only, 24px, single color, matches the existing toolbar glyph family.
6. **Change a token?** Update this file, then refresh the materials it references. The mat files are the implementation; this doc is the spec.

---

## 11. Open questions

Document, don't decide:

- The exact Settings top-toolbar tab list is not yet itemized here — needs a screenshot pass once the icons stop being icon-only or the user confirms the intent of each tab.
- The chat bubble tail geometry (4px corner override) was inferred from the AI bubble shader's behaviour; if there's a separate sprite for the tail, it should be documented here.
- Disabled-state colors for primary buttons aren't in `.mat` yet — currently `fg.muted` on `surface.input` is assumed. Validate the next time a button needs a true disabled state.

---

## Sources

- Live screenshots: `Mate-Engine/Screenshots/design-audit/01..08-*.png` (captured 2026-05-27).
- Color values: extracted from `Assets/MATE ENGINE - Scripts/ThemeManager/*.mat` and `Assets/MATE ENGINE - Scripts/OpenaiCompatibleAgent/UI/*.prefab`.
- Theming runtime: `Assets/MATE ENGINE - Scripts/Tools/ThemeManager.cs`.
- Layout cues: `Settings/SettingsMenu/`, `BlendshapeManager/`, scene `Mate Engine Main.unity` canvas hierarchy.
