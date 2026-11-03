# InkLink

Nova aplicação InkLink, criada do zero e separada do protótipo.

## Organização

- `docs/`: fonte oficial da documentação SDD.
- `src/`: código da nova aplicação; serviços Identity, Catalog, Engagements, Messaging e Scheduling em src/backend/services/, Gateway e BFF em pastas próprias.

O backend utiliza .NET 10 com microsserviços. Os canais previstos são website React e aplicativos iOS e Android em React Native. A prioridade inicial é o backend.

## Documentos

- [Requisitos](docs/requirements.md)
- [Arquitetura](docs/architecture.md)
- [Plano](docs/plan.md)
- [Tarefas](docs/tasks.md)
- [Revisão da fundação P00](docs/decisions/0006-p00-review.md)

## Situação

P00–P06 concluídas: Identity, Catalog e Engagements .NET 10 com bancos PostgreSQL privados, autenticação Keycloak, funções locais, Gateway YARP e BFF com sessão no servidor. Catalog oferece publicação do próprio Artist, perfil/mídia públicos, descoberta, pesquisa aproximada e favoritos persistentes. Engagements oferece solicitações, snapshots de artista/flash, versões de proposta e aceite idempotente, com autorização e proteção concorrente. Messaging acrescenta conversas autorizadas, interlocutor/situação oficial, histórico incremental, envio idempotente, Inbox e bloqueio local. A Outbox integra Atendimentos via RabbitMQ com recuperação. Scheduling acrescenta disponibilidade por data/fuso IANA, reservas idempotentes, proteção GiST contra conflitos e recibo técnico de confirmação. M1/M2/M3 atingidos no recorte backend; P07 permanece pendente. Nenhuma implementação do protótipo foi copiada.

A validação da P06 terminou com 192 casos únicos com resultado final aprovado, incluindo PostgreSQL, RabbitMQ e Keycloak reais. Houve reexecução direcionada após atualizar a expectativa das tabelas de Atendimentos; a [verificação P06](docs/verification/p06.md) registra todas as rodadas. Build Release sem avisos/erros, restauração bloqueada, Compose e hosts locais verificados. Consulte [evidências da P05](docs/verification/p05.md) e [execução de Messaging](docs/development/messaging.md). Consulte [evidências e limites da P04](docs/verification/p04.md) e [execução de Engagements](docs/development/engagements.md).

Consulte [execução e contratos de Catalog](docs/development/catalog.md), [evidências da P03](docs/verification/p03.md), [execução de Identity, Gateway e BFF](docs/development/authentication.md), [evidências da P02](docs/verification/p02.md) e o [histórico da P01](docs/verification/p01.md).

Nome do repositório aprovado: `InkLink-Platform`. Repositório criado em https://github.com/vitorvianna05/InkLink-Platform.

## Contribuição e commits

Consulte [CONTRIBUTING.md](CONTRIBUTING.md) para ativar o hook e o template de commits semânticos com resumo. As instruções para agentes ficam em AGENTS.md.

P05 possui implementação inicial de conversas, Outbox e Inbox em validação. Consulte [pendências e evidências](docs/verification/p05.md) antes de considerar a etapa concluída.

P06: Agenda backend concluída, com disponibilidade por data/fuso IANA, reservas autorizadas/idempotentes, exclusão concorrente em PostgreSQL e Outbox com recibo técnico. [Execução](docs/development/scheduling.md) e [evidências](docs/verification/p06.md): 192 casos com resultado final aprovado, incluindo reexecução direcionada da expectativa de esquema. Próxima etapa: P07.

Documentação interativa antecipada: [guia do Gateway](docs/development/gateway-documentation.md), com seleção dos serviços em http://127.0.0.1:5191/swagger e autenticação Code/PKCE em desenvolvimento/testes.

Incremento GW concluído: documentação interativa dos serviços pelo Gateway, com login Code/PKCE. [Evidências](docs/verification/gateway-documentation.md): 49 testes finais aprovados e conferência dos cinco documentos reais. Próximo passo: T07.01.
