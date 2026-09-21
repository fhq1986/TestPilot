using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "TestSuites",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "TestSuites",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "TestPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "TestPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "TestCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "TestCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "SharedStepGroups",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "Schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "Schedules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "Requirements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Requirements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "Requirements",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "Projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "Defects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatedById",
                table: "DataSets",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedById",
                table: "DataSets",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "TestSuites");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "TestSuites");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "TestPlans");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "TestPlans");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "SharedStepGroups");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "Defects");

            migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "DataSets");

            migrationBuilder.DropColumn(
                name: "UpdatedById",
                table: "DataSets");
        }
    }
}
