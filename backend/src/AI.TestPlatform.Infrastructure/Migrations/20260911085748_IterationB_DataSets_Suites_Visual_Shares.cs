using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IterationB_DataSets_Suites_Visual_Shares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DataSetId",
                table: "TestCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "VisualEnabled",
                table: "TestCases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<double>(
                name: "VisualThreshold",
                table: "TestCases",
                type: "double precision",
                nullable: false,
                defaultValue: 0.01);

            migrationBuilder.AddColumn<string>(
                name: "Browsers",
                table: "Schedules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ExpandDataSets",
                table: "Schedules",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "BrowserName",
                table: "Executions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DataSetRowIndex",
                table: "Executions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DataSetRowLabel",
                table: "Executions",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuiteId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SuiteRunId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Variables",
                table: "Executions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BaselineImageUrl",
                table: "ExecutionResults",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiffImageUrl",
                table: "ExecutionResults",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "VisualDiffRatio",
                table: "ExecutionResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VisualNote",
                table: "ExecutionResults",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VisualStatus",
                table: "ExecutionResults",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "VisualThreshold",
                table: "ExecutionResults",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Browser",
                table: "Environments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DataSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Columns = table.Column<List<string>>(type: "text[]", nullable: false),
                    Rows = table.Column<string>(type: "jsonb", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    FirstRowIsSample = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DataSets_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    RefId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    From = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    To = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Revoked = table.Column<bool>(type: "boolean", nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    LastViewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportShares", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestSuites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSuiteRunId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestSuites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestSuites_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TestSuites_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VisualBaselines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    ImagePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    FilePath = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    SourceExecutionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CompareCount = table.Column<int>(type: "integer", nullable: false),
                    LastComparedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisualBaselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisualBaselines_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestSuiteCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SuiteId = table.Column<Guid>(type: "uuid", nullable: false),
                    TestCaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestSuiteCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestSuiteCases_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TestSuiteCases_TestSuites_SuiteId",
                        column: x => x.SuiteId,
                        principalTable: "TestSuites",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_DataSetId",
                table: "TestCases",
                column: "DataSetId");

            migrationBuilder.CreateIndex(
                name: "IX_Executions_SuiteRunId",
                table: "Executions",
                column: "SuiteRunId");

            migrationBuilder.CreateIndex(
                name: "IX_DataSets_ProjectId_Name",
                table: "DataSets",
                columns: new[] { "ProjectId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportShares_Kind_RefId",
                table: "ReportShares",
                columns: new[] { "Kind", "RefId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportShares_Token",
                table: "ReportShares",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestSuiteCases_SuiteId_Order",
                table: "TestSuiteCases",
                columns: new[] { "SuiteId", "Order" });

            migrationBuilder.CreateIndex(
                name: "IX_TestSuiteCases_SuiteId_TestCaseId",
                table: "TestSuiteCases",
                columns: new[] { "SuiteId", "TestCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestSuiteCases_TestCaseId",
                table: "TestSuiteCases",
                column: "TestCaseId");

            migrationBuilder.CreateIndex(
                name: "IX_TestSuites_EnvironmentId",
                table: "TestSuites",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestSuites_ProjectId_Kind",
                table: "TestSuites",
                columns: new[] { "ProjectId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_VisualBaselines_TestCaseId_StepOrder",
                table: "VisualBaselines",
                columns: new[] { "TestCaseId", "StepOrder" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TestCases_DataSets_DataSetId",
                table: "TestCases",
                column: "DataSetId",
                principalTable: "DataSets",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestCases_DataSets_DataSetId",
                table: "TestCases");

            migrationBuilder.DropTable(
                name: "DataSets");

            migrationBuilder.DropTable(
                name: "ReportShares");

            migrationBuilder.DropTable(
                name: "TestSuiteCases");

            migrationBuilder.DropTable(
                name: "VisualBaselines");

            migrationBuilder.DropTable(
                name: "TestSuites");

            migrationBuilder.DropIndex(
                name: "IX_TestCases_DataSetId",
                table: "TestCases");

            migrationBuilder.DropIndex(
                name: "IX_Executions_SuiteRunId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "DataSetId",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "VisualEnabled",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "VisualThreshold",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "Browsers",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "ExpandDataSets",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "BrowserName",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "DataSetRowIndex",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "DataSetRowLabel",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "SuiteId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "SuiteRunId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "Variables",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "BaselineImageUrl",
                table: "ExecutionResults");

            migrationBuilder.DropColumn(
                name: "DiffImageUrl",
                table: "ExecutionResults");

            migrationBuilder.DropColumn(
                name: "VisualDiffRatio",
                table: "ExecutionResults");

            migrationBuilder.DropColumn(
                name: "VisualNote",
                table: "ExecutionResults");

            migrationBuilder.DropColumn(
                name: "VisualStatus",
                table: "ExecutionResults");

            migrationBuilder.DropColumn(
                name: "VisualThreshold",
                table: "ExecutionResults");

            migrationBuilder.DropColumn(
                name: "Browser",
                table: "Environments");
        }
    }
}
