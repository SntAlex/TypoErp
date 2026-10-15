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

public sealed class ProductCatalogQueryServiceTests : IAsyncLifetime
{
    private readonly DateTimeOffset _now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    private readonly ProductsStorageOptions _options = new() { RootPath = "/storage/products" };
    private SqliteConnection _connection = null!;
    private ProductsDbContext _dbContext = null!;
    private ProductCatalogQueryService _queryService = null!;
    private FakeProductStorage _storage = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var dbOptions = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _dbContext = new ProductsDbContext(dbOptions);
        await _dbContext.Database.EnsureCreatedAsync();

        _storage = new FakeProductStorage(_options);
        _queryService = new ProductCatalogQueryService(_dbContext, _storage, _options);

        await SeedSampleCatalogAsync();
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task GetFilterLookupsAsync_ShouldReturnCountersAndActiveLookups()
    {
        _storage.IsRootAccessible = true;

        var lookups = await _queryService.GetFilterLookupsAsync();

        lookups.IsStorageRootAccessible.ShouldBeTrue();
        lookups.StorageRootPath.ShouldBe("/storage/products");
        lookups.TotalProductsCount.ShouldBe(4);
        lookups.ValidProductsCount.ShouldBe(2);
        lookups.InvalidProductsCount.ShouldBe(2);

        lookups.PrimaryTags.Count.ShouldBe(2);
        lookups.PrimaryTags.ShouldContain(t => t.Name == "новый год" && t.ProductsCount == 2);
        lookups.PrimaryTags.ShouldContain(t => t.Name == "школа" && t.ProductsCount == 2);

        // Мягко удаленная категория не должна попадать в стандартный фильтр, если includeDeleted = false
        lookups.Categories.Count.ShouldBe(2);
        lookups.Categories.ShouldContain(c =>
            c.Code == "card" && c.DisplayName == "Открытки" && c.ProductsCount == 2
        );
        lookups.Categories.ShouldContain(c => c.Code == "poster" && c.ProductsCount == 1);

        // Два разных формата с одинаковым кодом "45к" в разных категориях ("card" и "poster")
        lookups.Formats.Count.ShouldBe(3);
        lookups.Formats.Count(f => f.Code == "45к").ShouldBe(2);

        lookups.SecondaryTags.Count.ShouldBe(3);
    }

    [Fact]
    public async Task QueryProductsAsync_ShouldFilterBySearchText_CaseInsensitiveCyrillic()
    {
        var resultByArticle = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(SearchText: "  СНЕЖИНКА ")
        );

        resultByArticle.TotalMatchedCount.ShouldBe(1);
        resultByArticle.Items.Single().Article.ShouldBe("45к снежинка синяя");

