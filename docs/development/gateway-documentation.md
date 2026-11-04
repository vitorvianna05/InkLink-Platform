# Testar APIs pela documentação do Gateway

O Gateway oferece Swagger UI em http://127.0.0.1:5191/swagger somente em Development/Testing. O seletor no topo carrega os contratos de cada serviço pelo Gateway. As operações Try it out executam requisições reais: alterações válidas são persistidas no ambiente configurado.

## Preparação

Na raiz de InkLink-Platform, com Docker Linux, PowerShell 7, SDK fixado e certificado localhost já confiável:

~~~powershell
dotnet tool restore
dotnet restore InkLink.Platform.slnx --locked-mode
./src/backend/scripts/Initialize-Local.ps1
./src/backend/scripts/Start-Database.ps1
./src/backend/scripts/Configure-Keycloak.ps1
./src/backend/scripts/Apply-Migrations.ps1
~~~

A sincronização é necessária em realms existentes para criar inklink-swagger; importar apenas no primeiro boot não atualiza clientes. Initialize-Local acrescenta o cliente às listas aprovadas dos serviços por User Secrets. Não copie credenciais para arquivos versionados.

No Visual Studio, selecione múltiplos projetos de inicialização:

| Projeto | URL local |
| --- | --- |
| InkLink.Identity.Api | http://127.0.0.1:5180 |
| InkLink.Catalog.Api | http://127.0.0.1:5193 |
| InkLink.Engagements.Api | http://localhost:5194 |
| InkLink.Messaging.Api | http://localhost:5195 |
| InkLink.Scheduling.Api | http://127.0.0.1:5196 |
| InkLink.Finance.Api | http://127.0.0.1:5197 |
| InkLink.Gateway | http://127.0.0.1:5191/swagger |

Use os perfis Project com ASPNETCORE_ENVIRONMENT=Development. Gateway e Identity têm perfis alinhados às URLs de integração. BFF é opcional para testes separados de sessão web; os testes nesta interface usam Bearer via PKCE.

## Autenticar e executar

1. Abra /swagger no Gateway e selecione o serviço.
2. Clique em Authorize e use o cliente público inklink-swagger com openid/profile.
3. Na tela do Keycloak, entre com uma conta de desenvolvimento criada pelo administrador. Nenhuma senha ou conta fictícia é adicionada automaticamente por esta entrega.
4. Volte à interface e execute GET /api/v1/profiles/me em Identity para criar/consultar o vínculo interno.
5. Para ações de artista, atribua Artist conforme o procedimento administrativo em authentication.md; o Swagger não concede essa função.
6. Abra uma operação, use Try it out, preencha parâmetros/corpo e, quando exigido, Idempotency-Key com UUID. Execute e confira código HTTP e resposta.

O cliente não utiliza segredo ou password grant. O token expira conforme a política vigente (padrão local de cinco minutos); se receber 401, refaça Authorize. Logout no diálogo remove a autorização da ferramenta, mas não encerra a sessão SSO do Keycloak. Para alternar cliente/artista, encerre também a sessão no Keycloak ou use perfis/janelas privadas independentes do navegador. Recarregar a página não deve manter autorização persistida em armazenamento local. O website mantém o fluxo BFF próprio, sem compartilhar tokens da ferramenta.

## Documentos publicados

- /swagger/documents/identity.json
- /swagger/documents/catalog.json
- /swagger/documents/engagements.json — API v2 vigente
- /swagger/documents/messaging.json
- /swagger/documents/scheduling.json
- /swagger/documents/finance.json

Os documentos OpenAPI originais continuam em cada serviço, em /openapi/v1.json. O Gateway adapta apenas a apresentação de consumo público. Endpoints técnicos entre serviços não aparecem no catálogo; não use credenciais técnicas para contornar permissões de usuário.

Se um documento retornar 503 documentation.service_unavailable, confira se o serviço correspondente está rodando, seu /health/ready, sua URL configurada em Gateway e o ambiente Development. Isso não significa que os demais serviços estejam indisponíveis. Para o fluxo de Agenda, aceite da proposta e disponibilidade são pré-requisitos; uma consulta livre não confirma reserva. Documentação em scheduling.md e demais guias detalha os contratos.

## Roteiro inicial

Consultar/publicar artista em Catalog; criar solicitação em Engagements; apresentar proposta com Artist; aceitar com Client; consultar e enviar mensagens em Messaging; cadastrar disponibilidade e confirmar reserva em Scheduling. Serviços e broker precisam estar iniciados para os efeitos assíncronos. Encerramento distribuído e gestão financeira estão implementados em P07/P08; consultar ciclo integrado e Finance. A consolidação P09 está em [contratos dos consumidores](consumer-contracts.md), com exemplos e política de compatibilidade.

A ferramenta é de desenvolvimento/testes e não é registrada em produção. Assets são embarcados no pacote, sem necessidade de CDN. O limite do documento é 4 MiB; origem upstream é configuração fixa de serviço, não parâmetro livre do usuário.

Para execução integral isolada com HTTPS nos oito hosts, configuração e dados próprios, seguir o [ambiente P09](integration.md). O catálogo correspondente está em https://localhost:5291/swagger.
