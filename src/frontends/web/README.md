# Website InkLink (P10)

Aplicação React nova, organizada por funcionalidades, que consome somente o BFF web. Não importa código, dados ou estado do protótipo. Decisão: [0016](../../../docs/decisions/0016-p10-website.md). Evidências: [verificação P10](../../../docs/verification/p10.md).

## Stack

Vite 7, React 19, TypeScript 5.9 estrito, react-router 7 (BrowserRouter), TanStack Query 5, lucide-react. Testes com Vitest, Testing Library e MSW; verificação no navegador com Playwright (Chromium).

```
src/
  app/          rotas, layout e QueryClient
  features/     session, catalog, engagements, messaging, scheduling, finance
  shared/       cliente HTTP (CSRF, idempotência, problem+json), contratos, formatação, datas, UI
  test/         MSW, fixtures e renderização com rotas
e2e/            checagens visuais e de teclado no navegador
```

## Executar

Pré-requisitos: Node 22+, backend em execução (BFF e Gateway) e um certificado localhost confiável em PEM.

```sh
cd src/frontends/web
npm ci
cp .env.example .env.local   # ajuste INKLINK_BFF_URL e o certificado
npm run dev                  # https://localhost:5173
```

| Variável | Uso |
| --- | --- |
| INKLINK_BFF_URL | BFF de destino do proxy. Desenvolvimento: https://localhost:5192. Ambiente isolado P09: https://localhost:5292. |
| INKLINK_DEV_CERT / INKLINK_DEV_KEY | Certificado e chave PEM do localhost. Os cookies `__Host-` do BFF exigem HTTPS; sem eles o login não se mantém. |
| INKLINK_WEB_PORT | Porta do servidor local (padrão 5173). |
| INKLINK_BFF_INSECURE | `true` apenas se o certificado do BFF não for confiável para o Node. Não usar fora da máquina local. |

O servidor do Vite encaminha `/bff`, `/api`, `/signin-oidc` e `/signout-callback-oidc` ao BFF. O navegador vê uma única origem; tokens nunca chegam ao JavaScript.

## Sessão

- O BFF não aceita `returnUrl` e redireciona `/bff/login` para `/bff/session` (JSON). Para preservar a página, o login abre `/bff/login` em uma janela própria; a aba principal consulta `/bff/session` a cada 1,5 s e fecha a janela quando a sessão existe. Bloqueio de pop-up é informado com instrução.
- O Keycloak devolve o fluxo para `https://localhost:<porta do BFF>/signin-oidc`. Cookies não são isolados por porta, então a sessão criada no BFF vale para o website em `localhost`. O host deve ser o mesmo (`localhost`), não `127.0.0.1`.
- A sessão do BFF dura até 5 minutos, sem renovação. Ao receber 401, a interface abre o aviso de sessão expirada sem descartar a página ou o formulário; as chaves de idempotência são mantidas para que o reenvio não duplique a operação.
- Função Artist: `GET /api/v1/scheduling/artist-context` responde 200 para artistas e 403 para os demais. Não existe troca de personagens; a área Estúdio só aparece quando o backend confirma a função.
- Escritas enviam `X-CSRF-TOKEN` obtido em `/bff/antiforgery`; `csrf.invalid` renova o token uma vez. Logout é `POST /bff/logout`.

## Verificar

```sh
npm run lint
npm run typecheck
npm test
npm run build
PLAYWRIGHT_CHROMIUM_PATH=<chromium opcional> npm run e2e
```

`npm run e2e` sobe o Vite em HTTP na porta 4173 e simula as respostas do backend por `page.route` somente para checar layout (360/768/1280 px), teclado e foco. Capturas ficam em `test-results/screens/`, ignorado pelo Git. Essas respostas não são fonte de dados do produto. O roteiro com backend real está em [verificação P10](../../../docs/verification/p10.md).

## Publicação

`npm run build` gera `dist/`. Requisitos do ambiente:

1. Servir `dist/` na mesma origem HTTPS que responde `/bff`, `/api` e os retornos OIDC, por proxy reverso à frente do BFF (o BFF atual não hospeda arquivos estáticos).
2. Fallback de SPA: qualquer caminho que não seja `/bff`, `/api`, `/signin-oidc`, `/signout-callback-oidc` ou arquivo existente responde `index.html`. Sem isso, recarregar `/mensagens/<id>` resulta em 404.
3. `index.html` sem cache; `assets/*` com cache longo (nomes com hash).

`npm run preview` reproduz esse arranjo localmente com o mesmo proxy.

## Limitações conhecidas

- Não existe `GET /artists/me`. O identificador de catálogo do artista vem da resposta da publicação do perfil ou de um atendimento e é lembrado em `localStorage` apenas como conveniência; perdê-lo exige publicar o perfil novamente para recuperar a edição de obras.
- O cliente consulta reservas; somente o artista confirma horários em Agenda. O combinado do cliente acontece na conversa.
- Mensagens são atualizadas por consulta a cada 4 s com `resumeCursor`; não há push.
- Sugestões de filtros (cidade e estilo) são derivadas da primeira página de `/api/v1/artists?limit=100`.
