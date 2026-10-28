# Decisão 0007 — Identidade, funções, Gateway e sessão web

**Etapa:** P02 / T02.01–T02.06. **Política de contas:** aprovada pelo responsável na conversa de implementação. **Escolhas técnicas:** detalhamento da decisão 0003.

## Contas e funções

O administrador cria as contas no Keycloak. Cadastro público permanece desabilitado nesta entrega. No primeiro acesso autenticado a profiles/me, Identity cria o perfil e o vínculo pela combinação exata de issuer e subject. Não utiliza e-mail como chave. Uma chave composta e uma transação local impedem vínculos duplicados sob concorrência.

O bootstrap é um comando explícito de Application (ProvisionAccount), executado pelo adaptador de identidade após autorização e apenas nos endpoints marcados do próprio perfil. A seleção usa metadata do endpoint roteado, não comparação literal da URL, preservando o comportamento com barra final. O comando coordena criação, vínculo e resolução de concorrência; Infrastructure apenas consulta/grava e traduz o conflito da chave externa. GetMyProfile permanece uma consulta sem escrita. Isso mantém a política de primeiro acesso sem colocar decisões de negócio no middleware ou repositório.

A ativação administrativa é coordenada por ActivateArtist em Application e a alteração de nome por UpdateMyProfile. Os casos de uso aplicam as transições de Domain e solicitam a persistência por portas.

Toda conta interna possui a função Client. A função Artist é adicional e somente a ferramenta administrativa InkLink.Identity.Admin pode solicitá-la, com credencial operacional fornecida pelo ambiente. Não existe rota pública de concessão de função. A mesma conta pode ter Client e Artist. A decisão do vínculo é de Identity e não vem de campos de formulário, headers ou funções escolhidas pelo usuário.

O perfil editável desta etapa contém apenas displayName, obrigatório e não vazio após trim. Identificadores, issuer, subject e funções não são editáveis pelo contrato HTTP. Propriedades JSON desconhecidas são rejeitadas. A projeção HTTP do próprio perfil informa id, displayName e roles. Credenciais permanecem exclusivamente no Keycloak.

## Validação e entrada das APIs

Identity e Gateway validam independentemente assinatura, issuer, audience inklink-api e validade do access token. Exigem o tipo de access token Bearer emitido pelo Keycloak e um subject. ID token não é credencial de API.

YARP 2.3 foi escolhido para o Gateway .NET: roteamento e encaminhamento, sem regras de perfil ou armazenamento de identidade. O primeiro destino é Identity. Identidade não é aceita por headers X-User-Id ou X-Roles. Gateway remove esses headers e cookies nas chamadas aos serviços.

O componente InkLink.Authentication compartilha somente configuração e validação técnica de autenticação; não contém entidades, repositórios ou autorização sobre dados de negócio. Application e Domain de Identity não o referenciam. Outros serviços não foram criados.

## Chamadores técnicos

O cliente confidencial inklink-catalog é reservado à futura integração de Catálogo, com Client Credentials, sem login interativo. O único escopo inicial é identity.profile.read. A operação permitida é GET /api/v1/internal/profiles/{id}, retornando apenas id e displayName. Não concede alteração, acesso à própria conta humana ou ativação de artista.

O realm emite inklink_actor=user para os clientes de usuário e inklink_actor=service para o cliente técnico. O serviço verifica esse atributo assinado, o cliente autorizado (azp) e o escopo da operação. Um token de usuário não pode substituir um técnico nem o inverso. Segredos técnicos não são distribuídos aos frontends.

## Sessão do BFF

O BFF usa Authorization Code com PKCE S256 e o middleware OIDC do ASP.NET Core, incluindo state, nonce e validação de tokens do login. O cookie __Host-inklink-session é HttpOnly, Secure, Path=/ e SameSite=Lax. Contém apenas uma referência protegida para o ticket, não access/refresh tokens.

Tickets com tokens ficam criptografados no PostgreSQL privado do BFF. Data Protection mantém chaves compartilhadas no mesmo contexto operacional e protege seu conteúdo com um certificado configurado, com chave privada disponível exclusivamente às instâncias autorizadas do BFF. Cookie e tokens são compatíveis com múltiplas instâncias que compartilhem banco, application name e certificado. O BFF não acessa tabelas de Identity.

A sessão dura no máximo cinco minutos e nunca ultrapassa a expiração do access token. Não há renovação automática nem sliding expiration nesta etapa; ao expirar, o usuário reinicia o login. Se o provedor retornar refresh token, ele permanece somente no ticket protegido e não é usado para estender essa sessão.

Logout exige POST autenticado e antiforgery válido. Remove o ticket durável antes de iniciar o logout OIDC. GET não encerra sessão. Operações de escrita encaminhadas pelo BFF também exigem antiforgery, com cookie Secure/HttpOnly/SameSite=Strict e header X-CSRF-TOKEN. O BFF encaminha o access token do ticket ao Gateway; não aceita token fornecido pelo navegador para substituir a identidade da sessão.

## Ambientes e operação

O realm versionado exige TLS. O ambiente local usa HTTPS no Keycloak e no BFF com certificado de desenvolvimento já confiável. HTTP de metadados é permitido somente com configuração explícita em Testing/Development; produção rejeita essa opção. Gateway e Identity podem comunicar por loopback HTTP no desenvolvimento. Ambiente publicado exige endereços HTTPS.

O realm local não contém contas, senhas ou client secrets reais: o script gera os valores em arquivos ignorados e configura User Secrets. Keycloak tem banco próprio, gerenciado pelo produto. Identity e BFF possuem migrações e credenciais distintas de execução e DDL; nenhum startup aplica migrações automaticamente.

Nos testes isolados, Testcontainers prepara realm descartável e contas sintéticas. O password grant existe apenas em clientes específicos dessa fixture para obter tokens nas verificações de API; clientes versionados do produto usam PKCE e não habilitam password grant. O fluxo do BFF é verificado por login real com código e PKCE.

## Observabilidade básica

Identity, Gateway e BFF usam o componente técnico InkLink.Observability para registrar conclusão das requisições. O evento contém método, caminho sem query string, status e duração, com traceId e serviço no escopo estruturado. O traceId corresponde ao cabeçalho X-Trace-Id e aos problemas HTTP. Os formatadores JSON incluem escopos; corpos, cookies, Authorization e parâmetros da URL não são registrados por esse middleware. Logs informativos do forwarder que incluiriam a URL completa são filtrados.

As operações privadas de perfil documentam também a resposta 500 em application/problem+json. Métricas e exportadores de rastreamento continuam sujeitos ao refinamento operacional de P09.

## Consequências

Sessão curta sem renovação simplifica a primeira entrega e exige novo login ao expirar. A disponibilidade do PostgreSQL do BFF participa da disponibilidade das sessões. Produção precisará distribuir e proteger certificados, manter material anterior durante rotação, configurar limites e proteção de rede e definir metas operacionais em P09. Frontends e funcionalidades de outros contextos continuam em suas etapas próprias.

## Referências técnicas

- [Protocol mappers oficiais do Keycloak, incluindo subject](https://www.keycloak.org/admin-api/protocol-mappers).
- [Mudança do client scope basic](https://github.com/keycloak/keycloak/blob/main/docs/documentation/upgrading/topics/changes/changes-25_0_0.adoc). O realm inclui basic e profile explicitamente, preservando sub no access token.
- [Keycloak em contêiner](https://www.keycloak.org/server/containers).
