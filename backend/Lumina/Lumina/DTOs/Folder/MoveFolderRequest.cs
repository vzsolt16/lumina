namespace Lumina.DTOs.Folder;

public class MoveFolderRequest
{
    // Null moves the folder to the root level.
    public Guid? ParentId { get; set; }
}
