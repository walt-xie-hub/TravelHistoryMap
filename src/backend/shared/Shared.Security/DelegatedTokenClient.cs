using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Shared.Security;

/// <summary>
/// 换取**用户委托令牌**的发起侧（ADR-0024）：把「调用方自己的服务凭证 + 用户的 access token」
/// 交给 identity-service，换回一枚 <c>sub</c>=用户、<c>act</c>=<c>service:&lt;clientId&gt;</c>、
/// <c>aud</c>=被调服务的短 TTL 令牌。
///
/// **为什么不能复用 <see cref="IServiceTokenClient"/>**：它的缓存键是 <c>(audience, scope)</c>，
/// **不含 subject**。委托令牌是按主体签发的，用那个键缓存就等于把甲用户换到的令牌发给乙用户
/// （ADR-0024 的 Consequences 已点名这个坑）。
///
/// 本实现**故意不做缓存**：委托令牌 TTL 只有 5 分钟，缓存收益本就有限；而"每次调用都真换一次"
/// 让验收更接近真实链路 —— 缓存漏键是这个流程里最危险的错误，不值得为首版省那一次往返。
/// </summary>
public interface IDelegatedTokenClient
{
    /// <summary>
    /// 以 <paramref name="subjectToken"/> 的主体身份，换一枚面向 <paramref name="audience"/> 的委托令牌。
    ///
    /// <paramref name="scope"/> **必填**且必须已授予给本服务的 Client：委托少了 scope 就退化成
    /// "用户令牌的副本"，identity-service 会直接判 <c>invalid_scope</c>（ADR-0024 的裁决 ④）。
    /// </summary>
    Task<string> ExchangeAsync(
        string subjectToken,
        string audience,
        string scope,
        CancellationToken cancellationToken = default);
}

public static class DelegatedTokenClientExtensions
{
    /// <summary>
    /// 注册委托令牌客户端。与 <c>AddTravelMapServiceTokenClient</c> 读同一份 <see cref="ServiceIdentityOptions"/>：
    /// 在 identity-service 眼里调用方始终是同一个 Client，只是授权模式不同（client credentials vs token exchange）。
    /// </summary>
    public static IServiceCollection AddTravelMapDelegatedTokenClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = new ServiceIdentityOptions();
        configuration.GetSection(ServiceIdentityOptions.SectionName).Bind(options);
        options.Validate();

        // TryAdd：调用方可能已经注册过同一份选项（同时使用 IServiceTokenClient 时），值相同，不重复注册
        services.TryAddSingleton(Options.Create(options));

        // 短超时：这条调用在**请求路径**上（用户点开资料页就要用）。identity 挂住时宁可快速失败，
        // 把"依赖不可用"如实报给上游，也不要把上游请求一起拖到默认的 100 秒。
        services.AddHttpClient<IDelegatedTokenClient, DelegatedTokenClient>(
            http => http.Timeout = TimeSpan.FromSeconds(5));

        return services;
    }
}

/// <summary>
/// identity-service 不可达或自身故障（网络/超时/5xx/空响应）。
/// 调用方应把它翻译成"我依赖的下游暂时不可用"，而不是"请求非法"。
/// </summary>
public sealed class DelegatedTokenUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// identity-service **明确拒绝**了这次换取（<c>invalid_client</c> / <c>invalid_target</c> /
/// <c>invalid_grant</c> / <c>invalid_scope</c>）。
///
/// 这些几乎总是**配置或契约错误**（scope 没授予、client 没注册、audience 没登记），
/// 因此有意与"暂时不可用"分开：吞掉它会把一个必须在部署时修好的问题伪装成偶发抖动。
/// </summary>
public sealed class DelegatedTokenDeniedException(string error)
    : Exception($"Token exchange was rejected by the identity service ({error}).")
{
    public string Error { get; } = error;
}

public sealed class DelegatedTokenClient(
    HttpClient http,
    IOptions<ServiceIdentityOptions> configured) : IDelegatedTokenClient
{
    /// <summary>RFC 8693 的 token exchange grant type。</summary>
    private const string TokenExchangeGrantType = "urn:ietf:params:oauth:grant-type:token-exchange";

    /// <summary>只接受用户 access token 作为主体令牌。</summary>
    private const string AccessTokenType = "urn:ietf:params:oauth:token-type:access_token";

    private readonly ServiceIdentityOptions options = configured.Value;

    public async Task<string> ExchangeAsync(
        string subjectToken,
        string audience,
        string scope,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(subjectToken))
            throw new ArgumentException("Subject token is required.", nameof(subjectToken));

        if (string.IsNullOrWhiteSpace(audience))
            throw new ArgumentException("Audience is required.", nameof(audience));

        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException(
                "Scope is required: a delegated token without scope would be a copy of the user token.",
                nameof(scope));

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsync(
                $"{options.Issuer.TrimEnd('/')}/token",
                new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("grant_type", TokenExchangeGrantType),
                    new KeyValuePair<string, string>("client_id", options.ClientId),
                    new KeyValuePair<string, string>("client_secret", options.ClientSecret),
                    new KeyValuePair<string, string>("subject_token", subjectToken),
                    new KeyValuePair<string, string>("subject_token_type", AccessTokenType),
                    new KeyValuePair<string, string>("audience", audience),
                    new KeyValuePair<string, string>("scope", scope),
                ]),
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new DelegatedTokenUnavailableException(
                "The identity service could not be reached to exchange a delegated token.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                // 5xx = 对面坏了（可重试/可降级）；4xx = 我们请求得不对（配置错误，必须显式暴露）
                if ((int)response.StatusCode >= 500)
                {
                    throw new DelegatedTokenUnavailableException(
                        $"The identity service failed to exchange a delegated token (HTTP {(int)response.StatusCode}).");
                }

                throw new DelegatedTokenDeniedException(await ReadErrorAsync(response, cancellationToken));
            }

            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken: cancellationToken);

            if (token is null || string.IsNullOrWhiteSpace(token.AccessToken))
            {
                throw new DelegatedTokenUnavailableException(
                    "The identity service returned an empty delegated token response.");
            }

            return token.AccessToken;
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var body = await response.Content.ReadFromJsonAsync<OAuthError>(cancellationToken: ct);
            return string.IsNullOrWhiteSpace(body?.Error) ? "unknown_error" : body.Error;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // 非 JSON 错误体（网关 HTML、空体…）不该盖掉"这次换取失败了"这个事实
            return "unknown_error";
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken);

    private sealed record OAuthError(
        [property: JsonPropertyName("error")] string? Error);
}
