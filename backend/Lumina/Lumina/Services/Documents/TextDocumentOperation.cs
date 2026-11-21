namespace Lumina.Services.Documents;

public sealed record TextDocumentOperation(int Start, string ExpectedText, string Replacement);
