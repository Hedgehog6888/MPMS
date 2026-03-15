using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MPMS.API.Migrations
{
    public partial class SplitUserNameToFirstLast : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FirstName",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastName",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql(@"
                UPDATE Users SET
                    FirstName = CASE WHEN CHARINDEX(' ', Name) > 0 THEN LEFT(Name, CHARINDEX(' ', Name) - 1) ELSE Name END,
                    LastName = CASE WHEN CHARINDEX(' ', Name) > 0 THEN LTRIM(SUBSTRING(Name, CHARINDEX(' ', Name) + 1, LEN(Name))) ELSE N'' END
                WHERE FirstName IS NULL
            ");

            migrationBuilder.AlterColumn<string>(
                name: "FirstName",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<string>(
                name: "LastName",
                table: "Users",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Users");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Users",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql(@"
                UPDATE Users SET Name = RTRIM(FirstName + ' ' + LastName)
            ");

            migrationBuilder.DropColumn(
                name: "FirstName",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastName",
                table: "Users");
        }
    }
}
