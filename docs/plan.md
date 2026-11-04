# Plano de execução do InkLink

## 1. Objetivo e diretrizes

Este documento organiza a ordem de implementação da nova aplicação, conforme o [requirements.md](requirements.md) e o [architecture.md](architecture.md).

A prioridade é desenvolver e validar a estrutura e os fluxos do backend antes de implementar os frontends. O backend será criado do zero em .NET 10, com microsserviços por domínio. Depois serão implementados o website em React e, em etapas posteriores, os aplicativos iOS e Android em React Native.

O protótipo existente é referência de experiência, não uma base de código a migrar. Este plano não determina alterações no protótipo nem inicia a implementação da nova aplicação.

O plano é evolutivo. As etapas representam uma ordem de dependências e entregas, não um cronograma fechado, promessa de prazo ou obrigação de implementar todas as capacidades de uma vez. Os itens começam pendentes e só serão concluídos mediante evidência da nova aplicação.

Princípios de execução:

- Entregar casos de uso completos e testáveis em pequenos incrementos, mesmo enquanto não houver interface.
- Validar o backend por APIs, testes automatizados e cenários reproduzíveis antes do consumo pelos frontends.
- Implementar estrutura de serviços conforme surgirem funcionalidades reais, sem criar antecipadamente todos os serviços vazios.
- Introduzir autenticação, autorização, testes e observabilidade junto das funcionalidades que precisam deles.
- Preservar autonomia dos serviços, sem bancos compartilhados por acesso direto ou bibliotecas com entidades de negócio comuns.
- Definir ferramentas quando forem necessárias à próxima entrega, sem transformar todas as decisões futuras em bloqueios iniciais.
- Não incluir processamento de pagamentos no escopo atual.

## 2. Marcos e sequência de referência

| Marco | Etapas | Resultado esperado |
| --- | --- | --- |
| M0 — Preparação | P00 | Decisões mínimas e organização da nova implementação definidas. |
| M1 — Fundação técnica | P01–P02 | Primeiro serviço executável, integração de identidade e acesso autorizado às APIs. |
| M2 — Primeiro fluxo de negócio | P03–P04 | Catálogo consultável e negociação de um atendimento persistida. |
| M3 — Comunicação e agenda | P05–P06 | Conversas autorizadas e reservas protegidas contra conflitos. |
| M4 — Ciclo de atendimento integrado | P07 | Encerramento seguro e novo atendimento coordenados entre serviços. |
| M5 — Gestão e backend validado | P08–P09 | Indicadores consistentes e backend integrado pronto para um frontend real. |
| M6 — Website | P10 | Fluxos de cliente e artista disponíveis em React para web. |
| M7 — Aplicativos móveis | P11–P13 | Base móvel e entregas de iOS e Android em React Native. |

Ordem principal:

```text
P00 -> P01 -> P02 -> P03 -> P04 -> P05 -> P06 -> GW -> P07 -> P08 -> P09
                                                                    |
                                                                    v
                                                                   P10
                                                                    |
                                                                    v
                                                                   P11
                                                                /       \
                                                              P12       P13
                                                              iOS     Android
```

A sequência favorece foco e entregas demonstráveis. Regras de Agenda podem ser modeladas antes da conclusão de Conversas, por exemplo, quando os contratos necessários estiverem definidos. Essa flexibilidade não elimina os critérios de integração de P07 e P09. iOS e Android não precisam ter uma ordem fixa entre si.

## 3. Padrão de execução de cada etapa

Cada etapa deve ser dividida em itens pequenos que contenham resultado, dependências, critérios de aceite, verificação e evidência. Checklists deste documento funcionam como planejamento; sua granularidade pode ser refinada no backlog antes da execução.

Antes de iniciar um caso de uso:

- Seu objetivo e os requisitos relacionados estão identificados.
- O serviço responsável e os dados que ele controla estão claros.
- O contrato mínimo e as regras necessárias estão definidos.
- As escolhas técnicas indispensáveis à implementação foram registradas.
- Existe uma forma de verificar o resultado, incluindo falhas relevantes.

Para concluir uma entrega:

- O comportamento foi implementado na nova aplicação e os critérios aplicáveis foram demonstrados.
- Compilação, testes e verificações de dependências passam para os componentes afetados.
- Validação e autorização são aplicadas no backend, quando exigidas.
- Persistência e migrações necessárias são reproduzíveis.
- Operações e integrações possuem erros e estados de falha definidos.
- Contratos, instruções e documentos de SDD refletem a entrega.
- Não há segredos ou dados reais de clientes em fixtures, logs de teste ou arquivos versionados.
- A evidência é registrada no item de trabalho, revisão ou histórico de entrega.

