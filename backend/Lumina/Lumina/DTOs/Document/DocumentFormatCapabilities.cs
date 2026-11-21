namespace Lumina.DTOs.Document;

public sealed class DocumentFormatCapabilities
{
    public string Extension { get; set; } = "";
    public string Representation { get; set; } = "";
    public bool Upload { get; set; }
    public bool OriginalFilePreserved { get; set; }
    public bool TextExtraction { get; set; }
    public bool InAppEditing { get; set; }
    public bool AiEditing { get; set; }
    public bool SemanticIndexing { get; set; }
    public bool CurrentContentDownload { get; set; }
    public bool OriginalFormatReconstruction { get; set; }
    public bool EditsAffectExtractedContentOnly { get; set; }
    public bool LegacyStructuredRepresentation { get; set; }
}
