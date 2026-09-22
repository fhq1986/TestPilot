using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using AI.TestPlatform.Api.AI;
using AI.TestPlatform.Api.Auth;
using AI.TestPlatform.Api.Auth.Sso;
using AI.TestPlatform.Api.Execution;
using AI.TestPlatform.Api.LoadTesting;
using AI.TestPlatform.Api.Modules.LoadTests;
using AI.TestPlatform.Api.Audit;
using AI.TestPlatform.Api.Hubs;
using AI.TestPlatform.Api.Mocks;
using AI.TestPlatform.Api.Modules.Artifacts;
using AI.TestPlatform.Api.Observability;
using AI.TestPlatform.Api.Recorder;
using AI.TestPlatform.Api.TestCases;
using AI.TestPlatform.Api.TestPlans;
using AI.TestPlatform.Api.Modules.AI;
using AI.TestPlatform.Api.Modules.Audit;
using AI.TestPlatform.Api.Modules.Auth;
using AI.TestPlatform.Api.Modules.Chat;
using AI.TestPlatform.Api.Modules.Comments;
using AI.TestPlatform.Api.Modules.DataSets;
using AI.TestPlatform.Api.Modules.Requirements;
using AI.TestPlatform.Api.Modules.Defects;
using AI.TestPlatform.Api.Modules.Nodes;
using AI.TestPlatform.Api.Modules.Notifications;
using AI.TestPlatform.Api.Modules.Environments;
using AI.TestPlatform.Api.Modules.Executions;
using AI.TestPlatform.Api.Modules.Mocks;
using AI.TestPlatform.Api.Modules.Projects;
using AI.TestPlatform.Api.Modules.PublicReports;
using AI.TestPlatform.Api.Modules.Recorder;
using AI.TestPlatform.Api.Modules.Reports;
using AI.TestPlatform.Api.Modules.Schedules;
using AI.TestPlatform.Api.Modules.Scripts;
using AI.TestPlatform.Api.Modules.Settings;
using AI.TestPlatform.Api.Modules.SharedSteps;
using AI.TestPlatform.Api.Modules.Shares;
using AI.TestPlatform.Api.Modules.Stats;
using AI.TestPlatform.Api.Modules.Suites;
using AI.TestPlatform.Api.Modules.TestCases;
using AI.TestPlatform.Api.Modules.TestPlans;
using AI.TestPlatform.Api.Modules.Users;
using AI.TestPlatform.Api.Modules.Visual;
using AI.TestPlatform.Api.Modules.Webhooks;
using AI.TestPlatform.Api.Notifications;
using AI.TestPlatform.Api.Reports;
using AI.TestPlatform.Api.Schedules;
using AI.TestPlatform.Api.Settings;
using AI.TestPlatform.Api.Startup;
using AI.TestPlatform.Api.Suites;
using AI.TestPlatform.Api.Visual;
using AI.TestPlatform.Application.AI;
using AI.TestPlatform.Application.Projects;
using AI.TestPlatform.Infrastructure.Data;
using Microsoft.Extensions.Options;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// 迭代 E·③：可观测性（Serilog 结构化日志 + OpenTelemetry trace/metrics；OTLP 导出 env 门控）
builder.AddPlatformObservability();

// 迭代 F·§3.8：本进程的角色（控制面 / 执行面）。
// 「注册哪些 HostedService」「要不要映射 HTTP 端点」都是**注册期**决定，运行期再判断就晚了，
// 所以在这里先从同一配置节读一份出来。DI 里的权威来源仍是下面的 Configure<ExecutionOptions>，
// 两处读的是同一个键，不会分叉。
var executionRole = builder.Configuration.GetSection("Execution").Get<ExecutionOptions>()?.Role
                    ?? ExecutionRole.All;
if (!executionRole.IsControlPlane())
{
    // 纯执行面进程不对外提供服务。这里绑随机回环端口而不是把 Kestrel 关掉：
    // 后台任务与 SignalR 的 IHubContext 都依赖 Host 正常启动，只是没有任何可路由的端点。
    builder.WebHost.UseUrls("http://127.0.0.1:0");
}

