import { useState } from 'react'

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
        <div className={`quiz-feedback ${selected === correct ? 'ok' : 'no'}`}>
          {selected === correct
            ? '// CORRECT'
            : `// INCORRECT · ANSWER: ${correct}`}
        </div>
      )}
    </div>
  )
}

// Interactive multiple-choice quiz. `quiz` is { title, questions: [...] }.
// Selecting an option locks the question and reveals the correct answer.
export default function QuizView({ quiz }) {
  const questions = quiz?.questions ?? []
  const [answers, setAnswers] = useState({})

  if (!questions.length) return null

  const answeredCount = Object.keys(answers).length
  const score = questions.reduce(
    (acc, q, i) => (answers[i] === q.correctAnswer ? acc + 1 : acc),
    0,
  )

  return (
    <div>
      <div className="quiz-title">{quiz.title || 'Quiz'}</div>
      <div className="quiz-score">
        SCORE: {score} / {questions.length} · ANSWERED: {answeredCount} /{' '}
        {questions.length}
      </div>
      {questions.map((q, i) => (
        <Question
          key={i}
          index={i}
          q={q}
          selected={answers[i] ?? null}
          onSelect={(letter) => setAnswers((a) => ({ ...a, [i]: letter }))}
        />
      ))}
    </div>
  )
}
