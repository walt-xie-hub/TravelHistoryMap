using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public class ServiceClientConfiguration : IEntityTypeConfiguration<ServiceClient>
{
    public void Configure(EntityTypeBuilder<ServiceClient> builder)
    {
        builder.ToTable("ServiceClients");
        builder.HasKey(c => c.ClientId);

        builder.Property(c => c.ClientId).HasMaxLength(100);
        // secret 只存哈希（PBKDF2），与用户口令同一实现
        builder.Property(c => c.ClientSecretHash).IsRequired().HasMaxLength(256);
        builder.Property(c => c.DisplayName).IsRequired().HasMaxLength(200);
        builder.Property(c => c.IsActive).HasDefaultValue(true);
        builder.Property(c => c.CreatedAt).IsRequired();

        builder.HasMany(c => c.Scopes)
            .WithOne(s => s.Client)
            .HasForeignKey(s => s.ClientId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
