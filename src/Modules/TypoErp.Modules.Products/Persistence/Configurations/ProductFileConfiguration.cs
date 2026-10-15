using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TypoErp.Modules.Products.Entities;

namespace TypoErp.Modules.Products.Persistence.Configurations;

internal sealed class ProductFileConfiguration : IEntityTypeConfiguration<ProductFile>
{
    public void Configure(EntityTypeBuilder<ProductFile> builder)
    {
        builder.ToTable("product_files");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.ProductId).IsRequired();
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(260);
        builder.Property(x => x.NormalizedFileName).IsRequired().HasMaxLength(260);
        builder.Property(x => x.Kind).IsRequired();
        builder.Property(x => x.Prefix).IsRequired().HasMaxLength(120);
        builder.Property(x => x.Extension).IsRequired().HasMaxLength(40);
        builder.Property(x => x.SizeBytes).IsRequired();
        builder.Property(x => x.XxHash64).IsRequired().HasMaxLength(16);
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder
            .HasIndex(x => new { x.ProductId, x.FileName })
            .IsUnique()
            .HasDatabaseName("IX_product_files_product_id_file_name");

        builder
            .HasOne(x => x.Product)
            .WithMany(x => x.Files)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
