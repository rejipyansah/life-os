using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.AspNetCore.Identity;

namespace LifeOS.Api.Services;

public static class GuestSessionEndpoints
{
    public static void MapGuestSessionEndpoints(this WebApplication app)
    {
        app.MapGet("/api/guest/session", async (HttpContext http, GuestTokenService guestTokens) =>
        {
            if (http.User.Identity?.IsAuthenticated == true)
            {
                return ApiErrorResponses.Create(
                    http,
                    StatusCodes.Status403Forbidden,
                    "guest_session_not_allowed",
                    "Pengguna yang sudah masuk tidak dapat menggunakan sesi guest.");
            }

            var resolution = await guestTokens.ResolveWithStatusAsync(http, http.RequestAborted);
            if (resolution.Session is null)
            {
                return ApiErrorResponses.Create(
                    http,
                    resolution.IsExpired ? StatusCodes.Status410Gone : StatusCodes.Status401Unauthorized,
                    resolution.IsExpired ? "GuestSessionExpired" : "NoGuestSession",
                    resolution.IsExpired
                        ? "Sesi guest telah berakhir atau tidak lagi tersedia."
                        : "Tidak ada sesi guest yang dapat dilanjutkan.");
            }

            return Results.Ok(new { isGuest = true });
        })
        .WithName("ResumeGuestSession");

        app.MapPost("/api/guest/session", async (
            HttpContext http,
            GuestTokenService guestTokens,
            ApplicationDbContext db) =>
        {
            if (http.User.Identity?.IsAuthenticated == true)
            {
                return ApiErrorResponses.Create(
                    http,
                    StatusCodes.Status403Forbidden,
                    "guest_session_not_allowed",
                    "Pengguna yang sudah masuk tidak dapat membuat sesi guest.");
            }

            var existingSession = await guestTokens.ResolveAsync(http, http.RequestAborted);
            if (existingSession is not null)
                return Results.Ok(new { isGuest = true });

            // Invalid/expired cookies are cleared during resolution.
            var guestScope = new Scope { Type = ScopeType.Guest };
            var token = GuestTokenService.GenerateToken();
            var guestSession = new GuestSession
            {
                Scope = guestScope,
                TokenHash = GuestTokenService.HashToken(token)
            };

            db.Scopes.Add(guestScope);
            db.GuestSessions.Add(guestSession);
            await db.SaveChangesAsync(http.RequestAborted);
            await ScopeInitializer.EnsureDefaultAccountAsync(db, guestScope.Id, http.RequestAborted);
            guestTokens.SetGuestCookie(http, token);

            return Results.Ok(new { isGuest = true });
        })
        .WithName("CreateGuestSession");
    }
}
