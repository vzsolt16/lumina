using Lumina.DTOs.Document;

namespace Lumina.Services.Documents;

public interface IDocumentEditorService
{
    Task<DocumentDetailResponse> ApplyOperationAsync(
        Guid documentId,
        Guid userId,
        DocumentOperation operation,
        CancellationToken cancellationToken = default);
}
