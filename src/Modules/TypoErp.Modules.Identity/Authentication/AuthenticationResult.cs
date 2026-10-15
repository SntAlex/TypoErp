using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;

namespace TypoErp.Modules.Identity.Authentication;

/// <summary>
///     Результат проверки учетных данных пользователя.
/// </summary>
public sealed record AuthenticationResult(
    AuthenticationStatus Status,
    AppUser? User,
    IReadOnlyList<AppPermission> EffectivePermissions
)
{
    public bool IsSucceeded => Status == AuthenticationStatus.Succeeded && User is not null;
}
