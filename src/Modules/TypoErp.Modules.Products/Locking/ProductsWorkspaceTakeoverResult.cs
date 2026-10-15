namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Результат принудительного перехвата блокировки раздела «Товары» администратором.
/// </summary>
public sealed record ProductsWorkspaceTakeoverResult(
    ProductsWorkspaceLockInfo? PreviousLock,
    ProductsWorkspaceLockInfo NewLock,
    DateTimeOffset TakenOverAtUtc,
    string? Reason = null
)
{
    public bool WasTakenOverFromAnotherSession =>
        PreviousLock is not null
        && !string.Equals(PreviousLock.SessionId, NewLock.SessionId, StringComparison.Ordinal);
}
