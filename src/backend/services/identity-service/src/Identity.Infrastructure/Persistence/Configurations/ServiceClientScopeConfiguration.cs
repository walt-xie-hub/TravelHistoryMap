using Identity.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public class ServiceClientScopeConfiguration : IEntityTypeConfiguration<ServiceClientScope>
{
    public void Configure(EntityTypeBuilder<ServiceClientScope> builder)
    {
        builder.ToTable("ServiceClientScopes");
        builder.HasKey(s => new { s.ClientId, s.Scope });

        builder.Property(s => s.ClientId).HasMaxLength(100);
        builder.Property(s => s.Scope).HasMaxLength(100);
    }
}
