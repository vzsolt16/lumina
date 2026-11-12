namespace Lumina.Models;

public class DocumentAsset
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public Document Document { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public DateTime CreatedAt { get; set; }
}
