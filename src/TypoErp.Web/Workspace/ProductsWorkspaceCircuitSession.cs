using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using TypoErp.Modules.Products.Locking;
using TypoErp.Web.Security;

namespace TypoErp.Web.Workspace;

/// <summary>
///     Реализация Scoped-координатора блокировки раздела «Товары» для Blazor Interactive Server.
///     Использует устойчивый идентификатор сессии из Cookie пользователя, благодаря чему обновление страницы (F5)
///     не сбрасывает захваченную блокировку раздела.
/// </summary>
public sealed class ProductsWorkspaceCircuitSession : IProductsWorkspaceCircuitSession
{
    private readonly IProductsWorkspaceLockService _lockService;
    private bool _disposed;

    public ProductsWorkspaceCircuitSession(
        IProductsWorkspaceLockService lockService,
        IHttpContextAccessor? httpContextAccessor = null,
        string? sessionId = null
    )
    {
        ArgumentNullException.ThrowIfNull(lockService);
        _lockService = lockService;

        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            SessionId = sessionId.Trim();
        }
        else if (
            httpContextAccessor?.HttpContext?.User.GetWorkspaceSessionId() is
            { Length: > 0 } cookieSessionId
        )
        {
            SessionId = cookieSessionId;
        }
        else
        {
            SessionId = $"circuit-{Guid.NewGuid():N}";
        }

        _lockService.LockStateChanged += OnLockStateChanged;
        _lockService.LockTakenOver += OnLockTakenOver;
    }

    public string SessionId { get; private set; }

    public ProductsWorkspaceLockInfo? CurrentLock => _lockService.GetCurrentLock();

    public bool IsHoldingLock => _lockService.IsHeldBySession(SessionId);

    public WorkspaceTakeoverAlert? TakeoverAlert { get; private set; }

    public event Action? StateChanged;

    public void SynchronizeSession(ClaimsPrincipal? user)
    {
        if (_disposed)
            return;

        var resolvedSessionId = user.GetWorkspaceSessionId();
        if (
            string.IsNullOrWhiteSpace(resolvedSessionId)
            || string.Equals(SessionId, resolvedSessionId, StringComparison.Ordinal)
        )
            return;

        SessionId = resolvedSessionId;
        NotifyStateChanged();
    }

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
