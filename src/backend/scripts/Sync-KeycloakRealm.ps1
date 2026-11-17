# Aplica a um realm existente as configurações versionadas de sessão, tokens, tema, idioma e cadastro
# (decisões 0019, 0020 e T10.21). O Keycloak só importa o realm na primeira inicialização.
param(
    [Parameter(Mandatory)][string]$BaseUri,
    [Parameter(Mandatory)][string]$AdminPassword,
    [string]$RealmFile = (Join-Path $PSScriptRoot '../identity-provider/inklink-realm.json')
)
$ErrorActionPreference = 'Stop'
$realm = Get-Content -LiteralPath $RealmFile -Raw | ConvertFrom-Json -AsHashtable
$keys = 'accessTokenLifespan','ssoSessionIdleTimeout','ssoSessionMaxLifespan','revokeRefreshToken','refreshTokenMaxReuse','loginTheme','internationalizationEnabled','supportedLocales','defaultLocale','registrationAllowed','registrationEmailAsUsername','duplicateEmailsAllowed','verifyEmail','bruteForceProtected'
$update = @{}
foreach ($key in $keys) { if ($realm.ContainsKey($key)) { $update[$key] = $realm[$key] } }
$token = Invoke-RestMethod "$BaseUri/realms/master/protocol/openid-connect/token" -Method Post -TimeoutSec 30 -ContentType 'application/x-www-form-urlencoded' -Body @{ grant_type = 'password'; client_id = 'admin-cli'; username = 'admin'; password = $AdminPassword }
$headers = @{ Authorization = 'Bearer ' + $token.access_token }
$null = Invoke-WebRequest "$BaseUri/admin/realms/inklink" -Method Put -Headers $headers -ContentType 'application/json' -Body ($update | ConvertTo-Json -Depth 5) -TimeoutSec 30
$current = Invoke-RestMethod "$BaseUri/admin/realms/inklink" -Headers $headers -TimeoutSec 30
foreach ($key in $keys) {
    if ($update.ContainsKey($key) -and (($current.$key | ConvertTo-Json -Compress) -ne ($update[$key] | ConvertTo-Json -Compress))) { throw "Realm setting $key was not applied." }
}
# Escopo inklink.contact (decisão 0020): email e email_verified no access token dos clientes de usuário.
$scope = $realm.clientScopes | Where-Object { $_.name -eq 'inklink.contact' } | Select-Object -First 1
if (-not $scope) { throw 'Realm file has no inklink.contact client scope.' }
$existing = @(Invoke-RestMethod "$BaseUri/admin/realms/inklink/client-scopes" -Headers $headers -TimeoutSec 30 | ForEach-Object { $_ }) | Where-Object { $_.name -eq 'inklink.contact' } | Select-Object -First 1
if (-not $existing) {
    $null = Invoke-WebRequest "$BaseUri/admin/realms/inklink/client-scopes" -Method Post -Headers $headers -ContentType 'application/json' -Body ($scope | ConvertTo-Json -Depth 10) -TimeoutSec 30
    $existing = @(Invoke-RestMethod "$BaseUri/admin/realms/inklink/client-scopes" -Headers $headers -TimeoutSec 30 | ForEach-Object { $_ }) | Where-Object { $_.name -eq 'inklink.contact' } | Select-Object -First 1
}
foreach ($client in $realm.clients | Where-Object { $_.defaultClientScopes -contains 'inklink.contact' }) {
    $found = @(Invoke-RestMethod "$BaseUri/admin/realms/inklink/clients?clientId=$($client.clientId)" -Headers $headers -TimeoutSec 30 | ForEach-Object { $_ }) | Select-Object -First 1
    if (-not $found) { continue }
    $assigned = @(Invoke-RestMethod "$BaseUri/admin/realms/inklink/clients/$($found.id)/default-client-scopes" -Headers $headers -TimeoutSec 30 | ForEach-Object { $_ })
    if (-not ($assigned | Where-Object { $_.name -eq 'inklink.contact' })) {
        $null = Invoke-WebRequest "$BaseUri/admin/realms/inklink/clients/$($found.id)/default-client-scopes/$($existing.id)" -Method Put -Headers $headers -TimeoutSec 30
    }
}
Write-Host 'Keycloak realm session, token, theme, locale and registration settings are in sync.'
