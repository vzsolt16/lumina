using Lumina.DTOs;
using Lumina.Models;

namespace Lumina.Services.Documents;

public interface IDocumentService
{
    Task<Document> UploadAsync(IFormFile file, Guid userId);

    Task<Document?> GetByIdAsync(Guid id, Guid userId);

    Task<List<DocumentSummaryResponse>> GetAllAsync(Guid userId);
}