using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;

namespace LifeOS.Api.Services;

public static class ApiSecurityPolicies
{
    public const string InterpretRateLimitPolicy = "InterpretRateLimit";

    public static readonly string[] ProductionFrontendOrigins =
    [
        "https://life-os.rejipyansah.workers.dev",
        "https://life-os.rejip.my.id",
        "https://rejip.my.id"
    ];

    public static void ConfigureApplicationCookie(CookieAuthenticationOptions options, bool isDevelopment)
    {
        options.Cookie.HttpOnly = true;
        // Local development uses the Vite /api proxy over HTTP, so Lax avoids
        // browsers rejecting SameSite=None cookies without Secure.
        options.Cookie.SameSite = isDevelopment ? SameSiteMode.Lax : SameSiteMode.None;
        options.Cookie.SecurePolicy = isDevelopment
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
    }

    public static string[] GetAllowedFrontendOrigins(bool isDevelopment)
        => isDevelopment
            ? [
                .. ProductionFrontendOrigins,
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:5174",
                "http://127.0.0.1:5174"
            ]
            : ProductionFrontendOrigins;

    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.HttpContext.Response.WriteAsJsonAsync(
                    new ApiErrorResponse(
                        "rate_limited",
                        "Terlalu banyak permintaan interpretasi. Silakan coba lagi nanti.",
                        context.HttpContext.TraceIdentifier),
                    cancellationToken);
            };

            options.AddPolicy(InterpretRateLimitPolicy, httpContext =>
            {
                var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
                var guestToken = GuestTokenService.ReadToken(httpContext);
                var partitionKey = !string.IsNullOrWhiteSpace(userId)
                    ? $"user:{userId}"
                    : !string.IsNullOrWhiteSpace(guestToken)
                        ? $"guest:{GuestTokenService.HashToken(guestToken)}"
                        : $"ip:{httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";

                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        // The signed-in owner is exempt from application-level limits.
                        // Provider-enforced Groq quotas still apply.
                        PermitLimit = !string.IsNullOrWhiteSpace(userId) ? int.MaxValue : 10,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        return services;
    }

    public static IApplicationBuilder UseTrustedOriginProtection(
        this IApplicationBuilder app,
        IEnumerable<string> trustedOrigins)
    {
        var allowedOrigins = trustedOrigins.ToHashSet(StringComparer.OrdinalIgnoreCase);

        return app.Use(async (context, next) =>
        {
            var method = context.Request.Method;
            var isMutation = method is not ("GET" or "HEAD" or "OPTIONS");
            if (!isMutation || !context.Request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            var requestOrigin = GetRequestOrigin(context.Request);
            if (requestOrigin is null || !allowedOrigins.Contains(requestOrigin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new ApiErrorResponse(
                        "forbidden",
                        "Asal permintaan tidak diizinkan.",
                        context.TraceIdentifier));
                return;
            }

            await next();
        });
    }

    private static string? GetRequestOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(origin))
        {
            var referer = request.Headers.Referer.FirstOrDefault();
            if (!Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
                return null;
            origin = refererUri.GetLeftPart(UriPartial.Authority);
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            || (originUri.Scheme != Uri.UriSchemeHttps && originUri.Scheme != Uri.UriSchemeHttp)
            || originUri.UserInfo.Length > 0
            || originUri.AbsolutePath != "/"
            || originUri.Query.Length > 0
            || originUri.Fragment.Length > 0)
        {
            return null;
        }

        return originUri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
    }
}
