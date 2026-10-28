using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace _23DTHD5_Demo.Services;

public static class FacebookAuthentication
{
    public static IServiceCollection AddFacebookLogin(this IServiceCollection services, IConfiguration configuration)
    {
        var appId = configuration["Authentication:Facebook:AppId"];
        var appSecret = configuration["Authentication:Facebook:AppSecret"];
        if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret)) return services;

        services.AddAuthentication().AddFacebook(options =>
        {
            options.AppId = appId;
            options.AppSecret = appSecret;
            options.SignInScheme = IdentityConstants.ExternalScheme;
            options.CallbackPath = "/signin-facebook";
            // The .NET 8 package defaults to Graph v14. Use the endpoints from current ASP.NET Core instead.
            options.AuthorizationEndpoint = "https://www.facebook.com/v22.0/dialog/oauth";
            options.TokenEndpoint = "https://graph.facebook.com/v22.0/oauth/access_token";
            options.UserInformationEndpoint = "https://graph.facebook.com/v22.0/me";
            options.SaveTokens = false;
            options.UsePkce = true;
            options.SendAppSecretProof = true;
            options.Scope.Add("email");
            options.Fields.Add("email");
            options.Events.OnRemoteFailure = async context =>
            {
                await context.HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                    .CreateLogger("FacebookAuthentication");
                logger.LogWarning("Facebook sign-in failed ({FailureType}).", context.Failure?.GetType().Name);
                context.Response.Redirect("/?authError=facebook_failed");
                context.HandleResponse();
            };
            options.Events.OnAccessDenied = async context =>
            {
                await context.HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                context.Response.Redirect("/?authError=facebook_cancelled");
                context.HandleResponse();
            };
        });
        return services;
    }
}
