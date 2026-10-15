using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Строка товара в главной таблице каталога со всеми связями, файлами и ошибками валидации.
/// </summary>
public sealed record ProductCatalogItemDto(
    Guid Id,
    string Article,
    string NormalizedArticle,
    Guid? PrimaryTagId,
    string? PrimaryTagName,
    Guid? CategoryId,
    string? CategoryCode,
    string? CategoryDisplayName,
    Guid? FormatId,
    string? FormatCode,
    bool IsValid,
    IReadOnlyList<ProductValidationError> ValidationErrors,
    IReadOnlyList<string> SecondaryTags,
    IReadOnlyList<string> InheritedSecondaryTags,
    IReadOnlyList<ProductCatalogFileDto> Files,
    int CoversCount,
    int VideoCoversCount,
    int AssemblyFilesCount,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
)
{
    /// <summary>
    ///     Признак того, что единственной причиной невалидности является отсутствие файлов сборки (черновик).
    /// </summary>
    public bool IsDraftOnly => ProductValidationErrorDescriptor.IsDraftOnly(ValidationErrors);

    /// <summary>
    ///     Форматированное название категории: "DisplayName (Code)" или "Code".
    /// </summary>
    public string CategoryLabel =>
        string.IsNullOrWhiteSpace(CategoryCode) ? "—"
        : string.IsNullOrWhiteSpace(CategoryDisplayName) ? CategoryCode
        : $"{CategoryDisplayName} ({CategoryCode})";
}