// 生产环境启动校验：敏感配置必须通过环境变量覆盖，禁止携带开发默认值上线
if (builder.Environment.IsProduction())
{
    var failures = new List<string>();
    var jwtKey = builder.Configuration["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
        failures.Add("Jwt:Key 未配置或长度不足 32 字符（环境变量 Jwt__Key）");
    if (string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("Default")))
        failures.Add("ConnectionStrings:Default 未配置（环境变量 ConnectionStrings__Default）");
    if (string.IsNullOrWhiteSpace(builder.Configuration["Webhook:Token"]))
        failures.Add("Webhook:Token 未配置（环境变量 Webhook__Token）");
    if (string.IsNullOrWhiteSpace(builder.Configuration["AllowedOrigins"]))
        failures.Add("AllowedOrigins 未配置（环境变量 AllowedOrigins，逗号分隔的前端地址）");
    if (failures.Count > 0)
        throw new InvalidOperationException("生产环境配置校验失败：\n" + string.Join("\n", failures));
}

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // 让 Swagger 尊重 C# 可空引用类型标注：`SelectorConfig? Selector` 会生成
    // `nullable: true`，而不是一律按非空对象声明。
    // 不开启的话，契约里「可空」与「非空」全被抹平——响应校验脚本会拿一个
    // 声称必填的对象 schema 去校验实际为 null 的字段，报出成片假阳性，
    // 门禁就失去意义（迭代 F·② 实测：selector / project 两个字段）。
    options.SupportNonNullableReferenceTypes();

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "输入 JWT Token（不含 Bearer 前缀）",
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            },
            Array.Empty<string>()
        },
    });
});

// M8/审计：SaveChanges 拦截器统一给带审计字段的实体盖「创建人/创建时间/修改人/修改时间」
builder.Services.AddSingleton<AuditStampInterceptor>();
builder.Services.AddDbContext<TestDbContext>((sp, options) =>
    // 迭代 E·④ 韧性：Npgsql 瞬时故障（连接闪断/主备切换）自动重试。
    // 与启动时 pg_advisory_lock 迁移不冲突：那段代码显式 OpenConnectionAsync 后复用同一连接，
    // 重试策略的 OpenAsync 对已打开的连接是 no-op，会话锁不会因重试而丢。
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default"),
               npgsql => npgsql.UseVector().EnableRetryOnFailure())
           .AddInterceptors(sp.GetRequiredService<AuditStampInterceptor>()));

var jwtOptions = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
// 迭代 E·①：项目级授权（成员激活式；管理员全局放行）。供 ProjectScopeFilter 与成员管理端点复用。
builder.Services.AddScoped<IProjectAuthorization, ProjectAuthorization>();
builder.Services.AddScoped<IAuthService, AuthService>();
// SSO 扫码登录（企业微信 / 钉钉 / 标准 OIDC）：生效配置在系统设置页维护（SystemConfig 表，保存即生效）；
// appsettings 的 Sso 节仅作为首次种子。默认全关——不配置时登录页只显示密码表单。
// 迭代 E·④：IdP 侧偶发 5xx / 连接抖动由标准弹性处理器兜底（发现文档与换令牌都是幂等只读/短事务）。
builder.Services.AddHttpClient("sso").AddStandardResilienceHandler();
// 外部缺陷系统（Jira/禅道）推送客户端：目标是公网/企业内网系统，走系统代理而非强制直连。
// 弹性处理器自带超时管理，故把 HttpClient.Timeout 放开为无限——否则它会先取消整条重试管道。
builder.Services.AddHttpClient("external-defects", client =>
    client.Timeout = Timeout.InfiniteTimeSpan)
    .AddStandardResilienceHandler(o =>
    {
        o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(60);
        // 必须 ≤ 熔断采样窗口(默认 30s) 的一半，否则启动时 OptionsValidationException：
        // "sampling duration ... needs to be at least double of an attempt timeout"
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(15);
    });
