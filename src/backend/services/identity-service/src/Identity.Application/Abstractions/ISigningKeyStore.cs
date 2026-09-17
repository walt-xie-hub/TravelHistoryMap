using Identity.Application.Models;

namespace Identity.Application.Abstractions;

/// <summary>
/// 签名密钥集合（ADR-0023）。必须支持**多把密钥并存**：
/// 轮换时 JWKS 要同时暴露新旧公钥，直到旧密钥签发的令牌全部过期。
/// </summary>
public interface ISigningKeyStore
{
    /// <summary>JWKS 要公布的公钥集合（含已轮换但仍需可验签的旧密钥）。</summary>
    IReadOnlyList<SigningKey> GetKeys();

    /// <summary>当前用于签发的密钥。</summary>
    SigningKey GetActiveKey();
}
