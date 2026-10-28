using _23DTHD5_Demo.Models;
using _23DTHD5_Demo.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;

namespace _23DTHD5_Demo.Controllers.api;

[ApiController]
[Route("api/auth")]
[AutoValidateAntiforgeryToken]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class AuthController(
    UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn,
    PendingAuthStore pending,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf")]
    public IActionResult Csrf() => Ok(new { token = antiforgery.GetAndStoreTokens(HttpContext).RequestToken });

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await users.GetUserAsync(User);
        return user is null ? Unauthorized() : Ok(new { account = Account(user) });
    }

    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser
        {
            UserName = request.Username.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Email = request.Email.Trim()
        };
        if (user.DisplayName.Length < 2)
            return BadRequest(new { message = "Tên hiển thị từ 2–30 ký tự." });

        var errors = new List<IdentityError>();
        foreach (var validator in users.UserValidators)
            errors.AddRange((await validator.ValidateAsync(users, user)).Errors);
        foreach (var validator in users.PasswordValidators)
            errors.AddRange((await validator.ValidateAsync(users, user, request.Password)).Errors);
        if (errors.Count > 0) return IdentityFailure(errors);

        // The draft exists only on the server, with a password hash, until OTP verification.
        user.PasswordHash = users.PasswordHasher.HashPassword(user, request.Password);
        pending.Set(HttpContext, "register", new PendingAuth { RegistrationUser = user });
        return Ok(new { message = "Nhập mã OTP demo 123456 để hoàn tất đăng ký.", expiresIn = PendingAuthStore.OtpLifetimeSeconds });
    }

    [HttpPost("verify-registration")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyRegistration(OtpRequest request)
    {
        var state = pending.Get(HttpContext, "register");
        if (state?.RegistrationUser is null) return MissingOtp();
        await state.Gate.WaitAsync();
        try
        {
            var failure = CheckOtp(state, request.Code);
            if (failure is not null) return failure;
            var user = state.RegistrationUser;
            user.EmailConfirmed = true;
            try
            {
                var result = await users.CreateAsync(user);
                if (!result.Succeeded) return IdentityFailure(result.Errors);
            }
            catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
            {
                return Conflict(new { message = "Tên đăng nhập hoặc email đã được đăng ký. Vui lòng dùng thông tin khác." });
            }
            state.Consumed = true;
            pending.Remove(HttpContext, "register");
            await signIn.SignInAsync(user, isPersistent: false);
            return Ok(new { message = "Đăng ký thành công!", account = Account(user) });
        }
        finally { state.Gate.Release(); }
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var identity = request.Identity.Trim();
        var user = identity.Contains('@')
            ? await users.FindByEmailAsync(identity)
            : await users.FindByNameAsync(identity);
        if (user is null) return InvalidLogin();
        var result = await signIn.PasswordSignInAsync(user, request.Password, request.RememberMe, lockoutOnFailure: true);
        if (result.IsLockedOut)
            return StatusCode(StatusCodes.Status423Locked, new { message = "Tài khoản tạm khóa do nhập sai nhiều lần. Vui lòng thử lại sau 5 phút." });
        if (!result.Succeeded) return InvalidLogin();
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Ok(new { account = Account(user) });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        pending.Remove(HttpContext, "register");
        pending.Remove(HttpContext, "reset");
        return Ok(new { message = "Đã đăng xuất." });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        // Google-only accounts must authenticate with Google; demo OTP must not create a local password for them.
        if (user is not null && !await users.HasPasswordAsync(user)) user = null;
        pending.Set(HttpContext, "reset", new PendingAuth
        {
            ResetUserId = user?.Id,
            ResetToken = user is null ? null : await users.GeneratePasswordResetTokenAsync(user)
        });
        // Same response whether or not the email exists.
        return Ok(new { message = "Nếu email đã đăng ký, bạn có thể dùng OTP demo để đặt lại mật khẩu.", expiresIn = PendingAuthStore.OtpLifetimeSeconds });
    }

    [HttpPost("verify-reset-otp")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> VerifyResetOtp(OtpRequest request)
    {
        var state = pending.Get(HttpContext, "reset");
        if (state is null) return MissingOtp();
        await state.Gate.WaitAsync();
        try
        {
            return (IActionResult?)CheckOtp(state, request.Code) ?? Ok(new { message = "Mã hợp lệ. Vui lòng nhập mật khẩu mới." });
        }
        finally { state.Gate.Release(); }
    }

    [HttpPost("reset-password")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var state = pending.Get(HttpContext, "reset");
        if (state is null) return MissingOtp();
        await state.Gate.WaitAsync();
        try
        {
            var failure = CheckOtp(state, request.Code);
            if (failure is not null) return failure;
            var user = state.ResetUserId is null ? null : await users.FindByIdAsync(state.ResetUserId);
            if (user is null || state.ResetToken is null)
                return BadRequest(new { message = "Không thể đặt lại mật khẩu. Vui lòng kiểm tra email và thử lại." });
            if (await users.CheckPasswordAsync(user, request.Password))
                return BadRequest(new { message = "Mật khẩu mới không được trùng mật khẩu hiện tại." });
            var result = await users.ResetPasswordAsync(user, state.ResetToken, request.Password);
            if (!result.Succeeded) return IdentityFailure(result.Errors);
            state.Consumed = true;
            pending.Remove(HttpContext, "reset");
            await signIn.SignOutAsync();
            return Ok(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập." });
        }
        finally { state.Gate.Release(); }
    }

    [HttpPost("resend-otp/{purpose}")]
    [EnableRateLimiting("auth")]
    public async Task<IActionResult> ResendOtp(string purpose)
    {
        if (purpose is not ("register" or "reset")) return NotFound();
        var state = pending.Get(HttpContext, purpose);
        if (state is null) return MissingOtp();
        await state.Gate.WaitAsync();
        try
        {
            if (state.Consumed) return MissingOtp();
            if (state.ExpiresAt > DateTimeOffset.UtcNow)
                return BadRequest(new { message = "Vui lòng đợi mã hiện tại hết hạn trước khi gửi lại." });
            if (state.Resends >= 3)
                return BadRequest(new { message = "Đã hết lượt gửi lại. Vui lòng bắt đầu lại." });
            state.Resends++;
            state.Attempts = 0;
            state.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(PendingAuthStore.OtpLifetimeSeconds);
            return Ok(new { message = "Mã OTP demo vẫn là 123456.", expiresIn = PendingAuthStore.OtpLifetimeSeconds });
        }
        finally { state.Gate.Release(); }
    }

    private BadRequestObjectResult? CheckOtp(PendingAuth state, string code)
    {
        if (state.Consumed) return MissingOtp();
        if (DateTimeOffset.UtcNow >= state.ExpiresAt)
            return BadRequest(new { message = "Mã OTP đã hết hạn. Vui lòng gửi lại mã." });
        if (state.Attempts >= 5)
            return BadRequest(new { message = "Đã nhập sai OTP 5 lần. Vui lòng đợi hết hạn và gửi lại mã." });
        if (code == PendingAuthStore.DemoOtp) return null;
        state.Attempts++;
        return BadRequest(new { message = "Mã OTP không chính xác." });
    }

    private BadRequestObjectResult MissingOtp() => BadRequest(new { message = "Phiên xác thực không còn hiệu lực. Vui lòng bắt đầu lại." });
    private UnauthorizedObjectResult InvalidLogin() => Unauthorized(new { message = "Tên đăng nhập hoặc mật khẩu không chính xác." });
    private BadRequestObjectResult IdentityFailure(IEnumerable<IdentityError> errors) =>
        BadRequest(new { message = string.Join(" ", errors.Select(e => e.Description)) });
    private static object Account(ApplicationUser user) => new
    {
        id = user.Id, username = user.UserName, displayName = user.DisplayName, email = user.Email
    };
}
