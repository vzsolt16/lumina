using Lumina.Data;
using Lumina.DTOs.Folder;
using Lumina.Models;
using Lumina.Services.Documents;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Lumina.Services.Folders;

public class FolderService : IFolderService
{
    // Root folders are depth 1, so 5 means at most five nested levels.
    private const int MaxDepth = 5;

    private const int MaxNameLength = 100;

    private readonly LuminaDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<FolderService> _logger;

    public FolderService(
        LuminaDbContext db,
        IFileStorage fileStorage,
        ILogger<FolderService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task<List<FolderResponse>> GetAllAsync(Guid userId)
    {
        return await _db.Folders
            .Where(f => f.UserId == userId)
            .OrderBy(f => f.Name)
            .Select(f => new FolderResponse
            {
                Id = f.Id,
                Name = f.Name,
                ParentId = f.ParentId,
                CreatedAt = f.CreatedAt
            })
            .ToListAsync();
    }

    public async Task<FolderResponse?> CreateAsync(Guid userId, string name, Guid? parentId)
    {
        name = NormalizeName(name);

        var folders = await LoadUserFoldersAsync(userId);

        if (parentId is not null)
        {
            if (!folders.TryGetValue(parentId.Value, out var parent))
            {
                return null; // parent missing or not owned
            }

            if (DepthOf(parent, folders) + 1 > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Maximum folder nesting depth ({MaxDepth}) reached.");
            }
        }

        EnsureNameAvailable(folders.Values, parentId, name, excludeId: null);

        var folder = new Folder
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentId = parentId,
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        _db.Folders.Add(folder);
        await SaveGuardingNameAsync();

        return ToResponse(folder);
    }

    public async Task<FolderResponse?> RenameAsync(Guid userId, Guid id, string name)
    {
        name = NormalizeName(name);

        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);

        if (folder is null)
        {
            return null;
        }

        if (!string.Equals(folder.Name, name, StringComparison.Ordinal))
        {
            var siblings = await _db.Folders
                .Where(f => f.UserId == userId && f.ParentId == folder.ParentId)
                .ToListAsync();

            EnsureNameAvailable(siblings, folder.ParentId, name, excludeId: id);

            folder.Name = name;
            await SaveGuardingNameAsync();
        }

        return ToResponse(folder);
    }

    public async Task<FolderResponse?> MoveAsync(Guid userId, Guid id, Guid? newParentId)
    {
        var folders = await LoadUserFoldersAsync(userId);

        if (!folders.TryGetValue(id, out var folder))
        {
            return null;
        }

        if (newParentId == folder.ParentId)
        {
            return ToResponse(folder); // no-op
        }

        if (newParentId is not null)
        {
            if (newParentId == id)
            {
                throw new InvalidOperationException("Cannot move a folder into itself.");
            }

            if (!folders.TryGetValue(newParentId.Value, out var target))
            {
                return null; // target missing or not owned
            }

            if (IsDescendant(newParentId.Value, id, folders))
            {
                throw new InvalidOperationException(
                    "Cannot move a folder into one of its own subfolders.");
            }

            // The deepest descendant of the moved subtree must still fit under the cap.
            if (DepthOf(target, folders) + SubtreeHeight(id, folders) > MaxDepth)
            {
                throw new InvalidOperationException(
                    $"Move would exceed the maximum folder nesting depth ({MaxDepth}).");
            }
        }

        EnsureNameAvailable(folders.Values, newParentId, folder.Name, excludeId: id);

        folder.ParentId = newParentId;
        await SaveGuardingNameAsync();

        return ToResponse(folder);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id)
    {
        var folder = await _db.Folders
            .FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);

        if (folder is null)
        {
            return false;
        }

        var folderIds = await _db.Folders
            .Where(f => f.UserId == userId)
            .Select(f => new { f.Id, f.ParentId })
            .ToListAsync();
        var deletedFolderIds = new HashSet<Guid> { id };
        var added = true;
        while (added)
        {
            added = false;
            foreach (var child in folderIds.Where(f => f.ParentId is not null))
            {
                if (child.ParentId is Guid parentId
                    && deletedFolderIds.Contains(parentId)
                    && deletedFolderIds.Add(child.Id))
                {
                    added = true;
                }
            }
        }

