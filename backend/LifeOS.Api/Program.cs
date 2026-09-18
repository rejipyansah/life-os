using System.Security.Claims;
using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

if (args.Contains("--reset-owner-password"))
{
    var exitCode = await RunPasswordResetAsync();
    Environment.Exit(exitCode);
}

if (args.Contains("--provision-owner"))
{
    var exitCode = await RunProvisioningAsync(args);
    Environment.Exit(exitCode);
}

if (args.Contains("--ensure-owner-scope"))
{
    var exitCode = await RunEnsureOwnerScopeAsync();
    Environment.Exit(exitCode);
}

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
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
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddAuthorization();
builder.Services.AddScoped<GuestTokenService>();
builder.Services.AddScoped<TransactionService>();
builder.Services.AddScoped<AllocationService>();
builder.Services.AddScoped<AccountService>();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

// ENDPOINTS

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }))
    .WithName("GetHealth");

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
    var body = await http.Request.ReadFromJsonAsync<LoginRequest>();
    if (body is null || string.IsNullOrWhiteSpace(body.Email) || string.IsNullOrWhiteSpace(body.Password))
        return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);

    var user = await userManager.FindByEmailAsync(body.Email);
    if (user is null)
        return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);

    var result = await signInManager.PasswordSignInAsync(user, body.Password, isPersistent: true, lockoutOnFailure: false);
    if (!result.Succeeded)
        return Results.Json(new { error = "Invalid email or password." }, statusCode: 401);

    return Results.Ok(new { isAuthenticated = true });
})
.WithName("Login");

app.MapPost("/api/auth/logout", async (SignInManager<IdentityUser> signInManager) =>
{
    await signInManager.SignOutAsync();
    return Results.NoContent();
})
.RequireAuthorization()
.WithName("Logout");

// GUEST SESSION
app.MapPost("/api/guest/session", async (HttpContext http) =>
{
    if (http.User.Identity?.IsAuthenticated == true)
    {
        return Results.Json(new { error = "Authenticated users must not create guest sessions." }, statusCode: 403);
    }

    var guestTokenService = http.RequestServices.GetRequiredService<GuestTokenService>();

    var existingSession = await guestTokenService.ResolveAsync(http);
    if (existingSession is not null)
    {
        await guestTokenService.UpdateActivityAsync(existingSession);
        return Results.Ok(new { isGuest = true });
    }

    // Invalid/expired cookie: clear it and create a fresh session
    var existingToken = GuestTokenService.ReadToken(http);
    if (existingToken is not null)
    {
        GuestTokenService.ClearGuestCookie(http);
    }

    // Create new Guest Scope + GuestSession atomically
    var db = http.RequestServices.GetRequiredService<ApplicationDbContext>();
    var scope = new Scope { Type = ScopeType.Guest };
    var token = GuestTokenService.GenerateToken();
    var session = new GuestSession { Scope = scope, TokenHash = GuestTokenService.HashToken(token) };

    db.Scopes.Add(scope);
    db.GuestSessions.Add(session);
    await db.SaveChangesAsync();

    GuestTokenService.SetGuestCookie(http, token);

    return Results.Ok(new { isGuest = true });
})
.WithName("CreateGuestSession");

// FINANCE - TRANSACTIONS
app.MapPost("/api/finance/transactions", async (
    HttpContext http,
    TransactionService transactionService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        // Authenticated user: resolve Owner Scope
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        // Guest user: resolve Guest Scope from session
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    CreateTransactionCommand? command;
    try
    {
        command = await http.Request.ReadFromJsonAsync<CreateTransactionCommand>();
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);
    }
    if (command is null)
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);

    // Override ScopeId with server-resolved value
    command.ScopeId = scopeId;

    try
    {
        var (transaction, entries) = await transactionService.CreateTransactionAsync(command);
        return Results.Ok(new
        {
            transactionId = transaction.Id,
            type = transaction.Type,
            amount = transaction.Amount,
            occurredOn = transaction.OccurredOn,
            createdAt = transaction.CreatedAt,
            entryCount = entries.Count
        });
    }
    catch (ValidationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 422);
    }
    catch (SerializationConflictException)
    {
        return Results.Json(
            new { error = "The request conflicted with a concurrent operation. Please try again." },
            statusCode: 409);
    }
})
.WithName("CreateTransaction");

app.MapGet("/api/finance/transactions", async (
    HttpContext http,
    TransactionService transactionService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    var transactions = await transactionService.GetTransactionsAsync(scopeId);
    return Results.Ok(new { items = transactions });
})
.WithName("GetTransactions");

app.MapGet("/api/finance/transactions/{id:guid}", async (
    Guid id,
    HttpContext http,
    TransactionService transactionService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    var transaction = await transactionService.GetTransactionByIdAsync(id, scopeId);
    if (transaction is null)
        return Results.Json(new { error = "Transaction not found." }, statusCode: 404);

    return Results.Ok(transaction);
})
.WithName("GetTransactionById");

