# Integração do website com o backend local

Complementa a [decisão 0016](../decisions/0016-p10-website.md), os [contratos](consumer-contracts.md) e o [ambiente P09](integration.md). O website não utiliza respostas simuladas em execução normal. Navegador → Vite/proxy HTTPS → BFF → Gateway → APIs; Keycloak autentica por código/PKCE. Tokens permanecem no servidor e as APIs continuam responsáveis pela autorização e validação.

## Executar

Windows, PowerShell 7, Node 22+, SDK da solution, Docker Linux e certificado localhost já confiável são pré-requisitos. Na raiz do produto, preparar o backend conforme P09 (a inicialização preserva dados e credenciais):

```powershell
dotnet tool restore
dotnet restore InkLink.Platform.slnx --locked-mode
dotnet build InkLink.Platform.slnx --configuration Release --no-restore --warnaserror
./src/backend/scripts/integration/Initialize-Integration.ps1
./src/backend/scripts/integration/Start-Integration.ps1
```

Instalar as dependências web uma vez e iniciar em outro terminal:

```powershell
Push-Location src/frontends/web
npm ci
Pop-Location
./src/frontends/web/scripts/Start-WebIntegration.ps1
```

Abrir **https://localhost:5173**. O script verifica o BFF em 5292, aponta o proxy para ele e fornece o PEM/chave de artifacts/integration ao Vite. Configura confiança do proxy Node por INKLINK_BFF_CA_CERT, sem desabilitar TLS, alterar a confiança do sistema ou escrever segredos no frontend. As variáveis só valem para esse processo e são restauradas ao sair. Ctrl+C encerra o servidor web; os comandos de parada P09 encerram somente o backend isolado. Outra porta pode ser escolhida com -Port; o roteiro automatizado real usa 5173.

Para verificar o bundle compilado, executar npm run build em src/frontends/web e iniciar o script com -Preview. Dev e preview encaminham /bff, /api, /signin-oidc e /signout-callback-oidc e permitem recarga de rotas SPA. Preview é verificação local, não hospedagem de produção.

## Configuração manual

Alternativamente, copiar src/frontends/web/.env.example para .env.local. INKLINK_BFF_URL deve ser https://localhost:5292 para P09 (5192 é o BFF do ambiente de desenvolvimento original). INKLINK_DEV_CERT e INKLINK_DEV_KEY recebem os caminhos absolutos de artifacts/integration/localhost.pem e localhost.key. INKLINK_BFF_CA_CERT recebe o PEM do BFF. Manter INKLINK_BFF_INSECURE=false. Certificado/chave, credenciais e arquivos .env locais não são versionados nem incluídos em dist/; somente o servidor Vite lê essas configurações.

Usar localhost em todos os endereços, inclusive no navegador. Os cookies __Host- exigem HTTPS. Cookies não são isolados por porta; usar perfis separados de navegador ao alternar entre ambientes. Não substituir por tokens em localStorage, conexão direta ao Gateway ou CORS permissivo.

## Conta local padrão

Por solicitação do responsável, Start-Integration.ps1 prepara o usuário **master** no realm isolado da P09. A mesma conta possui Client e Artist para testar as duas áreas. O nome não representa uma nova função administrativa: continuam valendo as autorizações por recurso, sem acesso aos dados privados de outros usuários ou administração do Keycloak.

A senha é gerada uma vez e guardada em artifacts/integration/master-account.json, ignorado pelo Git. Reexecuções preservam senha, subject e perfil. Para preparar a conta no ambiente já ativo ou consultar suas credenciais, na raiz:

```powershell
./src/backend/scripts/integration/Ensure-IntegrationMaster.ps1
Get-Content artifacts/integration/master-account.json | ConvertFrom-Json | Select-Object Username,Password
```

Abrir https://localhost:5173, clicar em Entrar e usar essas credenciais no Keycloak. Estúdio do artista aparece após a sessão ser confirmada. A conta é criada administrativamente no Keycloak, vinculada por profiles/me e ativada com InkLink.Identity.Admin, seguindo a política inicial. O script nunca redefine uma senha existente e confere a identidade armazenada antes de aplicar a função. Se outra conta já usar o nome sem as credenciais locais correspondentes, a preparação falha sem assumir essa identidade. Esse provisionamento é exclusivo dos scripts locais P09; não há conta/senha padrão em produção ou no realm versionado.

## Contas e fluxos

Entrar leva a /entrar e, dali, a /bff/login?returnUrl=<destino> (decisão 0017). O callback continua na porta do BFF, já registrada no realm; ao concluir, o BFF devolve o navegador a https://localhost:5173 no destino, já autenticado (Bff__WebOrigin definido por Start-Integration.ps1). Somente o novo login após sessão expirada usa janela própria; permitir pop-ups para esse caso. O primeiro acesso ao perfil cria Client; somente a ferramenta administrativa concede Artist. Estúdio depende da função confirmada pelo backend. O catálogo inicialmente pode estar vazio: publicar um artista pela conta apropriada, sem inserir cards fictícios.

A escrita usa o cookie de sessão e X-CSRF-TOKEN, obtido em /bff/antiforgery. Se a sessão expirar durante essa obtenção, o website também abre o aviso de expiração e preserva a intenção de escrita. Indisponibilidade desse endpoint é tratada como falha transitória. Favoritos e pedidos aguardam a resolução da sessão antes de aceitar cliques, evitando abrir outro login durante o carregamento.

