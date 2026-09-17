namespace Identity.Application.Models;

/// <summary>
/// 认证成功后的令牌对。access token 是自包含 JWT（资源服务验签即可，零运行时调用），
/// refresh token 是随机不透明串（只在签发响应里出现，库里只存哈希）。
/// </summary>
public sealed record AuthTokens(
    string AccessToken,
    string RefreshToken,
    int AccessTokenExpiresInSeconds,
    int RefreshTokenExpiresInDays);
