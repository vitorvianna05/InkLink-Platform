# Execução e contratos de Engagements

## Preparação

Requer SDK conforme `global.json`, Docker Compose para o ambiente padrão e os pré-requisitos de Identity/Catalog. Na raiz:

```powershell
dotnet tool restore --tool-manifest dotnet-tools.json
./src/backend/scripts/Initialize-Local.ps1
./src/backend/scripts/Start-Database.ps1
./src/backend/scripts/Configure-Keycloak.ps1
./src/backend/scripts/Apply-Migrations.ps1
dotnet run --project src/backend/services/Engagements/src/InkLink.Engagements.Api
```

Initialize preserva credenciais existentes e adiciona segredos de Engagements. Configure-Keycloak cria o novo cliente em realms existentes e associa o escopo opcional; o import sozinho não atualiza realms já criados. Não há usuários ou publicações sintéticas automáticas. Inicie também Identity, Catalog e Gateway conforme seus guias. Porta de desenvolvimento: 5194; PostgreSQL próprio: 54332; schema `engagements`. Migração usa owner, execução usa app sem DDL. Gateway recebe `Gateway:EngagementsAddress`; BFF usa sua rota privada existente e mantém defesa CSRF.

Configuração obrigatória em User Secrets/ambiente: `EngagementsDatabase:ConnectionString`, `Authentication` conforme P02, `Engagements:IdentityAddress`, `Engagements:CatalogAddress`, `Engagements:ClientSecret` (cliente fixo `inklink-engagements`). Token técnico obtido no endpoint OIDC do Authority, escopo `catalog.engagement-context.read`; sem cache persistente ou log de tokens. Clientes HTTP têm timeout de 10 segundos, cancelamento e redirects desabilitados. HTTPS é obrigatório fora de Development/Testing. Não repita automaticamente escritas com novas chaves após timeout.

## API

Todas as rotas exigem token de usuário aprovado. Identidade interna e Artist são confirmados no serviço Identity; headers e campos de identidade fornecidos pelo cliente não concedem acesso. Respostas privadas usam `Cache-Control: no-store`. OpenAPI em `/openapi/v1.json` em Development/Testing. `/health/live` verifica processo; `/health/ready` consulta o schema de Engagements.

| Método e rota | Entrada / resultado |
| --- | --- |
| POST `/api/v2/engagements` | `artistId`, `idea`, `bodyRegion`, `approximateSizeCm`, `flashId` opcional. Header `Idempotency-Key` UUID. 201 com Location; repetição 200. |
| GET `/api/v2/engagements` | `perspective=client` (padrão) ou `artist`, `limit` 1–100 (padrão 20), `cursor`. Retorna items, total, hasMore, nextCursor. |
| GET `/api/v2/engagements/{id}` | Detalhe autorizado, snapshot do catálogo e apenas a proposta vigente em currentProposal. |
| POST `/api/v2/engagements/{id}/proposals` | Artist participante; `description`, `amount`; header `Idempotency-Key` UUID. 201 com Location, repetição 200. Substitui proposta vigente pendente. |
| GET `/api/v2/engagements/{id}/proposals` | Histórico paginado: limit 1–100 (padrão 20), cursor vinculado à conta/atendimento; items, total, hasMore, nextCursor. |
| GET `/api/v2/engagements/{id}/proposals/{proposalId}` | Versão específica para participante autorizado. |
| PUT `/api/v2/engagements/{id}/proposals/{proposalId}/acceptance` | Cliente participante confirma a versão vigente. Sem corpo; 200. Repetição preserva data e revisão. |

Exemplo de pedido (o tamanho está em centímetros):

```json
{"artistId":"0199ed27-0000-7000-8000-000000000001","idea":"Flor em blackwork","bodyRegion":"Antebraço","approximateSizeCm":12}
```

Descrição e região não vazias, limites de entrada 4000/120 antes do trim; tamanho positivo até 300 com duas casas; proposta não vazia até 4000 e valor positivo até 9999999.99 com duas casas. BRL. IDs não vazios; autoatendimento rejeitado. Campos desconhecidos são rejeitados. Chave idempotente vinculada à conta e operação (proposta inclui o atendimento), sem expiração nesta etapa. Trim e equivalência numérica normalizam o payload. Chave com payload diferente retorna 409. Respostas repetidas refletem a situação persistida atual do mesmo recurso, sem refazer o efeito.

Erros seguem Problem Details, `code`, `traceId` e `errors` quando aplicável: 400 entrada inválida, 401 token ausente/inválido, 403 função/chamador não permitido, 404 recurso inexistente/não participante ou contexto de catálogo inválido, 409 conflito de estado/idempotência, 503 dependência indisponível e 500 falha inesperada sanitizada. Não se confirma pagamento, reserva ou conversa efetivamente criada.

## Verificação

```powershell
dotnet restore InkLink.Platform.slnx --locked-mode
dotnet format InkLink.Platform.slnx --no-restore --verify-no-changes
dotnet build InkLink.Platform.slnx -c Release --no-restore --warnaserror -m:1 --disable-build-servers
dotnet test InkLink.Platform.slnx -c Release --no-build
```

A suíte P04 usa PostgreSQL real (Testcontainers 17.7 por padrão) e hosts HTTP reais em memória de Identity, Catalog, Engagements e Gateway. JWTs RSA são emitidos por um provedor controlado de teste; não é um bypass da assinatura/autorização, nem uma verificação do Keycloak. Os testes P02/P03 continuam cobrindo Keycloak real em containers.

Alternativa sem Docker: `INKLINK_TEST_POSTGRES` pode apontar exclusivamente para um cluster PostgreSQL descartável de teste com administrador capaz de criar bancos e roles. A fixture cria bancos/roles aleatórios e remove somente esses recursos ao terminar. Nunca use conexão de produção. Defina a variável por ambiente seguro, sem imprimir senha, e execute apenas `src/backend/services/Engagements/tests/InkLink.Engagements.IntegrationTests`. Unitários e arquitetura não precisam de banco. Ver resultados efetivamente executados em `docs/verification/p04.md`.

## Transição de versão após revisão de P04

A revisão de conformidade substituiu a coleção ilimitada `proposals` do atendimento por `currentProposal` (objeto ou null). O histórico completo é recuperado por páginas em GET `/api/v2/engagements/{id}/proposals`; o GET de uma versão e o aceite carregam apenas os registros relevantes. A rota antiga `/api/v1/engagements` e seus subrecursos retornam 410 `engagements.api_version_retired` para usuários autenticados, indicando a migração. Não há redirecionamento automático de escritas. Identity/Catalog permanecem em v1. Gateway e BFF encaminham Engagements v2 com a mesma autorização e defesa CSRF.

Não havia entrega publicada ou frontend consumidor de P04 nos registros da entrega. Consumidores locais devem trocar o prefixo por v2, usar `currentProposal` em listagem/detalhe e percorrer `nextCursor` para consultar versões anteriores. UUIDs, propostas, chaves idempotentes e seus efeitos persistidos são preservados: a correção não requer migração ou recriação dos bancos. Depois de timeout, a mesma chave/corpo pode ser repetida no endpoint v2.

OpenAPI descreve campos obrigatórios, IDs não vazios, texto não branco, comprimentos de entrada, valores mínimos/máximos e múltiplos de 0.01. O comprimento é verificado antes do trim, tornando o limite de entrada coerente com maxLength; texto aceito é armazenado sem espaços externos.