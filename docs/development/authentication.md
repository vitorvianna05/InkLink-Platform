# Execução de P02 — Identidade, Gateway e BFF

Este guia complementa [Identity/P01](identity.md) e a [decisão 0007](../decisions/0007-p02-identity-and-sessions.md). A política inicial foi aprovada: contas administrativas no Keycloak, Client no primeiro acesso e Artist adicional por operação administrativa. Não há frontend nesta etapa.

## Componentes e isolamento

| Componente | Caminho | Responsabilidade |
| --- | --- | --- |
| Identity | src/backend/services/Identity/src | Perfil privado, vínculo issuer/subject e funções locais. |
| Gateway | src/backend/gateway/InkLink.Gateway | Validar access tokens e encaminhar chamadas a Identity via YARP. |
| BFF | src/backend/bff/InkLink.Bff | Login OIDC/PKCE, sessão por cookie e encaminhamento com token do servidor. |
| Authentication | src/backend/building-blocks/InkLink.Authentication | Validação técnica de tokens e políticas de chamador; sem domínio compartilhado. |
| Admin | src/backend/services/Identity/tools/InkLink.Identity.Admin | Ativar Artist em conta já vinculada, fora da superfície HTTP pública. |

Identity, BFF e Keycloak usam bancos privados. BFF não lê o banco de Identity. Keycloak não recebe migrações do produto. Tickets e chaves do BFF são dados operacionais, não entidades compartilhadas entre serviços.

## Preparação local no Windows

Requer PowerShell 7, Docker Linux, SDK de global.json e certificado localhost de desenvolvimento já confiável e exportável. O script não instala certificados nem altera a confiança do sistema. Ele seleciona um certificado existente em CurrentUser/My e CurrentUser/Root e exporta arquivos privados somente para artifacts/local, ignorado pelo Git. O Kestrel do BFF recebe o PFX correspondente explicitamente.

Na raiz:

~~~powershell
dotnet tool restore
dotnet restore InkLink.Platform.slnx --locked-mode
./src/backend/scripts/Initialize-Local.ps1
./src/backend/scripts/Start-Database.ps1
./src/backend/scripts/Configure-Keycloak.ps1
./src/backend/scripts/Apply-Migrations.ps1
dotnet build InkLink.Platform.slnx --configuration Release --no-restore --warnaserror
~~~

O Compose é exclusivamente de desenvolvimento e executa Keycloak em start-dev, mesmo com TLS habilitado. Não é um manifesto de produção. Initialize-Local preserva senhas existentes em .env.local, gera apenas as ausentes e configura User Secrets sem imprimir os valores. O realm resolvido com os segredos e os certificados ficam em artifacts/local. Não compartilhe esses arquivos.

O primeiro import do Keycloak só ocorre se o realm não existir. Configure-Keycloak sincroniza os clientes e scopes conhecidos em um realm local existente, preservando usuários. Ele só se conecta ao endereço HTTPS localhost documentado, usa a credencial bootstrap local e aguarda a disponibilidade do emissor por tempo limitado. O realm versionado exige TLS, contém basic/profile e o scope técnico, não contém contas de demonstração e não habilita password grant nos clientes do produto.

Para Linux ou outro ambiente, configure as variáveis correspondentes diretamente e forneça um certificado PFX compartilhado com chave privada. A seleção automática pelo provider Cert: do PowerShell é específica do Windows. Não use os certificados de desenvolvimento em produção.

## Inicialização dos processos

Em três terminais na raiz, mantendo o ambiente Development explícito:

~~~powershell
dotnet run --project src/backend/services/Identity/src/InkLink.Identity.Api --configuration Release --no-build -- --environment Development --urls http://127.0.0.1:5180
dotnet run --project src/backend/gateway/InkLink.Gateway --configuration Release --no-build -- --environment Development --urls http://127.0.0.1:5191
dotnet run --project src/backend/bff/InkLink.Bff --configuration Release --no-build -- --environment Development --urls https://localhost:5192
~~~

As linhas representam processos separados. Keycloak: https://localhost:5189. BFF: https://localhost:5192/bff/login. Os endereços HTTP dos dois serviços são apenas loopback de desenvolvimento. Publicação exige HTTPS e configuração própria.

Crie uma conta no console administrativo do realm inklink, com senha definida pelo operador. A senha bootstrap local está em .env.local; não é uma senha de produção. O login usa a tela do Keycloak e retorna para /bff/session. Consulte /api/v1/profiles/me pelo mesmo BFF para criar/recuperar o perfil interno. Repetir o acesso preserva o ID.

## Contratos

| Rota | Acesso e resultados principais |
| --- | --- |
| GET /api/v1/profiles/me | Access token de usuário aprovado: 200 com id, displayName e roles. 401 sem token válido; 403 para chamador técnico/não autorizado. |
| PUT /api/v1/profiles/me | Apenas displayName. 204 ao atualizar; 400 para nome vazio, JSON inválido ou campos extras; 401/403 conforme autenticação. |
| GET /api/v1/internal/profiles/{id} | Somente cliente técnico autorizado com identity.profile.read. 200 com id/displayName, 404 se inexistente, 401/403 se não autorizado. |
| GET /bff/login | Inicia OIDC por código, PKCE S256, state/nonce e PAR quando anunciado pelo provedor. Retorno fixo para /bff/session. |
| GET /bff/session | 200 com authenticated e name da identidade federada; 401 sem sessão válida. Perfil e funções atuais são consultados em Identity. Não devolve tokens. |
| GET /bff/antiforgery | Sessão válida: devolve requestToken e cookie antiforgery. |
| POST /bff/logout | Sessão e X-CSRF-TOKEN válidos: remove ticket durável e redireciona ao logout OIDC. Sem CSRF: 400. |

