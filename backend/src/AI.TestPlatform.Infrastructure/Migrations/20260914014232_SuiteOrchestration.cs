using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SuiteOrchestration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailurePolicy",
                table: "TestSuites",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "DependsOnTestCaseId",
                table: "TestSuiteCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DependsOnTestCaseId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SkipReason",
                table: "Executions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestSuiteCases_DependsOnTestCaseId",
                table: "TestSuiteCases",
                column: "DependsOnTestCaseId");

            migrationBuilder.AddForeignKey(
                name: "FK_TestSuiteCases_TestCases_DependsOnTestCaseId",
                table: "TestSuiteCases",
                column: "DependsOnTestCaseId",
                principalTable: "TestCases",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestSuiteCases_TestCases_DependsOnTestCaseId",
                table: "TestSuiteCases");

            migrationBuilder.DropIndex(
                name: "IX_TestSuiteCases_DependsOnTestCaseId",
                table: "TestSuiteCases");

            migrationBuilder.DropColumn(
                name: "FailurePolicy",
                table: "TestSuites");

            migrationBuilder.DropColumn(
                name: "DependsOnTestCaseId",
                table: "TestSuiteCases");

            migrationBuilder.DropColumn(
                name: "DependsOnTestCaseId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "SkipReason",
                table: "Executions");
        }
    }
}
