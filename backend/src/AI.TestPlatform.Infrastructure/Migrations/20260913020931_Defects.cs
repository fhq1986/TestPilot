using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Defects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Defects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    AssignedToId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    FoundInExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    FoundInStepOrder = table.Column<int>(type: "integer", nullable: true),
                    FoundInTestCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExternalRef = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FixedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Defects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Defects_Executions_FoundInExecutionId",
                        column: x => x.FoundInExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Defects_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Defects_TestCases_FoundInTestCaseId",
                        column: x => x.FoundInTestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Defects_Users_AssignedToId",
                        column: x => x.AssignedToId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Defects_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Defects_Users_VerifiedById",
                        column: x => x.VerifiedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DefectCases",
                columns: table => new
                {
                    DefectId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectCases", x => new { x.DefectId, x.TestCaseId });
                    table.ForeignKey(
                        name: "FK_DefectCases_Defects_DefectId",
                        column: x => x.DefectId,
                        principalTable: "Defects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DefectCases_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DefectOccurrences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DefectId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DefectOccurrences_Defects_DefectId",
                        column: x => x.DefectId,
                        principalTable: "Defects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DefectOccurrences_Executions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "Executions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DefectCases_TestCaseId",
                table: "DefectCases",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectOccurrences_DefectId_ExecutionId_StepOrder",
                table: "DefectOccurrences",
                columns: new[] { "DefectId", "ExecutionId", "StepOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DefectOccurrences_ExecutionId",
                table: "DefectOccurrences",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_AssignedToId",
                table: "Defects",
                column: "AssignedToId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_CreatedById",
                table: "Defects",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_FoundInExecutionId",
                table: "Defects",
                column: "FoundInExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_FoundInTestCaseId",
                table: "Defects",
                column: "FoundInTestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Defects_ProjectId_Severity",
                table: "Defects",
                columns: new[] { "ProjectId", "Severity" });

            migrationBuilder.CreateIndex(
                name: "IX_Defects_ProjectId_Status",
                table: "Defects",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Defects_VerifiedById",
                table: "Defects",
                column: "VerifiedById");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DefectCases");

            migrationBuilder.DropTable(
                name: "DefectOccurrences");

            migrationBuilder.DropTable(
                name: "Defects");
        }
    }
}
