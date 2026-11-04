param([switch]$VerifyOnly)
. "$PSScriptRoot/Integration.Common.ps1"
$secrets=Get-IntegrationSecrets
$script:Tokens=@{};$script:Users=@{};$script:Trace=[Guid]::NewGuid().ToString('N')
$script:Checks=[Collections.Generic.List[string]]::new()
function Check {param([bool]$Condition,[string]$Name)
    if(!$Condition){throw "Integration assertion failed: $Name"};$script:Checks.Add($Name)
}
function Get-UserToken {param([string]$Actor)
    $cached=$script:Tokens[$Actor]
    if($cached -and $cached.Until -gt [DateTimeOffset]::UtcNow.AddSeconds(20)){return $cached.Token}
    try{$token=Invoke-RestMethod 'https://localhost:5289/realms/inklink/protocol/openid-connect/token' -Method Post -ContentType 'application/x-www-form-urlencoded' -TimeoutSec 60 -Body @{grant_type='password';client_id='inklink-integration-users';username=$script:Users[$Actor];password=$secrets.INTEGRATION_USER_PASSWORD}}
    catch{throw 'Unable to authenticate synthetic integration account.'}
    $script:Tokens[$Actor]=@{Token=$token.access_token;Until=[DateTimeOffset]::UtcNow.AddSeconds($token.expires_in)}
    return $token.access_token
}
function Invoke-Api {param([string]$Actor,[string]$Method,[string]$Path,[object]$Body=$null,[int[]]$Expected=@(200),[string]$Key='')
    $headers=@{traceparent=('00-'+$script:Trace+'-'+[Guid]::NewGuid().ToString('N').Substring(0,16)+'-01')}
    if($Actor){$headers.Authorization='Bearer '+(Get-UserToken $Actor)}
    if($Method -eq 'Post'){$headers['Idempotency-Key']=$(if($Key){$Key}else{[Guid]::NewGuid().ToString()})}
    $options=@{Uri=('https://localhost:5291'+$Path);Method=$Method;Headers=$headers;TimeoutSec=20;SkipHttpErrorCheck=$true}
    if($null -ne $Body){$options.Body=$Body|ConvertTo-Json -Depth 15;$options.ContentType='application/json'}
    $response=Invoke-WebRequest @options
    if([int]$response.StatusCode -notin $Expected){
        $failureCode='unknown'
        try{$failureCode=($response.Content|ConvertFrom-Json).code}catch{}
        throw "Unexpected HTTP $([int]$response.StatusCode) for $Method $Path; code=$failureCode; traceId=$($response.Headers['X-Trace-Id'] -join '')"
    }
    Check (($response.Headers['X-Trace-Id'] -join '') -eq $script:Trace) 'HTTP trace propagated through Gateway'
    if($response.Content){return $response.Content|ConvertFrom-Json}
}
function Wait-Condition {param([scriptblock]$Condition,[string]$Name,[int]$Seconds=60)
    $until=[DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    do{if(& $Condition){$script:Checks.Add($Name);return};Start-Sleep -Milliseconds 500}while([DateTimeOffset]::UtcNow -lt $until)
    throw "Integration timeout: $Name"
}
function Find-Conversation {param([string]$Engagement)
    $page=Invoke-Api client Get '/api/v1/conversations'
    return $page.items|Where-Object engagementId -eq $Engagement|Select-Object -First 1
}
function Verify-PreservedData {param($Data)
    foreach($actor in 'client','artist','other'){$script:Users[$actor]=$Data.users.$actor}
    $entry=Invoke-Api artist Get ('/api/v1/finance/entries/'+$Data.receipt)
    Check ($entry.amount -eq 4000) 'Persisted receipt survives service publication'
    $summary=Invoke-Api artist Get ('/api/v1/finance/dashboard?from='+$Data.from+'&to='+$Data.to)
    Check ($summary.financial.received -eq 4000 -and $summary.financial.costs -eq 940 -and $summary.financial.estimatedProfit -eq 3060 -and $summary.financial.estimatedMargin -eq 76.5) 'Financial indicators preserved'
    $conversation=Invoke-Api client Get ('/api/v1/conversations/'+$Data.oldConversation)
    Check $conversation.writingBlocked 'Closed conversation remains blocked'
    $history=Invoke-Api client Get ('/api/v1/conversations/'+$Data.oldConversation+'/messages')
    Check ($history.items.Count -eq 2) 'Closed conversation retains both messages'
    $null=Invoke-Api other Get ('/api/v1/finance/entries/'+$Data.receipt) -Expected @(403)
}
New-Item -ItemType Directory -Force -Path "$IntegrationRoot/contracts"|Out-Null
$prefixes=@{identity=@('/api/v1/profiles');catalog=@('/api/v1/artists','/api/v1/favorites');engagements=@('/api/v2/engagements');messaging=@('/api/v1/conversations');scheduling=@('/api/v1/scheduling');finance=@('/api/v1/finance')}
foreach($service in $prefixes.Keys){
    $doc=Invoke-RestMethod "https://localhost:5291/swagger/documents/$service.json" -TimeoutSec 15
    Check ($doc.servers[0].url -eq '/') "$service OpenAPI uses Gateway origin"
    foreach($path in $doc.paths.PSObject.Properties.Name){Check ($path -notmatch '/internal/' -and @($prefixes[$service]|Where-Object {$path.StartsWith($_,[StringComparison]::Ordinal)}).Count -gt 0) "$service OpenAPI excludes private contracts"}
    [IO.File]::WriteAllText("$IntegrationRoot/contracts/$service.json",($doc|ConvertTo-Json -Depth 100))
}
Write-Host 'Six public OpenAPI contracts verified.'
if($VerifyOnly){
    $data=Get-Content "$IntegrationRoot/smoke.json" -Raw|ConvertFrom-Json
    Verify-PreservedData $data
    [IO.File]::WriteAllText("$IntegrationRoot/post-deployment.json",(@{verifiedAt=[DateTimeOffset]::UtcNow;checks=$script:Checks;traceId=$script:Trace}|ConvertTo-Json -Depth 10))
    Write-Host 'Post-publication business and contract checks passed.';return
}
$admin=Invoke-RestMethod 'https://localhost:5289/realms/master/protocol/openid-connect/token' -Method Post -TimeoutSec 60 -ContentType 'application/x-www-form-urlencoded' -Body @{grant_type='password';client_id='admin-cli';username='admin';password=$secrets.KEYCLOAK_ADMIN_PASSWORD}
$adminHeaders=@{Authorization='Bearer '+$admin.access_token};$adminUntil=[DateTimeOffset]::UtcNow.AddSeconds($admin.expires_in);$ids=@{};$run=[Guid]::NewGuid().ToString('N').Substring(0,10)
foreach($actor in 'client','artist','other'){
    if($adminUntil -le [DateTimeOffset]::UtcNow.AddSeconds(20)){
        $admin=Invoke-RestMethod 'https://localhost:5289/realms/master/protocol/openid-connect/token' -Method Post -TimeoutSec 60 -ContentType 'application/x-www-form-urlencoded' -Body @{grant_type='password';client_id='admin-cli';username='admin';password=$secrets.KEYCLOAK_ADMIN_PASSWORD}
        $adminHeaders=@{Authorization='Bearer '+$admin.access_token};$adminUntil=[DateTimeOffset]::UtcNow.AddSeconds($admin.expires_in)
    }
    $username="p09-$run-$actor";$script:Users[$actor]=$username
    $body=@{username=$username;enabled=$true;emailVerified=$true;email=($username+'@example.test');firstName=$actor;lastName='Integration';credentials=@(@{type='password';value=$secrets.INTEGRATION_USER_PASSWORD;temporary=$false})}|ConvertTo-Json -Depth 10
    $null=Invoke-WebRequest 'https://localhost:5289/admin/realms/inklink/users' -Method Post -Headers $adminHeaders -ContentType 'application/json' -Body $body -TimeoutSec 15
    $user=Invoke-RestMethod ('https://localhost:5289/admin/realms/inklink/users?exact=true&username='+$username) -Headers $adminHeaders -TimeoutSec 15
    $ids[$actor]=$user[0].id
    $null=Invoke-Api $actor Get '/api/v1/profiles/me'
}
Write-Host 'Synthetic accounts provisioned and linked.'
$owner=@{IdentityDatabase__ConnectionString=('Host=127.0.0.1;Port=55329;Database=inklink_identity;Username=inklink_identity_owner;Password='+$secrets.IDENTITY_OWNER_PASSWORD)}
$adminDll="$IntegrationRepo/src/backend/services/Identity/tools/InkLink.Identity.Admin/bin/Release/net10.0/InkLink.Identity.Admin.dll"
$null=Start-IntegrationDotnet -Arguments @($adminDll,'grant-artist','https://localhost:5289/realms/inklink',$ids.artist) -Environment $owner -Log "$IntegrationRoot/activate-artist.log" -Wait
$artist=Invoke-Api artist Put '/api/v1/artists/me' @{name="Artista $run";studio='Integração';city='São Paulo';biography='Dados sintéticos P09';styles=@('Blackwork');startingPrice=300} @(200,201)
$flash=Invoke-Api artist Post '/api/v1/artists/me/flashes' @{title='Flash de integração';imageUrl='https://example.test/flash.jpg';alternativeText='Desenho sintético';style='Blackwork';referencePrice=450} @(201)
$null=Invoke-Api artist Post '/api/v1/artists/me/tattoos' @{title='Trabalho de integração';imageUrl='https://example.test/tattoo.jpg';alternativeText='Trabalho sintético';style='Blackwork'} @(201)
$found=Invoke-Api '' Get ('/api/v1/artists/search?term='+$run)
Check (@($found.items|Where-Object id -eq $artist.id).Count -eq 1) 'Public search finds synthetic artist'
$null=Invoke-Api client Put ('/api/v1/favorites/'+$artist.id) -Expected @(204)
$favorites=Invoke-Api client Get '/api/v1/favorites'
Check ($favorites.items.Count -eq 1) 'Favorite is persisted'
$null=Invoke-Api '' Get '/api/v1/finance/dashboard' -Expected @(401)
$null=Invoke-Api client Get '/api/v1/finance/entries?from=2026-01-01&to=2026-01-02' -Expected @(403)
$first=$null
try{
    Invoke-IntegrationCompose @('stop','rabbitmq')|Out-Null
    $first=Invoke-Api client Post '/api/v2/engagements' @{artistId=$artist.id;idea='Projeto sintético';bodyRegion='Braço';approximateSizeCm=10;flashId=$flash.id} @(201)
    $pending=Invoke-IntegrationCompose @('exec','-T','engagements-db','psql','-U','postgres','-d','inklink_engagements','-At','-c',('SELECT count(*) FROM engagements.outbox WHERE '+[char]34+'EngagementId'+[char]34+'='+[char]39+$first.id+[char]39+' AND '+[char]34+'PublishedAt'+[char]34+' IS NULL'))
    Check ([int]($pending -join '') -ge 1) 'Broker interruption leaves durable Outbox pending'
}finally{Invoke-IntegrationCompose @('up','-d','--wait','rabbitmq')|Out-Null}
Wait-Condition { $null -ne (Find-Conversation $first.id) } 'Outbox recovers conversation after broker restart'
$old=Find-Conversation $first.id
$canary='private-p09-'+[Guid]::NewGuid().ToString('N')
$queryCanary='private-query-p09-'+[Guid]::NewGuid().ToString('N')
$null=Invoke-Api client Get ('/api/v1/profiles/me?private='+$queryCanary)
$null=Invoke-Api client Post ('/api/v1/conversations/'+$old.id+'/messages') @{body=$canary} @(201)
$null=Invoke-Api artist Post ('/api/v1/conversations/'+$old.id+'/messages') @{body='Resposta sintética'} @(201)
$null=Invoke-Api other Get ('/api/v1/conversations/'+$old.id) -Expected @(404)
$null=Invoke-Api client Post ('/api/v2/engagements/'+$first.id+'/closure') @{confirmed=$true} @(202)
Wait-Condition {(Invoke-Api client Get ('/api/v2/engagements/'+$first.id)).state -eq 'Closed'} 'Closure waits for durable message barrier'
$null=Invoke-Api client Post ('/api/v1/conversations/'+$old.id+'/messages') @{body='Envio bloqueado'} @(409)
$history=Invoke-Api client Get ('/api/v1/conversations/'+$old.id+'/messages')
Check ($history.items.Count -eq 2 -and @($history.items|Where-Object body -eq $canary).Count -eq 1) 'Closure preserves the private conversation history'
Check (@($history.items.authorId|Select-Object -Unique).Count -eq 2) 'Both participant authors are preserved'
$next=Invoke-Api client Post '/api/v2/engagements' @{artistId=$artist.id;idea='Retorno sintético';bodyRegion='Braço';approximateSizeCm=10} @(201)
$proposal=Invoke-Api artist Post ('/api/v2/engagements/'+$next.id+'/proposals') @{description='Sessão sintética';amount=450} @(201)
$null=Invoke-Api client Put ('/api/v2/engagements/'+$next.id+'/proposals/'+$proposal.id+'/acceptance') -Expected @(200)
Wait-Condition {$null -ne (Find-Conversation $next.id)} 'Return has a separate conversation'
$return=Find-Conversation $next.id
$null=Invoke-Api client Post ('/api/v1/conversations/'+$return.id+'/messages') @{body='Antes da reserva'} @(409)
$local=[DateTimeOffset]::UtcNow.ToOffset([TimeSpan]::FromHours(-3));$start=[DateTimeOffset]::new($local.Year,$local.Month,$local.Day,$local.Hour,$local.Minute,0,$local.Offset).AddMinutes(2);$end=$start.AddMinutes(1)
if($start.Date -ne $end.Date){throw 'Run the session smoke outside the midnight boundary.'}
$day=$start.ToString('yyyy-MM-dd');$to=$start.AddDays(1).ToString('yyyy-MM-dd')
$null=Invoke-Api artist Put ('/api/v1/scheduling/availability/'+$day) @{timeZoneId='America/Sao_Paulo';windows=@(@{start=$start.ToString('HH:mm:ss');end=$end.ToString('HH:mm:ss')})}
$reservation=Invoke-Api artist Post '/api/v1/scheduling/reservations' @{engagementId=$next.id;start=$start.ToString('O');end=$end.ToString('O')} @(201)
Wait-Condition {!(Find-Conversation $next.id).writingBlocked} 'Confirmed reservation enables return conversation'
$null=Invoke-Api artist Post ('/api/v1/conversations/'+$return.id+'/messages') @{body='Retorno habilitado'} @(201)
$null=Invoke-Api client Post ('/api/v2/engagements/'+$next.id+'/closure') @{confirmed=$true} @(202)
Wait-Condition {(Invoke-Api client Get ('/api/v2/engagements/'+$next.id+'/lifecycle')).operations.Where({$_.kind -eq 'Close' -and $_.status -eq 'Rejected'}).Count -eq 1} 'Future reservation rejects closure without losing chat'
$projects=Invoke-Api artist Get '/api/v2/engagements?perspective=artist'
Check ($projects.items.Count -eq 2) 'Artist can consult own client engagements'
$before=Invoke-Api artist Get ("/api/v1/finance/dashboard?from=$day&to=$to")
Check ($before.financial.received -eq 0 -and $before.scheduledReservations -eq 1) 'Reservation is not a receipt'
Write-Host 'Waiting for the real reserved minute to finish before recording session completion.'
Wait-Condition {[DateTimeOffset]::UtcNow -ge $end} 'Real session end reached' 200
$null=Invoke-Api artist Put ('/api/v1/scheduling/reservations/'+$reservation.id+'/completion') @{completedAt=$end.ToString('O');amount=450}
$key=[Guid]::NewGuid().ToString();$receiptBody=@{kind='Receipt';description='Recebimento sintético';amount=4000;effectiveDate=$day;engagementId=$next.id}
$receipt=Invoke-Api artist Post '/api/v1/finance/entries' $receiptBody @(201) $key
$repeat=Invoke-Api artist Post '/api/v1/finance/entries' $receiptBody @(200) $key
Check ($repeat.id -eq $receipt.id) 'Repeated receipt does not duplicate cash'
$null=Invoke-Api artist Post '/api/v1/finance/entries' @{kind='Cost';description='Custo sintético';amount=940;effectiveDate=$day} @(201)
$summary=Invoke-Api artist Get ("/api/v1/finance/dashboard?from=$day&to=$to")
Check ($summary.completedSessions -eq 1 -and $summary.clientsServed -eq 1 -and $summary.averageSessionValue -eq 450 -and $summary.scheduledReservations -eq 0) 'Completed session indicators use authoritative operational data'
$data=@{users=$script:Users;receipt=$receipt.id;oldConversation=$old.id;newConversation=$return.id;engagement=$next.id;reservation=$reservation.id;from=$day;to=$to;traceId=$script:Trace;verifiedAt=[DateTimeOffset]::UtcNow;checks=$script:Checks}
Verify-PreservedData ([pscustomobject]$data)
foreach($file in Get-ChildItem -LiteralPath $IntegrationRoot -File|Where-Object {$_.Extension -in '.log','.error'}){
    Check (!(Select-String -LiteralPath $file.FullName -SimpleMatch $canary -Quiet)) 'Logs exclude private message content'
    Check (!(Select-String -LiteralPath $file.FullName -SimpleMatch $queryCanary -Quiet)) 'Logs exclude private query values'
}
foreach($service in 'Identity','Catalog','Engagements','Messaging','Scheduling','Finance','Gateway'){
    Check (Select-String -LiteralPath "$IntegrationRoot/$service.log" -SimpleMatch $script:Trace -Quiet) "$service request logs retain the distributed trace"
}
[IO.File]::WriteAllText("$IntegrationRoot/smoke.json",($data|ConvertTo-Json -Depth 15))
Write-Host 'Integrated synthetic flow passed through real HTTPS, Gateway, Keycloak and RabbitMQ.'
