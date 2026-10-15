using TypoErp.Modules.Products.Configuration;
using TypoErp.SharedKernel.Normalization;
using TypoErp.SharedKernel.Validation;

namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Валидатор названия и ограничений первичного тега.
/// </summary>
public static class PrimaryTagValidator
{
    public static PrimaryTagValidationResult Validate(
        string? primaryTagName,
        ProductsStorageOptions? options = null,
        int? currentProductCount = null
    )
    {
        var opts = options ?? new ProductsStorageOptions();

        if (string.IsNullOrWhiteSpace(primaryTagName))
            return new PrimaryTagValidationResult(
                string.Empty,
                [ProductValidationError.MissingPrimaryTag]
            );

        var trimmed = primaryTagName.Trim();
        var normalized = StringNormalizer.Normalize(primaryTagName);
        var errors = new List<ProductValidationError>();

        if (
            string.IsNullOrEmpty(normalized)
            || trimmed.EndsWith('.')
            || !FileSystemNameValidator.IsValid(normalized)
        )
            errors.Add(ProductValidationError.InvalidPrimaryTagName);

        if (normalized.Length > opts.MaxPrimaryTagLength)
            errors.Add(ProductValidationError.PrimaryTagTooLong);

        if (
            currentProductCount.HasValue
            && currentProductCount.Value > opts.MaxProductsPerPrimaryTag
        )
            errors.Add(ProductValidationError.PrimaryTagCapacityExceeded);

        return new PrimaryTagValidationResult(normalized, errors);
    }
}
