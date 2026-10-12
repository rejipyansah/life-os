using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public static class OwnerAdministrationCommands
{
    public static async Task<int> EnsureOwnerScopeAsync()
    {
        var builder = WebApplication.CreateBuilder(args: []);
        AddIdentityAndDatabase(builder.Services, builder.Configuration);

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
        var existingScope = await db.Scopes.FirstOrDefaultAsync(scope =>
            scope.Type == ScopeType.Owner && scope.OwnerUserId == owner.Id);
        if (existingScope is not null)
        {
            Console.WriteLine($"Owner Scope already exists (Id: {existingScope.Id}).");
            return 0;
        }

        var ownerScope = new Scope { Type = ScopeType.Owner, OwnerUserId = owner.Id };
        db.Scopes.Add(ownerScope);
        await db.SaveChangesAsync();
        await ScopeInitializer.EnsureDefaultAccountAsync(db, ownerScope.Id);
        Console.WriteLine($"Owner Scope created (Id: {ownerScope.Id}).");
        return 0;
    }

    public static async Task<int> ProvisionOwnerAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        AddIdentityAndDatabase(builder.Services, builder.Configuration);

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

        if (await userManager.Users.CountAsync() > 0)
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

        var user = new IdentityUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true
        };
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                Console.Error.WriteLine($"Error: {error.Description}");
            return 1;
        }

        var ownerScope = new Scope { Type = ScopeType.Owner, OwnerUserId = user.Id };
        db.Scopes.Add(ownerScope);
        await db.SaveChangesAsync();
        await ScopeInitializer.EnsureDefaultAccountAsync(db, ownerScope.Id);
        Console.WriteLine($"Owner created successfully (Id: {user.Id}).");
        Console.WriteLine($"Owner Scope created (Id: {ownerScope.Id}).");
        return 0;
    }

    public static async Task<int> ResetOwnerPasswordAsync()
    {
        var builder = WebApplication.CreateBuilder(args: []);
        AddIdentityAndDatabase(builder.Services, builder.Configuration);

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

    private static void AddIdentityAndDatabase(IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        services.AddIdentity<IdentityUser, IdentityRole>(options =>
        {
            options.Password.RequireDigit = false;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireLowercase = false;
        })
        .AddEntityFrameworkStores<ApplicationDbContext>()
        .AddDefaultTokenProviders();
    }

    private static string? ReadPassword()
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
}
