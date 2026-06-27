import { useEffect } from 'react'
import { useNavigate } from 'react-router-dom'
import Nav from '../components/Nav.jsx'
import Footer from '../components/Footer.jsx'
import { NotesIcon, FlashcardsIcon, QuizIcon, ChatIcon } from '../components/icons.jsx'
import './Landing.css'

const TICKER_ITEMS = [
  'Flashcard generation', 'AI-powered quizzes', 'Spaced repetition',
  'Study companion', 'Note processing', 'Progress tracking',
  'Multi-subject support', 'Instant card creation',
]

const FEATURES = [
  { num: '01 // NOTES', icon: NotesIcon, name: 'Notes', desc: 'Clean, distraction-free writing. Organise your material the way your mind works — Lumina keeps it structured without getting in the way.' },
  { num: '02 // FLASH', icon: FlashcardsIcon, name: 'Flashcards', desc: 'Auto-generated or hand-crafted. Spaced repetition built in. Study the right cards at the right time — the system decides, you focus on learning.' },
  { num: '03 // QUIZ', icon: QuizIcon, name: 'Quizzes', desc: 'Multiple choice, short answer — generated from your notes. Find the gaps before the exam does.' },
  { num: '04 // COMP', icon: ChatIcon, name: 'Study Corner', desc: 'An AI companion that explains, quizzes back, and talks concepts through. Not a chatbot. A study partner that knows your material.' },
]

const STEPS = [
  { num: '01', label: 'Input', title: 'Upload your material', desc: 'Drop a PDF, paste text, or write directly. Any subject. Any format. Lumina processes it immediately — no setup, no configuration.' },
  { num: '02', label: 'Generate', title: 'Build your study set', desc: 'One click generates flashcards and quizzes from your content. Review the output, adjust what you like, and start studying immediately.' },
  { num: '03', label: 'Execute', title: 'Study and track', desc: 'Flip cards, take tests, ask the companion. Your progress is tracked passively — you always know what needs more work without having to think about it.' },
]

const STATS = [
  { num: '12K+', label: 'Active students', width: '78%' },
  { num: '3×', label: 'Retention improvement', width: '100%' },
  { num: '50+', label: 'Subject areas', width: '55%' },
  { num: '4.9', label: 'Average rating', width: '98%' },
]

const TESTIMONIALS = [
  { text: 'I used to dread revision. Lumina turned my lecture slides into flashcards in minutes and the quiz feature caught every gap in my knowledge before the exam.', author: 'Sara M.', role: 'MEDICAL_STUDENT · YEAR_03' },
  { text: "The Study Corner is genuinely different. It doesn't just give you answers — it asks you questions back. I retained so much more just by talking through the material.", author: 'James K.', role: 'COMP_SCI · FINAL_YEAR' },
  { text: "It doesn't demand my attention. I open it, run my session, close it. That's exactly what studying should feel like — clean signal, no noise.", author: 'Priya D.', role: 'SELF_STUDY · LANGUAGES' },
]

const AI_BULLETS = [
  'Explains concepts in plain language',
  'Tests your understanding by asking back',
  'Creates flashcards from the conversation',
  'Knows your notes, not just the internet',
]

function Crosshairs({ full = true }) {
  return (
    <>
      <div className="crosshair" style={{ top: '-1px', left: '-1px' }} />
      <div className="crosshair tr" />
      {full && <div className="crosshair bl" />}
      {full && <div className="crosshair br" />}
    </>
  )
}

