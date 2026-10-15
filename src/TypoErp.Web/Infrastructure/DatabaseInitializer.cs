using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using TypoErp.Modules.Identity.Management;
using TypoErp.Modules.Identity.Persistence;
using TypoErp.Modules.Products.Configuration;
using TypoErp.Modules.Products.Persistence;
using TypoErp.Web.Configuration;

namespace TypoErp.Web.Infrastructure;

/// <summary>
///     Инициализатор схемы PostgreSQL для всех модулей монолита, дефолтного администратора и корневой папки хранилища.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var logger = provider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseInitializer");

        var identityDb = provider.GetRequiredService<IdentityDbContext>();
        var productsDb = provider.GetRequiredService<ProductsDbContext>();

        await EnsureContextTablesCreatedAsync(identityDb, "users", logger, cancellationToken);
        await EnsureContextTablesCreatedAsync(productsDb, "products", logger, cancellationToken);

        var adminOptions = provider.GetRequiredService<IOptions<DefaultAdminOptions>>().Value;
        var userManagement = provider.GetRequiredService<IUserManagementService>();

        await userManagement.EnsureDefaultAdminExistsAsync(
            adminOptions.Username,
            adminOptions.DisplayName,
            adminOptions.Password,
            cancellationToken
        );

        var storageOptions = provider.GetRequiredService<ProductsStorageOptions>();
        if (!string.IsNullOrWhiteSpace(storageOptions.RootPath))
        {
            try
            {
                Directory.CreateDirectory(storageOptions.RootPath);
                logger.LogInformation(
                    "Ensured products storage root directory exists at '{RootPath}'",
                    Path.GetFullPath(storageOptions.RootPath)
                );
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Failed to create or access products storage root directory '{RootPath}'",
                    storageOptions.RootPath
                );
            }
        }
    }

    private static async Task EnsureContextTablesCreatedAsync(
        DbContext dbContext,
        string probeTableName,
        ILogger logger,
        CancellationToken cancellationToken
    )
    {
        var creator = dbContext.GetService<IRelationalDatabaseCreator>();

        if (!await creator.ExistsAsync(cancellationToken))
            await creator.CreateAsync(cancellationToken);

        try
        {
            await creator.CreateTablesAsync(cancellationToken);
            logger.LogInformation(
                "Created database tables for context {DbContextName} (probe table '{TableName}')",
                dbContext.GetType().Name,
                probeTableName
            );
        }
        catch (Exception)
        {
            // Таблицы данного контекста уже существуют в БД
        }
    }
}
