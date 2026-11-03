# Decisão 0009 — Atendimentos, propostas e aceite

**Tarefas:** T04.01–T04.06. **Fluxo aprovado pelo responsável:** 02/11/2026.

## Fluxo e limites

O pedido cria um atendimento `Negotiating`, com cliente e artista distintos. A criação já autoriza a futura conversa de negociação: não exige aceite prévio. P04 apenas persiste essa intenção e define os contratos; criação efetiva e envio de mensagens pertencem a P05, com Outbox e consumo idempotente. Nenhum evento é publicado em P04.

O artista proprietário apresenta propostas imutáveis. Uma nova proposta substitui a vigente ainda não aceita (`Superseded`), mantendo versões anteriores consultáveis. Só o cliente participante aceita a proposta vigente (`Accepted`), tornando o atendimento `Agreed`. Após aceite não há edição nem nova proposta neste recorte. Aceitar uma versão substituída retorna conflito. Repetir o aceite da mesma proposta retorna o resultado já persistido. Aceite não cria reserva, receita ou pagamento. Encerramento, recusa, cancelamento, expiração e reabertura não são introduzidos nesta etapa.

A autorização inicial de conversa trata a primeira negociação. Quando o encerramento existir em P07, RF15 continua obrigatório: outro pedido sozinho não habilita chat após serviço encerrado; novo contexto depende do novo agendamento confirmado.

## Entrada e identidade

Engagements tem quatro camadas, banco/schema/credenciais/migrações próprios. Resolve o usuário interno e suas funções por chamada delegada a Identity, por requisição, sem confiar em IDs ou funções recebidos no corpo. Todas as consultas se restringem a participantes; recurso inexistente e recurso de terceiros retornam 404. A função Artist é exigida para apresentar proposta, além do vínculo de proprietário. Conta Artist também pode solicitar a outro artista como Client; autoatendimento é rejeitado.

O contexto do artista e do flash é obtido por contrato HTTP técnico de Catalog, autenticado por Client Credentials, cliente `inklink-engagements`, escopo `catalog.engagement-context.read`. Esse escopo não permite ler perfis privados, escrever no catálogo ou impersonar usuários. A rota interna não é publicada no Gateway. Falhas de dependência retornam 503 sem criar pedidos. Catalog retorna uma leitura consistente do artista e do flash escolhido, incluindo o identificador interno do proprietário apenas nesse contrato técnico.

O pedido preserva snapshot do nome, estúdio, cidade, preço inicial e, quando selecionado, identificação, título, estilo, imagem, texto alternativo e preço de referência do flash. Alteração/exclusão posterior de publicação não altera a negociação. O snapshot representa o contexto lido na criação, não uma promessa de disponibilidade nem uma trava distribuída sobre Catalog. Não há acesso a bancos alheios, FK entre serviços ou entidades de produto compartilhadas.

Descrição da ideia: 1–4000 caracteres de entrada, com trim e rejeição de branco; região: 1–120; tamanho aproximado em centímetros: maior que zero até 300, com até duas casas decimais. Proposta: descrição de 1–4000 e valor BRL maior que zero até 9999999.99, com até duas casas. IDs devem ser UUIDs não vazios. Datas são UTC. Limites são contratos iniciais próprios, não herdados do protótipo.

## Repetição e concorrência

Criação de pedido e apresentação de proposta exigem `Idempotency-Key` UUID não vazio. Chaves são vinculadas à conta e à operação, persistidas sem expiração neste incremento. Mesmo payload normalizado repete o resultado original; chave reutilizada com outro payload retorna 409. Uma repetição de pedido consulta o registro antes de chamar Catalog, preservando o resultado mesmo se a publicação já foi removida. Repetição de proposta não reativa uma versão substituída. Não há repetição automática de escritas externas.

Gravações de proposta e aceite bloqueiam a linha do atendimento dentro da transação local. Assim, duas apresentações ou apresentação/aceite concorrentes têm ordem consistente: só a proposta vigente pode ser aceita. As versões são preservadas e a troca da vigente é atômica. Unicidade no banco protege a criação repetida de pedidos. Listagens usam cursor vinculado à conta e perspectiva (`client`/`artist`), UUID crescente, limite padrão 20 e máximo 100; cursor não concede autorização. Itens e contagem usam o mesmo snapshot local.

## Integrações futuras

O contrato de intenção de conversa contém versão, atendimento, participantes, data e revisão. P05 deverá gravar a intenção e Outbox na mesma transação de negócio, inclusive para atendimentos anteriores à implantação da integração, com reconciliação idempotente. Não há endpoint anônimo ou público para habilitar chat.

Agenda continuará proprietária da reserva. Seu contrato deverá carregar atendimento, cliente, artista, intervalo e chave idempotente. Aceite é consultável mas não confirma horários. Estados pendentes não equivalem a reserva ou encerramento concluído. Os contratos detalhados estão em `docs/contracts/p04-integrations.md`; transporte e autenticação específicos de Messaging/Scheduling serão definidos com seus serviços.

## Revisão de conformidade — 02/11/2026

Apresentação valida que a proposta anterior é exatamente a vigente do mesmo atendimento e que a nova versão está Pending e é distinta. Falhas não alteram qualquer proposta nem a revisão. Uma proposta vigente não pode ser reapresentada nem uma versão Superseded reaproveitada.

O histórico passou a recurso paginado (20 por padrão, máximo 100), com cursor vinculado à conta e atendimento. Listagem/detalhe retornam apenas `currentProposal`; apresentação carrega no máximo a vigente e a chave repetida, aceite carrega a vigente e a versão solicitada. Comprimentos de texto são limites da entrada antes do trim; OpenAPI explicita esses limites, não branco, intervalos numéricos e precisão decimal.

A mudança de formato é incompatível. Conforme decisão 0005, o contrato corrigido usa `/api/v2/engagements`, e v1 sinaliza retirada com 410 e orientação de migração, sem redirecionar escritas. Não há alteração dos dados persistidos ou do escopo das chaves idempotentes. Gateway/BFF aceitam v2 com suas proteções existentes. A transição e o mapeamento para consumidores locais estão em `docs/development/engagements.md`.