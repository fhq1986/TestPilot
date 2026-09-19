using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IterationA_Schedule_CiContext_Flaky_Notify : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FlakeCheckedAt",
                table: "TestCases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "FlakeRate",
                table: "TestCases",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "IsFlaky",
                table: "TestCases",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MailTo",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NotifyDingtalkWebhook",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "NotifyEnabled",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NotifyFeishuWebhook",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnFailureOnly",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NotifyWecomWebhook",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SmtpPassword",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "SystemConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpUseSsl",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUser",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Branch",
                table: "Executions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuildNumber",
                table: "Executions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommitSha",
                table: "Executions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TriggerSource",
                table: "Executions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CronExpression = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    Module = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    TestCaseIds = table.Column<string>(type: "text", nullable: true),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastCreatedCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schedules_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Schedules_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_ProjectId_IsFlaky",
                table: "TestCases",
                columns: new[] { "ProjectId", "IsFlaky" });

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_Enabled_NextRunAt",
                table: "Schedules",
                columns: new[] { "Enabled", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_EnvironmentId",
                table: "Schedules",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_ProjectId",
                table: "Schedules",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropIndex(
                name: "IX_TestCases_ProjectId_IsFlaky",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "FlakeCheckedAt",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "FlakeRate",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "IsFlaky",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "MailTo",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "NotifyDingtalkWebhook",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "NotifyEnabled",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "NotifyFeishuWebhook",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "NotifyOnFailureOnly",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "NotifyWecomWebhook",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SmtpPassword",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SmtpUseSsl",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SmtpUser",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "Branch",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "BuildNumber",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "CommitSha",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "TriggerSource",
                table: "Executions");
        }
    }
}
