using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSetAsideDefaultSourceAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DefaultSourceAccountId",
                table: "SetAsides",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SetAsides_DefaultSourceAccountId",
                table: "SetAsides",
                column: "DefaultSourceAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_SetAsides_Accounts_DefaultSourceAccountId",
                table: "SetAsides",
                column: "DefaultSourceAccountId",
                principalTable: "Accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SetAsides_Accounts_DefaultSourceAccountId",
                table: "SetAsides");

            migrationBuilder.DropIndex(
                name: "IX_SetAsides_DefaultSourceAccountId",
                table: "SetAsides");

            migrationBuilder.DropColumn(
                name: "DefaultSourceAccountId",
                table: "SetAsides");
        }
    }
}
