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
    }
}
