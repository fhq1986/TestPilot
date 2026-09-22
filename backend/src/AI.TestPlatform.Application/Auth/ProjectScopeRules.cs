using AI.TestPlatform.Domain.Auth;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.Application.Auth;

/// <summary>
/// 项目作用域的**纯规则**（迭代 E·①）：把端点已有的全局权限反推为「至少需要哪个项目角色」。
///
/// 抽成无副作用的静态函数，是为了能被单元测试直接断言——授权映射写错（比如把"查看"误判成"可写"）
/// 不会编译报错、也不会在自测里暴露，只有纯函数单测拦得住。
/// </summary>
public static class ProjectScopeRules
{
    /// <summary>项目内业务写操作对应的权限位（查看之外、且不属于"改项目自身"的部分）</summary>
    private const Permission TesterBits =
        Permission.ManageTestCases | Permission.RunExecutions | Permission.ManageDataSets |
        Permission.ManageBaselines | Permission.ManageSharedSteps | Permission.ManageSchedules |
        Permission.ManageTestPlans;

    /// <summary>
    /// 由全局权限反推所需项目角色：
    /// 无权限/纯查看 → Viewer；改成员或系统设置 → Owner；改项目自身 → Manager；
    /// 项目内业务写（用例/执行/数据集/基线/共享步骤/计划/定时）→ Tester。
    /// </summary>
    public static ProjectRole MinRoleFor(Permission required)
    {
        if (required == Permission.None)
            return ProjectRole.Viewer;

        if ((required & (Permission.ManageUsers | Permission.ManageSettings)) != 0)
            return ProjectRole.Owner;

        if ((required & Permission.ManageProjects) != 0)
            return ProjectRole.Manager;

        if ((required & TesterBits) != 0)
            return ProjectRole.Tester;

        return ProjectRole.Viewer;
    }
}
