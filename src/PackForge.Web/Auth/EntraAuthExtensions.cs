using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;

namespace PackForge.Web.Auth;

public static class EntraAuthExtensions
{
    /// <summary>
    /// Wires Entra ID (Azure AD) sign-in when AzureAd config is present. Guarded so
    /// local dev with no tenant still runs fully open — auth is a Phase 3 production
    /// concern, not a dev blocker.
    /// </summary>
    public static bool AddEntraAuthentication(this IServiceCollection services, IConfiguration config)
    {
        if (string.IsNullOrWhiteSpace(config["AzureAd:ClientId"]))
            return false;

        services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApp(config.GetSection("AzureAd"));
        services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build());
        services.AddCascadingAuthenticationState();
        return true;
    }
}
