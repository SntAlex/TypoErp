using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TypoErp.Modules.Products.Entities;

namespace TypoErp.Modules.Products.Persistence.Configurations;

internal sealed class FormatConfiguration : IEntityTypeConfiguration<Format>
{
    public void Configure(EntityTypeBuilder<Format> builder)
    {
        builder.ToTable("formats");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.CategoryId).IsRequired();
        builder.Property(x => x.Code).IsRequired().HasMaxLength(120);
        builder.Property(x => x.IsDeleted).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder
            .HasIndex(x => new { x.CategoryId, x.Code })
            .IsUnique()
            .HasDatabaseName("IX_formats_category_id_code");

        builder.HasIndex(x => x.IsDeleted).HasDatabaseName("IX_formats_is_deleted");

        builder
            .HasOne(x => x.Category)
            .WithMany(x => x.Formats)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder
            .HasMany(x => x.SecondaryTags)
            .WithMany(x => x.Formats)
            .UsingEntity<Dictionary<string, object>>(
                "format_secondary_tags",
                right =>
                    right
                        .HasOne<SecondaryTag>()
                        .WithMany()
                        .HasForeignKey("secondary_tag_id")
                        .OnDelete(DeleteBehavior.Cascade),
                left =>
                    left.HasOne<Format>()
                        .WithMany()
                        .HasForeignKey("format_id")
                        .OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("format_secondary_tags");
                    join.HasKey("format_id", "secondary_tag_id");
                }
            );
    }
}
