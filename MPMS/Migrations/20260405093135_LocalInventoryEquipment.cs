using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.Migrations
{
    /// <inheritdoc />
    public partial class LocalInventoryEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Materials",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "Materials",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "Materials",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "Materials",
                type: "TEXT",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Materials",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.Sql("""
                UPDATE "Materials" SET "UpdatedAt" = "CreatedAt"
                WHERE "UpdatedAt" = '0001-01-01 00:00:00' OR "UpdatedAt" IS NULL
                """);

            migrationBuilder.CreateTable(
                name: "EquipmentCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EquipmentHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EquipmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EventType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PreviousStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    NewStatus = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentHistoryEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Equipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CategoryName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ImagePath = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    InventoryNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CheckedOutProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CheckedOutTaskId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsSynced = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastModifiedLocally = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialStockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MaterialId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Delta = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    QuantityAfter = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    OperationType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Comment = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProjectId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TaskId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialStockMovements", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EquipmentCategories");

            migrationBuilder.DropTable(
                name: "EquipmentHistoryEntries");

            migrationBuilder.DropTable(
                name: "Equipments");

            migrationBuilder.DropTable(
                name: "MaterialCategories");

            migrationBuilder.DropTable(
                name: "MaterialStockMovements");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Materials");
        }
    }
}
