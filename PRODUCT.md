# Product

## Register

product

## Users

Students studying from their own material — primarily university / CS-type learners who
upload their own notes, lecture handouts, and readings (TXT, MD, PDF). Their context is a
focused study session: they have a specific document in front of them and a goal (review
before an exam, drill recall, clarify something they don't understand). The job to be done
is **turn my own documents into active-recall practice** — flashcards, quizzes, and a chat
that answers from the source — without leaving the app or sending their material to a cloud
service (generation runs on a locally hosted LLM via Ollama).

## Product Purpose

Lumina is an AI-powered study companion that helps people learn from documents they already
have. Upload a file, and it generates flashcards and quizzes from it and lets you chat with
it, with answers streamed in real time. It exists because the gap between "I have the
material" and "I've actually studied it" is the hard part — Lumina closes it by converting
passive documents into the active-recall formats that actually build retention, all locally.

This is a **portfolio / learning project**. Success is a polished, demonstrable full-stack
app that shows craft end to end: a real auth flow, streaming AI over WebSockets, a coherent
document-centric workspace, and a distinctive, consistent interface. The bar is "this looks
and works like something built by someone who knows what they're doing," not user growth
metrics.

## Brand Personality

**Sharp, capable, precise.** Lumina should feel like a well-made instrument — fast,
technical, and trustworthy. Confident without shouting. The voice is direct and competent:
system-style labels, clear status, no filler and no hand-holding cheerfulness. It respects
that the user came to work. Three words: **precise, technical, composed.**

This is already carried visually by the established "Gen X Soft Club" system (see
`DESIGN.md` at the project root): airy teal-on-pale-blue glass, hairline borders, sharp
corners, monospace data labels — *clean signal, no noise.* Personality and visuals agree:
the interface signals competence rather than personality-as-decoration.

## Anti-references

- **Generic AI-chat SaaS** — purple/indigo gradients, the hero-metric template, glassy cards
  everywhere, "✨ powered by AI" sparkle. The saturated 2026 AI-slop look. Lumina's AI is a
  tool, not a marketing badge.
- **Gamified ed-tech (Duolingo-style)** — mascots, streaks, confetti, badges, playful
  rounded cartoon UI. No gamification reflex; recall is the reward.
- **Consumer flashcard apps (Quizlet-style)** — busy, ad-heavy, candy-colored, social-feed
  clutter. Lumina is calm and single-purpose.
- Also off-limits (from the visual system): warm tones, rounded corners on structure, drop
  shadows, gradient text, neon/glow, serif fonts.

## Design Principles

1. **Clean signal, no noise.** Every element earns its place. Decoration that doesn't carry
   information (status, structure, hierarchy) gets cut. The technical chrome is a language,
   not ornament.
2. **The document is the subject.** Flashcards, quiz, and chat are views onto one document.
   The UI keeps the user oriented to *which* document and *what* they're doing with it; the
   workspace never makes them hunt for context.
3. **Show the work.** Generation and chat are async and streamed — make state legible.
   Progress, "online", streaming tokens, completion: the user always knows what the machine
   is doing and that it's working, never a frozen spinner with no story.
4. **Competence over cheerfulness.** Tone and motion stay composed. Guide and reassure
   through clarity and responsiveness, not mascots, exclamation marks, or celebration.
5. **Local and private by posture.** The product runs AI locally; the design shouldn't
   undercut that trust with cloud-SaaS tropes or anything that implies the material leaves
   the machine.

## Accessibility & Inclusion

- **WCAG 2.1 AA.** Body text ≥4.5:1 against its background; large/bold text ≥3:1. This needs
  active vigilance given the low-contrast teal-on-pale-blue palette — the muted `--text-light`
  slate is the likely failure point for body and placeholder text; keep prose toward
  `--text-dark` / `--text-mid`.
- **Color-blind safe.** Never rely on color alone to carry meaning. Quiz correct/incorrect
  states, live/offline status dots, and progress/error states must pair color with an icon,
  shape, or text label.
- **Reduced motion respected.** Every animation (data-stream pulse, ticker, streaming
  indicators, progress fills) needs a `prefers-reduced-motion: reduce` alternative — a
  crossfade or static state, never motion as the only way state is conveyed.
