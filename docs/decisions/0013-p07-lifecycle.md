# Decisão 0013 — Ciclo integrado de atendimento

Tarefas T07.01–T07.06. Políticas aprovadas pelo responsável em 03/11/2026.

## Políticas

Cliente ou artista participante pode solicitar encerramento com confirmação explícita, sem aprovação da outra parte. Reservas com término futuro impedem o encerramento; nenhuma reserva é cancelada automaticamente. Encerrar chat não conclui sessão financeira nem registra receita.

Após encerramento, novo pedido/proposta pode ser registrado, mas sua conversa fica bloqueada até novo agendamento confirmado. Conversas são separadas por atendimento. Falha de habilitação preserva a reserva e informa pendência recuperável.

## Coordenação

Engagements persiste operação Close e estado Closing na mesma transação. O processo chama Scheduling para congelar novas reservas do atendimento: Agenda serializa essa barreira pelo mesmo calendário usado na confirmação e recusa se existir reserva com término futuro. Essa verificação usa dados oficiais de Agenda, nunca recibos eventualmente consistentes.

Se houver reserva futura, a operação fica Rejected e o atendimento recupera seu estado anterior. O participante pode solicitar novamente depois. Após congelamento confirmado, o processo bloqueia definitivamente Messaging e só então grava Closed. Timeout mantém Closing/Pending. Falha após bloqueio não desbloqueia o chat; repetição recupera o resultado.

Messaging aceita comando técnico de lifecycle apenas de inklink-engagements com messaging.conversation.lifecycle. Pode criar conversa bloqueada antes da chegada do evento inicial, preservando uma barreira definitiva contra eventos atrasados. Closed local domina ativação: uma ativação antiga não reabre esse atendimento.

Scheduling aceita congelamento apenas de inklink-engagements com scheduling.engagement.freeze. O congelamento permanece após sucesso e rejeita confirmações novas, inclusive as que leram contexto Agreed antes da solicitação de encerramento. Repetição de reserva já confirmada continua retornando o registro original.

Criação de novo pedido e solicitação de encerramento usam bloqueio local por par cliente/artista em Engagements. Um pedido criado quando já existe Closed/Closing recebe RequiresReservation. A Outbox inicial carrega writingAllowed=false nesse caso; o consumidor aceita abertura bloqueada. Pedido/proposta não remove essa restrição.

O recibo de reserva e a operação Activate são registrados atomicamente em Engagements quando o atendimento requer agendamento. A operação habilita a conversa somente do novo atendimento. Enquanto ele estiver Closing, a ativação permanece Pending com espera, sem consumir tentativas de falha. Se o encerramento for rejeitado e o atendimento voltar a Agreed, ela continua. Somente Closed rejeita definitivamente a ativação. O histórico anterior não é modificado.

## Recuperação e contrato

Operações possuem identificador, tipo, situação, tentativas, erro estável e data de atualização. Worker recupera Pending em lotes limitados, usando bloqueios por atendimento. Falhas têm espera progressiva e quarentena após dez tentativas. Participantes consultam a situação e podem solicitar retomada de operações em quarentena; novas chamadas não desfazem barreiras definitivas.

APIs públicas de Engagements v2:
- POST /api/v2/engagements/{id}/closure com confirmed=true: 202 com Location enquanto pendente; 200 quando já concluída.
- GET /api/v2/engagements/{id}/lifecycle: situação autorizada, RequiresReservation e operações.
- POST /api/v2/engagements/{id}/lifecycle/retry: retoma operações em quarentena, sem reabrir atendimento encerrado.

Operações técnicas não são expostas no catálogo Swagger do Gateway. Não há novas dependências de implementação entre serviços, transação global ou acesso a banco alheio. APIs v2 recebem adições compatíveis; estados Closing/Closed são a evolução prevista do ciclo e devem ser tratados pelos consumidores.

Cancelamento/reagendamento continua fora do incremento. Não se usa compensação que libere uma reserva confirmada para resolver indisponibilidade de Messaging: mantém-se a reserva e recupera-se a habilitação.

## Correções da revisão de conformidade

Application prepara a política de novo pedido a partir do snapshot de estados do par e coordena criação/retomada de Close e criação de Activate. Repositórios oferecem snapshots sob bloqueio, transações e persistência das alterações devolvidas pelos casos de uso; não escolhem transições ou criam intenções de negócio autonomamente.

LifecycleCorrelationAndRecovery acrescenta TraceParent/CorrelationId opcionais às operações. Application captura o contexto da solicitação ou do evento de reserva; o worker continua a partir do pai W3C persistido e registra a correlação no escopo de log. Repetição conserva o contexto de origem. Operações anteriores, sem metadados, continuam processáveis.

A mesma migração recupera apenas Activate/Rejected com erro engagements.closing em atendimentos que exigem reserva, já receberam sua confirmação e permanecem Agreed/Closing. Atendimentos Closed não são reativados. A espera lógica por Closing é distinta de falha de dependência e não leva à quarentena.
