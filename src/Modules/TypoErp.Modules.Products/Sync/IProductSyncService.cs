namespace TypoErp.Modules.Products.Sync;

/// <summary>
///     Движок синхронизации файлового хранилища товаров (<see cref="Storage.IProductStorage" />) с базой данных PostgreSQL.
/// </summary>
public interface IProductSyncService
{
    /// <summary>
    ///     Выполняет обход файлового хранилища, автоматическую нормализацию имен папок и файлов на диске,
    ///     чтение и создание .typoerp_id, diff-сверку файлов по XxHash64, автоматический вывод и мягкое удаление/восстановление
    ///     категорий и форматов, а также сохранение валидных и невалидных товаров в БД.
    ///     Требует активной сессионной блокировки раздела товаров для <paramref name="sessionId" />.
    /// </summary>
    Task<ProductSyncResult> SynchronizeAsync(
        string sessionId,
        CancellationToken cancellationToken = default
    );
}
