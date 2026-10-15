using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Files;

namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Агрегирующий доменный валидатор товара. Проверяет первичный тег, артикул,
///     классифицирует файлы, проверяет правило единого префикса сборки и выводит коды категории и формата.
/// </summary>
public static class ProductValidator
{
    public static ProductValidationResult Validate(
        string? primaryTagName,
        string? articleName,
        IEnumerable<string>? fileNames,
        ProductsStorageOptions? options = null,
        int? primaryTagProductCount = null,
        IEnumerable<ProductValidationError>? additionalErrors = null
    )
    {
        var opts = options ?? new ProductsStorageOptions();
        var errors = new List<ProductValidationError>();

        var primaryTagResult = PrimaryTagValidator.Validate(
            primaryTagName,
            opts,
            primaryTagProductCount
        );
        AddUniqueErrors(errors, primaryTagResult.Errors);

        var articleResult = ArticleValidator.Validate(articleName, opts);
        AddUniqueErrors(errors, articleResult.Errors);

        var classifiedFiles = ProductFileClassifier.ClassifyMany(fileNames, opts);
        foreach (var file in classifiedFiles)
            AddUniqueErrors(errors, file.Errors);

        var assemblyFiles = classifiedFiles
            .Where(f => f.Kind == ProductFileKind.AssemblyFile)
            .ToList();

        string? categoryCode = null;

        if (assemblyFiles.Count == 0)
        {
            AddUniqueError(errors, ProductValidationError.MissingAssemblyFiles);
        }
        else
        {
            var distinctPrefixes = assemblyFiles
                .Where(f => !string.IsNullOrEmpty(f.Prefix))
                .Select(f => f.Prefix)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (distinctPrefixes.Count > 1)
                AddUniqueError(errors, ProductValidationError.InconsistentAssemblyPrefixes);
            else if (
                distinctPrefixes.Count == 1
                && assemblyFiles.All(f =>
                    !f.Errors.Contains(ProductValidationError.InvalidAssemblyFilePrefix)
                )
            )
                categoryCode = distinctPrefixes[0];
        }

        if (additionalErrors is not null)
            AddUniqueErrors(errors, additionalErrors);

        return new ProductValidationResult(
            primaryTagResult.NormalizedName,
            articleResult.NormalizedArticle,
            categoryCode,
            articleResult.FormatCode,
            classifiedFiles,
            errors
        );
    }

    private static void AddUniqueErrors(
        List<ProductValidationError> target,
        IEnumerable<ProductValidationError> source
    )
    {
        foreach (var error in source)
            AddUniqueError(target, error);
    }

    private static void AddUniqueError(
        List<ProductValidationError> target,
        ProductValidationError error
    )
    {
        if (!target.Contains(error))
            target.Add(error);
    }
}
