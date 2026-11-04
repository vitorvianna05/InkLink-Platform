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

Entrar abre a tela do Keycloak em uma janela própria. O callback continua na porta do BFF, já registrada no realm; o website detecta a sessão e fecha a janela. Permitir pop-ups para o website. O primeiro acesso ao perfil cria Client; somente a ferramenta administrativa concede Artist. Estúdio depende da função confirmada pelo backend. O catálogo inicialmente pode estar vazio: publicar um artista pela conta apropriada, sem inserir cards fictícios.

A escrita usa o cookie de sessão e X-CSRF-TOKEN, obtido em /bff/antiforgery. Se a sessão expirar durante essa obtenção, o website também abre o aviso de expiração e preserva a intenção de escrita. Indisponibilidade desse endpoint é tratada como falha transitória. Favoritos e pedidos aguardam a resolução da sessão antes de aceitar cliques, evitando abrir outro login durante o carregamento.

Atendimentos usam /api/v2/engagements; demais serviços usam /api/v1. Favoritos, propostas, mensagens, reservas e lançamentos usam os contratos existentes, com idempotência quando exigida. Sessão, CSRF e permissões não são substituídos por fixtures. A consulta periódica de mensagens permanece a estratégia aprovada.

## Roteiro de navegador com serviços reais

O roteiro é opt-in e usa exclusivamente o ambiente P09 e suas contas sintéticas. Antes da primeira execução, completar Test-Integration.ps1 da P09 para produzir smoke.json e as contas no realm isolado. Esse roteiro prepara dados por APIs reais; não misturar com usuários de produção.

Com o website e backend ativos, na raiz:

```powershell
$env:INKLINK_RUN_REAL_E2E='1'
$env:NODE_EXTRA_CA_CERTS=(Resolve-Path artifacts/integration/localhost.pem).Path
# Opcional, se Chromium não estiver instalado pelo Playwright:
$env:PLAYWRIGHT_CHROMIUM_PATH='C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
Push-Location src/frontends/web
npm run e2e:integration
Pop-Location
```

playwright.integration.config.ts é separado das checagens visuais simuladas; não utiliza page.route/MSW nem ignora erros TLS. Lê as contas e senha dos artefatos ignorados da P09, sem copiá-las para código, parâmetros de linha de comando ou relatórios. Traces, vídeo e screenshots estão desabilitados para não persistir o tráfego de autenticação. Não versionar artefatos de execução ou estado de sessão.

O roteiro verifica catálogo/sugestões, login por popup, favorito com recarga, envio e persistência de mensagem, histórico bloqueado, pedido originado em flash, dashboard/agenda do artista e negação de acesso a terceiro. Altera somente os dados sintéticos: salva favorito, acrescenta mensagem, cria pedido e define disponibilidade futura do artista sintético; não apaga bancos ou reinicia serviços. O total de testes web é separado da suíte .NET. Responsividade/teclado continuam na suíte e2e visual; concluir esse roteiro reduz a pendência T10.10, mas não demonstra sozinho expiração temporal, reserva/encerramento completos e publicação de produção.

## Diagnóstico

- Conexão recusada em 5292: iniciar dependências e os oito hosts antes do website. A checagem /health/live do BFF não equivale à prontidão de todas as APIs.
- Erro TLS no proxy Node: apontar INKLINK_BFF_CA_CERT para o certificado correto. Não usar NODE_TLS_REJECT_UNAUTHORIZED=0.
- 401 em /bff/session antes de entrar é esperado. Após login, conferir HTTPS, localhost e cookies habilitados. A sessão não é renovada automaticamente.
- 403 em artist-context para Client é esperado; não libera Estúdio. 404 de conversa privada não revela se ela pertence a outra pessoa.
- Falha de uma API é apresentada como erro; ausência de dados não aciona catálogo ou indicadores fictícios. Usar traceId nos logs conforme o guia P09.
