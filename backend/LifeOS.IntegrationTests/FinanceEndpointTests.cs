using System.Net;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LifeOS.IntegrationTests;

public sealed class FinanceEndpointTests
{
    [Fact]
    public async Task Account_endpoint_rejects_invalid_enum_and_uses_server_resolved_scope()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        Guid ownerScopeId;
        Guid guestScopeId;
        var guestToken = GuestTokenService.GenerateToken();
        var otherGuestToken = GuestTokenService.GenerateToken();
        await using (var setupDb = new ApplicationDbContext(options))
        {
            await setupDb.Database.EnsureCreatedAsync();
            var ownerScope = new Scope { Type = ScopeType.Owner };
            var guestScope = new Scope { Type = ScopeType.Guest };
            var otherGuestScope = new Scope { Type = ScopeType.Guest };
            setupDb.Scopes.AddRange(ownerScope, guestScope, otherGuestScope);
            setupDb.GuestSessions.AddRange(new GuestSession
            {
                Scope = guestScope,
                TokenHash = GuestTokenService.HashToken(guestToken)
            }, new GuestSession
            {
                Scope = otherGuestScope,
                TokenHash = GuestTokenService.HashToken(otherGuestToken)
            });
            await setupDb.SaveChangesAsync();
            ownerScopeId = ownerScope.Id;
            guestScopeId = guestScope.Id;
        }

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDbContext<ApplicationDbContext>(serviceOptions => serviceOptions.UseSqlite(connection));
        builder.Services.ConfigureHttpJsonOptions(json =>
            json.SerializerOptions.Converters.Add(new JsonStringEnumConverter(null, allowIntegerValues: false)));
        builder.Services.AddScoped<GuestTokenService>();
        builder.Services.AddScoped<BalanceCalculator>();
        builder.Services.AddScoped<AccountService>();
        builder.Services.AddScoped<TransactionService>();
        builder.Services.AddScoped<SetAsideService>();
        builder.Services.AddScoped<UpcomingEventService>();
        builder.Services.AddScoped<FinanceStateService>();

