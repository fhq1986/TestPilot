using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOidcSsoConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SsoOidcAuthority",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SsoOidcClientId",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SsoOidcClientSecret",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SsoOidcDisplayName",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "SsoOidcEnabled",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SsoOidcScopes",
                table: "SystemConfigs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SsoOidcAuthority",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoOidcClientId",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoOidcClientSecret",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoOidcDisplayName",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoOidcEnabled",
                table: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "SsoOidcScopes",
                table: "SystemConfigs");
        }
    }
}
