using TypoErp.Modules.Products.Entities;

namespace TypoErp.Modules.Products.Management;

/// <summary>
///     Сервис управления товарами, черновиками, починкой невалидных товаров, массовыми операциями (Bulk CRUD),
///     а также категориями (<see cref="Category.DisplayName" />), форматами и непервичными тегами.
///     Все операции требуют активной сессионной блокировки раздела товаров.
/// </summary>
public interface IProductManagementService
{
    /// <summary>
    ///     Создает новый товар (или черновик без файлов сборки) на диске и в PostgreSQL.
    /// </summary>
    Task<ProductMutationResult> CreateProductAsync(
        string sessionId,
        CreateProductRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Редактирует или чинит существующий товар: переименовывает/перемещает папку на диске,
    ///     восстанавливает поврежденный .typoerp_id, удаляет и переименовывает файлы,
    ///     выполняет diff-загрузку файлов по XxHash64 и пересчитывает валидность, категорию и формат.
    /// </summary>
    Task<ProductMutationResult> UpdateProductAsync(
        string sessionId,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Удаляет товар с диска и из базы данных, автоматически помечая мягким удалением (Soft Delete)
    ///     категорию и формат, если к ним больше не привязано ни одного товара.
    /// </summary>
    Task<bool> DeleteProductAsync(
        string sessionId,
        Guid productId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Переименовывает папку первичного тега на диске и в БД и пересчитывает валидацию товаров внутри него.
    /// </summary>
    Task RenamePrimaryTagAsync(
        string sessionId,
        Guid primaryTagId,
        string newPrimaryTagName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Массово переносит выбранные товары в целевой первичный тег на диске и в БД.
    /// </summary>
    Task<BulkMoveProductsResult> BulkMoveProductsAsync(
        string sessionId,
        IReadOnlyCollection<Guid> productIds,
        string targetPrimaryTagName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Массово добавляет, удаляет или заменяет непервичные теги у выбранных товаров.
    /// </summary>
    Task<int> BulkUpdateProductSecondaryTagsAsync(
        string sessionId,
        IReadOnlyCollection<Guid> productIds,
        IReadOnlyCollection<string> secondaryTagNames,
        BulkSecondaryTagOperationMode mode,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Массово удаляет выбранные товары с диска и из БД с пересчетом Soft Delete категорий и форматов.
    /// </summary>
    Task<int> BulkDeleteProductsAsync(
        string sessionId,
        IReadOnlyCollection<Guid> productIds,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Обновляет пользовательское отображаемое название категории (<see cref="Category.DisplayName" />).
    /// </summary>
    Task UpdateCategoryDisplayNameAsync(
        string sessionId,
        Guid categoryId,
        string? displayName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Задает список непервичных тегов для категории.
    /// </summary>
    Task SetCategorySecondaryTagsAsync(
        string sessionId,
        Guid categoryId,
        IReadOnlyCollection<string> secondaryTagNames,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Задает список непервичных тегов для формата внутри категории.
    /// </summary>
    Task SetFormatSecondaryTagsAsync(
        string sessionId,
        Guid formatId,
        IReadOnlyCollection<string> secondaryTagNames,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Задает список непервичных тегов для конкретного товара.
    /// </summary>
    Task SetProductSecondaryTagsAsync(
        string sessionId,
        Guid productId,
        IReadOnlyCollection<string> secondaryTagNames,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Возвращает существующий или создает новый нормализованный непервичный тег.
    /// </summary>
    Task<SecondaryTag> GetOrCreateSecondaryTagAsync(
        string sessionId,
        string tagName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Переименовывает существующий непервичный тег (с нормализацией и проверкой уникальности).
    /// </summary>
    Task RenameSecondaryTagAsync(
        string sessionId,
        Guid secondaryTagId,
        string newName,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    ///     Удаляет непервичный тег из базы данных.
    /// </summary>
    Task<bool> DeleteSecondaryTagAsync(
        string sessionId,
        Guid secondaryTagId,
        CancellationToken cancellationToken = default
    );
}
