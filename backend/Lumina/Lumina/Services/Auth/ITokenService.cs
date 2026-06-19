using Lumina.Models;

namespace Lumina.Services.Auth;

public interface ITokenService
{
    // Signed JWT access token for the given user.
    string CreateAccessToken(ApplicationUser user);

    int AccessTokenLifetimeSeconds { get; }

    // Creates a new refresh token: returns the raw value (to put in the cookie)
    // and the entity to persist (which stores only the hash).
    (string RawToken, RefreshToken Entity) CreateRefreshToken(Guid userId);

    // SHA-256 hash used to look up / compare a raw refresh token.
    string HashRefreshToken(string rawToken);
}
