# Decisão 0017 — Página de login do website

Tarefa T10.13; orienta T10.12 e T10.14. Opção (a) aprovada pelo responsável em 03/11/2026 e revisada em 04/11/2026 (retorno por redirecionamento, ver [Revisão](#revisão--retorno-por-redirecionamento-04112026)). Complementa a [decisão 0016](0016-p10-website.md) sem alterar as decisões [0003](0003-authentication.md) e [0007](0007-p02-identity-and-sessions.md).

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
- ~~A ação Entrar reutiliza o fluxo da decisão 0016 em janela própria.~~ Substituído na revisão de 04/11/2026: a ação Entrar redireciona a página inteira para `/bff/login?returnUrl=<destino>`.
- O destino após o login é mantido pela página, no parâmetro `destino`. Somente caminhos internos são aceitos: começam com uma única `/` e não apontam para `/entrar`, `/bff` ou `/api`. Qualquer outro valor leva a Descobrir.
- Usuário já autenticado que abre `/entrar` é levado ao destino.
- O botão Entrar do menu do usuário (T10.12) e o convite de RequireSession levam a `/entrar` com o destino atual.
- O aviso de sessão expirada continua no próprio contexto, com novo login em pop-up, para preservar formulários e chaves de idempotência (decisão 0016). Ele não redireciona a `/entrar`.
- A página não contém campos de credenciais e não armazena dados de sessão no navegador.

## Consequências

- T10.12 não exige alteração de backend. A revisão abaixo acrescenta `returnUrl` ao BFF, sem mudar realm, tokens ou contratos das APIs.
- A tela de credenciais continua com a aparência padrão do Keycloak. A opção (b) pode ser retomada como tarefa própria, sem invalidar esta decisão.
- A opção (c) foi rejeitada: exigiria password grant em cliente do produto e a passagem de senhas pelo React e pelo BFF, contrariando as decisões 0003 e 0007.
- Cadastro e recuperação de senha permanecem fora do recorte.
- A janela própria permanece somente no novo login após sessão expirada.

## Revisão — retorno por redirecionamento (04/11/2026)

**Contexto.** Na validação manual, a janela de login abriu na mesma aba do navegador embutido. Como `/bff/login` terminava sempre em `/bff/session`, o usuário ficou num JSON em vez de voltar ao website autenticado. O fluxo de pop-up depende de como cada navegador trata `window.open` (navegadores embutidos, bloqueadores, COOP, navegadores móveis), e o responsável espera voltar à página principal com as informações do usuário.

**Decisão.**

- `GET /bff/login` aceita `returnUrl` opcional. Sem o parâmetro, o contrato original é mantido e o fluxo termina em `/bff/session`. Com o parâmetro, o BFF exige um caminho local (uma única `/` inicial, sem `\`, sem caracteres de controle, até 2048 caracteres), responde 400 `login.return_url_invalid` caso contrário e, ao concluir o OIDC, redireciona para `Bff:WebOrigin` + caminho.
- `Bff:WebOrigin` é opcional e só existe quando o website não compartilha a origem do BFF (desenvolvimento e integração local: `https://localhost:5173`). Precisa ser uma origem HTTPS sem caminho, query ou credenciais; vazio em publicação de mesma origem, quando o retorno é relativo.
- Falha do provedor em login iniciado pelo website (state válido) volta a `/entrar?erro=login&destino=<caminho>`. Callback forjado ou sem state continua respondendo 401 `login.failed`.
- O website (`/entrar`, convites e ações que exigem conta) navega para `/bff/login?returnUrl=<destino validado>`. A página informa falhas devolvidas pelo provedor e libera o botão ao ser restaurada do histórico.
- O aviso de sessão expirada mantém o novo login em janela própria, para preservar a página, os formulários e as chaves de idempotência (decisão 0016).

**Consequências.** Code + PKCE, tokens somente no servidor, cookie de sessão e duração continuam iguais (decisões 0003 e 0007); não há redirecionamento aberto. A configuração de integração define `Bff__WebOrigin`. O fluxo deixa de depender de pop-up para o login comum.
