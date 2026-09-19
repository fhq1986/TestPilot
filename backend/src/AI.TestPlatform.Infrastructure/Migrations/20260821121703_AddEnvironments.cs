using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnvironments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "EnvironmentId",
                table: "Executions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EnvironmentSnapshot",
                table: "Executions",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    LoginUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LoginUsername = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LoginPassword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    LoginSuccessIndicator = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    AutoLogin = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Environments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Environments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Executions_EnvironmentId",
                table: "Executions",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Environments_ProjectId",
                table: "Environments",
                column: "ProjectId");

            migrationBuilder.AddForeignKey(
                name: "FK_Executions_Environments_EnvironmentId",
                table: "Executions",
                column: "EnvironmentId",
                principalTable: "Environments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Executions_Environments_EnvironmentId",
                table: "Executions");

            migrationBuilder.DropTable(
                name: "Environments");

            migrationBuilder.DropIndex(
                name: "IX_Executions_EnvironmentId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "EnvironmentId",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "EnvironmentSnapshot",
                table: "Executions");
        }
    }
}
