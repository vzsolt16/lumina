---
name: Lumina
description: Gen X Soft Club study companion — pale teal glass, hairline wireframe, monospace signal.
colors:
  bg: "#d8edf5"
  bg2: "#c4e2f0"
  teal: "#3ea8c8"
  teal-deep: "#2790b0"
  cyan: "#58c8e0"
  seafoam: "#5acaae"
  text-dark: "#0f2a38"
  text-mid: "#2a5468"
  text-light: "#5a8498"
  off-white: "#f4fafd"
  panel: "#ffffff61"
  panel-blue: "#b4dcf073"
  border-teal: "#3ea8c84d"
  border-white: "#ffffff99"
  grid: "#3ea8c826"
typography:
  display:
    fontFamily: "'Barlow Condensed', sans-serif"
    fontSize: "clamp(3.5rem, 7vw, 5.5rem)"
    fontWeight: 700
    lineHeight: 0.95
    letterSpacing: "-0.01em"
  headline:
    fontFamily: "'Barlow Condensed', sans-serif"
    fontSize: "clamp(2rem, 4vw, 3rem)"
    fontWeight: 700
    lineHeight: 1
    letterSpacing: "0.02em"
  title:
    fontFamily: "'Barlow Condensed', sans-serif"
    fontSize: "1.2rem"
    fontWeight: 600
    lineHeight: 1.1
    letterSpacing: "0.06em"
  body:
    fontFamily: "'Barlow', sans-serif"
    fontSize: "0.9rem"
    fontWeight: 400
    lineHeight: 1.7
    letterSpacing: "normal"
  label:
    fontFamily: "'Share Tech Mono', monospace"
    fontSize: "0.65rem"
    fontWeight: 400
    lineHeight: 1.4
    letterSpacing: "0.18em"
rounded:
  none: "0px"
spacing:
  xs: "8px"
  sm: "16px"
  md: "24px"
  lg: "40px"
  xl: "64px"
  xxl: "88px"
components:
  button-primary:
    backgroundColor: "{colors.teal}"
    textColor: "{colors.off-white}"
    rounded: "{rounded.none}"
    padding: "0 28px"
    height: "44px"
    typography: "{typography.label}"
  button-primary-hover:
    backgroundColor: "{colors.teal-deep}"
    textColor: "{colors.off-white}"
  button-ghost:
    backgroundColor: "#ffffff4d"
    textColor: "{colors.text-mid}"
    rounded: "{rounded.none}"
    padding: "0 28px"
    height: "44px"
  input-field:
    backgroundColor: "#ffffff8c"
    textColor: "{colors.text-dark}"
    rounded: "{rounded.none}"
    padding: "0 14px"
    height: "44px"
  panel:
    backgroundColor: "#ffffff66"
    textColor: "{colors.text-dark}"
    rounded: "{rounded.none}"
---

# Design System: Lumina

## 1. Overview

**Creative North Star: "Clean Signal, No Noise"**

Lumina's interface is **Gen X Soft Club**: a Y2K-adjacent technical language built from pale
blue-teal washes, semi-transparent glass panels, hairline wireframe overlays, and monospace
data typography. It reads as a precise instrument — a study console rather than a study *app* —
without ever turning cold or aggressive. The mood is soft-toned, airy, and composed: a quiet
machine that shows its work.

This system serves a focused student at a desk, mid study-session, who came to do work. So the
chrome behaves like signal, not decoration: every monospace tag, status dot, and progress bar
carries real state (what document, what mode, what the machine is doing right now). The
"technical" surface is a *language*, never ornament — if a wireframe mark or a `SYSTEM_LABEL`
isn't reporting something true, it doesn't belong.

It explicitly rejects the look of the category it lives in. **Not** the generic AI-chat SaaS
(purple/indigo gradients, hero-metric templates, "✨ powered by AI" sparkle). **Not** gamified
ed-tech (mascots, streaks, confetti, candy color). **Not** the busy, ad-heavy consumer
flashcard app. Lumina is single-purpose and calm; recall is the reward, not a badge.

**Key Characteristics:**
- Tinted everything — no neutral grays, no pure black or white.
- Glass over grid — translucent panels layered on a fixed teal wireframe.
- Sharp corners always (0px radius), flat always (no shadows).
- Three voices of type: condensed display, humanist body, monospace data labels.
- One saturated hue (teal); everything else stays desaturated and airy.

## 2. Colors

A single saturated teal carries the whole system; every other value is a desaturated
blue-teal tint. Transparency and layering do the work that solid fills and shadows do elsewhere.

### Primary
- **Signal Teal** (`#3EA8C8`): the only saturated hue. Accents, active labels, focus borders,
  primary buttons, progress fills, the wireframe grid. Used as the system's single "voice."
