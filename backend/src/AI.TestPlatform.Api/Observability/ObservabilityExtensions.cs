using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;

namespace AI.TestPlatform.Api.Observability;

/// <summary>
/// 迭代 E·③：可观测性接线（Serilog 结构化日志 + OpenTelemetry trace/metrics）。
///
/// 设计取舍：
/// - **导出是 opt-in**：只有配置了 <c>Observability:OtlpEndpoint</c> 才挂 OTLP 导出器；
///   不配置时只做**进程内采集**（trace 仍在 Activity 层面可关联、日志仍是结构化），
///   不依赖任何 collector —— 本地/测试环境零负担，不留"启动即失败"的隐患。
/// - 日志用 Serilog 覆盖默认控制台：带 machine/environment 标签，便于多节点区分
///   （与项目已有的"多实例共享队列"部署形态配套）。
/// - trace 采集 ASP.NET Core 入站 + HttpClient 出站 + 运行时指标；自定义源
///   <see cref="PlatformTelemetry.Source"/> 用于执行链路的显式 span。
/// </summary>
public static class ObservabilityExtensions
{
    public static WebApplicationBuilder AddPlatformObservability(this WebApplicationBuilder builder)
    {
        var cfg = builder.Configuration;
        var serviceName = cfg["Observability:ServiceName"] ?? PlatformTelemetry.ServiceName;
        var serviceVersion = cfg["Observability:ServiceVersion"] ?? "1.0";
        var otlpEndpoint = cfg["Observability:OtlpEndpoint"];
        var hasOtlp = !string.IsNullOrWhiteSpace(otlpEndpoint);

        // ---- 结构化日志（Serilog）----
        builder.Host.UseSerilog((context, _, loggerConfig) => loggerConfig
            .ReadFrom.Configuration(context.Configuration)
            .Enrich.WithMachineName()
            // 环境名用宿主环境注入（Serilog.Enrichers.Environment 2.1.3 无 WithEnvironmentName）
            .Enrich.WithProperty("Environment", context.HostingEnvironment.EnvironmentName)
            .Enrich.FromLogContext()
            .WriteTo.Console(outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

        // ---- OpenTelemetry（trace + metrics）----
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing.AddSource(PlatformTelemetry.Source.Name)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation();
                if (hasOtlp)
                    tracing.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!));
            })
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();
                if (hasOtlp)
                    metrics.AddOtlpExporter(o => o.Endpoint = new Uri(otlpEndpoint!));
            });

        return builder;
    }
}
