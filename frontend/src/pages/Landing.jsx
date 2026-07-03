import { useEffect, useRef } from 'react'
import { Link } from 'react-router-dom'
import './Landing.css'

const TRACKS = [
  { no: '01', name: 'upload', desc: 'add your notes or pdfs to start a new study session.', time: '0:07' },
  { no: '02', name: 'flashcards', desc: 'automatically generated from your material — focus on remembering, not creating cards.', time: '∞' },
  { no: '03', name: 'quiz', desc: 'test your understanding with questions generated directly from your notes.', time: '12:00' },
  { no: '04', name: 'chat', desc: 'ask questions about your study material and explore topics without digging through pages of notes.', time: 'live' },
  { no: '05', name: 'repeat', desc: 'until it sticks. the deck remembers where you left off.', time: '--:--' },
]

const SPECS = [
  { key: 'track 02', val: 'flashcards', note: 'automatically generated from your material.' },
  { key: 'track 03', val: 'quizzes', note: 'built from your notes.' },
  { key: 'track 04', val: 'chat', note: 'ask follow-up questions anytime.' },
  { key: 'session', val: 'progress', note: 'continue where you left off.' },
]

const TICKER = 'active recall · flashcards · quizzes · ai chat · study sessions · spaced repetition · exam prep · learn faster ·'

/* ----------------------------------------------------------------
   tiny 3d wireframe engine (no dependencies) — models are line
   segments in unit space; render spins/tilts/projects per frame.
   ---------------------------------------------------------------- */
function buildDisc() {
  const segs = []
  const ring = (r, n, w) => {
    for (let i = 0; i < n; i++) {
      const a1 = (i / n) * Math.PI * 2
      const a2 = ((i + 1) / n) * Math.PI * 2
      segs.push({ a: [r * Math.cos(a1), r * Math.sin(a1), 0], b: [r * Math.cos(a2), r * Math.sin(a2), 0], w })
    }
  }
  const arc = (r, a0, a1, n, w) => {
    for (let i = 0; i < n; i++) {
      const t1 = a0 + (a1 - a0) * (i / n)
      const t2 = a0 + (a1 - a0) * ((i + 1) / n)
      segs.push({ a: [r * Math.cos(t1), r * Math.sin(t1), 0], b: [r * Math.cos(t2), r * Math.sin(t2), 0], w })
    }
  }
  ring(1.0, 120, 1)
  ring(0.97, 120, 0.5)
  ring(0.38, 60, 0.9)
  ring(0.35, 60, 0.5)
  ring(0.13, 40, 1)
  for (let i = 0; i < 24; i++) {
    const a = (i / 24) * Math.PI * 2
    segs.push({ a: [0.38 * Math.cos(a), 0.38 * Math.sin(a), 0], b: [0.97 * Math.cos(a), 0.97 * Math.sin(a), 0], w: 0.35 })
  }
  // pseudo-random "data track" arcs — seeded so the disc is stable
  let seed = 7
  const rnd = () => { seed = (seed * 16807) % 2147483647; return seed / 2147483647 }
  for (let k = 0; k < 14; k++) {
    const r = 0.42 + rnd() * 0.5
    const start = rnd() * Math.PI * 2
    const span = 0.5 + rnd() * 2.2
    arc(r, start, start + span, Math.ceil(span * 14), 0.65)
  }
  return segs
}

function buildIco() {
  const p = (1 + Math.sqrt(5)) / 2
  const s = 1 / Math.sqrt(1 + p * p)
  const a = s, c = p * s
  const v = [
    [-a, c, 0], [a, c, 0], [-a, -c, 0], [a, -c, 0],
    [0, -a, c], [0, a, c], [0, -a, -c], [0, a, -c],
    [c, 0, -a], [c, 0, a], [-c, 0, -a], [-c, 0, a],
  ].map((q) => [q[0] * 0.78, q[1] * 0.78, q[2] * 0.78])
  const faces = [
    [0, 11, 5], [0, 5, 1], [0, 1, 7], [0, 7, 10], [0, 10, 11], [1, 5, 9], [5, 11, 4], [11, 10, 2],
    [10, 7, 6], [7, 1, 8], [3, 9, 4], [3, 4, 2], [3, 2, 6], [3, 6, 8], [3, 8, 9], [4, 9, 5],
    [2, 4, 11], [6, 2, 10], [8, 6, 7], [9, 8, 1],
  ]
  const segs = []
  const seen = {}
  faces.forEach((f) => {
    for (let i = 0; i < 3; i++) {
      const x = f[i], y = f[(i + 1) % 3]
      const key = Math.min(x, y) + '-' + Math.max(x, y)
      if (!seen[key]) { seen[key] = 1; segs.push({ a: v[x], b: v[y], w: 1 }) }
    }
  })
  return segs
}

