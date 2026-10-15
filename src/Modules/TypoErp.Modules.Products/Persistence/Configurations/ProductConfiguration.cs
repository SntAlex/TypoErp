using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TypoErp.Modules.Products.Entities;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Article).IsRequired().HasMaxLength(260);
        builder.Property(x => x.NormalizedArticle).IsRequired().HasMaxLength(260);
        builder.Property(x => x.IsValid).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        var errorsConverter = new ValueConverter<List<ProductValidationError>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v =>
                string.IsNullOrWhiteSpace(v)
                    ? new List<ProductValidationError>()
                    : JsonSerializer.Deserialize<List<ProductValidationError>>(v, JsonOptions)
                        ?? new List<ProductValidationError>()
        );

        var errorsComparer = new ValueComparer<List<ProductValidationError>>(
            (left, right) =>
                ReferenceEquals(left, right)
                || (left != null && right != null && left.SequenceEqual(right)),
            list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            list => list.ToList()
        );

        var validationErrorsProperty = builder
            .Property(x => x.ValidationErrors)
            .HasConversion(errorsConverter)
            .IsRequired();

        validationErrorsProperty.Metadata.SetValueComparer(errorsComparer);

        builder
            .HasIndex(x => new { x.PrimaryTagId, x.Article })
            .IsUnique()
            .HasDatabaseName("IX_products_primary_tag_id_article");

        builder
            .HasIndex(x => x.NormalizedArticle)
            .HasDatabaseName("IX_products_normalized_article");

        builder.HasIndex(x => x.IsValid).HasDatabaseName("IX_products_is_valid");

        builder
            .HasOne(x => x.PrimaryTag)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.PrimaryTagId)
            .OnDelete(DeleteBehavior.Restrict);

        builder
            .HasOne(x => x.Category)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasOne(x => x.Format)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.FormatId)
            .OnDelete(DeleteBehavior.SetNull);

        builder
            .HasMany(x => x.SecondaryTags)
            .WithMany(x => x.Products)
            .UsingEntity<Dictionary<string, object>>(
                "product_secondary_tags",
                right =>
                    right
                        .HasOne<SecondaryTag>()
                        .WithMany()
                        .HasForeignKey("secondary_tag_id")
                        .OnDelete(DeleteBehavior.Cascade),
                left =>
                    left.HasOne<Product>()
                        .WithMany()
                        .HasForeignKey("product_id")
                        .OnDelete(DeleteBehavior.Cascade),
                join =>
                {
                    join.ToTable("product_secondary_tags");
                    join.HasKey("product_id", "secondary_tag_id");
                }
            );
    }
}
