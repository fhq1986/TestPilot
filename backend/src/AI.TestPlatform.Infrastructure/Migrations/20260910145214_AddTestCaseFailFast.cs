using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTestCaseFailFast : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "FailFast",
                table: "TestCases",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FailFast",
                table: "TestCases");
        }
    }
}
