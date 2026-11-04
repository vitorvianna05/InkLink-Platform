. "$PSScriptRoot/Integration.Common.ps1"
New-Item -ItemType Directory -Force -Path $IntegrationRoot|Out-Null
$sourceCompose=Join-Path $IntegrationRepo 'src/backend/compose.yaml'
$realmText=Get-Content "$IntegrationRepo/src/backend/identity-provider/inklink-realm.json" -Raw
$composeText=Get-Content $sourceCompose -Raw
$keys=@([regex]::Matches($composeText+' '+$realmText,'\$\{([A-Z_]+)')|ForEach-Object {$_.Groups[1].Value})+@('BFF_CERT_PASSWORD','INTEGRATION_USER_PASSWORD')
$envPath="$IntegrationRoot/.env"
if(!(Test-Path -LiteralPath $envPath)){[IO.File]::WriteAllText($envPath,'')}
$existing=Get-Content -LiteralPath $envPath
foreach($key in $keys|Select-Object -Unique){if(!($existing -match "^$key=")){Add-Content -LiteralPath $envPath -Value ($key+'='+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32)))}}
$secrets=Get-IntegrationSecrets
$cert=Get-ChildItem Cert:\CurrentUser\My|Where-Object {$_.Subject -eq 'CN=localhost' -and $_.HasPrivateKey -and $_.NotAfter -gt [datetime]::Now -and (Test-Path "Cert:\CurrentUser\Root\$($_.Thumbprint)")}|Sort-Object NotAfter -Descending|Select-Object -First 1
if(!$cert){throw 'A trusted localhost development certificate is required; trust is never changed by this script.'}
[IO.File]::WriteAllText("$IntegrationRoot/localhost.pem",$cert.ExportCertificatePem())
[IO.File]::WriteAllBytes("$IntegrationRoot/localhost-https.pfx",$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$secrets.BFF_CERT_PASSWORD))
$key=[Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
try{[IO.File]::WriteAllText("$IntegrationRoot/localhost.key",$key.ExportPkcs8PrivateKeyPem())}finally{$key.Dispose()}
if(!(Test-Path -LiteralPath "$IntegrationRoot/protection.pfx")){
    $rsa=[Security.Cryptography.RSA]::Create(3072)
    try{$request=[Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=InkLink Integration Protection',$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1);$protection=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-1),[DateTimeOffset]::UtcNow.AddYears(1));try{[IO.File]::WriteAllBytes("$IntegrationRoot/protection.pfx",$protection.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$secrets.BFF_CERT_PASSWORD))}finally{$protection.Dispose()}}finally{$rsa.Dispose()}
}
foreach($name in $secrets.Keys){$realmText=$realmText.Replace('${'+$name+'}',$secrets[$name])}
$realmText=$realmText.Replace('https://localhost:5192','https://localhost:5292').Replace('http://127.0.0.1:5191','https://localhost:5291').Replace('http://localhost:5191','https://localhost:5291')
$realm=$realmText|ConvertFrom-Json -AsHashtable
$testClient=($realm.clients|Where-Object clientId -eq 'inklink-ios'|ConvertTo-Json -Depth 30|ConvertFrom-Json -AsHashtable)
$testClient.clientId='inklink-integration-users';$testClient.directAccessGrantsEnabled=$true;$testClient.standardFlowEnabled=$false
$realm.clients+=@($testClient)
[IO.File]::WriteAllText("$IntegrationRoot/inklink-realm.json",($realm|ConvertTo-Json -Depth 40))
$raw=& docker compose --env-file $envPath -f $sourceCompose config --format json
if($LASTEXITCODE -ne 0){throw 'Unable to resolve dependency template.'}
$compose=$raw|ConvertFrom-Json -AsHashtable;$compose.name='inklink-integration'
foreach($service in $compose.services.Values){
    foreach($port in $service.ports){$number=[int]$port.published;$port.published=[string]$(if($number -ge 54000){$number+1000}else{$number+100})}
    foreach($mount in $service.volumes){
        if($mount.type -eq 'bind' -and $mount.target -match 'inklink-realm.json|localhost.pem|localhost.key'){$mount.source=Join-Path $IntegrationRoot ([IO.Path]::GetFileName($mount.target))}
    }
}
foreach($name in $compose.volumes.Keys){$compose.volumes[$name].name='inklink-integration_'+$name}
foreach($name in $compose.networks.Keys){$compose.networks[$name].name='inklink-integration_'+$name}
$compose.services.keycloak.environment.JAVA_OPTS_KC_HEAP='-Xms128m -Xmx512m'
$compose.services.keycloak.mem_limit='805306368'
$compose.services.rabbitmq.mem_limit='536870912'
$compose.services.rabbitmq.environment.RABBITMQ_SERVER_ADDITIONAL_ERL_ARGS='+S 2:2'
$compose.services.rabbitmq.environment.RABBITMQ_CTL_ERL_ARGS='+S 1:1'
$compose.services.rabbitmq.healthcheck.timeout='30s'
$compose.services.rabbitmq.healthcheck.interval='10s'
$compose.services.rabbitmq.user='0'
$compose.services.rabbitmq.entrypoint=@('/bin/sh','-ec','if [ ! -f /var/lib/rabbitmq/.erlang.cookie ]; then head -c 32 /dev/urandom | base64 > /var/lib/rabbitmq/.erlang.cookie; fi; chown rabbitmq:rabbitmq /var/lib/rabbitmq /var/lib/rabbitmq/.erlang.cookie; chmod 600 /var/lib/rabbitmq/.erlang.cookie; exec docker-entrypoint.sh rabbitmq-server')
[IO.File]::WriteAllText($IntegrationCompose,($compose|ConvertTo-Json -Depth 40))
Invoke-IntegrationCompose @('config','--quiet')
Invoke-IntegrationCompose @('run','--rm','--no-deps','--user','0','--entrypoint','sh','rabbitmq','-ec','if [ -f /var/lib/rabbitmq/.erlang.cookie ]; then chown rabbitmq:rabbitmq /var/lib/rabbitmq/.erlang.cookie; chmod 600 /var/lib/rabbitmq/.erlang.cookie; fi')
Invoke-IntegrationCompose @('up','-d','--wait')
Invoke-IntegrationCompose @('exec','-T','rabbitmq','sh','/opt/inklink/configure-broker.sh')
$ready=$false
for($i=0;$i -lt 60;$i++){try{$null=Invoke-RestMethod 'https://localhost:5289/realms/inklink/.well-known/openid-configuration' -TimeoutSec 3;$ready=$true;break}catch{Start-Sleep -Seconds 1}}
if(!$ready){throw 'Integration Keycloak did not become ready over trusted HTTPS.'}
foreach($service in 'Identity','Catalog','Engagements','Messaging','Scheduling','Finance','Bff'){
    $project=if($service -eq 'Bff'){Get-IntegrationProject Bff}else{"$IntegrationRepo/src/backend/services/$service/src/InkLink.$service.Infrastructure/InkLink.$service.Infrastructure.csproj"}
    $ports=@{Identity=55329;Catalog=55331;Engagements=55332;Messaging=55333;Scheduling=55334;Finance=55335;Bff=55330};$lower=$service.ToLowerInvariant()
    $variable=if($service -eq 'Bff'){'Bff__ConnectionString'}else{$service+'Database__ConnectionString'}
    $environment=@{};$environment[$variable]="Host=127.0.0.1;Port=$($ports[$service]);Database=inklink_$lower;Username=inklink_${lower}_owner;Password=$($secrets[$service.ToUpperInvariant()+'_OWNER_PASSWORD'])"
    $null=Start-IntegrationDotnet -Arguments @('ef','database','update','--no-build','--configuration','Release','--project',$project,'--startup-project',$project) -Environment $environment -Log "$IntegrationRoot/migrate-$service.log" -Wait
    Write-Host "$service integration migration applied."
}
Write-Host 'Isolated dependencies initialized; development data, volumes and user secrets were not modified.'
