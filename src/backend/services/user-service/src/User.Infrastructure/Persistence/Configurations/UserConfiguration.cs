using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using User.Domain.Entities;

namespace User.Infrastructure.Persistence.Configurations;

/// <summary>
/// User 实体的表映射配置：与 AppUser 属性及数据库列一一同步。
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(u => u.Email)
            .IsRequired()
            .HasMaxLength(200);

        builder.HasIndex(u => u.Email)
            .IsUnique();   // 邮箱唯一

        builder.Property(u => u.PhoneNumber)
            .HasMaxLength(20);

        builder.Property(u => u.PasswordHash)
            .HasMaxLength(256);

        builder.Property(u => u.AvatarUrl)
            .HasMaxLength(500);

        builder.Property(u => u.IsActive)
            .HasDefaultValue(true);

        builder.Property(u => u.CreatedAt)
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .IsRequired(false);

        // 乐观锁：使用 EF Core shadow property 显式映射到 PostgreSQL 的隐藏系统列 xmin
        // （每行所属事务 ID，PG 自动维护），无需在表里建任何列。
        // EF Core 在 UPDATE/DELETE 的 WHERE 子句中自动带上 xmin 做并发校验，
        // 冲突抛 DbUpdateConcurrencyException。
        // 为什么用 shadow property 而不是在 AppUser 加 uint RowVersion 属性：
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
