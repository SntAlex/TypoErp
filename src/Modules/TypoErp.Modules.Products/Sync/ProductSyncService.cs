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

namespace TypoErp.Modules.Products.Sync;

/// <summary>
///     Реализация движка синхронизации файлового хранилища товаров с PostgreSQL.
/// </summary>
public sealed class ProductSyncService : IProductSyncService
{
    private readonly ProductsDbContext _dbContext;
    private readonly IProductsWorkspaceLockService _lockService;
    private readonly ILogger<ProductSyncService> _logger;
    private readonly ProductsStorageOptions _options;
    private readonly IProductStorage _storage;
    private readonly TimeProvider _timeProvider;

    public ProductSyncService(
        ProductsDbContext dbContext,
        IProductStorage storage,
        IProductsWorkspaceLockService lockService,
        ProductsStorageOptions? options = null,
        TimeProvider? timeProvider = null,
        ILogger<ProductSyncService>? logger = null
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
        _logger = logger ?? NullLogger<ProductSyncService>.Instance;
    }

    public async Task<ProductSyncResult> SynchronizeAsync(
        string sessionId,
        CancellationToken cancellationToken = default
    )
    {
        _lockService.EnsureHeldBySession(sessionId);

        if (!await _storage.IsRootAccessibleAsync(cancellationToken))
        {
            _logger.LogError(
                "Cannot synchronize products: storage root '{RootPath}' is not accessible",
                _options.RootPath
            );
            throw new InvalidOperationException(
                "Корневой путь хранилища товаров не существует или недоступен."
            );
        }

        var now = _timeProvider.GetUtcNow();
        var autoNormalizedItemsCount = 0;
        var normalizationConflictsCount = 0;

        _logger.LogInformation(
            "Starting products storage synchronization for session '{SessionId}'",
            sessionId
        );

        // 1. Загружаем текущее состояние из БД
        var existingPrimaryTags = await _dbContext.PrimaryTags.ToListAsync(cancellationToken);
        var existingCategories = await _dbContext.Categories.ToListAsync(cancellationToken);
        var existingFormats = await _dbContext.Formats.ToListAsync(cancellationToken);
        var existingProducts = await _dbContext
            .Products.Include(p => p.Files)
            .Include(p => p.PrimaryTag)
            .ToListAsync(cancellationToken);

        var existingProductsById = existingProducts.ToDictionary(p => p.Id);

        // 2. Обходим и авто-нормализуем папки первичных тегов, артикулов и файлы в хранилище
        var scannedPrimaryTags = new List<ScannedPrimaryTag>();
        var scannedProducts = new List<ScannedProductItem>();

        var rawPrimaryTagNames = await _storage.GetPrimaryTagDirectoriesAsync(cancellationToken);
        foreach (var rawTagName in rawPrimaryTagNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedTagName = StringNormalizer.Normalize(rawTagName);
            var actualTagName = rawTagName;
            var hasTagNormalizationConflict = false;

            if (!string.Equals(rawTagName, normalizedTagName, StringComparison.Ordinal))
            {
                if (
                    string.IsNullOrEmpty(normalizedTagName)
                    || !FileSystemNameValidator.IsValid(normalizedTagName)
                )
                {
                    hasTagNormalizationConflict = true;
                    normalizationConflictsCount++;
                    _logger.LogWarning(
                        "Primary tag directory '{RawTagName}' cannot be normalized to '{NormalizedTagName}': invalid file system name",
                        rawTagName,
                        normalizedTagName
                    );
                }
                else
                {
                    var renameTagResult = await _storage.RenamePrimaryTagDirectoryAsync(
                        rawTagName,
                        normalizedTagName,
                        cancellationToken
                    );

                    if (renameTagResult.IsSuccess)
                    {
                        actualTagName = renameTagResult.FinalName;
                        if (renameTagResult.Status == StorageRenameStatus.Success)
                            autoNormalizedItemsCount++;
                    }
                    else
                    {
                        hasTagNormalizationConflict = true;
                        normalizationConflictsCount++;
                        _logger.LogWarning(
                            "Normalization conflict for primary tag '{RawTagName}' -> '{NormalizedTagName}': {Status}",
                            rawTagName,
                            normalizedTagName,
                            renameTagResult.Status
                        );
                    }
                }
            }

            scannedPrimaryTags.Add(new ScannedPrimaryTag(rawTagName, actualTagName));

            var rawArticleNames = await _storage.GetProductDirectoriesAsync(
                actualTagName,
                cancellationToken
            );
            var primaryTagProductCount = rawArticleNames.Count;

            foreach (var rawArticleName in rawArticleNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedArticleName = StringNormalizer.Normalize(rawArticleName);
                var actualArticleName = rawArticleName;
                var hasArticleNormalizationConflict = false;

                if (!string.Equals(rawArticleName, normalizedArticleName, StringComparison.Ordinal))
                {
                    if (
                        string.IsNullOrEmpty(normalizedArticleName)
                        || !FileSystemNameValidator.IsValid(normalizedArticleName)
                    )
                    {
                        hasArticleNormalizationConflict = true;
                        normalizationConflictsCount++;
                        _logger.LogWarning(
                            "Product directory '{PrimaryTag}/{RawArticle}' cannot be normalized to '{NormalizedArticle}': invalid file system name",
                            actualTagName,
                            rawArticleName,
                            normalizedArticleName
                        );
                    }
                    else
                    {
                        var renameArticleResult = await _storage.RenameProductDirectoryAsync(
                            actualTagName,
                            rawArticleName,
                            normalizedArticleName,
                            cancellationToken
                        );

                        if (renameArticleResult.IsSuccess)
                        {
                            actualArticleName = renameArticleResult.FinalName;
                            if (renameArticleResult.Status == StorageRenameStatus.Success)
                                autoNormalizedItemsCount++;
                        }
                        else
                        {
                            hasArticleNormalizationConflict = true;
                            normalizationConflictsCount++;
                            _logger.LogWarning(
                                "Normalization conflict for product '{PrimaryTag}/{RawArticle}' -> '{NormalizedArticle}': {Status}",
                                actualTagName,
                                rawArticleName,
                                normalizedArticleName,
                                renameArticleResult.Status
                            );
                        }
                    }
                }

                // Нормализация файлов внутри папки товара
                var rawFiles = await _storage.GetProductFilesAsync(
                    actualTagName,
                    actualArticleName,
                    cancellationToken
                );
                var nonIgnoredFiles = new List<StoredProductFileInfo>(rawFiles.Count);
                var hasFileNormalizationConflict = false;

                foreach (var rawFile in rawFiles)
                {
                    var initialClassification = ProductFileClassifier.Classify(
                        rawFile.FileName,
                        _options
                    );
                    if (initialClassification.Kind == ProductFileKind.Ignored)
                        continue;

                    var normalizedFileName = StringNormalizer.NormalizeFileName(rawFile.FileName);
                    var actualFileName = rawFile.FileName;

                    if (
                        !string.Equals(
                            rawFile.FileName,
                            normalizedFileName,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        if (
                            string.IsNullOrEmpty(normalizedFileName)
                            || !FileSystemNameValidator.IsValid(normalizedFileName)
                        )
                        {
                            hasFileNormalizationConflict = true;
                            normalizationConflictsCount++;
                            _logger.LogWarning(
                                "File '{PrimaryTag}/{Article}/{RawFile}' cannot be normalized to '{NormalizedFile}': invalid file system name",
                                actualTagName,
                                actualArticleName,
                                rawFile.FileName,
                                normalizedFileName
                            );
                        }
                        else
                        {
                            var renameFileResult = await _storage.RenameProductFileAsync(
                                actualTagName,
                                actualArticleName,
                                rawFile.FileName,
                                normalizedFileName,
                                cancellationToken
                            );

                            if (renameFileResult.IsSuccess)
                            {
                                actualFileName = renameFileResult.FinalName;
                                if (renameFileResult.Status == StorageRenameStatus.Success)
                                    autoNormalizedItemsCount++;
                            }
                            else
                            {
                                hasFileNormalizationConflict = true;
                                normalizationConflictsCount++;
                                _logger.LogWarning(
                                    "Normalization conflict for file '{PrimaryTag}/{Article}/{RawFile}' -> '{NormalizedFile}': {Status}",
                                    actualTagName,
                                    actualArticleName,
                                    rawFile.FileName,
                                    normalizedFileName,
                                    renameFileResult.Status
                                );
                            }
                        }
                    }

                    nonIgnoredFiles.Add(
                        new StoredProductFileInfo(
                            actualFileName,
                            rawFile.SizeBytes,
                            rawFile.XxHash64
                        )
                    );
                }

                var idReadResult = await _storage.ReadProductIdAsync(
                    actualTagName,
                    actualArticleName,
                    cancellationToken
                );

                var additionalErrors = new List<ProductValidationError>();
                if (
                    hasTagNormalizationConflict
                    || hasArticleNormalizationConflict
                    || hasFileNormalizationConflict
                )
                    additionalErrors.Add(ProductValidationError.NormalizationConflict);

                if (idReadResult.Status == ProductIdReadStatus.InvalidContent)
                    additionalErrors.Add(ProductValidationError.InvalidProductIdFile);

                var validationResult = ProductValidator.Validate(
                    actualTagName,
                    actualArticleName,
                    nonIgnoredFiles.Select(f => f.FileName),
                    _options,
                    primaryTagProductCount,
                    additionalErrors
                );

                scannedProducts.Add(
                    new ScannedProductItem(
                        rawTagName,
                        actualTagName,
                        rawArticleName,
                        actualArticleName,
                        idReadResult,
                        nonIgnoredFiles,
                        validationResult
                    )
                );
            }
        }

        // 3. Обходим неигнорируемые файлы, лежащие прямо в корне хранилища (вне первичных тегов)
        var rootFileNames = await _storage.GetRootFileNamesAsync(cancellationToken);
        foreach (var rootFileName in rootFileNames)
        {
            var classifiedRootFile = ProductFileClassifier.Classify(rootFileName, _options);
            if (classifiedRootFile.Kind == ProductFileKind.Ignored)
                continue;

            var rootValidation = ProductValidator.Validate(
                primaryTagName: null,
                articleName: rootFileName,
                fileNames: [rootFileName],
                options: _options
            );

            scannedProducts.Add(
                new ScannedProductItem(
                    RawPrimaryTagName: null,
                    ActualPrimaryTagName: null,
                    RawArticleName: rootFileName,
                    ActualArticleName: rootFileName,
                    IdReadResult: new ProductIdReadResult(ProductIdReadStatus.NotFound, null),
                    Files: [],
                    ValidationResult: rootValidation
                )
            );
        }

        // 4. Разрешаем ProductId для всех найденных товаров (с защитой от дубликатов при копировании папки на диске)
        await ResolveAndWriteProductIdsAsync(
            scannedProducts,
            existingProducts,
            existingProductsById,
            cancellationToken
        );

        // 5. Сохраняем изменения в БД атомарно в рамках транзакции
        var addedProductsCount = 0;
        var updatedProductsCount = 0;
        var deletedProductsCount = 0;
        var addedOrUpdatedFilesCount = 0;
        var unchangedFilesCount = 0;
        var deletedFilesCount = 0;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(
            cancellationToken
        );

        var claimedProductIds = scannedProducts.Select(p => p.ResolvedProductId).ToHashSet();
        var staleProducts = existingProducts.Where(p => !claimedProductIds.Contains(p.Id)).ToList();

        if (staleProducts.Count > 0)
        {
            deletedProductsCount = staleProducts.Count;
            deletedFilesCount += staleProducts.Sum(p => p.Files.Count);
            _dbContext.Products.RemoveRange(staleProducts);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        // 5.1. Синхронизация PrimaryTags
        var primaryTagsByActualName = new Dictionary<string, PrimaryTag>(StringComparer.Ordinal);
        var activePrimaryTagIds = new HashSet<Guid>();

        foreach (var scannedTag in scannedPrimaryTags)
        {
            if (primaryTagsByActualName.ContainsKey(scannedTag.ActualName))
                continue;

            var tagEntity = existingPrimaryTags.FirstOrDefault(t =>
                string.Equals(t.Name, scannedTag.ActualName, StringComparison.Ordinal)
            );

            if (tagEntity is null)
            {
                tagEntity = existingPrimaryTags.FirstOrDefault(t =>
                    !activePrimaryTagIds.Contains(t.Id)
                    && string.Equals(t.Name, scannedTag.RawName, StringComparison.Ordinal)
                );

                if (tagEntity is not null)
                    tagEntity.UpdateName(scannedTag.ActualName, now);
            }

            if (tagEntity is null)
            {
                tagEntity = new PrimaryTag(scannedTag.ActualName, now);
                _dbContext.PrimaryTags.Add(tagEntity);
                existingPrimaryTags.Add(tagEntity);
            }

            activePrimaryTagIds.Add(tagEntity.Id);
            primaryTagsByActualName[scannedTag.ActualName] = tagEntity;
        }

        // 5.2. Синхронизация Categories и Formats + привязка к товарам
        var categoriesByCode = existingCategories.ToDictionary(c => c.Code, StringComparer.Ordinal);
        var formatsByKey = existingFormats.ToDictionary(f => (f.CategoryId, f.Code));
        var activeCategoryIds = new HashSet<Guid>();
        var activeFormatIds = new HashSet<Guid>();

        foreach (var item in scannedProducts)
        {
            Guid? primaryTagId = null;
            if (
                item.ActualPrimaryTagName is not null
                && primaryTagsByActualName.TryGetValue(
                    item.ActualPrimaryTagName,
                    out var primaryTag
                )
            )
                primaryTagId = primaryTag.Id;

            Guid? categoryId = null;
            Guid? formatId = null;

            if (!string.IsNullOrEmpty(item.ValidationResult.CategoryCode))
            {
                var categoryCode = item.ValidationResult.CategoryCode;
                if (!categoriesByCode.TryGetValue(categoryCode, out var category))
                {
                    category = new Category(categoryCode, now);
                    _dbContext.Categories.Add(category);
                    existingCategories.Add(category);
                    categoriesByCode[categoryCode] = category;
                }
                else if (category.IsDeleted)
                {
                    category.Restore(now);
                }

                categoryId = category.Id;
                activeCategoryIds.Add(category.Id);

                if (!string.IsNullOrEmpty(item.ValidationResult.FormatCode))
                {
                    var formatCode = item.ValidationResult.FormatCode;
                    var formatKey = (category.Id, formatCode);
                    if (!formatsByKey.TryGetValue(formatKey, out var format))
                    {
                        format = new Format(category.Id, formatCode, now);
                        _dbContext.Formats.Add(format);
                        existingFormats.Add(format);
                        formatsByKey[formatKey] = format;
                    }
                    else if (format.IsDeleted)
                    {
                        format.Restore(now);
                    }

                    formatId = format.Id;
                    activeFormatIds.Add(format.Id);
                }
            }

            var classifiedByFileName = item
                .ValidationResult.ClassifiedFiles.Where(f => f.Kind != ProductFileKind.Ignored)
                .ToDictionary(f => f.OriginalFileName, StringComparer.Ordinal);

            if (!existingProductsById.TryGetValue(item.ResolvedProductId, out var product))
            {
                product = new Product(
                    item.ResolvedProductId,
                    item.ActualArticleName,
                    primaryTagId,
                    categoryId,
                    formatId,
                    now,
                    item.ValidationResult.Errors
                );

                foreach (var diskFile in item.Files)
                {
                    var classified = classifiedByFileName[diskFile.FileName];
                    product.Files.Add(
                        new ProductFile(
                            product.Id,
                            diskFile.FileName,
                            classified.Kind,
                            classified.Prefix,
                            classified.Extension,
                            diskFile.SizeBytes,
                            diskFile.XxHash64,
                            now
                        )
                    );
                    addedOrUpdatedFilesCount++;
                }

                _dbContext.Products.Add(product);
                addedProductsCount++;
            }
            else
            {
                var productChanged = false;

                if (
                    !string.Equals(
                        product.Article,
                        item.ActualArticleName,
                        StringComparison.Ordinal
                    )
                )
                {
                    product.UpdateArticle(item.ActualArticleName, now);
                    productChanged = true;
                }

                if (product.PrimaryTagId != primaryTagId)
                {
                    product.PrimaryTagId = primaryTagId;
                    productChanged = true;
                }

                if (product.CategoryId != categoryId)
                {
                    product.CategoryId = categoryId;
                    productChanged = true;
                }

                if (product.FormatId != formatId)
                {
                    product.FormatId = formatId;
                    productChanged = true;
                }

                var newErrors = item.ValidationResult.Errors.Distinct().ToList();
                if (!product.ValidationErrors.SequenceEqual(newErrors))
                {
                    product.SetValidationErrors(newErrors);
                    productChanged = true;
                }

                // Diff-синхронизация файлов товара по XxHash64 и размеру
                var diskFilesByName = item.Files.ToDictionary(
                    f => f.FileName,
                    StringComparer.Ordinal
                );
                var existingFiles = product.Files.ToList();

                foreach (var existingFile in existingFiles)
                {
                    if (!diskFilesByName.ContainsKey(existingFile.FileName))
                    {
                        product.Files.Remove(existingFile);
                        _dbContext.ProductFiles.Remove(existingFile);
                        deletedFilesCount++;
                        productChanged = true;
                    }
                }

                var remainingFilesByName = product.Files.ToDictionary(
                    f => f.FileName,
                    StringComparer.Ordinal
                );

                foreach (var diskFile in item.Files)
                {
                    var classified = classifiedByFileName[diskFile.FileName];

                    if (remainingFilesByName.TryGetValue(diskFile.FileName, out var existingFile))
                    {
                        var isUnchanged =
                            string.Equals(
                                existingFile.XxHash64,
                                diskFile.XxHash64,
                                StringComparison.OrdinalIgnoreCase
                            )
                            && existingFile.SizeBytes == diskFile.SizeBytes
                            && existingFile.Kind == classified.Kind
                            && string.Equals(
                                existingFile.Prefix,
                                classified.Prefix,
                                StringComparison.Ordinal
                            )
                            && string.Equals(
                                existingFile.Extension,
                                classified.Extension,
                                StringComparison.Ordinal
                            )
                            && string.Equals(
                                existingFile.NormalizedFileName,
                                classified.NormalizedFileName,
                                StringComparison.Ordinal
                            );

                        if (isUnchanged)
                        {
                            unchangedFilesCount++;
                        }
                        else
                        {
                            existingFile.NormalizedFileName = classified.NormalizedFileName;
                            existingFile.Kind = classified.Kind;
                            existingFile.Prefix = classified.Prefix;
                            existingFile.Extension = classified.Extension;
                            existingFile.SizeBytes = diskFile.SizeBytes;
                            existingFile.XxHash64 = diskFile.XxHash64;
                            existingFile.UpdatedAtUtc = now;

                            addedOrUpdatedFilesCount++;
                            productChanged = true;
                        }
                    }
                    else
                    {
                        var newFile = new ProductFile(
                            product.Id,
                            diskFile.FileName,
                            classified.Kind,
                            classified.Prefix,
                            classified.Extension,
                            diskFile.SizeBytes,
                            diskFile.XxHash64,
                            now
                        );
                        product.Files.Add(newFile);
                        _dbContext.ProductFiles.Add(newFile);

                        addedOrUpdatedFilesCount++;
                        productChanged = true;
                    }
                }

                if (productChanged)
                {
                    product.UpdatedAtUtc = now;
                    updatedProductsCount++;
                }
            }
        }

        // 5.3. Удаляем первичные теги, которых больше нет на диске
        var stalePrimaryTags = existingPrimaryTags
            .Where(t => !activePrimaryTagIds.Contains(t.Id))
            .ToList();
        if (stalePrimaryTags.Count > 0)
            _dbContext.PrimaryTags.RemoveRange(stalePrimaryTags);

        // 5.4. Мягкое удаление (Soft Delete) категорий и форматов, у которых не осталось товаров
        foreach (var category in existingCategories)
        {
            if (!activeCategoryIds.Contains(category.Id) && !category.IsDeleted)
                category.SoftDelete(now);
        }

        foreach (var format in existingFormats)
        {
            if (!activeFormatIds.Contains(format.Id) && !format.IsDeleted)
                format.SoftDelete(now);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var totalProducts = scannedProducts.Count;
        var validProducts = scannedProducts.Count(p => p.ValidationResult.IsValid);
        var invalidProducts = totalProducts - validProducts;

        var result = new ProductSyncResult(
            now,
            totalProducts,
            validProducts,
            invalidProducts,
            addedProductsCount,
            updatedProductsCount,
            deletedProductsCount,
            autoNormalizedItemsCount,
            normalizationConflictsCount,
            addedOrUpdatedFilesCount,
            unchangedFilesCount,
            deletedFilesCount
        );

        _logger.LogInformation(
            "Completed products synchronization: Total={TotalProducts} (Valid={ValidProducts}, Invalid={InvalidProducts}), Added={Added}, Updated={Updated}, Deleted={Deleted}, AutoNormalized={AutoNormalized}, Conflicts={Conflicts}, FilesChanged={FilesChanged}, FilesUnchanged={FilesUnchanged}",
            result.TotalProducts,
            result.ValidProducts,
            result.InvalidProducts,
            result.AddedProducts,
            result.UpdatedProducts,
            result.DeletedProducts,
            result.AutoNormalizedItemsCount,
            result.NormalizationConflictsCount,
            result.AddedOrUpdatedFilesCount,
            result.UnchangedFilesCount
        );

        return result;
    }

    private async Task ResolveAndWriteProductIdsAsync(
        List<ScannedProductItem> scannedProducts,
        List<Product> existingProducts,
        Dictionary<Guid, Product> existingProductsById,
        CancellationToken cancellationToken
    )
    {
        var claimedIds = new HashSet<Guid>();

        // 1. Сначала обрабатываем папки, в которых уже есть валидный .typoerp_id
        var foundGroups = scannedProducts
            .Where(p =>
                p.ActualPrimaryTagName is not null
                && p.IdReadResult.Status == ProductIdReadStatus.Found
                && p.IdReadResult.ProductId.HasValue
            )
            .GroupBy(p => p.IdReadResult.ProductId!.Value);

        foreach (var group in foundGroups)
        {
            var candidateId = group.Key;
            var items = group.ToList();

            if (items.Count == 1)
            {
                items[0].ResolvedProductId = candidateId;
                claimedIds.Add(candidateId);
                continue;
            }

            // Если работник скопировал папку товара вместе с .typoerp_id, сохраняем старый Id за оригинальной папкой
            ScannedProductItem? originalOwner = null;
            if (existingProductsById.TryGetValue(candidateId, out var dbProduct))
            {
                originalOwner = items.FirstOrDefault(i =>
                    string.Equals(
                        dbProduct.PrimaryTag?.Name,
                        i.ActualPrimaryTagName,
                        StringComparison.Ordinal
                    )
                    && string.Equals(
                        dbProduct.Article,
                        i.ActualArticleName,
                        StringComparison.Ordinal
                    )
                );
            }

            originalOwner ??= items[0];
            originalOwner.ResolvedProductId = candidateId;
            claimedIds.Add(candidateId);

            foreach (var duplicateItem in items.Where(i => !ReferenceEquals(i, originalOwner)))
            {
                var regeneratedId = Guid.NewGuid();
                duplicateItem.ResolvedProductId = regeneratedId;
                claimedIds.Add(regeneratedId);

                await _storage.WriteProductIdAsync(
                    duplicateItem.ActualPrimaryTagName!,
                    duplicateItem.ActualArticleName,
                    regeneratedId,
                    cancellationToken
                );

                _logger.LogWarning(
                    "Detected duplicate .typoerp_id '{DuplicateId}' in '{PrimaryTag}/{Article}'. Generated new ProductId '{NewId}'",
                    candidateId,
                    duplicateItem.ActualPrimaryTagName,
                    duplicateItem.ActualArticleName,
                    regeneratedId
                );
            }
        }

        // 2. Обрабатываем папки без .typoerp_id, с поврежденным .typoerp_id и корневые элементы
        foreach (var item in scannedProducts.Where(p => p.ResolvedProductId == Guid.Empty))
        {
            var matchedExisting = existingProducts.FirstOrDefault(p =>
                !claimedIds.Contains(p.Id)
                && (
                    (
                        item.ActualPrimaryTagName is null
                        && p.PrimaryTagId is null
                        && string.Equals(
                            p.Article,
                            item.ActualArticleName,
                            StringComparison.Ordinal
                        )
                    )
                    || (
                        item.ActualPrimaryTagName is not null
                        && (
                            string.Equals(
                                p.PrimaryTag?.Name,
                                item.ActualPrimaryTagName,
                                StringComparison.Ordinal
                            )
                            || string.Equals(
                                p.PrimaryTag?.Name,
                                item.RawPrimaryTagName,
                                StringComparison.Ordinal
                            )
                        )
                        && (
                            string.Equals(
                                p.Article,
                                item.ActualArticleName,
                                StringComparison.Ordinal
                            )
                            || string.Equals(
                                p.Article,
                                item.RawArticleName,
                                StringComparison.Ordinal
                            )
                        )
                    )
                )
            );

            var resolvedId = matchedExisting?.Id ?? Guid.NewGuid();
            item.ResolvedProductId = resolvedId;
            claimedIds.Add(resolvedId);

            if (
                item.ActualPrimaryTagName is not null
                && item.IdReadResult.Status == ProductIdReadStatus.NotFound
            )
            {
                await _storage.WriteProductIdAsync(
                    item.ActualPrimaryTagName,
                    item.ActualArticleName,
                    resolvedId,
                    cancellationToken
                );
            }
        }
    }

    private sealed record ScannedPrimaryTag(string RawName, string ActualName);

    private sealed class ScannedProductItem(
        string? RawPrimaryTagName,
        string? ActualPrimaryTagName,
        string RawArticleName,
        string ActualArticleName,
        ProductIdReadResult IdReadResult,
        IReadOnlyList<StoredProductFileInfo> Files,
        ProductValidationResult ValidationResult
    )
    {
        public string? RawPrimaryTagName { get; } = RawPrimaryTagName;
        public string? ActualPrimaryTagName { get; } = ActualPrimaryTagName;
        public string RawArticleName { get; } = RawArticleName;
        public string ActualArticleName { get; } = ActualArticleName;
        public ProductIdReadResult IdReadResult { get; } = IdReadResult;
        public IReadOnlyList<StoredProductFileInfo> Files { get; } = Files;
        public ProductValidationResult ValidationResult { get; } = ValidationResult;
        public Guid ResolvedProductId { get; set; }
    }
}
