namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Сервис чтения, фильтрации и поиска по каталогу товаров для UI-таблицы.
/// </summary>
public interface IProductCatalogQueryService
{
    /// <summary>
    ///     Возвращает справочники для выпадающих списков фильтрации, общие счетчики и статус доступности корня хранилища.
    /// </summary>
    Task<ProductCatalogLookups> GetFilterLookupsAsync(
        bool includeDeletedCategoriesAndFormats = false,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Выполняет фильтрацию, поиск, сортировку и пагинацию товаров.
    /// </summary>
    Task<ProductCatalogPageResult> QueryProductsAsync(
        ProductCatalogFilter filter,
        CancellationToken cancellationToken = default
    );
}
