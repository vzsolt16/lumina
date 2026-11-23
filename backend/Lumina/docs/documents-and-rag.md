# Documents and RAG

## Document lifecycle

`DocumentsController` is the HTTP boundary (`api/documents`). It passes the authenticated user ID to `DocumentService`, which enforces ownership in queries.

```mermaid
sequenceDiagram
  participant Client
  participant API as DocumentsController
  participant DS as DocumentService
  participant Extract as DocumentContentExtractor
  participant DB as SQLite
  participant Files as LocalFileStorage
  participant Index as DocumentIndexScheduler
  Client->>API: POST /api/documents (file, optional folder)
  API->>DS: UploadAsync(userId, folderId)
  DS->>Extract: extract text / structured content
  DS->>Files: save original and extracted assets
  DS->>DB: save Document and assets
  DS-->>Index: Schedule(documentId)
  API-->>Client: document id and filename
```

`DocumentValidation` checks upload constraints and `DocumentFormatDescriptor` separates canonical source-text files from structured formats. PDF/DOCX extraction can retain structured content and extracted assets; text/code formats retain source text. The database record stores the editable representation and metadata, while the original upload is stored separately under a generated key by [LocalFileStorage](../Services/Documents/LocalFileStorage.cs). Failed upload cleanup removes files already written.

Editing is handled through `DocumentService.UpdateAsync` (simple content/name updates) and [DocumentEditorService](../Services/Documents/DocumentEditorService.cs) (versioned rich-text operations and exact-range source-text operations). Content edits increment `ContentVersion`, update `UpdatedAt`, and schedule reindexing. Stale expected versions are rejected. Chat-generated edits are stored as pending proposals; see [chat docs](chat-and-workers.md).

Downloads are distinct: `GET /{id}/original` streams the stored upload; `GET /{id}/content` exports current editable content. PDF/DOCX current content downloads as extracted text. Delete first removes vectors, then deletes the database document and attempts original/asset cleanup. A vector cleanup error preserves the database document so deletion can be retried; a failure removing the original can occur after the database row has already been deleted. Routes and behavior are in [DocumentsController](../Controllers/DocumentsController.cs) and [DocumentService](../Services/Documents/DocumentService.cs).

## Indexing pipeline

Indexing is derived data, scheduled after upload/content changes and by the explicit reindex route. [DocumentIndexScheduler](../Services/Rag/DocumentIndexScheduler.cs) debounces repeated changes per document, serializes runs per document, and recovers pending/stale records on startup. It is in-process and not a durable queue.

```mermaid
flowchart LR
  Save[Upload or content edit] --> Debounce[Debounced scheduler]
  Debounce --> Read[Load current document/version]
  Read --> Small{Within full-document threshold?}
  Small -- yes --> Clear[Delete vectors; mark NotIndexed]
  Small -- no --> Chunk[DocumentChunker]
  Chunk --> Embed[Ollama embeddings]
  Embed --> Upsert[Qdrant upsert with version/generation metadata]
  Upsert --> Current{Document version still current?}
  Current -- no --> Discard[Delete new generation; mark Pending]
  Current -- yes --> Activate[Activate generation; delete old generation]
```

[DocumentIndexService](../Services/Rag/DocumentIndexService.cs) intentionally writes a new generation before activating it, so retrieval can keep using the old active generation during reindexing. If the document changes during the run, the new generation is discarded. Failures persist `IndexingStatus=Failed` and a truncated error. Documents at or below `Rag:FullDocumentMaxTokens` have old vectors removed and stay `NotIndexed`; chat can use their full text directly. Structured content is chunked with heading paths/node IDs; source-text content uses `ChunkSourceText` with its format-aware path. Embedding inputs include the title and, when available, heading path. [OllamaEmbeddingService](../Services/Rag/EmbeddingService.cs) calls Ollama's `api/embed`; [QdrantVectorStore](../Services/Rag/QdrantVectorStore.cs) stores vectors plus ownership, document/version/generation, and chunk payload.

## Retrieval pipeline

[DocumentContextSelector](../Services/Rag/DocumentRetriever.cs) chooses evidence for the current document and user. It uses full document text when it fits the request evidence budget. For structured documents, an unambiguous heading reference can use structural section retrieval. Otherwise it requires an index whose indexed content version and chunker version match the document, embeds the question, and searches Qdrant using user, document, and active-generation filters. It additionally checks version/generation on results, ranks by score, removes highly overlapping chunks, and caps evidence to budget. Source metadata is returned with semantic/structural evidence.

If the document is too large and the index is absent, stale, or running, retrieval returns a limitation rather than presenting old passages as current. Vector service errors likewise return a failed/no-evidence context. A source list is attached only when retrieval produced sources. Retrieval configuration is in the `Rag` section of [appsettings.json](../appsettings.json).
