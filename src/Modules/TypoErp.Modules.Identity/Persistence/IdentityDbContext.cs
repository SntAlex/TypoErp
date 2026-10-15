using Microsoft.EntityFrameworkCore;
using TypoErp.Modules.Identity.Entities;

namespace TypoErp.Modules.Identity.Persistence;

/// <summary>
///     Контекст базы данных модуля авторизации и управления работниками (<see cref="TypoErp.Modules.Identity" />).
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
