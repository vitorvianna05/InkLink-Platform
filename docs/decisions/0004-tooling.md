# Decisão 0004 — Ferramentas locais e testes

**Tarefa:** T00.05. **Situação:** aprovada pelo responsável do produto.

## Ferramentas adotadas

| Necessidade | Decisão |
| --- | --- |
| SDK | .NET 10, fixado por global.json na implementação. Referência local verificada: SDK 10.0.303. |
| Dependências de desenvolvimento | Docker Compose, introduzindo PostgreSQL e Keycloak conforme necessários. |
| Testes unitários | xUnit e suas próprias asserções inicialmente. |
| Integração das APIs | WebApplicationFactory. |
| Persistência nos testes | Testcontainers para PostgreSQL real. |
| Regras arquiteturais | ArchUnitNET. |
| Configuração sensível local | .NET User Secrets e variáveis de ambiente, sem segredos versionados. |
| Consistência de código | .editorconfig, analisadores .NET e dotnet format. |
| Automação | GitHub Actions para restauração, compilação e testes. |

As versões compatíveis dos pacotes, runners e imagens serão fixadas na implementação. A referência ao SDK instalado não significa que global.json já exista ou que dependências tenham sido instaladas.

## Separação dos ambientes

Docker Compose mantém dependências do ambiente de desenvolvimento; Testcontainers controla recursos de teste isolados, sem depender dos registros do banco usado manualmente pelo desenvolvedor.

Testes de integração aplicarão as migrações e executarão contra PostgreSQL real. Banco em memória não substitui a verificação de transações, tipos de intervalo e concorrência de Agenda. A configuração deve permitir repetição, limpeza e isolamento entre execuções, com cuidado com o paralelismo.

Dados sintéticos, contas Keycloak de teste e configurações de desenvolvimento não são credenciais ou registros de produção. Exemplos versionados não contêm segredos. A CI usa seu mecanismo de configuração segura.

## Estratégia de testes

- Unitários: domínio e casos de uso isolados, sem dependência obrigatória de PostgreSQL ou Keycloak.
- Integração: APIs, persistência, migrações e transações, usando WebApplicationFactory e dependências reais isoladas quando necessário.
- Autenticação: cenários próprios com Keycloak verificam sua integração. Identidade simulada em outros testes não comprova autenticação real e não substitui esses cenários.
- Arquitetura: regras de dependência, incluindo Domain independente de Infrastructure e ausência de referência à implementação interna de outro serviço.
- Contratos e fluxos distribuídos: acrescentados junto às integrações, com repetição de eventos, falhas parciais e recuperação.

Não é obrigatório introduzir uma biblioteca adicional de mocks ou asserções na fundação. As verificações são executadas junto das funcionalidades, sem esperar a etapa P09.

## Execução e limites da aprovação

A futura implementação documentará comandos de restauração, build, execução, migração e testes, incluindo separação entre testes sem dependências externas e testes que exigem contêineres.

A verificação local identificou os executáveis .NET, Docker e Compose. O motor Linux do Docker não estava acessível nessa consulta. Deixá-lo operacional e validar os pré-requisitos são atividades de preparação do ambiente antes de executar testes com contêineres; esta decisão não afirma que o ambiente já esteja pronto.

A escolha reduz variação entre desenvolvimento e integração e oferece uma base verificável sem frontend. O custo é exigir um runtime de contêineres operacional nos ambientes de testes correspondentes. Nuvem, broker e stack móvel continuam em suas tarefas específicas.

## Referências

- https://dotnet.testcontainers.org/modules/postgres/
- https://learn.microsoft.com/en-us/aspnet/core/testing/integration-testing
- https://docs.docker.com/compose/gettingstarted/
- https://github.com/TNG/ArchUnitNET
