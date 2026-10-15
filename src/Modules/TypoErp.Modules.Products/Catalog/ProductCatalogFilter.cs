using TypoErp.Modules.Products.Validation;

namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Параметры поиска, фильтрации, сортировки и пагинации таблицы товаров.
/// </summary>
public sealed record ProductCatalogFilter(
    string? SearchText = null,
    ProductValidityFilter Validity = ProductValidityFilter.All,
    Guid? PrimaryTagId = null,
    Guid? CategoryId = null,
    Guid? FormatId = null,
    IReadOnlyCollection<Guid>? SecondaryTagIds = null,
    ProductValidationError? ValidationError = null,
    ProductCatalogSortField SortBy = ProductCatalogSortField.Article,
    bool SortDescending = false,
    int Page = 0,
    int PageSize = 25
);
