namespace Lumina.DTOs.Folder;

public class FolderResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = "";

    public Guid? ParentId { get; set; }

    public DateTime CreatedAt { get; set; }
}