As variantes /api/v1/profiles/me e /api/v1/profiles/me/ usam a mesma identidade para GET e PUT. Apenas endpoints suportados e autorizados disparam o comando de bootstrap; um método não mapeado não cria conta.

O OpenAPI de Identity, em /openapi/v1.json durante Development/Testing, declara Bearer nas operações privadas e schemas de erros, incluindo erros por campo e resposta 500. Erros não dependem de preferência de mídia para usar application/problem+json.

O navegador envia cookies ao BFF. Para PUT e POST, primeiro obtenha requestToken e envie-o em X-CSRF-TOKEN, preservando o cookie antiforgery. O BFF troca qualquer Authorization recebido pelo access token do ticket armazenado no servidor; Gateway remove cookies e headers de identidade não confiáveis antes de encaminhar.

A sessão tem limite fixo de cinco minutos, reduzido à validade do access token se necessário. Sem renovação automática; a expiração retorna 401 e exige novo login. Tickets e material de Data Protection são compartilhados no banco privado e protegidos pelo certificado configurado. Logout invalida o ticket em todas as instâncias do BFF; não depende apenas de apagar o cookie.

## Ativação administrativa de Artist

Após o primeiro acesso da conta, o operador identifica issuer e subject no provedor. Injete IdentityDatabase__ConnectionString com a credencial administrativa por mecanismo seguro e execute:

~~~powershell
dotnet run --project src/backend/services/Identity/tools/InkLink.Identity.Admin --configuration Release --no-build -- grant-artist https://localhost:5189/realms/inklink SUBJECT_DO_KEYCLOAK
~~~

O processo retorna 0 em sucesso, 2 para argumentos inválidos, 3 se a conta ainda não foi vinculada e 1 para erro operacional sanitizado. Não informe connection strings nos argumentos nem compartilhe o ambiente administrativo com frontends. Não existe endpoint de concessão de Artist. O próximo GET do perfil retorna Client e Artist, sem depender de alterar ou emitir novamente o access token.

## Configuração em outros ambientes

- Identity e Gateway: Authentication__Authority, Authentication__Audience e arrays Authentication__UserClients__0... / Authentication__TechnicalClients__0... . AllowHttpMetadata deve permanecer false em produção.
- Identity: IdentityDatabase__ConnectionString com a credencial de execução.
- Gateway: Gateway__IdentityAddress com URL HTTPS do serviço.
- BFF: Bff__Authority, ClientId, ClientSecret, GatewayAddress, ConnectionString, CertificatePath, CertificatePassword; no ambiente, use o separador duplo de .NET, como Bff__ClientId. Certificado deve estar válido e conter chave privada; a mesma aplicação, banco e material de proteção são compartilhados pelas instâncias.
- Kestrel do BFF e infraestrutura de entrada: configurar HTTPS com certificado apropriado ao ambiente. A chave de proteção de sessão não substitui o certificado TLS do servidor.

Os endpoints e clientes do realm devem ser ajustados para as origens publicadas. Segredos vêm do cofre/ambiente; não copie .env.local, User Secrets nem clientes de teste. Aplicar as migrações de Identity e BFF é uma etapa separada com as credenciais proprietárias, nunca no startup. Rotação de certificados precisa preservar o material necessário para ler tickets/chaves ainda válidos; a operação definitiva é refinada em P09.

## Testes

~~~powershell
dotnet test InkLink.Platform.slnx --configuration Release --no-build --logger "trx;LogFilePrefix=p02" --results-directory artifacts/test-results
~~~

A integração usa PostgreSQL real e Keycloak HTTPS descartável, com certificado temporário validado por correspondência exata apenas nos hosts de teste. Não instala raízes de confiança. Testes de API usam clientes descartáveis com password grant somente dentro da fixture; o BFF é testado pelo fluxo real de código com PKCE/PAR, formulário do provedor, cookies e troca de código.

Cenários incluem duas contas, funções, vínculo concorrente, emissor, ID token, assinatura adulterada, destinatário incorreto, expiração, acesso direto e pelo Gateway, sessão entre instâncias, CSRF e logout. Resultados consolidados ficam na verificação da P02; testes não substituem a autorização nos serviços.

## Logs e coordenação dos casos de uso

ProvisionAccount, ActivateArtist e UpdateMyProfile coordenam as alterações em Application; o repositório fica restrito às consultas, escrita atômica e tradução de conflito de chave. GetMyProfile permanece somente leitura, após o bootstrap explícito de primeiro acesso. Nenhum endpoint administrativo foi adicionado.

Os três processos .NET registram cada requisição com Method, Path (sem query), StatusCode e ElapsedMs. O escopo JSON inclui service e traceId. Use X-Trace-Id ou o traceId do Problem Details para localizar o evento, inclusive em rejeições de autenticação e erros tratados. Não habilite logs de URL completa, headers de credenciais ou conteúdo de tickets para obter correlação.
