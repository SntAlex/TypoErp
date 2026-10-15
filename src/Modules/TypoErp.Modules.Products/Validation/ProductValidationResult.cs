using TypoErp.Modules.Products.Files;

namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Полный результат валидации товара: нормализованные имена, выведенные коды категории и формата,
///     классифицированные файлы и полный перечень ошибок валидации.
/// </summary>
public sealed record ProductValidationResult(
    string NormalizedPrimaryTag,
    string NormalizedArticle,
    string? CategoryCode,
    string? FormatCode,
    IReadOnlyList<ClassifiedProductFile> ClassifiedFiles,
    IReadOnlyList<ProductValidationError> Errors
)
{
    public bool IsValid => Errors.Count == 0;
}
