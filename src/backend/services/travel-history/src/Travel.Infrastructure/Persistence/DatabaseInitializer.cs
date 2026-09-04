using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Travel.Infrastructure.Persistence;

/// <summary>
/// 共享 appdb 的 schema 引导（多服务共享一库，见 docs/adr/0002）。
///
/// EF Core 的 EnsureCreated 只在「数据库不存在 或 库内无任何表」时建表；
/// 一旦库内已有其他服务（如 user-service）建的表，它会整体跳过且不建任何表。
/// 因此在共享库场景下采用官方建议的组合拳：
///   1. EnsureCreated() —— 负责空库时建库 + 建本模型全部表；
///   2. 若返回 false 且本模型关键表缺失 —— 用 IRelationalDatabaseCreator.CreateTables()
///      仅补建本 DbContext 的表（不影响其他服务的表）；
///   3. 幂等补建跨服务外键（TravelRecords.UserId → Users.Id, ON DELETE CASCADE）。
/// 该方法不做迁移；表结构变更走「模型驱动」演进（见 docs/adr/0001/0002）。
/// </summary>
public static class DatabaseInitializer
{
    private const string TravelTable = "TravelRecords";
    private const string UserTable = "Users";
    private const string FkName = "fk_travel_records_user_id";

    public static void EnsureTravelSchema(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TravelDbContext>();

        // 并发启动（compose 中多个服务同时起）时可能存在短暂竞态，做几次重试兜底。
        var lastError = (Exception?)null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!db.Database.EnsureCreated())
                {
                    var creator = db.GetService<IRelationalDatabaseCreator>();
                    if (!TableExists(db, TravelTable))
                        creator.CreateTables();   // 只建本模型缺失的表
                }

                EnsureTravelUserForeignKey(db);
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        throw new InvalidOperationException("Failed to initialize travel schema.", lastError);
    }

    /// <summary>
    /// 幂等补建跨服务外键。user-service 的 Users 表可能尚未建好（服务启动顺序不定），
    /// 遇到 undefined_table(42P01) 短时重试；其他错误直接放弃，下次启动重试。
    /// </summary>
    private static void EnsureTravelUserForeignKey(TravelDbContext db)
    {
        const string ddl = $"""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = '{FkName}') THEN
                    ALTER TABLE "{TravelTable}"
                        ADD CONSTRAINT "{FkName}"
                        FOREIGN KEY ("UserId") REFERENCES "{UserTable}"("Id") ON DELETE CASCADE;
                END IF;
            END $$;
            """;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                db.Database.ExecuteSqlRaw(ddl);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01") // undefined_table：Users 尚未建表
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
            catch (PostgresException)
            {
                return; // 其他错误（如无权限）：放弃，下次启动重试
            }
        }
    }

    private static bool TableExists(TravelDbContext db, string table)
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
