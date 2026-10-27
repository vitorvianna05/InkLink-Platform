# Contribuição

## Configuração Git em cada clone

Na raiz do repositório, execute uma vez:

```sh
git config --local core.hooksPath .githooks
git config --local commit.template .gitmessage
```

As configurações locais não são transportadas pelo clone. Os arquivos do hook e do template são versionados; os comandos acima ativam seu uso naquele checkout.

## Padrão de commits

Todos os novos commits seguem Conventional Commits e têm resumo no corpo:

```text
feat(agenda): impedir reservas sobrepostas

Valida o intervalo na confirmação da reserva e protege a gravação concorrente.
Adiciona testes para sobreposição e horários adjacentes.

Tarefa: T06.03
Validação: testes unitários e de integração da Agenda.
```

Use `tipo: descrição` ou `tipo(escopo): descrição`. A descrição e o resumo devem ser objetivos, preferencialmente em português. Escopo é opcional.

| Tipo | Uso |
| --- | --- |
| feat | Nova funcionalidade. |
| fix | Correção de comportamento. |
| docs | Documentação. |
| style | Formatação sem alteração de comportamento. |
| refactor | Reorganização interna sem nova funcionalidade ou correção. |
| perf | Melhoria de desempenho. |
| test | Inclusão ou alteração de testes. |
| build | Compilação e dependências de build. |
| ci | Automação de integração e entrega. |
| chore | Manutenção e configuração do projeto. |
| revert | Reversão de uma alteração. |

Para uma mudança incompatível, use `!`, como em `feat(api)!: alterar contrato de reserva`, e explique o impacto em um parágrafo `BREAKING CHANGE:` no corpo.

O template é apresentado em commits abertos no editor. Em commits com `-m` ou `-F`, o hook continua validando o título, a linha de separação e a existência de um resumo; inclua o corpo explicitamente.

O hook `commit-msg` rejeita mensagens fora do formato. Para mudanças incompatíveis, também exige a descrição do impacto. Ele não consegue avaliar a qualidade do resumo: isso continua sendo responsabilidade de quem escreve e revisa o commit.

Essa validação é local e depende da configuração do clone. Regras no servidor ou na CI poderão ser adicionadas posteriormente. Não reescrever commits publicados apenas para adequar mensagens antigas.
