using TypoErp.Modules.Products.Files;
using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Management.Preview;

/// <summary>
///     Элемент превью файла в форме создания/редактирования/починки товара.
/// </summary>
public sealed record ProductDraftFilePreviewItem(
    string SourceFileName,
    string EffectiveNormalizedFileName,
    ProductFileKind Kind,
    string Prefix,
    string Extension,
    long SizeBytes,
    string XxHash64,
    ProductDraftFileDiffStatus DiffStatus,
    bool IsUploadedFile,
    bool IsMarkedForDeletion,
    bool HasDuplicateTargetName,
    IReadOnlyList<ProductValidationError> Errors
)
{
    public bool IsValid => Errors.Count == 0 && !HasDuplicateTargetName;
}
