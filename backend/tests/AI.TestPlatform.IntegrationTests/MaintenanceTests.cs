using AI.TestPlatform.Api.Startup;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AI.TestPlatform.IntegrationTests;

[Collection("api")]
public class MaintenanceTests
{
    private readonly TestApiFactory _factory;

    public MaintenanceTests(TestApiFactory factory) => _factory = factory;

    /// <summary>
    /// 启动清理只回收「心跳过期的 Running」，**绝不动 Pending**。
    /// Pending 是持久化任务队列的真身：进程重启后由执行器继续抢占执行，
    /// 多实例部署时其它实例可能正在处理它——把它置为 Error 会直接丢任务。
    /// </summary>
    [Fact]
    public async Task CleanupOnStartup_KeepsPendingAndFailsStaleRunning()
    {
        Execution pending, running, passed;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            pending = new Execution { Status = ExecutionStatus.Pending, TriggerType = TriggerType.CIWebhook };
            running = new Execution
            {
                Status = ExecutionStatus.Running,
                TriggerType = TriggerType.Manual,
                StartedAt = DateTime.UtcNow.AddMinutes(-30),
                // 心跳必须早于 StaleRunningMinutes（默认 5 分钟）才会被判定僵死
                HeartbeatAt = DateTime.UtcNow.AddMinutes(-30),
            };
            passed = new Execution
            {
                Status = ExecutionStatus.Passed,
                TriggerType = TriggerType.Manual,
                StartedAt = DateTime.UtcNow.AddMinutes(-5),
                EndedAt = DateTime.UtcNow.AddMinutes(-4),
                DurationMs = 60_000,
            };
            db.Executions.AddRange(pending, running, passed);
            await db.SaveChangesAsync();
        }

        await MaintenanceService.CleanupOnStartupAsync(_factory.Services);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();

            var pendingAfter = await db.Executions.AsNoTracking().SingleAsync(e => e.Id == pending.Id);
            Assert.Equal(ExecutionStatus.Pending, pendingAfter.Status);
            Assert.Null(pendingAfter.EndedAt);

            var runningAfter = await db.Executions.AsNoTracking().SingleAsync(e => e.Id == running.Id);
            Assert.Equal(ExecutionStatus.Error, runningAfter.Status);
            Assert.Equal("执行器实例心跳超时，执行已中断", runningAfter.AIDiagnosis);
            Assert.NotNull(runningAfter.EndedAt);
            Assert.NotNull(runningAfter.DurationMs);
            Assert.Null(runningAfter.ClaimedBy);
            Assert.Null(runningAfter.HeartbeatAt);

            var passedAfter = await db.Executions.AsNoTracking().SingleAsync(e => e.Id == passed.Id);
            Assert.Equal(ExecutionStatus.Passed, passedAfter.Status);
            Assert.Null(passedAfter.AIDiagnosis);
        }
    }

    /// <summary>心跳仍然新鲜的 Running 不能被误杀——它可能正是本进程正在跑的任务。</summary>
    [Fact]
    public async Task CleanupOnStartup_KeepsFreshRunningExecution()
    {
        Execution fresh;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            fresh = new Execution
            {
                Status = ExecutionStatus.Running,
                TriggerType = TriggerType.Manual,
                StartedAt = DateTime.UtcNow.AddSeconds(-10),
                HeartbeatAt = DateTime.UtcNow,
                ClaimedBy = "test-instance",
            };
            db.Executions.Add(fresh);
            await db.SaveChangesAsync();
        }

        await MaintenanceService.CleanupOnStartupAsync(_factory.Services);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var after = await db.Executions.AsNoTracking().SingleAsync(e => e.Id == fresh.Id);
            Assert.Equal(ExecutionStatus.Running, after.Status);
            Assert.Equal("test-instance", after.ClaimedBy);
        }
    }

    [Fact]
    public async Task CleanupOnStartup_StopsStaleRunningMocks()
    {
        MockDefinition runningMock, stoppedMock;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var admin = await db.Users.FirstAsync();
            var project = new Project { Name = $"proj-{Guid.NewGuid():N}", CreatedById = admin.Id };
            db.Projects.Add(project);
            runningMock = new MockDefinition
            {
                ProjectId = project.Id,
                Name = "stale-running",
                BasePath = "/",
                Port = 9101,
                Status = MockStatus.Running,
                Spec = "{}",
            };
            stoppedMock = new MockDefinition
            {
                ProjectId = project.Id,
                Name = "kept-stopped",
                BasePath = "/",
                Status = MockStatus.Stopped,
                Spec = "{}",
            };
            db.MockDefinitions.AddRange(runningMock, stoppedMock);
            await db.SaveChangesAsync();
        }

        await MaintenanceService.CleanupOnStartupAsync(_factory.Services);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            var runningAfter = await db.MockDefinitions.AsNoTracking().SingleAsync(m => m.Id == runningMock.Id);
            Assert.Equal(MockStatus.Stopped, runningAfter.Status);
            Assert.Null(runningAfter.Port);

            var stoppedAfter = await db.MockDefinitions.AsNoTracking().SingleAsync(m => m.Id == stoppedMock.Id);
            Assert.Equal(MockStatus.Stopped, stoppedAfter.Status);
        }
    }
}
