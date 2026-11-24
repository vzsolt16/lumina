using System.Runtime.CompilerServices;
using System.Text.Json;
using Lumina.Data;
using Lumina.Models;
using Lumina.Services.AI;
using Lumina.Services.Chat;
using Lumina.Services.Documents;
using Lumina.Services.Rag;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Lumina.Tests;

public sealed class ChatServiceOrchestrationTests
{
    private const string TargetNodeId = "node-00000000000000000000000000000001";
    [Fact]
    public async Task RenameStillAppliesImmediatelyAndCleanExchangePersists()
    {
        var ai = new ScriptedAiService([[new AiChatDelta(null, [Call("rename_document", "{\"new_name\":\"Renamed\"}")])]]);
        await using var provider = CreateProvider(ai);
        var (documentId, conversationId, userId) = await Seed(provider);

        using (var scope = provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var events = await Collect(chat.StreamAnswerAsync(conversationId, userId, "Rename this", null));
            Assert.Contains(events, item => item.Type == "document" && item.Document?.FileName == "Renamed.md");
            Assert.Equal(2, (await chat.GetMessagesAsync(documentId, conversationId, userId)).Count);
        }

        using var verify = provider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<LuminaDbContext>();
        Assert.Equal("Renamed.md", await db.Documents.Where(d => d.Id == documentId).Select(d => d.FileName).SingleAsync());
        Assert.Equal("Rename this", await db.ChatMessages.Where(m => m.ConversationId == conversationId && m.Role == "user").Select(m => m.Content).SingleAsync());
    }

    [Fact]
    public async Task HighLevelEditCreatesPendingProposalAndRequiresApproval()
    {
        var args = JsonSerializer.Serialize(new
        {
            summary = "Rewrite the paragraph in two passes",
            changes = new object[]
            {
                new { action = "replace_block", target = TargetNodeId, replacement = "Updated paragraph" },
                new { action = "replace_block", target = TargetNodeId, replacement = "Final paragraph" }
            }
        });
        var ai = new ScriptedAiService([[new AiChatDelta(null, [Call("propose_document_edit", args)])]]);
        await using var provider = CreateProvider(ai);
        var (documentId, conversationId, userId) = await Seed(provider, "Original paragraph");

        Guid assistantMessageId;
        using (var scope = provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var events = await Collect(chat.StreamAnswerAsync(conversationId, userId, "Rewrite this paragraph", null));
            var proposal = Assert.Single(events, item => item.Type == "proposal");
            Assert.Equal("structured", proposal.Proposal!.Kind);
            Assert.Contains("Replace paragraph", proposal.Proposal.Target);
            assistantMessageId = proposal.Proposal.MessageId;

            var beforeApproval = await scope.ServiceProvider.GetRequiredService<LuminaDbContext>()
                .Documents.Where(d => d.Id == documentId).Select(d => d.Content).SingleAsync();
            Assert.Equal("Original paragraph", beforeApproval);

            await chat.ApplyProposalAsync(documentId, conversationId, assistantMessageId, userId);
            await Assert.ThrowsAsync<InvalidOperationException>(() => chat.ApplyProposalAsync(documentId, conversationId, assistantMessageId, userId));
        }

        using var verify = provider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<LuminaDbContext>();
        Assert.Equal("Final paragraph", await db.Documents.Where(d => d.Id == documentId).Select(d => d.Content).SingleAsync());
        Assert.Equal(2, await db.Documents.Where(d => d.Id == documentId).Select(d => d.ContentVersion).SingleAsync());
        Assert.Equal(EditProposalStatus.Applied, await db.ChatMessages.Where(m => m.Id == assistantMessageId).Select(m => m.ProposalStatus).SingleAsync());
        Assert.Equal(2, await db.ChatMessages.CountAsync(m => m.ConversationId == conversationId));
    }

