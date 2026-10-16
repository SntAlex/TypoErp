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

        _options = new ProductsStorageOptions
        {
            RootPath = "/virtual/storage/products",
            MaxProductsPerPrimaryTag = 500,
        };
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

    [Fact]
    public async Task GetTaxonomyOverviewAsync_ShouldReturnCategoryFormatTreeWithInheritedTagsSoftDeletedItemsAndTagSummaries()
    {
        await SeedSampleCatalogAsync();
        var now = DateTimeOffset.UtcNow;

        // Добавим мягко удаленную категорию со своим мягко удаленным форматом и сохраненными тегами
        var deletedCategory = new Category("sticker", now, "Старые стикеры");
        deletedCategory.SoftDelete(now);
        var deletedFormat = new Format(deletedCategory.Id, "10х15", now);
        deletedFormat.SoftDelete(now);

        // Также добавим мягко удаленный формат внутрь активной категории "card"
        var activeCardCategory = await _dbContext
            .Categories.Include(c => c.SecondaryTags)
            .SingleAsync(c => c.Code == "card");
        var deletedCardFormat = new Format(activeCardCategory.Id, "а6", now);
        deletedCardFormat.SoftDelete(now);

        var archiveTag = new SecondaryTag("архивный тег", now);
        deletedCategory.SecondaryTags.Add(archiveTag);
        deletedFormat.SecondaryTags.Add(archiveTag);

        _dbContext.Categories.Add(deletedCategory);
        _dbContext.Formats.AddRange(deletedFormat, deletedCardFormat);
        _dbContext.SecondaryTags.Add(archiveTag);
        await _dbContext.SaveChangesAsync();

        // 1. Запрос с includeDeleted = true (показывает и активные, и мягко удаленные)
        var fullOverview = await _queryService.GetTaxonomyOverviewAsync(includeDeleted: true);

        fullOverview.ActiveCategoriesCount.ShouldBe(2);
        fullOverview.DeletedCategoriesCount.ShouldBe(1);
        fullOverview.ActiveFormatsCount.ShouldBe(2);
        fullOverview.DeletedFormatsCount.ShouldBe(2);
        fullOverview.Categories.Count.ShouldBe(3);

        var cardNode = fullOverview.Categories.Single(c => c.Code == "card");
        cardNode.DisplayName.ShouldBe("Наклейки");
        cardNode.DisplayLabel.ShouldBe("Наклейки (card)");
        cardNode.IsDeleted.ShouldBeFalse();
        cardNode.TotalProductsCount.ShouldBe(1);
        cardNode.ValidProductsCount.ShouldBe(1);
        cardNode.InvalidProductsCount.ShouldBe(0);
        cardNode.SecondaryTags.ShouldBe(["глянец"]);
        cardNode.Formats.Count.ShouldBe(2);

        var format45k = cardNode.Formats.Single(f => f.Code == "45к");
        format45k.IsDeleted.ShouldBeFalse();
        format45k.TotalProductsCount.ShouldBe(1);
        format45k.SecondaryTags.ShouldBe(["хит"]);
        format45k.InheritedCategorySecondaryTags.ShouldBe(["глянец"]);

        var formatA6 = cardNode.Formats.Single(f => f.Code == "а6");
        formatA6.IsDeleted.ShouldBeTrue();
        formatA6.TotalProductsCount.ShouldBe(0);

        var stickerNode = fullOverview.Categories.Single(c => c.Code == "sticker");
        stickerNode.IsDeleted.ShouldBeTrue();
        stickerNode.DisplayName.ShouldBe("Старые стикеры");
        stickerNode.SecondaryTags.ShouldBe(["архивный тег"]);
        stickerNode.Formats.ShouldHaveSingleItem().Code.ShouldBe("10х15");

        // Проверяем статистику первичных тегов
        fullOverview.PrimaryTags.Count.ShouldBe(2);
        var springTag = fullOverview.PrimaryTags.Single(t => t.NormalizedName == "весна 2026");
        springTag.TotalProductsCount.ShouldBe(2);
        springTag.ValidProductsCount.ShouldBe(2);
        springTag.InvalidProductsCount.ShouldBe(0);
        springTag.MaxCapacity.ShouldBe(500);

        var autumnTag = fullOverview.PrimaryTags.Single(t => t.NormalizedName == "осень 2026");
        autumnTag.TotalProductsCount.ShouldBe(1);
        autumnTag.ValidProductsCount.ShouldBe(0);
        autumnTag.InvalidProductsCount.ShouldBe(1);

        // Проверяем статистику непервичных тегов
        fullOverview.SecondaryTags.Count.ShouldBe(4);
        var archiveTagSummary = fullOverview.SecondaryTags.Single(t => t.Name == "архивный тег");
        archiveTagSummary.DirectProductsCount.ShouldBe(0);
        archiveTagSummary.CategoriesCount.ShouldBe(1);
        archiveTagSummary.FormatsCount.ShouldBe(1);

        // 2. Запрос с includeDeleted = false (скрывает мягко удаленные категории и форматы)
        var activeOnlyOverview = await _queryService.GetTaxonomyOverviewAsync(
            includeDeleted: false
        );

        activeOnlyOverview.Categories.Count.ShouldBe(2);
        activeOnlyOverview.Categories.ShouldNotContain(c => c.IsDeleted);
        var activeCardNode = activeOnlyOverview.Categories.Single(c => c.Code == "card");
        activeCardNode.Formats.ShouldHaveSingleItem().Code.ShouldBe("45к");
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
