namespace TypoErp.Modules.Identity.Entities;

/// <summary>
///     Базовая роль пользователя в системе TypoErp.
/// </summary>
public enum UserRole
{
    /// <summary>
    ///     Работник (оператор). Обладает только теми разрешениями, которые ему явно делегировал администратор.
    /// </summary>
    Worker = 0,

    /// <summary>
    ///     Администратор системы. Автоматически обладает всеми разрешениями, включая перехват блокировок и управление работниками.
    /// </summary>
    Administrator = 1,
}
