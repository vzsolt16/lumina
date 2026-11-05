using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;

namespace Lumina.Services.Documents;

public sealed class DocumentContentExtractor
{
    private const long MaxExtractedContentSize = 2 * 1024 * 1024;
    private readonly IReadOnlyList<IDocumentContentExtractor> _extractors;

    public DocumentContentExtractor(IEnumerable<IDocumentContentExtractor> extractors)
    {
        _extractors = extractors.ToList();
    }

    public async Task<string> ExtractAsync(
        string extension,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanExtract(extension));
        if (extractor is null)
        {
            throw new InvalidOperationException("Unsupported document format.");
        }

        var extracted = await extractor.ExtractAsync(content, cancellationToken);
        if (Encoding.UTF8.GetByteCount(extracted) > MaxExtractedContentSize)
        {
            throw new InvalidOperationException("The extracted document content is too large.");
        }

        return extracted;
    }
}

public sealed class PlainTextContentExtractor : IDocumentContentExtractor
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".cs", ".js", ".jsx", ".ts", ".tsx", ".py", ".java", ".html",
        ".css", ".json", ".xml", ".yaml", ".yml", ".sql", ".sh", ".bash", ".dockerfile",
        ".tf", ".go", ".rs", ".toml", ".ini", ".csv", ".log", ".graphql", ".vue"
    };

    public bool CanExtract(string extension) => Extensions.Contains(extension);

    public async Task<string> ExtractAsync(Stream content, CancellationToken cancellationToken = default)
    {
        try
        {
            using var reader = new StreamReader(
                content,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);
            var text = await reader.ReadToEndAsync(cancellationToken);
            if (text.Contains('\0'))
            {
                throw new InvalidOperationException("The text file contains binary data.");
            }

            return text;
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidOperationException("The text file uses an unsupported encoding.");
        }
    }
}

public sealed class PdfContentExtractor : IDocumentContentExtractor
{
    public bool CanExtract(string extension) =>
        string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractAsync(Stream content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var document = PdfDocument.Open(content);
            return Task.FromResult(string.Join(
                Environment.NewLine,
                document.GetPages().Select(page => page.Text)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("The PDF file is invalid or could not be read.", ex);
        }
    }
}

public sealed class DocxContentExtractor : IDocumentContentExtractor
{
    private const int MaxArchiveEntries = 256;
    private const long MaxEntrySize = 8 * 1024 * 1024;

    public bool CanExtract(string extension) =>
        string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase);

    public async Task<string> ExtractAsync(
        Stream content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);
            if (archive.Entries.Count > MaxArchiveEntries)
            {
                throw new InvalidOperationException("The DOCX archive contains too many entries.");
            }

            var entry = archive.GetEntry("word/document.xml")
                ?? throw new InvalidOperationException("The DOCX file is missing its document body.");
            if (entry.Length > MaxEntrySize)
            {
                throw new InvalidOperationException("The DOCX document body is too large.");
            }

            await using var entryStream = entry.Open();
            await using var buffer = new MemoryStream();
            await entryStream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;

            var document = XDocument.Load(buffer);
            XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            return string.Join(
                Environment.NewLine,
                document.Descendants(word + "p")
                    .Select(paragraph => string.Concat(
                        paragraph.Descendants(word + "t").Select(text => text.Value))));
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new InvalidOperationException("The DOCX file is invalid or could not be read.", ex);
        }
    }
}
