# Decisão 0021 — Estúdios e vínculos com artistas

**Tarefa:** T10.26. **Situação:** aprovada pelo responsável do produto em 17/11/2026.

## Contexto

RF01, RF04, RF06 e RF20 descrevem áreas por função, busca por estúdios, perfil público do artista e gestão do portfólio. A decisão 0008 de Catálogo mantém hoje um campo obrigatório `studio` no perfil público; Atendimentos também preserva um snapshot singular desse nome no pedido. A arquitetura atribui ao Catálogo a apresentação pública do artista e exige propriedade de dados por serviço, sem acesso cruzado a bancos.

O responsável aprovou as seguintes regras: um artista pode ter vários vínculos com estúdios e um estúdio pode reunir vários artistas; estúdios são públicos e o perfil lista todos os vínculos; somente a administração cria e gerencia estúdios e associações; o website terá role e painel administrativo; pedidos e agendamentos continuam vinculados ao artista, sem seleção de unidade/local.

Não há arquivo de Constitution no repositório. Esta decisão segue `requirements.md`, `architecture.md` e as decisões vigentes, conforme confirmado pelo responsável.

## Decisão

### Entidade e propriedade

- `Catalog` é proprietário de `Studio` e da associação muitos-para-muitos entre `Studio` e o perfil público `Artist`. A associação tem unicidade por par Studio/Artist. Não há tabela compartilhada ou FK para o banco de Identity.
- Um Studio tem identificador UUIDv7 e nome público, com limite inicial de 160 caracteres e validação textual compatível com o campo legado. Nomes iguais são permitidos: nome não é identidade, e estúdios homônimos permanecem distintos conforme a política de busca da decisão 0008.
- Neste incremento, Studio não recebe cidade, endereço, geolocalização ou outros dados de negócio. A cidade continua pertencendo ao perfil do artista. A superfície pública do Studio é sua associação/lista de nomes nos perfis dos artistas e as sugestões de busca; não se cria diretório ou página pública independente de estúdio.
- A gestão administrativa cria e renomeia Studio e associa/desassocia perfis públicos de artistas com função Artist. A retirada de um vínculo não exclui o artista nem o Studio. Não há exclusão permanente de Studio nesta entrega; registros sem artistas associados ficam fora de perfis e sugestões públicas.

### Autorização administrativa

- `Admin` é uma função interna adicional da aplicação, mantida por Identity. Uma conta pode acumular `Client`, `Artist` e `Admin`; Admin não substitui as demais funções.
- Somente operador confiável concede Admin pela ferramenta operacional `InkLink.Identity.Admin`, com credencial administrativa fornecida pelo ambiente. O painel não concede função; cadastro público, formulário, headers e Keycloak não são fontes de autorização do role.
- A página de administração de estúdios no website só é apresentada a sessões cujo perfil autenticado inclui Admin. Cada API de escrita verifica Admin no servidor, por Identity, independentemente da interface. Catalog continua responsável pelas regras e dados dos estúdios.
- Artistas não criam, editam, associam ou removem por conta própria os vínculos de Studio.

### Contratos e compatibilidade

- Os contratos novos do Catálogo representam o artista com `studios: [{ id, name }]`, em vez do campo singular `studio`. Cards, perfil, favoritos e demais respostas que incluam apresentação de artista seguem o mesmo formato.
- Como a troca do campo é incompatível, os novos contratos públicos do Catálogo usam `/api/v2`, conforme a decisão 0005. A v1 permanece disponível durante a transição; consumidores migram para v2 antes de qualquer retirada da v1. A busca/autocomplete continua identificando sugestões por categoria e passa a derivar termos das associações de Studio.
- O contrato interno do Catálogo consumido por Atendimentos também passa a fornecer as associações na versão apropriada. Pedidos novos preservam uma lista imutável de IDs e nomes dos estúdios associados no instante da criação. Isso registra afiliação pública, não o local físico do serviço. Agendamentos continuam associados a cliente, artista e atendimento, sem StudioId ou seleção de unidade.
- O formato de resposta de Atendimentos que expõe esse snapshot deve receber nova versão se a lista substituir o campo singular, mantendo os contratos anteriores durante a transição definida pela decisão 0005. Snapshots históricos não são reescritos e continuam apresentando o valor singular original.
- A decisão não altera proposta, autorização de atendimento, calendário, local de reserva ou lançamento financeiro.

### Migração dos dados atuais

- Cada perfil legado com valor `studio` recebe uma entidade Studio e uma associação. A migração não funde registros por igualdade de nome normalizado, porque nomes iguais podem representar locais distintos.
- Perfis e snapshots antigos permanecem consultáveis até migração de seus consumidores. Novas gravações usam a lista de associações.
- A migração deve ser transacional, repetível e validada antes de remover o campo legado da persistência. O rollout do serviço não aplica migrações no startup.

## Consequências

- Catálogo passa a projetar múltiplos nomes de estúdio nos perfis, cards e índice de busca; a associação fica na fonte oficial do serviço.
- A administração exige um role interno e interface nova, mas não introduz um role de realm Keycloak nem um caminho público de concessão.
- A versão v2 do Catálogo e a nova versão de Atendimentos exigem transição documentada. A experiência web e futuros consumidores móveis precisam usar os contratos compatíveis.
- A implementação fica dividida em T14.01–T14.04 em `docs/tasks.md`. Nenhuma dessas tarefas pressupõe mudanças no protótipo.

## Referências

- [Requisitos](../requirements.md), RF01, RF04, RF06, RF20 e RF24.
- [Arquitetura](../architecture.md), identidade e perfis, Catálogo, Atendimentos e website.
- [Decisão 0003 — Autenticação e identidade](0003-authentication.md).
- [Decisão 0005 — Convenções das APIs](0005-api-conventions.md).
- [Decisão 0007 — Identidade, funções, Gateway e sessão web](0007-p02-identity-and-sessions.md).
- [Decisão 0008 — Catálogo, busca e favoritos](0008-p03-catalog-and-search.md).
- [Decisão 0009 — Atendimentos, propostas e aceite](0009-p04-engagements.md).
- [Decisão 0016 — Website React](0016-p10-website.md).
