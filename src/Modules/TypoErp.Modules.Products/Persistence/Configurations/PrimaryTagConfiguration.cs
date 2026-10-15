using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TypoErp.Modules.Products.Entities;

namespace TypoErp.Modules.Products.Persistence.Configurations;

internal sealed class PrimaryTagConfiguration : IEntityTypeConfiguration<PrimaryTag>
{
    public void Configure(EntityTypeBuilder<PrimaryTag> builder)
    {
        builder.ToTable("primary_tags");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(260);
        builder.Property(x => x.NormalizedName).IsRequired().HasMaxLength(260);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();

        builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("IX_primary_tags_name");
        builder.HasIndex(x => x.NormalizedName).HasDatabaseName("IX_primary_tags_normalized_name");
    }
}
