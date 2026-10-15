namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Результат массового переноса товаров в другой первичный тег.
/// </summary>
public sealed record BulkMoveProductsResult(
    Guid TargetPrimaryTagId,
    string NormalizedTargetPrimaryTag,
    int MovedProductsCount
);
