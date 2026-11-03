# InkLink

Nova aplicação InkLink, criada do zero e separada do protótipo.

## Organização

- `docs/`: fonte oficial da documentação SDD.
- `src/`: código da nova aplicação; serviços Identity, Catalog e Engagements em src/backend/services/, Gateway e BFF em pastas próprias.

O backend será desenvolvido em .NET 10 com microsserviços. Os canais previstos são website React e aplicativos iOS e Android em React Native. A prioridade inicial é o backend.

## Documentos

- [Requisitos](docs/requirements.md)
- [Arquitetura](docs/architecture.md)
- [Plano](docs/plan.md)
- [Tarefas](docs/tasks.md)
- [Revisão da fundação P00](docs/decisions/0006-p00-review.md)

## Situação

P00–P04 concluídas: Identity, Catalog e Engagements .NET 10 com bancos PostgreSQL privados, autenticação Keycloak, funções locais, Gateway YARP e BFF com sessão no servidor. Catalog oferece publicação do próprio Artist, perfil/mídia públicos, descoberta, pesquisa aproximada e favoritos persistentes. Engagements oferece solicitações, snapshots de artista/flash, versões de proposta e aceite idempotente, com autorização e proteção concorrente. M1 e M2 atingidos; P05 está em andamento. Nenhuma implementação do protótipo foi copiada.

P04 e as correções de revisão somam 39 cenários; a regressão local executou 90 testes aprovados com PostgreSQL real e JWTs de provedor controlado. Os testes antigos de integração com Keycloak/Docker não foram reexecutados neste ambiente. Consulte [evidências e limites da P04](docs/verification/p04.md) e [execução de Engagements](docs/development/engagements.md).

Consulte [execução e contratos de Catalog](docs/development/catalog.md), [evidências da P03](docs/verification/p03.md), [execução de Identity, Gateway e BFF](docs/development/authentication.md), [evidências da P02](docs/verification/p02.md) e o [histórico da P01](docs/verification/p01.md).

Nome do repositório aprovado: `InkLink-Platform`. Repositório criado em https://github.com/vitorvianna05/InkLink-Platform.

## Contribuição e commits

Consulte [CONTRIBUTING.md](CONTRIBUTING.md) para ativar o hook e o template de commits semânticos com resumo. As instruções para agentes ficam em AGENTS.md.

P05 possui implementação inicial de conversas, Outbox e Inbox em validação. Consulte [pendências e evidências](docs/verification/p05.md) antes de considerar a etapa concluída.
