$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
$envFile = Join-Path $repoRoot '.env.local'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Run Initialize-Local.ps1 first.' }
$settings = @{}
foreach ($line in Get-Content -LiteralPath $envFile) {
    if ($line -match '^([A-Z_]+)=(.+)$') { $settings[$matches[1]] = $matches[2] }
}
$previousConnection = $env:IdentityDatabase__ConnectionString
try {
    $env:IdentityDatabase__ConnectionString = 'Host=127.0.0.1;Port=54329;Database=inklink_identity;Username=inklink_identity_owner;Password=' + $settings.IDENTITY_OWNER_PASSWORD
    Push-Location $repoRoot
    try {
        dotnet ef database update --project src/backend/services/Identity/src/InkLink.Identity.Infrastructure --startup-project src/backend/services/Identity/src/InkLink.Identity.Infrastructure
        if ($LASTEXITCODE -ne 0) { throw 'Migration failed.' }
    } finally { Pop-Location }
} finally { $env:IdentityDatabase__ConnectionString = $previousConnection }

$previousBffConnection = $env:Bff__ConnectionString
try {
    $env:Bff__ConnectionString = 'Host=127.0.0.1;Port=54330;Database=inklink_bff;Username=inklink_bff_owner;Password=' + $settings.BFF_OWNER_PASSWORD
    Push-Location $repoRoot
    try {
        dotnet ef database update --project src/backend/bff/InkLink.Bff --startup-project src/backend/bff/InkLink.Bff
        if ($LASTEXITCODE -ne 0) { throw 'BFF migration failed.' }
    } finally { Pop-Location }
} finally { $env:Bff__ConnectionString = $previousBffConnection }
