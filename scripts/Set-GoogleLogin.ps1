# Store credentials locally without placing the client secret in command-line arguments or Git.
$ErrorActionPreference = 'Stop'
$projectPath = Join-Path (Split-Path $PSScriptRoot -Parent) '23DTHD5_Demo\23DTHD5_Demo.csproj'
$clientId = (Read-Host 'Google Client ID').Trim()
if ($clientId -notmatch '^[A-Za-z0-9_.-]+\.apps\.googleusercontent\.com$') {
    throw 'Enter the Client ID ending in .apps.googleusercontent.com from your Web application OAuth client.'
}
$secureSecret = Read-Host 'Google Client Secret (input is hidden)' -AsSecureString
$secretPointer = [IntPtr]::Zero
try {
    $secretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSecret)
    $clientSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($secretPointer)
    if ([string]::IsNullOrWhiteSpace($clientSecret)) { throw 'Client Secret cannot be empty.' }
    $settings = @{ Authentication = @{ Google = @{ ClientId = $clientId; ClientSecret = $clientSecret.Trim() } } }
    $settings | ConvertTo-Json -Depth 4 -Compress | dotnet user-secrets set --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not save Google credentials.' }
    Write-Output 'Google credentials saved to local User Secrets. Restart the application with the https launch profile.'
} finally {
    if ($secretPointer -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($secretPointer) }
    $clientSecret = $null; $settings = $null
    $secureSecret.Dispose()
}
