using TypoErp.Modules.Products.Configuration;
using TypoErp.SharedKernel.Normalization;
using TypoErp.SharedKernel.Validation;

namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Валидатор артикула товара и экстрактор кода формата (подстрока до первого пробела).
/// </summary>
public static class ArticleValidator
{
    public static ArticleValidationResult Validate(
        string? articleName,
        ProductsStorageOptions? options = null
    )
    {
        var opts = options ?? new ProductsStorageOptions();
        var trimmed = articleName?.Trim() ?? string.Empty;
        var normalized = StringNormalizer.Normalize(articleName);
        var errors = new List<ProductValidationError>();

        if (
            string.IsNullOrEmpty(normalized)
            || trimmed.EndsWith('.')
            || !FileSystemNameValidator.IsValid(normalized)
        )
            errors.Add(ProductValidationError.InvalidArticleName);

        if (normalized.Length > opts.MaxArticleLength)
            errors.Add(ProductValidationError.ArticleTooLong);

        string? formatCode = null;
        if (!string.IsNullOrEmpty(normalized))
        {
            var firstSpaceIndex = normalized.IndexOf(' ');
            if (firstSpaceIndex <= 0)
                errors.Add(ProductValidationError.ArticleMissingSpace);
            else
                formatCode = normalized[..firstSpaceIndex];
        }

        return new ArticleValidationResult(normalized, formatCode, errors);
    }
}
