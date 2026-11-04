. "$PSScriptRoot/Integration.Common.ps1"
$processes=Get-IntegrationProcesses
foreach($service in $IntegrationServices.Keys){
    $health=if($service -in 'Gateway','Bff'){'live'}else{'ready'}
    try{$response=Invoke-WebRequest "https://localhost:$($IntegrationServices[$service])/health/$health" -TimeoutSec 3;$status=[int]$response.StatusCode}catch{$status=503}
    [pscustomobject]@{Service=$service;OwnedProcessRunning=($processes.ContainsKey($service) -and (Test-IntegrationProcess $processes[$service]));Health=$status;HealthKind=$health}
}
# Operator-only read queries expose aggregate metadata, never message bodies, event payloads or credentials.
foreach($service in 'engagements','scheduling'){
    $query='BEGIN READ ONLY; SELECT count(*) FILTER (WHERE "PublishedAt" IS NULL AND "QuarantinedAt" IS NULL) AS pending, count(*) FILTER (WHERE "QuarantinedAt" IS NOT NULL) AS quarantined, min("CreatedAt") FILTER (WHERE "PublishedAt" IS NULL) AS oldest_pending FROM '+$service+'.outbox; COMMIT;'
    Write-Host "$service Outbox:"
    Invoke-IntegrationCompose @('exec','-T',($service+'-db'),'psql','-U','postgres','-d',('inklink_'+$service),'-c',$query)
}
Write-Host 'Engagement lifecycle by state:'
Invoke-IntegrationCompose @('exec','-T','engagements-db','psql','-U','postgres','-d','inklink_engagements','-c','BEGIN READ ONLY; SELECT "Kind", "Status", count(*) AS operations, max("Attempts") AS max_attempts FROM engagements.lifecycle_operations GROUP BY "Kind", "Status"; COMMIT;')
