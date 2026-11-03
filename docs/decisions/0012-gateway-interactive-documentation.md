# Decisão 0012 — Documentação interativa antecipada no Gateway

Tarefas TGW.01–TGW.04; incremento entre P06 e P07, aprovado pelo responsável. Antecipação da interface de exploração prevista genericamente na consolidação T09.01, sem declarar P09 concluída.

## Decisão

O Gateway oferece Swagger UI em /swagger, somente Development/Testing, com seleção de Identity, Catalog, Engagements, Messaging e Scheduling configurados. Os serviços seguem sendo fontes oficiais do OpenAPI; o Gateway consulta /openapi/v1.json por cliente HTTP dedicado e não referencia implementações de negócio.

O documento de consumo publicado pelo Gateway filtra caminhos públicos roteados e remove contratos internos, endpoints de saúde e versões aposentadas. Preserva schemas alcançáveis pelos caminhos públicos; componentes internos não utilizados são removidos. Destinos servers de documento, caminhos e operações são adaptados para a origem/base do Gateway. O teste interativo usa os endpoints reais existentes e mantém autorização/autoria/validação nos serviços. Nenhum endpoint interno novo é publicado para viabilizar a documentação. A rota técnica de perfis já existente mantém seu contrato de P02 e não aparece no catálogo interativo.

Documentos /swagger/documents/{serviço}.json não recebem cookies ou tokens para acessar os OpenAPIs de desenvolvimento. O cliente HTTP tem timeout, limite de tamanho e não segue redirects. Falha de uma API retorna 503 documentation.service_unavailable no documento correspondente, sem substituir erro por documento vazio e sem indisponibilizar os outros serviços. Swagger UI usa assets embarcados, sem CDN ou validador externo.

## Autenticação

Um cliente público inklink-swagger usa Authorization Code com PKCE S256, audience inklink-api, ator user e escopos openid/profile. Não tem segredo, Client Credentials ou password grant. Callbacks/origens locais exatos: http://127.0.0.1:5191 e http://localhost:5191, callback /swagger/oauth2-redirect.html. O cliente é adicionado aos chamadores de usuário aprovados via inicialização local.

A interface de desenvolvimento mantém o access token em memória para executar chamadas Bearer. PersistAuthorization fica desativado. Não muda o contrato do BFF: o website continua com tokens somente no servidor e sessão protegida. A exceção é a ferramenta de teste de desenvolvimento, que não deve ser publicada como frontend do produto. Não há relaxamento de funções Artist/Client ou acesso a dados de terceiros.

## Operação e limites

Perfis Visual Studio de Identity e Gateway passam a usar 5180 e 5191, alinhados aos destinos já configurados por Initialize-Local.ps1. Gateway abre /swagger. Para testar todas as capacidades, iniciar os cinco projetos Api e o Gateway com dependências locais e contas configuradas. BFF não é necessário para o teste direto de APIs com o cliente Swagger.

Os demais serviços são implantáveis independentemente; documentação de serviço indisponível não impede os demais documentos. Produção não registra UI, assets nem agregação OpenAPI. Para habilitar outra origem de desenvolvimento, revisar explicitamente perfil, callbacks e webOrigins no realm e reaplicar sincronização; não usar curingas.

T09.01 permanece responsável por consolidar exemplos, compatibilidade e contratos novos de P07/P08; este incremento não antecipa regras de negócio, Finance ou frontends.

Referências de implementação: https://github.com/domaindrivendev/Swashbuckle.AspNetCore e https://swagger.io/docs/open-source-tools/swagger-ui/usage/oauth2/ . Versão inicial fixada: Swashbuckle.AspNetCore.SwaggerUI 10.3.1, preservando o gerador OpenAPI já usado nos serviços.
