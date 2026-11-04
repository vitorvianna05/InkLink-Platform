# Decisão 0017 — Página de login do website

Tarefa T10.13; orienta T10.12 e T10.14. Opção (a) aprovada pelo responsável em 03/11/2026. Complementa a [decisão 0016](0016-p10-website.md) sem alterar as decisões [0003](0003-authentication.md) e [0007](0007-p02-identity-and-sessions.md).

## Contexto

O website não tinha página de entrada própria: o botão Entrar e o convite das áreas privadas abriam diretamente o login do provedor. O responsável pediu uma página de login dividida 50/50 com a identidade visual do InkLink.

O backend já oferece o login web completo pelo BFF: `GET /bff/login` inicia Authorization Code + PKCE no Keycloak e termina em `/bff/session` (JSON, sem `returnUrl`); `GET /bff/session`, `GET /bff/antiforgery` e `POST /bff/logout` completam o ciclo. Tokens ficam no BFF e o cookie `__Host-inklink-session` dura até cinco minutos, sem renovação. Não há endpoint que receba usuário e senha. O realm `inklink` usa o tema padrão do Keycloak, com cadastro e recuperação de senha desativados.

## Alternativas

- (a) Página React `/entrar` que apresenta a marca e inicia o fluxo atual do BFF; as credenciais são digitadas no Keycloak.
- (b) A opção (a) com um tema de login do Keycloak na identidade InkLink, versionado em `src/backend/identity-provider`.
- (c) Formulário de usuário e senha no React enviado ao BFF, que obteria tokens por password grant.

## Decisão

Adotar a opção (a).

- A rota pública `/entrar` fica fora do layout principal e é dividida em duas metades: painel de marca à esquerda e ação de entrada à direita. Até 860 px os painéis são empilhados.
- A ação Entrar reutiliza o fluxo da decisão 0016: abre `/bff/login` em janela própria, consulta `/bff/session` até a sessão existir e fecha a janela. Pop-up bloqueado, cancelamento (janela fechada) e tempo esgotado são estados informados na própria página.
- O destino após o login é mantido pela página, no parâmetro `destino`. Somente caminhos internos são aceitos: começam com uma única `/` e não apontam para `/entrar`, `/bff` ou `/api`. Qualquer outro valor leva a Descobrir.
- Usuário já autenticado que abre `/entrar` é levado ao destino.
- O botão Entrar do menu do usuário (T10.12) e o convite de RequireSession levam a `/entrar` com o destino atual.
- O aviso de sessão expirada continua no próprio contexto, com novo login em pop-up, para preservar formulários e chaves de idempotência (decisão 0016). Ele não redireciona a `/entrar`.
- A página não contém campos de credenciais e não armazena dados de sessão no navegador.

## Consequências

- Nenhuma alteração de backend, realm ou contratos é necessária para T10.12 e T10.14.
- A tela de credenciais continua com a aparência padrão do Keycloak. A opção (b) pode ser retomada como tarefa própria, sem invalidar esta decisão.
- A opção (c) foi rejeitada: exigiria password grant em cliente do produto e a passagem de senhas pelo React e pelo BFF, contrariando as decisões 0003 e 0007.
- Cadastro e recuperação de senha permanecem fora do recorte.
- A dependência de pop-up permanece: navegadores que o bloqueiam exigem permissão do usuário, como já ocorre hoje.
