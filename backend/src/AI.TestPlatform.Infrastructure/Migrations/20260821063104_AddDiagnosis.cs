using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AIDiagnosis",
                table: "Executions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AISuggestedFix",
                table: "Executions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<float>(
                name: "DiagnosisConfidence",
                table: "Executions",
                type: "real",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AIDiagnosis",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "AISuggestedFix",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "DiagnosisConfidence",
                table: "Executions");
        }
    }
}
