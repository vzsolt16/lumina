namespace Lumina.DTOs.Document;

public class UpdateDocumentRequest
{
    // Both fields are optional: a rename-only request omits Content, while a
    // content edit sends both. Only non-null fields are applied.
    public string? FileName { get; set; }

    public string? Content { get; set; }
}