Não é necessário esperar a etapa de validação integrada para criar testes, proteger endpoints ou verificar falhas. P09 reúne a evidência do conjunto e não substitui as verificações das etapas anteriores.

## 4. Etapas de implementação do backend

### P00 — Preparar as decisões mínimas e o novo projeto

**Prioridade:** inicial. **Dependências:** requisitos e arquitetura revisados. **Situação:** concluída no nível de decisões e revisão, conforme T00.07.

**Objetivo:** viabilizar o início da nova solução sem reaproveitar a implementação do protótipo.

- [x] Definir o local da nova aplicação e a estratégia de repositório: `InkLink-Platform`, repositório próprio `InkLink-Platform`, SDD em `docs/` e implementação em `src/`, separados do protótipo. Repositório remoto criado como `vitorvianna05/InkLink-Platform`.
- [x] Definir a organização física inicial, nomes técnicos, projetos e testes conforme a decisão 0001 (T00.02), incluindo GetMyProfile como primeiro caso de uso.
- [x] Selecionar PostgreSQL, EF Core 10 e Npgsql 10, com isolamento por serviço, migrações controladas e restrição de intervalos na Agenda, conforme decisão 0002 (T00.03).
- [x] Selecionar Keycloak, OIDC/OAuth 2.0, PKCE, JWT e BFF web conforme decisão 0003 (T00.04).
- [x] Definir SDK .NET 10, Docker Compose, xUnit, WebApplicationFactory, Testcontainers PostgreSQL, ArchUnitNET, configuração segura e GitHub Actions conforme decisão 0004 (T00.05).
- [x] Definir convenções HTTP, UUIDv7, Problem Details, paginação por cursor, idempotência seletiva, OpenAPI e /api/v1, conforme decisão 0005 (T00.06).
- [x] Revisar a fundação e delimitar GetMyProfile em Identity, conforme revisão 0006 (T00.07), mantendo a execução e validação do ambiente em P01.

**Critério de conclusão:** é possível iniciar P01 sem assumir decisões não documentadas. Não se exige, neste ponto, escolher broker, busca, stack móvel ou plataforma final de nuvem.

### P01 — Construir a fundação técnica com um primeiro serviço executável

**Prioridade:** backend. **Dependências:** P00. **Situação:** concluída; [evidências P01](verification/p01.md).

**Objetivo:** validar a estrutura .NET 10 por meio do início real de Identidade e perfis, sem uma solução composta apenas por projetos vazios.

- [x] Criar os projetos necessários ao primeiro serviço em `net10.0`.
- [x] Organizar API, Application, Domain e Infrastructure conforme a complexidade do primeiro caso de uso.
- [x] Configurar injeção de dependências e verificar a direção das referências entre camadas.
- [x] Configurar persistência privada, credenciais próprias e primeira migração.
- [x] Implementar configuração por ambiente e tratamento consistente de erros.
- [x] Disponibilizar sinais básicos de saúde e prontidão e documentação de execução.
- [x] Introduzir logs estruturados e correlação das requisições, sem conteúdo sensível.
- [x] Configurar testes unitários e de integração e uma verificação de dependências arquiteturais.
- [x] Configurar o processo inicial de build e testes automatizados.
- [x] Demonstrar acesso à persistência por um caso de uso controlado de Identidade, sem expor operações privadas anonimamente.

**Critério de conclusão:** um ambiente limpo consegue restaurar, compilar, iniciar e testar o serviço seguindo instruções documentadas. O núcleo de negócio não depende de HTTP ou do banco. A fundação é ampliada conforme novas necessidades surgirem, sem impor cópia de regras ou entidades entre serviços.

**Evidência:** 29 testes aprovados em cópia limpa, build Release sem avisos/erros, migrações repetíveis e verificação local dos sinais operacionais. [Instruções](development/identity.md). A CI foi preparada e validada pela sequência local; execução remota após envio. M1 ainda depende de P02.

**Revisão dos contratos:** os desvios de Problem Details por Accept e de schemas/respostas OpenAPI foram corrigidos. A suíte atual passou com 38 testes; detalhes na [verificação P01](verification/p01.md#correção-dos-contratos-após-revisão--27102026).

### P02 — Integrar identidade, funções e entrada das APIs

**Prioridade:** backend. **Dependências:** P01. **Situação:** concluída; [evidências P02](verification/p02.md).

**Objetivo:** estabelecer identidade e autorização antes dos fluxos privados de cliente e artista.

- [x] Implementar a integração com o mecanismo de autenticação escolhido.
- [x] Associar a identidade autenticada a usuários internos e funções permitidas.
- [x] Disponibilizar os casos mínimos de consulta e manutenção do perfil previstos para a entrega.
- [x] Definir a política inicial de cadastro e vínculo de artista necessária às etapas seguintes.
- [x] Introduzir o Gateway, o roteamento e a base do BFF web .NET, com sessão protegida e tokens no servidor, testável sem a interface React.
- [x] Validar identidade no Gateway e no serviço e contextualizar o usuário nas operações.
- [x] Definir a identidade técnica para chamadas entre serviços quando necessária.
- [x] Testar sessão inválida, ausência de permissão e tentativa de acessar outro usuário.
- [x] Usar identidades de teste controladas, sem transportar o botão de personagens do protótipo para a aplicação real.

**Critério de conclusão:** usuários de teste identificados como cliente e artista realizam somente as operações permitidas, com verificação no backend. O Gateway não substitui autorização por recurso dentro dos serviços.

**Rastreabilidade:** RF01, RF21; RNF01, RNF04, RNF05 e RNF08.

**Evidência e marco:** [71 testes aprovados e conferência local dos processos](verification/p02.md), com Keycloak e PostgreSQL reais. M1 atingido: primeiro serviço executável, identidade integrada e entrada protegida por Gateway/BFF. Não inclui frontend ou infraestrutura de produção.

**Revisão de conformidade (28/10/2026):** corrigidas resolução por endpoint, coordenação em Application, respostas 500 no OpenAPI e logs correlacionados nos três componentes. [Evidências](verification/p02.md).

### P03 — Implementar Catálogo e o primeiro fluxo de pesquisa

**Prioridade:** backend. **Dependências:** P02. **Situação:** concluída; [evidências P03](verification/p03.md).

**Objetivo:** disponibilizar informações públicas reais da nova aplicação e favoritos privados, verificáveis por API.

- [x] Criar Catálogo com persistência e migrações próprias e acesso pelo Gateway.
- [x] Modelar perfil público, estilos, localização, trabalhos, flashes e preço de referência.
- [x] Definir o mecanismo mínimo autorizado de alimentação do catálogo para a entrega, sem presumir uma interface administrativa completa.
- [x] Implementar consulta de perfil e listagem de artistas para Descobrir.
- [x] Implementar pesquisa livre e filtros combinados com tratamento de ausência de resultados.
- [x] Implementar sugestões aproximadas e testes de acentos, prefixos, relevância e pequenas diferenças de escrita.
- [x] Definir a tecnologia de pesquisa conforme a necessidade desta entrega, sem presumir Elasticsearch obrigatório.
- [x] Implementar inclusão, remoção e consulta de favoritos por cliente.
- [x] Definir o tratamento inicial de metadados e referências de mídia do portfólio, com acesso apropriado.
- [x] Documentar os contratos com seleção de campos e paginação apropriadas às listagens.
- [x] Verificar isolamento entre favoritos de usuários e proteção das alterações do catálogo.

**Critério de conclusão:** uma coleção de testes consegue pesquisar artistas, consultar portfólio e flashes e persistir favoritos sem interface gráfica. Dados são obtidos do serviço e não de arrays de demonstração do frontend.

**Rastreabilidade:** RF02–RF08 e RF21; RNF04–RNF08.

**Evidência:** 39 cenários de Catalog aprovados, com 110 cenários na suíte atual, build Release sem avisos/erros e migração/conferência HTTP locais. [Decisão e política de publicação](decisions/0008-p03-catalog-and-search.md); [execução](development/catalog.md). Na conclusão de P03, M2 ainda dependia de P04; o marco foi concluído posteriormente conforme as evidências de P04.

### P04 — Implementar solicitações, propostas e aceite

**Prioridade:** backend. **Dependências:** P02–P03. **Situação:** concluída; [evidências P04](verification/p04.md).

**Objetivo:** registrar uma negociação completa no contexto Atendimentos.

- [x] Criar Atendimentos com banco lógico, credenciais e migrações próprios.
- [x] Definir participantes, identificadores, estados mínimos e transições de pedido e proposta.
- [x] Definir em qual etapa inicial a conversa é criada e o envio é permitido, evitando exigir aceite antes de uma negociação que dependa do chat.
- [x] Implementar solicitação com ideia, região do corpo, tamanho e referência ao flash, quando houver.
- [x] Preservar o contexto necessário da negociação quando o catálogo for alterado.
- [x] Implementar apresentação de proposta pelo artista e confirmação do aceite pelo cliente.
- [x] Implementar consultas de pedidos e propostas por participante.
- [x] Garantir isolamento de estado por atendimento e proposta.
- [x] Definir idempotência para repetição de operações críticas e respostas sob concorrência.
- [x] Definir os contratos mínimos de integração com Conversas e Agenda, sem publicar eventos por mecanismo não confiável.

**Critério de conclusão:** cliente e artista realizam por API a solicitação, apresentação e aceite da proposta correta. Uma operação em outro atendimento não altera sua situação. Aceite não registra recebimento nem reserva automaticamente um horário.

**Rastreabilidade:** RF07, RF09, RF10 e RF21; RNF04–RNF06.

**Evidência:** negociação completa por API, snapshots, isolamento, idempotência e concorrência verificados conforme [P04](verification/p04.md). M2 concluído no recorte backend P03/P04. Conversas e reservas efetivas continuam em P05/P06.

### P05 — Implementar Conversas e a primeira integração confiável

**Prioridade:** backend. **Dependências:** P02 e P04. **Situação:** concluída; [evidências P05](verification/p05.md).

**Objetivo:** permitir histórico e envio autorizado nas duas perspectivas, já com integração confiável ao contexto de atendimento.

- [x] Selecionar broker e ferramentas de integração necessários ao primeiro fluxo assíncrono e registrar a decisão.
- [x] Definir o envelope inicial de eventos, correlação, versões e política de repetição.
- [x] Implementar Outbox no produtor da primeira integração, com gravação na mesma transação dos dados de negócio.
- [x] Implementar consumo idempotente, com deduplicação coordenada à alteração local.
- [x] Criar Conversas com persistência própria e identificação de participantes e atendimento.
- [x] Coordenar criação e habilitação inicial de conversa a partir do fluxo definido em P04.
- [x] Implementar listagem, leitura individual e envio com autoria e data/hora preservadas.
- [x] Aplicar autorização por conversa e validação de conteúdo em cada operação.
- [x] Implementar o bloqueio local de escrita e o contrato idempotente necessário ao encerramento.
- [x] Definir e testar a ordem consistente entre gravação de mensagem e bloqueio na persistência de Conversas.
- [x] Definir a atualização de mensagens pelos clientes e o contrato de histórico; transporte em tempo real será introduzido se fizer parte da entrega aprovada.
- [x] Testar perda temporária de comunicação, repetição de eventos e proteção contra envio por terceiros.

**Critério de conclusão:** dois participantes de teste trocam mensagens por APIs, consultam o mesmo histórico e não acessam outras conversas. A primeira integração possui entrega recuperável e efeitos idempotentes. O bloqueio local é testado; o encerramento distribuído só estará concluído em P07.

**Rastreabilidade:** RF11–RF14 e RF21; RNF04–RNF06.

**Evidência:** 171 testes aprovados, sem falhas/ignorados; build Release sem avisos/erros; recuperação e permissões com RabbitMQ, Keycloak e PostgreSQL reais, além de Compose e hosts locais. [Decisão 0010](decisions/0010-p05-messaging.md) e [execução](development/messaging.md). M3 foi concluído com P06; encerramento distribuído em P07.

**Revisão de conformidade P05 (03/11/2026):** abertura em Application; orçamento durável de dez tentativas no consumidor e diagnóstico/reprocessamento; rastreamento W3C e métricas de eventos. Contratos e operação atualizados na decisão 0010 e na verificação P05. A verificação operacional mais ampla foi consolidada em T09.03.

### P06 — Implementar Agenda com proteção de concorrência

**Prioridade:** backend. **Dependências:** P02 e P04; padrões de integração de P05 para publicações externas. **Situação:** concluída no recorte backend; [evidências](verification/p06.md).

**Objetivo:** confirmar reservas válidas, inclusive quando duas requisições tentam ocupar o mesmo horário.

- [x] Definir disponibilidade inicial, dias permitidos, fuso horário e interpretação dos intervalos necessários à primeira entrega.
- [x] Criar Agenda com persistência, migrações e permissões próprias.
- [x] Implementar consulta de disponibilidade, calendário e reservas por artista e data.
- [x] Implementar a confirmação da reserva vinculada ao atendimento, ao cliente e ao artista.
- [x] Detalhar e implementar a proteção transacional por restrição de exclusão de intervalos no PostgreSQL para o mesmo artista, conforme decisão 0002, permitindo intervalos adjacentes com início incluído e término excluído. A modelagem deverá definir os estados de reserva que ocupam a agenda e a representação temporal apropriada.
- [x] Testar intervalos adjacentes, parcialmente sobrepostos, contidos e completamente envolventes.
- [x] Testar conflitos com solicitações simultâneas e repetição da mesma operação.
- [x] Publicar confirmação por Outbox e documentar o resultado síncrono da reserva.
- [x] Garantir que uma consulta anterior de disponibilidade não seja tratada como reserva já confirmada.

**Critério de conclusão:** não são persistidas reservas conflitantes sob concorrência. Cliente e artista consultam a mesma reserva autorizada. A confirmação da reserva não é confundida com a conclusão de toda a coordenação entre serviços.

**Rastreabilidade:** RF15, RF18, RF19 e RF21; RNF04–RNF06.

**Validação:** 192 casos com resultado final aprovado, incluindo reexecução direcionada após atualizar a expectativa do esquema. Build sem avisos/erros, formatação/lockfile e operação local verificados. M3 concluído no recorte backend; coordenação distribuída permanece em P07.

**Correções da revisão P06:** validação JSON de datas, falhas de telemetria sob orçamento da Outbox e invariantes/fuso em Domain. 29 casos de Scheduling com resultado final aprovado; [evidências](verification/p06.md) detalham o recorte e a reexecução da expectativa de jsonb.

### Incremento GW — Antecipar documentação interativa do Gateway

**Dependências:** P06. **Situação:** concluído; [evidências](verification/gateway-documentation.md). Decisão 0012, tarefas TGW.01–TGW.04. Antecipação aprovada pelo responsável para permitir testes manuais dos serviços antes de P07.

- [x] Registrar fonte OpenAPI por serviço, limites e política de acesso.
- [x] Disponibilizar Swagger UI com seleção dos serviços e chamadas pelas rotas públicas do Gateway.
- [x] Integrar Authorization Code com PKCE por cliente público de desenvolvimento.
- [x] Verificar filtro de contratos internos, autorização, indisponibilidade e ausência da ferramenta em produção.
- [x] Documentar execução no Visual Studio e evidências reais de autenticação/consumo.

T09.01 consolidou exemplos e compatibilidade dos contratos atuais; incrementos futuros deverão manter essa documentação. Este incremento não altera os critérios de conclusão de P07/P09.

### P07 — Integrar o ciclo de atendimento, encerramento e novo agendamento

**Prioridade:** backend; marco crítico antes do frontend. **Dependências:** P04–P06. **Situação:** concluída no recorte backend; [evidências P07](verification/p07.md).

**Objetivo:** cumprir as regras que atravessam Atendimentos, Conversas e Agenda com tratamento de falhas parciais.

- [x] Definir quem pode encerrar um atendimento e as confirmações exigidas para a primeira entrega.
- [x] Especificar o protocolo de encerramento descrito em architecture.md, com identificadores, ordem e confirmações.
- [x] Implementar o processo durável em Atendimentos e a confirmação idempotente de bloqueio em Conversas.
- [x] Não concluir o encerramento antes do bloqueio efetivo de novas mensagens.
- [x] Representar coordenação pendente, falha temporária e conclusão por contratos claros, sem atribuir sucesso definitivo antes da hora.
- [x] Implementar retomada e reconciliação de operações que falham entre o bloqueio e a conclusão do atendimento.
- [x] Integrar novo agendamento confirmado à habilitação de um novo contexto de atendimento e conversa.
- [x] Preservar o histórico e distinguir os contextos antigos e novos, independentemente da organização visual futura.
- [x] Definir tratamento de reserva confirmada quando uma etapa posterior de coordenação falha, incluindo eventual compensação se aprovada.
- [x] Testar envio concorrente com encerramento, confirmação perdida, processo reiniciado e mensagens repetidas ou fora de ordem.
- [x] Garantir que um evento atrasado de atendimento anterior não reabra ou bloqueie outro serviço.

**Critério de conclusão:** o roteiro completo via API demonstra negociação, conversa, reserva, encerramento, histórico bloqueado e novo atendimento. Falhas parciais não permitem mensagens depois da confirmação de encerramento, não duplicam reservas e não apagam históricos. Processos pendentes podem ser encontrados e retomados.

**Rastreabilidade:** RF09–RF15, RF18, RF19 e RF21; RNF04–RNF06.

**Evidência:** políticas da [decisão 0013](decisions/0013-p07-lifecycle.md), sete cenários novos e 215 casos com resultado final aprovado após reexecuções descritas na verificação. M4 concluído no recorte backend. Incremento seguinte, P08, concluído conforme evidências próprias.

**Revisão de conformidade P07:** ativação aguarda encerramento provisório; coordenação em Application; correlação persistida até o worker. 59 testes do recorte aprovados; [evidências](verification/p07.md) e decisão 0013 atualizadas. Migração aditiva LifecycleCorrelationAndRecovery.

### P08 — Implementar Gestão financeira e dados do dashboard

**Prioridade:** backend. **Dependências:** P04, P06 e P07. **Situação:** concluída no recorte backend; [evidências](verification/p08.md).

**Objetivo:** oferecer indicadores baseados em registros do artista, mantendo recebimentos distintos de reservas e conclusão de sessão.

- [x] Definir o registro gerencial mínimo de receitas, custos e respectivas datas, sem cobrança pela plataforma.
- [x] Definir como uma sessão concluída é registrada e como essa informação se relaciona com o atendimento, sem confundir encerramento de chat com recebimento.
- [x] Criar Gestão financeira com persistência e migrações próprias.
- [x] Implementar os registros autorizados necessários aos indicadores, com validação de valores e contexto.
- [x] Implementar cálculos de receita, custos, lucro e margem estimados e ticket médio conforme requirements.md.
- [x] Implementar consultas por período e isolamento por artista.
- [x] Integrar os dados operacionais necessários de reservas, sessões e conversas por contratos ou projeções.
- [x] Definir onde ocorre a composição do dashboard, sem introduzir obrigatoriamente um serviço exclusivo para a tela.
- [x] Tratar repetição de eventos, atraso e reconstrução de projeções sem duplicar lançamentos.
- [x] Testar períodos vazios, datas de referência diferentes e divergência entre valor agendado e valor recebido.

**Critério de conclusão:** o backend retorna indicadores verificáveis para uma base controlada, sem incluir dados de outro artista. Reservar ou encerrar um serviço não gera recebimento automaticamente. A composição do dashboard não consulta bancos privados de outros serviços.

**Rastreabilidade:** RF16, RF17, RF20 e RF21; RNF04–RNF06.

**Entrega:** políticas aprovadas na [decisão 0014](decisions/0014-p08-finance.md). Finance mantém ledger privado e compõe Scheduling/Messaging por APIs delegadas. Não há nova projeção: recomposição consulta autoridades, sem reaplicar efeitos. 66 testes do recorte aprovados, migrações e operação local verificadas. M5 foi concluído pelo aceite integrado em P09.

### P09 — Validar e disponibilizar o backend integrado

**Prioridade:** conclusão da base backend antes do frontend. **Dependências:** P01–P08. **Situação:** concluída no recorte backend local; [evidências](verification/p09.md).

**Objetivo:** consolidar um backend executável, protegido e verificável, pronto para sustentar os canais de interface.

- [x] Executar o roteiro integrado das duas funções sem depender do protótipo.
- [x] Executar os testes de contrato, autorização, persistência, concorrência e falhas distribuídas.
- [x] Revisar paginação, limites de entrada, compatibilidade e documentação das APIs.
- [x] Revisar timeouts, cancelamento, repetição segura, filas e diagnóstico de falhas permanentes.
- [x] Verificar correlação entre chamadas, eventos e processos incompletos.
- [x] Demonstrar publicação e teste de um serviço sem exigir publicação simultânea dos demais quando seus contratos forem compatíveis.
- [x] Preparar ambiente de integração com configuração e segredos separados, escolhido conforme a necessidade de validar a entrega.
- [x] Verificar HTTPS e configuração de consumo pelos frontends previstos.
- [x] Documentar inicialização dos serviços necessários, migrações e preparação de dados sintéticos em desenvolvimento e testes.
- [x] Preparar exemplos de chamadas e evidências de sucesso e falha para os futuros consumidores.
- [x] Registrar limitações da entrega e critérios não atendidos, sem marcá-los implicitamente como concluídos.

**Critério de conclusão:** existe uma versão do backend integrado executável por instruções reproduzíveis, com contratos documentados e evidência dos fluxos fundamentais. Nenhuma dependência de interface é usada como substituto de autorização, persistência ou regra de negócio. Este marco não exige antecipar toda a infraestrutura de escala futura.

**Rastreabilidade:** todos os requisitos funcionais de backend aplicáveis e RNF01, RNF04–RNF08.

**Aceite M5:** contratos dos seis serviços, regressão, recuperação e privacidade consolidados; ambiente isolado com oito hosts HTTPS e demonstração de publicação independente de Finance. 249 casos únicos com resultado final aprovado, incluindo o recorte final de OpenAPI. Decisão [0015](decisions/0015-p09-backend-validation.md). Próxima etapa: P10. Nuvem, produção e frontends permanecem fora deste aceite técnico.

## 5. Etapas posteriores de frontend

### P10 — Implementar o website em React

**Prioridade:** após a validação do backend integrado. **Dependências:** M5 / P09. **Situação:** em andamento; website implementado e verificado com testes automatizados e no navegador, quatro cenários com backend real aprovados; homologação restante de T10.10 pendente. [Evidências](verification/p10.md) e [decisão 0016](decisions/0016-p10-website.md).

**Objetivo:** construir a experiência web sobre os serviços reais, usando o protótipo apenas como referência visual e de navegação.

- [x] Definir build, rotas, cliente HTTP, cache e estrutura por funcionalidades.
- [x] Implementar a integração de sessão e a navegação apropriada a cliente e artista.
- [x] Entregar Descobrir, Explorar, sugestões, filtros, perfil e favoritos.
- [x] Entregar pedidos, propostas e acompanhamento do atendimento.
- [x] Entregar listagem e página individual de conversas, com autoria e estados de bloqueio.
- [x] Entregar calendário, reserva e acompanhamento do novo atendimento.
- [x] Entregar dashboard e acesso aos clientes do artista.
- [x] Implementar carregamento, falha, estado vazio e coordenação pendente em cada fluxo.
- [x] Verificar responsividade e navegação por teclado, incluindo foco em diálogos e sugestões.
- [x] Corrigir o menu do cabeçalho para abrir as informações e ações do usuário (entrar/sair), conforme T10.12.
- [x] Definir o modelo de login web: página React que inicia o fluxo atual do BFF, conforme [decisão 0017](decisions/0017-p10-web-login.md) (T10.13).
- [x] Entregar a página `/entrar` dividida 50/50 com a identidade InkLink, preservando Code + PKCE no BFF, conforme T10.14. Retorno ao website por `/bff/login?returnUrl=` (decisão 0017 revisada), verificado com Keycloak real.
- [ ] Executar testes de integração com o backend e o roteiro completo pelo navegador.
- [ ] Documentar e validar a publicação do website no ambiente escolhido. Instruções documentadas; validação pendente.

**Critério de conclusão:** cliente e artista executam os fluxos da entrega pelo website, com dados persistidos no backend. Não são reaproveitados o estado global, os personagens ou o armazenamento do protótipo como mecanismo de aplicação real.

**Rastreabilidade:** RF01–RF21 conforme os fluxos entregues; RNF01–RNF08, especialmente RNF02 e RNF03.

### P11 — Preparar a base móvel em React Native

**Prioridade:** posterior à primeira entrega web validada. **Dependências:** P09 e P10. **Situação:** pendente.

**Objetivo:** preparar iOS e Android para consumir as mesmas capacidades de backend com uma experiência apropriada a aplicativos.

- [ ] Definir a abordagem React Native, incluindo Expo ou configuração nativa, conforme as necessidades aprovadas.
- [ ] Definir base compartilhada ou separada e os limites de reaproveitamento com a web.
- [ ] Definir navegação, sessão, armazenamento seguro e configuração por ambiente.
- [ ] Confirmar os contratos que devem suportar versões anteriores dos aplicativos.
- [ ] Implementar um primeiro fluxo mínimo de autenticação e consulta real de catálogo nas duas plataformas.
- [ ] Preparar testes, ferramentas de compilação e instruções locais adequadas a cada plataforma.
- [ ] Refinar os fluxos móveis de cliente e artista a partir da experiência validada na web.

**Critério de conclusão:** o primeiro fluxo consome o backend real em iOS e Android, sem pressupor reutilização direta de componentes DOM ou CSS. Regras oficiais permanecem no backend. Offline, push e integrações nativas adicionais não entram implicitamente no escopo.

### P12 — Entregar o aplicativo iOS

**Prioridade:** evolução móvel. **Dependências:** P11. **Situação:** pendente.

- [ ] Implementar os fluxos de cliente e artista incluídos na entrega iOS.
- [ ] Adaptar navegação, formulários, calendário e mensagens à plataforma.
- [ ] Verificar sessão, permissões, histórico e retomada dos fluxos com o backend.
- [ ] Verificar acessibilidade e comportamento em dispositivos de referência definidos para a entrega.
- [ ] Configurar empacotamento, assinatura e distribuição no ambiente iOS aprovado.
- [ ] Registrar versões e compatibilidade de contratos com o backend.

**Critério de conclusão:** os fluxos aprovados funcionam em uma distribuição iOS verificável, consumindo os serviços reais. Publicação pública depende da definição e execução do processo correspondente, não apenas de uma compilação local.

### P13 — Entregar o aplicativo Android

**Prioridade:** evolução móvel. **Dependências:** P11. **Situação:** pendente.

- [ ] Implementar os fluxos de cliente e artista incluídos na entrega Android.
- [ ] Adaptar navegação, formulários, calendário e mensagens à plataforma.
- [ ] Verificar sessão, permissões, histórico e retomada dos fluxos com o backend.
- [ ] Verificar acessibilidade e comportamento em dispositivos de referência definidos para a entrega.
- [ ] Configurar empacotamento, assinatura e distribuição no ambiente Android aprovado.
- [ ] Registrar versões e compatibilidade de contratos com o backend.

**Critério de conclusão:** os fluxos aprovados funcionam em uma distribuição Android verificável, consumindo os serviços reais. A ordem entre P12 e P13 pode ser alterada conforme prioridade e capacidade de execução.

## 6. Rastreabilidade resumida

| Capacidade | Requisitos principais | Etapas de backend | Entrega de interface |
| --- | --- | --- | --- |
| Identidade, áreas e acesso | RF01; RNF04 | P01–P02 | P10–P13 |
| Descoberta, pesquisa e favoritos | RF02–RF08 | P03 | P10–P13 |
| Solicitações e propostas | RF07, RF09–RF10 | P04 | P10–P13 |
| Listagem, histórico e mensagens | RF11–RF13 | P05 | P10–P13 |
| Encerramento e novo atendimento | RF14–RF15 | P05–P07 | P10–P13 |
| Dashboard e finanças | RF16–RF17 | P08 | P10–P13 |
| Calendário e reservas | RF18–RF19 | P06–P07 | P10–P13 |
| Clientes e acesso ao perfil | RF20 | P03–P04, P08 | P10–P13 |
| Persistência e consistência | RF21; RNF04–RNF06 | P01–P09 | Verificação em P10–P13 |
| Responsividade e acessibilidade | RNF02–RNF03 | Contratos de suporte nas etapas aplicáveis | P10–P13 |
| Execução, publicação e ambientes | RNF01, RNF07–RNF08 | P00–P09 | P10–P13 |

Os requisitos não funcionais são transversais. A tabela indica concentração de responsabilidade e não autoriza adiar segurança ou testes até o fim.

## 7. Gestão das decisões e do progresso

Antes de executar uma etapa, seu checklist deve ser refinado em tarefas pequenas. Cada tarefa mantém vínculo com os requisitos e decisões arquiteturais afetados. Estimativas, responsáveis e duração de ciclos serão definidos no planejamento operacional, sem fixar datas artificiais neste documento.

Escolhas que afetem persistência, comunicação, identidade ou limites dos serviços devem registrar contexto, alternativas, decisão e consequências. Uma atualização aprovada pode mudar a sequência do plano; dependências e critérios de conclusão devem ser revisados junto com a mudança.

Situações de referência para acompanhamento:

| Situação | Significado |
| --- | --- |
| Pendente | Ainda não iniciada na nova aplicação. |
| Em andamento | Há trabalho ativo, sem todos os critérios concluídos. |
| Em validação | Implementação disponível para verificação dos critérios. |
| Concluída | Critérios demonstrados e evidências registradas. |
| Bloqueada | Uma dependência concreta impede a continuação, com motivo registrado. |

A conclusão de uma etapa não significa a conclusão automática das etapas dependentes. Telas, dados e testes do protótipo não marcam tarefas deste plano como entregues.

## 8. Próxima ação

P00–P09 e o incremento GW estão concluídos nos recortes documentados. M1–M5 atingidos no backend; a [verificação P09](verification/p09.md) consolida o aceite técnico local, seus resultados e limitações. P10 possui o website React e quatro cenários de integração HTTPS real aprovados; o próximo passo é concluir a homologação restante de T10.10, conforme a verificação P10. M6, aplicativos móveis e operação de produção permanecem pendentes.

GetMyProfile possui transporte autenticado, atualização controlada de nome, vínculo externo/interno e funções locais. Keycloak, Gateway e BFF estão integrados e verificados em P02. Os próximos fluxos continuam priorizando backend, com autorização própria em cada serviço.

## Revisão de conformidade P08

T08.05/T08.06: o adaptador de Finance exige os campos obrigatórios dos contratos operacionais e rejeita ausência/null com 503, preservando zero explícito como resultado válido. Scheduling resolve limites de datas civis com meia-noite inexistente/repetida sem rejeitar o período. A regra de validação dos horários de reserva permanece independente. Cenários e resultados da revisão estão em [evidências P08](verification/p08.md).
