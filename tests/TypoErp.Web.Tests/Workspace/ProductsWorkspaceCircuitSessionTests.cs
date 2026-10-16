using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;
using TypoErp.Modules.Products.Locking;
using TypoErp.Web.Security;
using TypoErp.Web.Workspace;

namespace TypoErp.Web.Tests.Workspace;

public sealed class ProductsWorkspaceCircuitSessionTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TryAcquire_WhenPageReloadedWithF5_ShouldPreserveLockAcrossCircuitRecreationsUntilExplicitRelease()
    {
        var timeProvider = new FakeTimeProvider(FixedNow);
        var lockService = new ProductsWorkspaceLockService(timeProvider);

        var workerId = Guid.NewGuid();
        const string persistentCookieSessionId = "session-worker-browser-1";

        // 1. Первая загрузка страницы: работник захватывает блокировку
        using (
            var circuitBeforeF5 = new ProductsWorkspaceCircuitSession(
                lockService,
                sessionId: persistentCookieSessionId
            )
        )
        {
            circuitBeforeF5.SessionId.ShouldBe(persistentCookieSessionId);
            circuitBeforeF5.IsHoldingLock.ShouldBeFalse();

            var acquireResult = circuitBeforeF5.TryAcquire(workerId, "Антон Печатник");

            acquireResult.IsAcquired.ShouldBeTrue();
            circuitBeforeF5.IsHoldingLock.ShouldBeTrue();
            circuitBeforeF5.CurrentLock.ShouldNotBeNull();
            circuitBeforeF5.CurrentLock.UserDisplayName.ShouldBe("Антон Печатник");
        }

        // 2. Сработал Dispose старого Circuit при нажатии F5 — блокировка НЕ должна сбрасываться
        lockService.IsHeldBySession(persistentCookieSessionId).ShouldBeTrue();
        lockService.GetCurrentLock().ShouldNotBeNull();

        // 3. Новый Circuit после F5 подхватывает ту же сессию работника и видит активный лок
        using (
            var circuitAfterF5 = new ProductsWorkspaceCircuitSession(
                lockService,
                sessionId: persistentCookieSessionId
            )
        )
        {
            circuitAfterF5.IsHoldingLock.ShouldBeTrue();
            circuitAfterF5.CurrentLock.ShouldNotBeNull();
            circuitAfterF5.CurrentLock.UserId.ShouldBe(workerId);

            // Явное освобождение кнопкой «Освободить раздел» снимает блокировку
            circuitAfterF5.Release().ShouldBeTrue();
            circuitAfterF5.IsHoldingLock.ShouldBeFalse();
        }

        lockService.GetCurrentLock().ShouldBeNull();
    }

    [Fact]
    public void SynchronizeSession_WhenInitializedFromClaimsPrincipal_ShouldAdoptCookieSessionIdAndKeepLockOnF5()
    {
        var lockService = new ProductsWorkspaceLockService(new FakeTimeProvider(FixedNow));
        var worker = new AppUser(
            "operator1",
            "Оператор 1",
            "hash",
            UserRole.Worker,
            FixedNow,
            [AppPermission.ProductsView, AppPermission.ProductsEdit]
        );
        var principal = TypoErpClaimsPrincipalFactory.Create(
            worker,
            worker.GetEffectivePermissions(),
            sessionId: "cookie-session-abc"
        );

        using (var firstCircuit = new ProductsWorkspaceCircuitSession(lockService))
        {
            firstCircuit.SynchronizeSession(principal);
            firstCircuit.SessionId.ShouldBe("cookie-session-abc");

            firstCircuit.TryAcquire(worker.Id, worker.DisplayName).IsAcquired.ShouldBeTrue();
            firstCircuit.IsHoldingLock.ShouldBeTrue();
        }

        using (var secondCircuitAfterF5 = new ProductsWorkspaceCircuitSession(lockService))
        {
            secondCircuitAfterF5.SynchronizeSession(principal);
            secondCircuitAfterF5.SessionId.ShouldBe("cookie-session-abc");
            secondCircuitAfterF5.IsHoldingLock.ShouldBeTrue();
        }
    }

    [Fact]
    public void ForceTakeover_ByAdminSession_ShouldNotifyDisplacedWorkerSessionAndPreserveAdminLockOnWorkerDispose()
    {
        var timeProvider = new FakeTimeProvider(FixedNow);
        var lockService = new ProductsWorkspaceLockService(timeProvider);

        var workerSession = new ProductsWorkspaceCircuitSession(
            lockService,
            sessionId: "session-worker"
        );
        var adminSession = new ProductsWorkspaceCircuitSession(
            lockService,
            sessionId: "session-admin"
        );

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

        // Когда вытесненный работник обновляет/закрывает вкладку, лок администратора сохраняется
        workerSession.Dispose();
        adminSession.IsHoldingLock.ShouldBeTrue();
        lockService.IsHeldBySession(adminSession.SessionId).ShouldBeTrue();

        adminSession.Release().ShouldBeTrue();
        adminSession.Dispose();
        lockService.GetCurrentLock().ShouldBeNull();
    }

    [Fact]
    public void DismissTakeoverAlert_ShouldClearBannerWhileKeepingReadOnlyState()
    {
        var lockService = new ProductsWorkspaceLockService(new FakeTimeProvider(FixedNow));
        using var workerSession = new ProductsWorkspaceCircuitSession(
            lockService,
            sessionId: "session-worker"
        );
        using var adminSession = new ProductsWorkspaceCircuitSession(
            lockService,
            sessionId: "session-admin"
        );

        workerSession.TryAcquire(Guid.NewGuid(), "Работник 1");
        adminSession.ForceTakeover(Guid.NewGuid(), "Админ", "Проверка");

        workerSession.TakeoverAlert.ShouldNotBeNull();
        workerSession.DismissTakeoverAlert();

        workerSession.TakeoverAlert.ShouldBeNull();
        workerSession.IsHoldingLock.ShouldBeFalse();
    }
}
