using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;

namespace _23DTHD5_Demo.Services;

public static class GoogleAuthentication
{
    public const string VerifiedEmailClaim = "google:email_verified";

    public static IServiceCollection AddGoogleLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var clientId = configuration["Authentication:Google:ClientId"];
        var clientSecret = configuration["Authentication:Google:ClientSecret"];
        // Keep password login working when the developer has not configured Google yet.
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret)) return services;

        services.AddAuthentication().AddGoogle(options =>
        {
            options.ClientId = clientId;
            options.ClientSecret = clientSecret;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.CallbackPath = "/signin-google";
            options.UsePkce = true;
            options.SaveTokens = false;
            options.ClaimActions.MapJsonKey(VerifiedEmailClaim, "email_verified", ClaimValueTypes.Boolean);
            options.Events.OnRemoteFailure = async context =>
            {
                await context.HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("GoogleAuthentication");
                logger.LogWarning("Google sign-in failed ({FailureType}).", context.Failure?.GetType().Name);
                context.Response.Redirect("/?authError=google_failed");
                context.HandleResponse();
            };
            options.Events.OnAccessDenied = async context =>
            {
                await context.HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                context.Response.Redirect("/?authError=google_cancelled");
                context.HandleResponse();
            };
        });
        return services;
    }
}
