using System.Security.Cryptography;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public class GuestTokenService
{
    private readonly ApplicationDbContext _db;

    private const string CookieName = "guest_session";
    private const int TokenSizeBytes = 32;

    public GuestTokenService(
        ApplicationDbContext db)
    {
        _db = db;
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

                // HTTP: Strict (no cross-origin needed).
                // HTTPS: None (cross-origin frontend
                // needs cookie attachment).
                SameSite =
                    http.Request.IsHttps
                        ? SameSiteMode.None
                        : SameSiteMode.Strict,

                // Follows request protocol:
                // HTTP → false, HTTPS → true.
                Secure = http.Request.IsHttps,

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
                SameSite =
                    http.Request.IsHttps
                        ? SameSiteMode.None
                        : SameSiteMode.Strict,
                Secure = http.Request.IsHttps,
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