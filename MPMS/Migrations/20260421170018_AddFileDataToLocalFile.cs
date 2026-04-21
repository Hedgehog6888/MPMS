using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.Migrations
{
    /// <inheritdoc />
    public partial class AddFileDataToLocalFile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Materials",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "FileData",
                table: "Files",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Equipments",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SessionPasswordProtected",
                table: "AuthSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StageEquipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    StageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EquipmentName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InventoryNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsSynced = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastModifiedLocally = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageEquipments", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageEquipments");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "FileData",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "SessionPasswordProtected",
                table: "AuthSessions");
        }
    }
}
