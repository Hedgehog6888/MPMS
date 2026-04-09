using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.Migrations
{
    /// <inheritdoc />
    public partial class LocalEquipmentConditionAndStatusRefactor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Condition",
                table: "Equipments",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "Good");

            migrationBuilder.Sql("""
                UPDATE "Equipments"
                SET "Status" = CASE
                    WHEN "Status" = 'CheckedOut' THEN 'InUse'
                    WHEN "Status" = 'InMaintenance' THEN 'Available'
                    ELSE "Status"
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Condition",
                table: "Equipments");
        }
    }
}
