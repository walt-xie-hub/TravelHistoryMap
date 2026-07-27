using Microsoft.EntityFrameworkCore;
using User.Domain.Entities;

namespace User.Infrastructure.Persistence;

/// <summary>
/// EF Core 数据库上下文（基础设施层）。
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 从当前程序集加载所有 IEntityTypeConfiguration<>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
