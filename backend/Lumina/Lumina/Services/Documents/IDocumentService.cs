using Lumina.DTOs;
using Lumina.Models;

namespace Lumina.Services.Documents;

public interface IDocumentService
{
    Task<Document> UploadAsync(IFormFile file);

    Task<Document?> GetByIdAsync(Guid id);

    Task<List<DocumentSummaryResponse>> GetAllAsync();
}