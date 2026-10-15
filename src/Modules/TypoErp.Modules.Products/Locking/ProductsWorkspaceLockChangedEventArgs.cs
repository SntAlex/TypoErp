namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Аргументы события любого изменения состояния блокировки раздела «Товары» (захват, перехват, освобождение).
/// </summary>
public sealed class ProductsWorkspaceLockChangedEventArgs(
    ProductsWorkspaceLockInfo? previousLock,
    ProductsWorkspaceLockInfo? currentLock,
    DateTimeOffset changedAtUtc
) : EventArgs
{
    public ProductsWorkspaceLockInfo? PreviousLock { get; } = previousLock;

    public ProductsWorkspaceLockInfo? CurrentLock { get; } = currentLock;

    public DateTimeOffset ChangedAtUtc { get; } = changedAtUtc;
}
