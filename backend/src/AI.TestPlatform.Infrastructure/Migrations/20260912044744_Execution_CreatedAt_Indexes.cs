using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Execution_CreatedAt_Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Executions_CreatedAt",
                table: "Executions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Executions_TestCaseId_CreatedAt",
                table: "Executions",
                columns: new[] { "TestCaseId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Executions_CreatedAt",
                table: "Executions");

            migrationBuilder.DropIndex(
                name: "IX_Executions_TestCaseId_CreatedAt",
                table: "Executions");
        }
    }
}
