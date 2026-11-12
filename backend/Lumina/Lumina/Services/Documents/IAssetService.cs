using Lumina.DTOs.Document;

namespace Lumina.Services.Documents;

public interface IAssetService
{
    Task<DocumentAssetResponse?> GetAsync(
        Guid documentId,
        Guid assetId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AssetFile?> OpenAsync(
        Guid documentId,
        Guid assetId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<DocumentAssetResponse?> CreateAsync(
        Guid documentId,
        IFormFile file,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        Guid documentId,
        Guid assetId,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed record AssetFile(Stream Content, string FileName, string ContentType);
