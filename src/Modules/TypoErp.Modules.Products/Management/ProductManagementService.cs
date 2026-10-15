using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Entities;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Locking;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Validation;
using TypoErp.SharedKernel.Normalization;
using TypoErp.SharedKernel.Validation;

namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Реализация сервиса управления товарами, черновиками, починкой невалидных товаров,
///     массовыми операциями (Bulk CRUD), категориями, форматами и непервичными тегами.
/// </summary>
public sealed class ProductManagementService : IProductManagementService
{
    private const int MaxSecondaryTagLength = 200;

    private readonly ProductsDbContext _dbContext;
    private readonly IProductsWorkspaceLockService _lockService;
    private readonly ILogger<ProductManagementService> _logger;
    private readonly ProductsStorageOptions _options;
    private readonly IProductStorage _storage;
    private readonly TimeProvider _timeProvider;

    public ProductManagementService(
        ProductsDbContext dbContext,
        IProductStorage storage,
        IProductsWorkspaceLockService lockService,
        ProductsStorageOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<ProductManagementService>? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(lockService);

        _dbContext = dbContext;
        _storage = storage;
        _lockService = lockService;
        _options = options ?? new ProductsStorageOptions();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ProductManagementService>.Instance;
    }

    public async Task<ProductMutationResult> CreateProductAsync(
        string sessionId,
        CreateProductRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        _lockService.EnsureHeldBySession(sessionId);
        await EnsureStorageRootAccessibleAsync(cancellationToken);

        var primaryTagValidation = PrimaryTagValidator.Validate(request.PrimaryTagName, _options);
        if (!primaryTagValidation.IsValid)
            throw new ProductManagementException(
                ProductManagementErrorCode.InvalidPrimaryTag,
                $"Некорректное название первичного тега '{request.PrimaryTagName}'."
            );

        var articleValidation = ArticleValidator.Validate(request.Article, _options);
        if (!articleValidation.IsValid)
            throw new ProductManagementException(
                ProductManagementErrorCode.InvalidArticle,
                $"Некорректный артикул товара '{request.Article}'."
            );

        var normalizedPrimaryTag = primaryTagValidation.NormalizedName;
        var normalizedArticle = articleValidation.NormalizedArticle;

        var preparedUploads = ValidateAndPrepareUploads(request.Files);

        if (
            await _storage.ProductDirectoryExistsAsync(
                normalizedPrimaryTag,
                normalizedArticle,
                cancellationToken
            )
            || await _dbContext.Products.AnyAsync(
                p =>
                    p.PrimaryTag != null
                    && p.PrimaryTag.NormalizedName == normalizedPrimaryTag
                    && p.NormalizedArticle == normalizedArticle,
                cancellationToken
            )
        )
            throw new ProductManagementException(
                ProductManagementErrorCode.TargetAlreadyExists,
                $"Товар с артикулом '{normalizedArticle}' уже существует в первичном теге '{normalizedPrimaryTag}'."
            );

        var existingProductsInTag = await _storage.GetProductDirectoriesAsync(
            normalizedPrimaryTag,
            cancellationToken
        );
        var projectedProductCount = existingProductsInTag.Count + 1;

        if (projectedProductCount > _options.MaxProductsPerPrimaryTag)
            throw new ProductManagementException(
                ProductManagementErrorCode.PrimaryTagCapacityExceeded,
                $"Превышена максимальная вместимость первичного тега '{normalizedPrimaryTag}' ({_options.MaxProductsPerPrimaryTag})."
            );

        var now = _timeProvider.GetUtcNow();
        var productId = Guid.NewGuid();

        await _storage.CreateProductDirectoryAsync(
            normalizedPrimaryTag,
            normalizedArticle,
            cancellationToken
        );
        await _storage.WriteProductIdAsync(
            normalizedPrimaryTag,
            normalizedArticle,
            productId,
            cancellationToken
        );

        var storedFiles = new List<StoredProductFileInfo>(preparedUploads.Count);
        foreach (var upload in preparedUploads)
        {
            using var stream = new MemoryStream(upload.Content, writable: false);
            var storedInfo = await _storage.WriteProductFileAsync(
                normalizedPrimaryTag,
                normalizedArticle,
                upload.NormalizedFileName,
                stream,
                cancellationToken
            );
            storedFiles.Add(storedInfo);
        }

        var validationResult = ProductValidator.Validate(
            normalizedPrimaryTag,
            normalizedArticle,
            storedFiles.Select(f => f.FileName),
            _options,
            projectedProductCount
        );

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken
        );

        var primaryTag = await GetOrCreatePrimaryTagEntityAsync(
            normalizedPrimaryTag,
            now,
            cancellationToken
        );
        var (categoryId, formatId) = await ResolveCategoryAndFormatAsync(
            validationResult.CategoryCode,
            validationResult.FormatCode,
            now,
            cancellationToken
        );

        var product = new Product(
            productId,
            normalizedArticle,
            primaryTag.Id,
            categoryId,
            formatId,
            now,
            validationResult.Errors
        );

        var classifiedByName = validationResult
            .ClassifiedFiles.Where(f => f.Kind != ProductFileKind.Ignored)
            .ToDictionary(f => f.OriginalFileName, StringComparer.Ordinal);

        foreach (var storedFile in storedFiles)
        {
            var classified = classifiedByName[storedFile.FileName];
            product.Files.Add(
                new ProductFile(
                    product.Id,
                    storedFile.FileName,
                    classified.Kind,
                    classified.Prefix,
                    classified.Extension,
                    storedFile.SizeBytes,
                    storedFile.XxHash64,
                    now
                )
            );
        }

