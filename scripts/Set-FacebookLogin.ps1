# Keep the App Secret out of Git and command-line arguments.
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) '23DTHD5_Demo\23DTHD5_Demo.csproj'
$appId = (Read-Host 'Facebook App ID').Trim()
if ($appId -notmatch '^[0-9]+$') { throw 'Facebook App ID must contain only digits.' }
$secureSecret = Read-Host 'Facebook App Secret (input is hidden)' -AsSecureString
$secretPointer = [IntPtr]::Zero
try {
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSecret)
    $appSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
    if ([string]::IsNullOrWhiteSpace($appSecret)) { throw 'App Secret cannot be empty.' }
    $settings = @{ Authentication = @{ Facebook = @{ AppId = $appId; AppSecret = $appSecret.Trim() } } }
    $settings | ConvertTo-Json -Depth 4 -Compress | dotnet user-secrets set --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not save Facebook credentials.' }
    Write-Output 'Facebook credentials saved to local User Secrets. Restart the app with the https launch profile.'
} finally {
    if ($secretPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer) }
    $appSecret = $null; $settings = $null
    $secureSecret.Dispose()
}
