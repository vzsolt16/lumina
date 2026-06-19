namespace Lumina.Models;

// One row per issued refresh token. The raw token is never stored — only its
// SHA-256 hash — so a DB leak can't be replayed. Tokens rotate on every use:
// refreshing revokes the current row and inserts a new one.
public class RefreshToken
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public ApplicationUser User { get; set; } = null!;

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? RevokedAt { get; set; }

    public bool IsActive => RevokedAt is null && DateTime.UtcNow < ExpiresAt;
}
