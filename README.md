# InkLink

Nova aplicação InkLink, criada do zero e separada do protótipo.

## Organização

- `docs/`: fonte oficial da documentação SDD.
- `src/`: código da nova aplicação; serviços Identity e Catalog em src/backend/services/, Gateway e BFF em pastas próprias.

O backend será desenvolvido em .NET 10 com microsserviços. Os canais previstos são website React e aplicativos iOS e Android em React Native. A prioridade inicial é o backend.

## Documentos

- [Requisitos](docs/requirements.md)
- [Arquitetura](docs/architecture.md)
- [Plano](docs/plan.md)
- [Tarefas](docs/tasks.md)
- [Revisão da fundação P00](docs/decisions/0006-p00-review.md)

## Situação

P00–P03 concluídas: Identity e Catalog .NET 10 com bancos PostgreSQL privados, Keycloak, funções locais, Gateway YARP e BFF com sessão no servidor. Catalog oferece publicação do próprio Artist, perfil/mídia públicos, descoberta, pesquisa aproximada e favoritos persistentes. Suíte atual com 110 cenários aprovados; M1 atingido e M2 depende de P04. Nenhuma implementação do protótipo foi copiada. A próxima tarefa é T04.01.

Consulte [execução e contratos de Catalog](docs/development/catalog.md), [evidências da P03](docs/verification/p03.md), [execução de Identity, Gateway e BFF](docs/development/authentication.md), [evidências da P02](docs/verification/p02.md) e o [histórico da P01](docs/verification/p01.md).

Nome do repositório aprovado: `InkLink-Platform`. Repositório criado em https://github.com/vitorvianna05/InkLink-Platform.

## Contribuição e commits

Consulte [CONTRIBUTING.md](CONTRIBUTING.md) para ativar o hook e o template de commits semânticos com resumo. As instruções para agentes ficam em AGENTS.md.
