# Requisitos do InkLink

## 1. Objetivo e escopo

O InkLink será uma plataforma web para conectar pessoas que desejam uma tatuagem a tatuadores compatíveis com sua ideia, reunindo descoberta de artistas, portfólios, flashes, solicitação de orçamento, negociação, agendamento e acompanhamento do atendimento.

Para o cliente, o objetivo é encontrar um artista, apresentar uma ideia e acompanhar o serviço. Para o tatuador, é divulgar seu trabalho, conversar com clientes e organizar atendimentos, agenda e resultados financeiros.

A aplicação será criada completamente do zero, com backend em .NET 10 e evolução para três frontends: website em React para web, aplicativo iOS em React Native e aplicativo Android em React Native. A entrega dos canais será incremental, sem obrigatoriedade de disponibilizar os três simultaneamente. O protótipo existente será utilizado exclusivamente como referência de telas, organização visual, navegação, usabilidade e funcionalidades. Seu código, arquitetura, armazenamento local, dados fictícios e simplificações técnicas não constituem a base da implementação da nova aplicação.

A nova aplicação possui caminho local próprio em `C:\Users\INFIS001125\Documents\ChatGPT\InkLink-Platform` e repositório denominado `InkLink-Platform`, separado de `INKLINK-Prototype`. A documentação SDD oficial fica em `docs/` e o código será implementado em `src/`. O repositório remoto foi criado em `https://github.com/vitorvianna05/InkLink-Platform`.

Este documento especifica a fundação da nova aplicação, não uma manutenção ou evolução técnica do protótipo. Os requisitos e critérios de aceite descrevem o comportamento esperado do produto a ser construído. A existência de uma tela no protótipo não significa que o requisito já esteja implementado na nova aplicação.

O `requirements.md` e os demais documentos Markdown utilizados em SDD são mutáveis. Devem acompanhar as decisões de produto e a implementação, podendo receber ajustes, inclusões ou remoções em revisões futuras. Esta versão não pretende antecipar todas as decisões técnicas ou operacionais; os detalhes serão definidos conforme o desenvolvimento exigir.


## 2. Contrato de referência

Nesta versão, o contrato descreve os conceitos, dados essenciais e comportamentos da aplicação. A organização inicial, a persistência em PostgreSQL com EF Core/Npgsql 10, a autenticação com Keycloak e BFF web e as convenções de API já foram aprovadas em P00 e estão registradas em [architecture.md](architecture.md) e nas decisões correspondentes. Endpoints específicos, modelos de dados e detalhes de configuração serão refinados nas etapas de implementação, respeitando essa base. As rotas técnicas e os identificadores do protótipo não precisam ser reproduzidos.

- **Cliente:** usuário que pesquisa artistas, salva favoritos, solicita orçamento, acompanha propostas, conversa e agenda serviços.
- **Artista:** tatuador com identidade própria, estúdio, localização, biografia, estilos, portfólio e preço inicial de referência, que atende clientes e gerencia sua atividade.
- **Tatuagem realizada:** trabalho do portfólio apresentado como exemplo de execução do artista.
- **Flash:** desenho criado pelo tatuador que pode motivar uma solicitação de orçamento. Regras de exclusividade e reutilização poderão ser detalhadas posteriormente.
- **Solicitação de orçamento:** pedido dirigido a um artista, com descrição da ideia, região do corpo e tamanho aproximado da tatuagem. Pode ter origem em um flash.
- **Proposta:** condições apresentadas pelo artista para um atendimento, incluindo descrição e valor em reais. Seu aceite não representa pagamento nem confirmação automática de horário.
- **Atendimento ou serviço:** contexto de trabalho entre cliente e artista, ao qual se vinculam negociação, conversa e agendamento. Para o controle inicial do chat, os estados de referência são `ATIVO` e `ENCERRADO`.
- **Conversa:** histórico de mensagens entre os participantes de um atendimento, com autoria e data/hora. Permanece consultável após o encerramento do serviço.
- **Agendamento ou reserva:** intervalo associado a um cliente, um artista e um atendimento, com data, início, término e situação. Cancelamento e reagendamento poderão ser detalhados em revisões futuras.
- **Resultado financeiro:** receitas registradas, custos, lucro estimado, margem, ticket médio e quantidade de clientes e sessões, com período de referência explícito. Registro gerencial de recebimento não equivale a processamento de pagamentos pela plataforma.
- **Moeda e idioma:** reais com formatação brasileira e interface em português do Brasil. A definição operacional de fuso horário será detalhada com o desenho da agenda.