builder.Services.Configure<ExternalDefectOptions>(builder.Configuration.GetSection("ExternalDefects"));
builder.Services.AddScoped<ExternalDefectPusher>();
builder.Services.AddScoped<SsoLoginService>();
// 迭代 E·②：标准 OIDC 发现文档缓存（按 Authority 去重、含 jwks 轮换）。必须单例——缓存要跨请求存活。
builder.Services.AddSingleton<OidcDiscoveryCache>();
// 用户管理（迭代 C）：账号 CRUD / 改角色 / 重置密码 / 启停用，任何变更都自增 TokenVersion 踢掉旧会话
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<AuditLogService>();
builder.Services.Configure<AuditOptions>(builder.Configuration.GetSection("Audit"));
// 脚本录制（迭代 C）：驱动 Playwright codegen 生成脚本，会话状态保存在数据库
// 进程启动走抽象（IRecorderProcessLauncher），使命令行拼装与状态机可被单测覆盖
builder.Services.AddSingleton<IRecorderProcessLauncher, RecorderProcessLauncher>();
builder.Services.AddScoped<RecorderService>();
// 测试计划（验收过程）：范围体检、轮次生命周期、达标判定数据装配
builder.Services.AddScoped<TestPlanService>();
// 计划验收报告 xlsx 导出
builder.Services.AddScoped<TestPlanReportService>();
// 用例版本快照：保存时记录历史、支持查看与回滚
builder.Services.AddScoped<TestCaseVersionService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            },
            // 会话版本校验：改角色 / 重置密码 / 停用账号后，旧 token 立即失效（30 秒缓存窗口）
            OnTokenValidated = async context =>
            {
                var rawId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                var rawVer = context.Principal?.FindFirstValue(AuthService.TokenVersionClaimType);
                if (!Guid.TryParse(rawId, out var userId) || !int.TryParse(rawVer, out var version))
                {
                    context.Fail("令牌缺少会话标识");
                    return;
                }

                var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                if (!await authService.IsSessionValidAsync(userId, version, context.HttpContext.RequestAborted))
                    context.Fail("会话已失效，请重新登录");
            },
        };
    });
builder.Services.AddAuthorization();
// SignalR Redis backplane（审查发现）：单机部署不需要 Redis，留空即保持进程内广播；
// 多实例部署时配置 SignalR:Redis 连接串，跨实例的执行事件才能送达任意节点上的客户端。
// ⚠ 迭代 F·§3.8 起这一项对「执行面拆进程」是**必需**的：WorkerOnly 进程只拿 IHubContext
//   发事件（它不映射 Hub），没有 backplane 的话事件发在自己进程里、没有浏览器连接，等于全丢。
// 注意 AddSignalR 两种角色都要注册——它同时提供 IHubContext 这个「发布端」。
var signalrBuilder = builder.Services.AddSignalR();
var signalrRedis = builder.Configuration["SignalR:Redis"];
if (!string.IsNullOrWhiteSpace(signalrRedis))
    signalrBuilder.AddStackExchangeRedis(signalrRedis);
builder.Services.AddValidatorsFromAssemblyContaining<CreateProjectRequest>();
// Api 程序集里的校验器不会被上面的扫描覆盖（扫描范围是 Application 程序集），需显式注册
builder.Services.AddScoped<IValidator<CustomFieldApiExtensions.CreateCustomFieldRequest>,
    CustomFieldApiExtensions.CreateCustomFieldValidator>();

// CORS：AllowedOrigins 逗号分隔（环境变量 AllowedOrigins 覆盖）
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = (builder.Configuration["AllowedOrigins"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (origins.Length > 0)
            policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        else
            policy.SetIsOriginAllowed(_ => builder.Environment.IsDevelopment())
                .AllowAnyHeader().AllowAnyMethod();
    });
});

// 限流：AI 接口调用外部 LLM，并发 + 频率双重限制
// 全局兜底限额做成可配置——写死 100/分钟在两种场景下都会误伤：
//   1) 办公网出口 NAT 后所有用户共享同一 IP 分区；
//   2) 集成测试共享同一个宿主且都用 admin 登录，会命中同一个用户分区。
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var globalPermitLimit = builder.Configuration.GetValue("RateLimit:GlobalPermitLimit", 100);
    options.AddPolicy("ai", httpContext =>
        RateLimitPartition.GetConcurrencyLimiter(
            httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new ConcurrencyLimiterOptions
            {
                PermitLimit = 2,
                QueueLimit = 0,
            }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = globalPermitLimit,
                Window = TimeSpan.FromMinutes(1),
            }));

    // 免登录报告导出（安全审查 S3）：每次请求都重新生成整份 xlsx（项目报告内嵌最多 80 张
    // 截图并逐张缩放），全局限流 100/分钟挡不住滥用——单机 CPU 与磁盘 IO 会被打满。
    // 匿名请求按 IP 分区，给独立且更严的固定窗口。
    options.AddPolicy("public-export", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimit:PublicExportPermitLimit", 5),
                Window = TimeSpan.FromMinutes(1),
            }));

    // 登录（安全审查 S4）：匿名端点按 IP 给最严的固定窗口，弱密码没法被持续爆破。
    // 注意 NAT 后多人共享出口 IP 的场景，5 次/分钟可能误伤集体登录——可通过配置放宽。
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = builder.Configuration.GetValue("RateLimit:LoginPermitLimit", 5),
                Window = TimeSpan.FromMinutes(1),
            }));
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<TestDbContext>("database");

