namespace AI.TestPlatform.Domain.Auth;

/// <summary>
/// 项目级角色（迭代 E·① 引入）。与全局 <see cref="UserRole"/> 正交：
/// 全局角色决定「平台能干什么」，项目角色决定「在某个项目里能干什么」。
///
/// 数值越大权限越高（<c>Owner &gt; Manager &gt; Tester &gt; Viewer</c>），便于用
/// <c>role &gt;= required</c> 做「至少某档」的判定。
/// int 落库——只能追加末尾，改既有值会让历史成员错位且不报错。
/// </summary>
public enum ProjectRole
{
    /// <summary>只读：能看项目及其子资源（用例/执行/报告/计划），不能改</summary>
    Viewer = 0,

    /// <summary>测试工程师：在 Viewer 基础上可管理用例、跑执行、管数据集/基线/计划</summary>
    Tester = 1,

    /// <summary>项目管理员：在 Tester 基础上可改项目设置、环境、定时任务等配置</summary>
    Manager = 2,

    /// <summary>项目所有者：项目内最高权限，可管理成员、删除/归档项目</summary>
    Owner = 3,
}
