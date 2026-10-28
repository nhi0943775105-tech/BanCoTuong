# Bàn cờ tướng — tài khoản và SQL Server

Ứng dụng ASP.NET Core MVC .NET 8, Vue 3, SignalR và WebRTC.
Tài khoản dùng ASP.NET Core Identity + Entity Framework Core 8, được lưu trong SQL Server qua migration.

## Chạy trên máy hiện tại

SQL Server Express đang dùng: `.\SQLEXPRESS`. Database: `BanCoTuong`.
Kết nối dùng Windows Authentication, không cần ghi mật khẩu SQL vào mã nguồn.

Chạy các lệnh từ thư mục chứa `23DTHD5_Demo.sln`:

```powershell
dotnet restore
dotnet tool restore
dotnet ef database update --project .\23DTHD5_Demo
dotnet run --project .\23DTHD5_Demo --launch-profile http
```

Mở <http://localhost:5275>. Với HTTPS, dùng `--launch-profile https` và mở <https://localhost:7026>.

Khi dùng SSMS (SQL Server Management Studio), kết nối Server name `.\SQLEXPRESS`, chọn Windows Authentication,
rồi mở Databases → BanCoTuong → Tables. Nếu thông báo chứng chỉ SQL Server, bật Trust server certificate cho kết nối local.

Nếu máy khác dùng instance SQL khác, đặt `ConnectionStrings:DefaultConnection` trong
`23DTHD5_Demo/appsettings.Local.json` (file này được Git bỏ qua):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SQLEXPRESS;Database=BanCoTuong;Trusted_Connection=True;TrustServerCertificate=True"
  }
}
```

Tài khoản Windows chạy ứng dụng cần quyền truy cập database; tài khoản chạy migration cần quyền tạo database/bảng.

## Luồng tài khoản

1. Đăng ký tên đăng nhập (4–20 chữ cái/số), tên hiển thị (2–30 ký tự), email, mật khẩu và xác nhận mật khẩu.
2. Server kiểm tra dữ liệu, tên/email trùng và độ mạnh mật khẩu. Bản nháp được giữ ở bộ nhớ server với mật khẩu đã hash.
3. Nhập OTP **123456** để tạo tài khoản vào database và vào sảnh chờ.
4. Sau khi đăng xuất, có thể đăng nhập bằng tên đăng nhập hoặc email, không phân biệt hoa/thường.
5. “Ghi nhớ đăng nhập” tạo cookie có thời hạn 14 ngày; không chọn thì dùng cookie phiên.
6. “Quên mật khẩu” dùng cùng OTP demo để đặt lại mật khẩu trong SQL Server.

**OTP 123456 chỉ là giả lập theo yêu cầu, chưa gửi email thật và không chứng minh quyền sở hữu email.**
Không dùng bản OTP cố định này cho người dùng thật: người biết email cũng có thể dùng luồng quên mật khẩu.
Khi triển khai thật, cần thay bằng mã ngẫu nhiên và dịch vụ gửi email.

OTP hết hạn sau 120 giây, giới hạn 5 lần nhập sai; chỉ được gửi lại khi mã hết hạn, tối đa 3 lần.
Phiên OTP/bản nháp tồn tại tối đa 10 phút trong bộ nhớ; nếu restart ứng dụng, người dùng cần bắt đầu đăng ký/khôi phục lại.
Sai mật khẩu 5 lần khóa tài khoản trong 5 phút. Các API xác thực có kiểm tra CSRF và giới hạn tần suất.

Google, Facebook, Apple chưa triển khai; các nút chỉ thông báo sẽ bổ sung sau.
Tài khoản demo cũ trong localStorage không được chuyển sang SQL Server; cần đăng ký lại.
Danh sách phòng vẫn dùng localStorage theo thiết kế hiện tại; phần này chưa chuyển sang database.

## Các bảng

| Bảng | Mục đích |
| --- | --- |
| `AspNetUsers` | Tài khoản, tên hiển thị, email, password hash, thời gian tạo, trạng thái khóa |
| `AspNetRoles`, `AspNetUserRoles` | Vai trò và liên kết người dùng/vai trò theo chuẩn Identity |
| `AspNetUserClaims`, `AspNetRoleClaims` | Thuộc tính/quyền theo chuẩn Identity |
| `AspNetUserLogins` | Bảng chuẩn Identity cho liên kết đăng nhập ngoài (chưa sử dụng) |
| `AspNetUserTokens` | Token theo chuẩn Identity |
| `__EFMigrationsHistory` | Các migration đã áp dụng |

`NormalizedUserName` và `NormalizedEmail` có unique index để chống trùng cả khi có yêu cầu đồng thời.
Không có cột lưu mật khẩu gốc hoặc mật khẩu xác nhận. Mật khẩu được hash bằng PasswordHasher của Identity.
Phiên đăng nhập nằm trong cookie HttpOnly, không lưu tài khoản/mật khẩu/phiên vào localStorage.

## Thay đổi cấu trúc database về sau

Sửa model/DbContext, rồi tạo một migration mới:

```powershell
dotnet ef migrations add TenThayDoi --project .\23DTHD5_Demo --output-dir Data/Migrations
dotnet ef database update --project .\23DTHD5_Demo
```

Không sửa/xóa migration đã áp dụng trên database đang sử dụng.

## Kiểm thử

Sau khi chạy ứng dụng ở cổng 5275:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-Auth.ps1
```

Script kiểm thử API trên database SQL Server thật: CSRF, đăng ký, OTP sai/đúng, lưu hash,
trùng tên/email, cookie, ghi nhớ đăng nhập, đăng xuất, khóa do sai mật khẩu, khôi phục và đăng nhập lại.
Script chỉ tạo tài khoản thử có tên ngẫu nhiên và tự xóa tài khoản đó khi kết thúc.

Kiểm thử form trực tiếp bằng Microsoft Edge headless (không cần Node.js):

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test-AuthBrowser.ps1
```

Script mở một profile Edge tạm trong `.artifacts`, thử đăng ký/OTP/đăng xuất/đăng nhập bằng email,
kiểm tra tên người chơi ở sảnh và form tạo phòng, rồi tự xóa tài khoản thử.
