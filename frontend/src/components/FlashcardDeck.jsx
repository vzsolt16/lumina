import { useEffect, useState } from 'react'

// Teal L-shaped corner accents (mirrors the landing-page panels). Rendered
// inside each face so they rotate together with the card on flip.
function Corners() {
  return (
    <>
      <span className="fc-corner tl" />
      <span className="fc-corner tr" />
      <span className="fc-corner bl" />
      <span className="fc-corner br" />
    </>
  )
}

function FlipCard({ index, question, answer, dir }) {
  const [flipped, setFlipped] = useState(false)
  const num = String(index + 1).padStart(2, '0')
  return (
    <div
      className={`flip-card enter-${dir}${flipped ? ' flipped' : ''}`}
      onClick={() => setFlipped((f) => !f)}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if (e.key === 'Enter' || e.key === ' ') {
          e.preventDefault()
          setFlipped((f) => !f)
        }
      }}
    >
      <div className="flip-card-inner">
        <div className="flip-face flip-front">
          <div className="flip-content">
            <div className="flip-label">// CARD_{num} · QUESTION</div>
            <div className="flip-text">{question}</div>
          </div>
          <Corners />
        </div>
        <div className="flip-face flip-back">
          <div className="flip-content">
            <div className="flip-label">// CARD_{num} · ANSWER</div>
            <div className="flip-text">{answer}</div>
          </div>
          <Corners />
        </div>
      </div>
    </div>
  )
}

// One-at-a-time flashcard session. `cards` is [{ question, answer }, ...].
// A start screen gates the viewer; Next/Prev step through cards (Prev disabled
// on the first, Next becomes Finish on the last and returns to the start screen).
export default function FlashcardDeck({ cards }) {
  const [started, setStarted] = useState(false)
  const [index, setIndex] = useState(0)
  // Drives the slide-in direction of the entrance animation on navigation.
  const [dir, setDir] = useState('next')

  // Reset to the start screen whenever a new deck arrives (e.g. regenerate).
  useEffect(() => {
    setStarted(false)
    setIndex(0)
    setDir('next')
  }, [cards])

  if (!cards?.length) return null

  const total = cards.length
  const isLast = index === total - 1

  if (!started) {
    return (
      <div className="session-start">
        <div className="session-start-label">// DECK_READY · {total} CARDS</div>
        <button className="btn primary" onClick={() => setStarted(true)}>
          Start
        </button>
      </div>
    )
  }

  const card = cards[index]
  const num = String(index + 1).padStart(2, '0')
  // Fewer layers remain behind the card as we approach the end of the deck.
  const layersBehind = Math.min(2, total - 1 - index)

  return (
    <div className="session flashcard-session">
      <div className="session-bar">
        <span className="session-counter">CARD {num} / {total}</span>
        <button
          className="session-exit"
          onClick={() => {
            setStarted(false)
            setIndex(0)
          }}
        >
          EXIT
        </button>
      </div>

      <div className="session-stage">
        {/* the offset layers behind imply a deck; key by index so the flip
            state resets and the entrance animation replays on navigation */}
        <div className={`flashcard-stack layers-${layersBehind}`}>
          <FlipCard
            key={index}
            index={index}
            question={card.question}
            answer={card.answer}
            dir={dir}
          />
        </div>
      </div>

      <div className="session-nav">
        <button
          className="btn ghost"
          onClick={() => {
            setDir('prev')
            setIndex((i) => Math.max(0, i - 1))
          }}
          disabled={index === 0}
        >
          ‹ Prev
        </button>
        <button
          className="btn primary"
          onClick={() => {
            if (isLast) {
              setStarted(false)
              setIndex(0)
            } else {
              setDir('next')
              setIndex((i) => i + 1)
            }
          }}
        >
          {isLast ? 'Finish' : 'Next ›'}
        </button>
      </div>
    </div>
  )
}
