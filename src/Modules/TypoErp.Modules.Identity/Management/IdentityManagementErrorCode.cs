namespace TypoErp.Modules.Identity.Management;

/// <summary>
///     Коды ошибок бизнес-операций управления пользователями и правами доступа.
/// </summary>
public enum IdentityManagementErrorCode
{
    /// <summary>
    ///     Запрошенный пользователь не найден в БД.
    /// </summary>
    UserNotFound = 1,

    /// <summary>
    ///     У текущего пользователя недостаточно прав для выполнения операции.
    /// </summary>
    AccessDenied = 2,

    /// <summary>
    ///     Логин пустой, содержит пробелы внутри или выходит за допустимые пределы длины.
    /// </summary>
    InvalidUsername = 3,

    /// <summary>
    ///     Отображаемое имя пустое или превышает максимальную длину.
    /// </summary>
    InvalidDisplayName = 4,

    /// <summary>
    ///     Пароль пустой или короче минимально допустимой длины.
    /// </summary>
    InvalidPassword = 5,

    /// <summary>
    ///     Неверно указан текущий пароль при самостоятельной смене пароля.
    /// </summary>
    InvalidCurrentPassword = 6,

    /// <summary>
    ///     Пользователь с таким нормализованным логином уже существует.
    /// </summary>
    DuplicateUsername = 7,

    /// <summary>
    ///     Нельзя удалить, деактивировать или разжаловать последнего активного администратора системы.
    /// </summary>
    CannotRemoveLastAdministrator = 8,

    /// <summary>
    ///     Пользователь не может деактивировать собственную учетную запись.
    /// </summary>
    CannotDeactivateSelf = 9,

    /// <summary>
    ///     Пользователь не может удалить собственную учетную запись.
    /// </summary>
    CannotDeleteSelf = 10,

    /// <summary>
    ///     Работник с делегированным правом управления пользователями не может снять это право с самого себя.
    /// </summary>
    CannotRevokeOwnUsersManagePermission = 11,
}
