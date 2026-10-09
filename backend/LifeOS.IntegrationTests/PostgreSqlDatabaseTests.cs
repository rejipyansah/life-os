using LifeOS.Api.Data;
using LifeOS.Api.Models;
using LifeOS.Api.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LifeOS.IntegrationTests;

public sealed class PostgreSqlDatabaseTests
{
    [Fact]
    public async Task Migrations_apply_to_an_empty_postgres_schema_and_context_can_query_it()
    {
        var configuredConnection = Environment.GetEnvironmentVariable("LIFEOS_TEST_POSTGRES_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(configuredConnection),
            "Set LIFEOS_TEST_POSTGRES_CONNECTION to an isolated test database.");

        var schema = $"lifeos_test_{Guid.NewGuid():N}";
        var adminBuilder = new NpgsqlConnectionStringBuilder(configuredConnection);
        var schemaConnectionString = new NpgsqlConnectionStringBuilder(configuredConnection)
        {
            SearchPath = schema,
        };

        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var createSchema = admin.CreateCommand();
            createSchema.CommandText = $"CREATE SCHEMA \"{schema}\"";
            await createSchema.ExecuteNonQueryAsync();
        }

        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(schemaConnectionString.ConnectionString)
                .Options;

            await using var db = new ApplicationDbContext(options);
            await db.Database.MigrateAsync();

            var migrations = await db.Database.GetAppliedMigrationsAsync();
            Assert.NotEmpty(migrations);
            Assert.Equal(0, await db.Scopes.CountAsync());

            var scope = new Scope { Type = ScopeType.Guest };
            db.Scopes.Add(scope);
            var account = new Account
            {
                ScopeId = scope.Id,
                Name = "0",
                Type = AccountType.Bank,
            };
            db.Accounts.Add(account);
            await db.SaveChangesAsync();

            var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var attemptCount = 0;
            var firstAttempts = 0;
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(schemaConnectionString.ConnectionString);

            async Task IncrementAccountAsync()
            {
                await using var concurrentDb = new ApplicationDbContext(optionsBuilder.Options);
                await SerializableCommandRunner.RunAsync(concurrentDb, async ct =>
                {
                    var attempt = Interlocked.Increment(ref attemptCount);
                    var current = await concurrentDb.Accounts.SingleAsync(a => a.Id == account.Id, ct);
                    if (attempt <= 2)
                    {
                        if (Interlocked.Increment(ref firstAttempts) == 2)
                        {
                            bothRead.TrySetResult();
                        }
                        await bothRead.Task.WaitAsync(ct);
                    }

                    current.Name = (int.Parse(current.Name) + 1).ToString();
                    await concurrentDb.SaveChangesAsync(ct);
                });
            }

            // Both serializable transactions read the same value before either writes.
            // PostgreSQL must abort one writer; the command runner should retry it.
            await Task.WhenAll(IncrementAccountAsync(), IncrementAccountAsync());

            db.ChangeTracker.Clear();
            Assert.Equal("2", await db.Accounts.Where(a => a.Id == account.Id)
                .Select(a => a.Name).SingleAsync());
            Assert.True(attemptCount > 2, "A serialization conflict should cause a retry.");
        }
        finally
        {
            await using var admin = new NpgsqlConnection(adminBuilder.ConnectionString);
            await admin.OpenAsync();
            await using var dropSchema = admin.CreateCommand();
            dropSchema.CommandText = $"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE";
            await dropSchema.ExecuteNonQueryAsync();
        }
    }
}
