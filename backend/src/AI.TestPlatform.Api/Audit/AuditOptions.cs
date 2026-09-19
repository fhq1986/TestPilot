namespace AI.TestPlatform.Api.Audit;

/// <summary>审计日志配置（appsettings.json 的 Audit 节）</summary>
public class AuditOptions
{
    /// <summary>
    /// 审计日志保留天数。审计表只增不删，不设上限时长期运行会拖慢查询并撑大备份，
    /// 因此由维护任务按天清理。合规要求更长的场景把这个值调大即可。
    /// </summary>
    public int RetentionDays { get; set; } = 180;

    /// <summary>单次清理最多删除的行数（避免一次删太多把长事务拖垮）</summary>
    public int CleanupBatchSize { get; set; } = 5000;
}
