using Lumina.DTOs.Document;

namespace Lumina.Services.Documents;

public interface IDocumentEditorService
{
    Task ValidateOperationAsync(
        Guid documentId,
        Guid userId,
        DocumentOperation operation,
        CancellationToken cancellationToken = default);

    Task<DocumentDetailResponse> ApplyOperationAsync(
        Guid documentId,
        Guid userId,
        DocumentOperation operation,
        CancellationToken cancellationToken = default);

    Task ValidateOperationsAsync(Guid documentId, Guid userId, IReadOnlyList<DocumentOperation> operations,
        CancellationToken cancellationToken = default);

    Task<DocumentDetailResponse> ApplyOperationsAsync(Guid documentId, Guid userId,
        IReadOnlyList<DocumentOperation> operations, CancellationToken cancellationToken = default);

    Task<DocumentDetailResponse> ApplyTextOperationsAsync(Guid documentId, Guid userId, int expectedVersion,
        IReadOnlyList<TextDocumentOperation> operations, CancellationToken cancellationToken = default);
}
