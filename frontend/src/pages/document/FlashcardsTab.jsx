import GenerationTab from './GenerationTab.jsx'
import FlashcardDeck from '../../components/FlashcardDeck.jsx'
import { getFlashcards } from '../../api/client.js'

// Persisted flashcards come back as a flat [{ question, answer }] list —
// the same shape the live job emits, so FlashcardDeck handles both.
function fetchExisting(docId) {
  return getFlashcards(docId).then((cards) =>
    Array.isArray(cards) && cards.length ? cards : null,
  )
}

export default function FlashcardsTab() {
  return (
    <GenerationTab
      kind="flashcard"
      title="Flashcards"
      noun="flashcards"
      fetchExisting={fetchExisting}
      renderResult={(cards) => <FlashcardDeck cards={cards} />}
      emptyHint="// NO_DECK_YET — generate a 10-card deck from this document. Click any card to flip."
    />
  )
}
