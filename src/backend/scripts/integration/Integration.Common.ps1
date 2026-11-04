$ErrorActionPreference = 'Stop'
$script:IntegrationRepo = (Resolve-Path "$PSScriptRoot/../../../..").Path
$script:IntegrationRoot = Join-Path $IntegrationRepo 'artifacts/integration'
$script:IntegrationCompose = Join-Path $IntegrationRoot 'compose.json'
$script:IntegrationManifest = Join-Path $IntegrationRoot 'processes.json'
$script:IntegrationServices = [ordered]@{ Identity=5280; Catalog=5293; Engagements=5294; Messaging=5295; Scheduling=5296; Finance=5297; Gateway=5291; Bff=5292 }
function Get-IntegrationSecrets {
    $values=@{}
    foreach($line in Get-Content -LiteralPath "$IntegrationRoot/.env") {
        if($line -match '^([A-Z_]+)=(.+)$'){$values[$matches[1]]=$matches[2]}
    }
    return $values
}
function Invoke-IntegrationCompose {
    param([string[]]$Arguments)
    & docker compose --project-name inklink-integration --env-file "$IntegrationRoot/.env" -f $IntegrationCompose @Arguments
    if($LASTEXITCODE -ne 0){throw 'Isolated integration Compose command failed.'}
}
function Get-IntegrationProject {
    param([string]$Service)
    if($Service -eq 'Gateway'){return "$IntegrationRepo/src/backend/gateway/InkLink.Gateway/InkLink.Gateway.csproj"}
    if($Service -eq 'Bff'){return "$IntegrationRepo/src/backend/bff/InkLink.Bff/InkLink.Bff.csproj"}
    return "$IntegrationRepo/src/backend/services/$Service/src/InkLink.$Service.Api/InkLink.$Service.Api.csproj"
}
function Get-IntegrationAssembly {
    param([string]$Service)
    if($Service -in 'Gateway','Bff'){return "InkLink.$Service.dll"}
    return "InkLink.$Service.Api.dll"
}
function Get-IntegrationEnvironment {
    param([string]$Service)
    $secrets=Get-IntegrationSecrets
    $values=@{
        ASPNETCORE_ENVIRONMENT='Development'; DOTNET_ENVIRONMENT='Development'
        ASPNETCORE_URLS="https://localhost:$($IntegrationServices[$Service])"
        Kestrel__Certificates__Default__Path="$IntegrationRoot/localhost-https.pfx"
        Kestrel__Certificates__Default__Password=$secrets.BFF_CERT_PASSWORD
        Authentication__Authority='https://localhost:5289/realms/inklink'
        Authentication__Audience='inklink-api'; Authentication__AllowHttpMetadata='false'
    }
    $users=@('inklink-bff','inklink-ios','inklink-android','inklink-swagger','inklink-integration-users')
    for($i=0;$i -lt $users.Count;$i++){$values["Authentication__UserClients__$i"]=$users[$i]}
    $technical=@('inklink-catalog','inklink-messaging','inklink-scheduling')
    for($i=0;$i -lt $technical.Count;$i++){$values["Authentication__TechnicalClients__$i"]=$technical[$i]}
    $ports=@{Identity=55329; Catalog=55331; Engagements=55332; Messaging=55333; Scheduling=55334; Finance=55335; Bff=55330}
    if($Service -in 'Identity','Catalog','Engagements','Messaging','Scheduling','Finance'){
        $lower=$Service.ToLowerInvariant(); $key=$Service.ToUpperInvariant()+'_APP_PASSWORD'
        $values[$Service+'Database__ConnectionString']="Host=127.0.0.1;Port=$($ports[$Service]);Database=inklink_$lower;Username=inklink_${lower}_app;Password=$($secrets[$key])"
    }
    if($Service -in 'Catalog','Engagements','Messaging','Scheduling','Finance'){
        $values[$Service+'__IdentityAddress']='https://localhost:5280/'
    }
    switch($Service){
        Gateway {foreach($backend in 'Identity','Catalog','Engagements','Messaging','Scheduling','Finance'){$values['Gateway__'+$backend+'Address']="https://localhost:$($IntegrationServices[$backend])/"}}
        Engagements {
            foreach($backend in 'Catalog','Messaging','Scheduling'){$values['Engagements__'+$backend+'Address']="https://localhost:$($IntegrationServices[$backend])/"}
            $values.Engagements__ClientSecret=$secrets.INKLINK_ENGAGEMENTS_SECRET
            $values.Broker__Uri="amqp://inklink_publisher:$($secrets.INKLINK_BROKER_PUBLISHER_PASSWORD)@127.0.0.1:5773/"
            $values.Broker__ReservationConsumerUri="amqp://inklink_reservation_consumer:$($secrets.INKLINK_RESERVATION_CONSUMER_PASSWORD)@127.0.0.1:5773/"
        }
        Messaging {
            $values.Messaging__EngagementsAddress='https://localhost:5294/';$values.Messaging__ClientSecret=$secrets.INKLINK_MESSAGING_SECRET
            $values.Broker__Uri="amqp://inklink_consumer:$($secrets.INKLINK_BROKER_CONSUMER_PASSWORD)@127.0.0.1:5773/"
        }
        Scheduling {
            $values.Scheduling__EngagementsAddress='https://localhost:5294/';$values.Scheduling__ClientSecret=$secrets.INKLINK_SCHEDULING_SECRET
            $values.Broker__PublisherUri="amqp://inklink_scheduling_publisher:$($secrets.INKLINK_SCHEDULING_PUBLISHER_PASSWORD)@127.0.0.1:5773/"
        }
        Finance {foreach($backend in 'Engagements','Messaging','Scheduling'){$values['Finance__'+$backend+'Address']="https://localhost:$($IntegrationServices[$backend])/"}}
        Bff {
            $values.Bff__Authority='https://localhost:5289/realms/inklink';$values.Bff__ClientId='inklink-bff';$values.Bff__ClientSecret=$secrets.INKLINK_BFF_SECRET
            $values.Bff__GatewayAddress='https://localhost:5291/';$values.Bff__AllowHttpMetadata='false'
            # Website local (Start-WebIntegration.ps1): destino do retorno após o login (decisão 0017).
            $values.Bff__WebOrigin='https://localhost:5173'
            $values.Bff__ConnectionString="Host=127.0.0.1;Port=55330;Database=inklink_bff;Username=inklink_bff_app;Password=$($secrets.BFF_APP_PASSWORD)"
            $values.Bff__CertificatePath="$IntegrationRoot/protection.pfx";$values.Bff__CertificatePassword=$secrets.BFF_CERT_PASSWORD
        }
    }
    return $values
}
function Start-IntegrationDotnet {
    param([string[]]$Arguments,[hashtable]$Environment,[string]$Log,[switch]$Wait)
    $quoted=($Arguments|ForEach-Object {'"'+$_.Replace('"','\"')+'"'}) -join ' '
    $options=@{FilePath='dotnet';ArgumentList=$quoted;WorkingDirectory=$IntegrationRepo;WindowStyle='Hidden';PassThru=$true;RedirectStandardOutput=$Log;RedirectStandardError=($Log+'.error');Environment=$Environment}
    $process=Start-Process @options
    if($Wait){$process.WaitForExit();if($process.ExitCode -ne 0){throw "dotnet failed; inspect $Log and its error log."}}
    return $process
}
function Get-IntegrationProcesses {
    if(Test-Path -LiteralPath $IntegrationManifest){return Get-Content -LiteralPath $IntegrationManifest -Raw|ConvertFrom-Json -AsHashtable}
    return @{}
}
function Save-IntegrationProcesses {param([hashtable]$Processes)
    [IO.File]::WriteAllText($IntegrationManifest,($Processes|ConvertTo-Json -Depth 10))
}
function Test-IntegrationProcess {param($Entry)
    $process=Get-Process -Id $Entry.Id -ErrorAction SilentlyContinue
    if(!$process -or $process.StartTime.ToUniversalTime().Ticks -ne $Entry.StartTicks){return $false}
    $native=Get-CimInstance Win32_Process -Filter "ProcessId=$($Entry.Id)"
    return $native -and $native.CommandLine -and $native.CommandLine.Contains($Entry.Dll,[StringComparison]::OrdinalIgnoreCase)
}
function Stop-IntegrationService {param([string]$Service,[hashtable]$Processes)
    if($Processes.ContainsKey($Service)){
        if(Test-IntegrationProcess $Processes[$Service]){Stop-Process -Id $Processes[$Service].Id -ErrorAction Stop}
        $Processes.Remove($Service);Save-IntegrationProcesses $Processes
    }
}
function Get-IntegrationListeners {param([int]$Port)
    # Query failures must stop startup; an unknown owner is never a free port.
    Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object LocalPort -eq $Port
}
function Test-IntegrationListener {param($Entry,[int]$Port)
    $listeners=@(Get-IntegrationListeners $Port)
    return $listeners.Count -gt 0 -and @($listeners | Where-Object OwningProcess -ne $Entry.Id).Count -eq 0
}
function Start-IntegrationService {param([string]$Service,[string]$Release,[hashtable]$Processes)
    $dll=Join-Path $Release (Get-IntegrationAssembly $Service)
    if(!(Test-Path -LiteralPath $dll)){throw 'Published assembly is missing.'}
    $port=$IntegrationServices[$Service]
    if(@(Get-IntegrationListeners $port).Count){throw "$Service port $port is already in use; no process was started or stopped."}
    $process=Start-IntegrationDotnet -Arguments @($dll) -Environment (Get-IntegrationEnvironment $Service) -Log "$IntegrationRoot/$Service.log"
    $entry=@{Id=$process.Id;StartTicks=$process.StartTime.ToUniversalTime().Ticks;Dll=$dll;Release=$Release}
    $Processes[$Service]=$entry
    try{
        Save-IntegrationProcesses $Processes
        $health=if($Service -in 'Gateway','Bff'){'live'}else{'ready'}
        $ready=$false
        for($i=0;$i -lt 40;$i++){
            if(!(Test-IntegrationProcess $entry)){break}
            $listeners=@(Get-IntegrationListeners $port)
            if(@($listeners | Where-Object OwningProcess -ne $entry.Id).Count){throw "$Service port $port belongs to another process."}
            if($listeners.Count){
                try{$null=Invoke-WebRequest "https://localhost:$port/health/$health" -TimeoutSec 3;$ready=$true}catch{$ready=$false}
                if($ready){
                    # Recheck after HTTP: the listener may have changed while awaiting the response.
                    if(!(Test-IntegrationProcess $entry) -or !(Test-IntegrationListener $entry $port)){throw "$Service process or listener changed during its health check."}
                    break
                }
            }
            Start-Sleep -Milliseconds 500
        }
        if(!$ready){throw "$Service did not become ready. Inspect integration logs."}
    }catch{
        Stop-IntegrationService $Service $Processes
        throw
    }
    Write-Host "$Service ready over trusted HTTPS with verified process ownership."
}
