using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTestCaseNetworkRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NetworkRules",
                table: "TestCases",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NetworkRules",
                table: "TestCases");
        }
    }
}
