using System.Security.Claims;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

/// <summary>
/// API Finance berdasarkan domain final:
///   ACCOUNT        — list, create, update/archive
///   TRANSACTION    — list, detail, create, reverse/correct
///   SET-ASIDE      — list, create, update, add, withdraw, spend, close, history
///   UPCOMING EVENT — list, create, update, postpone, skip, cancel, delete, realize
///   FINANCE STATE  — satu read model untuk halaman Finance
///
/// Scope selalu diselesaikan di server. Client tidak pernah menentukan ScopeId.
/// </summary>
public static partial class FinanceEndpoints
{
    private static readonly object FinanceScopeItemKey = new();

    public static void MapFinanceApi(this WebApplication app)
    {
        var finance = app.MapGroup("/api/finance");
        finance.AddEndpointFilter(async (invocation, next) =>
        {
            var http = invocation.HttpContext;
            var tokens = http.RequestServices.GetRequiredService<GuestTokenService>();
            var db = http.RequestServices.GetRequiredService<ApplicationDbContext>();
            var scope = await ResolveScopeAsync(http, tokens, db);
            if (scope.Error is not null) return scope.Error;

            http.Items[FinanceScopeItemKey] = scope.ScopeId!.Value;
            try
            {
                return await next(invocation);
            }
            catch (SerializationConflictException)
            {
                return Conflict();
            }
            catch (ValidationException exception)
            {
                return ApiErrorResponses.FromStatus(exception.Message, StatusCodes.Status422UnprocessableEntity);
            }
        });

        MapFinanceStateEndpoint(finance);
        MapAccountEndpoints(finance);
        MapTransactionEndpoints(finance);
        MapSetAsideEndpoints(finance);
        MapUpcomingEventEndpoints(finance);
    }

    // ───────────────────────── Helpers ─────────────────────────

    /// <summary>
    /// Scope diselesaikan di server dari cookie auth/guest.
    /// Client tidak pernah menjadi otoritas atas ScopeId.
    /// </summary>
    private static async Task<(Guid? ScopeId, IResult? Error)> ResolveScopeAsync(
        HttpContext http,
        GuestTokenService guestTokenService,
        ApplicationDbContext db)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!string.IsNullOrEmpty(userId))
        {
            var scope = await db.Scopes.FirstOrDefaultAsync(
                s => s.Type == ScopeType.Owner && s.OwnerUserId == userId);

            return scope is null
                ? (null, Error("Owner Scope not found.", 400))
                : (scope.Id, null);
        }

        var resolution = await guestTokenService.ResolveWithStatusAsync(http, http.RequestAborted);
        if (resolution.Session is not null)
            return (resolution.Session.ScopeId, null);

        return resolution.IsExpired
            ? (null, ApiErrorResponses.Create(
                http,
                StatusCodes.Status410Gone,
                "GuestSessionExpired",
                "Sesi guest telah berakhir atau tidak lagi tersedia."))
            : (null, Error("Guest session not found.", 401));
    }

    private static Guid GetScopeId(HttpContext http)
        => http.Items.TryGetValue(FinanceScopeItemKey, out var scopeId) && scopeId is Guid id
            ? id
            : throw new InvalidOperationException("Finance endpoint was executed without resolved scope.");

    private static IResult Error(string message, int statusCode)
        => ApiErrorResponses.FromStatus(message, statusCode);

    private static IResult Conflict()
        => ApiErrorResponses.FromStatus(null, StatusCodes.Status409Conflict);
}