        if (request.SecondaryTagNames is not null && request.SecondaryTagNames.Count > 0)
        {
            var secondaryTags = await ResolveSecondaryTagsAsync(
                request.SecondaryTagNames,
                now,
                cancellationToken
            );
            foreach (var tag in secondaryTags)
                product.SecondaryTags.Add(tag);
        }

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Created product '{PrimaryTag}/{Article}' (Id={ProductId}, IsValid={IsValid}, FilesCount={FilesCount})",
            normalizedPrimaryTag,
            normalizedArticle,
            product.Id,
            product.IsValid,
            storedFiles.Count
        );

        return new ProductMutationResult(
            product.Id,
            normalizedPrimaryTag,
            normalizedArticle,
            primaryTag.Id,
            categoryId,
            formatId,
            product.IsValid,
            product.ValidationErrors,
            storedFiles.Count,
            0,
            0
        );
    }

    public async Task<ProductMutationResult> UpdateProductAsync(
        string sessionId,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        _lockService.EnsureHeldBySession(sessionId);
        await EnsureStorageRootAccessibleAsync(cancellationToken);

        var product = await _dbContext
            .Products.Include(p => p.PrimaryTag)
            .Include(p => p.Files)
            .Include(p => p.SecondaryTags)
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken);

        if (product is null)
            throw new ProductManagementException(
                ProductManagementErrorCode.NotFound,
                $"Товар с идентификатором '{request.ProductId}' не найден."
            );

        var primaryTagValidation = PrimaryTagValidator.Validate(
            request.NewPrimaryTagName,
            _options
        );
        if (!primaryTagValidation.IsValid)
            throw new ProductManagementException(
                ProductManagementErrorCode.InvalidPrimaryTag,
                $"Некорректное название первичного тега '{request.NewPrimaryTagName}'."
            );

        var articleValidation = ArticleValidator.Validate(request.NewArticle, _options);
        if (!articleValidation.IsValid)
            throw new ProductManagementException(
                ProductManagementErrorCode.InvalidArticle,
                $"Некорректный артикул товара '{request.NewArticle}'."
            );

        var targetPrimaryTag = primaryTagValidation.NormalizedName;
        var targetArticle = articleValidation.NormalizedArticle;

        var preparedRenames = ValidateAndPrepareRenames(request.FileRenames);
        var preparedUploads = ValidateAndPrepareUploads(request.FilesToUpsert);

        var currentTagName = product.PrimaryTag?.Name;
        var currentArticleName = product.Article;
        var previousPrimaryTagId = product.PrimaryTagId;

        var targetTagDirectories = await _storage.GetProductDirectoriesAsync(
            targetPrimaryTag,
            cancellationToken
        );
        var isStayingInSameTag =
            currentTagName is not null
            && string.Equals(currentTagName, targetPrimaryTag, StringComparison.OrdinalIgnoreCase)
            && targetTagDirectories.Any(a =>
                string.Equals(a, currentArticleName, StringComparison.OrdinalIgnoreCase)
            );

        var projectedTargetCount = isStayingInSameTag
            ? targetTagDirectories.Count
            : targetTagDirectories.Count + 1;

        if (projectedTargetCount > _options.MaxProductsPerPrimaryTag)
            throw new ProductManagementException(
                ProductManagementErrorCode.PrimaryTagCapacityExceeded,
                $"Превышена максимальная вместимость первичного тега '{targetPrimaryTag}' ({_options.MaxProductsPerPrimaryTag})."
            );

        // 1. Перемещение / переименование папки товара на диске
        if (currentTagName is null)
        {
            if (
                await _storage.ProductDirectoryExistsAsync(
                    targetPrimaryTag,
                    targetArticle,
                    cancellationToken
                )
            )
                throw new ProductManagementException(
                    ProductManagementErrorCode.TargetAlreadyExists,
                    $"Товар '{targetPrimaryTag}/{targetArticle}' уже существует."
                );

            await _storage.CreateProductDirectoryAsync(
                targetPrimaryTag,
                targetArticle,
                cancellationToken
            );
        }
        else if (
            !string.Equals(currentTagName, targetPrimaryTag, StringComparison.Ordinal)
            || !string.Equals(currentArticleName, targetArticle, StringComparison.Ordinal)
        )
        {
            StorageRenameResult moveOrRenameResult;
            if (string.Equals(currentTagName, targetPrimaryTag, StringComparison.Ordinal))
            {
                moveOrRenameResult = await _storage.RenameProductDirectoryAsync(
                    targetPrimaryTag,
                    currentArticleName,
                    targetArticle,
                    cancellationToken
                );
            }
            else
            {
                moveOrRenameResult = await _storage.MoveProductDirectoryAsync(
                    currentTagName,
                    currentArticleName,
                    targetPrimaryTag,
                    targetArticle,
                    cancellationToken
                );
            }

            if (!moveOrRenameResult.IsSuccess)
            {
                var errorCode =
                    moveOrRenameResult.Status == StorageRenameStatus.TargetCollision
                        ? ProductManagementErrorCode.TargetAlreadyExists
                        : ProductManagementErrorCode.StorageError;

                throw new ProductManagementException(
                    errorCode,
                    moveOrRenameResult.ErrorMessage
                        ?? $"Не удалось переименовать/переместить папку товара в '{targetPrimaryTag}/{targetArticle}'."
                );
            }
        }

        // 2. Проверка и починка .typoerp_id на диске
        var idReadResult = await _storage.ReadProductIdAsync(
            targetPrimaryTag,
            targetArticle,
            cancellationToken
        );
        if (
            idReadResult.Status != ProductIdReadStatus.Found
            || idReadResult.ProductId != product.Id
        )
        {
            await _storage.WriteProductIdAsync(
                targetPrimaryTag,
                targetArticle,
                product.Id,
                cancellationToken
            );
        }

        var now = _timeProvider.GetUtcNow();
        var deletedFilesCount = 0;
        var addedOrUpdatedFilesCount = 0;
        var unchangedFilesCount = 0;

        // 3. Удаление указанных файлов с диска
        if (request.FilesToDelete is not null)
        {
            foreach (var rawFileToDelete in request.FilesToDelete)
            {
                if (string.IsNullOrWhiteSpace(rawFileToDelete))
                    continue;

                var fileToDelete = rawFileToDelete.Trim();
                if (IsProductIdFile(fileToDelete))
                    continue;

                if (
                    await _storage.DeleteProductFileAsync(
                        targetPrimaryTag,
                        targetArticle,
                        fileToDelete,
                        cancellationToken
                    )
                )
                    deletedFilesCount++;
            }
        }

        // 4. Переименование существующих файлов на диске (например, починка конфликтных имен)
        foreach (var rename in preparedRenames)
        {
            var renameResult = await _storage.RenameProductFileAsync(
                targetPrimaryTag,
                targetArticle,
                rename.CurrentFileName,
                rename.NormalizedNewFileName,
                cancellationToken
            );

            if (!renameResult.IsSuccess)
            {
                var errorCode = renameResult.Status switch
                {
                    StorageRenameStatus.TargetCollision =>
                        ProductManagementErrorCode.TargetAlreadyExists,
                    StorageRenameStatus.SourceNotFound => ProductManagementErrorCode.NotFound,
                    _ => ProductManagementErrorCode.StorageError,
                };

                throw new ProductManagementException(
                    errorCode,
                    renameResult.ErrorMessage
                        ?? $"Не удалось переименовать файл '{rename.CurrentFileName}' в '{rename.NormalizedNewFileName}'."
                );
            }

            var trackedFile = product.Files.FirstOrDefault(f =>
                string.Equals(f.FileName, rename.CurrentFileName, StringComparison.Ordinal)
                || string.Equals(
                    f.FileName,
                    rename.CurrentFileName,
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (trackedFile is not null)
            {
                trackedFile.FileName = renameResult.FinalName;
                trackedFile.NormalizedFileName = rename.NormalizedNewFileName;
                trackedFile.UpdatedAtUtc = now;
            }
        }

        // 5. Diff-загрузка / обновление файлов по XxHash64
        var explicitlyUnchangedFileNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var upload in preparedUploads)
        {
            var existingOnDisk = await _storage.GetProductFileInfoAsync(
                targetPrimaryTag,
                targetArticle,
                upload.NormalizedFileName,
                cancellationToken
            );

            if (
                existingOnDisk is not null
                && existingOnDisk.SizeBytes == upload.HashResult.SizeBytes
                && string.Equals(
                    existingOnDisk.XxHash64,
                    upload.HashResult.HashHex,
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                if (
                    !string.Equals(
                        existingOnDisk.FileName,
                        upload.NormalizedFileName,
                        StringComparison.Ordinal
                    )
                )
                {
                    await _storage.RenameProductFileAsync(
                        targetPrimaryTag,
                        targetArticle,
                        existingOnDisk.FileName,
                        upload.NormalizedFileName,
                        cancellationToken
                    );
                }

                unchangedFilesCount++;
                explicitlyUnchangedFileNames.Add(upload.NormalizedFileName);
                continue;
            }

            if (
                existingOnDisk is not null
                && !string.Equals(
                    existingOnDisk.FileName,
                    upload.NormalizedFileName,
                    StringComparison.Ordinal
                )
            )
            {
                await _storage.RenameProductFileAsync(
                    targetPrimaryTag,
                    targetArticle,
                    existingOnDisk.FileName,
                    upload.NormalizedFileName,
                    cancellationToken
                );
            }

            using var stream = new MemoryStream(upload.Content, writable: false);
            await _storage.WriteProductFileAsync(
                targetPrimaryTag,
                targetArticle,
                upload.NormalizedFileName,
                stream,
                cancellationToken
            );
            addedOrUpdatedFilesCount++;
        }

        // 6. Повторное чтение файлов папки, авто-нормализация оставшихся файлов и валидация товара
        var currentDiskFiles = await _storage.GetProductFilesAsync(
            targetPrimaryTag,
            targetArticle,
            cancellationToken
        );
        var nonIgnoredFiles = new List<StoredProductFileInfo>(currentDiskFiles.Count);
        var hasFileNormalizationConflict = false;

        foreach (var diskFile in currentDiskFiles)
        {
            var initialClass = ProductFileClassifier.Classify(diskFile.FileName, _options);
            if (initialClass.Kind == ProductFileKind.Ignored)
                continue;

            var normalizedFileName = StringNormalizer.NormalizeFileName(diskFile.FileName);
            var actualFileName = diskFile.FileName;

            if (!string.Equals(diskFile.FileName, normalizedFileName, StringComparison.Ordinal))
            {
                if (
                    string.IsNullOrEmpty(normalizedFileName)
                    || !FileSystemNameValidator.IsValid(normalizedFileName)
                )
                {
                    hasFileNormalizationConflict = true;
                }
                else
                {
                    var renameRes = await _storage.RenameProductFileAsync(
                        targetPrimaryTag,
                        targetArticle,
                        diskFile.FileName,
                        normalizedFileName,
                        cancellationToken
                    );

                    if (renameRes.IsSuccess)
                        actualFileName = renameRes.FinalName;
                    else
                        hasFileNormalizationConflict = true;
                }
            }

            nonIgnoredFiles.Add(
                new StoredProductFileInfo(actualFileName, diskFile.SizeBytes, diskFile.XxHash64)
            );
        }

        var additionalErrors = new List<ProductValidationError>();
        if (hasFileNormalizationConflict)
            additionalErrors.Add(ProductValidationError.NormalizationConflict);

        var updatedDirProducts = await _storage.GetProductDirectoriesAsync(
            targetPrimaryTag,
            cancellationToken
        );
        var validationResult = ProductValidator.Validate(
            targetPrimaryTag,
            targetArticle,
            nonIgnoredFiles.Select(f => f.FileName),
            _options,
            updatedDirProducts.Count,
            additionalErrors
        );

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken
        );

        var targetPrimaryTagEntity = await GetOrCreatePrimaryTagEntityAsync(
            targetPrimaryTag,
            now,
            cancellationToken
        );
        var (categoryId, formatId) = await ResolveCategoryAndFormatAsync(
            validationResult.CategoryCode,
            validationResult.FormatCode,
            now,
            cancellationToken
        );

        product.UpdateArticle(targetArticle, now);
        product.PrimaryTagId = targetPrimaryTagEntity.Id;
        product.CategoryId = categoryId;
        product.FormatId = formatId;
        product.SetValidationErrors(validationResult.Errors);
        product.UpdatedAtUtc = now;

        // Синхронизируем коллекцию ProductFile в БД
        var diskFilesByName = nonIgnoredFiles.ToDictionary(f => f.FileName, StringComparer.Ordinal);
        var classifiedByName = validationResult
            .ClassifiedFiles.Where(f => f.Kind != ProductFileKind.Ignored)
            .ToDictionary(f => f.OriginalFileName, StringComparer.Ordinal);

        foreach (var existingFile in product.Files.ToList())
        {
            if (!diskFilesByName.ContainsKey(existingFile.FileName))
            {
                product.Files.Remove(existingFile);
                _dbContext.ProductFiles.Remove(existingFile);
            }
        }

        var trackedByName = product.Files.ToDictionary(f => f.FileName, StringComparer.Ordinal);

        foreach (var diskFile in nonIgnoredFiles)
        {
            var classified = classifiedByName[diskFile.FileName];
            if (trackedByName.TryGetValue(diskFile.FileName, out var existingEntity))
            {
                var isSameContent =
                    existingEntity.SizeBytes == diskFile.SizeBytes
                    && string.Equals(
                        existingEntity.XxHash64,
                        diskFile.XxHash64,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && existingEntity.Kind == classified.Kind
                    && string.Equals(
                        existingEntity.Prefix,
                        classified.Prefix,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        existingEntity.Extension,
                        classified.Extension,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        existingEntity.NormalizedFileName,
                        classified.NormalizedFileName,
                        StringComparison.Ordinal
                    );

                if (!isSameContent && !explicitlyUnchangedFileNames.Contains(diskFile.FileName))
                {
                    existingEntity.NormalizedFileName = classified.NormalizedFileName;
                    existingEntity.Kind = classified.Kind;
                    existingEntity.Prefix = classified.Prefix;
                    existingEntity.Extension = classified.Extension;
                    existingEntity.SizeBytes = diskFile.SizeBytes;
                    existingEntity.XxHash64 = diskFile.XxHash64;
                    existingEntity.UpdatedAtUtc = now;
                }
            }
            else
            {
                var newFileEntity = new ProductFile(
                    product.Id,
                    diskFile.FileName,
                    classified.Kind,
                    classified.Prefix,
                    classified.Extension,
                    diskFile.SizeBytes,
                    diskFile.XxHash64,
                    now
                );
                product.Files.Add(newFileEntity);
                _dbContext.ProductFiles.Add(newFileEntity);
            }
        }

        if (request.SecondaryTagNames is not null)
        {
            var resolvedTags = await ResolveSecondaryTagsAsync(
                request.SecondaryTagNames,
                now,
                cancellationToken
            );
            SyncCollection(product.SecondaryTags, resolvedTags);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (
            previousPrimaryTagId.HasValue
            && previousPrimaryTagId.Value != targetPrimaryTagEntity.Id
        )
            await CleanupOrRevalidatePrimaryTagAsync(
                previousPrimaryTagId.Value,
                now,
                cancellationToken
            );

        await SynchronizeCategoriesAndFormatsSoftDeleteStateAsync(now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Updated product '{PrimaryTag}/{Article}' (Id={ProductId}, IsValid={IsValid}, FilesAddedOrUpdated={FilesChanged}, FilesUnchanged={FilesUnchanged}, FilesDeleted={FilesDeleted})",
            targetPrimaryTag,
            targetArticle,
            product.Id,
            product.IsValid,
            addedOrUpdatedFilesCount,
            unchangedFilesCount,
            deletedFilesCount
        );

        return new ProductMutationResult(
            product.Id,
            targetPrimaryTag,
            targetArticle,
            targetPrimaryTagEntity.Id,
            categoryId,
            formatId,
            product.IsValid,
            product.ValidationErrors,
            addedOrUpdatedFilesCount,
            unchangedFilesCount,
            deletedFilesCount
        );
    }

    public async Task<bool> DeleteProductAsync(
        string sessionId,
        Guid productId,
        CancellationToken cancellationToken = default
    )
    {
        _lockService.EnsureHeldBySession(sessionId);
        await EnsureStorageRootAccessibleAsync(cancellationToken);

        var product = await _dbContext
            .Products.Include(p => p.PrimaryTag)
            .Include(p => p.Files)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
            return false;

        var now = _timeProvider.GetUtcNow();
        var oldPrimaryTagId = product.PrimaryTagId;

        if (product.PrimaryTag is not null)
        {
            await _storage.DeleteProductDirectoryAsync(
                product.PrimaryTag.Name,
                product.Article,
                cancellationToken
            );
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken
        );

        _dbContext.Products.Remove(product);
        await _dbContext.SaveChangesAsync(cancellationToken);

        if (oldPrimaryTagId.HasValue)
            await CleanupOrRevalidatePrimaryTagAsync(oldPrimaryTagId.Value, now, cancellationToken);

        await SynchronizeCategoriesAndFormatsSoftDeleteStateAsync(now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Deleted product '{PrimaryTag}/{Article}' (Id={ProductId})",
            product.PrimaryTag?.Name ?? "<root>",
            product.Article,
            product.Id
        );

        return true;
    }

    public async Task RenamePrimaryTagAsync(
        string sessionId,
        Guid primaryTagId,
        string newPrimaryTagName,
        CancellationToken cancellationToken = default
    )
    {
        _lockService.EnsureHeldBySession(sessionId);
        await EnsureStorageRootAccessibleAsync(cancellationToken);

        var primaryTag = await _dbContext
            .PrimaryTags.Include(t => t.Products)
                .ThenInclude(p => p.Files)
            .FirstOrDefaultAsync(t => t.Id == primaryTagId, cancellationToken);

        if (primaryTag is null)
            throw new ProductManagementException(
                ProductManagementErrorCode.NotFound,
                $"Первичный тег с идентификатором '{primaryTagId}' не найден."
            );

        var validation = PrimaryTagValidator.Validate(
            newPrimaryTagName,
            _options,
            primaryTag.Products.Count
        );
        if (!validation.IsValid)
        {
            var code = validation.Errors.Contains(ProductValidationError.PrimaryTagCapacityExceeded)
                ? ProductManagementErrorCode.PrimaryTagCapacityExceeded
                : ProductManagementErrorCode.InvalidPrimaryTag;

            throw new ProductManagementException(
                code,
                $"Некорректное новое имя первичного тега '{newPrimaryTagName}'."
            );
        }

        var normalizedNewName = validation.NormalizedName;
        var renameResult = await _storage.RenamePrimaryTagDirectoryAsync(
            primaryTag.Name,
            normalizedNewName,
            cancellationToken
        );

        if (!renameResult.IsSuccess)
        {
            var code =
                renameResult.Status == StorageRenameStatus.TargetCollision
                    ? ProductManagementErrorCode.TargetAlreadyExists
                    : ProductManagementErrorCode.StorageError;

            throw new ProductManagementException(
                code,
                renameResult.ErrorMessage
                    ?? $"Не удалось переименовать первичный тег '{primaryTag.Name}' в '{normalizedNewName}'."
            );
        }

        var now = _timeProvider.GetUtcNow();
        primaryTag.UpdateName(normalizedNewName, now);

        var productCountOnDisk = (
            await _storage.GetProductDirectoriesAsync(normalizedNewName, cancellationToken)
        ).Count;

        foreach (var product in primaryTag.Products)
        {
            await RevalidateProductEntityFromStorageAsync(
                product,
                normalizedNewName,
                productCountOnDisk,
                now,
                cancellationToken
            );
        }

        await SynchronizeCategoriesAndFormatsSoftDeleteStateAsync(now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Renamed primary tag {PrimaryTagId} to '{NewName}' and revalidated {ProductCount} products",
            primaryTag.Id,
            normalizedNewName,
            primaryTag.Products.Count
        );
    }

    public async Task<BulkMoveProductsResult> BulkMoveProductsAsync(
        string sessionId,
        IReadOnlyCollection<Guid> productIds,
        string targetPrimaryTagName,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(productIds);
        _lockService.EnsureHeldBySession(sessionId);
        await EnsureStorageRootAccessibleAsync(cancellationToken);

        var tagValidation = PrimaryTagValidator.Validate(targetPrimaryTagName, _options);
        if (!tagValidation.IsValid)
            throw new ProductManagementException(
                ProductManagementErrorCode.InvalidPrimaryTag,
                $"Некорректное название целевого первичного тега '{targetPrimaryTagName}'."
            );

        var normalizedTargetTag = tagValidation.NormalizedName;
        var distinctIds = productIds.Distinct().ToList();

        var products = await _dbContext
            .Products.Include(p => p.PrimaryTag)
            .Include(p => p.Files)
            .Where(p => distinctIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
        {
            var emptyTag = await GetOrCreatePrimaryTagEntityAsync(
                normalizedTargetTag,
                _timeProvider.GetUtcNow(),
                cancellationToken
            );
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new BulkMoveProductsResult(emptyTag.Id, normalizedTargetTag, 0);
        }

        var productsToMove = products
            .Where(p =>
                p.PrimaryTag is null
                || !string.Equals(p.PrimaryTag.Name, normalizedTargetTag, StringComparison.Ordinal)
            )
            .ToList();

        var existingInTarget = await _storage.GetProductDirectoriesAsync(
            normalizedTargetTag,
            cancellationToken
        );
        var projectedCount = existingInTarget.Count + productsToMove.Count;

        if (projectedCount > _options.MaxProductsPerPrimaryTag)
            throw new ProductManagementException(
                ProductManagementErrorCode.PrimaryTagCapacityExceeded,
                $"Превышена максимальная вместимость целевого первичного тега '{normalizedTargetTag}' ({_options.MaxProductsPerPrimaryTag})."
            );

        // Проверяем отсутствие коллизий артикулов в целевом первичном теге до начала перемещения
        var existingArticlesSet = new HashSet<string>(
            existingInTarget,
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var product in productsToMove)
        {
            if (!existingArticlesSet.Add(product.Article))
                throw new ProductManagementException(
                    ProductManagementErrorCode.TargetAlreadyExists,
                    $"В целевом первичном теге '{normalizedTargetTag}' уже существует артикул '{product.Article}'."
                );
        }

        var now = _timeProvider.GetUtcNow();
        var affectedSourceTagIds = new HashSet<Guid>();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken
        );

        var targetTagEntity = await GetOrCreatePrimaryTagEntityAsync(
            normalizedTargetTag,
            now,
            cancellationToken
        );

        if (!await _storage.PrimaryTagDirectoryExistsAsync(normalizedTargetTag, cancellationToken))
            await _storage.CreatePrimaryTagDirectoryAsync(normalizedTargetTag, cancellationToken);

        foreach (var product in productsToMove)
        {
            if (product.PrimaryTagId.HasValue)
                affectedSourceTagIds.Add(product.PrimaryTagId.Value);

            if (product.PrimaryTag is not null)
            {
                var moveRes = await _storage.MoveProductDirectoryAsync(
                    product.PrimaryTag.Name,
                    product.Article,
                    normalizedTargetTag,
                    product.Article,
                    cancellationToken
                );

                if (!moveRes.IsSuccess)
                    throw new ProductManagementException(
                        ProductManagementErrorCode.StorageError,
                        moveRes.ErrorMessage
                            ?? $"Ошибка при перемещении товара '{product.Article}' в '{normalizedTargetTag}'."
                    );
            }
            else
            {
                await _storage.CreateProductDirectoryAsync(
                    normalizedTargetTag,
                    product.Article,
                    cancellationToken
                );
                await _storage.WriteProductIdAsync(
                    normalizedTargetTag,
                    product.Article,
                    product.Id,
                    cancellationToken
                );
            }

            product.PrimaryTagId = targetTagEntity.Id;
            product.UpdatedAtUtc = now;

            await RevalidateProductEntityFromStorageAsync(
                product,
                normalizedTargetTag,
                projectedCount,
                now,
                cancellationToken
            );
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var sourceTagId in affectedSourceTagIds)
        {
            if (sourceTagId != targetTagEntity.Id)
                await CleanupOrRevalidatePrimaryTagAsync(sourceTagId, now, cancellationToken);
        }

        await SynchronizeCategoriesAndFormatsSoftDeleteStateAsync(now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Bulk moved {MovedCount} products to primary tag '{TargetPrimaryTag}'",
            productsToMove.Count,
            normalizedTargetTag
        );

        return new BulkMoveProductsResult(
            targetTagEntity.Id,
            normalizedTargetTag,
            productsToMove.Count
        );
    }

    public async Task<int> BulkUpdateProductSecondaryTagsAsync(
        string sessionId,
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<string> secondaryTagNames,
        BulkSecondaryTagOperationMode mode,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(productIds);
        ArgumentNullException.ThrowIfNull(secondaryTagNames);
        _lockService.EnsureHeldBySession(sessionId);

        var distinctIds = productIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return 0;

        var now = _timeProvider.GetUtcNow();
        var products = await _dbContext
            .Products.Include(p => p.SecondaryTags)
            .Where(p => distinctIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
            return 0;

        if (mode == BulkSecondaryTagOperationMode.Remove)
        {
            var normalizedToRemove = secondaryTagNames
                .Select(StringNormalizer.Normalize)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToHashSet(StringComparer.Ordinal);

            foreach (var product in products)
            {
                var toRemove = product
                    .SecondaryTags.Where(t => normalizedToRemove.Contains(t.Name))
                    .ToList();
                if (toRemove.Count > 0)
                {
                    foreach (var tag in toRemove)
                        product.SecondaryTags.Remove(tag);
                    product.UpdatedAtUtc = now;
                }
            }
        }
        else
        {
            var resolvedTags = await ResolveSecondaryTagsAsync(
                secondaryTagNames,
                now,
                cancellationToken
            );

            foreach (var product in products)
            {
                if (mode == BulkSecondaryTagOperationMode.Replace)
                {
                    SyncCollection(product.SecondaryTags, resolvedTags);
                    product.UpdatedAtUtc = now;
                }
                else
                {
                    var existingIds = product.SecondaryTags.Select(t => t.Id).ToHashSet();
                    var changed = false;
                    foreach (var tag in resolvedTags)
                    {
                        if (existingIds.Add(tag.Id))
                        {
                            product.SecondaryTags.Add(tag);
                            changed = true;
                        }
                    }

                    if (changed)
                        product.UpdatedAtUtc = now;
                }
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Bulk updated secondary tags (mode={Mode}) for {ProductCount} products",
            mode,
            products.Count
        );

        return products.Count;
    }

    public async Task<int> BulkDeleteProductsAsync(
        string sessionId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(productIds);
        _lockService.EnsureHeldBySession(sessionId);
        await EnsureStorageRootAccessibleAsync(cancellationToken);

        var distinctIds = productIds.Distinct().ToList();
        if (distinctIds.Count == 0)
            return 0;

        var products = await _dbContext
            .Products.Include(p => p.PrimaryTag)
            .Include(p => p.Files)
            .Where(p => distinctIds.Contains(p.Id))
            .ToListAsync(cancellationToken);

        if (products.Count == 0)
            return 0;

        var now = _timeProvider.GetUtcNow();
        var affectedPrimaryTagIds = new HashSet<Guid>();

        foreach (var product in products)
        {
            if (product.PrimaryTagId.HasValue)
                affectedPrimaryTagIds.Add(product.PrimaryTagId.Value);

            if (product.PrimaryTag is not null)
            {
                await _storage.DeleteProductDirectoryAsync(
                    product.PrimaryTag.Name,
                    product.Article,
                    cancellationToken
                );
            }
        }

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken
        );

        _dbContext.Products.RemoveRange(products);
        await _dbContext.SaveChangesAsync(cancellationToken);

        foreach (var tagId in affectedPrimaryTagIds)
            await CleanupOrRevalidatePrimaryTagAsync(tagId, now, cancellationToken);

        await SynchronizeCategoriesAndFormatsSoftDeleteStateAsync(now, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation("Bulk deleted {DeletedCount} products", products.Count);

        return products.Count;
    }

    public async Task UpdateCategoryDisplayNameAsync(
        string sessionId,
        Guid categoryId,
        string? displayName,
        CancellationToken cancellationToken = default
    )
    {
        _lockService.EnsureHeldBySession(sessionId);

        var category = await _dbContext.Categories.FirstOrDefaultAsync(
            c => c.Id == categoryId,
            cancellationToken
        );

        if (category is null)
            throw new ProductManagementException(
                ProductManagementErrorCode.NotFound,
                $"Категория с идентификатором '{categoryId}' не найдена."
            );

        var now = _timeProvider.GetUtcNow();
        category.UpdateDisplayName(displayName, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated DisplayName for category '{CategoryCode}' ({CategoryId}) to '{DisplayName}'",
            category.Code,
            category.Id,
            category.DisplayName ?? "<null>"
        );
    }

    public async Task SetCategorySecondaryTagsAsync(
        string sessionId,
        Guid categoryId,
        IReadOnlyCollection<string> secondaryTagNames,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(secondaryTagNames);
        _lockService.EnsureHeldBySession(sessionId);

        var category = await _dbContext
            .Categories.Include(c => c.SecondaryTags)
            .FirstOrDefaultAsync(c => c.Id == categoryId, cancellationToken);

        if (category is null)
            throw new ProductManagementException(
                ProductManagementErrorCode.NotFound,
                $"Категория с идентификатором '{categoryId}' не найдена."
            );

        var now = _timeProvider.GetUtcNow();
        var resolvedTags = await ResolveSecondaryTagsAsync(
            secondaryTagNames,
            now,
            cancellationToken
        );
        SyncCollection(category.SecondaryTags, resolvedTags);
        category.UpdatedAtUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetFormatSecondaryTagsAsync(
        string sessionId,
        Guid formatId,
        IReadOnlyCollection<string> secondaryTagNames,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(secondaryTagNames);
        _lockService.EnsureHeldBySession(sessionId);

        var format = await _dbContext
            .Formats.Include(f => f.SecondaryTags)
            .FirstOrDefaultAsync(f => f.Id == formatId, cancellationToken);

        if (format is null)
            throw new ProductManagementException(
                ProductManagementErrorCode.NotFound,
                $"Формат с идентификатором '{formatId}' не найден."
            );

        var now = _timeProvider.GetUtcNow();
        var resolvedTags = await ResolveSecondaryTagsAsync(
            secondaryTagNames,
            now,
            cancellationToken
        );
        SyncCollection(format.SecondaryTags, resolvedTags);
        format.UpdatedAtUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task SetProductSecondaryTagsAsync(
        string sessionId,
        Guid productId,
        IReadOnlyCollection<string> secondaryTagNames,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(secondaryTagNames);
        _lockService.EnsureHeldBySession(sessionId);

        var product = await _dbContext
            .Products.Include(p => p.SecondaryTags)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);

        if (product is null)
            throw new ProductManagementException(
                ProductManagementErrorCode.NotFound,
                $"Товар с идентификатором '{productId}' не найден."
            );

        var now = _timeProvider.GetUtcNow();
        var resolvedTags = await ResolveSecondaryTagsAsync(
            secondaryTagNames,
            now,
            cancellationToken
        );
        SyncCollection(product.SecondaryTags, resolvedTags);
        product.UpdatedAtUtc = now;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<SecondaryTag> GetOrCreateSecondaryTagAsync(
        string sessionId,
        string tagName,
        CancellationToken cancellationToken = default
    )
    {
        _lockService.EnsureHeldBySession(sessionId);

        var normalized = NormalizeAndValidateSecondaryTagName(tagName);
        var existing = await _dbContext.SecondaryTags.FirstOrDefaultAsync(
            t => t.Name == normalized,
            cancellationToken
        );

        if (existing is not null)
            return existing;

        var created = new SecondaryTag(normalized, _timeProvider.GetUtcNow());
        _dbContext.SecondaryTags.Add(created);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created secondary tag '{TagName}' ({TagId})",
            created.Name,
            created.Id
        );

        return created;
    }

    public async Task RenameSecondaryTagAsync(
        string sessionId,
        Guid secondaryTagId,
        string newName,
        CancellationToken cancellationToken = default
    )
    {
        _lockService.EnsureHeldBySession(sessionId);

        var normalizedNewName = NormalizeAndValidateSecondaryTagName(newName);
        var tag = await _dbContext.SecondaryTags.FirstOrDefaultAsync(
            t => t.Id == secondaryTagId,
            cancellationToken
        );

        if (tag is null)
            throw new ProductManagementException(
                ProductManagementErrorCode.NotFound,
                $"Непервичный тег с идентификатором '{secondaryTagId}' не найден."
            );

        if (string.Equals(tag.Name, normalizedNewName, StringComparison.Ordinal))
            return;

        var duplicateExists = await _dbContext.SecondaryTags.AnyAsync(
            t => t.Id != secondaryTagId && t.Name == normalizedNewName,
            cancellationToken
        );

        if (duplicateExists)
            throw new ProductManagementException(
                ProductManagementErrorCode.TargetAlreadyExists,
                $"Непервичный тег '{normalizedNewName}' уже существует."
            );

        var previousName = tag.Name;
        tag.Name = normalizedNewName;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Renamed secondary tag {TagId} from '{OldName}' to '{NewName}'",
            tag.Id,
            previousName,
            normalizedNewName
        );
    }

    public async Task<bool> DeleteSecondaryTagAsync(
        string sessionId,
        Guid secondaryTagId,
        CancellationToken cancellationToken = default
    )
    {
        _lockService.EnsureHeldBySession(sessionId);

        var tag = await _dbContext.SecondaryTags.FirstOrDefaultAsync(
            t => t.Id == secondaryTagId,
            cancellationToken
        );

        if (tag is null)
            return false;

        _dbContext.SecondaryTags.Remove(tag);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted secondary tag '{TagName}' ({TagId})", tag.Name, tag.Id);
        return true;
    }

    private async Task EnsureStorageRootAccessibleAsync(CancellationToken cancellationToken)
    {
        if (!await _storage.IsRootAccessibleAsync(cancellationToken))
        {
            _logger.LogError(
                "Cannot execute product management operation: storage root '{RootPath}' is not accessible",
                _options.RootPath
            );
            throw new InvalidOperationException(
                "Корневой путь хранилища товаров не существует или недоступен."
            );
        }
    }

    private List<PreparedUpload> ValidateAndPrepareUploads(IReadOnlyList<ProductFileUpload>? files)
    {
        if (files is null || files.Count == 0)
            return [];

        var result = new List<PreparedUpload>(files.Count);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            ArgumentNullException.ThrowIfNull(file);
            ArgumentNullException.ThrowIfNull(file.Content);

            var trimmed = file.FileName?.Trim() ?? string.Empty;
            var normalizedName = StringNormalizer.NormalizeFileName(file.FileName);

            if (
                string.IsNullOrEmpty(normalizedName)
                || trimmed.EndsWith('.')
                || !FileSystemNameValidator.IsValid(normalizedName)
            )
                throw new ProductManagementException(
                    ProductManagementErrorCode.InvalidFileName,
                    $"Имя файла '{file.FileName}' невалидно для файловой системы."
                );

            var classification = ProductFileClassifier.Classify(normalizedName, _options);
            if (classification.Kind == ProductFileKind.Ignored)
                throw new ProductManagementException(
                    ProductManagementErrorCode.InvalidFileName,
                    $"Файл '{file.FileName}' относится к системным или игнорируемым маскам и не может быть загружен."
                );

            if (!seenNames.Add(normalizedName))
                throw new ProductManagementException(
                    ProductManagementErrorCode.DuplicateFileNamesInRequest,
                    $"В запросе присутствуют дублирующиеся файлы с нормализованным именем '{normalizedName}'."
                );

            var hashResult = ProductFileHasher.Compute(file.Content);
            result.Add(new PreparedUpload(normalizedName, file.Content, hashResult));
        }

        return result;
    }

    private List<PreparedRename> ValidateAndPrepareRenames(
        IReadOnlyList<ProductFileRenameInput>? renames
    )
    {
        if (renames is null || renames.Count == 0)
            return [];

        var result = new List<PreparedRename>(renames.Count);
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rename in renames)
        {
            ArgumentNullException.ThrowIfNull(rename);

            if (string.IsNullOrWhiteSpace(rename.CurrentFileName))
                throw new ProductManagementException(
                    ProductManagementErrorCode.InvalidFileName,
                    "Исходное имя переименовываемого файла не может быть пустым."
                );

            var trimmedNew = rename.NewFileName?.Trim() ?? string.Empty;
            var normalizedNew = StringNormalizer.NormalizeFileName(rename.NewFileName);

            if (
                string.IsNullOrEmpty(normalizedNew)
                || trimmedNew.EndsWith('.')
                || !FileSystemNameValidator.IsValid(normalizedNew)
            )
                throw new ProductManagementException(
                    ProductManagementErrorCode.InvalidFileName,
                    $"Новое имя файла '{rename.NewFileName}' невалидно для файловой системы."
                );

            var classification = ProductFileClassifier.Classify(normalizedNew, _options);
            if (classification.Kind == ProductFileKind.Ignored)
                throw new ProductManagementException(
                    ProductManagementErrorCode.InvalidFileName,
                    $"Новое имя файла '{rename.NewFileName}' совпадает с системной/игнорируемой маской."
                );

            if (!seenTargets.Add(normalizedNew))
                throw new ProductManagementException(
                    ProductManagementErrorCode.DuplicateFileNamesInRequest,
                    $"Несколько файлов не могут быть переименованы в одно и то же имя '{normalizedNew}'."
                );

            result.Add(new PreparedRename(rename.CurrentFileName.Trim(), normalizedNew));
        }

        return result;
    }

    private async Task<PrimaryTag> GetOrCreatePrimaryTagEntityAsync(
        string normalizedPrimaryTagName,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var existing = await _dbContext.PrimaryTags.FirstOrDefaultAsync(
            t => t.Name == normalizedPrimaryTagName,
            cancellationToken
        );

        if (existing is not null)
            return existing;

        var created = new PrimaryTag(normalizedPrimaryTagName, now);
        _dbContext.PrimaryTags.Add(created);
        return created;
    }

    private async Task<(Guid? CategoryId, Guid? FormatId)> ResolveCategoryAndFormatAsync(
        string? categoryCode,
        string? formatCode,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrEmpty(categoryCode))
            return (null, null);

        var category =
            _dbContext.Categories.Local.FirstOrDefault(c =>
                string.Equals(c.Code, categoryCode, StringComparison.Ordinal)
            )
            ?? await _dbContext.Categories.FirstOrDefaultAsync(
                c => c.Code == categoryCode,
                cancellationToken
            );

        if (category is null)
        {
            category = new Category(categoryCode, now);
            _dbContext.Categories.Add(category);
        }
        else if (category.IsDeleted)
        {
            category.Restore(now);
        }

        Guid? formatId = null;
        if (!string.IsNullOrEmpty(formatCode))
        {
            var format =
                _dbContext.Formats.Local.FirstOrDefault(f =>
                    f.CategoryId == category.Id
                    && string.Equals(f.Code, formatCode, StringComparison.Ordinal)
                )
                ?? await _dbContext.Formats.FirstOrDefaultAsync(
                    f => f.CategoryId == category.Id && f.Code == formatCode,
                    cancellationToken
                );

            if (format is null)
            {
                format = new Format(category.Id, formatCode, now);
                _dbContext.Formats.Add(format);
            }
            else if (format.IsDeleted)
            {
                format.Restore(now);
            }

            formatId = format.Id;
        }

        return (category.Id, formatId);
    }

    private async Task<List<SecondaryTag>> ResolveSecondaryTagsAsync(
        IEnumerable<string> rawTagNames,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var normalizedNames = rawTagNames
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(NormalizeAndValidateSecondaryTagName)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalizedNames.Count == 0)
            return [];

        var existingTags = await _dbContext
            .SecondaryTags.Where(t => normalizedNames.Contains(t.Name))
            .ToListAsync(cancellationToken);

        var byName = existingTags.ToDictionary(t => t.Name, StringComparer.Ordinal);
        var result = new List<SecondaryTag>(normalizedNames.Count);

        foreach (var name in normalizedNames)
        {
            if (!byName.TryGetValue(name, out var tag))
            {
                tag =
                    _dbContext.SecondaryTags.Local.FirstOrDefault(t =>
                        string.Equals(t.Name, name, StringComparison.Ordinal)
                    ) ?? new SecondaryTag(name, now);

                if (_dbContext.Entry(tag).State == EntityState.Detached)
                    _dbContext.SecondaryTags.Add(tag);

                byName[name] = tag;
            }

            result.Add(tag);
        }

        return result;
    }

    private static string NormalizeAndValidateSecondaryTagName(string? rawName)
    {
        var normalized = StringNormalizer.Normalize(rawName);
        if (string.IsNullOrEmpty(normalized) || normalized.Length > MaxSecondaryTagLength)
            throw new ProductManagementException(
                ProductManagementErrorCode.InvalidSecondaryTagName,
                $"Некорректное название непервичного тега '{rawName}'."
            );

        return normalized;
    }

    private async Task RevalidateProductEntityFromStorageAsync(
        Product product,
        string actualPrimaryTagName,
        int primaryTagProductCount,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var diskFiles = await _storage.GetProductFilesAsync(
            actualPrimaryTagName,
            product.Article,
            cancellationToken
        );

        var nonIgnoredFileNames = diskFiles
            .Select(f => f.FileName)
            .Where(name =>
                ProductFileClassifier.Classify(name, _options).Kind != ProductFileKind.Ignored
            )
            .ToList();

        var idReadResult = await _storage.ReadProductIdAsync(
            actualPrimaryTagName,
            product.Article,
            cancellationToken
        );

        var additionalErrors = new List<ProductValidationError>();
        if (idReadResult.Status == ProductIdReadStatus.InvalidContent)
            additionalErrors.Add(ProductValidationError.InvalidProductIdFile);

        var validation = ProductValidator.Validate(
            actualPrimaryTagName,
            product.Article,
            nonIgnoredFileNames,
            _options,
            primaryTagProductCount,
            additionalErrors
        );

        var (categoryId, formatId) = await ResolveCategoryAndFormatAsync(
            validation.CategoryCode,
            validation.FormatCode,
            now,
            cancellationToken
        );

        product.CategoryId = categoryId;
        product.FormatId = formatId;
        product.SetValidationErrors(validation.Errors);
        product.UpdatedAtUtc = now;
    }

    private async Task CleanupOrRevalidatePrimaryTagAsync(
        Guid primaryTagId,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var primaryTag = await _dbContext
            .PrimaryTags.Include(t => t.Products)
            .FirstOrDefaultAsync(t => t.Id == primaryTagId, cancellationToken);

        if (primaryTag is null)
            return;

        if (primaryTag.Products.Count == 0)
        {
            var diskDirs = await _storage.GetProductDirectoriesAsync(
                primaryTag.Name,
                cancellationToken
            );
            if (diskDirs.Count == 0)
            {
                await _storage.DeletePrimaryTagDirectoryAsync(
                    primaryTag.Name,
                    recursive: false,
                    cancellationToken
                );
                _dbContext.PrimaryTags.Remove(primaryTag);
            }

            return;
        }

        // Если после удаления/перемещения товаров количество стало в пределах лимита, снимаем PrimaryTagCapacityExceeded
        if (primaryTag.Products.Count <= _options.MaxProductsPerPrimaryTag)
        {
            foreach (
                var product in primaryTag.Products.Where(p =>
                    p.ValidationErrors.Contains(ProductValidationError.PrimaryTagCapacityExceeded)
                )
            )
            {
                var updatedErrors = product
                    .ValidationErrors.Where(e =>
                        e != ProductValidationError.PrimaryTagCapacityExceeded
                    )
                    .ToList();
                product.SetValidationErrors(updatedErrors);
                product.UpdatedAtUtc = now;
            }
        }
    }

    private async Task SynchronizeCategoriesAndFormatsSoftDeleteStateAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var activeCategoryIds = await _dbContext
            .Products.Where(p => p.CategoryId.HasValue)
            .Select(p => p.CategoryId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var activeFormatIds = await _dbContext
            .Products.Where(p => p.FormatId.HasValue)
            .Select(p => p.FormatId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);

        var activeCategorySet = activeCategoryIds.ToHashSet();
        var activeFormatSet = activeFormatIds.ToHashSet();

        var allCategories = await _dbContext.Categories.ToListAsync(cancellationToken);
        foreach (var category in allCategories)
        {
            if (activeCategorySet.Contains(category.Id))
            {
                if (category.IsDeleted)
                    category.Restore(now);
            }
            else if (!category.IsDeleted)
            {
                category.SoftDelete(now);
            }
        }

        var allFormats = await _dbContext.Formats.ToListAsync(cancellationToken);
        foreach (var format in allFormats)
        {
            if (activeFormatSet.Contains(format.Id))
            {
                if (format.IsDeleted)
                    format.Restore(now);
            }
            else if (!format.IsDeleted)
            {
                format.SoftDelete(now);
            }
        }
    }

    private bool IsProductIdFile(string fileName)
    {
        var idFileName = string.IsNullOrWhiteSpace(_options.ProductIdFileName)
            ? ProductsStorageOptions.DefaultProductIdFileName
            : _options.ProductIdFileName.Trim();

        return string.Equals(fileName, idFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static void SyncCollection<T>(ICollection<T> target, IReadOnlyCollection<T> desired)
        where T : class
    {
        var desiredSet = desired.ToHashSet();

        foreach (var existing in target.ToList())
        {
            if (!desiredSet.Contains(existing))
                target.Remove(existing);
        }

        foreach (var item in desired)
        {
            if (!target.Contains(item))
                target.Add(item);
        }
    }

    private sealed record PreparedUpload(
        string NormalizedFileName,
        byte[] Content,
        ProductFileHashResult HashResult
    );

    private sealed record PreparedRename(string CurrentFileName, string NormalizedNewFileName);
}
