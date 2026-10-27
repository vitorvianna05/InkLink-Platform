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

P00 e P01 concluídas: Identity em .NET 10, PostgreSQL privado, GetMyProfile em host controlado, sinais operacionais, testes e CI. Nenhuma implementação do protótipo foi copiada. A próxima etapa é P02, autenticação e entrada protegida das APIs. O marco M1 ainda depende de P02.

Consulte [execução e testes do Identity](docs/development/identity.md) e [evidências da P01](docs/verification/p01.md).

Nome do repositório aprovado: `InkLink-Platform`. Repositório criado em https://github.com/vitorvianna05/InkLink-Platform.

## Contribuição e commits

Consulte [CONTRIBUTING.md](CONTRIBUTING.md) para ativar o hook e o template de commits semânticos com resumo. As instruções para agentes ficam em AGENTS.md.
