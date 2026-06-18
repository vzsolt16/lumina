import { useState } from 'react'

function FlipCard({ index, question, answer }) {
  const [flipped, setFlipped] = useState(false)
  const num = String(index + 1).padStart(2, '0')
  return (
    <div
      className={`flip-card${flipped ? ' flipped' : ''}`}
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
          <div className="flip-label">// CARD_{num} · QUESTION</div>
          <div className="flip-text">{question}</div>
        </div>
        <div className="flip-face flip-back">
          <div className="flip-label">// CARD_{num} · ANSWER</div>
          <div className="flip-text">{answer}</div>
        </div>
      </div>
    </div>
  )
}

// Grid of click-to-flip flashcards. `cards` is [{ question, answer }, ...].
export default function FlashcardDeck({ cards }) {
  if (!cards?.length) return null
  return (
    <div className="deck-grid">
      {cards.map((c, i) => (
        <FlipCard key={i} index={i} question={c.question} answer={c.answer} />
      ))}
    </div>
  )
}
