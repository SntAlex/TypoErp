using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Management.Preview;

/// <summary>
///     Результат интерактивного расчета превью товара перед сохранением в диалоге создания/редактирования/починки.
/// </summary>
public sealed record ProductDraftPreviewResult(
    string NormalizedPrimaryTag,
    string NormalizedArticle,
    bool CanSavePrimaryTagAndArticle,
    string? CategoryCode,
    string? FormatCode,
    bool IsValid,
    bool IsDraftOnly,
    bool HasDuplicateTargetFileNames,
    int ActiveCoversCount,
    int ActiveVideoCoversCount,
    int ActiveAssemblyFilesCount,
    int ChangedOrNewUploadsCount,
    int UnchangedUploadsCount,
    int DeletedExistingFilesCount,
    int RenamedExistingFilesCount,
    IReadOnlyList<ProductValidationError> ValidationErrors,
    IReadOnlyList<ProductDraftFilePreviewItem> Files
);
