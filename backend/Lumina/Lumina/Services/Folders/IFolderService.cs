using Lumina.DTOs.Folder;

namespace Lumina.Services.Folders;

public interface IFolderService
{
    Task<List<FolderResponse>> GetAllAsync(Guid userId);

    /// <summary>Create a folder. Returns null if the parent is missing/not owned.
    /// Throws <see cref="InvalidOperationException"/> on a validation failure
    /// (blank/duplicate name, depth limit).</summary>
    Task<FolderResponse?> CreateAsync(Guid userId, string name, Guid? parentId);

    /// <summary>Rename a folder. Returns null if the folder is missing/not owned.
    /// Throws <see cref="InvalidOperationException"/> on blank/duplicate name.</summary>
    Task<FolderResponse?> RenameAsync(Guid userId, Guid id, string name);

    /// <summary>Move a folder under a new parent (null = root). Returns null if the
    /// folder or target parent is missing/not owned. Throws
    /// <see cref="InvalidOperationException"/> on a cycle, depth limit or duplicate
    /// name at the destination.</summary>
    Task<FolderResponse?> MoveAsync(Guid userId, Guid id, Guid? newParentId);

    Task<bool> DeleteAsync(Guid userId, Guid id);
}
