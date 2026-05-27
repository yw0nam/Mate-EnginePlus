---
register: product
---

# PRODUCT.md — Mate-Engine

A free, moddable desktop companion app. A VRM avatar lives on the user's Windows desktop with a transparent background, reacts to mouse/audio/system events, dances, talks, and chats through a local-or-remote AI backend. The UI is small floating panels that appear next to the avatar on demand and disappear again.

## Register

`product`. Design SERVES the avatar. The 3D character is the experience; UI is the control surface. Every panel should feel like it belongs to a quiet, well-engineered tool — not a marketing artifact.

## Product purpose

Mate-Engine exists because Desktop Mate charges $10–$25 per character model and disables modding. Mate-Engine is the open-source, custom-VRM-supporting alternative: free on GitHub, $3.99 on Steam (with cosmetic DLC + workshop), AGPLv3 + MateProv2 license.

Core value loops:

1. **Drop in your own VRM** → it lives on the desktop and reacts to drags, idle, music, and touch regions.
2. **Talk to it** → streaming chat via an OpenAI-compatible backend (Hermes on `localhost:8642`), TTS via Irodori (`localhost:8091`), emotion-driven blendshape crossfade.
3. **Customize without limits** → blendshape editor, theme hue shift, mods via Steam Workshop, animation modding, .ME file format.

## Users

- **Primary**: VRM/vtuber enthusiasts on Windows desktops. They have a favorite character, install custom shaders without flinching, and run this in the background while gaming or working. Often dual-monitor.
- **Secondary**: Hobbyist modders who write custom dances, accessory mods, or alternate avatar packs.
- **Tertiary**: Anime/vtuber fans who saw a clip on Twitter/Reddit and want a $0 entry point.

Context of use:
- Long-running (hours/days), mostly idle in the corner of the screen.
- The user is doing something else — gaming, coding, browsing, watching a stream. They glance at it. They occasionally interact (chat, drag, right-click).
- Often in dim/evening light. Headphones common (TTS audio matters).
- Settings/chat panels open in short bursts (10s–2min), then close.

## Tone

Quiet, technical, slightly futuristic. The UI is the *frame*, not the *content*. Personality lives in the avatar; the panels should feel like an Apple-ish tool that knows when to step back.

- **Voice**: factual, terse, never marketing-y. Labels are nouns ("BLENDSHAPES", "ALARM", "VOL"). Buttons are imperative verbs ("RESET", "ADD NEW ALARM", "SKIP").
- **No emoji in UI strings.** Emotion is conveyed by the avatar.
- **Caps used deliberately** as section/panel titles, not for emphasis in body copy.

## Anti-references

We are explicitly **not**:

- **Desktop Mate's UI** — busy, gradient-heavy, fragmented modal stacks. We collapse into one panel at a time.
- **Sanrio/kawaii pastel chrome** — pink-purple gradients, sparkles, bubble fonts. Our character may be cute; the *frame* is not.
- **VRoid Studio / generic anime tools** — washed-out grey, soft baby-blue accents, low contrast, MS-Sans-feel. We're sharper and darker.
- **SaaS dashboards** — card grids, hero metrics, sidebar nav. We're a 360×640-ish vertical panel, single column.
- **Glassmorphism** — blur-heavy translucent panels. Our panels are solid dark with crisp edges. Transparency belongs to the *window*, not the chrome.
- **Discord/Spotify dark mode clones** — flat charcoal greys with green/purple accents. Our dark has a blue undertone, not warm-grey.

## Strategic principles

1. **The avatar is the hero.** UI never obscures more than ~25% of the avatar's bounding box at default size. Panels park to one side; they don't center over the character.
2. **One panel at a time.** Settings, Chat, Blendshapes, Alarm, Dance Player are mutually exclusive surfaces — opening one closes the others. No stacked modals.
3. **Vertical panel as the default form factor.** Phone-shaped (~320×640) suits sitting next to a standing character. Welcome/onboarding is the only horizontal exception.
4. **Localized labels, not localized layouts.** Strings flow through `Lang/` and TMP font replacement (`TMPFontReplacer`). The grid never rebuilds for a language.
5. **Hue-shift, not theme-swap.** User customization runs through `ThemeManager` (single hue + saturation slider) over a fixed dark base. No light theme. No third-party theme files.
6. **Inspector-first wiring.** Per AGENTS.md, design decisions must survive the agent not seeing the scene — components are MonoBehaviours with `[Header]` fields wired by the user, not deep `Find()` paths.
7. **Defer to the OS for chrome.** The window has no titlebar — it's the desktop chrome around the avatar. The panel is the only thing that needs to look like "an app."

## What this app is NOT

- Not a chat client primarily. Chat is one panel; the companion is the product.
- Not a productivity tool. There is no calendar, no notes, no todo. The Alarm panel exists for the avatar to wake the user, not for time management.
- Not a multi-user social product. It's local, single-machine, single-character (up to 9 avatars supported, but all yours).
- Not browser-based. It's a native Unity desktop app with Win32 / SteamWorks / Discord RPC dependencies.

## Cross-references

- Project conventions and codebase layout: [`AGENTS.md`](AGENTS.md)
- Visual tokens and component patterns: [`DESIGN.md`](DESIGN.md)
- Theming runtime: `Assets/MATE ENGINE - Scripts/Tools/ThemeManager.cs`
- Localization: `Assets/MATE ENGINE - Scripts/Lang/`