- **Deep Teal** (`#2790B0`): the pressed/hover state of Signal Teal on solid surfaces.

### Secondary
- **Cyan Highlight** (`#58C8E0`): rare brightening accent for emphasis within teal contexts.
- **Seafoam** (`#5ACAAE`): reserved exclusively for *live / success / online* state — the
  pulsing status dot, "ONLINE", completion. The only place a second hue is allowed to speak.

### Neutral
- **Pale Sky** (`#D8EDF5`): the page background. The canvas the glass floats on.
- **Soft Blue** (`#C4E2F0`): secondary background / nav fill, a half-step darker than the page.
- **Dark Navy** (`#0F2A38`): primary text and headings. The ink end of the ramp.
- **Blue-Grey** (`#2A5468`): secondary text and body prose. Safe for sustained reading.
- **Slate** (`#5A8498`): muted/passive labels and metadata **only** — see the Contrast Floor Rule.
- **Near White** (`#F4FAFD`): text on solid teal surfaces. Stands in for pure white, which is banned.

### Glass & Structure (transparency tokens)
- **White Glass** (`rgba(255,255,255,0.38–0.55)`): the default frosted panel fill, over `blur(10–16px)`.
- **Blue Glass** (`rgba(180,220,240,0.45)`): alternate panel fill for layered depth.
- **Teal Border** (`rgba(62,168,200,0.3)`): structural hairline borders, cell dividers.
- **White Border** (`rgba(255,255,255,0.6)`): glass-panel edges layered over backgrounds.
- **Grid Teal** (`rgba(62,168,200,0.15)`): the fixed 60×60px background wireframe.

### Named Rules
**The One Hue Rule.** Signal Teal is the only saturated color on any screen. Seafoam is permitted
*only* to mean live/success. If a third saturated hue appears, the system is broken.

**The Contrast Floor Rule.** Body and interactive text must clear WCAG AA (≥4.5:1) against its
glass-on-sky background. Slate (`#5A8498`) clears AA on Pale Sky but is reserved for *passive*
metadata; **prose, labels users must read, and placeholders use Blue-Grey (`#2A5468`) or darker.**
When in doubt, step toward Dark Navy. Elegance is never a reason to drop contrast.

**The No-Pure Rule.** Never `#000` or `#fff`, never an untinted gray. Every neutral is tinted
blue-teal; warmth (amber/orange/brown/cream) is forbidden outright.

## 3. Typography

**Display Font:** Barlow Condensed (sans-serif fallback)
**Body Font:** Barlow (sans-serif fallback)
**Label/Mono Font:** Share Tech Mono (monospace fallback)

**Character:** A three-register system pairing on contrast, not similarity. Condensed display
type shouts headlines in uppercase; a humanist sans carries calm prose; a monospace face speaks
the machine's data language. The contrast between condensed caps, relaxed body, and tracked
mono *is* the aesthetic.

### Hierarchy
- **Display** (Barlow Condensed 700, `clamp(3.5rem, 7vw, 5.5rem)`, line-height 0.95, UPPERCASE):
  hero headlines only. Secondary lines may use outline text (`-webkit-text-stroke: 1.5px teal;
  color: transparent`).
- **Headline** (Barlow Condensed 700, `clamp(2rem, 4vw, 3rem)`, line-height 1, UPPERCASE):
  section headings.
- **Title** (Barlow Condensed 600, `~1.2rem`, UPPERCASE, tracked `0.06em`): card and panel titles.
- **Body** (Barlow 400, `0.82–0.9rem`, line-height 1.7, sentence case): all prose. Cap measure at
  65–75ch. Weight 300 allowed for large descriptive intros only, never small body.
- **Label** (Share Tech Mono, `0.58–0.72rem`, tracked `0.08–0.2em`, UPPERCASE): system tags,
  status, section markers, build strings. Multi-word values use underscores: `MODULE_STATUS: ACTIVE`.

### Named Rules
**The Three Voices Rule.** Condensed display, humanist body, monospace data. Never blur them —
prose is never set in the mono face; system labels are never set in the body face.

**The Uppercase Display Rule.** All display and heading type is uppercase. Sentence-case headings
break the system. Prose stays sentence case.

**The Mono-Is-Data Rule.** Share Tech Mono is reserved for things the machine reports — status,
IDs, counts, timestamps, section markers. Never use it for content a human wrote.

## 4. Elevation

This system is **flat by doctrine — no drop shadows, ever.** Depth comes entirely from
*translucency and layering*: frosted glass panels (`background: rgba(255,255,255,0.4);
backdrop-filter: blur(10–16px)`) stack over a fixed background wireframe, and overlapping
semi-transparent planes read as depth the way shadows would elsewhere. Hairline borders separate
planes; the grid shows through the glass to signal "above."

