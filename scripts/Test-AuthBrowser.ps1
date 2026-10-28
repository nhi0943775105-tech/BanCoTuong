param([string]$BaseUrl = 'http://localhost:5275', [int]$DebugPort = 9237)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$edgePath = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
if (-not (Test-Path -LiteralPath $edgePath)) { throw 'Microsoft Edge was not found.' }
$artifactPath = Join-Path (Split-Path $PSScriptRoot -Parent) '.artifacts'
[void][IO.Directory]::CreateDirectory($artifactPath)
$profilePath = Join-Path $artifactPath ('browser-' + [Guid]::NewGuid().ToString('N'))
$browser = Start-Process -FilePath $edgePath -WindowStyle Hidden -PassThru -ArgumentList @(
    '--headless=new', "--remote-debugging-port=$DebugPort", ('--user-data-dir="' + $profilePath + '"'),
    '--no-first-run', '--no-default-browser-check', 'about:blank'
)
$httpHandler = [Net.Http.HttpClientHandler]::new()
$httpHandler.UseProxy = $false
$http = [Net.Http.HttpClient]::new($httpHandler)
$socket = [Net.WebSockets.ClientWebSocket]::new()
$script:commandId = 0
$testUsername = 'ui' + [Guid]::NewGuid().ToString('N').Substring(0, 12)

function Cdp([string]$Method, $Params = @{}) {
    $script:commandId++
    $id = $script:commandId
    $payload = @{ id = $id; method = $Method; params = $Params } | ConvertTo-Json -Depth 15 -Compress
    $bytes = [Text.Encoding]::UTF8.GetBytes($payload)
    $timeout = [Threading.CancellationTokenSource]::new(30000)
    try {
        [void]$socket.SendAsync([ArraySegment[byte]]::new($bytes), [Net.WebSockets.WebSocketMessageType]::Text, $true, $timeout.Token).GetAwaiter().GetResult()
        while ($true) {
            $buffer = [byte[]]::new(65536)
            $stream = [IO.MemoryStream]::new()
            try {
                do {
                    $part = $socket.ReceiveAsync([ArraySegment[byte]]::new($buffer), $timeout.Token).GetAwaiter().GetResult()
                    $stream.Write($buffer, 0, $part.Count)
                } while (-not $part.EndOfMessage)
                $message = [Text.Encoding]::UTF8.GetString($stream.ToArray()) | ConvertFrom-Json
            } finally { $stream.Dispose() }
            if ($message.id -eq $id) {
                if ($message.error) { throw ($message.error | ConvertTo-Json -Compress) }
                return $message.result
            }
        }
    } finally { $timeout.Dispose() }
}

function Evaluate([string]$Expression) {
    $result = Cdp 'Runtime.evaluate' @{ expression = $Expression; awaitPromise = $true; returnByValue = $true }
    if ($result.exceptionDetails) { throw ($result.exceptionDetails | ConvertTo-Json -Depth 8 -Compress) }
    return $result.result.value
}

try {
    $target = $null
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        try {
            $response = $http.PutAsync("http://localhost:$DebugPort/json/new?$BaseUrl", $null).GetAwaiter().GetResult()
            try { $target = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json }
            finally { $response.Dispose() }
            if ($target.webSocketDebuggerUrl) { break }
        } catch { Start-Sleep -Milliseconds 100 }
    }
    if (-not $target.webSocketDebuggerUrl) { throw 'Cannot connect to the Edge debugging endpoint.' }
    [void]$socket.ConnectAsync([Uri]$target.webSocketDebuggerUrl, [Threading.CancellationToken]::None).GetAwaiter().GetResult()
    $null = Cdp 'Page.enable'
    for ($attempt = 0; $attempt -lt 150; $attempt++) {
        try {
            $ready = Evaluate "location.origin === '$BaseUrl' && document.readyState !== 'loading' && !!window.hongKyAuth"
            if ($ready) { break }
        } catch { }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw 'The application page did not finish loading.' }
    $registrationFlow = @'
