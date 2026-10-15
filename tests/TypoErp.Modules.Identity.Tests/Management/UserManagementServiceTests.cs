using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TypoErp.Modules.Identity.Authentication;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Management;
using TypoErp.Modules.Identity.Permissions;
using TypoErp.Modules.Identity.Persistence;
using TypoErp.Modules.Identity.Security;

namespace TypoErp.Modules.Identity.Tests.Management;

public sealed class UserManagementServiceTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    private SqliteConnection _connection = null!;
    private DbContextOptions<IdentityDbContext> _dbOptions = null!;
    private Pbkdf2PasswordHasher _passwordHasher = null!;
    private FakeTimeProvider _timeProvider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _dbOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection)
            .Options;

        _passwordHasher = new Pbkdf2PasswordHasher();
        _timeProvider = new FakeTimeProvider(FixedNow);

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task EnsureDefaultAdminExistsAsync_ShouldCreateInitialAdminOnceAndAllowAuthentication()
    {
        AppUser admin;
        await using (var db1 = CreateDbContext())
        {
            var service = CreateService(db1);
            admin = await service.EnsureDefaultAdminExistsAsync(
                "admin",
                "Главный Администратор",
                "AdminPass123!"
            );

            // Повторный вызов идемпотентен и возвращает того же администратора
            var secondCall = await service.EnsureDefaultAdminExistsAsync(
                "admin",
                "Другое Имя",
                "AnotherPass123!"
            );
            secondCall.Id.ShouldBe(admin.Id);
        }

        admin.Role.ShouldBe(UserRole.Administrator);
        admin.IsActive.ShouldBeTrue();
        admin.GetEffectivePermissions().Count.ShouldBe(AppPermissions.All.Count);

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        await using (var authDb = CreateDbContext())
        {
            var service = CreateService(authDb);

            var successAuth = await service.AuthenticateAsync("  ADMIN ", "AdminPass123!");
            successAuth.IsSucceeded.ShouldBeTrue();
            successAuth.Status.ShouldBe(AuthenticationStatus.Succeeded);
            successAuth.User.ShouldNotBeNull();
            successAuth.User.Id.ShouldBe(admin.Id);
            successAuth.User.LastLoginAtUtc.ShouldBe(FixedNow.AddMinutes(5));
            successAuth.EffectivePermissions.Count.ShouldBe(AppPermissions.All.Count);

            var wrongPassAuth = await service.AuthenticateAsync("admin", "WrongPassword");
            wrongPassAuth.IsSucceeded.ShouldBeFalse();
            wrongPassAuth.Status.ShouldBe(AuthenticationStatus.InvalidCredentials);

            var unknownUserAuth = await service.AuthenticateAsync("unknown", "AdminPass123!");
            unknownUserAuth.IsSucceeded.ShouldBeFalse();
            unknownUserAuth.Status.ShouldBe(AuthenticationStatus.InvalidCredentials);
        }
    }

    [Fact]
    public async Task CreateUserAndDelegatePermissions_WhenCalledByAdmin_ShouldPersistAndReflectInAuthentication()
    {
        Guid adminId;
        Guid workerId;

        await using (var db = CreateDbContext())
        {
            var service = CreateService(db);
            var admin = await service.EnsureDefaultAdminExistsAsync(
                "admin",
                "Администратор Ольга",
                "AdminPass123!"
            );
            adminId = admin.Id;

            var worker = await service.CreateUserAsync(
                adminId,
                new CreateUserRequest(
                    "  anton.print ",
                    "  Антон Оператор ",
                    "WorkerPass123",
                    UserRole.Worker,
                    [AppPermission.ProductsView, AppPermission.ProductsEdit]
                )
            );

            workerId = worker.Id;
            worker.Username.ShouldBe("anton.print");
            worker.NormalizedUsername.ShouldBe("anton.print");
            worker.DisplayName.ShouldBe("Антон Оператор");
            worker.Role.ShouldBe(UserRole.Worker);
            worker.Permissions.ShouldBe([AppPermission.ProductsView, AppPermission.ProductsEdit]);
        }

        // Администратор делегирует работнику дополнительные права (включая синхронизацию и сборку)
        await using (var delegateDb = CreateDbContext())
        {
            var service = CreateService(delegateDb);
            var updated = await service.SetUserPermissionsAsync(
                adminId,
                workerId,
                [
                    AppPermission.ProductsView,
                    AppPermission.ProductsEdit,
                    AppPermission.ProductsSync,
                    AppPermission.AssemblyView,
                ]
            );

            updated.Permissions.ShouldBe([
                AppPermission.ProductsView,
                AppPermission.ProductsEdit,
                AppPermission.ProductsSync,
                AppPermission.AssemblyView,
            ]);
        }

        // Проверяем вход работника и его эффективные права
        await using (var authDb = CreateDbContext())
        {
            var service = CreateService(authDb);
            var auth = await service.AuthenticateAsync("ANTON.PRINT", "WorkerPass123");

            auth.IsSucceeded.ShouldBeTrue();
            auth.EffectivePermissions.ShouldBe([
                AppPermission.ProductsView,
                AppPermission.ProductsEdit,
                AppPermission.ProductsSync,
                AppPermission.AssemblyView,
            ]);
        }
    }

    [Fact]
    public async Task DelegatedUsersManagePermission_ShouldAllowWorkerToManageWorkersButForbidManagingAdmins()
    {
        Guid adminId;
        Guid hrWorkerId;
        Guid regularWorkerId;

        await using (var setupDb = CreateDbContext())
        {
            var service = CreateService(setupDb);
            var admin = await service.EnsureDefaultAdminExistsAsync(
                "admin",
                "Админ",
                "AdminPass123!"
            );
            adminId = admin.Id;

            // Создаем старшего оператора без прав управления пользователями
            var hrWorker = await service.CreateUserAsync(
                adminId,
                new CreateUserRequest(
                    "senior_worker",
                    "Старший Оператор",
                    "SeniorPass123",
                    UserRole.Worker,
                    [AppPermission.ProductsView]
                )
            );
            hrWorkerId = hrWorker.Id;
        }

        // 1. Пока право UsersManage не делегировано — попытка создать работника отклоняется (AccessDenied)
        await using (var deniedDb = CreateDbContext())
        {
            var service = CreateService(deniedDb);
            var ex = await Should.ThrowAsync<IdentityManagementException>(() =>
                service.CreateUserAsync(
                    hrWorkerId,
                    new CreateUserRequest("new_worker", "Новый Работник", "Pass123456")
                )
            );
            ex.ErrorCode.ShouldBe(IdentityManagementErrorCode.AccessDenied);
        }

        // 2. Администратор делегирует старшему оператору право UsersManage
        await using (var grantDb = CreateDbContext())
        {
            var service = CreateService(grantDb);
            await service.SetUserPermissionsAsync(
                adminId,
                hrWorkerId,
                [AppPermission.ProductsView, AppPermission.UsersManage]
            );
        }

        // 3. Теперь старший оператор может создавать обычных работников и делегировать им права
        await using (var manageDb = CreateDbContext())
        {
            var service = CreateService(manageDb);
            var created = await service.CreateUserAsync(
                hrWorkerId,
                new CreateUserRequest(
                    "junior_worker",
                    "Младший Оператор",
                    "JuniorPass123",
                    UserRole.Worker,
                    [AppPermission.ProductsView]
                )
            );
            regularWorkerId = created.Id;

            await service.SetUserPermissionsAsync(
                hrWorkerId,
                regularWorkerId,
                [AppPermission.ProductsView, AppPermission.ProductsEdit]
            );

            // Но не может повысить кого-либо до роли Administrator
            var promoteEx = await Should.ThrowAsync<IdentityManagementException>(() =>
                service.UpdateUserAsync(
                    hrWorkerId,
                    new UpdateUserRequest(
                        regularWorkerId,
                        "junior_worker",
                        "Младший Оператор",
                        UserRole.Administrator
                    )
                )
            );
            promoteEx.ErrorCode.ShouldBe(IdentityManagementErrorCode.AccessDenied);

            // И не может редактировать или сбрасывать пароль Администратора
            var editAdminEx = await Should.ThrowAsync<IdentityManagementException>(() =>
                service.ResetPasswordAsync(hrWorkerId, adminId, "HackedAdminPass123")
            );
            editAdminEx.ErrorCode.ShouldBe(IdentityManagementErrorCode.AccessDenied);

            // И не может случайно лишить самого себя права UsersManage
            var selfRevokeEx = await Should.ThrowAsync<IdentityManagementException>(() =>
                service.SetUserPermissionsAsync(
                    hrWorkerId,
                    hrWorkerId,
                    [AppPermission.ProductsView]
                )
            );
            selfRevokeEx.ErrorCode.ShouldBe(
                IdentityManagementErrorCode.CannotRevokeOwnUsersManagePermission
            );
        }
    }

    [Fact]
    public async Task DeactivateAndPasswordOperations_ShouldBlockDeactivatedUserLoginAndAllowPasswordChanges()
    {
        Guid adminId;
        Guid workerId;

        await using (var setupDb = CreateDbContext())
        {
            var service = CreateService(setupDb);
            var admin = await service.EnsureDefaultAdminExistsAsync(
                "admin",
                "Админ",
                "AdminPass123!"
            );
            adminId = admin.Id;

            var worker = await service.CreateUserAsync(
                adminId,
                new CreateUserRequest(
                    "operator1",
                    "Оператор 1",
                    "InitialPass123",
                    UserRole.Worker,
                    [AppPermission.ProductsView]
                )
            );
            workerId = worker.Id;
        }

        // Работник меняет свой пароль самостоятельно
        await using (var changePassDb = CreateDbContext())
        {
            var service = CreateService(changePassDb);

            var wrongCurrentEx = await Should.ThrowAsync<IdentityManagementException>(() =>
                service.ChangeOwnPasswordAsync(workerId, "WrongCurrent", "NewPass456")
            );
            wrongCurrentEx.ErrorCode.ShouldBe(IdentityManagementErrorCode.InvalidCurrentPassword);

            await service.ChangeOwnPasswordAsync(workerId, "InitialPass123", "NewPass456");

            var authWithNew = await service.AuthenticateAsync("operator1", "NewPass456");
            authWithNew.IsSucceeded.ShouldBeTrue();
        }

        // Администратор деактивирует работника — вход блокируется, эффективные права пустые
        await using (var deactivateDb = CreateDbContext())
        {
            var service = CreateService(deactivateDb);
            var deactivated = await service.SetUserActiveStatusAsync(adminId, workerId, false);

            deactivated.IsActive.ShouldBeFalse();
            deactivated.HasPermission(AppPermission.ProductsView).ShouldBeFalse();
            deactivated.GetEffectivePermissions().ShouldBeEmpty();

            var authAttempt = await service.AuthenticateAsync("operator1", "NewPass456");
            authAttempt.IsSucceeded.ShouldBeFalse();
            authAttempt.Status.ShouldBe(AuthenticationStatus.UserDeactivated);
        }
    }

    [Fact]
    public async Task LastAdministratorAndSelfProtectionRules_ShouldPreventSystemLockout()
    {
        Guid admin1Id;
        Guid admin2Id;

        await using (var db = CreateDbContext())
        {
            var service = CreateService(db);
            var admin1 = await service.EnsureDefaultAdminExistsAsync(
                "admin1",
                "Админ 1",
                "AdminPass123!"
            );
            admin1Id = admin1.Id;

            // Единственный администратор не может понизить свою роль до Worker
            var demoteLastAdminEx = await Should.ThrowAsync<IdentityManagementException>(() =>
                service.UpdateUserAsync(
                    admin1Id,
                    new UpdateUserRequest(admin1Id, "admin1", "Админ 1", UserRole.Worker)
                )
            );
            demoteLastAdminEx.ErrorCode.ShouldBe(
                IdentityManagementErrorCode.CannotRemoveLastAdministrator
            );

            // Не может деактивировать или удалить самого себя
            var selfDeactivateEx = await Should.ThrowAsync<IdentityManagementException>(() =>
                service.SetUserActiveStatusAsync(admin1Id, admin1Id, false)
            );
            selfDeactivateEx.ErrorCode.ShouldBe(IdentityManagementErrorCode.CannotDeactivateSelf);

            var selfDeleteEx = await Should.ThrowAsync<IdentityManagementException>(() =>
                service.DeleteUserAsync(admin1Id, admin1Id)
            );
            selfDeleteEx.ErrorCode.ShouldBe(IdentityManagementErrorCode.CannotDeleteSelf);

            // Создаем второго администратора
            var admin2 = await service.CreateUserAsync(
                admin1Id,
                new CreateUserRequest("admin2", "Админ 2", "Admin2Pass123!", UserRole.Administrator)
            );
            admin2Id = admin2.Id;
        }

        // При наличии второго активного администратора первого можно перевести в Worker или удалить (от лица admin2)
        await using (var db2 = CreateDbContext())
        {
            var service = CreateService(db2);

            var demoted = await service.UpdateUserAsync(
                admin2Id,
                new UpdateUserRequest(admin1Id, "admin1", "Бывший Админ 1", UserRole.Worker)
            );
            demoted.Role.ShouldBe(UserRole.Worker);

            var deleted = await service.DeleteUserAsync(admin2Id, admin1Id);
            deleted.ShouldBeTrue();

            var remainingUsers = await service.GetAllUsersAsync();
            remainingUsers.ShouldHaveSingleItem().Id.ShouldBe(admin2Id);
        }
    }

    private IdentityDbContext CreateDbContext() => new(_dbOptions);

    private UserManagementService CreateService(IdentityDbContext dbContext) =>
        new(dbContext, _passwordHasher, _timeProvider);

    private sealed class FakeTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }
}
