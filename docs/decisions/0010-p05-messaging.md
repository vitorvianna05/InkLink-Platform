# Decisão 0010 — Mensageria e conversas de P05

**Tarefas:** T05.01–T05.08. **Situação:** implementada e verificada no recorte P05; [evidências](../verification/p05.md).

RabbitMQ é o broker inicial, com fila durável própria de Messaging, mensagens persistentes, confirmação de publicação e confirmação manual após transação do consumidor. A escolha atende roteamento explícito e entrega recuperável; PostgreSQL continua a fonte oficial dos serviços. Não se pressupõe entrega exatamente uma vez nem ordem global. O transporte em tempo real não é exigido: clientes consultam histórico incremental por cursor; uma futura interface pode repetir consultas.

O envelope v1 contém `messageId`, `type`, `schemaVersion`, `occurredAt`, `correlationId`, `causationId` e dados de `engagementId`, `clientId`, `artistAccountId`, `revision` e `writingAllowed`. O produtor registra evento na mesma transação da criação de atendimento. Instâncias da Outbox selecionam registros com bloqueio no banco, publicam e só marcam entregue após confirmação do broker. Publicação repetida é esperada quando a confirmação ou a transação local falha. O consumidor registra mensagem e efeito na mesma transação e confirma o broker somente após commit. Contratos desconhecidos ou incompatíveis devem ser isolados para diagnóstico e reprocessamento controlado, sem repetição infinita.

Atendimentos existentes em P04 necessitam backfill transacional idempotente. Um evento inicial atrasado nunca reabre conversa bloqueada nem substitui participantes; evento duplicado não altera mensagem ou conversa. Bloqueio local é idempotente e serializado pela mesma linha usada no envio. A confirmação do encerramento distribuído e a autorização de quem encerra pertencem a P07; a operação técnica de bloqueio em P05 não equivale ao encerramento do atendimento.

Após revisão, mensagens recebem sequência monotônica por conversa, alocada sob bloqueio da conversa na mesma transação da gravação. O timestamp permanece informativo e não determina o cursor incremental. Mensagens anteriores recebem sequências por data e UUID na migração; cursores antigos por data são rejeitados e devem ser reiniciados. Application decide idempotência, validação e transição; Infrastructure mantém bloqueio, transação e persistência.

Falhas da Outbox têm espera progressiva e, após dez tentativas, quarentena durável por evento. Outros eventos continuam elegíveis. A operação de diagnóstico e reprocessamento individual está em `docs/development/messaging.md`. Até falhas de infraestrutura prolongadas podem exigir reprocessamento manual após a quarentena; isso não representa publicação bem-sucedida.


## Complemento da finalização — 03/11/2026

O provisionamento de filas, exchange e binding pertence à operação administrativa. broker-definitions.json, sem segredos, é importado por configure-broker.sh. O produtor recebe somente escrita no exchange direto durável inklink.engagements.events.v1; routing key engagement.negotiation-opened.v1. Não declara recursos nem consome. O consumidor lê somente inklink.messaging.opened.v1 e publica somente no exchange restrito inklink.messaging.retry.v1 para retentativas confirmadas. A fila de diagnóstico conserva o dead-letter routing já definido, preservando compatibilidade com recursos anteriores. O script é idempotente e não apaga filas, mensagens ou usuários existentes. AMQP sem TLS só é permitido em Development/Testing; produção exige AMQPS e certificados válidos.

O publicador recuperável fica em Infrastructure; o host apenas o registra por extensão. Em Messaging, Application coordena envio, leitura autorizada e bloqueio; a persistência oferece transação e bloqueio de linha para aplicar as transições. Nenhuma camada de produto referencia a implementação de outro serviço, fronteira verificada por testes arquiteturais.

### Apresentação da conversa

ConversationView acrescenta counterpart (id/displayName) e engagementState. Primeiro é validada a participação local. Depois, a API v2 de Engagements é consultada com o token delegado do solicitante, confirmando atendimento e participantes. O nome público do artista vem do snapshot imutável da negociação; o nome atual do cliente vem de Identity pelo contrato identity.profile.read, usando Client Credentials de inklink-messaging. Esse cliente possui somente esse escopo opcional, sem impersonar usuários ou bloquear conversas.

A composição usa no máximo quatro consultas simultâneas por página e cache de token/nome somente dentro da consulta, sem cache global de autorização. Não há acesso cruzado a bancos. Campos ausentes, participantes divergentes, contrato inesperado ou dependência indisponível retornam 503 messaging.presentation_unavailable; não se inventam nomes nem encerramentos. Histórico/envio não dependem dessa consulta de apresentação: usam autorização e barreira locais.

engagementState é a situação oficial de Engagements (Negotiating/Agreed nesta entrega). writingBlocked é exclusivamente a barreira durável local; true não afirma encerramento de negócio. O protocolo distribuído e eventuais novos estados continuam em P07. A atualização permanece por consultas incrementais com resumeCursor e sequência monotônica por conversa, sem SignalR/WebSocket. Não há abertura arbitrária de conversa por rota de usuário: ela decorre do fato persistido em Atendimentos.

Referências operacionais: [RabbitMQ: controle de acesso](https://www.rabbitmq.com/docs/access-control) e [confirmações](https://www.rabbitmq.com/docs/confirms). Versões e execução real estão na verificação P05.


## Correções da revisão de conformidade — 03/11/2026

OpenConversation em Application valida o envelope, cria o agregado e decide compatibilidade de fingerprint/participantes. O consumidor é adaptador de transporte. ApplyOpeningAsync oferece atomicidade Inbox/conversa e executa a validação do caso de uso dentro da transação; não escolhe transições de domínio.

Falhas de processamento têm limite de dez tentativas. O header AMQP inteiro inklink-processing-attempt preserva o orçamento entre instâncias/reinícios. Em falha recuperável antes do limite, o consumidor publica cópia persistente e íntegra no exchange durável inklink.messaging.retry.v1, ligado somente à própria fila, espera publisher confirm/ausência de return e então confirma a entrega original. Há intervalo de dois segundos entre ciclos. Duplicação após falha de confirmação continua segura pela Inbox. Falha de transporte ao transferir mantém a original sem ack; indisponibilidade de transporte não equivale a uma tentativa de negócio concluída. Ao esgotar o limite, ou receber contrato inválido, rejeita sem requeue para o diagnóstico existente. Não há descarte voluntário da mensagem nem reset automático do orçamento. Esta estratégia não promete exactly-once.

O provisionamento adiciona exchange/binding e somente essa permissão de escrita ao consumidor, sem mudar argumentos ou apagar filas existentes. O produtor não pode escrever no exchange de retry; o consumidor não pode publicar fatos em nome de Atendimentos. Reprocessamento exige operador, correção da causa, envelope/messageId intactos e reset apenas do header de tentativa após confirmação da nova publicação.

O envelope v1 aceita traceParent W3C opcional, capturado na transação da Outbox. Eventos antigos permanecem aceitos e mantêm a correlação existente; ausência desse campo não altera o fingerprint anteriormente calculado. Publicação propaga traceparent AMQP e consumo inicia ActivityKind.Consumer com esse pai, continuando a cadeia nas tentativas. Conteúdo de chat e segredos não entram em tags. ActivitySources/Meters são InkLink.Engagements.Events e InkLink.Messaging.Events; métricas usam somente outcomes de cardinalidade limitada. A instrumentação é consumível por listeners .NET; configuração de coletor/exportador de produção continua na entrega operacional.
