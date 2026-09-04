using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Entities;

namespace Travel.Infrastructure.Persistence.Configurations;

/// <summary>
/// TravelRecord 实体的表映射配置：与 TravelRecord 属性及数据库列一一同步。
/// </summary>
public class TravelRecordConfiguration : IEntityTypeConfiguration<TravelRecord>
{
    public void Configure(EntityTypeBuilder<TravelRecord> builder)
    {
        builder.ToTable("TravelRecords");

        builder.HasKey(t => t.Id);

        // user_id 不建导航属性（跨服务零耦合），跨服务外键由 DatabaseInitializer
        // 以幂等原生 SQL 补建（见 docs/adr/0002）。
        builder.Property(t => t.UserId)
            .IsRequired();

        builder.Property(t => t.LocationName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.Latitude)
            .IsRequired()
            .HasPrecision(9, 6);   // WGS84，-90 ~ 90

        builder.Property(t => t.Longitude)
            .IsRequired()
            .HasPrecision(9, 6);   // WGS84，-180 ~ 180

        // 时间以 UTC 时刻存储（timestamptz）
        builder.Property(t => t.ArrivedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        builder.Property(t => t.DepartedAt)
            .IsRequired(false)
            .HasColumnType("timestamptz");

        builder.Property(t => t.CreatedAt)
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .IsRequired(false);

        // 主查询：按用户取历史并按到达时间倒序，因此建 (UserId, ArrivedAt) 复合索引
        builder.HasIndex(t => new { t.UserId, t.ArrivedAt });

        // 乐观锁：使用 EF Core shadow property 显式映射到 PostgreSQL 的隐藏系统列 xmin
        // （每行所属事务 ID，PG 自动维护），无需在表里建任何列。
        // EF Core 在 UPDATE/DELETE 的 WHERE 子句中自动带上 xmin 做并发校验，
        // 冲突抛 DbUpdateConcurrencyException。
        // 为什么用 shadow property 而不是在 TravelRecord 加 uint RowVersion 属性：
        // EF Core 内置约定会把任何名为 RowVersion 的属性强制识别为 byte[]（SQL Server
        // [Timestamp] 语义），即便 CLR 声明为 uint 也无法改变，导致 HasColumnType("xid")
        // 报 "byte[] cannot be mapped to xid"。Shadow property 绕开了该约定，名字也叫 Xmin。
        builder.Property<uint>("Xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
