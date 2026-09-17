using System.Security.Cryptography;

namespace Identity.Application;

/// <summary>
/// 刷新令牌的生成与哈希。原文用密码学随机数生成、**只在响应中出现一次**；
/// 库里只存 SHA-256 十六进制串，因此即使库被读走也无法直接使用。
/// </summary>
public static class TokenHasher
{
    private const int TokenBytes = 32;

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    public static string Hash(string rawToken) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
}
