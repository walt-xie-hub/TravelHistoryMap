using System.Security.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Security;

/// <summary>
/// 生产环境的签名密钥来源（ADR-0023）：Key Vault 中名为 <c>jwt-signing-&lt;kid&gt;</c> 的 secret，
/// 值为 PEM 私钥；凭托管身份读取，不落任何静态密钥。
///
/// 轮换：写入新 kid 的 secret → JWKS 自动同时暴露新旧 → 旧 secret 需保留到它签发的令牌全部过期。
/// key set 靠"列出该前缀"得到，因此**不需要签名密钥表**。
/// </summary>
public sealed class KeyVaultSigningKeyStore : ISigningKeyStore, IDisposable
{
    private const string Prefix = "jwt-signing-";
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);

    private readonly SecretClient _client;
    private readonly string _configuredActiveKid;
    private readonly ILogger<KeyVaultSigningKeyStore> _logger;
    private readonly Lock _gate = new();
    private readonly Timer _timer;

    private List<SigningKey> _keys = [];

    public KeyVaultSigningKeyStore(IOptions<IdentityOptions> options, ILogger<KeyVaultSigningKeyStore> logger)
    {
        _logger = logger;
        _configuredActiveKid = options.Value.SigningKeyStore.ActiveKid;

        var uri = options.Value.SigningKeyStore.KeyVaultUri;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var vaultUri))
            throw new InvalidOperationException("Identity:SigningKeyStore:KeyVaultUri 未配置或不是合法 URL。");

        _client = new SecretClient(vaultUri, new DefaultAzureCredential());

        // 启动期同步加载一次（仅这一次阻塞），之后由定时器在后台刷新，
        // 请求路径上永远不会做同步等待。
        RefreshAsync().GetAwaiter().GetResult();

        // fail closed（ADR-0023）：拿不到任何签名密钥就拒绝启动，
        // 而不是带着一把没有钥匙的身份服务起来、直到用户第一次登录才报 500。
        if (GetKeys().Count == 0)
            throw new InvalidOperationException(
                "Key Vault 中没有可用的 jwt-signing-* 签名密钥：拒绝启动（fail closed，ADR-0023）。");

        _timer = new Timer(_ => _ = RefreshAsync(), null, RefreshInterval, RefreshInterval);
    }

    public IReadOnlyList<SigningKey> GetKeys()
    {
        lock (_gate)
        {
            return _keys;
        }
    }

    public SigningKey GetActiveKey()
    {
        lock (_gate)
        {
            if (_keys.Count == 0)
                throw new InvalidOperationException("Key Vault 中没有可用的 jwt-signing-* 密钥。");

            if (!string.IsNullOrWhiteSpace(_configuredActiveKid))
            {
                var configured = _keys.FirstOrDefault(k => k.Kid == _configuredActiveKid);
                if (configured is not null)
                    return configured;

                _logger.LogWarning("配置的 ActiveKid {Kid} 未找到，回落到最新密钥。", _configuredActiveKid);
            }

            return _keys.OrderByDescending(k => k.CreatedAt).First();
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            var loaded = new List<SigningKey>();

            await foreach (var properties in _client.GetPropertiesOfSecretsAsync())
            {
                if (properties.Enabled != true || !properties.Name.StartsWith(Prefix, StringComparison.Ordinal))
                    continue;

                var kid = properties.Name[Prefix.Length..];
                var secret = await _client.GetSecretAsync(properties.Name);

                var rsa = RSA.Create();
                rsa.ImportFromPem(secret.Value.Value);
                loaded.Add(new SigningKey(kid, rsa, properties.CreatedOn?.UtcDateTime ?? DateTime.UtcNow));
            }

            if (loaded.Count == 0)
            {
                _logger.LogError("Key Vault 中没有 jwt-signing-* 密钥：无法签发令牌。");
                return;   // 保留上一次的快照（旧令牌仍需可验签）
            }

            lock (_gate)
            {
                foreach (var old in _keys)
                {
                    if (loaded.All(k => k.Kid != old.Kid))
                        old.Key.Dispose();
                }

                _keys = loaded;
            }
        }
        catch (Exception ex)
        {
            // Key Vault 暂时不可用：继续用现有快照（验签不受影响），但新签发会被 GetActiveKey 拦住。
            _logger.LogError(ex, "刷新 Key Vault 签名密钥失败，继续使用上一次的快照。");
        }
    }

    public void Dispose() => _timer.Dispose();
}
