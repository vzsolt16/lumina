# Lumina — Design System

## Visual Direction

Lumina's UI draws from the **Gen X Soft Club** aesthetic: a Y2K-adjacent design language that layers pale blue-teal washes, semi-transparent glass panels, wireframe overlays, and monospace data typography. The result is technical and precise without being cold — soft-toned, airy, and composed rather than aggressive or neon-heavy.

The overall mood is: **clean signal. no noise.**

---

## Color Palette

| Role | Name | Value |
|------|------|-------|
| Page background | Pale sky | `#D8EDF5` |
| Secondary background | Soft blue | `#C4E2F0` |
| Glass panel | White overlay | `rgba(255,255,255,0.38)` |
| Glass panel alt | Blue overlay | `rgba(180,220,240,0.45)` |
| Primary accent | Teal | `#3EA8C8` |
| Primary accent dark | Deep teal | `#2790B0` |
| Highlight | Cyan | `#58C8E0` |
| Success / live | Seafoam | `#5ACAAE` |
| Primary text | Dark navy | `#0F2A38` |
| Secondary text | Blue-grey | `#2A5468` |
| Muted text | Slate | `#5A8498` |
| Off-white | Near white | `#F4FAFD` |
| Border (teal tint) | Teal border | `rgba(62,168,200,0.3)` |
| Border (white tint) | White border | `rgba(255,255,255,0.6)` |
| Grid lines | Grid teal | `rgba(62,168,200,0.15)` |

**Rules:**
- Never use pure black or pure white.
- All backgrounds are tinted blue or teal — no neutral greys.
- Accent color (`#3EA8C8`) is the only saturated hue; everything else stays desaturated.
- Transparency and layering are central — panels stack with `rgba` values, not solid fills.

---

## Typography

### Display / Headings
**Barlow Condensed** — Google Fonts

- Weight: 700 (headings), 600 (subheadings), 400 italic (secondary labels)
- Always uppercase for headings
- Letter spacing: `0.02em` to `0.14em` depending on size
- Use outline text (`-webkit-text-stroke: 1.5px var(--teal); color: transparent`) for secondary headline lines

### Body / UI
**Barlow** — Google Fonts

- Weight: 300 (body copy), 400 (default), 500 (labels)
- Sentence case for prose
- Font size: 0.82rem – 0.9rem for body, 1rem+ for larger descriptors

### Data / System Labels
**Share Tech Mono** — Google Fonts

- Used for: system tags, section markers, status labels, data streams, footer build strings
- Always small (0.58rem – 0.72rem), always letter-spaced (`0.08em` – `0.2em`)
- Uppercase with underscores for multi-word values: `MODULE_STATUS: ACTIVE`, `COMP_SCI · FINAL_YEAR`
- Color: `var(--teal)` for active labels, `var(--text-light)` for passive labels

---

## Layout Principles

### Grid
- A fixed 48px sidebar on the left holds the scrolling data stream — all content is offset by this width.
- Main content max-width: `1200px`, centered with `padding: 0 64px`.
- Internal grids use `gap: 1px` with a `background: var(--border-t)` on the grid container to create hairline dividers between cells — never use individual cell borders for this.

### Spacing
Base unit: `8px`. Standard values: `8 16 20 24 28 32 36 40 48 56 64 80 88px`.

### Borders & Panels
- Use `border: 1px solid var(--border-t)` (teal-tinted) for structural borders.
- Use `border: 1px solid var(--border-w)` (white-tinted) for glass panels layered over backgrounds.
- No border-radius on structural elements (cards, cells, windows) — all corners are sharp at 0px.
- Frosted glass effect: `background: rgba(255,255,255,0.4); backdrop-filter: blur(10px–16px)`.

### Crosshair Corners
Overlay panels use corner markers (20×20px L-shaped borders) instead of a full rectangle border to create a wireframe / targeting reticle aesthetic. Apply to hero panels and large decorative containers.

---

## Background Treatment

### Fixed Grid
A CSS `background-image` grid of horizontal and vertical lines covers the entire viewport at `60px × 60px`:
```css
background-image:
  linear-gradient(var(--grid) 1px, transparent 1px),
  linear-gradient(90deg, var(--grid) 1px, transparent 1px);
background-size: 60px 60px;
```
This sits as a fixed overlay (`position: fixed; inset: 0; z-index: 0; pointer-events: none`).

### Gradient Wash
A subtle directional gradient on `body::before` adds depth:
- `rgba(90,202,174,0.08)` diagonal from top-left (seafoam hint)
- `rgba(62,168,200,0.12)` from top-right (teal wash)
- Radial ellipse at `70% 30%` for a soft light source feel

---

## Data Stream Sidebar

