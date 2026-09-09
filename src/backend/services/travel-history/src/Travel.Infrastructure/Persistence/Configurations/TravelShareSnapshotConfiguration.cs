using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Entities;

namespace Travel.Infrastructure.Persistence.Configurations;

public class TravelShareSnapshotConfiguration : IEntityTypeConfiguration<TravelShareSnapshot>
{
    public void Configure(EntityTypeBuilder<TravelShareSnapshot> builder)
    {
        builder.ToTable("TravelShareSnapshots");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Token)
            .IsRequired()
            .HasMaxLength(64);
        builder.HasIndex(s => s.Token).IsUnique();

        builder.Property(s => s.UserId).IsRequired();
        builder.Property(s => s.Title).HasMaxLength(120);
        builder.Property(s => s.RowsJson).IsRequired().HasColumnType("text");
        builder.Property(s => s.RecordCount).IsRequired();
        builder.Property(s => s.CreatedAt).IsRequired().HasColumnType("timestamptz");
    }
}
