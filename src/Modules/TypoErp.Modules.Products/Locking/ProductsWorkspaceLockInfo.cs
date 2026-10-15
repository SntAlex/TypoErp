namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Сведения о текущей активной сессионной блокировке раздела «Товары».
/// </summary>
public sealed record ProductsWorkspaceLockInfo(
    Guid LockId,
    string SessionId,
    Guid UserId,
    string UserDisplayName,
    DateTimeOffset AcquiredAtUtc,
    bool IsForceTakenOver = false
);
