# Execução experimental de Messaging — P05 em andamento

P05 ainda não recebeu aceite: veja [verificação parcial](../verification/p05.md) para pendências e limitações. O ambiente padrão requer SDK fixado, Docker Compose e Keycloak. Após configurar pré-requisitos de Identity e Catalog:

```powershell
dotnet tool restore --tool-manifest dotnet-tools.json
./src/backend/scripts/Initialize-Local.ps1
./src/backend/scripts/Start-Database.ps1
./src/backend/scripts/Configure-Keycloak.ps1
./src/backend/scripts/Apply-Migrations.ps1
dotnet run --project src/backend/services/Engagements/src/InkLink.Engagements.Api
dotnet run --project src/backend/services/Messaging/src/InkLink.Messaging.Api
```

Inicialize a configuração antes de atualizar dependências. `Start-Database.ps1` cria/configura usuários distintos de produtor e consumidor no RabbitMQ local; o primeiro declara a fila e publica, o segundo apenas consome. As credenciais ficam em configurações locais não versionadas. Em produção, forneça `Broker:Uri` distinto para cada serviço com `amqps`, permissões mínimas e certificados válidos. A configuração local e seu provisionamento de ACL ainda não foram validados com RabbitMQ real.

Engagements grava pedido e evento inicial na mesma transação. O processo em background procura atendimentos antigos sem Outbox e os registra em lotes, sem ler bancos de outros serviços. A publicação usa confirmações, e erros mantêm o evento pendente com contador e tipo de falha. Messaging registra Inbox e conversa atomicamente antes de confirmar a entrega. Eventos incompatíveis são rejeitados para a fila `inklink.messaging.invalid.v1`; a operação de inspeção/reprocessamento dessa fila ainda precisa ser exercitada no ambiente com broker real. Retentativas e eventos antigos não podem reabrir conversa bloqueada.

## Contratos de usuário

Todas as rotas abaixo exigem JWT de usuário e verificam participação em Messaging. IDs externos não substituem autorização. O gateway encaminha `/api/v1/conversations`; o BFF conserva proteção CSRF nas escritas.

| Método e rota | Comportamento |
| --- | --- |
| GET `/api/v1/conversations` | Lista paginada, padrão 20/máximo 100, participantes, atendimento, barreira de escrita e última mensagem. Por enquanto, participantes são IDs; nomes e estado de negócio ainda precisam ser integrados. |
| GET `/api/v1/conversations/{id}` | Detalhe somente para participante; inexistente ou terceiro retorna 404. |
| GET `/api/v1/conversations/{id}/messages` | Histórico por sequência monotônica por conversa, padrão 20/máximo 100; cursor é vinculado à conta e à conversa. `nextCursor` pagina enquanto há mais dados; `resumeCursor` permite consultar mensagens futuras mesmo após a última página. |
| GET `/api/v1/conversations/{id}/messages/{messageId}` | Mensagem específica autorizada. |
| POST `/api/v1/conversations/{id}/messages` | JSON `{"body":"Olá"}` e header `Idempotency-Key` UUID. Autor vem da conta autenticada; texto não branco, máximo 4000 caracteres. Repetição com mesmo conteúdo retorna registro original; conteúdo divergente retorna 409. |

Atualização inicial do histórico usa consultas incrementais; nenhum transporte de tempo real é exigido nesta etapa. A operação técnica idempotente de bloqueio é `PUT /api/v1/internal/engagements/{id}/writing-block`, restrita ao cliente de serviço `inklink-engagements` e escopo `messaging.conversation.block`, sem rota no Gateway. O banco serializa bloqueio e envio pela linha da conversa. Um bloqueio confirmado impede novas gravações, mas não representa encerramento de atendimento: o protocolo distribuído será implementado em P07.

A migração `MessageSequence` atribui sequências às mensagens anteriores pela ordem histórica `CreatedAt`/UUID e inicializa o contador local. Novas mensagens usam esse contador sob bloqueio transacional; repetição idempotente não consome outra sequência. Cursores anteriores baseados em data não são compatíveis e retornam 400; clientes precisam reiniciar a leitura do histórico. A P05 ainda não foi publicada.

## Diagnóstico e reprocessamento da Outbox

Uma publicação com falha recebe espera progressiva de 2 a 300 segundos. Após dez falhas o evento fica em quarentena; outros eventos seguem elegíveis. Uma indisponibilidade longa do broker também pode levar eventos à quarentena, então sua recuperação exige intervenção operacional. O evento permanece persistido e não é marcado como publicado.

Após corrigir a causa, um operador autorizado pode listar `Id`, `Attempts`, `LastError`, `NextAttemptAt` e `QuarantinedAt` na tabela privada `engagements.outbox`, sem consultar o payload. Para reprocessar um evento específico, execute na transação operacional, substituindo o parâmetro pelo UUID revisado:

```sql
UPDATE engagements.outbox
SET "Attempts" = 0, "LastError" = NULL, "NextAttemptAt" = NULL, "QuarantinedAt" = NULL
WHERE "Id" = :event_id AND "PublishedAt" IS NULL AND "QuarantinedAt" IS NOT NULL;
```

Confirme que uma linha foi atualizada e acompanhe nova tentativa por `Id`; não altere o payload nem reenvie eventos já publicados. Falhas temporárias ainda em espera retomam automaticamente. A inspeção/reprocessamento e a recuperação com RabbitMQ real seguem pendentes de validação operacional.

## Testes

`InkLink.Messaging.UnitTests` cobre regras de conversa; `InkLink.Messaging.IntegrationTests` usa PostgreSQL real e hosts HTTP. Sem `INKLINK_TEST_POSTGRES`, a fixture usa Testcontainers. `RabbitFlowTests` também cria RabbitMQ real e precisa de Docker. Para reproduzir a validação parcial sem Docker, aponte `INKLINK_TEST_POSTGRES` exclusivamente a um cluster descartável com permissão para criar/remover bancos e roles e exclua `RabbitFlowTests` no filtro. Não use banco de produção.
