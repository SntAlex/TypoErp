using Shouldly;
using TypoErp.Modules.Products.Locking;

namespace TypoErp.Modules.Products.Tests.Locking;

public sealed class ProductsWorkspaceLockServiceTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_WhenWorkspaceIsUnlocked_ShouldAcquireLockAndNotifyStateChanged()
    {
        var timeProvider = new FakeTimeProvider(FixedNow);
        var service = new ProductsWorkspaceLockService(timeProvider);
        var workerId = Guid.NewGuid();
        ProductsWorkspaceLockChangedEventArgs? changedEvent = null;
        service.LockStateChanged += (_, args) => changedEvent = args;

        var result = service.TryAcquire("session-1", workerId, "Иван Печатников");

        result.IsAcquired.ShouldBeTrue();
        result.Status.ShouldBe(ProductsWorkspaceLockAcquireStatus.Acquired);
        result.CurrentLock.SessionId.ShouldBe("session-1");
        result.CurrentLock.UserId.ShouldBe(workerId);
        result.CurrentLock.UserDisplayName.ShouldBe("Иван Печатников");
        result.CurrentLock.AcquiredAtUtc.ShouldBe(FixedNow);
        result.CurrentLock.IsForceTakenOver.ShouldBeFalse();

        service.IsHeldBySession("session-1").ShouldBeTrue();
        service.GetCurrentLock().ShouldBe(result.CurrentLock);

        changedEvent.ShouldNotBeNull();
        changedEvent.PreviousLock.ShouldBeNull();
        changedEvent.CurrentLock.ShouldBe(result.CurrentLock);
    }

    [Fact]
    public void TryAcquire_WhenCalledAgainBySameSession_ShouldReturnAlreadyHeldBySession()
    {
        var service = new ProductsWorkspaceLockService(new FakeTimeProvider(FixedNow));
        var workerId = Guid.NewGuid();

        var first = service.TryAcquire("session-1", workerId, "Иван Печатников");
        var second = service.TryAcquire("session-1", workerId, "Иван Печатников");

        second.IsAcquired.ShouldBeTrue();
        second.Status.ShouldBe(ProductsWorkspaceLockAcquireStatus.AlreadyHeldBySession);
        second.CurrentLock.LockId.ShouldBe(first.CurrentLock.LockId);
    }

    [Fact]
    public void TryAcquire_WhenLockedByAnotherSession_ShouldRejectAndReturnCurrentOwnerInfo()
    {
        var service = new ProductsWorkspaceLockService(new FakeTimeProvider(FixedNow));
        var worker1Id = Guid.NewGuid();
        var worker2Id = Guid.NewGuid();

        var first = service.TryAcquire("session-1", worker1Id, "Работник 1");
        var second = service.TryAcquire("session-2", worker2Id, "Работник 2");

        second.IsAcquired.ShouldBeFalse();
        second.Status.ShouldBe(ProductsWorkspaceLockAcquireStatus.LockedByAnotherSession);
        second.CurrentLock.ShouldBe(first.CurrentLock);

        service.IsHeldBySession("session-1").ShouldBeTrue();
        service.IsHeldBySession("session-2").ShouldBeFalse();
    }

    [Fact]
    public void ForceTakeover_WhenLockedByWorker_ShouldTransferLockRaiseTakeoverEventAndBlockPreviousWorker()
    {
        var timeProvider = new FakeTimeProvider(FixedNow);
        var service = new ProductsWorkspaceLockService(timeProvider);
        var workerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var workerAcquire = service.TryAcquire("worker-session", workerId, "Оператор Антон");

        ProductsWorkspaceLockTakenOverEventArgs? takeoverEvent = null;
        ProductsWorkspaceLockChangedEventArgs? changedEvent = null;
        service.LockTakenOver += (_, args) => takeoverEvent = args;
        service.LockStateChanged += (_, args) => changedEvent = args;

        var takeoverTime = FixedNow.AddMinutes(5);
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        var takeoverResult = service.ForceTakeover(
            "admin-session",
            adminId,
            "Администратор Ольга",
            "Срочная выгрузка тиража"
        );

        takeoverResult.WasTakenOverFromAnotherSession.ShouldBeTrue();
        takeoverResult.PreviousLock.ShouldBe(workerAcquire.CurrentLock);
        takeoverResult.NewLock.SessionId.ShouldBe("admin-session");
        takeoverResult.NewLock.UserId.ShouldBe(adminId);
        takeoverResult.NewLock.UserDisplayName.ShouldBe("Администратор Ольга");
        takeoverResult.NewLock.AcquiredAtUtc.ShouldBe(takeoverTime);
        takeoverResult.NewLock.IsForceTakenOver.ShouldBeTrue();
        takeoverResult.Reason.ShouldBe("Срочная выгрузка тиража");

        // Событие принудительного перехвата должно быть доставлено предыдущему работнику
        takeoverEvent.ShouldNotBeNull();
        takeoverEvent.PreviousLock.SessionId.ShouldBe("worker-session");
        takeoverEvent.NewLock.SessionId.ShouldBe("admin-session");
        takeoverEvent.Reason.ShouldBe("Срочная выгрузка тиража");
        takeoverEvent.TakenOverAtUtc.ShouldBe(takeoverTime);

        changedEvent.ShouldNotBeNull();
        changedEvent.PreviousLock.ShouldBe(workerAcquire.CurrentLock);
        changedEvent.CurrentLock.ShouldBe(takeoverResult.NewLock);

        // Предыдущий работник мгновенно теряет доступ и блокируется при проверке
        service.IsHeldBySession("worker-session").ShouldBeFalse();
        service.IsHeldBySession("admin-session").ShouldBeTrue();

        var ex = Should.Throw<ProductsWorkspaceLockException>(() =>
            service.EnsureHeldBySession("worker-session")
        );
        ex.AttemptedSessionId.ShouldBe("worker-session");
        ex.CurrentLock.ShouldBe(takeoverResult.NewLock);

        Should.NotThrow(() => service.EnsureHeldBySession("admin-session"));
    }

    [Fact]
    public void Release_WhenCalledByEvictedWorkerAfterAdminTakeover_ShouldNotReleaseAdminLock()
    {
        var service = new ProductsWorkspaceLockService(new FakeTimeProvider(FixedNow));
        service.TryAcquire("worker-session", Guid.NewGuid(), "Оператор Антон");
        service.ForceTakeover("admin-session", Guid.NewGuid(), "Администратор Ольга");

        // Вытесненный работник закрывает вкладку (срабатывает Dispose его компонента)
        var releasedByEvictedWorker = service.Release("worker-session");

        releasedByEvictedWorker.ShouldBeFalse();
        service.IsHeldBySession("admin-session").ShouldBeTrue();
        service.GetCurrentLock().ShouldNotBeNull();

        // Сам администратор успешно освобождает лок
        var releasedByAdmin = service.Release("admin-session");
        releasedByAdmin.ShouldBeTrue();
        service.GetCurrentLock().ShouldBeNull();
    }

    [Fact]
    public void ForceTakeover_WhenOneEventSubscriberThrows_ShouldStillCompleteTakeoverAndNotifyOtherSubscribers()
    {
        var service = new ProductsWorkspaceLockService(new FakeTimeProvider(FixedNow));
        service.TryAcquire("worker-session", Guid.NewGuid(), "Оператор Антон");

        var secondSubscriberCalled = false;
        service.LockTakenOver += (_, _) =>
            throw new InvalidOperationException("Simulated disconnected circuit error");
        service.LockTakenOver += (_, _) => secondSubscriberCalled = true;

        var result = Should.NotThrow(() =>
            service.ForceTakeover("admin-session", Guid.NewGuid(), "Администратор")
        );

        result.WasTakenOverFromAnotherSession.ShouldBeTrue();
        secondSubscriberCalled.ShouldBeTrue();
        service.IsHeldBySession("admin-session").ShouldBeTrue();
    }

    [Fact]
    public async Task ConcurrentTryAcquire_WhenMultipleSessionsCompete_ShouldAllowOnlySingleWinner()
    {
        var service = new ProductsWorkspaceLockService(new FakeTimeProvider(FixedNow));
        const int contenders = 40;

        var tasks = Enumerable
            .Range(1, contenders)
            .Select(i =>
                Task.Run(() => service.TryAcquire($"session-{i}", Guid.NewGuid(), $"Работник {i}"))
            );

        var results = await Task.WhenAll(tasks);

        var winners = results
            .Where(r => r.Status == ProductsWorkspaceLockAcquireStatus.Acquired)
            .ToList();
        var rejected = results
            .Where(r => r.Status == ProductsWorkspaceLockAcquireStatus.LockedByAnotherSession)
            .ToList();

        winners.ShouldHaveSingleItem();
        rejected.Count.ShouldBe(contenders - 1);
        service.GetCurrentLock().ShouldBe(winners[0].CurrentLock);
    }

    private sealed class FakeTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
