using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReviewReviewedByFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TestCases_ReviewedById",
                table: "TestCases",
                column: "ReviewedById");

            migrationBuilder.AddForeignKey(
                name: "FK_TestCases_Users_ReviewedById",
                table: "TestCases",
                column: "ReviewedById",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TestCases_Users_ReviewedById",
                table: "TestCases");

            migrationBuilder.DropIndex(
                name: "IX_TestCases_ReviewedById",
                table: "TestCases");
        }
    }
}
