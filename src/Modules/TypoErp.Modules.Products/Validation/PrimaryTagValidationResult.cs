namespace TypoErp.Modules.Products.Validation;

/// <summary>
///     Результат валидации первичного тега.
/// </summary>
public sealed record PrimaryTagValidationResult(
    string NormalizedName,
    IReadOnlyList<ProductValidationError> Errors
)
{
    public bool IsValid => Errors.Count == 0;
}
