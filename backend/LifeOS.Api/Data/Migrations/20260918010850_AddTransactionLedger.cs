using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    Description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CategoryName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RelatedTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FeeAmount = table.Column<decimal>(type: "numeric(18,2)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Scopes_ScopeId",
                        column: x => x.ScopeId,
                        principalTable: "Scopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Transactions_Transactions_RelatedTransactionId",
                        column: x => x.RelatedTransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TransactionEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionEntries_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionEntries_Transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "Transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionEntries_AccountId",
                table: "TransactionEntries",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionEntries_TransactionId",
                table: "TransactionEntries",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_RelatedTransactionId",
                table: "Transactions",
                column: "RelatedTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ScopeId",
                table: "Transactions",
                column: "ScopeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TransactionEntries");

            migrationBuilder.DropTable(
                name: "Transactions");
        }
    }
}