Atendimentos usam /api/v2/engagements; demais serviços usam /api/v1. Favoritos, propostas, mensagens, reservas e lançamentos usam os contratos existentes, com idempotência quando exigida. Sessão, CSRF e permissões não são substituídos por fixtures. A consulta periódica de mensagens permanece a estratégia aprovada.

## Roteiro de navegador com serviços reais

O roteiro é opt-in e usa exclusivamente o ambiente P09 e suas contas sintéticas. Antes da primeira execução, completar Test-Integration.ps1 da P09 para produzir smoke.json e as contas no realm isolado. Esse roteiro prepara dados por APIs reais; não misturar com usuários de produção.

Com o website e backend ativos, na raiz:

```powershell
$env:INKLINK_RUN_REAL_E2E='1'
# Opcional, se Chromium não estiver instalado pelo Playwright:
$env:PLAYWRIGHT_CHROMIUM_PATH='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
Push-Location src/frontends/web
npm run e2e:integration
Pop-Location
```

playwright.integration.config.ts é separado das checagens visuais simuladas; não utiliza page.route/MSW nem ignora erros TLS. As verificações diretas de API são feitas por fetch no próprio navegador, na origem do website e com os cookies da sessão: o cliente Node do Playwright não aceita o certificado de desenvolvimento do ASP.NET Core (autoassinado, CA:FALSE) como raiz, mesmo com NODE_EXTRA_CA_CERTS. Lê as contas e senha dos artefatos ignorados da P09, sem copiá-las para código, parâmetros de linha de comando ou relatórios. Traces, vídeo e screenshots estão desabilitados para não persistir o tráfego de autenticação. Não versionar artefatos de execução ou estado de sessão.

O roteiro tem três arquivos em `e2e-real/`, com auxiliares comuns em `support.ts`:
- **`backend.spec.ts`:** catálogo e sugestões; login por `/entrar` e pela tela de credenciais com o tema InkLink; favorito com recarga; envio e persistência de mensagem; histórico bloqueado; pedido originado em flash; dashboard e agenda do artista; saída pelo menu; negação de acesso a terceiro.
- **`lifecycle.spec.ts`:** ciclo completo pelo website, com duas contas: pedido, proposta, aceite, reserva confirmada e conflito, encerramento recusado por sessão futura, conclusão da sessão depois do término real, lançamento de recebimento e encerramento definitivo. Também cobre a sessão perdida durante um formulário: aviso, novo login em janela própria e reenvio sem duplicar a mensagem.
- **`media.spec.ts`:** obra enviada pelo Portfólio e servida em `/media`; foto do cliente privada e assinada, vista pelo artista na conversa e recusada sem assinatura; foto do artista projetada no perfil público pelo evento da Identity.

O roteiro altera somente os dados sintéticos: salva favorito, acrescenta mensagens, cria pedidos, obras e fotos, define disponibilidade do dia e registra reserva, conclusão e lançamento do artista sintético. Não apaga bancos nem reinicia serviços. O ciclo completo espera o término real de uma reserva de um minuto, de 3 a 4 minutos no total, e não deve rodar perto da meia-noite no fuso do artista. Responsividade e teclado continuam na suíte e2e visual.

## Homologação da publicação

`./src/frontends/web/scripts/Start-WebPublication.ps1` faz o build de produção e o serve pelo Caddy (`src/frontends/web/deploy/Caddyfile`) em https://localhost:5173, com o certificado local e o ambiente P09 ativo. Pare o servidor de desenvolvimento antes, porque a porta é a mesma.

A configuração é a mesma que se usa em qualquer host:
- **Mesma origem:** o build estático, `/bff`, `/api` e os retornos OIDC encaminhados ao BFF, com TLS verificado e o Host preservado (assim o `redirect_uri` registrado no realm continua válido), e `/media` encaminhado ao armazenamento.
- **SPA:** rotas desconhecidas recebem `index.html` (recarga de endereço profundo).
- **Cache:** `/assets/*` com cache imutável e `index.html` sem cache.
- **Cabeçalhos de segurança:** HSTS, `nosniff`, `X-Frame-Options`, `Referrer-Policy` e `Permissions-Policy`.

Em produção, troque os upstreams e o certificado por variáveis de ambiente. Configure `Bff:WebOrigin` vazio quando BFF e website compartilharem a origem, e aponte `PublicBaseUrl` para o domínio público do R2 em vez de `/media` (decisão 0018). Com o contêiner ativo, o roteiro real pode ser repetido contra o build publicado.

## Diagnóstico

- Conexão recusada em 5292: iniciar dependências e os oito hosts antes do website. A checagem /health/live do BFF não equivale à prontidão de todas as APIs.
- Erro TLS no proxy Node: apontar INKLINK_BFF_CA_CERT para o certificado correto. Não usar NODE_TLS_REJECT_UNAUTHORIZED=0.
- 401 em /bff/session antes de entrar é esperado. Após login, conferir HTTPS, localhost e cookies habilitados. A sessão não é renovada automaticamente.
- 403 em artist-context para Client é esperado; não libera Estúdio. 404 de conversa privada não revela se ela pertence a outra pessoa.
- Falha de uma API é apresentada como erro; ausência de dados não aciona catálogo ou indicadores fictícios. Usar traceId nos logs conforme o guia P09.
