using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;

namespace TypoErp.Web.Security;

/// <summary>
///     Фабрика создания <see cref="ClaimsPrincipal" /> из доменной сущности <see cref="AppUser" />.
/// </summary>
public static class TypoErpClaimsPrincipalFactory
{
    public static ClaimsPrincipal Create(
        AppUser user,
        IReadOnlyCollection<AppPermission> effectivePermissions,
        string authenticationType = CookieAuthenticationDefaults.AuthenticationScheme
    )
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(effectivePermissions);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString("D")),
            new(ClaimTypes.Name, user.Username),
            new(TypoErpClaimTypes.DisplayName, user.DisplayName),
            new(ClaimTypes.Role, user.Role.ToString()),
        };

        foreach (var permission in effectivePermissions.Distinct())
            claims.Add(new Claim(TypoErpClaimTypes.Permission, permission.ToString()));

        var identity = new ClaimsIdentity(claims, authenticationType);
        return new ClaimsPrincipal(identity);
    }
}
