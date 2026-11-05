namespace Lumina.Services.Documents;

public interface IDocumentContentExtractor
{
    bool CanExtract(string extension);

    Task<string> ExtractAsync(Stream content, CancellationToken cancellationToken = default);
}
