using Identity.Application;
using Identity.Application.Abstractions;
using Identity.Application.Services;
using Identity.Infrastructure.Persistence;
using Identity.Infrastructure.Repositories;
using Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Shared.Observability;

namespace Identity.Infrastructure;

/// <summary>基础设施层依赖注入扩展（组合根的一部分）。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionString 'DefaultConnection' not found.");

        services.AddDbContextPool<IdentityDbContext>(opt =>
            opt.UseNpgsql(connectionString, npgsql => npgsql.UseNodaTime()));

        // Users 表走参数化 SQL 直读（ADR-0020），共用同一个连接池
        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddSingleton<ICredentialReader, NpgsqlCredentialReader>();
        services.AddSingleton<PasswordVerifier>();
        services.AddSingleton<IPasswordVerifier>(sp => sp.GetRequiredService<PasswordVerifier>());
        services.AddSingleton<IServiceSecretHasher>(sp => sp.GetRequiredService<PasswordVerifier>());
        services.AddSingleton<IClock, SystemClock>();

        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IServiceClientRepository, ServiceClientRepository>();
        services.AddScoped<ILoginAttemptRepository, LoginAttemptRepository>();
        services.AddSingleton<IAuditLog, LoggerAuditLog>();

        // 配置绑定：整个服务只在这里读一次 Identity:* 配置
        var options = new IdentityOptions();
        configuration.GetSection(IdentityOptions.SectionName).Bind(options);
        services.AddSingleton(options);
        services.AddSingleton(Options.Create(options));

        // 签名密钥来源：生产 Key Vault、本地文件（ADR-0023）
        if (string.Equals(options.SigningKeyStore.Provider, "KeyVault", StringComparison.OrdinalIgnoreCase))
            services.AddSingleton<ISigningKeyStore, KeyVaultSigningKeyStore>();
        else
            services.AddSingleton<ISigningKeyStore, DevFileSigningKeyStore>();

        services.AddScoped<RefreshTokenService>();
        services.AddScoped<AuthenticationService>();
        services.AddScoped<ClientCredentialsService>();
        services.AddScoped<TokenExchangeService>();
        services.AddScoped<ServiceClientRegistrationService>();

        return services;
    }
}
