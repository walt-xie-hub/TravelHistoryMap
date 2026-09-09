using Microsoft.EntityFrameworkCore;
using Travel.Domain.Entities;

namespace Travel.Infrastructure.Persistence;

/// <summary>
/// EF Core 数据库上下文（基础设施层）。
/// </summary>
public class TravelDbContext : DbContext
{
    public TravelDbContext(DbContextOptions<TravelDbContext> options) : base(options) { }

    public DbSet<TravelRecord> TravelRecords => Set<TravelRecord>();
    public DbSet<TravelImage> TravelImages => Set<TravelImage>();
    public DbSet<TravelShareSnapshot> TravelShareSnapshots => Set<TravelShareSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 从当前程序集加载所有 IEntityTypeConfiguration<>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TravelDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
