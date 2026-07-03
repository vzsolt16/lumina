---
name: Lumina
description: Gen X Soft Club, leaned all the way in — drenched washed-teal record sleeve, lowercase techno type, hairline wireframe, grain.
colors:
  deep: "#06222e"
  deep-2: "#0a3140"
  deep-3: "#0e3d4f"
  ice: "#d9f2f8"
  ice-dim: "#9fcfdd"
  cyan: "#6fd8ee"
  cyan-soft: "#58c8e0"
  seafoam: "#5acaae"
  frost: "#e9f6fa"
  frost-2: "#d8edf5"
  ink: "#0f2a38"
  ink-mid: "#2a5468"
  moss: "#0a2e20"
  moss-2: "#0e3a29"
  mint: "#d7f5e4"
  acid: "#7fe6b4"
  hair-cyan: "#6fd8ee47"
  hair-ice: "#d9f2f838"
  hair-green: "#7fe6b447"
  hair-ink: "#0f2a3840"
typography:
  display:
    fontFamily: "'Audiowide', 'Trebuchet MS', sans-serif"
    fontSize: "clamp(2.9rem, 10.5vw, 6rem)"
    fontWeight: 400
    lineHeight: 1
    letterSpacing: "0.04em"
  headline:
    fontFamily: "'Audiowide', 'Trebuchet MS', sans-serif"
    fontSize: "clamp(1.9rem, 4.6vw, 3.4rem)"
    fontWeight: 400
    lineHeight: 1.1
    letterSpacing: "0.02em"
  statement:
    fontFamily: "'Jura', 'Century Gothic', sans-serif"
    fontSize: "clamp(1.7rem, 4vw, 3rem)"
    fontWeight: 300
    lineHeight: 1.3
    letterSpacing: "0.01em"
  body:
    fontFamily: "'Jura', 'Century Gothic', sans-serif"
    fontSize: "1rem"
    fontWeight: 400
    lineHeight: 1.7
    letterSpacing: "normal"
  tag:
    fontFamily: "'Jura', 'Century Gothic', sans-serif"
    fontSize: "0.66rem"
    fontWeight: 600
    lineHeight: 1.4
    letterSpacing: "0.32em"
rounded:
  none: "0px"
spacing:
  xs: "8px"
  sm: "16px"
  md: "24px"
  lg: "40px"
  xl: "64px"
  section: "clamp(88px, 14vh, 150px)"
components:
  button-primary:
    backgroundColor: "{colors.cyan}"
    textColor: "{colors.deep}"
    rounded: "{rounded.none}"
    padding: "0 30px"
    height: "52px"
    typography: "{typography.tag}"
  button-primary-hover:
    backgroundColor: "{colors.seafoam}"
    textColor: "{colors.deep}"
  button-ghost:
    backgroundColor: "#d9f2f80f"
    textColor: "{colors.ice}"
    border: "1px solid {colors.hair-cyan}"
    rounded: "{rounded.none}"
    padding: "0 30px"
    height: "52px"
  input-field:
    backgroundColor: "#d9f2f80f"
    textColor: "{colors.ice}"
    border: "1px solid {colors.hair-cyan}"
    rounded: "{rounded.none}"
    padding: "0 14px"
    height: "52px"
  panel:
    backgroundColor: "#d9f2f80a"
    textColor: "{colors.ice}"
    border: "1px solid {colors.hair-cyan}"
    rounded: "{rounded.none}"
---

# Design System: Lumina

## 1. Overview

**Creative North Star: "The page is the record sleeve; the product is the session."**

Lumina's interface is **Gen X Soft Club, leaned all the way in**: a late-90s ambient-compilation
language built from drenched washed-teal gradients, film grain and scanlines, hairline rules,
rounded Y2K techno type, and CD-sleeve ephemera (catalog numbers, tracklists, barcodes, session
meters). It reads like a limited-edition release you found in 1999 — a *session* you put on, not
software you operate. The canonical reference surface is `frontend/landing-softclub.html`; the
mood sources are `frontend/example1–4.jpeg` (Gen X *Ambient Lounge*, Aphex Twin *SAW 85–92*,
Y2K transparent hardware, green grainy street photography).

The chrome still behaves like signal, not decoration: every tag, meter, and track number reports
something true (what session, what progress, what track). But the register has shifted from
"quiet instrument" to "immersive sleeve" — the surface sells the *experience of studying*, and the
features present themselves as tracks on a record.

