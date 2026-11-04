param([ValidateRange(1024,65535)][int]$Port=5173,[switch]$Preview)
$ErrorActionPreference='Stop'
$repo=(Resolve-Path "$PSScriptRoot/../../../..").Path
$web=Join-Path $repo 'src/frontends/web'
$integration=Join-Path $repo 'artifacts/integration'
$certificate=Join-Path $integration 'localhost.pem'
$key=Join-Path $integration 'localhost.key'
foreach($path in @($certificate,$key,(Join-Path $web 'node_modules/vite/package.json'))){
    if(!(Test-Path -LiteralPath $path)){throw 'Prepare the P09 environment and run npm ci in src/frontends/web first.'}
}
if($Preview -and !(Test-Path -LiteralPath "$web/dist/index.html")){throw 'Run npm run build before starting the preview.'}
$null=Invoke-WebRequest 'https://localhost:5292/health/live' -TimeoutSec 5
$variables=@{
    INKLINK_BFF_URL='https://localhost:5292'
    INKLINK_DEV_CERT=$certificate
    INKLINK_DEV_KEY=$key
    INKLINK_BFF_CA_CERT=$certificate
    INKLINK_BFF_INSECURE='false'
    INKLINK_WEB_PORT=[string]$Port
}
$previous=@{}
foreach($name in $variables.Keys){$previous[$name]=[Environment]::GetEnvironmentVariable($name,'Process');[Environment]::SetEnvironmentVariable($name,$variables[$name],'Process')}
Push-Location $web
try{
    Write-Host "Website: https://localhost:$Port (BFF integrado, cookies seguros, TLS verificado)."
    [string[]]$viteArguments=@()
    if($Preview){$viteArguments=@('preview')}
    & node (Join-Path $web 'node_modules/vite/bin/vite.js') @viteArguments --host localhost
    if($LASTEXITCODE -ne 0){throw 'Website server exited with an error.'}
}finally{
    Pop-Location
    foreach($name in $variables.Keys){[Environment]::SetEnvironmentVariable($name,$previous[$name],'Process')}
}
