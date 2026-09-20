using System.Security.Cryptography;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public class GuestTokenService
{
    private readonly ApplicationDbContext _db;
    private readonly IWebHostEnvironment _environment;

    private const string CookieName = "guest_session";
    private const int TokenSizeBytes = 32;

    public GuestTokenService(
        ApplicationDbContext db,
        IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    public static string GenerateToken()
    {
        var bytes =
            RandomNumberGenerator.GetBytes(
                TokenSizeBytes
            );

        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    public static string HashToken(string token)
    {
        var bytes =
            SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(
                    token
                )
            );

        return Convert.ToHexString(bytes)
            .ToLowerInvariant();
    }

    public void SetGuestCookie(
        HttpContext http,
        string token)
    {
        http.Response.Cookies.Append(
            CookieName,
            token,
            new CookieOptions
            {
                HttpOnly = true,

                // Needed for frontend/backend on different origins.
                SameSite = SameSiteMode.None,

                // Local HTTP works in Development.
                // Production requires HTTPS.
                Secure = _environment.IsProduction(),

                Expires =
                    DateTimeOffset.UtcNow.AddYears(1),

                Path = "/"
            }
        );
    }

    public static string? ReadToken(
        HttpContext http)
    {
        http.Request.Cookies.TryGetValue(
            CookieName,
            out var token
        );

        return token;
    }

    public void ClearGuestCookie(
        HttpContext http)
    {
        http.Response.Cookies.Delete(
            CookieName,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.None,
                Secure = _environment.IsProduction(),
                Path = "/"
            }
        );
    }

    public async Task<GuestSession?> ResolveAsync(
        HttpContext http)
    {
        var token =
            ReadToken(http);

        if (string.IsNullOrEmpty(token))
            return null;

        var tokenHash =
            HashToken(token);

        var session =
            await _db.GuestSessions
                .Include(s => s.Scope)
                .FirstOrDefaultAsync(
                    s =>
                        s.TokenHash == tokenHash &&
                        s.Scope.Type == ScopeType.Guest
                );

        return session;
    }

    public async Task UpdateActivityAsync(
        GuestSession session)
    {
        session.LastActivityAt =
            DateTime.UtcNow;

        await _db.SaveChangesAsync();
    }
}