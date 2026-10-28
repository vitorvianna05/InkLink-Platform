# InkLink

Nova aplicação InkLink, criada do zero e separada do protótipo.

## Organização

- `docs/`: fonte oficial da documentação SDD.
- `src/`: código da nova aplicação; primeiro serviço Identity em src/backend/services/Identity.

O backend será desenvolvido em .NET 10 com microsserviços. Os canais previstos são website React e aplicativos iOS e Android em React Native. A prioridade inicial é o backend.

## Documentos

- [Requisitos](docs/requirements.md)
- [Arquitetura](docs/architecture.md)
- [Plano](docs/plan.md)
- [Tarefas](docs/tasks.md)
- [Revisão da fundação P00](docs/decisions/0006-p00-review.md)

## Situação

P00, P01 e P02 concluídas: Identity .NET 10 com PostgreSQL privado, perfis protegidos por Keycloak, funções locais, Gateway YARP e BFF com sessão no servidor. Marco M1 atingido; 71 testes aprovados. Nenhuma implementação do protótipo foi copiada. A próxima etapa é P03, Catálogo.

Consulte [execução de Identity, Gateway e BFF](docs/development/authentication.md), [evidências da P02](docs/verification/p02.md) e o [histórico da P01](docs/verification/p01.md).

Nome do repositório aprovado: `InkLink-Platform`. Repositório criado em https://github.com/vitorvianna05/InkLink-Platform.

## Contribuição e commits

Consulte [CONTRIBUTING.md](CONTRIBUTING.md) para ativar o hook e o template de commits semânticos com resumo. As instruções para agentes ficam em AGENTS.md.
