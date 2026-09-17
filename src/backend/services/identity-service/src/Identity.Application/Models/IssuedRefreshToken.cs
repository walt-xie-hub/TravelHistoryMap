using Identity.Domain.Entities;

namespace Identity.Application.Models;

/// <summary>
/// 新签发的 refresh token：实体（入库，只含哈希）与原文（只此一次返回给客户端，绝不落库）。
/// </summary>
public sealed record IssuedRefreshToken(RefreshToken Token, string RawToken);
