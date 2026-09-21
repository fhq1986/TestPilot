using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentLoop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AgentLoopEnabled",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AgentLoopEnabled",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "AgentLoopSuspendedAt",
                table: "Projects",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TreatAgentHealedAsPass",
                table: "Projects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AgentBudgetUsed",
                table: "Executions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AgentFinalVerdict",
                table: "Executions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AgentHealed",
                table: "Executions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "AgentLoopCount",
                table: "Executions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "AgentAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    TargetStepOrder = table.Column<int>(type: "integer", nullable: false),
                    FailureEvidence = table.Column<string>(type: "jsonb", nullable: true),
                    DiagnosisRaw = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    FixCategory = table.Column<int>(type: "integer", nullable: false),
                    Confidence = table.Column<float>(type: "real", nullable: false),
                    FixSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    AppliedActions = table.Column<string>(type: "jsonb", nullable: true),
                    AppliedSuccessfully = table.Column<bool>(type: "boolean", nullable: false),
                    Persisted = table.Column<bool>(type: "boolean", nullable: false),
                    Result = table.Column<int>(type: "integer", nullable: false),
                    FailureAfterFix = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    NeedsApproval = table.Column<bool>(type: "boolean", nullable: false),
                    Approved = table.Column<bool>(type: "boolean", nullable: true),
                    ApprovedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LlmInputTokens = table.Column<int>(type: "integer", nullable: false),
                    LlmOutputTokens = table.Column<int>(type: "integer", nullable: false),
                    LlmModel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentAttempts_Executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentAttempts_ExecutionId",
                table: "AgentAttempts",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentAttempts_ExecutionId_AttemptNumber",
                table: "AgentAttempts",
                columns: new[] { "ExecutionId", "AttemptNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentAttempts");

            migrationBuilder.DropColumn(
                name: "AgentLoopEnabled",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "AgentLoopEnabled",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "AgentLoopSuspendedAt",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "TreatAgentHealedAsPass",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "AgentBudgetUsed",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "AgentFinalVerdict",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "AgentHealed",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "AgentLoopCount",
                table: "Executions");
        }
    }
}
