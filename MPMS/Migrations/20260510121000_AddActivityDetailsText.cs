using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MPMS.Data;

#nullable disable

namespace MPMS.Migrations
{
    [DbContext(typeof(LocalDbContext))]
    [Migration("20260510121000_AddActivityDetailsText")]
    public partial class AddActivityDetailsText : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailsText",
                table: "ActivityLogs",
                type: "TEXT",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailsText",
                table: "ActivityLogs");
        }
    }
}
