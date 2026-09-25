using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LifeOS.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAllocationStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Status column first
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Allocations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Migrate data: IsActive=true → Active(0), IsActive=false → Completed(1)
            migrationBuilder.Sql(
                """
                UPDATE "Allocations"
                SET "Status" = CASE WHEN "IsActive" = true THEN 0 ELSE 1 END
                """);

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Allocations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Allocations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql(
                """
                UPDATE "Allocations"
                SET "IsActive" = CASE WHEN "Status" = 0 THEN true ELSE false END
                """);

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Allocations");
        }
    }
}
