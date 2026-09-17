using System.Globalization;
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
    /// OTLP 端点取环境变量 OTEL_EXPORTER_OTLP_ENDPOINT；**未配置时不启用 OTLP 导出**。
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

        // OTLP 只在显式配置了端点时才启用（理由见 ResolveOtlpEndpoint 的注释）
        var otlpEndpoint = ResolveOtlpEndpoint();

        // ---- Logging：通过 OTLP 导出结构化日志 ----
        services.AddLogging(logging =>
        {
            logging.AddOpenTelemetry(otelLogging =>
            {
                otelLogging.SetResourceBuilder(resourceBuilder);
                otelLogging.IncludeFormattedMessage = true;
                otelLogging.IncludeScopes = true;
#if DEBUG
                otelLogging.AddConsoleExporter();           // DEBUG 时控制台可见日志
#endif
                if (otlpEndpoint is not null)
                {
                    otelLogging.AddOtlpExporter(o => o.Endpoint = otlpEndpoint);
                }
            });
        });

        // ---- Tracing + Metrics：通过 OpenTelemetry.Extensions.Hosting 注册 ----
        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName)
                .AddTelemetrySdk())

            // Tracing 管线
            .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(asp =>
                {
                    asp.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");
                    asp.RecordException = true;
                })
                .AddNpgsql();                             // Npgsql 数据库命令级追踪：每条 SQL 自动创建 span
                tracing.SetSampler(ResolveSampler());
#if DEBUG
                tracing.AddConsoleExporter();             // DEBUG 构建输出到控制台，便于诊断
#endif
                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = otlpEndpoint);
                }
            })

            // Metrics 管线：双导出 —— OTLP + Prometheus scrape
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                       .AddRuntimeInstrumentation()
                       .AddPrometheusExporter();          // 暴露 /metrics 供 Prometheus 抓取
                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = otlpEndpoint);
                }
            });

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
    /// 解析 OTLP 端点。**未显式配置时不导出**。
    ///
    /// 旧实现会回落到 http://localhost:4317：在没有 collector 的环境里（例如 ACA 上
    /// 未启用托管 agent 时）表现为持续导出失败与日志噪音——看起来像"配了遥测"，
    /// 实际什么都到不了。现在的语义是：要么明确给了端点，要么不启用 OTLP
    /// （本地仍保留 /metrics 与 DEBUG 控制台导出）。
    /// </summary>
    private static Uri? ResolveOtlpEndpoint()
    {
        var endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT");
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>
    /// 采样策略：默认全采（保持本地开发的既有行为），可用
    /// <c>OTEL_TRACE_SAMPLING_RATIO</c>（0..1）改为按比例采样。
    ///
    /// 生产必须设成小比例：原来的 AlwaysOnSampler 是 100% 采样，会把免费额度很快烧完
    /// （见 docs/security/observability-and-audit.md）。用 ParentBased 是为了尊重上游
    /// 的采样决定，避免同一条 trace 在服务之间断开。
    /// 注意：这里做不到"错误 100% 采集"——那需要 tail sampling，不在应用侧解决。
    /// </summary>
    private static Sampler ResolveSampler()
    {
        var raw = Environment.GetEnvironmentVariable("OTEL_TRACE_SAMPLING_RATIO");
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            && ratio >= 0d && ratio <= 1d)
        {
            return new ParentBasedSampler(new TraceIdRatioBasedSampler(ratio));
        }

        return new ParentBasedSampler(new AlwaysOnSampler());
    }
}
