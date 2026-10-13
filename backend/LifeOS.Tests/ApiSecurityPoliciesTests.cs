using LifeOS.Api.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;

namespace LifeOS.Tests;

public class ApiSecurityPoliciesTests
{
    [Fact]
    public void ConfigureApplicationCookie_DevelopmentAllowsHttpViteProxy()
    {
        var options = new CookieAuthenticationOptions();

        ApiSecurityPolicies.ConfigureApplicationCookie(options, isDevelopment: true);

        Assert.Equal(SameSiteMode.Lax, options.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.SameAsRequest, options.Cookie.SecurePolicy);
        Assert.True(options.Cookie.HttpOnly);
        Assert.Equal(TimeSpan.FromDays(30), options.ExpireTimeSpan);
    }

    [Fact]
    public void ConfigureApplicationCookie_ProductionKeepsCrossOriginSecureCookie()
    {
        var options = new CookieAuthenticationOptions();

        ApiSecurityPolicies.ConfigureApplicationCookie(options, isDevelopment: false);

        Assert.Equal(SameSiteMode.None, options.Cookie.SameSite);
        Assert.Equal(CookieSecurePolicy.Always, options.Cookie.SecurePolicy);
        Assert.True(options.Cookie.HttpOnly);
    }
}
