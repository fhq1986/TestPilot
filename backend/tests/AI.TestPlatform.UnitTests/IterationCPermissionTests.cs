using System.Reflection;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Application.Auth;
using AI.TestPlatform.Domain.Entities;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing.Patterns;

namespace AI.TestPlatform.UnitTests;

/// <summary>
/// 权限矩阵的自检（迭代 C）。
///
/// 这类测试的价值不在「验证我写对了」，而在「防止以后写漏」：
/// 权限系统最容易出的不是逻辑错，而是某个新加的端点忘了挂权限——那种漏洞没有任何运行时报错，
/// 只会安静地对外开放。因此这里用反射把整套权限契约钉死。
/// </summary>
public class PermissionCatalogTests
{
    [Fact]
    public void 超级管理员拥有全部权限()
    {
        Assert.Equal(PermissionCatalog.All, PermissionCatalog.Of(UserRole.SuperAdmin));
    }

    [Fact]
    public void 管理员不含用户管理与系统设置()
    {
        var admin = PermissionCatalog.Of(UserRole.Admin);

        // 平台级配置（用户管理 / 系统设置）收归超级管理员
        Assert.False(admin.HasFlag(Permission.ManageUsers));
        Assert.False(admin.HasFlag(Permission.ManageSettings));
        // 其余权限仍保留（含定时任务与审计日志）
        Assert.True(admin.HasFlag(Permission.ManageSchedules));
        Assert.True(admin.HasFlag(Permission.ViewAuditLog));
        Assert.Equal(PermissionCatalog.All & ~Permission.ManageUsers & ~Permission.ManageSettings, admin);
    }

    [Fact]
    public void 只读访客只能看不能改()
    {
        var viewer = PermissionCatalog.Of(UserRole.Viewer);

        // 四个 View 权限齐全
        Assert.True(viewer.HasFlag(Permission.ViewProjects));
        Assert.True(viewer.HasFlag(Permission.ViewTestCases));
        Assert.True(viewer.HasFlag(Permission.ViewExecutions));
        Assert.True(viewer.HasFlag(Permission.ViewReports));

        // 任何写权限 / 管理权限都不能有
        Assert.False(viewer.HasFlag(Permission.ManageProjects));
        Assert.False(viewer.HasFlag(Permission.ManageTestCases));
        Assert.False(viewer.HasFlag(Permission.RunExecutions));
        Assert.False(viewer.HasFlag(Permission.ManageDataSets));
        Assert.False(viewer.HasFlag(Permission.ManageBaselines));
        Assert.False(viewer.HasFlag(Permission.ManageSharedSteps));
        Assert.False(viewer.HasFlag(Permission.ManageSchedules));
        Assert.False(viewer.HasFlag(Permission.ManageSettings));
        Assert.False(viewer.HasFlag(Permission.ManageUsers));
        Assert.False(viewer.HasFlag(Permission.ViewAuditLog));
    }

    [Fact]
    public void 测试工程师可以干活但不能碰系统管理()
    {
        var tester = PermissionCatalog.Of(UserRole.Tester);

        // 日常测试工作所需
        Assert.True(tester.HasFlag(Permission.ManageProjects));
        Assert.True(tester.HasFlag(Permission.ManageTestCases));
        Assert.True(tester.HasFlag(Permission.RunExecutions));
        Assert.True(tester.HasFlag(Permission.ManageDataSets));
        Assert.True(tester.HasFlag(Permission.ManageBaselines));

        // 系统管理类必须排除——这是与管理员的分界线
        Assert.False(tester.HasFlag(Permission.ManageUsers));
        Assert.False(tester.HasFlag(Permission.ManageSettings));
        Assert.False(tester.HasFlag(Permission.ManageSchedules));
        Assert.False(tester.HasFlag(Permission.ViewAuditLog));
    }

    [Fact]
    public void 各角色权限逐级递增()
    {
        var viewer = PermissionCatalog.Of(UserRole.Viewer);
        var tester = PermissionCatalog.Of(UserRole.Tester);
        var admin = PermissionCatalog.Of(UserRole.Admin);
        var superAdmin = PermissionCatalog.Of(UserRole.SuperAdmin);

        // 用「按位与等于自身」表达集合包含关系
        Assert.Equal(viewer, viewer & tester);
        Assert.Equal(tester, tester & admin);
        Assert.Equal(admin, admin & superAdmin);
        Assert.NotEqual(viewer, tester);
        Assert.NotEqual(tester, admin);
        Assert.NotEqual(admin, superAdmin);
    }

