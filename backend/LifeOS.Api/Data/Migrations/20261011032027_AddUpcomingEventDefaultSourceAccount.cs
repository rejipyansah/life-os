using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUpcomingEventDefaultSourceAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultSourceAccountId",
                table: "UpcomingEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UpcomingEvents_DefaultSourceAccountId",
                table: "UpcomingEvents",
                column: "DefaultSourceAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_UpcomingEvents_Accounts_DefaultSourceAccountId",
                table: "UpcomingEvents",
                column: "DefaultSourceAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UpcomingEvents_Accounts_DefaultSourceAccountId",
                table: "UpcomingEvents");

            migrationBuilder.DropIndex(
                name: "IX_UpcomingEvents_DefaultSourceAccountId",
                table: "UpcomingEvents");

            migrationBuilder.DropColumn(
                name: "DefaultSourceAccountId",
                table: "UpcomingEvents");
        }
    }
}
