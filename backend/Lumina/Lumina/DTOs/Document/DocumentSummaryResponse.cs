namespace Lumina.DTOs.Document;

public class DocumentSummaryResponse
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = "";

    public Guid? FolderId { get; set; }

    public DateTime UploadedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}