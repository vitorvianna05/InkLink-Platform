param([Parameter(Mandatory)][ValidateSet('Identity','Catalog','Engagements','Messaging','Scheduling','Finance','Gateway','Bff')][string]$Service)
. "$PSScriptRoot/Integration.Common.ps1"
$processes=Get-IntegrationProcesses
$before=$processes|ConvertTo-Json -Depth 10|ConvertFrom-Json -AsHashtable
$contractBefore=$null
if($Service -notin 'Gateway','Bff'){
    $contractBefore=Invoke-RestMethod ('https://localhost:5291/swagger/documents/'+$Service.ToLowerInvariant()+'.json') -TimeoutSec 15|ConvertTo-Json -Depth 100 -Compress
}
$release=Join-Path $IntegrationRoot ('releases/'+[DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')+'-'+$Service)
$null=Start-IntegrationDotnet -Arguments @('publish',(Get-IntegrationProject $Service),'--configuration','Release','--no-build','--output',$release) -Environment @{} -Log "$IntegrationRoot/publish-$Service.log" -Wait
$previous=if($processes.ContainsKey($Service)){$processes[$Service].Release}else{$null}
Stop-IntegrationService $Service $processes
try{Start-IntegrationService $Service $release $processes}catch{if($previous){Start-IntegrationService $Service $previous $processes};throw}
foreach($other in $before.Keys|Where-Object {$_ -ne $Service}){
    if($processes[$other].Id -ne $before[$other].Id -or !(Test-IntegrationProcess $processes[$other])){throw 'Independent deployment affected another process.'}
}
$contractUnchanged=$null
if($null -ne $contractBefore){
    $contractAfter=Invoke-RestMethod ('https://localhost:5291/swagger/documents/'+$Service.ToLowerInvariant()+'.json') -TimeoutSec 15|ConvertTo-Json -Depth 100 -Compress
    $contractUnchanged=$contractBefore -ceq $contractAfter
}
$evidence=@{contractUnchanged=$contractUnchanged;service=$Service;previous=$previous;release=$release;verifiedAt=[DateTimeOffset]::UtcNow;otherProcessesPreserved=$true}
[IO.File]::WriteAllText("$IntegrationRoot/deployment.json",($evidence|ConvertTo-Json -Depth 10))
if($null -ne $contractUnchanged -and !$contractUnchanged){throw 'Published contract changed; review compatibility before accepting this deployment.'}
Write-Host "$Service published independently. Run Test-Integration.ps1 -VerifyOnly for business verification."
