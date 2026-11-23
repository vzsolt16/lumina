using Serilog.Core;
using Serilog.Events;

namespace Lumina.Logging;

/// <summary>Adds a compact subsystem label derived from the existing ILogger category.</summary>
public sealed class SubsystemEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var category = logEvent.Properties.TryGetValue("SourceContext", out var source)
            ? source.ToString().Trim('"')
            : string.Empty;
        var name = category.Split('.').LastOrDefault() ?? category;
        var subsystem = category switch
        {
            var value when value.EndsWith("ChatOrchestrator", StringComparison.Ordinal) => "LLM",
            var value when value.EndsWith("DocumentIndexScheduler", StringComparison.Ordinal) || value.EndsWith("DocumentIndexService", StringComparison.Ordinal) => "WORKER",
            var value when value.Contains("Services.Chat", StringComparison.Ordinal) || value.Contains("WebSockets.ChatHub", StringComparison.Ordinal) => "CHAT",
            var value when value.Contains("Services.Rag", StringComparison.Ordinal) => name.Contains("Embedding", StringComparison.Ordinal) ? "EMBED" : name.Contains("Vector", StringComparison.Ordinal) || name.Contains("Qdrant", StringComparison.Ordinal) ? "VECTOR" : "RAG",
            var value when value.Contains("Services.Documents", StringComparison.Ordinal) || value.Contains("Controllers.Documents", StringComparison.Ordinal) => "DOCS",
            var value when value.Contains("Services.Ai", StringComparison.Ordinal) => "LLM",
            var value when value.Contains("Services.Auth", StringComparison.Ordinal) || value.Contains("Controllers.Auth", StringComparison.Ordinal) => "AUTH",
            var value when value.Contains("Services.Quizzes", StringComparison.Ordinal) || value.Contains("Services.Flashcards", StringComparison.Ordinal) || value.Contains("WebSockets.QuizHub", StringComparison.Ordinal) || value.Contains("WebSockets.FlashcardHub", StringComparison.Ordinal) => "WORKER",
            _ => "APP"
        };
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Subsystem", subsystem));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SourceName", name));
    }
}
