using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--reset-owner-password"))
    Environment.Exit(await OwnerAdministrationCommands.ResetOwnerPasswordAsync());

if (args.Contains("--provision-owner"))
    Environment.Exit(await OwnerAdministrationCommands.ProvisionOwnerAsync(args));

if (args.Contains("--ensure-owner-scope"))
    Environment.Exit(await OwnerAdministrationCommands.EnsureOwnerScopeAsync());

var builder = WebApplication.CreateBuilder(args);
var allowedFrontendOrigins = ApiSecurityPolicies.GetAllowedFrontendOrigins(
    builder.Environment.IsDevelopment());

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = false;
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireLowercase = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy => policy
        .WithOrigins(allowedFrontendOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.ConfigureApplicationCookie(options =>
    ApiSecurityPolicies.ConfigureApplicationCookie(options, builder.Environment.IsDevelopment()));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter(null, allowIntegerValues: false)));

builder.Services.AddAuthorization();
builder.Services.AddApiRateLimiting();
builder.Services.AddOpenApi();

builder.Services.AddScoped<GuestTokenService>();
builder.Services.AddScoped<GuestSandboxCleanupService>();
builder.Services.AddHostedService<GuestSandboxCleanupWorker>();
builder.Services.AddScoped<BalanceCalculator>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<SetAsideProjectionService>();
builder.Services.AddScoped<SetAsideService>();
builder.Services.AddScoped<UpcomingEventService>();
builder.Services.AddScoped<FinanceStateService>();
builder.Services.AddHttpClient<GroqInterpreter>();
builder.Services.AddScoped<IInterpreter>(services => services.GetRequiredService<GroqInterpreter>());

var app = builder.Build();
app.UseSafeExceptionHandler();
app.UseStatusCodePages(async statusCodeContext =>
{
    var http = statusCodeContext.HttpContext;
    if (http.Request.Path.StartsWithSegments("/api"))
        await ApiErrorResponses.FromStatus(null, http.Response.StatusCode).ExecuteAsync(http);
});
app.UseCors("Frontend");
app.UseTrustedOriginProtection(allowedFrontendOrigins);

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth");
app.MapIdentityEndpoints();
app.MapGuestSessionEndpoints();
app.MapFinanceApi();
app.MapInterpretEndpoint();

app.Run();

public partial class Program { }
