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
    public async Task ProviderFailureDoesNotPersistPartialExchange()
    {
        var ai = new ScriptedAiService([], fail: true);
        await using var provider = CreateProvider(ai);
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

    private static ServiceProvider CreateProvider(IAiService ai)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        var databaseName = Guid.NewGuid().ToString();
        services.AddDbContext<LuminaDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<IAiService>(ai);
        services.AddSingleton<IDocumentContentConverter, ProseMirrorDocumentConverter>();
        services.AddSingleton<IDocumentContextBuilder, DocumentContextBuilder>();
        services.AddSingleton<IDocumentContextSelector, FixedContextSelector>();
        services.AddSingleton<IGeminiModelCatalog, TestModelCatalog>();
        services.AddSingleton<ChatRequestBudgeter>();
        services.AddScoped<IDocumentEditorService, DocumentEditorService>();
        services.AddScoped<ChatOrchestrator>();
        services.AddScoped<IChatService, ChatService>();
        return services.BuildServiceProvider();
    }

    private static async Task<(Guid DocumentId, Guid ConversationId, Guid UserId)> Seed(IServiceProvider provider, string content = "Original content")
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
            FileName = "Notes.md",
            Content = content,
            ContentJson = $"{{\"type\":\"doc\",\"content\":[{{\"type\":\"paragraph\",\"attrs\":{{\"id\":\"{TargetNodeId}\"}},\"content\":[{{\"type\":\"text\",\"text\":{JsonSerializer.Serialize(content)}}}]}}]}}",
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

    private sealed class TestModelCatalog : IGeminiModelCatalog
    {
        private static readonly GeminiModelDefinition Model = new("gemini-flash", "Test", "test-model");
        public string DefaultModelId => Model.Id;
        public IReadOnlyList<GeminiModelDefinition> Models => [Model];
        public GeminiModelDefinition Resolve(string? modelId) => Model;
    }

    private sealed class ScriptedAiService(IReadOnlyList<AiChatDelta[]> turns, bool fail = false) : IAiService
    {
        private int _index;
        public List<AiMessage[]> Requests { get; } = [];
        public Task<string> GenerateAsync(string prompt, JsonElement? schema = null, int maxTokens = 2000, string? modelId = null) => throw new NotSupportedException();
        public IAsyncEnumerable<string> GenerateStreamAsync(string prompt, int maxTokens = 2000, CancellationToken cancellationToken = default, string? modelId = null) => throw new NotSupportedException();
        public async IAsyncEnumerable<AiChatDelta> ChatStreamAsync(IReadOnlyList<AiMessage> messages, IReadOnlyList<AiTool> tools,
            int maxTokens = 4000, [EnumeratorCancellation] CancellationToken cancellationToken = default, string? modelId = null)
        {
            if (fail) throw new InvalidOperationException("provider failed");
            Requests.Add(messages.ToArray());
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
