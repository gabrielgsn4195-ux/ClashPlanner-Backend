using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClashPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class RenameAccountsToVillages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Accounts",
                table: "Accounts");

            migrationBuilder.RenameTable(
                name: "Accounts",
                newName: "Villages");

            migrationBuilder.RenameIndex(
                name: "IX_Accounts_UserId",
                table: "Villages",
                newName: "IX_Villages_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Villages",
                table: "Villages",
                columns: new[] { "UserId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Villages",
                table: "Villages");

            migrationBuilder.RenameTable(
                name: "Villages",
                newName: "Accounts");

            migrationBuilder.RenameIndex(
                name: "IX_Villages_UserId",
                table: "Accounts",
                newName: "IX_Accounts_UserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Accounts",
                table: "Accounts",
                columns: new[] { "UserId", "Id" });
        }
    }
}
