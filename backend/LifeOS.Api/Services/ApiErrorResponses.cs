using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;

namespace LifeOS.Api.Services;

public sealed record ApiErrorResponse(string Code, string Error, string? TraceId = null);

public static class ApiErrorResponses
{
    public static IResult FromStatus(string? detail, int statusCode, string? code = null)
    {
        var issue = FinanceValidationErrorCatalog.Resolve(detail, statusCode);
        return Results.Json(
            new ApiErrorResponse(
                code ?? issue.Code,
                issue.Message,
                Activity.Current?.Id),
            statusCode: statusCode);
    }

    public static IResult Create(
        HttpContext http,
        int statusCode,
        string code,
        string error)
        => Results.Json(
            new ApiErrorResponse(code, error, http.TraceIdentifier),
            statusCode: statusCode);

    public static IApplicationBuilder UseSafeExceptionHandler(this IApplicationBuilder app)
        => app.UseExceptionHandler(handler => handler.Run(async http =>
        {
            var exception = http.Features.Get<IExceptionHandlerFeature>()?.Error;
            var logger = http.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("LifeOS.Api.UnhandledException");
            logger.LogError(exception, "Unhandled request failure. TraceId: {TraceId}", http.TraceIdentifier);

            http.Response.Clear();
            await Create(
                http,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "Terjadi kesalahan pada server. Silakan coba lagi.")
                .ExecuteAsync(http);
        }));

}
