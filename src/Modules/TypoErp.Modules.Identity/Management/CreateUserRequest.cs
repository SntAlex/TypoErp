using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;

namespace TypoErp.Modules.Identity.Management;

/// <summary>
///     Запрос на создание нового работника или администратора.
/// </summary>
public sealed record CreateUserRequest(
    string Username,
    string DisplayName,
    string Password,
    UserRole Role = UserRole.Worker,
    IReadOnlyList<AppPermission>? Permissions = null
);
