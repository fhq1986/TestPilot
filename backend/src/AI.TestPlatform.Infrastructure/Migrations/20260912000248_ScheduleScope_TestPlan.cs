using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <summary>
    /// 定时任务的执行范围显式化，并把「计划 → 定时任务」的绑定搬到「定时任务 → 计划」。
    ///
    /// 背景：原来范围是隐式的（靠「指定用例 &gt; 模块/优先级 &gt; 全项目」推断），
    /// 与「按测试计划执行」无法区分；而 TestPlan.ScheduleId 是单个外键，
    /// 一个计划只能绑一个定时任务，「每晚跑一遍 + 发版前再跑一遍」表达不了。
    ///
    /// ⚠️ 执行顺序很关键：**必须先把数据搬到新列，再删旧列**。
    /// EF 自动生成的顺序是「先 DropColumn 再 AddColumn」，那会先删掉数据再迁移——
    /// 所以这里手工把顺序调整为 加列 → 搬数据 → 删列。
    /// </summary>
    public partial class ScheduleScope_TestPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScopeKind",
                table: "Schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "TestPlanIds",
                table: "Schedules",
                type: "text",
                nullable: true);

            // 数据搬迁：把每个计划上的 ScheduleId 汇总到对应定时任务的计划范围里。
            // 用 string_agg 按 ScheduleId 分组而不是逐行 UPDATE：
            // 原实现允许一个定时任务被多个计划绑定，逐行更新会让后写的覆盖先写的、丢掉其余计划。
            // ORDER BY "Id" 保证同样的数据每次迁移得到同样的字符串（否则快照比对会出现假差异）。
            migrationBuilder.Sql("""
                UPDATE "Schedules" s
                SET "ScopeKind" = 1,
                    "TestPlanIds" = agg.ids
                FROM (
                    SELECT "ScheduleId" AS sid,
                           string_agg("Id"::text, ',' ORDER BY "Id") AS ids
                    FROM "TestPlans"
                    WHERE "ScheduleId" IS NOT NULL
                    GROUP BY "ScheduleId"
                ) agg
                WHERE agg.sid = s."Id";
                """);

            migrationBuilder.DropColumn(
                name: "ScheduleId",
                table: "TestPlans");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ScheduleId",
                table: "TestPlans",
                type: "uuid",
                nullable: true);

            // 回滚搬迁：只保留「第一个」定时任务的绑定。
            // 新模型允许一个计划被多个定时任务引用，旧模型表达不了这种关系，
            // 回滚必然丢信息——这是单向演进，Down 只保证结构可回退。
            migrationBuilder.Sql("""
                UPDATE "TestPlans" p
                SET "ScheduleId" = src.sid
                FROM (
                    SELECT s."Id" AS sid, unnest(string_to_array(s."TestPlanIds", ','))::uuid AS plan_id
                    FROM "Schedules" s
                    WHERE s."ScopeKind" = 1 AND s."TestPlanIds" IS NOT NULL
                ) src
                WHERE p."Id" = src.plan_id;
                """);

            migrationBuilder.DropColumn(
                name: "ScopeKind",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "TestPlanIds",
                table: "Schedules");
        }
    }
}
