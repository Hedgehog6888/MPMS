using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.API.Migrations
{
    /// <inheritdoc />
    public partial class MaterialsInventoryEquipment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "Materials",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "Materials",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Quantity",
                table: "Materials",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Materials",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.CreateTable(
                name: "EquipmentCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaterialStockMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    MaterialId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Delta = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    QuantityAfter = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    OperationType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaterialStockMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MaterialStockMovements_Materials_MaterialId",
                        column: x => x.MaterialId,
                        principalTable: "Materials",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MaterialStockMovements_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MaterialStockMovements_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_MaterialStockMovements_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Equipments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ImagePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    InventoryNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CheckedOutProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CheckedOutTaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Equipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Equipments_EquipmentCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "EquipmentCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Equipments_Projects_CheckedOutProjectId",
                        column: x => x.CheckedOutProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Equipments_Tasks_CheckedOutTaskId",
                        column: x => x.CheckedOutTaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EquipmentHistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    EquipmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    EventType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EquipmentHistoryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EquipmentHistoryEntries_Equipments_EquipmentId",
                        column: x => x.EquipmentId,
                        principalTable: "Equipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EquipmentHistoryEntries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentHistoryEntries_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_EquipmentHistoryEntries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Materials_CategoryId",
                table: "Materials",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentCategories_Name",
                table: "EquipmentCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHistoryEntries_EquipmentId",
                table: "EquipmentHistoryEntries",
                column: "EquipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHistoryEntries_ProjectId",
                table: "EquipmentHistoryEntries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHistoryEntries_TaskId",
                table: "EquipmentHistoryEntries",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_EquipmentHistoryEntries_UserId",
                table: "EquipmentHistoryEntries",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_CategoryId",
                table: "Equipments",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_CheckedOutProjectId",
                table: "Equipments",
                column: "CheckedOutProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Equipments_CheckedOutTaskId",
                table: "Equipments",
                column: "CheckedOutTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialCategories_Name",
                table: "MaterialCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MaterialStockMovements_MaterialId",
                table: "MaterialStockMovements",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialStockMovements_ProjectId",
                table: "MaterialStockMovements",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialStockMovements_TaskId",
                table: "MaterialStockMovements",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_MaterialStockMovements_UserId",
                table: "MaterialStockMovements",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Materials_MaterialCategories_CategoryId",
                table: "Materials",
                column: "CategoryId",
                principalTable: "MaterialCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("UPDATE [Materials] SET [UpdatedAt] = [CreatedAt];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Materials_MaterialCategories_CategoryId",
                table: "Materials");

            migrationBuilder.DropTable(
                name: "EquipmentHistoryEntries");

            migrationBuilder.DropTable(
                name: "MaterialCategories");

            migrationBuilder.DropTable(
                name: "MaterialStockMovements");

            migrationBuilder.DropTable(
                name: "Equipments");

            migrationBuilder.DropTable(
                name: "EquipmentCategories");

            migrationBuilder.DropIndex(
                name: "IX_Materials_CategoryId",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "CategoryId",
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