    [Fact]
    public async Task SourceTextEditUsesExactTargetAndApprovalWithoutRewritingOtherWhitespace()
    {
        var args = JsonSerializer.Serialize(new
        {
            summary = "Update the return value",
            changes = new[] { new { action = "replace_text", target = "return 1", replacement = "return 2" } }
        });
        await using var provider = CreateProvider(new ScriptedAiService([[new AiChatDelta(null, [Call("propose_document_edit", args)])]]));
        var original = "def run():\r\n    return 1\r\n";
        var (documentId, conversationId, userId) = await Seed(provider, original, sourceText: true);
        Guid messageId;
        using (var scope = provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var events = await Collect(chat.StreamAnswerAsync(conversationId, userId, "Change the return value", null));
            var proposal = Assert.Single(events, item => item.Type == "proposal").Proposal!;
            Assert.Equal("text", proposal.Kind);
            Assert.Contains("return 1", proposal.Replacement);
            Assert.Equal(original, await scope.ServiceProvider.GetRequiredService<LuminaDbContext>().Documents
                .Where(d => d.Id == documentId).Select(d => d.Content).SingleAsync());
            messageId = proposal.MessageId;
            await chat.ApplyProposalAsync(documentId, conversationId, messageId, userId);
        }
        using var verify = provider.CreateScope();
        var stored = await verify.ServiceProvider.GetRequiredService<LuminaDbContext>().Documents.SingleAsync(d => d.Id == documentId);
        Assert.Equal("def run():\r\n    return 2\r\n", stored.Content);
        Assert.Equal(string.Empty, stored.ContentJson);
        Assert.Equal(2, stored.ContentVersion);
    }

    [Fact]
    public async Task ProviderFailureDoesNotPersistPartialExchange()
    {
        var logs = new CapturingLoggerProvider();
        var ai = new ScriptedAiService([], fail: true, failAfterUsage: true);
        await using var provider = CreateProvider(ai, logs);
        var (_, conversationId, userId) = await Seed(provider);

        using (var scope = provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await Collect(chat.StreamAnswerAsync(conversationId, userId, "This should not persist", null)));
        }

