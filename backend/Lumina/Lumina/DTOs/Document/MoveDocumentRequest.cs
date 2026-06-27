namespace Lumina.DTOs.Document;

public class MoveDocumentRequest
{
    // Null moves the document to the root level (out of any folder).
    public Guid? FolderId { get; set; }
}
