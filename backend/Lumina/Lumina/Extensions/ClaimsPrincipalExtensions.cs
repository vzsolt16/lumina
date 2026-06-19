using System.Security.Claims;

namespace Lumina.Extensions;

public static class ClaimsPrincipalExtensions
{
    // The user id is issued as the "sub" claim, which JwtBearer maps to
    // ClaimTypes.NameIdentifier by default.
    public static Guid GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(value, out var userId))
        {
            return userId;
        }

        throw new InvalidOperationException("Authenticated principal has no valid user id claim.");
    }
}