builder.Services.AddSingleton<ExecutionQueue>();
// 产物存储（截图/基线/trace）：local=本地磁盘（单机默认），minio=对象存储（容器化部署）。
// 按 Storage:Provider 切换；URL 形态与存量数据完全兼容
if (builder.Configuration["Storage:Provider"]?.Equals("minio", StringComparison.OrdinalIgnoreCase) == true)
{
    builder.Services.AddSingleton<IArtifactStore, MinioArtifactStore>();
}
else
{
    builder.Services.AddSingleton<IArtifactStore, LocalArtifactStore>();
}
builder.Services.AddSingleton<ScreenshotStorage>();
// 迭代 D：浏览器实例池（Playwright 驱动进程 + 每个引擎一个常驻 IBrowser，每次执行只新建 context）
builder.Services.AddSingleton<BrowserPool>();
// 迭代 D：Playwright trace 存储（失败时保留可回放包，成功时丢弃）
builder.Services.AddSingleton<TraceStorage>();
// 迭代 D：无障碍（WCAG）扫描，基于内置 axe-core
builder.Services.AddSingleton<A11yScanner>();
// 迭代 F：执行录像存储（失败时保留，与 trace 同一套取舍）
builder.Services.AddSingleton<VideoStorage>();
// 迭代 F：自动登录得到的 storageState 进程内缓存（单例——缓存必须跨执行共享才有意义）
builder.Services.AddSingleton<AuthStateCache>();
// 执行引擎参数（选择器快速探测 / 自愈行为 / 并行度 / 心跳），见 appsettings.json 的 Execution 节
builder.Services.Configure<ExecutionOptions>(builder.Configuration.GetSection("Execution"));
builder.Services.AddScoped<TestRunner>();
// 迭代 F·§3.8：执行面与调度面按角色注册。
//   执行面（All / WorkerOnly）= 真正跑用例：执行 worker + 节点心跳 + 保留期清理
//   调度面（All / ApiOnly）  = 只负责把到期的定时任务入队、以及录制器——它们属控制面职责，
//                              放在 WorkerOnly 里没人触发，放在 ApiOnly 里必须有（否则定时任务永远不跑）
if (executionRole.IsExecutionPlane())
{
    builder.Services.AddHostedService<ExecutionWorker>();
    // 节点登记/心跳（分布式执行可观测）：每个运行执行器的进程都在 ExecutionNodes 表登记自己
    builder.Services.AddHostedService<ExecutionNodeRegistry>();
    // 周期保留期清理（审查发现）：截图/trace/审计日志的保留期不能只靠重启触发
    builder.Services.AddHostedService<MaintenanceWorker>();
}
if (executionRole.IsControlPlane())
{
    // 定时任务：调度器 + 展开服务
    builder.Services.AddScoped<ScheduleService>();
    builder.Services.AddHostedService<ScheduleWorker>();
    // 迭代 C：录制器参数（开关 / 录制目录 / 并发上限 / 空闲回收），见 appsettings.json 的 Recorder 节
    builder.Services.Configure<RecorderOptions>(builder.Configuration.GetSection("Recorder"));
    builder.Services.AddHostedService<RecorderWorker>();
}
builder.Services.Configure<MaintenanceOptions>(builder.Configuration.GetSection("Maintenance"));
// 迭代 F·P2-9：压测场景（k6）。执行器属执行面，脚本生成器与 API 属控制面/共用。
builder.Services.Configure<LoadTestOptions>(builder.Configuration.GetSection("LoadTest"));
builder.Services.AddSingleton<LoadTestQueue>();
builder.Services.AddScoped<LoadTestScriptBuilder>();
builder.Services.AddSingleton<IK6ProcessRunner, K6ProcessRunner>();
if (executionRole.IsExecutionPlane())
{
    builder.Services.AddHostedService<LoadTestWorker>();
}
// 迭代 B：执行计划器（浏览器矩阵 / 数据驱动展开）、套件运行、视觉回归
builder.Services.AddScoped<ExecutionPlanner>();
builder.Services.AddScoped<SuiteRunner>();
// 执行编排：前置依赖与失败策略的收尾结算（执行落地后按批次扫描一次）
builder.Services.AddScoped<ExecutionOrchestrator>();
builder.Services.AddScoped<VisualRegressionService>();
// 迭代 C：录制器参数（开关 / 录制目录 / 并发上限 / 空闲回收），见 appsettings.json 的 Recorder 节
builder.Services.Configure<RecorderOptions>(builder.Configuration.GetSection("Recorder"));
builder.Services.AddHostedService<RecorderWorker>();

