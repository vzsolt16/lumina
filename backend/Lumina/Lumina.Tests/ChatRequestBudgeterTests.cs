using System.Text.Json;
using Lumina.Services.AI;
using Lumina.Services.Chat;
using Microsoft.Extensions.Configuration;

namespace Lumina.Tests;

public sealed class ChatRequestBudgeterTests
{
    private static readonly GeminiModelDefinition Model = new("test", "Test", "test-model");
    private sealed class Catalog : IGeminiModelCatalog
    {
        public string DefaultModelId => "test";
        public IReadOnlyList<GeminiModelDefinition> Models => [Model];
        public GeminiModelDefinition Resolve(string? modelId) => Model;
    }

    [Fact]
    public void RemovesEvidenceBeforeHistoryAndPreservesCurrentQuestionAndSystem()
    {
        var budgeter = Create();
        var messages = new List<AiMessage>
        {
            new("system", "policy"), new("user", new string('d', 600), Purpose: AiMessagePurpose.Evidence),
            new("user", new string('q', 100), Purpose: AiMessagePurpose.History),
            new("assistant", new string('a', 100), Purpose: AiMessagePurpose.History), new("user", "current")
        };
        Assert.True(budgeter.Fit(messages, [], 80));
        Assert.StartsWith("policy", messages[0].Content);
        Assert.Equal("current", messages[^1].Content);
        Assert.DoesNotContain(messages, m => m.Purpose == AiMessagePurpose.Evidence);
        Assert.DoesNotContain(messages, m => m.Purpose == AiMessagePurpose.History);
    }

    [Fact]
    public void KeepsToolProtocolAndRefusesIfRequiredRequestCannotFit()
    {
        var budgeter = Create();
        var messages = new List<AiMessage> { new("system", "required"), new("user", "current"), new("tool", "result") };
        Assert.False(budgeter.Fit(messages, [], 1));
        Assert.Equal(3, messages.Count);
    }

    private static ChatRequestBudgeter Create() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Gemini:Models:test:ContextTokens"] = "1024", ["Chat:MaxOutputTokens"] = "100", ["Chat:ContextSafetyMarginTokens"] = "50" }).Build(), new Catalog());
}
