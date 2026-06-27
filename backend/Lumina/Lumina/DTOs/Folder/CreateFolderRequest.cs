namespace Lumina.DTOs.Folder;

public class CreateFolderRequest
{
    public string Name { get; set; } = "";

    // Null creates a root-level folder.
    public Guid? ParentId { get; set; }
}
