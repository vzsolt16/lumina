namespace Lumina.Models;

public class Document
{
    public Guid Id { get; set; }

    // Owner. Every Document belongs to exactly one user; Quizzes, Flashcards
    // and ChatMessages inherit ownership through their parent Document.
    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    // Null = the document lives at the library root (not inside any folder).
    public Guid? FolderId { get; set; }

    public Folder? Folder { get; set; }

    public string FileName { get; set; } = string.Empty;

    // Nullable for documents created before original-file storage was introduced.
    public string? OriginalFileName { get; set; }

    public string? OriginalStorageKey { get; set; }

    public string? OriginalContentType { get; set; }

    public string Content { get; set; } = string.Empty;
    
    // Original uploaded byte size. This does not change when Content is edited.
    public long FileSize { get; set; }

    public long ContentSize { get; set; }

    public int ContentVersion { get; set; } = 1;

    public string ContentType { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; }

    // Null until the document's title or content is first edited.
    public DateTime? UpdatedAt { get; set; }

    public ICollection<Flashcard> Flashcards { get; set; } = [];

    public ICollection<Quiz> Quizzes { get; set; } = [];

    public ICollection<ChatConversation> Conversations { get; set; } = [];
}