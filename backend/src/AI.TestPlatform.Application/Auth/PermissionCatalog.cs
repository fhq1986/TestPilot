using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Auth;

/// <summary>
/// 角色 → 权限矩阵（迭代 C 的唯一权威定义）。
///
/// 设计取舍：矩阵硬编码在代码里而不是落库，理由是——
/// 1) 三角色是产品级约定，不是租户可配项；
/// 2) 落库会引入「配置与代码不一致」的运行时风险（端点已改、数据没改）；
/// 3) 硬编码可被单元测试直接断言，越权回归测试成本最低。
/// 将来若需要「自定义角色」，只需把本类换成读库实现，调用方（<see cref="Has"/>）签名不变。
/// </summary>
public static class PermissionCatalog
{
    /// <summary>所有可用权限点的全集（用于「管理员」角色与调试展示）</summary>
    public const Permission All =
        Permission.ViewProjects | Permission.ViewTestCases | Permission.ViewExecutions |
        Permission.ViewReports | Permission.ManageProjects | Permission.ManageTestCases |
        Permission.RunExecutions | Permission.ManageDataSets | Permission.ManageBaselines |
        Permission.ManageSharedSteps | Permission.ManageSchedules | Permission.ManageSettings |
        Permission.ManageUsers | Permission.ViewAuditLog |
        Permission.ViewTestPlans | Permission.ManageTestPlans;

    /// <summary>只读访客可用的权限</summary>
    public const Permission ViewerPermissions =
        Permission.ViewProjects | Permission.ViewTestCases |
        Permission.ViewExecutions | Permission.ViewReports |
        // 计划与报告是验收材料，访客要能看（但不能改）
        Permission.ViewTestPlans;

    /// <summary>测试工程师可用权限（在访客基础上叠加日常测试工作所需的写权限）</summary>
    public const Permission TesterPermissions = ViewerPermissions |
        Permission.ManageProjects | Permission.ManageTestCases | Permission.RunExecutions |
        Permission.ManageDataSets | Permission.ManageBaselines | Permission.ManageSharedSteps |
        Permission.ManageTestPlans;

    private static readonly IReadOnlyDictionary<UserRole, Permission> Matrix =
        new Dictionary<UserRole, Permission>
        {
            [UserRole.Admin] = All,
            [UserRole.Tester] = TesterPermissions,
            [UserRole.Viewer] = ViewerPermissions,
        };

    /// <summary>取某角色的权限位图</summary>
    public static Permission Of(UserRole role) =>
        Matrix.TryGetValue(role, out var permissions) ? permissions : Permission.None;

    /// <summary>判断权限位图是否满足所需权限（全部满足才算通过——多权限点默认「与」语义）</summary>
    public static bool Has(Permission granted, Permission required) =>
        required == Permission.None || (granted & required) == required;

    /// <summary>
    /// 解析 JWT <c>perm</c> 声明中的权限位图。缺失或非法时返回 <see cref="Permission.None"/>，
    /// 而不是抛异常——老 token（迭代 C 之前签发）没有该声明，应当退化成「无任何权限」并由
    /// 端点过滤器统一返回 403，让用户重新登录，而不是 500。
    /// </summary>
    public static Permission Parse(string? value) =>
        int.TryParse(value, out var raw) ? (Permission)raw : Permission.None;

    /// <summary>角色中文名（用于前端展示与审计日志）</summary>
    public static string DisplayName(UserRole role) => role switch
    {
        UserRole.Admin => "管理员",
        UserRole.Tester => "测试工程师",
        UserRole.Viewer => "只读访客",
        _ => role.ToString(),
    };

    /// <summary>权限点中文名（审计日志 / 403 提示用）</summary>
    public static string Describe(Permission permission) => permission switch
    {
        Permission.None => "无",
        Permission.ViewProjects => "查看项目",
        Permission.ViewTestCases => "查看用例",
        Permission.ViewExecutions => "查看执行",
        Permission.ViewReports => "查看报告",
        Permission.ManageProjects => "管理项目",
        Permission.ManageTestCases => "管理用例",
        Permission.RunExecutions => "执行测试",
        Permission.ManageDataSets => "管理数据集",
        Permission.ManageBaselines => "管理基线",
        Permission.ManageSharedSteps => "管理共享步骤",
        Permission.ViewTestPlans => "查看测试计划",
        Permission.ManageTestPlans => "管理测试计划",
        Permission.ManageSchedules => "管理定时任务",
        Permission.ManageSettings => "管理系统设置",
        Permission.ManageUsers => "管理用户",
        Permission.ViewAuditLog => "查看审计日志",
        _ => permission.ToString(),
    };

    /// <summary>把位图展开成权限点列表（前端展示 / 调试）</summary>
    public static IReadOnlyList<Permission> Expand(Permission permissions) =>
        Enum.GetValues<Permission>()
            .Where(p => p != Permission.None && Permissions.IsSingleBit(p) && permissions.HasFlag(p))
            .ToList();

    /// <summary>把位图展开成中文名列表</summary>
    public static IReadOnlyList<string> ExpandNames(Permission permissions) =>
        Expand(permissions).Select(Describe).ToList();
}

// 小工具：判断枚举值是否为「单一位」，用于把位图拆成离散权限点
file static class Permissions
{
    public static bool IsSingleBit(Permission value)
    {
        var raw = (int)value;
        return raw != 0 && (raw & (raw - 1)) == 0;
    }
}
