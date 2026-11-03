# Finance e gestão do artista — P08

Executar Initialize-Local.ps1 para preservar/configurar segredos, conexão privada de Finance e destino do Gateway. Novo PostgreSQL local: finance-db, porta 54335; API Finance: http://127.0.0.1:5197. O Compose utiliza volume próprio e identidades distintas de owner/aplicação.

Aplicar InitialFinance em Finance e CompletedSessions em Scheduling por Apply-Migrations.ps1 com as credenciais de migração. Nunca usar a credencial de runtime para alterar schema. Iniciar Identity, Catalog, Engagements, Scheduling, Messaging, Finance e Gateway; BFF é opcional para testes diretos por Swagger.

No Swagger do Gateway, selecionar Finance e autenticar como Artist. Dados de outros artistas não são selecionáveis por parâmetro artistId: a identidade vem do token e de Identity.

## Endpoints

- POST /api/v1/finance/entries: kind Receipt/Cost, description, amount, effectiveDate e engagementId (obrigatório para Receipt).
- GET /api/v1/finance/entries: from/to, kind opcional, limit 20/100, cursor vinculado à conta e critérios.
- GET /api/v1/finance/entries/{id}: lançamento próprio.
- POST /api/v1/finance/entries/{id}/reversal: reason; estorno integral na data original.
- GET /api/v1/finance/dashboard: from/to locais, com financeiro, sessões, clientes distintos, ticket, reservas e conversas atuais.
- PUT /api/v1/scheduling/reservations/{id}/completion: completedAt com offset e amount opcional, após fim previsto.
- GET /api/v1/scheduling/reservations/{id}/completion: conclusão autorizada.
- GET /api/v1/scheduling/artist-context: fuso e identidade próprios.
- GET /api/v1/scheduling/artist-summary: resumo de sessões/reservas do próprio artista por período.
- GET /api/v1/conversations/artist-summary: conversas com escrita permitida, no instante observado.

POSTs financeiros exigem Idempotency-Key UUID não vazio. Estorno e criação usam chave por artista; mesma chave/payload recupera o resultado, outro payload retorna 409. PUT completion é idempotente por reserva e instante/valor; mudança gera 409.

## Roteiro de verificação

1. Criar pedido/proposta, aceitar e confirmar reservas pelas APIs existentes. Nenhuma receita deve ser criada.
2. Após o fim previsto, concluir uma sessão com o artista. Conferir ticket e cliente distinto, mantendo recebimentos zerados.
3. Registrar parcelas Receipt de um atendimento e Cost geral ou do atendimento.
4. Conferir R$ 4.000 de receitas e R$ 940 de custos: lucro estimado R$ 3.060 e margem 76,5%.
5. Estornar um lançamento: original permanece, estorno aparece e o indicador do período original é recomposto. Não há devolução bancária.
6. Repetir comandos e consultar com outro Artist/Client: não duplicar dados nem expor registros.
7. Indisponibilizar uma fonte operacional: dashboard responde 503 em vez de apresentar zero.
8. Reiniciar Finance e consultar novamente: o resumo é recomposto das origens, sem criar lançamentos.

Conversas atuais não são filtradas pelo período financeiro; consultar as datas de observação. Valor agendado soma referências por reserva sem conclusão, não constitui receita nem saldo a cobrar. Para clientes/projetos e navegação, usar a listagem existente de Engagements com perspective=artist e as referências de atendimento/reserva/conversa.

Finanças é registro gerencial manual, não cobrança ou contabilidade fiscal. Sem edição/exclusão de lançamentos e sem ajuste de sessão concluída neste incremento.

### Revisão P08 — contratos e limites civis de período

Respostas operacionais precisam conter todos os campos obrigatórios, inclusive contadores e valores. Ausência ou null não equivale a zero; Finance rejeita o contrato com 503 e code finance.source_unavailable. Um resumo completo com contadores explicitamente zerados permanece válido. A exigência fica no adaptador de integração, sem introduzir serialização em Domain/Application.

Períodos por data usam o primeiro instante existente do dia no fuso do artista. Meia-noite inexistente avança até o primeiro minuto válido, conforme a precisão da Agenda; meia-noite repetida utiliza sua primeira ocorrência em UTC. A regra também se aplica ao limite exclusivo, mantendo períodos adjacentes sem lacunas ou sobreposição. Não altera a rejeição de horários de reserva ambíguos/inexistentes.
