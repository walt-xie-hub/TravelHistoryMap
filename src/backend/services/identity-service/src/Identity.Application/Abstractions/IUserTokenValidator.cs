namespace Identity.Application.Abstractions;

/// <summary>
/// 校验本服务**自己签发**的用户令牌（token exchange 的 subject_token）。
///
/// 抽象放在应用层、由 API 层用密钥集合实现，理由与 <see cref="IAccessTokenIssuer"/> 相同：
/// 交换逻辑（服务凭证校验、audience/scope 裁决、审计）应当能脱离 HTTP 与 JWT 库做单元测试。
/// </summary>
public interface IUserTokenValidator
{
    /// <summary>
    /// 有效则返回用户 id（即 token 的 sub）；无效返回 <c>null</c>。
    ///
    /// 只接受**用户令牌**：服务令牌的 sub 形如 <c>service:xxx</c>，不是数字，因此天然取不到 id
    /// —— 这是刻意的，防止拿服务令牌当主体去换出一枚“代表服务”的委托令牌。
    ///
    /// 同步签名：验签是 CPU 工作，没有 I/O 可等 —— 不要为了“看起来像网络调用”而包成异步。
    /// </summary>
    int? Validate(string token);
}
