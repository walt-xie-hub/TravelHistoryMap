namespace User.Infrastructure.Clients;

/// <summary>
/// 被调服务（travel-service）的地址与标识（ADR-0025）。
///
/// <see cref="Audience"/> 是"travel-service 在 identity-service 里的注册名"，也就是委托令牌的 <c>aud</c>。
/// 它必须同时与三处一致：identity 的 <c>Identity:AllowedServiceAudiences</c>、
/// travel-service 自己的 <c>ServiceIdentity:ClientId</c>、以及这里的配置 ——
/// 不一致会在换取时被判 <c>invalid_target</c>（有意为之：默认拒绝，不会静默降级）。
/// </summary>
public sealed class TravelServiceOptions
{
    public const string SectionName = "TravelService";

    /// <summary>被调服务标识（委托令牌的 aud）。</summary>
    public string Audience { get; set; } = "travel-service";

    /// <summary>被调服务基础地址，如 <c>http://travel-server:8080</c>。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>启动期校验：缺项即拒绝启动（与本仓库其它配置同一套 fail-closed 做法）。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException($"{SectionName}:Audience is required.");

        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException($"{SectionName}:BaseUrl is required.");
    }
}
