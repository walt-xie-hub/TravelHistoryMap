using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// 共享 appdb 的 schema 引导（多服务共享一库，见 docs/adr/0002）。
/// 与 user-service / travel-history 同一套组合拳：EnsureCreated + 表缺失时 CreateTables 兜底。
/// 本服务只会建自己的四张表，绝不建/改 Users。
/// </summary>
public static class DatabaseInitializer
{
    private static readonly string[] OwnTables =
        ["RefreshTokens", "ServiceClients", "ServiceClientScopes", "LoginAttempts"];

    public static void EnsureIdentitySchema(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        var lastError = (Exception?)null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!db.Database.EnsureCreated())
                {
                    var creator = db.GetService<IRelationalDatabaseCreator>();
                    if (OwnTables.Any(t => !TableExists(db, t)))
                        creator.CreateTables();
                }

                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        throw new InvalidOperationException("Failed to initialize identity schema.", lastError);
    }

    private static bool TableExists(IdentityDbContext db, string table)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            connection.Open();

        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT to_regclass('public.\"{table}\"') IS NOT NULL";
            return (bool)(command.ExecuteScalar() ?? false);
        }
        finally
        {
            if (shouldClose)
                connection.Close();
        }
    }
}
