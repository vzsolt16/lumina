using Lumina.Data;
using Lumina.DTOs;
using Lumina.DTOs.Document;
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

    public async Task<Document> UploadAsync(IFormFile file, Guid userId)
    {
        using var reader = new StreamReader(file.OpenReadStream());

        var content = await reader.ReadToEndAsync();

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
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

    public async Task<DocumentDetailResponse?> GetByIdAsync(Guid id, Guid userId)
    {
        return await _db.Documents
            .Where(d => d.Id == id && d.UserId == userId)
            .Select(d => new DocumentDetailResponse
            {
                Id = d.Id,
                FileName = d.FileName,
                FileSize = d.FileSize,
                UploadedAt = d.UploadedAt,
                Content = d.Content
            })
            .FirstOrDefaultAsync();
    }

    public async Task<bool> DeleteAsync(Guid id, Guid userId)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document is null)
        {
            return false;
        }

        // Flashcards, quizzes, chat messages and jobs cascade-delete with the document.
        _db.Documents.Remove(document);

        await _db.SaveChangesAsync();

        return true;
    }

    public async Task<List<DocumentSummaryResponse>> GetAllAsync(Guid userId)
    {
        return await _db.Documents
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.UploadedAt)
            .Select(d => new DocumentSummaryResponse
            {
                Id = d.Id,
                FileName = d.FileName,
                UploadedAt = d.UploadedAt
            })
            .ToListAsync();
    }
}