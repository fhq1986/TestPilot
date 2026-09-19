using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExecutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Executions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TriggeredById = table.Column<Guid>(type: "uuid", nullable: true),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    BrowserVersion = table.Column<string>(type: "text", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Executions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Executions_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Executions_Users_TriggeredById",
                        column: x => x.TriggeredById,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ExecutionResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestStepId = table.Column<Guid>(type: "uuid", nullable: true),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    ScreenshotUrl = table.Column<string>(type: "text", nullable: true),
                    Log = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    StackTrace = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StepSnapshot = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExecutionResults_Executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExecutionResults_TestSteps_TestStepId",
                        column: x => x.TestStepId,
                        principalTable: "TestSteps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionResults_ExecutionId_StepOrder",
                table: "ExecutionResults",
                columns: new[] { "ExecutionId", "StepOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionResults_TestStepId",
                table: "ExecutionResults",
                column: "TestStepId");

            migrationBuilder.CreateIndex(
                name: "IX_Executions_Status_CreatedAt",
                table: "Executions",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Executions_TestCaseId",
                table: "Executions",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Executions_TriggeredById",
                table: "Executions",
                column: "TriggeredById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionResults");

            migrationBuilder.DropTable(
                name: "Executions");
        }
    }
}
