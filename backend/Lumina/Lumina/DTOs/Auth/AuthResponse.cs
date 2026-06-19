namespace Lumina.DTOs.Auth;

// Returned by register / login / refresh. The access token lives in the
// response body (held in a JS variable on the client); the refresh token is
// delivered separately as an HttpOnly cookie and never appears here.
public class AuthResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public int ExpiresInSeconds { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = string.Empty;
}
