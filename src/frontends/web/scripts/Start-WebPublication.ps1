# Homologação local da publicação do website (T10.10): build de produção servido pelo Caddy na mesma
# origem que o website usa no ambiente P09 (https://localhost:5173), com BFF 5292 e SeaweedFS 5288.
# Não publica nada fora da máquina; Ctrl+C encerra o contêiner.
param([ValidateRange(1024,65535)][int]$Port=5173,[switch]$SkipBuild)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../../..").Path
$web=Join-Path $repo 'src/frontends/web'
$integration=Join-Path $repo 'artifacts/integration'
foreach($path in @("$integration/localhost.pem","$integration/localhost.key")){
    if(!(Test-Path -LiteralPath $path)){throw 'Prepare the P09 environment first (Initialize-Integration.ps1).'}
}
if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw "Port $Port is in use; stop the development server first."}
$null=Invoke-WebRequest 'https://localhost:5292/health/live' -TimeoutSec 5
if(!$SkipBuild){
    Push-Location $web
    try{npm run build;if($LASTEXITCODE -ne 0){throw 'Website build failed.'}}finally{Pop-Location}
}
if(!(Test-Path -LiteralPath "$web/dist/index.html")){throw 'Run npm run build first.'}
Write-Host "Website publicado localmente: https://localhost:$Port (Caddy, mesma origem do BFF e de /media)."
& docker run --rm --name inklink-web-publication `
    -p "127.0.0.1:${Port}:${Port}" `
    -v "${web}/dist:/srv:ro" `
    -v "${web}/deploy/Caddyfile:/etc/caddy/Caddyfile:ro" `
    -v "${integration}/localhost.pem:/certs/localhost.pem:ro" `
    -v "${integration}/localhost.key:/certs/localhost.key:ro" `
    -e "INKLINK_SITE_ADDRESS=https://localhost:$Port" `
    -e 'INKLINK_TLS_CERT=/certs/localhost.pem' -e 'INKLINK_TLS_KEY=/certs/localhost.key' `
    -e 'INKLINK_BFF_UPSTREAM=https://host.docker.internal:5292' -e 'INKLINK_BFF_HOST=localhost:5292' -e 'INKLINK_BFF_CA=/certs/localhost.pem' `
    -e 'INKLINK_MEDIA_UPSTREAM=http://host.docker.internal:5288' -e 'INKLINK_MEDIA_HOST=127.0.0.1:5288' `
    caddy:2.11.6-alpine
if($LASTEXITCODE -ne 0){throw 'Publication container exited with an error.'}
