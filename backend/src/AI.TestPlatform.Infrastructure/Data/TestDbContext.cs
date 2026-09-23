using System.Text.Json;
using AI.TestPlatform.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Environment = AI.TestPlatform.Domain.Entities.Environment;

namespace AI.TestPlatform.Infrastructure.Data;

public class TestDbContext : DbContext
{
    /// <summary>jsonb 列统一使用的序列化选项（不转义中文，便于直接查库排查）</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options) { }

    /// <summary>
    /// D4：将元素缓存向量列（AIElementCaches.Embedding）的 pgvector 维度对齐到期望值。
    /// 默认关闭语义检索时 desired=512（与现有列一致，零影响）；启用外部 embeddings 时 desired=模型维度，
    /// 若与当前列维度不同则清空不兼容的历史向量并 ALTER 列类型。幂等：仅在维度变化时执行，
    /// 避免每次启动清空缓存。维度是整数字面量（DDL 的 TYPE 名不能参数化），故拼接而非参数化。
    /// </summary>
    public void EnsureEmbeddingColumnDimension(int desiredDim)
    {
        // 优先用 pgvector 的 vector_dims 读取已有向量的真实维度（最可靠），其次回退到列 typmod
        int current = 0;
        var sample = Database.SqlQuery<int?>($"SELECT vector_dims(\"Embedding\") FROM \"AIElementCaches\" WHERE \"Embedding\" IS NOT NULL LIMIT 1").ToList();
        int? firstSample = sample.FirstOrDefault();
        if (firstSample is { } sv)
        {
            current = sv;
        }
        else
        {
            var tm = Database.SqlQuery<int?>($"SELECT NULLIF(a.atttypmod, -1) FROM pg_attribute a JOIN pg_class c ON c.oid = a.attrelid WHERE c.relname = 'AIElementCaches' AND a.attname = 'Embedding'").ToList();
            int? firstTm = tm.FirstOrDefault();
            if (firstTm is { } tv) current = tv;
        }

        if (current == desiredDim) return;

        // 维度变化：现有向量维度不兼容无法隐式转换，先清空（由流量惰性回填），再调整列类型
        Database.ExecuteSqlRaw("UPDATE \"AIElementCaches\" SET \"Embedding\" = NULL WHERE \"Embedding\" IS NOT NULL;");
        Database.ExecuteSqlRaw("ALTER TABLE \"AIElementCaches\" ALTER COLUMN \"Embedding\" TYPE vector(" + desiredDim + ");");
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<ProjectMember> ProjectMembers => Set<ProjectMember>();
    public DbSet<TestCase> TestCases => Set<TestCase>();
    public DbSet<TestCaseVersion> TestCaseVersions => Set<TestCaseVersion>();
    public DbSet<TestStep> TestSteps => Set<TestStep>();
    public DbSet<Execution> Executions => Set<Execution>();
    public DbSet<ExecutionResult> ExecutionResults => Set<ExecutionResult>();
    // M8 Agent：失败修复尝试轨迹
    public DbSet<AgentAttempt> AgentAttempts => Set<AgentAttempt>();
    public DbSet<ExecutionNode> ExecutionNodes => Set<ExecutionNode>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<DataSet> DataSets => Set<DataSet>();
    public DbSet<TestSuite> TestSuites => Set<TestSuite>();
    public DbSet<TestSuiteCase> TestSuiteCases => Set<TestSuiteCase>();
    public DbSet<VisualBaseline> VisualBaselines => Set<VisualBaseline>();
    public DbSet<ReportShare> ReportShares => Set<ReportShare>();
    public DbSet<AIElementCache> AIElementCaches => Set<AIElementCache>();
    public DbSet<ApiDefinition> ApiDefinitions => Set<ApiDefinition>();
    public DbSet<MockDefinition> MockDefinitions => Set<MockDefinition>();
    // 迭代 F·P2-9：压测场景（k6），独立于功能执行流水线
    public DbSet<LoadTestScenario> LoadTestScenarios => Set<LoadTestScenario>();
    public DbSet<LoadTestScenarioCase> LoadTestScenarioCases => Set<LoadTestScenarioCase>();
    public DbSet<LoadTestRun> LoadTestRuns => Set<LoadTestRun>();
    public DbSet<SystemConfig> SystemConfigs => Set<SystemConfig>();
    public DbSet<Environment> Environments => Set<Environment>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // 迭代 C：脚本录制会话
    public DbSet<RecorderSession> RecorderSessions => Set<RecorderSession>();

    // 迭代 C：共享步骤组
    public DbSet<SharedStepGroup> SharedStepGroups => Set<SharedStepGroup>();

    // 测试计划（验收过程）
    public DbSet<Defect> Defects => Set<Defect>();
    public DbSet<DefectCase> DefectCases => Set<DefectCase>();
    public DbSet<DefectOccurrence> DefectOccurrences => Set<DefectOccurrence>();

    // 需求（覆盖统计锚点）
    public DbSet<Requirement> Requirements => Set<Requirement>();

    // 项目级 API Token（CI/外部系统按项目触发）
    public DbSet<ProjectApiToken> ProjectApiTokens => Set<ProjectApiToken>();

    // 用例扩展字段定义（按项目配置）
    public DbSet<CustomFieldDef> CustomFieldDefs => Set<CustomFieldDef>();

    // 通用评论（用例/缺陷/计划）
    public DbSet<Comment> Comments => Set<Comment>();

    // 站内消息（消息中心）
    public DbSet<InAppNotification> InAppNotifications => Set<InAppNotification>();

    public DbSet<TestPlan> TestPlans => Set<TestPlan>();
    public DbSet<TestPlanItem> TestPlanItems => Set<TestPlanItem>();
    public DbSet<TestPlanRound> TestPlanRounds => Set<TestPlanRound>();
    public DbSet<PlanRoundCase> PlanRoundCases => Set<PlanRoundCase>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username).IsUnique();
            entity.Property(u => u.Username).HasMaxLength(50);
            entity.Property(u => u.DisplayName).HasMaxLength(100);
            // 可空且不加唯一约束：见 User.Email 的注释（同一邮箱可能对应多个账号）
            entity.Property(u => u.Email).HasMaxLength(200);
            // SSO 绑定：可空 + (SsoProvider, SsoSubject) 唯一——Postgres 对 NULL 不判重，
            // 纯密码账号（两列皆 NULL）互不冲突
            entity.Property(u => u.SsoProvider).HasMaxLength(50);
            entity.Property(u => u.SsoSubject).HasMaxLength(200);
            entity.HasIndex(u => new { u.SsoProvider, u.SsoSubject }).IsUnique();