A fixed 48px left column displays scrolling monospace system data (hex codes, abbreviations, binary strings). This reinforces the technical aesthetic without distracting from content.

- Font: Share Tech Mono, 7px
- Color: `rgba(62,168,200,0.5)`
- Background: `rgba(15,42,56,0.07)` with a right border in `var(--border-t)`
- Animation: opacity pulse on individual items, no scroll transform (vertical rhythm via repeated items)

---

## Navigation

- Height: `60px`, sticky, `backdrop-filter: blur(16px)`
- Logo: Barlow Condensed 700, uppercase, `1.5rem`, with a light-weight secondary descriptor separated by a left border
- Nav links: bordered left/right with `var(--border-t)`, full-height hover fill `rgba(62,168,200,0.12)`
- Buttons: flat rectangles, no border-radius, 1px border. Ghost + solid pair flush together (overlapping `-1px` margin)

---

## Buttons

All buttons are rectangular (no border-radius).

| Variant | Background | Border | Text |
|---------|-----------|--------|------|
| Primary / Solid | `#3EA8C8` | same | `#F4FAFD` |
| Ghost | transparent | `rgba(62,168,200,0.3)` | `var(--text-mid)` |
| Glass | `rgba(255,255,255,0.3)` + blur | `rgba(62,168,200,0.3)` | `var(--text-mid)` |

- Font: Barlow Condensed 600, uppercase, letter-spacing `0.14em – 0.16em`
- Buttons placed side-by-side share a border with `margin-left: -1px` to avoid double borders
- Hover on primary: darken to `#2790B0`
- No shadows, no border-radius, no gradients

---

## Section Structure

Each content section follows the same header pattern:

1. **Section tag** — Share Tech Mono, `0.65rem`, teal, with a `16px` horizontal rule preceding it
2. **Heading** — Barlow Condensed 700, uppercase, tight leading (`line-height: 1`)
3. **Subheading** — Barlow Condensed italic, muted, with an em-dash prefix: `— Subtitle text here`

Section tags use a `::before` pseudo-element line:
```css
.section-tag::before {
  content: '';
  width: 16px; height: 1px;
  background: var(--teal);
}
```

---

## Components

### Feature Grid
Four equal columns, `gap: 0`, bordered via container background trick. Each cell has:
- A system number (`01 // NOTES`) in Share Tech Mono
- An icon in a 40×40px bordered square with teal-tinted background
- Name in Barlow Condensed uppercase
- Description in Barlow 300 light
- Footer label (`MODULE_STATUS: ACTIVE`) in Share Tech Mono

### Stat Cells
Large number in Barlow Condensed 700 teal, with a label below and a 2px progress bar at the bottom of each cell.

### Terminal / Chat Window
- Title bar: `rgba(62,168,200,0.1)` fill, teal uppercase label + live status indicator (pulsing dot + `ONLINE`)
- Messages have a `// USER INPUT` or `// LUMINA RESPONSE` mono label above the text
- User messages: dark tint, flush right. System messages: teal tint, flush left
- Input field: Share Tech Mono, `// INPUT QUERY` placeholder, 1px teal focus border
- Send button: solid teal, monospace `SEND →` label

### Scrolling Ticker
Full-width band with a pinned label cell on the left and a looping track of items separated by teal-tinted vertical borders. Items contain a 4px teal dot + uppercase condensed text. Animated with `translateX(-50%)` over ~28s.

### Testimonials
Grid cells share borders via the hairline grid technique. Each cell has an oversized `"` quote character in `rgba(62,168,200,0.25)`, light body text, and the author name in uppercase Barlow Condensed with a monospace role tag below.

---

## Micro-details

- Section dividers: `border-top: 1px solid var(--border-t)` on alternating sections with a faint background shift (`rgba(255,255,255,0.12)` to `rgba(15,42,56,0.04)`)
- Hover states on cells/cards: `background: rgba(255,255,255,0.35–0.45)` — subtle, no transform
- Live indicators: `animation: pulse 2s ease-in-out infinite` with opacity cycling `1 → 0.3 → 1`
- Decorative tag overlays on hero: Share Tech Mono, `0.62rem`, `rgba(62,168,200,0.7)`, positioned absolutely
- Footer: logo + build string (`BUILD_2026.1`) on the left; link bar with bordered items in the center; copyright on the right

---

## Things to Avoid

- No warm tones (no amber, orange, brown, cream)
- No rounded corners on structural elements
- No drop shadows
- No gradients on interactive elements
- No full-opacity fills — always use transparency for layering
- No serif fonts
- No sentence-case display text (headings are always uppercase)
- No emojis in UI chrome (use only in content/demo areas)
- No neon or glow effects — keep the palette desaturated and airy
