using System.Security.Claims;
using TypoErp.Modules.Products.Locking;

namespace TypoErp.Web.Workspace;

/// <summary>
///     Сессионный координатор блокировки раздела «Товары» для текущего Blazor-подключения (Circuit).
///     Сохраняет удержание блокировки при обновлении страницы (F5) в рамках аутентификационной сессии работника
///     и обеспечивает реактивное уведомление при перехвате блокировки администратором.
/// </summary>
public interface IProductsWorkspaceCircuitSession : IDisposable
{
    /// <summary>
    ///     Уникальный идентификатор сессии работника (сохраняется между перезагрузками страницы по F5).
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
    ///     Синхронизирует идентификатор сессии с текущим аутентифицированным пользователем.
    /// </summary>
    void SynchronizeSession(ClaimsPrincipal? user);

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
