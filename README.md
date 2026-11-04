# InkLink-Platform

Nova aplicação InkLink, criada do zero e separada do protótipo. Backend em .NET 10 com os serviços Identity, Catalog, Engagements, Messaging, Scheduling e Finance, além de Gateway e BFF web.

P00–P09 concluídas nos recortes documentados. **M5 técnico do backend atingido no ambiente local isolado.** Próximo passo: T10.01, website React; iOS e Android em React Native permanecem posteriores. O código do protótipo não é base da implementação.

## Organização e SDD

- docs/: [requisitos](docs/requirements.md), [arquitetura](docs/architecture.md), [spec](docs/spec.md), [plano](docs/plan.md) e [tarefas](docs/tasks.md).
- src/backend/services/: microsserviços com camadas e dados privados.
- src/backend/gateway/ e src/backend/bff/: entradas dos consumidores.
- src/backend/scripts/: execução, migrações e verificação.

## Executar e testar

O [guia de integração P09](docs/development/integration.md) descreve ambiente próprio com HTTPS, Docker Compose, segredos e dados sintéticos separados. Gateway/Swagger: https://localhost:5291/swagger após a inicialização. [Contratos e exemplos](docs/development/consumer-contracts.md) cobrem os seis serviços, versões, paginação, limites, erros e processos pendentes.

A regressão é executada por Test-Backend.ps1 e pela CI, com TRX distintos por projeto. A [verificação P09](docs/verification/p09.md) registra 249 casos únicos com resultado final aprovado, a rodada inicial com falhas, a reexecução completa e o recorte final de OpenAPI. O roteiro real demonstra recuperação, histórico bloqueado, retorno por reserva, indicadores e publicação independente de Finance.

Nuvem, produção, frontends, acessibilidade visual, carga e distribuição mobile continuam fora deste aceite técnico local. As evidências históricas de cada incremento permanecem em docs/verification/.

## Contribuição

Usar Conventional Commits com resumo no corpo, conforme [CONTRIBUTING.md](CONTRIBUTING.md) e [AGENTS.md](AGENTS.md). Repositório: [InkLink-Platform](https://github.com/vitorvianna05/InkLink-Platform).