// FINANCE - ALLOCATIONS
app.MapPost("/api/finance/allocations", async (
    HttpContext http,
    AllocationService allocationService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    CreateAllocationCommand? command;
    try
    {
        command = await http.Request.ReadFromJsonAsync<CreateAllocationCommand>();
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);
    }
    if (command is null)
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);

    command.ScopeId = scopeId;

    try
    {
        var allocation = await allocationService.CreateAllocationAsync(command);
        return Results.Ok(new
        {
            allocationId = allocation.Id,
            accountId = allocation.AccountId,
            name = allocation.Name,
            amount = allocation.Amount,
            isActive = allocation.IsActive,
            createdAt = allocation.CreatedAt
        });
    }
    catch (ValidationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 422);
    }
})
.WithName("CreateAllocation");

app.MapGet("/api/finance/allocations", async (
    HttpContext http,
    AllocationService allocationService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    var allocations = await allocationService.GetAllocationsAsync(scopeId);
    return Results.Ok(allocations.Select(a => new
    {
        allocationId = a.Id,
        accountId = a.AccountId,
        name = a.Name,
        amount = a.Amount,
        isActive = a.IsActive,
        createdAt = a.CreatedAt
    }));
})
.WithName("GetAllocations");

app.MapPatch("/api/finance/allocations/{id:guid}", async (
    Guid id,
    HttpContext http,
    AllocationService allocationService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    UpdateAllocationCommand? command;
    try
    {
        command = await http.Request.ReadFromJsonAsync<UpdateAllocationCommand>();
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);
    }
    if (command is null)
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);

    command.ScopeId = scopeId;

    try
    {
        var allocation = await allocationService.UpdateAllocationAsync(id, command);
        return Results.Ok(new
        {
            allocationId = allocation.Id,
            accountId = allocation.AccountId,
            name = allocation.Name,
            amount = allocation.Amount,
            isActive = allocation.IsActive,
            createdAt = allocation.CreatedAt
        });
    }
    catch (ValidationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 422);
    }
})
.WithName("UpdateAllocation");

// FINANCE - ACCOUNTS
app.MapPost("/api/finance/accounts", async (
    HttpContext http,
    AccountService accountService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    CreateAccountCommand? command;
    try
    {
        command = await http.Request.ReadFromJsonAsync<CreateAccountCommand>();
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);
    }
    if (command is null)
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);

    command.ScopeId = scopeId;

    try
    {
        var account = await accountService.CreateAccountAsync(command);
        return Results.Ok(new
        {
            accountId = account.Id,
            name = account.Name,
            type = account.Type,
            isArchived = account.IsArchived,
            createdAt = account.CreatedAt
        });
    }
    catch (ValidationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 422);
    }
})
.WithName("CreateAccount");

app.MapGet("/api/finance/accounts", async (
    HttpContext http,
    AccountService accountService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db,
    bool? includeArchived) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    var result = await accountService.GetAccountsAsync(scopeId, includeArchived ?? false);
    return Results.Ok(result);
})
.WithName("GetAccounts");

app.MapGet("/api/finance/accounts/{id:guid}", async (
    Guid id,
    HttpContext http,
    AccountService accountService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    try
    {
        var account = await accountService.GetAccountByIdAsync(id, scopeId);
        return Results.Ok(account);
    }
    catch (ValidationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 404);
    }
})
.WithName("GetAccountById");

app.MapPatch("/api/finance/accounts/{id:guid}", async (
    Guid id,
    HttpContext http,
    AccountService accountService,
    GuestTokenService guestTokenService,
    ApplicationDbContext db) =>
{
    // Resolve current Scope server-side
    Guid scopeId;
    var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);

    if (!string.IsNullOrEmpty(userId))
    {
        var scope = await db.Scopes.FirstOrDefaultAsync(s =>
            s.Type == ScopeType.Owner && s.OwnerUserId == userId);
        if (scope is null)
            return Results.Json(new { error = "Owner Scope not found." }, statusCode: 400);
        scopeId = scope.Id;
    }
    else
    {
        var session = await guestTokenService.ResolveAsync(http);
        if (session is null)
            return Results.Json(new { error = "Guest session not found." }, statusCode: 401);
        scopeId = session.ScopeId;
    }

    UpdateAccountCommand? command;
    try
    {
        command = await http.Request.ReadFromJsonAsync<UpdateAccountCommand>();
    }
    catch (System.Text.Json.JsonException)
    {
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);
    }
    if (command is null)
        return Results.Json(new { error = "Invalid request body." }, statusCode: 400);

    command.ScopeId = scopeId;

    try
    {
        var account = await accountService.UpdateAccountAsync(id, command);
        return Results.Ok(new
        {
            accountId = account.Id,
            name = account.Name,
            type = account.Type,
            isArchived = account.IsArchived,
            createdAt = account.CreatedAt
        });
    }
    catch (ValidationException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: 422);
    }
})
.WithName("UpdateAccount");

