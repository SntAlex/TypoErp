using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Locking;
using TypoErp.Modules.Products.Management;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Sync;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Storage;

public sealed class LocalDiskProductStorageTests : IDisposable
{
    private readonly ProductsStorageOptions _options;
    private readonly string _tempRootPath;

    public LocalDiskProductStorageTests()
    {
        _tempRootPath = Path.Combine(
            Path.GetTempPath(),
            $"TypoErp_LocalDiskTests_{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(_tempRootPath);

        _options = new ProductsStorageOptions
        {
            RootPath = _tempRootPath,
            MaxProductsPerPrimaryTag = 50,
        };
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRootPath))
        {
            try
            {
                Directory.Delete(_tempRootPath, recursive: true);
            }
            catch
            {
                // Игнорируем ошибки очистки временной папки ОС
            }
        }
    }

    [Fact]
    public async Task IsRootAccessibleAsync_WhenDirectoryExistsOrMissing_ShouldReflectStateAndThrowWhenMissing()
    {
        var validStorage = new LocalDiskProductStorage(_options);
        (await validStorage.IsRootAccessibleAsync()).ShouldBeTrue();

        var missingOptions = new ProductsStorageOptions
        {
            RootPath = Path.Combine(_tempRootPath, "non_existent_root"),
        };
        var missingStorage = new LocalDiskProductStorage(missingOptions);

        (await missingStorage.IsRootAccessibleAsync()).ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            missingStorage.GetPrimaryTagDirectoriesAsync()
        );
    }

    [Fact]
    public async Task PrimaryTagAndRootFileOperations_ShouldCreateListCheckExistenceAndDeleteOnRealDisk()
    {
        var storage = new LocalDiskProductStorage(_options);

        await File.WriteAllBytesAsync(
            Path.Combine(_tempRootPath, "stray_root.pdf"),
            "abc"u8.ToArray()
        );
        var rootFiles = await storage.GetRootFileNamesAsync();
        rootFiles.ShouldBe(["stray_root.pdf"]);

        await storage.CreatePrimaryTagDirectoryAsync("  Мой   Тег ");
        await storage.CreatePrimaryTagDirectoryAsync("аниме наклейки");

        Directory.Exists(Path.Combine(_tempRootPath, "  Мой   Тег ")).ShouldBeTrue();
        Directory.Exists(Path.Combine(_tempRootPath, "аниме наклейки")).ShouldBeTrue();

        (await storage.PrimaryTagDirectoryExistsAsync("  Мой   Тег ")).ShouldBeTrue();
        (await storage.PrimaryTagDirectoryExistsAsync("  мой   тег ")).ShouldBeTrue();

        var tags = await storage.GetPrimaryTagDirectoriesAsync();
        tags.ShouldContain("  Мой   Тег ");
        tags.ShouldContain("аниме наклейки");

        var deleted = await storage.DeletePrimaryTagDirectoryAsync("аниме наклейки");
        deleted.ShouldBeTrue();
        (await storage.PrimaryTagDirectoryExistsAsync("аниме наклейки")).ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAndWriteOperations_WhenCalledWithDifferentCasing_ShouldNotCreateCaseDuplicatesOnDisk()
    {
        var storage = new LocalDiskProductStorage(_options);

        await storage.CreatePrimaryTagDirectoryAsync("МОЙ ТЕГ");
        await storage.CreatePrimaryTagDirectoryAsync("мой тег");

        var tags = await storage.GetPrimaryTagDirectoriesAsync();
        tags.ShouldHaveSingleItem().ShouldBe("МОЙ ТЕГ");

        await storage.CreateProductDirectoryAsync("мой тег", "45К АРТИКУЛ");
        await storage.CreateProductDirectoryAsync("МОЙ ТЕГ", "45к артикул");

        var products = await storage.GetProductDirectoriesAsync("мой тег");
        products.ShouldHaveSingleItem().ShouldBe("45К АРТИКУЛ");

        await storage.WriteProductFileAsync(
            "мой тег",
            "45к артикул",
            "CARD_1.PDF",
            "v1"u8.ToArray()
        );
        await storage.WriteProductFileAsync(
            "мой тег",
            "45к артикул",
            "card_1.pdf",
            "v2-updated"u8.ToArray()
        );

        var files = await storage.GetProductFilesAsync("мой тег", "45к артикул");
        files.ShouldHaveSingleItem().SizeBytes.ShouldBe("v2-updated"u8.Length);
    }

    [Fact]
    public async Task RenamePrimaryTagDirectoryAsync_WhenChangingCaseOnly_ShouldPerformTwoStepRenameOnRealDisk()
    {
        var storage = new LocalDiskProductStorage(_options);
        await storage.CreatePrimaryTagDirectoryAsync("МОЙ ТЕГ");
        await storage.CreateProductDirectoryAsync("МОЙ ТЕГ", "45к артикул");
        await storage.WriteProductFileAsync(
            "МОЙ ТЕГ",
            "45к артикул",
            "card_1.pdf",
            "payload"u8.ToArray()
        );

        var result = await storage.RenamePrimaryTagDirectoryAsync("МОЙ ТЕГ", "мой тег");

        result.IsSuccess.ShouldBeTrue();
        result.Status.ShouldBe(StorageRenameStatus.Success);
        result.FinalName.ShouldBe("мой тег");

        var tags = await storage.GetPrimaryTagDirectoriesAsync();
        tags.ShouldHaveSingleItem().ShouldBe("мой тег");
        (await storage.ProductDirectoryExistsAsync("мой тег", "45к артикул")).ShouldBeTrue();

        var files = await storage.GetProductFilesAsync("мой тег", "45к артикул");
        files.ShouldHaveSingleItem().FileName.ShouldBe("card_1.pdf");
    }

    [Fact]
    public async Task RenamePrimaryTagDirectoryAsync_WhenAnotherFolderNormalizesToSameName_ShouldReturnTargetCollision()
    {
        var storage = new LocalDiskProductStorage(_options);
        await storage.CreatePrimaryTagDirectoryAsync("мой тег");
        await storage.CreatePrimaryTagDirectoryAsync("мой  тег");

        var result = await storage.RenamePrimaryTagDirectoryAsync("мой  тег", "мой тег");

        result.IsSuccess.ShouldBeFalse();
        result.IsConflict.ShouldBeTrue();
        result.Status.ShouldBe(StorageRenameStatus.TargetCollision);

        var tags = await storage.GetPrimaryTagDirectoriesAsync();
        tags.Count.ShouldBe(2);
    }

    [Fact]
    public async Task RenameProductDirectoryAsync_ShouldSupportCaseOnlyRenameAndDetectCollisionsOnDisk()
    {
        var storage = new LocalDiskProductStorage(_options);
        await storage.CreateProductDirectoryAsync("тег", "45К АРТИКУЛ 1");
        await storage.CreateProductDirectoryAsync("тег", "45к  артикул 1");

        var caseRename = await storage.RenameProductDirectoryAsync(
            "тег",
            "45К АРТИКУЛ 1",
            "45к артикул 1"
        );
        caseRename.Status.ShouldBe(StorageRenameStatus.Success);
        caseRename.FinalName.ShouldBe("45к артикул 1");

        var productsAfterCaseRename = await storage.GetProductDirectoriesAsync("тег");
        productsAfterCaseRename.ShouldContain("45к артикул 1");

        var collisionRename = await storage.RenameProductDirectoryAsync(
            "тег",
            "45к  артикул 1",
            "45к артикул 1"
        );
        collisionRename.Status.ShouldBe(StorageRenameStatus.TargetCollision);
    }

    [Fact]
    public async Task MoveProductDirectoryAsync_ShouldMoveBetweenPrimaryTagsAndDetectTargetCollisionOnDisk()
    {
        var storage = new LocalDiskProductStorage(_options);
        var productId = Guid.NewGuid();

        await storage.CreateProductDirectoryAsync("тег 1", "45к товар 1");
        await storage.WriteProductIdAsync("тег 1", "45к товар 1", productId);
        await storage.WriteProductFileAsync(
            "тег 1",
            "45к товар 1",
            "card_1.pdf",
            "pdf-data"u8.ToArray()
        );

        var moveResult = await storage.MoveProductDirectoryAsync(
            "тег 1",
            "45к товар 1",
            "тег 2",
            "45к товар 1 новый"
        );

        moveResult.IsSuccess.ShouldBeTrue();
        (await storage.ProductDirectoryExistsAsync("тег 1", "45к товар 1")).ShouldBeFalse();
        (await storage.ProductDirectoryExistsAsync("тег 2", "45к товар 1 новый")).ShouldBeTrue();

        var readId = await storage.ReadProductIdAsync("тег 2", "45к товар 1 новый");
        readId.Status.ShouldBe(ProductIdReadStatus.Found);
        readId.ProductId.ShouldBe(productId);

        await storage.CreateProductDirectoryAsync("тег 1", "45к дубликат");
        var collisionMove = await storage.MoveProductDirectoryAsync(
            "тег 1",
            "45к дубликат",
            "тег 2",
            "45К ТОВАР 1 НОВЫЙ"
        );
        collisionMove.Status.ShouldBe(StorageRenameStatus.TargetCollision);
    }

    [Fact]
    public async Task ReadAndWriteProductIdAsync_ShouldHandleFoundNotFoundAndInvalidContentOnDisk()
    {
        var storage = new LocalDiskProductStorage(_options);
        await storage.CreateProductDirectoryAsync("тег", "45к товар");

        var notFound = await storage.ReadProductIdAsync("тег", "45к товар");
        notFound.Status.ShouldBe(ProductIdReadStatus.NotFound);
        notFound.ProductId.ShouldBeNull();

        var expectedId = Guid.NewGuid();
        await storage.WriteProductIdAsync("тег", "45к товар", expectedId);

        var found = await storage.ReadProductIdAsync("тег", "45к товар");
        found.Status.ShouldBe(ProductIdReadStatus.Found);
        found.ProductId.ShouldBe(expectedId);

        await storage.WriteProductFileAsync(
            "тег",
            "45к товар",
            ".typoerp_id",
            Encoding.UTF8.GetBytes("corrupted-guid-value")
        );

        var invalid = await storage.ReadProductIdAsync("тег", "45к товар");
        invalid.Status.ShouldBe(ProductIdReadStatus.InvalidContent);
        invalid.ProductId.ShouldBeNull();
    }

    [Fact]
    public async Task ProductFileOperations_ShouldStreamXxHash64SupportCaseOnlyRenameAndDetectCollisionsOnDisk()
    {
        var storage = new LocalDiskProductStorage(_options);
        await storage.CreateProductDirectoryAsync("тег", "45к товар");

        // Создаем поток > 64 КБ для проверки потокового XxHash64
        var payload = new byte[150 * 1024];
        Random.Shared.NextBytes(payload);
        var expectedHash = ProductFileHasher.Compute(payload);

        using var inputStream = new MemoryStream(payload);
        var writtenInfo = await storage.WriteProductFileAsync(
            "тег",
            "45к товар",
            "CARD_1.PDF",
            inputStream
        );

        writtenInfo.FileName.ShouldBe("CARD_1.PDF");
        writtenInfo.SizeBytes.ShouldBe(payload.Length);
        writtenInfo.XxHash64.ShouldBe(expectedHash.HashHex);

        // Двухшаговое переименование файла со сменой только регистра (CARD_1.PDF -> card_1.pdf)
        var renameCaseOnly = await storage.RenameProductFileAsync(
            "тег",
            "45к товар",
            "CARD_1.PDF",
            "card_1.pdf"
        );
        renameCaseOnly.Status.ShouldBe(StorageRenameStatus.Success);
        renameCaseOnly.FinalName.ShouldBe("card_1.pdf");

        var fileInfoAfterRename = await storage.GetProductFileInfoAsync(
            "тег",
            "45к товар",
            "card_1.pdf"
        );
        fileInfoAfterRename.ShouldNotBeNull();
        fileInfoAfterRename.FileName.ShouldBe("card_1.pdf");
        fileInfoAfterRename.SizeBytes.ShouldBe(payload.Length);
        fileInfoAfterRename.XxHash64.ShouldBe(expectedHash.HashHex);

        // Проверка коллизии при переименовании в существующий файл
        await storage.WriteProductFileAsync("тег", "45к товар", "card  2.pdf", "a"u8.ToArray());
        await storage.WriteProductFileAsync("тег", "45к товар", "card 2.pdf", "b"u8.ToArray());

        var fileCollision = await storage.RenameProductFileAsync(
            "тег",
            "45к товар",
            "card  2.pdf",
            "card 2.pdf"
        );
        fileCollision.Status.ShouldBe(StorageRenameStatus.TargetCollision);

        // Чтение через поток
        await using (
            var readStream = await storage.OpenReadProductFileAsync(
                "тег",
                "45к товар",
                "card_1.pdf"
            )
        )
        {
            using var ms = new MemoryStream();
            await readStream.CopyToAsync(ms);
            ms.ToArray().ShouldBe(payload);
        }

        // Удаление файла
        var deleted = await storage.DeleteProductFileAsync("тег", "45к товар", "card  2.pdf");
        deleted.ShouldBeTrue();
        (await storage.GetProductFileInfoAsync("тег", "45к товар", "card  2.pdf")).ShouldBeNull();
    }

    [Fact]
    public async Task EndToEndIntegration_WithProductSyncAndManagementServices_ShouldWorkOnRealFileSystem()
    {
        var storage = new LocalDiskProductStorage(_options);
        var lockService = new ProductsWorkspaceLockService();
        const string sessionId = "local-disk-session";
        lockService.TryAcquire(sessionId, Guid.NewGuid(), "Инженер Типографии");

        // Создаем на реальном диске ненормализованные папки и файлы напрямую через System.IO
        var rawTagDir = Directory.CreateDirectory(
            Path.Combine(_tempRootPath, "  Аниме   Наклейки ")
        );
        var rawProductDir = Directory.CreateDirectory(
            Path.Combine(rawTagDir.FullName, "  45К   Стикерпак   Геншин ")
        );
        await File.WriteAllBytesAsync(
            Path.Combine(rawProductDir.FullName, "  COVER_1.JPG "),
            "jpg-bytes"u8.ToArray()
        );
        await File.WriteAllBytesAsync(
            Path.Combine(rawProductDir.FullName, "  STICKER_sheet_1.PDF "),
            "pdf-bytes"u8.ToArray()
        );

        await using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var dbOptions = new DbContextOptionsBuilder<ProductsDbContext>()
            .UseSqlite(connection)
            .Options;

        await using (var initDb = new ProductsDbContext(dbOptions))
        {
            await initDb.Database.EnsureCreatedAsync();
        }

        Guid syncedProductId;
        await using (var syncDb = new ProductsDbContext(dbOptions))
        {
            var syncService = new ProductSyncService(syncDb, storage, lockService, _options);
            var syncResult = await syncService.SynchronizeAsync(sessionId);

            syncResult.TotalProducts.ShouldBe(1);
            syncResult.ValidProducts.ShouldBe(1);
            syncResult.AutoNormalizedItemsCount.ShouldBe(4); // тег + артикул + 2 файла

            var product = await syncDb
                .Products.Include(p => p.PrimaryTag)
                .Include(p => p.Category)
                .Include(p => p.Format)
                .SingleAsync();

            product.IsValid.ShouldBeTrue();
            product.PrimaryTag!.Name.ShouldBe("аниме наклейки");
            product.Article.ShouldBe("45к стикерпак геншин");
            product.Category!.Code.ShouldBe("sticker");
            product.Format!.Code.ShouldBe("45к");
            syncedProductId = product.Id;
        }

        // Проверяем, что на реальной ФС папки переименованы и создан .typoerp_id
        var normalizedProductPath = Path.Combine(
            _tempRootPath,
            "аниме наклейки",
            "45к стикерпак геншин"
        );
        Directory.Exists(normalizedProductPath).ShouldBeTrue();
        File.Exists(Path.Combine(normalizedProductPath, ".typoerp_id")).ShouldBeTrue();
        File.Exists(Path.Combine(normalizedProductPath, "cover_1.jpg")).ShouldBeTrue();
        File.Exists(Path.Combine(normalizedProductPath, "sticker_sheet_1.pdf")).ShouldBeTrue();

        // Обновляем товар через ProductManagementService на реальном диске
        await using (var manageDb = new ProductsDbContext(dbOptions))
        {
            var managementService = new ProductManagementService(
                manageDb,
                storage,
                lockService,
                _options
            );
            var updateResult = await managementService.UpdateProductAsync(
                sessionId,
                new UpdateProductRequest(
                    syncedProductId,
                    "аниме наклейки",
                    "а5 стикерпак геншин обновленный",
                    FilesToUpsert:
                    [
                        new ProductFileUpload("sticker_sheet_2.pdf", "pdf-page-2"u8.ToArray()),
                    ]
                )
            );

            updateResult.IsValid.ShouldBeTrue();
            updateResult.NormalizedArticle.ShouldBe("а5 стикерпак геншин обновленный");
            updateResult.AddedOrUpdatedFilesCount.ShouldBe(1);
        }

        var movedProductPath = Path.Combine(
            _tempRootPath,
            "аниме наклейки",
            "а5 стикерпак геншин обновленный"
        );
        Directory.Exists(movedProductPath).ShouldBeTrue();
        File.Exists(Path.Combine(movedProductPath, "sticker_sheet_2.pdf")).ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrentOperations_WhenExecutedInParallelOnRealDisk_ShouldBeThreadSafe()
    {
        var storage = new LocalDiskProductStorage(_options);
        const int count = 30;

        var tasks = Enumerable
            .Range(1, count)
            .Select(async i =>
            {
                var tag = $"тег {i % 5}";
                var article = $"45к артикул {i}";
                await storage.CreateProductDirectoryAsync(tag, article);
                await storage.WriteProductIdAsync(tag, article, Guid.NewGuid());
                await storage.WriteProductFileAsync(
                    tag,
                    article,
                    $"card_{i}.pdf",
                    Encoding.UTF8.GetBytes($"payload-{i}")
                );
            });

        await Task.WhenAll(tasks);

        var allTags = await storage.GetPrimaryTagDirectoriesAsync();
        allTags.Count.ShouldBe(5);

        var totalProducts = 0;
        foreach (var tag in allTags)
        {
            var products = await storage.GetProductDirectoriesAsync(tag);
            totalProducts += products.Count;
        }

        totalProducts.ShouldBe(count);
    }
}
