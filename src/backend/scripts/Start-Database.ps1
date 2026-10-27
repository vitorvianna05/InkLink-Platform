$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
$envFile = Join-Path $repoRoot '.env.local'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Run Initialize-Local.ps1 first.' }
docker compose --env-file $envFile -f "$repoRoot/src/backend/compose.yaml" up -d --wait
if ($LASTEXITCODE -ne 0) { throw 'Database startup failed.' }
