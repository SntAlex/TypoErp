namespace TypoErp.Modules.Products.Catalog;

/// <summary>
///     Сервис чтения, фильтрации и поиска по каталогу товаров для UI-таблицы и диалогов.
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

    /// <summary>
    ///     Возвращает полную карточку товара по идентификатору со всеми его файлами, ошибками валидации и тегами,
    ///     либо null, если товар не найден.
    /// </summary>
    Task<ProductCatalogItemDto?> GetProductByIdAsync(
        Guid productId,
        CancellationToken cancellationToken = default
    );
}
