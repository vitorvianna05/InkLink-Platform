# Focused regression for T09.04/T09.05; no services, manifests or databases are changed.
. "$PSScriptRoot/../integration/Integration.Common.ps1"
function Assert-Startup {param([bool]$Condition,[string]$Name)
    if(!$Condition){throw "Startup regression failed: $Name"}
}
# Exercise the real Windows listener query with an ephemeral port owned by this process.
$listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
try{
    $listener.Start();$port=$listener.LocalEndpoint.Port
    Assert-Startup (Test-IntegrationListener @{Id=$PID} $port) 'real listener owner accepted'
    Assert-Startup (!(Test-IntegrationListener @{Id=-1} $port)) 'foreign listener owner rejected'
}finally{$listener.Stop()}
# Controlled process/HTTP boundaries reproduce races without stopping any real process.
function Get-IntegrationAssembly {param($Service);'pwsh.dll'}
function Get-IntegrationEnvironment {param($Service);@{}}
function Start-IntegrationDotnet {
    param($Arguments,$Environment,$Log)
    $script:started++
    [pscustomobject]@{Id=4242;StartTime=[datetime]::Now}
}
function Save-IntegrationProcesses {param($Processes)}
function Stop-IntegrationService {param($Service,$Processes)
    $script:stopped++;$Processes.Remove($Service)
}
function Test-IntegrationProcess {param($Entry)
    $script:aliveChecks++
    return $script:scenario -ne 'exited' -and !($script:scenario -eq 'exited-after-response' -and $script:aliveChecks -gt 1)
}
function Get-IntegrationListeners {param($Port)
    $script:listenerChecks++
    if($script:scenario -eq 'occupied'){return [pscustomobject]@{OwningProcess=9999}}
    if($script:listenerChecks -eq 1){return}
    $owner=if($script:scenario -eq 'foreign-after-start' -or ($script:scenario -eq 'swapped-after-response' -and $script:listenerChecks -gt 2)){9999}else{4242}
    [pscustomobject]@{OwningProcess=$owner}
}
function Invoke-WebRequest {param($TimeoutSec,[Parameter(Position=0)]$Uri)
    $script:requests++;[pscustomobject]@{StatusCode=200}
}
function Start-Sleep {param($Milliseconds)}
foreach($case in 'occupied','exited','foreign-after-start','swapped-after-response','exited-after-response','success'){
    $script:scenario=$case;$script:started=0;$script:stopped=0;$script:requests=0;$script:listenerChecks=0;$script:aliveChecks=0
    $processes=@{};$failed=$false
    try{Start-IntegrationService Finance $PSHOME $processes}catch{$failed=$true}
    if($case -eq 'success'){
        Assert-Startup (!$failed -and $started -eq 1 -and $stopped -eq 0 -and $processes.ContainsKey('Finance') -and $requests -eq 1) $case
    }else{
        Assert-Startup ($failed -and !$processes.ContainsKey('Finance')) "$case rejects readiness"
        if($case -eq 'occupied'){
            Assert-Startup ($started -eq 0 -and $stopped -eq 0 -and $requests -eq 0) 'occupied port leaves other process untouched'
        }else{
            Assert-Startup ($started -eq 1 -and $stopped -eq 1) "$case cleans up owned startup"
            if($case -in 'exited','foreign-after-start'){Assert-Startup ($requests -eq 0) "$case never queries another process"}
        }
    }
    Write-Host "PASS: $case"
}
Write-Host 'Startup regression passed: real listener ownership and six startup scenarios.'
