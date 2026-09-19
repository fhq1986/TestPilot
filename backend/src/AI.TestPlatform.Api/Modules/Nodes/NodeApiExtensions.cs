using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.TestPlatform.Api.Modules.Nodes;

/// <summary>执行节点看板行</summary>
public sealed record NodeView(
    Guid Id,
    string Name,
    string MachineName,
    string InstanceId,
    string Version,
    int MaxConcurrency,
    int RunningCount,
    bool Online,
    string? LastHeartbeatAt,
    string StartedAt);

public static class NodeApiExtensions
{
    /// <summary>离线判定阈值：心跳间隔的 3 倍（进程崩溃 / 容器被杀后 3 个心跳内从看板转灰）</summary>
    private const int OfflineAfterHeartbeats = 3;

    public static RouteGroupBuilder MapNodeApi(this RouteGroupBuilder group)
    {
        // 节点看板：所有运行执行器的实例（主 API、远程 worker、容器副本）
        group.MapGet("/", async (
            TestDbContext db,
            [FromServices] IOptions<ExecutionOptions> optionsAccessor,
            CancellationToken ct) =>
        {
            var options = optionsAccessor.Value;
            var offlineAfter = TimeSpan.FromSeconds(
                Math.Max(15, Math.Max(options.HeartbeatSeconds, 5) * OfflineAfterHeartbeats));
            var now = DateTime.UtcNow;

            var nodes = await db.ExecutionNodes.AsNoTracking()
                .OrderByDescending(n => n.LastHeartbeatAt)
                .ToListAsync(ct);

            // 顺带统计各节点今日完成执行数（跑量感知：谁在干活一目了然）。
            // 只取 ClaimedBy 原始串回内存再分组——Split 不能进 SQL 表达式树（params 重载），
            // 今日完成量是窄集合（每节点几百条），内存分组无压力。
            var todayStart = now.Date;
            var claimedRaw = await db.Executions.AsNoTracking()
                .Where(e => e.EndedAt != null && e.EndedAt >= todayStart && e.ClaimedBy != null)
                .Select(e => e.ClaimedBy!)
                .ToListAsync(ct);
            var doneMap = claimedRaw
                .GroupBy(c => c.Split(':')[0])
                .ToDictionary(g => g.Key, g => g.Count());

            var view = nodes.Select(n => new NodeView(
                n.Id,
                n.Name,
                n.MachineName,
                n.InstanceId,
                n.Version,
                n.MaxConcurrency,
                n.RunningCount,
                Online: now - n.LastHeartbeatAt < offlineAfter,
                n.LastHeartbeatAt.ToString("o"),
                n.StartedAt.ToString("o"))).ToList();

            // 今日跑量按节点名补上（ClaimedBy 前缀 = 节点名）
            var result = view
                .Select(v => new
                {
                    v.Id,
                    v.Name,
                    v.MachineName,
                    v.InstanceId,
                    v.Version,
                    v.MaxConcurrency,
                    v.RunningCount,
                    v.Online,
                    v.LastHeartbeatAt,
                    v.StartedAt,
                    TodayCompleted = doneMap.TryGetValue(v.Name, out var c) ? c : 0,
                })
                .ToList();

            return Results.Ok(new
            {
                nodes = result,
                onlineCount = result.Count(n => n.Online),
                offlineCount = result.Count(n => !n.Online),
            });
        }).WithPermission(Permission.ViewExecutions);

        // 移除一个离线节点的登记（重启/换名后留下的废行；在线节点拒绝删除）
        group.MapDelete("/{name}", async (
            string name,
            TestDbContext db,
            [FromServices] IOptions<ExecutionOptions> optionsAccessor,
            CancellationToken ct) =>
        {
            var options = optionsAccessor.Value;
            var node = await db.ExecutionNodes.FirstOrDefaultAsync(n => n.Name == name, ct);
            if (node is null)
                return Results.NotFound(new { message = "节点不存在" });

            var offlineAfter = TimeSpan.FromSeconds(
                Math.Max(15, Math.Max(options.HeartbeatSeconds, 5) * OfflineAfterHeartbeats));
            if (DateTime.UtcNow - node.LastHeartbeatAt < offlineAfter)
                return Results.Json(new { message = "节点在线，不能删除" }, statusCode: 400);

            db.ExecutionNodes.Remove(node);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).WithPermission(Permission.ManageSettings).WithAudit("Delete", "ExecutionNode");

        return group;
    }
}