It still explicitly rejects the category's look. **Not** generic AI-chat SaaS (purple gradients,
hero metrics, sparkle). **Not** gamified ed-tech (mascots, streaks, confetti). **Not** the busy
consumer flashcard app. Lumina is single-purpose and atmospheric; recall is the reward.

**Key Characteristics:**
- Drenched, not airy — the surface IS the color: deep washed teal, with a frost interlude and a
  green "side b" as sanctioned per-section art direction.
- Texture over gloss — film grain, scanlines, blurred glass panes, hairline rules.
- Sharp corners always (0px radius), flat always (no shadows).
- Two voices of type, both lowercase: Audiowide display, Jura everything else.
- One saturated signal (cyan); seafoam speaks only for live/play; acid only on green sections.

## 2. Colors

The palette is three washes and one signal. Side A (deep teal) is the default world; the frost
and the green wash are deliberate section-level shifts, like flipping the record over.

### Side A — the deep wash (default)
- **Deep** (`#06222E`): the deepest wash; page background, footer fade.
- **Deep-2** (`#0A3140`): section wash, a half-step lifted.
- **Deep-3** (`#0E3D4F`): lifted wash for emphasis panels and gradient tops.
- **Ice** (`#D9F2F8`): primary ink on dark. All prose and headings on the deep wash.
- **Ice-dim** (`#9FCFDD`): secondary ink — supporting copy, tags, metadata.
- **Signal Cyan** (`#6FD8EE`): the one saturated voice. Accents, links, primary buttons,
  emphasis spans, wireframe 3D strokes, focus outlines, the session meter.
- **Cyan-soft** (`#58C8E0`): quieter cyan for secondary accents.
- **Seafoam** (`#5ACAAE`): reserved for *live / play / success* — hover state of play buttons,
  EQ bars, "live" markers. The only second hue allowed on Side A.

### The Frost — light interlude sections
- **Frost** (`#E9F6FA`) / **Frost-2** (`#D8EDF5`): pale section washes for the "interlude".
- **Ink** (`#0F2A38`) / **Ink-mid** (`#2A5468`): primary / secondary text on frost.

### Side B — the green wash
- **Moss** (`#0A2E20`) / **Moss-2** (`#0E3A29`): deep green section washes.
- **Mint** (`#D7F5E4`): primary ink on green.
- **Acid** (`#7FE6B4`): the green wash's signal accent. Never leaves Side B sections.

### Hairlines & structure (transparency tokens)
- **Cyan hairline** (`rgba(111,216,238,0.28)`): default borders, rules, dividers on dark.
- **Ice hairline** (`rgba(217,242,248,0.22)`): edges of glass panes over imagery/washes.
- **Green hairline** (`rgba(127,230,180,0.28)`): Side B's structural lines.
- **Ink hairline** (`rgba(15,42,56,0.25)`): borders and grid gaps on frost sections.

### Named Rules
**The One Signal Rule.** Cyan (`#6FD8EE`) is the only saturated voice on any deep-wash screen.
Seafoam may speak *only* to mean live/play/success. Acid may speak *only* inside a green Side B
section. If a fourth saturated hue appears, the system is broken.

**The Three Washes Rule.** Per-section art direction is sanctioned — deep teal, frost, green —
but a *section commits to one wash*; washes never mix inside a section, and the green wash never
carries primary CTAs.

**The Contrast Floor Rule.** Prose and interactive text on the deep wash use Ice (`#D9F2F8`);
on frost use Ink or Ink-mid; on moss use Mint. Ice-dim clears AA on the deep washes and may carry
supporting copy, but long-form reading defaults to Ice. Acid is for short accents, never body
text. Elegance is never a reason to drop contrast.

**The No-Pure Rule.** Never `#000` or `#fff`, never an untinted gray. Every neutral is tinted
toward its wash; warmth (amber/orange/brown/cream) is forbidden outright.

## 3. Typography

**Display Font:** Audiowide (single weight, rounded Y2K techno; `Trebuchet MS` fallback)
**Text Font:** Jura 300–700 (rounded technical sans; `Century Gothic` fallback)

Share Tech Mono and Barlow/Barlow Condensed are **retired** — they belong to the previous, airier
system and do not appear in this direction.

**Character:** Two voices pairing on contrast. Audiowide is "the label" — the wordmark, section
titles, big display moments, always lowercase, wide and rounded like a 1999 compilation logo.
Jura carries everything else: light 300 for big statements, 400 for prose, 600 tracked-tiny for
the sleeve-ephemera tags. Its rounded terminals echo the Aphex-style tracklist type.