builder.Services.AddHttpClient<AIClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["AIWorker:BaseUrl"] ?? "http://127.0.0.1:8000");
    // 之前写死 120s，AIWorker:TimeoutSeconds 配置无人引用（审查发现）；需与 Worker 的
    // LLM 总预算（默认 110s）保持「后端略大于 Worker」的关系，先报错的是前端能感知的后端
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("AIWorker:TimeoutSeconds", 120));
    // 服务间鉴权：与 AIWorker 的 WORKER_TOKEN 环境变量保持一致（本地开发即 appsettings.json 的 dev 值）
    var workerToken = builder.Configuration["AIWorker:Token"];
    if (!string.IsNullOrWhiteSpace(workerToken))
        client.DefaultRequestHeaders.Add("X-Worker-Token", workerToken);
})
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    // AI Worker 部署在本机回环，必须直连：若系统设置了 HTTP_PROXY（且未配置 NO_PROXY），
    // HttpClient 会把 Worker 请求当代理请求处理，在同一 keep-alive 连接上改用绝对 URI 请求行
    // （POST http://127.0.0.1:8000/api/xxx），Worker 路由无法匹配 → 404 {"detail":"Not Found"}
    UseProxy = false,
});

builder.Services.AddHttpClient();
builder.Services.AddTransient<SwaggerImporter>();
builder.Services.AddSingleton<MockService>();

builder.Services.AddScoped<ElementCacheService>();
// D4：语义向量化（默认关闭→走 ElementEmbedder 哈希，零回归；启用时改走 AIWorker /api/embed）
builder.Services.Configure<EmbeddingOptions>(builder.Configuration.GetSection("Embedding"));
builder.Services.AddScoped<IEmbeddingProvider, EmbeddingProvider>();
builder.Services.AddScoped<DiagnosisService>();
builder.Services.AddScoped<SettingsService>();

// M8 Agent 失败自愈闭环。熔断器为**进程内单例**（AILivenessBreaker 自带状态机，勿注册为 Scoped）；
// 编排服务按作用域（依赖 TestRunner/DbContext）。总开关在 SystemConfig，参数走 AgentLoop 配置节。
builder.Services.Configure<AgentLoopOptions>(builder.Configuration.GetSection("AgentLoop"));
builder.Services.AddSingleton<AILivenessBreaker>();
builder.Services.AddScoped<AgentLoopService>();
// M8 Phase 3：Planner（按目标 + 失败历史重构步骤序列）
builder.Services.AddScoped<IPlannerService, AgentPlannerService>();
// 缺陷管理（内建轻量模块，ExternalRef 预留外部对接）
builder.Services.AddScoped<DefectService>();
// 需求覆盖（精简版：覆盖统计锚点，ExternalKey 预留外部需求系统）
builder.Services.AddScoped<RequirementService>();
// Excel 用例导入 / 测试报告导出
builder.Services.AddScoped<TestCaseImportService>();
builder.Services.AddScoped<TestCaseReportService>();
// 分享链接的生成与"取或建"（计划验收邮件与页面分享共用同一套令牌逻辑）
builder.Services.AddScoped<ReportShareLinkService>();
// 在线报告聚合（分享链接与 /share 页面共用）
builder.Services.AddScoped<ReportAggregator>();
// 迭代 A：flake 识别 + 通知推送
builder.Services.AddScoped<FlakeDetectionService>();
builder.Services.AddScoped<NotificationService>();
// 站内消息（消息中心）：与上面的对外推送分工不同，见 InAppNotificationService 的注释
builder.Services.AddScoped<InAppNotificationService>();

