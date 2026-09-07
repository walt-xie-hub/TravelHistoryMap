using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Travel.Domain.Entities;

namespace Travel.Infrastructure.Persistence.Configurations;

public class TravelImageConfiguration : IEntityTypeConfiguration<TravelImage>
{
    public void Configure(EntityTypeBuilder<TravelImage> builder)
    {
        builder.ToTable("TravelImages");
        builder.HasKey(image => image.Id);
        builder.Property(image => image.OriginalFileName).IsRequired().HasMaxLength(255);
        builder.Property(image => image.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(image => image.OriginalPath).IsRequired().HasMaxLength(500);
        builder.Property(image => image.ThumbnailPath).IsRequired().HasMaxLength(500);
        builder.Property(image => image.CreatedAt).IsRequired();
        builder.HasIndex(image => image.TravelRecordId);
        builder.HasOne<TravelRecord>()
            .WithMany()
            .HasForeignKey(image => image.TravelRecordId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}