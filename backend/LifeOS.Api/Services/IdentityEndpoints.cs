using System.Security.Claims;
using Microsoft.AspNetCore.Identity;

namespace LifeOS.Api.Services;

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/me", (ClaimsPrincipal user) =>
        {
            var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
            var userName = user.FindFirstValue(ClaimTypes.Name);
            var email = user.FindFirstValue(ClaimTypes.Email);

            return Results.Ok(new
            {
                userId,
                userName,
                email,
                isAuthenticated = user.Identity?.IsAuthenticated ?? false
            });
        })
        .RequireAuthorization()
        .WithName("GetAuthMe");

        app.MapPost("/api/auth/login", async (
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            HttpContext http) =>
        {
            LoginRequest? body;
            try
            {
                body = await http.Request.ReadFromJsonAsync<LoginRequest>(http.RequestAborted);
            }
            catch (System.Text.Json.JsonException)
            {
                return ApiErrorResponses.FromStatus("Invalid request body.", StatusCodes.Status400BadRequest);
            }
            catch (BadHttpRequestException)
            {
                return ApiErrorResponses.FromStatus("Invalid request body.", StatusCodes.Status400BadRequest);
            }

            if (body is null
                || string.IsNullOrWhiteSpace(body.Email)
                || string.IsNullOrWhiteSpace(body.Password))
            {
                return InvalidCredentials(http);
            }

            var user = await userManager.FindByEmailAsync(body.Email);
            if (user is null)
                return InvalidCredentials(http);

            var result = await signInManager.PasswordSignInAsync(
                user,
                body.Password,
                isPersistent: true,
                lockoutOnFailure: false);
            return result.Succeeded
                ? Results.Ok(new { isAuthenticated = true })
                : InvalidCredentials(http);
        })
        .WithName("Login");

        app.MapPost("/api/auth/logout", async (SignInManager<IdentityUser> signInManager) =>
        {
            await signInManager.SignOutAsync();
            return Results.NoContent();
        })
        .RequireAuthorization()
        .WithName("Logout");
    }

    private static IResult InvalidCredentials(HttpContext http)
        => ApiErrorResponses.Create(
            http,
            StatusCodes.Status401Unauthorized,
            "invalid_credentials",
            "Email atau kata sandi tidak valid.");
}

public sealed record LoginRequest(string Email, string Password);
