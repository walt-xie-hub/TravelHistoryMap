using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Shared.Observability;

/// <summary>
/// 跨服务统一可观测性引导。各服务只需调用 <see cref="AddObservability"/> 即可获得一致的
/// Tracing / Metrics / Logging 遥测管线，通过 OTLP gRPC 协议统一导出。
/// Metrics 额外暴露 /metrics 端点供 Prometheus 抓取。
/// </summary>
public static class Telemetry
{
    /// <summary>用于 Tracer / Meter 命名的服务名键，例如 "user-service"。</summary>
    public const string ServiceNameSource = "ServiceName";

    /// <summary>
    /// 注册 OpenTelemetry Tracing + Metrics + Logging 管线。
    ///
    /// OTLP 端点优先从代码配置读取，其次从环境变量 OTEL_EXPORTER_OTLP_ENDPOINT 读取。
    ///
    /// 自动埋点：
    ///   - ASP.NET Core  HTTP 请求/响应 (tracing + metrics)
    ///   - Runtime        GC/CPU/内存 (metrics)
    ///   - Npgsql         数据库命令追踪 (tracing)
    ///
    /// Metrics 双导出：
    ///   - OTLP/gRPC  →  Jaeger / OpenTelemetry Collector
    ///   - /metrics   →  Prometheus 抓取 (scrape)
    /// </summary>
    /// <param name="services">DI 容器</param>
    /// <param name="serviceName">当前服务名，作为 resource 属性上报</param>
    public static IServiceCollection AddObservability(this IServiceCollection services, string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        // ---- 构建 Resource ----
        var resourceBuilder = ResourceBuilder.CreateDefault().AddService(serviceName);

        // ---- Logging：通过 OTLP 导出结构化日志 ----
        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otelLogging =>
            {
                otelLogging.SetResourceBuilder(resourceBuilder);
                otelLogging.IncludeFormattedMessage = true;
                otelLogging.IncludeScopes = true;
                otelLogging.AddOtlpExporter(ConfigureOtlp);
            });
        });

        // ---- Tracing + Metrics：通过 OpenTelemetry.Extensions.Hosting 注册 ----
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddTelemetrySdk())

            // Tracing 管线
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(asp =>
                {
                    asp.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    asp.RecordException = true;
                })
                .AddNpgsql()                              // Npgsql 数据库命令级追踪：每条 SQL 自动创建 span
                .SetSampler(new AlwaysOnSampler())
#if DEBUG
                .AddConsoleExporter()                  // DEBUG 构建输出到控制台，便于诊断
#endif
                .AddOtlpExporter(ConfigureOtlp))

            // Metrics 管线：双导出 —— OTLP + Prometheus scrape
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddRuntimeInstrumentation()
                .AddPrometheusExporter()                  // 暴露 /metrics 供 Prometheus 抓取
                .AddOtlpExporter(ConfigureOtlp));

        return services;
    }

    /// <summary>
    /// 启用可观测性中间件：映射 /metrics 端点（Prometheus scrape），
    /// 路径不受健康检查过滤影响，专供监控系统使用。
    /// 必须在 <c>app.UseRouting()</c> 之后、<c>app.MapControllers()</c> 之类端点之前调用。
    /// </summary>
    public static IApplicationBuilder UseObservability(this IApplicationBuilder app)
    {
        return app.UseOpenTelemetryPrometheusScrapingEndpoint();
    }

    /// <summary>
    /// 统一 OTLP 导出器配置：开发环境使用 Jaeger gRPC :4317。
    /// 端点可通过 <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> 环境变量覆盖。
    /// </summary>
    private static void ConfigureOtlp(OtlpExporterOptions options)
    {
        var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            endpoint = "http://localhost:4317";
        }

        options.Endpoint = new Uri(endpoint);
    }
}
