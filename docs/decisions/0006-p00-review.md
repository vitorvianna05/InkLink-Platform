# Revisão de P00 — Preparação para o primeiro incremento

**Tarefa:** T00.07. **Resultado:** revisão documental concluída; P01 pode ser iniciado pelo escopo abaixo. **Data:** 27/10/2026.

Esta revisão consolida decisões aprovadas pelo responsável e verifica suas dependências. Não declara a implementação ou o ambiente de execução concluídos.

## 1. Decisões verificadas

| Tarefa | Resultado | Evidência |
| --- | --- | --- |
| T00.01 | Destino próprio, repositório e separação docs/src definidos. | Registro em tasks.md; repositório InkLink-Platform já criado e publicado. |
| T00.02 | Organização por serviço, nomes técnicos e GetMyProfile definidos. | [Decisão 0001](0001-organization.md). |
| T00.03 | PostgreSQL, EF Core/Npgsql 10, migrações privadas e estratégia de Agenda definidos. | [Decisão 0002](0002-persistence.md). |
| T00.04 | Keycloak, OIDC/OAuth, PKCE, JWT e BFF web definidos. | [Decisão 0003](0003-authentication.md). |
| T00.05 | SDK, dependências locais, testes, qualidade e automação definidos. | [Decisão 0004](0004-tooling.md). |
| T00.06 | Convenções HTTP, IDs, erros, paginação, idempotência e documentação definidas. | [Decisão 0005](0005-api-conventions.md). |

Os arquivos foram conferidos e o planejamento foi alinhado às decisões. A aprovação ocorreu incrementalmente na conversa com o responsável. A presença de decisões no repositório de trabalho não significa que todas já tenham sido commitadas ou enviadas ao remoto.

## 2. Escopo do primeiro incremento: P01

Criar apenas a fundação necessária ao serviço Identity, com projetos e testes definidos na decisão 0001. Os demais serviços, Gateway, BFF e frontends não serão gerados antecipadamente.

### Caso de uso GetMyProfile

- Entrada: identidade interna do solicitante obtida de um contexto confiável.
- Resultado de sucesso: identificador do perfil e nome de exibição.
- Persistência: perfil real no banco privado de Identity, acessado via Infrastructure com EF Core e Npgsql.
- Identificadores novos: UUIDv7; o identificador externo do provedor mantém sua semântica própria.
- Verificação mínima: perfil existente, inexistente, identidade inválida e impossibilidade de consultar outro usuário escolhendo livremente seu ID.
- Preparação do perfil: fixture controlada de integração, sem criar implicitamente cadastro público ou comando de alteração de permissões.
- P01: execução por hospedeiro de testes controlado, sem endpoint de perfil privado disponível anonimamente.
- P02: integração com Keycloak e exposição protegida do contrato de referência `GET /api/v1/profiles/me`.

O caso de uso retorna resultados de aplicação independentes de HTTP. A tradução pública usará a convenção aprovada: sucesso 200; autenticação inválida 401; perfil inexistente 404; falta de permissão 403 ou ocultação 404 conforme o contrato. Os cenários controlados de P01 não simulam uma autenticação real já implementada.

### Entregáveis de P01

1. Solução .NET 10 e referências corretas entre Api, Application, Domain e Infrastructure.
2. Configuração por ambiente e segredos fora do versionamento.
3. Banco privado de Identity, migração inicial e persistência do perfil.
4. GetMyProfile validado com dados controlados.
5. Tratamento de erros, documentação pertinente e sinais de saúde/prontidão com logs e correlação.
6. Testes unitários, integração com PostgreSQL real e regras de dependência com ArchUnitNET.
7. Pipeline inicial GitHub Actions e instruções reproduzíveis de restauração, execução e testes.

## 3. Pré-requisitos operacionais antes dos testes dependentes

- Fixar o SDK .NET 10 por global.json; SDK 10.0.303 foi identificado como referência local disponível.
- Fixar versões mantidas e compatíveis de pacotes, ferramentas e imagens durante a preparação da implementação.
- Validar o motor Linux do Docker: a última consulta identificou Compose instalado, mas não conseguiu acessar o engine. Não há evidência de ambiente de contêineres pronto.
- Disponibilizar PostgreSQL de desenvolvimento e PostgreSQL isolado para testes conforme as necessidades das tarefas.
- Restaurar pacotes e preparar configurações/credenciais locais sem expô-las em arquivos ou logs.

São pré-requisitos de execução a verificar em P01, não escolhas arquiteturais ainda sem resolução. Uma falha nesses itens impede concluir a tarefa de implementação afetada; não autoriza substituir PostgreSQL real por banco em memória para comprovar integração ou concorrência.

## 4. Decisões diferidas com responsável no plano

| Tema | Etapa/tarefa de refinamento | Motivo para não bloquear P01 |
| --- | --- | --- |
| Cadastro, funções e ativação de artistas | P02 / T02.02 e T02.03 | Primeiro incremento consulta perfil em contexto controlado. |
| Gateway, sessão e configuração operacional de Keycloak/BFF | P02 / T02.01–T02.05 | Autenticação real e entrada pública são entregues em P02. |
| Broker, Outbox e contratos de eventos | P05 / T05.01–T05.04 | GetMyProfile não produz integração distribuída obrigatória. |
| Busca e mídia | P03 / T03.02 e T03.05 | Não participam do caso inicial de Identity. |
| Fuso, disponibilidade e modelagem de intervalos | P06 / T06.01–T06.04 | Proteção PostgreSQL foi escolhida; detalhes pertencem ao domínio de Agenda. |
| Protocolo completo de encerramento | P07 / T07.01–T07.05 | Barreira de escrita e coordenação não participam da consulta de perfil. |
| Registro financeiro e projeções | P08 / T08.01–T08.06 | Fora do primeiro contexto implementado. |
| Infraestrutura final de hospedagem | P09 / T09.04 e T09.05 | Execução local e CI podem começar sem selecionar nuvem definitiva. |
| Website e ferramentas móveis | P10–P13 | Frontends aguardam o marco de backend integrado. |

## 5. Critério de saída e próximo passo

P00 está concluída quanto às decisões e à revisão da fundação. P01 e todas as suas tarefas continuam pendentes. Não há código de aplicação criado por esta revisão.

O próximo passo é T01.01: inicializar o serviço Identity no novo projeto, dentro do escopo aprovado. Uma escolha que altere esta base ou introduza uma nova política de negócio deverá ser registrada antes de implementar o comportamento dependente.
