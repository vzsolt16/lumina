using System.Text;

namespace Lumina.Services.Documents;

internal static class DocumentValidation
{
    internal const long MaxFileSize = 2 * 1024 * 1024;
    internal const int MaxFileNameLength = 255;

    internal static readonly string[] SupportedExtensions =
    [
        ".txt", ".md", ".pdf", ".docx", ".cs", ".js", ".jsx", ".ts", ".tsx", ".py",
        ".java", ".html", ".css", ".json", ".xml", ".yaml", ".yml", ".sql", ".sh",
        ".bash", ".dockerfile", ".tf", ".go", ".rs", ".toml", ".ini", ".csv", ".log",
        ".graphql", ".vue"
    ];

    internal static string GetSafeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("A valid file name is required.");
        }
        if (name.Length > MaxFileNameLength)
        {
            throw new InvalidOperationException("File name is too long (max 255 characters).");
        }

        return name;
    }

    internal static string GetExtension(string fileName)
    {
        var extension = string.Equals(
                Path.GetFileName(fileName),
                "dockerfile",
                StringComparison.OrdinalIgnoreCase)
            ? ".dockerfile"
            : Path.GetExtension(fileName).ToLowerInvariant();
        if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This file type is not supported.");
        }

        return extension;
    }

    internal static void ValidateUpload(IFormFile file)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("The uploaded file is empty.");
        }
        if (file.Length > MaxFileSize)
        {
            throw new InvalidOperationException("File too large (max 2 MB).");
        }

        _ = GetSafeFileName(file.FileName);
        _ = GetExtension(file.FileName);
    }

    internal static void ValidateEditableContent(string content)
    {
        if (Encoding.UTF8.GetByteCount(content) > MaxFileSize)
        {
            throw new InvalidOperationException("Content too large (max 2 MB).");
        }
    }

    internal static bool HasSameExtension(string originalName, string proposedName) =>
        string.Equals(
            GetExtension(originalName),
            GetExtension(proposedName),
            StringComparison.OrdinalIgnoreCase);
}
