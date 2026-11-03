# Execução de Messaging — P05

Veja a [verificação P05](../verification/p05.md) para evidências e limites. O ambiente padrão requer SDK fixado, Docker Compose e Keycloak. Após configurar pré-requisitos de Identity e Catalog:

```powershell
dotnet tool restore --tool-manifest dotnet-tools.json
./src/backend/scripts/Initialize-Local.ps1
./src/backend/scripts/Start-Database.ps1
./src/backend/scripts/Configure-Keycloak.ps1
./src/backend/scripts/Apply-Migrations.ps1
dotnet run --project src/backend/services/Engagements/src/InkLink.Engagements.Api
dotnet run --project src/backend/services/Messaging/src/InkLink.Messaging.Api
```

Inicialize a configuração antes de atualizar dependências. `Start-Database.ps1` cria/configura usuários distintos de produtor e consumidor no RabbitMQ local; o provisionamento administrativo importa os recursos; o produtor somente publica no exchange de Atendimentos e o consumidor lê sua fila e publica apenas no exchange restrito de retry. As credenciais ficam em configurações locais não versionadas. Em produção, forneça `Broker:Uri` distinto para cada serviço com `amqps`, permissões mínimas e certificados válidos. O Compose e os testes reais utilizam as mesmas definições e o mesmo script de permissões. Não altere argumentos de filas existentes sem um plano que preserve as mensagens.

Engagements grava pedido e evento inicial na mesma transação. O processo em background procura atendimentos antigos sem Outbox e os registra em lotes, sem ler bancos de outros serviços. A publicação usa confirmações, e erros mantêm o evento pendente com contador e tipo de falha. Messaging registra Inbox e conversa atomicamente antes de confirmar a entrega. Eventos incompatíveis são rejeitados para a fila `inklink.messaging.invalid.v1`; o desvio e a leitura administrativa dessa fila são verificados com broker real. Reprocessamento exige corrigir a causa, preservar o envelope e confirmar a publicação antes de remover o diagnóstico. Retentativas e eventos antigos não podem reabrir conversa bloqueada.

## Contratos de usuário

Todas as rotas abaixo exigem JWT de usuário e verificam participação em Messaging. IDs externos não substituem autorização. O gateway encaminha `/api/v1/conversations`; o BFF conserva proteção CSRF nas escritas.

| Método e rota | Comportamento |
| --- | --- |
| GET `/api/v1/conversations` | Lista paginada, padrão 20/máximo 100, participantes, atendimento, barreira de escrita e última mensagem. Inclui counterpart (id/displayName) conforme a perspectiva e engagementState oficial. |
| GET `/api/v1/conversations/{id}` | Detalhe somente para participante; inexistente ou terceiro retorna 404. |
| GET `/api/v1/conversations/{id}/messages` | Histórico por sequência monotônica por conversa, padrão 20/máximo 100; cursor é vinculado à conta e à conversa. `nextCursor` pagina enquanto há mais dados; `resumeCursor` permite consultar mensagens futuras mesmo após a última página. |
| GET `/api/v1/conversations/{id}/messages/{messageId}` | Mensagem específica autorizada. |
| POST `/api/v1/conversations/{id}/messages` | JSON `{"body":"Olá"}` e header `Idempotency-Key` UUID. Autor vem da conta autenticada; texto não branco, máximo 4000 caracteres. Repetição com mesmo conteúdo retorna registro original; conteúdo divergente retorna 409. |

Atualização inicial do histórico usa consultas incrementais; nenhum transporte de tempo real é exigido nesta etapa. A operação técnica idempotente de bloqueio é `PUT /api/v1/internal/engagements/{id}/writing-block`, restrita ao cliente de serviço `inklink-engagements` e escopo `messaging.conversation.block`, sem rota no Gateway. O banco serializa bloqueio e envio pela linha da conversa. Um bloqueio confirmado impede novas gravações, mas não representa encerramento de atendimento: o protocolo distribuído será implementado em P07.

