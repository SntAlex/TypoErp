using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TypoErp.Modules.Products.Locking;
using TypoErp.Web.Workspace;

namespace TypoErp.Web.Tests.Workspace;

public sealed class ProductsWorkspaceCircuitSessionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_ShouldAcquireLockAndReleaseAutomaticallyOnCircuitDispose()
    {
        var timeProvider = new FakeTimeProvider(FixedNow);
        var lockService = new ProductsWorkspaceLockService(timeProvider);

        var workerId = Guid.NewGuid();
        string capturedSessionId;

        using (var circuitSession = new ProductsWorkspaceCircuitSession(lockService))
        {
            capturedSessionId = circuitSession.SessionId;
            capturedSessionId.ShouldNotBeNullOrWhiteSpace();
            circuitSession.IsHoldingLock.ShouldBeFalse();

            var acquireResult = circuitSession.TryAcquire(workerId, "Антон Печатник");

            acquireResult.IsAcquired.ShouldBeTrue();
            circuitSession.IsHoldingLock.ShouldBeTrue();
            circuitSession.CurrentLock.ShouldNotBeNull();
            circuitSession.CurrentLock.UserDisplayName.ShouldBe("Антон Печатник");
            lockService.IsHeldBySession(capturedSessionId).ShouldBeTrue();
        }

        // После закрытия вкладки (Dispose сессии) блокировка автоматически снимается
        lockService.GetCurrentLock().ShouldBeNull();
        lockService.IsHeldBySession(capturedSessionId).ShouldBeFalse();
    }

    [Fact]
    public void ForceTakeover_ByAdminSession_ShouldNotifyDisplacedWorkerSessionAndPreserveAdminLockOnWorkerDispose()
    {
        var timeProvider = new FakeTimeProvider(FixedNow);
        var lockService = new ProductsWorkspaceLockService(timeProvider);

        var workerSession = new ProductsWorkspaceCircuitSession(lockService);
        var adminSession = new ProductsWorkspaceCircuitSession(lockService);

        var workerId = Guid.NewGuid();
        var adminId = Guid.NewGuid();

        var workerStateChangeCount = 0;
        workerSession.StateChanged += () => workerStateChangeCount++;

        workerSession.TryAcquire(workerId, "Оператор Иван").IsAcquired.ShouldBeTrue();
        workerSession.IsHoldingLock.ShouldBeTrue();
        workerSession.TakeoverAlert.ShouldBeNull();

        timeProvider.Advance(TimeSpan.FromMinutes(3));

        var takeoverResult = adminSession.ForceTakeover(
            adminId,
            "Администратор Ольга",
            "Срочная правка тиража"
        );

        takeoverResult.WasTakenOverFromAnotherSession.ShouldBeTrue();
        adminSession.IsHoldingLock.ShouldBeTrue();

        // У вытесненного работника мгновенно появляется баннер о перехвате управления
        workerSession.IsHoldingLock.ShouldBeFalse();
        workerSession.TakeoverAlert.ShouldNotBeNull();
        workerSession.TakeoverAlert.TakenOverByDisplayName.ShouldBe("Администратор Ольга");
        workerSession.TakeoverAlert.Reason.ShouldBe("Срочная правка тиража");
        workerSession.TakeoverAlert.TakenOverAtUtc.ShouldBe(FixedNow.AddMinutes(3));
        workerStateChangeCount.ShouldBeGreaterThanOrEqualTo(2);

        // Когда вытесненный работник закрывает свою вкладку, лок администратора НЕ сбрасывается
        workerSession.Dispose();
        adminSession.IsHoldingLock.ShouldBeTrue();
        lockService.IsHeldBySession(adminSession.SessionId).ShouldBeTrue();

        adminSession.Dispose();
        lockService.GetCurrentLock().ShouldBeNull();
    }

    [Fact]
    public void DismissTakeoverAlert_ShouldClearBannerWhileKeepingReadOnlyState()
    {
        var lockService = new ProductsWorkspaceLockService(new FakeTimeProvider(FixedNow));
        using var workerSession = new ProductsWorkspaceCircuitSession(lockService);
        using var adminSession = new ProductsWorkspaceCircuitSession(lockService);

        workerSession.TryAcquire(Guid.NewGuid(), "Работник 1");
        adminSession.ForceTakeover(Guid.NewGuid(), "Админ", "Проверка");

        workerSession.TakeoverAlert.ShouldNotBeNull();
        workerSession.DismissTakeoverAlert();

        workerSession.TakeoverAlert.ShouldBeNull();
        workerSession.IsHoldingLock.ShouldBeFalse();
    }
}
