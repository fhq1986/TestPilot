using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IterationC_SharedSteps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SharedGroupId",
                table: "TestSteps",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SharedVariables",
                table: "TestSteps",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SharedStepGroups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Variables = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedStepGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedStepGroups_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SharedStepItem",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    StepOrder = table.Column<int>(type: "integer", nullable: false),
                    ActionType = table.Column<int>(type: "integer", nullable: false),
                    AIInstruction = table.Column<string>(type: "text", nullable: true),
                    AIElementDescription = table.Column<string>(type: "text", nullable: true),
                    Config = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedStepItem", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedStepItem_SharedStepGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "SharedStepGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestSteps_SharedGroupId",
                table: "TestSteps",
                column: "SharedGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedStepGroups_ProjectId",
                table: "SharedStepGroups",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedStepGroups_ProjectId_Name",
                table: "SharedStepGroups",
                columns: new[] { "ProjectId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SharedStepItem_GroupId",
                table: "SharedStepItem",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_TestSteps_SharedStepGroups_SharedGroupId",
                table: "TestSteps",
                column: "SharedGroupId",
                principalTable: "SharedStepGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestSteps_SharedStepGroups_SharedGroupId",
                table: "TestSteps");

            migrationBuilder.DropTable(
                name: "SharedStepItem");

            migrationBuilder.DropTable(
                name: "SharedStepGroups");

            migrationBuilder.DropIndex(
                name: "IX_TestSteps_SharedGroupId",
                table: "TestSteps");

            migrationBuilder.DropColumn(
                name: "SharedGroupId",
                table: "TestSteps");

            migrationBuilder.DropColumn(
                name: "SharedVariables",
                table: "TestSteps");
        }
    }
}
