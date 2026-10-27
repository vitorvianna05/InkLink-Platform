# Decisão 0002 — Persistência e migrações

**Tarefa:** T00.03. **Situação:** aprovada pelo responsável do produto.

## Contexto e decisão

O backend terá dados transacionais por microsserviço e uma regra crítica de não sobreposição na agenda. Foi aprovado PostgreSQL como banco relacional inicial, Entity Framework Core 10 para acesso a dados e o provedor Npgsql compatível da linha 10.

A versão mantida do PostgreSQL e os patches compatíveis dos pacotes serão fixados na preparação do ambiente e da primeira implementação. Esta decisão não instala dependências ou serviços.

## Isolamento e organização

- Cada serviço possui banco lógico, credenciais, DbContext e migrações próprios.
- Uma instância PostgreSQL pode hospedar inicialmente os bancos dos serviços, sem autorizar acesso cruzado a tabelas ou transações distribuídas por conexão compartilhada.
- Nomes de referência: inklink_identity, inklink_catalog, inklink_engagements, inklink_scheduling, inklink_messaging e inklink_finance. Os bancos serão criados conforme os serviços forem implementados.
- DbContext, mapeamentos e migrações ficam em Infrastructure. Domain permanece independente de EF Core e PostgreSQL.
- Consultas e persistência seguem os casos de uso. Não há obrigação de repositório genérico para cada entidade.

## Concorrência de reservas

Agenda utilizará proteção transacional no PostgreSQL contra intervalos sobrepostos do mesmo artista, por restrição de exclusão e tipos de intervalo. Intervalos adjacentes serão permitidos com início incluído e término excluído.

A modelagem e migração deverão definir os registros que efetivamente ocupam a agenda, incluindo índices, extensão btree_gist quando necessária e representação temporal compatível com as regras de fuso a definir. SQL específico pode ser incluído em migrações revisadas quando necessário.

Consultar disponibilidade antes da inserção não substitui a proteção no banco. Testes de integração e concorrência usarão PostgreSQL real; simulação em memória não comprova essa garantia.

## Migrações

Migrações serão versionadas com o serviço, revisadas e testadas antes da aplicação. Em publicação, serão executadas em uma etapa controlada, por script ou bundle adequado à entrega, sem depender da inicialização de cada instância.

A identidade que altera o esquema será distinta da credencial de execução da aplicação. Mudanças deverão preservar a compatibilidade de implantação e explicitar riscos de perda de dados. Não haverá migração global responsável por todos os bancos dos serviços.

## Alternativas e consequências

SQL Server foi considerado uma alternativa relacional viável. PostgreSQL foi escolhido, entre outros motivos, pelo suporte a intervalos e restrições de exclusão úteis à Agenda.

EF Core foi escolhido pelo suporte ao mapeamento, consultas, transações e evolução de esquema no ecossistema .NET. SQL específico permanece permitido para regras que o exijam.

Padronizar a tecnologia inicial reduz variações operacionais. Outra persistência poderá ser adotada por um serviço futuramente mediante necessidade e decisão documentadas, preservando propriedade dos dados e contratos.

## Referências

- https://www.postgresql.org/docs/17/rangetypes.html
- https://www.npgsql.org/efcore/release-notes/10.0.html
- https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying
