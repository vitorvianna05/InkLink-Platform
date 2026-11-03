# Execução de Scheduling — P06

Políticas em [decisão 0011](../decisions/0011-p06-scheduling.md). Backend em .NET 10, sem frontend ou alteração do protótipo.

## Preparação

Execute na raiz do produto:

~~~powershell
./src/backend/scripts/Initialize-Local.ps1
./src/backend/scripts/Start-Database.ps1
./src/backend/scripts/Configure-Keycloak.ps1
./src/backend/scripts/Apply-Migrations.ps1
dotnet run --project src/backend/services/Identity/src/InkLink.Identity.Api
dotnet run --project src/backend/services/Catalog/src/InkLink.Catalog.Api
dotnet run --project src/backend/services/Engagements/src/InkLink.Engagements.Api
dotnet run --project src/backend/services/Scheduling/src/InkLink.Scheduling.Api
~~~

Execute os hosts em terminais distintos. Scheduling usa porta HTTP local 5196, banco local 54334 e schema scheduling. Configuração fica em User Secrets/variáveis: SchedulingDatabase:ConnectionString, Scheduling:IdentityAddress, Scheduling:EngagementsAddress, Scheduling:ClientSecret e Broker:PublisherUri. Engagements recebe Broker:ReservationConsumerUri. Fora de Development/Testing, integrações HTTP exigem HTTPS e broker exige AMQPS.

Inicialização preserva segredos, usuários e volumes existentes. A identidade de migração é diferente da identidade de execução. A migração inicial instala btree_gist e cria a restrição ex_artist_time com tstzrange de início incluído/fim excluído. Não substitua por checagem de disponibilidade na aplicação.

Keycloak possui cliente técnico inklink-scheduling com somente os escopos opcionais engagements.scheduling-context.read e identity.profile.read. Identity aprova o leitor técnico; Engagements exige ator service, azp exato e escopo correto na rota interna. Contas de usuário não acessam esse contrato. O Gateway expõe apenas a API de usuário.

## Contratos

Todas as rotas de usuário exigem access token e resolução do perfil interno por Identity. Escritas de disponibilidade/confirmação exigem Artist. Cada consulta verifica propriedade/participação no backend. Erros usam Problem Details com code e traceId; respostas privadas usam no-store.

| Método e caminho | Finalidade |
| --- | --- |
| PUT /api/v1/scheduling/availability/{date} | Substitui janelas da data do artista autenticado. |
| GET /api/v1/scheduling/availability/{date} | Consulta disponibilidade própria do artista. |
| GET /api/v1/scheduling/engagements/{id}/availability/{date} | Participantes consultam janelas/intervalos ocupados sem identificar outras reservas/clientes. |
| POST /api/v1/scheduling/reservations | Confirma reserva; exige header Idempotency-Key UUID. |
| GET /api/v1/scheduling/reservations/{id} | Detalhe autorizado para cliente/artista da reserva; terceiro recebe 404. |
| GET /api/v1/scheduling/operations/{operationId} | Artista recupera resultado da operação após timeout. |
| GET /api/v1/scheduling/calendar?from=2030-01-01&to=2030-02-01&perspective=artist | Calendário por datas locais, ordenado por início/UUID, com cursor e limite. |

Datas usam yyyy-MM-dd. O calendário aceita perspectiva artist ou client, intervalo [from,to) até 93 dias por consulta, limite padrão 20/máximo 100. Cada item contém LocalDate e TimeZoneId. Para o cliente, as datas são as datas locais do artista de cada reserva; não se pressupõe um fuso único entre artistas. Cursor é vinculado a usuário, perspectiva e período. Uma página vazia é explícita.

Disponibilidade por data, sem recorrência ou janela fixa:

~~~json
{
  "timeZoneId": "America/Sao_Paulo",
  "windows": [
    { "start": "09:00:00", "end": "12:00:00" },
    { "start": "13:00:00", "end": "18:00:00" }
  ]
}
~~~

timeZoneId omitido usa America/Sao_Paulo. Use IANA; fusos com entradas existentes não podem ser trocados. Janelas devem ter precisão de minutos, não sobrepor e terminar após iniciar; até 100 por data. Lista vazia remove janelas somente quando não invalida reserva existente. Horários locais ambíguos/inexistentes são rejeitados.

Exemplo de confirmação (instantes precisam ser futuros no momento da execução):

~~~json
{
  "engagementId": "0199ed27-0000-7000-8000-000000000004",
  "start": "2030-01-15T10:00:00-03:00",
  "end": "2030-01-15T11:00:00-03:00"
}
~~~

O offset é obrigatório e precisa corresponder ao fuso do artista. Início/fim na mesma data local, minutos inteiros, término posterior e início futuro. A reserva cabe integralmente em uma janela. Proposta aceita é obrigatória, mas o aceite não reserva automaticamente. Cliente/artista/valor/projeto não são aceitos do solicitante: vêm dos contratos oficiais e identidade autenticada.

201 cria; 200 repete mesma operação sem duplicar; 409 indica conflito de horário, chave reutilizada com outro payload, proposta não aceita ou redução incompatível de disponibilidade; 400 indica dados inválidos; 503 indica dependência indisponível. Repetição idempotente já gravada precede revalidação de tempo/dependência; não muda a reserva original. O mesmo atendimento pode ter várias sessões adjacentes ou separadas.

ReferenceAmount/Currency guardam referência da proposta aceita; não são cobrança ou alocação financeira por sessão. Nomes/projeto são snapshots identificados e históricos. Consulta de disponibilidade retorna GuaranteesReservation=false e não confirma ocupação. Cancelamento/reagendamento, passagem por meia-noite e efeitos distribuídos ficam fora da P06.

## Eventos e recuperação

Reserva e Outbox são gravadas na mesma transação. Produtor publica scheduling.reservation-confirmed.v1 no exchange durável inklink.scheduling.events.v1, com payload imutável, persistência, mandatory e publisher confirm. Falhas da Outbox têm espera progressiva de 2 a 300 segundos; após dez tentativas, quarentena durável. Reprocessamento individual por operador:

~~~sql
UPDATE scheduling.outbox
SET "Attempts"=0, "LastError"=NULL, "NextAttemptAt"=NULL, "QuarantinedAt"=NULL
WHERE "Id"=:event_id AND "PublishedAt" IS NULL AND "QuarantinedAt" IS NOT NULL;
~~~

Corrija a causa e confira uma linha atualizada; não edite payload/ID nem reprocesse publicados. Publicação confirmada não significa coordenação de negócio concluída.

Engagements consome inklink.engagements.reservations.v1, com Inbox e recibo técnico na mesma transação, validando participantes/fingerprint. Duplicações por evento e reserva não duplicam recibos nem mudam o ciclo de vida. Erros de contrato/participantes seguem para inklink.engagements.reservations.invalid.v1. Falhas de processamento usam orçamento persistido em header e até dez tentativas confirmadas pelo exchange inklink.engagements.reservations.retry.v1. Transferência sem confirmação mantém original sem ack.

Diagnóstico exige operador: leia com ack manual, corrija causa, preserve envelope/ID, reinicie apenas metadados de tentativa e publique persistentemente com mandatory/confirm. Só então confirme a entrega de diagnóstico. A aplicação não lê diagnóstico nem declara recursos.

Permissões: inklink_scheduling_publisher somente escreve no exchange de Scheduling; inklink_reservation_consumer somente lê sua fila e escreve no exchange restrito de retry. Ambos sem configure. Configure-broker.sh/definições são os mesmos no Compose e nos testes. Contas P06 são opcionais em fixtures P05 que não configuram suas variáveis.

Telemetria: ActivitySource/Meter InkLink.Scheduling.Events para publicação (contadores de outcome, duração e idade da Outbox); InkLink.Engagements.Reservations para recebimento (outcome/duração). Contexto W3C continua no envelope/header sem usar trace como chave idempotente ou registrar conteúdo privado. Exportador/alertas de produção permanecem na entrega operacional.

## Verificação

Testes de domínio, arquitetura, APIs, migrações, isolamento de credenciais e concorrência usam a solução oficial. Testcontainers cria PostgreSQL/RabbitMQ; o fluxo completo usa Keycloak HTTPS com certificado temporário fixado somente nos clientes de teste. Bancos em memória não comprovam exclusão GiST.

~~~powershell
dotnet restore InkLink.Platform.slnx --locked-mode
dotnet format InkLink.Platform.slnx --no-restore --verify-no-changes
dotnet build InkLink.Platform.slnx --configuration Release --no-restore --warnaserror
dotnet test InkLink.Platform.slnx --configuration Release --no-build
~~~

Resultados e limites serão registrados em [verificação P06](../verification/p06.md). P07 continua responsável pela coordenação distribuída, compensação e nova conversa após novo agendamento.


## Ajustes de conformidade da P06

Timestamp JSON deve ser string ISO com offset explícito; tipos JSON incompatíveis retornam 400 Problem Details. O domínio protege janelas que cobrem reservas existentes e concentra o fuso padrão. O retorno 409 de redução incompatível permanece.

Falhas ao interpretar traceParent da Outbox entram no mesmo orçamento de tentativas da publicação. Após corrigir o metadado incompatível por procedimento operacional revisado, o reprocessamento continua sujeito à preservação da identidade do fato e à verificação da Inbox; não altere livremente payload de evento já entregue. A quarentena mantém o registro original e libera outros elegíveis.
