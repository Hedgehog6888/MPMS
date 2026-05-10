using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MPMS.API.Data;

#nullable disable

namespace MPMS.API.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260510121000_AddSyncedActivityDetailsText")]
    public partial class AddSyncedActivityDetailsText : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailsText",
                table: "SyncedActivityLogs",
                type: "nvarchar(max)",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DetailsText",
                table: "SyncedActivityLogs");
        }
    }
}
