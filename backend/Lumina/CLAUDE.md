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

## External dependency: Ollama

The AI layer calls a local Ollama instance at `http://localhost:11434`. It must be running before starting the app. The model used is `qwen3:4b`. The HTTP client timeout is 5 minutes to accommodate slow local inference.

## Architecture

Lumina is a study-assistant API. Users upload documents (`.txt` / `.md`), which are stored in full as text in SQLite. All AI features derive from that stored `Document.Content`.

**Request flow for quiz generation (the most complex feature):**

1. `POST /api/documents/{documentId}/quizzes` → `QuizzesController` creates a `QuizJob` (status: `Processing`) and enqueues a work item on `IBackgroundTaskQueue`.
2. `QuizBackgroundWorker` (a `BackgroundService`) dequeues and executes the work item, calling `QuizService.ProcessQuizJobAsync`.
3. `QuizService` starts a simulated progress timer (fires every 1.5 s, increments `QuizJob.Progress` by 5%, caps at 90%) and runs a single Ollama call to generate all 5 questions at once.
4. On completion the timer is cancelled, progress is set to 100, and the quiz is persisted as `Quiz` + `QuizQuestion` rows.
5. Real-time updates are pushed to the frontend via SignalR (`QuizHub` at `/ws/quiz`). The client must call `JoinQuizGroup(quizId)` after connecting. The hub emits three events: `Progress` (incremental ticks), `Completed` (with the full quiz payload), and `Failed`.

**Why all 5 questions are generated in one AI call:** generating them one at a time caused the model to repeat the same question, because each independent call latched onto the same salient fact. A single prompt forces the model to diversify within the same response.

**Quiz question shape:** each `QuizQuestion` has `AnswerA`–`AnswerD` (the four options) and `CorrectAnswer` (the letter `"A"`, `"B"`, `"C"`, or `"D"`). The Ollama structured-output schema enforces this with a JSON Schema `enum`.

**AI service:** `OllamaService` implements `IAiService` and has a hardcoded `QuizSchema` (`JsonElement`) passed as Ollama's `format` parameter for structured output. If the quiz question shape changes, update both `QuizSchema` and `GeneratedQuizQuestionDto`.

**Database:** SQLite (`lumina.db` in the project folder). EF Core with `OnDelete: Cascade` on all document-owned collections. Migrations live in `Lumina/Migrations/`.

**Scoping:** `IQuizService` is registered as `Scoped`. Because `QuizBackgroundWorker` is a singleton-lifetime `BackgroundService`, it creates a DI scope manually (`_serviceProvider.CreateScope()`) before resolving `IQuizService`.
