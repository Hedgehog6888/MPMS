using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MPMS.API.Data;

#nullable disable

namespace MPMS.API.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260601150000_AddStageIdToDiscussionMessages")]
    public partial class AddStageIdToDiscussionMessages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StageId",
                table: "DiscussionMessages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscussionMessages_StageId",
                table: "DiscussionMessages",
                column: "StageId");

            migrationBuilder.AddForeignKey(
                name: "FK_DiscussionMessages_TaskStages_StageId",
                table: "DiscussionMessages",
                column: "StageId",
                principalTable: "TaskStages",
                principalColumn: "Id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DiscussionMessages_TaskStages_StageId",
                table: "DiscussionMessages");

            migrationBuilder.DropIndex(
                name: "IX_DiscussionMessages_StageId",
                table: "DiscussionMessages");

            migrationBuilder.DropColumn(
                name: "StageId",
                table: "DiscussionMessages");
        }
    }
}
