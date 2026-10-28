# Decisão 0003 — Autenticação e identidade

**Tarefa:** T00.04. **Situação:** aprovada pelo responsável do produto.

## Decisão

Adotar Keycloak como provedor dedicado de identidade, com OpenID Connect e OAuth 2.0. Utilizar Authorization Code com PKCE para login, access tokens JWT para APIs e Client Credentials quando um serviço atua em nome próprio.

O website terá um BFF, implementado como componente backend .NET 10, para manter tokens no servidor e oferecer sessão por cookie protegido ao navegador. iOS e Android serão clientes nativos separados, usando Authorization Code com PKCE e o navegador do sistema. Não haverá segredo de cliente embutido em código web ou aplicativo móvel.

## Responsabilidades

- Keycloak: credenciais, login, sessões e emissão de tokens. Recursos adicionais, como MFA e verificação de e-mail, são capacidades disponíveis; sua política de ativação será definida quando necessária.
- Identity: usuário interno, perfil e vínculos de função do InkLink. Associação estável pela combinação de emissor e identificador do sujeito no provedor, sem usar e-mail como chave imutável.
- Gateway e APIs: validação de access token, incluindo assinatura, emissor, destinatário e validade aplicáveis ao recurso.
- Cada serviço: autorização sobre seus próprios recursos e participantes. Uma função geral de artista não permite acessar todos os atendimentos ou conversas.
- BFF web: fluxo de login, sessão, encaminhamento autorizado às APIs e tratamento de saída/expiração. Não concentra regras de negócio.

ID token do login não é usado como access token das APIs. Identidade técnica e ação em nome de um usuário são contextos distintos; credenciais técnicas devem ter escopo mínimo e não substituir automaticamente permissões de usuário.

## Website e BFF

O navegador não mantém os access/refresh tokens do provedor como estado de aplicação. A sessão usa cookie com proteção apropriada, incluindo HttpOnly e Secure no ambiente publicado, política SameSite compatível e defesa contra CSRF nas operações necessárias.

Sessões e tokens no BFF devem permitir execução com múltiplas instâncias. O mecanismo de armazenamento e proteção de sessão, tempo de vida, renovação e saída foi detalhado para P02 na [decisão 0007](0007-p02-identity-and-sessions.md). Um cookie de sessão não elimina a necessidade de autorização nos serviços.

## Desenvolvimento e fases

A configuração local utilizará um ambiente Keycloak isolado, com registros próprios de clientes web/BFF, iOS, Android e chamadores técnicos conforme forem necessários. Identidades sintéticas e configurações de exemplo não são contas ou segredos de produção.

P01 pode verificar GetMyProfile por hospedeiro de testes controlado, sem endpoint privado anônimo. P02 integra a identidade real, Gateway e a base backend do BFF; a interface React permanece posterior ao marco de validação do backend.

O banco e as estruturas internas do Keycloak são de responsabilidade do próprio produto e não são alterados pelas migrações de Identity. O serviço Identity mantém somente os dados de aplicação sob sua responsabilidade.

## Alternativas e consequências

OpenIddict com ASP.NET Core Identity permitiria construir a solução de identidade em .NET com mais controle e mais implementação própria. Um provedor gerenciado reduziria parte da operação direta, sujeito a avaliação específica de limites e custos.

Keycloak foi escolhido para utilizar uma solução dedicada e pronta para autenticação. Isso adiciona um componente externo que precisa ser operado, atualizado e monitorado; não altera a decisão de implementar os serviços de negócio em .NET 10.

A opção de SPA web conduzindo diretamente o fluxo foi preterida em favor do BFF, para manter tokens no servidor. A decisão acrescenta responsabilidades explícitas de sessão, CSRF e operação do BFF.

Cadastro administrativo, ativação de artista, coexistência de funções e sessão inicial foram refinados na decisão 0007. Esta decisão 0003 registra a seleção original; a implementação e a verificação de P02 estão registradas em [verificação P02](../verification/p02.md).

## Referências

- https://www.keycloak.org/securing-apps/oidc-layers
- https://www.rfc-editor.org/rfc/rfc8252.html
- https://www.rfc-editor.org/info/rfc9700/
- https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication
