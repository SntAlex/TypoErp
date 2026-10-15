using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Entities;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Locking;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Sync;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Tests.Sync;

public sealed class ProductSyncServiceTests : IAsyncLifetime
{
    private const string WorkerSessionId = "worker-session-1";
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 15, 0, 0, TimeSpan.Zero);

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

        _options = new ProductsStorageOptions { MaxProductsPerPrimaryTag = 50 };
        _timeProvider = new FakeTimeProvider(FixedNow);
        _storage = new FakeProductStorage(_options);
        _lockService = new ProductsWorkspaceLockService(_timeProvider);

        _lockService.TryAcquire(WorkerSessionId, Guid.NewGuid(), "Оператор Синхронизации");

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SynchronizeAsync_WhenSessionDoesNotHoldLock_ShouldThrowProductsWorkspaceLockException()
    {
        await using var dbContext = CreateDbContext();
        var syncService = CreateSyncService(dbContext);

        await Should.ThrowAsync<ProductsWorkspaceLockException>(() =>
            syncService.SynchronizeAsync("unregistered-session")
        );
    }

    [Fact]
    public async Task SynchronizeAsync_WhenStorageRootIsInaccessible_ShouldThrowInvalidOperationException()
    {
        _storage.IsRootAccessible = false;
        await using var dbContext = CreateDbContext();
        var syncService = CreateSyncService(dbContext);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            syncService.SynchronizeAsync(WorkerSessionId)
        );
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldAutoNormalizeFoldersAndFilesCreateProductIdAndDeriveCategoryAndFormat()
    {
        await _storage.CreateProductDirectoryAsync(
            "  Аниме   Тетради ",
            "  45К   Тетрадь   Наруто "
        );
        await _storage.WriteProductFileAsync(
            "  Аниме   Тетради ",
            "  45К   Тетрадь   Наруто ",
            "  Cover_1   (2).JPG ",
            "cover-bytes"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "  Аниме   Тетради ",
            "  45К   Тетрадь   Наруто ",
            "  VIDEO_1.MP4 ",
            "video-bytes"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "  Аниме   Тетради ",
            "  45К   Тетрадь   Наруто ",
            "  CARD_front_1.PDF ",
            "pdf-layout-bytes"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "  Аниме   Тетради ",
            "  45К   Тетрадь   Наруто ",
            "Thumbs.db",
            "os-cache"u8.ToArray()
        );

        await using (var dbContext = CreateDbContext())
        {
            var syncService = CreateSyncService(dbContext);
            var syncResult = await syncService.SynchronizeAsync(WorkerSessionId);

            syncResult.TotalProducts.ShouldBe(1);
            syncResult.ValidProducts.ShouldBe(1);
            syncResult.InvalidProducts.ShouldBe(0);
            syncResult.AddedProducts.ShouldBe(1);
            syncResult.AutoNormalizedItemsCount.ShouldBe(5); // 1 тег + 1 артикул + 3 файла
            syncResult.NormalizationConflictsCount.ShouldBe(0);
        }

        // Проверяем, что папки и файлы в хранилище реально переименованы в нормализованный вид
        var tagsOnDisk = await _storage.GetPrimaryTagDirectoriesAsync();
        tagsOnDisk.ShouldHaveSingleItem().ShouldBe("аниме тетради");

        var articlesOnDisk = await _storage.GetProductDirectoriesAsync("аниме тетради");
        articlesOnDisk.ShouldHaveSingleItem().ShouldBe("45к тетрадь наруто");

        var filesOnDisk = await _storage.GetProductFilesAsync(
            "аниме тетради",
            "45к тетрадь наруто"
        );
        var fileNamesOnDisk = filesOnDisk.Select(f => f.FileName).ToList();
        fileNamesOnDisk.ShouldContain("cover_1 (2).jpg");
        fileNamesOnDisk.ShouldContain("video_1.mp4");
        fileNamesOnDisk.ShouldContain("card_front_1.pdf");
        fileNamesOnDisk.ShouldContain(".typoerp_id");

        var idOnDisk = await _storage.ReadProductIdAsync("аниме тетради", "45к тетрадь наруто");
        idOnDisk.Status.ShouldBe(ProductIdReadStatus.Found);
        idOnDisk.ProductId.ShouldNotBeNull();

        await using (var verifyDb = CreateDbContext())
        {
            var product = await verifyDb
                .Products.Include(p => p.PrimaryTag)
                .Include(p => p.Category)
                .Include(p => p.Format)
                .Include(p => p.Files)
                .SingleAsync();

            product.Id.ShouldBe(idOnDisk.ProductId.Value);
            product.IsValid.ShouldBeTrue();
            product.ValidationErrors.ShouldBeEmpty();
            product.Article.ShouldBe("45к тетрадь наруто");
            product.NormalizedArticle.ShouldBe("45к тетрадь наруто");

            product.PrimaryTag.ShouldNotBeNull();
            product.PrimaryTag.Name.ShouldBe("аниме тетради");
            product.PrimaryTag.NormalizedName.ShouldBe("аниме тетради");

            product.Category.ShouldNotBeNull();
            product.Category.Code.ShouldBe("card");
            product.Category.IsDeleted.ShouldBeFalse();

            product.Format.ShouldNotBeNull();
            product.Format.Code.ShouldBe("45к");
            product.Format.CategoryId.ShouldBe(product.Category.Id);
            product.Format.IsDeleted.ShouldBeFalse();

            // Игнорируемые файлы (.typoerp_id и Thumbs.db) не должны попадать в product_files
            product.Files.Count.ShouldBe(3);
            product.Files.ShouldContain(f =>
                f.FileName == "cover_1 (2).jpg" && f.Kind == ProductFileKind.Cover
            );
            product.Files.ShouldContain(f =>
                f.FileName == "video_1.mp4" && f.Kind == ProductFileKind.VideoCover
            );
            product.Files.ShouldContain(f =>
                f.FileName == "card_front_1.pdf" && f.Kind == ProductFileKind.AssemblyFile
            );
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenNormalizationCollisionsOccurOnTagArticleOrFile_ShouldNotAbortAndRecordNormalizationConflict()
    {
        // 1. Коллизия первичных тегов: "мой тег" и "мой  тег"
        await _storage.CreateProductDirectoryAsync("мой тег", "45к валидный товар");
        await _storage.WriteProductFileAsync(
            "мой тег",
            "45к валидный товар",
            "card_1.pdf",
            "pdf-1"u8.ToArray()
        );

        await _storage.CreateProductDirectoryAsync("мой  тег", "45к товар в конфликтном теге");
        await _storage.WriteProductFileAsync(
            "мой  тег",
            "45к товар в конфликтном теге",
            "card_1.pdf",
            "pdf-2"u8.ToArray()
        );

        // 2. Коллизия артикулов внутри "мой тег": "45к дубликат" и "45к  дубликат"
        await _storage.CreateProductDirectoryAsync("мой тег", "45к дубликат");
        await _storage.WriteProductFileAsync(
            "мой тег",
            "45к дубликат",
            "card_1.pdf",
            "pdf-3"u8.ToArray()
        );

        await _storage.CreateProductDirectoryAsync("мой тег", "45к  дубликат");
        await _storage.WriteProductFileAsync(
            "мой тег",
            "45к  дубликат",
            "card_1.pdf",
            "pdf-4"u8.ToArray()
        );

        // 3. Коллизия файлов внутри товара: "card 1.pdf" и "card  1.pdf"
        await _storage.CreateProductDirectoryAsync("мой тег", "45к конфликт файлов");
        await _storage.WriteProductFileAsync(
            "мой тег",
            "45к конфликт файлов",
            "card 1.pdf",
            "pdf-5"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "мой тег",
            "45к конфликт файлов",
            "card  1.pdf",
            "pdf-6"u8.ToArray()
        );

        await using (var dbContext = CreateDbContext())
        {
            var syncService = CreateSyncService(dbContext);
            var result = await syncService.SynchronizeAsync(WorkerSessionId);

            result.TotalProducts.ShouldBe(5);
            result.ValidProducts.ShouldBe(2);
            result.InvalidProducts.ShouldBe(3);
            result.NormalizationConflictsCount.ShouldBe(3);
        }

        await using (var verifyDb = CreateDbContext())
        {
            var products = await verifyDb
                .Products.Include(p => p.PrimaryTag)
                .Include(p => p.Files)
                .ToListAsync();

            products.Count.ShouldBe(5);

            var tagConflictProduct = products.Single(p =>
                p.Article == "45к товар в конфликтном теге"
            );
            tagConflictProduct.IsValid.ShouldBeFalse();
            tagConflictProduct.PrimaryTag!.Name.ShouldBe("мой  тег");
            tagConflictProduct.ValidationErrors.ShouldContain(
                ProductValidationError.NormalizationConflict
            );

            var articleConflictProduct = products.Single(p => p.Article == "45к  дубликат");
            articleConflictProduct.IsValid.ShouldBeFalse();
            articleConflictProduct.ValidationErrors.ShouldContain(
                ProductValidationError.NormalizationConflict
            );

            var fileConflictProduct = products.Single(p => p.Article == "45к конфликт файлов");
            fileConflictProduct.IsValid.ShouldBeFalse();
            fileConflictProduct.ValidationErrors.ShouldContain(
                ProductValidationError.NormalizationConflict
            );
            fileConflictProduct.Files.Count.ShouldBe(2);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldPerformXxHash64DiffAndUpdateOnlyModifiedOrAddedFiles()
    {
        await _storage.CreateProductDirectoryAsync("тег", "45к товар");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к товар",
            "cover_1.jpg",
            "unchanged-image-payload"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "тег",
            "45к товар",
            "card_1.pdf",
            "initial-pdf-v1"u8.ToArray()
        );

        await using (var db1 = CreateDbContext())
        {
            var sync1 = await CreateSyncService(db1).SynchronizeAsync(WorkerSessionId);
            sync1.AddedOrUpdatedFilesCount.ShouldBe(2);
            sync1.UnchangedFilesCount.ShouldBe(0);
        }

        // Сдвигаем время и меняем только card_1.pdf + добавляем card_2.pdf
        var secondSyncTime = FixedNow.AddMinutes(30);
        _timeProvider.Advance(TimeSpan.FromMinutes(30));

        await _storage.WriteProductFileAsync(
            "тег",
            "45к товар",
            "card_1.pdf",
            "updated-pdf-v2-longer-content"u8.ToArray()
        );
        await _storage.WriteProductFileAsync(
            "тег",
            "45к товар",
            "card_2.pdf",
            "new-pdf-page-2"u8.ToArray()
        );

        await using (var db2 = CreateDbContext())
        {
            var sync2 = await CreateSyncService(db2).SynchronizeAsync(WorkerSessionId);
            sync2.UnchangedFilesCount.ShouldBe(1); // cover_1.jpg не пересчитывался и не перезаписывался в БД
            sync2.AddedOrUpdatedFilesCount.ShouldBe(2); // card_1.pdf обновлен, card_2.pdf добавлен
            sync2.UpdatedProducts.ShouldBe(1);
        }

        await using (var verifyDb = CreateDbContext())
        {
            var files = await verifyDb.ProductFiles.ToListAsync();
            files.Count.ShouldBe(3);

            var coverFile = files.Single(f => f.FileName == "cover_1.jpg");
            coverFile.UpdatedAtUtc.ShouldBe(FixedNow); // Осталась старая метка времени

            var updatedCard1 = files.Single(f => f.FileName == "card_1.pdf");
            updatedCard1.UpdatedAtUtc.ShouldBe(secondSyncTime);
            updatedCard1.XxHash64.ShouldBe(
                ProductFileHasher.Compute("updated-pdf-v2-longer-content"u8).HashHex
            );

            var addedCard2 = files.Single(f => f.FileName == "card_2.pdf");
            addedCard2.UpdatedAtUtc.ShouldBe(secondSyncTime);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenProductMovedOnDiskWithSameProductId_ShouldPreserveProductIdAndSecondaryTags()
    {
        await _storage.CreateProductDirectoryAsync("старый тег", "45к старый артикул");
        await _storage.WriteProductFileAsync(
            "старый тег",
            "45к старый артикул",
            "card_1.pdf",
            "pdf-data"u8.ToArray()
        );

        Guid productId;
        await using (var db1 = CreateDbContext())
        {
            await CreateSyncService(db1).SynchronizeAsync(WorkerSessionId);
            var product = await db1.Products.SingleAsync();
            productId = product.Id;

            var secondaryTag = new SecondaryTag("хит продаж", FixedNow);
            db1.SecondaryTags.Add(secondaryTag);
            product.SecondaryTags.Add(secondaryTag);
            await db1.SaveChangesAsync();
        }

        // Перемещаем папку товара в другой первичный тег и переименовываем артикул на диске
        await _storage.MoveProductDirectoryAsync(
            "старый тег",
            "45к старый артикул",
            "новый тег",
            "а4 новый артикул"
        );
        await _storage.DeletePrimaryTagDirectoryAsync("старый тег");

        _timeProvider.Advance(TimeSpan.FromMinutes(10));

        await using (var db2 = CreateDbContext())
        {
            var result = await CreateSyncService(db2).SynchronizeAsync(WorkerSessionId);
            result.TotalProducts.ShouldBe(1);
            result.AddedProducts.ShouldBe(0);
            result.UpdatedProducts.ShouldBe(1);
            result.DeletedProducts.ShouldBe(0);
        }

        await using (var verifyDb = CreateDbContext())
        {
            var product = await verifyDb
                .Products.Include(p => p.PrimaryTag)
                .Include(p => p.Format)
                .Include(p => p.SecondaryTags)
                .SingleAsync();

            product.Id.ShouldBe(productId);
            product.PrimaryTag!.Name.ShouldBe("новый тег");
            product.Article.ShouldBe("а4 новый артикул");
            product.Format!.Code.ShouldBe("а4");
            product.SecondaryTags.ShouldHaveSingleItem().Name.ShouldBe("хит продаж");

            // Старый удаленный с диска первичный тег должен быть очищен из БД
            (await verifyDb.PrimaryTags.CountAsync()).ShouldBe(1);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldSoftDeleteUnusedCategoriesAndFormatsAndRestoreThemWithDisplayNameAndTagsWhenReused()
    {
        await _storage.CreateProductDirectoryAsync("тег", "45к карточка");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к карточка",
            "card_1.pdf",
            "card-pdf"u8.ToArray()
        );

        await _storage.CreateProductDirectoryAsync("тег", "45к постер");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к постер",
            "poster_1.pdf",
            "poster-pdf"u8.ToArray()
        );

        // 1. Первая синхронизация создает категории "card" и "poster" и два отдельных формата "45к" в них
        await using (var db1 = CreateDbContext())
        {
            await CreateSyncService(db1).SynchronizeAsync(WorkerSessionId);

            var cardCategory = await db1.Categories.SingleAsync(c => c.Code == "card");
            var cardFormat45k = await db1.Formats.SingleAsync(f =>
                f.CategoryId == cardCategory.Id && f.Code == "45к"
            );

            cardCategory.UpdateDisplayName("Открытки коллекционные", FixedNow);
            var tagCategory = new SecondaryTag("плотный картон", FixedNow);
            var tagFormat = new SecondaryTag("карманный размер", FixedNow);
            db1.SecondaryTags.AddRange(tagCategory, tagFormat);
            cardCategory.SecondaryTags.Add(tagCategory);
            cardFormat45k.SecondaryTags.Add(tagFormat);

            await db1.SaveChangesAsync();
        }

        // 2. Удаляем товар "45к карточка" с диска (товар "45к постер" остается)
        await _storage.DeleteProductDirectoryAsync("тег", "45к карточка");
        _timeProvider.Advance(TimeSpan.FromMinutes(15));

        await using (var db2 = CreateDbContext())
        {
            var sync2 = await CreateSyncService(db2).SynchronizeAsync(WorkerSessionId);
            sync2.DeletedProducts.ShouldBe(1);
        }

        await using (var checkSoftDeletedDb = CreateDbContext())
        {
            var cardCategory = await checkSoftDeletedDb.Categories.SingleAsync(c =>
                c.Code == "card"
            );
            var posterCategory = await checkSoftDeletedDb.Categories.SingleAsync(c =>
                c.Code == "poster"
            );
            var cardFormat45k = await checkSoftDeletedDb.Formats.SingleAsync(f =>
                f.CategoryId == cardCategory.Id && f.Code == "45к"
            );
            var posterFormat45k = await checkSoftDeletedDb.Formats.SingleAsync(f =>
                f.CategoryId == posterCategory.Id && f.Code == "45к"
            );

            cardCategory.IsDeleted.ShouldBeTrue();
            cardFormat45k.IsDeleted.ShouldBeTrue();

            posterCategory.IsDeleted.ShouldBeFalse();
            posterFormat45k.IsDeleted.ShouldBeFalse();
        }

        // 3. Снова появляется товар с категорией "card" и форматом "45к"
        await _storage.CreateProductDirectoryAsync("тег", "45к возвращение карточки");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к возвращение карточки",
            "card_new_1.pdf",
            "card-pdf-v2"u8.ToArray()
        );
        _timeProvider.Advance(TimeSpan.FromMinutes(15));

        await using (var db3 = CreateDbContext())
        {
            await CreateSyncService(db3).SynchronizeAsync(WorkerSessionId);
        }

        await using (var verifyRestoredDb = CreateDbContext())
        {
            var cardCategory = await verifyRestoredDb
                .Categories.Include(c => c.SecondaryTags)
                .SingleAsync(c => c.Code == "card");
            var cardFormat45k = await verifyRestoredDb
                .Formats.Include(f => f.SecondaryTags)
                .SingleAsync(f => f.CategoryId == cardCategory.Id && f.Code == "45к");

            cardCategory.IsDeleted.ShouldBeFalse();
            cardCategory.DisplayName.ShouldBe("Открытки коллекционные");
            cardCategory.SecondaryTags.ShouldHaveSingleItem().Name.ShouldBe("плотный картон");

            cardFormat45k.IsDeleted.ShouldBeFalse();
            cardFormat45k.SecondaryTags.ShouldHaveSingleItem().Name.ShouldBe("карманный размер");
        }
    }

    [Fact]
    public async Task SynchronizeAsync_ShouldPersistInvalidProductsCorruptedProductIdsAndRootStrayFiles()
    {
        // 1. Черновик без файлов сборки
        await _storage.CreateProductDirectoryAsync("тег", "45к пустой черновик");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к пустой черновик",
            "cover_1.jpg",
            "img"u8.ToArray()
        );

        // 2. Поврежденный .typoerp_id
        await _storage.CreateProductDirectoryAsync("тег", "45к битый айди");
        await _storage.WriteProductFileAsync(
            "тег",
            "45к битый айди",
            ".typoerp_id",
            Encoding.UTF8.GetBytes("corrupted-not-a-guid")
        );
        await _storage.WriteProductFileAsync(
            "тег",
            "45к битый айди",
            "card_1.pdf",
            "pdf"u8.ToArray()
        );

        // 3. Файлы прямо в корне хранилища (игнорируемый .DS_Store и невалидный stray_file.pdf)
        _storage.SeedRootFile(".DS_Store", [1, 2, 3]);
        _storage.SeedRootFile("stray_file.pdf", "stray"u8.ToArray());

        await using (var db1 = CreateDbContext())
        {
            var result = await CreateSyncService(db1).SynchronizeAsync(WorkerSessionId);

            result.TotalProducts.ShouldBe(3);
            result.ValidProducts.ShouldBe(0);
            result.InvalidProducts.ShouldBe(3);
        }

        await using (var verifyDb = CreateDbContext())
        {
            var products = await verifyDb.Products.ToListAsync();
            products.Count.ShouldBe(3);

            var draft = products.Single(p => p.Article == "45к пустой черновик");
            draft.IsValid.ShouldBeFalse();
            draft.ValidationErrors.ShouldContain(ProductValidationError.MissingAssemblyFiles);

            var corruptedIdProduct = products.Single(p => p.Article == "45к битый айди");
            corruptedIdProduct.IsValid.ShouldBeFalse();
            corruptedIdProduct.ValidationErrors.ShouldContain(
                ProductValidationError.InvalidProductIdFile
            );

            var strayRootProduct = products.Single(p => p.Article == "stray_file.pdf");
            strayRootProduct.PrimaryTagId.ShouldBeNull();
            strayRootProduct.IsValid.ShouldBeFalse();
            strayRootProduct.ValidationErrors.ShouldContain(
                ProductValidationError.MissingPrimaryTag
            );
        }

        // Убираем мусорный файл из корня и проверяем, что при следующей синхронизации запись очищается
        _storage.RemoveRootFile("stray_file.pdf");

        await using (var db2 = CreateDbContext())
        {
            var result2 = await CreateSyncService(db2).SynchronizeAsync(WorkerSessionId);
            result2.TotalProducts.ShouldBe(2);
            result2.DeletedProducts.ShouldBe(1);
        }
    }

    [Fact]
    public async Task SynchronizeAsync_WhenFolderCopiedWithDuplicateProductId_ShouldRegenerateIdForCopyAndPersistBoth()
    {
        var sharedId = Guid.NewGuid();

        await _storage.CreateProductDirectoryAsync("тег", "45к оригинал");
        await _storage.WriteProductIdAsync("тег", "45к оригинал", sharedId);
        await _storage.WriteProductFileAsync(
            "тег",
            "45к оригинал",
            "card_1.pdf",
            "pdf-1"u8.ToArray()
        );

        await _storage.CreateProductDirectoryAsync("тег", "45к копия папки");
        await _storage.WriteProductIdAsync("тег", "45к копия папки", sharedId);
        await _storage.WriteProductFileAsync(
            "тег",
            "45к копия папки",
            "card_1.pdf",
            "pdf-2"u8.ToArray()
        );

        await using (var dbContext = CreateDbContext())
        {
            var result = await CreateSyncService(dbContext).SynchronizeAsync(WorkerSessionId);
            result.TotalProducts.ShouldBe(2);
            result.ValidProducts.ShouldBe(2);
        }

        var id1 = await _storage.ReadProductIdAsync("тег", "45к оригинал");
        var id2 = await _storage.ReadProductIdAsync("тег", "45к копия папки");

        id1.ProductId.ShouldNotBeNull();
        id2.ProductId.ShouldNotBeNull();
        id1.ProductId.ShouldNotBe(id2.ProductId);
    }

    private ProductsDbContext CreateDbContext() => new(_dbOptions);

    private ProductSyncService CreateSyncService(ProductsDbContext dbContext) =>
        new(dbContext, _storage, _lockService, _options, _timeProvider);

    private sealed class FakeTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
