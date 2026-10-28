# Desenvolvimento do Identity — P01

## Escopo

Primeiro serviço da aplicação definitiva, criado do zero em .NET 10. Domain contém Profile; Application contém a consulta GetMyProfile e as portas ICurrentUser/IProfileReader; Infrastructure implementa EF Core/Npgsql; Api compõe as dependências e os sinais operacionais.

A consulta recebe somente a identidade interna do contexto confiável e devolve ID e nome de exibição. Não recebe um ID arbitrário de outra pessoa. O host de integração fornece a identidade controlada e a fixture persiste perfis reais. Essa foi a base da P01. A P02 acrescentou Keycloak, vínculo externo/interno, autorização, Gateway e BFF; veja o [guia atual de autenticação](authentication.md). O perfil agora possui transporte HTTP autenticado.

## Pré-requisitos

- SDK definido em global.json (10.0.303, atualização de patch permitida).
- Docker Engine com contêineres Linux; Docker Compose v2.
- PowerShell 7 para os scripts locais, tanto no Windows quanto no Linux.
- Acesso ao NuGet e ao registro de imagens na primeira execução.

Execute na raiz do repositório:

~~~powershell
dotnet tool restore
dotnet restore InkLink.Platform.slnx --locked-mode
dotnet build InkLink.Platform.slnx --configuration Release --no-restore --warnaserror
dotnet format InkLink.Platform.slnx --no-restore --verify-no-changes
dotnet test InkLink.Platform.slnx --configuration Release --no-build --logger "trx;LogFilePrefix=identity" --results-directory artifacts/test-results
~~~

Os testes não dependem do banco local ou de seus User Secrets. Testcontainers cria PostgreSQL isolado, porta dinâmica e credenciais descartáveis, e remove o contêiner ao terminar. Docker indisponível causa falha de integração; não há substituição por banco em memória. Os relatórios TRX ficam em artifacts/ (ignorado pelo Git).

## Preparação e execução local

~~~powershell
./src/backend/scripts/Initialize-Local.ps1
./src/backend/scripts/Start-Database.ps1
./src/backend/scripts/Apply-Migrations.ps1
dotnet run --project src/backend/services/Identity/src/InkLink.Identity.Api --configuration Release --no-build -- --environment Development --urls http://127.0.0.1:5180
~~~

Initialize-Local gera três senhas aleatórias em .env.local, ignorado pelo Git, preservando o arquivo caso já exista. Configura somente a conexão de execução em .NET User Secrets, via entrada padrão, sem imprimir a senha ou passá-la nos argumentos. Não compartilhe esse arquivo. O armazenamento User Secrets é exclusivo de desenvolvimento; não é um cofre de produção.

O Compose publica PostgreSQL somente em 127.0.0.1:54329. O bootstrap cria banco inklink_identity, schema identity, usuário proprietário inklink_identity_owner e usuário restrito inklink_identity_app. A aplicação usa o segundo; o script de migração usa o primeiro e restaura a variável de ambiente anterior ao terminar. A migração não ocorre no startup da API.

O bootstrap só roda na criação do volume. Alterar .env.local depois de criar o banco não altera as senhas gravadas no PostgreSQL. Preserve arquivo e volume correspondentes; rotação requer alteração explícita das credenciais. Para parar sem perder dados:

~~~powershell
docker compose --env-file .env.local -f src/backend/compose.yaml down
~~~

Para outros ambientes, forneça IdentityDatabase__ConnectionString pelo gerenciador de segredos/ambiente, com host, database, username e password. A P02 também exige a configuração Authentication de issuer, audience e clientes; consulte o [guia atual](authentication.md). A aplicação falha no startup se a configuração estiver ausente, incompleta ou inválida, com mensagem sanitizada. Ambiente Testing não é selecionado automaticamente nem oferece configuração substituta. A conexão é validada sintaticamente no startup; sua disponibilidade é verificada pela prontidão.

## Migrações controladas

A migração InitialProfile e o snapshot estão versionados em Infrastructure/Migrations. Aplicar novamente é seguro: EF verifica o histórico no schema privado. Em desenvolvimento, use Apply-Migrations.ps1; nunca troque a conexão da aplicação por uma credencial com DDL.

Para preparar uma publicação, gere SQL revisável ou um bundle com a mesma versão de ferramentas. Exemplo sem aplicar no banco:

~~~powershell
dotnet ef migrations script --idempotent --project src/backend/services/Identity/src/InkLink.Identity.Infrastructure --startup-project src/backend/services/Identity/src/InkLink.Identity.Infrastructure --output artifacts/identity-migration.sql
~~~

A geração usa a factory de design e exige IdentityDatabase__ConnectionString válida. Injete a conexão pelo ambiente, sem gravá-la no código. O script é revisado e aplicado em etapa separada com a identidade proprietária; a credencial de execução não cria tabelas nem conecta a bancos privados de outros contextos. A infraestrutura definitiva de publicação será definida em P09.

## Operação e contratos disponíveis

| Rota | Resultado |
| --- | --- |
| GET /health/live | 200 enquanto o processo responde; independe de consulta ao PostgreSQL. |
| GET /health/ready | 200 quando consegue consultar a tabela profiles; 503 se o banco estiver indisponível, sem permissão ou sem migração. |
| GET /openapi/v1.json | Documento dos sinais operacionais e perfis protegidos, incluindo schemas de sucesso, erros e Bearer; somente em Development/Testing. |
| GET /api/v1/profiles/me | Implementada em P02: 200 para o próprio usuário autenticado; 401/403 para credenciais ausentes ou sem autorização. |

Erros HTTP usam application/problem+json, status, code e traceId, inclusive quando Accept solicita texto, HTML ou XML. O contrato de erro permanece JSON independentemente da preferência de mídia do cliente. Exceções inesperadas retornam internal_error sem mensagem interna ou stack trace. Indisponibilidade retorna identity.database_unavailable. O cabeçalho X-Trace-Id corresponde ao campo traceId do problema e ao escopo dos logs JSON. O middleware registra método, caminho sem query, status e duração; não registra corpo, autorização ou connection string. Logs detalhados do EF e de exceções automáticas são filtrados para preservar essa política. A investigação usa a correlação, sem habilitar dados sensíveis em produção.

## Verificação e CI

UnitTests cobre invariantes do perfil, UUIDv7, identidade ausente/vazia, perfil inexistente e seleção exclusiva do usuário atual. IntegrationTests usa WebApplicationFactory e PostgreSQL real para persistência, validação no banco, permissões, migrações repetíveis, configuração obrigatória, ausência de transporte privado, sinais operacionais, sanitização e correlação. ArchitectureTests verifica referências entre camadas e inclui uma dependência proibida deliberada somente na assembly de teste; o teste confirma que ArchUnitNET a detecta.

A CI em .github/workflows/identity.yml executa restauração bloqueada pelos lockfiles, formatação, build Release com avisos como erros e as três suítes. Falhas interrompem o job; TRX é publicado mesmo em falha. Configure esse job como verificação obrigatória na proteção da branch no GitHub caso queira impedir merge por regra do repositório. A execução remota será observável após o envio; a validação local está registrada em [P01](../verification/p01.md).
