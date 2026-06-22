import { useEffect, useState } from 'react'

function optionsOf(q) {
  return [
    { letter: 'A', text: q.answerA },
    { letter: 'B', text: q.answerB },
    { letter: 'C', text: q.answerC },
    { letter: 'D', text: q.answerD },
  ]
}

function Question({ index, q, selected, onSelect }) {
  const num = String(index + 1).padStart(2, '0')
  const answered = selected != null
  const correct = q.correctAnswer

  return (
    <div className="quiz-q">
      <div className="quiz-q-num">QUESTION_{num}</div>
      <div className="quiz-q-text">{q.question}</div>
      {optionsOf(q).map((opt) => {
        let cls = 'quiz-opt'
        if (answered) {
          if (opt.letter === correct) cls += ' correct'
          else if (opt.letter === selected) cls += ' wrong'
        }
        return (
          <button
            key={opt.letter}
            className={cls}
            disabled={answered}
            onClick={() => onSelect(opt.letter)}
          >
            <span className="quiz-opt-letter">{opt.letter}</span>
            <span>{opt.text}</span>
          </button>
        )
      })}
      {answered && (
        <div role="status" className={`quiz-feedback ${selected === correct ? 'ok' : 'no'}`}>
          {selected === correct
            ? '// CORRECT'
            : `// INCORRECT · ANSWER: ${correct}`}
        </div>
      )}
    </div>
  )
}

// One-at-a-time multiple-choice quiz session. `quiz` is { title, questions: [...] }.
// A start screen gates the viewer; one question shows at a time. Selecting an
// option locks that question and reveals the correct answer; Next/Prev step
// through (unanswered questions can be skipped). The last question's Next
// finishes the quiz and shows a results screen.
export default function QuizView({ quiz }) {
  const questions = quiz?.questions ?? []
  const [started, setStarted] = useState(false)
  const [finished, setFinished] = useState(false)
  const [index, setIndex] = useState(0)
  const [answers, setAnswers] = useState({})

  // Reset to the start screen whenever a new quiz arrives (e.g. regenerate).
  useEffect(() => {
    setStarted(false)
    setFinished(false)
    setIndex(0)
    setAnswers({})
  }, [quiz])

  if (!questions.length) return null

  const total = questions.length
  const title = quiz.title || 'Quiz'

  if (!started) {
    return (
      <div className="session-start">
        <div className="quiz-title">{title}</div>
        <div className="session-start-label">// QUIZ_READY · {total} QUESTIONS</div>
        <button className="btn primary" onClick={() => setStarted(true)}>
          Start
        </button>
      </div>
    )
  }

  if (finished) {
    const score = questions.reduce(
      (acc, q, i) => (answers[i] === q.correctAnswer ? acc + 1 : acc),
      0,
    )
    return (
      <div className="session-results">
        <div className="quiz-title">{title}</div>
        <div className="session-score">SCORE: {score} / {total}</div>
        <div className="session-nav">
          <button
            className="btn primary"
            onClick={() => {
              setAnswers({})
              setIndex(0)
              setFinished(false)
            }}
          >
            Restart
          </button>
          <button
            className="btn ghost"
            onClick={() => {
              setAnswers({})
              setIndex(0)
              setFinished(false)
              setStarted(false)
            }}
          >
            Exit
          </button>
        </div>
      </div>
    )
  }

  const isLast = index === total - 1
  const num = String(index + 1).padStart(2, '0')

  return (
    <div className="session">
      <div className="session-bar">
        <span className="session-counter">QUESTION {num} / {total}</span>
        <button
          className="session-exit"
          onClick={() => {
            setAnswers({})
            setIndex(0)
            setStarted(false)
          }}
        >
          EXIT
        </button>
      </div>

      <Question
        index={index}
        q={questions[index]}
        selected={answers[index] ?? null}
        onSelect={(letter) => setAnswers((a) => ({ ...a, [index]: letter }))}
      />

      <div className="session-nav">
        <button
          className="btn ghost"
          onClick={() => setIndex((i) => Math.max(0, i - 1))}
          disabled={index === 0}
        >
          ‹ Prev
        </button>
        <button
          className="btn primary"
          onClick={() => {
            if (isLast) setFinished(true)
            else setIndex((i) => i + 1)
          }}
        >
          {isLast ? 'Finish' : 'Next ›'}
        </button>
      </div>
    </div>
  )
}
