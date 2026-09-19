using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVisualIgnoreRegionsAndBrowserBaselines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VisualBaselines_TestCaseId_StepOrder",
                table: "VisualBaselines");

            migrationBuilder.AddColumn<string>(
                name: "Browser",
                table: "VisualBaselines",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualIgnoreRegions",
                table: "TestCases",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisualBaselines_TestCaseId_StepOrder_Browser",
                table: "VisualBaselines",
                columns: new[] { "TestCaseId", "StepOrder", "Browser" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VisualBaselines_TestCaseId_StepOrder_Browser",
                table: "VisualBaselines");

            migrationBuilder.DropColumn(
                name: "Browser",
                table: "VisualBaselines");

            migrationBuilder.DropColumn(
                name: "VisualIgnoreRegions",
                table: "TestCases");

            migrationBuilder.CreateIndex(
                name: "IX_VisualBaselines_TestCaseId_StepOrder",
                table: "VisualBaselines",
                columns: new[] { "TestCaseId", "StepOrder" },
                unique: true);
        }
    }
}