A migração `MessageSequence` atribui sequências às mensagens anteriores pela ordem histórica `CreatedAt`/UUID e inicializa o contador local. Novas mensagens usam esse contador sob bloqueio transacional; repetição idempotente não consome outra sequência. Cursores anteriores baseados em data não são compatíveis e retornam 400; clientes precisam reiniciar a leitura do histórico. Esta entrega não inclui frontend ou implantação em produção.

## Diagnóstico e reprocessamento da Outbox

Uma publicação com falha recebe espera progressiva de 2 a 300 segundos. Após dez falhas o evento fica em quarentena; outros eventos seguem elegíveis. Uma indisponibilidade longa do broker também pode levar eventos à quarentena, então sua recuperação exige intervenção operacional. O evento permanece persistido e não é marcado como publicado.

Após corrigir a causa, um operador autorizado pode listar `Id`, `Attempts`, `LastError`, `NextAttemptAt` e `QuarantinedAt` na tabela privada `engagements.outbox`, sem consultar o payload. Para reprocessar um evento específico, execute na transação operacional, substituindo o parâmetro pelo UUID revisado:

```sql
UPDATE engagements.outbox
SET "Attempts" = 0, "LastError" = NULL, "NextAttemptAt" = NULL, "QuarantinedAt" = NULL
WHERE "Id" = :event_id AND "PublishedAt" IS NULL AND "QuarantinedAt" IS NOT NULL;
```

Confirme que uma linha foi atualizada e acompanhe nova tentativa por `Id`; não altere o payload nem reenvie eventos já publicados. Falhas temporárias ainda em espera retomam automaticamente. Os testes exercitam recuperação após indisponibilidade real, redelivery sem ack, repetição de abertura, desvio de contrato incompatível e leitura administrativa do diagnóstico. Não edite o payload de um fato com o mesmo messageId. Após corrigir a causa, um operador autorizado deve republicar o envelope íntegro, aguardar publisher confirm e só então confirmar a entrega antiga de diagnóstico. A Inbox torna a repetição segura; não se promete exactly-once.

## Testes

`InkLink.Messaging.UnitTests` cobre regras de conversa; `InkLink.Messaging.IntegrationTests` usa PostgreSQL real e hosts HTTP. Sem `INKLINK_TEST_POSTGRES`, a fixture usa Testcontainers. `RabbitFlowTests` também cria RabbitMQ real e precisa de Docker. Para reproduzir a validação parcial sem Docker, aponte `INKLINK_TEST_POSTGRES` exclusivamente a um cluster descartável com permissão para criar/remover bancos e roles e exclua `RabbitFlowTests` e `LiveIdentityFlowTests` no filtro. Esse recorte não substitui a suíte completa. Não use banco de produção.


## Nomes, estado e dependências

Messaging:EngagementsAddress aponta para Atendimentos; Messaging:ClientSecret é a credencial de inklink-messaging no Keycloak. Identity aprova esse cliente em Authentication:TechnicalClients e o realm permite identity.profile.read como escopo opcional. Initialize-Local.ps1 e Configure-Keycloak.ps1 configuram isso sem apagar contas.

A participação é validada antes da apresentação. GET /api/v2/engagements/{id} usa o token delegado do participante; o nome do artista vem do snapshot da negociação. Para a perspectiva do artista, o nome atual do cliente vem de GET /api/v1/internal/profiles/{clientId} com Client Credentials de Messaging. Cache somente da consulta, sem persistir tokens/nomes em Conversas, e no máximo quatro enriquecimentos simultâneos. Terceiro recebe 404 antes da consulta de metadados; falha de apresentação retorna 503 messaging.presentation_unavailable.

Agreed significa proposta aceita; writingBlocked indica barreira local. Mesmo true não confirma serviço encerrado: essa coordenação pertence a P07. Histórico é retomado por resumeCursor, inclusive após hasMore false. Lista vazia retorna items vazio e total zero.

Permissões locais, sem imprimir senhas:

~~~powershell
docker compose --env-file .env.local -f src/backend/compose.yaml exec -T rabbitmq rabbitmqctl list_user_permissions inklink_publisher
docker compose --env-file .env.local -f src/backend/compose.yaml exec -T rabbitmq rabbitmqctl list_user_permissions inklink_consumer
~~~

Produtor: configure/read negados; write apenas em inklink.engagements.events.v1. Consumidor: configure negado; write apenas em inklink.messaging.retry.v1; read apenas em inklink.messaging.opened.v1. Diagnóstico/definições exigem operador administrativo, não credenciais dos aplicativos. Não apague filas para repetir uma publicação.


O script de subida prepara somente o proprietário/modo do cookie Erlang existente, sem trocar seu valor. O broker tem hostname estável inklink-broker e a prontidão verifica a aplicação (check_running), além da espera explícita antes de importar definições. O script de Keycloak renova o token administrativo quando necessário; repetir a sincronização não exige apagar o realm. Os problemas e resultados reais estão registrados na verificação P05.


## Recuperação do consumidor

Atualize o provisionamento com Start-Database.ps1 antes de iniciar o host atualizado: a única permissão adicional do consumidor é escrita no exchange inklink.messaging.retry.v1, roteado somente à fila de entrada. Recursos antigos são preservados. A aplicação não declara recursos.

Falhas de processamento são repetidas no máximo dez vezes, com contador persistido no header inklink-processing-attempt. O intervalo entre ciclos é dois segundos. A nova cópia conserva o corpo/messageId e só permite ack da antiga depois de confirmação de publicação persistente. Falha nessa transferência mantém a antiga sem ack, para recuperação quando o broker voltar. Reiniciar o host não reinicia o contador. Contratos inválidos ou orçamento esgotado vão para inklink.messaging.invalid.v1; não use credenciais da aplicação para ler diagnóstico.

O operador deve consumir o diagnóstico com ack manual, revisar eventId, header de tentativa/tipo de falha e logs correlacionados, corrigir a causa e republicar o corpo íntegro no exchange de Atendimentos com credencial administrativa. Remova somente os headers de retry/última falha para iniciar um novo orçamento; preserve traceparent quando válido. Aguarde confirmação persistente e verifique ausência de return antes de confirmar a entrega de diagnóstico. Se falhar, mantenha o diagnóstico sem ack. Não edite payload nem messageId; a Inbox evita reaplicação.

## Rastreamento e métricas de eventos

ActivitySource e Meter: InkLink.Engagements.Events e InkLink.Messaging.Events. O produtor inicia engagement.publish; o consumidor inicia conversation.open com o contexto remoto W3C enviado em traceparent. O envelope novo também guarda traceParent opcional para retomada da Outbox. Eventos anteriores sem esse campo continuam válidos. Identificadores de evento/correlação entram apenas em spans/logs; não são dimensões de métricas.

Instrumentos:
- inklink.engagements.publications: contador por outcome published/retry/quarantined.
- inklink.engagements.publication.duration: segundos por tentativa.
- inklink.engagements.outbox.age: idade em segundos da entrada ao tentar publicar.
- inklink.messaging.deliveries: contador por outcome processed/retry/invalid/quarantined/transport_failure.
- inklink.messaging.processing.duration: segundos por entrega.

Use listeners .NET ou um coletor configurado para esses nomes (por exemplo, dotnet-counters monitor --process-id <PID> --counters InkLink.Messaging.Events). Instrumentação não implica backend de telemetria já implantado: exportação/retenção/alertas de produção continuam na configuração operacional. Sem listener, Activities podem não ser materializadas; o envelope ainda preserva o contexto HTTP de origem quando disponível. Testes usam ActivityListener e MeterListener reais para verificar pai/filho entre produtor/consumidor e registrar outcomes/duração sem dados privados.
