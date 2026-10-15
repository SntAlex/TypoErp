namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Результат валидации артикула товара и извлечения кода формата.
/// </summary>
public sealed record ArticleValidationResult(
    string NormalizedArticle,
    string? FormatCode,
    IReadOnlyList<ProductValidationError> Errors
)
{
    public bool IsValid => Errors.Count == 0;
}
