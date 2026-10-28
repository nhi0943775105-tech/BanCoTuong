using Microsoft.AspNetCore.Identity;

namespace _23DTHD5_Demo.Services;

public class VietnameseIdentityErrors : IdentityErrorDescriber
{
    public override IdentityError DuplicateUserName(string userName) => Error(nameof(DuplicateUserName), "Tên đăng nhập đã được sử dụng.");
    public override IdentityError DuplicateEmail(string email) => Error(nameof(DuplicateEmail), "Email này đã được đăng ký.");
    public override IdentityError InvalidEmail(string? email) => Error(nameof(InvalidEmail), "Email không hợp lệ.");
    public override IdentityError InvalidUserName(string? userName) => Error(nameof(InvalidUserName), "Tên đăng nhập không hợp lệ.");
    public override IdentityError PasswordTooShort(int length) => Error(nameof(PasswordTooShort), $"Mật khẩu cần ít nhất {length} ký tự.");
    public override IdentityError PasswordRequiresDigit() => Error(nameof(PasswordRequiresDigit), "Mật khẩu cần có chữ số.");
    public override IdentityError PasswordRequiresLower() => Error(nameof(PasswordRequiresLower), "Mật khẩu cần có chữ thường.");
    public override IdentityError PasswordRequiresUpper() => Error(nameof(PasswordRequiresUpper), "Mật khẩu cần có chữ hoa.");
    public override IdentityError PasswordRequiresNonAlphanumeric() => Error(nameof(PasswordRequiresNonAlphanumeric), "Mật khẩu cần có ký tự đặc biệt.");
    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
