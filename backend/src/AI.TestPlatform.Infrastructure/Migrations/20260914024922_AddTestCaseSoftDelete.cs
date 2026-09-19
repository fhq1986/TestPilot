using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTestCaseSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 顺序很重要：**先加列，再搬数据**。
            // （本项目开发环境启动时会自动应用迁移，如果把搬运写在加列之前、
            //   或者让工具把顺序排错，数据会在列还不存在时就丢——这类坑记在项目备忘里。）
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "TestCases",
                type: "timestamp with time zone",
                nullable: true);

            // 把原来用「Status = 2（Deprecated）」表示的"已删除"搬到新字段上，
            // 并把 Status 一并归一化：枚举里已经删掉 Deprecated，
            // DB 里继续留着 2 就是**非法枚举值**——将来枚举再加值就会串位（2 变成别的含义）。
            //
            // 删除时间取 UpdatedAt：那正是当初被置为 Deprecated 的时刻，
            // 比记成"迁移执行的这一刻"更接近事实。
            migrationBuilder.Sql(
                """
                UPDATE "TestCases"
                SET "DeletedAt" = "UpdatedAt", "Status" = 0
                WHERE "Status" = 2;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回滚时必须**先把删除标记还原成 Status = 2**，再删列——
            // 否则那批用例会"复活"：DeletedAt 没了、状态还是 0（草稿），
            // 于是它们会重新出现在列表里。
            migrationBuilder.Sql(
                """
                UPDATE "TestCases"
                SET "Status" = 2
                WHERE "DeletedAt" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "TestCases");
        }
    }
}
