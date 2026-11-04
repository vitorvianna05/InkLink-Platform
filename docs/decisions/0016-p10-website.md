# Decisão 0016 — Website React

Tarefas T10.01–T10.10. Escolhas de escopo aprovadas pelo responsável em 04/11/2026: o cliente consulta reservas e o artista confirma; telas mínimas de gestão do artista entram em P10; o backend não é alterado nesta etapa.

## Build, rotas e organização

Aplicação nova em src/frontends/web com Vite, React 19 e TypeScript estrito. Rotas por endereço com react-router (BrowserRouter), sem estado global de personagens. Estrutura por funcionalidade (session, catalog, engagements, messaging, scheduling, finance) e camada shared para HTTP, contratos, formatação, datas e componentes de interface. O protótipo é apenas referência visual e de navegação: nenhum componente, dado de demonstração ou armazenamento foi importado.

## Transporte e cache

O website fala somente com o BFF, na mesma origem. Em desenvolvimento, o servidor do Vite encaminha /bff, /api e os retornos OIDC ao BFF configurado por INKLINK_BFF_URL; em publicação, um proxy reverso serve dist/ e encaminha os mesmos caminhos. Tokens permanecem no BFF.

O cliente HTTP envia X-CSRF-TOKEN em escritas (renovação única em csrf.invalid), Idempotency-Key UUID por intenção de escrita, interpreta problem+json com code e traceId e distingue falha transitória (rede, 502, 503, 504) de erro de negócio. TanStack Query é o cache: somente falhas transitórias são repetidas automaticamente; 202 é tratado como operação pendente e acompanhado por consulta até estado final. Mensagens usam resumeCursor a cada 4 s. Datas civis e horários são convertidos no fuso IANA do artista; horário ambíguo ou inexistente é recusado na interface antes do envio.

## Sessão e áreas

O BFF não aceita returnUrl e /bff/login termina em /bff/session (JSON). Sem alterar o backend, o login abre /bff/login em janela própria e a página principal consulta /bff/session até a sessão existir, fechando a janela em seguida. Cookies não são isolados por porta: em localhost, a sessão criada no host do BFF vale para o website. Pop-up bloqueado é informado.

A sessão do BFF dura até 5 minutos e não é renovada. 401 abre um aviso de sessão expirada que preserva página e formulário; as chaves de idempotência são mantidas para que o reenvio após novo login não duplique a operação. Nenhum dado é mantido em armazenamento local como fonte oficial.

A área Estúdio depende da função Artist confirmada pelo backend: GET /api/v1/scheduling/artist-context responde 200 ao artista e 403 aos demais. GET /api/v1/profiles/me vincula a identidade e provisiona Client no primeiro acesso.

## Fluxos

- Cliente: Descobrir, Explorar (sugestões por combobox acessível, filtros, busca explícita, favoritos), perfil público com tatuagens e flashes, pedido com ou sem flash, Meu espaço, atendimento com proposta e aceite explícito, conversas em páginas distintas e consulta de reservas.
- Artista: painel por período com acesso aos atendimentos e conversas, agenda mensal com disponibilidade e confirmação de reserva, conclusão de sessão, encerramento com confirmação e retentativa, lançamentos financeiros com estorno, perfil público e gestão de tatuagens e flashes.
- Reserva: somente o artista confirma em Agenda. O cliente vê suas reservas e combina o horário na conversa. Encerramento pendente, rejeitado e definitivo são estados distintos; encerramento recusado por reservas futuras é explicado.

## Limitações aceitas

Não existe GET /artists/me: o identificador de catálogo do artista vem da publicação do perfil ou de um atendimento e é lembrado em localStorage apenas como conveniência. Filtros de cidade e estilo são derivados da primeira página de /api/v1/artists. O BFF não hospeda arquivos estáticos; a publicação exige proxy reverso com fallback de SPA para index.html. Cada limitação pode ser removida em incremento de backend próprio.
