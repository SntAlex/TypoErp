namespace TypoErp.Modules.Identity.Security;

/// <summary>
///     Сервис криптографического хэширования и проверки паролей пользователей.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>
    ///     Создает криптографически стойкий хэш пароля со случайной солью.
    /// </summary>
    string HashPassword(string password);

    /// <summary>
    ///     Проверяет соответствие пароля сохраненному хэшу за константное время.
    /// </summary>
    bool VerifyPassword(string password, string passwordHash);
}
