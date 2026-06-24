import GenerationTab from './GenerationTab.jsx'
import QuizView from '../../components/QuizView.jsx'
import { getQuizzes } from '../../api/client.js'

// A document can hold several quizzes; the API returns them newest-first
// (ordered by CreatedAt), so the first element is the most recent. The live job
// emits a single quiz of the same { title, questions } shape, so QuizView
// handles both.
function fetchExisting(docId) {
  return getQuizzes(docId).then((quizzes) =>
    Array.isArray(quizzes) && quizzes.length ? quizzes[0] : null,
  )
}

export default function QuizTab() {
  return (
    <GenerationTab
      kind="quiz"
      title="Quiz"
      noun="quiz"
      fetchExisting={fetchExisting}
      renderResult={(quiz) => <QuizView quiz={quiz} />}
      emptyHint="// NO_QUIZ_YET — generate 5 multiple-choice questions from this document. Pick an answer to check it."
    />
  )
}
