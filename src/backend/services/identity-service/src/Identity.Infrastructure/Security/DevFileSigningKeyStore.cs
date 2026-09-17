using System.Security.Cryptography;
using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Security;

/// <summary>
/// 本地/开发环境的签名密钥来源（ADR-0023）：目录下的 <c>jwt-signing-&lt;kid&gt;.pem</c>。
///
/// 目录为空时自动生成一把 RSA 2048，因此本地开箱即用；往里丢第二个文件即完成一次轮换，
/// 无需重启（缓存 1 分钟后自动重扫）——这正是 ADR-0023 要求的"热轮换"。
/// 生产用 <see cref="KeyVaultSigningKeyStore"/>，两者共用同一套命名与选主规则。
/// </summary>
public sealed class DevFileSigningKeyStore : ISigningKeyStore
{
    private const string Prefix = "jwt-signing-";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(1);

    private readonly string _directory;
    private readonly string _configuredActiveKid;
    private readonly ILogger<DevFileSigningKeyStore> _logger;
    private readonly Lock _gate = new();

    private List<SigningKey> _keys = [];
    private DateTime _loadedAt = DateTime.MinValue;

    public DevFileSigningKeyStore(IOptions<IdentityOptions> options, ILogger<DevFileSigningKeyStore> logger)
    {
        _directory = Path.GetFullPath(options.Value.SigningKeyStore.Path);
        _configuredActiveKid = options.Value.SigningKeyStore.ActiveKid;
        _logger = logger;

        Directory.CreateDirectory(_directory);
        Reload(generateIfEmpty: true);
    }

    public IReadOnlyList<SigningKey> GetKeys()
    {
        EnsureFresh();
        return _keys;
    }

    public SigningKey GetActiveKey()
    {
        EnsureFresh();

        if (!string.IsNullOrWhiteSpace(_configuredActiveKid))
        {
            var configured = _keys.FirstOrDefault(k => k.Kid == _configuredActiveKid);
            if (configured is not null)
                return configured;

            _logger.LogWarning("配置的 ActiveKid {Kid} 未找到，回落到最新密钥。", _configuredActiveKid);
        }

        return _keys.OrderByDescending(k => k.CreatedAt).First();
    }

    private void EnsureFresh()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow - _loadedAt > CacheTtl)
                Reload(generateIfEmpty: false);
        }
    }

    private void Reload(bool generateIfEmpty)
    {
        lock (_gate)
        {
            var files = Directory.Exists(_directory)
                ? Directory.GetFiles(_directory, $"{Prefix}*.pem")
                : [];

            if (files.Length == 0 && generateIfEmpty)
            {
                files = [GenerateKeyFile()];
            }

            var loaded = new List<SigningKey>(files.Length);
            foreach (var file in files)
            {
                var kid = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];
                try
                {
                    var rsa = RSA.Create();
                    rsa.ImportFromPem(File.ReadAllText(file));
                    loaded.Add(new SigningKey(kid, rsa, File.GetLastWriteTimeUtc(file)));
                }
                catch (Exception ex)
                {
                    // 单个文件坏掉不该让整个服务起不来；跳过并留下痕迹。
                    _logger.LogError(ex, "签名密钥文件 {File} 无法解析，已跳过。", file);
                }
            }

            if (loaded.Count == 0)
                throw new InvalidOperationException($"目录 {_directory} 下没有可用的 jwt-signing-*.pem 签名密钥。");

            ReplaceKeys(loaded);
            _loadedAt = DateTime.UtcNow;
        }
    }

    private void ReplaceKeys(List<SigningKey> loaded)
    {
        foreach (var old in _keys)
        {
            if (loaded.All(k => k.Kid != old.Kid))
                old.Key.Dispose();   // 已被移除的密钥：释放（它签发的令牌此刻应已全部过期）
        }

        _keys = loaded;
    }

    private string GenerateKeyFile()
    {
        using var rsa = RSA.Create(2048);
        var kid = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        var path = Path.Combine(_directory, $"{Prefix}{kid}.pem");
        File.WriteAllText(path, rsa.ExportPkcs8PrivateKeyPem());
        _logger.LogInformation("已生成本地签名密钥 {Kid}（{Path}）。", kid, path);
        return path;
    }
}
