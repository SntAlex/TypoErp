namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Результат попытки захвата блокировки раздела «Товары».
/// </summary>
public sealed record ProductsWorkspaceLockAcquireResult(
    ProductsWorkspaceLockAcquireStatus Status,
    ProductsWorkspaceLockInfo CurrentLock
)
{
    public bool IsAcquired =>
        Status
            is ProductsWorkspaceLockAcquireStatus.Acquired
                or ProductsWorkspaceLockAcquireStatus.AlreadyHeldBySession;
}
