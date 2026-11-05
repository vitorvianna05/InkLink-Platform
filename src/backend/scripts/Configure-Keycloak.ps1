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
$script:inklinkAdminToken = $null
$script:inklinkAdminExpiry = [DateTimeOffset]::MinValue
function Update-KeycloakAdminToken {
    param([switch]$Force)
    if ($Force -or -not $script:inklinkAdminToken -or $script:inklinkAdminExpiry -le [DateTimeOffset]::UtcNow.AddSeconds(15)) {
        $script:inklinkAdminToken = Invoke-RestMethod "$baseUri/realms/master/protocol/openid-connect/token" -Method Post -TimeoutSec 20 -ContentType 'application/x-www-form-urlencoded' -Body @{ grant_type = 'password'; client_id = 'admin-cli'; username = 'admin'; password = $settings.KEYCLOAK_ADMIN_PASSWORD }
        $script:inklinkAdminExpiry = [DateTimeOffset]::UtcNow.AddSeconds([int]$script:inklinkAdminToken.expires_in)
    }
}
function Invoke-KeycloakAdmin {
    param([Parameter(Mandatory)][string]$Uri, [string]$Method = 'Get', [object]$Body, [string]$ContentType = 'application/json')
    for ($adminAttempt = 0; $adminAttempt -lt 2; $adminAttempt++) {
        Update-KeycloakAdminToken -Force:($adminAttempt -gt 0)
        $requestOptions = @{ Uri = $Uri; Method = $Method; TimeoutSec = 20; Headers = @{ Authorization = 'Bearer ' + $script:inklinkAdminToken.access_token } }
        if ($PSBoundParameters.ContainsKey('Body')) { $requestOptions.Body = $Body; $requestOptions.ContentType = $ContentType }
        try { return Invoke-RestMethod @requestOptions }
        catch {
            if ($adminAttempt -eq 0 -and [int]$_.Exception.Response.StatusCode -eq 401) { continue }
            throw
        }
    }
}
$realm = Get-Content -LiteralPath (Join-Path $repoRoot 'artifacts/local/inklink-realm.json') -Raw | ConvertFrom-Json
$existingScopes = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/client-scopes"
foreach ($scope in $realm.clientScopes) {
    $existing = $existingScopes | Where-Object name -eq $scope.name | Select-Object -First 1
    if ($existing) {
        $null = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/client-scopes/$($existing.id)" -Method Put -ContentType 'application/json' -Body ($scope | ConvertTo-Json -Depth 20)
    } else {
        $null = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/client-scopes" -Method Post -ContentType 'application/json' -Body ($scope | ConvertTo-Json -Depth 20)
    }
}
$existingScopes = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/client-scopes"
foreach ($client in $realm.clients) {
    $clients = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/clients?clientId=$($client.clientId)"
    $existing = $clients | Where-Object clientId -eq $client.clientId | Select-Object -First 1
    if (-not $existing) {
        $null = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/clients" -Method Post -ContentType 'application/json' -Body ($client | ConvertTo-Json -Depth 20)
        $clients = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/clients?clientId=$($client.clientId)"
        $existing = $clients | Where-Object clientId -eq $client.clientId | Select-Object -First 1
        if (-not $existing) { throw 'Unable to create the local client.' }
    }
    $null = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/clients/$($existing.id)" -Method Put -ContentType 'application/json' -Body ($client | ConvertTo-Json -Depth 20)
    foreach ($name in $client.defaultClientScopes) {
        $scope = $existingScopes | Where-Object name -eq $name | Select-Object -First 1
        $null = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/clients/$($existing.id)/default-client-scopes/$($scope.id)" -Method Put
    }
    foreach ($name in $client.optionalClientScopes) {
        $scope = $existingScopes | Where-Object name -eq $name | Select-Object -First 1
        if (-not $scope) { throw 'Expected optional scope is missing.' }
        $null = Invoke-KeycloakAdmin "$baseUri/admin/realms/inklink/clients/$($existing.id)/optional-client-scopes/$($scope.id)" -Method Put
    }
}
& "$PSScriptRoot/Sync-KeycloakRealm.ps1" -BaseUri $baseUri -AdminPassword $settings.KEYCLOAK_ADMIN_PASSWORD
Write-Host 'Local client and scope configuration synchronized. Existing users were preserved.'
