using System.Runtime.CompilerServices;
using System.Text.Json;
using Lumina.Services.AI;
using Lumina.Services.Chat;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lumina.Tests;

public sealed class ChatOrchestratorTests
{
    private static readonly AiTool DummyTool = new("read", "Read data", Json("{}"));
    private static readonly Guid ConversationId = Guid.NewGuid();

    [Fact]
    public async Task NormalResponseStreamsTokensWithoutTools()
    {
        var provider = new FakeAiService([Turn(Content("  Hello"), Content(" there"))]);
        var events = await Run(provider, (_, _) => Task.FromResult(Succeeded("unused")));

        Assert.Equal("Hello there", string.Concat(events.Where(e => e.Kind == ChatOrchestrationEventKind.Token).Select(e => e.Text)));
        Assert.Equal(1, provider.RequestCount);
        Assert.DoesNotContain(events, e => e.Kind == ChatOrchestrationEventKind.ToolStarted);
    }

    [Fact]
    public async Task OneToolResultIsAddedBeforeFinalModelTurn()
    {
        var call = Call("lookup", "{\"b\":2,\"a\":1}");
        var provider = new FakeAiService([Turn(ToolCall(call)), Turn(Content("Found it"))]);
        var events = await Run(provider, (_, _) => Task.FromResult(Succeeded("tool result")));

        Assert.Equal(2, provider.RequestCount);
        Assert.Contains(provider.RequestMessages[1], m => m.Role == "tool" && m.Content == "tool result");
        Assert.Contains(events, e => e.Kind == ChatOrchestrationEventKind.ToolCompleted && e.ToolResult?.Status == ToolExecutionStatus.Succeeded);
        Assert.Contains(events, e => e.Kind == ChatOrchestrationEventKind.Token && e.Text == "Found it");
    }

    [Fact]
    public async Task MultipleToolsExecuteSequentiallyAndAcrossTurns()
    {
        var first = Call("read", "{\"n\":1}");
        var second = Call("read", "{\"n\":2}");
        var provider = new FakeAiService([Turn(ToolCall(first), ToolCall(second)), Turn(Content("Done"))]);
        var order = new List<int>();
        await Run(provider, (call, _) =>
        {
            order.Add(call.Arguments.GetProperty("n").GetInt32());
            return Task.FromResult(Succeeded("read"));
        });

        Assert.Equal([1, 2], order);
        Assert.Equal(2, provider.RequestCount);
    }

