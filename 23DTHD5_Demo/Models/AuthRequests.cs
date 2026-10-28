using System.ComponentModel.DataAnnotations;

namespace _23DTHD5_Demo.Models;

public class RegisterRequest
{
    [Required(ErrorMessage = "Vui lòng nhập tên đăng nhập.")]
    [RegularExpression("^[a-zA-Z0-9]{4,20}$", ErrorMessage = "Tên đăng nhập gồm 4–20 chữ cái hoặc chữ số.")]
    public string Username { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập tên hiển thị.")]
    [StringLength(30, MinimumLength = 2, ErrorMessage = "Tên hiển thị từ 2–30 ký tự.")]
    public string DisplayName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập email.")]
    [EmailAddress(ErrorMessage = "Email không hợp lệ.")]
    [StringLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu.")]
    [StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu từ 8–128 ký tự.")]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu.")]
    [Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class LoginRequest
{
    [Required, StringLength(256)]
    public string Identity { get; set; } = string.Empty;
    [Required, StringLength(128)]
    public string Password { get; set; } = string.Empty;
    public bool RememberMe { get; set; }
}

public class OtpRequest
{
    [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "Mã OTP phải gồm 6 chữ số.")]
    public string Code { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [Required, EmailAddress, StringLength(256)]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest : OtpRequest
{
    [Required, StringLength(128, MinimumLength = 8, ErrorMessage = "Mật khẩu từ 8–128 ký tự.")]
    public string Password { get; set; } = string.Empty;
    [Required, Compare(nameof(Password), ErrorMessage = "Mật khẩu xác nhận không khớp.")]
    public string ConfirmPassword { get; set; } = string.Empty;
}
