using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public class LoginAttemptConfiguration : IEntityTypeConfiguration<LoginAttempt>
{
    public void Configure(EntityTypeBuilder<LoginAttempt> builder)
    {
        builder.ToTable("LoginAttempts");
        builder.HasKey(a => a.Key);

        builder.Property(a => a.Key).HasMaxLength(200);
        builder.Property(a => a.FailedCount).IsRequired();
        builder.Property(a => a.FirstFailedAt).IsRequired();
        builder.Property(a => a.LockedUntil);
    }
}
