# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Git

Commit messages must follow conventional commits: `<type>: <short description>`.
Types: `feat`, `fix`, `refactor`, `chore`, `docs`, `style`, `test`.
Example: `feat: add document deletion functionality`

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

Tests live in `Lumina.Tests/` (xUnit). Run them from the solution root:

```bash
dotnet test Lumina.Tests/Lumina.Tests.csproj
```

Chat loop and chat-service integration coverage can be run alone with:

```bash
dotnet test Lumina.Tests/Lumina.Tests.csproj --filter "FullyQualifiedName~ChatOrchestratorTests|FullyQualifiedName~ChatServiceOrchestrationTests"
```

Pure-logic helpers in service classes are tested as `internal static` methods — `InternalsVisibleTo("Lumina.Tests")` is declared in `Lumina/Properties/AssemblyInfo.cs`. When adding a new service with non-trivial pure logic (validation, tree algorithms, parsing), add unit tests for those helpers in `Lumina.Tests/`.

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

## External AI services

The active `IAiService` runtime registration is `GeminiService` in `Program.cs`. Gemini supplies generation, streamed chat, and tool calling; configure `Gemini:ApiKey`, `Gemini:DefaultModel`, and `Gemini:TimeoutMinutes`. `OllamaService` remains implemented but is not registered as the active AI provider.

Ollama is used for local embeddings through `OllamaEmbeddingService` at `Ollama:BaseUrl` (default `http://localhost:11434/`), with the model configured by `Ollama:EmbeddingModel`. Qdrant stores document vectors. A working Ollama instance and Qdrant service are required for indexing and semantic retrieval, but not for Gemini text generation itself.

## Authentication & authorization

All endpoints and SignalR hubs require a valid JWT (`[Authorize]`); only `/api/auth/*` is anonymous.

- **Identity:** ASP.NET Core Identity via `AddIdentityCore` with a Guid-keyed `ApplicationUser`. `LuminaDbContext` is an `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`.
- **Access token:** `TokenService` issues a short-lived JWT (default 15 min) carrying the user id as the `sub` claim. The frontend holds it in memory only (never in storage).
- **Refresh tokens:** rotating and DB-backed (`RefreshToken` table; only a SHA-256 hash of the token is stored). `POST /api/auth/refresh` revokes the presented token and issues a new one — reusing a revoked or expired token returns 401. Delivered as an HttpOnly cookie scoped to `/api/auth` (default lifetime 7 days). Endpoints: `register`, `login`, `refresh`, `logout`.
- **Ownership:** every `Document` has a `UserId`; quizzes, flashcards and chat conversations (and their messages) inherit ownership through their parent `Document`. Controllers read the caller with `User.GetUserId()` (extension in `Extensions/ClaimsPrincipalExtensions.cs`, which reads `ClaimTypes.NameIdentifier` — JwtBearer maps the `sub` claim to it) and pass that id into the service layer, which scopes every query. Cross-user access returns 404, never another user's data.
- **SignalR auth:** hubs are `[Authorize]`'d. Browsers can't set the `Authorization` header on a WebSocket handshake, so the client supplies the token via `accessTokenFactory`; the `OnMessageReceived` event in `Program.cs` reads `?access_token=` from the query string, but **only for `/ws` paths**. `JoinQuizGroup` / `JoinFlashcardGroup` additionally verify the job belongs to one of the caller's own documents before joining the group. `ChatHub.StreamAnswer` verifies the conversation belongs to one of the caller's own documents before streaming.

## Architecture

Lumina is a study-assistant API. Users upload documents (`.txt`, `.md`, `.pdf` and `.docx`), which are extracted and stored as text in SQLite. All AI features derive from that stored `Document.Content`.

**Request flow for quiz generation (the most complex feature):**

1. `POST /api/documents/{documentId}/quizzes` → `QuizzesController` creates a `QuizJob` (status: `Processing`) and enqueues a work item on `IBackgroundTaskQueue`.
2. `QuizBackgroundWorker` (a `BackgroundService`) dequeues and executes the work item, calling `QuizService.ProcessQuizJobAsync`.
3. `QuizService` starts a simulated progress timer (fires every 1.5 s, increments `QuizJob.Progress` by 5%, caps at 90%) and runs a single `IAiService.GenerateAsync` call (Gemini at runtime) to generate all 5 questions at once.
4. On completion the timer is cancelled, progress is set to 100, and the quiz is persisted as `Quiz` + `QuizQuestion` rows.
5. Real-time updates are pushed to the frontend via SignalR (`QuizHub` at `/ws/quiz`). After connecting, the client calls `JoinQuizGroup(jobId)` — the group is keyed by the `QuizJob` id (returned from the POST as `quizId`), and the join is rejected unless that job belongs to one of the caller's own documents. The hub emits three events: `Progress` (incremental ticks), `Completed` (with the full quiz payload), and `Failed`.

**Why all 5 questions are generated in one AI call:** generating them one at a time caused the model to repeat the same question, because each independent call latched onto the same salient fact. A single prompt forces the model to diversify within the same response.

**Quiz question shape:** each `QuizQuestion` has `AnswerA`–`AnswerD` (the four options) and `CorrectAnswer` (the letter `"A"`, `"B"`, `"C"`, or `"D"`). The generation schema constrains the answer to a JSON Schema `enum` through the active Gemini provider.

