using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TestPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlanId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlanRoundId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TestPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReleaseName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TargetPassRate = table.Column<double>(type: "double precision", nullable: false),
                    AllowErrors = table.Column<bool>(type: "boolean", nullable: false),
                    ExcludeFlakyFromFailure = table.Column<bool>(type: "boolean", nullable: false),
                    GateMode = table.Column<int>(type: "integer", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Browsers = table.Column<string>(type: "text", nullable: true),
                    ExpandDataSets = table.Column<bool>(type: "boolean", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastRoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestPlans_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TestPlans_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestPlans_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TestPlanItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPlanItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestPlanItems_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestPlanItems_TestPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TestPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestPlanRounds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundNo = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    TriggerType = table.Column<int>(type: "integer", nullable: false),
                    TriggerSource = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    TriggeredById = table.Column<Guid>(type: "uuid", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Browsers = table.Column<string>(type: "text", nullable: true),
                    ExpandDataSets = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestPlanRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestPlanRounds_TestPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "TestPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlanRoundCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoundId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanRoundCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanRoundCases_TestPlanRounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "TestPlanRounds",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Executions_PlanRoundId",
                table: "Executions",
                column: "PlanRoundId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanRoundCases_RoundId_Order",
                table: "PlanRoundCases",
                columns: new[] { "RoundId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanItems_PlanId_Order",
                table: "TestPlanItems",
                columns: new[] { "PlanId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanItems_PlanId_TestCaseId",
                table: "TestPlanItems",
                columns: new[] { "PlanId", "TestCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanItems_TestCaseId",
                table: "TestPlanItems",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRounds_OneRunningPerPlan",
                table: "TestPlanRounds",
                column: "PlanId",
                unique: true,
                filter: "\"Status\" = 0");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlanRounds_PlanId_RoundNo",
                table: "TestPlanRounds",
                columns: new[] { "PlanId", "RoundNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_EnvironmentId",
                table: "TestPlans",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_OwnerId",
                table: "TestPlans",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_ProjectId_Status",
                table: "TestPlans",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_ReleaseName",
                table: "TestPlans",
                column: "ReleaseName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanRoundCases");

            migrationBuilder.DropTable(
                name: "TestPlanItems");

            migrationBuilder.DropTable(
                name: "TestPlanRounds");

            migrationBuilder.DropTable(
                name: "TestPlans");

            migrationBuilder.DropIndex(
                name: "IX_Executions_PlanRoundId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PlanId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "PlanRoundId",
                table: "Executions");
        }
    }
}
