using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 审计盖章拦截器（M8/审计）。
///
/// 钉两条「会静默算错 / 静默写错」的约定：新增实体时，拦截器只能在字段
/// <b>尚未写入</b>的情况下补章，绝不能覆盖实体自己已显式设置的值——
/// <c>CreatedAt</c> 和 <c>CreatedById</c> 是同一条规则。
///
/// 背景一（CreatedAt）：AgentAttempt 在自愈循环里「尝试开始」时就记 <c>CreatedAt</c>，循环结束才
/// <c>SaveChanges</c>。拦截器若在 SaveChanges 那一刻统一盖章，CreatedAt 就会晚于
/// 业务上更早的 CompletedAt，于是自愈度量的「平均修复时长」恒为负数。
///
/// 背景二（CreatedById）：没有 HTTP 上下文的写入（种子数据、后台/Worker、测试直接造数）
/// 解析不出「当前用户」。旧实现把显式设好的创建人无条件改写成 null，落到非空 Guid 列
/// 就成了 <c>Guid.Empty</c>，插入直接撞外键违约（23503）——报错只说 FK，不说谁改的。
///
/// 这两条都没有编译期约束，改错一行不会有任何报错，所以必须钉住。
/// </summary>
public class AuditStampInterceptorTests
{
    /// <summary>
    /// 最小上下文：只映射 AgentAttempt / AuditProbeEntity 并掐掉导航。
    /// 不直接复用 TestDbContext —— 它的 OnModelCreating 里有 ToJson / pgvector 这类
    /// 关系型专有配置，内存 provider 建不起模型；而本测试只关心拦截器按「属性名约定」
    /// 的行为，不需要真实关系图。
    /// </summary>
    private sealed class MinimalContext : DbContext
    {
        public MinimalContext(DbContextOptions options) : base(options) { }
        public DbSet<AgentAttempt> AgentAttempts => Set<AgentAttempt>();
        public DbSet<AuditProbeEntity> Probes => Set<AuditProbeEntity>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<AgentAttempt>().Ignore(a => a.Execution);
    }

    private static MinimalContext NewContext(HttpContextAccessor? accessor = null)
    {
        var options = new DbContextOptionsBuilder<MinimalContext>()
            .UseInMemoryDatabase($"audit-stamp-{Guid.NewGuid():N}")
            .AddInterceptors(new AuditStampInterceptor(accessor ?? new HttpContextAccessor()))
            .Options;
        return new MinimalContext(options);
    }

