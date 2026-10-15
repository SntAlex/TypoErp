using TypoErp.Modules.Products.Locking;

namespace TypoErp.Web.Workspace;

/// <summary>
///     Реализация Scoped-сессии блокировки раздела «Товары» для Blazor Interactive Server.
/// </summary>
public sealed class ProductsWorkspaceCircuitSession : IProductsWorkspaceCircuitSession
{
    private readonly IProductsWorkspaceLockService _lockService;
    private bool _disposed;

    public ProductsWorkspaceCircuitSession(IProductsWorkspaceLockService lockService)
    {
        ArgumentNullException.ThrowIfNull(lockService);
        _lockService = lockService;
        SessionId = $"circuit-{Guid.NewGuid():N}";

        _lockService.LockStateChanged += OnLockStateChanged;
        _lockService.LockTakenOver += OnLockTakenOver;
    }

    public string SessionId { get; }

    public ProductsWorkspaceLockInfo? CurrentLock => _lockService.GetCurrentLock();

    public bool IsHoldingLock => _lockService.IsHeldBySession(SessionId);

    public WorkspaceTakeoverAlert? TakeoverAlert { get; private set; }

    public event Action? StateChanged;

    public ProductsWorkspaceLockAcquireResult TryAcquire(Guid userId, string userDisplayName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var result = _lockService.TryAcquire(SessionId, userId, userDisplayName);
        if (result.IsAcquired)
            TakeoverAlert = null;

        NotifyStateChanged();
        return result;
    }

    public ProductsWorkspaceTakeoverResult ForceTakeover(
        Guid adminUserId,
        string adminDisplayName,
        string? reason = null
    )
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        TakeoverAlert = null;
        var result = _lockService.ForceTakeover(SessionId, adminUserId, adminDisplayName, reason);
        NotifyStateChanged();
        return result;
    }

    public bool Release()
    {
        if (_disposed)
            return false;

        var released = _lockService.Release(SessionId);
        if (released)
            NotifyStateChanged();

        return released;
    }

    public void DismissTakeoverAlert()
    {
        if (TakeoverAlert is null)
            return;

        TakeoverAlert = null;
        NotifyStateChanged();
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _lockService.LockStateChanged -= OnLockStateChanged;
        _lockService.LockTakenOver -= OnLockTakenOver;

        // Если вкладка работника закрылась, автоматически освобождаем удерживаемую ей блокировку
        _lockService.Release(SessionId);
    }

    private void OnLockTakenOver(object? sender, ProductsWorkspaceLockTakenOverEventArgs e)
    {
        if (_disposed)
            return;

        if (string.Equals(e.PreviousLock.SessionId, SessionId, StringComparison.Ordinal))
        {
            TakeoverAlert = new WorkspaceTakeoverAlert(
                e.NewLock.UserDisplayName,
                e.TakenOverAtUtc,
                e.Reason
            );
        }

        NotifyStateChanged();
    }

    private void OnLockStateChanged(object? sender, ProductsWorkspaceLockChangedEventArgs e)
    {
        if (_disposed)
            return;

        NotifyStateChanged();
    }

    private void NotifyStateChanged()
    {
        StateChanged?.Invoke();
    }
}
