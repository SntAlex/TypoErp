using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Locking;
using TypoErp.Modules.Products.Management;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Sync;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Management;

public sealed class ProductManagementServiceTests : IAsyncLifetime
{
    private const string WorkerSessionId = "worker-session-1";
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 16, 0, 0, TimeSpan.Zero);

    private SqliteConnection _connection = null!;
    private DbContextOptions<ProductsDbContext> _dbOptions = null!;
    private ProductsWorkspaceLockService _lockService = null!;
    private ProductsStorageOptions _options = null!;
    private FakeProductStorage _storage = null!;
    private FakeTimeProvider _timeProvider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _dbOptions = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlite(_connection)
            .Options;

        _options = new ProductsStorageOptions { MaxProductsPerPrimaryTag = 5 };
        _timeProvider = new FakeTimeProvider(FixedNow);
        _storage = new FakeProductStorage(_options);
        _lockService = new ProductsWorkspaceLockService(_timeProvider);

        _lockService.TryAcquire(WorkerSessionId, Guid.NewGuid(), "Оператор Товаров");

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task Operations_WhenSessionDoesNotHoldLock_ShouldThrowProductsWorkspaceLockException()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateManagementService(dbContext);

        await Should.ThrowAsync<ProductsWorkspaceLockException>(() =>
            service.CreateProductAsync(
                "other-session",
                new CreateProductRequest("аниме", "45к товар")
            )
        );
    }

    [Fact]
    public async Task CreateProductAsync_WhenValidProductWithFilesAndSecondaryTags_ShouldCreateOnDiskAndPersistInDb()
    {
        var request = new CreateProductRequest(
            "  Аниме   Тетради ",
            "  45К   Тетрадь   Наруто ",
            [
                new ProductFileUpload("  Cover_1   (2).JPG ", "cover-bytes"u8.ToArray()),
                new ProductFileUpload("  VIDEO_1.MP4 ", "video-bytes"u8.ToArray()),
                new ProductFileUpload("  CARD_front_1.PDF ", "card-layout-bytes"u8.ToArray()),
            ],
            ["  Хит   Продаж ", "Осень 2026", "хит продаж"]
        );

        ProductMutationResult result;
        await using (var dbContext = CreateDbContext())
        {
            var service = CreateManagementService(dbContext);
            result = await service.CreateProductAsync(WorkerSessionId, request);
        }

        result.IsValid.ShouldBeTrue();
        result.ValidationErrors.ShouldBeEmpty();
        result.NormalizedPrimaryTag.ShouldBe("аниме тетради");
        result.NormalizedArticle.ShouldBe("45к тетрадь наруто");
        result.CategoryId.ShouldNotBeNull();
        result.FormatId.ShouldNotBeNull();
        result.AddedOrUpdatedFilesCount.ShouldBe(3);

        // Проверяем физическое состояние в хранилище
        (
            await _storage.ProductDirectoryExistsAsync("аниме тетради", "45к тетрадь наруто")
        ).ShouldBeTrue();

        var idOnDisk = await _storage.ReadProductIdAsync("аниме тетради", "45к тетрадь наруто");
        idOnDisk.Status.ShouldBe(ProductIdReadStatus.Found);
        idOnDisk.ProductId.ShouldBe(result.ProductId);

        var diskFiles = await _storage.GetProductFilesAsync("аниме тетради", "45к тетрадь наруто");
        diskFiles.Select(f => f.FileName).ShouldContain("cover_1 (2).jpg");
        diskFiles.Select(f => f.FileName).ShouldContain("video_1.mp4");
        diskFiles.Select(f => f.FileName).ShouldContain("card_front_1.pdf");

        // Проверяем состояние в PostgreSQL
        await using (var verifyDb = CreateDbContext())
        {
            var product = await verifyDb
                .Products.Include(p => p.PrimaryTag)
                .Include(p => p.Category)
                .Include(p => p.Format)
                .Include(p => p.Files)
                .Include(p => p.SecondaryTags)
                .SingleAsync(p => p.Id == result.ProductId);

            product.IsValid.ShouldBeTrue();
            product.Article.ShouldBe("45к тетрадь наруто");
            product.PrimaryTag!.Name.ShouldBe("аниме тетради");
            product.Category!.Code.ShouldBe("card");
            product.Format!.Code.ShouldBe("45к");
            product.Files.Count.ShouldBe(3);
            product
                .SecondaryTags.Select(t => t.Name)
                .OrderBy(n => n)
                .ShouldBe(["осень 2026", "хит продаж"]);
        }
    }

    [Fact]
    public async Task CreateProductAsync_WhenEmptyDraftWithoutAssemblyFiles_ShouldCreateFolderAndProductIdAndMarkInvalid()
    {
        var request = new CreateProductRequest(
            "черновики",
            "45к будущая открытка",
            [new ProductFileUpload("cover_1.jpg", "preview-only"u8.ToArray())]
        );

        ProductMutationResult draftResult;
        await using (var dbContext = CreateDbContext())
        {
            var service = CreateManagementService(dbContext);
            draftResult = await service.CreateProductAsync(WorkerSessionId, request);
        }

        draftResult.IsValid.ShouldBeFalse();
        draftResult.ValidationErrors.ShouldBe([ProductValidationError.MissingAssemblyFiles]);
        draftResult.CategoryId.ShouldBeNull();
        draftResult.FormatId.ShouldBeNull();

        (
            await _storage.ProductDirectoryExistsAsync("черновики", "45к будущая открытка")
        ).ShouldBeTrue();
        var idOnDisk = await _storage.ReadProductIdAsync("черновики", "45к будущая открытка");
        idOnDisk.Status.ShouldBe(ProductIdReadStatus.Found);
        idOnDisk.ProductId.ShouldBe(draftResult.ProductId);

        // Загружаем файл сборки через UpdateProductAsync — черновик должен стать валидным товаром
        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        await using (var updateDb = CreateDbContext())
        {
            var service = CreateManagementService(updateDb);
            var updated = await service.UpdateProductAsync(
                WorkerSessionId,
                new UpdateProductRequest(
                    draftResult.ProductId,
                    "черновики",
                    "45к будущая открытка",
                    FilesToUpsert: [new ProductFileUpload("card_1.pdf", "print-ready"u8.ToArray())]
                )
            );

            updated.IsValid.ShouldBeTrue();
            updated.ValidationErrors.ShouldBeEmpty();
            updated.CategoryId.ShouldNotBeNull();
            updated.FormatId.ShouldNotBeNull();
            updated.AddedOrUpdatedFilesCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task CreateProductAsync_WhenDuplicateOrInvalidInputOrCapacityExceeded_ShouldThrowProductManagementException()
    {
        await using var dbContext = CreateDbContext();
        var service = CreateManagementService(dbContext);

        // 1. Артикул без пробела
        var invalidArticleEx = await Should.ThrowAsync<ProductManagementException>(() =>
            service.CreateProductAsync(
                WorkerSessionId,
                new CreateProductRequest("тег", "артикулбезпробела")
            )
        );
        invalidArticleEx.ErrorCode.ShouldBe(ProductManagementErrorCode.InvalidArticle);

        // 2. Невалидный первичный тег
        var invalidTagEx = await Should.ThrowAsync<ProductManagementException>(() =>
            service.CreateProductAsync(
                WorkerSessionId,
                new CreateProductRequest("bad:tag", "45к товар")
            )
        );
        invalidTagEx.ErrorCode.ShouldBe(ProductManagementErrorCode.InvalidPrimaryTag);

        // 3. Заполнение тега до лимита (MaxProductsPerPrimaryTag = 5) и проверка коллизии/лимита
        for (var i = 1; i <= 5; i++)
        {
            await service.CreateProductAsync(
                WorkerSessionId,
                new CreateProductRequest("лимит тег", $"45к товар {i}")
            );
        }

        // Дубликат существующего артикула
        var duplicateEx = await Should.ThrowAsync<ProductManagementException>(() =>
            service.CreateProductAsync(
                WorkerSessionId,
                new CreateProductRequest("лимит тег", "  45К   ТОВАР 1 ")
            )
        );
        duplicateEx.ErrorCode.ShouldBe(ProductManagementErrorCode.TargetAlreadyExists);

        // Превышение вместимости первичного тега (6-й товар при лимите 5)
        var capacityEx = await Should.ThrowAsync<ProductManagementException>(() =>
            service.CreateProductAsync(
                WorkerSessionId,
                new CreateProductRequest("лимит тег", "45к товар 6")
            )
        );
        capacityEx.ErrorCode.ShouldBe(ProductManagementErrorCode.PrimaryTagCapacityExceeded);
    }

    [Fact]
    public async Task UpdateProductAsync_WhenRepairingInvalidSyncedProduct_ShouldRenameFoldersAndFilesRewriteProductIdAndMakeValid()
    {
        // Создаем на диске проблемную ситуацию:
        // 1) "45к дубликат" (валидный) и "45к  дубликат" (конфликт нормализации артикула + битый .typoerp_id + конфликт файлов + битая обложка)
        await _storage.CreateProductDirectoryAsync("тег", "45к дубликат");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к дубликат",
            "card_1.pdf",
            "pdf-ok"u8.ToArray()
        );

        await _storage.CreateProductDirectoryAsync("тег", "45к  дубликат");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к  дубликат",
            ".typoerp_id",
            Encoding.UTF8.GetBytes("broken-guid-content")
        );
        await _storage.WriteProductFileAsync(
            "тег",
            "45к  дубликат",
            "card 1.pdf",
            "pdf-page-1"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "тег",
            "45к  дубликат",
            "card  1.pdf",
            "pdf-page-2"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "тег",
            "45к  дубликат",
            "cover_bad.pdf",
            "wrong-cover-ext"u8.ToArray()
        );

        Guid brokenProductId;
        await using (var syncDb = CreateDbContext())
        {
            var syncService = new ProductSyncService(
                syncDb,
                _storage,
                _lockService,
                _options,
                _timeProvider
            );
            await syncService.SynchronizeAsync(WorkerSessionId);

            var brokenProduct = await syncDb.Products.SingleAsync(p =>
                p.Article == "45к  дубликат"
            );
            brokenProduct.IsValid.ShouldBeFalse();
            brokenProduct.ValidationErrors.ShouldContain(
                ProductValidationError.NormalizationConflict
            );
            brokenProduct.ValidationErrors.ShouldContain(
                ProductValidationError.InvalidProductIdFile
            );
            brokenProduct.ValidationErrors.ShouldContain(
                ProductValidationError.InvalidCoverExtension
            );
            brokenProductId = brokenProduct.Id;
        }

        // Чиним проблемный товар через ProductManagementService.UpdateProductAsync:
        // - переименовываем артикул в "а4 исправленный товар" и переносим в "новый тег"
        // - удаляем битую обложку "cover_bad.pdf"
        // - переименовываем "card  1.pdf" -> "card_2.pdf" и "card 1.pdf" -> "card_1.pdf"
        // - загружаем валидную обложку "cover_1.png"
        _timeProvider.Advance(TimeSpan.FromMinutes(15));

        await using (var repairDb = CreateDbContext())
        {
            var service = CreateManagementService(repairDb);
            var repairResult = await service.UpdateProductAsync(
                WorkerSessionId,
                new UpdateProductRequest(
                    brokenProductId,
                    "  Новый   Тег ",
                    "  А4   Исправленный   Товар ",
                    FilesToDelete: ["cover_bad.pdf"],
                    FileRenames:
                    [
                        new ProductFileRenameInput("card  1.pdf", "  CARD_2.PDF "),
                        new ProductFileRenameInput("card 1.pdf", "card_1.pdf"),
                    ],
                    FilesToUpsert: [new ProductFileUpload("cover_1.png", "valid-png"u8.ToArray())]
                )
            );

            repairResult.IsValid.ShouldBeTrue();
            repairResult.ValidationErrors.ShouldBeEmpty();
            repairResult.DeletedFilesCount.ShouldBe(1);
            repairResult.AddedOrUpdatedFilesCount.ShouldBe(1);
        }

        // Проверяем, что на диске старая папка перемещена, .typoerp_id починен, файлы переименованы
        (await _storage.ProductDirectoryExistsAsync("тег", "45к  дубликат")).ShouldBeFalse();
        (
            await _storage.ProductDirectoryExistsAsync("новый тег", "а4 исправленный товар")
        ).ShouldBeTrue();

        var repairedIdOnDisk = await _storage.ReadProductIdAsync(
            "новый тег",
            "а4 исправленный товар"
        );
        repairedIdOnDisk.Status.ShouldBe(ProductIdReadStatus.Found);
        repairedIdOnDisk.ProductId.ShouldBe(brokenProductId);

        var repairedFilesOnDisk = await _storage.GetProductFilesAsync(
            "новый тег",
            "а4 исправленный товар"
        );
        repairedFilesOnDisk
            .Select(f => f.FileName)
            .ShouldBe([".typoerp_id", "card_1.pdf", "card_2.pdf", "cover_1.png"]);

        await using (var verifyDb = CreateDbContext())
        {
            var product = await verifyDb
                .Products.Include(p => p.PrimaryTag)
                .Include(p => p.Category)
                .Include(p => p.Format)
                .Include(p => p.Files)
                .SingleAsync(p => p.Id == brokenProductId);

            product.IsValid.ShouldBeTrue();
            product.ValidationErrors.ShouldBeEmpty();
            product.PrimaryTag!.Name.ShouldBe("новый тег");
            product.Article.ShouldBe("а4 исправленный товар");
            product.Category!.Code.ShouldBe("card");
            product.Format!.Code.ShouldBe("а4");
            product.Files.Count.ShouldBe(3);
        }
    }

    [Fact]
    public async Task UpdateProductAsync_WhenDeletingFileAndRenamingAnotherFileToDeletedName_ShouldSucceedWithoutDuplicateKeyConflict()
    {
        await _storage.CreateProductDirectoryAsync("тег", "45к замена файла");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к замена файла",
            "card 1.pdf",
            "old-version"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "тег",
            "45к замена файла",
            "card  1.pdf",
            "new-version"u8.ToArray()
        );

        Guid productId;
        await using (var syncDb = CreateDbContext())
        {
            var syncService = new ProductSyncService(
                syncDb,
                _storage,
                _lockService,
                _options,
                _timeProvider
            );
            await syncService.SynchronizeAsync(WorkerSessionId);
            productId = (await syncDb.Products.SingleAsync()).Id;
        }

        await using (var updateDb = CreateDbContext())
        {
            var service = CreateManagementService(updateDb);
            var result = await service.UpdateProductAsync(
                WorkerSessionId,
                new UpdateProductRequest(
                    productId,
                    "тег",
                    "45к замена файла",
                    FilesToDelete: ["card 1.pdf"],
                    FileRenames: [new ProductFileRenameInput("card  1.pdf", "card 1.pdf")]
                )
            );

            result.IsValid.ShouldBeTrue();
            result.DeletedFilesCount.ShouldBe(1);
        }

        await using (var verifyDb = CreateDbContext())
        {
            var files = await verifyDb
                .ProductFiles.Where(f => f.ProductId == productId)
                .ToListAsync();
            var singleFile = files.ShouldHaveSingleItem();
            singleFile.FileName.ShouldBe("card 1.pdf");
            singleFile.XxHash64.ShouldBe(ProductFileHasher.Compute("new-version"u8).HashHex);
        }
    }

    [Fact]
    public async Task UpdateProductAsync_ShouldPerformXxHash64DiffAndSkipUnchangedFiles()
    {
        Guid productId;
        await using (var createDb = CreateDbContext())
        {
            var service = CreateManagementService(createDb);
            var created = await service.CreateProductAsync(
                WorkerSessionId,
                new CreateProductRequest(
                    "тег",
                    "45к товар",
                    [
                        new ProductFileUpload("cover_1.jpg", "same-cover-bytes"u8.ToArray()),
                        new ProductFileUpload("card_1.pdf", "pdf-v1"u8.ToArray()),
                    ]
                )
            );
            productId = created.ProductId;
        }

        var updateTime = FixedNow.AddMinutes(30);
        _timeProvider.Advance(TimeSpan.FromMinutes(30));

        await using (var updateDb = CreateDbContext())
        {
            var service = CreateManagementService(updateDb);
            var updateResult = await service.UpdateProductAsync(
                WorkerSessionId,
                new UpdateProductRequest(
                    productId,
                    "тег",
                    "45к товар",
                    FilesToUpsert:
                    [
                        new ProductFileUpload("COVER_1.JPG", "same-cover-bytes"u8.ToArray()),
                        new ProductFileUpload("card_1.pdf", "pdf-v2-modified"u8.ToArray()),
                        new ProductFileUpload("card_2.pdf", "pdf-page-2"u8.ToArray()),
                    ]
                )
            );

            updateResult.UnchangedFilesCount.ShouldBe(1);
            updateResult.AddedOrUpdatedFilesCount.ShouldBe(2);
        }

        await using (var verifyDb = CreateDbContext())
        {
            var files = await verifyDb
                .ProductFiles.Where(f => f.ProductId == productId)
                .ToListAsync();
            files.Count.ShouldBe(3);

            var cover = files.Single(f => f.FileName == "cover_1.jpg");
            cover.UpdatedAtUtc.ShouldBe(FixedNow); // Не перезаписывался

            var card1 = files.Single(f => f.FileName == "card_1.pdf");
            card1.UpdatedAtUtc.ShouldBe(updateTime);
            card1.XxHash64.ShouldBe(ProductFileHasher.Compute("pdf-v2-modified"u8).HashHex);

            var card2 = files.Single(f => f.FileName == "card_2.pdf");
            card2.UpdatedAtUtc.ShouldBe(updateTime);
        }
    }

    [Fact]
    public async Task DeleteProductAsync_ShouldDeleteFromStorageSoftDeleteUnusedCategoryAndFormatAndRestoreWhenReused()
    {
        Guid productId;
        Guid categoryId;
        Guid formatId;

        await using (var db1 = CreateDbContext())
        {
            var service = CreateManagementService(db1);
            var created = await service.CreateProductAsync(
                WorkerSessionId,
                new CreateProductRequest(
                    "тег",
                    "45к открытка",
                    [new ProductFileUpload("card_1.pdf", "pdf-data"u8.ToArray())]
                )
            );

            productId = created.ProductId;
            categoryId = created.CategoryId!.Value;
            formatId = created.FormatId!.Value;

            await service.UpdateCategoryDisplayNameAsync(
                WorkerSessionId,
                categoryId,
                "Открытки подарочные"
            );
            await service.SetCategorySecondaryTagsAsync(
                WorkerSessionId,
                categoryId,
                ["Плотный картон"]
            );
            await service.SetFormatSecondaryTagsAsync(
                WorkerSessionId,
                formatId,
                ["Карманный формат"]
            );
        }

        // Удаляем единственный товар этой категории и формата
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        await using (var db2 = CreateDbContext())
        {
            var service = CreateManagementService(db2);
            var deleted = await service.DeleteProductAsync(WorkerSessionId, productId);
            deleted.ShouldBeTrue();
        }

        (await _storage.ProductDirectoryExistsAsync("тег", "45к открытка")).ShouldBeFalse();

        await using (var checkDb = CreateDbContext())
        {
            (await checkDb.Products.CountAsync()).ShouldBe(0);

            var category = await checkDb.Categories.SingleAsync(c => c.Id == categoryId);
            var format = await checkDb.Formats.SingleAsync(f => f.Id == formatId);

            category.IsDeleted.ShouldBeTrue();
            format.IsDeleted.ShouldBeTrue();
        }

        // Создаем новый товар с тем же префиксом "card" и форматом "45к" — категория и формат восстанавливаются со старым DisplayName и тегами
        _timeProvider.Advance(TimeSpan.FromMinutes(10));
        await using (var db3 = CreateDbContext())
        {
            var service = CreateManagementService(db3);
            var recreated = await service.CreateProductAsync(
                WorkerSessionId,
                new CreateProductRequest(
                    "тег",
                    "45к новая открытка",
                    [new ProductFileUpload("card_new.pdf", "pdf-new"u8.ToArray())]
                )
            );

            recreated.CategoryId.ShouldBe(categoryId);
            recreated.FormatId.ShouldBe(formatId);
        }

        await using (var verifyRestoredDb = CreateDbContext())
        {
            var category = await verifyRestoredDb
                .Categories.Include(c => c.SecondaryTags)
                .SingleAsync(c => c.Id == categoryId);
            var format = await verifyRestoredDb
                .Formats.Include(f => f.SecondaryTags)
                .SingleAsync(f => f.Id == formatId);

            category.IsDeleted.ShouldBeFalse();
            category.DisplayName.ShouldBe("Открытки подарочные");
            category.SecondaryTags.ShouldHaveSingleItem().Name.ShouldBe("плотный картон");

            format.IsDeleted.ShouldBeFalse();
            format.SecondaryTags.ShouldHaveSingleItem().Name.ShouldBe("карманный формат");
        }
    }

    [Fact]
    public async Task BulkCrudOperations_ShouldMoveProductsUpdateSecondaryTagsAndBulkDelete()
    {
        Guid p1Id;
        Guid p2Id;
        Guid p3Id;

        await using (var seedDb = CreateDbContext())
        {
            var service = CreateManagementService(seedDb);

            p1Id = (
                await service.CreateProductAsync(
                    WorkerSessionId,
                    new CreateProductRequest(
                        "исходный тег",
                        "45к товар 1",
                        [new ProductFileUpload("card_1.pdf", "1"u8.ToArray())],
                        ["стартовый"]
                    )
                )
            ).ProductId;

            p2Id = (
                await service.CreateProductAsync(
                    WorkerSessionId,
                    new CreateProductRequest(
                        "исходный тег",
                        "45к товар 2",
                        [new ProductFileUpload("card_1.pdf", "2"u8.ToArray())],
                        ["стартовый"]
                    )
                )
            ).ProductId;

            p3Id = (
                await service.CreateProductAsync(
                    WorkerSessionId,
                    new CreateProductRequest(
                        "исходный тег",
                        "а3 постер 3",
                        [new ProductFileUpload("poster_1.pdf", "3"u8.ToArray())]
                    )
                )
            ).ProductId;
        }

        // 1. Bulk Move: переносим p1 и p2 в "целевой тег"
        await using (var moveDb = CreateDbContext())
        {
            var service = CreateManagementService(moveDb);
            var moveResult = await service.BulkMoveProductsAsync(
                WorkerSessionId,
                [p1Id, p2Id],
                "  Целевой   Тег "
            );

            moveResult.MovedProductsCount.ShouldBe(2);
            moveResult.NormalizedTargetPrimaryTag.ShouldBe("целевой тег");
        }

        (await _storage.ProductDirectoryExistsAsync("исходный тег", "45к товар 1")).ShouldBeFalse();
        (await _storage.ProductDirectoryExistsAsync("целевой тег", "45к товар 1")).ShouldBeTrue();
        (await _storage.ProductDirectoryExistsAsync("целевой тег", "45к товар 2")).ShouldBeTrue();

        // 2. Bulk Secondary Tags: Add, Remove, Replace
        await using (var tagsDb = CreateDbContext())
        {
            var service = CreateManagementService(tagsDb);

            await service.BulkUpdateProductSecondaryTagsAsync(
                WorkerSessionId,
                [p1Id, p2Id],
                ["  Акция ", "Хит"],
                BulkSecondaryTagOperationMode.Add
            );

            await service.BulkUpdateProductSecondaryTagsAsync(
                WorkerSessionId,
                [p1Id],
                ["стартовый"],
                BulkSecondaryTagOperationMode.Remove
            );

            await service.BulkUpdateProductSecondaryTagsAsync(
                WorkerSessionId,
                [p2Id],
                ["только этот тег"],
                BulkSecondaryTagOperationMode.Replace
            );
        }

        await using (var verifyTagsDb = CreateDbContext())
        {
            var p1 = await verifyTagsDb
                .Products.Include(p => p.SecondaryTags)
                .SingleAsync(p => p.Id == p1Id);
            var p2 = await verifyTagsDb
                .Products.Include(p => p.SecondaryTags)
                .SingleAsync(p => p.Id == p2Id);

            p1.SecondaryTags.Select(t => t.Name).OrderBy(n => n).ShouldBe(["акция", "хит"]);
            p2.SecondaryTags.Select(t => t.Name).ShouldBe(["только этот тег"]);
        }

        // 3. Bulk Delete: удаляем p1 и p2 (категория "card" должна уйти в SoftDelete, а "poster" у p3 остаться активной)
        await using (var deleteDb = CreateDbContext())
        {
            var service = CreateManagementService(deleteDb);
            var deletedCount = await service.BulkDeleteProductsAsync(WorkerSessionId, [p1Id, p2Id]);
            deletedCount.ShouldBe(2);
        }

        (await _storage.ProductDirectoryExistsAsync("целевой тег", "45к товар 1")).ShouldBeFalse();
        (await _storage.ProductDirectoryExistsAsync("целевой тег", "45к товар 2")).ShouldBeFalse();
        (await _storage.ProductDirectoryExistsAsync("исходный тег", "а3 постер 3")).ShouldBeTrue();

        await using (var finalDb = CreateDbContext())
        {
            (await finalDb.Products.CountAsync()).ShouldBe(1);
            var cardCategory = await finalDb.Categories.SingleAsync(c => c.Code == "card");
            var posterCategory = await finalDb.Categories.SingleAsync(c => c.Code == "poster");

            cardCategory.IsDeleted.ShouldBeTrue();
            posterCategory.IsDeleted.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task RenamePrimaryTagAndSecondaryTagManagement_ShouldUpdateEntitiesAndRevalidateProducts()
    {
        // Создаем на диске первичный тег с длиной > MaxPrimaryTagLength, синхронизируем, затем чиним через RenamePrimaryTagAsync
        var tooLongTagName = new string('т', 205);
        await _storage.CreateProductDirectoryAsync(tooLongTagName, "45к товар в длинном теге");
        await _storage.WriteProductFileAsync(
            tooLongTagName,
            "45к товар в длинном теге",
            "card_1.pdf",
            "pdf"u8.ToArray()
        );

        Guid primaryTagId;
        Guid productId;
        await using (var syncDb = CreateDbContext())
        {
            var syncService = new ProductSyncService(
                syncDb,
                _storage,
                _lockService,
                _options,
                _timeProvider
            );
            await syncService.SynchronizeAsync(WorkerSessionId);

            var product = await syncDb.Products.Include(p => p.PrimaryTag).SingleAsync();
            product.IsValid.ShouldBeFalse();
            product.ValidationErrors.ShouldContain(ProductValidationError.PrimaryTagTooLong);
            primaryTagId = product.PrimaryTagId!.Value;
            productId = product.Id;
        }

        await using (var manageDb = CreateDbContext())
        {
            var service = CreateManagementService(manageDb);

            await service.RenamePrimaryTagAsync(WorkerSessionId, primaryTagId, "  Короткий   Тег ");

            var tag = await service.GetOrCreateSecondaryTagAsync(WorkerSessionId, "  Сезон   1 ");
            await service.SetProductSecondaryTagsAsync(WorkerSessionId, productId, ["сезон 1"]);
            await service.RenameSecondaryTagAsync(WorkerSessionId, tag.Id, "  Сезон   2 ");
        }

        (
            await _storage.ProductDirectoryExistsAsync("короткий тег", "45к товар в длинном теге")
        ).ShouldBeTrue();

        await using (var verifyDb = CreateDbContext())
        {
            var product = await verifyDb
                .Products.Include(p => p.PrimaryTag)
                .Include(p => p.SecondaryTags)
                .SingleAsync(p => p.Id == productId);

            product.IsValid.ShouldBeTrue();
            product.ValidationErrors.ShouldBeEmpty();
            product.PrimaryTag!.Name.ShouldBe("короткий тег");
            product.SecondaryTags.ShouldHaveSingleItem().Name.ShouldBe("сезон 2");
        }
    }

    private ProductsDbContext CreateDbContext() => new(_dbOptions);

    private ProductManagementService CreateManagementService(ProductsDbContext dbContext) =>
        new(dbContext, _storage, _lockService, _options, _timeProvider);

    private sealed class FakeTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