app.Run();

// --- CLI Commands ---

static async Task<int> RunEnsureOwnerScopeAsync()
{
    var builder = WebApplication.CreateBuilder(args: []);

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

    await using var host = builder.Build();

    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;
    var db = services.GetRequiredService<ApplicationDbContext>();
    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();

    var userCount = await userManager.Users.CountAsync();
    if (userCount == 0)
    {
        Console.Error.WriteLine("Error: no Identity users found. Run --provision-owner first.");
        return 1;
    }
    if (userCount > 1)
    {
        Console.Error.WriteLine("Error: multiple Identity users found. Cannot determine Owner.");
        return 1;
    }

    var owner = await userManager.Users.FirstAsync();

    var existingScope = await db.Scopes.FirstOrDefaultAsync(s =>
        s.Type == ScopeType.Owner && s.OwnerUserId == owner.Id);

    if (existingScope is not null)
    {
        Console.WriteLine($"Owner Scope already exists (Id: {existingScope.Id}).");
        return 0;
    }

    var ownerScope = new Scope
    {
        Type = ScopeType.Owner,
        OwnerUserId = owner.Id
    };

    db.Scopes.Add(ownerScope);
    await db.SaveChangesAsync();

    Console.WriteLine($"Owner Scope created (Id: {ownerScope.Id}).");
    return 0;
}

static async Task<int> RunProvisioningAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

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

    await using var host = builder.Build();

    using var scope = host.Services.CreateScope();
    var services = scope.ServiceProvider;
    var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
    var db = services.GetRequiredService<ApplicationDbContext>();

    var emailIndex = Array.IndexOf(args, "--email");
    var email = emailIndex >= 0 && emailIndex + 1 < args.Length
        ? args[emailIndex + 1]
        : null;

    if (string.IsNullOrWhiteSpace(email))
    {
        Console.Write("Owner email: ");
        email = Console.ReadLine()?.Trim();
    }

    if (string.IsNullOrWhiteSpace(email))
    {
        Console.Error.WriteLine("Error: email is required.");
        return 1;
    }

    var existingUsers = await userManager.Users.CountAsync();
    if (existingUsers > 0)
    {
        Console.Error.WriteLine("Error: an Owner already exists. Provisioning is single-use only.");
        return 1;
    }

    Console.Write("Owner password: ");
    var password = ReadPassword();

    if (string.IsNullOrEmpty(password))
    {
        Console.Error.WriteLine("Error: password is required.");
        return 1;
    }

    var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
    var result = await userManager.CreateAsync(user, password);

    if (!result.Succeeded)
    {
        foreach (var error in result.Errors)
            Console.Error.WriteLine($"Error: {error.Description}");
        return 1;
    }

    var ownerScope = new Scope
    {
        Type = ScopeType.Owner,
        OwnerUserId = user.Id
    };
    db.Scopes.Add(ownerScope);
    await db.SaveChangesAsync();

    Console.WriteLine($"Owner created successfully (Id: {user.Id}).");
    Console.WriteLine($"Owner Scope created (Id: {ownerScope.Id}).");
    return 0;
}

static async Task<int> RunPasswordResetAsync()
{
    var builder = WebApplication.CreateBuilder(args: []);

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

    await using var host = builder.Build();

    using var scope = host.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

    var userCount = await userManager.Users.CountAsync();
    if (userCount == 0)
    {
        Console.Error.WriteLine("Error: no users found. Use --provision-owner first.");
        return 1;
    }
    if (userCount > 1)
    {
        Console.Error.WriteLine("Error: multiple users found. Cannot determine which account to reset.");
        return 1;
    }

    var owner = await userManager.Users.FirstAsync();

    Console.Write("New password: ");
    var newPassword = ReadPassword();

    if (string.IsNullOrEmpty(newPassword))
    {
        Console.Error.WriteLine("Error: password is required.");
        return 1;
    }

    var resetToken = await userManager.GeneratePasswordResetTokenAsync(owner);
    var result = await userManager.ResetPasswordAsync(owner, resetToken, newPassword);

    if (!result.Succeeded)
    {
        foreach (var error in result.Errors)
            Console.Error.WriteLine($"Error: {error.Description}");
        return 1;
    }

    Console.WriteLine("Owner password reset successfully.");
    return 0;
}

static string? ReadPassword()
{
    var password = new System.Text.StringBuilder();
    while (true)
    {
        var key = Console.ReadKey(intercept: true);
        if (key.Key == ConsoleKey.Enter)
        {
            Console.WriteLine();
            break;
        }
        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password.Remove(password.Length - 1, 1);
            Console.Write("\b \b");
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
            Console.Write('*');
        }
    }
    return password.ToString();
}

public class LoginRequest
{
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}
