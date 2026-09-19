using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.TestCases;
using AI.TestPlatform.Domain.Entities;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace AI.TestPlatform.Api.TestCases;

/// <summary>
/// 用例版本快照：记录、读取、回滚。
///
/// **核心取舍：内容没变就不记版本。**
/// 判据是快照的内容签名，不是"有没有调保存接口"——否则"点开用例详情、顺手保存一下"
/// 也会多出一版，历史很快被无意义版本淹没，最后用户就不看了。
///
/// 历史**只追加**：回滚也是一次变更，所以回滚前同样先记录当前内容。
/// 这样"回滚错了"还能再回滚回去，不会丢东西。
/// </summary>
public class TestCaseVersionService
{
    private readonly TestDbContext _db;
    private readonly ICurrentUser _currentUser;

    public TestCaseVersionService(TestDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    /// <summary>
    /// 在**应用新内容之前**调用：把当前内容存成第 <c>Version</c> 版快照，并把用例版本号 +1。
    /// 返回是否真的记了一版（内容没变返回 false，版本号不动）。
    /// </summary>
    public async Task<bool> RecordBeforeChangeAsync(
        TestCase testCase, TestCaseSnapshot incoming, CancellationToken ct)
    {
        var old = testCase.ToSnapshot();
        if (old.Signature() == incoming.Signature())
            return false;

        _db.TestCaseVersions.Add(new TestCaseVersion
        {
            TestCaseId = testCase.Id,
            Version = testCase.Version,
            Snapshot = old.Serialize(),
            ChangeSummary = TestCaseSnapshotExtensions.BuildSummary(old, incoming),
            StepCount = old.Steps.Count,
            OperatorId = _currentUser.Id,
            OperatorName = _currentUser.Username,
        });
        testCase.Version += 1;
        return true;
    }

    public Task<List<TestCaseVersion>> ListAsync(Guid testCaseId, CancellationToken ct) =>
        _db.TestCaseVersions.AsNoTracking()
            .Where(v => v.TestCaseId == testCaseId)
            .OrderByDescending(v => v.Version)
            .ToListAsync(ct);

    public Task<TestCaseVersion?> GetAsync(Guid testCaseId, int version, CancellationToken ct) =>
        _db.TestCaseVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.TestCaseId == testCaseId && v.Version == version, ct);

    /// <summary>
    /// 把用例内容回滚到指定版本。回滚前会先记录当前内容（历史只追加），
    /// 所以"回滚"本身可以再被回滚回去。
    /// </summary>
    public async Task<(bool Ok, string? Error)> RestoreAsync(
        TestCase testCase, int version, CancellationToken ct)
    {
        var target = await GetAsync(testCase.Id, version, ct);
        if (target is null)
            return (false, $"找不到第 {version} 版");

        var snapshot = TestCaseSnapshotExtensions.Deserialize(target.Snapshot);
        if (snapshot is null)
            return (false, $"第 {version} 版的快照读不出来（可能是旧格式）");

        // 先记当前内容，再覆盖——顺序反了就会丢掉"回滚前是什么样"
        await RecordBeforeChangeAsync(testCase, snapshot, ct);
        ApplySnapshot(testCase, snapshot);
        testCase.UpdatedAt = DateTime.UtcNow;
        return (true, null);
    }

    /// <summary>把快照内容写回用例实体（步骤整体重建，与更新步骤端点的做法一致）</summary>
    private void ApplySnapshot(TestCase testCase, TestCaseSnapshot snapshot)
    {
        testCase.Name = snapshot.Name;
        testCase.Description = snapshot.Description;
        testCase.Type = snapshot.Type;
        testCase.CaseCode = snapshot.CaseCode;
        testCase.Module = snapshot.Module;
        testCase.Priority = snapshot.Priority;
        testCase.Browser = snapshot.Browser;
        testCase.Timeout = snapshot.Timeout;
        testCase.RetryCount = snapshot.RetryCount;
        testCase.FailFast = snapshot.FailFast;
        testCase.BaseUrl = snapshot.BaseUrl;
        testCase.ExpectedResult = snapshot.ExpectedResult;
        testCase.SourceSteps = snapshot.SourceSteps;
        testCase.VisualEnabled = snapshot.VisualEnabled;
        testCase.VisualThreshold = snapshot.VisualThreshold;
        testCase.DataSetId = snapshot.DataSetId;
        testCase.RequirementId = snapshot.RequirementId;

        // 注意：给新步骤**不能预设 Id**——新步骤挂在已跟踪的父实体集合上，
        // EF 看到主键非空会当成"已存在"发 UPDATE，影响 0 行后抛并发异常（项目备忘里记过）。
        foreach (var step in testCase.Steps) _db.TestSteps.Remove(step);
        testCase.Steps = snapshot.Steps.Select(s => new TestStep
        {
            StepOrder = s.StepOrder,
            ActionType = s.ActionType,
            // 走快照自己的解析器（带容错的命名策略），
            // 不要在这里 new 一套默认选项——那样读旧格式会静默变成空配置
            Config = TestCaseSnapshotExtensions.ParseConfig(s.Config) ?? new StepConfig(),
            AIInstruction = s.AIInstruction,
            AIElementDescription = s.AIElementDescription,
            SharedGroupId = s.SharedGroupId,
            SharedVariables = s.SharedVariables?.ToList(),
        }).ToList();
    }

}
