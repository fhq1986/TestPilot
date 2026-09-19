namespace AI.TestPlatform.Domain.Entities;

/// <summary>录制会话状态</summary>
public enum RecorderStatus
{
    /// <summary>已创建，浏览器尚未拉起</summary>
    Idle = 0,
    /// <summary>浏览器已打开，正在捕获用户操作</summary>
    Recording = 1,
    /// <summary>用户主动停止或保存，浏览器已关闭</summary>
    Stopped = 2,
    /// <summary>浏览器意外退出（崩溃 / 被外部结束）</summary>
    Failed = 3,
}

/// <summary>
/// 脚本录制会话（迭代 C）。
///
/// 工作机制：后端用 Playwright 自带的 <c>codegen</c> 拉起一个真实浏览器并指定
/// <c>--target playwright-test --output &lt;file&gt;</c>；codegen 会**每次交互后都把最新脚本
/// 覆写进该文件**。因此这里不需要自己做协议级事件捕获：
/// 前端定时轮询，后端读文件 → 交给 <c>PlaywrightScriptParser</c> 解析成步骤即可，
/// 录制结束直接复用同一条解析链路落库，不需要第二套"录制步骤"模型。
///
/// 重要：录制是**有状态的交互会话**（进程 + 临时文件都在本实例上），
/// 与执行队列不同，它**不能**在多实例间漂移。多实例部署需要前置路由做会话粘性，
/// 或把会话表换成 Redis 并保证同一会话固定路由到同一实例。
/// </summary>
public class RecorderSession
{
    /// <summary>
    /// 显式生成主键：会话创建时就要用 ID 拼脚本文件路径（recordings/{id}.ts），
    /// 而 EF Core 的 Guid 值生成器要到 Add() 才赋值——若依赖它，这里会拿到 Guid.Empty，
    /// 导致所有会话共用同一个 00000000….ts，互相覆盖脚本。
    /// </summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    /// <summary>会话展示名（保存为用例时作为默认用例名）</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>起始地址；为空时 codegen 打开空白页，用户自行导航</summary>
    public string? BaseUrl { get; set; }
    public string Browser { get; set; } = "chromium";

    public RecorderStatus Status { get; set; } = RecorderStatus.Idle;

    /// <summary>codegen 进程 PID（仅用于诊断；跨重启后不可信，判活以进程表为准）</summary>
    public int ProcessId { get; set; }
    /// <summary>codegen 输出的 .ts 脚本绝对路径（位于录制根目录，按会话 ID 命名）</summary>
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>最近一次轮询到的步骤数（避免前端每次都重新解析才知道有没有变化）</summary>
    public int StepCount { get; set; }
    /// <summary>脚本内容指纹（长度 + 简易哈希），用于判断"文件有没有变"</summary>
    public string? ScriptFingerprint { get; set; }
    public string? LastError { get; set; }

    public Guid? CreatedById { get; set; }
    public string? CreatedByName { get; set; }

    /// <summary>保存生成的用例 ID（保存后回填，便于前端跳转）</summary>
    public Guid? SavedTestCaseId { get; set; }
    public string? SavedTestCaseName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
    /// <summary>最近一次被前端轮询的时间：长时间无人轮询说明页面已关闭，可回收</summary>
    public DateTime? LastPolledAt { get; set; }
}
