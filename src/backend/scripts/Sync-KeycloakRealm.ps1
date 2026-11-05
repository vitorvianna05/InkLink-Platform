# Aplica a um realm existente as configurações versionadas de sessão, tokens, tema e idioma
# (decisões 0019 e T10.21). O Keycloak só importa o realm na primeira inicialização.
param(
    [Parameter(Mandatory)][string]$BaseUri,
    [Parameter(Mandatory)][string]$AdminPassword,
    [string]$RealmFile = (Join-Path $PSScriptRoot '../identity-provider/inklink-realm.json')
)
$ErrorActionPreference = 'Stop'
$realm = Get-Content -LiteralPath $RealmFile -Raw | ConvertFrom-Json -AsHashtable
$keys = 'accessTokenLifespan','ssoSessionIdleTimeout','ssoSessionMaxLifespan','revokeRefreshToken','refreshTokenMaxReuse','loginTheme','internationalizationEnabled','supportedLocales','defaultLocale'
$update = @{}
foreach ($key in $keys) { if ($realm.ContainsKey($key)) { $update[$key] = $realm[$key] } }
$token = Invoke-RestMethod "$BaseUri/realms/master/protocol/openid-connect/token" -Method Post -TimeoutSec 30 -ContentType 'application/x-www-form-urlencoded' -Body @{ grant_type = 'password'; client_id = 'admin-cli'; username = 'admin'; password = $AdminPassword }
$headers = @{ Authorization = 'Bearer ' + $token.access_token }
$null = Invoke-WebRequest "$BaseUri/admin/realms/inklink" -Method Put -Headers $headers -ContentType 'application/json' -Body ($update | ConvertTo-Json -Depth 5) -TimeoutSec 30
$current = Invoke-RestMethod "$BaseUri/admin/realms/inklink" -Headers $headers -TimeoutSec 30
foreach ($key in $keys) {
    if ($update.ContainsKey($key) -and (($current.$key | ConvertTo-Json -Compress) -ne ($update[$key] | ConvertTo-Json -Compress))) { throw "Realm setting $key was not applied." }
}
Write-Host 'Keycloak realm session, token, theme and locale settings are in sync.'
