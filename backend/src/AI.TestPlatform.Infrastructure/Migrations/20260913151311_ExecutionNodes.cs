using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExecutionNodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExecutionNodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    MachineName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    InstanceId = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MaxConcurrency = table.Column<int>(type: "integer", nullable: false),
                    RunningCount = table.Column<int>(type: "integer", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExecutionNodes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExecutionNodes_Name",
                table: "ExecutionNodes",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExecutionNodes");
        }
    }
}
