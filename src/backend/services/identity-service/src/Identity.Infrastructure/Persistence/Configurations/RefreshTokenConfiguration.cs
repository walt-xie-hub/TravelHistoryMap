using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("RefreshTokens");
        builder.HasKey(t => t.Id);

        // 只存哈希，且按哈希查（唯一）。
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.ClientId).IsRequired().HasMaxLength(100);
        builder.Property(t => t.FamilyId).IsRequired();
        builder.HasIndex(t => t.FamilyId);

        builder.Property(t => t.ReplacedByHash).HasMaxLength(64);
        builder.Property(t => t.IssuedAt).IsRequired();
        builder.Property(t => t.ExpiresAt).IsRequired();
        builder.Property(t => t.RevokedAt);
        builder.Property(t => t.CredentialVersionAtIssue).IsRequired().HasDefaultValue(1);
        builder.Property(t => t.UserId).IsRequired();

        builder.Property<uint>("Xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
