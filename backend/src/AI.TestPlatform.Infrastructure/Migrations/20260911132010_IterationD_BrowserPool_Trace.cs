using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IterationD_BrowserPool_Trace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "TraceSizeBytes",
                table: "Executions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraceUrl",
                table: "Executions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TraceSizeBytes",
                table: "Executions");

            migrationBuilder.DropColumn(
                name: "TraceUrl",
                table: "Executions");
        }
    }
}
