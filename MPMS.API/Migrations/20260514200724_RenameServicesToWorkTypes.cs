using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.API.Migrations
{
    /// <inheritdoc />
    public partial class RenameServicesToWorkTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskStages_ServiceTemplates_ServiceTemplateId",
                table: "TaskStages");

            migrationBuilder.DropTable(
                name: "StageServices");

            migrationBuilder.DropTable(
                name: "ServiceTemplates");

            migrationBuilder.DropTable(
                name: "ServiceCategories");

            migrationBuilder.RenameColumn(
                name: "ServiceTemplateId",
                table: "TaskStages",
                newName: "WorkTypeTemplateId");

            migrationBuilder.RenameColumn(
                name: "ServiceNameSnapshot",
                table: "TaskStages",
                newName: "WorkTypeNameSnapshot");

            migrationBuilder.RenameColumn(
                name: "ServiceDescriptionSnapshot",
                table: "TaskStages",
                newName: "WorkTypeDescriptionSnapshot");

            migrationBuilder.RenameIndex(
                name: "IX_TaskStages_ServiceTemplateId",
                table: "TaskStages",
                newName: "IX_TaskStages_WorkTypeTemplateId");

            migrationBuilder.CreateTable(
                name: "WorkTypeCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTypeCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkTypeTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Article = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkTypeTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkTypeTemplates_WorkTypeCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "WorkTypeCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageWorkTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkTypeTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkTypeNameSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkTypeDescriptionSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UnitSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PricePerUnit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageWorkTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageWorkTypes_TaskStages_StageId",
                        column: x => x.StageId,
                        principalTable: "TaskStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageWorkTypes_WorkTypeTemplates_WorkTypeTemplateId",
                        column: x => x.WorkTypeTemplateId,
                        principalTable: "WorkTypeTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StageWorkTypes_StageId",
                table: "StageWorkTypes",
                column: "StageId");

            migrationBuilder.CreateIndex(
                name: "IX_StageWorkTypes_WorkTypeTemplateId",
                table: "StageWorkTypes",
                column: "WorkTypeTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTypeCategories_Name",
                table: "WorkTypeCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkTypeTemplates_Article",
                table: "WorkTypeTemplates",
                column: "Article",
                unique: true,
                filter: "[Article] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTypeTemplates_CategoryId",
                table: "WorkTypeTemplates",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkTypeTemplates_Name",
                table: "WorkTypeTemplates",
                column: "Name");

            // Normalize существующие данные в TaskStages перед добавлением FK:
            // 1) Обнулить ссылки, указывающие на несуществующие шаблоны работ
            // 2) Обнулить нулевой GUID, если такой встретится
            migrationBuilder.Sql(@"
UPDATE s SET WorkTypeTemplateId = NULL
FROM [dbo].[TaskStages] s
LEFT JOIN [dbo].[WorkTypeTemplates] w ON w.[Id] = s.[WorkTypeTemplateId]
WHERE (s.[WorkTypeTemplateId] IS NOT NULL AND w.[Id] IS NULL)
   OR s.[WorkTypeTemplateId] = '00000000-0000-0000-0000-000000000000';

-- Дополнительно убедимся, что индексы корректны перед FK (без действий, если всё в порядке)
");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskStages_WorkTypeTemplates_WorkTypeTemplateId",
                table: "TaskStages",
                column: "WorkTypeTemplateId",
                principalTable: "WorkTypeTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskStages_WorkTypeTemplates_WorkTypeTemplateId",
                table: "TaskStages");

            migrationBuilder.DropTable(
                name: "StageWorkTypes");

            migrationBuilder.DropTable(
                name: "WorkTypeTemplates");

            migrationBuilder.DropTable(
                name: "WorkTypeCategories");

            migrationBuilder.RenameColumn(
                name: "WorkTypeTemplateId",
                table: "TaskStages",
                newName: "ServiceTemplateId");

            migrationBuilder.RenameColumn(
                name: "WorkTypeNameSnapshot",
                table: "TaskStages",
                newName: "ServiceNameSnapshot");

            migrationBuilder.RenameColumn(
                name: "WorkTypeDescriptionSnapshot",
                table: "TaskStages",
                newName: "ServiceDescriptionSnapshot");

            migrationBuilder.RenameIndex(
                name: "IX_TaskStages_WorkTypeTemplateId",
                table: "TaskStages",
                newName: "IX_TaskStages_ServiceTemplateId");

            migrationBuilder.CreateTable(
                name: "ServiceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceTemplates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Article = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    BasePrice = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Unit = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceTemplates_ServiceCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ServiceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    ServiceTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PricePerUnit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    ServiceDescriptionSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ServiceNameSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnitSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageServices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageServices_ServiceTemplates_ServiceTemplateId",
                        column: x => x.ServiceTemplateId,
                        principalTable: "ServiceTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StageServices_TaskStages_StageId",
                        column: x => x.StageId,
                        principalTable: "TaskStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceCategories_Name",
                table: "ServiceCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTemplates_Article",
                table: "ServiceTemplates",
                column: "Article",
                unique: true,
                filter: "[Article] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTemplates_CategoryId",
                table: "ServiceTemplates",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTemplates_Name",
                table: "ServiceTemplates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_StageServices_ServiceTemplateId",
                table: "StageServices",
                column: "ServiceTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StageServices_StageId",
                table: "StageServices",
                column: "StageId");

            migrationBuilder.AddForeignKey(
                name: "FK_TaskStages_ServiceTemplates_ServiceTemplateId",
                table: "TaskStages",
                column: "ServiceTemplateId",
                principalTable: "ServiceTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
