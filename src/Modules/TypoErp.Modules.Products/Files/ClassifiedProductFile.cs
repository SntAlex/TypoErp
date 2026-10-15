using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Files;

/// <summary>
///     Результат классификации и валидации отдельного файла в папке товара.
/// </summary>
public sealed record ClassifiedProductFile(
    string OriginalFileName,
    string NormalizedFileName,
    ProductFileKind Kind,
    string Prefix,
    string Extension,
    IReadOnlyList<ProductValidationError> Errors
)
{
    public bool IsValid => Errors.Count == 0;
}
