using Lumina.Services.AI;

namespace Lumina.Services.Chat;

/// <summary>Per-stream counters shared by ChatService and ChatOrchestrator.</summary>
public sealed class ChatInteractionMetrics
{
    private readonly HashSet<string> _modelIds = new(StringComparer.Ordinal);
    private readonly List<ProviderRequestUsage> _providerRequests = [];

    public Guid? DocumentId { get; private set; }
    public IReadOnlyCollection<string> ModelIds => _modelIds;
    public int ProviderRequests { get; private set; }
    public int TotalInputTokens { get; private set; }
    public int TotalOutputTokens { get; private set; }
    public int TotalTokens { get; private set; }
    public int ToolExecutions { get; private set; }
    public IReadOnlyList<ProviderRequestUsage> ProviderRequestsUsage => _providerRequests;
    public bool TurnLimitReached { get; set; }
    public bool Completed { get; set; }

    public void SetDocument(Guid documentId) => DocumentId = documentId;

    public void AddModel(string modelId) => _modelIds.Add(modelId);

    public int StartProviderRequest(string modelId)
    {
        _modelIds.Add(modelId);
        return ++ProviderRequests;
    }

    public void RecordUsage(int requestNumber, AiUsageMetadata? usage, IReadOnlyList<string> toolCalls)
    {
        _providerRequests.Add(new ProviderRequestUsage(requestNumber, usage?.InputTokens, usage?.OutputTokens,
            usage?.TotalTokens, toolCalls));
        if (usage is not null)
        {
            var input = usage.InputTokens ?? 0;
            var output = usage.OutputTokens ?? 0;
            TotalInputTokens += input;
            TotalOutputTokens += output;
            TotalTokens += usage.TotalTokens ?? input + output;
        }
    }

    public void RecordToolExecution() => ToolExecutions++;
}

public sealed record ProviderRequestUsage(
    int RequestNumber,
    int? InputTokens,
    int? OutputTokens,
    int? TotalTokens,
    IReadOnlyList<string> ToolCalls);
