# Decisão 0010 — Mensageria e conversas de P05

**Tarefas:** T05.01–T05.08. **Situação:** implementação em andamento.

RabbitMQ é o broker inicial, com fila durável própria de Messaging, mensagens persistentes, confirmação de publicação e confirmação manual após transação do consumidor. A escolha atende roteamento explícito e entrega recuperável; PostgreSQL continua a fonte oficial dos serviços. Não se pressupõe entrega exatamente uma vez nem ordem global. O transporte em tempo real não é exigido: clientes consultam histórico incremental por cursor; uma futura interface pode repetir consultas.

O envelope v1 contém `messageId`, `type`, `schemaVersion`, `occurredAt`, `correlationId`, `causationId` e dados de `engagementId`, `clientId`, `artistAccountId`, `revision` e `writingAllowed`. O produtor registra evento na mesma transação da criação de atendimento. Instâncias da Outbox selecionam registros com bloqueio no banco, publicam e só marcam entregue após confirmação do broker. Publicação repetida é esperada quando a confirmação ou a transação local falha. O consumidor registra mensagem e efeito na mesma transação e confirma o broker somente após commit. Contratos desconhecidos ou incompatíveis devem ser isolados para diagnóstico e reprocessamento controlado, sem repetição infinita.

Atendimentos existentes em P04 necessitam backfill transacional idempotente. Um evento inicial atrasado nunca reabre conversa bloqueada nem substitui participantes; evento duplicado não altera mensagem ou conversa. Bloqueio local é idempotente e serializado pela mesma linha usada no envio. A confirmação do encerramento distribuído e a autorização de quem encerra pertencem a P07; a operação técnica de bloqueio em P05 não equivale ao encerramento do atendimento.

Após revisão, mensagens recebem sequência monotônica por conversa, alocada sob bloqueio da conversa na mesma transação da gravação. O timestamp permanece informativo e não determina o cursor incremental. Mensagens anteriores recebem sequências por data e UUID na migração; cursores antigos por data são rejeitados e devem ser reiniciados. Application decide idempotência, validação e transição; Infrastructure mantém bloqueio, transação e persistência.

Falhas da Outbox têm espera progressiva e, após dez tentativas, quarentena durável por evento. Outros eventos continuam elegíveis. A operação de diagnóstico e reprocessamento individual está em `docs/development/messaging.md`. Até falhas de infraestrutura prolongadas podem exigir reprocessamento manual após a quarentena; isso não representa publicação bem-sucedida.
