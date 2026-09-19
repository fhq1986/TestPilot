using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Api.Visual;

/// <summary>
/// 「哪张步骤截图可以成为视觉基线」的**唯一判定处**。
///
/// 为什么要有这条规矩：失败界面一旦被固化成基准，后续每次执行都是在跟一个错误页比对——
/// 表现成"视觉回归一直是无变化"，真实问题反而被掩盖，比不开视觉回归更糟。
///
/// 但**不能简单要求「步骤必须通过」**：视觉差异本身就会把步骤改判为 Failed
/// （见 <see cref="VisualRegressionService"/> 里判定 Changed 的分支），
/// 而界面上「接受变化」按钮要处理的恰恰是这种失败——
/// 若只放行 Passed，那个按钮就永远点不动了。
///
/// 所以放行条件收窄为：**通过，或者失败原因就是这个视觉差异本身**。
///
/// 规则集中在这里而不是散在调用点：建立基线有「执行时自动建立」与「人工接受变化」
/// 两条路径，两处各写一遍必然会分叉（本项目已经因为"同一判据两处实现"踩过坑）。
/// </summary>
public static class BaselinePolicy
{
    /// <summary>该步骤结果能不能拿来当基线</summary>
    public static bool CanBecomeBaseline(ExecutionStatus status, VisualStatus visualStatus) =>
        status == ExecutionStatus.Passed
        || (status == ExecutionStatus.Failed && visualStatus == VisualStatus.Changed);

    /// <summary>不能当基线时的原因（中文短语，供拼装提示语）</summary>
    public static string DescribeRejection(ExecutionStatus status) => status switch
    {
        ExecutionStatus.Failed => "该步骤执行失败",
        ExecutionStatus.Error => "该步骤执行报错",
        ExecutionStatus.Skipped => "该步骤已被跳过",
        ExecutionStatus.Running => "该步骤仍在执行中",
        ExecutionStatus.Pending => "该步骤尚未执行",
        _ => $"该步骤状态为 {status}",
    };
}
