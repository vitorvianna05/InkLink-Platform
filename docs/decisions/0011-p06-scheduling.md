# Decisão 0011 — Agenda e reservas da P06

Tarefas T06.01–T06.06. Políticas iniciais aprovadas pelo responsável em 03/11/2026.

Disponibilidade é cadastrada pelo Artist em intervalos explícitos por data. Fuso IANA por artista, padrão America/Sao_Paulo. Precisão de minutos, início incluído e fim excluído. Nenhuma janela fixa do protótipo é reutilizada. Intervalos da mesma data podem ser adjacentes, mas não sobrepostos; a reserva deve caber integralmente em uma única janela cadastrada.

Somente o artista confirma reservas de seus atendimentos com proposta aceita. Clientes consultam as próprias reservas e a disponibilidade do artista do atendimento. Um atendimento pode ter várias reservas/sessões. Reservas têm início futuro, início/fim com offset explícito consistente com o fuso do artista, mesma data local e término posterior ao início. Não há antecedência comercial, duração comercial mínima ou horizonte máximo impostos. Horários locais ambíguos/inexistentes de transições de fuso são rejeitados nesta entrega, sem conversões silenciosas.

Cancelamento, reagendamento e reservas atravessando meia-noite permanecem para incremento próprio. Todas as reservas desta entrega são Confirmed e ocupam a agenda. O fuso pode ser escolhido antes de cadastrar janelas; não é alterado enquanto houver janelas/reservas. Remover/reduzir disponibilidade não pode invalidar reservas existentes.

Scheduling possui banco, schema, credenciais e migrações próprios. Um registro de calendário por artista serializa mudanças de disponibilidade e confirmação; a restrição PostgreSQL de exclusão GiST/tstzrange por artista garante não sobreposição inclusive contra gravações concorrentes. Consultar disponibilidade não garante reserva.

Confirmação usa operação UUID idempotente por artista; repetição com mesmo atendimento/instantes retorna o registro já gravado, inclusive após início da sessão, antes de consultar dependências novamente. Payload divergente retorna 409. A consulta por operationId permite resolver timeout.

Engagements publica contrato técnico de contexto autenticado para inklink-scheduling com escopo engagements.scheduling-context.read. Scheduling valida as partes e a proposta aceita em Application; não compartilha implementações ou bancos. Nome do cliente vem do contrato técnico de Identity; nome público do artista e projeto vêm do snapshot/contexto de Engagements. São snapshots identificados na reserva para apresentação histórica, não autoridade de permissões.

Outbox de confirmação e reserva são atômicas. Evento scheduling.reservation-confirmed.v1 tem envelope versionado, correlação e contexto W3C opcional. Engagements recebe Inbox e recibo técnico idempotentes; o recibo não altera ciclo de negócio, não habilita chat e não confirma coordenação distribuída. Esse protocolo permanece em P07. Retry/quarentena, métricas, permissões segregadas e provisionamento seguem os princípios de P05.

APIs retornam Problem Details, UUIDv7, paginação por cursor (20/100), autorização local e no-store. Calendários filtram datas locais de cada reserva com intervalo [from,to), limitado a 93 dias por consulta; isso não limita datas reserváveis. Datas e fuso são retornados explicitamente. Disponibilidade de terceiros não expõe nomes/IDs de outras reservas.


## Correções da revisão de conformidade

A API verifica o tipo do token JSON antes de ler timestamps e exige formato ISO com offset explícito. Tokens numéricos, booleanos, objetos, listas e null seguem para validação 400, sem reserva/Outbox.

A leitura do contexto de telemetria ocorre dentro do controle de falhas por evento da Outbox. Metadados incompatíveis consomem tentativas e podem chegar à quarentena; não ficam presos no ciclo externo nem impedem o próximo evento elegível. O payload original permanece preservado para diagnóstico/reprocessamento.

ArtistCalendar protege a disponibilidade de reservas existentes e define DefaultTimeZone. Application coordena sua execução sob o bloqueio/transação e traduz conflito de domínio para o contrato 409. O repositório recebe o fuso como parâmetro e não escolhe política. Nenhuma migração ou alteração do protocolo de P07 é necessária.