Páginas e áreas de referência:

| Página ou área | Finalidade |
| --- | --- |
| Descobrir | Apresentação do produto e artistas em destaque. |
| Explorar | Pesquisa livre com sugestões e filtros. |
| Perfil público do artista | Identidade, tatuagens realizadas, flashes e acesso à conversa existente. |
| Meu espaço | Informações, pedidos e favoritos do cliente. |
| Mensagens do cliente | Listagem das conversas com artistas. |
| Conversa individual | Histórico e informações do atendimento selecionado. |
| Dashboard do artista | Indicadores, clientes, calendário e acesso ao portfólio. |
| Mensagens do artista | Listagem das conversas com clientes. |

As telas e os fluxos do protótipo orientam a experiência dessas áreas. A implementação de rotas, componentes e contratos será própria da nova aplicação.

## 3. Requisitos funcionais

### RF01 — Disponibilizar áreas de cliente e artista

A aplicação deve oferecer experiências próprias para cliente e artista, com navegação e ações adequadas à função do usuário.

**Critérios de aceite:** o cliente acessa descoberta, exploração, seus pedidos, favoritos e conversas. O artista acessa dashboard, seus clientes, conversas, agenda e portfólio. A identificação do usuário e a autoria das ações permanecem coerentes com a área utilizada. O acesso a uma área não concede acesso aos dados particulares de outros usuários.

O botão temporário de troca entre personagens do protótipo não é um requisito do produto. Cadastro e a possibilidade de uma mesma pessoa exercer as duas funções serão detalhados posteriormente. A base de autenticação já está definida na [decisão 0003](decisions/0003-authentication.md): Keycloak, OIDC/OAuth 2.0, PKCE, access tokens JWT e BFF web; sua configuração operacional será refinada antes da implementação correspondente.

### RF02 — Apresentar a página Descobrir

A página Descobrir deve apresentar a proposta do InkLink, artistas em destaque e acessos à exploração.

**Critérios de aceite:** a página apresenta o produto e cards de artistas disponíveis no catálogo. É possível consultar o perfil de um artista e acessar Explorar. A pesquisa inicial sem recomendações pertence à página Explorar, preservando a função de apresentação de Descobrir.

### RF03 — Iniciar a exploração pela pesquisa

A página Explorar deve priorizar o campo de pesquisa livre e os filtros antes de apresentar artistas.

**Critérios de aceite:** no estado inicial, não são exibidos cards de artistas. O usuário pode pesquisar por texto, por filtros ou sem critérios. A seção de resultados aparece após acionar “Buscar artistas”. “Limpar busca” remove os critérios e retorna ao estado inicial sem resultados.

### RF04 — Sugerir termos durante a digitação

O campo de pesquisa livre deve apresentar opções próximas ao texto digitado, considerando artistas, estúdios, estilos e cidades do catálogo.

**Critérios de aceite:** a digitação apresenta sugestões ordenadas por proximidade e identificadas por categoria. A pesquisa ignora diferenças de maiúsculas, minúsculas e acentos e tolera pequenos erros de escrita. Por exemplo, “sao” encontra “São Paulo” e “blakwork” encontra “Blackwork”, quando esses valores existem no catálogo. Não há repetição da mesma opção na mesma categoria. Campo vazio não apresenta sugestões. Sem correspondências, a interface informa essa condição. Selecionar uma sugestão preenche o campo; o botão de busca aplica a pesquisa. Os filtros de seleção não recebem esse comportamento de autocompletar.

A tecnologia de pesquisa será definida no desenho técnico. O algoritmo local do protótipo não determina a implementação da nova aplicação.

### RF05 — Aplicar pesquisa e filtros combinados

A exploração deve combinar o termo livre com estilo de tatuagem, cidade e limite de orçamento.

**Critérios de aceite:** os critérios são opcionais e combinados cumulativamente. O orçamento compara o limite escolhido com o preço inicial de referência, sem representá-lo como orçamento final aprovado. A busca informa a quantidade encontrada e ordena as correspondências textuais por relevância. Alterar filtros após uma busca indica a necessidade de buscar novamente. Sem correspondências, apresenta estado vazio e permite ajustar ou limpar os critérios.

### RF06 — Consultar o perfil público do artista

O cliente deve consultar a identidade do artista e exemplos de seu trabalho antes de solicitar atendimento.

