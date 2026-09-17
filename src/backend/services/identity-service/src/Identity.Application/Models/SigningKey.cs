namespace Identity.Application.Models;

/// <summary>
/// 一把签名密钥（RSA 私钥 + 其 kid）。私钥来源见 ADR-0023：
/// 生产从 Key Vault 读取，本地从文件读取；两者都以 <c>jwt-signing-&lt;kid&gt;</c> 命名，
/// 因此 key set 靠"列出该前缀"得到，不需要一张签名密钥表。
/// </summary>
public sealed record SigningKey(string Kid, System.Security.Cryptography.RSA Key, DateTime CreatedAt);
