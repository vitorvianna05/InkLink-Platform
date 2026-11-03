# Decisão 0008 — Catálogo, publicação, pesquisa e favoritos

**Tarefas:** T03.01–T03.07. **Política de alimentação:** aprovada pelo responsável em 28/10/2026. **Recorte:** backend P03, sem interface gráfica.

## Limites e identidade

Catalog é executável independente em .NET 10 com Api/Application/Domain/Infrastructure. Possui banco PostgreSQL próprio, schema catalog, migrações, usuário de execução e proprietário de migração separados. Nenhuma camada de produto referencia a implementação de Identity; não há joins, FKs ou acesso ao banco desse serviço. OwnerId e ClientId são identificadores internos de conta, nunca o subject do provedor.

O Gateway e o BFF permitem GET/HEAD públicos em artists. Escritas e favoritos exigem sessão/token, e Catalog verifica novamente a autenticação e as permissões. Cabeçalhos X-User-Id/X-Roles não são fontes de autorização. Nos endpoints privados, uma chamada síncrona autenticada a GET /api/v1/profiles/me de Identity confirma o UUID interno e a função Artist usando o access token delegado do próprio usuário. A informação vale apenas para aquela requisição, sem cache de permissão ou armazenamento do token em Catalog. A API de Identity permanece a fonte oficial dessas funções. A consulta técnica identity.profile.read de P02 não concede capacidade de publicar ou acessar favoritos; não se amplia esse escopo para escrita ou impersonação. Falha temporária dessa integração retorna 503 e não libera a operação. Consultas públicas não dependem da disponibilidade de Identity.

## Publicação e campos

O usuário com Artist publica imediatamente seu próprio perfil, tatuagens e flashes por API autenticada. Não há painel administrativo, upload, aprovação, moderação, agenda ou cobrança nesta etapa. Identidade do proprietário é resolvida no servidor e não consta do contrato de entrada. Um UUIDv7 público de artista é criado separadamente, com unicidade por proprietário. PUT do próprio perfil cria ou substitui seus campos, preservando o UUID; corrida na criação retorna 409 para a gravação perdedora, sem duplicação. Nas atualizações, a última transação concluída prevalece; controle de versões/editor colaborativo fica fora deste recorte.

Campos obrigatórios do perfil: name (120), studio (160), city (100), biography (4000), styles (1 a 20 valores únicos após normalização, até 80 caracteres cada) e startingPrice. Preço em BRL, de zero a 9999999.99, com até duas casas decimais. Nome, estúdio e cidade precisam conter letras ou números. Apenas esses dados de apresentação são públicos; proprietário, vínculos de provedor e dados de favoritos não aparecem nos DTOs públicos.

Trabalho e flash possuem título (160), imageUrl (2048), alternativeText (300) e style (80). Flash pode ter referencePrice, com a mesma regra monetária; tatuagem realizada não recebe preço de flash. O tipo é definido pela rota e permanece imutável. URL deve ser HTTPS absoluta, sem credenciais, query, fragmento ou loopback. Catalog guarda a referência e os metadados; não baixa, serve, inspeciona nem garante a disponibilidade da imagem. Quem publica deve usar uma imagem pública estável e autorizada, sem segredos ou URLs assinadas temporárias. Upload, armazenamento de binários e remoção no provedor externo exigem incremento próprio. Excluir uma publicação de mídia remove seus metadados no catálogo, sem remover arquivo externo.

Outro artista não altera mídia alheia: IDs são consultados no escopo do próprio artista e do tipo da rota; divergência retorna 404. Não há endpoint de alteração de perfil por UUID arbitrário. Não se criam publicações sintéticas automaticamente, em nenhum ambiente.

## Consultas e paginação

Descoberta ordena por nome normalizado em collation C e UUID crescente; não presume recomendação personalizada nem ordena por uma data inferida do UUID. Pesquisa ordena por relevância decrescente, nome normalizado e UUID. Tatuagens, flashes e favoritos usam UUID crescente como desempate estável de paginação, sem apresentá-lo como data de negócio.

