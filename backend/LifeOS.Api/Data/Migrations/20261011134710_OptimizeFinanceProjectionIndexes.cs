using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeFinanceProjectionIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_ScopeId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_SetAsideEntries_SetAsideId",
                table: "SetAsideEntries");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ScopeId_Type_RelatedTransactionId",
                table: "Transactions",
                columns: new[] { "ScopeId", "Type", "RelatedTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SetAsideEntries_SetAsideId_Type_CreatedAt",
                table: "SetAsideEntries",
                columns: new[] { "SetAsideId", "Type", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Transactions_ScopeId_Type_RelatedTransactionId",
                table: "Transactions");

            migrationBuilder.DropIndex(
                name: "IX_SetAsideEntries_SetAsideId_Type_CreatedAt",
                table: "SetAsideEntries");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ScopeId",
                table: "Transactions",
                column: "ScopeId");

            migrationBuilder.CreateIndex(
                name: "IX_SetAsideEntries_SetAsideId",
                table: "SetAsideEntries",
                column: "SetAsideId");
        }
    }
}
