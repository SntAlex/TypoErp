using TypoErp.Modules.Identity.Permissions;

namespace TypoErp.Web.Security;

/// <summary>
///     Константы типов утверждений (Claims) для аутентификационной Cookie в TypoErp.
/// </summary>
public static class TypoErpClaimTypes
{
    public const string DisplayName = "typoerp_display_name";
    public const string Permission = AppPermissions.ClaimType;
    public const string SessionId = "typoerp_session_id";
}
