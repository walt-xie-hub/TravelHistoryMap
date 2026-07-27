namespace Shared.Observability;

/// <summary>
/// 跨服务统一可观测性引导。集中注册 OpenTelemetry 的 Tracing / Metrics，
/// 各服务只需调用 <see cref="ConfigureSharedTelemetry"/> 即可获得一致的遥测管线。
///
/// 当前为无外部依赖的骨架：接入真实遥测时，引入
/// <c>OpenTelemetry</c> / <c>OpenTelemetry.Exporter.OpenTelemetryProtocol</c> 等包，
/// 并在方法体内补全 TracerProvider / MeterProvider 的配置（OtlpExporter、采样率、资源属性等）。
/// </summary>
public static class Telemetry
{
    /// <summary>用于 Tracer / Meter 命名的服务名键，例如 "user-service"。</summary>
    public const string ServiceNameSource = "ServiceName";

    /// <summary>
    /// 占位引导方法：真实实现应在此配置 OpenTelemetry 导出管线。
    /// </summary>
    /// <param name="serviceName">当前服务名，作为资源属性上报。</param>
    public static void ConfigureSharedTelemetry(string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        // TODO: 接入 OpenTelemetry .NET SDK 后在此注册 TracerProvider / MeterProvider。
    }
}
