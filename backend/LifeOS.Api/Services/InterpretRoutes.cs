using System.Security.Claims;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public static class InterpretRoutes
{
    public static void MapInterpretEndpoint(this WebApplication app)
    {
        app.MapPost("/api/interpret", async (
            HttpContext http,
            IInterpreter interpreter,
            GuestTokenService guestTokens,
            ApplicationDbContext db) =>
        {
            var scopeId = await ResolveInterpretScopeAsync(http, guestTokens, db);
            if (scopeId.Error is not null) return scopeId.Error;

            InterpretInputRequest? body;
            try
            {
                body = await http.Request.ReadFromJsonAsync<InterpretInputRequest>(http.RequestAborted);
            }
            catch (System.Text.Json.JsonException)
            {
                return ApiErrorResponses.FromStatus("Invalid request body.", StatusCodes.Status400BadRequest);
            }
            catch (BadHttpRequestException)
            {
                return ApiErrorResponses.FromStatus("Invalid request body.", StatusCodes.Status400BadRequest);
            }

            if (body is null || string.IsNullOrWhiteSpace(body.Input))
                return ApiErrorResponses.FromStatus("Input is required.", StatusCodes.Status400BadRequest);

            try
            {
                var result = await InterpretEndpoint.HandleAsync(
                    body,
                    interpreter,
                    scopeId.ScopeId!.Value,
                    db,
                    http.RequestAborted);
                return Results.Json(result.Body, statusCode: result.StatusCode);
            }
            catch (ValidationException exception)
            {
                return ApiErrorResponses.FromStatus(exception.Message, StatusCodes.Status422UnprocessableEntity);
            }
        })
        .RequireRateLimiting(ApiSecurityPolicies.InterpretRateLimitPolicy)
        .WithName("Interpret")
        .WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
    }

    private static async Task<(Guid? ScopeId, IResult? Error)> ResolveInterpretScopeAsync(
        HttpContext http,
        GuestTokenService guestTokens,
        ApplicationDbContext db)
    {
        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrEmpty(userId))
        {
            var ownerScope = await db.Scopes.FirstOrDefaultAsync(
                scope => scope.Type == ScopeType.Owner && scope.OwnerUserId == userId,
                http.RequestAborted);
            return ownerScope is null
                ? (null, ApiErrorResponses.Create(
                    http,
                    StatusCodes.Status400BadRequest,
                    "owner_scope_not_found",
                    "Ruang data pemilik tidak ditemukan."))
                : (ownerScope.Id, null);
        }

        var resolution = await guestTokens.ResolveWithStatusAsync(http, http.RequestAborted);
        if (resolution.Session is not null)
            return (resolution.Session.ScopeId, null);

        return resolution.IsExpired
            ? (null, ApiErrorResponses.Create(
                http,
                StatusCodes.Status410Gone,
                "GuestSessionExpired",
                "Sesi guest telah berakhir atau tidak lagi tersedia."))
            : (null, ApiErrorResponses.Create(
                http,
                StatusCodes.Status401Unauthorized,
                "NoGuestSession",
                "Sesi guest tidak ditemukan."));
    }
}
