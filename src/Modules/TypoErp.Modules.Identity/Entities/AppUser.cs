using TypoErp.Modules.Identity.Permissions;
using TypoErp.SharedKernel.Normalization;

namespace TypoErp.Modules.Identity.Entities;

/// <summary>
///     Сущность пользователя (работника или администратора) в PostgreSQL.
/// </summary>
public sealed class AppUser
{
    private AppUser() { }

    public AppUser(
        string username,
        string displayName,
        string passwordHash,
        UserRole role,
        DateTimeOffset createdAtUtc,
        IEnumerable<AppPermission>? permissions = null,
        Guid? id = null
    )
    {
        var trimmedUsername = username?.Trim() ?? string.Empty;
        var normalizedUsername = StringNormalizer.Normalize(trimmedUsername);
        var trimmedDisplayName = displayName?.Trim() ?? string.Empty;

        ArgumentException.ThrowIfNullOrWhiteSpace(trimmedUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(trimmedDisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        Id = id ?? Guid.NewGuid();
        Username = trimmedUsername;
        NormalizedUsername = normalizedUsername;
        DisplayName = trimmedDisplayName;
        PasswordHash = passwordHash;
        Role = role;
        IsActive = true;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;

        SetPermissions(permissions, createdAtUtc);
    }

    public Guid Id { get; set; }

    /// <summary>
    ///     Логин работника для входа в систему.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    ///     Нормализованный уникальный логин работника (в нижнем регистре).
    /// </summary>
    public string NormalizedUsername { get; set; } = string.Empty;

    /// <summary>
    ///     Отображаемое имя работника (например, «Иван Печатников»).
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    ///     Криптографический хэш пароля (PBKDF2-SHA256 с солью).
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    ///     Роль пользователя (Worker или Administrator).
    /// </summary>
    public UserRole Role { get; set; }

    /// <summary>
    ///     Признак активности учетной записи (false — вход и операции заблокированы).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    ///     Список гранулярных разрешений, явно делегированных работнику.
    /// </summary>
    public List<AppPermission> Permissions { get; set; } = [];

    public DateTimeOffset CreatedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? LastLoginAtUtc { get; set; }

    /// <summary>
    ///     Проверяет наличие указанного разрешения с учетом активности учетной записи и роли администратора.
    /// </summary>
    public bool HasPermission(AppPermission permission)
    {
        if (!IsActive)
            return false;

        if (Role == UserRole.Administrator)
            return true;

        return Permissions.Contains(permission);
    }

    /// <summary>
    ///     Возвращает полный список действующих разрешений пользователя.
    /// </summary>
    public IReadOnlyList<AppPermission> GetEffectivePermissions()
    {
        if (!IsActive)
            return [];

        if (Role == UserRole.Administrator)
            return AppPermissions.All;

        return Permissions;
    }

    public void SetPermissions(IEnumerable<AppPermission>? permissions, DateTimeOffset updatedAtUtc)
    {
        Permissions = permissions?.Distinct().OrderBy(p => (int)p).ToList() ?? [];
        UpdatedAtUtc = updatedAtUtc;
    }

    public void UpdateProfile(
        string username,
        string displayName,
        UserRole role,
        DateTimeOffset updatedAtUtc
    )
    {
        var trimmedUsername = username?.Trim() ?? string.Empty;
        var normalizedUsername = StringNormalizer.Normalize(trimmedUsername);
        var trimmedDisplayName = displayName?.Trim() ?? string.Empty;

        ArgumentException.ThrowIfNullOrWhiteSpace(trimmedUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(trimmedDisplayName);

        Username = trimmedUsername;
        NormalizedUsername = normalizedUsername;
        DisplayName = trimmedDisplayName;
        Role = role;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void ChangePasswordHash(string newPasswordHash, DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newPasswordHash);
        PasswordHash = newPasswordHash;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void SetActive(bool isActive, DateTimeOffset updatedAtUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = updatedAtUtc;
    }

    public void RecordLogin(DateTimeOffset loggedInAtUtc)
    {
        LastLoginAtUtc = loggedInAtUtc;
    }
}
