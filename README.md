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

Đăng nhập Google đã có luồng tích hợp; cần cấu hình OAuth Client theo hướng dẫn bên dưới.
Facebook và Apple chưa triển khai; các nút chỉ thông báo sẽ bổ sung sau.
Tài khoản demo cũ trong localStorage không được chuyển sang SQL Server; cần đăng ký lại.
Danh sách phòng vẫn dùng localStorage theo thiết kế hiện tại; phần này chưa chuyển sang database.

## Các bảng

| Bảng | Mục đích |
| --- | --- |
| `AspNetUsers` | Tài khoản, tên hiển thị, email, password hash, thời gian tạo, trạng thái khóa |
| `AspNetRoles`, `AspNetUserRoles` | Vai trò và liên kết người dùng/vai trò theo chuẩn Identity |
| `AspNetUserClaims`, `AspNetRoleClaims` | Thuộc tính/quyền theo chuẩn Identity |
| `AspNetUserLogins` | Liên kết ID tài khoản Google với người dùng trong game |
| `AspNetUserTokens` | Token theo chuẩn Identity |
| `__EFMigrationsHistory` | Các migration đã áp dụng |

`NormalizedUserName` và `NormalizedEmail` có unique index để chống trùng cả khi có yêu cầu đồng thời.
Không có cột lưu mật khẩu gốc hoặc mật khẩu xác nhận. Mật khẩu được hash bằng PasswordHasher của Identity.
Phiên đăng nhập nằm trong cookie HttpOnly, không lưu tài khoản/mật khẩu/phiên vào localStorage.

## Bật đăng nhập Google

1. Mở [Google Cloud Console](https://console.cloud.google.com/), tạo hoặc chọn project.
2. Trong Google Auth Platform → Branding, nhập tên ứng dụng, email hỗ trợ và email liên hệ.
3. Trong Audience, chọn External. Nếu dùng chế độ Testing và có mục Test users, thêm Gmail dùng để thử.
4. Trong Clients → Create client, chọn **Web application**.
5. Thêm Authorized redirect URI chính xác: **`https://localhost:7026/signin-google`**.
6. Lưu Client ID và Client Secret sau khi tạo. Luồng backend này không cần JavaScript origin hay API key.

Nhập cấu hình tại Terminal ở thư mục project:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-GoogleLogin.ps1
```

Script hỏi Client ID và Client Secret (ẩn khi nhập), rồi lưu bằng .NET User Secrets trên máy.
Secret không được ghi vào mã nguồn/Git hoặc đưa vào tham số dòng lệnh.
Script chỉ cập nhật cấu hình Google, không thay đổi các User Secrets khác.

Chạy lại ứng dụng bằng HTTPS:

```powershell
dotnet run --project .\23DTHD5_Demo --launch-profile https
```

Mở **https://localhost:7026**, bấm **G**, chọn Gmail và chấp thuận đăng nhập.
Nếu máy chưa tin cậy chứng chỉ HTTPS phát triển, chạy `dotnet dev-certs https --trust` trên máy để cài chứng chỉ.
Giữ nguyên địa chỉ HTTPS khi kiểm thử Google; nếu thay host/cổng phải cập nhật redirect URI trong Google Cloud cho khớp.
Endpoint `/api/auth/google/status` trả `enabled: true` khi ứng dụng đã nạp cả hai giá trị cấu hình,
nhưng không xác nhận Client ID/Secret có được Google chấp nhận hay không.

Lần đầu, tài khoản Google được lưu vào `AspNetUsers` và liên kết `Google` + ID người dùng tại `AspNetUserLogins`.
Tên hiển thị lấy từ hồ sơ Google (tối đa 30 ký tự), tên đăng nhập game được tạo tự động bắt đầu bằng `gg`.
Tài khoản chỉ dùng Google không có mật khẩu game và không thể dùng OTP demo để tạo lại mật khẩu.
Lần sau, ID Google được dùng để tìm đúng tài khoản cũ.

Nếu email trùng tài khoản game có sẵn, ứng dụng yêu cầu nhập mật khẩu game để liên kết Google,
rồi các lần sau có thể dùng cả Google lẫn mật khẩu game. Không tự gộp tài khoản chỉ bằng email.
Phiên xác thực Google để liên kết tồn tại 5 phút; nếu hết hạn hãy bấm Google và bắt đầu lại.
Cookie của game giữ phiên đăng nhập; access token/client secret Google không được trả cho JavaScript.

Khi deploy, cấu hình `Authentication__Google__ClientId` và `Authentication__Google__ClientSecret`
bằng biến môi trường hoặc kho bí mật của máy chủ, rồi thêm callback HTTPS của tên miền thật vào Google Cloud.
User Secrets chỉ dành cho môi trường Development. Google Login cần Internet và SQL Server hoạt động.
Không cần migration mới vì `AspNetUserLogins` đã có trong migration Identity ban đầu.

Tài liệu: [Google trên ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/google-logins?view=aspnetcore-8.0),
[Google OAuth/OpenID Connect](https://developers.google.com/identity/openid-connect/openid-connect).

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

Kiểm thử tích hợp Google với phản hồi giả lập (không cần Google Client ID/Secret thật, không liên hệ Google):

```powershell
dotnet run --project .\tests\GoogleLoginChecks
```

Kiểm thử sử dụng OAuth middleware thật và SQL Server, bao gồm state/PKCE/correlation cookie,
callback giả mạo, email chưa xác thực, hủy đăng nhập, tạo và đăng nhập lại tài khoản,
liên kết tài khoản cũ sau khi kiểm tra mật khẩu, trạng thái khóa và chống đặt lại mật khẩu Google bằng OTP demo.
Các tài khoản thử được tự dọn theo hậu tố email ngẫu nhiên riêng cho từng lần chạy.
Kiểm thử Gmail thật cần bạn cấu hình OAuth Client và tự chọn tài khoản trên trang Google.