function makeScene(canvas, segs, opts, getScrollFrac, reduced) {
  const ctx = canvas.getContext('2d')
  let dpr = 1

  const fit = () => {
    dpr = Math.min(window.devicePixelRatio || 1, 2)
    const rect = canvas.parentElement.getBoundingClientRect()
    const size = Math.max(rect.width, 10)
    canvas.width = size * dpr
    canvas.height = size * dpr
  }
  fit()

  const render = (t) => {
    const w = canvas.width, h = canvas.height
    ctx.clearRect(0, 0, w, h)
    const cx = w / 2, cy = h / 2
    const R = (w / 2) * 0.86
    const sf = getScrollFrac()
    const spin = opts.spin(t, sf)
    const tilt = opts.tilt(t, sf)
    const cs = Math.cos(spin), ss = Math.sin(spin)
    const ct = Math.cos(tilt), st = Math.sin(tilt)
    const persp = 3.1

    const proj = (pt) => {
      const x = pt[0] * cs - pt[1] * ss
      const y = pt[0] * ss + pt[1] * cs
      const y2 = y * ct - pt[2] * st
      const z2 = y * st + pt[2] * ct
      const k = persp / (persp - z2)
      return [cx + x * R * k, cy + y2 * R * k, z2]
    }

    // two passes: seafoam fringe offset, then cyan main — cheap chromatic wash
    for (let pass = 0; pass < 2; pass++) {
      ctx.lineWidth = dpr
      for (let i = 0; i < segs.length; i++) {
        const sgm = segs[i]
        const A = proj(sgm.a), B = proj(sgm.b)
        const depth = (A[2] + B[2]) / 2
        const alpha = (0.28 + 0.55 * (depth + 1) / 2) * sgm.w
        ctx.beginPath()
        if (pass === 0) {
          ctx.strokeStyle = 'rgba(127,230,180,' + (alpha * 0.28).toFixed(3) + ')'
          ctx.moveTo(A[0] + 2.5 * dpr, A[1])
          ctx.lineTo(B[0] + 2.5 * dpr, B[1])
        } else {
          ctx.strokeStyle = 'rgba(111,216,238,' + alpha.toFixed(3) + ')'
          ctx.moveTo(A[0], A[1])
          ctx.lineTo(B[0], B[1])
        }
        ctx.stroke()
      }
    }
  }

  const onResize = () => { fit(); if (reduced) render(1.2) }
  window.addEventListener('resize', onResize)

  if (reduced) {
    render(1.2)
    return () => window.removeEventListener('resize', onResize)
  }

  let visible = true
  const io = new IntersectionObserver((en) => { visible = en[0].isIntersecting })
  io.observe(canvas)

  let raf
  const loop = (now) => {
    if (visible) render(now / 1000)
    raf = requestAnimationFrame(loop)
  }
  raf = requestAnimationFrame(loop)

  return () => {
    cancelAnimationFrame(raf)
    io.disconnect()
    window.removeEventListener('resize', onResize)
  }
}

