param(
    [string]$BaseUrl = 'http://localhost:5275',
    [string]$ConnectionString = 'Server=.\SQLEXPRESS;Database=BanCoTuong;Integrated Security=True;TrustServerCertificate=True'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.UseProxy = $false
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.BaseAddress = [Uri]$BaseUrl
$client.Timeout = [TimeSpan]::FromSeconds(20)
$database = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)
$testUsername = 'test' + [Guid]::NewGuid().ToString('N').Substring(0, 12)
$testEmail = "$testUsername@example.com"
$testPassword = 'TestAuth!123'
$newPassword = 'ChangedAuth!456'
$script:checks = 0

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
    $script:checks++
}

function Request([string]$Method, [string]$Path, $Body = $null, [int]$Expected = 200, [switch]$NoCsrf) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), $Path)
    try {
        if ($Method -eq 'POST') {
            if (-not $NoCsrf) {
                $csrfResponse = $client.GetAsync('/api/auth/csrf').GetAwaiter().GetResult()
                try {
                    $csrf = $csrfResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
                    [void]$request.Headers.Add('X-CSRF-TOKEN', $csrf.token)
                } finally { $csrfResponse.Dispose() }
            }
            $json = if ($null -eq $Body) { '{}' } else { ConvertTo-Json -InputObject $Body -Compress }
            $request.Content = [System.Net.Http.StringContent]::new($json, [Text.Encoding]::UTF8, 'application/json')
        }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            Assert-True ([int]$response.StatusCode -eq $Expected) "$Method $Path expected $Expected, received $([int]$response.StatusCode): $text"
            if ($text.StartsWith('{')) { return $text | ConvertFrom-Json }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

function Sql([string]$Query) {
    $command = $database.CreateCommand()
    try {
        $command.CommandText = $Query
        [void]$command.Parameters.AddWithValue('@username', $testUsername)
        return $command.ExecuteScalar()
    } finally { $command.Dispose() }
}

try {
    $database.Open()
    $null = Request GET '/api/auth/me' -Expected 401
    $null = Request POST '/chessHub/negotiate?negotiateVersion=1' -Expected 401 -NoCsrf
    $registration = @{ username = $testUsername; displayName = 'Test Player'; email = $testEmail; password = $testPassword; confirmPassword = $testPassword }
    $null = Request POST '/api/auth/register' $registration -Expected 400 -NoCsrf
    $invalid = $registration.Clone(); $invalid.password = 'weak'; $invalid.confirmPassword = 'weak'
    $null = Request POST '/api/auth/register' $invalid -Expected 400
    $invalid = $registration.Clone(); $invalid.confirmPassword = 'Different!123'
    $null = Request POST '/api/auth/register' $invalid -Expected 400
    $null = Request POST '/api/auth/register' $registration
    Assert-True ((Sql 'SELECT COUNT(*) FROM AspNetUsers WHERE UserName = @username') -eq 0) 'Account persisted before OTP verification.'
    $null = Request POST '/api/auth/verify-registration' @{ code = '000000' } -Expected 400
    $null = Request POST '/api/auth/resend-otp/register' -Expected 400
    $result = Request POST '/api/auth/verify-registration' @{ code = '123456' }
    Assert-True ($result.account.username -eq $testUsername) 'Wrong registration account.'
    Assert-True ($null -eq $result.account.passwordHash -and $null -eq $result.account.password) 'API exposed password data.'
    $storedHash = Sql 'SELECT PasswordHash FROM AspNetUsers WHERE UserName = @username'
    Assert-True ($storedHash.Length -gt 40 -and $storedHash -ne $testPassword) 'Password was not hashed.'
    Assert-True ((Sql 'SELECT EmailConfirmed FROM AspNetUsers WHERE UserName = @username') -eq $true) 'Account not confirmed.'
    $result = Request GET '/api/auth/me'
    Assert-True ($result.account.displayName -eq 'Test Player') 'Display name was not saved.'
    $null = Request POST '/api/auth/verify-registration' @{ code = '123456' } -Expected 400
    $null = Request POST '/api/auth/logout' -Expected 400 -NoCsrf
    $null = Request GET '/api/auth/me'
    $null = Request POST '/api/auth/logout'
    $null = Request GET '/api/auth/me' -Expected 401
    $null = Request POST '/api/auth/login' @{ identity = $testUsername.ToUpperInvariant(); password = $testPassword }
    $null = Request POST '/api/auth/logout'
    $null = Request POST '/api/auth/login' @{ identity = $testEmail.ToUpperInvariant(); password = $testPassword; rememberMe = $true }
    $cookie = $handler.CookieContainer.GetCookies([Uri]$BaseUrl)['HongKy.Auth']
    Assert-True ($cookie.HttpOnly -and $cookie.Expires -gt [DateTime]::Now) 'Remember-me cookie is not persistent and HttpOnly.'
    $duplicate = $registration.Clone(); $duplicate.username = $testUsername.ToUpperInvariant(); $duplicate.email = "other$testEmail"
    $null = Request POST '/api/auth/register' $duplicate -Expected 400
    $duplicate = $registration.Clone(); $duplicate.username = $testUsername.Replace('test', 'alt'); $duplicate.email = $testEmail.ToUpperInvariant()
    $null = Request POST '/api/auth/register' $duplicate -Expected 400
    $null = Request POST '/api/auth/logout'
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $expectedStatus = if ($attempt -lt 5) { 401 } else { 423 }
        $null = Request POST '/api/auth/login' @{ identity = $testUsername; password = 'Incorrect!123' } -Expected $expectedStatus
    }
    $null = Request POST '/api/auth/login' @{ identity = $testUsername; password = $testPassword } -Expected 423
    $null = Sql 'UPDATE AspNetUsers SET LockoutEnd = NULL, AccessFailedCount = 0 WHERE UserName = @username'
    $null = Request POST '/api/auth/forgot-password' @{ email = $testEmail }
    $null = Request POST '/api/auth/verify-reset-otp' @{ code = '000000' } -Expected 400
    $null = Request POST '/api/auth/verify-reset-otp' @{ code = '123456' }
    $null = Request POST '/api/auth/reset-password' @{ code = '123456'; password = $testPassword; confirmPassword = $testPassword } -Expected 400
    $null = Request POST '/api/auth/reset-password' @{ code = '123456'; password = $newPassword; confirmPassword = $newPassword }
    $null = Request POST '/api/auth/login' @{ identity = $testEmail; password = $testPassword } -Expected 401
    $null = Request POST '/api/auth/login' @{ identity = $testEmail; password = $newPassword }
    $null = Request GET '/api/auth/me'
    $null = Request POST '/api/auth/logout'
    $null = Request GET '/api/auth/me' -Expected 401
    Write-Output "PASS: $script:checks assertions against HTTP endpoints and SQL Server."
} finally {
    # Remove only the randomly named account created by this script.
    if ($database.State -eq [System.Data.ConnectionState]::Open) {
        $null = Sql 'DELETE FROM AspNetUsers WHERE UserName = @username'
        $database.Close()
    }
    $database.Dispose(); $client.Dispose(); $handler.Dispose()
}
