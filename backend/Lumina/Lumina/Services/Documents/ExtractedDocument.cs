using System.Text.Json.Nodes;

namespace Lumina.Services.Documents;

public sealed class ExtractedDocument
{
    public string? ContentJson { get; init; }

    public List<ExtractedAsset> Assets { get; init; } = [];
}

public sealed class ExtractedAsset
{
    public string ReferenceId { get; init; } = string.Empty;

    public string FileName { get; init; } = string.Empty;

    public string ContentType { get; init; } = string.Empty;

    public byte[] Content { get; init; } = [];

    public int? Width { get; init; }

    public int? Height { get; init; }
}
