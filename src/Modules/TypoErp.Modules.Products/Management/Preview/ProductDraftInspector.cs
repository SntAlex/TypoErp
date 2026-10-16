using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Storage;
using TypoErp.Modules.Products.Validation;
using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Products.Management.Preview;

/// <summary>
///     Чистый доменный инспектор состояния формы создания/редактирования/починки товара.
///     Вычисляет нормализованные имена, классификацию файлов, diff-статус по XxHash64,
///     автоматически выводимые коды категории и формата, а также прогнозируемые ошибки валидации.
/// </summary>
public static class ProductDraftInspector
{
    public static ProductDraftPreviewResult Inspect(
        string? primaryTagName,
        string? article,
        IReadOnlyList<ProductDraftExistingFileInput>? existingFiles = null,
        IReadOnlyList<ProductFileUpload>? uploads = null,
        ProductsStorageOptions? options = null,
        int? projectedPrimaryTagProductCount = null
    )
    {
        var opts = options ?? new ProductsStorageOptions();
        var existingList = existingFiles ?? [];
        var uploadsList = uploads ?? [];

        var primaryTagValidation = PrimaryTagValidator.Validate(
            primaryTagName,
            opts,
            projectedPrimaryTagProductCount
        );
        var articleValidation = ArticleValidator.Validate(article, opts);

        var canSavePrimaryTagAndArticle = primaryTagValidation.IsValid && articleValidation.IsValid;

        // 1. Обрабатываем загружаемые файлы и вычисляем их XxHash64
        var uploadByNormalizedName = new Dictionary<
            string,
            (ProductFileUpload Upload, ProductFileHashResult Hash, int Count)
        >(StringComparer.OrdinalIgnoreCase);

        foreach (var upload in uploadsList)
        {
            var normalizedUploadName = StringNormalizer.NormalizeFileName(upload.FileName);
            var hash = ProductFileHasher.Compute(upload.Content);

            if (uploadByNormalizedName.TryGetValue(normalizedUploadName, out var prev))
            {
                uploadByNormalizedName[normalizedUploadName] = (upload, hash, prev.Count + 1);
            }
            else
            {
                uploadByNormalizedName[normalizedUploadName] = (upload, hash, 1);
            }
        }

        // 2. Собираем целевые имена всех активных существующих файлов (не помеченных на удаление и не перезаписанных загрузкой)
        var targetNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var existing in existingList)
        {
            if (existing.IsMarkedForDeletion)
                continue;

            var targetNormalized = StringNormalizer.NormalizeFileName(
                string.IsNullOrWhiteSpace(existing.NewFileName)
                    ? existing.CurrentFileName
                    : existing.NewFileName
            );

            if (uploadByNormalizedName.ContainsKey(targetNormalized))
                continue;

            targetNameCounts[targetNormalized] =
                targetNameCounts.GetValueOrDefault(targetNormalized) + 1;
        }

        foreach (var kvp in uploadByNormalizedName)
        {
            targetNameCounts[kvp.Key] =
                targetNameCounts.GetValueOrDefault(kvp.Key) + kvp.Value.Count;
        }

        var hasDuplicateTargets = targetNameCounts.Values.Any(count => count > 1);

        // Индекс существующих активных файлов по их целевому нормализованному имени для diff-сверки XxHash64
        var activeExistingByTargetName = new Dictionary<string, ProductDraftExistingFileInput>(
            StringComparer.OrdinalIgnoreCase
        );
        foreach (var existing in existingList)
        {
            if (existing.IsMarkedForDeletion)
                continue;

            var targetNormalized = StringNormalizer.NormalizeFileName(
                string.IsNullOrWhiteSpace(existing.NewFileName)
                    ? existing.CurrentFileName
                    : existing.NewFileName
            );

            activeExistingByTargetName.TryAdd(targetNormalized, existing);
        }

        var previewFiles = new List<ProductDraftFilePreviewItem>(
            existingList.Count + uploadsList.Count
        );
        var deletedExistingCount = 0;
        var renamedExistingCount = 0;
        var changedOrNewUploadsCount = 0;
        var unchangedUploadsCount = 0;

