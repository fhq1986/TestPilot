using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoadTesting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoadTestScenarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Source = table.Column<int>(type: "integer", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ApiDefinitionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Operations = table.Column<string>(type: "jsonb", nullable: false),
                    Variables = table.Column<string>(type: "jsonb", nullable: true),
                    VirtualUsers = table.Column<int>(type: "integer", nullable: false),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: false),
                    ScriptText = table.Column<string>(type: "text", nullable: true),
                    ScriptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScriptGeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Profile = table.Column<string>(type: "jsonb", nullable: false),
                    Thresholds = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadTestScenarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoadTestScenarios_ApiDefinitions_ApiDefinitionId",
                        column: x => x.ApiDefinitionId,
                        principalTable: "ApiDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LoadTestScenarios_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LoadTestScenarios_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoadTestRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    TriggeredById = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    HeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    TargetBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    ScriptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ScriptArtifactKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    SummaryArtifactKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    LogArtifactKey = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    K6Version = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExitCode = table.Column<int>(type: "integer", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    TotalRequests = table.Column<long>(type: "bigint", nullable: false),
                    Rps = table.Column<double>(type: "double precision", nullable: true),
                    AvgMs = table.Column<double>(type: "double precision", nullable: true),
                    P50Ms = table.Column<double>(type: "double precision", nullable: true),
                    P95Ms = table.Column<double>(type: "double precision", nullable: true),
                    P99Ms = table.Column<double>(type: "double precision", nullable: true),
                    MaxMs = table.Column<double>(type: "double precision", nullable: true),
                    ErrorRate = table.Column<double>(type: "double precision", nullable: true),
                    ChecksRate = table.Column<double>(type: "double precision", nullable: true),
                    Iterations = table.Column<long>(type: "bigint", nullable: false),
                    VusMax = table.Column<int>(type: "integer", nullable: true),
                    ThresholdsPassed = table.Column<bool>(type: "boolean", nullable: true),
                    ThresholdTotal = table.Column<int>(type: "integer", nullable: false),
                    ThresholdFailed = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ThresholdResults = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadTestRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoadTestRuns_LoadTestScenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "LoadTestScenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoadTestRuns_Users_TriggeredById",
                        column: x => x.TriggeredById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LoadTestScenarioCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ScenarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoadTestScenarioCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoadTestScenarioCases_LoadTestScenarios_ScenarioId",
                        column: x => x.ScenarioId,
                        principalTable: "LoadTestScenarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoadTestScenarioCases_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestRuns_ProjectId_CreatedAt",
                table: "LoadTestRuns",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestRuns_ScenarioId_CreatedAt",
                table: "LoadTestRuns",
                columns: new[] { "ScenarioId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestRuns_Status_CreatedAt",
                table: "LoadTestRuns",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestRuns_Status_HeartbeatAt",
                table: "LoadTestRuns",
                columns: new[] { "Status", "HeartbeatAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestRuns_TriggeredById",
                table: "LoadTestRuns",
                column: "TriggeredById");

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestScenarioCases_ScenarioId_TestCaseId",
                table: "LoadTestScenarioCases",
                columns: new[] { "ScenarioId", "TestCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestScenarioCases_TestCaseId",
                table: "LoadTestScenarioCases",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestScenarios_ApiDefinitionId",
                table: "LoadTestScenarios",
                column: "ApiDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestScenarios_EnvironmentId",
                table: "LoadTestScenarios",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestScenarios_ProjectId",
                table: "LoadTestScenarios",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LoadTestScenarios_ProjectId_Name",
                table: "LoadTestScenarios",
                columns: new[] { "ProjectId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoadTestRuns");

            migrationBuilder.DropTable(
                name: "LoadTestScenarioCases");

            migrationBuilder.DropTable(
                name: "LoadTestScenarios");
        }
    }
}
