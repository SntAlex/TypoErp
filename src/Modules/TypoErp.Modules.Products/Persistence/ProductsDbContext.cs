using Microsoft.EntityFrameworkCore;
using TypoErp.Modules.Products.Entities;

namespace TypoErp.Modules.Products.Persistence;

/// <summary>
///     Контекст базы данных модуля «Товары».
/// </summary>
public sealed class ProductsDbContext(DbContextOptions<ProductsDbContext> options)
    : DbContext(options)
{
    public DbSet<PrimaryTag> PrimaryTags => Set<PrimaryTag>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Format> Formats => Set<Format>();

    public DbSet<SecondaryTag> SecondaryTags => Set<SecondaryTag>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductFile> ProductFiles => Set<ProductFile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductsDbContext).Assembly);
    }
}
