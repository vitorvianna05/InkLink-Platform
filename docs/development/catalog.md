# Execução de Catalog — P03

Catalog usa .NET 10, PostgreSQL 17.7/EF Core/Npgsql 10 e pg_trgm. O código fica em src/backend/services/Catalog, com camadas próprias e testes próximos. A [decisão 0008](../decisions/0008-p03-catalog-and-search.md) define política, campos, limites, pesquisa, autorização e reconstrução. Nenhum perfil de demonstração é criado automaticamente.

## Preparar e executar

Na raiz do repositório, com Docker e SDK configurados:

~~~powershell
./src/backend/scripts/Initialize-Local.ps1
./src/backend/scripts/Start-Database.ps1
./src/backend/scripts/Apply-Migrations.ps1
./src/backend/scripts/Configure-Keycloak.ps1
dotnet run --project src/backend/services/Catalog/src/InkLink.Catalog.Api
~~~

O script preserva credenciais existentes e acrescenta segredos locais de Catalog. O banco independente escuta em 127.0.0.1:54331 e a API local em http://127.0.0.1:5193. Identity, Gateway e BFF seguem o [guia de autenticação](authentication.md). Execute Identity para publicar ou acessar favoritos. Gateway agora requer Gateway:CatalogAddress; Initialize-Local configura http://127.0.0.1:5193/. Em produção, integração e destinos exigem HTTPS. Não publique os endereços HTTP locais.

CatalogDatabase:ConnectionString usa inklink_catalog_app em execução; migrações e manutenção usam inklink_catalog_owner. O banco e schema de Identity não são acessados por Catalog. Credenciais ficam em .env.local/user-secrets/variáveis; não devem ser copiadas para arquivos versionados. A extensão pg_trgm é criada pela migração, como proprietário do banco.

## Contratos

Base /api/v1. Todos os erros usam application/problem+json com code e traceId. Campos desconhecidos são rejeitados. OpenAPI fica em /openapi/v1.json apenas em Development/Testing. Saúde: /health/live e /health/ready; schema ausente retorna 503 catalog.database_unavailable. Logs estruturados têm traceId, serviço, método, caminho, status e duração, sem token, corpo ou query.

| Método e caminho | Finalidade / autorização |
| --- | --- |
| GET /artists?limit=20&cursor=... | Descoberta pública por nome e UUID. |
| GET /artists/search?term=...&style=...&city=...&maxPrice=... | Pesquisa pública cumulativa; suporta limit/cursor. |
| GET /artists/suggestions?term=...&limit=10 | Sugestões públicas categorizadas; vazio não sugere. |
| GET /artists/{id} | Perfil público. |
| GET /artists/{id}/tattoos e /flashes | Listas públicas separadas, com limit/cursor. |
| GET /artists/{id}/tattoos/{artworkId} e /flashes/{artworkId} | Item público no artista e tipo corretos. |
| PUT /artists/me | Cria (201 com Location) ou atualiza (200) perfil do próprio Artist. |
| POST /artists/me/tattoos e /flashes | Publica mídia do próprio Artist; 201 com Location consultável. |
| PUT /artists/me/tattoos/{id} e /flashes/{id} | Atualiza metadados próprios; 204. |
| DELETE /artists/me/tattoos/{id} e /flashes/{id} | Remove metadados próprios; 204. |
| GET /favorites?limit=20&cursor=... | Favoritos do cliente autenticado. |
| PUT /favorites/{artistId} | Adiciona favorito idempotentemente; 204. |
| DELETE /favorites/{artistId} | Remove favorito idempotentemente; 204. |

Exemplo de corpo de PUT /artists/me, após cadastro e ativação de Artist conforme P02:

~~~json
{
  "name": "Nome público do artista",
  "studio": "Estúdio",
  "city": "São Paulo",
  "biography": "Apresentação pública",
  "styles": ["Blackwork"],
  "startingPrice": 300.00
}
~~~

Exemplo de flash:

~~~json
{
  "title": "Rosa",
  "imageUrl": "https://images.example.com/rosa.jpg",
  "alternativeText": "Desenho de rosa em preto",
  "style": "Blackwork",
  "referencePrice": 450.00
}
~~~

Exemplos são apenas contratos de documentação. O endereço de imagem precisa ser substituído por conteúdo público autorizado. Não use URLs assinadas nem coloque segredos na mídia. Não envie referencePrice em tatuagens realizadas. O perfil é publicado antes de adicionar mídia. IDs, proprietário, funções e tipo não são aceitos no corpo; as rotas e o contexto autenticado os definem.

Listagens retornam items, nextCursor, hasMore, total; total é a quantidade antes do cursor, após filtros. Limite padrão 20/máximo 100. Reutilize nextCursor sem interpretá-lo; mudar critérios exige reiniciar a consulta. Respostas públicas não incluem proprietário nem dados de outras contas. Favoritos e publicação usam no-store e são negados se Identity não puder confirmar a conta. Usuário técnico não pode substituir um usuário cliente/artista.

## Reconstruir a pesquisa

A ferramenta não cria dados sintéticos e não lê Identity. Com CatalogDatabase__ConnectionString configurada externamente com a credencial do proprietário:

~~~powershell
dotnet run --project src/backend/services/Catalog/tools/InkLink.Catalog.Admin -- rebuild-search
~~~

Não coloque a conexão em argumentos de linha de comando. Saída 0 indica conclusão; 1 indica falha sem detalhes sensíveis; 2 indica argumentos inválidos. O comando recompõe os termos a partir de perfis oficiais e pode bloquear escritas de perfil durante a operação; agende conforme o volume. Não requer apagar dados, volumes ou favoritos.

## Verificar

~~~powershell
dotnet restore InkLink.Platform.slnx --locked-mode
dotnet format InkLink.Platform.slnx --no-restore --verify-no-changes
dotnet build InkLink.Platform.slnx --configuration Release --no-restore --warnaserror
dotnet test InkLink.Platform.slnx --configuration Release --no-build
~~~

Testes de Catalog usam PostgreSQL e Keycloak HTTPS descartáveis, usuários e mídia sintéticos exclusivos das fixtures. Os cenários de integração reutilizam fontes auxiliares de teste da fundação P02; isso não cria referência entre os serviços de produto. Os testes arquiteturais verificam essa separação. Resultados e limites serão registrados na verificação P03.
