using System.Runtime.CompilerServices;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lumina.Services.AI;

namespace Lumina.Services.Chat;

public enum ToolExecutionStatus
{
    Succeeded,
    InvalidArguments,
    NotFound,
    ToolNotFound,
    Failed,
    NeedsContext,
    Blocked,
    ProposalCreated,
    TargetAmbiguous,
    InsufficientContext,
    StaleDocumentVersion,
    ValidationFailed,
    DocumentNotAccessible
}

public sealed record ToolExecutionResult(
    ToolExecutionStatus Status,
    string Message,
    bool StopAfterTurn = false,
    object? Metadata = null);

public enum ChatOrchestrationEventKind { Token, ToolStarted, ToolCompleted, TurnLimitReached, ToolLimitReached, RequestBudgetExceeded }

public sealed record ChatOrchestrationEvent(
    ChatOrchestrationEventKind Kind,
    string? Text = null,
    AiToolCall? ToolCall = null,
    ToolExecutionResult? ToolResult = null);

/// <summary>Coordinates provider turns and tool execution without owning feature-specific tool logic.</summary>
public sealed class ChatOrchestrator(IAiService aiService, ILogger<ChatOrchestrator> logger, ChatRequestBudgeter? budgeter = null)
{
    public const int MaxModelTurns = 5;
    public const int MaxToolExecutions = 20;