**Critérios de aceite:** o perfil apresenta nome, estúdio, localização, biografia, estilos e preço inicial de referência. As abas “Tatuagens realizadas” e “Flashes” apresentam conteúdos distintos. A aba de conversa aparece quando existe histórico entre o cliente e o artista, inclusive encerrado, para consulta. Abrir e sair do perfil preserva o contexto da navegação anterior. Os dados apresentados pertencem ao artista selecionado.

### RF07 — Consultar flashes e iniciar interesse

O cliente deve visualizar desenhos do artista e iniciar uma solicitação de orçamento a partir de um flash.

**Critérios de aceite:** cada flash apresenta imagem, identificação e informações disponibilizadas pelo artista, como estilo e valor de referência. A ação de interesse abre uma solicitação dirigida ao artista correspondente e mantém a identificação do flash escolhido. O preço de referência não equivale ao aceite de uma proposta ou à realização de pagamento.

### RF08 — Gerenciar artistas favoritos

O cliente deve salvar e remover artistas favoritos.

**Critérios de aceite:** favoritar atualiza a indicação visual e inclui o artista na lista do cliente. Remover desfaz a seleção. Os favoritos permanecem associados ao cliente após recarregar a aplicação e retornar à sua conta. “Meu espaço” apresenta os favoritos quando existirem e um estado apropriado quando a lista estiver vazia. Limpar uma pesquisa não remove favoritos.

### RF09 — Solicitar um orçamento

O cliente deve enviar ao artista uma descrição da ideia, a região do corpo e o tamanho aproximado da tatuagem.

**Critérios de aceite:** o formulário identifica o artista e exige descrição não vazia, região do corpo e tamanho aproximado positivo. Dados inválidos não criam pedidos e são identificados ao usuário. Um envio válido cria um pedido consultável em “Meu espaço” e permite acessar a negociação correspondente. O envio não implica aceite de proposta, pagamento ou reserva de horário. Limites detalhados de tamanho e texto serão definidos na especificação do formulário, sem herdar automaticamente os valores do protótipo.

### RF10 — Apresentar e acompanhar propostas

O artista deve apresentar uma proposta para o atendimento, e o cliente deve consultá-la e registrar seu aceite.

**Critérios de aceite:** a proposta identifica o atendimento, sua descrição e o valor. O cliente consulta o pedido e a proposta do artista correto. O aceite exige confirmação explícita, fica vinculado àquela proposta e não afeta outros atendimentos. O aceite não realiza cobrança nem ocupa um horário automaticamente. As duas partes conseguem consultar a situação da proposta associada ao atendimento.

### RF11 — Listar conversas em página própria

Cliente e artista devem possuir uma página de listagem de suas conversas, separada da leitura do histórico.

**Critérios de aceite:** na área do cliente, cada item identifica o tatuador; na área do artista, identifica o cliente. Os itens apresentam a última mensagem disponível e a situação do atendimento. Clicar em um item abre a conversa individual correspondente. A listagem não apresenta simultaneamente o histórico completo de uma conversa. Quando não houver conversas, a página informa essa condição.

### RF12 — Ler conversas em página individual

O usuário deve consultar a conversa selecionada em uma página com endereço próprio e retorno à listagem.

**Critérios de aceite:** a página identifica o interlocutor e o atendimento, apresenta apenas o histórico autorizado e oferece retorno à listagem. Recarregar um endereço válido mantém a conversa selecionada. Um identificador inexistente ou não autorizado não expõe o conteúdo de outra conversa e apresenta uma alternativa de navegação.

### RF13 — Enviar mensagens com autoria preservada

Cliente e artista devem trocar mensagens vinculadas à conversa e ao atendimento corretos.

**Critérios de aceite:** mensagens vazias ou compostas apenas por espaços não são enviadas. Mensagens válidas registram conteúdo, autor e data/hora e ficam disponíveis aos dois participantes. A interface distingue mensagens próprias das recebidas. Mensagens permanecem após recarregar a aplicação e não se misturam às de outro atendimento. Usuários sem participação na conversa não conseguem consultar ou enviar mensagens nela. A tecnologia de entrega e atualização das mensagens será definida posteriormente.

### RF14 — Encerrar serviço e preservar histórico

O encerramento do serviço deve preservar as mensagens e bloquear novos envios para as duas partes.

