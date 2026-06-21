using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ClashPlanner.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAccountMapsModifiedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MapsModifiedAt",
                table: "Villages",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MapsModifiedAt",
                table: "Villages");
        }
    }
}
