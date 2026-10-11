using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shared.Contracts;
using Shared.Security;
using User.Application.Abstractions;
using User.Domain.Common;

namespace User.Infrastructure.Clients;

/// <summary>
/// <see cref="ITravelFootprintGateway"/> 的适配器（ADR-0025）：**首个真实跨服务调用**。
///
/// 调用链：<c>浏览器 → user-service →（identity 换委托令牌）→ travel-service</c>。
/// 用户令牌只到 identity 为止，流向 travel-service 的是**重新签发的**收窄令牌
/// （<c>sub</c>=用户、<c>act</c>=service:user-service、<c>aud</c>=travel-service）——
/// 既不转发用户令牌，也不用不可验证的自定义头（ADR-0024 的两条禁令）。
///
/// 三步各自的失败语义不同，这是本类最需要说清的事：
/// <list type="number">
/// <item>换取委托令牌：5xx/网络 → 依赖不可用（503）；4xx → 配置错误（500，响亮暴露）；</item>
/// <item>请求内部端点：5xx/网络/超时 → 依赖不可用（503）；401/403 → 配置或契约错误（500）；</item>
/// <item>反序列化：失败即契约破裂（500）—— 绝不能静默回一个"0 条记录"的假摘要。</item>
/// </list>
/// </summary>
public sealed class DelegatedTravelFootprintGateway(
    IDelegatedTokenClient delegatedTokens,
    HttpClient http,
    IOptions<TravelServiceOptions> options) : ITravelFootprintGateway
{
    /// <summary>
    /// 内部端点路径（ADR-0019/0022）：内部能力一律在 <c>/internal/*</c>，不得落在
    /// 网关全量转发的 <c>/api/*</c> 下。
    /// </summary>
    internal const string FootprintPath = "internal/travels/footprint";

    public async Task<FootprintOverview> GetAsync(
        string subjectToken,
        CancellationToken cancellationToken = default)
    {
        // ① 用「本服务凭证 + 用户令牌」换一枚面向 travel-service 的委托令牌，并要求 travel:read。
        //    换不到分两种：对面坏了（可降级）vs 我们请求得不对（配置错误，必须暴露）。
        string accessToken;
        try
        {
            accessToken = await delegatedTokens.ExchangeAsync(
                subjectToken,
                options.Value.Audience,
                ServiceScopes.TravelRead,
                cancellationToken);
        }
        catch (DelegatedTokenUnavailableException ex)
        {
            throw new FootprintUnavailableException("暂时无法读取足迹概览：身份服务不可用。", ex);
        }

        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri(new Uri(options.Value.BaseUrl.TrimEnd('/') + "/"), FootprintPath));

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            // 注意请求里**没有**任何 userId：归属由令牌的 sub 表达，服务端也不会去读它
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            throw new FootprintUnavailableException("暂时无法读取足迹概览：travel-service 不可达。", ex);
        }

        using (response)
        {
            if ((int)response.StatusCode >= 500)
            {
                throw new FootprintUnavailableException(
                    $"暂时无法读取足迹概览：travel-service 返回 HTTP {(int)response.StatusCode}。");
            }

            if (!response.IsSuccessStatusCode)
            {
                // 401/403 说明委托令牌没被接受：多半是 scope 没授予或 audience 没登记。
                // 这是**部署时必须修好**的错，伪装成"暂时不可用"会让人去重启服务而不是去改注册。
                throw new InvalidOperationException(
                    $"travel-service 拒绝了足迹概览调用（HTTP {(int)response.StatusCode}）："
                    + $"请确认 user-service 这个 Client 已被授予 {ServiceScopes.TravelRead}，"
                    + $"且 {options.Value.Audience} 在 Identity:AllowedServiceAudiences 中。");
            }

            FootprintOverview? overview;
            try
            {
                overview = await response.Content.ReadFromJsonAsync<FootprintOverview>(
                    cancellationToken: cancellationToken);
            }
            catch (JsonException ex)
            {
                // 空体或非 JSON：同样属于契约破裂，不能让它以 JsonException 的形式糊弄上层
                throw new InvalidOperationException(
                    "travel-service 返回的足迹概览不是合法 JSON（契约破裂）。", ex);
            }

            return overview ?? throw new InvalidOperationException(
                "travel-service 返回了空的足迹概览响应（契约破裂）。");
        }
    }
}
