using Lumina.DTOs.Document;
using System.Text.Json;
using System.Xml.Linq;

namespace Lumina.Services.Documents;

/// <summary>Central capability policy for the formats Lumina currently accepts.</summary>
public static class DocumentFormatDescriptor
{
    public static readonly string[] SupportedExtensions =
    [
        ".txt", ".md", ".pdf", ".docx", ".cs", ".js", ".jsx", ".ts", ".tsx", ".py",
        ".java", ".html", ".css", ".json", ".xml", ".yaml", ".yml", ".sql", ".sh",
        ".bash", ".dockerfile", ".tf", ".go", ".rs", ".toml", ".ini", ".graphql", ".vue"
    ];
    public static readonly string[] RecognizedExtensions = [.. SupportedExtensions, ".csv", ".log"];
    private static readonly HashSet<string> SourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".cs", ".js", ".jsx", ".ts", ".tsx", ".py", ".java",
        ".html", ".css", ".json", ".xml", ".yaml", ".yml", ".sql", ".sh",
        ".bash", ".dockerfile", ".tf", ".go", ".rs", ".toml", ".ini", ".graphql", ".vue", ".csv", ".log"
    };

    public static bool IsSourceText(string extension) => SourceExtensions.Contains(Normalize(extension));

    private static string ExtensionForFileName(string fileName) =>
        string.Equals(Path.GetFileName(fileName), "dockerfile", StringComparison.OrdinalIgnoreCase)
            ? ".dockerfile"
            : Normalize(Path.GetExtension(fileName));

    public static string GetRepresentation(string extension) => Normalize(extension) switch
    {
        ".pdf" or ".docx" => "extractedRichText",
        _ when IsSourceText(extension) => "sourceText",
        _ => throw new InvalidOperationException("This file type is not supported.")
    };

    public static bool UsesCanonicalSourceText(string fileName, string? contentJson) =>
        IsSourceText(ExtensionForFileName(fileName)) && string.IsNullOrEmpty(contentJson);

    public static void ValidateSourceTextSyntax(string fileName, string content)
    {
        try
        {
            switch (ExtensionForFileName(fileName))
            {
                case ".json": using (JsonDocument.Parse(content)) { } break;
                case ".xml": _ = XDocument.Parse(content); break;
            }
        }
        catch (Exception ex) when (ex is JsonException or System.Xml.XmlException)
        {
            throw new InvalidOperationException($"The proposed {ExtensionForFileName(fileName)} content is not well-formed.", ex);
        }
    }

    public static DocumentFormatCapabilities Describe(string fileName, string? contentJson = null)
    {
        var extension = ExtensionForFileName(fileName);
        var source = IsSourceText(extension);
        var extracted = extension is ".pdf" or ".docx";
        var legacyStructured = source && !string.IsNullOrEmpty(contentJson);
        return new DocumentFormatCapabilities
        {
            Extension = extension,
            Representation = source ? legacyStructured ? "legacyRichText" : "sourceText" : "extractedRichText",
            Upload = true,
            OriginalFilePreserved = true,
            TextExtraction = true,
            InAppEditing = true,
            AiEditing = true,
            SemanticIndexing = true,
            CurrentContentDownload = true,
            OriginalFormatReconstruction = !extracted && !legacyStructured,
            EditsAffectExtractedContentOnly = extracted || legacyStructured,
            LegacyStructuredRepresentation = legacyStructured
        };
    }

    private static string Normalize(string extension) => extension.ToLowerInvariant();
}