            // 执行节点登记：名字唯一，重启的同名节点原地覆盖（Upsert by Name）
            modelBuilder.Entity<ExecutionNode>(entity =>
            {
                entity.HasIndex(n => n.Name).IsUnique();
                entity.Property(n => n.Name).HasMaxLength(100);
                entity.Property(n => n.MachineName).HasMaxLength(100);
                entity.Property(n => n.InstanceId).HasMaxLength(150);
                entity.Property(n => n.Version).HasMaxLength(50);
            });
            // 迭代 C：角色 / 启用状态 / 会话版本。数据库默认值必须与实体初始值一致——
            // 迁移会新增列到已有行，若不写默认值，老行会取到 CLR 零值（= Admin / false）
            //
            // 哨兵值 -1 是必须的：UserRole.Admin = 0 恰好是 CLR 默认值，
            // 而 EF Core 对"有数据库默认值 + 属性等于 CLR 默认值"的属性会**直接省略该列**，
            // 于是「新建管理员」会被数据库默认值改写成 Viewer——静默降权。
            // 把哨兵设成永不合法的 -1，EF 就会把 0 当成真实值写进去。
            entity.Property(u => u.Role).HasDefaultValue(UserRole.Viewer).HasSentinel((UserRole)(-1));
            entity.Property(u => u.IsActive).HasDefaultValue(true);
            entity.Property(u => u.TokenVersion).HasDefaultValue(0);
            entity.Property(u => u.LastLoginIp).HasMaxLength(64);
            entity.HasIndex(u => u.Role);
            entity.HasOne(u => u.CreatedBy).WithMany().HasForeignKey(u => u.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // 迭代 C：审计日志。查询模式固定为「时间倒序 + 维度过滤」，因此索引围绕这两者建
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(l => l.Username).HasMaxLength(50);
            entity.Property(l => l.UserRole).HasMaxLength(30);
            entity.Property(l => l.Action).HasMaxLength(50);
            entity.Property(l => l.ResourceType).HasMaxLength(50);
            entity.Property(l => l.ResourceName).HasMaxLength(300);
            entity.Property(l => l.Method).HasMaxLength(10);
            entity.Property(l => l.Path).HasMaxLength(500);
            entity.Property(l => l.Detail).HasMaxLength(2000);
            // 比 Detail 宽一倍：创建类接口返回的是完整 DTO（带步骤的用例动辄上千字符），
            // 2000 会在正常业务下频繁截断，反而让"响应结果"失去证据价值
            entity.Property(l => l.ResponseBody).HasMaxLength(4000);
            entity.Property(l => l.IpAddress).HasMaxLength(64);
            entity.Property(l => l.UserAgent).HasMaxLength(300);
            entity.HasIndex(l => l.CreatedAt);
            entity.HasIndex(l => new { l.ResourceType, l.ResourceId });
            entity.HasIndex(l => new { l.Username, l.CreatedAt });
        });

        // 站内消息：查询永远是「我的消息，按时间倒序」和「我的未读数」两种，
        // 索引就照这两条查询建，不额外加用不上的组合
        modelBuilder.Entity<InAppNotification>(entity =>
        {
            entity.Property(n => n.Title).HasMaxLength(200);
            entity.Property(n => n.Body).HasMaxLength(1000);
            entity.Property(n => n.LinkUrl).HasMaxLength(500);
            entity.Property(n => n.LinkLabel).HasMaxLength(30);
            entity.Property(n => n.SourceType).HasMaxLength(50);
            entity.HasIndex(n => new { n.UserId, n.IsRead, n.CreatedAt });
            entity.HasIndex(n => new { n.UserId, n.CreatedAt });
            // 接收人删号后消息一并清掉：没有收件人的消息留着也投不出去
            entity.HasOne(n => n.User).WithMany().HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 迭代 C：脚本录制会话。会话按创建时间倒序列表查询，且维护任务按状态筛选
        modelBuilder.Entity<RecorderSession>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.Browser).HasMaxLength(50);
            entity.Property(s => s.OutputPath).HasMaxLength(500);
            entity.Property(s => s.CreatedByName).HasMaxLength(100);
            entity.Property(s => s.SavedTestCaseName).HasMaxLength(200);
            entity.Property(s => s.LastError).HasMaxLength(500);
            entity.HasIndex(s => s.CreatedAt);
            entity.HasIndex(s => new { s.Status, s.LastPolledAt });
            entity.HasOne(s => s.Project).WithMany().HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // 迭代 C：共享步骤组。步骤随组一起读写，因此 Items 走级联；
        // TestStep.SharedGroupId 用 SetNull：删组不该连坐删掉引用它的用例步骤，
        // 而是让那些步骤变成"孤儿引用"，运行时由展开器记录告警并跳过（用户能看见并去修）。
        modelBuilder.Entity<SharedStepGroup>(entity =>
        {
            entity.Property(g => g.Name).HasMaxLength(200);
            entity.Property(g => g.Description).HasMaxLength(2000);
            entity.HasMany(g => g.Items)
                .WithOne(i => i.Group)
                .HasForeignKey(i => i.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.OwnsMany(g => g.Variables, owned => owned.ToJson());
            entity.HasIndex(g => g.ProjectId);
            entity.HasIndex(g => new { g.ProjectId, g.Name });
        });

        modelBuilder.Entity<SharedStepItem>(entity =>
        {
            entity.HasOne(i => i.Group).WithMany(g => g.Items)
                .HasForeignKey(i => i.GroupId)
                .OnDelete(DeleteBehavior.Cascade);
            // StepConfig 同时被 TestStep 和 SharedStepItem 用作 jsonb 自有类型，
            // 同一个 CLR 类型用在两处时必须明确各自的映射，否则 EF 无法定位
            // "共用类型实体"的目标而报错（The navigation 'Config' must be configured ... explicit name）
            entity.OwnsOne(i => i.Config, config =>
            {
                config.ToJson();
                config.OwnsOne(c => c.Selector);
                config.OwnsMany(c => c.Headers);
            });
        });

        modelBuilder.Entity<Project>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(100);
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.HasOne(p => p.CreatedBy).WithMany().HasForeignKey(p => p.CreatedById);

            // 项目负责人 / 测试负责人。Project 到 User 有三条外键，**每一条都必须显式写出来**——
            // 靠约定推断会报「无法确定关系」。
            // 删除策略用 SetNull：人离职删账号不该连带删掉项目（项目是用例的容器），
            // 只是这两个字段变空，后续发验收邮件时会因为「找不到收件人」而跳过。
            entity.HasOne(p => p.Manager).WithMany().HasForeignKey(p => p.ManagerId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(p => p.TestOwner).WithMany().HasForeignKey(p => p.TestOwnerId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(p => p.TestOwnerId);
            // Project→User 的第 4 条外键（CreatedBy / Manager / TestOwner / DeveloperOwner）。
            // 每条都必须这样显式写出来：靠约定推断会直接报"无法确定关系"——一个实体指向同一张表多次时，
            // EF 没有依据猜"这一条导航对应哪个外键"。
            entity.HasOne(p => p.DeveloperOwner).WithMany().HasForeignKey(p => p.DeveloperOwnerId)
                .OnDelete(DeleteBehavior.SetNull);

            // 项目名称唯一。应用层（ProjectApiExtensions）已经做了 Trim + 查重并返回 409，
            // 那个校验负责的是"给用户一句能看懂的中文提示"；这里再加唯一索引，负责的是
            // "并发下不能出现两个同名项目"——「先查后写」在两个人同时提交时双方都会查到"不存在"，
            // 只有数据库索引拦得住这种竞态。两层职责不同，缺一不可。
            //
            // 之所以能直接加：Project 没有软删除字段（删除是物理删除），
            // 不存在"已删行的名称把新项目挡住"的问题。
            entity.HasIndex(p => p.Name).IsUnique();

            // M8 Agent 自愈项目级开关（系统级总开关在 SystemConfig）。默认全部关闭。
            entity.Property(p => p.AgentLoopEnabled).HasDefaultValue(false);
            entity.Property(p => p.TreatAgentHealedAsPass).HasDefaultValue(false);
        });

        // 迭代 E·①：项目成员（项目级授权）。唯一索引保证同一用户在同一个项目只有一个角色；
        // 删项目/删用户都级联清成员关系（成员关系是附属数据，不构成"留痕"）。
        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.HasIndex(m => new { m.ProjectId, m.UserId }).IsUnique();
            // 按用户反查「我参与了哪些项目」是列表过滤的最常用路径
            entity.HasIndex(m => m.UserId);
            entity.HasOne(m => m.Project).WithMany().HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestCase>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(200);
            entity.Property(t => t.Browser).HasMaxLength(50);
            entity.Property(t => t.CaseCode).HasMaxLength(50);
            entity.Property(t => t.Module).HasMaxLength(100);
            entity.Property(t => t.ExpectedResult).HasMaxLength(2000);
            // 用例级网络规则：JSON 数组。用 jsonb 而不是 text，是为了查库排障时能直接按规则内容过滤
            entity.Property(t => t.NetworkRules).HasColumnType("jsonb");
            entity.Property(t => t.VisualIgnoreRegions).HasColumnType("jsonb");
            entity.Property(t => t.CustomFields).HasColumnType("jsonb");
            // 评审人导航：详情页展示「谁评审的」。Restrict——用户注销不应连带清掉评审留痕
            entity.HasOne(t => t.ReviewedBy).WithMany()
                .HasForeignKey(t => t.ReviewedById).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(t => new { t.ProjectId, t.Status });
            entity.HasIndex(t => new { t.ProjectId, t.Module });
            entity.HasIndex(t => new { t.ProjectId, t.IsFlaky });
            entity.Property(t => t.FailFast).HasDefaultValue(false);
            entity.Property(t => t.VisualEnabled).HasDefaultValue(false);
            entity.Property(t => t.VisualThreshold).HasDefaultValue(0.01);
            entity.HasOne(t => t.Parent).WithMany().HasForeignKey(t => t.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
            // 数据集被删除时用例保留，只是不再参数化
            entity.HasOne(t => t.DataSet).WithMany().HasForeignKey(t => t.DataSetId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(t => t.DataSetId);

            // 需求删除时用例保留（覆盖统计口径：需求没了就回到"未关联"）
            entity.HasOne(t => t.Requirement).WithMany(r => r.TestCases)
                .HasForeignKey(t => t.RequirementId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(t => new { t.RequirementId, t.Status });

            // 软删除过滤器：已删除的用例默认不可见。
            // 原来过滤的是 `Status != Deprecated`——把删除塞进了状态字段，
            // 现在软删除拆成独立的 DeletedAt（对应设计文档 4.3 的"废弃不可见"意图）。
            entity.HasQueryFilter(t => t.DeletedAt == null);
        });

        // 用例版本快照。刻意**不加**软删除过滤：用例被删时快照跟着级联删掉，
        // 不需要"隐藏但保留"这一层。
        modelBuilder.Entity<TestCaseVersion>(entity =>
        {
            entity.HasOne(v => v.TestCase)
                .WithMany()
                .HasForeignKey(v => v.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);

            // 同一用例的版本号唯一——并发保存时靠它兜底，而不是靠应用层"先查后写"
            entity.HasIndex(v => new { v.TestCaseId, v.Version }).IsUnique();
            entity.Property(v => v.Snapshot).HasColumnType("jsonb");
        });

        modelBuilder.Entity<TestStep>(entity =>
        {
            entity.HasIndex(s => new { s.TestCaseId, s.StepOrder });
            // JSONB 映射（Npgsql 对 ToJson 默认使用 jsonb 列类型）
            entity.OwnsOne(s => s.Config, config =>
            {
                config.ToJson();
                config.OwnsOne(c => c.Selector);
                config.OwnsMany(c => c.Headers);
            });
            // 迭代 C：共享步骤引用。删除组时置空引用而不是删掉步骤——
            // 用例的步骤顺序由用户编排，静默少一步比留一个可见的失效引用更糟。
            entity.HasOne(s => s.SharedGroup).WithMany()
                .HasForeignKey(s => s.SharedGroupId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(s => s.SharedGroupId);
            entity.OwnsMany(s => s.SharedVariables!, owned => owned.ToJson());
        });

        modelBuilder.Entity<Schedule>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.CronExpression).HasMaxLength(100);
            entity.Property(s => s.Module).HasMaxLength(100);
            entity.Property(s => s.Priority).HasMaxLength(10);
            entity.Property(s => s.LastError).HasMaxLength(500);
            // 指定用例集合以逗号分隔存储，保持实体为 List<Guid>
            var testCaseIdsComparer = new ValueComparer<List<Guid>>(
                (a, b) => a == null || b == null ? a == b : a.SequenceEqual(b),
                v => v.Aggregate(0, (acc, id) => HashCode.Combine(acc, id.GetHashCode())),
                v => v.ToList());
            entity.Property(s => s.TestCaseIds)
                .HasConversion(
                    v => v == null ? null : string.Join(',', v),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList())
                .Metadata.SetValueComparer(testCaseIdsComparer);
            // 浏览器矩阵同样以逗号分隔存储，保持实体为 List<string>
            var browsersComparer = new ValueComparer<List<string>>(
                (a, b) => a == null || b == null ? a == b : a.SequenceEqual(b),
                v => v.Aggregate(0, (acc, item) => HashCode.Combine(acc, item)),
                v => v.ToList());
            entity.Property(s => s.Browsers)
                .HasConversion(
                    v => v == null ? null : string.Join(',', v),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList())
                .HasMaxLength(100);
            entity.Property(s => s.Browsers).Metadata.SetValueComparer(browsersComparer);
            // 计划范围同样以逗号分隔存储，保持实体为 List<Guid>
            // （规模假设：定时任务通常是个位到百位量级，按 CSV 存足够；
            //   若将来需要「按计划反查引用它的定时任务」变频繁，可改为 uuid[] 原生数组）
            var testPlanIdsComparer = new ValueComparer<List<Guid>>(
                (a, b) => a == null || b == null ? a == b : a.SequenceEqual(b),
                v => v.Aggregate(0, (acc, id) => HashCode.Combine(acc, id.GetHashCode())),
                v => v.ToList());
            entity.Property(s => s.TestPlanIds)
                .HasConversion(
                    v => v == null ? null : string.Join(',', v),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : v.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList())
                .Metadata.SetValueComparer(testPlanIdsComparer);
            entity.Property(s => s.ExpandDataSets).HasDefaultValue(true);
            entity.HasIndex(s => new { s.Enabled, s.NextRunAt });
            entity.HasOne(s => s.Project).WithMany().HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(s => s.Environment).WithMany().HasForeignKey(s => s.EnvironmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Execution>(entity =>
        {
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
            entity.Property(e => e.CommitSha).HasMaxLength(64);
            entity.Property(e => e.Branch).HasMaxLength(200);
            entity.Property(e => e.BuildNumber).HasMaxLength(100);
            entity.Property(e => e.TriggerSource).HasMaxLength(100);
            entity.Property(e => e.ClaimedBy).HasMaxLength(120);
            entity.Property(e => e.BrowserName).HasMaxLength(50);
            entity.Property(e => e.DataSetRowLabel).HasMaxLength(300);
            // 编排跳过的原因（前缀未通过 / 快停），500 足够放下「前置用例「xxx」未通过」这类文案
            entity.Property(e => e.SkipReason).HasMaxLength(500);
            // 本次执行的变量覆盖（jsonb）
            entity.Property(e => e.Variables)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions))
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>?>(
                    (a, b) => a == null || b == null ? a == b : a.Count == b.Count && !a.Except(b).Any(),
                    v => v == null ? 0 : v.Aggregate(0, (acc, kv) => HashCode.Combine(acc, kv.Key, kv.Value)),
                    v => v == null ? null : new Dictionary<string, string>(v)));
            // 心跳扫描（清理僵死执行）走这个索引
            entity.HasIndex(e => new { e.Status, e.HeartbeatAt });
            entity.HasIndex(e => e.TestCaseId);
            // 报告/列表按时间过滤与「每用例最新一条」（安全性能审查 P2）：
            // IX_Executions_Status_CreatedAt 引导列是 Status，纯时间过滤用不上、实测全表扫描
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.TestCaseId, e.CreatedAt });
            // 套件运行聚合查询
            entity.HasIndex(e => e.SuiteRunId);
            // 计划轮次聚合查询（轮次通过率实时算，走这个索引）
            entity.HasIndex(e => e.PlanRoundId);
            entity.HasOne(e => e.TestCase).WithMany(t => t.Executions)
                .HasForeignKey(e => e.TestCaseId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(e => e.TriggeredBy).WithMany().HasForeignKey(e => e.TriggeredById);
            entity.HasOne(e => e.Environment).WithMany()
                .HasForeignKey(e => e.EnvironmentId).OnDelete(DeleteBehavior.SetNull);
            entity.OwnsOne(e => e.EnvironmentSnapshot, snapshot =>
            {
                snapshot.ToJson();
                snapshot.Property(s => s.Name).HasMaxLength(100);
                snapshot.Property(s => s.BaseUrl).HasMaxLength(500);
            });
            // M8 Agent 自愈：新列非空，显式默认值（迁移据此回填历史行）
            entity.Property(e => e.AgentLoopCount).HasDefaultValue(0);
            entity.Property(e => e.AgentHealed).HasDefaultValue(false);
            entity.Property(e => e.AgentBudgetUsed).HasDefaultValue(0);
        });

        // M8 Agent：一次「失败 → 归因 → 修复 → 重跑」尝试的轨迹，挂在 Execution 下。
        // 数据只在"执行失败且自愈开启"时产生；结论分两层：AgentAttempt.Result（尝试级）
        // 与 Execution.AgentFinalVerdict（执行级）。
        modelBuilder.Entity<AgentAttempt>(entity =>
        {
            entity.HasIndex(a => a.ExecutionId);
            // 同一次执行内 AttemptNumber 唯一——并发下防重（应用层不做"先查后写"）
            entity.HasIndex(a => new { a.ExecutionId, a.AttemptNumber }).IsUnique();
            // 证据/动作按项目既有约定用 jsonb（查库排障时可按内容过滤）
            entity.Property(a => a.FailureEvidence).HasColumnType("jsonb");
            entity.Property(a => a.AppliedActions).HasColumnType("jsonb");
            // 提议的动作（供「采纳」落库用）。与 AppliedActions 分开存：后者对需审批的
            // 尝试必然为空（循环在应用动作前就 break 了），采纳流程不能依赖它。
            entity.Property(a => a.ProposedFixes).HasColumnType("jsonb");
            // LLM 原始返回：应用层截断到 8000（省略号计入上限，防 Postgres 22001 静默丢记录）
            entity.Property(a => a.DiagnosisRaw).HasMaxLength(8000);
            entity.Property(a => a.FixSummary).HasMaxLength(2000);
            entity.Property(a => a.FailureAfterFix).HasMaxLength(2000);
            entity.Property(a => a.LlmModel).HasMaxLength(100);
            entity.HasOne(a => a.Execution).WithMany()
                .HasForeignKey(a => a.ExecutionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ---------------- 迭代 F·P2-9：压测场景（k6）
        modelBuilder.Entity<LoadTestScenario>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.Description).HasMaxLength(1000);
            entity.Property(s => s.TargetBaseUrl).HasMaxLength(500);
            entity.Property(s => s.ScriptHash).HasMaxLength(64);
            entity.HasIndex(s => s.ProjectId);
            // 同一项目内重名没有意义，且会让「选哪个压测场景」变得含糊
            entity.HasIndex(s => new { s.ProjectId, s.Name }).IsUnique();
            entity.HasOne(s => s.Project).WithMany()
                .HasForeignKey(s => s.ProjectId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(s => s.Environment).WithMany()
                .HasForeignKey(s => s.EnvironmentId).OnDelete(DeleteBehavior.SetNull);
            // 文档被删时置空引用而不是删场景：场景本身（名称/负载/阈值）仍然有效，
            // 只是暂时无法重新生成脚本——比静默丢一个场景好。
            entity.HasOne(s => s.ApiDefinition).WithMany()
                .HasForeignKey(s => s.ApiDefinitionId).OnDelete(DeleteBehavior.SetNull);

            // 选中的 OpenAPI 操作（jsonb 字符串数组）
            var operationsComparer = new ValueComparer<List<string>>(
                (a, b) => a == null || b == null ? a == b : a.SequenceEqual(b),
                v => v.Aggregate(0, (acc, item) => HashCode.Combine(acc, item)),
                v => v.ToList());
            entity.Property(s => s.Operations)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                    // 属性声明为非空 List，反序列化出 null 会破坏实体不变式（且 NRT 会告警），故兜空集合
                    v => string.IsNullOrEmpty(v)
                        ? new List<string>()
                        : JsonSerializer.Deserialize<List<string>>(v, JsonOptions) ?? new List<string>())
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(operationsComparer);

            // 负载配置与阈值：字段随 executor 互斥，列化会产出一片恒 NULL 列，故整体走 jsonb
            entity.OwnsOne(s => s.Profile, profile =>
            {
                profile.ToJson();
                profile.OwnsMany(p => p.Stages);
            });
            entity.OwnsMany(s => s.Thresholds, thresholds => thresholds.ToJson());

            // 变量覆盖：与 Execution.Variables 同一套写法（jsonb + ValueComparer）
            entity.Property(s => s.Variables)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, JsonOptions),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonOptions))
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>?>(
                    (a, b) => a == null || b == null ? a == b : a.Count == b.Count && !a.Except(b).Any(),
                    v => v == null ? 0 : v.Aggregate(0, (acc, kv) => HashCode.Combine(acc, kv.Key, kv.Value)),
                    v => v == null ? null : new Dictionary<string, string>(v)));
        });

        modelBuilder.Entity<LoadTestScenarioCase>(entity =>
        {
            // 同一场景内同一用例只出现一次（顺序由 Order 表达）
            entity.HasIndex(c => new { c.ScenarioId, c.TestCaseId }).IsUnique();
            // 支持「这个用例被哪些压测场景引用」（用例删除前的提示）
            entity.HasIndex(c => c.TestCaseId);
            entity.HasOne(c => c.Scenario).WithMany(s => s.Cases)
                .HasForeignKey(c => c.ScenarioId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(c => c.TestCase).WithMany()
                .HasForeignKey(c => c.TestCaseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<LoadTestRun>(entity =>
        {
            entity.Property(r => r.ClaimedBy).HasMaxLength(120);
            entity.Property(r => r.TargetBaseUrl).HasMaxLength(500);
            entity.Property(r => r.ScriptHash).HasMaxLength(64);
            entity.Property(r => r.ScriptArtifactKey).HasMaxLength(300);
            entity.Property(r => r.SummaryArtifactKey).HasMaxLength(300);
            entity.Property(r => r.LogArtifactKey).HasMaxLength(300);
            // k6 的 version 输出是整行，如
            // "k6 v2.3.0 (commit/e088784614, go1.27.1, linux/amd64)"（59 字符）。
            // 原来给 50 会让**整条运行记录保存失败**（22001 value too long），
            // 表现为运行直接变 Error 且指标全丢——比版本号被截断严重得多，故留足余量。
            entity.Property(r => r.K6Version).HasMaxLength(200);
            entity.Property(r => r.ErrorMessage).HasMaxLength(2000);
            // 抢占队列扫描（与 Executions 同构）
            entity.HasIndex(r => new { r.Status, r.CreatedAt });
            // 僵尸运行回收（执行器重启会带走 k6 子进程，必须能扫出来）
            entity.HasIndex(r => new { r.Status, r.HeartbeatAt });
            // 场景下的历史运行列表 / 趋势
            entity.HasIndex(r => new { r.ScenarioId, r.CreatedAt });
            // 项目作用域反查与按项目统计（冗余 ProjectId 就是为了这两件事）
            entity.HasIndex(r => new { r.ProjectId, r.CreatedAt });
            entity.HasOne(r => r.Scenario).WithMany(s => s.Runs)
                .HasForeignKey(r => r.ScenarioId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(r => r.TriggeredBy).WithMany()
                .HasForeignKey(r => r.TriggeredById).OnDelete(DeleteBehavior.SetNull);
            entity.OwnsMany(r => r.ThresholdResults, results => results.ToJson());
        });

        modelBuilder.Entity<ExecutionResult>(entity =>
        {
            entity.HasIndex(r => new { r.ExecutionId, r.StepOrder });
            entity.Property(r => r.BaselineImageUrl).HasMaxLength(300);
            entity.Property(r => r.DiffImageUrl).HasMaxLength(300);
            entity.HasOne(r => r.TestStep).WithMany().HasForeignKey(r => r.TestStepId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.OwnsOne(r => r.StepSnapshot, snapshot =>
            {
                snapshot.ToJson();
                snapshot.OwnsOne(c => c.Selector);
                snapshot.OwnsMany(c => c.Headers);
            });
            // M8：失败步骤采集的页面可交互元素快照（jsonb，供 Agent 归因产出具体定位符）
            entity.Property(r => r.ElementSnapshot).HasColumnType("jsonb");
        });

        modelBuilder.Entity<AIElementCache>(entity =>
        {
            entity.HasIndex(e => new { e.ProjectId, e.PageUrl, e.ElementDescription }).IsUnique();
            entity.Property(e => e.PageUrl).HasMaxLength(500);
            entity.Property(e => e.ElementDescription).HasMaxLength(300);
            // pgvector：维度与 ElementEmbedder.Dimensions 必须一致，改维度=要重建列
            entity.Property(e => e.Embedding).HasColumnType("vector(512)");
            entity.HasOne(e => e.Project).WithMany().HasForeignKey(e => e.ProjectId);
            entity.OwnsMany(e => e.SelectorHistory, h => h.ToJson());
        });

        modelBuilder.Entity<ApiDefinition>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.BaseUrl).HasMaxLength(500);
            entity.Property(e => e.Spec).HasColumnType("text");
            entity.HasIndex(e => e.ProjectId);
        });

        modelBuilder.Entity<MockDefinition>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(200);
            entity.Property(e => e.BasePath).HasMaxLength(200);
            entity.Property(e => e.OwnerNode).HasMaxLength(100);
            entity.Property(e => e.Spec).HasColumnType("text");
            entity.HasIndex(e => e.ProjectId);
        });

        modelBuilder.Entity<SystemConfig>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.AiBaseUrl).HasMaxLength(500);
            entity.Property(c => c.AiModel).HasMaxLength(200);
            entity.Property(c => c.WebhookToken).HasMaxLength(500);
            // 通知渠道配置
            entity.Property(c => c.NotifyWecomWebhook).HasMaxLength(500);
            entity.Property(c => c.NotifyDingtalkWebhook).HasMaxLength(500);
            entity.Property(c => c.NotifyFeishuWebhook).HasMaxLength(500);
            entity.Property(c => c.SmtpHost).HasMaxLength(200);
            entity.Property(c => c.SmtpUser).HasMaxLength(200);
            entity.Property(c => c.SmtpPassword).HasMaxLength(200);
            entity.Property(c => c.MailTo).HasMaxLength(1000);
            // 数据库默认值：与实体初始值保持一致，避免老数据行在这几列上取到 CLR 零值
            entity.Property(c => c.NotifyOnFailureOnly).HasDefaultValue(true);
            entity.Property(c => c.SmtpPort).HasDefaultValue(465);
            entity.Property(c => c.SmtpUseSsl).HasDefaultValue(true);
            entity.Property(c => c.NotifyPlanResultEmail).HasDefaultValue(true);
        });

        modelBuilder.Entity<Environment>(entity =>
        {
            entity.Property(e => e.Name).HasMaxLength(100);
            entity.Property(e => e.BaseUrl).HasMaxLength(500);
            entity.Property(e => e.LoginUrl).HasMaxLength(500);
            entity.Property(e => e.LoginUsername).HasMaxLength(200);
            entity.Property(e => e.LoginPassword).HasMaxLength(200);
            entity.Property(e => e.LoginSuccessIndicator).HasMaxLength(300);
            entity.Property(e => e.Browser).HasMaxLength(50);
            entity.HasIndex(e => e.ProjectId);
            entity.HasOne(e => e.Project).WithMany(p => p.Environments)
                .HasForeignKey(e => e.ProjectId).OnDelete(DeleteBehavior.Cascade);
        });

        // ------------------------------ 迭代 B：数据集 / 套件 / 视觉基线 / 报告分享

        modelBuilder.Entity<DataSet>(entity =>
        {
            entity.Property(d => d.Name).HasMaxLength(200);
            entity.Property(d => d.Description).HasMaxLength(2000);
            // 列名用 PostgreSQL 原生 text[]；数据行用 jsonb（行内是「列名 → 值」）
            entity.Property(d => d.Columns).HasColumnType("text[]");
            entity.Property(d => d.Rows)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonOptions),
                    v => string.IsNullOrEmpty(v)
                        ? new List<Dictionary<string, string>>()
                        : JsonSerializer.Deserialize<List<Dictionary<string, string>>>(v, JsonOptions)!
                )
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(new ValueComparer<List<Dictionary<string, string>>>(
                    (a, b) => JsonSerializer.Serialize(a, JsonOptions) == JsonSerializer.Serialize(b, JsonOptions),
                    v => JsonSerializer.Serialize(v, JsonOptions).GetHashCode(),
                    v => JsonSerializer.Deserialize<List<Dictionary<string, string>>>(
                        JsonSerializer.Serialize(v, JsonOptions), JsonOptions)!));
            entity.HasIndex(d => new { d.ProjectId, d.Name });
            entity.HasOne(d => d.Project).WithMany().HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestSuite>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(200);
            entity.Property(s => s.Description).HasMaxLength(1000);
            entity.Property(s => s.LastError).HasMaxLength(500);
            entity.HasIndex(s => new { s.ProjectId, s.Kind });
            entity.HasOne(s => s.Project).WithMany().HasForeignKey(s => s.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(s => s.Environment).WithMany().HasForeignKey(s => s.EnvironmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // 测试计划。删除计划连带删范围与轮次：轮次离开计划没有意义，
        // 而计划本身在有轮次时会被业务层拒绝删除（只能归档），所以级联不会误删验收材料。
        modelBuilder.Entity<TestPlan>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(200);
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.ReleaseName).HasMaxLength(100);
            entity.Property(p => p.LastError).HasMaxLength(500);
            entity.HasIndex(p => new { p.ProjectId, p.Status });
            entity.HasIndex(p => p.ReleaseName);
            entity.HasOne(p => p.Project).WithMany().HasForeignKey(p => p.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(p => p.Environment).WithMany().HasForeignKey(p => p.EnvironmentId)
                .OnDelete(DeleteBehavior.SetNull);
            // 负责人被删除时置空而不是连带删计划：计划是验收材料，不能因为人员离职而消失
            entity.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.SetNull);
            // 需求被删时计划保留（回到未关联状态，与 TestCase→Requirement 同口径）
            entity.HasOne(p => p.Requirement).WithMany(r => r.TestPlans).HasForeignKey(p => p.RequirementId)
                .OnDelete(DeleteBehavior.SetNull);
            var browsersComparer = new ValueComparer<List<string>>(
                (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
                v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
                v => v.ToList());
            entity.Property(p => p.Browsers).HasConversion(
                v => v == null ? null : string.Join(',', v),
                v => string.IsNullOrWhiteSpace(v)
                    ? null
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                browsersComparer);
        });

        modelBuilder.Entity<TestPlanItem>(entity =>
        {
            entity.HasIndex(i => new { i.PlanId, i.Order });
            // 同一用例在一个计划里只出现一次（防止导入时重复叠加）
            entity.HasIndex(i => new { i.PlanId, i.TestCaseId }).IsUnique();
            entity.HasOne(i => i.Plan).WithMany(p => p.Items).HasForeignKey(i => i.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(i => i.TestCase).WithMany().HasForeignKey(i => i.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestPlanRound>(entity =>
        {
            entity.Property(r => r.TriggerSource).HasMaxLength(300);
            entity.Property(r => r.Error).HasMaxLength(1000);
            entity.HasIndex(r => new { r.PlanId, r.RoundNo }).IsUnique();
            entity.HasOne(r => r.Plan).WithMany(p => p.Rounds).HasForeignKey(r => r.PlanId)
                .OnDelete(DeleteBehavior.Cascade);
            var roundBrowsersComparer = new ValueComparer<List<string>>(
                (a, b) => a == null ? b == null : b != null && a.SequenceEqual(b),
                v => v.Aggregate(0, (acc, s) => HashCode.Combine(acc, s.GetHashCode())),
                v => v.ToList());
            entity.Property(r => r.Browsers).HasConversion(
                v => v == null ? null : string.Join(',', v),
                v => string.IsNullOrWhiteSpace(v)
                    ? null
                    : v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                roundBrowsersComparer);

            // 同一计划同时只能有一个进行中的轮次。
            // 必须放在数据库层：应用层「先查后写」在并发触发下会双双通过，
            // 于是同一个计划出现两个 Running 轮次，「第 N 轮通过率」就失去意义了。
            // 部分唯一索引只约束 Status = Running（0），已完成的轮次可以随意多。
            entity.HasIndex(r => r.PlanId)
                .IsUnique()
                .HasDatabaseName("IX_TestPlanRounds_OneRunningPerPlan")
                .HasFilter("\"Status\" = 0");
        });

        modelBuilder.Entity<PlanRoundCase>(entity =>
        {
            entity.Property(c => c.TestCaseName).HasMaxLength(200);
            entity.Property(c => c.Module).HasMaxLength(100);
            entity.HasIndex(c => new { c.RoundId, c.Order });
            entity.HasOne(c => c.Round).WithMany().HasForeignKey(c => c.RoundId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestSuiteCase>(entity =>
        {
            entity.HasIndex(c => new { c.SuiteId, c.TestCaseId }).IsUnique();
            entity.HasIndex(c => new { c.SuiteId, c.Order });
            entity.HasOne(c => c.Suite).WithMany(s => s.Cases).HasForeignKey(c => c.SuiteId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(c => c.TestCase).WithMany().HasForeignKey(c => c.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);
            // 前置用例：显式声明为「无导航 + SetNull」。不写导航属性是有意的——
            // TestSuiteCase 已经有一条指向 TestCases 的关系（TestCase），再多一条无导航的
            // 关系 EF 才能明确区分；前置用例被删除时置空（这条用例只是失去前置，不该被连带删掉）。
            entity.HasOne<TestCase>().WithMany()
                .HasForeignKey(c => c.DependsOnTestCaseId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<VisualBaseline>(entity =>
        {
            entity.Property(b => b.ImagePath).HasMaxLength(500);
            entity.Property(b => b.FilePath).HasMaxLength(500);
            entity.Property(b => b.Browser).HasMaxLength(50);
            // 基线按浏览器分存：唯一键加 Browser 维度（历史行 Browser 为 null，
            // PG 唯一索引对 NULL 互不冲突，旧数据不需要回填）
            entity.HasIndex(b => new { b.TestCaseId, b.StepOrder, b.Browser }).IsUnique();
            entity.HasOne(b => b.TestCase).WithMany().HasForeignKey(b => b.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Comment>(entity =>
        {
            entity.Property(c => c.Body).HasMaxLength(2000);
            // 列表页按挂载对象 + 时间倒序是最常用路径；Author 不做级联（评论保留，作者显示脱敏为"已注销"由读侧处理）
            entity.HasIndex(c => new { c.Target, c.TargetId, c.CreatedAt });
            entity.HasOne(c => c.Author).WithMany().HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CustomFieldDef>(entity =>
        {
            entity.Property(f => f.Name).HasMaxLength(50);
            // 同项目内字段名唯一：重复名字在编辑控件上无法区分
            entity.HasIndex(f => new { f.ProjectId, f.Name }).IsUnique();
            entity.HasOne(f => f.Project).WithMany().HasForeignKey(f => f.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReportShare>(entity =>
        {
            entity.Property(s => s.Token).HasMaxLength(64);
            entity.Property(s => s.Title).HasMaxLength(300);
            entity.HasIndex(s => s.Token).IsUnique();
            entity.HasIndex(s => new { s.Kind, s.RefId });
        });

        modelBuilder.Entity<ProjectApiToken>(entity =>
        {
            entity.Property(t => t.Name).HasMaxLength(100);
            // TokenHash 定长 64（SHA-256 hex），精确匹配走索引；不必唯一约束到数据库层
            // （哈希碰撞在 SHA-256 上不可行），但加索引让 Bearer 鉴权是 O(log n)
            entity.Property(t => t.TokenHash).HasMaxLength(64);
            entity.HasIndex(t => t.TokenHash);
            entity.Property(t => t.Prefix).HasMaxLength(12);
            entity.HasIndex(t => new { t.ProjectId, t.CreatedAt });
        });

        modelBuilder.Entity<Requirement>(entity =>
        {
            entity.Property(r => r.Title).HasMaxLength(300);
            entity.Property(r => r.ExternalKey).HasMaxLength(200);
            entity.Property(r => r.Priority).HasMaxLength(20);
            // 列表页按项目过滤是最常用路径
            entity.HasIndex(r => new { r.ProjectId, r.CreatedAt });
            entity.HasOne(r => r.Project).WithMany().HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Defect>(entity =>
        {
            entity.Property(d => d.Title).HasMaxLength(200);
            entity.Property(d => d.ExternalRef).HasMaxLength(500);
            entity.Property(d => d.ResolutionNote).HasMaxLength(500);
            // 列表页最常用过滤：项目 + 状态（存量/未闭环统计走同一索引）
            entity.HasIndex(d => new { d.ProjectId, d.Status });
            entity.HasIndex(d => new { d.ProjectId, d.Severity });
            entity.HasIndex(d => d.AssignedToId);
            entity.HasIndex(d => d.FoundInExecutionId);
            entity.HasOne(d => d.Project).WithMany().HasForeignKey(d => d.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.AssignedTo).WithMany().HasForeignKey(d => d.AssignedToId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.CreatedBy).WithMany().HasForeignKey(d => d.CreatedById)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.FoundInExecution).WithMany().HasForeignKey(d => d.FoundInExecutionId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.FoundInTestCase).WithMany().HasForeignKey(d => d.FoundInTestCaseId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(d => d.VerifiedBy).WithMany().HasForeignKey(d => d.VerifiedById)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DefectCase>(entity =>
        {
            entity.HasKey(dc => new { dc.DefectId, dc.TestCaseId });
            entity.HasOne(dc => dc.Defect).WithMany().HasForeignKey(dc => dc.DefectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(dc => dc.TestCase).WithMany().HasForeignKey(dc => dc.TestCaseId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DefectOccurrence>(entity =>
        {
            entity.HasIndex(o => new { o.DefectId, o.ExecutionId, o.StepOrder }).IsUnique();
            entity.HasOne(o => o.Defect).WithMany().HasForeignKey(o => o.DefectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(o => o.Execution).WithMany().HasForeignKey(o => o.ExecutionId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