export default function Landing() {
  const rootRef = useRef(null)
  const discRef = useRef(null)
  const solidRef = useRef(null)
  const meterFillRef = useRef(null)
  const meterPctRef = useRef(null)
  const scrollFracRef = useRef(0)

  useEffect(() => {
    const root = rootRef.current
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches

    /* reveals — hidden states only exist behind [data-motion="ready"], so the
       default render is fully visible; motion only ever enhances it */
    root.setAttribute('data-motion', 'ready')
    const revealed = Array.from(root.querySelectorAll('.rv'))
    const inView = revealed.filter((el) => el.getBoundingClientRect().top < window.innerHeight)
    let raf1, raf2
    raf1 = requestAnimationFrame(() => {
      raf2 = requestAnimationFrame(() => inView.forEach((el) => el.classList.add('in')))
    })

    let io
    if ('IntersectionObserver' in window && !reduced) {
      io = new IntersectionObserver((entries) => {
        entries.forEach((e) => {
          if (e.isIntersecting) { e.target.classList.add('in'); io.unobserve(e.target) }
        })
      }, { threshold: 0.12, rootMargin: '0px 0px -8% 0px' })
      revealed.forEach((el) => io.observe(el))
    } else {
      revealed.forEach((el) => el.classList.add('in'))
    }

    /* session meter + hero pane parallax */
    const panes = Array.from(root.querySelectorAll('[data-px]'))
    const onScroll = () => {
      const max = document.documentElement.scrollHeight - window.innerHeight
      const frac = max > 0 ? window.scrollY / max : 0
      scrollFracRef.current = frac
      if (meterPctRef.current) meterPctRef.current.textContent = String(Math.round(frac * 100)).padStart(3, '0') + '%'
      if (meterFillRef.current) meterFillRef.current.style.transform = 'scaleX(' + frac + ')'
      if (!reduced) {
        panes.forEach((p) => {
          p.style.translate = '0 ' + (window.scrollY * parseFloat(p.getAttribute('data-px'))) + 'px'
        })
      }
    }
    window.addEventListener('scroll', onScroll, { passive: true })
    onScroll()

    /* wireframe scenes */
    const getFrac = () => scrollFracRef.current
    const stopDisc = makeScene(discRef.current, buildDisc(), {
      spin: (t, sf) => t * 0.16 + sf * 4.2,
      tilt: (t, sf) => 1.12 + sf * 0.5 + Math.sin(t * 0.2) * 0.05,
    }, getFrac, reduced)
    const stopSolid = makeScene(solidRef.current, buildIco(), {
      spin: (t, sf) => t * 0.3 + sf * 2.0,
      tilt: (t) => 0.7 + Math.sin(t * 0.24) * 0.3,
    }, getFrac, reduced)

    return () => {
      cancelAnimationFrame(raf1)
      cancelAnimationFrame(raf2)
      io?.disconnect()
      window.removeEventListener('scroll', onScroll)
      stopDisc()
      stopSolid()
      root.removeAttribute('data-motion')
    }
  }, [])

  return (
    <div className="sleeve" ref={rootRef} id="top">
      <header className="sl-nav">
        <a className="sl-nav__brand" href="#top">
          <span className="sl-nav__logo">lumina</span>
          <span className="tag tag--dim">lmn·001</span>
        </a>
        <span className="sl-nav__cat tag tag--dim">study sessions</span>
        <div className="sl-nav__meter" aria-hidden="true">
          <span className="tag tag--dim lbl">session</span>
          <span className="bar"><i ref={meterFillRef} /></span>
          <span className="tag" ref={meterPctRef}>000%</span>
        </div>
        <Link className="sl-nav__play" to="/register">play</Link>
      </header>

      {/* ============================ hero ============================ */}
      <section className="hero">
        <div className="hero__marks" aria-hidden="true"><i /><i /><i /><i /></div>
        <div className="hero__pane hero__pane--1" data-px="0.12" aria-hidden="true" />
        <div className="hero__pane hero__pane--2" data-px="0.2" aria-hidden="true" />
        <div className="hero__pane hero__pane--3" data-px="0.06" aria-hidden="true" />

        <div className="hero__disc" aria-hidden="true"><canvas ref={discRef} /></div>

        <div className="hero__corner hero__corner--tr">
          stereo · 44.1&nbsp;khz<br />limited edition
        </div>
        <div className="hero__corner hero__corner--br">
          47.4979°&nbsp;n · 19.0402°&nbsp;e<br />rendered locally
        </div>

        <div className="hero__head">
          <h1 className="hero__title rv">lumina</h1>
          <div className="hero__rules">
            <div className="rule-tag rule-tag--left rv" data-d="1"><span className="tag">study tools</span></div>
            <div className="rule-tag rule-tag--left rv" data-d="2"><span className="tag tag--dim">flashcards&nbsp;·&nbsp;quizzes&nbsp;·&nbsp;chat</span></div>
          </div>
        </div>

        <div className="hero__foot">
          <p className="hero__vol rv" data-d="2">study sessions <strong>vol.&nbsp;1</strong> — turn your notes into flashcards, quizzes, and conversations.</p>
          <p className="hero__promo rv" data-d="3">put your notes on repeat.</p>
          <div className="hero__cta rv" data-d="4">
            <Link className="btn btn--solid" to="/register"><span className="tri" aria-hidden="true" />press play</Link>
            <a className="btn btn--ghost" href="#tracklist">browse tracklist</a>
          </div>
        </div>
      </section>

      {/* =========================== ticker =========================== */}
      <div className="ticker" aria-hidden="true">
        <div className="ticker__inner">
          <span>{TICKER}</span>
          <span>{TICKER}</span>
        </div>
      </div>

      {/* ========================= liner notes ======================== */}
      <section className="sec liner" id="liner">
        <div className="sec__in">
          <div className="rule-tag rule-tag--left rv" style={{ marginBottom: '38px' }}><span className="tag">liner notes</span></div>
          <div className="liner__grid">
            <h2 className="liner__lede rv">you've had the file for weeks. <em>it's still not in your head.</em></h2>
            <div className="liner__body rv" data-d="2">
              <p>lumina turns your study material into interactive practice. upload lecture notes, slides, or pdfs and instantly study them with <strong>flashcards, quizzes, and ai-powered conversations</strong>.</p>
              <p>no complicated setup. just bring your material and start studying.</p>
            </div>
          </div>
        </div>
      </section>

      {/* ========================= tracklist ========================== */}
      <section className="sec tracks" id="tracklist">
        <div className="sec__in">
          <div className="tracks__head rv">
            <h2 className="tracks__side">side a</h2>
            <span className="tag tag--dim">the material</span>
            <span className="tracks__note tag tag--dim">total running time — one exam season</span>
          </div>

          {TRACKS.map((t, i) => (
            <Link className="track rv rv--l" data-d={i || undefined} to="/register" key={t.no}>
              <span className="track__no">{t.no}</span>
              <span className="track__main">
                <span className="track__name">{t.name}</span>
                <span className="track__desc">{t.desc}</span>
              </span>
              <span className="track__time">
                <span className="track__eq" aria-hidden="true"><i /><i /><i /></span>
                {t.time}
              </span>
            </Link>
          ))}
        </div>
      </section>

      {/* ================ thesis — retrieval beats rereading ========== */}
      <section className="sec thesis" id="thesis">
        <div className="sec__in">
          <div className="thesis__frame rv">
            <i aria-hidden="true" /><i aria-hidden="true" /><i aria-hidden="true" /><i aria-hidden="true" />
            <h2 className="thesis__line">reading isn't enough. <em>retrieval is.</em></h2>
            <p className="thesis__body rv" data-d="2">research consistently shows that <strong>actively recalling information beats passive rereading</strong> for long-term retention. lumina helps you practice recall by turning your notes into flashcards, quizzes, and conversations — all from the material you're already studying.</p>
          </div>
        </div>
      </section>

      {/* ================ interlude — built for studying =============== */}
      <section className="sec frost" id="interlude">
        <div className="sec__in">
          <div className="frost__head">
            <div className="rule-tag rule-tag--left rv"><span className="tag tag--ink">interlude — built for studying</span></div>
            <h2 className="frost__title rv rv--blur" data-d="1">everything you need in one session</h2>
          </div>
          <p className="frost__copy rv" data-d="2">lumina keeps everything in one place. read your material, review flashcards, test yourself with quizzes, and <strong>ask questions whenever you get stuck</strong> — switching between study tools shouldn't interrupt your focus.</p>

          <div className="spec-grid">
            {SPECS.map((s, i) => (
              <div className="spec rv" data-d={i + 1} key={s.val}>
                <span className="spec__key">{s.key}</span>
                <span className="spec__val">{s.val}</span>
                <p className="spec__note">{s.note}</p>
              </div>
            ))}
          </div>
        </div>
      </section>

      {/* ===================== side b — the loop ====================== */}
      <section className="sec sideb" id="sideb">
        <div className="sec__in">
          <div className="sideb__head rv">
            <h2 className="sideb__side">side b</h2>
            <span className="tag tag--acid">the loop</span>
          </div>
          <div className="sideb__grid">
            <div>
              <div className="loop-words" aria-label="read, drill, ask, repeat">
                <span className="rv rv--l">read.</span>
                <span className="rv rv--l" data-d="1">drill.</span>
                <span className="rv rv--l" data-d="2">ask.</span>
                <span className="rv rv--l" data-d="3"><b>repeat.</b></span>
              </div>
              <p className="sideb__copy rv" data-d="4">stop whenever you want and continue later. <strong>your flashcards, quizzes, and conversations are ready when you come back.</strong></p>
            </div>
            <div className="sideb__solid rv" data-d="2">
              <canvas ref={solidRef} aria-hidden="true" />
              <span className="tag tag--acid">recall_object · rotating</span>
            </div>
          </div>
        </div>
      </section>

      {/* ============================ outro =========================== */}
      <section className="sec outro" id="outro">
        <div className="sec__in">
          <div className="rule-tag outro__kick rv"><span className="tag">final track</span></div>
          <h2 className="outro__title rv" data-d="1">press play</h2>
          <p className="outro__sub rv" data-d="2">the session starts when you do. bring your notes — we'll help you learn them.</p>
          <div className="outro__cta rv" data-d="3">
            <Link className="btn btn--solid" to="/register"><span className="tri" aria-hidden="true" />begin session</Link>
            <Link className="btn btn--ghost" to="/login">resume session</Link>
          </div>

          <div className="outro__cat rv" data-d="2">
            <div className="barcode" aria-hidden="true">
              <svg width="150" height="44" viewBox="0 0 150 44" fill="none">
                <g fill="#9fcfdd">
                  <rect x="0" width="3" height="44" /><rect x="5" width="1" height="44" /><rect x="9" width="2" height="44" />
                  <rect x="14" width="1" height="44" /><rect x="17" width="4" height="44" /><rect x="23" width="1" height="44" />
                  <rect x="27" width="2" height="44" /><rect x="31" width="3" height="44" /><rect x="36" width="1" height="44" />
                  <rect x="40" width="1" height="44" /><rect x="44" width="2" height="44" /><rect x="49" width="4" height="44" />
                  <rect x="55" width="1" height="44" /><rect x="59" width="3" height="44" /><rect x="64" width="1" height="44" />
                  <rect x="68" width="2" height="44" /><rect x="72" width="1" height="44" /><rect x="76" width="3" height="44" />
                  <rect x="82" width="1" height="44" /><rect x="85" width="2" height="44" /><rect x="90" width="4" height="44" />
                  <rect x="96" width="1" height="44" /><rect x="100" width="2" height="44" /><rect x="105" width="1" height="44" />
                  <rect x="109" width="3" height="44" /><rect x="114" width="1" height="44" /><rect x="118" width="2" height="44" />
                  <rect x="123" width="1" height="44" /><rect x="127" width="4" height="44" /><rect x="133" width="1" height="44" />
                  <rect x="137" width="2" height="44" /><rect x="142" width="1" height="44" /><rect x="146" width="3" height="44" />
                </g>
              </svg>
              <span className="num">5&nbsp;012026&nbsp;000001</span>
            </div>
            <div className="outro__credits">
              <b>lumina</b> · lmn·001 · study sessions vol. 1<br />
              built for students<br />
              © 2026 lumina
            </div>
            <nav className="outro__links" aria-label="footer">
              <Link to="/register">sign up</Link>
              <Link to="/login">sign in</Link>
              <a href="#top">rewind</a>
            </nav>
          </div>
        </div>
      </section>

      <div className="fx-scan" aria-hidden="true" />
      <div className="fx-grain" aria-hidden="true" />
    </div>
  )
}
