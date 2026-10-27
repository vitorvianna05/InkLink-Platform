# Tarefas de implementação do InkLink

## 1. Objetivo e convenções

Este documento detalha o [plan.md](plan.md), com referência ao [requirements.md](requirements.md) e ao [architecture.md](architecture.md). A nova aplicação será criada do zero; o protótipo é referência de experiência e não uma implementação a migrar.

O backend .NET 10 tem prioridade nas etapas P00–P09. O website React será implementado após a validação do backend em P10. A base React Native e as entregas iOS e Android aparecem em P11–P13.

Cada linha abaixo é uma tarefa com ID estável, dependências diretas, entregável e aceite verificável. Os itens começam pendentes e são atualizados conforme as decisões e entregas forem verificadas. Criar este documento não inicia implementação ou seleciona ferramentas ainda em aberto.

- IDs seguem o formato `T00.01`, vinculado à etapa P00. Preservar os IDs ao revisar ou dividir tarefas e registrar relações de substituição quando necessário.
- As dependências exigem resultados disponíveis, não apenas arquivos criados. As referências documentais são entradas implícitas de todas as tarefas.
- Cada tarefa de decisão deve produzir registro revisável com contexto, alternativas, escolha e consequências. Uma política de produto não definida não deve ser presumida pelo implementador.
- Cada tarefa de implementação inclui testes proporcionais ao comportamento, atualização de contratos e configuração pertinente. Tarefas de verificação consolidam evidências e não adiam testes, autorização ou tratamento de falhas.
- Para concluir, marcar o checkbox e registrar evidência no item ou no sistema de acompanhamento: decisão revisada, teste, commit, revisão ou publicação conforme o caso. Código ou tela sem comportamento validado não basta.
- Responsável, estimativa e situação intermediária podem ser adicionados posteriormente. Nenhum prazo ou atribuição foi presumido.
- A sequência principal segue o plano. Trabalho independente pode ser antecipado quando suas dependências estiverem atendidas, preservando o marco backend antes de P10.
- Se uma tarefa não couber em uma entrega pequena, dividi-la antes da execução. Alterações devem manter coerência entre os documentos de SDD.

## 2. Etapas de backend

### P00 — Preparação e decisões mínimas

**Referências:** RNF01, RNF04, RNF07 e RNF08. **Marco:** M0. **Situação:** concluída; T00.01 a T00.07 verificadas no nível de decisão e revisão.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T00.01 — [x] Definir destino da nova solução | Nenhuma tarefa anterior | Caminho `C:\Users\INFIS001125\Documents\ChatGPT\InkLink-Platform`, repositório próprio `InkLink-Platform`, documentação em `docs/` e código em `src/`. | Decisão aprovada pelo responsável; pasta, separação e transferência do SDD verificadas. Git local independente inicializado. Repositório remoto criado no GitHub como `vitorvianna05/InkLink-Platform`. |
| T00.02 — [x] Definir organização e primeiro caso de uso | T00.01 | Estrutura por serviço em src/backend e frontends em src/frontends; nomes técnicos e camadas definidos; primeiro caso GetMyProfile em Identity. | Sugestões aprovadas pelo responsável. Entrada, saída, validação e execução controlada registradas em [decisão 0001](decisions/0001-organization.md). |
| T00.03 — [x] Selecionar persistência e migrações | T00.02 | PostgreSQL, EF Core 10 e Npgsql 10, bancos e migrações por serviço, proteção de intervalos na Agenda. | Proposta aprovada e registrada na [decisão 0002](decisions/0002-persistence.md). Testes de concorrência usarão PostgreSQL real na implementação. |
| T00.04 — [x] Selecionar autenticação e identidade | T00.02 | Keycloak, OIDC/OAuth 2.0, Authorization Code com PKCE, JWT nas APIs e BFF web. | Decisão aprovada e registrada em [decisão 0003](decisions/0003-authentication.md), incluindo identidade interna e autorização por serviço. |
| T00.05 — [x] Definir ferramentas locais e de testes | T00.03, T00.04 | SDK .NET 10, Compose, xUnit, WebApplicationFactory, Testcontainers PostgreSQL, ArchUnitNET, User Secrets, analisadores e GitHub Actions. | Ferramentas aprovadas na [decisão 0004](decisions/0004-tooling.md). Instalação/configuração e validação do motor Docker serão verificadas na preparação da implementação. |
| T00.06 — [x] Definir convenções de API | T00.02 | JSON/camelCase, recursos em inglês, UUIDv7, Problem Details, cursor, idempotência seletiva, OpenAPI e /api/v1. | Convenções aprovadas pelo responsável e registradas na [decisão 0005](decisions/0005-api-conventions.md). |
| T00.07 — [x] Revisar as decisões para iniciar P01 | T00.03, T00.04, T00.05, T00.06 | Decisões consolidadas, GetMyProfile delimitado e pré-requisitos operacionais identificados. | [Revisão P00](decisions/0006-p00-review.md) concluída; implementação e validação do ambiente continuam nas tarefas de P01. |

#### Registro da decisão T00.01 — 27/10/2026

- Destino local aprovado e criado: `C:\Users\INFIS001125\Documents\ChatGPT\InkLink-Platform`.
- Repositório próprio aprovado: `InkLink-Platform`; Git local inicializado na branch `main`.
- Documentação SDD oficial transferida para `docs/`: requirements.md, architecture.md, plan.md e tasks.md.
- Código da nova aplicação ficará em `src/`, atualmente sem implementação.
- Código, artefatos e Git do protótipo continuam no projeto original, sem cópia para a nova aplicação.
- A transferência dos quatro documentos foi verificada por SHA-256 antes das atualizações desta decisão.
- A URL prevista do remoto é `https://github.com/vitorvianna05/InkLink-Platform.git`. O repositório remoto foi criado pelo responsável e está conectado ao Git local.
- Evidência: autorização do responsável para o destino sugerido e verificação da estrutura local. T00.01 é uma tarefa de definição; a configuração do remoto não é apresentada como publicação concluída.

**Conclusão de P00:** decisões mínimas disponíveis sem presumir ferramentas futuras.

### P01 — Fundação técnica do primeiro serviço

**Referências:** RNF01, RNF04–RNF08. **Marco:** M1. **Situação:** concluída; evidências em [verificação P01](verification/p01.md).

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T01.01 — [x] Inicializar Identidade em .NET 10 | T00.07 | Solução nova com projetos necessários, composição e referências de camadas. | Build passa em net10.0; Domain não depende de transporte ou infraestrutura. Nenhum projeto referencia o código do protótipo. |
| T01.02 — [x] Configurar ambientes e segredos | T01.01 | Configuração de desenvolvimento/teste e exemplos sem credenciais. | Ausência de configuração obrigatória é identificada sem expor segredos. Não há substituição silenciosa de ambiente por configurações de teste. |
| T01.03 — [x] Criar a persistência privada inicial | T01.02 | Modelo de perfil, migração inicial e credencial do serviço. | Um banco limpo recebe a migração, nova execução não duplica estruturas e a credencial não permite acesso aos dados privados de outros contextos. |
| T01.04 — [x] Implementar o caso controlado de perfil | T01.03 | Caso de uso escolhido em P00 com validação e persistência real. | Teste de integração cria e consulta o perfil e rejeita dados inválidos. Antes de P02, o teste pode usar hospedeiro controlado, sem endpoint privado anônimo. |
| T01.05 — [x] Implementar erros e sinais operacionais | T01.04 | Tratamento de erros, saúde, prontidão, logs estruturados e correlação. | Erros respeitam T00.06 sem stack trace ou segredos; sinais distinguem processo ativo e dependência indisponível; uma requisição é rastreável. |
| T01.06 — [x] Configurar testes e restrições arquiteturais | T01.05 | Suítes unitária, integração e verificação da direção de dependências. | A execução é repetível; um cenário controlado comprova que referência proibida entre camadas é detectada. |
| T01.07 — [x] Automatizar build e validar execução limpa | T01.06 | Pipeline inicial e instruções de restauração, migração, execução e testes. | Um ambiente limpo executa a sequência documentada e falhas de teste impedem aprovação do build. |

**Evidência:** [verificação por tarefa, comandos e resultados](verification/p01.md); [execução local](development/identity.md). 29 testes aprovados, build Release sem avisos/erros e validação em cópia limpa. Workflow preparado; execução remota após envio ao GitHub. M1 ainda depende de P02.

**Conclusão de P01:** primeiro serviço executável e verificável, com persistência e responsabilidades reais.

**Revisão dos contratos:** os desvios de Problem Details por Accept e de schemas/respostas OpenAPI foram corrigidos. A suíte atual passou com 38 testes; detalhes na [verificação P01](verification/p01.md#correção-dos-contratos-após-revisão--27102026).

### P02 — Identidade, funções e entrada das APIs

**Referências:** RF01 e RF21; RNF04, RNF05, RNF07 e RNF08. **Marco:** M1. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T02.01 — [ ] Integrar validação da autenticação | T01.07 | Middleware/adaptador da solução escolhida e identificação do solicitante. | Credencial válida identifica o usuário; credenciais ausentes, inválidas ou expiradas não autorizam ações privadas. |
| T02.02 — [ ] Vincular conta interna e funções | T02.01 | Associação estável de identidade externa, usuário interno e funções autorizadas. | Reidentificação não duplica conta; alterar corpo da requisição não concede função ou acesso a outra identidade. Política de cadastro e vínculo de artista está definida. |
| T02.03 — [ ] Expor o próprio perfil com autorização | T02.02 | Consulta e atualização dos campos de perfil aprovados para a entrega. | Usuário não altera identidade ou permissões pelo formulário nem acessa perfil privado de terceiro. Validação e erros são testados. |
| T02.04 — [ ] Configurar Gateway e a base do BFF web | T02.01, T00.06 | Decisão de Gateway, rota de Identity e BFF .NET com login, sessão protegida, tokens no servidor e defesa CSRF apropriada. | APIs validam identidade; login/saída/expiração e requisições de sessão são testáveis sem frontend React. BFF não concentra regras de negócio. |
| T02.05 — [ ] Definir identidade técnica entre serviços | T02.02, T02.04 | Contrato de autenticação e autorização para chamadas técnicas das primeiras integrações. | Chamador técnico só executa operações previstas; identidade de usuário não concede automaticamente capacidade de atuar como serviço. |
| T02.06 — [ ] Verificar isolamento no Gateway e serviço | T02.03, T02.04, T02.05 | Testes com duas contas e funções distintas, incluindo acesso direto ao serviço. | Mudar identificador, sessão ou rota não expõe outra conta. A autorização não depende somente da passagem pelo Gateway. |

**Conclusão de P02:** operações privadas protegidas e identidade adequada às integrações.

### P03 — Catálogo, pesquisa e favoritos

**Referências:** RF02–RF08 e RF21; RNF04–RNF08. **Marco:** M2. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T03.01 — [ ] Criar Catálogo e sua persistência | T02.06 | Serviço independente, modelo público inicial, migrações e rota no Gateway. | Build e migração passam sem referência à implementação ou banco de Identidade; vínculos de usuário usam identificadores. |
| T03.02 — [ ] Definir alimentação e mídia do catálogo | T03.01 | Contrato mínimo autorizado de perfil, trabalhos e flashes e tratamento das referências de mídia. | Define obrigatoriedade e quem publica, sem presumir painel administrativo completo. Dados sintéticos permanecem separados de produção. |
| T03.03 — [ ] Implementar dados públicos e consultas de perfil | T03.02 | Operações mínimas de gravação aprovadas e consultas de artista, tatuagens e flashes. | Outro artista não altera a publicação; identificador inexistente não retorna outro perfil; dados privados não aparecem nas consultas públicas. |
| T03.04 — [ ] Implementar listagem de descoberta | T03.03, T00.06 | Consulta paginada de artistas, com ordenação estável. | Limites e páginas são respeitados; catálogo vazio retorna situação explícita sem conteúdo fictício automático. |
| T03.05 — [ ] Escolher pesquisa e implementar filtros | T03.04 | Decisão do mecanismo e consulta por termo, estilo, cidade e preço inicial. | Testes combinam critérios e verificam limite de preço e ausência de resultados; índice, se adotado, tem fonte oficial e reconstrução definidas. |
| T03.06 — [ ] Implementar sugestões aproximadas | T03.05 | Consulta de autocompletar por nome, estúdio, estilo e cidade com categoria e relevância. | Testes cobrem prefixos, acentos, pequenas diferenças de escrita, ausência de resultado e duplicação. Não há sugestões privadas ou automáticas com termo vazio. |
| T03.07 — [ ] Implementar favoritos persistentes | T03.03, T02.06 | Inclusão, remoção e listagem de favoritos por cliente. | Repetir inclusão não duplica; limpar pesquisa não remove; duas contas comprovam isolamento e persistência após novo acesso. |

**Conclusão de P03:** catálogo e favoritos utilizáveis por API sem interface gráfica.

### P04 — Solicitações, propostas e aceite

**Referências:** RF07, RF09, RF10 e RF21; RNF04–RNF06. **Marco:** M2. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T04.01 — [ ] Especificar estados e habilitação inicial de chat | T03.07 | Transições de pedido/proposta, participantes e momento de criação e escrita da conversa. | O fluxo permite negociar sem exigir um aceite que dependa da própria conversa; as permissões ainda não decididas são registradas antes do código dependente. |
| T04.02 — [ ] Criar Atendimentos e a solicitação | T04.01 | Persistência privada e comando de pedido com ideia, região, tamanho e referência ao flash. | Dados inválidos não criam pedidos; artista e flash escolhidos são preservados, sem consultar bancos alheios. A política de snapshot do contexto é explícita. |
| T04.03 — [ ] Implementar apresentação de proposta | T04.02 | Operação do artista para registrar descrição, valor e vínculo ao atendimento. | Outro artista não apresenta proposta nesse atendimento; valores inválidos são rejeitados e alterações concorrentes têm tratamento definido. |
| T04.04 — [ ] Implementar aceite específico e idempotente | T04.03 | Confirmação pelo cliente correto, vinculada à proposta e protegida contra repetição. | Repetir aceite não duplica efeitos; aceitar uma proposta não altera outra e não registra receita nem reserva horário. |
| T04.05 — [ ] Implementar consultas das negociações | T04.02, T04.04 | Listagem e detalhe de pedidos e propostas por participante. | Cliente e artista consultam o mesmo contexto; terceiro não tem acesso e novo acesso preserva o aceite. |
| T04.06 — [ ] Definir contratos com Conversas e Agenda | T04.01, T04.05, T02.05 | Contratos mínimos de integração, identificadores, validação, erros e idempotência. | Contratos são verificáveis sem entidades internas compartilhadas. Não se publica evento sem o mecanismo confiável a implementar em P05. |

**Conclusão de P04:** negociação executável e persistida por API, isolada por proposta e atendimento.

### P05 — Conversas e primeira integração confiável

**Referências:** RF11–RF14 e RF21; RNF04–RNF06. **Marco:** M3. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T05.01 — [ ] Escolher mensageria e contrato de eventos | T04.06 | Decisão de broker e ferramentas, envelope com versão e contexto, correlação e política de repetição/ordenação. | Um exemplo válido, duplicado e incompatível tem tratamento definido; não se pressupõe entrega exatamente uma vez ou ordenação global. |
| T05.02 — [ ] Implementar Outbox no produtor inicial | T05.01 | Evento e dados de atendimento na mesma transação e publicador recuperável. | Rollback não deixa evento de fato inexistente; falha de publicação mantém registro recuperável e repetição não perde o fato original. |
| T05.03 — [ ] Criar Conversas com consumo idempotente | T05.01, T02.06 | Persistência privada de conversa/participantes e deduplicação coordenada à gravação local. | Falha antes da confirmação e repetição do evento não duplicam conversa nem registram consumo sem o efeito correspondente. |
| T05.04 — [ ] Integrar criação e habilitação da conversa | T05.02, T05.03 | Processo que aplica T04.01 e vincula participantes, conversa e atendimento. | Repetição não cria contexto duplicado; falha temporária permite retomada; chamador técnico indevido não habilita escrita. |
| T05.05 — [ ] Implementar listagem e leitura individual | T05.04 | APIs de lista com interlocutor/última mensagem e detalhe paginado autorizado. | Detalhe não mistura atendimentos; um terceiro não acessa o histórico alterando identificadores. Lista vazia é explícita. |
| T05.06 — [ ] Implementar envio com autoria e atualização | T05.05 | Comando de mensagem, política de repetição e decisão de transporte para atualização do histórico. | Autor deriva da identidade autenticada; branco é rejeitado; os dois participantes recuperam o mesmo registro após interrupção sem duplicação indevida. |
| T05.07 — [ ] Implementar bloqueio local da escrita | T05.06, T04.06 | Operação técnica idempotente de bloqueio por atendimento, atômica em relação à aceitação de mensagens. | Corrida entre mensagem e bloqueio tem ordem consistente; depois da confirmação de bloqueio não há nova gravação e repetir comando não reabre escrita. |
| T05.08 — [ ] Verificar falhas, repetição e acesso | T05.07 | Suíte de broker indisponível, publicação/consumo repetidos, interrupção e tentativa por terceiros. | Histórico não se perde ou duplica nos cenários; falhas são diagnosticáveis e recuperáveis. O encerramento distribuído ainda depende de P07. |

**Conclusão de P05:** conversas autorizadas, autoria preservada e integração recuperável com bloqueio local verificado.

### P06 — Agenda e concorrência

**Referências:** RF15, RF18, RF19 e RF21; RNF04–RNF06. **Marco:** M3. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T06.01 — [ ] Definir disponibilidade e horários | T04.06, T00.03 | Regras iniciais de dias, intervalos, fuso, precisão e autorização das reservas. | Exemplos válidos, inválidos e adjacentes são inequívocos; não se herda a janela fixa de horários do protótipo. |
| T06.02 — [ ] Criar Agenda e consultar calendário | T06.01, T02.06 | Persistência privada, migrações, disponibilidade e consultas de reservas por artista/data/período. | Há vínculo ao atendimento e consultas autorizadas ordenadas; uma consulta livre não é apresentada como garantia de reserva. |
| T06.03 — [ ] Confirmar reservas com proteção transacional | T06.02 | Comando com validação, idempotência e proteção de sobreposição adequada ao banco. | Reserva válida é persistida uma vez; intervalo inválido ou fora da disponibilidade é rejeitado e timeout permite consultar o resultado anterior. |
| T06.04 — [ ] Testar intervalos e requisições concorrentes | T06.03 | Testes contra o banco real escolhido, inclusive solicitações simultâneas. | Adjacência é aceita; sobreposições parciais, contidas e envolventes são rejeitadas. Duas reservas conflitantes não são confirmadas simultaneamente. |
| T06.05 — [ ] Publicar confirmação com Outbox | T06.03, T05.02, T05.03 | Publicação recuperável e contrato da confirmação para Atendimentos. | Rollback não publica sucesso; entrega duplicada não duplica efeitos. Contrato diferencia resultado da reserva e coordenação posterior. |
| T06.06 — [ ] Verificar reserva nas duas perspectivas | T06.02, T06.04, T06.05 | Testes de cliente e artista consultando a mesma reserva e rejeição de terceiro. | Os intervalos são os mesmos após novo acesso; autorização existe no serviço e não somente no Gateway. |

**Conclusão de P06:** reserva consistente sob concorrência e visível às partes autorizadas.

### P07 — Ciclo integrado e recuperação

**Referências:** RF09–RF15, RF18, RF19 e RF21; RNF04–RNF06. **Marco:** M4. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T07.01 — [ ] Especificar encerramento e suas permissões | T05.08, T06.06 | Decisão de quem encerra, protocolo de bloqueio, estados técnicos e confirmação final. | Timeout ou pendência não equivalem a encerramento concluído; confirmação depende do bloqueio durável em Conversas. |
| T07.02 — [ ] Implementar coordenação durável de encerramento | T07.01 | Processo em Atendimentos com solicitação autorizada e comando idempotente de bloqueio. | Repetir pedido não cria processos incompatíveis. O estado final só é alcançado após a confirmação de Conversas. |
| T07.03 — [ ] Implementar retomada e reconciliação | T07.02 | Detecção e recuperação de processos pendentes com diagnóstico de etapas. | Falha entre bloqueio e conclusão não libera escrita; reiniciar o processo permite concluir sem duplicar efeitos ou apagar histórico. |
| T07.04 — [ ] Definir e integrar novo atendimento | T07.03, T06.05 | Protocolo e implementação da reserva confirmada, novo contexto e habilitação de conversa; decisão de compensação quando necessária. | Consultar perfil ou pedir orçamento não reabre sozinho o chat; contexto antigo permanece rastreável e falha posterior à reserva tem tratamento explícito. |
| T07.05 — [ ] Testar corridas e eventos atrasados | T07.04 | Cenários de envio simultâneo, confirmação perdida, processo reiniciado e eventos fora de ordem. | Mensagem não é gravada após encerramento confirmado; evento antigo não habilita nem bloqueia outro atendimento; repetição não duplica reserva. |
| T07.06 — [ ] Demonstrar o fluxo completo via API | T07.05 | Roteiro de solicitação, proposta, conversa, reserva, encerramento e novo atendimento com duas identidades. | As partes autorizadas completam o roteiro; terceiros e transições inválidas são rejeitados; pendências podem ser identificadas e retomadas. |

**Conclusão de P07:** ciclo integrado com bloqueio seguro e comportamento definido diante de falhas parciais.

### P08 — Gestão financeira e dashboard

**Referências:** RF16, RF17, RF20 e RF21; RNF04–RNF06. **Marco:** M5. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T08.01 — [ ] Especificar lançamentos e sessões | T07.06 | Contrato mínimo de receitas, custos, datas, moeda e registro de sessões concluídas. | Recebimento, conclusão de sessão e encerramento do chat são distintos; não há processamento de pagamento no site. |
| T08.02 — [ ] Criar Gestão financeira e receitas | T08.01, T02.06 | Serviço, migrações privadas e registro gerencial autorizado de recebimento. | Registro persiste com data e origem; repetir operação não duplica receita e reserva ou aceite não criam lançamento automaticamente. |
| T08.03 — [ ] Registrar custos e consultar lançamentos | T08.02 | Operações validadas e listagem por artista/período com paginação. | Entrada inválida não é gravada e outro artista não lê ou altera o lançamento. |
| T08.04 — [ ] Implementar indicadores financeiros | T08.03 | Consultas de receita, custos, lucro e margem com referência temporal explícita. | Base de R$ 4.000,00 e R$ 940,00 retorna R$ 3.060,00 e 76,5%; período vazio não divide por zero e recebimento respeita sua data. |
| T08.05 — [ ] Integrar dados operacionais do dashboard | T08.01, T08.04, T06.05, T05.05 | Composição contratada ou projeções de sessões, clientes distintos, ticket, reservas e conversas ativas. | Não há leitura de tabelas alheias; atraso ou falha de uma origem não se apresenta como zero real sem indicação. Valor agendado não compõe receita recebida. |
| T08.06 — [ ] Verificar reconstrução e isolamento | T08.05 | Testes de reprocessamento, períodos distintos e dois artistas. | Projeções reconstruídas mantêm totais sem duplicar lançamentos; clientes são distintos, datas corretas e nenhum indicador vaza entre artistas. |

**Conclusão de P08:** resultados gerenciais verificáveis por artista e período sem confundir previsão e recebimento.

### P09 — Validação e disponibilização do backend

**Referências:** RF01–RF21 nas operações de backend; RNF01 e RNF04–RNF08. **Marco:** M5. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T09.01 — [ ] Consolidar os contratos dos consumidores | T08.06 | Documentação e exemplos de APIs, eventos, paginação, limites, erros e operações em processamento. | Exemplos de sucesso e falha são verificáveis sem o protótipo; a política de compatibilidade está registrada. |
| T09.02 — [ ] Executar regressão integrada e revisão de acesso | T09.01 | Suíte e relatório dos fluxos das duas funções, incluindo autorização nos serviços. | Roteiro de P07 e indicadores de P08 passam em ambiente integrado; critérios não atendidos são identificados em vez de omitidos. |
| T09.03 — [ ] Verificar observabilidade e recuperação | T09.02 | Cenários de interrupção com correlação, Outbox pendente e processos incompletos visíveis. | Uma operação pode ser rastreada entre serviços sem expor conteúdo privado; retomada não duplica escrita ou perde fatos. |
| T09.04 — [ ] Preparar ambiente de integração reproduzível | T09.03 | Ambiente com HTTPS, configurações, migrações e dados sintéticos separados; instruções de execução. | Ambiente limpo inicia os componentes necessários, sem credenciais versionadas ou dependência de armazenamento do navegador. |
| T09.05 — [ ] Demonstrar implantação independente | T09.04 | Publicação de um serviço com contrato compatível e verificação pós-publicação. | O serviço pode ser atualizado sem republicar todos os demais e mantendo os fluxos existentes; instruções registram o procedimento. |
| T09.06 — [ ] Registrar aceite do marco backend | T09.05 | Checklist dos requisitos demonstrados, evidências e limitações da entrega. | Falhas críticas de acesso, conflito de reserva ou bloqueio de chat impedem M5. Só após esse aceite P10 pode começar. |

**Conclusão de P09:** backend integrado pronto para consumo real pelos frontends; não exige antecipar toda a infraestrutura de escala futura.

## 3. Etapas de frontend após o backend

### P10 — Website React

**Referências:** RF01–RF21; RNF01–RNF08, especialmente RNF02 e RNF03. **Marco:** M6. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T10.01 — [ ] Definir e inicializar o website | T09.06 | Escolhas de build, rotas, transporte e cache e aplicação React nova por funcionalidades. | Executa e compila com backend configurado por ambiente, sem importar componentes ou dados de demonstração do protótipo. |
| T10.02 — [ ] Implementar sessão e áreas por função | T10.01, T02.06 | Autenticação, navegação e encerramento/expiração de sessão em cliente e artista. | Identidade permanece coerente; sessão expirada é tratada sem exposição de dados e não existe troca fictícia de personagens como autorização. |
| T10.03 — [ ] Construir Descobrir e perfil público | T10.02, T03.04 | Cards e apresentação, tatuagens e flashes via Catálogo, preservando contexto ao abrir e fechar perfil. | Artista escolhido é o exibido; loading, erro e vazio são distintos e não geram catálogo fictício. |
| T10.04 — [ ] Construir Explorar e favoritos | T10.03, T03.06, T03.07 | Autocompletar no campo livre, filtros, busca explícita, resultados e favorito persistente. | Inicialmente não há cards; teclado opera sugestões; resposta antiga não substitui busca recente e limpar critérios não remove favoritos. |
| T10.05 — [ ] Construir Meu espaço e negociação | T10.04, T04.05 | Pedidos do usuário, formulário com flash, apresentação de proposta e aceite explícito. | Cada pedido abre o contexto correto; validação é informada e falha não vira sucesso. Aceite não efetua cobrança ou reserva. |
| T10.06 — [ ] Construir lista e leitura de conversas | T10.05, T05.06 | Páginas distintas, histórico com autoria, envio e retorno à listagem para cliente/artista. | Recarregar endereço válido mantém a seleção; terceiro não acessa dados e mensagens persistem no backend. |
| T10.07 — [ ] Construir calendário e confirmação de reserva | T10.06, T06.06 | Calendário mensal, horários do dia e formulário de reserva nas duas áreas. | Selecionar dia preserva contexto; conflito ou horário inválido é informado sem ocupar o intervalo e sucesso reflete o estado persistido de Agenda. |
| T10.08 — [ ] Construir encerramento e novo atendimento | T10.07, T07.06 | Confirmação de encerramento, histórico bloqueado e novo agendamento vinculado ao serviço. | A interface distingue processo pendente de encerramento definitivo; chat encerrado não envia e novo atendimento preserva o histórico anterior. |
| T10.09 — [ ] Construir dashboard e acesso aos clientes | T10.08, T08.05 | Indicadores por período e atalhos aos atendimentos do artista. | Valores vêm do backend; loading, falha e defasagem diferem de período vazio; cliente aberto corresponde ao item selecionado. |
| T10.10 — [ ] Validar experiência e publicação web | T10.09 | Roteiros automatizados/manuais, evidências em 360/768/1280 px e instruções de publicação. | Fluxos passam por HTTPS; recarga de conversa funciona; teclado, foco de diálogo e sugestões são utilizáveis. Não há dados locais como fonte oficial. |

**Conclusão de P10:** website novo sobre os serviços reais, com fluxos de cliente e artista verificados.

### P11 — Base móvel React Native

**Referências:** capacidades do primeiro incremento móvel; RNF01–RNF08. **Marco:** M7. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T11.01 — [ ] Definir ferramentas e compartilhamento móvel | T10.10 | Decisão Expo/configuração nativa, organização compartilhada ou separada e limites de reutilização da web. | Compilação e distribuição em ambas as plataformas são consideradas sem presumir reaproveitamento direto de DOM/CSS ou incluir offline/push. |
| T11.02 — [ ] Inicializar iOS e Android | T11.01 | Estrutura React Native, entradas e configurações de ambiente com instruções de pré-requisitos. | As duas plataformas executam uma base nova apontando para o backend apropriado, sem ferramentas de publicação presumidas. |
| T11.03 — [ ] Implementar sessão móvel segura | T11.02, T02.06 | Autenticação, armazenamento de sessão adequado a cada plataforma, expiração e saída. | Credenciais administrativas não vão nos aplicativos; retomar sessão não mistura contas e o backend continua verificando autorização. |
| T11.04 — [ ] Validar contratos e primeiro catálogo nas duas plataformas | T11.03, T09.01, T03.03 | Consulta real e perfil de artista, com decisão de compatibilidade entre versões móveis. | iOS e Android recuperam dados reais; falhas de rede e vazio são explícitos. Teste de cliente anterior verifica política de compatibilidade. |
| T11.05 — [ ] Preparar testes e recorte das entregas móveis | T11.04 | Dispositivos de referência, ferramentas de teste e escopo de funcionalidades por plataforma. | Primeiro fluxo está demonstrado nos dois sistemas. P12 e P13 possuem critérios específicos, sem confundir compilação com publicação pública. |

**Conclusão de P11:** base móvel apta a evoluir em iOS e Android usando os mesmos contratos. P12 e P13 não dependem uma da outra.

### P12 — Entrega iOS

**Referências:** RF01–RF21 conforme recorte aprovado; RNF01–RNF08. **Marco:** M7. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T12.01 — [ ] Implementar catálogo e conta no iOS | T11.05 | Descoberta, pesquisa, sugestões, filtros, perfil e favoritos do recorte iOS. | Toque e acessibilidade são verificados nos dispositivos definidos e favoritos permanecem no backend após novo acesso. |
| T12.02 — [ ] Implementar atendimento e conversas no iOS | T12.01 | Pedidos, propostas, lista e histórico individual com autorização e autoria. | As partes acessam o contexto correto; chat encerrado bloqueia escrita e retomada recupera histórico persistido sem duplicar envio. |
| T12.03 — [ ] Implementar agenda e gestão no iOS | T12.02 | Reservas, novo atendimento e indicadores incluídos no recorte aprovado. | Conflitos e pendências são explícitos; valores financeiros mantêm significado e período corretos. Verificação de sessão expirada e falha de rede cobre esses fluxos. |
| T12.04 — [ ] Validar e distribuir a versão iOS | T12.03 | Regressão em dispositivos, build assinado e instruções de distribuição no ambiente aprovado. | Versão instalada executa os fluxos com backend real. Publicação pública só é marcada quando seu processo próprio for aprovado e realizado. |

**Conclusão de P12:** entrega iOS verificável e distribuída no ambiente acordado.

### P13 — Entrega Android

**Referências:** RF01–RF21 conforme recorte aprovado; RNF01–RNF08. **Marco:** M7. **Situação inicial:** pendente.

| ID / tarefa | Dependências | Entregável | Aceite e verificação |
| --- | --- | --- | --- |
| T13.01 — [ ] Implementar catálogo e conta no Android | T11.05 | Descoberta, pesquisa, sugestões, filtros, perfil e favoritos do recorte Android. | Toque e acessibilidade são verificados nos dispositivos definidos e favoritos permanecem no backend após novo acesso. |
| T13.02 — [ ] Implementar atendimento e conversas no Android | T13.01 | Pedidos, propostas, lista e histórico individual com autorização e autoria. | As partes acessam o contexto correto; chat encerrado bloqueia escrita e retomada recupera histórico persistido sem duplicar envio. |
| T13.03 — [ ] Implementar agenda e gestão no Android | T13.02 | Reservas, novo atendimento e indicadores incluídos no recorte aprovado. | Conflitos e pendências são explícitos; valores financeiros mantêm significado e período corretos. Verificação de sessão expirada e falha de rede cobre esses fluxos. |
| T13.04 — [ ] Validar e distribuir a versão Android | T13.03 | Regressão em dispositivos, build assinado e instruções de distribuição no ambiente aprovado. | Versão instalada executa os fluxos com backend real. Publicação pública exige a execução do processo próprio de distribuição. |

**Conclusão de P13:** entrega Android verificável e distribuída no ambiente acordado.

## 4. Próximo lote e acompanhamento

P00 foi concluída no nível de decisões e revisão. A [revisão T00.07](decisions/0006-p00-review.md) consolida os registros de T00.01 a T00.06 e o escopo do primeiro incremento. P01 foi implementada e verificada conforme [evidências](verification/p01.md), incluindo Docker e PostgreSQL real. O próximo passo é T02.01. As tarefas de P02 em diante permanecem pendentes.

Broker, pesquisa e stack móvel têm tarefas próprias quando se tornam necessários. Não precisam ser decididos no primeiro incremento de Identity.

Ao concluir uma tarefa, manter referência à evidência e atualizar o marco do plano apenas quando seus critérios estiverem demonstrados. Bloqueios devem identificar a dependência concreta e a ação necessária, sem marcar tarefas incompletas como concluídas.

Os itens de backend detalham operações e cenários críticos. Os itens de web e mobile são um refinamento inicial por fluxo e deverão ser decompostos novamente em histórias pequenas antes da respectiva etapa. Nenhuma tarefa futura autoriza adicionar funcionalidades que não façam parte do recorte aprovado.

Este arquivo é mutável. A ordem e o detalhamento devem acompanhar as decisões e entregas, mantendo a prioridade do backend, a autonomia dos serviços e os critérios de segurança e consistência dos documentos de referência.
