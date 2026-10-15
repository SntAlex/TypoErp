using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;

namespace TypoErp.Modules.Identity.Persistence.Configurations;

internal sealed class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    private static readonly JsonSerializerOptions JsonOptions = new();

    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("users");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Username).IsRequired().HasMaxLength(64);
        builder.Property(x => x.NormalizedUsername).IsRequired().HasMaxLength(64);
        builder.Property(x => x.DisplayName).IsRequired().HasMaxLength(150);
        builder.Property(x => x.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(x => x.Role).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.UpdatedAtUtc).IsRequired();
        builder.Property(x => x.LastLoginAtUtc);

        var permissionsConverter = new ValueConverter<List<AppPermission>, string>(
            v => JsonSerializer.Serialize(v, JsonOptions),
            v =>
                string.IsNullOrWhiteSpace(v)
                    ? new List<AppPermission>()
                    : JsonSerializer.Deserialize<List<AppPermission>>(v, JsonOptions)
                        ?? new List<AppPermission>()
        );

        var permissionsComparer = new ValueComparer<List<AppPermission>>(
            (left, right) =>
                ReferenceEquals(left, right)
                || (left != null && right != null && left.SequenceEqual(right)),
            list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
            list => list.ToList()
        );

        var permissionsProperty = builder
            .Property(x => x.Permissions)
            .HasConversion(permissionsConverter)
            .IsRequired();

        permissionsProperty.Metadata.SetValueComparer(permissionsComparer);

        builder
            .HasIndex(x => x.NormalizedUsername)
            .IsUnique()
            .HasDatabaseName("IX_users_normalized_username");

        builder.HasIndex(x => x.IsActive).HasDatabaseName("IX_users_is_active");
    }
}
