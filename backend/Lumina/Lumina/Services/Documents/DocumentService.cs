using System.Text;
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

    public async Task<Document> UploadAsync(IFormFile file, Guid userId, Guid? folderId)
    {
        if (folderId is not null && !await OwnsFolderAsync(folderId.Value, userId))
        {
            throw new InvalidOperationException("Folder not found.");
        }

        using var reader = new StreamReader(file.OpenReadStream());

        var content = await reader.ReadToEndAsync();

        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
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
                UpdatedAt = d.UpdatedAt,
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

    public async Task<DocumentDetailResponse?> UpdateAsync(Guid id, UpdateDocumentRequest request, Guid userId)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document is null)
        {
            return null;
        }

        if (request.FileName is not null)
        {
            var name = request.FileName.Trim();
            if (name.Length == 0)
            {
                throw new InvalidOperationException("File name cannot be empty.");
            }
            if (name.Length > 255)
            {
                throw new InvalidOperationException("File name is too long (max 255 characters).");
            }
            document.FileName = name;
        }

        if (request.Content is not null)
        {
            const long MaxContentSize = 2 * 1024 * 1024; // mirror the 2 MB upload cap
            var size = Encoding.UTF8.GetByteCount(request.Content);
            if (size > MaxContentSize)
            {
                throw new InvalidOperationException("Content too large (max 2 MB).");
            }
            document.Content = request.Content;
            document.FileSize = size;
        }

        document.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return new DocumentDetailResponse
        {
            Id = document.Id,
            FileName = document.FileName,
            FileSize = document.FileSize,
            UploadedAt = document.UploadedAt,
            UpdatedAt = document.UpdatedAt,
            Content = document.Content
        };
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
                FolderId = d.FolderId,
                UploadedAt = d.UploadedAt,
                UpdatedAt = d.UpdatedAt
            })
            .ToListAsync();
    }

    public async Task<bool> MoveAsync(Guid id, Guid? folderId, Guid userId)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);

        if (document is null)
        {
            return false;
        }

        if (folderId is not null && !await OwnsFolderAsync(folderId.Value, userId))
        {
            throw new InvalidOperationException("Folder not found.");
        }

        document.FolderId = folderId;
        await _db.SaveChangesAsync();

        return true;
    }

    private Task<bool> OwnsFolderAsync(Guid folderId, Guid userId) =>
        _db.Folders.AnyAsync(f => f.Id == folderId && f.UserId == userId);
}