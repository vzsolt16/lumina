namespace Lumina.DTOs.Document;

public class DocumentDetailResponse
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = "";

    public long FileSize { get; set; }

    public long ContentSize { get; set; }

    public int ContentVersion { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string Content { get; set; } = "";
}