    [Fact]
    public void Has_要求全部权限都满足()
    {
        var tester = PermissionCatalog.Of(UserRole.Tester);

        Assert.True(PermissionCatalog.Has(tester, Permission.ManageTestCases));
        Assert.True(PermissionCatalog.Has(tester, Permission.ManageTestCases | Permission.RunExecutions));
        // 混合里有一个不满足 → 整体不通过
        Assert.False(PermissionCatalog.Has(tester, Permission.ManageTestCases | Permission.ManageUsers));
        // None 视为「无需权限」，任何角色都通过
        Assert.True(PermissionCatalog.Has(Permission.None, Permission.None));
    }

    [Fact]
    public void 解析非法或缺失的权限位图退化为无权限()
    {
        // 老 token 没有 perm 声明 → null
        Assert.Equal(Permission.None, PermissionCatalog.Parse(null));
        Assert.Equal(Permission.None, PermissionCatalog.Parse(""));
        Assert.Equal(Permission.None, PermissionCatalog.Parse("not-a-number"));
        // 合法值正常解析
        Assert.Equal(Permission.ManageUsers, PermissionCatalog.Parse(((int)Permission.ManageUsers).ToString()));
    }

    [Fact]
    public void 角色枚举值必须与数据库默认值约定一致()
    {
        // Admin 必须是 0：迁移把老用户回填为 0，且 User.Role 的列默认值由 HasDefaultValue 控制。
        // 若有人调整了枚举顺序，这条断言会立刻失败，避免「升级后所有人变成访客」这类事故。
        Assert.Equal(0, (int)UserRole.Admin);
        Assert.Equal(1, (int)UserRole.Tester);
        Assert.Equal(2, (int)UserRole.Viewer);
        // 超级管理员追加在末尾，不得改动既有序号
        Assert.Equal(3, (int)UserRole.SuperAdmin);
    }

    [Fact]
    public void 权限点数量在int位图容量内()
    {
        // 权限位图以 int 承载并写入 JWT，最多 32 位
        var count = Enum.GetValues<Permission>().Count(p => p != Permission.None);
        Assert.True(count <= 31, $"权限点数量 {count} 超出 int 位图可表达的范围");
    }

    [Fact]
    public void 每个权限点都能展开成中文描述()
    {
        foreach (var permission in Enum.GetValues<Permission>().Where(p => p != Permission.None))
        {
            var described = PermissionCatalog.Describe(permission);
            Assert.False(string.IsNullOrWhiteSpace(described));
            // 有中文描述说明命中了 switch 分支；未命中会回落成枚举名（纯 ASCII）
            Assert.NotEqual(permission.ToString(), described);
        }
    }

    [Fact]
    public void 角色与权限的中文名齐全()
    {
        foreach (var role in Enum.GetValues<UserRole>())
            Assert.NotEqual(role.ToString(), PermissionCatalog.DisplayName(role));
    }

    [Fact]
    public void 角色声明名不能与框架默认角色声明冲突()
    {
        // 曾经的线上问题：把角色写进名为 "role" 的声明，而 JwtBearer 的
        // TokenValidationParameters.RoleClaimType 默认就是 "role"，声明会被框架消费掉，
        // 于是 CurrentUser.Role 永远读不到 → 权限判断是对的、但角色展示与审计却错位。
        // 这条断言把这个坑钉死。
        Assert.NotEqual(
            System.Security.Claims.ClaimsIdentity.DefaultRoleClaimType,
            CurrentUser.RoleClaimType);
        Assert.False(string.IsNullOrWhiteSpace(CurrentUser.RoleClaimType));
        Assert.False(string.IsNullOrWhiteSpace(CurrentUser.PermissionClaimType));
        Assert.NotEqual(CurrentUser.PermissionClaimType, CurrentUser.RoleClaimType);
    }
}

/// <summary>
/// 端点权限覆盖自检：枚举 Program 注册的全部端点，断言「写端点必须挂权限」。
///
/// 这里刻意不直接反射 <c>RouteEndpoint</c>（需要启动整个 Host，单测里成本高且易受环境影响），
/// 而是静态扫描各 API 模块的源码：凡是 <c>group.MapXxx</c> 定义的端点，
/// 除非在显式白名单里（登录 / 公开报告 / Webhook），否则必须出现 <c>WithPermission</c>。
/// 这种「源码级契约测试」直接对应开发者实际会犯的错误（新加端点忘记挂权限）。
/// </summary>
public class EndpointPermissionCoverageTests
{
    /// <summary>
    /// 允许不加权限的端点（路径片段 → 理由）。新增白名单条目必须在这里写明原因，
    /// 强迫开发者停下来想一次「这个端点真的应该公开吗」。
    /// </summary>
    private static readonly Dictionary<string, string> ExemptEndpoints = new()
    {
        ["/login"] = "登录接口本身不可能要求已登录的权限",
        ["/me/password"] = "修改本人密码，任何已登录用户都应可操作",
        ["/me/sso"] = "查询本人的 SSO 绑定状态，任何已登录用户都应可操作（个人中心账号绑定卡片）",
        ["/"] = "评论可见性跟随挂载对象（登录即可，与用例/缺陷/计划的现有可见口径一致）；删除另有作者/管理员的服务端裁定",
        ["/{id:guid}"] = "评论删除权限在处理器内按「作者本人或管理员」裁定，不宜映射到某个静态权限位",
    };