### Hierarchy
- **Display** (Audiowide 400, `clamp(2.9rem, 10.5vw, 6rem)`, line-height 1, tracked `0.04em`,
  lowercase): the wordmark and hero-scale moments only. May carry the chromatic fringe
  (see Elevation).
- **Headline** (Audiowide 400, `clamp(1.9rem, 4.6vw, 3.4rem)`, line-height 1.1, lowercase):
  section headings ("side a", "everything you need in one session").
- **Statement** (Jura 300, `clamp(1.7rem, 4vw, 3rem)`, line-height 1.3, lowercase): editorial
  ledes and big manifesto lines; emphasis via a 500–600-weight cyan `<em>`, never italics.
- **Body** (Jura 400, `0.82–1rem`, line-height 1.7, sentence case): all prose. Cap measure at
  58–65ch. Emphasis via 600 weight in the wash's primary ink.
- **Tag** (Jura 600, `0.62–0.72rem`, tracked `0.22–0.34em`, UPPERCASE): the sleeve-ephemera
  voice — catalog numbers, section markers, durations, status. The only uppercase in the system.

### Named Rules
**The Lowercase Rule.** Display and heading type is lowercase — a deliberate reversal of the old
uppercase system. The record sleeve whispers its titles. Only Tag-voice ephemera is uppercase.

**The Two Voices Rule.** Audiowide is the label; Jura is everything else. Prose is never set in
Audiowide; the wordmark is never set in Jura. No third family, no mono.

**The Ephemera Rule.** The Tag voice is reserved for things the sleeve reports — catalog numbers
(`lmn·001`), track numbers, durations, session progress, status. If a tag isn't reporting
something true, it doesn't belong.

## 4. Elevation

Still **flat by doctrine — no drop shadows, ever.** Depth now comes from *atmosphere*, in layers:

1. **Wash gradients**: large radial/linear gradients inside a section's wash (cyan glow at one
   corner, seafoam breath at another) create space the way lighting does on a sleeve photograph.
2. **Blurred panes**: translucent, hairline-bordered, slightly skewed rectangles
   (`filter: blur(2px)`, ice-tint gradients) float behind content like out-of-focus lounge
   architecture.
3. **Texture overlays**: a fixed film-grain layer (inline SVG `feTurbulence`, `opacity 0.09`,
   `mix-blend-mode: overlay`) plus a scanline layer (repeating 1px gradient) sit above everything,
   pointer-events none. They are the "print" of the sleeve.
4. **Hairlines**: 1px tinted rules separate planes and cells; crosshair corner marks (18–20px
   L-shapes) frame hero-scale containers.
5. **Chromatic fringe**: display type and wireframe 3D get a misregistered-print offset — a cyan
   shadow a few px one way, a fainter seafoam shadow the other (`text-shadow: 3px 0 0
   rgba(111,216,238,0.35), -3px 0 0 rgba(90,202,174,0.22)`). This is print misregistration, not
   neon glow; keep offsets small and alphas low.

### Named Rules
**The Flat-Wash Rule.** Surfaces never cast shadows. If something needs to feel lifted, lift its
wash (deep → deep-3), brighten its hairline, or float a blurred pane behind it.

**The Hairline Divider Rule.** Grids divide cells with a `gap: 1px` over a hairline-token
container background, never per-cell borders. One pixel, wash-tinted, always.

**The Grain-Is-Global Rule.** Grain and scanlines cover the whole viewport, including nav — the
sleeve is printed in one pass. They are decorative only (`aria-hidden`, pointer-events none) and
never carry information.

## 5. Components

### Buttons
- **Shape:** sharp rectangles, 0px radius, 52px tall, `0 30px` padding. Jura 700, uppercase Tag
  voice, tracked `0.3em`.
- **Primary ("press play"):** solid Signal Cyan fill, Deep text, optional play-triangle glyph.
  Hover shifts to Seafoam (play = live). 0.2s ease; no transform, no glow.
- **Ghost:** translucent ice fill (`rgba(217,242,248,0.06)`), cyan hairline border, Ice text;
  hover lifts fill to `rgba(111,216,238,0.14)`.
- **Flush pairs:** side-by-side buttons separated by a `1px` gap, sharing the sleeve's hairline
  grid rather than touching.

### Inputs / Fields
- **Style:** translucent ice fill on the deep wash, 1px cyan hairline, 0px radius, 52px tall,
  Ice text. On frost sections: white-translucent fill with ink hairline.
- **Focus:** border shifts to solid Signal Cyan; `outline: 1px solid cyan` offset 3px for
  keyboard focus. No glow.
