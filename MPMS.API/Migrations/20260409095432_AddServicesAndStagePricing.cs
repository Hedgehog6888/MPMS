using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddServicesAndStagePricing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServiceDescriptionSnapshot",
                table: "TaskStages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceNameSnapshot",
                table: "TaskStages",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ServiceTemplateId",
                table: "TaskStages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WorkPricePerUnit",
                table: "TaskStages",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WorkQuantity",
                table: "TaskStages",
                type: "decimal(18,3)",
                precision: 18,
                scale: 3,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "WorkUnitSnapshot",
                table: "TaskStages",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricePerUnit",
                table: "StageMaterials",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ServiceCategories",
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
                    table.PrimaryKey("PK_ServiceCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceTemplates",
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
                    table.PrimaryKey("PK_ServiceTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceTemplates_ServiceCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "ServiceCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskStages_ServiceTemplateId",
                table: "TaskStages",
                column: "ServiceTemplateId");

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

            migrationBuilder.AddForeignKey(
                name: "FK_TaskStages_ServiceTemplates_ServiceTemplateId",
                table: "TaskStages",
                column: "ServiceTemplateId",
                principalTable: "ServiceTemplates",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskStages_ServiceTemplates_ServiceTemplateId",
                table: "TaskStages");

            migrationBuilder.DropTable(
                name: "ServiceTemplates");

            migrationBuilder.DropTable(
                name: "ServiceCategories");

            migrationBuilder.DropIndex(
                name: "IX_TaskStages_ServiceTemplateId",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "ServiceDescriptionSnapshot",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "ServiceNameSnapshot",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "ServiceTemplateId",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "WorkPricePerUnit",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "WorkQuantity",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "WorkUnitSnapshot",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "PricePerUnit",
                table: "StageMaterials");
        }
    }
}
