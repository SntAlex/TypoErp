using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using TypoErp.Modules.Identity.Authentication;
using TypoErp.Modules.Identity.Management;
using TypoErp.Modules.Products.Locking;
using TypoErp.Web.Security;

namespace TypoErp.Web.Endpoints;

/// <summary>
///     HTTP-эндпоинты для входа и выхода из системы с установкой/очисткой аутентификационной Cookie.
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints
            .MapPost(
                "/account/login",
                async (
                    HttpContext httpContext,
                    [FromForm] string? username,
                    [FromForm] string? password,
                    [FromForm] string? returnUrl,
                    IUserManagementService userManagementService,
                    CancellationToken cancellationToken
                ) =>
                {
                    var result = await userManagementService.AuthenticateAsync(
                        username ?? string.Empty,
                        password ?? string.Empty,
                        cancellationToken
                    );

                    if (!result.IsSucceeded || result.User is null)
                    {
                        var errorCode =
                            result.Status == AuthenticationStatus.UserDeactivated
                                ? "deactivated"
                                : "invalid";

                        return Results.LocalRedirect($"/login?error={errorCode}");
                    }

                    var principal = TypoErpClaimsPrincipalFactory.Create(
                        result.User,
                        result.EffectivePermissions
                    );

                    await httpContext.SignInAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        principal,
                        new AuthenticationProperties
                        {
                            IsPersistent = true,
                            ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12),
                        }
                    );

                    var targetUrl =
                        !string.IsNullOrWhiteSpace(returnUrl)
                        && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                        && returnUrl.StartsWith('/')
                        && !returnUrl.StartsWith("//")
                            ? returnUrl
                            : "/products";

                    return Results.LocalRedirect(targetUrl);
                }
            )
            .DisableAntiforgery();

        endpoints
            .MapPost(
                "/account/logout",
                async (HttpContext httpContext, IProductsWorkspaceLockService lockService) =>
                {
                    var sessionId = httpContext.User.GetWorkspaceSessionId();
                    if (!string.IsNullOrWhiteSpace(sessionId))
                        lockService.Release(sessionId);

                    await httpContext.SignOutAsync(
                        CookieAuthenticationDefaults.AuthenticationScheme
                    );
                    return Results.LocalRedirect("/login");
                }
            )
            .DisableAntiforgery();

        return endpoints;
    }
}
