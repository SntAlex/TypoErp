namespace TypoErp.Modules.Identity.Authentication;

/// <summary>
///     Статус попытки входа пользователя в систему.
/// </summary>
public enum AuthenticationStatus
{
    /// <summary>
    ///     Успешная аутентификация.
    /// </summary>
    Succeeded = 0,

    /// <summary>
    ///     Неверный логин или пароль.
    /// </summary>
    InvalidCredentials = 1,

    /// <summary>
    ///     Учетная запись работника заблокирована (деактивирована) администратором.
    /// </summary>
    UserDeactivated = 2,
}
