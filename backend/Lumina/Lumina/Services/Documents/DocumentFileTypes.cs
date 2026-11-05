namespace Lumina.Services.Documents;

internal static class DocumentFileTypes
{
    internal static readonly string[] SupportedExtensions = DocumentValidation.SupportedExtensions;

    internal static bool IsSupported(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    internal static string GetExtension(string fileName) =>
        DocumentValidation.GetExtension(fileName);

    internal static bool HasSameExtension(string originalName, string proposedName) =>
        DocumentValidation.HasSameExtension(originalName, proposedName);
}
