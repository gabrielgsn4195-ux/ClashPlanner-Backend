using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClashPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDeletions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Deletions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ModifiedAt = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deletions", x => new { x.UserId, x.Kind, x.EntityId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Deletions_UserId",
                table: "Deletions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deletions");
        }
    }
}
