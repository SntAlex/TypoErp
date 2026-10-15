using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TypoErp.Modules.Products.Entities;

namespace TypoErp.Modules.Products.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Code).IsRequired().HasMaxLength(120);
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.IsDeleted).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("IX_categories_code");
        builder.HasIndex(x => x.IsDeleted).HasDatabaseName("IX_categories_is_deleted");

        builder
            .HasMany(x => x.SecondaryTags)
            .WithMany(x => x.Categories)
            .UsingEntity<Dictionary<string, object>>(
                "category_secondary_tags",
                right =>
                    right
                        .HasOne<SecondaryTag>()
                        .WithMany()
                        .HasForeignKey("secondary_tag_id")
                        .OnDelete(DeleteBehavior.Cascade),
                left =>
                    left.HasOne<Category>()
                        .WithMany()
                        .HasForeignKey("category_id")
                        .OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("category_secondary_tags");
                    join.HasKey("category_id", "secondary_tag_id");
                }
            );
    }
}
