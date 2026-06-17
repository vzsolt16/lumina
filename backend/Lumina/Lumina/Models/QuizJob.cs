using Lumina.Models;

namespace Lumina.Models;

public class QuizJob
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;

    public string Status { get; set; } = "Processing"; // Processing, Completed, Failed
    public int Progress { get; set; }  // 0 - 100

    public string? ResultJson { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
