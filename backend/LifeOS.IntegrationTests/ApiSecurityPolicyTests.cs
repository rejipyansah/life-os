using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using LifeOS.Api.Services;

namespace LifeOS.IntegrationTests;

public sealed class ApiSecurityPolicyTests
{
    [Fact]
    public async Task Mutation_with_untrusted_or_missing_origin_is_rejected()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();

        using var untrusted = CreatePost("https://attacker.example");
        using var untrustedResponse = await client.SendAsync(untrusted);
        Assert.Equal(HttpStatusCode.Forbidden, untrustedResponse.StatusCode);

        using var missingOriginResponse = await client.PostAsync("/api/mutate", null);
        Assert.Equal(HttpStatusCode.Forbidden, missingOriginResponse.StatusCode);
    }

    [Fact]
    public async Task Mutation_with_trusted_origin_is_allowed_and_reads_are_not_blocked()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();

        using var mutation = CreatePost("https://rejip.my.id");
        using var mutationResponse = await client.SendAsync(mutation);
        Assert.Equal(HttpStatusCode.OK, mutationResponse.StatusCode);

        using var readResponse = await client.GetAsync("/api/mutate");
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
    }

    [Fact]
    public async Task Interpret_rate_limit_allows_ten_requests_per_minute_per_partition()
    {
        await using var app = await CreateAppAsync();
        using var client = app.GetTestClient();

        for (var requestNumber = 1; requestNumber <= 10; requestNumber++)
        {
            using var request = CreatePost("https://rejip.my.id");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var limitedRequest = CreatePost("https://rejip.my.id");
        using var limitedResponse = await client.SendAsync(limitedRequest);
        Assert.Equal(HttpStatusCode.TooManyRequests, limitedResponse.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await limitedResponse.Content.ReadAsStringAsync());
        Assert.Equal("rate_limited", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("Terlalu banyak permintaan interpretasi. Silakan coba lagi nanti.",
            body.RootElement.GetProperty("error").GetString());
    }

    private static HttpRequestMessage CreatePost(string? origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/mutate");
        if (origin is not null)
            request.Headers.TryAddWithoutValidation("Origin", origin);
        return request;
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddApiRateLimiting();

        var app = builder.Build();
        app.UseRouting();
        app.UseTrustedOriginProtection(ApiSecurityPolicies.GetAllowedFrontendOrigins(isDevelopment: false));
        app.UseRateLimiter();
        app.MapMethods("/api/mutate", ["GET", "POST"], () => Results.Ok())
            .RequireRateLimiting(ApiSecurityPolicies.InterpretRateLimitPolicy);
        await app.StartAsync();
        return app;
    }
}
