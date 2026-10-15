using Shouldly;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;
using TypoErp.Web.Security;

namespace TypoErp.Web.Tests.Security;

public sealed class TypoErpClaimsPrincipalFactoryTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreatePrincipal_ForWorkerWithDelegatedPermissions_ShouldPopulateClaimsAndPermissionsCorrectly()
    {
        var worker = new AppUser(
            "ivan.operator",
            "Иван Оператор",
            "hash",
            UserRole.Worker,
            FixedNow,
            [AppPermission.ProductsView, AppPermission.ProductsEdit, AppPermission.AssemblyView]
        );

        var principal = TypoErpClaimsPrincipalFactory.Create(
            worker,
            worker.GetEffectivePermissions()
        );

        principal.IsAuthenticated().ShouldBeTrue();
        principal.GetUserId().ShouldBe(worker.Id);
        principal.GetUsername().ShouldBe("ivan.operator");
        principal.GetDisplayName().ShouldBe("Иван Оператор");
        principal.GetUserRole().ShouldBe(UserRole.Worker);
        principal.IsAdministrator().ShouldBeFalse();

        principal.HasPermission(AppPermission.ProductsView).ShouldBeTrue();
        principal.HasPermission(AppPermission.ProductsEdit).ShouldBeTrue();
        principal.HasPermission(AppPermission.AssemblyView).ShouldBeTrue();

        principal.HasPermission(AppPermission.ProductsSync).ShouldBeFalse();
        principal.HasPermission(AppPermission.ProductsForceUnlock).ShouldBeFalse();
        principal.HasPermission(AppPermission.UsersManage).ShouldBeFalse();
        principal.CanForceTakeoverProductsLock().ShouldBeFalse();
    }

    [Fact]
    public void CreatePrincipal_ForAdministrator_ShouldGrantAllPermissionsAndForceTakeover()
    {
        var admin = new AppUser(
            "admin",
            "Главный Администратор",
            "hash",
            UserRole.Administrator,
            FixedNow
        );

        var principal = TypoErpClaimsPrincipalFactory.Create(
            admin,
            admin.GetEffectivePermissions()
        );

        principal.IsAuthenticated().ShouldBeTrue();
        principal.GetUserId().ShouldBe(admin.Id);
        principal.GetDisplayName().ShouldBe("Главный Администратор");
        principal.GetUserRole().ShouldBe(UserRole.Administrator);
        principal.IsAdministrator().ShouldBeTrue();
        principal.CanForceTakeoverProductsLock().ShouldBeTrue();

        foreach (var permission in AppPermissions.All)
            principal.HasPermission(permission).ShouldBeTrue();
    }

    [Fact]
    public void CanForceTakeoverProductsLock_ForWorkerWithDelegatedForceUnlockPermission_ShouldReturnTrue()
    {
        var seniorWorker = new AppUser(
            "senior.worker",
            "Старший Смены",
            "hash",
            UserRole.Worker,
            FixedNow,
            [AppPermission.ProductsView, AppPermission.ProductsForceUnlock]
        );

        var principal = TypoErpClaimsPrincipalFactory.Create(
            seniorWorker,
            seniorWorker.GetEffectivePermissions()
        );

        principal.IsAdministrator().ShouldBeFalse();
        principal.CanForceTakeoverProductsLock().ShouldBeTrue();
    }
}
