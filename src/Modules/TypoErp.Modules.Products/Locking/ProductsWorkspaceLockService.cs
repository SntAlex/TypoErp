using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TypoErp.Modules.Products.Locking;

/// <summary>
///     Потокобезопасная реализация сервиса сессионной блокировки раздела «Товары».
/// </summary>
public sealed class ProductsWorkspaceLockService : IProductsWorkspaceLockService
{
    private readonly ILogger<ProductsWorkspaceLockService> _logger;
    private readonly Lock _syncRoot = new();
    private readonly TimeProvider _timeProvider;
    private ProductsWorkspaceLockInfo? _currentLock;

    public ProductsWorkspaceLockService(
        TimeProvider? timeProvider = null,
        ILogger<ProductsWorkspaceLockService>? logger = null
    )
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<ProductsWorkspaceLockService>.Instance;
    }

    public event EventHandler<ProductsWorkspaceLockTakenOverEventArgs>? LockTakenOver;

    public event EventHandler<ProductsWorkspaceLockChangedEventArgs>? LockStateChanged;

    public ProductsWorkspaceLockInfo? GetCurrentLock()
    {
        lock (_syncRoot)
        {
            return _currentLock;
        }
    }

    public bool IsHeldBySession(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        lock (_syncRoot)
        {
            return _currentLock is not null
                && string.Equals(
                    _currentLock.SessionId,
                    sessionId.Trim(),
                    StringComparison.Ordinal
                );
        }
    }

    public void EnsureHeldBySession(string sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var normalizedSessionId = sessionId.Trim();

        lock (_syncRoot)
        {
            if (
                _currentLock is not null
                && string.Equals(
                    _currentLock.SessionId,
                    normalizedSessionId,
                    StringComparison.Ordinal
                )
            )
                return;

            _logger.LogWarning(
                "Rejected products workspace operation for session '{AttemptedSessionId}'. Active lock owner: '{OwnerDisplayName}' (session '{OwnerSessionId}')",
                normalizedSessionId,
                _currentLock?.UserDisplayName ?? "<none>",
                _currentLock?.SessionId ?? "<none>"
            );

            throw new ProductsWorkspaceLockException(normalizedSessionId, _currentLock);
        }
    }

    public ProductsWorkspaceLockAcquireResult TryAcquire(
        string sessionId,
        Guid userId,
        string userDisplayName
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userDisplayName);
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId cannot be Guid.Empty.", nameof(userId));

        var normalizedSessionId = sessionId.Trim();
        var trimmedDisplayName = userDisplayName.Trim();
        var now = _timeProvider.GetUtcNow();

        ProductsWorkspaceLockInfo acquiredLock;
        lock (_syncRoot)
        {
            if (_currentLock is not null)
            {
                if (
                    string.Equals(
                        _currentLock.SessionId,
                        normalizedSessionId,
                        StringComparison.Ordinal
                    )
                )
                    return new ProductsWorkspaceLockAcquireResult(
                        ProductsWorkspaceLockAcquireStatus.AlreadyHeldBySession,
                        _currentLock
                    );

                _logger.LogInformation(
                    "Session '{SessionId}' (user '{UserDisplayName}', {UserId}) could not acquire products workspace lock: already held by '{OwnerDisplayName}' ({OwnerUserId}, session '{OwnerSessionId}')",
                    normalizedSessionId,
                    trimmedDisplayName,
                    userId,
                    _currentLock.UserDisplayName,
                    _currentLock.UserId,
                    _currentLock.SessionId
                );

                return new ProductsWorkspaceLockAcquireResult(
                    ProductsWorkspaceLockAcquireStatus.LockedByAnotherSession,
                    _currentLock
                );
            }

            acquiredLock = new ProductsWorkspaceLockInfo(
                Guid.NewGuid(),
                normalizedSessionId,
                userId,
                trimmedDisplayName,
                now
            );
            _currentLock = acquiredLock;

            _logger.LogInformation(
                "Products workspace lock acquired by '{UserDisplayName}' ({UserId}, session '{SessionId}', LockId={LockId})",
                acquiredLock.UserDisplayName,
                acquiredLock.UserId,
                acquiredLock.SessionId,
                acquiredLock.LockId
            );
        }

        RaiseLockStateChanged(new ProductsWorkspaceLockChangedEventArgs(null, acquiredLock, now));

        return new ProductsWorkspaceLockAcquireResult(
            ProductsWorkspaceLockAcquireStatus.Acquired,
            acquiredLock
        );
    }

    public ProductsWorkspaceTakeoverResult ForceTakeover(
        string adminSessionId,
        Guid adminUserId,
        string adminDisplayName,
        string? reason = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adminSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminDisplayName);
        if (adminUserId == Guid.Empty)
            throw new ArgumentException("AdminUserId cannot be Guid.Empty.", nameof(adminUserId));

        var normalizedAdminSessionId = adminSessionId.Trim();
        var trimmedAdminName = adminDisplayName.Trim();
        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var now = _timeProvider.GetUtcNow();

        ProductsWorkspaceLockInfo? previousLock;
        ProductsWorkspaceLockInfo newLock;
        bool wasTakenOverFromAnotherSession;

        lock (_syncRoot)
        {
            previousLock = _currentLock;
            wasTakenOverFromAnotherSession =
                previousLock is not null
                && !string.Equals(
                    previousLock.SessionId,
                    normalizedAdminSessionId,
                    StringComparison.Ordinal
                );

            newLock = new ProductsWorkspaceLockInfo(
                Guid.NewGuid(),
                normalizedAdminSessionId,
                adminUserId,
                trimmedAdminName,
                now,
                IsForceTakenOver: wasTakenOverFromAnotherSession
            );

            _currentLock = newLock;

            if (wasTakenOverFromAnotherSession && previousLock is not null)
                _logger.LogWarning(
                    "Administrator '{AdminDisplayName}' ({AdminUserId}, session '{AdminSessionId}') forcefully took over products workspace lock from '{PreviousUserDisplayName}' ({PreviousUserId}, session '{PreviousSessionId}'). Reason: {Reason}",
                    newLock.UserDisplayName,
                    newLock.UserId,
                    newLock.SessionId,
                    previousLock.UserDisplayName,
                    previousLock.UserId,
                    previousLock.SessionId,
                    trimmedReason ?? "<not specified>"
                );
            else
                _logger.LogInformation(
                    "Administrator '{AdminDisplayName}' ({AdminUserId}, session '{AdminSessionId}') acquired products workspace lock via ForceTakeover",
                    newLock.UserDisplayName,
                    newLock.UserId,
                    newLock.SessionId
                );
        }

        if (wasTakenOverFromAnotherSession && previousLock is not null)
            RaiseLockTakenOver(
                new ProductsWorkspaceLockTakenOverEventArgs(
                    previousLock,
                    newLock,
                    now,
                    trimmedReason
                )
            );

        RaiseLockStateChanged(
            new ProductsWorkspaceLockChangedEventArgs(previousLock, newLock, now)
        );

        return new ProductsWorkspaceTakeoverResult(previousLock, newLock, now, trimmedReason);
    }

    public bool Release(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
            return false;

        var normalizedSessionId = sessionId.Trim();
        var now = _timeProvider.GetUtcNow();
        ProductsWorkspaceLockInfo? releasedLock;

        lock (_syncRoot)
        {
            if (
                _currentLock is null
                || !string.Equals(
                    _currentLock.SessionId,
                    normalizedSessionId,
                    StringComparison.Ordinal
                )
            )
                return false;

            releasedLock = _currentLock;
            _currentLock = null;

            _logger.LogInformation(
                "Products workspace lock released by '{UserDisplayName}' ({UserId}, session '{SessionId}', LockId={LockId})",
                releasedLock.UserDisplayName,
                releasedLock.UserId,
                releasedLock.SessionId,
                releasedLock.LockId
            );
        }

        RaiseLockStateChanged(new ProductsWorkspaceLockChangedEventArgs(releasedLock, null, now));
        return true;
    }

    private void RaiseLockTakenOver(ProductsWorkspaceLockTakenOverEventArgs args)
    {
        var handler = LockTakenOver;
        if (handler is null)
            return;

        foreach (
            var singleHandler in handler
                .GetInvocationList()
                .Cast<EventHandler<ProductsWorkspaceLockTakenOverEventArgs>>()
        )
            try
            {
                singleHandler(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error while notifying subscriber about products workspace lock takeover"
                );
            }
    }

    private void RaiseLockStateChanged(ProductsWorkspaceLockChangedEventArgs args)
    {
        var handler = LockStateChanged;
        if (handler is null)
            return;

        foreach (
            var singleHandler in handler
                .GetInvocationList()
                .Cast<EventHandler<ProductsWorkspaceLockChangedEventArgs>>()
        )
            try
            {
                singleHandler(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Error while notifying subscriber about products workspace lock state change"
                );
            }
    }
}