        using var verify = provider.CreateScope();
        Assert.Empty(await verify.ServiceProvider.GetRequiredService<LuminaDbContext>()
            .ChatMessages.Where(m => m.ConversationId == conversationId).ToListAsync());
        var summary = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Chat interaction summary.", StringComparison.Ordinal));
        Assert.Equal("Failed", summary.Properties["Outcome"]);
        Assert.Equal(1, (int)summary.Properties["ProviderRequests"]!);
        Assert.Equal(4, (int)summary.Properties["TotalInputTokens"]!);
        Assert.Equal(2, (int)summary.Properties["TotalOutputTokens"]!);
        Assert.Equal(6, (int)summary.Properties["TotalTokens"]!);
        var failedExecution = await verify.ServiceProvider.GetRequiredService<LuminaDbContext>()
            .RagExecutions.SingleAsync(x => x.ConversationId == conversationId);
        Assert.Equal("Failed", failedExecution.Status);
        Assert.Equal("generation", failedExecution.FailureStage);
        Assert.Equal(4, failedExecution.InputTokens);
        Assert.Null(failedExecution.Answer);
    }

    [Fact]
    public async Task MultipleProviderRequestsAggregateUsageAndEmitOneSummary()
    {
        var logs = new CapturingLoggerProvider();
        var ai = new ScriptedAiService([
            [new AiChatDelta(null, [Call("rename_document", "{\"new_name\":\"Renamed\"}")], new AiUsageMetadata(5, 2, 7))],
            [new AiChatDelta("Done", null, new AiUsageMetadata(10, 3, 13))]
        ]);
        await using var provider = CreateProvider(ai, logs);
        var (documentId, conversationId, userId) = await Seed(provider);

        using (var scope = provider.CreateScope())
            await Collect(scope.ServiceProvider.GetRequiredService<IChatService>()
                .StreamAnswerAsync(conversationId, userId, "Rename and finish", null));

        var summary = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Chat interaction summary.", StringComparison.Ordinal));
        Assert.Equal(documentId, summary.Properties["DocumentId"]);
        Assert.Equal(2, (int)summary.Properties["ProviderRequests"]!);
        Assert.Equal(15, (int)summary.Properties["TotalInputTokens"]!);
        Assert.Equal(5, (int)summary.Properties["TotalOutputTokens"]!);
        Assert.Equal(20, (int)summary.Properties["TotalTokens"]!);
        Assert.Equal(1, (int)summary.Properties["ToolExecutions"]!);
        Assert.Equal("Completed", summary.Properties["Outcome"]);

        using (var verify = provider.CreateScope())
        {
            var execution = await verify.ServiceProvider.GetRequiredService<LuminaDbContext>()
                .RagExecutions.SingleAsync(x => x.ConversationId == conversationId);
            Assert.Equal("Completed", execution.Status);
            Assert.Equal("Rename and finish", execution.Query);
            Assert.Equal(15, execution.InputTokens);
            Assert.Contains("Original content", execution.ContextJson);
            Assert.Equal("Done", execution.Answer);
        }

        var usage = logs.Entries.Where(entry => entry.Message.StartsWith("Chat provider usage.", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, usage.Length);
        Assert.Equal(new[] { 1, 2 }, usage.Select(entry => (int)entry.Properties["RequestNumber"]!).ToArray());
        Assert.Equal(new[] { 1, 0 }, usage.Select(entry => (int)entry.Properties["ToolCallCount"]!).ToArray());
    }

    [Fact]
    public async Task FullDocumentAndMultipleSemanticToolSearchesAreRecordedSeparatelyAndInOrder()
    {
        var ai = new ScriptedAiService([
            [new AiChatDelta(null, [Call("search_document", "{\"query\":\"island ferry crossing\"}")], new AiUsageMetadata(11, 1, 12))],
            [new AiChatDelta(null, [Call("search_document", "{\"query\":\"Calder Reach longest crossing\"}")], new AiUsageMetadata(17, 1, 18))],
            [new AiChatDelta("Sundergrave.", null, new AiUsageMetadata(23, 2, 25))]
        ]);
        await using var provider = CreateProvider(ai, selector: new PresetContextSelector(
            new RagContext("full", "Complete document text", [], 25),
            new RagContext("rag", "Passage one\n\nPassage two\n\nPassage three\n\nPassage four\n\nPassage five",
                Enumerable.Range(0, 5).Select(index => new Lumina.DTOs.Chat.ChatSource(
                    Guid.Empty, "Notes.md", "Ferries", index, [$"node-{index}"], ["Ferries"], 1,
                    "semantic", 0.9f - index / 100f)).ToArray(), 25,
                RetrievalOutcome.Complete, true, null, 5, 5, 0)));
        var (documentId, conversationId, userId) = await Seed(provider);

        using (var scope = provider.CreateScope())
            await Collect(scope.ServiceProvider.GetRequiredService<IChatService>()
                .StreamAnswerAsync(conversationId, userId, "Which island has the longest ferry crossing?", null));

        using var verify = provider.CreateScope();
        var execution = await verify.ServiceProvider.GetRequiredService<LuminaDbContext>()
            .RagExecutions.SingleAsync(item => item.ConversationId == conversationId);
        using var details = JsonDocument.Parse(execution.ContextJson!);
        var root = details.RootElement;
        Assert.Equal("full", root.GetProperty("initialContext").GetProperty("strategy").GetString());
        Assert.Equal(0, root.GetProperty("initialContext").GetProperty("retrievedChunkCount").GetInt32());
        var operations = root.GetProperty("toolRetrievals").EnumerateArray().ToArray();
        Assert.Equal(2, operations.Length);
        Assert.Equal(new[] { 1, 2 }, operations.Select(item => item.GetProperty("order").GetInt32()).ToArray());
        Assert.Equal(new[] { "island ferry crossing", "Calder Reach longest crossing" },
            operations.Select(item => item.GetProperty("query").GetString()).ToArray());
        Assert.All(operations, item =>
        {
            Assert.Equal("semantic", item.GetProperty("strategy").GetString());
            Assert.Equal(5, item.GetProperty("acceptedCandidateCount").GetInt32());
            Assert.Equal(5, item.GetProperty("sources").GetArrayLength());
        });
        Assert.Equal(10, root.GetProperty("sourceAccounting").GetProperty("toolRetrievedPassages").GetInt32());
        Assert.Equal(10, execution.RetrievedChunkCount);
        Assert.Equal(3, root.GetProperty("providerRequests").GetArrayLength());
        Assert.Equal(11, root.GetProperty("providerRequests")[0].GetProperty("inputTokens").GetInt32());
        Assert.Equal("search_document", root.GetProperty("providerRequests")[0].GetProperty("toolCalls")[0].GetString());
        Assert.Equal(documentId, execution.DocumentId);
    }

    [Fact]
    public async Task FailedToolSemanticRetrievalIsCaptured()
    {
        var ai = new ScriptedAiService([
            [new AiChatDelta(null, [Call("search_document", "{\"query\":\"missing evidence\"}")])],
            [new AiChatDelta("I could not retrieve it.", null)]
        ]);
        await using var provider = CreateProvider(ai, selector: new PresetContextSelector(
            new RagContext("full", "Complete document", [], 10),
            new RagContext("failed", "", [], 0, RetrievalOutcome.Failed, false, "Vector retrieval failed.", 5, 0, 5)));
        var (_, conversationId, userId) = await Seed(provider);
        using (var scope = provider.CreateScope())
            await Collect(scope.ServiceProvider.GetRequiredService<IChatService>()
                .StreamAnswerAsync(conversationId, userId, "Find it", null));

        using var verify = provider.CreateScope();
        var execution = await verify.ServiceProvider.GetRequiredService<LuminaDbContext>()
            .RagExecutions.SingleAsync(item => item.ConversationId == conversationId);
        using var details = JsonDocument.Parse(execution.ContextJson!);
        var operation = Assert.Single(details.RootElement.GetProperty("toolRetrievals").EnumerateArray());
        Assert.Equal("Failed", operation.GetProperty("outcome").GetString());
        Assert.Equal(5, operation.GetProperty("candidateCount").GetInt32());
        Assert.Equal(0, operation.GetProperty("acceptedCandidateCount").GetInt32());
        Assert.Equal(0, operation.GetProperty("returnedSourceCount").GetInt32());
        Assert.Equal(0, execution.RetrievedChunkCount);
    }

    [Fact]
    public async Task FullCompleteSourceTextHidesRedundantTextToolButKeepsPolicyAndSearch()
    {
        var ai = new ScriptedAiService([[new AiChatDelta("Answered from context.", null)]]);
        await using var provider = CreateProvider(ai);
        var (_, conversationId, userId) = await Seed(provider, "Complete source text", sourceText: true);
        using (var scope = provider.CreateScope())
            await Collect(scope.ServiceProvider.GetRequiredService<IChatService>()
                .StreamAnswerAsync(conversationId, userId, "A question", null));

        var declaredNames = ai.ToolRequests.Single().Select(tool => tool.Name).ToArray();
        Assert.DoesNotContain("get_document_text", declaredNames);
        Assert.Contains("search_document", declaredNames);
        Assert.Contains("A complete current copy of the document is supplied below", ai.Requests.Single().Single(message => message.Role == "system").Content);
        Assert.Contains("do not call get_document_text merely to reread it", ai.Requests.Single().Single(message => message.Role == "system").Content);
    }

    [Fact]
    public async Task PartialSourceTextRetainsDocumentTextTool()
    {
        var ai = new ScriptedAiService([[new AiChatDelta("Answered from the supplied section.", null)]]);
        var partial = new RagContext("rag", "Relevant passage", [], 15, RetrievalOutcome.Partial, false, "Other content omitted.");
        await using var provider = CreateProvider(ai, selector: new PresetContextSelector(partial, partial));
        var (_, conversationId, userId) = await Seed(provider, "Long source text", sourceText: true);
        using (var scope = provider.CreateScope())
            await Collect(scope.ServiceProvider.GetRequiredService<IChatService>()
                .StreamAnswerAsync(conversationId, userId, "A question", null));

        Assert.Contains(ai.ToolRequests.Single(), tool => tool.Name == "get_document_text");
    }

    [Fact]
    public async Task CancellationEmitsOneCancelledSummary()
    {
        var logs = new CapturingLoggerProvider();
        var ai = new ScriptedAiService([], block: true);
        await using var provider = CreateProvider(ai, logs);
        var (_, conversationId, userId) = await Seed(provider);
        using var cancellation = new CancellationTokenSource();
        using var scope = provider.CreateScope();
        var enumerator = scope.ServiceProvider.GetRequiredService<IChatService>()
            .StreamAnswerAsync(conversationId, userId, "Wait", null, cancellation.Token).GetAsyncEnumerator();
        var pending = enumerator.MoveNextAsync().AsTask();
        await ai.RequestStarted.Task;
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await enumerator.DisposeAsync();

        var summary = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Chat interaction summary.", StringComparison.Ordinal));
        Assert.Equal("Cancelled", summary.Properties["Outcome"]);
        Assert.Equal(1, (int)summary.Properties["ProviderRequests"]!);
    }

    [Fact]
    public async Task TurnLimitEmitsOneTurnLimitSummaryWithAllToolExecutions()
    {
        var logs = new CapturingLoggerProvider();
        var turns = Enumerable.Range(0, ChatOrchestrator.MaxModelTurns)
            .Select(i => new[] { new AiChatDelta(null, [Call($"missing_{i}", "{}")]) }).ToArray();
        await using var provider = CreateProvider(new ScriptedAiService(turns), logs);
        var (_, conversationId, userId) = await Seed(provider);
        using (var scope = provider.CreateScope())
            await Collect(scope.ServiceProvider.GetRequiredService<IChatService>()
                .StreamAnswerAsync(conversationId, userId, "Keep trying", null));

        var summary = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Chat interaction summary.", StringComparison.Ordinal));
        Assert.Equal("TurnLimitReached", summary.Properties["Outcome"]);
        Assert.Equal(ChatOrchestrator.MaxModelTurns, (int)summary.Properties["ProviderRequests"]!);
        Assert.Equal(ChatOrchestrator.MaxModelTurns, (int)summary.Properties["ToolExecutions"]!);
    }

    [Fact]
    public async Task MissingConversationStillEmitsOneFailureSummary()
    {
        var logs = new CapturingLoggerProvider();
        await using var provider = CreateProvider(new ScriptedAiService([]), logs);
        using var scope = provider.CreateScope();
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await Collect(scope.ServiceProvider.GetRequiredService<IChatService>()
                .StreamAnswerAsync(Guid.NewGuid(), Guid.NewGuid(), "Missing", null)));

        var summary = Assert.Single(logs.Entries, entry => entry.Message.StartsWith("Chat interaction summary.", StringComparison.Ordinal));
        Assert.Equal("Failed", summary.Properties["Outcome"]);
        Assert.Null(summary.Properties["DocumentId"]);
        Assert.Equal(0, (int)summary.Properties["ProviderRequests"]!);
    }

    [Fact]
    public async Task RejectingHighLevelProposalLeavesCanonicalDocumentUnchanged()
    {
        var args = JsonSerializer.Serialize(new
        {
            summary = "Replace the paragraph",
            changes = new[] { new { action = "replace_block", target = TargetNodeId, replacement = "Rejected text" } }
        });
        await using var provider = CreateProvider(new ScriptedAiService([[new AiChatDelta(null, [Call("propose_document_edit", args)])]]));
        var (documentId, conversationId, userId) = await Seed(provider, "Original paragraph");
        using (var scope = provider.CreateScope())
        {
            var chat = scope.ServiceProvider.GetRequiredService<IChatService>();
            var events = await Collect(chat.StreamAnswerAsync(conversationId, userId, "Rewrite it", null));
            var proposal = Assert.Single(events, item => item.Type == "proposal").Proposal!;
            await chat.RejectProposalAsync(documentId, conversationId, proposal.MessageId, userId);
        }
        using var verify = provider.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<LuminaDbContext>();
        Assert.Equal("Original paragraph", await db.Documents.Where(d => d.Id == documentId).Select(d => d.Content).SingleAsync());
        Assert.Equal(EditProposalStatus.Rejected, await db.ChatMessages.Where(m => m.ConversationId == conversationId && m.Role == "assistant").Select(m => m.ProposalStatus).SingleAsync());
    }

    [Fact]
    public async Task HighLevelToolCanInsertAfterAResolvedCurrentNode()
    {
        var args = JsonSerializer.Serialize(new
        {
            summary = "Add a follow-up paragraph",
            changes = new[] { new { action = "insert_after", target = TargetNodeId, replacement = "Follow-up", node_type = "paragraph" } }
        });
        var ai = new ScriptedAiService([[new AiChatDelta(null, [Call("propose_document_edit", args)])]]);
        await using var provider = CreateProvider(ai);
        var (documentId, conversationId, userId) = await Seed(provider, "Original paragraph");
        using var scope = provider.CreateScope();
        var events = await Collect(scope.ServiceProvider.GetRequiredService<IChatService>()
            .StreamAnswerAsync(conversationId, userId, "Add a paragraph after this", null));
        var proposalEvent = events.FirstOrDefault(item => item.Type == "proposal");
        Assert.True(proposalEvent is not null, string.Join("\n", ai.Requests.SelectMany(request => request)
            .Where(message => message.Role == "tool").Select(message => message.Content)));
        var proposal = proposalEvent!.Proposal!;
        await scope.ServiceProvider.GetRequiredService<IChatService>().ApplyProposalAsync(documentId, conversationId, proposal.MessageId, userId);
        using var verify = provider.CreateScope();
        Assert.Equal("Original paragraph\nFollow-up", await verify.ServiceProvider.GetRequiredService<LuminaDbContext>()
            .Documents.Where(d => d.Id == documentId).Select(d => d.Content).SingleAsync());
    }

    private static ServiceProvider CreateProvider(IAiService ai, CapturingLoggerProvider? logs = null,
        IDocumentContextSelector? selector = null, bool captureContent = true)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => { if (logs is not null) builder.AddProvider(logs); });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Rag:Telemetry:CaptureContent"] = captureContent.ToString() }).Build());
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<LuminaDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IAiService>(ai);
        services.AddSingleton<IDocumentContentConverter, ProseMirrorDocumentConverter>();
        services.AddSingleton<IDocumentContextBuilder, DocumentContextBuilder>();
        services.AddSingleton<IDocumentContextSelector>(selector ?? new FixedContextSelector());
        services.AddSingleton<IGeminiModelCatalog, TestModelCatalog>();
        services.AddSingleton<ChatRequestBudgeter>();
        services.AddScoped<IDocumentEditorService, DocumentEditorService>();
        services.AddScoped<RagTelemetryService>();
        services.AddScoped<ChatOrchestrator>();
        services.AddScoped<IChatService, ChatService>();
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid DocumentId, Guid ConversationId, Guid UserId)> Seed(IServiceProvider provider, string content = "Original content", bool sourceText = false)
    {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LuminaDbContext>();
        var userId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        db.Users.Add(new ApplicationUser { Id = userId, UserName = "user@test", Email = "user@test" });
        db.Documents.Add(new Document
        {
            Id = documentId,
            UserId = userId,
            FileName = sourceText ? "source.py" : "Notes.md",
            Content = content,
            ContentJson = sourceText ? string.Empty : $"{{\"type\":\"doc\",\"content\":[{{\"type\":\"paragraph\",\"attrs\":{{\"id\":\"{TargetNodeId}\"}},\"content\":[{{\"type\":\"text\",\"text\":{JsonSerializer.Serialize(content)}}}]}}]}}",
            ContentVersion = 1
        });
        db.ChatConversations.Add(new ChatConversation
        {
            Id = conversationId, DocumentId = documentId, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return (documentId, conversationId, userId);
    }

    private static AiToolCall Call(string name, string arguments) => new(name,
        JsonDocument.Parse(arguments).RootElement.Clone());

    private static async Task<List<Lumina.DTOs.Chat.ChatStreamEvent>> Collect(
        IAsyncEnumerable<Lumina.DTOs.Chat.ChatStreamEvent> stream)
    {
        var result = new List<Lumina.DTOs.Chat.ChatStreamEvent>();
        await foreach (var item in stream) result.Add(item);
        return result;
    }

    private sealed class FixedContextSelector : IDocumentContextSelector
    {
        public Task<RagContext> SelectAsync(Guid documentId, Guid userId, string question,
            IReadOnlyList<string> history, CancellationToken ct, int? evidenceBudgetTokens = null, bool forceSemantic = false) => Task.FromResult(new RagContext("full", "Original content", [], 5));
    }

    private sealed class PresetContextSelector(RagContext initial, RagContext semantic) : IDocumentContextSelector
    {
        public Task<RagContext> SelectAsync(Guid documentId, Guid userId, string question,
            IReadOnlyList<string> history, CancellationToken ct, int? evidenceBudgetTokens = null, bool forceSemantic = false)
        {
            var selected = forceSemantic ? semantic : initial;
            return Task.FromResult(selected with
            {
                Sources = selected.Sources.Select(source => source with { DocumentId = documentId }).ToArray()
            });
        }
    }

    private sealed class TestModelCatalog : IGeminiModelCatalog
    {
        private static readonly GeminiModelDefinition Model = new("gemini-flash", "Test", "test-model");
        public string DefaultModelId => Model.Id;
        public IReadOnlyList<GeminiModelDefinition> Models => [Model];
        public GeminiModelDefinition Resolve(string? modelId) => Model;
    }

    private sealed class ScriptedAiService(IReadOnlyList<AiChatDelta[]> turns, bool fail = false, bool block = false,
        bool failAfterUsage = false) : IAiService
    {
        private int _index;
        public List<AiMessage[]> Requests { get; } = [];
        public List<IReadOnlyList<AiTool>> ToolRequests { get; } = [];
        public TaskCompletionSource RequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> GenerateAsync(string prompt, JsonElement? schema = null, int maxTokens = 2000, string? modelId = null) => throw new NotSupportedException();
        public IAsyncEnumerable<string> GenerateStreamAsync(string prompt, int maxTokens = 2000, CancellationToken cancellationToken = default, string? modelId = null) => throw new NotSupportedException();
        public async IAsyncEnumerable<AiChatDelta> ChatStreamAsync(IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools,
            int maxTokens = 4000, [EnumeratorCancellation] CancellationToken cancellationToken = default, string? modelId = null)
        {
            RequestStarted.TrySetResult();
            if (block) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (fail)
            {
                if (failAfterUsage) yield return new AiChatDelta(null, null, new AiUsageMetadata(4, 2, 6));
                throw new InvalidOperationException("provider failed");
            }
            Requests.Add(messages.ToArray());
            ToolRequests.Add(tools.ToArray());
            if (_index >= turns.Count) yield break;
            foreach (var delta in turns[_index++])
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return delta;
            }
            await Task.CompletedTask;
        }
    }
}
