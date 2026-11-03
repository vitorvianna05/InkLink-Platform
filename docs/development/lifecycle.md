# Ciclo integrado — P07

Aplicar migrações LifecycleCoordination (Engagements), PermanentClosure (Messaging) e EngagementFreeze (Scheduling) com identidades de migração próprias. Executar Initialize-Local.ps1 e Configure-Keycloak.ps1 para configurar os novos destinos e escopos, preservando credenciais. Iniciar Identity, Catalog, Engagements, Messaging, Scheduling e Gateway com PostgreSQL, Keycloak e RabbitMQ.

Engagements requer Engagements:MessagingAddress e Engagements:SchedulingAddress além de suas configurações anteriores. Em Development o worker é habilitado quando os destinos são configurados; Testing exige Lifecycle:EnableWorkers=true para execução automática. HTTPS é obrigatório fora de Development/Testing. Chamadas técnicas usam timeout de dez segundos, sem redirects/cookies e sem tokens de usuário.

Atualizar primeiro os schemas e os serviços Scheduling/Messaging para reconhecerem as barreiras; depois atualizar os escritores de Engagements e habilitar a coordenação. Não misturar escritores antigos de Engagements após habilitar a política de retorno da P07. A estratégia de implantação de produção continua na etapa operacional.

Readiness de Engagements e Scheduling verifica também as novas tabelas; Messaging verifica a estrutura da conversa com a nova coluna. Ausência de migração não deve ser tratada como ambiente pronto.

## Roteiro pelo Gateway / Swagger

1. Com conta Artist, publicar catálogo; com Client distinto, criar pedido. Confirmar abertura da conversa pela Outbox.
2. Enviar mensagem pelas duas identidades e consultar histórico.
3. Apresentar/aceitar proposta e confirmar reserva futura. Solicitar closure com confirmed=true; consultar lifecycle até Rejected/future_reservations. A reserva e o chat permanecem.
4. Em atendimento sem sessão futura, solicitar closure e acompanhar lifecycle. Somente Closed/Completed confirma encerramento. Estado Closing é pendência.
5. Consultar histórico preservado; envio posterior deve retornar 409.
6. Criar novo pedido para o mesmo artista, apresentar/aceitar proposta. Novo chat existe bloqueado. Confirmar novo agendamento: aguardar Activate/Completed para enviar. O chat anterior continua bloqueado.

RequiresReservation registra a política do atendimento; não afirma prontidão. Para retorno após encerramento, aguardar Activate/Completed. Antes de receber o fato de reserva, a lista de operações pode estar vazia mesmo com uma reserva já confirmada em Agenda; verificar a Outbox correspondente.

A ausência de uma conversa na primeira consulta pode refletir Outbox pendente. Não abrir conversa por rota pública arbitrária. Operações de lifecycle exigem participação; terceiro recebe 404.

## Diagnóstico e retomada

GET /api/v2/engagements/{id}/lifecycle retorna operações Close/Activate, status, attempts, error e updatedAt. Pending possui repetição progressiva; Quarantined exige corrigir dependência/permissões e POST /api/v2/engagements/{id}/lifecycle/retry por participante. A tentativa preserva o contexto e não apaga histórico. Rejected/future_reservations exige aguardar o fim das sessões e solicitar closure novamente.

Perda de resposta após congelamento ou bloqueio deve ser recuperada repetindo o processo, sem desfazer a barreira. Não alterar manualmente Closed/WritingBlocked nem cancelar reserva para contornar pendência. Operação concluída não é reaplicada pelo worker.

Encerramento não é registro de sessão concluída nem lançamento de receita. Esses contratos permanecem em P08.

## Revisão P07: ativação pendente e rastreamento

Aplicar também LifecycleCorrelationAndRecovery em Engagements com a identidade de migração. Ela acrescenta metadados opcionais e recupera o caso antigo de ativação rejeitada por Closing, sem reabrir atendimentos Closed.

Activate/Pending com engagements.awaiting_closure aguarda o encerramento: não representa falha de transporte e não consome tentativas. Se Close for rejeitada pela sessão futura, a ativação prossegue; se chegar a Closed, é rejeitada definitivamente.

O contexto de solicitação ou evento é persistido na operação. Retomadas usam o mesmo pai W3C e correlationId, enquanto operationId continua identificando idempotência. Registros antigos sem contexto iniciam um novo trace sem impedir o processamento.