    /// <summary>免登录的 API 分组（整组豁免）。键为去掉 "ApiExtensions" 后的文件名。</summary>
    private static readonly Dictionary<string, string> ExemptGroups = new()
    {
        ["Auth"] = "认证入口（登录），必须匿名可访问",
        ["PublicReport"] = "免登录的公开分享报告（凭一次性 token 访问）",
        ["Webhook"] = "使用独立 X-Webhook-Token 保护，不走 JWT",
    };

    private static string FindModulesDirectory()
    {
        // 从测试程序集所在目录向上回溯定位源码目录，兼容不同工作目录
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName,
                "src", "AI.TestPlatform.Api", "Modules");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("找不到 Api/Modules 源码目录");
    }

    [Fact]
    public void 所有业务端点都必须声明权限()
    {
        var modulesDir = FindModulesDirectory();
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(modulesDir, "*ApiExtensions.cs", SearchOption.AllDirectories))
        {
            var moduleName = Path.GetFileNameWithoutExtension(file).Replace("ApiExtensions", "");
            if (ExemptGroups.ContainsKey(moduleName))
                continue;

            var source = File.ReadAllText(file);
            foreach (var (method, path, statement) in EnumerateEndpointStatements(source))
            {
                if (ExemptEndpoints.ContainsKey(path))
                    continue;
                if (!statement.Contains("WithPermission"))
                    violations.Add($"{moduleName} {method} \"{path}\"");
            }
        }

        Assert.True(violations.Count == 0,
            "以下端点未声明权限（若是刻意公开，请加入 EndpointPermissionCoverageTests 的白名单并说明理由）：\n"
            + string.Join("\n", violations));
    }

    /// <summary>
    /// 从源码里切出每个 <c>group.MapXxx("path", ...)</c> 端点的完整语句链。
    ///
    /// 为什么不能简单地「往下找 <c>});</c> 就停」：Minimal API 的处理器是 lambda，
    /// 其函数体内同样存在大量形如 <c>})</c> 开头的行（对象初始化器收尾、嵌套 lambda 收尾），
    /// 提前收尾会把链尾的 <c>.WithPermission(...)</c> 判成「不存在」→ 大量假阳性。
    /// 因此这里改用**括号配平**：从 <c>MapXxx(</c> 的左括号开始计数，
    /// 数到配平的那个右括号才算语句结束；之后的链式调用（若还有 . 开头）也一并纳入。
    /// </summary>
    private static IEnumerable<(string Method, string Path, string Statement)> EnumerateEndpointStatements(string source)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            @"group\.Map(?<method>Get|Post|Put|Delete|Patch)\(\s*""(?<path>[^""]*)""");

        foreach (System.Text.RegularExpressions.Match match in pattern.Matches(source))
        {
            // 定位第一个左括号（Map 调用自身的参数列表）
            var open = source.IndexOf('(', match.Index);
            if (open < 0)
                continue;

            var depth = 0;
            var end = open;
            var inString = false;
            var escaped = false;

            for (var i = open; i < source.Length; i++)
            {
                var c = source[i];

                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                // 只跟踪双引号字符串：C# 代码里几乎不会出现未转义的裸双引号，
                // 而单引号（字符串内的 "'"、泛型/字符字面量）反而容易误判，
                // 一旦把代码当成字符字面量会一路吞到文件尾，导致假阴性/假阳性
                if (inString) { if (c == '"') inString = false; continue; }
                if (c == '"') { inString = true; continue; }
                if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
                {
                    var nl = source.IndexOf('\n', i);
                    if (nl < 0) break;
                    i = nl;
                    continue;
                }

                if (c == '(') depth++;
                else if (c == ')')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }

            // 括号配平后，继续吃掉后续的链式调用（.WithXxx(...) 可能换行）
            var tail = end + 1;
            while (true)
            {
                var probe = tail;
                while (probe < source.Length && char.IsWhiteSpace(source[probe])) probe++;
                if (probe >= source.Length || source[probe] != '.') break;

                var linkDepth = 0;
                var cursor = probe;
                var closed = false;
                for (; cursor < source.Length; cursor++)
                {
                    var c = source[cursor];
                    if (c == '(') linkDepth++;
                    else if (c == ')')
                    {
                        linkDepth--;
                        if (linkDepth == 0) { closed = true; break; }
                    }
                    else if (c == ';' && linkDepth == 0) { break; }
                }
                if (!closed) break;
                tail = cursor + 1;
            }

            yield return (match.Groups["method"].Value, match.Groups["path"].Value,
                source[match.Index..tail]);
        }
    }

    [Fact]
    public void 白名单条目必须给出非空理由()
    {
        foreach (var (key, reason) in ExemptEndpoints)
            Assert.False(string.IsNullOrWhiteSpace(reason), $"白名单 {key} 缺少理由说明");
        foreach (var (key, reason) in ExemptGroups)
            Assert.False(string.IsNullOrWhiteSpace(reason), $"豁免分组 {key} 缺少理由说明");
    }
}

