using Lumina.Models;

namespace Lumina.Services.Documents;

public interface IDocumentContextBuilder
{
    Task<string> BuildAsync(
        Document document,
        DocumentContextType contextType,
        CancellationToken cancellationToken = default);
}
