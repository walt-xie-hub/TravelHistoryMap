using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace Shared.Security.Tests;

/// <summary>
/// 测试用的最小 <see cref="IConfiguration"/>：只实现索引器，因为被测代码只用它。
/// 刻意不引 in-memory provider 包：少一个依赖，也少一处版本漂移。
/// </summary>
internal sealed class StubConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public StubConfiguration(Dictionary<string, string?> values) => _values = values;

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => [];

    public IChangeToken GetReloadToken() => throw new NotSupportedException();

    public IConfigurationSection GetSection(string key) => throw new NotSupportedException();
}
