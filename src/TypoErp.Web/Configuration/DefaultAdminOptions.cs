namespace TypoErp.Web.Configuration;

/// <summary>
///     Настройки учетной записи администратора по умолчанию, создаваемой при первом запуске системы.
/// </summary>
public sealed class DefaultAdminOptions
{
    public const string SectionName = "DefaultAdmin";

    public string Username { get; set; } = "admin";

    public string DisplayName { get; set; } = "Администратор";

    public string Password { get; set; } = "Admin123!";
}
