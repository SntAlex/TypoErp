namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Исключение, выбрасываемое при попытке выполнить изменяющую операцию в разделе «Товары»
///     сессией, которая не владеет активной блокировкой (например, после перехвата управления администратором).
/// </summary>
public sealed class ProductsWorkspaceLockException : InvalidOperationException
{
    public ProductsWorkspaceLockException(
        string attemptedSessionId,
        ProductsWorkspaceLockInfo? currentLock
    )
        : base(BuildMessage(attemptedSessionId, currentLock))
    {
        AttemptedSessionId = attemptedSessionId;
        CurrentLock = currentLock;
    }

    public string AttemptedSessionId { get; }

    public ProductsWorkspaceLockInfo? CurrentLock { get; }

    private static string BuildMessage(
        string attemptedSessionId,
        ProductsWorkspaceLockInfo? currentLock
    )
    {
        return currentLock is null
            ? $"Сессия '{attemptedSessionId}' не удерживает блокировку раздела товаров (раздел не заблокирован)."
            : $"Сессия '{attemptedSessionId}' не удерживает блокировку раздела товаров. Текущий владелец: '{currentLock.UserDisplayName}' (сессия '{currentLock.SessionId}').";
    }
}
