using System.Text.Json;

namespace Lumina.Services.Documents;

public abstract record DocumentOperation(int ExpectedContentVersion);

public sealed record ReplaceDocumentNode(
    int ExpectedContentVersion,
    string TargetId,
    string NewText) : DocumentOperation(ExpectedContentVersion);

public sealed record InsertDocumentNode(
    int ExpectedContentVersion,
    string? ParentId,
    int Index,
    string NodeType,
    string Content,
    IReadOnlyDictionary<string, JsonElement>? Attributes = null) : DocumentOperation(ExpectedContentVersion);

public sealed record DeleteDocumentNode(
    int ExpectedContentVersion,
    string TargetId) : DocumentOperation(ExpectedContentVersion);

public sealed record MoveDocumentNode(
    int ExpectedContentVersion,
    string TargetId,
    string? ParentId,
    int Index) : DocumentOperation(ExpectedContentVersion);
