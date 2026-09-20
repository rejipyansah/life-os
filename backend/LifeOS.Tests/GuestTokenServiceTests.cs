using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Tests;

public class GuestTokenServiceTests : IDisposable
{
    private readonly ApplicationDbContext _db;
    private readonly SqliteConnection _connection;
    private readonly GuestTokenService _sut;

    public GuestTokenServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
        _sut = new GuestTokenService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    // --- GenerateToken / HashToken ---

    [Fact]
    public void GenerateToken_ReturnsNonEmptyString()
    {
        var token = GuestTokenService.GenerateToken();

        Assert.False(string.IsNullOrEmpty(token));
    }

    [Fact]
    public void GenerateToken_ReturnsDifferentValues()
    {
        var t1 = GuestTokenService.GenerateToken();
        var t2 = GuestTokenService.GenerateToken();

        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public void GenerateToken_UsesUrlSafeCharacters()
    {
        var token = GuestTokenService.GenerateToken();

        Assert.Matches("^[A-Za-z0-9_-]+$", token);
    }

    [Fact]
    public void HashToken_ReturnsConsistentHash()
    {
        var token = "test-token";
        var h1 = GuestTokenService.HashToken(token);
        var h2 = GuestTokenService.HashToken(token);

        Assert.Equal(h1, h2);
    }

    [Fact]
    public void HashToken_ReturnsDifferentHashesForDifferentTokens()
    {
        var h1 = GuestTokenService.HashToken("token-a");
        var h2 = GuestTokenService.HashToken("token-b");

        Assert.NotEqual(h1, h2);
    }

    // --- SetGuestCookie: Secure flag ---

    [Fact]
    public void SetGuestCookie_HttpRequest_SecureIsFalse()
    {
        var context = new DefaultHttpContext();
        context.Request.IsHttps = false;

        GuestTokenService.SetGuestCookie(context, "test-token");

        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("guest_session=test-token", cookie);
        Assert.DoesNotContain("secure", cookie);
    }

    [Fact]
    public void SetGuestCookie_HttpsRequest_SecureIsTrue()
    {
        var context = new DefaultHttpContext();
        context.Request.IsHttps = true;

        GuestTokenService.SetGuestCookie(context, "test-token");

        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("secure", cookie);
    }

    [Fact]
    public void SetGuestCookie_AlwaysSetsHttpOnly()
    {
        var context = new DefaultHttpContext();

        GuestTokenService.SetGuestCookie(context, "test-token");

        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", cookie);
    }

    [Fact]
    public void SetGuestCookie_AlwaysSetsSameSiteStrict()
    {
        var context = new DefaultHttpContext();

        GuestTokenService.SetGuestCookie(context, "test-token");

        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("samesite=strict", cookie);
    }

    [Fact]
    public void SetGuestCookie_AlwaysSetsPath()
    {
        var context = new DefaultHttpContext();

        GuestTokenService.SetGuestCookie(context, "test-token");

        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("path=/", cookie);
    }

    [Fact]
    public void SetGuestCookie_SetsOneYearExpiry()
    {
        var context = new DefaultHttpContext();

        GuestTokenService.SetGuestCookie(context, "test-token");

        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("expires=", cookie);
    }

    // --- ReadToken ---

    [Fact]
    public void ReadToken_ReturnsNull_WhenNoCookie()
    {
        var context = new DefaultHttpContext();

        var result = GuestTokenService.ReadToken(context);

        Assert.Null(result);
    }

    [Fact]
    public void ReadToken_ReturnsToken_WhenCookiePresent()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "guest_session=my-token-value";

        var result = GuestTokenService.ReadToken(context);

        Assert.Equal("my-token-value", result);
    }

    // --- ClearGuestCookie ---

    [Fact]
    public void ClearGuestCookie_DeletesCookie()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "guest_session=old-token";

        GuestTokenService.ClearGuestCookie(context);

        var cookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains("guest_session=; expires=", cookie.ToLowerInvariant());
    }

    // --- ResolveAsync ---

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenNoCookie()
    {
        var context = new DefaultHttpContext();

        var result = await _sut.ResolveAsync(context);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNull_WhenTokenNotFoundInDb()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "guest_session=nonexistent-token";

        var result = await _sut.ResolveAsync(context);

        Assert.Null(result);
    }

    [Fact]
    public async Task ResolveAsync_ReturnsSession_WhenValidTokenExists()
    {
        var token = GuestTokenService.GenerateToken();
        var tokenHash = GuestTokenService.HashToken(token);

        var scope = new Scope { Type = ScopeType.Guest };
        var session = new GuestSession { Scope = scope, TokenHash = tokenHash };
        _db.Scopes.Add(scope);
        _db.GuestSessions.Add(session);
        await _db.SaveChangesAsync();

        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"guest_session={token}";

        var result = await _sut.ResolveAsync(context);

        Assert.NotNull(result);
        Assert.Equal(session.Id, result!.Id);
        Assert.Equal(ScopeType.Guest, result.Scope.Type);
    }

    // --- Full lifecycle: Set → Read → Resolve ---

    [Fact]
    public async Task FullLifecycle_SetReadResolve_Http()
    {
        var createContext = new DefaultHttpContext();
        createContext.Request.IsHttps = false;
        var token = GuestTokenService.GenerateToken();

        GuestTokenService.SetGuestCookie(createContext, token);

        var readContext = new DefaultHttpContext();
        readContext.Request.IsHttps = false;
        var setCookie = createContext.Response.Headers.SetCookie.ToString();
        var cookieValue = setCookie.Split(';')[0].Split('=')[1];
        readContext.Request.Headers.Cookie = $"guest_session={cookieValue}";

        var readToken = GuestTokenService.ReadToken(readContext);
        Assert.Equal(token, readToken);
    }

    [Fact]
    public async Task FullLifecycle_SetReadResolve_Https()
    {
        var createContext = new DefaultHttpContext();
        createContext.Request.IsHttps = true;
        var token = GuestTokenService.GenerateToken();

        GuestTokenService.SetGuestCookie(createContext, token);

        var setCookie = createContext.Response.Headers.SetCookie.ToString();
        Assert.Contains("secure", setCookie);

        var readContext = new DefaultHttpContext();
        readContext.Request.IsHttps = true;
        var cookieValue = setCookie.Split(';')[0].Split('=')[1];
        readContext.Request.Headers.Cookie = $"guest_session={cookieValue}";

        var readToken = GuestTokenService.ReadToken(readContext);
        Assert.Equal(token, readToken);
    }
}
