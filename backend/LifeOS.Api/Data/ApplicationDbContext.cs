using LifeOS.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LifeOS.Api.Data;

public class ApplicationDbContext : IdentityDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Scope> Scopes => Set<Scope>();
    public DbSet<GuestSession> GuestSessions => Set<GuestSession>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionEntry> TransactionEntries => Set<TransactionEntry>();
    public DbSet<Allocation> Allocations => Set<Allocation>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Scope configuration
        builder.Entity<Scope>(e =>
        {
            e.HasKey(s => s.Id);

            e.Property(s => s.Type)
                .HasConversion<string>()
                .HasMaxLength(16);

            // One Owner scope per user: unique index on OwnerUserId for Owner scopes
            e.HasIndex(s => s.OwnerUserId)
                .IsUnique()
                .HasFilter("\"Type\" = 'Owner'");

            e.HasOne(s => s.OwnerUser)
                .WithMany()
                .HasForeignKey(s => s.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // GuestSession configuration
        builder.Entity<GuestSession>(e =>
        {
            e.HasKey(gs => gs.Id);

            e.Property(gs => gs.TokenHash)
                .HasMaxLength(64);

            e.HasIndex(gs => gs.TokenHash)
                .IsUnique();

            // One-to-one: GuestSession <-> Guest Scope
            e.HasIndex(gs => gs.ScopeId)
                .IsUnique();

            e.HasOne(gs => gs.Scope)
                .WithMany()
                .HasForeignKey(gs => gs.ScopeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Account configuration
        builder.Entity<Account>(e =>
        {
            e.HasKey(a => a.Id);

            e.Property(a => a.Name)
                .HasMaxLength(256);

            e.Property(a => a.Type)
                .HasConversion<string>()
                .HasMaxLength(16);

            // FK to Scope
            e.HasOne(a => a.Scope)
                .WithMany()
                .HasForeignKey(a => a.ScopeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Index for querying accounts within a scope
            e.HasIndex(a => a.ScopeId);
        });

        // Transaction configuration
        builder.Entity<Transaction>(e =>
        {
            e.HasKey(t => t.Id);

            e.Property(t => t.Type)
                .HasConversion<string>()
                .HasMaxLength(16);

            e.Property(t => t.Amount)
                .HasColumnType("decimal(18,2)");

            e.Property(t => t.FeeAmount)
                .HasColumnType("decimal(18,2)");

            e.Property(t => t.Description)
                .HasMaxLength(512);

            e.Property(t => t.CategoryName)
                .HasMaxLength(128);

            // FK to Scope
            e.HasOne(t => t.Scope)
                .WithMany()
                .HasForeignKey(t => t.ScopeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Self-reference for RelatedTransaction
            e.HasOne(t => t.RelatedTransaction)
                .WithMany()
                .HasForeignKey(t => t.RelatedTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            e.HasIndex(t => t.ScopeId);
        });

        // TransactionEntry configuration
        builder.Entity<TransactionEntry>(e =>
        {
            e.HasKey(te => te.Id);

            e.Property(te => te.Amount)
                .HasColumnType("decimal(18,2)");

            // FK to Transaction
            e.HasOne(te => te.Transaction)
                .WithMany()
                .HasForeignKey(te => te.TransactionId)
                .OnDelete(DeleteBehavior.Cascade);

            // FK to Account
            e.HasOne(te => te.Account)
                .WithMany()
                .HasForeignKey(te => te.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            e.HasIndex(te => te.TransactionId);
            e.HasIndex(te => te.AccountId);
        });

        // Allocation configuration
        builder.Entity<Allocation>(e =>
        {
            e.HasKey(a => a.Id);

            e.Property(a => a.Name)
                .HasMaxLength(256);

            e.Property(a => a.Amount)
                .HasColumnType("decimal(18,2)");

            // FK to Scope
            e.HasOne(a => a.Scope)
                .WithMany()
                .HasForeignKey(a => a.ScopeId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK to Account
            e.HasOne(a => a.Account)
                .WithMany()
                .HasForeignKey(a => a.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Indexes
            e.HasIndex(a => a.ScopeId);
            e.HasIndex(a => a.AccountId);
        });
    }
}
