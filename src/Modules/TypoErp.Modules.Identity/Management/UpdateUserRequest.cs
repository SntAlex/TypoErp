using TypoErp.Modules.Identity.Entities;

namespace TypoErp.Modules.Identity.Management;

/// <summary>
///     Запрос на обновление профиля и роли существующего пользователя.
/// </summary>
public sealed record UpdateUserRequest(
    Guid UserId,
    string Username,
    string DisplayName,
    UserRole Role
);