**Critérios de aceite:** o encerramento exige confirmação de um participante autorizado. Depois de confirmado, o atendimento aparece como encerrado e o histórico continua consultável. O campo e a ação de envio ficam desabilitados, e o backend rejeita tentativas de envio incompatíveis com esse estado. O bloqueio permanece após recarregar a aplicação. Consultar o histórico não reabre o serviço.

A definição de quem pode encerrar o atendimento e de eventuais confirmações adicionais será detalhada em revisão futura.

### RF15 — Iniciar novo atendimento após encerramento

Para voltar a conversar após um serviço encerrado, o cliente deve realizar um novo agendamento com o artista.

**Critérios de aceite:** consultar o histórico, abrir o perfil ou enviar outro pedido de orçamento não habilita, por si só, novos envios no chat encerrado. Um novo agendamento confirmado permite iniciar a conversa do novo atendimento, preservando o histórico anterior e a identificação de cada contexto de serviço. A reserva aparece na agenda do artista e está sujeita à mesma validação de disponibilidade e conflitos das demais reservas.

A organização visual em um histórico contínuo ou em conversas separadas por atendimento será detalhada posteriormente, mantendo a rastreabilidade dos serviços.

### RF16 — Apresentar o dashboard gerencial do artista

O artista deve consultar um resumo financeiro e operacional por período.

**Critérios de aceite:** o dashboard apresenta receitas registradas, custos, lucro e margem estimados, clientes atendidos, sessões, ticket médio, quantidade e valor de reservas e conversas ativas. O período está explícito e sua alteração recalcula os indicadores a partir dos dados do artista. Um período sem movimentação apresenta valores zerados ou estado vazio apropriado. Os resultados não incluem dados de outros artistas.

### RF17 — Calcular indicadores de rentabilidade

Os indicadores devem utilizar definições explícitas e separar resultados realizados de valores futuros agendados.

| Indicador | Regra de referência |
| --- | --- |
| Receita recebida | Soma dos recebimentos registrados no período para os atendimentos do artista, sem processamento de pagamento no site. |
| Custos | Soma dos custos registrados para o período e contexto considerados. |
| Lucro estimado | Receita recebida menos custos considerados no período. |
| Margem estimada | Lucro estimado dividido pela receita recebida, multiplicado por 100. Sem receita, apresentar zero. |
| Clientes atendidos | Quantidade de clientes distintos com sessões concluídas no período. |
| Ticket médio por sessão | Soma dos valores das sessões concluídas no período dividida pela quantidade dessas sessões. Sem sessões, apresentar zero. |
| Valor agendado | Soma dos valores previstos para serviços reservados no período, separada dos recebimentos efetivos. |

**Critérios de aceite:** uma base controlada verifica os cálculos de cada indicador. Recebimentos de R$ 4.000,00 e custos de R$ 940,00, por exemplo, produzem lucro estimado de R$ 3.060,00 e margem de 76,5%. Reservar ou concluir uma sessão não registra automaticamente seu recebimento. Sessões e recebimentos são considerados segundo suas respectivas datas e regras. Períodos vazios não geram divisões por zero. A interface informa a natureza estimada do lucro e os custos abrangidos.

Os indicadores apoiam a gestão do artista. O detalhamento do registro de receitas e despesas será especificado em etapa própria, sem integrar pagamentos diretamente ao site.

### RF18 — Consultar calendário e horários reservados

O artista deve visualizar reservas em um calendário mensal e consultar os horários de um dia selecionado.

**Critérios de aceite:** o calendário permite avançar e retornar meses e sinaliza dias com reservas. Selecionar um dia apresenta intervalos em ordem de início, com cliente e projeto. Um dia sem reservas informa essa condição. Iniciar uma reserva mantém o dia selecionado como contexto. Uma reserva permite acessar o atendimento ou a conversa correspondente.

### RF19 — Adicionar reservas sem sobreposição

As reservas devem estar associadas a cliente, artista, atendimento, data e intervalo de horário.

**Critérios de aceite:** os dados obrigatórios são validados e o término deve ser posterior ao início. Uma reserva é rejeitada quando está fora da disponibilidade do artista ou sobrepõe outra reserva válida dele na mesma data. Intervalos adjacentes são permitidos. A validação considera o intervalo completo e ocorre no backend. Uma reserva confirmada aparece de forma consistente para o cliente e na agenda do artista. Solicitações simultâneas para um mesmo horário não podem resultar em reservas conflitantes.

A especificação de disponibilidade, dias de atendimento, duração, antecedência e fuso horário será detalhada posteriormente. O intervalo fixo utilizado na demonstração não é uma restrição definitiva da aplicação.

