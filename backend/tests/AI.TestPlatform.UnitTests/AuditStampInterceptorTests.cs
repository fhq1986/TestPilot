using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 审计盖章拦截器（M8/审计）。
///
/// 这里只钉一件事，但它是**会静默算错数**的那种：新增实体时，拦截器只能在
/// <c>CreatedAt</c> 尚未写入的情况下补章，绝不能覆盖实体自己已显式设置的值。
///
/// 背景：AgentAttempt 在自愈循环里「尝试开始」时就记 <c>CreatedAt</c>，循环结束才
/// <c>SaveChanges</c>。拦截器若在 SaveChanges 那一刻统一盖章，CreatedAt 就会晚于
/// 业务上更早的 CompletedAt，于是自愈度量的「平均修复时长」恒为负数。
/// 这个约定没有编译期约束、也没有别的测试覆盖，改错一行不会有任何报错——只有看板上
/// 的数字悄悄变错，所以必须钉住。
/// </summary>
public class AuditStampInterceptorTests
{
    /// <summary>
    /// 最小上下文：只映射 AgentAttempt 并掐掉导航。
    /// 不直接复用 TestDbContext —— 它的 OnModelCreating 里有 ToJson / pgvector 这类
    /// 关系型专有配置，内存 provider 建不起模型；而本测试只关心拦截器按「属性名约定」
    /// 的行为，不需要真实关系图。
    /// </summary>
    private sealed class MinimalContext : DbContext
    {
        public MinimalContext(DbContextOptions options) : base(options) { }
        public DbSet<AgentAttempt> AgentAttempts => Set<AgentAttempt>();

        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.Entity<AgentAttempt>().Ignore(a => a.Execution);
    }

    private static MinimalContext NewContext()
    {
        var options = new DbContextOptionsBuilder<MinimalContext>()
            .UseInMemoryDatabase($"audit-stamp-{Guid.NewGuid():N}")
            .AddInterceptors(new AuditStampInterceptor(new HttpContextAccessor()))
            .Options;
        return new MinimalContext(options);
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
}
