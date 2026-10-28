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
$requiredKeys = 'BFF_POSTGRES_PASSWORD', 'BFF_OWNER_PASSWORD', 'BFF_APP_PASSWORD', 'KEYCLOAK_DB_PASSWORD', 'KEYCLOAK_ADMIN_PASSWORD', 'INKLINK_BFF_SECRET', 'INKLINK_CATALOG_SECRET', 'BFF_CERT_PASSWORD'
$existing = Get-Content -LiteralPath $envFile
foreach ($name in $requiredKeys) {
    if (-not ($existing -match "^$name=")) {
        $secretValue = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        Add-Content -LiteralPath $envFile -Value "$name=$secretValue"
    }
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

$localDirectory = Join-Path $repoRoot 'artifacts/local'
New-Item -ItemType Directory -Path $localDirectory -Force | Out-Null
$certificatePath = Join-Path $localDirectory 'bff-protection.pfx'
if (-not (Test-Path -LiteralPath $certificatePath)) {
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    try {
        $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=InkLink Local Protection', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1), [DateTimeOffset]::UtcNow.AddYears(1))
        try { [IO.File]::WriteAllBytes($certificatePath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $settings.BFF_CERT_PASSWORD)) }
        finally { $certificate.Dispose() }
    } finally { $rsa.Dispose() }
}
# Export only an existing trusted localhost certificate; never change certificate trust here.
$tlsCertificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq 'CN=localhost' -and $_.HasPrivateKey -and $_.NotAfter -gt [DateTime]::Now -and (Test-Path "Cert:\CurrentUser\Root\$($_.Thumbprint)") } | Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $tlsCertificate) { throw 'Configure a trusted .NET HTTPS development certificate first.' }
[IO.File]::WriteAllText("$localDirectory/localhost.pem", $tlsCertificate.ExportCertificatePem())
[IO.File]::WriteAllBytes("$localDirectory/localhost-https.pfx", $tlsCertificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $settings.BFF_CERT_PASSWORD))
$tlsPrivateKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($tlsCertificate)
try { [IO.File]::WriteAllText("$localDirectory/localhost.key", $tlsPrivateKey.ExportPkcs8PrivateKeyPem()) }
finally { $tlsPrivateKey.Dispose() }
$realm = [IO.File]::ReadAllText((Join-Path $repoRoot 'src/backend/identity-provider/inklink-realm.json'))
$realm = $realm.Replace(([string][char]36 + '{INKLINK_BFF_SECRET}'), $settings.INKLINK_BFF_SECRET).Replace(([string][char]36 + '{INKLINK_CATALOG_SECRET}'), $settings.INKLINK_CATALOG_SECRET)
[IO.File]::WriteAllText((Join-Path $localDirectory 'inklink-realm.json'), $realm)
$authentication = @{ Authority = 'https://localhost:5189/realms/inklink'; Audience = 'inklink-api'; UserClients = @('inklink-bff', 'inklink-ios', 'inklink-android'); TechnicalClients = @('inklink-catalog'); AllowHttpMetadata = $false }
@{ Authentication = $authentication } | ConvertTo-Json -Depth 5 | dotnet user-secrets set --project $apiProject
if ($LASTEXITCODE -ne 0) { throw 'Unable to configure Identity authentication.' }
@{ Authentication = $authentication; Gateway = @{ IdentityAddress = 'http://127.0.0.1:5180/' } } | ConvertTo-Json -Depth 5 | dotnet user-secrets set --project "$repoRoot/src/backend/gateway/InkLink.Gateway"
if ($LASTEXITCODE -ne 0) { throw 'Unable to configure Gateway.' }
@{ Kestrel = @{ Certificates = @{ Default = @{ Path = "$localDirectory/localhost-https.pfx"; Password = $settings.BFF_CERT_PASSWORD } } }; Bff = @{ Authority = $authentication.Authority; ClientId = 'inklink-bff'; ClientSecret = $settings.INKLINK_BFF_SECRET; GatewayAddress = 'http://127.0.0.1:5191/'; ConnectionString = ('Host=127.0.0.1;Port=54330;Database=inklink_bff;Username=inklink_bff_app;Password=' + $settings.BFF_APP_PASSWORD); CertificatePath = $certificatePath; CertificatePassword = $settings.BFF_CERT_PASSWORD; AllowHttpMetadata = $false } } | ConvertTo-Json -Depth 5 | dotnet user-secrets set --project "$repoRoot/src/backend/bff/InkLink.Bff"
if ($LASTEXITCODE -ne 0) { throw 'Unable to configure BFF.' }
Write-Host 'Identity, Gateway, BFF and local Keycloak import configured without printing credentials.'
