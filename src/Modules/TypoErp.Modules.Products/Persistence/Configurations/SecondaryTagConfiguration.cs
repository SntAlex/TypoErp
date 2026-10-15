using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TypoErp.Modules.Products.Entities;

namespace TypoErp.Modules.Products.Persistence.Configurations;

internal sealed class SecondaryTagConfiguration : IEntityTypeConfiguration<SecondaryTag>
{
    public void Configure(EntityTypeBuilder<SecondaryTag> builder)
    {
        builder.ToTable("secondary_tags");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);
        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.HasIndex(x => x.Name).IsUnique().HasDatabaseName("IX_secondary_tags_name");
    }
}
