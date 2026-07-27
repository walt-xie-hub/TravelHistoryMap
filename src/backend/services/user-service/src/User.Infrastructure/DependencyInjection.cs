using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using User.Domain.Abstractions;
using User.Infrastructure.Persistence;
using User.Infrastructure.Repositories;

namespace User.Infrastructure;

/// <summary>
/// 基础设施层依赖注入扩展（组合根的一部分）。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionString 'DefaultConnection' not found.");

        services.AddDbContextPool<AppDbContext>(opt =>
            opt.UseNpgsql(connectionString, npgsql =>
            {
                // 不强制指定版本，由 Npgsql 在首次连接时自动探测（兼容 PG 16/17 等）。
                npgsql.UseNodaTime();               // 如需 NodaTime 类型
            }));

        // 仓储实现注册到领域抽象
        services.AddScoped<IUserRepository, UserRepository>();

        return services;
    }
}
