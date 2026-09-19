using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSsoSystemConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SsoAutoProvision",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SsoDingtalkClientId",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SsoDingtalkClientSecret",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SsoDingtalkEnabled",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SsoFrontendBaseUrl",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SsoWecomAgentId",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SsoWecomCorpId",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SsoWecomEnabled",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SsoWecomSecret",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SsoAutoProvision",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoDingtalkClientId",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoDingtalkClientSecret",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoDingtalkEnabled",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoFrontendBaseUrl",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoWecomAgentId",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoWecomCorpId",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoWecomEnabled",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoWecomSecret",
                table: "SystemConfigs");
        }
    }
}