var app = builder.Build();

// Swagger 也属控制面：WorkerOnly 进程没有任何端点可文档化，注册了只会多两个无用中间件
if (app.Environment.IsDevelopment() && executionRole.IsControlPlane())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// 全局异常处理：生产环境不泄露堆栈
app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();

        // 请求体不是合法 JSON / 缺少必填参数属于**客户端问题**，应当是 400 而不是 500。
        // Minimal API 的隐式 body 绑定失败会抛 BadHttpRequestException，不特殊处理的话
        // 调用方会以为服务端挂了，日志里也会混进一堆无意义的 error。
        if (feature?.Error is BadHttpRequestException badRequest)
        {
            logger.LogWarning("请求体无效 {Path}：{Message}", context.Request.Path, badRequest.Message);
            context.Response.StatusCode = badRequest.StatusCode;
            context.Response.ContentType = "application/json";
            var detail = app.Environment.IsDevelopment() ? badRequest.Message : "请求内容格式不正确";
            await context.Response.WriteAsync(
                $"{{\"message\":\"{detail.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}");
            return;
        }

        // 唯一约束冲突是**业务冲突**（重名）而不是服务端故障，回 409 更贴近事实。
        // 端点里的应用层查重会先一步拦住绝大多数情况，能走到这里的是并发竞态
        // （两个请求同时通过了查重）——此时数据库索引是最后一道防线，
        // 如果这里仍按 500 返回，用户看到的就是"系统错误"而不是"名称已存在"。
        if (feature?.Error is DbUpdateException dbUpdate
            && dbUpdate.InnerException is Npgsql.PostgresException { SqlState: "23505" } uniqueViolation)
        {
            logger.LogWarning("唯一约束冲突 {Path}：{Constraint}", context.Request.Path, uniqueViolation.ConstraintName);
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{\"message\":\"该名称已存在，请换一个\"}");
            return;
        }

        if (feature?.Error is not null)
            logger.LogError(feature.Error, "未处理异常 {Path}", context.Request.Path);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        var message = app.Environment.IsDevelopment() && feature?.Error is not null
            ? feature.Error.Message
            : "服务器内部错误";
        await context.Response.WriteAsync($"{{\"message\":\"{message.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"}}");
    });
});

app.UseAuthentication();

// 安全响应头（安全审查 S4）：此前一项都没有——报告页可被任意站点 iframe 嵌入、
// 分享令牌可能经 Referer 外泄、嗅探类攻击无防护。
// CSP 收紧到自源；Element Plus 等运行时注入 <style>，故 style-src 保留 unsafe-inline；
// 开发环境 Swagger UI 有内联脚本，script-src 相应放宽（生产不带）。
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["Referrer-Policy"] = "no-referrer";
    headers.XContentTypeOptions = "nosniff";
    headers.XFrameOptions = "DENY";
    headers.ContentSecurityPolicy = context.RequestServices
        .GetRequiredService<IHostEnvironment>().IsDevelopment()
            ? "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; connect-src 'self'; object-src 'none'; base-uri 'self'"
            : "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'self'";
    await next();
});

// 审计变更摘要（安全审查 S2）：请求体是一次性流，端点过滤器在模型绑定**之后**才执行，
// 那时 body 已被读空，审计里 Detail/ResourceName 永远是空。这里在绑定之前对中小体积的
// 写请求开启缓冲，AuditFilter 才能把流拨回开头读到变更摘要。
// 阈值与 AuditFilter.CaptureBody 的判断保持一致；不覆盖大文件上传，避免额外落盘。
app.Use(async (context, next) =>
{
    if (context.Request.ContentLength is > 0 and < 65536)
        context.Request.EnableBuffering();
    await next();
});

var screenshotsPath = Path.GetFullPath(
    builder.Configuration["Screenshots:Path"] ?? "screenshots",
    builder.Environment.ContentRootPath);
