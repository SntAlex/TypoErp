namespace TypoErp.Modules.Identity.Permissions;

/// <summary>
///     Гранулярные разрешения (Permissions) системы TypoErp, которые администратор может делегировать работникам.
/// </summary>
public enum AppPermission
{
    /// <summary>
    ///     Просмотр раздела «Товары», таблицы товаров, категорий, форматов и тегов.
    /// </summary>
    ProductsView = 100,

    /// <summary>
    ///     Создание, редактирование, починка и удаление товаров, категорий и тегов (при удерживаемой блокировке раздела).
    /// </summary>
    ProductsEdit = 101,

    /// <summary>
    ///     Запуск синхронизации файлового хранилища товаров с базой данных.
    /// </summary>
    ProductsSync = 102,

    /// <summary>
    ///     Принудительный перехват блокировки раздела товаров у другой активной сессии (ForceTakeover).
    /// </summary>
    ProductsForceUnlock = 103,

    /// <summary>
    ///     Просмотр раздела «Сборка».
    /// </summary>
    AssemblyView = 200,

    /// <summary>
    ///     Управление заданиями и файлами в разделе «Сборка».
    /// </summary>
    AssemblyManage = 201,

    /// <summary>
    ///     Просмотр раздела «Маркетплейсы».
    /// </summary>
    MarketplacesView = 300,

    /// <summary>
    ///     Управление карточками и выгрузками в разделе «Маркетплейсы».
    /// </summary>
    MarketplacesManage = 301,

    /// <summary>
    ///     Управление работниками и делегирование гранулярных прав доступа.
    /// </summary>
    UsersManage = 900,
}
