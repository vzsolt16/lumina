namespace Lumina.DTOs.Document;

public class UploadDocumentResponse
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;
}