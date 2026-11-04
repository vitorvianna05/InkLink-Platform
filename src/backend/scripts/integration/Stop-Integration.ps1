param([switch]$Dependencies)
. "$PSScriptRoot/Integration.Common.ps1"
$processes=Get-IntegrationProcesses
foreach($service in @($processes.Keys)){Stop-IntegrationService $service $processes}
if($Dependencies){Invoke-IntegrationCompose @('stop')}
Write-Host 'Owned integration processes stopped; volumes and other environments preserved.'
