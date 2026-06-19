# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All commands should be run from the solution root (`/backend/Lumina`), targeting the project at `Lumina/Lumina.csproj`.

```bash
# Build
dotnet build Lumina/Lumina.csproj

# Run (dev)
dotnet run --project Lumina/Lumina.csproj

# Add a migration
dotnet ef migrations add <MigrationName> --project Lumina/Lumina.csproj

# Apply migrations
dotnet ef database update --project Lumina/Lumina.csproj

# Remove last migration (if not yet applied)
dotnet ef migrations remove --project Lumina/Lumina.csproj
```

There are no automated tests in this project yet.

## Configuration & secrets

The JWT signing key is **not** in `appsettings.json`. It is read from `Jwt__Key` in a
`.env` file at the solution root (`/backend/Lumina/.env`, gitignored), loaded into
environment variables at startup by `DotNetEnv` (`Env.TraversePath().Load()` at the top
of `Program.cs`, before the host reads configuration). The `__` maps to the `Jwt:Key`
config path and overrides `appsettings.json`.

To set up locally: copy `.env.example` → `.env` and set a key of at least 32 chars
(`openssl rand -base64 48`). Startup throws a clear error if the key is missing or too
short. The non-secret `Jwt` settings (issuer, audience, token lifetimes) stay in
`appsettings.json`.

## External dependency: Ollama

The AI layer calls a local Ollama instance at `http://localhost:11434`. It must be running before starting the app. The model used is `qwen3:4b`. The HTTP client timeout is 5 minutes to accommodate slow local inference.

## Authentication & authorization

All endpoints and SignalR hubs require a valid JWT (`[Authorize]`); only `/api/auth/*` is anonymous.

- **Identity:** ASP.NET Core Identity via `AddIdentityCore` with a Guid-keyed `ApplicationUser`. `LuminaDbContext` is an `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`.
- **Access token:** `TokenService` issues a short-lived JWT (default 15 min) carrying the user id as the `sub` claim. The frontend holds it in memory only (never in storage).
- **Refresh tokens:** rotating and DB-backed (`RefreshToken` table; only a SHA-256 hash of the token is stored). `POST /api/auth/refresh` revokes the presented token and issues a new one — reusing a revoked or expired token returns 401. Delivered as an HttpOnly cookie scoped to `/api/auth` (default lifetime 7 days). Endpoints: `register`, `login`, `refresh`, `logout`.
- **Ownership:** every `Document` has a `UserId`; quizzes, flashcards and chat messages inherit ownership through their parent `Document`. Controllers read the caller with `User.GetUserId()` (extension in `Extensions/ClaimsPrincipalExtensions.cs`, which reads `ClaimTypes.NameIdentifier` — JwtBearer maps the `sub` claim to it) and pass that id into the service layer, which scopes every query. Cross-user access returns 404, never another user's data.
- **SignalR auth:** hubs are `[Authorize]`'d. Browsers can't set the `Authorization` header on a WebSocket handshake, so the client supplies the token via `accessTokenFactory`; the `OnMessageReceived` event in `Program.cs` reads `?access_token=` from the query string, but **only for `/ws` paths**. `JoinQuizGroup` / `JoinFlashcardGroup` additionally verify the job belongs to one of the caller's own documents before joining the group.

## Architecture

Lumina is a study-assistant API. Users upload documents (`.txt` / `.md`), which are stored in full as text in SQLite. All AI features derive from that stored `Document.Content`.

**Request flow for quiz generation (the most complex feature):**

1. `POST /api/documents/{documentId}/quizzes` → `QuizzesController` creates a `QuizJob` (status: `Processing`) and enqueues a work item on `IBackgroundTaskQueue`.
2. `QuizBackgroundWorker` (a `BackgroundService`) dequeues and executes the work item, calling `QuizService.ProcessQuizJobAsync`.
3. `QuizService` starts a simulated progress timer (fires every 1.5 s, increments `QuizJob.Progress` by 5%, caps at 90%) and runs a single Ollama call to generate all 5 questions at once.
4. On completion the timer is cancelled, progress is set to 100, and the quiz is persisted as `Quiz` + `QuizQuestion` rows.
5. Real-time updates are pushed to the frontend via SignalR (`QuizHub` at `/ws/quiz`). After connecting, the client calls `JoinQuizGroup(jobId)` — the group is keyed by the `QuizJob` id (returned from the POST as `quizId`), and the join is rejected unless that job belongs to one of the caller's own documents. The hub emits three events: `Progress` (incremental ticks), `Completed` (with the full quiz payload), and `Failed`.

**Why all 5 questions are generated in one AI call:** generating them one at a time caused the model to repeat the same question, because each independent call latched onto the same salient fact. A single prompt forces the model to diversify within the same response.

**Quiz question shape:** each `QuizQuestion` has `AnswerA`–`AnswerD` (the four options) and `CorrectAnswer` (the letter `"A"`, `"B"`, `"C"`, or `"D"`). The Ollama structured-output schema enforces this with a JSON Schema `enum`.

**AI service:** `OllamaService` implements `IAiService` and has a hardcoded `QuizSchema` (`JsonElement`) passed as Ollama's `format` parameter for structured output. If the quiz question shape changes, update both `QuizSchema` and `GeneratedQuizQuestionDto`.

**Database:** SQLite (`lumina.db` in the project folder). EF Core with `OnDelete: Cascade` on all document-owned collections, plus `Document → User` and `RefreshToken → User`. Also holds the ASP.NET Core Identity tables and `RefreshTokens`. Migrations live in `Lumina/Migrations/`.

**Scoping:** `IQuizService` / `IFlashcardService` are registered as `Scoped`, but the background work runs outside any request scope. The controller therefore captures `IServiceScopeFactory` and, inside the queued work item, creates a fresh DI scope (`_scopeFactory.CreateScope()`) to resolve the scoped service before calling `Process…JobAsync`. `QuizBackgroundWorker` itself just dequeues and invokes the work-item delegate.
