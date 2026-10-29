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

Đăng nhập Google và Facebook đã có luồng tích hợp; cần cấu hình khóa riêng của từng ứng dụng theo hướng dẫn bên dưới.
Apple chưa triển khai; nút chỉ thông báo sẽ bổ sung sau.
Tài khoản demo cũ trong localStorage không được chuyển sang SQL Server; cần đăng ký lại.
Danh sách phòng và trạng thái người chơi (`Online` / `In-game` / `Offline`) được đồng bộ thời gian thực trên server qua `RoomManagerService` và SignalR (`ChessHub`).

## Các bảng

| Bảng | Mục đích |
| --- | --- |
| `AspNetUsers` | Tài khoản, tên hiển thị, email, password hash, thời gian tạo, trạng thái khóa |
| `AspNetRoles`, `AspNetUserRoles` | Vai trò và liên kết người dùng/vai trò theo chuẩn Identity |
| `AspNetUserClaims`, `AspNetRoleClaims` | Thuộc tính/quyền theo chuẩn Identity |
| `AspNetUserLogins` | Liên kết ID tài khoản Google/Facebook với người dùng trong game |
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

## Bật đăng nhập Facebook

1. Mở [Meta for Developers → My Apps](https://developers.facebook.com/apps/), chọn Create App và đặt tên, ví dụ BanCoTuong.
2. Chọn use case Facebook Login (có thể có tên “Authenticate and request data from users with Facebook Login” hoặc “Allow people to log in with their Facebook account”). Dùng luồng đăng nhập Facebook cho người chơi; không chọn Facebook Login for Business.
3. Trong Use cases → Facebook Login → Customize, bật quyền `email` bên cạnh `public_profile`.
4. Vào phần Settings của Facebook Login, bật Client OAuth Login và Web OAuth Login.
5. Thêm **Valid OAuth Redirect URI** chính xác: **`https://localhost:7026/signin-facebook`**, rồi Save Changes.
6. Nếu Meta hỏi nền tảng Website/Site URL, dùng `https://localhost:7026/` cho bản phát triển.
7. Trong App settings → Basic, lấy **App ID** và **App Secret** (nút Show có thể yêu cầu xác nhận mật khẩu Facebook).

Giao diện Meta có thể thay đổi tên/vị trí menu theo tài khoản; callback và quyền email vẫn cần cấu hình đúng.
Khi ứng dụng ở Development mode, thêm thành viên nhóm vào App roles (Developer/Tester) và yêu cầu họ chấp nhận lời mời trước khi thử.
Để mở cho người dùng ngoài nhóm, hoàn thành các yêu cầu xuất bản/quyền truy cập Meta hiển thị trên dashboard,
bao gồm các URL chính sách quyền riêng tư và xóa dữ liệu nếu được yêu cầu. Không cần xuất bản chỉ để thử trong nhóm có vai trò trên app.

Nhập App ID/App Secret trên mỗi máy phát triển:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Set-FacebookLogin.ps1
```

App Secret được ẩn khi nhập, lưu vào .NET User Secrets và không đưa vào Git hoặc tham số dòng lệnh.
Script chỉ cập nhật cấu hình Facebook, giữ nguyên cấu hình Google và các User Secrets khác.
Người khác pull code phải nhập cấu hình trên máy mình; có thể dùng chung Meta app và thêm nhau vào App roles.

Chạy lại ứng dụng bằng HTTPS:

```powershell
dotnet run --project .\23DTHD5_Demo --launch-profile https
```

Mở **https://localhost:7026**, bấm **f**, chọn tài khoản Facebook và cấp quyền.
Nếu Facebook báo redirect không hợp lệ, kiểm tra URL có đúng HTTPS, localhost, cổng 7026 và `/signin-facebook` không.
Nếu gặp lỗi thiếu quyền `email`/Invalid Scopes, kiểm tra quyền email trong use case và tài khoản thử có vai trò trên app.
Nếu Facebook không chia sẻ email (ví dụ chỉ có số điện thoại hoặc người dùng từ chối quyền),
đăng ký mới sẽ báo rõ; tài khoản Facebook đã liên kết vẫn có thể đăng nhập theo ID mà không cần email trả về lại.

Lần đầu, server tạo người dùng có tên đăng nhập bắt đầu bằng `fb`, tên hiển thị từ hồ sơ Facebook,
và liên kết Facebook ID trong `AspNetUserLogins`. Không tạo mật khẩu game giả.
Nếu email trùng tài khoản game, nhập mật khẩu game để liên kết; không tự gộp tài khoản chỉ bằng email.
Nếu tài khoản đang dùng Google, đăng nhập Google trước rồi bấm **Liên kết Facebook** trong sảnh.
Liên kết được gắn với phiên đăng nhập đã bắt đầu yêu cầu, không tự chuyển Facebook đang thuộc tài khoản khác.

Endpoint `/api/auth/facebook/status` cho biết đã nạp đủ App ID/App Secret hay chưa, không xác nhận khóa có hợp lệ với Meta.
Các endpoint được cấu hình Graph API v22.0 theo handler ASP.NET Core hiện hành, thay cho mặc định v14.0 cũ trong package .NET 8.
Khi deploy, cấu hình `Authentication__Facebook__AppId` và `Authentication__Facebook__AppSecret` trên máy chủ,
và cập nhật callback sang tên miền HTTPS thật. Không cần migration mới vì dùng bảng Identity hiện có.

Tài liệu: [Facebook Login cho ASP.NET Core](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/social/facebook-logins?view=aspnetcore-8.0),
[Facebook Login for Web](https://developers.facebook.com/docs/facebook-login/web/).

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

Kiểm thử Facebook bằng phản hồi giả lập, không liên hệ Meta hoặc cần App ID/App Secret thật:

```powershell
dotnet run --project .\tests\FacebookLoginChecks
```

Bộ kiểm thử dùng middleware OAuth thật và SQL Server: tạo tài khoản, đăng nhập lại, hủy đăng nhập,
state/correlation/PKCE, app secret proof, thiếu email, khóa tài khoản, liên kết bằng mật khẩu,
liên kết khi đã đăng nhập, chống chiếm liên kết và chống đổi phiên đăng nhập giữa lúc OAuth.
Tài khoản thử có hậu tố email ngẫu nhiên, được tự dọn sau khi chạy.


## Tham gia vào Room (3 hình thức)

Hệ thống quản lý phòng và trạng thái kết nối người chơi theo thời gian thực qua `RoomManagerService`, `RoomController` (`/api/rooms`) và SignalR (`ChessHub`):

### 1. Mời bạn bè trong game (In-game Invitation)

1. Khi tạo phòng xong hoặc đang ở trong phòng cờ, Host bấm **👥 Mời bạn bè**.
2. Hệ thống tải danh sách người chơi từ `AspNetUsers` kèm trạng thái kết nối thời gian thực:
   - **Online**: Đang ở sảnh chờ, có thể bấm **Mời**.
   - **In-game**: Đang ở trong một phòng đấu khác (hiển thị mã phòng hiện tại, khóa nút mời).
   - **Offline**: Chưa kết nối (khóa nút mời).
3. Khi Host bấm **Mời**, SignalR gửi sự kiện `ReceiveRoomInvite` trực tiếp tới thiết bị của người bạn với thông báo:
   `"[Tên Host] mời bạn vào phòng [Room ID]. [Chấp nhận] / [Từ chối]"`.
4. Khi người bạn bấm **Chấp nhận**, hệ thống dùng `inviteToken` của lời mời để đưa thẳng vào phòng của Host và **tự động bỏ qua bước nhập mật khẩu** kể cả với phòng riêng tư.

### 2. Mời qua Mạng xã hội (Deep Link)

1. Host bấm **↗ Chia sẻ MXH** để mở bảng chia sẻ phòng.
2. Ứng dụng sinh **Deep Link** chuẩn `cotuong://room/{roomId}` (kèm `?token=...` nếu là phòng riêng tư) và Link Web tương ứng (`/room/{roomId}?deeplink=cotuong://room/{roomId}`), hỗ trợ chia sẻ qua **Zalo**, **Facebook**, **Messenger** hoặc sao chép nhanh.
3. Khi người nhận mở link trên trình duyệt hoặc dán Deep Link vào ô tìm phòng, trang web tự kích hoạt và đưa thẳng người dùng vào bàn cờ của phòng đó.

### 3. Vào phòng bằng Mã phòng (Room Code — 6 chữ số)

1. Khách tại Sảnh chờ bấm **🔢 Tìm phòng**, nhập đúng **6 chữ số Room ID** rồi bấm **Vào phòng** (hoặc nhập mã 6 số tại ô tìm kiếm sảnh chờ và nhấn Enter).
2. Nếu là **phòng công khai**: Vào thẳng bàn cờ.
3. Nếu là **phòng riêng tư**: Hiện popup yêu cầu nhập mật khẩu phòng.
4. **Xử lý ngoại lệ (Edge Cases)**:
   - Nhập sai mã phòng (không tồn tại): Báo lỗi **`"Không tìm thấy phòng"`**.
   - Phòng đã đủ 2 người và không cho phép xem: Báo lỗi **`"Phòng đã đủ người chơi"`**.
   - Phòng đang trong ván đấu và tắt chế độ xem: Báo lỗi **`"Phòng đang thi đấu, không cho phép khán giả theo dõi"`**.
   - Nhập sai mật khẩu phòng riêng tư: Báo lỗi **`"Mật khẩu phòng không chính xác"`**.

Các mã phòng mẫu có sẵn khi khởi động để kiểm thử nhanh:
- `839201`: Phòng công khai đang chờ (1/2 người).
- `706324`: Phòng riêng tư (mật khẩu: `1234`).
- `482715`: Phòng đang thi đấu (2/2 người), cho phép khán giả vào xem.
- `519402`: Phòng đã đủ 2/2 người, tắt chế độ xem (kiểm thử lỗi *"Phòng đã đủ người chơi"*).
- `628190`: Phòng đang thi đấu, tắt chế độ xem (kiểm thử lỗi *"Phòng đang thi đấu, không cho phép khán giả theo dõi"*).
