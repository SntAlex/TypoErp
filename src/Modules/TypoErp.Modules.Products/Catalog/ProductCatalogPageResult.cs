namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Результат постраничной выборки товаров с общими счетчиками каталога.
/// </summary>
public sealed record ProductCatalogPageResult(
    IReadOnlyList<ProductCatalogItemDto> Items,
    int TotalMatchedCount,
    int TotalAllProductsCount,
    int TotalValidProductsCount,
    int TotalInvalidProductsCount
);
