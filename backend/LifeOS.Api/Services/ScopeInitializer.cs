using LifeOS.Api.Data;
using LifeOS.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Services;

public static class ScopeInitializer
{
    public static async Task EnsureDefaultAccountAsync(
        ApplicationDbContext db, Guid scopeId, CancellationToken ct = default)
    {
        var hasAccounts = await db.Accounts.AnyAsync(a => a.ScopeId == scopeId, ct);
        if (hasAccounts)
            return;

        db.Accounts.Add(new Account
        {
            ScopeId = scopeId,
            Name = "Tunai",
            Type = AccountType.Cash,
            IsArchived = false
        });
        await db.SaveChangesAsync(ct);
    }
}
