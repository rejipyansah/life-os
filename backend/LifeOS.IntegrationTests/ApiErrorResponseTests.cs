using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using LifeOS.Api.Services;

namespace LifeOS.IntegrationTests;

public sealed class ApiErrorResponseTests
{
    [Fact]
    public async Task Unhandled_exception_returns_sanitized_error_envelope_and_trace_id()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Production"
        });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseSafeExceptionHandler();
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var http = statusCodeContext.HttpContext;
            if (http.Request.Path.StartsWithSegments("/api"))
                await ApiErrorResponses.FromStatus(null, http.Response.StatusCode).ExecuteAsync(http);
        });
        app.MapGet("/api/failure", (HttpContext _) =>
        {
            throw new InvalidOperationException("secret-programming-detail");
        });
        app.MapGet("/api/missing", () => Results.NotFound());
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/api/failure");
        var responseText = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("secret-programming-detail", responseText);
        using var body = System.Text.Json.JsonDocument.Parse(responseText);
        Assert.Equal("internal_error", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Terjadi kesalahan pada server. Silakan coba lagi.", body.RootElement.GetProperty("error").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));

        using var notFoundResponse = await client.GetAsync("/api/missing");
        using var notFoundBody = System.Text.Json.JsonDocument.Parse(await notFoundResponse.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
        Assert.Equal("not_found", notFoundBody.RootElement.GetProperty("code").GetString());
        Assert.Equal("Data yang diminta tidak ditemukan.", notFoundBody.RootElement.GetProperty("error").GetString());

        await app.StopAsync();
        await app.DisposeAsync();
    }
}
