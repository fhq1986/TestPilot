using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.AI;

/// <summary>
/// 把**已采纳**的 Agent 修复动作落到真实 <see cref="TestStep"/> 上。
///
/// 与 <see cref="FixActionApplier"/> 的分工：后者只改传入的步骤副本（执行期自愈用，
/// 铁律是绝不碰库里的真实步骤）；本类负责把那份改动**映射回 EF 变更跟踪**，
/// 让 add_step / delete_step 这类结构性动作真的进库。
///
/// ⚠ 为什么不能只调 FixActionApplier 然后 SaveChanges：
/// 它的 add_step 是 <c>steps.Add(...)</c>、delete_step 是 <c>steps.Remove(...)</c>，
/// 而这里传进去的是 <c>ToListAsync()</c> 出来的普通 <c>List</c>——往 List 里塞/摘不会让
/// EF 追踪到，结果是「采纳成功但步骤没变」（线上就是这样：Persisted=false、用例仍是原三步）。
/// 必须按前后差集显式 Add/Remove。
/// </summary>
public static class AgentFixPersister
{
    /// <summary>被安全校验拒绝的动作（动作白名单 / 参数非法 / URL 越出被测站点等）</summary>
    public sealed record RejectedFix(string ActionType, string Reason);

    public sealed record PersistResult(int Applied, IReadOnlyList<RejectedFix> Rejected);

    /// <summary>纯逻辑：就地应用动作，并给出需要显式增删的差集（<paramref name="steps"/> 会被改）。</summary>
    public static (
        int Applied,
        IReadOnlyList<RejectedFix> Rejected,
        IReadOnlyList<TestStep> Added,
        IReadOnlyList<TestStep> Removed) Apply(
        IList<TestStep> steps, IReadOnlyList<FixActionDto> fixes, string? baseUrl)
    {
        var rejected = new List<RejectedFix>();
        var originals = steps.ToList();
        var applied = 0;

        foreach (var fix in fixes)
        {
            // 采纳与执行期走**同一套**安全校验，不因为"人工点过确认"就放宽
            if (FixActionApplier.TryApply(steps, fix, out var error, baseUrl))
                applied++;
            else
                rejected.Add(new RejectedFix(fix.ActionType ?? "(空)", error ?? "未应用"));
        }

        if (applied == 0)
            return (0, rejected, Array.Empty<TestStep>(), Array.Empty<TestStep>());

        // 差集按引用相等判定（TestStep 未重写 Equals），正好区分"原有"与"applier 新建"
        var removed = originals.Except(steps).ToList();
        var added = steps.Except(originals).ToList();

        // StepOrder 归一为 1-based：applier 的 Renumber 是 0-based，用在执行期副本上无所谓，
        // 但落库会直接显示成用例详情页「序号」列的 0、1、2……与新建用例的 1、2、3 不一致
        var ordered = steps.OrderBy(s => s.StepOrder).ToList();
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].StepOrder = i + 1;

        return (applied, rejected, added, removed);
    }

    /// <summary>加载用例的真实步骤 → 应用动作 → 把结构性改动落到 DbContext（不 SaveChanges）。</summary>
    public static async Task<PersistResult> ApplyAsync(
        TestDbContext db, Guid testCaseId, string? baseUrl,
        IReadOnlyList<FixActionDto> fixes, CancellationToken ct)
    {
        if (fixes.Count == 0)
            return new PersistResult(0, Array.Empty<RejectedFix>());

        var steps = await db.TestSteps
            .Where(s => s.TestCaseId == testCaseId)
            .OrderBy(s => s.StepOrder)
            .ToListAsync(ct);

        var (applied, rejected, added, removed) = Apply(steps, fixes, baseUrl);
        if (applied == 0)
            return new PersistResult(0, rejected);

        foreach (var gone in removed)
            db.TestSteps.Remove(gone);
        foreach (var fresh in added)
        {
            fresh.TestCaseId = testCaseId;
            db.TestSteps.Add(fresh);
        }

        return new PersistResult(applied, rejected);
    }
}