### RF20 — Consultar clientes e acessar o portfólio do artista

O artista deve acessar os atendimentos de seus clientes pelo dashboard e consultar seu perfil público.

**Critérios de aceite:** o resumo identifica cliente, projeto e situação do atendimento. Cada ação abre o histórico correspondente. O acesso ao perfil público apresenta a identidade, os trabalhos e os flashes do artista conectado. Gestão de cadastro, edição e publicação de trabalhos e flashes será detalhada em especificação própria; não deve ser substituída por conteúdo fixo do protótipo.

### RF21 — Preservar dados entre acessos

A aplicação deve preservar pedidos, propostas, mensagens, favoritos, reservas e estados dos atendimentos de forma durável e vinculada aos respectivos usuários.

**Critérios de aceite:** recarregar ou reabrir a aplicação não apaga registros nem duplica operações já concluídas. Encerramentos e confirmações permanecem consistentes nas duas perspectivas. Os dados ficam disponíveis ao usuário autorizado em seus acessos posteriores e não dependem exclusivamente do armazenamento de um único navegador. Dados fictícios usados em desenvolvimento ou testes não são tratados como registros reais de produção.

## 4. Requisitos não funcionais

### RNF01 — Base tecnológica e implementação independente

A nova aplicação deve utilizar React para web, React Native nos aplicativos iOS e Android e .NET 10 no backend, em uma implementação criada do zero. O compartilhamento de código entre canais será definido na arquitetura detalhada.

**Critérios de aceite:** o website é implementado em React; conforme os aplicativos entrarem no escopo das entregas, iOS e Android utilizam React Native com execução e compilação verificadas na plataforma correspondente. Os projetos do backend utilizam `net10.0` e compilam com o SDK .NET 10. Os três canais consomem contratos compatíveis do mesmo backend. A solução possui instruções próprias de instalação, execução e compilação. Componentes, contratos e organização técnica são definidos para o novo projeto, sem depender do código ou dos arquivos de dados do protótipo. A ferramenta de build do frontend e demais escolhas internas serão definidas no desenho técnico.

### RNF02 — Responsividade e identidade visual

A interface deve adaptar as áreas de cliente e artista a celular e desktop, usando o protótipo como referência de identidade visual e usabilidade.

**Critérios de aceite:** navegação, pesquisa, listagens, conversas, formulários, dashboard e calendário são utilizáveis em larguras de referência de 360 px, 768 px e 1280 px, sem sobreposição que impeça leitura ou ação. O navbar identifica a área utilizada. Cores, tipografia, logo e favicon mantêm a identidade InkLink. A referência visual não obriga o reaproveitamento do CSS ou da estrutura de componentes do protótipo.

### RNF03 — Navegação por teclado e identificação de controles

Controles interativos devem ser identificáveis e utilizáveis por teclado.

**Critérios de aceite:** campos possuem identificação acessível; botões somente com ícone possuem nome acessível; o foco é visível. Sugestões podem ser percorridas com setas, selecionadas com Enter e fechadas com Escape. Abas indicam seleção e permitem navegação por teclado. Diálogos recebem foco ao abrir, mantêm o foco em seu contexto e o devolvem ao controle de origem ao fechar.

### RNF04 — Persistência e controle de acesso

A aplicação deve persistir os dados de negócio no backend e restringir consulta e alteração aos usuários autorizados.

**Critérios de aceite:** favoritos, pedidos, propostas, mensagens, reservas e encerramentos permanecem após novos acessos. O backend verifica acesso e participação antes de consultar ou alterar dados privados. Alterar identificadores ou o estado local da interface não permite acessar dados de outra conta. A persistência seguirá a [decisão 0002](decisions/0002-persistence.md), com PostgreSQL, EF Core/Npgsql 10 e isolamento por serviço. A autenticação seguirá a [decisão 0003](decisions/0003-authentication.md), com Keycloak e autorização por recurso nos serviços. Modelos de dados, configurações e políticas específicas serão detalhados na implementação; o armazenamento local e a troca de personagens da demonstração não serão utilizados como substitutos dessas capacidades.

### RNF05 — Validação e consistência dos estados

Entradas inválidas e transições incompatíveis devem ser rejeitadas sem corromper dados existentes.

