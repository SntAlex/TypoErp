namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Сервис эксклюзивной сессионной блокировки рабочего пространства «Товары».
///     Гарантирует, что только одна сессия работника одновременно изменяет товары и синхронизирует хранилище,
///     и позволяет администратору принудительно перехватить управление с мгновенным уведомлением предыдущего работника.
/// </summary>
public interface IProductsWorkspaceLockService
{
    /// <summary>
    ///     Срабатывает при принудительном перехвате блокировки администратором у другой сессии.
    /// </summary>
    event EventHandler<ProductsWorkspaceLockTakenOverEventArgs>? LockTakenOver;

    /// <summary>
    ///     Срабатывает при любом изменении состояния блокировки (захват, перехват, освобождение).
    /// </summary>
    event EventHandler<ProductsWorkspaceLockChangedEventArgs>? LockStateChanged;

    /// <summary>
    ///     Возвращает информацию о текущей активной блокировке или null, если раздел свободен.
    /// </summary>
    ProductsWorkspaceLockInfo? GetCurrentLock();

    /// <summary>
    ///     Проверяет, удерживается ли блокировка указанной сессией в данный момент.
    /// </summary>
    bool IsHeldBySession(string sessionId);

    /// <summary>
    ///     Проверяет, что указанная сессия владеет блокировкой, иначе выбрасывает <see cref="ProductsWorkspaceLockException" />.
    /// </summary>
    void EnsureHeldBySession(string sessionId);

    /// <summary>
    ///     Пытается захватить блокировку раздела для указанной сессии работника.
    /// </summary>
    ProductsWorkspaceLockAcquireResult TryAcquire(
        string sessionId,
        Guid userId,
        string userDisplayName
    );

    /// <summary>
    ///     Принудительно перехватывает блокировку раздела для сессии администратора,
    ///     блокируя работу предыдущего владельца и уведомляя его через событие <see cref="LockTakenOver" />.
    /// </summary>
    ProductsWorkspaceTakeoverResult ForceTakeover(
        string adminSessionId,
        Guid adminUserId,
        string adminDisplayName,
        string? reason = null
    );

    /// <summary>
    ///     Освобождает блокировку, если она в данный момент принадлежит указанной сессии.
    /// </summary>
    bool Release(string sessionId);
}
