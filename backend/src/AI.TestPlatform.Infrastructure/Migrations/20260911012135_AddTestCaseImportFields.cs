using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTestCaseImportFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaseCode",
                table: "TestCases",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedResult",
                table: "TestCases",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Module",
                table: "TestCases",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceSteps",
                table: "TestCases",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_ProjectId_Module",
                table: "TestCases",
                columns: new[] { "ProjectId", "Module" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TestCases_ProjectId_Module",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "CaseCode",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ExpectedResult",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "Module",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "SourceSteps",
                table: "TestCases");
        }
    }
}
