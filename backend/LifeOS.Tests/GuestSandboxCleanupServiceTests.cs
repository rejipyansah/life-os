using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LifeOS.Tests;

public sealed class GuestSandboxCleanupServiceTests
{
    [Fact]
    public async Task Cleanup_deletes_guest_after_expiry_and_retention_but_preserves_owner_and_recent_guest()
    {
        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var now = DateTime.UtcNow;
        var expiredScope = new Scope { Type = ScopeType.Guest };
        var recentScope = new Scope { Type = ScopeType.Guest };
        var ownerScope = new Scope { Type = ScopeType.Owner };
        var guestAccount = new Account { Scope = expiredScope, Name = "Guest account", Type = AccountType.Bank };
        var ownerAccount = new Account { Scope = ownerScope, Name = "Owner account", Type = AccountType.Bank };
        var transaction = new Transaction
        {
            Scope = expiredScope,
            Type = TransactionType.Expense,
            Amount = 100m,
            OccurredOn = DateOnly.FromDateTime(now)
        };
        var setAside = new SetAside
        {
            Scope = expiredScope,
            Name = "Guest set-aside",
            CycleAnchorDate = DateOnly.FromDateTime(now)
        };
        var upcomingEvent = new UpcomingEvent
        {
            Scope = expiredScope,
            Account = guestAccount,
            RealizedTransaction = transaction,
            Title = "Guest event",
            Amount = 100m
        };
        db.Scopes.AddRange(expiredScope, recentScope, ownerScope);
        db.Accounts.AddRange(guestAccount, ownerAccount);
        db.Transactions.Add(transaction);
        db.TransactionEntries.Add(new TransactionEntry
        {
            Transaction = transaction,
            Account = guestAccount,
            Amount = -100m
        });
        db.SetAsides.Add(setAside);
        db.SetAsideEntries.Add(new SetAsideEntry
        {
            SetAside = setAside,
            Scope = expiredScope,
            Transaction = transaction,
            Type = SetAsideEntryType.Spent,
            Amount = -100m
        });
        db.UpcomingEvents.Add(upcomingEvent);
        db.GuestSessions.AddRange(
            new GuestSession
            {
                Scope = expiredScope,
                TokenHash = GuestTokenService.HashToken("expired-token"),
                LastActivityAt = now - GuestTokenService.InactivityExpiry - GuestSandboxCleanupService.RetentionAfterExpiry
            },
            new GuestSession
            {
                Scope = recentScope,
                TokenHash = GuestTokenService.HashToken("recent-token"),
                LastActivityAt = now - GuestTokenService.InactivityExpiry - GuestSandboxCleanupService.RetentionAfterExpiry + TimeSpan.FromTicks(1)
            });
        await db.SaveChangesAsync();

        var service = new GuestSandboxCleanupService(db, NullLogger<GuestSandboxCleanupService>.Instance);
        var deleted = await service.DeleteExpiredSandboxesAsync(now);

        Assert.Equal(1, deleted);
        Assert.False(await db.Scopes.AnyAsync(scope => scope.Id == expiredScope.Id));
        Assert.True(await db.Scopes.AnyAsync(scope => scope.Id == recentScope.Id));
        Assert.True(await db.Scopes.AnyAsync(scope => scope.Id == ownerScope.Id));
        Assert.False(await db.GuestSessions.AnyAsync(session => session.ScopeId == expiredScope.Id));
        Assert.False(await db.Accounts.AnyAsync(account => account.ScopeId == expiredScope.Id));
        Assert.False(await db.Transactions.AnyAsync(item => item.ScopeId == expiredScope.Id));
        Assert.False(await db.SetAsides.AnyAsync(item => item.ScopeId == expiredScope.Id));
        Assert.False(await db.UpcomingEvents.AnyAsync(item => item.ScopeId == expiredScope.Id));
        Assert.True(await db.Accounts.AnyAsync(account => account.Id == ownerAccount.Id));
    }
}
