using System.Security.Claims;
using _23DTHD5_Demo.Models;
using _23DTHD5_Demo.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Facebook;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace _23DTHD5_Demo.Controllers.api;

[ApiController]
[Route("api/auth/facebook")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class FacebookAuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ExternalAccountService accounts,
    IAuthenticationSchemeProvider schemes) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(new
    {
        enabled = await schemes.GetSchemeAsync(FacebookDefaults.AuthenticationScheme) is not null
    });

    [HttpPost("start")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Start([FromForm] bool rememberMe = false)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (await schemes.GetSchemeAsync(FacebookDefaults.AuthenticationScheme) is null)
            return ErrorRedirect("facebook_not_configured");
        // An authenticated start explicitly links Facebook to the current game account.
        var userId = users.GetUserId(User);
        var properties = signIn.ConfigureExternalAuthenticationProperties(
            FacebookDefaults.AuthenticationScheme, Url.Action(nameof(Callback))!, userId);
        properties.Items["rememberMe"] = rememberMe.ToString();
        if (userId is not null) properties.Items["linkUserId"] = userId;
        return Challenge(properties, FacebookDefaults.AuthenticationScheme);
    }

    [HttpGet("callback")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Callback()
    {
        var currentUserId = users.GetUserId(User);
        var info = await signIn.GetExternalLoginInfoAsync(currentUserId);
        if (!ValidFacebookIdentity(info)) return await ClearAndRedirect("facebook_failed");
        var rememberMe = info!.AuthenticationProperties?.Items.TryGetValue("rememberMe", out var remember) == true
            && remember == bool.TrueString;
        string? linkUserId = null;
        var linking = info.AuthenticationProperties?.Items.TryGetValue("linkUserId", out linkUserId) == true;
        if (linking)
        {
            if (currentUserId is null || linkUserId != currentUserId) return await ClearAndRedirect("facebook_failed");
            var user = await users.FindByIdAsync(currentUserId);
            if (user is null || !await signIn.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
                return await ClearAndRedirect("facebook_locked");
            var result = await AddLink(user, info);
            if (result is not null) return await ClearAndRedirect("facebook_link_conflict");
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return LocalRedirect("/?authNotice=facebook_linked");
        }
        // Never silently link a provider if the authentication session changed during OAuth.
        if (currentUserId is not null) return await ClearAndRedirect("facebook_failed");

        var existing = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (existing is not null)
        {
            var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, rememberMe, bypassTwoFactor: false);
            if (!result.Succeeded) return await ClearAndRedirect(result.IsLockedOut ? "facebook_locked" : "facebook_failed");
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return LocalRedirect("/");
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrWhiteSpace(email)) return await ClearAndRedirect("facebook_email_missing");
        if (await users.FindByEmailAsync(email) is not null) return ErrorRedirect("facebook_link_required");
        try
        {
            var user = await accounts.CreateAsync(info);
            if (user is null) return await ClearAndRedirect("facebook_account_failed");
            await signIn.SignInAsync(user, rememberMe, info.LoginProvider);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return LocalRedirect("/");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return await ClearAndRedirect("facebook_retry");
        }
    }

    [HttpPost("link")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Link(LoginRequest request)
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (!ValidFacebookIdentity(info) || string.IsNullOrWhiteSpace(info!.Principal.FindFirstValue(ClaimTypes.Email)))
            return BadRequest(new { message = "Phiên Facebook đã hết hạn hoặc thiếu email. Vui lòng bấm Facebook để bắt đầu lại." });
        // This endpoint is for an anonymous collision flow, not a previously authenticated link flow.
        if (info.AuthenticationProperties?.Items.ContainsKey("linkUserId") == true)
            return BadRequest(new { message = "Vui lòng bắt đầu lại liên kết Facebook từ sảnh chờ." });
        var identity = request.Identity.Trim();
        var user = identity.Contains('@') ? await users.FindByEmailAsync(identity) : await users.FindByNameAsync(identity);
        var email = info.Principal.FindFirstValue(ClaimTypes.Email)!;
        if (user is null || users.NormalizeEmail(user.Email) != users.NormalizeEmail(email))
            return Unauthorized(new { message = "Vui lòng nhập đúng tài khoản đã đăng ký với email Facebook này." });
        var passwordResult = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (passwordResult.IsLockedOut) return StatusCode(423, new { message = "Tài khoản tạm khóa. Vui lòng thử lại sau 5 phút." });
        if (!passwordResult.Succeeded) return Unauthorized(new { message = "Tên đăng nhập hoặc mật khẩu không chính xác." });
        var error = await AddLink(user, info);
        if (error is not null) return error;
        await signIn.SignInAsync(user, request.RememberMe, info.LoginProvider);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Ok(new { account = new { id = user.Id, username = user.UserName, displayName = user.DisplayName, email = user.Email } });
    }

    private async Task<IActionResult?> AddLink(ApplicationUser user, ExternalLoginInfo info)
    {
        var existing = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (existing is not null) return existing.Id == user.Id ? null : Conflict(new { message = "Facebook này đã liên kết với tài khoản khác." });
        try
        {
            var result = await users.AddLoginAsync(user, info);
            return result.Succeeded ? null : Conflict(new { message = "Không thể liên kết Facebook. Vui lòng thử lại." });
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            return Conflict(new { message = "Liên kết Facebook vừa thay đổi. Vui lòng thử lại." });
        }
    }

    private static bool ValidFacebookIdentity(ExternalLoginInfo? info) =>
        info?.LoginProvider == FacebookDefaults.AuthenticationScheme && !string.IsNullOrWhiteSpace(info.ProviderKey);
    private LocalRedirectResult ErrorRedirect(string code) => LocalRedirect("/?authError=" + code);
    private async Task<IActionResult> ClearAndRedirect(string code)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return ErrorRedirect(code);
    }
}
