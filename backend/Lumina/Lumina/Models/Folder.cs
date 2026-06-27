namespace Lumina.Models;

public class Folder
{
    public Guid Id { get; set; }

    // Owner. Folders are scoped to a single user, same as Documents.
    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    // Null = a top-level (root) folder. Otherwise points at the parent folder,
    // forming the nested tree (depth capped at 5 in FolderService).
    public Guid? ParentId { get; set; }

    public Folder? Parent { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
