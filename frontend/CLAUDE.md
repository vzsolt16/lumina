# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Scope

This directory (`frontend/`) is the React + Vite client. The ASP.NET Core backend lives in the sibling `../backend` directory; the frontend depends on it at runtime and references several backend types/endpoints, so backend files are worth reading when changing the data contracts described below.

Stack: React 18, Vite 5, React Router 6, `@microsoft/signalr`. **JavaScript (.jsx), not TypeScript.**

## Commands

```bash
npm run dev      # Vite dev server on :5173 (proxies /api and /ws to the backend)
npm run build    # production build to dist/
npm run preview  # serve the built dist/
```

There is no test runner, linter, or formatter configured in this project — `package.json` only defines `dev`/`build`/`preview`. Treat `npm run build` as the smoke test that the app compiles and wires up.

### Running the full app
The frontend needs the backend up. From `../backend/Lumina`:
```bash
dotnet run --project Lumina        # http profile → http://localhost:5131
```
The Vite dev proxy (`vite.config.js`) forwards `/api` (HTTP) and `/ws` (HTTP negotiate + WebSocket upgrade) to `http://localhost:5131`, so all client fetch paths are origin-relative. The backend also needs **Ollama** running locally (model: Qwen 3) for generation/chat to actually produce results.

## Architecture

### Auth model (read before touching requests)
- The **access token lives in memory only** (`src/api/authToken.js`) — never localStorage. It vanishes on reload and is restored via the refresh cookie.
- The **refresh token is an HttpOnly cookie**, sent automatically with `credentials: 'include'`.
- `src/api/client.js` is the single fetch wrapper. On a 401 it transparently calls `/api/auth/refresh` **once** (de-duped via a shared in-flight promise) and replays the request; if refresh fails it clears the token and fires `notifyAuthExpired`.
- `AuthContext` restores the session on app load via `refreshSession()` and registers the `onAuthExpired` callback. `ProtectedRoute` gates `/studio*`.
- All new API calls go through `client.js`'s `request()` so they inherit auth + refresh-retry.

### Document-centric Studio
The Studio is built around the **document** as the central object; flashcards, quiz, and chat are views onto a document. Routing (in `App.jsx`):
- `/studio` → `Studio.jsx`: the **library**. Auto-fetches the user's documents on mount; upload navigates straight into the new document.
- `/studio/:docId` → `StudioDocument.jsx`: the **workspace layout** (header + tab bar, renders the active tab into an `<Outlet/>`). Index redirects to `flashcards`.
- Nested tabs in `src/pages/document/`: `flashcards`, `quiz`, `chat`.

Flashcards and quiz tabs share `GenerationTab.jsx`: on mount it fetches any **persisted** result and only offers Generate if empty. A live (streamed) result and a persisted result share the same shape, so one render path handles both — `FlashcardDeck` expects `[{question, answer}]`; `QuizView` expects a single `{title, questions:[...]}`. The chat tab is a placeholder (the backend `ChatController` is currently a stub).

### Generation jobs — app-level registry
This is the least obvious part of the codebase. Flashcard/quiz generation is async and streamed over SignalR, and jobs **must survive route changes**, so they do not live in component hooks.

- `src/context/JobsContext.jsx` is an app-level registry (`JobsProvider` mounted in `main.jsx` inside `AuthProvider`), **keyed by `docId`**. Each document owns a `flashcard` and a `quiz` job, each with its own SignalR connection.
- It's an external store read via `useSyncExternalStore` with a **per-document snapshot**, so a progress tick on one document only re-renders components viewing that document (plain context would re-render all consumers every tick).
- **Viewer ref-counting + `maybeDrop`**: a document's state/connections are freed once nothing references it and no job is in flight. Results are persisted server-side and re-fetched on return, so dropping idle entries is safe.
- `JobsProvider` calls `clearAll()` when the auth user goes null (logout / refresh expiry).
- `StudioDocument` reads `useDocumentJobs(docId)` and passes the jobs to tabs via Outlet context. **Do not reset jobs on document change** — the registry owns their lifecycle (resetting would kill a job running for another document).

#### SignalR flow per job
Each kind maps to a hub: flashcards → `/ws/flashcard` (`JoinFlashcardGroup`), quiz → `/ws/quiz` (`JoinQuizGroup`). The lifecycle is: build connection (WebSockets only, `skipNegotiation: true`, access token via `accessTokenFactory`) → `connection.start()` → POST `/api/documents/:id/{flashcards|quizzes}` to kick off the background job → `invoke(joinMethod, jobId)` to join the job's group → receive `Progress` / `Completed` / `Failed` events. The token is appended as the `access_token` query param because that's the only way to auth a browser WebSocket handshake.

### Backend data contracts the frontend relies on
- `GET /api/documents` → `[{ id, fileName, uploadedAt }]`; `GET /api/documents/:id` → document detail.
- `GET /api/documents/:id/flashcards` → `[{ question, answer }]` (flat list — see caveats).
- `GET /api/documents/:id/quizzes` → array of `{ title, questions: [{ question, answerA..D, correctAnswer }] }`.
- Generation: `POST` the same flashcards/quizzes paths; backend queues a background worker and streams progress over the matching hub.

## Caveats / known rough edges
- **Quiz "latest"**: quiz entities carry no timestamp, so `QuizTab` shows the *last* quiz in the returned array as "most recent". Add a `CreatedAt` on the backend for reliable ordering.
- **Flashcards may accumulate**: `GET .../flashcards` returns *all* cards for a document, so regeneration can pile up cards if the backend appends rather than replaces — verify backend behavior before assuming a fresh deck.

## Design system
UI follows a deliberate aesthetic (see `src/pages/Studio.css` headers and `src/styles/theme.css`): glass panels, hairline teal borders, **sharp corners (no border-radius), no shadows**, monospace system labels (`Share Tech Mono`), condensed display type (`Barlow Condensed`). Match this when adding UI — reuse existing classes (`.panel`, `.doc-*`, `.gen-empty`, `.progress-*`) rather than introducing new visual patterns. Icons are a custom line-art set in `src/components/icons.jsx` (outline-only, `currentColor`, sized via a `size` prop).
