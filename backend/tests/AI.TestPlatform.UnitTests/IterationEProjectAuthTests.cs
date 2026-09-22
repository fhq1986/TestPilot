using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Auth;
using AI.TestPlatform.Domain.Entities;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 迭代 E·① 项目级授权的契约测试。
/// 钉住：ProjectRole 数值只可追加（int 落库）；全局权限 → 项目角色的映射不漂移
/// （映射写错不会编译报错，只有单测拦得住）。
/// </summary>
public class IterationEProjectAuthTests
{
    // ------------------------------ 枚举数值契约（按 int 落库，只能追加） ------------------------------

    [Fact]
    public void ProjectRole_数值契约()
    {
        Assert.Equal(0, (int)ProjectRole.Viewer);
        Assert.Equal(1, (int)ProjectRole.Tester);
        Assert.Equal(2, (int)ProjectRole.Manager);
        Assert.Equal(3, (int)ProjectRole.Owner);
    }

    [Fact]
    public void ProjectResource_数值契约()
    {
        // ProjectResource 是端点元数据（不落库），但数值仍须稳定：改值会让 WithProjectScope 的声明错配。
        Assert.Equal(0, (int)AI.TestPlatform.Api.Auth.ProjectResource.None);
        Assert.Equal(9, (int)AI.TestPlatform.Api.Auth.ProjectResource.Project);
        Assert.Equal(10, (int)AI.TestPlatform.Api.Auth.ProjectResource.Environment);
        Assert.Equal(11, (int)AI.TestPlatform.Api.Auth.ProjectResource.SharedStep);
        Assert.Equal(12, (int)AI.TestPlatform.Api.Auth.ProjectResource.VisualBaseline);
    }

    [Fact]
    public void ProjectRole_排序_所有权限高于管理高于测试高于只读()
    {
        Assert.True(ProjectRole.Owner > ProjectRole.Manager);
        Assert.True(ProjectRole.Manager > ProjectRole.Tester);
        Assert.True(ProjectRole.Tester > ProjectRole.Viewer);
    }

    // ------------------------------ 权限 → 项目角色映射 ------------------------------

    [Theory]
    [InlineData(Permission.None)]
    [InlineData(Permission.ViewProjects)]
    [InlineData(Permission.ViewTestCases)]
    [InlineData(Permission.ViewExecutions)]
    [InlineData(Permission.ViewReports)]
    [InlineData(Permission.ViewTestPlans)]
    [InlineData(Permission.ViewAuditLog)]
    public void 纯查看与无权限_只要求Viewer(Permission required) =>
        Assert.Equal(ProjectRole.Viewer, ProjectScopeRules.MinRoleFor(required));

    [Theory]
    [InlineData(Permission.ManageTestCases)]
    [InlineData(Permission.RunExecutions)]
    [InlineData(Permission.ManageDataSets)]
    [InlineData(Permission.ManageBaselines)]
    [InlineData(Permission.ManageSharedSteps)]
    [InlineData(Permission.ManageTestPlans)]
    [InlineData(Permission.ManageSchedules)]
    public void 项目内业务写操作_要求Tester(Permission required) =>
        Assert.Equal(ProjectRole.Tester, ProjectScopeRules.MinRoleFor(required));

    [Fact]
    public void 改项目自身_要求Manager() =>
        Assert.Equal(ProjectRole.Manager, ProjectScopeRules.MinRoleFor(Permission.ManageProjects));

    [Theory]
    [InlineData(Permission.ManageUsers)]
    [InlineData(Permission.ManageSettings)]
    public void 改成员或系统设置_要求Owner(Permission required) =>
        Assert.Equal(ProjectRole.Owner, ProjectScopeRules.MinRoleFor(required));

    // ------------------------------ 组合位图的优先级（取最高档） ------------------------------

    [Fact]
    public void 多权限叠加_取最高档()
    {
        // 查看 + 业务写 → Tester（高于 Viewer）
        Assert.Equal(ProjectRole.Tester, ProjectScopeRules.MinRoleFor(Permission.ViewProjects | Permission.ManageTestCases));
        // 业务写 + 改项目 → Manager（高于 Tester）
        Assert.Equal(ProjectRole.Manager, ProjectScopeRules.MinRoleFor(Permission.ManageTestCases | Permission.ManageProjects));
        // 改项目 + 改成员 → Owner（高于 Manager）
        Assert.Equal(ProjectRole.Owner, ProjectScopeRules.MinRoleFor(Permission.ManageProjects | Permission.ManageUsers));
    }
}
