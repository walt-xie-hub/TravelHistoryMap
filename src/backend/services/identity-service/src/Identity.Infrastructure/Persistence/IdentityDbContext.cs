using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence;

/// <summary>
/// identity-service 自己的表（ADR-0020）：刷新令牌、服务客户端、登录失败计数。
///
/// **刻意不映射 Users 表**：Users 归 user-service 所有，本服务只通过
/// <see cref="Security.NpgsqlCredentialReader"/> 做只读查询。
/// 若把 Users 映射成实体，EnsureCreated/CreateTables 会在本服务先启动时
/// 用它"补建"一张结构不完整的 Users 表，破坏 ADR-0020 的单写者契约。
/// </summary>
public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ServiceClient> ServiceClients => Set<ServiceClient>();

    public DbSet<ServiceClientScope> ServiceClientScopes => Set<ServiceClientScope>();

    public DbSet<LoginAttempt> LoginAttempts => Set<LoginAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
}