- **Placeholder:** Ice-dim at minimum; never fainter.

### Nav (the sleeve spine)
- 52px fixed bar, deep translucent fill (`rgba(6,34,46,0.72)`) over `backdrop-filter: blur(14px)`,
  cyan hairline bottom border. Cells divided by full-height hairlines: wordmark + catalog tag,
  section tag, session meter, and a solid-cyan "play" cell as the persistent CTA.
- **Session meter:** a 72px hairline track with a cyan `scaleX` fill plus a `042%`-style Tag
  readout — live scroll/session progress, never fake.

### Tracklist rows (signature)
- Features and sequences present as record tracks: Tag-voice track number in cyan, Jura 500
  lowercase name, Ice-dim description, right-aligned Tag-voice duration. Rows divided by cyan
  hairlines; hover fills the row `rgba(111,216,238,0.08)` and may surface seafoam EQ bars
  (animated only under no-preference motion).

### Spec cards (frost sections)
- Hairline-gapped grid (`repeat(auto-fit, minmax(230px, 1fr))`, 1px ink-hairline gaps). Cards are
  "clear plastic": white-to-cyan translucent gradient fill over `backdrop-filter: blur(10px)`,
  a static diagonal glare sweep, Tag-voice key, Audiowide value, Ink-mid note.

### Sleeve ephemera (signature)
- **Rule-tags:** a Tag-voice label sitting on a hairline rule (the cover-bar motif).
- **Crosshair corners:** L-shaped corner marks framing hero panels and thesis frames.
- **Corner data:** tiny Tag-voice blocks pinned to hero corners (`stereo · 44.1 khz`,
  coordinates). Ephemera must obey the Ephemera Rule — playful is fine, false is not.
- **Barcode:** SVG stripe block + spaced digits in the footer catalog panel.

### Wireframe 3D (signature)
- Hand-rolled canvas line renderers (no libraries): the spinning CD on Side A, the icosahedron
  "recall object" on Side B. Thin strokes in the section's signal color with a chromatic-fringe
  second pass; depth-faded alpha; scroll-scrubbed rotation plus slow idle spin.
- Decorative only (`aria-hidden`), paused off-screen, and rendered as a **static frame** under
  `prefers-reduced-motion: reduce`.

### Motion
- Scroll reveals rise 26px with `cubic-bezier(0.16,1,0.3,1)` staggers; reveals *enhance* an
  already-visible default — content is never gated on scroll (no-JS and reduced-motion show
  everything immediately).
- The ticker marquee, EQ bars, glare sweeps, parallax panes, and canvas spin all stop under
  `prefers-reduced-motion: reduce`. No bounce, no elastic, ever.

## 6. Do's and Don'ts

### Do:
- **Do** keep Signal Cyan as the only saturated voice on the deep wash; Seafoam only for
  live/play/success; Acid only inside Side B green sections.
- **Do** commit each section to one wash (deep / frost / moss) and let hard cuts between washes
  read as sleeve panels.
- **Do** keep every corner sharp (0px) and every surface shadowless; depth is wash + grain +
  hairline + blurred panes.
- **Do** set display type lowercase in Audiowide and reserve uppercase for the tracked Tag voice.
- **Do** make ephemera true: session meters show real progress, track numbers number real
  sequences, catalog tags name real things.
- **Do** ship reduced-motion fallbacks for every animation (static disc frame, no marquee,
  instant reveals) and keep prose at the wash's primary ink (Ice / Ink / Mint).
- **Do** pair every color-coded state with a non-color cue (icon, shape, or text label).

### Don't:
- **Don't** look like generic AI-chat SaaS — no purple/indigo, no hero-metric template, no
  "powered by AI" sparkle.
- **Don't** look like gamified ed-tech — no mascots, streaks, confetti, badges, or candy color.
- **Don't** use warm tones (amber, orange, brown, cream) or untinted grays; never pure `#000`
  or `#fff`.
- **Don't** use drop shadows, rounded corners, gradient text, or serif fonts. The chromatic
  fringe is a small print-misregistration offset, not a neon glow — don't grow it into one.
- **Don't** bring back the retired voices: no Share Tech Mono, no Barlow, no uppercase display
  headings.
- **Don't** let Acid or Seafoam carry prose, CTAs, or anything outside their assigned meanings.
- **Don't** add a colored side-stripe (`border-left`/`right` > 1px) as a card/alert accent —
  full hairline or nothing.
- **Don't** let grain, scanlines, or ephemera carry information — atmosphere is never the only
  signal.
