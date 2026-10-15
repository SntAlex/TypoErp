using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Результат создания или обновления/починки товара.
/// </summary>
public sealed record ProductMutationResult(
    Guid ProductId,
    string NormalizedPrimaryTag,
    string NormalizedArticle,
    Guid PrimaryTagId,
    Guid? CategoryId,
    Guid? FormatId,
    bool IsValid,
    IReadOnlyList<ProductValidationError> ValidationErrors,
    int AddedOrUpdatedFilesCount,
    int UnchangedFilesCount,
    int DeletedFilesCount
);
