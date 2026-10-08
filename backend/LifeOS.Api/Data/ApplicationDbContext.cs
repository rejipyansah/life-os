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
    public DbSet<SetAside> SetAsides => Set<SetAside>();
    public DbSet<SetAsideEntry> SetAsideEntries => Set<SetAsideEntry>();
    public DbSet<UpcomingEvent> UpcomingEvents => Set<UpcomingEvent>();

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

        // SetAside configuration
        builder.Entity<SetAside>(e =>
        {
            e.HasKey(sa => sa.Id);

            e.Property(sa => sa.Name)
                .HasMaxLength(256);

            e.Property(sa => sa.Note)
                .HasMaxLength(512);

            e.Property(sa => sa.Kind)
                .HasConversion<string>()
                .HasMaxLength(32);

            e.Property(sa => sa.Status)
                .HasConversion<string>()
                .HasMaxLength(16);

            e.Property(sa => sa.CloseReason)
                .HasConversion<string>()
                .HasMaxLength(16);

            e.Property(sa => sa.CycleKind)
                .HasConversion<string>()
                .HasMaxLength(16);

            e.Property(sa => sa.TargetAmount)
                .HasColumnType("decimal(18,2)");

            // FK to Scope
            e.HasOne(sa => sa.Scope)
                .WithMany()
                .HasForeignKey(sa => sa.ScopeId)
                .OnDelete(DeleteBehavior.Restrict);

            // FK to Account (legacy binding — never used in calculations)
            e.HasOne(sa => sa.Account)
                .WithMany()
                .HasForeignKey(sa => sa.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // Hint non-binding: sumber dana default untuk proses manual.
            // Bukan kepemilikan; hanya pre-select UI.
            e.HasOne(sa => sa.DefaultSourceAccount)
                .WithMany()
                .HasForeignKey(sa => sa.DefaultSourceAccountId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(sa => sa.ScopeId);
            e.HasIndex(sa => sa.AccountId);
            e.HasIndex(sa => sa.DefaultSourceAccountId);
            e.HasIndex(sa => new { sa.ScopeId, sa.Status });
        });

        // SetAsideEntry configuration
        builder.Entity<SetAsideEntry>(e =>
        {
            e.HasKey(se => se.Id);

            e.Property(se => se.Amount)
                .HasColumnType("decimal(18,2)");

            e.Property(se => se.Type)
                .HasConversion<string>()
                .HasMaxLength(32);

            e.Property(se => se.Note)
                .HasMaxLength(512);

            e.HasOne(se => se.SetAside)
                .WithMany()
                .HasForeignKey(se => se.SetAsideId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(se => se.Scope)
                .WithMany()
                .HasForeignKey(se => se.ScopeId)
                .OnDelete(DeleteBehavior.Restrict);

            // The linked realized Transaction must survive SetAsideEntry removal.
            e.HasOne(se => se.Transaction)
                .WithMany()
                .HasForeignKey(se => se.TransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(se => se.SetAsideId);
            e.HasIndex(se => se.ScopeId);
            e.HasIndex(se => se.TransactionId);
        });

        // UpcomingEvent configuration
        builder.Entity<UpcomingEvent>(e =>
        {
            e.HasKey(ue => ue.Id);

            e.Property(ue => ue.Title)
                .HasMaxLength(256);

            e.Property(ue => ue.CategoryName)
                .HasMaxLength(128);

            e.Property(ue => ue.Note)
                .HasMaxLength(512);

            e.Property(ue => ue.StatusReason)
                .HasMaxLength(512);

            e.Property(ue => ue.Amount)
                .HasColumnType("decimal(18,2)");

            e.Property(ue => ue.Direction)
                .HasConversion<string>()
                .HasMaxLength(16);

            e.Property(ue => ue.Status)
                .HasConversion<string>()
                .HasMaxLength(16);

            e.Property(ue => ue.ScheduleKind)
                .HasConversion<string>()
                .HasMaxLength(16);

            e.Property(ue => ue.Recurrence)
                .HasConversion<string>()
                .HasMaxLength(16);

            e.HasOne(ue => ue.Scope)
                .WithMany()
                .HasForeignKey(ue => ue.ScopeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(ue => ue.Account)
                .WithMany()
                .HasForeignKey(ue => ue.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(ue => ue.RealizedTransaction)
                .WithMany()
                .HasForeignKey(ue => ue.RealizedTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(ue => ue.ScopeId);
            e.HasIndex(ue => ue.AccountId);
            e.HasIndex(ue => new { ue.ScopeId, ue.Status });
            e.HasIndex(ue => ue.RealizedTransactionId);
        });
    }
}
