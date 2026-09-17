using Identity.Application.Abstractions;
using Identity.Application.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Identity.Infrastructure.Security;

/// <summary>
/// Users 表的只读访问（ADR-0020）。用参数化 SQL 直接投影最小列，刻意**不经 EF**：
/// 既避免误建表/模型漂移，也让"只读这 5 列"这件事在代码里一目了然。
///
/// 唯一的写者始终是 user-service。本类没有任何写语句。
/// </summary>
public sealed class NpgsqlCredentialReader(
    NpgsqlDataSource dataSource,
    ILogger<NpgsqlCredentialReader> logger) : ICredentialReader
{
    private const string SelectColumns =
        "\"Id\", \"Email\", \"PasswordHash\", \"IsActive\", \"CredentialVersion\"";

    private const string SchemaProbe = """
        SELECT count(*) FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'Users'
          AND column_name IN ('Id', 'Email', 'PasswordHash', 'IsActive', 'CredentialVersion')
        """;

    public Task<UserCredential?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => QueryAsync(
            $"SELECT {SelectColumns} FROM \"Users\" WHERE lower(\"Email\") = @email LIMIT 1",
            "email",
            normalizedEmail,
            cancellationToken);

    public Task<UserCredential?> FindByIdAsync(int userId, CancellationToken cancellationToken = default)
        => QueryAsync(
            $"SELECT {SelectColumns} FROM \"Users\" WHERE \"Id\" = @id LIMIT 1",
            "id",
            userId,
            cancellationToken);

    /// <summary>
    /// 等待 Users 表与 CredentialVersion 列就绪。表由 user-service 建、列由 user-service 加，
    /// 而 identity-service 可能先被调度起来（ACA min-replicas 0），因此这里必须等待而不是失败退出。
    /// 超时仍不就绪时**拒绝启动**：宁可起不来，也不要在一个凭据契约不完整的库上提供认证。
    /// </summary>
    public async Task WaitUntilAvailableAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            if (await SchemaReadyAsync(cancellationToken))
                return;

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new InvalidOperationException(
            "Users 表或 CredentialVersion 列未就绪：请先启动（或升级）user-service —— 见 docs/adr/0020 的凭据列契约。");
    }

    private async Task<bool> SchemaReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var command = dataSource.CreateCommand(SchemaProbe);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt64(value ?? 0L) == 5;
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "等待 Users 表就绪…");
            return false;
        }
    }

    private async Task<UserCredential?> QueryAsync(
        string sql,
        string parameterName,
        object value,
        CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue(parameterName, value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return new UserCredential(
            reader.GetInt32(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.GetBoolean(3),
            reader.GetInt32(4));
    }
}
