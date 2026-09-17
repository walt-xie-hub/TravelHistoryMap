using Identity.Application.Abstractions;
using Identity.Application.Models;
using Identity.Domain.Entities;

namespace Identity.Application.Services;

/// <summary>
/// 刷新令牌的签发、轮换与撤销（ADR-0021）。
///
/// 三条不变量：
/// 1. 库里只存哈希，原文只在响应里出现一次；
/// 2. 每次刷新即轮换，旧令牌立刻失效；
/// 3. **旧令牌被再次使用 = 泄露信号 → 整族撤销**（这正是不用长期令牌换来的止损能力）。
/// </summary>
public sealed class RefreshTokenService(
    IRefreshTokenRepository refreshTokens,
    ICredentialReader credentials,
    IAccessTokenIssuer accessTokens,
    IClock clock,
    IdentityOptions options,
    IAuditLog audit)
{
    /// <summary>登录成功后开启一个新族。</summary>
    public async Task<AuthTokens> IssueNewFamilyAsync(UserCredential user, string clientId, CancellationToken ct = default)
    {
        var issued = await PersistAsync(user, clientId, familyId: null, ct);
        return ToAuthTokens(user, issued);
    }

    /// <summary>
    /// 用 refresh token 换新令牌。返回 null 表示拒绝（不区分原因，避免向外泄露状态）。
    /// </summary>
    public async Task<AuthTokens?> RotateAsync(string rawToken, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var existing = await refreshTokens.FindByHashAsync(TokenHasher.Hash(rawToken), ct);
        if (existing is null)
            return null;

        // 重用检测：已被替换过的令牌再次出现。
        if (existing.ReplacedByHash is not null)
        {
            await RevokeFamilyAsync(existing.FamilyId, ct);
            audit.RefreshReuseDetected(existing.UserId, existing.FamilyId, ip, userAgent);
            return null;
        }

        if (!existing.IsActive(clock.UtcNow))
            return null;

        // 每次刷新都重新确认账号仍然可用且凭据未被更换（ADR-0020 的 CredentialVersion 契约）。
        var user = await credentials.FindByIdAsync(existing.UserId, ct);
        if (user is null || !user.IsActive || user.CredentialVersion != existing.CredentialVersionAtIssue)
        {
            await RevokeFamilyAsync(existing.FamilyId, ct);
            return null;
        }

        var issued = await PersistAsync(user, existing.ClientId, existing.FamilyId, ct);
        existing.ReplacedByHash = issued.Token.TokenHash;
        await refreshTokens.SaveChangesAsync(ct);

        audit.TokenRefreshed(user.Id, ip, userAgent);
        return ToAuthTokens(user, issued);
    }

    /// <summary>登出：撤销该 refresh 所在的整族。</summary>
    public async Task<bool> RevokeByRawTokenAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return false;

        var existing = await refreshTokens.FindByHashAsync(TokenHasher.Hash(rawToken), ct);
        if (existing is null)
            return false;

        await RevokeFamilyAsync(existing.FamilyId, ct);
        audit.LoggedOut(existing.UserId, existing.FamilyId);
        return true;
    }

    private async Task<IssuedRefreshToken> PersistAsync(UserCredential user, string clientId, Guid? familyId, CancellationToken ct)
    {
        var raw = TokenHasher.NewToken();
        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            ClientId = clientId,
            TokenHash = TokenHasher.Hash(raw),
            FamilyId = familyId ?? Guid.NewGuid(),
            IssuedAt = clock.UtcNow,
            ExpiresAt = clock.UtcNow.AddDays(options.RefreshTokenDays),
            CredentialVersionAtIssue = user.CredentialVersion,
        };

        await refreshTokens.AddAsync(entity, ct);
        await refreshTokens.SaveChangesAsync(ct);
        return new IssuedRefreshToken(entity, raw);
    }

    private async Task RevokeFamilyAsync(Guid familyId, CancellationToken ct)
    {
        await refreshTokens.RevokeFamilyAsync(familyId, clock.UtcNow, ct);
        await refreshTokens.SaveChangesAsync(ct);
    }

    private AuthTokens ToAuthTokens(UserCredential user, IssuedRefreshToken issued) => new(
        accessTokens.CreateUserToken(user),
        issued.RawToken,
        accessTokens.AccessTokenLifetimeSeconds,
        options.RefreshTokenDays);
}