export default function Landing() {
  const navigate = useNavigate()
  const tickerItems = [...TICKER_ITEMS, ...TICKER_ITEMS]

  // Scroll-reveal motion. The hidden/animated states live behind
  // [data-motion="ready"] + a prefers-reduced-motion guard in CSS, so without
  // JS (or with reduced motion) every section renders visible at rest — the
  // reveal only ever *enhances* an already-painted default.
  useEffect(() => {
    const root = document.documentElement
    const targets = document.querySelectorAll('.reveal, .reveal-soft, .stagger')
    if (!targets.length) return

    root.setAttribute('data-motion', 'ready')

    const io = new IntersectionObserver(
      (entries, obs) => {
        for (const entry of entries) {
          if (entry.isIntersecting) {
            entry.target.classList.add('in-view')
            obs.unobserve(entry.target)
          }
        }
      },
      { threshold: 0.15, rootMargin: '0px 0px -8% 0px' },
    )

    targets.forEach((el) => io.observe(el))
    return () => {
      io.disconnect()
      root.removeAttribute('data-motion')
    }
  }, [])

  return (
    <>
      <Nav />

      {/* HERO */}
      <section className="hero">
        <div className="hero-bg-layer" aria-hidden="true">
          <div className="panel-a"><Crosshairs /></div>
          <div className="panel-b"><Crosshairs /></div>
          <div className="panel-c"><Crosshairs full={false} /></div>
        </div>

        <div className="hero-content stagger">
          <div className="hero-pre">AI-powered study companion · v2.0</div>
          <h1 className="hero-h1">
            LEARN<br />
            <span className="outline">DEEPER</span><br />
            RETAIN MORE
          </h1>
          <div className="hero-sub">Contemporary Study Systems</div>
          <p className="hero-desc">
            Lumina transforms your notes into active study tools — flashcards,
            quizzes, and an AI companion that works alongside you. Calm
            interface. Smart engine.
          </p>
          <div className="hero-actions">
            <button className="btn primary" onClick={() => navigate('/studio')}>
              Start free session
            </button>
            <a className="btn ghost" href="#how">View protocol</a>
          </div>
        </div>

        <div className="hero-card info-card reveal-soft" aria-hidden="true">
          <div className="card-label">Active learners</div>
          <div className="card-value">12,400</div>
          <div className="card-desc">students studying with Lumina this month</div>
          <div className="card-bar"><div className="card-bar-fill" /></div>
        </div>

        <div className="hero-card stat-card reveal-soft" aria-hidden="true">
          <div className="card-label">Avg. retention gain</div>
          <div className="card-value">3×</div>
          <div className="card-desc">improvement vs. passive reading</div>
          <div className="card-bar"><div className="card-bar-fill" style={{ width: '88%' }} /></div>
        </div>

        <div className="hero-tag t1" aria-hidden="true">SYS·LUMINA·2.0</div>
        <div className="hero-tag t2" aria-hidden="true">READY</div>
        <div className="hero-tag t3" aria-hidden="true">0xFF·STUDY·OK</div>
      </section>

      {/* TICKER */}
      <div className="ticker" aria-hidden="true">
        <div className="ticker-label">Lumina · Systems</div>
        <div className="ticker-track">
          {tickerItems.map((item, i) => (
            <div className="ticker-item" key={i}>
              <span className="ticker-dot" />
              {item}
            </div>
          ))}
        </div>
      </div>

      {/* FEATURES */}
      <section id="features" className="features-section">
        <div className="section">
          <div className="reveal">
            <div className="section-tag">Core modules</div>
            <h2 className="section-heading">Study tools.<br />Built for focus.</h2>
            <div className="section-sub">— Four components, one coherent system</div>
          </div>
          <div className="features-grid stagger">
            {FEATURES.map((f) => {
              const Icon = f.icon
              return (
              <div className="feat-cell" key={f.num}>
                <div className="feat-num">{f.num}</div>
                <div className="feat-icon-wrap"><Icon /></div>
                <div className="feat-name">{f.name}</div>
                <p className="feat-desc">{f.desc}</p>
                <div className="feat-cell-footer">MODULE_STATUS: ACTIVE</div>
              </div>
              )
            })}
          </div>
        </div>
      </section>

      {/* HOW IT WORKS */}
      <section id="how" className="section">
        <div className="reveal">
          <div className="section-tag">Protocol</div>
          <h2 className="section-heading">Three-step<br />study sequence</h2>
          <div className="section-sub">— From raw notes to active retention</div>
        </div>
        <div className="how-grid stagger">
          {STEPS.map((s) => (
            <div className="how-cell" key={s.num}>
              <div className="how-num" aria-hidden="true">{s.num}</div>
              <div className="how-step-label">{s.label}</div>
              <div className="how-title">{s.title}</div>
              <p className="how-desc">{s.desc}</p>
            </div>
          ))}
        </div>
      </section>

      {/* STATS */}
      <div className="stats-band">
        <div className="stats-inner stagger">
          {STATS.map((s) => (
            <div className="stat-cell" key={s.label}>
              <div className="stat-num">{s.num}</div>
              <div className="stat-label">{s.label}</div>
              <div className="stat-bar"><div className="stat-bar-fill" style={{ width: s.width }} /></div>
            </div>
          ))}
        </div>
      </div>

      {/* AI COMPANION */}
      <section id="companion" className="section">
        <div className="ai-grid">
          <div className="reveal">
            <div className="section-tag">AI Unit · 04</div>
            <h2 className="section-heading">The companion<br />that asks back</h2>
            <div className="section-sub">— More than answers. Active dialogue.</div>
            <p className="ai-intro">
              Lumina's Study Corner doesn't just retrieve information — it
              engages. Ask a question, get an explanation. Ask again, and it
              checks if you understood. It's studying, not searching.
            </p>
            <ul className="ai-bullets">
              {AI_BULLETS.map((b) => (
                <li key={b}><span />{b}</li>
              ))}
            </ul>
          </div>
          <div className="reveal">
            <div className="ai-window">
              <div className="ai-window-bar">
                <div className="ai-window-title">LUMINA · Study Corner · Session active</div>
                <div className="ai-window-status">
                  <div className="status-dot" />
                  ONLINE
                </div>
              </div>
              <div className="ai-window-body">
                <div className="ai-msg user">
                  <div className="ai-msg-label">// USER INPUT</div>
                  Explain the difference between mitosis and meiosis.
                </div>
                <div className="ai-msg system">
                  <div className="ai-msg-label">// LUMINA RESPONSE</div>
                  Mitosis is your body's copy machine — it creates identical
                  cells for growth and repair. Meiosis is for reproduction only:
                  it halves the chromosome count to produce sperm and egg cells,
                  shuffling the genetic deck in the process.
                </div>
                <div className="ai-msg user">
                  <div className="ai-msg-label">// USER INPUT</div>
                  So meiosis only happens in the gonads?
                </div>
                <div className="ai-msg system">
                  <div className="ai-msg-label">// LUMINA RESPONSE</div>
                  Correct. Want me to generate a flashcard pair to lock this in?
                </div>
              </div>
              <div className="ai-input-row">
                <input className="ai-input" type="text" placeholder="// INPUT QUERY" aria-label="Study Corner demo input" />
                <button className="ai-send" tabIndex={-1}>SEND →</button>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* TESTIMONIALS */}
      <section className="testi-section">
        <div className="section">
          <div className="reveal">
            <h2 className="section-heading">Field notes</h2>
            <div className="section-sub">— From students currently in the system</div>
          </div>
          <div className="testi-grid stagger">
            {TESTIMONIALS.map((t) => (
              <div className="testi-cell" key={t.author}>
                <div className="testi-quote" aria-hidden="true">"</div>
                <p className="testi-text">{t.text}</p>
                <div className="testi-author">{t.author}</div>
                <div className="testi-role">{t.role}</div>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* CTA */}
      <div className="cta-band">
        <div className="cta-inner">
          <div className="reveal">
            <h2 className="cta-heading">
              Begin your<br />
              <span className="line2">first session</span>
            </h2>
            <div className="cta-note">FREE_ACCESS · NO_CARD · NO_CONFIG · WORKS_NOW</div>
          </div>
          <div className="cta-actions reveal">
            <button className="btn-cta main" onClick={() => navigate('/studio')}>
              Create free account
            </button>
            <button
              className="btn-cta alt"
              onClick={() => document.getElementById('how')?.scrollIntoView({ behavior: 'smooth' })}
            >
              See how it works
            </button>
          </div>
        </div>
      </div>

      <Footer />
    </>
  )
}
