using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
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

    public async Task<ExtractedDocument?> ExtractStructuredAsync(
        string extension,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var extractor = _extractors.FirstOrDefault(e => e.CanExtract(extension));
        if (extractor is null)
        {
            throw new InvalidOperationException("Unsupported document format.");
        }

        var structured = await extractor.ExtractStructuredAsync(content, cancellationToken);
        if (structured?.ContentJson is not null
            && Encoding.UTF8.GetByteCount(structured.ContentJson) > MaxExtractedContentSize)
        {
            throw new InvalidOperationException("The extracted document content is too large.");
        }

        return structured;
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
                // Do not strip a UTF-8 BOM from canonical source text. Its decoded
                // U+FEFF remains in Content and is reproduced by current-content export.
                detectEncodingFromByteOrderMarks: false,
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

    public Task<ExtractedDocument?> ExtractStructuredAsync(
            Stream content, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var document = PdfDocument.Open(content);
                var assets = new List<ExtractedAsset>();
                var nodes = new JsonArray();
                foreach (var page in document.GetPages())
                {
                    var text = page.Text;
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        nodes.Add(new JsonObject
                        {
                            ["type"] = "paragraph",
                            ["content"] = new JsonArray(new JsonObject
                            {
                                ["type"] = "text",
                                ["text"] = text
                            })
                        });
                    }

                    foreach (var image in page.GetImages())
                    {
                        byte[] bytes;
                        string contentType;
                        if (image.TryGetPng(out var png))
                        {
                            bytes = png;
                            contentType = "image/png";
                        }
                        else
                        {
                            bytes = image.RawBytes.ToArray();
                            contentType = "image/jpeg";
                        }

                        if (bytes.Length == 0) continue;
                        var referenceId = $"pdf-{assets.Count}-{Guid.NewGuid():N}";
                        assets.Add(new ExtractedAsset
                        {
                            ReferenceId = referenceId,
                            FileName = $"{referenceId}.png",
                            ContentType = contentType,
                            Content = bytes,
                            Width = image.WidthInSamples,
                            Height = image.HeightInSamples
                        });
                        nodes.Add(new JsonObject
                        {
                            ["type"] = "image",
                            ["attrs"] = new JsonObject
                            {
                                ["assetId"] = referenceId,
                                ["alt"] = "Extracted PDF image",
                                ["width"] = image.WidthInSamples,
                                ["height"] = image.HeightInSamples
                            }
                        });
                    }
                }

                return Task.FromResult<ExtractedDocument?>(new ExtractedDocument
                {
                    ContentJson = new JsonObject
                    {
                        ["type"] = "doc",
                        ["content"] = nodes
                    }.ToJsonString(),
                    Assets = assets
                });
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

    public async Task<ExtractedDocument?> ExtractStructuredAsync(
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

                XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var document = LoadXml(archive, entry, cancellationToken);
                var numbering = LoadNumbering(archive, word, cancellationToken);
                var relationships = LoadImageRelationships(archive, cancellationToken);
                var assets = new List<ExtractedAsset>();
                var paragraphs = document
                    .Descendants(word + "p")
                    .Select(paragraph => CreateParagraphInfo(
                        paragraph, word, numbering, relationships, archive, assets, cancellationToken))
                    .ToList();
                var contentNodes = BuildContent(paragraphs);

                return new ExtractedDocument
                {
                    ContentJson = new JsonObject
                    {
                        ["type"] = "doc",
                        ["content"] = contentNodes
                    }.ToJsonString(),
                    Assets = assets
                };
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

    private sealed record ParagraphInfo(
        JsonObject Node,
        string? ListType,
        int Level,
        IReadOnlyList<JsonObject> Images);

    private sealed record ImageRelationship(string Target, string ContentType);

    private static XDocument LoadXml(
        ZipArchive archive,
        ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static Dictionary<string, Dictionary<int, string>> LoadNumbering(
        ZipArchive archive,
        XNamespace word,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("word/numbering.xml");
        if (entry is null)
        {
            return [];
        }

        var numbering = LoadXml(archive, entry, cancellationToken);
        var formats = numbering
            .Descendants(word + "abstractNum")
            .ToDictionary(
                abstractNum => (string?)abstractNum.Attribute(word + "abstractNumId") ?? string.Empty,
                abstractNum => abstractNum
                    .Elements(word + "lvl")
                    .ToDictionary(
                        level => (int?)level.Attribute(word + "ilvl") ?? 0,
                        level => (string?)level.Element(word + "numFmt")?.Attribute(word + "val")
                            ?? "bullet"));

        var result = new Dictionary<string, Dictionary<int, string>>();
        foreach (var num in numbering.Descendants(word + "num"))
        {
            var numId = (string?)num.Attribute(word + "numId");
            var abstractId = (string?)num.Element(word + "abstractNumId")?.Attribute(word + "val");
            if (numId is null || abstractId is null || !formats.TryGetValue(abstractId, out var levels))
            {
                continue;
            }

            result[numId] = levels;
        }

        return result;
    }

    private static Dictionary<string, ImageRelationship> LoadImageRelationships(
        ZipArchive archive,
        CancellationToken cancellationToken)
    {
        var entry = archive.GetEntry("word/_rels/document.xml.rels");
        if (entry is null) return [];

        var relationships = LoadXml(archive, entry, cancellationToken);
        XNamespace rel = "http://schemas.openxmlformats.org/package/2006/relationships";
        var result = new Dictionary<string, ImageRelationship>(StringComparer.Ordinal);
        foreach (var relationship in relationships.Root?.Elements(rel + "Relationship") ?? [])
        {
            var id = (string?)relationship.Attribute("Id");
            var target = (string?)relationship.Attribute("Target");
            var type = (string?)relationship.Attribute("Type");
            if (id is null || target is null || type is null || !type.EndsWith("/image", StringComparison.Ordinal))
                continue;

            var normalizedTarget = target.Replace('\\', '/').TrimStart('/');
            if (!normalizedTarget.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
                normalizedTarget = $"word/{normalizedTarget}";
            result[id] = new ImageRelationship(normalizedTarget, GetImageContentType(normalizedTarget));
        }

        return result;
    }

    private static string GetImageContentType(string target) => Path.GetExtension(target).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        _ => "application/octet-stream"
    };

    private static ParagraphInfo CreateParagraphInfo(
        XElement paragraph,
        XNamespace word,
        Dictionary<string, Dictionary<int, string>> numbering,
        Dictionary<string, ImageRelationship> relationships,
        ZipArchive archive,
        List<ExtractedAsset> assets,
        CancellationToken cancellationToken)
    {
        var node = CreateParagraph(paragraph, word);
        var images = new List<JsonObject>();
        foreach (var blip in paragraph.Descendants(XName.Get("blip", "http://schemas.openxmlformats.org/drawingml/2006/main")))
        {
            var relationshipId = (string?)blip.Attribute(XName.Get("embed", "http://schemas.openxmlformats.org/officeDocument/2006/relationships"));
            if (relationshipId is null
                || !relationships.TryGetValue(relationshipId, out var relationship)
                || archive.GetEntry(relationship.Target) is not { } imageEntry
                || imageEntry.Length > MaxEntrySize)
                continue;

            using var stream = imageEntry.Open();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var referenceId = $"docx-{assets.Count}-{Guid.NewGuid():N}";
            var asset = new ExtractedAsset
            {
                ReferenceId = referenceId,
                FileName = Path.GetFileName(relationship.Target),
                ContentType = relationship.ContentType,
                Content = buffer.ToArray()
            };
            assets.Add(asset);
            images.Add(new JsonObject
            {
                ["type"] = "image",
                ["attrs"] = new JsonObject
                {
                    ["assetId"] = referenceId,
                    ["alt"] = asset.FileName
                }
            });
        }
        var properties = paragraph.Element(word + "pPr");
        var numPr = properties?.Element(word + "numPr");
        var numId = (string?)numPr?.Element(word + "numId")?.Attribute(word + "val");
        var level = (int?)numPr?.Element(word + "ilvl")?.Attribute(word + "val") ?? 0;
        if (numId is null
            || !numbering.TryGetValue(numId, out var levels)
            || !levels.TryGetValue(level, out var format))
        {
            return new ParagraphInfo(node, null, 0, images);
        }

        return new ParagraphInfo(
            node,
            format == "bullet" ? "bulletList" : "orderedList",
            level,
            images);
    }

    private static JsonArray BuildContent(IReadOnlyList<ParagraphInfo> paragraphs)
    {
        var result = new JsonArray();
        var index = 0;
        while (index < paragraphs.Count)
        {
            if (paragraphs[index].ListType is null)
            {
                result.Add(paragraphs[index].Node);
                foreach (var image in paragraphs[index].Images)
                    result.Add(image);
                index++;
                continue;
            }

            result.Add(BuildList(paragraphs, ref index, paragraphs[index].Level));
        }

        return result;
    }

    private static JsonObject BuildList(
        IReadOnlyList<ParagraphInfo> paragraphs,
        ref int index,
        int level)
    {
        var listType = paragraphs[index].ListType!;
        var list = new JsonObject
        {
            ["type"] = listType,
            ["content"] = new JsonArray()
        };
        var items = (JsonArray)list["content"]!;

        while (index < paragraphs.Count)
        {
            var paragraph = paragraphs[index];
            if (paragraph.ListType is null || paragraph.Level < level)
            {
                break;
            }
            if (paragraph.Level > level)
            {
                if (items.Count == 0)
                {
                    break;
                }

                ((JsonObject)items[items.Count - 1]!)["content"]!.AsArray()
                    .Add(BuildList(paragraphs, ref index, paragraph.Level));
                continue;
            }
            if (paragraph.ListType != listType)
            {
                break;
            }

            var item = new JsonObject
            {
                ["type"] = "listItem",
                ["content"] = new JsonArray(paragraph.Node)
            };
            items.Add(item);
            index++;
        }

        return list;
    }

    private static JsonObject CreateParagraph(XElement paragraph, XNamespace word)
        {
            var result = new JsonObject
            {
                ["type"] = "paragraph",
                ["content"] = new JsonArray()
            };
            var content = (JsonArray)result["content"]!;

            foreach (var run in paragraph.Elements(word + "r"))
            {
                var runProperties = run.Element(word + "rPr");
                var marks = new JsonArray();
                if (runProperties?.Element(word + "b") is not null)
                {
                    marks.Add(new JsonObject { ["type"] = "bold" });
                }
                if (runProperties?.Element(word + "i") is not null)
                {
                    marks.Add(new JsonObject { ["type"] = "italic" });
                }
                if (runProperties?.Element(word + "u") is not null)
                {
                    marks.Add(new JsonObject { ["type"] = "underline" });
                }
                if (runProperties?.Element(word + "strike") is not null)
                {
                    marks.Add(new JsonObject { ["type"] = "strike" });
                }

                foreach (var text in run.Elements(word + "t"))
                {
                    var textNode = new JsonObject
                    {
                        ["type"] = "text",
                        ["text"] = text.Value
                    };
                    if (marks.Count > 0)
                    {
                        textNode["marks"] = marks.DeepClone();
                    }

                    content.Add(textNode);
                }

                foreach (var breakElement in run.Elements(word + "br"))
                {
                    content.Add(new JsonObject { ["type"] = "hardBreak" });
                }
            }

            if (content.Count == 0)
            {
                result.Remove("content");
            }

            return result;
        }
    }
