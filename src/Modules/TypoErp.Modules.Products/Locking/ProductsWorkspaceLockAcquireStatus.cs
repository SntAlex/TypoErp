namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Статус попытки захвата сессионной блокировки раздела «Товары».
/// </summary>
public enum ProductsWorkspaceLockAcquireStatus
{
    /// <summary>
    ///     Блокировка успешно захвачена текущей сессией.
    /// </summary>
    Acquired = 0,

    /// <summary>
    ///     Блокировка уже удерживается этой же сессией (повторный вызов).
    /// </summary>
    AlreadyHeldBySession = 1,

    /// <summary>
    ///     Раздел уже заблокирован другой сессией (другим работником или другой вкладкой).
    /// </summary>
    LockedByAnotherSession = 2,
}
