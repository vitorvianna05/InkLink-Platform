$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path "$PSScriptRoot/../../..").Path
$settings = @{}
foreach ($line in Get-Content -LiteralPath (Join-Path $repoRoot '.env.local')) {
    if ($line -match '^([A-Z_]+)=(.+)$') { $settings[$matches[1]] = $matches[2] }
}
$baseUri = 'https://localhost:5189'
$ready = $false
for ($attempt = 0; $attempt -lt 12; $attempt++) {
    try {
        $null = Invoke-RestMethod "$baseUri/realms/inklink/.well-known/openid-configuration" -TimeoutSec 10
        $ready = $true
        break
    } catch { Start-Sleep -Seconds 2 }
}
if (-not $ready) { throw 'Local Keycloak is not ready over trusted HTTPS.' }
$token = Invoke-RestMethod "$baseUri/realms/master/protocol/openid-connect/token" -Method Post -ContentType 'application/x-www-form-urlencoded' -Body @{ grant_type = 'password'; client_id = 'admin-cli'; username = 'admin'; password = $settings.KEYCLOAK_ADMIN_PASSWORD }
$headers = @{ Authorization = 'Bearer ' + $token.access_token }
$realm = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts/local/inklink-realm.json') -Raw | ConvertFrom-Json
$existingScopes = Invoke-RestMethod "$baseUri/admin/realms/inklink/client-scopes" -Headers $headers
foreach ($scope in $realm.clientScopes) {
    $existing = $existingScopes | Where-Object name -eq $scope.name | Select-Object -First 1
    if ($existing) {
        $null = Invoke-RestMethod "$baseUri/admin/realms/inklink/client-scopes/$($existing.id)" -Headers $headers -Method Put -ContentType 'application/json' -Body ($scope | ConvertTo-Json -Depth 20)
    } else {
        $null = Invoke-RestMethod "$baseUri/admin/realms/inklink/client-scopes" -Headers $headers -Method Post -ContentType 'application/json' -Body ($scope | ConvertTo-Json -Depth 20)
    }
}
$existingScopes = Invoke-RestMethod "$baseUri/admin/realms/inklink/client-scopes" -Headers $headers
foreach ($client in $realm.clients) {
    $clients = Invoke-RestMethod "$baseUri/admin/realms/inklink/clients?clientId=$($client.clientId)" -Headers $headers
    $existing = $clients | Where-Object clientId -eq $client.clientId | Select-Object -First 1
    if (-not $existing) { throw 'Expected local client is missing; initialize the realm first.' }
    $null = Invoke-RestMethod "$baseUri/admin/realms/inklink/clients/$($existing.id)" -Headers $headers -Method Put -ContentType 'application/json' -Body ($client | ConvertTo-Json -Depth 20)
    foreach ($name in $client.defaultClientScopes) {
        $scope = $existingScopes | Where-Object name -eq $name | Select-Object -First 1
        $null = Invoke-RestMethod "$baseUri/admin/realms/inklink/clients/$($existing.id)/default-client-scopes/$($scope.id)" -Headers $headers -Method Put
    }
}
Write-Host 'Local client and scope configuration synchronized. Existing users were preserved.'