        var resultByCategoryDisplayName = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(SearchText: "ОТКРЫТКИ")
        );

        resultByCategoryDisplayName.TotalMatchedCount.ShouldBe(2);
    }

    [Fact]
    public async Task QueryProductsAsync_ShouldFilterByCategoryAndFormat()
    {
        var lookups = await _queryService.GetFilterLookupsAsync();
        var cardCategory = lookups.Categories.Single(c => c.Code == "card");
        var cardFormat45 = lookups.Formats.Single(f =>
            f.CategoryId == cardCategory.Id && f.Code == "45к"
        );

        var byCategory = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(CategoryId: cardCategory.Id)
        );
        byCategory.TotalMatchedCount.ShouldBe(2);

        var byFormat = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(FormatId: cardFormat45.Id)
        );
        byFormat.TotalMatchedCount.ShouldBe(1);
        byFormat.Items.Single().Article.ShouldBe("45к снежинка синяя");
    }

    [Fact]
    public async Task QueryProductsAsync_ShouldFilterBySecondaryTags_DirectAndInherited()
    {
        var lookups = await _queryService.GetFilterLookupsAsync();
        var hitTag = lookups.SecondaryTags.Single(t => t.Name == "хит");
        var glossyTag = lookups.SecondaryTags.Single(t => t.Name == "глянец");

        // "хит" назначен напрямую одному товару
        var byDirectTag = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(SecondaryTagIds: [hitTag.Id])
        );
        byDirectTag.TotalMatchedCount.ShouldBe(1);
        byDirectTag.Items.Single().Article.ShouldBe("45к снежинка синяя");

        // "глянец" назначен на категорию "card", поэтому наследуется обоими товарами категории "card"
        var byInheritedCategoryTag = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(SecondaryTagIds: [glossyTag.Id])
        );
        byInheritedCategoryTag.TotalMatchedCount.ShouldBe(2);
        byInheritedCategoryTag
            .Items.All(i => i.InheritedSecondaryTags.Contains("глянец"))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task QueryProductsAsync_ShouldFilterByValidityAndSpecificValidationError()
    {
        var validOnly = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(Validity: ProductValidityFilter.ValidOnly)
        );
        validOnly.TotalMatchedCount.ShouldBe(2);
        validOnly.Items.All(i => i.IsValid).ShouldBeTrue();

        var invalidOnly = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(Validity: ProductValidityFilter.InvalidOnly)
        );
        invalidOnly.TotalMatchedCount.ShouldBe(2);
        invalidOnly.Items.All(i => !i.IsValid).ShouldBeTrue();

        var conflictOnly = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(ValidationError: ProductValidationError.NormalizationConflict)
        );
        conflictOnly.TotalMatchedCount.ShouldBe(1);
        conflictOnly.Items.Single().Article.ShouldBe("невалидныйбезпробела");
    }

    [Fact]
    public async Task QueryProductsAsync_ShouldSupportPaginationAndFileCounts()
    {
        var page0 = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(
                SortBy: ProductCatalogSortField.Article,
                SortDescending: false,
                Page: 0,
                PageSize: 2
            )
        );

        page0.TotalMatchedCount.ShouldBe(4);
        page0.Items.Count.ShouldBe(2);
        page0.Items[0].Article.ShouldBe("10м елка зеленая");
        page0.Items[0].IsDraftOnly.ShouldBeTrue();

        page0.Items[1].Article.ShouldBe("45к расписание уроков");
        page0.Items[1].AssemblyFilesCount.ShouldBe(1);

        var page1 = await _queryService.QueryProductsAsync(
            new ProductCatalogFilter(
                SortBy: ProductCatalogSortField.Article,
                SortDescending: false,
                Page: 1,
                PageSize: 2
            )
        );

        page1.Items.Count.ShouldBe(2);
        page1.Items[0].Article.ShouldBe("45к снежинка синяя");
        page1.Items[0].CoversCount.ShouldBe(1);
        page1.Items[0].VideoCoversCount.ShouldBe(1);
        page1.Items[0].AssemblyFilesCount.ShouldBe(1);
    }

    private async Task SeedSampleCatalogAsync()
    {
        var tagNewYear = new PrimaryTag("новый год", _now);
        var tagSchool = new PrimaryTag("школа", _now);

        var catCard = new Category("card", _now, "Открытки");
        var catPoster = new Category("poster", _now, "Плакаты");
        var catDeleted = new Category("oldcat", _now, "Старая категория");
        catDeleted.SoftDelete(_now);

        var fmtCard45 = new Format(catCard.Id, "45к", _now);
        var fmtCard10 = new Format(catCard.Id, "10м", _now);
        var fmtPoster45 = new Format(catPoster.Id, "45к", _now);

        var secHit = new SecondaryTag("хит", _now);
        var secWinter = new SecondaryTag("зима", _now);
        var secGlossy = new SecondaryTag("глянец", _now);

        catCard.SecondaryTags.Add(secGlossy);

        // 1. Валидный товар в категории "card", формат "45к"
        var p1 = new Product(
            Guid.NewGuid(),
            "45к снежинка синяя",
            tagNewYear.Id,
            catCard.Id,
            fmtCard45.Id,
            _now
        );
        p1.SecondaryTags.Add(secHit);
        p1.SecondaryTags.Add(secWinter);
        p1.Files.Add(
            new ProductFile(
                p1.Id,
                "cover_1.jpg",
                ProductFileKind.Cover,
                "cover",
                ".jpg",
                102400,
                "0011223344556677",
                _now
            )
        );
        p1.Files.Add(
            new ProductFile(
                p1.Id,
                "video_1.mp4",
                ProductFileKind.VideoCover,
                "video",
                ".mp4",
                2048000,
                "8899aabbccddeeff",
                _now
            )
        );
        p1.Files.Add(
            new ProductFile(
                p1.Id,
                "card_front.pdf",
                ProductFileKind.AssemblyFile,
                "card",
                ".pdf",
                512000,
                "1122334455667788",
                _now
            )
        );

        // 2. Черновик (нет файлов сборки) в теге "новый год"
        var p2 = new Product(
            Guid.NewGuid(),
            "10м елка зеленая",
            tagNewYear.Id,
            catCard.Id,
            fmtCard10.Id,
            _now,
            [ProductValidationError.MissingAssemblyFiles]
        );

        // 3. Валидный товар в категории "poster", формат "45к"
        var p3 = new Product(
            Guid.NewGuid(),
            "45к расписание уроков",
            tagSchool.Id,
            catPoster.Id,
            fmtPoster45.Id,
            _now
        );
        p3.Files.Add(
            new ProductFile(
                p3.Id,
                "poster_a3.pdf",
                ProductFileKind.AssemblyFile,
                "poster",
                ".pdf",
                900000,
                "abcdef0123456789",
                _now
            )
        );

        // 4. Невалидный товар с несколькими ошибками (без пробела + конфликт нормализации)
        var p4 = new Product(
            Guid.NewGuid(),
            "невалидныйбезпробела",
            tagSchool.Id,
            null,
            null,
            _now,
            [
                ProductValidationError.ArticleMissingSpace,
                ProductValidationError.MissingAssemblyFiles,
                ProductValidationError.NormalizationConflict,
            ]
        );

        _dbContext.PrimaryTags.AddRange(tagNewYear, tagSchool);
        _dbContext.Categories.AddRange(catCard, catPoster, catDeleted);
        _dbContext.Formats.AddRange(fmtCard45, fmtCard10, fmtPoster45);
        _dbContext.SecondaryTags.AddRange(secHit, secWinter, secGlossy);
        _dbContext.Products.AddRange(p1, p2, p3, p4);

        await _dbContext.SaveChangesAsync();
        _dbContext.ChangeTracker.Clear();
    }
}
