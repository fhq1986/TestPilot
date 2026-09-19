using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDeveloperOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeveloperOwnerId",
                table: "Projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_DeveloperOwnerId",
                table: "Projects",
                column: "DeveloperOwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Projects_Users_DeveloperOwnerId",
                table: "Projects",
                column: "DeveloperOwnerId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Projects_Users_DeveloperOwnerId",
                table: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Projects_DeveloperOwnerId",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DeveloperOwnerId",
                table: "Projects");
        }
    }
}