    [Fact]
    public async Task RepeatedCallIsBlockedWithoutNewProgressButAllowedAfterNewAssistantContent()
    {
        var call = Call("read", "{\"a\":1,\"b\":2}");
        var sameNormalizedCall = Call("read", "{\"b\":2, \"a\":1}");
        var provider = new FakeAiService([Turn(ToolCall(call)), Turn(ToolCall(sameNormalizedCall)), Turn(Content("Final"))]);
        var executions = 0;
        var events = await Run(provider, (_, _) => { executions++; return Task.FromResult(Succeeded("data")); });

        Assert.Equal(1, executions);
        Assert.Contains(events, e => e.Kind == ChatOrchestrationEventKind.ToolCompleted && e.ToolResult?.Status == ToolExecutionStatus.Blocked);
        Assert.Contains(provider.RequestMessages[2], m => m.Role == "tool" && m.Content.Contains("already made", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RepeatedFailingToolIsBlockedAndDoesNotStopFinalAnswer()
    {
        var call = Call("broken", "{}");
        var provider = new FakeAiService([Turn(ToolCall(call)), Turn(ToolCall(call)), Turn(Content("Recovered"))]);
        var executions = 0;
        var events = await Run(provider, (_, _) =>
        {
            executions++;
            return Task.FromResult(new ToolExecutionResult(ToolExecutionStatus.Failed, "failed"));
        });

        Assert.Equal(1, executions);
        Assert.Contains(events, e => e.ToolResult?.Status == ToolExecutionStatus.Blocked);
        Assert.Contains(events, e => e.Kind == ChatOrchestrationEventKind.Token && e.Text == "Recovered");
    }

    [Fact]
    public async Task InvalidArgumentsAndUnknownToolAreStructuredOutcomes()
    {
        var malformed = Call("read", "\"not an object\"");
        var unknown = Call("unknown", "{}");
        var undefined = new AiToolCall("read", default);
        var provider = new FakeAiService([Turn(ToolCall(malformed), ToolCall(undefined), ToolCall(unknown)), Turn(Content("Finished"))]);
        var invoked = 0;
        var events = await Run(provider, (_, _) =>
        {
            invoked++;
            return Task.FromResult(new ToolExecutionResult(ToolExecutionStatus.ToolNotFound, "unknown tool"));
        });

        Assert.Equal(1, invoked);
        Assert.Equal(2, events.Count(e => e.ToolResult?.Status == ToolExecutionStatus.InvalidArguments));
        Assert.Contains(events, e => e.ToolResult?.Status == ToolExecutionStatus.ToolNotFound);
        Assert.All(provider.RequestMessages[1].Single(m => m.Role == "assistant" && m.ToolCalls is not null)
            .ToolCalls!, call => Assert.Equal(JsonValueKind.Object, call.Arguments.ValueKind));
    }

    [Fact]
    public async Task RepeatedReadCanRunAgainAfterAssistantAddsContext()
    {
        var call = Call("read", "{\"query\":\"heading\"}");
        var provider = new FakeAiService([
            Turn(ToolCall(call)),
            Turn(Content("I should use the new context."), ToolCall(call)),
            Turn(Content("Done"))]);
        var executions = 0;
        var events = await Run(provider, (_, _) => { executions++; return Task.FromResult(Succeeded("data")); });

        Assert.Equal(2, executions);
        Assert.Equal(3, provider.RequestCount);
        Assert.DoesNotContain(events, e => e.ToolResult?.Status == ToolExecutionStatus.Blocked);
    }

    [Fact]
    public async Task ProviderFailurePropagates()
    {
        var provider = new FakeAiService([], failOnRequest: 1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(provider, (_, _) => Task.FromResult(Succeeded("unused"))));
    }

    [Fact]
    public async Task ToolExceptionBecomesFailureOutcomeAndModelCanContinue()
    {
        var provider = new FakeAiService([Turn(ToolCall(Call("crash", "{}"))), Turn(Content("Recovered"))]);
        var events = await Run(provider, (_, _) => throw new InvalidOperationException("private exception details"));

        Assert.Contains(events, e => e.ToolResult?.Status == ToolExecutionStatus.Failed
            && e.ToolResult.Message.Contains("failed to execute", StringComparison.Ordinal));
        Assert.Contains(events, e => e.Kind == ChatOrchestrationEventKind.Token && e.Text == "Recovered");
        Assert.Equal(2, provider.RequestCount);
    }

    [Fact]
    public async Task ModelTurnLimitIsSeparateFromToolExecutionLimit()
    {
        var turns = Enumerable.Range(0, ChatOrchestrator.MaxModelTurns)
            .Select(i => Turn(ToolCall(Call("read", $"{{\"n\":{i}}}"))))
            .ToArray();
        var provider = new FakeAiService(turns);
        var events = await Run(provider, (_, _) => Task.FromResult(Succeeded("ok")));

        Assert.Equal(ChatOrchestrator.MaxModelTurns, provider.RequestCount);
        Assert.Contains(events, e => e.Kind == ChatOrchestrationEventKind.TurnLimitReached);
    }

    [Fact]
    public async Task ToolExecutionLimitIsIndependentOfModelTurnLimit()
    {
        var calls = Enumerable.Range(0, ChatOrchestrator.MaxToolExecutions + 1)
            .Select(i => ToolCall(Call("read", $"{{\"n\":{i}}}"))).ToArray();
        var provider = new FakeAiService([calls]);
        var executions = 0;
        var events = await Run(provider, (_, _) => { executions++; return Task.FromResult(Succeeded("ok")); });

        Assert.Equal(ChatOrchestrator.MaxToolExecutions, executions);
        Assert.Equal(1, provider.RequestCount);
        Assert.Contains(events, e => e.Kind == ChatOrchestrationEventKind.ToolLimitReached);
        Assert.DoesNotContain(events, e => e.Kind == ChatOrchestrationEventKind.TurnLimitReached);
    }

    [Fact]
    public async Task CancellationDuringProviderRequestPropagates()
    {
        using var cts = new CancellationTokenSource();
        var provider = new FakeAiService([], blockOnRequest: true);
        var enumerator = RunAsync(provider, (_, _) => Task.FromResult(Succeeded("unused")), cts.Token).GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync().AsTask();
        await provider.RequestStarted.Task;
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await enumerator.DisposeAsync();
    }

    [Fact]
    public async Task CancellationDuringToolExecutionPropagates()
    {
        using var cts = new CancellationTokenSource();
        var provider = new FakeAiService([Turn(ToolCall(Call("read", "{}")))]);
        async Task<ToolExecutionResult> CancelTool(AiToolCall _, CancellationToken ct)
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return Succeeded("unreachable");
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Collect(RunAsync(provider, CancelTool, cts.Token)));
    }

    [Fact]
    public async Task StopAfterTurnPreservesProposalMetadataWithoutAnotherRequest()
    {
        var provider = new FakeAiService([Turn(ToolCall(Call("edit", "{}")))]);
        var marker = new object();
        var events = await Run(provider, (_, _) => Task.FromResult(new ToolExecutionResult(
            ToolExecutionStatus.Succeeded, "proposal ready", StopAfterTurn: true, Metadata: marker)));

        Assert.Equal(1, provider.RequestCount);
        Assert.Same(marker, events.Single(e => e.Kind == ChatOrchestrationEventKind.ToolCompleted).ToolResult!.Metadata);
    }

    private static Task<List<ChatOrchestrationEvent>> Run(
        FakeAiService provider,
        Func<AiToolCall, CancellationToken, Task<ToolExecutionResult>> execute) =>
        Collect(RunAsync(provider, execute, CancellationToken.None));

    private static IAsyncEnumerable<ChatOrchestrationEvent> RunAsync(
        FakeAiService provider,
        Func<AiToolCall, CancellationToken, Task<ToolExecutionResult>> execute,
        CancellationToken ct) => new ChatOrchestrator(provider, NullLogger<ChatOrchestrator>.Instance)
            .RunAsync(ConversationId, "test-model", [], [DummyTool], execute, ct);

    private static async Task<List<ChatOrchestrationEvent>> Collect(IAsyncEnumerable<ChatOrchestrationEvent> stream)
    {
        var result = new List<ChatOrchestrationEvent>();
        await foreach (var item in stream) result.Add(item);
        return result;
    }

    private static ToolExecutionResult Succeeded(string message) => new(ToolExecutionStatus.Succeeded, message);
    private static AiToolCall Call(string name, string arguments) => new(name, Json(arguments));
    private static AiChatDelta Content(string content) => new(content, null);
    private static AiChatDelta ToolCall(AiToolCall call) => new(null, [call]);
    private static AiChatDelta[] Turn(params AiChatDelta[] deltas) => deltas;
    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class FakeAiService(
        IReadOnlyList<AiChatDelta[]> turns,
        int? failOnRequest = null,
        bool blockOnRequest = false) : IAiService
    {
        private int _turnIndex;
        public int RequestCount { get; private set; }
        public List<List<AiMessage>> RequestMessages { get; } = [];
        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string> GenerateAsync(string prompt, JsonElement? schema = null, int maxTokens = 2000, string? modelId = null) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<string> GenerateStreamAsync(string prompt, int maxTokens = 2000,
            CancellationToken cancellationToken = default, string? modelId = null) =>
            throw new NotSupportedException();

        public async IAsyncEnumerable<AiChatDelta> ChatStreamAsync(IReadOnlyList<AiMessage> messages,
            IReadOnlyList<AiTool> tools, int maxTokens = 4000,
            [EnumeratorCancellation] CancellationToken cancellationToken = default, string? modelId = null)
        {
            RequestCount++;
            RequestMessages.Add(messages.ToList());
            RequestStarted.TrySetResult();
            if (blockOnRequest) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (failOnRequest == RequestCount) throw new InvalidOperationException("provider failed");
            if (_turnIndex >= turns.Count) yield break;
            foreach (var delta in turns[_turnIndex++])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return delta;
            }
        }
    }
}
