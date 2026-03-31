using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.Migrations
{
    /// <inheritdoc />
    public partial class AddAdditionalWorkerSubroles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdditionalSubRoles",
                table: "Users",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalSubRoles",
                table: "Users");
        }
    }
}
