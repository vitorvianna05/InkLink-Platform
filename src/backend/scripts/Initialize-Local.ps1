$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
$envFile = Join-Path $repoRoot '.env.local'
if (-not (Test-Path -LiteralPath $envFile)) {
    $entries = foreach ($name in 'POSTGRES_PASSWORD', 'IDENTITY_OWNER_PASSWORD', 'IDENTITY_APP_PASSWORD') {
        $secretValue = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        "$name=$secretValue"
    }
    [IO.File]::WriteAllLines($envFile, $entries)
}
$settings = @{}
foreach ($line in Get-Content -LiteralPath $envFile) {
    if ($line -match '^([A-Z_]+)=(.+)$') { $settings[$matches[1]] = $matches[2] }
}
$apiProject = Join-Path $repoRoot 'src/backend/services/Identity/src/InkLink.Identity.Api'
$runtimeConnection = 'Host=127.0.0.1;Port=54329;Database=inklink_identity;Username=inklink_identity_app;Password=' + $settings.IDENTITY_APP_PASSWORD
# JSON on standard input keeps the connection string out of command arguments.
@{ IdentityDatabase = @{ ConnectionString = $runtimeConnection } } | ConvertTo-Json | dotnet user-secrets set --project $apiProject
if ($LASTEXITCODE -ne 0) { throw 'Unable to configure local user secrets.' }
Write-Host 'Local secrets configured. Existing credentials were preserved.'
