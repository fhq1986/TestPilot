using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCaseReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReviewNote",
                table: "TestCases",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReviewStatus",
                table: "TestCases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewSubmittedAt",
                table: "TestCases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewSubmittedById",
                table: "TestCases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReviewedAt",
                table: "TestCases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewedById",
                table: "TestCases",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReviewNote",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ReviewStatus",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ReviewSubmittedAt",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ReviewSubmittedById",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ReviewedAt",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "ReviewedById",
                table: "TestCases");
        }
    }
}
