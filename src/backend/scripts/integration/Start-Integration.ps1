. "$PSScriptRoot/Integration.Common.ps1"
if(!(Test-Path -LiteralPath $IntegrationCompose)){throw 'Run Initialize-Integration.ps1 first.'}
$processes=Get-IntegrationProcesses
foreach($service in $IntegrationServices.Keys){
    if($processes.ContainsKey($service) -and (Test-IntegrationProcess $processes[$service])){continue}
    $release=Join-Path $IntegrationRoot ('releases/'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+'-'+$service)
    $null=Start-IntegrationDotnet -Arguments @('publish',(Get-IntegrationProject $service),'--configuration','Release','--no-build','--output',$release) -Environment @{} -Log "$IntegrationRoot/publish-$service.log" -Wait
    Start-IntegrationService $service $release $processes
}
& "$PSScriptRoot/Ensure-IntegrationMaster.ps1"
Write-Host 'Integration Gateway: https://localhost:5291/swagger; BFF: https://localhost:5292/bff/login'
