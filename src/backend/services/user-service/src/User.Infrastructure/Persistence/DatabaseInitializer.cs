using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace User.Infrastructure.Persistence;

/// <summary>
/// 共享 appdb 的 schema 引导（多服务共享一库，见 docs/adr/0002）。
///
/// EF Core 的 EnsureCreated 只在「数据库不存在 或 库内无任何表」时建表；
/// 一旦库内已有其他服务（如 travel-history）建的表，它会整体跳过且不建任何表。
/// 因此在共享库场景下采用官方建议的组合拳：
///   1. EnsureCreated() —— 负责空库时建库 + 建本模型全部表；
///   2. 若返回 false 且 Users 表缺失 —— 用 IRelationalDatabaseCreator.CreateTables()
///      仅补建本 DbContext 的表（不影响其他服务的表）。
/// 跨服务外键（TravelRecords.UserId → Users.Id）由 travel-history 侧幂等补建，
/// 本服务只需保证 Users 表存在即可。
/// 该方法不做迁移；表结构变更走「模型驱动」演进（见 docs/adr/0001/0002）。
/// </summary>
public static class DatabaseInitializer
{
    private const string UserTable = "Users";

    public static void EnsureUserSchema(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 并发启动（compose 中多个服务同时起）时可能存在短暂竞态，做几次重试兜底。
        var lastError = (Exception?)null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!db.Database.EnsureCreated())
                {
                    var creator = db.GetService<IRelationalDatabaseCreator>();
                    if (!TableExists(db, UserTable))
                        creator.CreateTables();   // 只建本模型缺失的表
                }

                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        throw new InvalidOperationException("Failed to initialize user schema.", lastError);
    }

    private static bool TableExists(AppDbContext db, string table)
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
