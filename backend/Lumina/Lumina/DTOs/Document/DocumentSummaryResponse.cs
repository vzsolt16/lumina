namespace Lumina.DTOs.Document;

public class DocumentSummaryResponse
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = "";

    public DateTime UploadedAt { get; set; }
}