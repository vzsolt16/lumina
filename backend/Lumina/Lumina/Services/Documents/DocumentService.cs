using System.Text;
using Lumina.Data;
using Lumina.DTOs.Document;
using Lumina.Models;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Documents;

public class DocumentService : IDocumentService
{
    private readonly LuminaDbContext _db;
    private readonly DocumentContentExtractor _contentExtractor;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<DocumentService> _logger;

    public DocumentService(
        LuminaDbContext db,
        DocumentContentExtractor contentExtractor,
        IFileStorage fileStorage,
        ILogger<DocumentService> logger)
    {
        _db = db;
        _contentExtractor = contentExtractor;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<Document> UploadAsync(
        IFormFile file,
        Guid userId,
        Guid? folderId,
        CancellationToken cancellationToken = default)
    {
        DocumentValidation.ValidateUpload(file);
        if (folderId is not null && !await OwnsFolderAsync(folderId.Value, userId, cancellationToken))
        {
            throw new InvalidOperationException("Folder not found.");
        }

        var fileName = DocumentValidation.GetSafeFileName(file.FileName);
        var extension = DocumentValidation.GetExtension(fileName);
        await using var uploadStream = file.OpenReadStream();
        await using var buffer = new MemoryStream(capacity: checked((int)file.Length));
        await uploadStream.CopyToAsync(buffer, cancellationToken);
        buffer.Position = 0;

        var content = await _contentExtractor.ExtractAsync(extension, buffer, cancellationToken);
        var document = new Document
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FolderId = folderId,
            FileName = fileName,
            OriginalFileName = fileName,
            OriginalStorageKey = $"documents/{userId:D}/{Guid.NewGuid():N}{extension}",
            OriginalContentType = GetServerContentType(extension),
            Content = content,
            FileSize = file.Length,
            ContentSize = Encoding.UTF8.GetByteCount(content),
            ContentVersion = 1,
            ContentType = GetServerContentType(extension),
            UploadedAt = DateTime.UtcNow
        };

        buffer.Position = 0;
        try
        {
            await _fileStorage.SaveAsync(document.OriginalStorageKey, buffer, cancellationToken);
            _db.Documents.Add(document);
            await _db.SaveChangesAsync(cancellationToken);
            return document;
        }
        catch
        {
            try
            {
                await _fileStorage.DeleteAsync(document.OriginalStorageKey, CancellationToken.None);
            }
            catch (Exception cleanupException)
            {
                _logger.LogError(
                    cleanupException,
                    "Failed to clean up original file {StorageKey} after upload failure.",
                    document.OriginalStorageKey);
            }

            throw;
        }
    }

    public async Task<DocumentDetailResponse?> GetByIdAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents
            .Where(d => d.Id == id && d.UserId == userId)
            .Select(d => new
            {
                d.Id,
                d.FileName,
                d.FileSize,
                d.ContentSize,
                d.ContentVersion,
                d.UploadedAt,
                d.UpdatedAt,
                d.Content
            })
            .FirstOrDefaultAsync(cancellationToken);

        return document is null
            ? null
            : new DocumentDetailResponse
            {
                Id = document.Id,
                FileName = document.FileName,
                FileSize = document.FileSize,
                ContentSize = document.ContentSize,
                ContentVersion = document.ContentVersion,
                UploadedAt = document.UploadedAt,
                UpdatedAt = document.UpdatedAt,
                Content = document.Content
            };
    }

    public async Task<DocumentOriginalFile?> OpenOriginalAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents
            .Where(d => d.Id == id && d.UserId == userId)
            .Select(d => new
            {
                d.OriginalStorageKey,
                d.OriginalFileName,
                d.OriginalContentType,
                d.FileName,
                d.ContentType
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (document is null || string.IsNullOrWhiteSpace(document.OriginalStorageKey))
        {
            return null;
        }

        try
        {
            var stream = await _fileStorage.OpenReadAsync(document.OriginalStorageKey, cancellationToken);
            return new DocumentOriginalFile(
                stream,
                document.OriginalFileName ?? document.FileName,
                document.OriginalContentType ?? document.ContentType);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);

        if (document is null)
        {
            return false;
        }

        _db.Documents.Remove(document);
        await _db.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(document.OriginalStorageKey))
        {
            try
            {
                await _fileStorage.DeleteAsync(document.OriginalStorageKey, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Document {DocumentId} was deleted but original file cleanup failed for {StorageKey}.",
                    id,
                    document.OriginalStorageKey);
                throw new InvalidOperationException(
                    "The document was deleted, but its original file could not be removed.", ex);
            }
        }

        return true;
    }

    public async Task<DocumentDetailResponse?> UpdateAsync(
        Guid id,
        UpdateDocumentRequest request,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var changed = false;
        if (request.FileName is not null)
        {
            var name = DocumentValidation.GetSafeFileName(request.FileName);
            if (!DocumentValidation.HasSameExtension(document.FileName, name))
            {
                throw new InvalidOperationException("The file extension cannot be changed.");
            }

            if (!string.Equals(document.FileName, name, StringComparison.Ordinal))
            {
                document.FileName = name;
                changed = true;
            }
        }

        if (request.Content is not null)
        {
            changed |= DocumentContentMutation.Apply(document, request.Content);
        }

        if (changed)
        {
            document.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return ToDetailResponse(document);
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

    public async Task<bool> MoveAsync(
        Guid id,
        Guid? folderId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.Documents
            .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId, cancellationToken);

        if (document is null)
        {
            return false;
        }

        if (folderId is not null && !await OwnsFolderAsync(folderId.Value, userId, cancellationToken))
        {
            throw new InvalidOperationException("Folder not found.");
        }

        document.FolderId = folderId;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<bool> OwnsFolderAsync(
        Guid folderId,
        Guid userId,
        CancellationToken cancellationToken) =>
        _db.Folders.AnyAsync(
            f => f.Id == folderId && f.UserId == userId,
            cancellationToken);

    private static DocumentDetailResponse ToDetailResponse(Document document) => new()
    {
        Id = document.Id,
        FileName = document.FileName,
        FileSize = document.FileSize,
        ContentSize = document.ContentSize,
        ContentVersion = document.ContentVersion,
        UploadedAt = document.UploadedAt,
        UpdatedAt = document.UpdatedAt,
        Content = document.Content
    };

    private static string GetServerContentType(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".json" => "application/json",
        ".xml" => "application/xml",
        ".html" => "text/html",
        ".css" => "text/css",
        _ => "text/plain"
    };
}