    /// <summary>构造一个带指定登录用户标识的访问器（模拟已认证请求）。</summary>
    private static HttpContextAccessor AccessorFor(Guid userId)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            })),
        };
        return new HttpContextAccessor { HttpContext = ctx };
    }

    [Fact]
    public async Task 已显式设置的CreatedAt不被SaveChanges覆盖()
    {
        // 自愈循环的真实时序：尝试开始记 CreatedAt → 结束记 CompletedAt → 最后才落库
        var startedAt = new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);
        var completedAt = startedAt.AddSeconds(3);

        await using var db = NewContext();
        var attempt = new AgentAttempt
        {
            Id = Guid.NewGuid(),
            ExecutionId = Guid.NewGuid(),
            CreatedAt = startedAt,
            CompletedAt = completedAt,
            Result = AgentAttemptResult.Fixed,
        };
        db.AgentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        Assert.Equal(startedAt, attempt.CreatedAt);
        // 耗时必须是正的：CreatedAt 被覆盖成 SaveChanges 那一刻就会算成负数
        Assert.True(attempt.CompletedAt > attempt.CreatedAt,
            $"CompletedAt({attempt.CompletedAt:O}) 应晚于 CreatedAt({attempt.CreatedAt:O})");
    }

    [Fact]
    public async Task 未设置CreatedAt时由拦截器补章()
    {
        // 约定的另一半：没显式写过的仍然要补，别把「不覆盖」改成「从不盖章」
        var before = DateTime.UtcNow.AddSeconds(-1);

        await using var db = NewContext();
        var attempt = new AgentAttempt
        {
            Id = Guid.NewGuid(),
            ExecutionId = Guid.NewGuid(),
            CreatedAt = default, // 等价于没写
            Result = AgentAttemptResult.Skipped,
        };
        db.AgentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        Assert.True(attempt.CreatedAt >= before, $"CreatedAt 未被补章：{attempt.CreatedAt:O}");
        Assert.Equal(DateTimeKind.Utc, attempt.CreatedAt.Kind);
    }

    [Fact]
    public async Task 更新实体时不改动CreatedAt()
    {
        var startedAt = new DateTime(2026, 9, 22, 8, 0, 0, DateTimeKind.Utc);

        await using var db = NewContext();
        var attempt = new AgentAttempt
        {
            Id = Guid.NewGuid(),
            ExecutionId = Guid.NewGuid(),
            CreatedAt = startedAt,
            Result = AgentAttemptResult.Fixed,
        };
        db.AgentAttempts.Add(attempt);
        await db.SaveChangesAsync();

        attempt.FixSummary = "改了描述";
        await db.SaveChangesAsync();

        Assert.Equal(startedAt, attempt.CreatedAt);
    }

    // ---------------- CreatedById 与 CreatedAt 适用同一条「只补不覆盖」规则

    /// <summary>
    /// 显式指定的创建人不能被覆盖。
    ///
    /// 踩坑现场：MaintenanceTests 直接在库里造数据（没有 HTTP 上下文），
    /// 显式写了 <c>CreatedById = 已存在用户.Id</c>，旧实现无条件把这个值改写成本次解析出的用户
    /// ——无 HTTP 上下文时解析结果是 null，落到非空 Guid 列上就成了 Guid.Empty，
    /// 插入直接撞外键违约（23503），而报错只说 FK、不说谁改的，极难定位。
    /// </summary>
    [Fact]
    public async Task 已显式设置的CreatedById不被覆盖()
    {
        var owner = Guid.NewGuid();

        await using var db = NewContext();
        var probe = new AuditProbeEntity { Id = Guid.NewGuid(), CreatedById = owner };
        db.Probes.Add(probe);
        await db.SaveChangesAsync();

        Assert.Equal(owner, probe.CreatedById);
    }

    /// <summary>
    /// 无 HTTP 用户、也没显式写创建人时：保持列默认值，不能凭空写入一个不存在的用户。
    /// （写成 Guid.Empty 至少是「空」，若被填成其它随机值就会变成脏外键。）
    /// </summary>
    [Fact]
    public async Task 无登录用户且未显式设置时保持空值()
    {
        await using var db = NewContext();
        var probe = new AuditProbeEntity { Id = Guid.NewGuid() };
        db.Probes.Add(probe);
        await db.SaveChangesAsync();

        Assert.Equal(Guid.Empty, probe.CreatedById);
    }

    /// <summary>带登录用户标识时，未写过的 CreatedById 由拦截器补成该用户（别把「不覆盖」做成「从不盖章」）。</summary>
    [Fact]
    public async Task 有登录用户时未设置的CreatedById补为该用户()
    {
        var user = Guid.NewGuid();

        await using var db = NewContext(AccessorFor(user));
        var probe = new AuditProbeEntity { Id = Guid.NewGuid() };
        db.Probes.Add(probe);
        await db.SaveChangesAsync();

        Assert.Equal(user, probe.CreatedById);
    }

    /// <summary>已显式指定创建人时，登录用户也不能顶掉它（如「代他人创建」「以系统身份写入」）。</summary>
    [Fact]
    public async Task 有登录用户时已设置的CreatedById仍不被覆盖()
    {
        var owner = Guid.NewGuid();

        await using var db = NewContext(AccessorFor(Guid.NewGuid()));
        var probe = new AuditProbeEntity { Id = Guid.NewGuid(), CreatedById = owner };
        db.Probes.Add(probe);
        await db.SaveChangesAsync();

        Assert.Equal(owner, probe.CreatedById);
    }
}

/// <summary>
/// 只带审计字段的最小实体，用来验证拦截器「按属性名约定」的盖章规则。
/// 必须是 <c>AI.TestPlatform</c> 命名空间下的类型——拦截器会跳过其它命名空间的实体。
/// </summary>
internal sealed class AuditProbeEntity
{
    public Guid Id { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid? UpdatedById { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
