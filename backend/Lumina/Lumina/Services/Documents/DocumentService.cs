using Lumina.Data;
using Lumina.Models;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Documents;

public class DocumentService : IDocumentService
{
    private readonly LuminaDbContext _db;

    public DocumentService(LuminaDbContext db)
    {
        _db = db;
    }

    public async Task<Document> UploadAsync(IFormFile file)
    {
        using var reader = new StreamReader(file.OpenReadStream());

        var content = await reader.ReadToEndAsync();

        var document = new Document
        {
            Id = Guid.NewGuid(),
            FileName = file.FileName,
            Content = content,
            ContentType = file.ContentType,
            FileSize = file.Length,
            UploadedAt = DateTime.UtcNow
        };

        _db.Documents.Add(document);

        await _db.SaveChangesAsync();

        return document;
    }

    public async Task<Document?> GetByIdAsync(Guid id)
    {
        return await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<List<Document>> GetAllAsync()
    {
        return await _db.Documents
            .OrderByDescending(d => d.UploadedAt)
            .ToListAsync();
    }
}