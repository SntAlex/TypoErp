using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;
using TypoErp.Modules.Identity.Persistence;

namespace TypoErp.Modules.Identity.Tests.Persistence;

public sealed class IdentityDbContextTests : IAsyncLifetime
{
    private static readonly DateTimeOffset FixedNow = new(2026, 10, 15, 10, 0, 0, TimeSpan.Zero);
    private SqliteConnection _connection = null!;
    private DbContextOptions<IdentityDbContext> _dbOptions = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _dbOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseSqlite(_connection)
            .Options;

        await using var dbContext = CreateDbContext();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
    }

    [Fact]
    public async Task SaveAndQuery_ShouldPersistUserRoleAndGranularPermissionsAndTrackChanges()
    {
        var user = new AppUser(
            "  Ivan_Operator ",
            "  Иван Печатников ",
            "hashed-password-value",
            UserRole.Worker,
            FixedNow,
            [AppPermission.ProductsView, AppPermission.ProductsEdit]
        );

        await using (var writeDb = CreateDbContext())
        {
            writeDb.Users.Add(user);
            await writeDb.SaveChangesAsync();
        }

        await using (var updateDb = CreateDbContext())
        {
            var loaded = await updateDb.Users.SingleAsync(u => u.Id == user.Id);

            loaded.Username.ShouldBe("Ivan_Operator");
            loaded.NormalizedUsername.ShouldBe("ivan_operator");
            loaded.DisplayName.ShouldBe("Иван Печатников");
            loaded.Role.ShouldBe(UserRole.Worker);
            loaded.IsActive.ShouldBeTrue();
            loaded.Permissions.ShouldBe([AppPermission.ProductsView, AppPermission.ProductsEdit]);
            loaded.HasPermission(AppPermission.ProductsEdit).ShouldBeTrue();
            loaded.HasPermission(AppPermission.ProductsForceUnlock).ShouldBeFalse();

            // Проверяем отслеживание мутаций списка разрешений через ValueComparer
            loaded.SetPermissions(
                [
                    AppPermission.ProductsView,
                    AppPermission.ProductsEdit,
                    AppPermission.ProductsSync,
                    AppPermission.ProductsForceUnlock,
                ],
                FixedNow.AddMinutes(10)
            );
            await updateDb.SaveChangesAsync();
        }

        await using (var verifyDb = CreateDbContext())
        {
            var verified = await verifyDb.Users.SingleAsync(u => u.Id == user.Id);
            verified.Permissions.ShouldBe([
                AppPermission.ProductsView,
                AppPermission.ProductsEdit,
                AppPermission.ProductsSync,
                AppPermission.ProductsForceUnlock,
            ]);
            verified.HasPermission(AppPermission.ProductsForceUnlock).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task UniqueConstraint_WhenDuplicateNormalizedUsername_ShouldThrowDbUpdateException()
    {
        var user1 = new AppUser("Worker_1", "Работник 1", "hash1", UserRole.Worker, FixedNow);
        var user2 = new AppUser("  worker_1  ", "Работник 2", "hash2", UserRole.Worker, FixedNow);

        await using var dbContext = CreateDbContext();
        dbContext.Users.AddRange(user1, user2);

        await Should.ThrowAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
    }

    private IdentityDbContext CreateDbContext() => new(_dbOptions);
}
