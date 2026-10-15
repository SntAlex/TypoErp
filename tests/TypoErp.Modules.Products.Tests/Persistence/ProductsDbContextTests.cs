using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TypoErp.Modules.Products.Entities;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Persistence;

public sealed class ProductsDbContextTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);
    private SqliteConnection _connection = null!;
    private DbContextOptions<ProductsDbContext> _dbOptions = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _dbOptions = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SaveAndQuery_WhenValidProductWithFullHierarchyAndSecondaryTags_ShouldPersistAllRelations()
    {
        var productId = Guid.NewGuid();
        var primaryTag = new PrimaryTag("аниме тетради", FixedNow);
        var category = new Category("card", FixedNow, "Карточки и открытки");
        var format = new Format(category.Id, "45к", FixedNow);

        var tagHit = new SecondaryTag("хит продаж", FixedNow);
        var tagSeason = new SecondaryTag("осень 2026", FixedNow);
        var tagPrint = new SecondaryTag("плотная бумага", FixedNow);

        category.SecondaryTags.Add(tagPrint);
        format.SecondaryTags.Add(tagSeason);

        var product = new Product(
            productId,
            "45к тетрадь наруто",
            primaryTag.Id,
            category.Id,
            format.Id,
            FixedNow
        );
        product.SecondaryTags.Add(tagHit);
        product.SecondaryTags.Add(tagSeason);

        product.Files.Add(
            new ProductFile(
                product.Id,
                "cover_1.jpg",
                ProductFileKind.Cover,
                "cover",
                ".jpg",
                120_500,
                "0123456789abcdef",
                FixedNow
            )
        );
        product.Files.Add(
            new ProductFile(
                product.Id,
                "card_front_1.pdf",
                ProductFileKind.AssemblyFile,
                "card",
                ".pdf",
                4_500_000,
                "fedcba9876543210",
                FixedNow
            )
        );

        await using (var writeDb = CreateDbContext())
        {
            writeDb.PrimaryTags.Add(primaryTag);
            writeDb.Categories.Add(category);
            writeDb.Formats.Add(format);
            writeDb.SecondaryTags.AddRange(tagHit, tagSeason, tagPrint);
            writeDb.Products.Add(product);

            await writeDb.SaveChangesAsync();
        }

        await using (var readDb = CreateDbContext())
        {
            var loaded = await readDb
                .Products.Include(p => p.PrimaryTag)
                .Include(p => p.Category)
                    .ThenInclude(c => c!.SecondaryTags)
                .Include(p => p.Format)
                    .ThenInclude(f => f!.SecondaryTags)
                .Include(p => p.Files)
                .Include(p => p.SecondaryTags)
                .SingleAsync(p => p.Id == productId);

            loaded.Article.ShouldBe("45к тетрадь наруто");
            loaded.NormalizedArticle.ShouldBe("45к тетрадь наруто");
            loaded.IsValid.ShouldBeTrue();
            loaded.ValidationErrors.ShouldBeEmpty();

            loaded.PrimaryTag.ShouldNotBeNull();
            loaded.PrimaryTag.NormalizedName.ShouldBe("аниме тетради");

            loaded.Category.ShouldNotBeNull();
            loaded.Category.Code.ShouldBe("card");
            loaded.Category.DisplayName.ShouldBe("Карточки и открытки");
            loaded.Category.IsDeleted.ShouldBeFalse();
            loaded.Category.SecondaryTags.Select(t => t.Name).ShouldBe(["плотная бумага"]);

            loaded.Format.ShouldNotBeNull();
            loaded.Format.Code.ShouldBe("45к");
            loaded.Format.CategoryId.ShouldBe(category.Id);
            loaded.Format.IsDeleted.ShouldBeFalse();
            loaded.Format.SecondaryTags.Select(t => t.Name).ShouldBe(["осень 2026"]);

            loaded
                .SecondaryTags.Select(t => t.Name)
                .OrderBy(n => n)
                .ShouldBe(["осень 2026", "хит продаж"]);

            loaded.Files.Count.ShouldBe(2);
            loaded.Files.ShouldContain(f =>
                f.FileName == "card_front_1.pdf"
                && f.Kind == ProductFileKind.AssemblyFile
                && f.XxHash64 == "fedcba9876543210"
                && f.SizeBytes == 4_500_000
            );
        }
    }

    [Fact]
    public async Task SaveAndQuery_WhenProductIsInvalidDraftOrHasErrors_ShouldPersistValidationErrorsAndTrackUpdates()
    {
        var primaryTag = new PrimaryTag("черновики", FixedNow);
        var product = new Product(
            Guid.NewGuid(),
            "45к пустой черновик",
            primaryTag.Id,
            categoryId: null,
            formatId: null,
            FixedNow,
            [
                ProductValidationError.MissingAssemblyFiles,
                ProductValidationError.InvalidCoverExtension,
            ]
        );

        await using (var writeDb = CreateDbContext())
        {
            writeDb.PrimaryTags.Add(primaryTag);
            writeDb.Products.Add(product);
            await writeDb.SaveChangesAsync();
        }

        await using (var updateDb = CreateDbContext())
        {
            var loaded = await updateDb.Products.SingleAsync(p => p.Id == product.Id);

            loaded.IsValid.ShouldBeFalse();
            loaded.CategoryId.ShouldBeNull();
            loaded.FormatId.ShouldBeNull();
            loaded.ValidationErrors.ShouldBe([
                ProductValidationError.MissingAssemblyFiles,
                ProductValidationError.InvalidCoverExtension,
            ]);

            // Мутируем список ошибок и проверяем, что EF Core ChangeTracker фиксирует изменения
            loaded.SetValidationErrors([ProductValidationError.NormalizationConflict]);
            await updateDb.SaveChangesAsync();
        }

        await using (var verifyDb = CreateDbContext())
        {
            var verified = await verifyDb.Products.SingleAsync(p => p.Id == product.Id);
            verified.IsValid.ShouldBeFalse();
            verified.ValidationErrors.ShouldBe([ProductValidationError.NormalizationConflict]);

            verified.SetValidationErrors([]);
            await verifyDb.SaveChangesAsync();
        }

        await using (var finalDb = CreateDbContext())
        {
            var validProduct = await finalDb.Products.SingleAsync(p => p.Id == product.Id);
            validProduct.IsValid.ShouldBeTrue();
            validProduct.ValidationErrors.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task FormatUniqueConstraint_WhenSameFormatCodeInDifferentCategories_ShouldSucceed()
    {
        var cardCategory = new Category("card", FixedNow, "Открытки");
        var posterCategory = new Category("poster", FixedNow, "Постеры");

        var cardFormat45k = new Format(cardCategory.Id, "45к", FixedNow);
        var posterFormat45k = new Format(posterCategory.Id, "45к", FixedNow);

        await using var dbContext = CreateDbContext();
        dbContext.Categories.AddRange(cardCategory, posterCategory);
        dbContext.Formats.AddRange(cardFormat45k, posterFormat45k);

        await Should.NotThrowAsync(() => dbContext.SaveChangesAsync());

        var formats = await dbContext.Formats.Where(f => f.Code == "45к").ToListAsync();
        formats.Count.ShouldBe(2);
    }

    [Fact]
    public async Task FormatUniqueConstraint_WhenDuplicateFormatCodeInSameCategory_ShouldThrowDbUpdateException()
    {
        var category = new Category("card", FixedNow);
        var format1 = new Format(category.Id, "45к", FixedNow);
        var format2 = new Format(category.Id, "45к", FixedNow);

        await using var dbContext = CreateDbContext();
        dbContext.Categories.Add(category);
        dbContext.Formats.AddRange(format1, format2);

        await Should.ThrowAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    [Fact]
    public async Task NormalizationConflictScenario_WhenTwoFoldersHaveDifferentRawNamesButSameNormalizedName_ShouldPersistBoth()
    {
        var primaryTag = new PrimaryTag("аниме", FixedNow);
        var validProduct = new Product(
            Guid.NewGuid(),
            "45к товар",
            primaryTag.Id,
            categoryId: null,
            formatId: null,
            FixedNow
        );
        var conflictingProduct = new Product(
            Guid.NewGuid(),
            "45к  товар",
            primaryTag.Id,
            categoryId: null,
            formatId: null,
            FixedNow,
            [ProductValidationError.NormalizationConflict]
        );

        await using var dbContext = CreateDbContext();
        dbContext.PrimaryTags.Add(primaryTag);
        dbContext.Products.AddRange(validProduct, conflictingProduct);

        await Should.NotThrowAsync(() => dbContext.SaveChangesAsync());

        var products = await dbContext
            .Products.Where(p => p.NormalizedArticle == "45к товар")
            .ToListAsync();
        products.Count.ShouldBe(2);
    }

    [Fact]
    public async Task SoftDeleteAndRestore_WhenCategoryAndFormatMarkedDeletedAndRestored_ShouldPreserveDisplayNameAndTags()
    {
        var category = new Category("sticker", FixedNow, "Наклейки виниловые");
        var format = new Format(category.Id, "а5", FixedNow);
        var secondaryTag = new SecondaryTag("глянец", FixedNow);

        category.SecondaryTags.Add(secondaryTag);
        format.SecondaryTags.Add(secondaryTag);

        await using (var seedDb = CreateDbContext())
        {
            seedDb.Categories.Add(category);
            seedDb.Formats.Add(format);
            seedDb.SecondaryTags.Add(secondaryTag);
            await seedDb.SaveChangesAsync();
        }

        var deletedAt = FixedNow.AddHours(1);
        await using (var deleteDb = CreateDbContext())
        {
            var cat = await deleteDb.Categories.SingleAsync(c => c.Id == category.Id);
            var fmt = await deleteDb.Formats.SingleAsync(f => f.Id == format.Id);

            cat.SoftDelete(deletedAt);
            fmt.SoftDelete(deletedAt);
            await deleteDb.SaveChangesAsync();
        }

        var restoredAt = FixedNow.AddHours(2);
        await using (var restoreDb = CreateDbContext())
        {
            var cat = await restoreDb
                .Categories.Include(c => c.SecondaryTags)
                .SingleAsync(c => c.Id == category.Id);
            var fmt = await restoreDb
                .Formats.Include(f => f.SecondaryTags)
                .SingleAsync(f => f.Id == format.Id);

            cat.IsDeleted.ShouldBeTrue();
            fmt.IsDeleted.ShouldBeTrue();

            cat.Restore(restoredAt);
            fmt.Restore(restoredAt);
            await restoreDb.SaveChangesAsync();
        }

        await using (var verifyDb = CreateDbContext())
        {
            var cat = await verifyDb
                .Categories.Include(c => c.SecondaryTags)
                .SingleAsync(c => c.Id == category.Id);
            var fmt = await verifyDb
                .Formats.Include(f => f.SecondaryTags)
                .SingleAsync(f => f.Id == format.Id);

            cat.IsDeleted.ShouldBeFalse();
            cat.DisplayName.ShouldBe("Наклейки виниловые");
            cat.SecondaryTags.ShouldHaveSingleItem().Name.ShouldBe("глянец");

            fmt.IsDeleted.ShouldBeFalse();
            fmt.SecondaryTags.ShouldHaveSingleItem().Name.ShouldBe("глянец");
        }
    }

    [Fact]
    public async Task DeleteProduct_ShouldCascadeDeleteProductFilesAndKeepSecondaryTagsAndCategory()
    {
        var primaryTag = new PrimaryTag("тег", FixedNow);
        var category = new Category("card", FixedNow);
        var format = new Format(category.Id, "45к", FixedNow);
        var secondaryTag = new SecondaryTag("новинка", FixedNow);
        var product = new Product(
            Guid.NewGuid(),
            "45к товар",
            primaryTag.Id,
            category.Id,
            format.Id,
            FixedNow
        );
        product.SecondaryTags.Add(secondaryTag);
        product.Files.Add(
            new ProductFile(
                product.Id,
                "card_1.pdf",
                ProductFileKind.AssemblyFile,
                "card",
                ".pdf",
                1024,
                "0011223344556677",
                FixedNow
            )
        );

        await using (var seedDb = CreateDbContext())
        {
            seedDb.PrimaryTags.Add(primaryTag);
            seedDb.Categories.Add(category);
            seedDb.Formats.Add(format);
            seedDb.SecondaryTags.Add(secondaryTag);
            seedDb.Products.Add(product);
            await seedDb.SaveChangesAsync();
        }

        await using (var deleteDb = CreateDbContext())
        {
            var loaded = await deleteDb.Products.SingleAsync(p => p.Id == product.Id);
            deleteDb.Products.Remove(loaded);
            await deleteDb.SaveChangesAsync();
        }

        await using (var verifyDb = CreateDbContext())
        {
            (await verifyDb.Products.CountAsync()).ShouldBe(0);
            (await verifyDb.ProductFiles.CountAsync()).ShouldBe(0);
            (await verifyDb.SecondaryTags.CountAsync()).ShouldBe(1);
            (await verifyDb.Categories.CountAsync()).ShouldBe(1);
            (await verifyDb.Formats.CountAsync()).ShouldBe(1);
        }
    }

    private ProductsDbContext CreateDbContext() => new(_dbOptions);
}