Directory.CreateDirectory(screenshotsPath);
// 截图 / 基线对外提供：不再直接挂静态目录——产物可能在 MinIO 里。
// ArtifactMiddleware 优先读产物存储，miss 时回退本地历史文件（对象存储启用前的存量），
// 公开访问语义与防枚举文件名规则保持不变（见 ScreenshotStorage）
app.UseMiddleware<ArtifactMiddleware>();
// 截图仍走静态文件（公开分享页要展示），但文件名含执行级随机串，不可枚举（见 ScreenshotStorage）。
// trace 曾同样经 /traces 静态提供——完全绕过鉴权，且内含 DOM 快照与网络请求头，
// 现已改为受权端点 GET /api/executions/{id}/trace（见 ExecutionApiExtensions）。

app.UseAuthorization();
app.UseCors();
app.UseRateLimiter();

app.MapHealthChecks("/health");

// 以下全部是**控制面**：HTTP API、SignalR Hub、运维指标端点。
// WorkerOnly 进程不映射它们——映射了也没人会访问（它绑在随机回环端口上），
// 反而会让「这个进程到底提供什么」变得含糊。
// 注意 /health 留在外面：Dockerfile 的 HEALTHCHECK 打的就是它，两种角色都需要。
if (executionRole.IsControlPlane())
{
    // 运行指标（Prometheus 文本格式）。**未配置 Metrics:Token 时不注册该端点**（表现为 404）——
    // 运维端点默认关闭，不给出"默认开启且无鉴权"的东西
    MetricsEndpoint.MapMetrics(app, builder.Configuration);

    app.MapGroup("/api/auth").MapAuthApi();
    app.MapGroup("/api/users").MapUserApi().RequireAuthorization();
    app.MapGroup("/api/nodes").MapNodeApi().RequireAuthorization();
    app.MapGroup("/api/audit").MapAuditApi().RequireAuthorization();
    app.MapGroup("/api/recorder").MapRecorderApi().RequireAuthorization();
    app.MapGroup("/api/shared-steps").MapSharedStepApi().WithProjectScope(ProjectResource.SharedStep).RequireAuthorization();
    // 迭代 E·①-4：项目作用域扩到各业务模块。resource 声明 by-id 端点如何把资源 id 反查成项目 id；
    // 查询串 projectId（列表端点）由过滤器直接解析，无需在此声明。
    app.MapGroup("/api/test-plans").MapTestPlanApi().WithProjectScope(ProjectResource.TestPlan).RequireAuthorization();
    app.MapGroup("/api/projects").MapProjectApi().WithProjectScope(ProjectResource.Project).RequireAuthorization();
    app.MapGroup("/api/projects").MapProjectApiTokenApi().WithProjectScope().RequireAuthorization();
    app.MapGroup("/api/projects").MapCustomFieldApi().WithProjectScope().RequireAuthorization();
    app.MapGroup("/api/projects").MapProjectMemberApi().WithProjectScope().RequireAuthorization();
    app.MapGroup("/api/comments").MapCommentApi();
    app.MapGroup("/api/notifications").MapNotificationApi();
    app.MapGroup("/api/defects").MapDefectApi().WithProjectScope(ProjectResource.Defect).RequireAuthorization();
    app.MapGroup("/api/requirements").MapRequirementApi().WithProjectScope(ProjectResource.Requirement).RequireAuthorization();
    // 富文本图片上传（/api/artifacts/image）：需求说明、缺陷描述里插入图片用
    app.MapGroup("/api/artifacts").MapArtifactApi().RequireAuthorization();
    app.MapGroup("/api/testcases").MapTestCaseApi().WithProjectScope(ProjectResource.TestCase).RequireAuthorization();
    // 用例版本历史（/api/testcases/{id}/versions…）
    app.MapGroup("/api/testcases").MapTestCaseVersionApi().WithProjectScope(ProjectResource.TestCase).RequireAuthorization();
    app.MapGroup("/api/executions").MapExecutionApi().WithProjectScope(ProjectResource.Execution).RequireAuthorization();
    app.MapGroup("/api/ai").MapAIApi().RequireAuthorization().RequireRateLimiting("ai");
    app.MapGroup("/api/chat").MapChatApi().RequireAuthorization().RequireRateLimiting("ai");
    app.MapGroup("/api/mocks").MapMockApi().RequireAuthorization();
    app.MapGroup("/api/settings").MapSettingsApi().RequireAuthorization();
    app.MapGroup("/api/stats").MapStatsApi().RequireAuthorization();
    app.MapGroup("/api/reports").MapReportApi().WithProjectScope(ProjectResource.Execution).RequireAuthorization();
    app.MapGroup("/api/schedules").MapScheduleApi().WithProjectScope(ProjectResource.Schedule).RequireAuthorization();
    app.MapGroup("/api/datasets").MapDataSetApi().WithProjectScope(ProjectResource.DataSet).RequireAuthorization();
    app.MapGroup("/api/suites").MapSuiteApi().WithProjectScope(ProjectResource.TestSuite).RequireAuthorization();
    app.MapGroup("/api/visual").MapVisualApi().WithProjectScope(ProjectResource.VisualBaseline).RequireAuthorization();
    // 报告分享：/api/shares 管理令牌（需登录），/api/public 为免登录只读报告
    app.MapGroup("/api/shares").MapShareApi().RequireAuthorization();
    app.MapGroup("/api/public").MapPublicReportApi();
    app.MapGroup("/api/scripts").MapScriptApi().RequireAuthorization();
    // 迭代 F·P2-9：压测场景（k6）。by-id 端点按 LoadTestScenario 反查项目；
    // /runs/{runId} 走 Run 上冗余的 ProjectId
    app.MapGroup("/api/loadtests").MapLoadTestApi().WithProjectScope(ProjectResource.LoadTestScenario).RequireAuthorization();
    app.MapGroup("/api/projects/{projectId:guid}/environments").MapProjectEnvironmentsApi().WithProjectScope().RequireAuthorization();
    app.MapGroup("/api/environments").MapEnvironmentApi().WithProjectScope(ProjectResource.Environment).RequireAuthorization();
    // Webhook 独立 token 保护（X-Webhook-Token），不加 RequireAuthorization
    app.MapGroup("/api/webhooks").MapWebhookApi();
    app.MapHub<ExecutionHub>("/hubs/execution").RequireAuthorization();
    // 站内消息 Hub：按用户分组推送，与执行 Hub 的分组语义不同，刻意分开（见 NotificationHub 注释）
    app.MapHub<NotificationHub>("/hubs/notification").RequireAuthorization();
}

