# Decisão 0019 — Renovação da sessão web

Tarefa T10.19; orienta T10.20. **Situação: aprovada e implementada.** Os limites foram aprovados pelo responsável em 04/11/2026: 30 minutos sem uso e 8 horas no total. A abordagem técnica foi definida na execução de T10.19–T10.20, que o responsável delegou. Revisa a seção de sessão da [decisão 0007](0007-p02-identity-and-sessions.md), que limitava a sessão a cinco minutos sem renovação. Não altera as decisões [0003](0003-authentication.md) e [0017](0017-p10-web-login.md).

## Contexto

O cookie `__Host-inklink-session` durava no máximo cinco minutos, igual ao access token, e não era renovado. O usuário precisava entrar de novo a cada cinco minutos. O refresh token emitido pelo Keycloak já ficava no ticket protegido do servidor, sem uso.

## Decisão

- **Inatividade:** a sessão expira após 30 minutos sem requisições autenticadas ao BFF.
- **Limite total:** a sessão termina, no máximo, 8 horas após o login, mesmo com uso contínuo. O limite é gravado no ticket no login e não se estende.
- **Renovação do access token:** a cada requisição autenticada (`/bff/session`, `/bff/antiforgery`, `/bff/logout` e chamadas de API encaminhadas), se o access token vencer em até 60 segundos, o BFF o renova no servidor com `grant_type=refresh_token`. O navegador nunca recebe access token nem refresh token.
- **Validade deslizante:** a validade do ticket passa a ser o menor valor entre agora + 30 minutos e o limite total. O ticket é regravado no PostgreSQL quando a validade avança pelo menos um minuto ou quando os tokens mudam.
- **Rotação e uso único:** o realm habilita `revokeRefreshToken` com `refreshTokenMaxReuse = 0`; cada renovação recebe um refresh token novo e invalida o anterior.
- **Renovações concorrentes:** a renovação de uma sessão é serializada por bloqueio consultivo do PostgreSQL (`pg_advisory_xact_lock`) sobre a chave da sessão. Depois do bloqueio, o BFF relê o ticket; se outra requisição ou outra instância já renovou, reaproveita o resultado. Requisições paralelas não queimam o refresh token nem encerram a sessão por engano.
- **Falhas:**
  - `invalid_grant` (refresh token revogado, expirado ou reutilizado) encerra a sessão: o ticket é removido e a requisição recebe 401 `session.required`. O website mostra o aviso de sessão expirada, que preserva a página.
  - Falha transitória do provedor (rede ou 5xx) não encerra a sessão: a requisição segue com o token atual; se ele já tiver vencido, a API responde 401 e a próxima requisição tenta renovar de novo.
- **Keycloak:** `ssoSessionIdleTimeout = 1800` e `ssoSessionMaxLifespan = 28800` permanecem iguais aos limites do BFF; `accessTokenLifespan` continua em 300 segundos.
- **Saída:** inalterada. `POST /bff/logout` remove o ticket e encerra a sessão no Keycloak, o que revoga o refresh token.

## Consequências

- O usuário não precisa entrar novamente enquanto usar o site, até o limite de 8 horas.
- O PostgreSQL do BFF continua participando da disponibilidade da sessão e passa a serializar as renovações.
- O relógio do BFF é injetável (`TimeProvider`), o que permite testar inatividade, limite total e renovação sem esperar o tempo real.
- Aplicativos móveis não são afetados: eles usam o próprio fluxo Code + PKCE (decisão 0003).
- O texto do aviso de sessão expirada no website passa a citar os novos limites.
