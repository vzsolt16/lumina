namespace Lumina.Models;

public class Document
{
    public Guid Id { get; set; }

    // Owner. Every Document belongs to exactly one user; Quizzes, Flashcards
    // and ChatMessages inherit ownership through their parent Document.
    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public string FileName { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    
    public long FileSize { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public DateTime UploadedAt { get; set; }

    public ICollection<Flashcard> Flashcards { get; set; } = [];

    public ICollection<Quiz> Quizzes { get; set; } = [];

    public ICollection<ChatMessage> ChatMessages { get; set; } = [];
}