**Critérios de aceite:** o backend valida operações de negócio, independentemente das validações da interface. Falhas não criam pedidos, propostas, reservas ou mensagens inválidas. Propostas e estados ficam vinculados ao atendimento correto. Encerramento e novo agendamento respeitam as regras de bloqueio e habilitação do chat. Estados vazios, identificadores inválidos e ausência de resultados oferecem informação adequada sem expor dados indevidos.

### RNF06 — Verificação dos comportamentos

A nova aplicação deve possuir verificações reproduzíveis alinhadas aos critérios de aceite.

**Critérios de aceite:** a compilação da solução termina com sucesso. Testes com dados controlados verificam pesquisa aproximada, persistência, isolamento de dados, seleção de conversas, autoria, bloqueio após encerramento, agendamento, cálculos financeiros e conflitos de reservas. Verificações visuais e por teclado cobrem os principais fluxos. Os testes são próprios da nova aplicação e não dependem da base fictícia ou do armazenamento local do protótipo. Organização e ferramentas da suíte serão definidas no desenho técnico.

### RNF07 — Execução e publicação da nova aplicação

Cada frontend entregue e o backend devem possuir processos documentados de execução e publicação compatíveis com suas tecnologias e plataformas.

**Critérios de aceite:** é possível executar e compilar os componentes da entrega seguindo as instruções do novo projeto, incluindo configuração, empacotamento e distribuição específicos de iOS e Android quando esses aplicativos forem entregues. No ambiente publicado, as páginas se comunicam com o backend configurado por HTTPS, e os principais fluxos funcionam após recarregar um endereço válido. Infraestrutura, domínio e processo de entrega serão definidos posteriormente. A hospedagem estática do protótipo no Cloudflare Pages não determina a infraestrutura da nova aplicação.

### RNF08 — Separação de ambientes e configurações

Dados e configurações de desenvolvimento, teste e produção devem permanecer separados.

**Critérios de aceite:** dados fictícios podem ser usados para validar os fluxos em ambientes apropriados sem serem inseridos como atendimentos reais em produção. Credenciais não são incluídas no frontend ou em arquivos versionados. Configurações de ambiente não exigem alteração de regras de negócio. Nenhuma funcionalidade desta fundação efetua cobrança pela plataforma.

## 5. Condições de aceite do escopo

A fundação será aceita após revisão deste documento pelo responsável do produto e demonstração dos critérios aplicáveis na nova aplicação, por testes, revisão de código e execução dos fluxos. O protótipo é uma referência de experiência e não uma evidência de implementação desses requisitos no novo projeto.

O roteiro de referência inclui: descobrir um artista; pesquisar com sugestões e filtros; consultar portfólio e flashes; favoritar; solicitar orçamento; apresentar e aceitar uma proposta; abrir conversas pela listagem; enviar mensagens nas perspectivas de cliente e artista; encerrar o serviço; consultar o histórico bloqueado; iniciar novo atendimento por agendamento; consultar indicadores e verificar reservas e conflitos no calendário.

Esta versão estabelece a fundação do produto: implementação independente em React para web, React Native para iOS e Android e backend .NET 10, descoberta e pesquisa de artistas, portfólio e flashes, negociação, conversas vinculadas ao atendimento e gestão do artista. Pagamentos diretamente no site permanecem fora do escopo por enquanto.

A fundação aprovada em P00 está registrada nas [decisões de organização](decisions/0001-organization.md), [persistência](decisions/0002-persistence.md), [autenticação](decisions/0003-authentication.md), [ferramentas](decisions/0004-tooling.md) e [convenções de API](decisions/0005-api-conventions.md). Permanecem para refinamento incremental os modelos de dados, endpoints específicos, versões e configurações operacionais, disponibilidade, infraestrutura de produção e políticas de negócio ainda não definidas. Esses detalhes serão especificados conforme as próximas etapas exigirem, sem reabrir implicitamente as escolhas aprovadas. Uma mudança nessa base exige atualização explícita da decisão correspondente.

O `requirements.md` e os demais arquivos Markdown de SDD devem acompanhar a evolução do projeto. Requisitos e critérios podem ser acrescentados, ajustados ou removidos conforme as decisões do responsável pelo produto, mantendo coerência entre os documentos e o escopo vigente. O histórico do Git permitirá acompanhar essas mudanças.

Simplificações da demonstração, como identidades fixas, troca temporária de perfil, dados mockados, persistência em localStorage, aceite global de proposta e agendas desconectadas, não são requisitos nem soluções a transportar para a nova aplicação. A nova implementação deve cumprir os comportamentos especificados com seus próprios componentes, modelo de dados e regras consistentes.
