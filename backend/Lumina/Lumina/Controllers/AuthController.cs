using Lumina.Data;
using Lumina.DTOs.Auth;
using Lumina.Models;
using Lumina.Services.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Lumina.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private const string RefreshCookieName = "refreshToken";
    private const string RefreshCookiePath = "/api/auth";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITokenService _tokenService;
    private readonly LuminaDbContext _db;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        ITokenService tokenService,
        LuminaDbContext db,
        IOptions<JwtOptions> jwtOptions)
    {
        _userManager = userManager;
        _tokenService = tokenService;
        _db = db;
        _jwtOptions = jwtOptions.Value;
    }

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var existing = await _userManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return Conflict("An account with this email already exists.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            UserName = request.Email,
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            return BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        return await IssueTokensAsync(user);
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || !await _userManager.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized("Invalid email or password.");
        }

        return await IssueTokensAsync(user);
    }

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResponse>> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshCookieName, out var rawToken) || string.IsNullOrEmpty(rawToken))
        {
            return Unauthorized("No refresh token.");
        }

        var hash = _tokenService.HashRefreshToken(rawToken);
        var stored = await _db.RefreshTokens
            .Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash);

        if (stored is null)
        {
            return Unauthorized("Invalid or expired refresh token.");
        }

        // Reuse detection: a token that was already revoked is being replayed.
        // Treat it as theft and revoke every active token for the user so an
        // attacker's rotated chain dies alongside the victim's.
        if (stored.RevokedAt is not null)
        {
            await _db.RefreshTokens
                .Where(rt => rt.UserId == stored.UserId && rt.RevokedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, DateTime.UtcNow));
            return Unauthorized("Invalid or expired refresh token.");
        }

        if (DateTime.UtcNow >= stored.ExpiresAt)
        {
            return Unauthorized("Invalid or expired refresh token.");
        }

        // Atomic, conditional rotation: only the request that flips RevokedAt
        // from null wins. Two concurrent refreshes with the same token can't
        // both mint a replacement — the loser gets 0 rows and is rejected.
        var rotated = await _db.RefreshTokens
            .Where(rt => rt.Id == stored.Id && rt.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(rt => rt.RevokedAt, DateTime.UtcNow));

        if (rotated == 0)
        {
            return Unauthorized("Invalid or expired refresh token.");
        }

        return await IssueTokensAsync(stored.User);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        if (Request.Cookies.TryGetValue(RefreshCookieName, out var rawToken) && !string.IsNullOrEmpty(rawToken))
        {
            var hash = _tokenService.HashRefreshToken(rawToken);
            var stored = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }

        Response.Cookies.Delete(RefreshCookieName, new CookieOptions { Path = RefreshCookiePath });
        return NoContent();
    }

    private async Task<ActionResult<AuthResponse>> IssueTokensAsync(ApplicationUser user)
    {
        var (rawRefreshToken, refreshEntity) = _tokenService.CreateRefreshToken(user.Id);
        _db.RefreshTokens.Add(refreshEntity);
        await _db.SaveChangesAsync();

        Response.Cookies.Append(RefreshCookieName, rawRefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = RefreshCookiePath,
            Expires = refreshEntity.ExpiresAt,
        });

        return Ok(new AuthResponse
        {
            AccessToken = _tokenService.CreateAccessToken(user),
            ExpiresInSeconds = _tokenService.AccessTokenLifetimeSeconds,
            UserId = user.Id,
            Email = user.Email ?? string.Empty,
        });
    }
}
