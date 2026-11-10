using Lumina.Models;

namespace Lumina.Services.Documents;

public interface IDocumentContentConverter
{
    string CreateFromPlainText(string content);

    string Normalize(string contentJson);

    string ExtractPlainText(string contentJson);

    string ExtractPlainText(Document document);
}
