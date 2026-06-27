using Microsoft.AspNetCore.Identity;

namespace Lumina.Models;

// Guid-keyed Identity user, so ownership FKs line up with the rest of the
// domain (Document, Quiz, … all use Guid keys).
public class ApplicationUser : IdentityUser<Guid>
{
    public ICollection<Document> Documents { get; set; } = [];

    public ICollection<Folder> Folders { get; set; } = [];

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
