namespace Lumina.DTOs.Document;

public sealed class DocumentAssetResponse
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }
}