### Named Rules
**The Flat-Glass Rule.** Surfaces never cast shadows. If something needs to feel lifted, raise its
opacity or layer it over the grid — don't reach for `box-shadow`.

**The Hairline Divider Rule.** Grids divide cells with a `gap: 1px` over a `background: teal-border`
container, **never** per-cell borders (which double up). One pixel, teal-tinted, always.

## 5. Components

### Buttons
- **Shape:** sharp rectangles, 0px radius. Barlow Condensed 600, uppercase, tracked `0.14–0.16em`.
- **Primary:** solid Signal Teal (`#3EA8C8`) fill, Near White text, 44px tall, `0 28px` padding.
- **Hover / Focus:** primary darkens to Deep Teal (`#2790B0`); 0.2s transition; no transform, no glow.
- **Ghost:** translucent white fill (`rgba(255,255,255,0.3)`) + blur, teal hairline border, Blue-Grey text.
- **Flush pairs:** side-by-side buttons share an edge via `margin-left: -1px` (no double border).

### Inputs / Fields
- **Style:** translucent white fill (`rgba(255,255,255,0.55)`), 1px teal hairline border, 0px radius,
  44px tall. Body face for forms; mono face for the terminal/chat query input.
- **Focus:** border shifts to solid Signal Teal, fill brightens (`rgba(255,255,255,0.8)`). No glow.
- **Placeholder:** must clear AA — use Blue-Grey-strength, not a faded slate. (See Contrast Floor Rule.)

### Cards / Panels (glass panel — signature)
- **Corner Style:** 0px, always sharp.
- **Background:** White Glass (`rgba(255,255,255,0.4)`) over `backdrop-filter: blur(12px)`.
- **Border:** 1px teal hairline (`rgba(62,168,200,0.3)`); white-tint border when layered over imagery.
- **Shadow Strategy:** none — see Elevation.
- **Panel bar:** optional header strip in `rgba(62,168,200,0.1)` with a mono uppercase title.
- **Hover (on grid cells):** subtle fill lift to `rgba(255,255,255,0.35–0.45)`. No transform.

### Navigation
- **Style:** 60px sticky bar, `backdrop-filter: blur(16px)`, Soft Blue translucent fill, teal
  bottom hairline. Links are full-height, bordered left/right, uppercase Barlow Condensed.
- **Hover:** full-cell fill `rgba(62,168,200,0.12)`, text to Dark Navy.
- **Logo:** Barlow Condensed 700 uppercase + a light-weight mono descriptor split by a left border.

### Crosshair Corners (signature)
Large decorative containers use 20×20px L-shaped corner marks (teal hairlines) instead of a full
border — a targeting-reticle / wireframe motif. Reserve for hero panels and big decorative frames,
never on functional cards.

### Status & Progress (signature)
- **Live dot:** 5px Seafoam circle, `animation: pulse 2s ease-in-out infinite`, paired with an
  uppercase mono `ONLINE` label — color is never the only signal.
- **Progress:** 2px teal fill on a teal-border track, with a mono `%` / count label alongside.

## 6. Do's and Don'ts

### Do:
- **Do** keep Signal Teal (`#3EA8C8`) as the only saturated hue; let Seafoam speak only for live/success.
- **Do** convey depth with translucency + `backdrop-filter: blur()` over the fixed grid — never shadow.
- **Do** keep every corner sharp (0px radius) on cards, panels, inputs, buttons, windows.
- **Do** set all headings uppercase in Barlow Condensed; keep prose sentence-case in Barlow.
- **Do** reserve Share Tech Mono for machine data (status, IDs, counts, markers).
- **Do** divide grids with a single teal-tinted `gap: 1px` over a bordered container.
- **Do** pair every color-coded state with a non-color cue — quiz correct/incorrect, live/offline,
  and error states get an icon, shape, or text label too (color-blind safety is a stated bar).
- **Do** keep prose and placeholders at Blue-Grey (`#2A5468`) or darker for AA contrast on the pale bg.

### Don't:
- **Don't** look like generic AI-chat SaaS — no purple/indigo gradients, no hero-metric template,
  no decorative glassmorphism beyond the established glass system, no "powered by AI" sparkle.
- **Don't** look like gamified ed-tech — no mascots, streaks, confetti, badges, or candy color.
- **Don't** look like a busy consumer flashcard app — no clutter, ads, or social-feed noise.
- **Don't** use warm tones (amber, orange, brown, cream) or any neutral gray. Everything is teal-tinted.
- **Don't** use drop shadows, rounded corners, gradient text, neon/glow, or serif fonts.
- **Don't** use pure `#000` or `#fff`.
- **Don't** let Slate (`#5A8498`) carry body prose or anything users must read — it's passive metadata only.
- **Don't** add a colored side-stripe (`border-left`/`right` > 1px) as a card/alert accent — full hairline or nothing.
