using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.API.Migrations
{
    /// <inheritdoc />
    public partial class ExtendedClientSyncFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "BlockedAt",
                table: "Users",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BlockedReason",
                table: "Users",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBlocked",
                table: "Users",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "TaskStages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMarkedForDeletion",
                table: "TaskStages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Tasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMarkedForDeletion",
                table: "Tasks",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Projects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsMarkedForDeletion",
                table: "Projects",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsWrittenOff",
                table: "Materials",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "WrittenOffAt",
                table: "Materials",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WrittenOffComment",
                table: "Materials",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsWrittenOff",
                table: "Equipments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "WrittenOffAt",
                table: "Equipments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WrittenOffComment",
                table: "Equipments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiscussionMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserInitials = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    UserColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    UserRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscussionMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscussionMessages_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiscussionMessages_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiscussionMessages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StageAssignees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StageAssignees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StageAssignees_TaskStages_StageId",
                        column: x => x.StageId,
                        principalTable: "TaskStages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StageAssignees_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncedActivityLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ActorRole = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UserInitials = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    UserColor = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ActionType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ActionText = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncedActivityLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncedActivityLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TaskAssignees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAssignees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAssignees_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TaskAssignees_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscussionMessages_ProjectId",
                table: "DiscussionMessages",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscussionMessages_TaskId",
                table: "DiscussionMessages",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscussionMessages_UserId",
                table: "DiscussionMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StageAssignees_StageId_UserId",
                table: "StageAssignees",
                columns: new[] { "StageId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StageAssignees_UserId",
                table: "StageAssignees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncedActivityLogs_UserId",
                table: "SyncedActivityLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignees_TaskId_UserId",
                table: "TaskAssignees",
                columns: new[] { "TaskId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskAssignees_UserId",
                table: "TaskAssignees",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscussionMessages");

            migrationBuilder.DropTable(
                name: "StageAssignees");

            migrationBuilder.DropTable(
                name: "SyncedActivityLogs");

            migrationBuilder.DropTable(
                name: "TaskAssignees");

            migrationBuilder.DropColumn(
                name: "BlockedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "BlockedReason",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsBlocked",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "IsMarkedForDeletion",
                table: "TaskStages");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "IsMarkedForDeletion",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IsMarkedForDeletion",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IsWrittenOff",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "WrittenOffAt",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "WrittenOffComment",
                table: "Materials");

            migrationBuilder.DropColumn(
                name: "IsWrittenOff",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "WrittenOffAt",
                table: "Equipments");

            migrationBuilder.DropColumn(
                name: "WrittenOffComment",
                table: "Equipments");
        }
    }
}
