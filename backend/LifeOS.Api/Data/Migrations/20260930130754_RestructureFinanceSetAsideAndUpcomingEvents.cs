using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Api.Data.Migrations
{
    /// <summary>
    /// Restructure Finance: Allocation → SetAside + SetAsideEntry, and add UpcomingEvent.
    ///
    /// MIGRATION STRATEGY (OLD → NEW):
    ///
    ///   Allocations (table)                 → SetAsides              : renamed, no data loss
    ///   Allocation.Id/ScopeId/AccountId/    → SetAside.*             : preserved as-is
    ///     Name/CreatedAt
    ///   Allocation.Amount                   → SetAsideEntry 'Opened' : moved into append-only
    ///                                       + 'Closed' for inactive     history, then dropped
    ///   Allocation.Status = 0 (Active)      → SetAsideStatus.Active
    ///   Allocation.Status = 1 (Completed)   → SetAsideStatus.Closed + CloseReason 'Spent'
    ///   Allocation.Status = 2 (Cancelled)   → SetAsideStatus.Closed + CloseReason 'Cancelled'
    ///
    ///   IMPORTANT: legacy "Completed" is NOT turned into an Expense here. The old
    ///   CompleteAllocationAsync already wrote the Expense transaction and that history
    ///   stays untouched. This migration only records how the reservation ended.
    ///
    ///   SetAside.Kind                       → NULL for legacy rows.
    ///     Legacy allocations had no kind. Leaving it NULL is the honest representation
    ///     of "created before this concept existed" — no value is invented. New rows
    ///     always carry a Kind.
    ///   SetAside.TargetAmount               → NULL (legacy had no target)
    ///   SetAside.CycleKind                  → 'None' (legacy had no cycle concept)
    ///   SetAside.CycleAnchorDate            → CreatedAt::date (unused while CycleKind = None)
    ///   SetAside.UpdatedAt                  → CreatedAt (closure timestamps were never stored)
    ///   SetAside.Note                       → NULL
    ///
    ///   UpcomingEvent / SetAsideEntry       : new tables, empty on migrate.
    /// </summary>
    public partial class RestructureFinanceSetAsideAndUpcomingEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Preserve legacy data: rename instead of drop/create ──
            migrationBuilder.RenameTable(
                name: "Allocations",
                newName: "SetAsides");

            migrationBuilder.RenameIndex(
                name: "IX_Allocations_AccountId",
                table: "SetAsides",
                newName: "IX_SetAsides_AccountId");

            migrationBuilder.RenameIndex(
                name: "IX_Allocations_ScopeId",
                table: "SetAsides",
                newName: "IX_SetAsides_ScopeId");

            migrationBuilder.Sql("ALTER TABLE \"SetAsides\" RENAME CONSTRAINT \"PK_Allocations\" TO \"PK_SetAsides\";");
            migrationBuilder.Sql("ALTER TABLE \"SetAsides\" RENAME CONSTRAINT \"FK_Allocations_Accounts_AccountId\" TO \"FK_SetAsides_Accounts_AccountId\";");
            migrationBuilder.Sql("ALTER TABLE \"SetAsides\" RENAME CONSTRAINT \"FK_Allocations_Scopes_ScopeId\" TO \"FK_SetAsides_Scopes_ScopeId\";");

            // ── 2. New SetAside columns ──
            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "SetAsides",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "SetAsides",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TargetAmount",
                table: "SetAsides",
                type: "numeric(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CycleKind",
                table: "SetAsides",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateOnly>(
                name: "CycleAnchorDate",
                table: "SetAsides",
                type: "date",
                nullable: false,
                defaultValue: new DateOnly(1, 1, 1));

            migrationBuilder.AddColumn<string>(
                name: "CloseReason",
                table: "SetAsides",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "SetAsides",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // ── 3. Backfill CloseReason while Status is still the legacy integer ──
            migrationBuilder.Sql("""
                UPDATE "SetAsides"
                SET "CloseReason" = CASE "Status"
                    WHEN 1 THEN 'Spent'
                    WHEN 2 THEN 'Cancelled'
                    ELSE NULL
                END;
                """);

            migrationBuilder.Sql("""
                UPDATE "SetAsides"
                SET "CycleAnchorDate" = "CreatedAt"::date,
                    "UpdatedAt" = "CreatedAt";
                """);

            // ── 4. Status: integer enum → string enum, same semantic mapping ──
            migrationBuilder.Sql("""
                ALTER TABLE "SetAsides"
                    ALTER COLUMN "Status" TYPE character varying(16)
                    USING (CASE "Status"
                        WHEN 0 THEN 'Active'
                        WHEN 1 THEN 'Closed'
                        WHEN 2 THEN 'Closed'
                        ELSE 'Closed'
                    END);
                ALTER TABLE "SetAsides" ALTER COLUMN "Status" SET NOT NULL;
                """);

            // ── 5. History + upcoming event tables ──
            migrationBuilder.CreateTable(
                name: "UpcomingEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CategoryName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ScheduleKind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Recurrence = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    RealizedTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StatusReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UpcomingEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UpcomingEvents_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UpcomingEvents_Scopes_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "Scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UpcomingEvents_Transactions_RealizedTransactionId",
                        column: x => x.RealizedTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SetAsideEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SetAsideId = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Note = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetAsideEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SetAsideEntries_Scopes_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "Scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SetAsideEntries_SetAsides_SetAsideId",
                        column: x => x.SetAsideId,
                        principalTable: "SetAsides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SetAsideEntries_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── 6. Move the legacy Amount into append-only history ──
            // Every legacy allocation becomes one 'Opened' entry. Rows that were already
            // inactive also get a compensating 'Closed' entry so the derived amount is 0
            // and SUM(entries) stays consistent with the stored state.
            migrationBuilder.Sql("""
                INSERT INTO "SetAsideEntries" ("Id", "SetAsideId", "ScopeId", "Type", "Amount", "TransactionId", "Note", "CreatedAt")
                SELECT gen_random_uuid(), sa."Id", sa."ScopeId", 'Opened', sa."Amount", NULL,
                       'Migrated from legacy allocation', sa."CreatedAt"
                FROM "SetAsides" sa;
                """);

            migrationBuilder.Sql("""
                INSERT INTO "SetAsideEntries" ("Id", "SetAsideId", "ScopeId", "Type", "Amount", "TransactionId", "Note", "CreatedAt")
                SELECT gen_random_uuid(), sa."Id", sa."ScopeId", 'Closed', -sa."Amount", NULL,
                       'Migrated from legacy allocation (already closed)', sa."CreatedAt"
                FROM "SetAsides" sa
                WHERE sa."Status" = 'Closed';
                """);

            migrationBuilder.DropColumn(
                name: "Amount",
                table: "SetAsides");

            // Drop the migration-only defaults so the database matches the EF model.
            migrationBuilder.Sql("ALTER TABLE \"SetAsides\" ALTER COLUMN \"CycleKind\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"SetAsides\" ALTER COLUMN \"CycleAnchorDate\" DROP DEFAULT;");
            migrationBuilder.Sql("ALTER TABLE \"SetAsides\" ALTER COLUMN \"UpdatedAt\" DROP DEFAULT;");

            // ── 7. Indexes ──
            migrationBuilder.CreateIndex(
                name: "IX_SetAsideEntries_ScopeId",
                table: "SetAsideEntries",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_SetAsideEntries_SetAsideId",
                table: "SetAsideEntries",
                column: "SetAsideId");

            migrationBuilder.CreateIndex(
                name: "IX_SetAsideEntries_TransactionId",
                table: "SetAsideEntries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_SetAsides_ScopeId_Status",
                table: "SetAsides",
                columns: new[] { "ScopeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UpcomingEvents_AccountId",
                table: "UpcomingEvents",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_UpcomingEvents_RealizedTransactionId",
                table: "UpcomingEvents",
                column: "RealizedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_UpcomingEvents_ScopeId",
                table: "UpcomingEvents",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_UpcomingEvents_ScopeId_Status",
                table: "UpcomingEvents",
                columns: new[] { "ScopeId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Best-effort reversal: the legacy table is rebuilt from SetAsides plus the
            // derived history amount. The Kind/Note/Target/Cycle columns have no legacy
            // counterpart and are intentionally discarded.
            migrationBuilder.AddColumn<decimal>(
                name: "Amount",
                table: "SetAsides",
                type: "numeric(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "SetAsides" sa
                SET "Amount" = COALESCE((
                    SELECT SUM(se."Amount") FROM "SetAsideEntries" se WHERE se."SetAsideId" = sa."Id"
                ), 0);
                """);

            // Status: string enum → legacy integer enum, using CloseReason to distinguish
            // the two legacy terminal states.
            migrationBuilder.Sql("""
                ALTER TABLE "SetAsides"
                    ALTER COLUMN "Status" TYPE integer
                    USING (CASE
                        WHEN "Status" = 'Active' THEN 0
                        WHEN "CloseReason" = 'Spent' THEN 1
                        ELSE 2
                    END);
                ALTER TABLE "SetAsides" ALTER COLUMN "Status" SET NOT NULL;
                """);

            migrationBuilder.DropTable(
                name: "SetAsideEntries");

            migrationBuilder.DropTable(
                name: "UpcomingEvents");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "SetAsides");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "SetAsides");

            migrationBuilder.DropColumn(
                name: "TargetAmount",
                table: "SetAsides");

            migrationBuilder.DropColumn(
                name: "CycleKind",
                table: "SetAsides");

            migrationBuilder.DropColumn(
                name: "CycleAnchorDate",
                table: "SetAsides");

            migrationBuilder.DropColumn(
                name: "CloseReason",
                table: "SetAsides");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "SetAsides");

            migrationBuilder.DropIndex(
                name: "IX_SetAsides_ScopeId_Status",
                table: "SetAsides");

            migrationBuilder.RenameTable(
                name: "SetAsides",
                newName: "Allocations");

            migrationBuilder.RenameIndex(
                name: "IX_SetAsides_AccountId",
                table: "Allocations",
                newName: "IX_Allocations_AccountId");

            migrationBuilder.RenameIndex(
                name: "IX_SetAsides_ScopeId",
                table: "Allocations",
                newName: "IX_Allocations_ScopeId");

            migrationBuilder.Sql("ALTER TABLE \"Allocations\" RENAME CONSTRAINT \"PK_SetAsides\" TO \"PK_Allocations\";");
            migrationBuilder.Sql("ALTER TABLE \"Allocations\" RENAME CONSTRAINT \"FK_SetAsides_Accounts_AccountId\" TO \"FK_Allocations_Accounts_AccountId\";");
            migrationBuilder.Sql("ALTER TABLE \"Allocations\" RENAME CONSTRAINT \"FK_SetAsides_Scopes_ScopeId\" TO \"FK_Allocations_Scopes_ScopeId\";");
        }
    }
}
