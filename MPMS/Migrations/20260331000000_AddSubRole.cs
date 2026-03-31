using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.Migrations
{
    /// <inheritdoc />
    public partial class AddSubRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubRole",
                table: "Users",
                type: "TEXT",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubRole",
                table: "Users");
        }
    }
}