Listagens recebem limit padrão 20, máximo 100, e cursor opaco. Retornam items, nextCursor, hasMore e total. total conta todas as correspondências antes do cursor. Em pesquisa, contagem e itens pertencem ao mesmo snapshot de leitura da requisição. Entre páginas, o catálogo pode mudar; não há snapshot de navegação retido no servidor. Cursores contêm recurso, hash dos filtros normalizados e última posição; são validados no backend. Alterar critérios, tipo de mídia, artista ou cliente invalida o cursor. O cursor não é uma credencial nem concede acesso.

GET artists representa Descobrir; GET artists/search executa explicitamente a pesquisa, inclusive sem critérios. O estado inicial de Explorar e o botão de busca serão implementados no frontend correspondente, sem conteúdo de demonstração no backend. Filtros de estilo e cidade são exatos após normalização; preço é um teto inclusivo sobre startingPrice. Termo e filtros combinam cumulativamente. Sem resultados retorna lista vazia, total zero e hasMore false. Sugestões não executam nem alteram a pesquisa ou os filtros por seleção.

## Mecanismo de pesquisa

PostgreSQL 17 com pg_trgm atende à primeira entrega, sem infraestrutura adicional de Elasticsearch. A escolha é um detalhe de Infrastructure, atrás dos contratos de consulta de Application. Normalização remove diferenças de caixa, acentos e pontuação e compacta espaços. Os termos vêm apenas de nome, estúdio, cidade e estilos dos perfis públicos; biografia, contas e favoritos não alimentam sugestões.

SearchTerms é uma projeção local derivada dos perfis de Catalog. A publicação grava fonte, campos normalizados e termos na mesma transação. Um índice GIN com gin_trgm_ops apoia busca por prefixos/similaridade. Consultas fixam localmente o limiar pg_trgm.similarity_threshold em 0.3, sem alterar configuração global. Correspondências exatas recebem relevância 3; prefixos, 2; demais correspondências aprovadas pelo operador de similaridade usam word_similarity entre 0 e 1. Com menos de três caracteres, apenas prefixos são usados. Empates têm ordenação explícita. Sugestões deduplicam por categoria e termo normalizado; cidades e estúdios homônimos continuam opções distintas de categoria. Campo vazio ou só pontuação retorna lista vazia; sem correspondência, também. Limite padrão de sugestões 10 e máximo 100. O limiar inicial permite pequenos erros, não promete equivalência fonética ou precisão universal; ajustes futuros devem atualizar testes e decisão.

A fonte oficial é a tabela de perfis públicos do próprio Catalog. A ferramenta administrativa rebuild-search recompõe campos normalizados e SearchTerms em lotes de 200 a partir dessa fonte, numa transação. Bloqueio local da tabela de artistas impede publicação concorrente durante a reconstrução; leitores continuam sujeitos ao MVCC do PostgreSQL. Falha reverte a reconstrução. O comando usa credencial do proprietário por variável de ambiente; não existe rota HTTP de manutenção. Não há índice externo com sincronização eventual ou eventos em P03.

## Favoritos

Favoritos pertencem a Catalog e são vinculados a ClientId e ArtistId, com chave composta e FK apenas para o artista local. Todas as contas aprovadas possuem Client, inclusive as que também têm Artist. PUT é idempotente e usa unicidade no banco sob concorrência. Remoção repetida retorna 204; inclusão de artista inexistente retorna 404. Leitura e remoção sempre usam a conta autenticada, sem clientId na URL ou corpo. Cursores são vinculados ao cliente. Mudar/limpar pesquisa não altera favoritos. Dados são duráveis e não dependem de sessão em memória ou navegador.

## Referências

- [Contratos de API](0005-api-conventions.md).
- [Identidade e sessões](0007-p02-identity-and-sessions.md).
- [PostgreSQL 17: pg_trgm](https://www.postgresql.org/docs/17/pgtrgm.html).
- [Npgsql: traduções para SQL](https://www.npgsql.org/efcore/mapping/translations.html).
