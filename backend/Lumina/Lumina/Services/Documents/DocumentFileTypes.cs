using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace Lumina.Services.Documents;

internal static class DocumentFileTypes
{
    internal static readonly string[] SupportedExtensions = [".txt", ".md", ".pdf", ".docx"];

    internal static bool IsSupported(string fileName) =>
        SupportedExtensions.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase);

    internal static string GetExtension(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant();

    internal static bool HasSameExtension(string originalName, string proposedName) =>
        string.Equals(GetExtension(originalName), GetExtension(proposedName), StringComparison.OrdinalIgnoreCase);

    internal static async Task<string> ExtractTextAsync(IFormFile file)
    {
        await using var input = file.OpenReadStream();
        await using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        buffer.Position = 0;

        return GetExtension(file.FileName) switch
        {
            ".txt" or ".md" => await ReadTextAsync(buffer),
            ".pdf" => ExtractPdfText(buffer),
            ".docx" => ExtractDocxText(buffer),
            _ => throw new InvalidOperationException("Unsupported document format.")
        };
    }

    private static async Task<string> ReadTextAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await reader.ReadToEndAsync();
    }

    private static string ExtractPdfText(Stream stream)
    {
        using var document = PdfDocument.Open(stream);
        return string.Join(Environment.NewLine, document.GetPages().Select(page => page.Text));
    }

    private static string ExtractDocxText(Stream stream)
    {
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("word/document.xml")
            ?? throw new InvalidOperationException("The DOCX file is missing its document body.");

        using var entryStream = entry.Open();
        var document = XDocument.Load(entryStream);
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        return string.Join(
            Environment.NewLine,
            document.Descendants(word + "p")
                .Select(paragraph => string.Concat(paragraph.Descendants(word + "t").Select(text => text.Value))));
    }
}
