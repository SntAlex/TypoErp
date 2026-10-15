using TypoErp.Modules.Products.Locking;

namespace TypoErp.Web.Workspace;

/// <summary>
///     Сессионный координатор блокировки раздела «Товары» для текущего Blazor-подключения (Circuit).
///     Обеспечивает реактивное уведомление при перехвате блокировки администратором
///     и автоматическое освобождение лока при закрытии вкладки браузера.
/// </summary>
public interface IProductsWorkspaceCircuitSession : IDisposable
{
    /// <summary>
    ///     Уникальный идентификатор текущей Blazor-сессии (вкладки).
    /// </summary>
    string SessionId { get; }

    /// <summary>
    ///     Текущая активная блокировка раздела (или null, если раздел свободен).
    /// </summary>
    ProductsWorkspaceLockInfo? CurrentLock { get; }

    /// <summary>
    ///     Возвращает true, если именно эта сессия в данный момент удерживает блокировку раздела «Товары».
    /// </summary>
    bool IsHoldingLock { get; }

    /// <summary>
    ///     Активное предупреждение о том, что блокировка этой сессии была принудительно перехвачена администратором.
    /// </summary>
    WorkspaceTakeoverAlert? TakeoverAlert { get; }

    /// <summary>
    ///     Событие изменения состояния блокировки для реактивной перерисовки Blazor-компонентов.
    /// </summary>
    event Action? StateChanged;

    /// <summary>
    ///     Пытается занять блокировку раздела для текущей сессии работника.
    /// </summary>
    ProductsWorkspaceLockAcquireResult TryAcquire(Guid userId, string userDisplayName);

    /// <summary>
    ///     Принудительно перехватывает блокировку раздела для текущей сессии администратора.
    /// </summary>
    ProductsWorkspaceTakeoverResult ForceTakeover(
        Guid adminUserId,
        string adminDisplayName,
        string? reason = null
    );

    /// <summary>
    ///     Освобождает блокировку, если она принадлежит текущей сессии.
    /// </summary>
    bool Release();

    /// <summary>
    ///     Скрывает баннер о перехвате управления (оставляя сессию в режиме чтения).
    /// </summary>
    void DismissTakeoverAlert();
}
