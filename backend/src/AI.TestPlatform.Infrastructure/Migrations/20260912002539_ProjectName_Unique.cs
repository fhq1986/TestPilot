using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AI.TestPlatform.Infrastructure.Migrations
{
    /// <summary>
    /// 项目名称唯一：先清理历史重名，再加唯一索引。
    ///
    /// 为什么顺序不能反：库里存在历史重名（RBAC 验证残留），
    /// 直接 <c>CREATE UNIQUE INDEX</c> 会失败；而这个失败发生在**启动自动迁移**阶段，
    /// 服务会起不来、迁移记录还可能处于半应用状态，现场比"多一个后缀"难收拾得多。
    ///
    /// 为什么是重命名而不是删除：项目行可能已被用例、执行记录、计划、报告引用，
    /// 删掉会顺着外键级联带走验收材料。改名只影响显示，数据一行不少。
    /// </summary>
    public partial class ProjectName_Unique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 保留原名的规则：
            //   1) 用例数最多的那一行 —— 那通常是用户真正在用的项目，不该因为
            //      存在几条空壳残留就把它改名（残留行让位更符合直觉）
            //   2) 用例数相同时取创建最早的
            //   3) 再相同取 Id，保证结果与行扫描顺序无关（迁移可重放）
            // 其余行追加 " (n)" 后缀；若后缀恰好被占用（库里本来就有「项目A (2)」）
            // 就继续往后找，直到不冲突为止，否则索引依旧建不起来。
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    dup_name text;
                    target record;
                    seq int;
                    candidate text;
                BEGIN
                    FOR dup_name IN
                        SELECT p."Name"
                        FROM "Projects" p
                        GROUP BY p."Name"
                        HAVING count(*) > 1
                    LOOP
                        seq := 1;
                        FOR target IN
                            SELECT p."Id"
                            FROM "Projects" p
                            WHERE p."Name" = dup_name
                            ORDER BY (SELECT count(*) FROM "TestCases" t WHERE t."ProjectId" = p."Id") DESC,
                                     p."CreatedAt",
                                     p."Id"
                            OFFSET 1
                        LOOP
                            seq := seq + 1;
                            candidate := dup_name || ' (' || seq || ')';
                            WHILE EXISTS (SELECT 1 FROM "Projects" WHERE "Name" = candidate) LOOP
                                seq := seq + 1;
                                candidate := dup_name || ' (' || seq || ')';
                            END LOOP;
                            UPDATE "Projects" SET "Name" = candidate WHERE "Id" = target."Id";
                        END LOOP;
                    END LOOP;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 只回退索引。改名是不可逆的（"Foo (2)" 无法知道它原本叫什么），
            // 强行还原反而可能撞上同一个重复——这属于单向演进，Down 只保证结构可退。
            migrationBuilder.DropIndex(
                name: "IX_Projects_Name",
                table: "Projects");
        }
    }
}
