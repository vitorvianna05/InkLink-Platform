# Instruções do InkLink-Platform

## Commits

- Todos os novos commits devem seguir Conventional Commits: `tipo(escopo opcional): descrição curta`.
- Tipos aceitos: `feat`, `fix`, `docs`, `style`, `refactor`, `perf`, `test`, `build`, `ci`, `chore` e `revert`.
- Use um escopo quando ajudar a identificar a parte alterada, por exemplo `agenda`, `catalogo`, `conversas` ou `repo`.
- Após o título, deixe uma linha em branco e escreva um resumo objetivo das alterações no corpo. Explique a finalidade quando relevante; inclua validação e a tarefa SDD relacionada quando aplicável.
- Escreva a descrição e o resumo preferencialmente em português. O tipo mantém o identificador padronizado em inglês.
- Mudanças incompatíveis usam `!` após o tipo/escopo e devem explicar o impacto no corpo com `BREAKING CHANGE:`.
- Não contorne o hook de validação, não desative hooks para commitar e não use `--no-verify`.
- Não altere commits já publicados para adaptar mensagens sem solicitação explícita.
- Na configuração de um novo clone, siga CONTRIBUTING.md para ativar o hook e o template locais.

## Documentação e escopo

- O SDD oficial fica em `docs/`; a implementação nova ficará em `src/`.
- Consulte requirements.md, architecture.md, plan.md e tasks.md antes de implementar o incremento correspondente.
- Preserve a separação do protótipo. Não copie sua implementação como base do produto.
