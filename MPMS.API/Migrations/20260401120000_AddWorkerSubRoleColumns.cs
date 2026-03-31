using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerSubRoleColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SubRole",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdditionalSubRoles",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubRole",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AdditionalSubRoles",
                table: "Users");
        }
    }
}
