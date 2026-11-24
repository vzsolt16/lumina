namespace Lumina.Models;

public class RagExecution
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid DocumentId { get; set; }
    public string? TraceId { get; set; }
    public DateTime StartedAt { get; set; }
    public long DurationMs { get; set; }
    public string Status { get; set; } = "Failed";
    public string Query { get; set; } = string.Empty;
    public string? ModelId { get; set; }
    public int? DocumentVersion { get; set; }
    public int? RetrievalDurationMs { get; set; }
    public int? GenerationDurationMs { get; set; }
    public int RetrievedChunkCount { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? Answer { get; set; }
    public string? ContextJson { get; set; }
    public string? FailureStage { get; set; }
    public string? ErrorType { get; set; }
}
