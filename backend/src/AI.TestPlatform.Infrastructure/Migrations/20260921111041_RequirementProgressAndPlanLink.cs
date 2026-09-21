using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RequirementProgressAndPlanLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RequirementId",
                table: "TestPlans",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualEndDate",
                table: "Requirements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ActualStartDate",
                table: "Requirements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanEndDate",
                table: "Requirements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PlanStartDate",
                table: "Requirements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Requirements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_TestPlans_RequirementId",
                table: "TestPlans",
                column: "RequirementId");

            migrationBuilder.AddForeignKey(
                name: "FK_TestPlans_Requirements_RequirementId",
                table: "TestPlans",
                column: "RequirementId",
                principalTable: "Requirements",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestPlans_Requirements_RequirementId",
                table: "TestPlans");

            migrationBuilder.DropIndex(
                name: "IX_TestPlans_RequirementId",
                table: "TestPlans");

            migrationBuilder.DropColumn(
                name: "RequirementId",
                table: "TestPlans");

            migrationBuilder.DropColumn(
                name: "ActualEndDate",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "ActualStartDate",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "PlanEndDate",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "PlanStartDate",
                table: "Requirements");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "Requirements");
        }
    }
}
