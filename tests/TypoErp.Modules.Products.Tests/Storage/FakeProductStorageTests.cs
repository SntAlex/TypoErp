using System.Text;
using Shouldly;
using TypoErp.Modules.Products.Storage;

namespace TypoErp.Modules.Products.Tests.Storage;

public sealed class FakeProductStorageTests
{
    [Fact]
    public async Task IsRootAccessibleAsync_WhenToggled_ShouldReflectRootStateAndBlockOperationsWhenFalse()
    {
        var storage = new FakeProductStorage();

        (await storage.IsRootAccessibleAsync()).ShouldBeTrue();

        storage.IsRootAccessible = false;

        (await storage.IsRootAccessibleAsync()).ShouldBeFalse();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            storage.GetPrimaryTagDirectoriesAsync()
        );
    }

    [Fact]
    public async Task PrimaryTagDirectoryOperations_ShouldCreateListAndDeleteDirectories()
    {
        var storage = new FakeProductStorage();

        await storage.CreatePrimaryTagDirectoryAsync("  Мой   Тег ");
        await storage.CreatePrimaryTagDirectoryAsync("аниме наклейки");

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
    public async Task RenamePrimaryTagDirectoryAsync_WhenChangingCaseOnly_ShouldSucceedWithoutCollision()
    {
        var storage = new FakeProductStorage();
        await storage.CreatePrimaryTagDirectoryAsync("МОЙ ТЕГ");
        await storage.CreateProductDirectoryAsync("МОЙ ТЕГ", "45к артикул");

        var result = await storage.RenamePrimaryTagDirectoryAsync("МОЙ ТЕГ", "мой тег");

        result.IsSuccess.ShouldBeTrue();
        result.Status.ShouldBe(StorageRenameStatus.Success);
        result.FinalName.ShouldBe("мой тег");

        var tags = await storage.GetPrimaryTagDirectoriesAsync();
        tags.ShouldHaveSingleItem().ShouldBe("мой тег");
        (await storage.ProductDirectoryExistsAsync("мой тег", "45к артикул")).ShouldBeTrue();
    }

    [Fact]
    public async Task RenamePrimaryTagDirectoryAsync_WhenAnotherFolderNormalizesToSameName_ShouldReturnTargetCollision()
    {
        var storage = new FakeProductStorage();
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
    public async Task RenameProductDirectoryAsync_ShouldSupportCaseChangeAndDetectCollisions()
    {
        var storage = new FakeProductStorage();
        await storage.CreateProductDirectoryAsync("тег", "45К АРТИКУЛ 1");
        await storage.CreateProductDirectoryAsync("тег", "45к  артикул 1");

        var caseRename = await storage.RenameProductDirectoryAsync(
            "тег",
            "45К АРТИКУЛ 1",
            "45к артикул 1"
        );
        caseRename.Status.ShouldBe(StorageRenameStatus.Success);

        var collisionRename = await storage.RenameProductDirectoryAsync(
            "тег",
            "45к  артикул 1",
            "45к артикул 1"
        );
        collisionRename.Status.ShouldBe(StorageRenameStatus.TargetCollision);
    }

    [Fact]
    public async Task MoveProductDirectoryAsync_ShouldMoveProductBetweenPrimaryTagsAndDetectTargetCollision()
    {
        var storage = new FakeProductStorage();
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

        // Проверим коллизию при переносе в существующую папку
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
    public async Task ReadAndWriteProductIdAsync_ShouldHandleFoundNotFoundAndInvalidContent()
    {
        var storage = new FakeProductStorage();
        await storage.CreateProductDirectoryAsync("тег", "45к товар");

        var notFound = await storage.ReadProductIdAsync("тег", "45к товар");
        notFound.Status.ShouldBe(ProductIdReadStatus.NotFound);
        notFound.ProductId.ShouldBeNull();

        var expectedId = Guid.NewGuid();
        await storage.WriteProductIdAsync("тег", "45к товар", expectedId);

        var found = await storage.ReadProductIdAsync("тег", "45к товар");
        found.Status.ShouldBe(ProductIdReadStatus.Found);
        found.ProductId.ShouldBe(expectedId);

        // Запишем поврежденное содержимое в .typoerp_id
        await storage.WriteProductFileAsync(
            "тег",
            "45к товар",
            ".typoerp_id",
            Encoding.UTF8.GetBytes("not-a-valid-guid")
        );

        var invalid = await storage.ReadProductIdAsync("тег", "45к товар");
        invalid.Status.ShouldBe(ProductIdReadStatus.InvalidContent);
        invalid.ProductId.ShouldBeNull();
    }

    [Fact]
    public async Task ProductFileOperations_ShouldComputeXxHash64SupportCaseOnlyRenameAndDetectCollisions()
    {
        var storage = new FakeProductStorage();
        await storage.CreateProductDirectoryAsync("тег", "45к товар");

        var payload = Encoding.UTF8.GetBytes("print-layout-content-v1");
        var expectedHash = ProductFileHasher.Compute(payload);

        var writtenInfo = await storage.WriteProductFileAsync(
            "тег",
            "45к товар",
            "CARD_1.PDF",
            payload
        );

        writtenInfo.FileName.ShouldBe("CARD_1.PDF");
        writtenInfo.SizeBytes.ShouldBe(payload.Length);
        writtenInfo.XxHash64.ShouldBe(expectedHash.HashHex);

        // Переименование со сменой только регистра (CARD_1.PDF -> card_1.pdf)
        var renameCaseOnly = await storage.RenameProductFileAsync(
            "тег",
            "45к товар",
            "CARD_1.PDF",
            "card_1.pdf"
        );
        renameCaseOnly.Status.ShouldBe(StorageRenameStatus.Success);
        renameCaseOnly.FinalName.ShouldBe("card_1.pdf");

        // Добавим второй файл с лишним пробелом и проверим коллизию при нормализации в существующее имя
        await storage.WriteProductFileAsync("тег", "45к товар", "card  2.pdf", payload);
        await storage.WriteProductFileAsync("тег", "45к товар", "card 2.pdf", payload);

        var fileCollision = await storage.RenameProductFileAsync(
            "тег",
            "45к товар",
            "card  2.pdf",
            "card 2.pdf"
        );
        fileCollision.Status.ShouldBe(StorageRenameStatus.TargetCollision);

        // Проверим чтение содержимого файла через поток
        await using var readStream = await storage.OpenReadProductFileAsync(
            "тег",
            "45к товар",
            "card_1.pdf"
        );
        using var reader = new StreamReader(readStream, Encoding.UTF8);
        var text = await reader.ReadToEndAsync();
        text.ShouldBe("print-layout-content-v1");
    }

    [Fact]
    public async Task ConcurrentOperations_WhenExecutedInParallel_ShouldBeThreadSafe()
    {
        var storage = new FakeProductStorage();
        const int count = 50;

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
