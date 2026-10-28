using System.Security.Claims;
using _23DTHD5_Demo.Models;
using _23DTHD5_Demo.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace _23DTHD5_Demo.Controllers.api;

[ApiController]
[Route("api/auth/google")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class GoogleAuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    ExternalAccountService accounts,
    IAuthenticationSchemeProvider schemes) : ControllerBase
{
    [HttpGet("status")]
    public async Task<IActionResult> Status() => Ok(new
    {
        enabled = await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is not null
    });

    [HttpPost("start")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Start([FromForm] bool rememberMe = false)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        if (await schemes.GetSchemeAsync(GoogleDefaults.AuthenticationScheme) is null)
            return ErrorRedirect("google_not_configured");
        var properties = signIn.ConfigureExternalAuthenticationProperties(
            GoogleDefaults.AuthenticationScheme, Url.Action(nameof(Callback))!);
        properties.Items["rememberMe"] = rememberMe.ToString();
        properties.SetParameter("prompt", "select_account");
        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("callback")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Callback()
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (!ValidGoogleIdentity(info)) return await ClearAndRedirect("google_failed");
        var rememberMe = info!.AuthenticationProperties?.Items.TryGetValue("rememberMe", out var remember) == true
            && remember == bool.TrueString;
        var existing = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (existing is not null)
        {
            var result = await signIn.ExternalLoginSignInAsync(info.LoginProvider, info.ProviderKey, rememberMe, bypassTwoFactor: false);
            if (!result.Succeeded) return await ClearAndRedirect(result.IsLockedOut ? "google_locked" : "google_failed");
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return LocalRedirect("/");
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email)!;
        if (await users.FindByEmailAsync(email) is not null)
        {
            // Email alone is not proof of ownership of an existing game account.
            // Retain the short-lived external cookie, then require its local password to link.
            return ErrorRedirect("google_link_required");
        }

        try
        {
            var user = await accounts.CreateAsync(info);
            if (user is null) return await ClearAndRedirect("google_account_failed");
            await signIn.SignInAsync(user, rememberMe, info.LoginProvider);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return LocalRedirect("/");
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // A concurrent callback may have created this account. Retry through a fresh login.
            return await ClearAndRedirect("google_retry");
        }
    }

    [HttpPost("link")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Link(LoginRequest request)
    {
        var info = await signIn.GetExternalLoginInfoAsync();
        if (!ValidGoogleIdentity(info))
            return BadRequest(new { message = "Phiên Google đã hết hạn. Vui lòng bấm Google để bắt đầu lại." });
        var identity = request.Identity.Trim();
        var user = identity.Contains('@') ? await users.FindByEmailAsync(identity) : await users.FindByNameAsync(identity);
        var googleEmail = info!.Principal.FindFirstValue(ClaimTypes.Email)!;
        if (user is null || users.NormalizeEmail(user.Email) != users.NormalizeEmail(googleEmail))
            return Unauthorized(new { message = "Vui lòng nhập đúng tài khoản đã đăng ký với email Google này." });
        var checkedPassword = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (checkedPassword.IsLockedOut)
            return StatusCode(423, new { message = "Tài khoản tạm khóa. Vui lòng thử lại sau 5 phút." });
        if (!checkedPassword.Succeeded)
            return Unauthorized(new { message = "Tên đăng nhập hoặc mật khẩu không chính xác." });
        var linkedUser = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (linkedUser is not null && linkedUser.Id != user.Id)
            return Conflict(new { message = "Google này đã liên kết với tài khoản khác." });
        if (linkedUser is null)
        {
            try
            {
                var result = await users.AddLoginAsync(user, info);
                if (!result.Succeeded) return Conflict(new { message = "Không thể liên kết Google. Vui lòng thử lại." });
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return Conflict(new { message = "Liên kết Google vừa thay đổi. Vui lòng bấm Google để thử lại." });
            }
        }
        await signIn.SignInAsync(user, request.RememberMe, info.LoginProvider);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Ok(new { account = new { id = user.Id, username = user.UserName, displayName = user.DisplayName, email = user.Email } });
    }

    private static bool ValidGoogleIdentity(ExternalLoginInfo? info) =>
        info?.LoginProvider == GoogleDefaults.AuthenticationScheme
        && !string.IsNullOrWhiteSpace(info.ProviderKey)
        && !string.IsNullOrWhiteSpace(info.Principal.FindFirstValue(ClaimTypes.Email))
        && string.Equals(info.Principal.FindFirstValue(GoogleAuthentication.VerifiedEmailClaim), "true", StringComparison.OrdinalIgnoreCase);

    private LocalRedirectResult ErrorRedirect(string code) => LocalRedirect("/?authError=" + code);
    private async Task<IActionResult> ClearAndRedirect(string code)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return ErrorRedirect(code);
    }
}
