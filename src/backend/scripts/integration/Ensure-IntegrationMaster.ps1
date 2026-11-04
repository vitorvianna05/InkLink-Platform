# Local P09 account only: Client + Artist, without administrative or cross-account access.
. "$PSScriptRoot/Integration.Common.ps1"
$accountPath=Join-Path $IntegrationRoot 'master-account.json'
$secrets=Get-IntegrationSecrets
if(Test-Path -LiteralPath $accountPath){
    $account=Get-Content -LiteralPath $accountPath -Raw|ConvertFrom-Json -AsHashtable
    if($account.Username -ne 'master' -or [string]::IsNullOrWhiteSpace($account.Password)){throw 'Invalid local master credential file.'}
}else{
    $account=@{Username='master';Password=('InkLink!'+[Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(8)))}
}
$adminDll=Join-Path $IntegrationRepo 'src/backend/services/Identity/tools/InkLink.Identity.Admin/bin/Release/net10.0/InkLink.Identity.Admin.dll'
if(!(Test-Path -LiteralPath $adminDll)){throw 'Build the backend Release solution before preparing the local master account.'}
$stage='admin authentication'
try{
    $admin=Invoke-RestMethod 'https://localhost:5289/realms/master/protocol/openid-connect/token' -Method Post -TimeoutSec 60 -ContentType 'application/x-www-form-urlencoded' -Body @{grant_type='password';client_id='admin-cli';username='admin';password=$secrets.KEYCLOAK_ADMIN_PASSWORD}
    $headers=@{Authorization='Bearer '+$admin.access_token}
    $stage='account lookup'
    $users=Invoke-RestMethod 'https://localhost:5289/admin/realms/inklink/users?exact=true&username=master' -Headers $headers -TimeoutSec 15
    if($users.Count -gt 1){throw 'Ambiguous local account.'}
    if($users.Count -eq 1 -and !(Test-Path -LiteralPath $accountPath)){
        throw 'An account already uses this username without matching local credentials; no changes were made.'
    }
    if($users.Count -eq 1 -and $account.Subject -and $account.Subject -ne $users[0].id){throw 'The local username belongs to a different identity.'}
    # Persist before creation so a retry can recover without changing the password.
    [IO.File]::WriteAllText($accountPath,($account|ConvertTo-Json -Depth 5))
    if($users.Count -eq 0){
        $body=@{username='master';enabled=$true;emailVerified=$true;email='master@example.test';firstName='Master';lastName='Local';requiredActions=@();credentials=@(@{type='password';value=$account.Password;temporary=$false})}|ConvertTo-Json -Depth 10
        $stage='account creation'
        $null=Invoke-WebRequest 'https://localhost:5289/admin/realms/inklink/users' -Method Post -Headers $headers -ContentType 'application/json' -Body $body -TimeoutSec 15
        $stage='account lookup'
        $users=Invoke-RestMethod 'https://localhost:5289/admin/realms/inklink/users?exact=true&username=master' -Headers $headers -TimeoutSec 15
    }
    if($users.Count -ne 1){throw 'Local account was not found after creation.'}
    $stage='user authentication'
    $token=Invoke-RestMethod 'https://localhost:5289/realms/inklink/protocol/openid-connect/token' -Method Post -TimeoutSec 60 -ContentType 'application/x-www-form-urlencoded' -Body @{grant_type='password';client_id='inklink-integration-users';username=$account.Username;password=$account.Password}
    $userHeaders=@{Authorization='Bearer '+$token.access_token}
    $stage='profile provisioning'
    $profile=Invoke-RestMethod 'https://localhost:5291/api/v1/profiles/me' -Headers $userHeaders -TimeoutSec 20
    if($account.ProfileId -and $account.ProfileId -ne $profile.id){throw 'The local profile identity changed.'}
    $account.Subject=$users[0].id
    $owner=@{IdentityDatabase__ConnectionString=('Host=127.0.0.1;Port=55329;Database=inklink_identity;Username=inklink_identity_owner;Password='+$secrets.IDENTITY_OWNER_PASSWORD)}
    $stage='artist activation'
    $null=Start-IntegrationDotnet -Arguments @($adminDll,'grant-artist','https://localhost:5289/realms/inklink',$users[0].id) -Environment $owner -Log "$IntegrationRoot/master-activation.log" -Wait
    $stage='artist verification'
    $null=Invoke-RestMethod 'https://localhost:5291/api/v1/scheduling/artist-context' -Headers $userHeaders -TimeoutSec 20
    $account.ProfileId=$profile.id
    [IO.File]::WriteAllText($accountPath,($account|ConvertTo-Json -Depth 5))
}catch{
    throw "Unable to prepare the local master account during $stage. Check the isolated P09 dependencies, credentials and master-activation logs. Existing passwords are never reset."
}
Write-Host "Local master account ready (Client + Artist). Credentials: $accountPath"
