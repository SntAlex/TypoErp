using System.Security.Claims;
using TypoErp.Modules.Identity.Entities;
using TypoErp.Modules.Identity.Permissions;

namespace TypoErp.Web.Security;

/// <summary>
///     Методы расширения для удобного чтения идентификатора, роли, сессии и разрешений работника из <see cref="ClaimsPrincipal" />.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static bool IsAuthenticated(this ClaimsPrincipal? principal)
    {
        return principal?.Identity?.IsAuthenticated == true;
    }

    public static Guid? GetUserId(this ClaimsPrincipal? principal)
    {
        var raw = principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) && id != Guid.Empty ? id : null;
    }

    public static string GetUsername(this ClaimsPrincipal? principal)
    {
        return principal?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;
    }

    public static string GetDisplayName(this ClaimsPrincipal? principal)
    {
        var displayName = principal?.FindFirstValue(TypoErpClaimTypes.DisplayName);
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName;

        return principal.GetUsername();
    }

    /// <summary>
    ///     Возвращает устойчивый идентификатор сессии пользователя из Cookie (сохраняется при обновлении страницы по F5).
    /// </summary>
    public static string? GetWorkspaceSessionId(this ClaimsPrincipal? principal)
    {
        if (!principal.IsAuthenticated())
            return null;

        var sessionClaim = principal?.FindFirstValue(TypoErpClaimTypes.SessionId);
        if (!string.IsNullOrWhiteSpace(sessionClaim))
            return sessionClaim.Trim();

        var userId = principal.GetUserId();
        return userId.HasValue ? $"user-session-{userId.Value:N}" : null;
    }

    public static UserRole GetUserRole(this ClaimsPrincipal? principal)
    {
        var rawRole = principal?.FindFirstValue(ClaimTypes.Role);
        return Enum.TryParse<UserRole>(rawRole, ignoreCase: true, out var role)
            ? role
            : UserRole.Worker;
    }

    public static bool IsAdministrator(this ClaimsPrincipal? principal)
    {
        return principal.IsAuthenticated() && principal.GetUserRole() == UserRole.Administrator;
    }

    public static bool HasPermission(this ClaimsPrincipal? principal, AppPermission permission)
    {
        if (!principal.IsAuthenticated())
            return false;

        if (principal.IsAdministrator())
            return true;

        var permissionString = permission.ToString();
        return principal!
            .FindAll(TypoErpClaimTypes.Permission)
            .Any(c => string.Equals(c.Value, permissionString, StringComparison.OrdinalIgnoreCase));
    }

    public static bool CanEditOrSyncProducts(this ClaimsPrincipal? principal)
    {
        return principal.HasPermission(AppPermission.ProductsEdit)
            || principal.HasPermission(AppPermission.ProductsSync);
    }

    public static bool CanForceTakeoverProductsLock(this ClaimsPrincipal? principal)
    {
        return principal.IsAdministrator()
            || principal.HasPermission(AppPermission.ProductsForceUnlock);
    }
}