        var storageKeys = await _db.Documents
            .Where(d => d.UserId == userId && d.FolderId != null && deletedFolderIds.Contains(d.FolderId.Value))
            .Select(d => d.OriginalStorageKey)
            .ToListAsync();

        // Subfolders and contained documents (and their flashcards/quizzes/chat/jobs)
        // cascade-delete via the FK configuration in LuminaDbContext.
        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync();

        foreach (var storageKey in storageKeys.Where(key => !string.IsNullOrWhiteSpace(key)))
        {
            try
            {
                await _fileStorage.DeleteAsync(storageKey!);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Folder {FolderId} was deleted but original file cleanup failed for {StorageKey}.",
                    id,
                    storageKey);
            }
        }

        return true;
    }

    /* ── helpers ──────────────────────────────────────────────────────── */

    // The EnsureNameAvailable pre-check is the friendly fast path, but it isn't
    // atomic. The unique index (UserId, COALESCE(ParentId,''), Name COLLATE NOCASE)
    // is the real backstop against a concurrent duplicate; translate its violation
    // into the same 400 the caller would otherwise have produced.
    private async Task SaveGuardingNameAsync()
    {
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw new InvalidOperationException("A folder with that name already exists here.");
        }
    }

    private async Task<Dictionary<Guid, Folder>> LoadUserFoldersAsync(Guid userId)
    {
        var folders = await _db.Folders
            .Where(f => f.UserId == userId)
            .ToListAsync();

        return folders.ToDictionary(f => f.Id);
    }

    internal static string NormalizeName(string name)
    {
        name = (name ?? "").Trim();

        if (name.Length == 0)
        {
            throw new InvalidOperationException("Folder name is required.");
        }

        if (name.Length > MaxNameLength)
        {
            throw new InvalidOperationException(
                $"Folder name must be {MaxNameLength} characters or fewer.");
        }

        return name;
    }

    // No two folders may share a (case-insensitive) name under the same parent.
    internal static void EnsureNameAvailable(
        IEnumerable<Folder> folders, Guid? parentId, string name, Guid? excludeId)
    {
        var clash = folders.Any(f =>
            f.ParentId == parentId
            && f.Id != excludeId
            && string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

        if (clash)
        {
            throw new InvalidOperationException(
                "A folder with that name already exists here.");
        }
    }

    internal static int DepthOf(Folder folder, Dictionary<Guid, Folder> folders)
    {
        var depth = 1;
        var current = folder;
        while (current.ParentId is not null && folders.TryGetValue(current.ParentId.Value, out var parent))
        {
            depth++;
            current = parent;
        }

        return depth;
    }

    // Height of the subtree rooted at folderId, counting the folder itself as 1.
    internal static int SubtreeHeight(Guid folderId, Dictionary<Guid, Folder> folders)
    {
        var children = folders.Values.Where(f => f.ParentId == folderId).ToList();

        if (children.Count == 0)
        {
            return 1;
        }

        return 1 + children.Max(c => SubtreeHeight(c.Id, folders));
    }

    // Is candidateId inside the subtree rooted at ancestorId?
    internal static bool IsDescendant(Guid candidateId, Guid ancestorId, Dictionary<Guid, Folder> folders)
    {
        var current = folders.GetValueOrDefault(candidateId);
        while (current?.ParentId is not null)
        {
            if (current.ParentId == ancestorId)
            {
                return true;
            }

            current = folders.GetValueOrDefault(current.ParentId.Value);
        }

        return false;
    }

    private static FolderResponse ToResponse(Folder folder) => new()
    {
        Id = folder.Id,
        Name = folder.Name,
        ParentId = folder.ParentId,
        CreatedAt = folder.CreatedAt
    };
}
