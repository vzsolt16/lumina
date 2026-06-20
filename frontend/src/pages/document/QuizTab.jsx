import GenerationTab from './GenerationTab.jsx'
import QuizView from '../../components/QuizView.jsx'
import { getQuizzes } from '../../api/client.js'

// A document can hold several quizzes; entities carry no timestamp, so we treat
// the last one returned as the most recent. The live job emits a single quiz of
// the same { title, questions } shape, so QuizView handles both.
function fetchExisting(docId) {
  return getQuizzes(docId).then((quizzes) =>
    Array.isArray(quizzes) && quizzes.length ? quizzes[quizzes.length - 1] : null,
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
