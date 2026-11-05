# Contratos para consumidores — P09

Fonte oficial de schemas e resultados: OpenAPI de cada serviço. Em desenvolvimento/testes, acessar o catálogo do Gateway em /swagger e /swagger/documents/{identity,catalog,engagements,messaging,scheduling,finance}.json. No ambiente isolado: https://localhost:5291/swagger. Snapshots exportados pelo roteiro ficam em artifacts/integration/contracts e não devem ser editados. Não há autorização adicional por utilizar Swagger.

## Capacidades e versões

| Serviço | Rotas públicas | Consumidores e pré-condições |
| --- | --- | --- |
| Identity | /api/v1/profiles/me, /api/v1/profiles/me/photo | Usuário aprovado; GET vincula identidade federada e função Client. Artist só por operação administrativa. Foto por multipart (`image`, JPEG/PNG/WebP até 5 MB); `photoUrl`/`photoThumbnailUrl` são assinados com validade curta quando a foto é privada ([decisão 0018](../decisions/0018-p10-images.md)). |
| Catalog | /api/v1/artists, search, suggestions, perfil, tattoos, flashes; /api/v2/artists/me/{tattoos,flashes} | Leitura pública; /artists/me exige Artist. Obras são criadas e alteradas por multipart na v2 (`image` até 10 MB); as escritas v1 por URL foram retiradas e respondem 410 `catalog.artwork_url_writes_retired`, com o caminho da v2. Leituras trazem `thumbnailUrl`, `externalImage` e a foto pública do artista. Favoritos próprios em /api/v1/favorites. |
| Engagements | /api/v2/engagements, proposals, acceptance, closure, lifecycle, lifecycle/retry | Participantes; cliente solicita/aceita, artista propõe. v2 é o contrato vigente do ciclo; v1 foi descontinuada e retorna 410 Gone. |
| Messaging | /api/v1/conversations, messages, artist-summary | Participante para histórico/envio; Artist para resumo. Bloqueio é autoridade local, não permissão inferida no consumidor. |
| Scheduling | /api/v1/scheduling/availability, reservations, operations, completion, artist-context, artist-summary | Disponibilidade/reserva/conclusão pelo artista; leituras privadas autorizadas por contexto. Proposta aceita não reserva automaticamente. |
| Finance | /api/v1/finance/entries, reversal, dashboard | Artist proprietário; recebimento exige atendimento próprio; não é processamento de pagamentos. |

As rotas exatas, inclusive parâmetros intermediários, são apresentadas no OpenAPI. Endpoints /internal não são APIs dos canais e ficam fora do Gateway. BFF usa /bff/login, /bff/session, /bff/antiforgery e /bff/logout: tokens permanecem no servidor e escritas exigem X-CSRF-TOKEN. Mobile usa access token via Gateway, Code/PKCE e armazenamento seguro da plataforma.

## Repetição, paginação e erros

POSTs críticos de pedido, proposta, mensagem e ledger usam Idempotency-Key UUID não vazio conforme seu schema. Confirmar reserva também exige chave. Repetir mesma chave/payload recupera resultado; conteúdo divergente retorna 409. Não presumir que timeout desfez uma escrita. Conclusão de sessão via PUT é idempotente por reserva e instante/valor; alteração conflita. Favorito usa PUT/DELETE idempotentes.

Listagens usam items, nextCursor e hasMore; limite padrão 20, máximo 100 salvo sugestões e contratos específicos. Mensagens usam sequência/cursor incremental; não ordenar por UUID como horário oficial. Cursores são opacos e vinculados ao recurso/conta/filtros; não reutilizar após mudar pesquisa ou período. Quantidade total de catálogo não se aplica automaticamente a históricos.

Erros são application/problem+json com status, code e traceId; validação inclui errors por campo. 400 entrada inválida, 401 login inválido/expirado, 403 função sem permissão, 404 recurso inexistente ou privado, 409 conflito, 500 falha inesperada e 503 fonte/serviço indisponível. Não interpretar o texto da mensagem para decisões. X-Trace-Id permite diagnóstico sem enviar tokens ou corpo privado a logs.

Closure retorna 202 enquanto a operação não concluiu. Consultar lifecycle: Closing/Pending não significa Closed. Quarantined exige diagnóstico e retry autorizado; reservas futuras podem produzir Rejected e preservar atendimento. Retorno após encerramento só escreve após novo agendamento e Activate/Completed. Reserva confirmada pode coexistir com ativação pendente; não descartá-la nem repetir com outra chave.

## Limites relevantes

Pedido: idea e bodyRegion obrigatórios, approximateSizeCm positivo; consultar os limites exatos do schema e [guia de atendimentos](engagements.md). Imagens são enviadas como arquivo e regravadas em WebP sem metadados: obras com pelo menos 600 px no menor lado (variantes de 1600 e 480 px) e fotos com pelo menos 256 px, recortadas em quadrado (512 e 128 px). Obras antigas por URL aparecem com `externalImage: true` até a troca. Mensagens têm corpo não vazio e limites documentados em [Messaging](messaging.md).

Agenda usa IANA, precisão de minutos e intervalos [início,fim). Confirmadas não se sobrepõem; adjacentes são permitidas. [Agenda](scheduling.md) descreve janela consultável e confirmação. Ledger exige valor positivo até 9.999.999,99 com até duas casas e data efetiva não futura. Períodos financeiros [from,to) de 1–366 dias; listagem de reservas mantém seu limite de 93 dias. Finance exige todos os campos das fontes, inclusive zero explícito, e falhas não se tornam vazio. [Finance](finance.md) detalha estornos, datas, sessões e conversas atuais.

## Eventos e recuperação

| Contrato | Produtor → consumidor | Deduplicação e consequência |
| --- | --- | --- |
| engagement.negotiation-opened.v1 | Engagements → Messaging | Inbox por messageId e abertura por atendimento; repetição não cria outro chat. |
| scheduling.reservation-confirmed.v1 | Scheduling → Engagements | Inbox/recibo por evento/reserva; ativações são coordenadas e recuperáveis. |
| identity.profile-photo-changed.v1 | Identity → Catalog | Projeção por `revision` crescente: repetição e eventos fora de ordem não alteram a foto mais nova. Publicado só para fotos públicas (contas Artist) e na remoção delas. |

Envelopes incluem messageId, type, schemaVersion=1, occurredAt, correlationId, causationId quando existente, data e traceParent opcional. Eventos não transportam entidades de domínio. Outbox e negócio são atômicos; publicação pode repetir. Falhas têm tentativas limitadas, quarentena e diagnóstico sem payload privado; ler [ciclo integrado](lifecycle.md), [Messaging](messaging.md) e [Agenda](scheduling.md). A entrega não pressupõe exatamente uma vez nem ordem global.

## Compatibilidade

Manter consumidores web/iOS/Android existentes: campos adicionais opcionais são evoluções compatíveis; não remover/renomear campos, mudar significado, obrigatório, tipo, moeda ou semântica temporal em uma versão vigente. Novos valores de enum exigem estratégia explícita do consumidor. Mudança incompatível exige nova versão/contrato e período de coexistência com migração registrada; não impor atualização simultânea de aplicativos móveis. Mudanças de schema de banco usam expansão compatível antes de novos escritores. Restaurar binário não reverte automaticamente migração; nenhuma demonstração local autoriza rollback destrutivo.

Exemplos manuais em [requests.http](requests.http). Preencher variáveis com dados reais do ambiente e autenticar separadamente por função. Nunca versionar tokens ou exportar o arquivo já preenchido.

Exemplo informativo de evento do servidor, sem endpoint de publicação para os canais:

~~~json
{
  "messageId": "019a46d0-0000-7000-8000-000000000001",
  "type": "engagement.negotiation-opened.v1",
  "schemaVersion": 1,
  "occurredAt": "2026-11-03T18:00:00Z",
  "correlationId": "example-correlation",
  "causationId": null,
  "data": {
    "engagementId": "019a46d0-0000-7000-8000-000000000002",
    "clientId": "019a46d0-0000-7000-8000-000000000003",
    "artistAccountId": "019a46d0-0000-7000-8000-000000000004",
    "revision": 1,
    "writingAllowed": true
  },
  "traceParent": "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01"
}
~~~
