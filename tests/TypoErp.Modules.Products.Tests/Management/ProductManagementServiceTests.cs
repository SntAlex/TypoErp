using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Entities;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Locking;
using TypoErp.Modules.Products.Management;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Sync;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Management;

public sealed class ProductManagementServiceTests : IDisposable
{
    private const string ActiveSessionId = "worker-session-1";
    private readonly SqliteConnection _connection;
    private readonly ProductsDbContext _dbContext;
    private readonly ProductsWorkspaceLockService _lockService;
    private readonly ProductManagementService _managementService;
    private readonly ProductsStorageOptions _options;
    private readonly FakeProductStorage _storage;
    private readonly ProductSyncService _syncService;

    public ProductManagementServiceTests()
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
            MaxPrimaryTagLength = 200,
            MaxArticleLength = 60,
            MaxProductsPerPrimaryTag = 5,
        };

        _storage = new FakeProductStorage(_options);
        _lockService = new ProductsWorkspaceLockService();
        _lockService.TryAcquire(ActiveSessionId, Guid.NewGuid(), "Оператор Тест");

        _syncService = new ProductSyncService(_dbContext, _storage, _lockService, _options);
        _managementService = new ProductManagementService(
            _dbContext,
            _storage,
            _lockService,
            _options
        );
    }

    public void Dispose()
    {
        _dbContext.Dispose();
        _connection.Dispose();
    }

    [Fact]
    public async Task CreateProductAsync_WhenDraftWithoutFiles_ShouldCreateOnDiskAndInDbAsInvalidWithMissingAssemblyFiles()
    {
        var request = new CreateProductRequest(
            PrimaryTagName: "  Новинки   2026 ",
            Article: "  45К   Артикул Черновик  ",
            Files: null,
            SecondaryTagNames: ["  Срочно ", "ХИТ"]
        );

        var result = await _managementService.CreateProductAsync(ActiveSessionId, request);

        result.NormalizedPrimaryTag.ShouldBe("новинки 2026");
        result.NormalizedArticle.ShouldBe("45к артикул черновик");
        result.IsValid.ShouldBeFalse();
        result.ValidationErrors.ShouldBe([ProductValidationError.MissingAssemblyFiles]);
        result.CategoryId.ShouldBeNull();
        result.FormatId.ShouldBeNull();

        (
            await _storage.ProductDirectoryExistsAsync("новинки 2026", "45к артикул черновик")
        ).ShouldBeTrue();
        var idOnDisk = await _storage.ReadProductIdAsync("новинки 2026", "45к артикул черновик");
        idOnDisk.Status.ShouldBe(ProductIdReadStatus.Found);
        idOnDisk.ProductId.ShouldBe(result.ProductId);

        var dbProduct = await _dbContext
            .Products.Include(p => p.SecondaryTags)
            .SingleAsync(p => p.Id == result.ProductId);
        dbProduct.IsValid.ShouldBeFalse();
        dbProduct.SecondaryTags.Select(t => t.Name).OrderBy(n => n).ShouldBe(["срочно", "хит"]);
    }

    [Fact]
    public async Task CreateProductAsync_WithValidCoverAndAssemblyFile_ShouldInferCategoryAndFormatAndBeValid()
    {
        var request = new CreateProductRequest(
            PrimaryTagName: "основной тег",
            Article: "45к визитка люкс",
            Files:
            [
                new ProductFileUpload("Cover_1.JPG", [1, 2, 3]),
                new ProductFileUpload("CARD_front.pdf", [10, 20, 30, 40]),
            ]
        );

        var result = await _managementService.CreateProductAsync(ActiveSessionId, request);

        result.IsValid.ShouldBeTrue();
        result.ValidationErrors.ShouldBeEmpty();
        result.AddedOrUpdatedFilesCount.ShouldBe(2);
        result.CategoryId.ShouldNotBeNull();
        result.FormatId.ShouldNotBeNull();

        var dbProduct = await _dbContext
            .Products.Include(p => p.Category)
            .Include(p => p.Format)
            .Include(p => p.Files)
            .SingleAsync(p => p.Id == result.ProductId);

        dbProduct.Category!.Code.ShouldBe("card");
        dbProduct.Format!.Code.ShouldBe("45к");
        dbProduct.Files.Count.ShouldBe(2);
    }

    [Fact]
    public async Task UpdateProductAsync_ShouldRepairInvalidProductAndSkipUnchangedFilesByXxHash64()
    {
        // Создаем невалидный товар на диске с конфликтом префиксов сборки и без пробела в артикуле
        await _storage.CreateProductDirectoryAsync("тег 1", "артикулбезпробела");
        var sameCoverBytes = Encoding.UTF8.GetBytes("cover-image-bytes");
        await _storage.WriteProductFileAsync(
            "тег 1",
            "артикулбезпробела",
            "cover_1.jpg",
            sameCoverBytes
        );
        await _storage.WriteProductFileAsync(
            "тег 1",
            "артикулбезпробела",
            "card_1.pdf",
            Encoding.UTF8.GetBytes("card-1")
        );
        await _storage.WriteProductFileAsync(
            "тег 1",
            "артикулбезпробела",
            "poster_2.pdf",
            Encoding.UTF8.GetBytes("poster-2")
        );

        await _syncService.SynchronizeAsync(ActiveSessionId);

        var invalidProduct = await _dbContext.Products.SingleAsync();
        invalidProduct.IsValid.ShouldBeFalse();
        invalidProduct.ValidationErrors.ShouldContain(ProductValidationError.ArticleMissingSpace);
        invalidProduct.ValidationErrors.Count.ShouldBeGreaterThanOrEqualTo(2);

        // Чиним товар через UpdateProductAsync:
        // 1. Исправляем артикул на "45к артикул с пробелом"
        // 2. Переименовываем poster_2.pdf -> card_2.pdf
        // 3. Повторно передаем cover_1.jpg с теми же байтами (должен быть пропущен по XxHash64)
        var updateResult = await _managementService.UpdateProductAsync(
            ActiveSessionId,
            new UpdateProductRequest(
                ProductId: invalidProduct.Id,
                NewPrimaryTagName: "тег 1",
                NewArticle: "45к артикул с пробелом",
                FilesToUpsert: [new ProductFileUpload("cover_1.jpg", sameCoverBytes)],
                FilesToDelete: null,
                FileRenames: [new ProductFileRenameInput("poster_2.pdf", "card_2.pdf")],
                SecondaryTagNames: ["починено"]
            )
        );

        updateResult.IsValid.ShouldBeTrue();
        updateResult.ValidationErrors.ShouldBeEmpty();
        updateResult.UnchangedFilesCount.ShouldBe(1);
        updateResult.AddedOrUpdatedFilesCount.ShouldBe(0);

        var repaired = await _dbContext
            .Products.Include(p => p.Category)
            .Include(p => p.Format)
            .Include(p => p.Files)
            .SingleAsync(p => p.Id == invalidProduct.Id);

        repaired.IsValid.ShouldBeTrue();
        repaired.Article.ShouldBe("45к артикул с пробелом");
        repaired.Category!.Code.ShouldBe("card");
        repaired.Format!.Code.ShouldBe("45к");
        repaired
            .Files.Select(f => f.FileName)
            .OrderBy(n => n)
            .ShouldBe(["card_1.pdf", "card_2.pdf", "cover_1.jpg"]);
    }

    [Fact]
    public async Task UpdateProductAsync_WhenFileIsInBothFilesToDeleteAndFileRenamesOrNoOpRename_ShouldSucceedCleanly()
    {
        var created = await _managementService.CreateProductAsync(
            ActiveSessionId,
            new CreateProductRequest(
                PrimaryTagName: "тег тест",
                Article: "45к товар тест",
                Files:
                [
                    new ProductFileUpload("card_1.pdf", [1, 2, 3]),
                    new ProductFileUpload("extra_delete.pdf", [4, 5, 6]),
                ]
            )
        );

        created.IsValid.ShouldBeFalse();

        var updated = await _managementService.UpdateProductAsync(
            ActiveSessionId,
            new UpdateProductRequest(
                ProductId: created.ProductId,
                NewPrimaryTagName: "тег тест",
                NewArticle: "45к товар тест",
                FilesToDelete: ["extra_delete.pdf"],
                FileRenames:
                [
                    new ProductFileRenameInput("card_1.pdf", "card_1.pdf"),
                    new ProductFileRenameInput("extra_delete.pdf", "card_2.pdf"),
                ]
            )
        );

        updated.IsValid.ShouldBeTrue();
        updated.DeletedFilesCount.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateProductAsync_WhenTargetArticleExistsInDatabase_ShouldThrowTargetAlreadyExists()
    {
        var first = await _managementService.CreateProductAsync(
            ActiveSessionId,
            new CreateProductRequest("тег 1", "45к первый товар")
        );
        var second = await _managementService.CreateProductAsync(
            ActiveSessionId,
            new CreateProductRequest("тег 1", "45к второй товар")
        );

        var ex = await Should.ThrowAsync<ProductManagementException>(() =>
            _managementService.UpdateProductAsync(
                ActiveSessionId,
                new UpdateProductRequest(
                    ProductId: second.ProductId,
                    NewPrimaryTagName: "тег 1",
                    NewArticle: first.NormalizedArticle
                )
            )
        );

        ex.ErrorCode.ShouldBe(ProductManagementErrorCode.TargetAlreadyExists);
    }

    [Fact]
    public async Task BulkOperations_MoveUpdateTagsAndDelete_ShouldUpdateStorageAndSoftDeleteCategories()
    {
        var p1 = await _managementService.CreateProductAsync(
            ActiveSessionId,
            new CreateProductRequest(
                "старый тег",
                "45к товар один",
                [new ProductFileUpload("card_1.pdf", [1, 2])]
            )
        );
        var p2 = await _managementService.CreateProductAsync(
            ActiveSessionId,
            new CreateProductRequest(
                "старый тег",
                "45к товар два",
                [new ProductFileUpload("card_2.pdf", [3, 4])]
            )
        );

        // 1. Bulk Move
        var moveResult = await _managementService.BulkMoveProductsAsync(
            ActiveSessionId,
            [p1.ProductId, p2.ProductId],
            "  Новый   Тег "
        );

        moveResult.MovedProductsCount.ShouldBe(2);
        moveResult.NormalizedTargetPrimaryTag.ShouldBe("новый тег");
        (await _storage.PrimaryTagDirectoryExistsAsync("старый тег")).ShouldBeFalse();
        (await _storage.ProductDirectoryExistsAsync("новый тег", "45к товар один")).ShouldBeTrue();
        (await _storage.ProductDirectoryExistsAsync("новый тег", "45к товар два")).ShouldBeTrue();

        // 2. Bulk Secondary Tags (Add -> Remove)
        await _managementService.BulkUpdateProductSecondaryTagsAsync(
            ActiveSessionId,
            [p1.ProductId, p2.ProductId],
            ["акция", "маркетплейс"],
            BulkSecondaryTagOperationMode.Add
        );

        await _managementService.BulkUpdateProductSecondaryTagsAsync(
            ActiveSessionId,
            [p1.ProductId],
            ["акция"],
            BulkSecondaryTagOperationMode.Remove
        );

        var dbP1 = await _dbContext
            .Products.Include(p => p.SecondaryTags)
            .SingleAsync(p => p.Id == p1.ProductId);
        dbP1.SecondaryTags.Select(t => t.Name).ShouldBe(["маркетплейс"]);

        // 3. Bulk Delete -> Category & Format должны уйти в Soft Delete
        var deletedCount = await _managementService.BulkDeleteProductsAsync(
            ActiveSessionId,
            [p1.ProductId, p2.ProductId]
        );
        deletedCount.ShouldBe(2);

        var category = await _dbContext.Categories.SingleAsync(c => c.Code == "card");
        category.IsDeleted.ShouldBeTrue();

        var format = await _dbContext.Formats.SingleAsync(f => f.Code == "45к");
        format.IsDeleted.ShouldBeTrue();
    }

    [Fact]
    public async Task CategoryFormatAndTagManagement_ShouldUpdateDisplayNameTagsAndRenamePrimaryAndSecondaryTags()
    {
        var created = await _managementService.CreateProductAsync(
            ActiveSessionId,
            new CreateProductRequest(
                PrimaryTagName: "исходный первичный тег",
                Article: "45к открытка праздник",
                Files: [new ProductFileUpload("card_1.pdf", [10, 20])],
                SecondaryTagNames: ["сезонный"]
            )
        );

        created.CategoryId.ShouldNotBeNull();
        created.FormatId.ShouldNotBeNull();

        // 1. Обновление DisplayName категории и назначение непервичных тегов категории и формату
        await _managementService.UpdateCategoryDisplayNameAsync(
            ActiveSessionId,
            created.CategoryId.Value,
            "  Коллекционные открытки  "
        );

        await _managementService.SetCategorySecondaryTagsAsync(
            ActiveSessionId,
            created.CategoryId.Value,
            ["  Плотный   Картон ", "УФ-лак"]
        );

        await _managementService.SetFormatSecondaryTagsAsync(
            ActiveSessionId,
            created.FormatId.Value,
            ["  Карманный   Формат "]
        );

        var updatedCategory = await _dbContext
            .Categories.AsNoTracking()
            .Include(c => c.SecondaryTags)
            .SingleAsync(c => c.Id == created.CategoryId.Value);
        updatedCategory.DisplayName.ShouldBe("Коллекционные открытки");
        updatedCategory
            .SecondaryTags.Select(t => t.Name)
            .OrderBy(n => n)
            .ShouldBe(["плотный картон", "уф-лак"]);

        var updatedFormat = await _dbContext
            .Formats.AsNoTracking()
            .Include(f => f.SecondaryTags)
            .SingleAsync(f => f.Id == created.FormatId.Value);
        updatedFormat.SecondaryTags.Select(t => t.Name).ShouldBe(["карманный формат"]);

        // 2. Переименование первичного тега на диске и в БД
        await _managementService.RenamePrimaryTagAsync(
            ActiveSessionId,
            created.PrimaryTagId,
            "  Переименованный   Тег 2026 "
        );

        (await _storage.PrimaryTagDirectoryExistsAsync("исходный первичный тег")).ShouldBeFalse();
        (await _storage.PrimaryTagDirectoryExistsAsync("переименованный тег 2026")).ShouldBeTrue();

        var renamedPrimaryTag = await _dbContext
            .PrimaryTags.AsNoTracking()
            .SingleAsync(t => t.Id == created.PrimaryTagId);
        renamedPrimaryTag.Name.ShouldBe("переименованный тег 2026");
        renamedPrimaryTag.NormalizedName.ShouldBe("переименованный тег 2026");

        // 3. Создание, переименование и удаление непервичного тега
        var customTag = await _managementService.GetOrCreateSecondaryTagAsync(
            ActiveSessionId,
            "  Спецвыпуск  "
        );
        customTag.Name.ShouldBe("спецвыпуск");

        await _managementService.RenameSecondaryTagAsync(
            ActiveSessionId,
            customTag.Id,
            "  Лимитированный   Выпуск "
        );

        var renamedSecondary = await _dbContext
            .SecondaryTags.AsNoTracking()
            .SingleAsync(t => t.Id == customTag.Id);
        renamedSecondary.Name.ShouldBe("лимитированный выпуск");

        // Попытка переименовать в уже существующий тег должна выбросить TargetAlreadyExists
        var duplicateEx = await Should.ThrowAsync<ProductManagementException>(() =>
            _managementService.RenameSecondaryTagAsync(ActiveSessionId, customTag.Id, "УФ-лак")
        );
        duplicateEx.ErrorCode.ShouldBe(ProductManagementErrorCode.TargetAlreadyExists);

        var deleted = await _managementService.DeleteSecondaryTagAsync(
            ActiveSessionId,
            customTag.Id
        );
        deleted.ShouldBeTrue();
        (await _dbContext.SecondaryTags.AnyAsync(t => t.Id == customTag.Id)).ShouldBeFalse();
    }
}
