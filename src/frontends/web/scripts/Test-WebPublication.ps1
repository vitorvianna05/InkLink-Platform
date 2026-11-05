# Verificação reproduzível da publicação (T10.10): regras do servidor do build publicado pelo Caddy
# (Start-WebPublication.ps1). Somente leitura; TLS verificado pela confiança do sistema.
param([string]$BaseUrl='https://localhost:5173')
$ErrorActionPreference='Stop'
$failures=[Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Name){
    if($Condition){Write-Host "ok   $Name"}else{Write-Host "FAIL $Name";$failures.Add($Name)}
}
function Get-Page([string]$Path){Invoke-WebRequest -Uri ($BaseUrl+$Path) -SkipHttpErrorCheck -TimeoutSec 20}
function Header($Response,[string]$Name){if($Response.Headers[$Name]){[string]$Response.Headers[$Name]}else{''}}

# SPA: rotas profundas recebem index.html sempre revalidado.
foreach($path in @('/','/mensagens/c1','/estudio/agenda?dia=2030-01-15','/entrar')){
    $page=Get-Page $path
    Check ($page.StatusCode -eq 200 -and (Header $page 'Content-Type') -like 'text/html*') "SPA $path devolve index.html"
    Check ((Header $page 'Cache-Control') -eq 'no-cache') "SPA $path com Cache-Control no-cache"
}

# Arquivos versionados: cache imutável só quando existem; ausentes respondem 404, nunca HTML.
$index=Get-Page '/'
$asset=[regex]::Match($index.Content,'/assets/[^"]+\.js').Value
Check ($asset -ne '') 'index.html referencia um script em /assets'
$script=Get-Page $asset
Check ($script.StatusCode -eq 200 -and (Header $script 'Cache-Control') -eq 'public, max-age=31536000, immutable') 'script existente com cache imutável'
$missing=Get-Page '/assets/chunk-inexistente.js'
Check ($missing.StatusCode -eq 404 -and (Header $missing 'Cache-Control') -notlike '*immutable*') 'arquivo ausente em /assets responde 404 sem cache longo'

# Cabeçalhos de segurança em todas as respostas do site.
foreach($name in @('Strict-Transport-Security','X-Content-Type-Options','X-Frame-Options','Referrer-Policy','Permissions-Policy')){
    Check ((Header $index $name) -ne '') "cabeçalho $name"
}
Check ((Header $index 'Server') -eq '') 'cabeçalho Server removido'

# BFF e APIs na mesma origem; retornos OIDC não caem no fallback da SPA.
Check ((Get-Page '/bff/session').StatusCode -eq 401) '/bff/session encaminhado ao BFF (visitante: 401)'
$artists=Get-Page '/api/v1/artists?limit=20'
Check ($artists.StatusCode -eq 200 -and (Header $artists 'Content-Type') -like 'application/json*') '/api encaminhado ao Gateway'
$callback=Get-Page '/signin-oidc'
Check ((Header $callback 'Content-Type') -notlike 'text/html*') '/signin-oidc encaminhado ao BFF, sem fallback'
# Redirecionamento lido sem segui-lo (Invoke-WebRequest trata 302 sem seguir como erro).
$handler=[Net.Http.HttpClientHandler]::new();$handler.AllowAutoRedirect=$false
$http=[Net.Http.HttpClient]::new($handler)
try{
    $login=$http.GetAsync($BaseUrl+'/bff/login?returnUrl=%2Fmeu-espaco').GetAwaiter().GetResult()
    Check ([int]$login.StatusCode -eq 302 -and [string]$login.Headers.Location -like '*/protocol/openid-connect/auth*') '/bff/login redireciona ao provedor'
}finally{$http.Dispose()}

# /media: obra pública servida sem cookies; objeto privado sem assinatura recusado.
$public=$null
foreach($artist in ($artists.Content|ConvertFrom-Json).items){
    foreach($kind in @('flashes','tattoos')){
        $works=(Get-Page "/api/v1/artists/$($artist.id)/$kind").Content|ConvertFrom-Json
        $public=$works.items|Where-Object {-not $_.externalImage -and $_.imageUrl -like "$BaseUrl/media/*"}|Select-Object -First 1
        if($public){break}
    }
    if($public){break}
}
if($public){
    $image=Invoke-WebRequest -Uri $public.thumbnailUrl -SkipHttpErrorCheck -TimeoutSec 20
    Check ($image.StatusCode -eq 200 -and (Header $image 'Content-Type') -eq 'image/webp' -and -not $image.Headers['Set-Cookie']) '/media serve a obra pública em WebP, sem cookies'
}else{
    Write-Host 'skip /media pública: nenhuma obra enviada no catálogo (rode o roteiro P09 antes)'
}
Check ((Get-Page '/media/inklink-identity-private/profiles/inexistente/foto/128.webp').StatusCode -eq 403) '/media recusa objeto privado sem assinatura'

if($failures.Count){throw "Publicação com $($failures.Count) verificação(ões) reprovada(s)."}
Write-Host 'Publicação verificada.'
