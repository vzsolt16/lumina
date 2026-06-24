using Lumina.DTOs;
using Lumina.DTOs.Document;
using Lumina.Models;

namespace Lumina.Services.Documents;

public interface IDocumentService
{
    Task<Document> UploadAsync(IFormFile file, Guid userId);

    Task<DocumentDetailResponse?> GetByIdAsync(Guid id, Guid userId);

    Task<List<DocumentSummaryResponse>> GetAllAsync(Guid userId);

    Task<bool> DeleteAsync(Guid id, Guid userId);
}