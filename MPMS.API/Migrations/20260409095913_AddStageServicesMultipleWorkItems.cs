using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.API.Migrations
{
    /// <inheritdoc />
    public partial class AddStageServicesMultipleWorkItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StageServices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false, defaultValueSql: "NEWSEQUENTIALID()"),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceTemplateId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceNameSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ServiceDescriptionSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UnitSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    PricePerUnit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
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
                name: "IX_StageServices_ServiceTemplateId",
                table: "StageServices",
                column: "ServiceTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_StageServices_StageId",
                table: "StageServices",
                column: "StageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StageServices");
        }
    }
}