**AI service:** `IAiService` is the provider boundary. Both `GeminiService` and `OllamaService` implement it; `Program.cs` currently registers Gemini. Quiz/flashcard generation uses `GenerateAsync` with a JSON schema, and chat uses `ChatStreamAsync`. `AiMessage`, `AiTool`, `AiToolCall`, and `AiChatDelta` are provider-neutral contracts. Provider-specific request/response formats and streaming differences stay inside each provider implementation.

**Chat ("chat with your document") — a different shape from quiz/flashcards.** Chat is a direct request/response stream, **not** a background job, so it does not use `IBackgroundTaskQueue` or the group/replay hub pattern.

1. The client opens `/ws/chat` (`ChatHub`) and calls `StreamAnswer(conversationId, question, modelId)`, a native SignalR streaming method returning typed `ChatStreamEvent`s. SignalR injects the `CancellationToken` and cancels it when the client unsubscribes or disconnects.
2. `ChatHub` validates conversation ownership, then delegates to `ChatService.StreamAnswerAsync`. ChatService loads the document and prior `ChatMessage` history in a short-lived DI scope, chooses the conversation model through `GeminiModelCatalog`, and asks `IDocumentContextSelector` for full-document or current RAG context.
3. `ChatService` builds the system prompt, messages, and existing tool definitions. `ChatOrchestrator` owns the streamed model-turn loop, sequential tool coordination, cancellation, repeated-call protection, model-turn limit (5), tool-execution limit (20), and orchestration logging. `ChatRequestBudgeter` checks every provider request (including tool follow-ups), accounting for configured model input limits, response reserve, safety margin, tool schemas, and protocol/history messages. Its fallback estimate uses UTF-8 bytes / 3 plus framing allowance; it removes whole evidence messages before oldest complete stored user/assistant pairs.
4. Tool results carry a structured outcome (`Succeeded`, `InvalidArguments`, `NotFound`, `ToolNotFound`, `Failed`, `NeedsContext`, or `Blocked`) as well as explanatory text for the model. Repeated calls are fingerprinted by tool name plus canonicalized JSON arguments. An identical call is blocked if no assistant content or other successful tool result has advanced the request. A new turn with meaningful assistant content can retry a read.
5. Tool calls remain sequential. Provider failures propagate and do not save an incomplete user/assistant exchange. Cancellation is passed to both provider streaming and tool execution. The model-turn and tool-execution limits are independent. Logs include conversation/model IDs, request/turn duration, tool name, outcome, argument/output sizes, and counts; document content and conversation history are not logged. Provider token usage is not yet exposed by `AiChatDelta`.
6. **History persistence:** the user message and assistant reply are saved together **only on clean completion** (tracked by a `completed` flag in a `try`/`finally`). On an error or mid-stream disconnect, nothing is persisted. On a clean save the conversation's `UpdatedAt` is bumped and, on the first exchange, its `Title` is derived from the opening question.

**AI edit proposals:** document mutation remains in ChatService and `DocumentEditorService`; the model has no direct write path. Existing content tools validate deterministic `DocumentOperation`s and create pending proposals. Proposal apply checks the expected `ContentVersion` (an EF concurrency token), preserves unrelated document content, synchronizes `Content` from `ContentJson`, increments the version, and schedules reindexing. The user must approve edits; rename is the existing immediate-action exception. `ChatOrchestrator` stops requesting model turns after a proposal is created so approval remains the next decision.

**Retrieval and context:** `DocumentChunker` walks nested ProseMirror content and records heading paths, stable node IDs, and logical block indices in Qdrant payloads (`blocks-v2`). Index generation activation remains version checked. The indexer embeds documents above `Rag:FullDocumentMaxTokens` so retrieval remains available when a request's remaining budget is smaller than the document. `RagContext` carries a typed outcome and completeness flag for stale, unavailable, indexing, empty, partial, and failed retrieval. `DocumentStructureRetriever` resolves heading matches (including ambiguity) and retrieves a bounded section from caller-supplied canonical content with version, node IDs, and continuation metadata. Model limits are configured under `Gemini:Models:{modelId}:ContextTokens`; Gemini's `countTokens` endpoint is used for final pre-send admission when available, and a conservative UTF-8 estimator is the fallback. Streamed Gemini usage metadata is logged when present. `Chat:MaxOutputTokens` and `Chat:ContextSafetyMarginTokens` reserve capacity before input.

**Multi-conversation model:** a `Document` has many `ChatConversation`s; each `ChatConversation` has many `ChatMessage`s (`ChatMessage` links to `ChatConversation`, not directly to `Document`). Cascade chain: `Document → ChatConversation → ChatMessage`. `ChatController` (`api/documents/{documentId}/chat/conversations`) exposes: `GET /` (list, newest-updated first), `POST /` (create empty conversation), `GET /{conversationId}/messages` (one thread's history), `DELETE /{conversationId}` (delete, cascades messages). Conversations start titled `"New chat"`.
**Database:** SQLite (`lumina.db` in the project folder). EF Core with `OnDelete: Cascade` on all document-owned collections, plus `Document → User` and `RefreshToken → User`. Also holds the ASP.NET Core Identity tables and `RefreshTokens`. Migrations live in `Lumina/Migrations/`.

**Scoping:** `IQuizService` / `IFlashcardService` are registered as `Scoped`, but the background work runs outside any request scope. The controller therefore captures `IServiceScopeFactory` and, inside the queued work item, creates a fresh DI scope (`_scopeFactory.CreateScope()`) to resolve the scoped service before calling `Process…JobAsync`. `QuizBackgroundWorker` itself just dequeues and invokes the work-item delegate.
