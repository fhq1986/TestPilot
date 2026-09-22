namespace AI.TestPlatform.Api.LoadTesting;

/// <summary>
/// 压测产物的存储约定（迭代 F·P2-9）。
///
/// 集中在一处而不是散着拼字符串：清理任务要按前缀排除/删除，
/// 前缀写歪一次就会「产物被截图清理顺手删掉」——traces/ 与 videos/ 当年都踩过这个坑。
/// </summary>
public static class LoadTestStorage
{
    /// <summary>对象存储里的统一前缀（维护任务按它做排除与保留期清理）</summary>
    public const string Prefix = "loadtests/";

    /// <summary>本地工作目录根（k6 脚本与输出先落这里，运行结束即删）</summary>
    public static string WorkRoot(string configured) =>
        string.IsNullOrWhiteSpace(configured) ? "loadtests" : configured;

    /// <summary>本次运行冻结的脚本</summary>
    public static string ScriptKey(Guid runId) => $"{Prefix}{runId:N}/script.js";

    /// <summary>k6 summary 原始 JSON</summary>
    public static string SummaryKey(Guid runId) => $"{Prefix}{runId:N}/summary.json";

    /// <summary>k6 stdout/stderr</summary>
    public static string LogKey(Guid runId) => $"{Prefix}{runId:N}/k6.log";

    /// <summary>某次运行的全部产物前缀（删运行/删场景时按它整目录清）</summary>
    public static string RunPrefix(Guid runId) => $"{Prefix}{runId:N}/";
}