        // 3. Формируем строки превью для существующих файлов на диске
        foreach (var existing in existingList)
        {
            var rawTarget = string.IsNullOrWhiteSpace(existing.NewFileName)
                ? existing.CurrentFileName
                : existing.NewFileName;
            var targetNormalized = StringNormalizer.NormalizeFileName(rawTarget);
            var classified = ProductFileClassifier.Classify(rawTarget, opts);

            if (existing.IsMarkedForDeletion)
            {
                deletedExistingCount++;
                previewFiles.Add(
                    new ProductDraftFilePreviewItem(
                        existing.CurrentFileName,
                        targetNormalized,
                        classified.Kind,
                        classified.Prefix,
                        classified.Extension,
                        existing.SizeBytes,
                        existing.XxHash64,
                        ProductDraftFileDiffStatus.MarkedForDeletion,
                        IsUploadedFile: false,
                        IsMarkedForDeletion: true,
                        HasDuplicateTargetName: false,
                        classified.Errors
                    )
                );
                continue;
            }

            // Если этот файл перезаписывается загружаемым файлом с таким же нормализованным именем,
            // он будет отображен в секции загружаемых файлов с соответствующим diff-статусом
            if (uploadByNormalizedName.ContainsKey(targetNormalized))
                continue;

            var isRenamed = !string.Equals(
                existing.CurrentFileName,
                targetNormalized,
                StringComparison.Ordinal
            );
            if (isRenamed)
                renamedExistingCount++;

            var isDuplicate = targetNameCounts.GetValueOrDefault(targetNormalized) > 1;

            previewFiles.Add(
                new ProductDraftFilePreviewItem(
                    existing.CurrentFileName,
                    targetNormalized,
                    classified.Kind,
                    classified.Prefix,
                    classified.Extension,
                    existing.SizeBytes,
                    existing.XxHash64,
                    isRenamed
                        ? ProductDraftFileDiffStatus.RenamedOnDisk
                        : ProductDraftFileDiffStatus.ExistingUnchanged,
                    IsUploadedFile: false,
                    IsMarkedForDeletion: false,
                    HasDuplicateTargetName: isDuplicate,
                    classified.Errors
                )
            );
        }

        // 4. Формируем строки превью для загружаемых файлов с проверкой XxHash64
        foreach (var upload in uploadsList)
        {
            var normalizedName = StringNormalizer.NormalizeFileName(upload.FileName);
            var classified = ProductFileClassifier.Classify(upload.FileName, opts);
            var hash = ProductFileHasher.Compute(upload.Content);
            var isDuplicate = targetNameCounts.GetValueOrDefault(normalizedName) > 1;

            ProductDraftFileDiffStatus diffStatus;
            if (activeExistingByTargetName.TryGetValue(normalizedName, out var matchedExisting))
            {
                var isSameContent =
                    matchedExisting.SizeBytes == hash.SizeBytes
                    && string.Equals(
                        matchedExisting.XxHash64,
                        hash.HashHex,
                        StringComparison.OrdinalIgnoreCase
                    );

                if (isSameContent)
                {
                    diffStatus = ProductDraftFileDiffStatus.UnchangedByHash;
                    unchangedUploadsCount++;
                }
                else
                {
                    diffStatus = ProductDraftFileDiffStatus.ContentModified;
                    changedOrNewUploadsCount++;
                }
            }
            else
            {
                diffStatus = ProductDraftFileDiffStatus.NewFile;
                changedOrNewUploadsCount++;
            }

            previewFiles.Add(
                new ProductDraftFilePreviewItem(
                    upload.FileName,
                    normalizedName,
                    classified.Kind,
                    classified.Prefix,
                    classified.Extension,
                    hash.SizeBytes,
                    hash.HashHex,
                    diffStatus,
                    IsUploadedFile: true,
                    IsMarkedForDeletion: false,
                    HasDuplicateTargetName: isDuplicate,
                    classified.Errors
                )
            );
        }

        // 5. Запускаем доменный ProductValidator по итоговому набору активных файлов
        var activeFileNamesForValidation = previewFiles
            .Where(f => !f.IsMarkedForDeletion)
            .Select(f =>
                string.IsNullOrEmpty(f.EffectiveNormalizedFileName)
                    ? f.SourceFileName
                    : f.EffectiveNormalizedFileName
            )
            .ToList();

        var additionalErrors = new List<ProductValidationError>();
        if (hasDuplicateTargets)
            additionalErrors.Add(ProductValidationError.NormalizationConflict);

        var productValidation = ProductValidator.Validate(
            primaryTagValidation.NormalizedName,
            articleValidation.NormalizedArticle,
            activeFileNamesForValidation,
            opts,
            projectedPrimaryTagProductCount,
            additionalErrors
        );

        var activeNonIgnored = previewFiles
            .Where(f => !f.IsMarkedForDeletion && f.Kind != ProductFileKind.Ignored)
            .ToList();

        var coversCount = activeNonIgnored.Count(f => f.Kind == ProductFileKind.Cover);
        var videoCoversCount = activeNonIgnored.Count(f => f.Kind == ProductFileKind.VideoCover);
        var assemblyFilesCount = activeNonIgnored.Count(f =>
            f.Kind == ProductFileKind.AssemblyFile
        );

        return new ProductDraftPreviewResult(
            primaryTagValidation.NormalizedName,
            articleValidation.NormalizedArticle,
            canSavePrimaryTagAndArticle && !hasDuplicateTargets,
            productValidation.CategoryCode,
            productValidation.FormatCode,
            productValidation.IsValid,
            ProductValidationErrorDescriptor.IsDraftOnly(productValidation.Errors),
            hasDuplicateTargets,
            coversCount,
            videoCoversCount,
            assemblyFilesCount,
            changedOrNewUploadsCount,
            unchangedUploadsCount,
            deletedExistingCount,
            renamedExistingCount,
            productValidation.Errors,
            previewFiles
        );
    }
}
