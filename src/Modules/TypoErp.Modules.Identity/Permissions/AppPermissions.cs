namespace TypoErp.Modules.Identity.Permissions;

/// <summary>
///     Каталог всех доступных гранулярных разрешений в системе TypoErp.
/// </summary>
public static class AppPermissions
{
    public const string ClaimType = "typoerp_permission";

    public static IReadOnlyList<AppPermission> All { get; } = Enum.GetValues<AppPermission>();

    public static IReadOnlyList<PermissionDefinition> Definitions { get; } =
    [
        new(
            AppPermission.ProductsView,
            "Товары",
            "Просмотр товаров",
            "Просмотр таблицы товаров, категорий, форматов и тегов."
        ),
        new(
            AppPermission.ProductsEdit,
            "Товары",
            "Редактирование товаров",
            "Создание, изменение, починка и удаление товаров, категорий и тегов."
        ),
        new(
            AppPermission.ProductsSync,
            "Товары",
            "Синхронизация с диском",
            "Запуск синхронизации файлового хранилища товаров с базой данных."
        ),
        new(
            AppPermission.ProductsForceUnlock,
            "Товары",
            "Принудительный перехват блокировки",
            "Принудительный перехват блокировки раздела «Товары» у другого работника."
        ),
        new(
            AppPermission.AssemblyView,
            "Сборка",
            "Просмотр сборки",
            "Просмотр листов и заданий на сборку."
        ),
        new(
            AppPermission.AssemblyManage,
            "Сборка",
            "Управление сборкой",
            "Формирование и редактирование заданий на сборку."
        ),
        new(
            AppPermission.MarketplacesView,
            "Маркетплейсы",
            "Просмотр маркетплейсов",
            "Просмотр привязок и карточек маркетплейсов."
        ),
        new(
            AppPermission.MarketplacesManage,
            "Маркетплейсы",
            "Управление маркетплейсами",
            "Редактирование карточек и синхронизация с маркетплейсами."
        ),
        new(
            AppPermission.UsersManage,
            "Администрирование",
            "Управление работниками и правами",
            "Создание работников, блокировка учетных записей и делегирование прав доступа."
        ),
    ];
}