// 启动时自动迁移 + 种子数据（开发/测试环境；生产环境改为显式迁移）
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
    // 多实例并发启动安全（审查发现）：两个实例同时 Migrate 会竞态。
    // pg_advisory_lock 是会话级锁——先显式打开并固定连接，锁/迁移/种子共用同一连接，
    // 否则连接池把会话归还后锁就悄悄丢了。锁 key 是平台专属常量，unlock 放 finally。
    await db.Database.OpenConnectionAsync();
    const int migrationLockKey = 421603198;
    await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({migrationLockKey})");
    try
    {
        await db.Database.MigrateAsync();
        await DatabaseSeeder.SeedAsync(db);

        // D4：语义 embedding 维度对齐（默认关闭→512，与现有列一致，零影响；启用外部 embeddings 时按需 ALTER 列维度并清空不兼容历史向量）
        var embeddingOpts = scope.ServiceProvider.GetRequiredService<IOptions<EmbeddingOptions>>().Value;
        var desiredDim = embeddingOpts.Enabled ? embeddingOpts.Dimensions : ElementEmbedder.Dimensions;
        db.EnsureEmbeddingColumnDimension(desiredDim);
    }
    finally
    {
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({migrationLockKey})");
        await db.Database.CloseConnectionAsync();
    }
}
await MaintenanceService.CleanupOnStartupAsync(app.Services);

// MinIO 模式：启动时确保 bucket 存在（失败只告警不阻塞——写入时会再提示）
if (builder.Configuration["Storage:Provider"]?.Equals("minio", StringComparison.OrdinalIgnoreCase) == true)
{
    if (app.Services.GetService<MinioArtifactStore>() is { } minio)
        await minio.EnsureBucketAsync(CancellationToken.None);
}

app.Run();

public partial class Program { }