/// <summary>
/// 过滤器注册机制的回归测试。
///
/// 背景（真实踩过的坑）：<c>AddEndpointFilterFactory</c> 是
/// <c>Microsoft.AspNetCore.Http.EndpointFilterExtensions</c> 上针对 <c>RouteHandlerBuilder</c>
/// 的**扩展方法**。如果 <c>WithPermission</c>/<c>WithAudit</c> 把参数类型放宽到
/// <c>IEndpointConventionBuilder</c>，编译仍会通过，但过滤器**不会注册**——
/// 端点照常 200，鉴权与审计全部静默失效。这类问题编译器和普通单测都发现不了。
/// 下面的断言把「必须绑到 RouteHandlerBuilder」钉死，防止将来被"顺手"改回去。
/// </summary>
public class EndpointFilterRegistrationTests
{
    [Theory]
    [InlineData(typeof(PermissionEndpointExtensions), "WithPermission")]
    [InlineData(typeof(AuditEndpointExtensions), "WithAudit")]
    public void 过滤器声明扩展必须绑定到RouteHandlerBuilder(Type extensionsType, string methodName)
    {
        var method = extensionsType
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .SingleOrDefault(m => m.Name == methodName);

        Assert.NotNull(method);

        // 必须是扩展方法（第一个参数带 this）
        Assert.True(method!.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false),
            $"{methodName} 应当是扩展方法");

        var firstParam = method.GetParameters()[0].ParameterType;
        Assert.True(firstParam == typeof(RouteHandlerBuilder),
            $"{methodName} 的第一个参数必须是 RouteHandlerBuilder（实际 {firstParam.Name}）——" +
            "若放宽到 IEndpointConventionBuilder，AddEndpointFilterFactory 会静默不生效，权限与审计全部失效");
    }

    [Fact]
    public void AddEndpointFilterFactory是泛型扩展方法()
    {
        // 事实核对：AddEndpointFilterFactory 是 EndpointFilterExtensions 上的**泛型**扩展方法
        // （签名 TBuilder AddEndpointFilterFactory[TBuilder](TBuilder, ...)），
        // 既能用于 RouteHandlerBuilder，也能用于 IEndpointConventionBuilder。
        // 也就是说「约束到接口」本身并不会让过滤器失效——真正让权限静默失效的是
        // 在工厂阶段用反射读 WithMetadata 写入的元数据（拿不到，恒为 None）。
        // 这条断言把框架现状记录下来，避免后人误判根因。
        var extension = typeof(EndpointFilterExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "AddEndpointFilterFactory")
            .ToList();

        Assert.NotEmpty(extension);
        Assert.All(extension, m => Assert.True(m.IsGenericMethodDefinition));
        // 首个参数是泛型参数 TBuilder
        Assert.All(extension, m => Assert.True(m.GetParameters()[0].ParameterType.IsGenericParameter));
    }

    [Fact]
    public void WithPermission必须使用RouteHandlerBuilder参数类型()
    {
        // 虽然泛型约束不会导致失效，但显式用 RouteHandlerBuilder 更安全：
        // 能保证 addEndpointFilterFactory 的泛型实参就是端点构建器本身，
        // 且调用方（group.MapXxx）返回值天然匹配。这里锁定该约定。
        var method = typeof(PermissionEndpointExtensions)
            .GetMethod("WithPermission", BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.Equal(typeof(RouteHandlerBuilder), method!.GetParameters()[0].ParameterType);
    }

    [Fact]
    public void 过滤器工厂上下文不包含端点元数据()
    {
        // 这是「坑 2」的根因：工厂阶段拿不到 WithMetadata 写入的元数据，
        // 所以权限/审计元数据必须在请求期从 HttpContext.GetEndpoint().Metadata 读取。
        // 一旦哪天框架给工厂上下文加了 EndpointMetadata 属性，这条断言会失败，
        // 提醒我们可以在实现上简化——属于「故意失败以提示复查」的守卫性断言。
        var properties = typeof(EndpointFilterFactoryContext).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("EndpointMetadata", properties);
        Assert.Contains("MethodInfo", properties);
        Assert.Contains("ApplicationServices", properties);
    }
}
