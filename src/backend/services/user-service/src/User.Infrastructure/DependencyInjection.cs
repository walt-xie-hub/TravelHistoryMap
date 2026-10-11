using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Observability;
using Shared.Security;
using User.Application.Abstractions;
using User.Domain.Abstractions;
using User.Infrastructure.Clients;
using User.Infrastructure.Persistence;
using User.Infrastructure.Repositories;
using User.Infrastructure.Security;

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

        // 密码哈希（PBKDF2，基于 ASP.NET Core Identity PasswordHasher）
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        // 安全事件审计（改密等）：与 identity-service 共用同一个端口与实现
        // （docs/security/observability-and-audit.md）
        services.AddSingleton<IAuditLog, LoggerAuditLog>();

        // 跨服务调用（ADR-0025）：以登录用户的名义读取 travel-service 的足迹概览。
        // 委托令牌客户端与 IServiceTokenClient 共用同一份 ServiceIdentity ——
        // 在 identity 眼里本服务始终是同一个 Client，只是授权模式不同（token exchange vs client credentials）。
        services.AddTravelMapDelegatedTokenClient(configuration);

        var travelService = new TravelServiceOptions();
        configuration.GetSection(TravelServiceOptions.SectionName).Bind(travelService);
        travelService.Validate();          // 缺项即拒绝启动，不做静默默认
        services.AddSingleton(Options.Create(travelService));

        // 超时（10s）大于换令牌的超时（5s）：一次请求最多两跳，且本调用在**请求路径**上，
        // 宁可快速失败把“依赖不可用”如实报到上层，也不要把用户请求一起拖住。
        services.AddHttpClient<ITravelFootprintGateway, DelegatedTravelFootprintGateway>(
            http => http.Timeout = TimeSpan.FromSeconds(10));

        return services;
    }
}
