using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IterationA_NotifyDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 老数据行在新增这几列时取到的是 CLR 零值（false/0），先按实体默认值回填
            migrationBuilder.Sql("""
                UPDATE "SystemConfigs"
                SET "SmtpPort" = 465
                WHERE "SmtpPort" = 0;

                UPDATE "SystemConfigs"
                SET "SmtpUseSsl" = TRUE
                WHERE "SmtpHost" = '' AND "SmtpUseSsl" = FALSE;

                UPDATE "SystemConfigs"
                SET "NotifyOnFailureOnly" = TRUE
                WHERE "NotifyEnabled" = FALSE AND "NotifyOnFailureOnly" = FALSE;
                """);

            migrationBuilder.AlterColumn<bool>(
                name: "SmtpUseSsl",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<int>(
                name: "SmtpPort",
                table: "SystemConfigs",
                type: "integer",
                nullable: false,
                defaultValue: 465,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "NotifyOnFailureOnly",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                defaultValue: true,
                oldClrType: typeof(bool),
                oldType: "boolean");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<bool>(
                name: "SmtpUseSsl",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);

            migrationBuilder.AlterColumn<int>(
                name: "SmtpPort",
                table: "SystemConfigs",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldDefaultValue: 465);

            migrationBuilder.AlterColumn<bool>(
                name: "NotifyOnFailureOnly",
                table: "SystemConfigs",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldDefaultValue: true);
        }
    }
}
