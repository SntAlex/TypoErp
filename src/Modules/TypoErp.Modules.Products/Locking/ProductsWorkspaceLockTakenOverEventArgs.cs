namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Аргументы события принудительного перехвата блокировки раздела «Товары» администратором.
///     Используется UI-компонентами для реактивного отображения блокирующего баннера у вытесненного работника.
/// </summary>
public sealed class ProductsWorkspaceLockTakenOverEventArgs(
    ProductsWorkspaceLockInfo previousLock,
    ProductsWorkspaceLockInfo newLock,
    DateTimeOffset takenOverAtUtc,
    string? reason = null
) : EventArgs
{
    public ProductsWorkspaceLockInfo PreviousLock { get; } = previousLock;

    public ProductsWorkspaceLockInfo NewLock { get; } = newLock;

    public DateTimeOffset TakenOverAtUtc { get; } = takenOverAtUtc;

    public string? Reason { get; } = reason;
}