        await using var app = builder.Build();
        app.UseSafeExceptionHandler();
        app.UseStatusCodePages(async statusCodeContext =>
        {
            var http = statusCodeContext.HttpContext;
            if (http.Request.Path.StartsWithSegments("/api"))
                await ApiErrorResponses.FromStatus(null, http.Response.StatusCode).ExecuteAsync(http);
        });
        app.MapFinanceApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using (var anonymousClient = app.GetTestClient())
        using (var anonymousResponse = await anonymousClient.GetAsync("/api/finance/accounts"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
            using var anonymousPayload = System.Text.Json.JsonDocument.Parse(
                await anonymousResponse.Content.ReadAsStringAsync());
            Assert.Equal("NoGuestSession", anonymousPayload.RootElement.GetProperty("code").GetString());
        }

        client.DefaultRequestHeaders.Add("Cookie", $"guest_session={guestToken}");

        var invalidEnum = await client.PostAsync("/api/finance/accounts",
            new StringContent("""{"name":"Bad enum","type":"NotAnAccountType"}""", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidEnum.StatusCode);
        using (var invalidEnumBody = System.Text.Json.JsonDocument.Parse(await invalidEnum.Content.ReadAsStringAsync()))
        {
            Assert.Equal("invalid_request", invalidEnumBody.RootElement.GetProperty("code").GetString());
            Assert.Equal("Permintaan tidak valid.", invalidEnumBody.RootElement.GetProperty("error").GetString());
        }

        var requestBody = $$"""{"scopeId":"{{ownerScopeId}}","name":"Guest account","type":"Bank"}""";
        var created = await client.PostAsync("/api/finance/accounts",
            new StringContent(requestBody, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        using var accountPayload = System.Text.Json.JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var accountId = accountPayload.RootElement.GetProperty("accountId").GetGuid();

        var incomeResponse = await PostJsonAsync(client, "/api/finance/transactions", $$"""{"type":"Income","amount":500000,"occurredOn":"{{BusinessDate.TodayWib:yyyy-MM-dd}}","entries":[{"accountId":"{{accountId}}","amount":500000}]}""");
        Assert.Equal(HttpStatusCode.OK, incomeResponse.StatusCode);

        var setAsideResponse = await PostJsonAsync(client, "/api/finance/set-asides", """{"name":"Dana Uji API","kind":"Saving","amount":100000}""");
        Assert.Equal(HttpStatusCode.OK, setAsideResponse.StatusCode);
        using var setAsidePayload = System.Text.Json.JsonDocument.Parse(await setAsideResponse.Content.ReadAsStringAsync());
        var setAsideId = setAsidePayload.RootElement.GetProperty("id").GetGuid();

        var invalidExpense = await PostJsonAsync(client, "/api/finance/transactions", $$"""{"type":"Expense","amount":150000,"occurredOn":"{{BusinessDate.TodayWib:yyyy-MM-dd}}","entries":[{"accountId":"{{accountId}}","amount":-100000}]}""");
        Assert.Equal((HttpStatusCode)422, invalidExpense.StatusCode);
        using (var invalidExpenseBody = System.Text.Json.JsonDocument.Parse(await invalidExpense.Content.ReadAsStringAsync()))
        {
            Assert.Equal("expense_entry_mismatch", invalidExpenseBody.RootElement.GetProperty("code").GetString());
            Assert.Equal("Nominal rincian tidak sesuai dengan nominal pengeluaran.",
                invalidExpenseBody.RootElement.GetProperty("error").GetString());
            Assert.True(invalidExpenseBody.RootElement.TryGetProperty("traceId", out _));
        }

        var expenseResponse = await PostJsonAsync(client, "/api/finance/transactions", $$"""{"type":"Expense","amount":150000,"occurredOn":"{{BusinessDate.TodayWib:yyyy-MM-dd}}","setAsideId":"{{setAsideId}}","entries":[{"accountId":"{{accountId}}","amount":-150000}]}""");
        Assert.Equal(HttpStatusCode.OK, expenseResponse.StatusCode);
        using var expensePayload = System.Text.Json.JsonDocument.Parse(await expenseResponse.Content.ReadAsStringAsync());
        var expenseId = expensePayload.RootElement.GetProperty("transactionId").GetGuid();

        var reverseResponse = await PostJsonAsync(client, $"/api/finance/transactions/{expenseId}/reverse", "{}");
        Assert.Equal(HttpStatusCode.OK, reverseResponse.StatusCode);

        using var verifyAccountRequest = new HttpRequestMessage(HttpMethod.Get, "/api/finance/accounts");
        using var accountsResponse = await client.SendAsync(verifyAccountRequest);
        using var accountsPayload = System.Text.Json.JsonDocument.Parse(await accountsResponse.Content.ReadAsStringAsync());
        Assert.Equal(500_000m, accountsPayload.RootElement.GetProperty("accounts")[0].GetProperty("actualBalance").GetDecimal());

        using var verifySetAsideRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/set-asides/{setAsideId}");
        using var verifiedSetAside = await client.SendAsync(verifySetAsideRequest);
        using var verifiedSetAsidePayload = System.Text.Json.JsonDocument.Parse(await verifiedSetAside.Content.ReadAsStringAsync());
        Assert.Equal(100_000m, verifiedSetAsidePayload.RootElement.GetProperty("amount").GetDecimal());

        var invalidEvent = await PostJsonAsync(client, "/api/finance/upcoming-events", """{"title":"Invalid","amount":0,"direction":"Expense"}""");
        Assert.Equal((HttpStatusCode)422, invalidEvent.StatusCode);
        var eventResponse = await PostJsonAsync(client, "/api/finance/upcoming-events", $$"""{"title":"Tagihan API","amount":250000,"direction":"Expense","defaultSourceAccountId":"{{accountId}}","dueDate":"{{BusinessDate.TodayWib:yyyy-MM-dd}}","scheduleKind":"Scheduled"}""");
        Assert.Equal(HttpStatusCode.OK, eventResponse.StatusCode);
        using var eventPayload = System.Text.Json.JsonDocument.Parse(await eventResponse.Content.ReadAsStringAsync());
        var eventId = eventPayload.RootElement.GetProperty("id").GetGuid();
        Assert.Equal(accountId, eventPayload.RootElement.GetProperty("defaultSourceAccountId").GetGuid());
        Assert.Equal("Guest account", eventPayload.RootElement.GetProperty("defaultSourceAccountName").GetString());

        using var stateResponse = await client.GetAsync("/api/finance/state");
        using var statePayload = System.Text.Json.JsonDocument.Parse(await stateResponse.Content.ReadAsStringAsync());
        var stateEvent = statePayload.RootElement.GetProperty("upcomingEvents").EnumerateArray()
            .Single(item => item.GetProperty("id").GetGuid() == eventId);
        Assert.Equal("Guest account", stateEvent.GetProperty("defaultSourceAccountName").GetString());

        var postponeResponse = await PostJsonAsync(client, $"/api/finance/upcoming-events/{eventId}/postpone", "{}");
        Assert.Equal(HttpStatusCode.OK, postponeResponse.StatusCode);
        using var postponedPayload = System.Text.Json.JsonDocument.Parse(await postponeResponse.Content.ReadAsStringAsync());
        Assert.Equal(BusinessDate.TodayWib.AddDays(1).ToString("yyyy-MM-dd"), postponedPayload.RootElement.GetProperty("dueDate").GetString());

        using var otherScopeRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/transactions/{expenseId}");
        otherScopeRequest.Headers.Add("Cookie", $"guest_session={otherGuestToken}");
        using var otherScopeResponse = await client.SendAsync(otherScopeRequest);
        Assert.Equal(HttpStatusCode.NotFound, otherScopeResponse.StatusCode);
        using var otherScopeSetAsideRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/finance/set-asides/{setAsideId}");
        otherScopeSetAsideRequest.Headers.Add("Cookie", $"guest_session={otherGuestToken}");
        using var otherScopeSetAsideResponse = await client.SendAsync(otherScopeSetAsideRequest);
        Assert.Equal(HttpStatusCode.NotFound, otherScopeSetAsideResponse.StatusCode);

        await using var verifyDb = new ApplicationDbContext(options);
        var storedAccount = await verifyDb.Accounts.SingleAsync(account => account.Name == "Guest account");
        Assert.Equal(guestScopeId, storedAccount.ScopeId);
        Assert.NotEqual(ownerScopeId, storedAccount.ScopeId);

        var storedGuestSession = await verifyDb.GuestSessions.SingleAsync(session => session.ScopeId == guestScopeId);
        storedGuestSession.LastActivityAt = DateTime.UtcNow - GuestTokenService.InactivityExpiry;
        await verifyDb.SaveChangesAsync();
        using var expiredSessionRequest = new HttpRequestMessage(HttpMethod.Get, "/api/finance/accounts");
        expiredSessionRequest.Headers.Add("Cookie", $"guest_session={guestToken}");
        using var expiredSessionResponse = await client.SendAsync(expiredSessionRequest);
        Assert.Equal((HttpStatusCode)410, expiredSessionResponse.StatusCode);
        using var expiredSessionPayload = System.Text.Json.JsonDocument.Parse(
            await expiredSessionResponse.Content.ReadAsStringAsync());
        Assert.Equal("GuestSessionExpired", expiredSessionPayload.RootElement.GetProperty("code").GetString());
    }

    private static Task<HttpResponseMessage> PostJsonAsync(HttpClient client, string path, string json)
        => client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
}
