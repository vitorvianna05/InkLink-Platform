# Decisão 0001 — Organização da solução e primeiro caso de uso

**Tarefa:** T00.02. **Situação:** aprovada pelo responsável do produto.

## Contexto

A solução será criada do zero no repositório InkLink-Platform. A organização deve tornar concretos os limites de serviços e as camadas já aprovadas, sem antecipar a criação de serviços vazios ou a implementação dos frontends.

## Decisão

- Documentação SDD em `docs/` e implementação em `src/`.
- Backend em `src/backend/`, com serviços em `services/` e Gateway em `gateway/` quando essa etapa for executada.
- Frontends futuros em `src/frontends/web/` e `src/frontends/mobile/`. A organização específica da base móvel será definida posteriormente.
- Código, projetos e namespaces em inglês; documentação e explicações em português.
- Nomes técnicos dos contextos: `Identity`, `Catalog`, `Engagements`, `Scheduling`, `Messaging` e `Finance`.
- Camadas de referência por serviço: `Api`, `Application`, `Domain` e `Infrastructure`.
- Testes próximos ao serviço, em `tests/`, com projetos de testes unitários, integração e arquitetura conforme a necessidade.
- O primeiro serviço implementado será `Identity`, com o caso de uso `GetMyProfile` (Consultar meu perfil).

## Estrutura de referência

```text
InkLink-Platform/
  docs/
  src/
    backend/
      services/
        Identity/
          src/
            InkLink.Identity.Api/
            InkLink.Identity.Application/
            InkLink.Identity.Domain/
            InkLink.Identity.Infrastructure/
          tests/
            InkLink.Identity.UnitTests/
            InkLink.Identity.IntegrationTests/
            InkLink.Identity.ArchitectureTests/
      gateway/
    frontends/
      web/
      mobile/
```

Essa árvore define convenções, não pastas e projetos já implementados. Na primeira entrega serão criados apenas os projetos necessários de Identity. Gateway, demais serviços e frontends surgirão em suas respectivas etapas.

## Primeiro caso de uso: GetMyProfile

- **Objetivo:** consultar o próprio perfil persistido.
- **Entrada:** identidade interna do solicitante fornecida por contexto confiável, não um identificador livremente escolhido para acessar outra conta.
- **Saída:** identificador do perfil e nome de exibição.
- **Validação:** identidade válida, perfil existente e acesso restrito ao próprio usuário.
- **Persistência:** leitura real de um perfil gravado no banco. A preparação de dados para a primeira verificação poderá ocorrer no ambiente controlado de testes.
- **Verificação:** perfil existente, inexistente, identidade inválida e tentativa de consulta indevida.
- **P01:** execução inicialmente controlada pelo hospedeiro de testes, sem endpoint privado exposto anonimamente.
- **P02:** integração da identidade autenticada e exposição protegida pela API.

O caso inicial não inclui cadastro completo, senhas, alteração de funções ou atualização de perfil. As capacidades adicionais serão refinadas nas tarefas correspondentes. Status HTTP e formatos de erro ficam em T00.06; o caso de uso deve permitir distinguir sucesso e falhas sem depender desse transporte.

## Alternativas e consequências

A organização global por camadas para todos os serviços foi preterida em favor da organização por serviço, preservando autonomia de código e testes. A estrutura distingue produtos frontend sem antecipar microfrontends ou bases móveis totalmente separadas.

Consultar o próprio perfil foi escolhido como primeiro caso de uso por permitir validar camadas, persistência e testes com escopo pequeno. Um cadastro completo anteciparia regras de identidade ainda em definição.

Persistência e migrações serão decididas em T00.03, autenticação em T00.04 e ferramentas de teste em T00.05. Esta decisão não seleciona essas ferramentas nem inicia implementação.
