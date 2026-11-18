using System.Text;
using System.Text.Json;
using Lumina.Data;
using Lumina.DTOs.Document;
using Lumina.Models;
using Microsoft.EntityFrameworkCore;
using Lumina.Services.Rag;

namespace Lumina.Services.Documents;

public class DocumentService : IDocumentService
{
    private readonly LuminaDbContext _db;
    private readonly DocumentContentExtractor _contentExtractor;
    private readonly IFileStorage _fileStorage;
    private readonly IDocumentContentConverter _contentConverter;
    private readonly ILogger<DocumentService> _logger;
    private readonly IDocumentIndexScheduler _indexScheduler;
    private readonly IVectorStore _vectorStore;

    public DocumentService(
        LuminaDbContext db,
        DocumentContentExtractor contentExtractor,
        IFileStorage fileStorage,
        IDocumentContentConverter contentConverter,
        ILogger<DocumentService> logger,
        IDocumentIndexScheduler indexScheduler,
        IVectorStore vectorStore)
    {
        _db = db;
        _contentExtractor = contentExtractor;
        _fileStorage = fileStorage;
        _contentConverter = contentConverter;
        _logger = logger;
        _indexScheduler = indexScheduler;
        _vectorStore = vectorStore;
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

        buffer.Position = 0;
        var extractedDocument = await _contentExtractor.ExtractStructuredAsync(
            extension,
            buffer,
            cancellationToken);
        buffer.Position = 0;
        var content = extractedDocument?.ContentJson is null
            ? await _contentExtractor.ExtractAsync(extension, buffer, cancellationToken)
            : _contentConverter.ExtractPlainText(extractedDocument.ContentJson);
        var contentJson = extractedDocument?.ContentJson is null
            ? _contentConverter.CreateFromPlainText(content)
            : extractedDocument.ContentJson;
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
            ContentJson = _contentConverter.Normalize(contentJson),
            FileSize = file.Length,
            ContentSize = Encoding.UTF8.GetByteCount(content),
            ContentVersion = 1,
            ContentType = GetServerContentType(extension),
            UploadedAt = DateTime.UtcNow
        };

        var savedAssetKeys = new List<string>();
        buffer.Position = 0;
        try
        {
            await _fileStorage.SaveAsync(document.OriginalStorageKey, buffer, cancellationToken);
            _db.Documents.Add(document);
            if (extractedDocument is not null)
            {
                foreach (var extractedAsset in extractedDocument.Assets)
                {
                    var asset = new DocumentAsset
                    {
                        Id = Guid.NewGuid(),
                        DocumentId = document.Id,
                        FileName = Path.GetFileName(extractedAsset.FileName),
                        StorageKey = $"documents/{userId:D}/assets/{Guid.NewGuid():N}{Path.GetExtension(extractedAsset.FileName)}",
                        ContentType = extractedAsset.ContentType,
                        FileSize = extractedAsset.Content.LongLength,
                        Width = extractedAsset.Width,
                        Height = extractedAsset.Height,
                        CreatedAt = DateTime.UtcNow
                    };
                    await using var assetStream = new MemoryStream(extractedAsset.Content, writable: false);
                    await _fileStorage.SaveAsync(asset.StorageKey, assetStream, cancellationToken);
                    savedAssetKeys.Add(asset.StorageKey);
                    document.ContentJson = document.ContentJson.Replace(
                        extractedAsset.ReferenceId,
                        asset.Id.ToString(),
                        StringComparison.Ordinal);
                    _db.DocumentAssets.Add(asset);
                }
            }
            await _db.SaveChangesAsync(cancellationToken);
            _indexScheduler.Schedule(document.Id);
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

            foreach (var key in savedAssetKeys)
            {
                try
                {
                    await _fileStorage.DeleteAsync(key, CancellationToken.None);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogError(
                        cleanupException,
                        "Failed to clean up extracted asset {StorageKey} after upload failure.",
                        key);
                }
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
                d.Content,
                d.ContentJson
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
                Content = document.Content,
                ContentJson = document.ContentJson
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

        try { await _vectorStore.DeleteDocumentAsync(id, cancellationToken); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Vector cleanup failed; preserving document {DocumentId} so deletion can be retried", id);
            throw new InvalidOperationException("The document could not be deleted because its search index could not be cleaned up. Please retry.", ex);
        }

        var assetKeys = await _db.DocumentAssets
            .Where(a => a.DocumentId == id)
            .Select(a => a.StorageKey)
            .ToListAsync(cancellationToken);

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

        foreach (var key in assetKeys)
        {
            try
            {
                await _fileStorage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clean up document asset {StorageKey}.", key);
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
        var contentChanged = false;
        if (request.FileName is not null)
        {
            var name = DocumentValidation.PreserveExtension(document.FileName, request.FileName);

            if (!string.Equals(document.FileName, name, StringComparison.Ordinal))
            {
                document.FileName = name;
                changed = true;
            }
        }

        if (request.Content is not null)
        {
            var didChange = DocumentContentMutation.Apply(document, request.Content, _contentConverter);
            changed |= didChange;
            contentChanged |= didChange;
        }

        if (request.ContentJson is not null)
        {
            if (request.ExpectedContentVersion is not null
                && request.ExpectedContentVersion.Value != document.ContentVersion)
            {
                throw new InvalidOperationException(
                    "The document changed before this update was applied. Reload it and try again.");
            }

            string normalized;
            try
            {
                normalized = _contentConverter.Normalize(request.ContentJson);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Structured document content is invalid.", ex);
            }

            var content = _contentConverter.ExtractPlainText(normalized);
            if (!string.Equals(document.ContentJson, normalized, StringComparison.Ordinal))
            {
                document.ContentJson = normalized;
                document.Content = content;
                document.ContentSize = Encoding.UTF8.GetByteCount(content);
                document.ContentVersion++;
                changed = true;
                contentChanged = true;
            }
        }

        if (changed)
        {
            document.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            if (contentChanged) _indexScheduler.Schedule(document.Id);
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
        Content = document.Content,
        ContentJson = document.ContentJson
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
