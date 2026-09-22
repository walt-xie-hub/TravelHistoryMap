namespace Shared.Security;

public sealed class ServiceIdentityOptions
{
    public const string SectionName = "ServiceIdentity";

    /// <summary>
    /// 跟踪在仓库 appsettings 里的占位符。它出现在启动期说明真实 secret **没有被注入**：
    /// 与其拿它去请求令牌并得到一个难排查的 401，不如直接拒绝启动（fail closed）。
    /// </summary>
    public const string EnvironmentPlaceholder = "__SET_VIA_ENV__";

    public string Issuer { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    public TimeSpan RefreshBeforeExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>启动期校验：缺项与占位符一律拒绝（与 Jwt 共存窗口同一套 fail-closed 做法）。</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException($"{SectionName}:Issuer is required.");

        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException($"{SectionName}:ClientId is required.");

        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException($"{SectionName}:ClientSecret is required.");

        if (string.Equals(ClientSecret, EnvironmentPlaceholder, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{SectionName}:ClientSecret 仍是占位符 {EnvironmentPlaceholder}：请通过环境变量注入真实值。");
    }
}