(async () => {
    const wait = async (predicate) => { for (let i = 0; i < 150; i++) { if (predicate()) return; await new Promise(r => setTimeout(r, 100)); } throw new Error('Timed out waiting for UI'); };
    const q = s => document.querySelector(s);
    await wait(() => window.hongKyAuth);
    await window.hongKyAuth.ready;
    q('[data-auth-view="register"]').click();
    const fields = { 'register-username': '__USERNAME__', 'register-display': 'Browser Player', 'register-email': '__USERNAME__@example.com', 'register-password': 'BrowserTest!123', 'register-confirm': 'BrowserTest!123' };
    for (const [id, value] of Object.entries(fields)) q('#' + id).value = value;
    q('#register-form').requestSubmit();
    await wait(() => !q('#otp-modal').classList.contains('hidden') || q('#register-feedback').textContent);
    if (q('#otp-modal').classList.contains('hidden')) throw new Error(q('#register-feedback').textContent);
    [...document.querySelectorAll('.otp-inputs input')].forEach(input => input.value = '0');
    q('#verify-otp').click();
    await wait(() => !q('#verify-otp').disabled && q('#otp-feedback').textContent);
    [...document.querySelectorAll('.otp-inputs input')].forEach((input, i) => input.value = '123456'[i]);
    q('#verify-otp').click();
    await wait(() => !q('#lobby-shell').classList.contains('hidden'));
    if (q('#lobby-player-name').textContent !== 'Browser Player') throw new Error('Wrong lobby display name');
    if (localStorage.getItem('hongky_session') || localStorage.getItem('hongky_accounts')) throw new Error('Credentials leaked to localStorage');
    if (!window.hongKyAuth.account.id) throw new Error('No server account ID');
    return { username: window.hongKyAuth.account.username, lobbyVisible: true };
})()
'@
    $result = Evaluate ($registrationFlow.Replace('__USERNAME__', $testUsername))
    if ($result.username -ne $testUsername) { throw 'Unexpected registration result.' }
    $screenshot = Cdp 'Page.captureScreenshot' @{ format = 'png' }
    [IO.File]::WriteAllBytes((Join-Path $artifactPath 'auth-lobby.png'), [Convert]::FromBase64String($screenshot.data))
    $null = Evaluate "document.querySelector('#logout-button').click(); true"
    Start-Sleep -Milliseconds 1000
    $loginFlow = @'
(async () => {
    const wait = async (predicate) => { for (let i = 0; i < 150; i++) { if (predicate()) return; await new Promise(r => setTimeout(r, 100)); } throw new Error('Timed out waiting for login UI'); };
    const q = s => document.querySelector(s);
    await wait(() => window.hongKyAuth && !q('#auth-shell').classList.contains('hidden'));
    await window.hongKyAuth.ready;
    q('#login-identity').value = '__USERNAME__@example.com';
    q('#login-password').value = 'BrowserTest!123';
    q('#remember-me').checked = true;
    q('#login-form').requestSubmit();
    await wait(() => !q('#lobby-shell').classList.contains('hidden') || q('#login-feedback').textContent);
    if (q('#lobby-shell').classList.contains('hidden')) throw new Error(q('#login-feedback').textContent);
    q('#create-room-button').click();
    if (!q('#room-name').value.endsWith('Browser Player')) throw new Error('Room form did not use authenticated profile');
    return window.hongKyAuth.account.username;
})()
'@
    $result = Evaluate ($loginFlow.Replace('__USERNAME__', $testUsername))
    if ($result -ne $testUsername) { throw 'Login returned the wrong user.' }
    Write-Output 'PASS: Edge registration form, wrong/correct OTP, lobby name, logout, email login, and room profile.'
} finally {
    if ($socket.State -eq [Net.WebSockets.WebSocketState]::Open) {
        try { $null = Cdp 'Browser.close' } catch { }
    }
    $socket.Dispose(); $http.Dispose(); $httpHandler.Dispose()
    if (-not $browser.HasExited) { Stop-Process -Id $browser.Id -ErrorAction SilentlyContinue }
    $db = [Data.SqlClient.SqlConnection]::new('Server=.\SQLEXPRESS;Database=BanCoTuong;Integrated Security=True;TrustServerCertificate=True')
    try {
        $db.Open(); $command = $db.CreateCommand()
        $command.CommandText = 'DELETE FROM AspNetUsers WHERE UserName = @username'
        [void]$command.Parameters.AddWithValue('@username', $testUsername)
        [void]$command.ExecuteNonQuery(); $command.Dispose()
    } finally { $db.Dispose() }
}
