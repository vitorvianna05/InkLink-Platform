# Decisão 0005 — Convenções das APIs

**Tarefa:** T00.06. **Situação:** aprovada pelo responsável do produto.

## 1. Contratos e recursos

APIs utilizarão HTTP, JSON, propriedades em camelCase, rotas em inglês orientadas a recursos e versão pública inicial por caminho em `/api/v1`. Contratos não expõem entidades de domínio, detalhes de banco ou nomes de classes internas.

Exemplos de referência, a serem concretizados nas funcionalidades correspondentes:

```http
GET  /api/v1/profiles/me
GET  /api/v1/artists
POST /api/v1/engagements
GET  /api/v1/conversations/{conversationId}/messages
POST /api/v1/reservations
```

Esses exemplos não representam endpoints já implementados nem definem todo o catálogo de rotas.

## 2. Identificadores

Novos identificadores internos serão UUIDv7 gerados no backend, representados como strings nos contratos e armazenados como UUID no PostgreSQL quando aplicável. Identificadores externos, como o sujeito do Keycloak, mantêm o contrato do seu emissor.

Conhecer ou adivinhar um identificador não autoriza acesso. O recurso e seus participantes são verificados em cada serviço. A ordenação de negócio não deve presumir que o identificador representa o horário oficial de uma operação.

## 3. Resultados HTTP

| Código | Convenção |
| --- | --- |
| 200 | Consulta ou operação concluída com representação. |
| 201 | Recurso criado, com sua localização em Location. |
| 202 | Processo aceito, ainda não concluído, com endereço para acompanhar a situação. |
| 204 | Operação concluída sem corpo. |
| 400 | Entrada inválida, incluindo campos obrigatórios, tipos e formatos. |
| 401 | Autenticação ausente, inválida ou expirada. |
| 403 | Usuário identificado sem a permissão necessária. |
| 404 | Recurso inexistente ou existência que não deve ser revelada ao solicitante. |
| 409 | Conflito com o estado atual, como reserva sobreposta ou envio em chat encerrado. |
| 500 | Falha inesperada. |
| 503 | Indisponibilidade temporária do serviço. |

A tabela estabelece os resultados principais, sem impedir respostas técnicas necessárias ao protocolo. Uso de 403 ou 404 para um recurso privado deve ser consistente no contrato e não revelar seu conteúdo.

Encerramento em processamento retorna um estado de operação pendente, não a afirmação de que o atendimento já está encerrado. A confirmação definitiva exige o bloqueio durável de mensagens conforme a arquitetura.

Uma reserva pode estar criada enquanto sua coordenação posterior está pendente. O contrato deve distinguir o recurso confirmado de processos ainda incompletos, sem usar 202 para disfarçar uma falha ou declarar etapas não concluídas.

## 4. Erros e validação

Erros utilizarão Problem Details com `application/problem+json`, status coerente com a resposta HTTP e os campos pertinentes ao problema. Acrescentar `code`, identificador estável para tratamento programático, e `traceId`, para correlação. Erros de validação também identificam os campos inválidos.

```json
{
  "type": "about:blank",
  "title": "Horário indisponível",
  "status": 409,
  "detail": "O artista já possui uma reserva nesse intervalo.",
  "code": "reservation.overlap",
  "traceId": "identificador-de-rastreamento"
}
```

Mensagens são voltadas ao usuário; clientes não devem interpretar seu texto para decidir comportamentos. Não são retornados stack traces, credenciais ou detalhes privados de banco. `traceId` não substitui identificadores de negócio nem chaves de idempotência.

- API: formato, tipos e obrigatoriedade dos campos de transporte.
- Application: autorização e coordenação do caso de uso.
- Domain: regras e transições de negócio.
- Persistência: garantias finais de integridade e concorrência.

Validação no frontend não dispensa nenhuma dessas garantias. Em Agenda, término anterior ao início é entrada inválida; sobreposição com reserva existente é conflito de estado.

## 5. Listagens

Adotar paginação por cursor com ordem estável e desempate por identificador. Limite inicial padrão de 20 e máximo de 100 itens; exceções devem ser justificadas no contrato da operação. Parâmetros inválidos são validados no backend.

```json
{
  "items": [],
  "nextCursor": null,
  "hasMore": false
}
```

A ordenação dos filtros e a validade do cursor precisam ser definidas para cada recurso. Alterar critérios não deve reutilizar um cursor incompatível silenciosamente.

A pesquisa de artistas informará a quantidade encontrada exigida pelos requisitos, com semântica de contagem definida em seu contrato. Não se exige contagem total para toda página de histórico de mensagens.

## 6. Idempotência

Operações críticas sujeitas a repetição, como confirmar reserva, utilizarão `Idempotency-Key` conforme seu contrato. O escopo da chave inclui solicitante e operação; o conteúdo relevante deve ser identificado para detectar reutilização incompatível.

- Mesma chave e mesmo conteúdo no contexto correto: recuperar o resultado da operação original.
- Mesma chave com conteúdo diferente: rejeitar com conflito.
- Operação ainda em processamento: informar sua situação sem iniciar uma segunda operação equivalente.
- Timeout: permitir consultar ou recuperar o resultado, sem pressupor que a operação não foi executada.

Operações abrangidas, retenção, persistência da chave e tratamento concorrente serão especificados em seus casos de uso. Registrar uma chave separadamente do efeito crítico, sem coordenação consistente, não comprova idempotência.

## 7. Documentação e evolução

Cada serviço terá documentação OpenAPI com contratos, autenticação, validações, exemplos e respostas de erro pertinentes. A implementação utilizará o suporte OpenAPI de ASP.NET Core; interface visual adicional não foi selecionada nesta decisão.

Alterações incompatíveis exigem nova versão pública e transição documentada. A versão da API não acompanha automaticamente cada implantação do serviço. Compatibilidade precisa considerar clientes móveis antigos e consumidores internos; uma adição de campo não é presumida segura sem analisar os contratos afetados.

Contratos de eventos têm versionamento próprio e serão detalhados junto às integrações. Esta decisão não cria biblioteca de entidades compartilhadas entre serviços.

## 8. Consequências e limites

Foram escolhidos contratos por recursos, versionamento no caminho e erros padronizados para reduzir ambiguidade entre os três canais e os serviços. Paginação por cursor foi priorizada para listagens que mudam durante a navegação. UUIDv7 é a convenção para IDs internos, sem ser mecanismo de segurança.

Limites específicos de entrada, nomes finais de todos os campos, retenção de idempotência e estados técnicos serão detalhados conforme cada operação. Não há exigência de definir agora endpoints de funcionalidades futuras nem de gerar código nesta etapa.

## Referências

- https://learn.microsoft.com/en-us/azure/architecture/best-practices/api-design
- https://learn.microsoft.com/en-us/azure/architecture/best-practices/api-implementation
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling?view=aspnetcore-10.0
- https://learn.microsoft.com/en-us/dotnet/api/system.guid.createversion7
- https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview
