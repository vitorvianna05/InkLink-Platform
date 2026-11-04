# Especificação consolidada do InkLink

## 1. Finalidade e fontes

Este documento consolida, por engenharia reversa documental, a especificação funcional e técnica já estabelecida para o InkLink. Sua finalidade principal é reunir comportamentos, responsabilidades, contratos e limites do produto, sem introduzir decisões novas ou substituir os documentos existentes.

**Data de referência:** 03/11/2026. **Recorte:** P00–P06 e incremento GW concluídos; P07 concluída no recorte backend; P08 concluída no recorte backend; P09 concluída no recorte backend local; P10 implementada com T10.10 parcialmente validada; P11–P13 planejados. A consolidação referencia as evidências de cada etapa; os resultados e limites da P09 estão em sua verificação própria.

| Fonte | Responsabilidade |
| --- | --- |
| [requirements.md](requirements.md) | Objetivos, requisitos funcionais e não funcionais e critérios de aceite. |
| [architecture.md](architecture.md) | Limites de serviços, organização interna, consistência, segurança e integrações. |
| [plan.md](plan.md) | Sequência, marcos e dependências das entregas. |
| [tasks.md](tasks.md) | Tarefas, dependências, situação e aceite verificável. |
| Registros em decisions/ | Refinamentos aprovados, contexto e consequências das decisões. |
| Guias em development/ e contratos em contracts/ | Execução e detalhes dos contratos entregues. |
| Registros em verification/ | Evidências históricas e limitações das verificações. |

O spec é mutável. Não resolve silenciosamente divergências entre fontes nem autoriza mudanças por inferência. Decisões futuras devem ser registradas nos documentos responsáveis e refletidas aqui. A situação abaixo é um retrato desta edição; o acompanhamento continua em tasks.md.

## 2. Objetivo e escopo

O InkLink conecta clientes a tatuadores e organiza descoberta, pesquisa, portfólio, flashes, orçamento, proposta, conversa, agendamento e acompanhamento do atendimento. Para o artista, acrescenta gestão de clientes, agenda e resultados financeiros.

O produto é criado do zero no repositório InkLink-Platform, com SDD em docs/ e implementação em src/. O protótipo é referência de telas, identidade visual, navegação e usabilidade. Seu código, dados fictícios, armazenamento local e simplificações não são base técnica nem evidência de entrega do produto.

O backend utiliza .NET 10. Os canais previstos são website em React/React DOM, aplicativo iOS em React Native e aplicativo Android em React Native. Consomem contratos compatíveis, sem obrigação de lançamento simultâneo ou reaproveitamento de DOM/CSS no mobile. O backend tem prioridade; website e aplicativos seguem suas etapas próprias.

Idioma de referência: português do Brasil. Moeda: reais. Processamento de pagamentos diretamente pela plataforma está fora do escopo atual. Registro gerencial de recebimentos não equivale a cobrança.

## 3. Atores e permissões

| Ator | Capacidades e limites |
| --- | --- |
| Visitante | Consulta informações públicas do catálogo, sem acesso a registros privados. |
| Cliente / Client | Mantém favoritos, solicita orçamento, acompanha e aceita sua proposta, participa das próprias conversas e consulta suas reservas. |
| Artista / Artist | Publica seu catálogo, apresenta propostas nos seus atendimentos, conversa com seus clientes, configura disponibilidade e confirma suas reservas. Registra seus lançamentos e conclusões de sessões, consultando indicadores próprios. |
| Operação administrativa | Cria contas no Keycloak e ativa Artist conforme a política inicial; não pressupõe painel administrativo completo. |
| Chamador técnico | Usa identidade e escopos específicos para integrações autorizadas, sem herdar automaticamente permissões de usuário. |

Contas são criadas administrativamente no Keycloak. O primeiro acesso provisiona a identidade interna com Client; Artist é ativado administrativamente, permitindo as duas funções na mesma conta. O botão de troca de personagens do protótipo não faz parte da autorização do produto.

Identity vincula a conta interna ao emissor e sujeito do provedor. Cada serviço valida acesso ao próprio recurso. Papel, identificador no corpo da requisição ou passagem pelo Gateway não bastam para conceder acesso.

A perspectiva cliente/artista altera apresentação e consultas permitidas, mas não muda autoria real nem concede participação em outro atendimento.

## 4. Modelo conceitual e propriedade

| Conceito | Contexto responsável | Informação e invariantes |
| --- | --- | --- |
| Conta e perfil privado | Identity | Identidade interna, vínculo com provedor, funções e dados básicos autorizados. Credenciais pertencem ao Keycloak. |
| Perfil público, trabalho e flash | Catalog | Identidade pública, estúdio, localização, estilos, biografia, preço de referência e mídias. Trabalhos realizados e flashes são conteúdos distintos. |
| Favorito | Catalog | Vínculo persistente entre cliente e artista, isolado por conta. |
| Pedido e atendimento | Engagements | Participantes, ideia, região, tamanho e contexto histórico da negociação. |
| Proposta | Engagements | Versão imutável, descrição, valor BRL e situação vinculada ao atendimento. |
| Conversa e mensagem | Messaging | Participantes, atendimento, autoria, conteúdo, data/hora, sequência e barreira local de escrita. |
| Calendário, disponibilidade e reserva | Scheduling | Artista, fuso, intervalos, participantes e atendimento; reserva confirmada ocupa tempo sem sobreposição. |
| Receita, custo e indicadores | Finance | Registros gerenciais e resultados por artista/período, separados de pagamento e reserva. |

Relações entre contextos utilizam identificadores e contratos, sem acesso a bancos alheios ou entidades de domínio compartilhadas. Projeções têm origem e atualização identificadas. Snapshots preservam o contexto histórico; não substituem autorização nem prometem disponibilidade futura.

## 5. Fluxos e regras de negócio

### 5.1 Identidade e áreas do usuário

**Referências:** RF01, RF21; RNF04, RNF08; T02.01–T02.06.

A identidade autenticada é associada à conta interna conforme a política de P02. Consulta e manutenção de perfil limitam-se aos campos autorizados; alterar o payload não permite escolher outra identidade ou obter Artist.

A experiência futura separa Meu espaço e mensagens do cliente do dashboard, agenda e mensagens do artista. Sessão inválida, expirada ou ausente não autoriza operações privadas.

**Aceite:** duas contas mantêm seus dados isolados inclusive no acesso direto ao serviço, sem depender exclusivamente do Gateway. A entrega de backend não implica existência dessas telas.

### 5.2 Descobrir, Explorar e favoritos

**Referências:** RF02–RF05, RF08; T03.04–T03.07; T10.03–T10.04.

Descobrir apresenta o produto, artistas e acesso à exploração. Explorar começa pelo campo livre e filtros, sem cards antes da busca explícita. É permitido buscar com texto, filtros ou sem critérios.

O campo livre sugere artistas, estúdios, estilos e cidades por proximidade, ignorando caixa e acentos e tolerando pequenos erros. Sugestões têm categoria, não repetem a mesma opção na mesma categoria e não aparecem para campo vazio. Selecionar uma opção preenche o campo; “Buscar artistas” aplica a pesquisa. Filtros de seleção não recebem autocompletar.

Texto, estilo, cidade e limite de orçamento combinam-se cumulativamente. Orçamento compara preço inicial de referência, sem equivaler à proposta final. Resultados informam quantidade e relevância textual. Alterar filtros indica necessidade de nova busca; limpar retorna ao estado inicial e não remove favoritos.

A busca inicial usa PostgreSQL/pg_trgm na infraestrutura de Catalog, com projeção reconstruível. Elasticsearch não é obrigatório. Na interface futura, respostas antigas não devem substituir sugestões de uma entrada mais recente.

**Aceite:** “sao” encontra “São Paulo” e “blakwork” encontra “Blackwork”, quando existentes no catálogo; ausência de resultados é explícita; favoritos persistem por cliente, sem duplicação por repetição de inclusão. APIs foram entregues em P03; os controles visuais serão entregues nos frontends.

### 5.3 Perfil, portfólio e flashes

**Referências:** RF06, RF07, RF20; T03.02–T03.03; T10.03.

O perfil apresenta nome, estúdio, localização, biografia, estilos e preço inicial do artista selecionado. Tatuagens realizadas e flashes ocupam abas distintas. A aba de conversa aparece quando existe histórico autorizado, inclusive encerrado. Abrir e sair do perfil preserva o contexto de navegação.

Interesse em um flash inicia um pedido para seu artista e preserva a referência escolhida. Preço de referência não representa aceite, reserva ou pagamento.

Na política de P03, o próprio Artist publica e edita seu perfil, trabalhos e flashes por API autenticada, com publicação imediata. Persistem referências públicas HTTPS de imagens e texto alternativo. Não há upload de binários, processamento de imagens, moderação ou painel completo nesta entrega.

**Aceite:** outro artista não altera a publicação; dados privados não aparecem como catálogo; o conteúdo pertence ao artista selecionado. Exclusividade e reutilização de flashes continuam abertas a refinamento.

### 5.4 Solicitação, proposta e aceite

**Referências:** RF07, RF09, RF10, RF21; T04.01–T04.06; [decisão 0009](decisions/0009-p04-engagements.md).

O cliente envia ideia, região do corpo, tamanho aproximado e referência opcional a flash. Cliente e artista são distintos. Engagements consulta Catalog por contrato técnico autorizado e preserva snapshots do artista e do flash. Indisponibilidade da dependência não cria pedido incompleto.

| Campo | Limite inicial aprovado |
| --- | --- |
| Ideia | Entrada de 1 a 4.000 caracteres, com trim e rejeição de branco. |
| Região do corpo | Entrada de 1 a 120 caracteres, com rejeição de branco. |
| Tamanho aproximado | Centímetros, maior que zero até 300, com até duas casas decimais. |
| Descrição da proposta | Entrada de 1 a 4.000 caracteres, com rejeição de branco. |
| Valor da proposta | BRL, maior que zero até 9.999.999,99, com até duas casas decimais. |

O pedido cria atendimento Negotiating. O artista participante apresenta proposta Pending. Uma nova proposta pode substituir a vigente ainda não aceita, que passa a Superseded; versões anteriores continuam consultáveis. Somente o cliente participante aceita a proposta vigente, que passa a Accepted, tornando o atendimento Agreed.

Após aceite, edição e nova proposta não fazem parte deste recorte. Aceitar versão substituída gera conflito; repetir o aceite da mesma proposta recupera o resultado persistido.

Criação de pedido e apresentação de proposta exigem Idempotency-Key UUID não vazio, por conta e operação. Mesmo conteúdo normalizado recupera o resultado original; conteúdo divergente com a mesma chave retorna conflito. A repetição de pedido é resolvida antes de consultar novamente Catalog. Proposta e aceite concorrentes são serializados em transação local.

**Aceite:** participantes consultam o mesmo contexto e terceiros não o acessam; alteração posterior do catálogo não reescreve o snapshot. Aceite não gera reserva, receita ou cobrança.

O contrato vigente utiliza /api/v2/engagements. A v1 sinaliza retirada com 410 e orientação de migração, sem redirecionar escritas. Consultar o [guia de Atendimentos](development/engagements.md) e os [contratos de integração](contracts/p04-integrations.md).

### 5.5 Conversas e mensagens

**Referências:** RF11–RF13, RF21; T05.01–T05.08; [decisão 0010](decisions/0010-p05-messaging.md).

A conversa inicial de negociação pode ser aberta antes do aceite. Sua abertura decorre de fato persistido em Engagements, publicado por Outbox e consumido por Messaging; não existe abertura arbitrária por rota de usuário.

Listagem e leitura são experiências separadas. O cliente vê o artista como interlocutor; o artista vê o cliente. Cada item identifica interlocutor, atendimento, última mensagem disponível e situação. A conversa individual possui endereço próprio e retorno à lista.

Mensagens válidas registram conteúdo, autor autenticado e data/hora; branco é rejeitado. O histórico pertence à conversa e ao atendimento corretos. A ordem incremental usa sequência monotônica por conversa; timestamp não determina o cursor. A entrega inicial usa consultas incrementais, sem exigir SignalR ou WebSocket.

A apresentação combina situação oficial de Engagements e nomes obtidos por contratos autorizados. Falha de dependência ou contrato inconsistente retorna erro explícito, sem inventar interlocutor ou estado. Histórico e envio usam autorização e barreira locais, sem depender dessa composição de apresentação.

**Aceite:** participantes recuperam o mesmo histórico durável; terceiros não leem ou enviam; autoria não vem do corpo da requisição. Repetir abertura não duplica conversa nem reabre escrita bloqueada.

writingBlocked significa barreira local durável. Não comprova, isoladamente, encerramento de negócio em Engagements. A autorização da primeira negociação não resolve automaticamente o retorno após serviço encerrado.

### 5.6 Disponibilidade e reservas

**Referências:** RF18, RF19, RF21; T06.01–T06.06; [decisão 0011](decisions/0011-p06-scheduling.md).

O artista cadastra intervalos explícitos por data, em fuso IANA, com padrão America/Sao_Paulo. O fuso não muda enquanto houver janelas/reservas. Reduzir ou remover disponibilidade não pode invalidar reservas existentes.

Somente o artista confirma reservas de seus atendimentos com proposta aceita. Clientes consultam suas reservas e a disponibilidade autorizada do artista do atendimento. Um atendimento pode ter várias sessões.

Uma reserva válida exige:

- Início futuro, término posterior ao início e precisão de minutos.
- Início/fim na mesma data local, com offset explícito coerente com o fuso.
- Rejeição de horários locais ambíguos ou inexistentes.
- Intervalo integralmente contido em uma única janela disponível.
- Ausência de sobreposição com outra reserva do artista.

Intervalos incluem início e excluem fim, permitindo adjacência. Não há duração comercial mínima, antecedência adicional ou horizonte máximo impostos. Cancelamento, reagendamento e passagem por meia-noite ficam para incremento posterior. As reservas de P06 são Confirmed e ocupam a agenda.

Scheduling serializa mudanças de disponibilidade e confirmação pelo calendário do artista e aplica restrição de exclusão de intervalos no PostgreSQL. Consulta prévia de disponibilidade não garante reserva.

Confirmação usa operação UUID idempotente por artista. Mesmo atendimento e instantes recuperam o registro original, inclusive após início da sessão; payload divergente retorna 409. Consulta por operationId permite resolver timeout sem criar outra reserva.

Reserva e Outbox são atômicas. O recebimento idempotente em Engagements é um recibo técnico: não habilita chat nem conclui a coordenação de P07.

**Aceite:** concorrência não persiste reservas conflitantes; adjacência é aceita; cliente e artista consultam a mesma reserva; terceiros não acessam seus dados. Calendários usam datas locais, intervalo [from,to) e máximo de 93 dias por consulta, sem limitar o horizonte reservável.

A interface mensal, seleção do dia, ordenação dos horários e navegação para atendimento/conversa pertencem aos frontends futuros.

### 5.7 Encerramento e novo atendimento

**Referências:** RF14, RF15; T07.01–T07.06; [decisão 0013](decisions/0013-p07-lifecycle.md).

Qualquer participante pode solicitar encerramento com confirmação explícita, sem aprovação da outra parte. A operação Close e o estado Closing são duráveis. Scheduling congela novas reservas sob o bloqueio de calendário, verificando que não existe sessão com término futuro. Uma sessão futura rejeita a operação e restaura a situação anterior, sem cancelar a reserva.

Após o congelamento, Messaging confirma bloqueio definitivo de escrita. Só então Engagements conclui Closed. Timeout ou falha após etapa remota mantém pendência recuperável; repetição não desbloqueia o histórico. Uma conversa bloqueada pode ser criada antes do evento inicial para preservar a barreira diante de eventos atrasados.

Depois de Closed/Closing, um novo pedido para o mesmo par recebe RequiresReservation. Pedido e proposta não permitem escrever no novo chat. A confirmação do novo agendamento gera recibo e operação Activate atomicamente em Engagements. A reserva permanece confirmada se Messaging falhar; a habilitação continua pendente até recuperação. A ativação não altera o chat anterior nem supera Closing/Closed do próprio atendimento.

Conversas são separadas por atendimento. Mensagens aceitas antes da barreira permanecem consultáveis. Encerramento não registra conclusão financeira de sessão nem receita.

Operações possuem situação Pending, Completed, Rejected ou Quarantined, tentativas e erro estável. Falhas recebem espera progressiva e quarentena após dez tentativas. Participantes consultam lifecycle e podem retomar operações em quarentena. Rejected por reservas futuras exige nova solicitação depois do fim das sessões.

**Aceite:** nenhum envio é aceito depois do encerramento confirmado; concorrência com reserva não atravessa a barreira; falhas parciais não duplicam efeitos nem apagam histórico. Consulte [execução e diagnóstico](development/lifecycle.md).

### 5.8 Finanças e dashboard

**Referências:** RF16, RF17, RF20; T08.01–T08.06. **Situação:** implementado no backend; [evidências](verification/p08.md).

O dashboard reúne informações do próprio artista por período explícito: recebimentos, custos, lucro e margem estimados, clientes atendidos, sessões, ticket médio, quantidade/valor de reservas e conversas ativas. Permite acessar clientes/atendimentos e o perfil público do artista.

| Indicador | Regra de referência |
| --- | --- |
| Receita recebida | Soma dos recebimentos registrados no período. |
| Custos | Soma dos custos registrados no período e contexto considerados. |
| Lucro estimado | Receita recebida menos custos considerados. |
| Margem estimada | Lucro estimado / receita recebida × 100; zero sem receita. |
| Clientes atendidos | Clientes distintos com sessões concluídas no período. |
| Ticket médio por sessão | Valores de sessões concluídas / quantidade dessas sessões; zero sem sessões. |
| Valor agendado | Referências por reserva ainda sem conclusão no período local, sem rateio automático e separadas dos recebimentos. |

Conclusão de sessão, encerramento de chat, reserva e recebimento são fatos distintos. Os primeiros não geram receita automaticamente. Dados usam suas respectivas datas e origem.

**Aceite verificado:** R$ 4.000,00 de receitas e R$ 940,00 de custos geram R$ 3.060,00 de lucro e margem de 76,5%. Período vazio não divide por zero. Defasagem ou falha de origem não aparece como zero real sem indicação. Reconstrução de projeções não duplica lançamentos.

Dashboard é uma composição por contratos/projeções, sem acesso a bancos alheios e sem exigir um microsserviço próprio.

Políticas aprovadas na [decisão 0014](decisions/0014-p08-finance.md): ledger manual BRL, Artist proprietário, recebimentos parceláveis vinculados ao atendimento e custos gerais ou vinculados. Valores positivos com até duas casas; data efetiva não futura no fuso do artista. Sem edição/exclusão: estorno integral único preserva original e recompõe sua data efetiva. Chave idempotente por artista em criação/estorno; divergência retorna 409. Consultas paginadas e períodos [from,to) de 1–366 dias.

Só o artista conclui sua sessão após o término previsto, com completedAt explícito e valor (padrão referência da reserva). Repetir os mesmos dados é idempotente; alterar gera conflito. A reserva permanece Confirmed em seu contrato e a conclusão é recurso separado. Clientes distintos e ticket usam conclusões dentro do período; conversas com escrita permitida são quantidade atual independente do período.

Finance compõe Scheduling e Messaging via token delegado, valida atendimento em Engagements e preserva observação por fonte. Não há projeção operacional nova nem snapshot distribuído; indisponibilidade ou contrato inválido retorna 503. Recompor não grava o ledger. O Gateway inclui Finance em seu catálogo OpenAPI e proxy protegido. Navegação e interface permanecem para os frontends; a consulta de atendimentos do artista já existe em Engagements.

## 6. Arquitetura e contratos transversais

### 6.1 Organização interna

Microsserviços seguem capacidades de negócio: Identity, Catalog, Engagements, Messaging, Scheduling e Finance. Possuem dados, migrações e contratos próprios, permitindo implantação independente quando os contratos permanecerem compatíveis.

A referência interna combina Clean Architecture, DDD seletivo, casos de uso por funcionalidade e CQRS simplificado.

| Camada | Responsabilidade |
| --- | --- |
| API / Presentation | Transporte, identificação do solicitante, validação estrutural e tradução de respostas. |
| Application | Autorização contextual, coordenação de casos de uso e portas de integração/transação. |
| Domain | Invariantes, entidades, objetos de valor e transições de negócio. |
| Infrastructure | Persistência, transações/bloqueios, publicação e clientes de integrações. |

Domain não depende de HTTP, EF Core ou broker. Application depende de Domain; Infrastructure implementa necessidades internas. API utiliza Application e referencia Infrastructure para composição. Program.cs mantém chamadas explícitas a extensões segregadas por contexto, sem regras de negócio no bootstrap.

SOLID orienta responsabilidades e contratos específicos. Não exige interface por classe, repositório genérico, MediatR, event sourcing ou bancos separados para leitura/escrita.

### 6.2 Dados e comunicação

PostgreSQL com EF Core/Npgsql 10 é a base relacional. Cada serviço possui banco lógico, DbContext, credenciais e migrações próprios. Migrações têm execução controlada e identidade distinta da aplicação. Compartilhar servidor físico não permite joins, FKs ou transações entre bancos de serviços.

APIs síncronas atendem consultas e decisões imediatas. Eventos propagam fatos persistidos. Chamadas entre serviços exigem autenticação apropriada, timeout, cancelamento e tratamento de indisponibilidade; não transferem propriedade de regras ao chamador.

RabbitMQ é o broker inicial. Outbox grava fato e publicação atomicamente; Inbox coordena deduplicação e efeito local. Entrega pode se repetir; não existe garantia de exactly-once ou ordem global. Contratos versionados identificam evento, tipo, instante, contexto e correlação/causalidade pertinentes.

Ordenação necessária pertence à entidade/atendimento. Tentativas são limitadas; falhas esgotadas seguem para diagnóstico e reprocessamento controlado, conforme P05/P06. Projeções não decidem conflitos de agenda ou autorização de escrita. Processos distribuídos exigem estado durável e recuperação; compensação não equivale a rollback global.

### 6.3 Segurança e canais

Keycloak fornece OIDC/OAuth 2.0. Login usa Authorization Code com PKCE; APIs validam access tokens JWT, não ID tokens. Chamadores técnicos em nome próprio usam Client Credentials e escopos restritos.

O website usa BFF .NET 10, com tokens no servidor, sessão por cookie protegido e defesa CSRF. Mobile consome o Gateway e deverá usar armazenamento seguro adequado à plataforma. O Gateway YARP roteia chamadas e valida identidade, sem substituir autorização nos serviços ou coordenar regras distribuídas.

Segredos não são versionados nem enviados no frontend. Ambientes e dados de desenvolvimento, teste e produção são separados. Logs não expõem credenciais ou conteúdo integral de conversas por padrão.

### 6.4 HTTP e compatibilidade

APIs utilizam JSON/camelCase, recursos em inglês e IDs internos UUIDv7 gerados no backend. Versionamento inicial é /api/v1, com Engagements em v2 por mudança incompatível documentada.

Listagens usam cursor com ordem estável, padrão 20 e máximo 100, salvo contrato específico. Cursor respeita contexto/critérios e não concede acesso.

Erros usam Problem Details com application/problem+json, status coerente, code e traceId. Validação identifica campos pertinentes; consumidores não interpretam texto de mensagem para decidir comportamento.

| Resposta | Significado |
| --- | --- |
| 200 / 201 / 204 | Operação concluída conforme contrato; criação informa Location quando aplicável. |
| 202 | Processo aceito e pendente, com acompanhamento conforme contrato; não afirma entrega de processos futuros. |
| 400 | Entrada inválida. |
| 401 / 403 | Falha de autenticação ou ausência de permissão. |
| 404 | Recurso inexistente ou existência que não deve ser revelada. |
| 409 | Conflito de estado ou reutilização incompatível de operação idempotente. |
| 500 / 503 | Falha inesperada ou indisponibilidade temporária. |

Reserva confirmada, coordenação pendente e encerramento definitivo são resultados distintos. Após timeout, recuperar ou consultar resultado conforme contrato evita presumir que a escrita não ocorreu. Repetição automática de escrita exige segurança e idempotência.

Mudanças incompatíveis exigem versão e transição documentadas, considerando coexistência de versões móveis. Contratos HTTP/eventos não expõem entidades internas.

## 7. Documentação interativa do Gateway

**Referências:** RNF06–RNF08; TGW.01–TGW.04; [decisão 0012](decisions/0012-gateway-interactive-documentation.md).

Swagger UI em /swagger, somente Development/Testing, permite selecionar Identity, Catalog, Engagements, Messaging e Scheduling configurados. Cada serviço continua fonte oficial do OpenAPI. O Gateway filtra contratos públicos e adapta destinos para que Try it out utilize suas rotas reais.

Contratos internos, saúde e versões aposentadas ficam fora do catálogo. A documentação não cria endpoints de negócio nem permissões adicionais. A rota técnica de perfis preexistente mantém seu contrato e não aparece na interface.

O cliente público inklink-swagger autentica por Code/PKCE S256, sem segredo ou password grant. O access token permanece em memória, com persistência de autorização desabilitada. Essa ferramenta de desenvolvimento não altera o modelo de sessão do website/BFF.

Falha de obtenção de documento retorna erro explícito para o serviço afetado, sem substituição por documento vazio e sem impedir os demais. Produção não registra UI, assets ou agregação.

O [guia do Gateway](development/gateway-documentation.md) descreve execução dos seis projetos Api e Gateway com dependências configuradas. BFF não é necessário para esses testes diretos. T09.01 consolidou os [contratos dos consumidores](development/consumer-contracts.md), exemplos e compatibilidade; versões futuras deverão mantê-los atualizados.

## 8. Qualidade e verificação

Os critérios abaixo consolidam requisitos existentes; não registram novos resultados de teste.

| Dimensão | Critério verificável |
| --- | --- |
| Isolamento | Trocar identificador, perspectiva ou rota não expõe dados de outro participante/artista. |
| Persistência | Novo acesso recupera registros autorizados; dados oficiais não dependem de localStorage ou arrays fictícios. |
| Validação | Entrada inválida ou transição proibida não gera registro ou efeito parcial de sucesso. |
| Concorrência | Reservas não se sobrepõem; proposta vigente é consistente; envio e bloqueio possuem ordem local. |
| Integração | Falhas, repetição e reinício não duplicam efeitos; pendências são diagnosticáveis e recuperáveis. |
| Observabilidade | Logs estruturados, correlação, métricas e rastreamento permitem investigar API/Outbox/consumo sem expor segredos. |
| Arquitetura | Dependências respeitam camadas e não referenciam implementações de outro serviço. |
| Interface futura | Carregamento, erro, vazio e coordenação pendente são distintos; navegação preserva contexto. |
| Acessibilidade futura | Controles identificados, foco visível, sugestões com setas/Enter/Escape, abas por teclado e foco de diálogos gerenciado. |
| Responsividade futura | Fluxos utilizáveis em 360, 768 e 1280 px, preservando identidade visual, logo e favicon. |
| Operação | Configuração segura, migrações reproduzíveis, saúde/prontidão apropriadas e execução/publicação documentadas por componente. |

Ferramentas adotadas: xUnit, WebApplicationFactory, Testcontainers/PostgreSQL real e ArchUnitNET. Docker Compose apoia dependências locais; GitHub Actions, analisadores e dotnet format apoiam verificação. Identidade controlada em teste não substitui testes reais de autenticação com Keycloak.

P09 consolida regressão integrada, recuperação, ambiente e implantação independente; não adia testes das etapas anteriores. Verificações visuais e por teclado dependem dos frontends. O roteiro completo inclui pesquisa, perfil, favorito, pedido, proposta, conversa, reserva, encerramento, novo atendimento e indicadores nas duas perspectivas.

## 9. Rastreabilidade e situação

“Concluído” refere-se ao recorte registrado, não à entrega integral de todas as telas ou requisitos envolvidos.

| Etapa / tarefas | Capacidade | Requisitos principais | Situação nesta edição |
| --- | --- | --- | --- |
| P00 / T00.01–T00.07 | Organização e decisões mínimas | RNF01, RNF04, RNF07–RNF08 | Decisões e revisão concluídas. |
| P01 / T01.01–T01.07 | Fundação de Identity | RNF01, RNF04–RNF08 | Concluído; [evidências P01](verification/p01.md). |
| P02 / T02.01–T02.06 | Identidade, funções, Gateway/BFF | RF01, RF21 | Concluído; [evidências P02](verification/p02.md). |
| P03 / T03.01–T03.07 | Catálogo, pesquisa e favoritos | RF02–RF08, RF20–RF21 | APIs concluídas; [evidências P03](verification/p03.md). |
| P04 / T04.01–T04.06 | Solicitações, propostas e aceite | RF07, RF09–RF10, RF21 | Concluído; [evidências P04](verification/p04.md). |
| P05 / T05.01–T05.08 | Conversas e bloqueio local | RF11–RF14, RF21 | Recorte local concluído; [evidências P05](verification/p05.md). |
| P06 / T06.01–T06.06 | Agenda e concorrência | RF15, RF18–RF19, RF21 | Reserva/recibo técnico concluídos; [evidências P06](verification/p06.md). |
| GW / TGW.01–TGW.04 | Swagger interativo | RNF06–RNF08 | Concluído; [evidências GW](verification/gateway-documentation.md). |
| P07 / T07.01–T07.06 | Encerramento e novo atendimento | RF09–RF15, RF18–RF19, RF21 | Concluído; [evidências P07](verification/p07.md). |
| P08 / T08.01–T08.06 | Finance e dashboard | RF16–RF17, RF20–RF21 | Concluído no backend; [evidências P08](verification/p08.md). |
| P09 / T09.01–T09.06 | Consolidação/aceite backend | RF01–RF21 no backend; RNF01, RNF04–RNF08 | Concluído no recorte local; [evidências P09](verification/p09.md). |
| P10 / T10.01–T10.14 | Website React | RF01–RF21; RNF01–RNF08 | Implementado; quatro cenários com backend real aprovados. T10.10/M6 parcialmente pendentes, conforme verificação P10; modelo de login definido na decisão 0017; menu do usuário e página de login (T10.12, T10.14) pendentes. |
| P11 / T11.01–T11.05 | Base React Native | Capacidades do primeiro incremento móvel | Pendente. |
| P12 / T12.01–T12.04 | iOS | Recorte móvel aprovado | Pendente; depende de P11. |
| P13 / T13.01–T13.04 | Android | Recorte móvel aprovado | Pendente; depende de P11, não de P12. |

M0–M5 estão atendidos nos recortes documentados do backend. As interfaces permanecem pendentes. As evidências vinculadas mantêm os resultados e limites de cada execução; contagens históricas não são somadas como se fossem uma nova suíte completa.

## 10. Limites e decisões futuras

Permanecem para refinamento explícito:

- Evolução do ciclo P07: cancelamento/reagendamento e novas políticas além da decisão 0013.
- Evolução financeira além da decisão 0014: correções de sessões, estornos parciais e eventual projeção mediante necessidade.
- Evolução operacional após P09: nuvem/produção, coleta e alertas, carga e novas versões de contrato, sem reabrir os resultados técnicos locais já verificados.
- P10: concluir homologação de fluxos, expiração temporal e publicação web. Ferramentas, rotas, cache e transporte pelo BFF estão na decisão 0016; a página de login usa o fluxo do BFF conforme a decisão 0017.
- P11–P13: Expo ou configuração nativa, compartilhamento de código, recorte funcional, testes e distribuição móvel.
- Upload, tratamento e armazenamento de mídia, moderação e exclusividade/reutilização de flashes.
- Cancelamento, reagendamento e passagem por meia-noite fora do recorte P06.
- Evolução da apresentação dos históricos, mantendo a separação por atendimento aprovada em P07, e eventual transporte em tempo real.
- Nuvem, orquestração, metas e observabilidade de produção.

Não há compromisso inicial com Kubernetes, microfrontends, event sourcing, Elasticsearch, notificações push ou funcionamento offline. Prever mobile e escalabilidade não inclui automaticamente essas capacidades. A hospedagem do protótipo no Cloudflare Pages não determina a infraestrutura do produto.

A evolução continua por incrementos pequenos, com requisitos identificados, serviço responsável, contrato mínimo, aceite e evidência proporcional. Esta edição documenta a fundação existente e seus limites, sem iniciar tarefas pendentes ou alterar sua situação.

## Complemento da revisão P07

Enquanto o encerramento está em Closing, a ativação do novo chat aguarda em Pending, sem consumir tentativas de falha. Se a Agenda impedir o encerramento e o atendimento voltar a Agreed, a ativação continua; Closed mantém a proibição definitiva.

Application decide a política de retorno, criação e retomada de operações e a intenção de ativação no consumo de reserva. Infrastructure oferece snapshots bloqueados e atomicidade, sem escolher essas transições. As operações preservam o contexto de correlação até o worker, inclusive após reinício, conforme a atualização da decisão 0013.

### Revisão P08 — contratos e limites civis de período

Respostas operacionais precisam conter todos os campos obrigatórios, inclusive contadores e valores. Ausência ou null não equivale a zero; Finance rejeita o contrato com 503 e code finance.source_unavailable. Um resumo completo com contadores explicitamente zerados permanece válido. A exigência fica no adaptador de integração, sem introduzir serialização em Domain/Application.

Períodos por data usam o primeiro instante existente do dia no fuso do artista. Meia-noite inexistente avança até o primeiro minuto válido, conforme a precisão da Agenda; meia-noite repetida utiliza sua primeira ocorrência em UTC. A regra também se aplica ao limite exclusivo, mantendo períodos adjacentes sem lacunas ou sobreposição. Não altera a rejeição de horários de reserva ambíguos/inexistentes.

## Aceite técnico P09

M5 consolidado no ambiente local isolado aprovado na [decisão 0015](decisions/0015-p09-backend-validation.md): seis APIs, Gateway e BFF por HTTPS, dependências próprias e dados sintéticos separados. O roteiro real percorre as duas funções, recuperação após parada do broker, encerramento, novo agendamento, conclusão de sessão e indicadores, sem usar o protótipo.

[Contratos e exemplos](development/consumer-contracts.md) consolidam versões, limites, paginação, erros e pendências. OpenAPI de Agenda explicita timestamps textuais com offset e limites monetários da conclusão. Logs de Identity preservam correlação e suprimem valores privados de consulta; verificação automatizada e marcadores no roteiro real comprovam o comportamento.

A regressão final possui 249 casos únicos aprovados, conforme o detalhamento das rodadas e correções de infraestrutura de testes na [verificação P09](verification/p09.md). Finance foi publicado individualmente com contrato inalterado e demais processos preservados; consultas posteriores confirmaram persistência e autorização. P10 pode iniciar pelo recorte técnico; páginas, acessibilidade, responsividade, mobile, carga e operação de produção continuam posteriores.
