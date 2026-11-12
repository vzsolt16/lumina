using Lumina.Data;
using Lumina.DTOs.Document;
using Lumina.Models;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Documents;

public sealed class AssetService : IAssetService
{
    private const long MaxAssetSize = 10 * 1024 * 1024;
    private readonly LuminaDbContext _db;
    private readonly IFileStorage _storage;

    public AssetService(LuminaDbContext db, IFileStorage storage)
    {
        _db = db;
        _storage = storage;
    }

    public async Task<DocumentAssetResponse?> GetAsync(
        Guid documentId, Guid assetId, Guid userId, CancellationToken cancellationToken = default) =>
        await _db.DocumentAssets
            .Where(a => a.Id == assetId && a.DocumentId == documentId && a.Document.UserId == userId)
            .Select(a => new DocumentAssetResponse
            {
                Id = a.Id,
                DocumentId = a.DocumentId,
                FileName = a.FileName,
                ContentType = a.ContentType,
                FileSize = a.FileSize,
                Width = a.Width,
                Height = a.Height
            })
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AssetFile?> OpenAsync(
        Guid documentId, Guid assetId, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await _db.DocumentAssets
            .Where(a => a.Id == assetId && a.DocumentId == documentId && a.Document.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);
        if (asset is null) return null;

        try
        {
            return new AssetFile(
                await _storage.OpenReadAsync(asset.StorageKey, cancellationToken),
                asset.FileName,
                asset.ContentType);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    public async Task<DocumentAssetResponse?> CreateAsync(
        Guid documentId, IFormFile file, Guid userId, CancellationToken cancellationToken = default)
    {
        var ownsDocument = await _db.Documents.AnyAsync(
            d => d.Id == documentId && d.UserId == userId, cancellationToken);
        if (!ownsDocument) return null;

        if (file.Length <= 0 || file.Length > MaxAssetSize)
            throw new InvalidOperationException("The image must be between 1 byte and 10 MB.");

        var detected = await DetectTypeAsync(file, cancellationToken);
        if (detected is null)
            throw new InvalidOperationException("Unsupported or invalid image file.");

        var asset = new DocumentAsset
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            FileName = Path.GetFileName(file.FileName),
            StorageKey = $"documents/{userId:D}/assets/{Guid.NewGuid():N}{detected.Value.Extension}",
            ContentType = detected.Value.ContentType,
            FileSize = file.Length,
            CreatedAt = DateTime.UtcNow
        };

        await using var input = file.OpenReadStream();
        try
        {
            await _storage.SaveAsync(asset.StorageKey, input, cancellationToken);
            _db.DocumentAssets.Add(asset);
            await _db.SaveChangesAsync(cancellationToken);
            return ToResponse(asset);
        }
        catch
        {
            await _storage.DeleteAsync(asset.StorageKey, CancellationToken.None);
            throw;
        }
    }

    public async Task<bool> DeleteAsync(
        Guid documentId, Guid assetId, Guid userId, CancellationToken cancellationToken = default)
    {
        var asset = await _db.DocumentAssets
            .Where(a => a.Id == assetId && a.DocumentId == documentId && a.Document.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);
        if (asset is null) return false;

        _db.DocumentAssets.Remove(asset);
        await _db.SaveChangesAsync(cancellationToken);
        await _storage.DeleteAsync(asset.StorageKey, CancellationToken.None);
        return true;
    }

    private static DocumentAssetResponse ToResponse(DocumentAsset asset) => new()
    {
        Id = asset.Id,
        DocumentId = asset.DocumentId,
        FileName = asset.FileName,
        ContentType = asset.ContentType,
        FileSize = asset.FileSize,
        Width = asset.Width,
        Height = asset.Height
    };

    private static async Task<(string ContentType, string Extension)?> DetectTypeAsync(
        IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        var header = new byte[12];
        var read = await stream.ReadAsync(header, cancellationToken);
        if (read >= 8 && header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            return ("image/png", ".png");
        if (read >= 3 && header[..3].SequenceEqual(new byte[] { 255, 216, 255 }))
            return ("image/jpeg", ".jpg");
        if (read >= 6 && (header[..6].SequenceEqual("GIF87a"u8.ToArray()) || header[..6].SequenceEqual("GIF89a"u8.ToArray())))
            return ("image/gif", ".gif");
        if (read >= 12 && header[..4].SequenceEqual("RIFF"u8.ToArray()) && header[8..12].SequenceEqual("WEBP"u8.ToArray()))
            return ("image/webp", ".webp");
        return null;
    }
}