    public async IAsyncEnumerable<ChatOrchestrationEvent> RunAsync(
        Guid conversationId,
        string modelId,
        List<AiMessage> messages,
        IReadOnlyList<AiTool> tools,
        Func<AiToolCall, CancellationToken, Task<ToolExecutionResult>> executeTool,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seenCalls = new Dictionary<string, int>(StringComparer.Ordinal);
        var progress = 0;
        var toolExecutions = 0;
        var seenContent = false;

        for (var turn = 0; turn < MaxModelTurns; turn++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (budgeter is not null)
            {
                var inputBudget = budgeter.AvailableInputTokens(modelId);
                var fits = budgeter.Fit(messages, tools, inputBudget);
                if (fits)
                {
                    var providerCount = await aiService.CountInputTokensAsync(messages, tools, cancellationToken, modelId);
                    // When a provider exposes counting, use it for final admission. The local estimate
                    // remains the fallback and the provider count may omit transport framing.
                    while (providerCount is not null && providerCount > inputBudget)
                    {
                        var conservativeLimit = inputBudget - (providerCount.Value - inputBudget) - 32;
                        var priorCount = messages.Count;
                        var priorEstimate = ChatRequestBudgeter.EstimateTokens(messages, tools);
                        if (!budgeter.Fit(messages, tools, Math.Max(0, conservativeLimit))
                            || (messages.Count == priorCount && ChatRequestBudgeter.EstimateTokens(messages, tools) == priorEstimate))
                        { fits = false; break; }
                        providerCount = await aiService.CountInputTokensAsync(messages, tools, cancellationToken, modelId);
                    }
                }
                if (!fits)
                {
                    yield return new(ChatOrchestrationEventKind.RequestBudgetExceeded,
                        "The required instructions, current message, and tool protocol exceed this model's configured context budget. Please shorten the request.");
                    yield break;
                }
            }
            var requestWatch = System.Diagnostics.Stopwatch.StartNew();
            var calls = new List<AiToolCall>();
            var rawText = new StringBuilder();
            var requestNumber = turn + 1;
            logger.LogInformation("Chat model request started. ConversationId={ConversationId} ModelId={ModelId} RequestNumber={RequestNumber}",
                conversationId, modelId, requestNumber);
            try
            {
                await using var stream = aiService.ChatStreamAsync(
                    messages, tools, maxTokens: 4000, cancellationToken: cancellationToken, modelId: modelId)
                    .GetAsyncEnumerator(cancellationToken);
                while (true)
                {
                    bool hasNext;
                    try { hasNext = await stream.MoveNextAsync(); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        logger.LogError("Chat provider request failed. ConversationId={ConversationId} ModelId={ModelId} RequestNumber={RequestNumber} DurationMs={DurationMs} ErrorType={ErrorType}",
                            conversationId, modelId, requestNumber, requestWatch.ElapsedMilliseconds, ex.GetType().Name);
                        throw;
                    }
                    if (!hasNext) break;
                    var delta = stream.Current;
                    if (delta.Usage is not null)
                        logger.LogInformation("Chat provider usage. ConversationId={ConversationId} ModelId={ModelId} InputTokens={InputTokens} OutputTokens={OutputTokens} TotalTokens={TotalTokens}",
                            conversationId, modelId, delta.Usage.InputTokens, delta.Usage.OutputTokens, delta.Usage.TotalTokens);
                    if (delta.ToolCalls is { Count: > 0 } deltaCalls) calls.AddRange(deltaCalls);
                    if (string.IsNullOrEmpty(delta.Content)) continue;

                    rawText.Append(delta.Content);
                    if (!string.IsNullOrWhiteSpace(delta.Content)) progress++;
                    var visible = delta.Content;
                    if (!seenContent)
                    {
                        visible = visible.TrimStart();
                        if (visible.Length == 0) continue;
                        seenContent = true;
                    }
                    yield return new(ChatOrchestrationEventKind.Token, Text: visible);
                }
            }
            finally
            {
                logger.LogInformation("Chat model request completed. ConversationId={ConversationId} ModelId={ModelId} RequestNumber={RequestNumber} DurationMs={DurationMs} ToolCallCount={ToolCallCount}",
                    conversationId, modelId, requestNumber, requestWatch.ElapsedMilliseconds, calls.Count);
            }

            if (calls.Count == 0) yield break;
            // Tool arguments in history must remain serializable by every provider.
            // Keep the original call for validation, but replace malformed arguments
            // with an empty JSON object in the provider-neutral replay transcript.
            messages.Add(new AiMessage("assistant", rawText.ToString(),
                ToolCalls: calls.Select(NormalizeForHistory).ToArray()));
            var stopAfterTurn = false;

            foreach (var call in calls)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fingerprint = Fingerprint(call);
                if (seenCalls.TryGetValue(fingerprint, out var priorProgress) && priorProgress == progress)
                {
                    var blocked = new ToolExecutionResult(
                        ToolExecutionStatus.Blocked,
                        "This exact tool call was already made without new progress. Use the result already in the conversation or try a different action.");
                    messages.Add(new AiMessage("tool", blocked.Message, ToolName: call.Name));
                    logger.LogWarning("Repeated chat tool call blocked. ConversationId={ConversationId} ToolName={ToolName} Outcome={Outcome}",
                        conversationId, call.Name, blocked.Status);
                    yield return new(ChatOrchestrationEventKind.ToolCompleted, ToolCall: call, ToolResult: blocked);
                    continue;
                }
                seenCalls[fingerprint] = progress;

                if (toolExecutions >= MaxToolExecutions)
                {
                    var blocked = new ToolExecutionResult(ToolExecutionStatus.Blocked,
                        "The tool execution limit was reached. Stop using tools and provide the best answer possible from the results already available.");
                    messages.Add(new AiMessage("tool", blocked.Message, ToolName: call.Name));
                    yield return new(ChatOrchestrationEventKind.ToolLimitReached, ToolCall: call, ToolResult: blocked);
                    yield break;
                }

                yield return new(ChatOrchestrationEventKind.ToolStarted, ToolCall: call);
                var toolWatch = System.Diagnostics.Stopwatch.StartNew();
                ToolExecutionResult result;
                try
                {
                    if (call.Arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                        || call.Arguments.ValueKind != JsonValueKind.Object)
                    {
                        result = new(ToolExecutionStatus.InvalidArguments,
                            "Tool arguments must be a JSON object. Provide valid arguments and try again.");
                    }
                    else
                    {
                        result = await executeTool(call, cancellationToken);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    result = new(ToolExecutionStatus.Failed, "The tool failed to execute. You may try a different approach.");
                    logger.LogError("Chat tool execution failed. ConversationId={ConversationId} ToolName={ToolName} ErrorType={ErrorType}",
                        conversationId, call.Name, ex.GetType().Name);
                }
                toolExecutions++;
                messages.Add(new AiMessage("tool", result.Message, ToolName: call.Name));
                if (result.Status == ToolExecutionStatus.Succeeded)
                {
                    progress++;
                    seenCalls[fingerprint] = progress;
                }
                logger.LogInformation("Chat tool completed. ConversationId={ConversationId} ToolName={ToolName} Outcome={Outcome} DurationMs={DurationMs} InputBytes={InputBytes} OutputBytes={OutputBytes} ExecutionNumber={ExecutionNumber}",
                    conversationId, call.Name, result.Status, toolWatch.ElapsedMilliseconds,
                    GetArgumentSize(call.Arguments), result.Message.Length, toolExecutions);
                yield return new(ChatOrchestrationEventKind.ToolCompleted, ToolCall: call, ToolResult: result);
                stopAfterTurn |= result.StopAfterTurn;
            }
            if (stopAfterTurn) yield break;
        }

        logger.LogWarning("Chat model-turn limit reached. ConversationId={ConversationId} ModelId={ModelId} MaxModelTurns={MaxModelTurns} ToolExecutions={ToolExecutions}",
            conversationId, modelId, MaxModelTurns, toolExecutions);
        yield return new(ChatOrchestrationEventKind.TurnLimitReached);
    }

    internal static string Fingerprint(AiToolCall call)
    {
        var normalized = call.Arguments.ValueKind == JsonValueKind.Undefined
            ? "undefined"
            : Canonicalize(call.Arguments);
        var bytes = Encoding.UTF8.GetBytes(call.Name + "\n" + normalized);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static string Canonicalize(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Object => "{" + string.Join(",", value.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Serialize(p.Name) + ":" + Canonicalize(p.Value))) + "}",
        JsonValueKind.Array => "[" + string.Join(",", value.EnumerateArray().Select(Canonicalize)) + "]",
        JsonValueKind.String => JsonSerializer.Serialize(value.GetString()),
        JsonValueKind.Number => CanonicalNumber(value),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        _ => value.GetRawText()
    };

    private static string CanonicalNumber(JsonElement value)
    {
        var number = value.GetDouble();
        return number == 0 ? "0" : number.ToString("R", CultureInfo.InvariantCulture);
    }

    private static int GetArgumentSize(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Undefined ? 0 : arguments.GetRawText().Length;

    private static AiToolCall NormalizeForHistory(AiToolCall call) =>
        call.Arguments.ValueKind == JsonValueKind.Object
            ? call
            : call with { Arguments = EmptyArguments() };

    private static JsonElement EmptyArguments()
    {
        using var document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
