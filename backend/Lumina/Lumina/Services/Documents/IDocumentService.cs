using Lumina.DTOs;
using Lumina.DTOs.Document;
using Lumina.Models;

namespace Lumina.Services.Documents;

public interface IDocumentService
{
    /// <summary>Upload a document, optionally into a folder. Throws
    /// <see cref="InvalidOperationException"/> if <paramref name="folderId"/> is set
    /// but missing/not owned.</summary>
    Task<Document> UploadAsync(IFormFile file, Guid userId, Guid? folderId);

    Task<DocumentDetailResponse?> GetByIdAsync(Guid id, Guid userId);

    Task<List<DocumentSummaryResponse>> GetAllAsync(Guid userId);

    Task<bool> DeleteAsync(Guid id, Guid userId);

    /// <summary>Update a document's file name and/or content. Only non-null fields
    /// on <paramref name="request"/> are applied. Returns null if the document is
    /// missing/not owned. Throws <see cref="InvalidOperationException"/> on invalid
    /// input (e.g. a blank name or oversized content).</summary>
    Task<DocumentDetailResponse?> UpdateAsync(Guid id, UpdateDocumentRequest request, Guid userId);

    /// <summary>Move a document to a folder (null = root). Returns false if the
    /// document is missing/not owned. Throws <see cref="InvalidOperationException"/>
    /// if <paramref name="folderId"/> is set but missing/not owned.</summary>
    Task<bool> MoveAsync(Guid id, Guid? folderId, Guid userId);
}