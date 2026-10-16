using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TypoErp.Modules.Products.Catalog;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Entities;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Catalog;

public sealed class ProductCatalogQueryServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ProductsDbContext _dbContext;
    private readonly ProductsStorageOptions _options;
    private readonly ProductCatalogQueryService _queryService;
    private readonly FakeProductStorage _storage;

    public ProductCatalogQueryServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var dbOptions = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new ProductsDbContext(dbOptions);
        _dbContext.Database.EnsureCreated();

        _options = new ProductsStorageOptions { RootPath = "/virtual/storage/products" };
        _storage = new FakeProductStorage(_options);
        _queryService = new ProductCatalogQueryService(_dbContext, _storage, _options);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task GetFilterLookupsAsync_ShouldReturnCountsTagsCategoriesFormatsAndStorageAccessibility()
    {
        await SeedSampleCatalogAsync();
        _storage.IsRootAccessible = false;

        var lookups = await _queryService.GetFilterLookupsAsync();

        lookups.IsStorageRootAccessible.ShouldBeFalse();
        lookups.StorageRootPath.ShouldBe("/virtual/storage/products");
        lookups.TotalProductsCount.ShouldBe(3);
        lookups.ValidProductsCount.ShouldBe(2);
        lookups.InvalidProductsCount.ShouldBe(1);

        lookups.PrimaryTags.Count.ShouldBe(2);
        lookups.Categories.Count.ShouldBe(2);
        lookups.Formats.Count.ShouldBe(2);
        lookups.SecondaryTags.Count.ShouldBe(3);
    }

    [Fact]
    public async Task QueryProductsAsync_ShouldSupportCyrillicCaseInsensitiveSearchAndInheritedSecondaryTags()
    {
        var seeded = await SeedSampleCatalogAsync();

        // Поиск по русскому пользовательскому названию категории в верхнем регистре
        var byCategoryDisplayName = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(SearchText: "НАКЛЕЙКИ")
        );

        byCategoryDisplayName.TotalMatchedCount.ShouldBe(1);
        byCategoryDisplayName.Items.Single().Id.ShouldBe(seeded.ValidCardProductId);
        byCategoryDisplayName.Items.Single().InheritedSecondaryTags.ShouldContain("глянец");
        byCategoryDisplayName.Items.Single().InheritedSecondaryTags.ShouldContain("хит");

        // Фильтрация по непервичному тегу, назначенному на Категорию (должен найтись товар этой категории)
        var byInheritedCategoryTag = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(SecondaryTagIds: [seeded.CategoryTagId])
        );

        byInheritedCategoryTag.TotalMatchedCount.ShouldBe(1);
        byInheritedCategoryTag.Items.Single().Id.ShouldBe(seeded.ValidCardProductId);
    }

    [Fact]
    public async Task QueryProductsAsync_ShouldFilterByValidityAndSpecificValidationError()
    {
        var seeded = await SeedSampleCatalogAsync();

        var invalidOnly = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(
                Validity: ProductValidityFilter.InvalidOnly,
                ValidationError: ProductValidationError.MissingAssemblyFiles
            )
        );

        invalidOnly.TotalMatchedCount.ShouldBe(1);
        var draftItem = invalidOnly.Items.Single();
        draftItem.Id.ShouldBe(seeded.DraftProductId);
        draftItem.IsValid.ShouldBeFalse();
        draftItem.IsDraftOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task GetProductByIdAsync_ShouldReturnFullProductDtoOrNullWhenNotFound()
    {
        var seeded = await SeedSampleCatalogAsync();

        var found = await _queryService.GetProductByIdAsync(seeded.ValidCardProductId);

        found.ShouldNotBeNull();
        found.Id.ShouldBe(seeded.ValidCardProductId);
        found.Article.ShouldBe("45к котики");
        found.PrimaryTagName.ShouldBe("весна 2026");
        found.CategoryCode.ShouldBe("card");
        found.CategoryDisplayName.ShouldBe("Наклейки");
        found.FormatCode.ShouldBe("45к");
        found.SecondaryTags.ShouldContain("новинка");
        found.InheritedSecondaryTags.ShouldContain("глянец");
        found.InheritedSecondaryTags.ShouldContain("хит");
        found.Files.Count.ShouldBe(2);
        found.CoversCount.ShouldBe(1);
        found.AssemblyFilesCount.ShouldBe(1);

        var missing = await _queryService.GetProductByIdAsync(Guid.NewGuid());
        missing.ShouldBeNull();
    }

    private async Task<(
        Guid ValidCardProductId,
        Guid ValidPosterProductId,
        Guid DraftProductId,
        Guid CategoryTagId
    )> SeedSampleCatalogAsync()
    {
        var now = DateTimeOffset.UtcNow;

        var tagSpring = new PrimaryTag("весна 2026", now);
        var tagAutumn = new PrimaryTag("осень 2026", now);

        var catCard = new Category("card", now, "Наклейки");
        var catPoster = new Category("poster", now, "Постеры");

        var formatCard45 = new Format(catCard.Id, "45к", now);
        var formatPosterA3 = new Format(catPoster.Id, "а3", now);

        var secTagDirect = new SecondaryTag("новинка", now);
        var secTagCategory = new SecondaryTag("глянец", now);
        var secTagFormat = new SecondaryTag("хит", now);

        catCard.SecondaryTags.Add(secTagCategory);
        formatCard45.SecondaryTags.Add(secTagFormat);

        var validCard = new Product(
            Guid.NewGuid(),
            "45к котики",
            tagSpring.Id,
            catCard.Id,
            formatCard45.Id,
            now
        );
        validCard.SecondaryTags.Add(secTagDirect);
        validCard.Files.Add(
            new ProductFile(
                validCard.Id,
                "cover_1.jpg",
                ProductFileKind.Cover,
                "cover",
                ".jpg",
                1024,
                "0011223344556677",
                now
            )
        );
        validCard.Files.Add(
            new ProductFile(
                validCard.Id,
                "card_print.pdf",
                ProductFileKind.AssemblyFile,
                "card",
                ".pdf",
                4096,
                "8899aabbccddeeff",
                now
            )
        );

        var validPoster = new Product(
            Guid.NewGuid(),
            "а3 горы",
            tagSpring.Id,
            catPoster.Id,
            formatPosterA3.Id,
            now
        );
        validPoster.Files.Add(
            new ProductFile(
                validPoster.Id,
                "poster_main.tif",
                ProductFileKind.AssemblyFile,
                "poster",
                ".tif",
                8192,
                "1122334455667788",
                now
            )
        );

        var draftProduct = new Product(
            Guid.NewGuid(),
            "45к черновик без сборки",
            tagAutumn.Id,
            null,
            null,
            now,
            [ProductValidationError.MissingAssemblyFiles]
        );

        _dbContext.PrimaryTags.AddRange(tagSpring, tagAutumn);
        _dbContext.Categories.AddRange(catCard, catPoster);
        _dbContext.Formats.AddRange(formatCard45, formatPosterA3);
        _dbContext.SecondaryTags.AddRange(secTagDirect, secTagCategory, secTagFormat);
        _dbContext.Products.AddRange(validCard, validPoster, draftProduct);

        await _dbContext.SaveChangesAsync();

        return (validCard.Id, validPoster.Id, draftProduct.Id, secTagCategory.Id);
    }
}
