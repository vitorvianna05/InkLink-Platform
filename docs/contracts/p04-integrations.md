# Contratos de integração de Atendimentos — P04

## Fronteiras e identidade

`Engagements` é a origem do pedido, participantes, propostas, aceite e ciclo de atendimento. `Messaging` controla conversa, histórico e escrita. `Scheduling` controla disponibilidade e reservas. As referências são UUIDs internos; `artistId` público do Catalog é diferente de `artistAccountId`. Não há FK, joins ou acesso cruzado a bancos.

A autenticação do usuário em Engagements usa access token JWT e validação delegada de identidade em Identity. Operações futuras entre serviços precisam de Client Credentials com cliente/escopo próprios, sem reutilizar credenciais de navegador. O escopo `catalog.engagement-context.read` autoriza somente Engagements a consultar o snapshot técnico de Catalog. `identity.profile.read` não concede essa permissão. Messaging tornou-se host em P05; inklink-messaging possui somente identity.profile.read para nomes e o bloqueio aceita inklink-engagements com messaging.conversation.block. Scheduling será configurado em P06. Não há rotas provisórias sem autorização.

## Consulta de contexto em Catalog

`GET /api/v1/internal/artists/{artistId}/engagement-context?flashId={flashId}`

JWT com `inklink_actor=service`, `azp=inklink-engagements`, audiência `inklink-api`, emissor aprovado, assinatura e validade verificadas, escopo `catalog.engagement-context.read`. Rota apenas no host Catalog, sem encaminhamento pelo Gateway/BFF. Não altera dados. Artista inexistente ou flash de outro artista/tipo: 404; token ausente/inválido: 401; chamador/escopo incorreto: 403; dependência indisponível: 503. Engagements traduz falhas técnicas da chamada/token para 503 e não grava o pedido.

Resposta 200:

```json
{
  "artistId": "0199ed27-0000-7000-8000-000000000001",
  "artistAccountId": "0199ed27-0000-7000-8000-000000000002",
  "name": "Artista", "studio": "Estúdio", "city": "São Paulo", "startingPrice": 300,
  "flash": {
    "id": "0199ed27-0000-7000-8000-000000000003", "title": "Flor",
    "imageUrl": "https://images.example.test/flower.png", "alternativeText": "Flor preta",
    "style": "Blackwork", "referencePrice": 500
  }
}
```

Sem flash solicitado, `flash` é null. A leitura tem snapshot transacional local em Catalog; a cópia em Engagements é imutável. Não representa confirmação de agenda ou preço negociado.

## Criação e habilitação inicial de conversa — contrato v1 para P05

Fato proposto: `engagement.negotiation-opened.v1`. Dados mínimos:

```json
{
  "schemaVersion": 1,
  "engagementId": "0199ed27-0000-7000-8000-000000000004",
  "clientId": "0199ed27-0000-7000-8000-000000000005",
  "artistAccountId": "0199ed27-0000-7000-8000-000000000002",
  "revision": 1,
  "occurredAt": "2026-11-02T15:00:00Z",
  "writingAllowed": true
}
```

O pedido persistido autoriza a negociação antes de qualquer proposta/aceite. `writingAllowed` descreve a intenção inicial, não comprova que Messaging já a aplicou. A interface futura deve distinguir conversa pendente de conversa disponível. A entrega P04 não publicava esse fato nem fabricava ID de conversa. P05 acrescenta Outbox transacional e consumo por Messaging, que gera seu UUID na transação local bem-sucedida. Confirmação de publicação não confirma consumo.

P05 definiu RabbitMQ e envelope v1 com messageId, correlationId/causationId, produtor e versão, conforme decisão 0010. Publicação exige Outbox na mesma transação local do fato; ack não equivale a processamento remoto. Consumo deve validar participantes distintos e IDs não vazios, deduplicar evento e criação por atendimento na mesma transação local, impedir mudanças incompatíveis de participantes e não reabrir um atendimento encerrado por evento atrasado. Entrega duplicada tem o mesmo efeito; erro de versão/contrato é diagnosticável, sem repetição infinita. Estado inicial dos atendimentos criados em P04 deverá ser reconciliado a partir da fonte persistida, sem perder os anteriores à introdução da Outbox. Revisão antiga não altera um estado mais recente. Deduplicação não usa traceId como chave.

Encerramento e novo atendimento exigem o protocolo durável de P07: nenhuma confirmação definitiva antes do bloqueio efetivo da escrita em Messaging. Aceite da proposta não bloqueia nem reabre chat.

## Referência de negociação para Agenda — contrato v1 para P06

Engagements já permite às partes consultar `id`, `clientId`, `artistAccountId`, `state`, `revision`, `currentProposalId` e a proposta vigente com `amount`, `currency=BRL`, `state` e `acceptedAt`. Scheduling obterá apenas os dados necessários por contrato técnico autenticado, a ser exposto com seu serviço; não usará token técnico para chamar uma rota de usuário ou ler o banco de Engagements.

A confirmação de reserva deverá receber `operationId`, `engagementId`, `clientId`, `artistAccountId`, início e término com fuso explícito. Agenda valida autorização, vínculo, disponibilidade e conflito no seu banco, usando chave idempotente; payload diferente com a mesma chave retorna conflito. O protocolo de consulta do resultado permite resolver timeout sem duplicar a reserva. A confirmação deverá carregar `reservationId`, participantes, atendimento, intervalo e revisão, com Outbox. Repetição de evento não cria outro atendimento nem habilita contexto antigo.

P04 não determina que aceite seja requisito suficiente ou obrigatório para reservar: disponibilidade, regras temporais e transições serão decididas em T06.01/T07.04. Também não cria reserva, recebimento ou compensação. Estados pendentes e indisponibilidade nunca são apresentados como confirmação. Nenhum contrato autoriza compartilhar entidades internas entre serviços.


Em P05, a apresentação consulta GET /api/v2/engagements/{id} com token delegado do participante e valida os IDs antes de exibir estado/nome do artista. Não se usa token técnico para uma rota de usuário nem se compartilham entidades internas. O cliente técnico de Messaging consulta separadamente apenas o nome do cliente em Identity.


Na revisão P05, o envelope v1 recebe traceParent W3C opcional e compatível com eventos anteriores. O header AMQP traceparent continua a cadeia de publicação/consumo; inklink-processing-attempt é metadado de transporte, não integra o fingerprint. Até dez falhas de processamento usam republicação persistente confirmada antes do ack original, seguida de diagnóstico ao esgotar orçamento. A abertura e compatibilidade ficam no caso de uso OpenConversation; Inbox/conversa continuam atomicamente persistidas.
