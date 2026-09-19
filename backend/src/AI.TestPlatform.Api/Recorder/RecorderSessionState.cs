using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Recorder;

/// <summary>
/// 录制会话的状态判定规则（纯函数，便于单测）。
///
/// 这些规则原本散在 <see cref="RecorderService"/> 里，与数据库访问、进程管理混在一起，
/// 只能靠跑起一个真实浏览器来验证。抽出来之后，「什么算活跃」「进程没了该判什么状态」
/// 「多久算废弃」都能用毫秒级单测覆盖——而这几个判断恰恰是录制器最容易出错的部位。
/// </summary>
public static class RecorderSessionState
{
    /// <summary>
    /// 占据并发额度、需要按进程判活的状态。
    /// 用数组 + <c>Contains</c> 而不是方法调用：EF 能把前者翻译成 SQL 里的 IN，
    /// 方法调用则无法翻译——这也让「活跃状态」的定义在查询与内存判断里保持同一份。
    /// </summary>
    public static readonly RecorderStatus[] ActiveStatuses =
        { RecorderStatus.Recording, RecorderStatus.Idle };

    /// <summary>占据并发额度、需要按进程判活的状态</summary>
    public static bool IsActive(RecorderStatus status) => ActiveStatuses.Contains(status);

    /// <summary>
    /// 进程已经结束（或被判定为没了）时该落到什么状态：
    /// 有脚本文件说明用户至少操作过一次 → Stopped；连文件都没生成 → Failed。
    /// </summary>
    public static RecorderStatus ResolveAfterProcessGone(bool scriptFileExists) =>
        scriptFileExists ? RecorderStatus.Stopped : RecorderStatus.Failed;

    /// <summary>空闲超时判定：从未被轮询过（刚创建就没人看）同样算废弃</summary>
    public static bool IsIdleTimeout(DateTime? lastPolledAt, DateTime utcNow, int idleTimeoutMinutes)
    {
        if (idleTimeoutMinutes <= 0) return false;
        if (lastPolledAt is null) return true;
        return lastPolledAt.Value < utcNow.AddMinutes(-idleTimeoutMinutes);
    }

    /// <summary>是否已超过保留期（用于清理历史会话）</summary>
    public static bool IsExpired(DateTime createdAt, DateTime utcNow, int retentionDays)
    {
        if (retentionDays <= 0) return false;
        return createdAt < utcNow.AddDays(-retentionDays);
    }

    /// <summary>
    /// 错误信息截断：LastError 列长度有限，进程输出的原始异常可能很长，
    /// 不截断会在保存时抛数据库异常，把「录制失败」变成「500 保存失败」。
    /// </summary>
    public static string Shorten(string message, int max = 500) =>
        message.Length <= max ? message : message[..max];
}
